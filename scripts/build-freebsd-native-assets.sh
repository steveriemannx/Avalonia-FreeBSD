#!/bin/sh
set -eu

if [ "$(uname -s)" != "FreeBSD" ] || [ "$(uname -m)" != "amd64" ]; then
    echo "This script builds native assets for FreeBSD 15 x64." >&2
    exit 1
fi

root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
tmp="$HOME/dotnet-tmp"
out="$HOME/dotnet-nupkg"
stage="$tmp/freebsd-native-assets"
package="$tmp/libskiasharp-116_3.pkg"
package_root="$tmp/libskiasharp-root"
source="$tmp/harfbuzz-8.3.1"
build="$tmp/harfbuzz-build"
harfbuzz_stage="$tmp/harfbuzz-stage"
meson="$tmp/meson-venv/bin/meson"

mkdir -p "$out" "$stage/skia" "$stage/harfbuzz" "$package_root"

if [ ! -f "$package" ]; then
    fetch -o "$package" \
        https://pkg.freebsd.org/FreeBSD:15:amd64/quarterly/All/libskiasharp-116_3.pkg
fi
tar -xf "$package" -C "$package_root"
cp "$package_root/usr/local/lib/libSkiaSharp.so.116.0.0" "$stage/skia/libSkiaSharp.so"
cp "$package_root/usr/local/share/licenses/libskiasharp-116_3/BSD3CLAUSE" "$stage/skia/LICENSE.txt"
cp /usr/local/share/doc/freetype2/FTL.TXT "$stage/skia/FTL.txt"

if [ ! -x "$meson" ]; then
    python3 -m venv "$tmp/meson-venv"
    "$tmp/meson-venv/bin/python" -m pip install meson
fi

if [ ! -f "$source/meson.build" ]; then
    git clone --depth 1 --branch 8.3.1 \
        https://gitclone.com/github.com/harfbuzz/harfbuzz.git "$source"
fi

if [ ! -f "$build/build.ninja" ]; then
    "$meson" setup "$build" "$source" --prefix=/usr/local \
        -Dglib=disabled -Dgobject=disabled -Dicu=disabled \
        -Dgraphite2=disabled -Dfreetype=enabled -Dtests=disabled \
        -Dintrospection=disabled -Ddocs=disabled -Dutilities=disabled \
        -Dbenchmark=disabled
fi

"$meson" compile -C "$build"
"$meson" install -C "$build" --destdir "$harfbuzz_stage"
cp "$harfbuzz_stage/usr/local/lib/libharfbuzz.so.0.60831.0" \
    "$stage/harfbuzz/libHarfBuzzSharp.so"
cp "$source/COPYING" "$stage/harfbuzz/COPYING.txt"
cp /usr/local/share/doc/freetype2/FTL.TXT "$stage/harfbuzz/FTL.txt"

if ldd "$stage/harfbuzz/libHarfBuzzSharp.so" | awk 'tolower($0) ~ /glib|graphite|icu/ { found = 1 } END { exit !found }'; then
    echo "The HarfBuzz build unexpectedly depends on GLib, Graphite, or ICU." >&2
    exit 1
fi

DOTNET_ROOT="$HOME/dotnet10" "$HOME/dotnet10/dotnet" pack \
    "$root/build/FreeBSDNativeAssets/SkiaSharp.NativeAssets.FreeBSD.csproj" \
    --configuration Release --output "$out"
DOTNET_ROOT="$HOME/dotnet10" "$HOME/dotnet10/dotnet" pack \
    "$root/build/FreeBSDNativeAssets/HarfBuzzSharp.NativeAssets.FreeBSD.csproj" \
    --configuration Release --output "$out"
