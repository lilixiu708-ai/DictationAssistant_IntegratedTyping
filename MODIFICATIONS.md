# 修改声明与文件范围

上游核对基准为 [ArcticLampyrid/DictationAssistant `v4.0.0`](https://github.com/ArcticLampyrid/DictationAssistant/tree/324cd043fd13c0b9d50e287d0301cdbf85ea7c2c)，完整提交 `324cd043fd13c0b9d50e287d0301cdbf85ea7c2c`。本 fork 的发行仓库为 <https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping>。

本文件明确记录相对于上游的修改及日期，配合原始版权通知、许可证和 CHANGELOG 提供版本溯源。维护者为 `lilixiu708-ai`；该维护关系不等于对上游、第三方或完全由 AI 生成内容主张新的独占版权。原作者及第三方声明继续保留。

## 修改日期与主要内容

| 日期 | 本 fork 的修改 |
| --- | --- |
| 2026-10-19 | 文档导入与预览、词表打乱、同步打字插件、命令/语音/音效 SDK、生命周期与资源释放、结果虚拟化、包体整理及发行流程 |
| 2026-10-20 | 后台词表数量核对、异常日志、文字布局与下划线占位移除；同步打字成对翻译提示、本地保存、可选 DeepL 提前翻译，以及独立 API 设置和 Windows 本机/账户绑定的加密密钥保存；版权与许可审计、原始声明恢复、第三方资料补齐，移除随附 BASS/AAC 原生库并替换解码实现；准备 R5 本体及插件独立发行，加入未解决许可事项的公开披露与对应资产校验；宿主功能级别 4 的逐插件配置入口、翻译多绑定与首选项管理，整份配置加密及显式保存迁移；准备 R6 本体与同步打字 1.2.0 独立发行，复用 Word 导入 1.0.1，固定披露发布范围，并将云环境安装脚本与启动说明纳入仓库 |
| 2026-10-20 | 同步打字插件 1.3.1 扩展常见官方翻译 API、按服务显示凭据与区域字段，并完成基于既有 R6 宿主的插件独立兼容校验及发布；R6 本体、Word 导入 1.0.1 和既有发行资产保持不变 |
| 2026-10-20 | 扩展 Word / Excel 导入插件 1.1.0，保留原插件身份并引入 ExcelDataReader 3.9.0，新增多工作表预览、选列及原始文本读取；完成 Linux 样本原始文本、默认表头、预览及替换导入的独立核对，并基于固定 R6 完成独立 Windows 构建、合成工作簿读取、兼容检查及公开发布；仅记录私人样本的计数、哈希与验证范围，不纳入工作簿或整份提取内容 |
| 2026-10-20 | 整理主程序和插件内部职责，分离词表导入、翻译绑定草稿及成绩显示/导出；统一预览区域选择、词条计数和翻译参数校验，共用文档输入边界并集中 Excel 包预检；改用 Python 标准库明确生成 Windows ZIP，更新代码结构和当前文档导航，保留插件契约、配置格式和历史发行资产 |
| 2026-10-20 | 将练习插件显示名改为背诵练习，增加文档前置配对、无音频计时与离子拼写判定，默认离子式提示/英文答案；增加保留 Word 直接上下标的可选读取和表头识别、宿主功能级别 5 文档选源能力，以及成绩提示/来源字段；保留稳定插件身份及本地配置路径，不包含私人文档或完整提取数据 |

下表从审计观察点 `5bff3fae648ff124b019140e55669556f29ab377` 的实际 Git 文件历史生成，相对于上述上游基准列出当时的修改和新增文件。日期是该文件在观察点的最后修改日期，不将它当作首次创作日期或版权权属证明。该观察点共 206 个跟踪文件：20 个修改、77 个新增/移入、109 个不变；下表不包含未改动的上游文件。

随后本次许可修复新增的 `NOTICE.md`、本文件、`docs/版权与许可审计.md`，以及原始第三方许可、组件清单和发布检查资料，均属于 2026-10-20 的许可修复范围；准确最终变更应以包含本文件的 Git 提交为准。第三方许可证副本的新增不表示其版权属于本 fork。

## 审计观察点的逐文件范围

| 状态 | 文件 | 最后修改日期 |
| --- | --- | --- |
| 修改 | `.editorconfig` | 2026-10-19 |
| 修改 | `.gitattributes` | 2026-10-19 |
| 新增 | `.github/ISSUE_TEMPLATE/bug_report.yml` | 2026-10-19 |
| 新增 | `.github/PULL_REQUEST_TEMPLATE.md` | 2026-10-19 |
| 新增 | `.github/workflows/build-typing-windows.yml` | 2026-10-19 |
| 修改 | `AGENTS.md` | 2026-10-19 |
| 新增 | `BUILD_VALIDATION.md` | 2026-10-19 |
| 新增 | `CHANGELOG.md` | 2026-10-20 |
| 新增 | `CONTRIBUTING.md` | 2026-10-19 |
| 修改 | `DictationAssistant.slnx` | 2026-10-19 |
| 修改 | `README.md` | 2026-10-20 |
| 新增 | `README_先读.md` | 2026-10-19 |
| 新增 | `README_先读_构建状态.md` | 2026-10-19 |
| 新增 | `build-windows-typing.cmd` | 2026-10-19 |
| 新增 | `docs/PLUGIN_SDK.md` | 2026-10-20 |
| 新增 | `docs/README.md` | 2026-10-20 |
| 新增 | `docs/RELEASING.md` | 2026-10-19 |
| 新增 | `docs/W02_听力词汇_英文示例.txt` | 2026-10-19 |
| 新增 | `docs/legacy/build-windows-typing.ps1` | 2026-10-19 |
| 新增 | `docs/legacy/build-windows.ps1` | 2026-10-19 |
| 原样移入历史目录 | `docs/legacy/原项目-release-workflow.yml` | 2026-10-19 |
| 新增 | `docs/同步打字插件使用说明.md` | 2026-10-20 |
| 新增 | `docs/同步打字验证清单.md` | 2026-10-20 |
| 新增 | `docs/插件开发指南.md` | 2026-10-19 |
| 新增 | `docs/插件版使用说明.md` | 2026-10-19 |
| 新增 | `docs/本次静态检查记录.txt` | 2026-10-19 |
| 新增 | `docs/构建与验证记录.md` | 2026-10-19 |
| 新增 | `global.json` | 2026-10-19 |
| 新增 | `plugins/DictationAssistant.Importers.Docx/DictationAssistant.Importers.Docx.csproj` | 2026-10-19 |
| 新增 | `plugins/DictationAssistant.Importers.Docx/DocxImporter.cs` | 2026-10-19 |
| 新增 | `plugins/DictationAssistant.Importers.Docx/README.md` | 2026-10-19 |
| 新增 | `plugins/DictationAssistant.Importers.Docx/plugin.json` | 2026-10-19 |
| 新增 | `plugins/DictationAssistant.Practices.Typing/DictationAssistant.Practices.Typing.csproj` | 2026-10-20 |
| 新增 | `plugins/DictationAssistant.Practices.Typing/PracticeCrashLog.cs` | 2026-10-20 |
| 新增 | `plugins/DictationAssistant.Practices.Typing/RepetitionPlayback.cs` | 2026-10-19 |
| 新增 | `plugins/DictationAssistant.Practices.Typing/SynchronousTypingPlugin.cs` | 2026-10-20 |
| 新增 | `plugins/DictationAssistant.Practices.Typing/TypingEvaluation.cs` | 2026-10-19 |
| 新增 | `plugins/DictationAssistant.Practices.Typing/TypingPracticeWindow.cs` | 2026-10-20 |
| 新增 | `plugins/DictationAssistant.Practices.Typing/plugin.json` | 2026-10-20 |
| 新增 | `scripts/prepare_release.py` | 2026-10-19 |
| 新增 | `scripts/verify_build.py` | 2026-10-19 |
| 新增 | `sdk/README.md` | 2026-10-19 |
| 新增 | `sdk/examples/ExtensionToolkit/ExtensionToolkit.cs` | 2026-10-19 |
| 新增 | `sdk/examples/ExtensionToolkit/ExtensionToolkit.csproj` | 2026-10-19 |
| 新增 | `sdk/examples/ExtensionToolkit/plugin.json` | 2026-10-19 |
| 新增 | `sdk/examples/GainEffect/GainEffect.cs` | 2026-10-19 |
| 新增 | `sdk/examples/GainEffect/GainEffect.csproj` | 2026-10-19 |
| 新增 | `sdk/examples/GainEffect/plugin.json` | 2026-10-19 |
| 新增 | `sdk/examples/PhrasesImporter/PhrasesImporter.cs` | 2026-10-19 |
| 新增 | `sdk/examples/PhrasesImporter/PhrasesImporter.csproj` | 2026-10-19 |
| 新增 | `sdk/examples/PhrasesImporter/plugin.json` | 2026-10-19 |
| 新增 | `sdk/examples/ToneVoice/ToneVoice.cs` | 2026-10-19 |
| 新增 | `sdk/examples/ToneVoice/ToneVoice.csproj` | 2026-10-19 |
| 新增 | `sdk/examples/ToneVoice/plugin.json` | 2026-10-19 |
| 修改 | `src/DictationAssistant.App/Abstractions/IDictationPlayer.cs` | 2026-10-19 |
| 修改 | `src/DictationAssistant.App/App.axaml.cs` | 2026-10-19 |
| 修改 | `src/DictationAssistant.App/Audio/PcmWriter.cs` | 2026-10-19 |
| 新增 | `src/DictationAssistant.App/Audio/PluginAudioBridge.cs` | 2026-10-19 |
| 修改 | `src/DictationAssistant.App/Audio/SdlPcmPlayer.cs` | 2026-10-19 |
| 修改 | `src/DictationAssistant.App/DictationAssistant.App.csproj` | 2026-10-20 |
| 新增 | `src/DictationAssistant.App/Extensions/WordListShuffle.cs` | 2026-10-19 |
| 修改 | `src/DictationAssistant.App/Helpers/CachedDataLoader.cs` | 2026-10-19 |
| 新增 | `src/DictationAssistant.App/Importing/DocumentImportService.cs` | 2026-10-19 |
| 新增 | `src/DictationAssistant.App/Importing/ImportCommandLine.cs` | 2026-10-19 |
| 新增 | `src/DictationAssistant.App/Importing/ImportPreviewBuilder.cs` | 2026-10-19 |
| 新增 | `src/DictationAssistant.App/Importing/PluginCommandLine.cs` | 2026-10-19 |
| 新增 | `src/DictationAssistant.App/MainWindow.PluginContext.cs` | 2026-10-19 |
| 新增 | `src/DictationAssistant.App/MainWindow.Plugins.cs` | 2026-10-19 |
| 新增 | `src/DictationAssistant.App/MainWindow.Practice.cs` | 2026-10-20 |
| 新增 | `src/DictationAssistant.App/MainWindow.WordListShuffle.cs` | 2026-10-19 |
| 修改 | `src/DictationAssistant.App/MainWindow.axaml` | 2026-10-19 |
| 修改 | `src/DictationAssistant.App/MainWindow.axaml.cs` | 2026-10-19 |
| 新增 | `src/DictationAssistant.App/PluginManagerWindow.axaml` | 2026-10-19 |
| 新增 | `src/DictationAssistant.App/PluginManagerWindow.axaml.cs` | 2026-10-19 |
| 新增 | `src/DictationAssistant.App/Plugins/PluginCatalog.cs` | 2026-10-19 |
| 新增 | `src/DictationAssistant.App/Plugins/PluginLoadContext.cs` | 2026-10-19 |
| 新增 | `src/DictationAssistant.App/Plugins/PluginManifest.cs` | 2026-10-19 |
| 新增 | `src/DictationAssistant.App/Plugins/PluginRuntime.cs` | 2026-10-19 |
| 修改 | `src/DictationAssistant.App/Program.cs` | 2026-10-19 |
| 修改 | `src/DictationAssistant.App/Services/AudioExporter.cs` | 2026-10-19 |
| 修改 | `src/DictationAssistant.App/Services/DictationPlayer.cs` | 2026-10-19 |
| 修改 | `src/DictationAssistant.App/Settings/AppSettingsStore.cs` | 2026-10-19 |
| 修改 | `src/DictationAssistant.App/ViewModels/MainWindowViewModel.cs` | 2026-10-19 |
| 修改 | `src/DictationAssistant.App/Voice/EdgeTtsVoice.cs` | 2026-10-19 |
| 新增 | `src/DictationAssistant.App/Voice/PluginEffectVoice.cs` | 2026-10-19 |
| 新增 | `src/DictationAssistant.App/Voice/PluginVoiceFactoryProvider.cs` | 2026-10-19 |
| 修改 | `src/DictationAssistant.App/Voice/VoiceAggregator.cs` | 2026-10-19 |
| 新增 | `src/DictationAssistant.App/WordListImportWindow.axaml` | 2026-10-19 |
| 新增 | `src/DictationAssistant.App/WordListImportWindow.axaml.cs` | 2026-10-19 |
| 新增 | `src/DictationAssistant.App/packages.lock.json` | 2026-10-19 |
| 新增 | `src/DictationAssistant.Plugin.Abstractions/DictationAssistant.Plugin.Abstractions.csproj` | 2026-10-19 |
| 新增 | `src/DictationAssistant.Plugin.Abstractions/IDocumentImporter.cs` | 2026-10-20 |
| 新增 | `src/DictationAssistant.Plugin.Abstractions/IPluginAudio.cs` | 2026-10-19 |
| 新增 | `src/DictationAssistant.Plugin.Abstractions/IPluginExtension.cs` | 2026-10-19 |
| 新增 | `src/DictationAssistant.Plugin.Abstractions/IPracticePlugin.cs` | 2026-10-20 |
| 新增 | `tests/TypingPlugin.SmokeTests/Program.cs` | 2026-10-19 |
| 新增 | `tests/TypingPlugin.SmokeTests/TypingPlugin.SmokeTests.csproj` | 2026-10-19 |

## 本次补入的第三方原始源码

`third-party/sources/libgifcodec-d06d2a6d42baf6c0c91cacc28df2542a911d05fe/` 提供固定上游提交的原始源码副本及来源说明，用于第三方源码交付。原文件的版权头和多重许可保持不变；实际采用的许可选项及组合依据由 [第三方组件声明](THIRD_PARTY_NOTICES.md) 和 [`third-party/components.json`](third-party/components.json) 记录。该目录不属于本 fork 原创或重新授权为 AGPL 的源码，补入文件不表示第三方版权转移给维护者。

## 同版本原生重建与源码交付

2026-10-20 保留 Avalonia 11.3.12、SkiaSharp 2.88.9，在原生重建中关闭 RAW/DNG、PIEX，未升级图形 API。新增固定源码/工具配置、两个下载与构建补丁、构建脚本、独立工作流及 [NATIVE_BUILD.md](docs/NATIVE_BUILD.md)。实际 [CI 39355910024](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39355910024) 已成功：1,124 输入及编译/实现检查排除 DNG/PIEX，941 个导出与官方相同、原生版本 88.1，上游 PNG 和 production App Windows 窗口开闭通过。新 DLL 与两 ZIP 的实际哈希及未验证范围均保留在原生构建文档和[原始证据](third-party/evidence/skia-no-dng-2.88.9-win-x64/README.md)。

本次补齐 HarfBuzz 和 ANGLE 的 Windows 编译/修改源码覆盖，分别保留 [固定输入与生成材料](third-party/sources/harfbuzzsharp-8.3.1.1-windows-inputs/README.md)和 [ANGLE 来源/交付清单](third-party/sources/angle-cb8b4e1-source-directions/README.md)。原始第三方字节、版权与许可不改，具体变换和可复建边界由对应目录记录；不宣称历史 DLL 逐字节可复现。

原 NuGet DLL 的 DNG 证据和原始通知保留用于历史溯源，与已验证的新替代分开记录。独立原生资产不含 Avalonia，技术检查已通过、源码闭包仍在复核，计划独立 native Release，当前未公开；[App 与实际 DLL 中 MS-PL 部分的组合授权路径独立未解决](third-party/evidence/avalonia-mspl-11.3.12/REVIEW.md)，特定源文件未找到 AGPL 双授权或链接例外，原许可不改写，当前 App 未公开发布。

## 本次许可修复移除的原生库与配置

2026-10-20 的新版从发行与源码树移除整个 `src/DictationAssistant.App/BassRuntimeAssets/` 目录，包括下列 40 个 BASS 原生二进制及 7 个 `bass_plugin` 配置标记；下表来自审计观察点的原始跟踪清单。相关加载、引用及替代解码实现的具体变化以本次提交为准。历史标签和资产没有因此被重写。

| 操作 | 原始文件 | 修改日期 |
| --- | --- | --- |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/linux-arm/native/libbass.so` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/linux-arm/native/libbass_aac.so` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/linux-arm/native/libbassalac.so` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/linux-arm/native/libbassape.so` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/linux-arm/native/libbassflac.so` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/linux-arm/native/libbassopus.so` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/linux-arm64/native/libbass.so` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/linux-arm64/native/libbass_aac.so` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/linux-arm64/native/libbassalac.so` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/linux-arm64/native/libbassape.so` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/linux-arm64/native/libbassflac.so` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/linux-arm64/native/libbassopus.so` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/linux-x64/native/libbass.so` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/linux-x64/native/libbass_aac.so` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/linux-x64/native/libbassalac.so` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/linux-x64/native/libbassape.so` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/linux-x64/native/libbassflac.so` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/linux-x64/native/libbassopus.so` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/linux-x86/native/libbass.so` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/linux-x86/native/libbass_aac.so` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/linux-x86/native/libbassalac.so` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/linux-x86/native/libbassape.so` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/linux-x86/native/libbassflac.so` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/linux-x86/native/libbassopus.so` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/osx/native/libbass.dylib` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/osx/native/libbassape.dylib` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/osx/native/libbassflac.dylib` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/osx/native/libbassopus.dylib` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/win-x64/native/bass.dll` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/win-x64/native/bass_aac.dll` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/win-x64/native/bassalac.dll` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/win-x64/native/bassape.dll` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/win-x64/native/bassflac.dll` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/win-x64/native/bassopus.dll` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/win-x86/native/bass.dll` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/win-x86/native/bass_aac.dll` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/win-x86/native/bassalac.dll` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/win-x86/native/bassape.dll` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/win-x86/native/bassflac.dll` | 2026-10-20 |
| 删除 | `src/DictationAssistant.App/BassRuntimeAssets/win-x86/native/bassopus.dll` | 2026-10-20 |
| 删除配置标记 | `src/DictationAssistant.App/BassRuntimeAssets/linux-arm/native/bass_plugin` | 2026-10-20 |
| 删除配置标记 | `src/DictationAssistant.App/BassRuntimeAssets/linux-arm64/native/bass_plugin` | 2026-10-20 |
| 删除配置标记 | `src/DictationAssistant.App/BassRuntimeAssets/linux-x64/native/bass_plugin` | 2026-10-20 |
| 删除配置标记 | `src/DictationAssistant.App/BassRuntimeAssets/linux-x86/native/bass_plugin` | 2026-10-20 |
| 删除配置标记 | `src/DictationAssistant.App/BassRuntimeAssets/osx/native/bass_plugin` | 2026-10-20 |
| 删除配置标记 | `src/DictationAssistant.App/BassRuntimeAssets/win-x64/native/bass_plugin` | 2026-10-20 |
| 删除配置标记 | `src/DictationAssistant.App/BassRuntimeAssets/win-x86/native/bass_plugin` | 2026-10-20 |

## 保留与边界

- 审计观察点的 43 个二进制均与上游字节一致，其中 3 个原图标/图片保留，40 个 BASS 原生二进制在本次新版移除。历史包中的字节一致只能证明来源相同，不能证明已取得再分发权或解决与 AGPL 的兼容性。
- 原工作流 `.github/workflows/release.yml` 原样保留为 `docs/legacy/原项目-release-workflow.yml`，不将这次移动称为重新创作。
- 根目录 AGPL 许可证全文保持原样，上游 `Copyright (C) 2013-2026 alampy.com` 及授权、无担保短通知在 README 和 NOTICE 中恢复保留。
- 源码许可与修改声明可以放在项目级文件中；没有在每个原始文件上另行添加新的版权人，也没有将第三方文件批量重标为 AGPL。
- 本 fork 的 AI 辅助修改说明仅覆盖本 fork 的修改，不覆盖上游原作的创作过程。
- 当前 `typing.4` 为源码预览，no-DNG 替代已通过技术与实物验证，App 与 Avalonia 中 MS-PL 部分的组合授权路径独立未解决，App 公开发布继续暂停。本文件、特定组件源码覆盖或构建成功均不等于全面合规认证；具体证据与处理状态见 [版权与许可审计](docs/版权与许可审计.md)。
