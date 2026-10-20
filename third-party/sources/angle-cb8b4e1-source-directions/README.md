# ANGLE Windows x64 source delivery

Scope: Avalonia.Angle.Windows.Natives 2.1.25547.20250602, `av_libglesv2.dll`, upstream AvaloniaUI/angle@cb8b4e1307a9d8f5ff56b8c5973bea4158ffead8. This directory contains source directions and review evidence, not a successful Windows rebuild or a guarantee of byte-identical historical output.

Extract the original ANGLE source archive. Extract the eight additional archives listed in SOURCE_ARCHIVES.json into their destination_subdir paths relative to that source root. For codeload archives remove the single enclosing repository directory; Gitiles archives have no enclosing directory. The archive index records this as strip_root. All are exact original upstream sources; do not apply the Linux graph-review stubs to them.

Use the original `scripts/bootstrap.py`, `.gclient`, `DEPS`, `.gn` and `azure-pipelines.yml` as the build recipe. On Windows use MSVC/Windows SDK, Python, Ninja and GN revision ebc8f16ca7b0d36a3e532ee90896f9eb48e5423b. These general platform/build tools are identified separately from source delivered here. The original x64 pipeline parameters are:

```
gn gen out/x64 --args="is_debug=false is_clang=false target_cpu=\"x64\" dcheck_always_on=false angle_enable_vulkan=false angle_enable_gl=false angle_enable_null=false" --root-pattern=//:libGLESv2
ninja -C out/x64 libGLESv2.dll.lib
```

The original pipeline renames `out/x64/libGLESv2.dll` to `av_libGLESv2.dll`. This review has not independently recompiled the Windows DLL or reconstructed its historical compiler/SDK versions. Source files, build definitions and generated source files remain readable and modifiable.

`gclient sync` normally creates build/config/gclient_args.gni and repository metadata. When assembling only these archives, use the four original DEPS defaults for that generated file: checkout_angle_internal=false, checkout_angle_mesa=false, checkout_angle_restricted_traces=false, generate_location_tags=false. Preserve and follow the upstream commit/last-change generation scripts; archive-only builders can generate fresh local metadata or use the upstream dummy-lastchange build option and record that choice. A graph-review placeholder time/SDK path is not historical version metadata and must not be passed off as it.

The reviewed libGLESv2 root has 48 targets and 1629 existing source/input/script files. It includes zlib and Chromium compression helpers, ASTC decode code, Vulkan headers/Volk for system information, LLVM libc++/llvm-libc headers, plus embedded xxHash and Khronos headers. It does not require every one of the 74 DEPS projects; excluded unrelated tests/backends are not labelled as shipped binary components. SOURCE_COVERAGE.json records the real source-scope evidence and the limits of the Linux-host graph review.

Original copyright/permission statements are retained under LICENSES/third-party/angle-cb8b4e1-dependencies. LLVM licenses include their full exception and legacy grants; generic ANGLE BSD does not replace these. Additional exact prefixes preserve copyrights in actual zlib/Chromium/ARM input files without adding their implementations to the notice viewer. The full original implementations remain in the fixed source archives.
