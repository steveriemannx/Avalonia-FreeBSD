# FreeBSD

Avalonia's desktop and Wayland backends can be built for FreeBSD 15 x64 with
the .NET 10 SDK. `UsePlatformDetect()` selects X11; to prefer the native
Wayland backend and retain X11 fallback, configure the app with:

```csharp
AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .UseWaylandWithFallback();
```

## Native assets and licenses

Linux Skia and HarfBuzz native packages are not used on FreeBSD. They contain
native dependencies with GPL/LGPL terms. Instead, build the FreeBSD-only
assets into `~/dotnet-nupkg`:

```sh
sh scripts/build-freebsd-native-assets.sh
```

The repository's `NuGet.Config` maps those two package IDs to the relative
`../../dotnet-nupkg` local source. Build and restore with the installed SDK:

```sh
export DOTNET_ROOT="$HOME/dotnet10"
export PATH="$DOTNET_ROOT:$PATH"
dotnet restore
dotnet build src/Avalonia.Desktop/Avalonia.Desktop.csproj --configuration Release --framework net10.0
dotnet build samples/ControlCatalog.Desktop/ControlCatalog.Desktop.csproj \
    --configuration Release --framework net10.0 --runtime freebsd.15-x64
```

The SkiaSharp native library comes from FreeBSD's BSD-3-Clause
`graphics/libskiasharp` port. HarfBuzz 8.3.1 is built locally with GLib,
Graphite, and ICU disabled. FreeType is used under its FTL license; the FTL
notice is included in both local asset packages. These native assets do not
use the Linux native packages or a GPL/LGPL component.

The Wayland session must provide `libwayland-client`, `libwayland-cursor`,
`libxkbcommon`, and EGL/Mesa libraries. Avalonia's Wayland event thread uses
POSIX `poll`; FreeBSD-specific `pipe2` flags and errno values are handled
separately from Linux.
