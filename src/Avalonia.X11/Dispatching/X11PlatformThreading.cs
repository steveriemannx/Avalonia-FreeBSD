using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.X11.Dispatching;
using static Avalonia.X11.XLib;

namespace Avalonia.X11
{
    internal unsafe class X11PlatformThreading : IControlledDispatcherImpl, IX11PlatformDispatcher
    {
        private readonly AvaloniaX11Platform _platform;
        private Thread _mainThread = Thread.CurrentThread;

        [StructLayout(LayoutKind.Explicit)]
        private struct epoll_data
        {
            [FieldOffset(0)]
            public IntPtr ptr;
            [FieldOffset(0)]
            public int fd;
            [FieldOffset(0)]
            public uint u32;
            [FieldOffset(0)]
            public ulong u64;
        }

        private const int EPOLLIN = 1;
        private const int EPOLL_CTL_ADD = 1;
        private const int O_NONBLOCK_LINUX = 0x800;
        private const int O_NONBLOCK_FREEBSD = 0x0004;
        private const int O_CLOEXEC_LINUX = 0x80000;
        private const int O_CLOEXEC_FREEBSD = 0x00100000;

        [StructLayout(LayoutKind.Sequential)]
        private struct PollFd
        {
            public int fd;
            public short events;
            public short revents;
        }

        private const short POLLIN = 0x0001;
        
        [StructLayout(LayoutKind.Sequential)]
        private struct epoll_event
        {
            public uint events;
            public epoll_data data;
        }
        
        [DllImport("libc")]
        private extern static int epoll_create1(int size);

        [DllImport("libc")]
        private extern static int epoll_ctl(int epfd, int op, int fd, ref epoll_event __event);

        [DllImport("libc")]
        private extern static int epoll_wait(int epfd, epoll_event* events, int maxevents, int timeout);

        [DllImport("libc")]
        private extern static int pipe2(int* fds, int flags);

        [DllImport("libc", SetLastError = true)]
        private extern static int poll(PollFd* fds, uint nfds, int timeout);

        [DllImport("libc")]
        private extern static IntPtr write(int fd, void* buf, IntPtr count);
        
        [DllImport("libc")]
        private extern static IntPtr read(int fd, void* buf, IntPtr count);

        private enum EventCodes
        {
            X11 = 1,
            Signal =2
        }

        private int _sigread, _sigwrite;
        private object _lock = new object();
        private bool _signaled;
        private bool _wakeupRequested;
        private long? _nextTimer;
        private int _epoll;
        private bool _usePoll;
        private Stopwatch _clock = Stopwatch.StartNew();
        private readonly X11EventDispatcher _x11Events;

        public X11PlatformThreading(AvaloniaX11Platform platform)
        {
            _platform = platform;
            _x11Events = new X11EventDispatcher(platform);

            var fds = stackalloc int[2];
            if (OperatingSystem.IsFreeBSD())
            {
                _usePoll = true;
                if (pipe2(fds, O_NONBLOCK_FREEBSD | O_CLOEXEC_FREEBSD) == -1)
                    throw new X11Exception("pipe2 failed");

                _sigread = fds[0];
                _sigwrite = fds[1];
                return;
            }

            var ev = new epoll_event()
            {
                events = EPOLLIN,
                data = {u32 = (int)EventCodes.X11}
            };
            _epoll = epoll_create1(0);
            if (_epoll == -1)
                throw new X11Exception("epoll_create1 failed");

            if (epoll_ctl(_epoll, EPOLL_CTL_ADD, _x11Events.Fd, ref ev) == -1)
                throw new X11Exception("Unable to attach X11 connection handle to epoll");

            if (pipe2(fds, O_NONBLOCK_LINUX | O_CLOEXEC_LINUX) == -1)
                throw new X11Exception("pipe2 failed");

            _sigread = fds[0];
            _sigwrite = fds[1];
            
            ev = new epoll_event
            {
                events = EPOLLIN,
                data = {u32 = (int)EventCodes.Signal}
            };
            if (epoll_ctl(_epoll, EPOLL_CTL_ADD, _sigread, ref ev) == -1)
                throw new X11Exception("Unable to attach signal pipe to epoll");
        }

        private void CheckSignaled()
        {
            lock (_lock)
            {
                if (!_signaled)
                    return;
                _signaled = false;
            }

            Signaled?.Invoke();
        }
        

        public void RunLoop(CancellationToken cancellationToken)
        {
            var pollFds = stackalloc PollFd[2];
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = _clock.ElapsedMilliseconds;
                if (_nextTimer.HasValue && now > _nextTimer.Value)
                {
                    Timer?.Invoke();
                }

                if (cancellationToken.IsCancellationRequested)
                    return;
                
                //Flush whatever requests were made to XServer
                _x11Events.Flush();
                epoll_event ev;
                if (!_x11Events.IsPending)
                {
                    now = _clock.ElapsedMilliseconds;
                    if (_nextTimer < now)
                        continue;
                    
                    var timeout = _nextTimer == null ? (int)-1 : Math.Max(1, _nextTimer.Value - now);

                    if (_usePoll)
                    {
                        pollFds[0] = new PollFd { fd = _x11Events.Fd, events = POLLIN };
                        pollFds[1] = new PollFd { fd = _sigread, events = POLLIN };
                        int result;
                        do
                        {
                            result = poll(pollFds, 2, (int)Math.Min(int.MaxValue, timeout));
                        } while (result == -1 && Marshal.GetLastPInvokeError() == 4);

                        if (result == -1)
                            throw new X11Exception($"poll failed with errno {Marshal.GetLastPInvokeError()}");
                    }
                    else
                    {
                        var result = epoll_wait(_epoll, &ev, 1, (int)Math.Min(int.MaxValue, timeout));
                        if (result == -1)
                        {
                            var errno = Marshal.GetLastPInvokeError();
                            if (errno == 4)
                                continue;
                            throw new X11Exception($"epoll_wait failed with errno {errno}");
                        }
                    }
                    
                    // Drain the signaled pipe
                    int buf = 0;
                    while (read(_sigread, &buf, new IntPtr(4)).ToInt64() > 0)
                    {
                    }

                    lock (_lock)
                        _wakeupRequested = false;
                }

                if (cancellationToken.IsCancellationRequested)
                    return;
                CheckSignaled();
                _x11Events.DispatchX11Events(cancellationToken);
                while (_platform.EventGrouperDispatchQueue.HasJobs)
                {
                    CheckSignaled();
                    _platform.EventGrouperDispatchQueue.DispatchNext();
                }
            }
        }

        private void Wakeup()
        {
            lock (_lock)
            {
                if(_wakeupRequested)
                    return;
                _wakeupRequested = true;
                int buf = 0;
                write(_sigwrite, &buf, new IntPtr(1));
            }
        }

        public void Signal()
        {
            lock (_lock)
            {
                if(_signaled)
                    return;
                _signaled = true;
                Wakeup();
            }
        }

        public bool CurrentThreadIsLoopThread => Thread.CurrentThread == _mainThread;
        
        public event Action? Signaled;
        public event Action? Timer;

        public void UpdateTimer(long? dueTimeInMs)
        {
            _nextTimer = dueTimeInMs;
            if (_nextTimer != null)
                Wakeup();
        }


        public long Now => _clock.ElapsedMilliseconds;
        public bool CanQueryPendingInput => true;

        public bool HasPendingInput => _platform.EventGrouperDispatchQueue.HasJobs || _x11Events.IsPending;
        public X11EventDispatcher EventDispatcher => _x11Events;
    }
}
