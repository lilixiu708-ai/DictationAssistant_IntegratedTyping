# DictationAssistant Typing · 同步打字练习版

面向单词、短语和句子听写的 Windows x64 桌面程序，基于 C#、.NET 10 与 Avalonia 11，在上游 DictationAssistant v4.0.0 上扩展。

**当前公开版本：R6（`v4.0.0-typing.6`）预发布。** Avalonia 11.3.12 的 MS-PL/AGPL 组合授权事项仍未解决，已随发行披露；原始许可与对应源码保留。详见[许可审计](docs/版权与许可审计.md)。

同步打字插件 **1.3.1 已独立发布**，支持 DeepL Free / Pro、必应 / Microsoft（Azure）、Google Cloud、百度、有道、腾讯云、阿里云和火山引擎翻译 API，可管理多个命名绑定、首选项并在准备页临时切换。只需更新插件，沿用 R6 本体和 Word 1.0.1；最低宿主功能级别仍为 4，不能直接用于 R5。

Word / Excel 导入插件 **1.1.0 正在准备独立发布，尚未公开**：新增 `.xlsx`、`.xls`、`.xlsb`、`.xlsm`，按工作表预览并选择列，保留 Unicode 文本；下方已发布的 Word 1.0.1 仍只支持 DOCX。读取库选用 [ExcelDataReader 3.9.0 固定源码](https://github.com/ExcelDataReader/ExcelDataReader/tree/f3343f5524518923bdeb4801e5003c0417fd35f3)，本轮 Linux 样本验证及未验证范围见 [Excel 导入验证](docs/Excel导入验证.md)。

## 功能

- 词表：文本编辑、DOCX 表格/正文导入、选择列、预览、去重和打乱顺序。
- 同步打字：播报结束后开放输入，限时作答、评分和 CSV 导出；后台核对实际/理论词条数，异常保存日志。
- 翻译提示：提前录入或在程序内配置后补全，校对保存；打字时可显示或隐藏。
- 界面与资源：倒计时减少重复刷新，成绩列表按可见范围加载，统一取消与资源释放。
- 插件 SDK：文档导入、自定义练习、通用命令、词表观察、语音引擎、音效及生命周期管理。

## 下载与使用

首次安装先下载 R6 本体，已包含 .NET 运行环境、同步打字 1.2.0 和 Word 导入插件；需要新增翻译服务时，再安装下方的同步打字 1.3.1 独立包。完整解压到新文件夹，运行 `DictationAssistant.App.exe`。

| 用途 | 版本与下载 | 选择的附件 |
| --- | --- | --- |
| 安装或更新完整程序 | [R6 本体](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/v4.0.0-typing.6) | `*-win-x64-portable.zip` |
| 单独更新同步打字 | [插件 1.3.1](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/plugin-synchronous-typing-v1.3.1) | [安装包](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/plugin-synchronous-typing-v1.3.1/synchronous-typing-1.3.1.zip) · [源码](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/plugin-synchronous-typing-v1.3.1/synchronous-typing-1.3.1-source.zip) · [SHA256SUMS](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/plugin-synchronous-typing-v1.3.1/SHA256SUMS) |
| 单独更新 Word 导入 | [插件 1.0.1](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/plugin-docx-vocabulary-v1.0.1) | `docx-vocabulary-1.0.1.zip` |

更新插件前关闭程序，将 ZIP 内完整插件目录替换到 `Plugins/` 后重启。源码开发选择对应 Release 的 `*-source.zip`；`SHA256SUMS` 用于校验下载，GitHub 自动生成的源码附件不包含额外收集的依赖源码。

1. 编辑每行一条的词表，或通过「插件 → 从文档提取词表」导入 `.docx`。
2. 设置语音、播报次数及间隔，需要时打乱顺序。
3. 打开「插件 → 同步打字练习」，设置时限开始练习；播报完毕后输入，整轮结束查看成绩或导出 CSV。

R6 可从「插件管理 → 配置…」管理翻译绑定和首选项，准备页切换只影响本次翻译；R5 的入口仍为「录入／提前翻译 → 翻译 API 设置」。个人设置均在本机程序内完成。配置与切换不会调用翻译服务，打字过程中也不翻译。详细步骤见[使用说明](docs/同步打字插件使用说明.md)。报告问题请到 [Issues](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/issues)，附版本、系统及复现步骤。

1.3.1 接入的是各服务商的官方 API 账户，需要开通对应服务；程序按服务显示密钥、应用 ID、附加密钥及区域字段。网页翻译账号或 Cookie 不能代替 API 凭据。各服务真实账户的鉴权和在线翻译仍待验证。

## 开发与构建

Linux 云环境可运行仓库内的 `bash .codex/setup.sh`，安装和日常启动说明见[云环境指南](docs/CLOUD_ENVIRONMENT.md)。该脚本与项目一起版本管理。

需要 .NET 10 SDK（遵循 `global.json`）及 Python 3.11+；首次还原需联网。Windows 根目录 CMD 入口：

```bat
build-windows-typing.cmd
python scripts\verify_build.py
```

脚本构建宿主和两个插件、运行既有检查并准备许可材料，输出位于 `artifacts/`。首次构建会下载并校验固定的无 DNG 原生资产及配套源码，失败则停止打包；详见[原生构建说明](docs/NATIVE_BUILD.md)与[发布规范](docs/RELEASING.md)。[独立原生构建依赖](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/native-skia-2.88.9-no-dng.1)供开发和复核使用，普通安装无需下载。

Linux 开发将锁文件放在各项目 `obj/`，避免覆盖已提交的 Windows RID 锁文件：

```bash
dotnet restore DictationAssistant.slnx -p:NuGetLockFilePath=obj/cloud.packages.lock.json
dotnet build DictationAssistant.slnx -c Release --no-restore
dotnet run --project src/DictationAssistant.App/DictationAssistant.App.csproj -c Release --no-build --no-restore
```

窗口运行需要图形会话及本机音频依赖。R6 已通过 [Windows 发行构建、包校验、15 项逻辑检查及主窗口正常开闭](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39377359747)，发行提交为 `0f008c72983b9d567612855498800886201572e6`。本地设置加密、旧配置迁移、在线翻译、扬声器、中文输入法和性能变化仍须[实机验证](docs/同步打字验证清单.md)；没有测量数据不承诺 CPU 或内存降幅。

## 文档与扩展

[文档索引](docs/README.md) · [插件开发](docs/插件开发指南.md) · [SDK 与可编译示例](docs/PLUGIN_SDK.md) · [贡献规则](CONTRIBUTING.md) · [代理约定](AGENTS.md) · [变更记录](CHANGELOG.md)

主程序在 `src/DictationAssistant.App/`，共享契约在 `src/DictationAssistant.Plugin.Abstractions/`，插件在 `plugins/`，示例在 `sdk/examples/`。程序集保留 `DictationAssistant.*`，插件 ID/API 版本独立于应用版本。

OCR 尚未集成，可选开源方案见 [OCR 说明](docs/OCR_OPTIONS.md)。桌面程序及插件通过 Releases 分发，无需发布 GitHub Packages。

## 来源与许可

基于 [ArcticLampyrid/DictationAssistant v4.0.0](https://github.com/ArcticLampyrid/DictationAssistant/tree/324cd043fd13c0b9d50e287d0301cdbf85ea7c2c)。本 fork 由 `lilixiu708-ai` 维护，使用 AI 辅助生成、修改和构建；该说明不涵盖上游原作。修改日期与范围见 [MODIFICATIONS.md](MODIFICATIONS.md)，原始归属见 [NOTICE.md](NOTICE.md)。

第一方源码采用 [AGPL-3.0-or-later](LICENSE.md)，第三方保留各自许可，见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)、[LICENSES/](LICENSES/) 和[组件/固定来源目录](third-party/components.json)。再分发须核对授权、同版本对应源码及构建材料；应用源码 ZIP 或来源链接不自动等于全部原生组件的完整对应源码。

<details>
<summary>上游原始版权、授权与无担保通知</summary>

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

</details>
