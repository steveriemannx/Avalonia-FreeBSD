# SkiaSharp native asset for FreeBSD

This package contains `libSkiaSharp.so` for FreeBSD 15 x64, built by the
FreeBSD `graphics/libskiasharp` port (SkiaSharp native ABI 3.116).

The native library is BSD-3-Clause. Its runtime libraries are supplied by
FreeBSD packages: fontconfig (MIT), FreeType (FTL; see `FTL.txt`), expat (MIT),
libjpeg-turbo (BSD-3-Clause/Zlib/IJG), libpng, and libwebp (BSD-3-Clause).
No GPL- or LGPL-licensed native asset is included or used by this package.

The repository pins the managed `SkiaSharp` package to 3.116.0 on FreeBSD so
its native API version matches this library. Metal rendering is unavailable
on FreeBSD; Vulkan rendering uses the compatible legacy constructor.
