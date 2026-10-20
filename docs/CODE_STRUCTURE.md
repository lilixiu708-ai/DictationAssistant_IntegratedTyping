# 代码结构与修改入口

主程序保持单体 App；共享契约只描述宿主和插件之间的数据与能力。插件按功能独立构建，通过 `plugin.json` 侧载，拥有各自的版本和私有依赖。

## 仓库目录

| 目录 | 职责 |
| --- | --- |
| `src/DictationAssistant.App/` | Avalonia 窗口、词表编辑、播报与音频、设置、插件加载和调用 |
| `src/DictationAssistant.Plugin.Abstractions/` | 导入、练习、配置、生命周期等公开契约 |
| `plugins/DictationAssistant.Importers.Docx/` | Word / Excel 内容读取，不修改宿主词表 |
| `plugins/DictationAssistant.Practices.Typing/` | 同步打字、评分、译文和翻译服务配置 |
| `sdk/examples/` | 使用公开契约的插件示例 |
| `scripts/`、`.github/workflows/` | 构建验证、源码与许可收集、版本化发行 |
| `docs/` | 当前使用与开发说明；`legacy/` 保留历史材料 |
| `LICENSES/`、`third-party/` | 原始声明、依赖来源、固定版本与审核记录 |
| `artifacts/`、各项目 `bin/` 和 `obj/` | 已忽略的生成物，不提交到 Git |

## 主程序与导入

`MainWindow` 按功能使用 partial：`Plugins` 管理插件菜单与生命周期，`PluginContext` 提供宿主能力，`WordListImport` 负责文件选择与词表写回，`Practice` 连接练习插件，`WordListShuffle` 处理打乱。

文档导入的数据流是：选择文件 → `PluginRuntime` 调用插件 → `DocumentImportService` 校验并复制结果 → `ImportPreviewBuilder` 选择区域和列 → 预览窗口 → 用户确认后写回编辑器。命令行与 GUI 共用预览规则；选择“全部区域”时优先使用表格，没有表格才合并正文。

普通文本通过 `WordListTextReader` 在后台读取，统一处理字节上限、BOM、严格 UTF-8 和取消。`Helpers/WordListText` 统一统计 CR/LF 分隔的非空白行；重复条目保留。

导入插件中，`DocxImporter` 是固定入口并读取 Word XML；`ExcelImporter` 负责工作表、行列和单元格值；`ExcelPackage` 负责包预检与取消保护；`XlsbRecordValidator` 校验 BIFF12 记录；`ImportStreams` 负责有大小上限的异步复制。插件返回原始内容与表头标记，宿主预览才执行空白规范化、跳过表头或去重。修改读取规则时须同时核对原始内容和最终词表。

## 同步打字插件

| 修改内容 | 主要入口 |
| --- | --- |
| 插件能力与宿主连接 | `SynchronousTypingPlugin` |
| 播报、计时、输入与练习流程 | `TypingPracticeWindow`、`RepetitionPlayback` |
| 判分、成绩和 CSV | `TypingEvaluation`、`TypingResults`、`TypingResultRow` |
| 录入译文、提前翻译和本地译文 | `TranslationEditorWindow`、`PracticeTranslations` |
| 配置窗口与未保存的编辑 | `TranslationApiSettingsWindow`、`TranslationEngineDraft` |
| 服务定义、统一校验与当前会话 | `TranslationEngineProfiles` |
| 本地加密保存与迁移 | `TranslationApiSettings` |
| 服务调用、分批与 HTTP | `TranslationService`、`TranslationBatches`、`TranslationHttp`、各 `*Translator` |

配置窗口编辑独立草稿，应用后才更新会话；持久化由设置存储负责。服务字段和参数约束在 `TranslationEngineProviders` 定义，UI 与存储使用同一套校验。已有凭据不回填到输入框；同一服务留空时可沿用，切换服务时须重新填写。

提前翻译只在用户点击后调用当前绑定，得到的结果经校对保存到本机译文。练习阶段使用已有译文，不发送翻译请求。成绩计算和 CSV 序列化独立于窗口，虚拟化结果行只负责显示。

## 构建与修改边界

构建命令见[贡献规则](../CONTRIBUTING.md#构建与检查)和[云环境指南](CLOUD_ENVIRONMENT.md)。优先使用已有的逻辑检查、插件发现命令和实际导入样本验证；涉及 UI、系统加密、音频和输入法时，记录实际平台与未验证范围。

同一项目内新增 C# 文件会由 SDK 自动编译，插件源码打包按整个项目目录收集已提交文件。新增脚本公共模块则须同步发布脚本的源码白名单。程序集名称、插件 ID、接口版本和已发布标签保持稳定；源码重构后须通过新版本发行，不能覆盖旧 Release。具体流程见[发布规范](RELEASING.md)。
