# DictationAssistant Typing · 同步打字练习版

用于单词、短语和句子听写的桌面程序，基于 C#、.NET 10 与 Avalonia。支持语音播报、Word 词表导入、随机顺序和独立的同步打字练习插件。

当前源码为 **`v4.0.0-typing.4` 源码预览，公开发布暂停**，面向 Windows x64，基于上游 `v4.0.0` 迭代。产品名称为 **DictationAssistant Typing**，GitHub 仓库为 [`lilixiu708-ai/DictationAssistant_IntegratedTyping`](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping)。C# 项目、程序集和命名空间保持 `DictationAssistant.*`。

## 下载与使用

**许可状态：** 新版移除 BASS/AAC 并替换解码实现，但已确认随附 Skia 原生库含 Adobe DNG SDK，其商业赔偿条款与 AGPL 的组合兼容性尚无权利人例外或权威结论，完整原生对应源码的交付核对也未完成。因此暂停公开二进制和 Release 发布，本地构建及源码审核资料不表示已获许可认证。历史 `typing.1`、`typing.2`、`typing.3` 已暂时撤回为草稿，保留 Git 标签及原资产，未被追溯修复。具体证据与剩余事项见[版权与许可审计](docs/版权与许可审计.md)。

当前公开下载暂停，以下便携包操作留待发布恢复后使用。恢复发布后，可从 [Releases](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases) 下载 Windows x64 便携 ZIP，完整解压后运行 `DictationAssistant.App.exe`。程序包自带 .NET 运行时和两个插件，普通使用无需安装 SDK。

升级时请解压到新的空文件夹，再打开已有词表，避免旧版依赖残留。GitHub 自动生成的 Source code 压缩包用于开发，运行程序请选择随 Release 上传的 Windows ZIP。

1. 编辑每行一条的词表，或通过「插件 → 从文档提取词表」导入 `.docx`。
2. 设置语音、播报次数和间隔；需要时使用「打乱顺序」。
3. 打开「插件 → 同步打字练习」，设置时限后开始；播报结束再输入，整轮结束后查看成绩或导出 CSV。

详细操作见 [同步打字插件使用说明](docs/同步打字插件使用说明.md)。问题请提交到 [Issues](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/issues)，注明版本、系统和复现步骤。

## 当前功能与验证范围

- DOCX 导入：选择表格或正文、选择列、预览、去重、替换或追加词表。
- 听写练习：保留重复词条和随机顺序；播报完成后开放输入，支持限时作答和 CSV 导出。
- 词表核对：后台读取并显示实际/理论条数；失败时保存 crash log，介绍居中显示，作答时移除下划线占位。
- 性能优化：降低倒计时文本刷新频率，后台初始化语音，成绩列表按可见范围创建和复用控件。
- 便携包：保留自带 .NET；插件共享主程序图形依赖，减少重复文件。
- 扩展 SDK：通用命令、自定义练习、词表观察、语音引擎及音效；提供上下文、数据目录、日志和生命周期管理。
- 稳定性：统一取消和退出清理，隔离音频回调异常，释放合成和导出资源，避免插件递归与互相等待。

云环境已完成构建、现有 15 项打字逻辑检查、插件加载、DOCX 提取及 Windows 包校验。**真实 Windows 窗口、扬声器音频、中文输入法和内存降幅仍需实机验证**，执行步骤见 [Windows 验证清单](docs/同步打字验证清单.md)。

## 仓库结构

| 路径 | 内容 |
| --- | --- |
| `src/DictationAssistant.App/` | Avalonia 主程序、音频和插件加载 |
| `src/DictationAssistant.Plugin.Abstractions/` | 导入、练习、命令、语音、音效及生命周期共享契约 |
| `plugins/` | DOCX 导入、同步打字两个独立插件 |
| `sdk/examples/` | 插件开发示例 |
| `tests/TypingPlugin.SmokeTests/` | 已有的打字逻辑检查程序 |
| `scripts/`、`.github/workflows/` | 包校验和 CI |
| `docs/` | 使用、开发、验证和历史记录 |
| `artifacts/` | 本地生成的程序包，不提交到 Git |

插件 ID 与 API 版本独立于应用发布版本，详见 [贡献规则](CONTRIBUTING.md)。

## 开发与构建

安装 .NET 10 SDK；完整 Windows 包还需 Python 3.11 或更高版本进行许可与文件校验。`global.json` 的基线是 `10.0.100`，允许 .NET 10 的较新 feature band，禁止预发布 SDK；首次还原 NuGet 依赖需要联网。

Windows 完整包使用仓库根目录中的纯 CMD 脚本：

```bat
build-windows-typing.cmd
python scripts\verify_build.py
```

脚本以 `win-x64 --locked-mode` 还原主程序，构建两个插件、运行已有检查并生成 `artifacts/DictationAssistant_IntegratedTyping_Windows_x64.zip`。也可在仓库的 [Actions](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions) 中运行相同构建。

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
- [插件扩展 SDK 与可编译示例](docs/PLUGIN_SDK.md)
- [贡献、命名与验证规则](CONTRIBUTING.md)
- [代理开发约定](AGENTS.md)
- [版权与来源声明](NOTICE.md)、[修改记录与文件范围](MODIFICATIONS.md)
- [第三方组件声明](THIRD_PARTY_NOTICES.md)、[版权与许可审计](docs/版权与许可审计.md)

## 来源与许可

本定制版本基于 [ArcticLampyrid/DictationAssistant](https://github.com/ArcticLampyrid/DictationAssistant) 的 `v4.0.0`，核对基准为 `324cd043fd13c0b9d50e287d0301cdbf85ea7c2c`。本 fork 的修改由 `lilixiu708-ai` 维护，修改日期及范围见 [MODIFICATIONS.md](MODIFICATIONS.md)。本 fork 使用 AI 工具辅助生成、修改和构建代码；该说明仅涉及本 fork 的修改，不将上游原作归为 AI 生成，也不代替许可证要求。

以下保留上游原始版权及 AGPL 授权、无担保通知：

    Copyright (C) 2013-2026 alampy.com

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as published
    by the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.

第一方源码采用 **AGPL-3.0-or-later**，完整原文见 [LICENSE.md](LICENSE.md)，来源与适用范围见 [NOTICE.md](NOTICE.md)。第三方代码、字体、原生库及其他材料保留各自权利人与原始许可；本仓库没有将它们统一重新授权为 AGPL。第三方清单、完整许可材料和待解决项分别见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)、[LICENSES/](LICENSES/) 及[许可审计](docs/版权与许可审计.md)。

分发本程序或修改版本时，应按适用许可证提供该发行版本的对应源码和构建材料。未来恢复发布时，应使用与程序包同标签的源码归档，核对 `SHA256SUMS`，并保留构建脚本、项目文件、锁文件及第三方固定来源；不能以持续变化的 `main` 分支替代发行版本源码。Skia/ANGLE 等 `DEPS` 与子模块来源定位见 [`third-party/components.json`](third-party/components.json) 和[发布规范](docs/RELEASING.md)。当前应用源码 ZIP 不代表已经单独交付全部原生库的完整对应源码；来源索引不自动替代实际源码交付义务，免责声明也不能代替必要授权。
