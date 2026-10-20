# Windows x64 原生图形库

保留 Avalonia **11.3.12**、SkiaSharp **2.88.9**，重建同版本 Windows x64 原生库，排除 RAW/DNG 与 PIEX，不升级图形 API。

## 实际验证结果

[Windows CI 39355910024](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39355910024) 已成功，构建提交为 `06ebe119f27cad05af71c14a69c51f5baca586b6`。原始记录见[证据目录](../third-party/evidence/skia-no-dng-2.88.9-win-x64/README.md)及 [`native-build.json`](../third-party/evidence/skia-no-dng-2.88.9-win-x64/native-build.json)。

- C++ 实际编译完成，1,124 个源码输入齐全；生成图、编译命令和 DLL 实现符号检查均排除 DNG/PIEX。
- 与官方 2.88.9 DLL 的 **941 个导出槽逐字段一致**，加载后原生版本同为 **88.1**。
- 使用上游现有 `NativeLibraryMiniTest` 创建位图并编码 PNG，通过实际运行；没有新建测试项目。
- 使用该 DLL 构建的 production App 在 Windows 正常创建窗口并关闭，见[窗口检查记录](../third-party/evidence/skia-no-dng-2.88.9-win-x64/production-app-smoke.json)。

未单独执行 JPEG、WebP、GIF 解码检查；PNG 编码和窗口启动不能扩大为全部图像路径已验证。技术检查通过也不等于 App 组合许可已解除。

## 实物哈希与发布状态

以下为该次 CI 下载后复核的最终字节，见 [`artifact-hashes.json`](../third-party/evidence/skia-no-dng-2.88.9-win-x64/artifact-hashes.json)：

| 实物 | 字节数 | SHA-256 |
| --- | ---: | --- |
| `SkiaSharp-2.88.9-no-dng-win-x64.zip` | 3693711 | `e47750495ae6c003262c61835ed121ae4386848c3a97d20c0d8346f4b2d918b4` |
| `libSkiaSharp.dll` | 8860160 | `e328c8b0a58cd66dace98c0597b404ceb0b585daadf9f8451089a8c09258c95e` |
| `SkiaSharp-2.88.9-no-dng-win-x64-sources.zip` | 218395661 | `95c5775e4c31b9db945c890339789273a43b942c73409fa051dca78c2f3e4465` |

计划独立发布为 [`native-skia-2.88.9-no-dng.1`](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/native-skia-2.88.9-no-dng.1)，目前仍在做源码闭包复核，**尚未公开发布该资产**。原生包不含 Avalonia，可按自身材料和检查独立审核；App 因 [Avalonia/MS-PL 组合授权路径未解决](../third-party/evidence/avalonia-mspl-11.3.12/REVIEW.md)继续暂停公开发行。

正式使用时，以 [`components.json`](../third-party/components.json) 的 `native_overrides` 为固定来源：核对实际 Release 的二进制 ZIP、DLL、`native-build.json` 与对应源码 ZIP 的地址、SHA-256、构建提交及匹配关系。不能用浮动 Actions 下载链接、空哈希或原 NuGet 含 DNG DLL 静默回退。上表与计划链接不表示 Release 已公开或目录 pin 已生效。

## 构建与复建

运行 Actions 的 **Build pinned SkiaSharp without DNG (Windows x64)**，固定来源、六项外部源码、补丁和工具见 [`skia-no-dng-build.json`](../third-party/skia-no-dng-build.json)及[构建脚本](../scripts/build_skia_without_dng.py)。需要 Windows x64、Python 3.12、.NET 10 SDK、MSVC v142、Windows SDK 和 LLVM 12：

```powershell
python scripts/build_skia_without_dng.py --msvc-path '<Visual Studio>/VC' --llvm-dir '<LLVM 12>' --build-commit '<完整提交 SHA>'
```

源码 ZIP 中 `sources/SkiaSharp` 为实际修改后构建树，`upstream-archives` 保留原始归档，另附六项依赖、补丁、配置、构建/校验脚本、原始许可及来源记录。重新构建时进入 `rebuild/`，使用根目录 `BUILD_PROVENANCE.json` 的原提交，并加 `--cache-dir ../upstream-archives`、独立的 `--work-dir` 和 `--output-dir`。MSVC/SDK 等通用工具仍需按配置安装，不将它们称为源码包随附内容。

`DICTATION_NATIVE_REVIEW_ARCHIVE` 仅供同一 CI 注入本地审核 ZIP；正式 App 打包必须使用已审核并固定的来源。重新构建产生新字节后重新核对全部哈希与记录，不能沿用本次实物 pin。
