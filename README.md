# DictationAssistant Typing · 同步打字练习版

用于单词、短语和句子听写的桌面程序，基于 C#、.NET 10 与 Avalonia。支持语音播报、Word 词表导入、随机顺序和独立的同步打字练习插件。

当前发布版本为 **`v4.0.0-typing.1` 预览版**，面向 Windows x64，基于上游 4.x 定制。产品名称为 **DictationAssistant Typing**，GitHub 仓库保留 [`lilixiu708-ai/DictationAssistant_IntegratedTyping_GitHubReady`](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping_GitHubReady)。C# 项目、程序集和命名空间保持 `DictationAssistant.*`。

## 下载与使用

从 [Releases](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping_GitHubReady/releases) 下载 Windows x64 便携 ZIP，完整解压后运行 `DictationAssistant.App.exe`。程序包自带 .NET 运行时和两个插件，普通使用无需安装 SDK。

升级时请解压到新的空文件夹，再打开已有词表，避免旧版依赖残留。GitHub 自动生成的 Source code 压缩包用于开发，运行程序请选择随 Release 上传的 Windows ZIP。

1. 编辑每行一条的词表，或通过「插件 → 从文档提取词表」导入 `.docx`。
2. 设置语音、播报次数和间隔；需要时使用「打乱顺序」。
3. 打开「插件 → 同步打字练习」，设置时限后开始；播报结束再输入，整轮结束后查看成绩或导出 CSV。

详细操作见 [同步打字插件使用说明](docs/同步打字插件使用说明.md)。问题请提交到 [Issues](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping_GitHubReady/issues)，注明版本、系统和复现步骤。

## 当前功能与验证范围

- DOCX 导入：选择表格或正文、选择列、预览、去重、替换或追加词表。
- 听写练习：保留重复词条和随机顺序；播报完成后开放输入，支持限时作答和 CSV 导出。
- 性能优化：降低倒计时文本刷新频率，后台初始化语音，成绩列表按可见范围创建和复用控件。
- 便携包：保留自带 .NET；插件共享主程序图形依赖，减少重复文件。

云环境已完成构建、现有 15 项打字逻辑检查、插件加载、DOCX 提取及 Windows 包校验。**真实 Windows 窗口、扬声器音频、中文输入法和内存降幅仍需实机验证**，执行步骤见 [Windows 验证清单](docs/同步打字验证清单.md)。

## 仓库结构

| 路径 | 内容 |
| --- | --- |
| `src/DictationAssistant.App/` | Avalonia 主程序、音频和插件加载 |
| `src/DictationAssistant.Plugin.Abstractions/` | 文档导入与练习插件的共享契约 |
| `plugins/` | DOCX 导入、同步打字两个独立插件 |
| `sdk/examples/` | 插件开发示例 |
| `tests/TypingPlugin.SmokeTests/` | 已有的打字逻辑检查程序 |
| `scripts/`、`.github/workflows/` | 包校验和 CI |
| `docs/` | 使用、开发、验证和历史记录 |
| `artifacts/` | 本地生成的程序包，不提交到 Git |

插件 ID 与 API 版本独立于应用发布版本，详见 [贡献规则](CONTRIBUTING.md)。

## 开发与构建

安装 .NET 10 SDK。`global.json` 的基线是 `10.0.100`，允许 .NET 10 的较新 feature band，禁止预发布 SDK；首次还原 NuGet 依赖需要联网。

Windows 完整包使用仓库根目录中的纯 CMD 脚本：

```bat
build-windows-typing.cmd
python scripts\verify_build.py
```

脚本以 `win-x64 --locked-mode` 还原主程序，构建两个插件、运行已有检查并生成 `artifacts/DictationAssistant_IntegratedTyping_Windows_x64.zip`。也可在仓库的 [Actions](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping_GitHubReady/actions) 中运行相同构建。

Linux 开发使用各项目 `obj/` 内的本地锁文件，保留已提交的 Windows RID 锁文件：

```bash
dotnet restore DictationAssistant.slnx -p:NuGetLockFilePath=obj/cloud.packages.lock.json
dotnet build DictationAssistant.slnx -c Release --no-restore
dotnet run --project src/DictationAssistant.App/DictationAssistant.App.csproj -c Release --no-build --no-restore
```

窗口运行需要可用的图形会话及本机音频依赖。上述解决方案构建用于开发，不会自动组装预装插件的便携目录；完整 Windows 包使用 CMD 脚本。

## 文档与贡献

- [文档索引](docs/README.md)
- [插件开发指南](docs/插件开发指南.md)
- [贡献、命名与验证规则](CONTRIBUTING.md)
- [代理开发约定](AGENTS.md)

## 来源与许可

基于上游 [ArcticLampyrid/DictationAssistant](https://github.com/ArcticLampyrid/DictationAssistant) 的 4.x 源码及其原有功能进行扩展，保留原作者与第三方组件声明。本仓库是定制版本，采用 **AGPL-3.0-or-later**，完整条款见 [LICENSE.md](LICENSE.md)。
