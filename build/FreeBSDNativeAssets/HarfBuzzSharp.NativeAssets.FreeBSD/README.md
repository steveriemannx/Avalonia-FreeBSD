# HarfBuzzSharp native asset for FreeBSD

This package contains HarfBuzz 8.3.1 as `libHarfBuzzSharp.so` for FreeBSD 15
x64. It is built from HarfBuzz source with GLib, GObject, Graphite, and ICU
disabled; FreeType integration is enabled and uses the FreeType License
(FTL, included as `FTL.txt`). The library has no GLib, Graphite, or ICU
runtime dependency.

HarfBuzz is MIT-licensed (`COPYING.txt`). No GPL- or LGPL-licensed native asset is
included or used by this package.
