# HarfBuzzSharp 8.3.1.1 Windows x64 build input

Extract the fixed SkiaSharp wrapper archive (64f24b1cddb68d30ec0ac6b661964178fc21d5ec) and the HarfBuzz archive (2b3631a866b3077d9d675caa4ec9010b342b5a7c). Place the HarfBuzz source tree at `externals/skia/third_party/externals/harfbuzz` inside the wrapper tree. Copy this directory's `externals/` overlay into that same wrapper tree. The header bytes are unchanged from mono/skia@30abf4a0dcd3006f3a555e46906151e486f87b6e.

Follow the wrapper's original `native/windows/build.cake` and `native/windows/libHarfBuzzSharp/libHarfBuzzSharp.vcxproj` for the Release x64 build. The project compiles HarfBuzz `src/harfbuzz-subset.cc` and includes the supplied Skia mutex configuration header. MSVC/Windows SDK/MSBuild are general platform tools; their source is not newly sublicensed here.

The source package also supplies Unicode 15.1.0 UCD and grouped UCDXML data for the table generators, plus original Microsoft USE and Unicode license notices. Upstream generated source tables remain readable and modifiable in the fixed HarfBuzz archive. Exact historical snapshots of two registry/web inputs to the already generated `hb-ot-tag-table.hh` were not reconstructed; no claim of byte-identical regeneration of historical tables or the original DLL is made. These snapshots are not needed by the native project compilation.
