# 文档索引

当前本体为 **[`v4.0.0-typing.6`（R6）预发布](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/v4.0.0-typing.6)**；独立插件更新为[同步打字 1.3.1](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/plugin-synchronous-typing-v1.3.1)和 [Word / Excel 导入 1.1.0](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/plugin-docx-vocabulary-v1.1.0)。普通安装、更新方法和附件选择见[项目首页](../README.md#下载与使用)。

R6 包内仍是同步打字 1.2.0 与 Word 导入 1.0.1；新增翻译服务和 Excel 导入通过上面的独立包更新。同步打字 1.3.1 需要宿主功能级别 4，不能直接用于 R5。

Excel 的逐工作表读取、Linux 全量样本比对和 Windows 合成样本检查见 [Excel 导入验证](Excel导入验证.md)。源代码职责与数据流见[代码结构](CODE_STRUCTURE.md)。

无 DNG 原生依赖已独立发布，供构建和复核使用。Avalonia 11.3.12 的 MS-PL/AGPL 组合授权事项仍按发行说明披露，详见[审计记录](版权与许可审计.md)和[发布规范](RELEASING.md)。历史版本与构建资料保留追溯。

## 当前文档

| 文档 | 用途 |
| --- | --- |
| [同步打字插件使用说明](同步打字插件使用说明.md) | 下载与更新、词表、翻译提示、练习和成绩导出 |
| [Windows 验证清单](同步打字验证清单.md) | Windows 界面、输入、音频、长列表与资源占用的实机检查 |
| [Excel 导入验证](Excel导入验证.md) | 导入插件 1.1.0 的选型依据、19 页样本独立比对结果及未验证范围 |
| [插件开发指南](插件开发指南.md) | 文档导入接口、插件清单、练习接口及 SDK 示例 |
| [插件扩展 SDK](PLUGIN_SDK.md) | 功能级别 2 的命令、词表观察、生命周期、语音、音效及可编译示例 |
| [离线 OCR 方案](OCR_OPTIONS.md) | 三条候选路线、许可分层与可选插件建议，尚未集成 |
| [版权与来源声明](../NOTICE.md) | 原作者版权、许可适用范围和 AI 辅助修改说明 |
| [修改声明与文件范围](../MODIFICATIONS.md) | 上游基准、修改日期及逐文件范围 |
| [版权与许可审计](版权与许可审计.md) | 已核对内容、补齐事项及尚未解决的第三方授权问题 |
| [第三方组件声明](../THIRD_PARTY_NOTICES.md) | 第三方归属、原始许可与来源索引 |
| [贡献规则](../CONTRIBUTING.md) | 目录、命名、开发与验证约定 |
| [代码结构](CODE_STRUCTURE.md) | 主程序和插件的职责、导入与翻译数据流、修改入口 |
| [发布说明](RELEASING.md) | 本体、插件和构建依赖的发行分类、标签及资产校验 |
| [云环境指南](CLOUD_ENVIRONMENT.md) | 仓库内 `bash .codex/setup.sh`、Linux 构建与日常启动 |
| [原生构建说明](NATIVE_BUILD.md) | [独立原生依赖](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/native-skia-2.88.9-no-dng.1)及其复核材料，普通安装无需下载 |
| [变更记录](../CHANGELOG.md) | 当前版本改动与限制 |
| [AGENTS.md](../AGENTS.md) | 架构与代理开发规则 |
| [英文示例词表](W02_听力词汇_英文示例.txt) | 本地词表格式参考 |

## 历史资料

以下文档记录早期交付，保留用于追溯。其中的包名、SDK、构建状态、目录和验证结论只适用于对应历史版本，当前操作以首页及当前文档为准。

| 文档 | 原始范围 |
| --- | --- |
| [早期构建与许可验证](../BUILD_VALIDATION.md) | `typing.4` 期间的构建及审核观察，不代表 R5 当前状态 |
| [插件版使用说明](插件版使用说明.md) | 早期 `0.0.0-plugins.1` DOCX 导入版 |
| [构建与验证记录](构建与验证记录.md) | 早期 DOCX 版构建和提取、Linux 窗口核对记录 |
| [静态检查记录](本次静态检查记录.txt) | 最初源码静态核对，不代表当前构建验证状态 |
| [`legacy/`](legacy/) | 历史 PowerShell 脚本与上游工作流存档，不是当前构建入口 |
