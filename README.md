# DictationAssistant Typing · 听写与背诵练习

看提示背诵，或听播报后拼写。直接从 Word / Excel 选择提示与答案，默认看离子式、输入英文名称，也可交换方向；支持静音计时、成绩复核和 CSV 导出。提供 Windows x64 和 macOS 的完整包。

**Windows 当前版本：R7（`v4.0.0-typing.7`）预发布。** 完整包包含背诵练习 1.4.0、Word / Excel 导入 1.2.0 和 .NET 运行环境。“背诵练习”是原同步打字插件的新名称，原有设置与译文继续沿用。

**背诵练习 1.4.1 已独立预发布。** 练习中可点击「结算成绩」提前结束，提示开关改为「显示提示」。R7 完整包仍包含 1.4.0；已有 R7，下载下方 1.4.1 插件包并替换练习插件目录即可。[查看用法](docs/同步打字插件使用说明.md#141-更新中途结算与提示)。

**[R8 macOS 整合版已预发布](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/v4.0.0-typing.8)。** 需要 macOS 15 或更新版本，Intel 和 M 系列电脑分别提供完整包，均自带背诵练习 1.4.1、Word / Excel 导入 1.2.0 及 .NET 运行环境。[安装说明](docs/MACOS.md)。

Avalonia 11.3.12 的 MS-PL/AGPL 组合授权事项仍未解决，已随发行披露；原始许可与对应源码保留。详见[许可审计](docs/版权与许可审计.md)。

## 功能

- 文档配对：选择表格、工作表和同行的提示 / 答案列，预览或交换方向；默认跳过表头，保留顺序和重复项。
- 背诵与听写：静音计时或播报后作答，每题 1–300 秒，Enter 提交或到时记录；1.4.1 可中途结算，查看成绩、导出 CSV。
- 化学记号：保留 Word 直接上下标，接受 `Na^+`、`SO4^2-` 和 Unicode 上下标；化学式区分元素大小写，英文名称可忽略大小写。
- 提示：提前录入或使用官方翻译 API 补全，校对保存；提示可隐藏，练习过程中不联网翻译。
- 词表与扩展：文本编辑、Word / Excel 导入、选列、预览、去重和打乱；插件 SDK 提供导入、练习、命令、语音及音效接口。

## 下载与使用

Windows 首次安装或从 R6 升级，选择 **R7 完整包**，解压到新文件夹后运行 `DictationAssistant.App.exe`；需要中途结算时，再安装下方的 1.4.1 独立更新。插件要求 R7 的宿主功能级别 **5**，不能单独安装到 R6。

| 用途 | 版本与下载 | 选择的附件 |
| --- | --- | --- |
| Windows 完整程序 | [R7 本体](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/v4.0.0-typing.7) | [便携包](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/v4.0.0-typing.7/DictationAssistant-4.0.0-typing.7-win-x64-portable.zip) · [源码](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/v4.0.0-typing.7/DictationAssistant-4.0.0-typing.7-source.zip) · [SHA256SUMS](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/v4.0.0-typing.7/SHA256SUMS) |
| macOS Intel 完整程序 | [R8 macOS](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/v4.0.0-typing.8) | [x64 整合包](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/v4.0.0-typing.8/DictationAssistant-4.0.0-typing.8-macos-x64.zip) |
| macOS M 系列完整程序 | [R8 macOS](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/v4.0.0-typing.8) | [arm64 整合包](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/v4.0.0-typing.8/DictationAssistant-4.0.0-typing.8-macos-arm64.zip) |
| 单独更新背诵练习 | [插件 1.4.1](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/plugin-synchronous-typing-v1.4.1) | [安装包](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/plugin-synchronous-typing-v1.4.1/synchronous-typing-1.4.1.zip) · [源码](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/plugin-synchronous-typing-v1.4.1/synchronous-typing-1.4.1-source.zip) · [SHA256SUMS](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/plugin-synchronous-typing-v1.4.1/SHA256SUMS) |
| 单独更新 Word / Excel 导入 | [插件 1.2.0](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/plugin-docx-vocabulary-v1.2.0) | [安装包](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/plugin-docx-vocabulary-v1.2.0/docx-vocabulary-1.2.0.zip) · [源码](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/plugin-docx-vocabulary-v1.2.0/docx-vocabulary-1.2.0-source.zip) · [SHA256SUMS](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/plugin-docx-vocabulary-v1.2.0/SHA256SUMS) |

macOS 解压后打开 `payload` 文件夹，将 `DictationAssistant.app` 拖到“应用程序”；更新时替换整个 `.app`。本版使用临时签名，尚未经过 Apple 公证；首次打开被拦截时，核对来源后可从“系统设置 → 隐私与安全性”确认打开。翻译凭据在插件配置中本地输入，macOS 目前仅本次运行使用。[R8 配套源码](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/v4.0.0-typing.8/DictationAssistant-4.0.0-typing.8-macos-source.zip)和 [SHA256SUMS](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/v4.0.0-typing.8/SHA256SUMS)随发行提供。

升级前关闭程序并备份用户数据；Windows 单独更新插件时，将 ZIP 内完整目录替换到程序旁的 `Plugins/` 后重启。本机练习设置、译文和翻译绑定沿用原目录，Windows 加密绑定仍限原机器和账户。`*-source.zip` 用于源码开发，`SHA256SUMS` 用于校验附件；GitHub 自动生成的源码附件不包含额外收集的依赖源码。

1. 打开「插件 → 背诵练习」或 `Ctrl+Shift+T`，点击「从 Word / Excel 选择…」，主词表可以为空。
2. 选表格 / 工作表及提示、答案列，核对预览；选择静音计时或播报后作答，再设置每题时限。
3. 开始练习，按 Enter 提交或等待超时，整轮结束查看成绩；1.4.1 也可点击「结算成绩」提前结束。使用「当前词表」入口可继续原有播报听写。

翻译服务从「插件管理 → 背诵练习 → 配置…」管理，须使用服务商的官方 API 账户；练习前主动补全译文，结果校对后保存。支持 DeepL、Microsoft、Google、百度、有道、腾讯云、阿里云和火山引擎，真实账户在线调用仍待验证。操作与验证范围见[使用说明](docs/同步打字插件使用说明.md)；问题请附版本、系统及复现步骤提交到 [Issues](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/issues)。

历史版本：[背诵练习 1.4.0](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/plugin-synchronous-typing-v1.4.0) · [R6 本体](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/v4.0.0-typing.6) · [同步打字 1.3.1](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/plugin-synchronous-typing-v1.3.1) · [Word / Excel 1.1.0](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/plugin-docx-vocabulary-v1.1.0) · [Word 1.0.1](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/plugin-docx-vocabulary-v1.0.1)。

## 开发与构建

Linux 云环境可运行仓库内的 `bash .codex/setup.sh`，安装和日常启动说明见[云环境指南](docs/CLOUD_ENVIRONMENT.md)。该脚本与项目一起版本管理。

需要 .NET 10 SDK（遵循 `global.json`）及 Python 3.11+；首次还原需联网。Windows 根目录 CMD 入口：

```bat
build-windows-typing.cmd
python scripts\verify_build.py
```

脚本构建宿主和两个插件、运行既有检查并准备许可材料，输出位于 `artifacts/`。首次构建会下载并校验固定的无 DNG 原生资产及配套源码，失败则停止打包；详见[原生构建说明](docs/NATIVE_BUILD.md)与[发布规范](docs/RELEASING.md)。[Windows](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/native-skia-2.88.9-no-dng.1)和 [macOS](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/native-skia-2.88.9-no-dng-macos.1) 的独立原生构建依赖均已公开，普通安装无需下载。macOS 构建入口为 `build-macos-typing.sh`，两个处理器版本均已在对应 macOS runner 完成构建、签名检查及所列运行验证，见 [macOS 说明](docs/MACOS.md#验证范围)。

Linux 开发将锁文件放在各项目 `obj/`，避免覆盖已提交的 Windows RID 锁文件：

```bash
dotnet restore DictationAssistant.slnx -p:NuGetLockFilePath=obj/cloud.packages.lock.json
dotnet build DictationAssistant.slnx -c Release --no-restore
dotnet run --project src/DictationAssistant.App/DictationAssistant.App.csproj -c Release --no-build --no-restore
```

窗口运行需要图形会话及本机音频依赖。R7 源码提交为 `20251457f881d5efca52eb39dc832f8ae2e9f135`，原标签工作流的 [windows-x64 任务（39398895391）](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39398895391) 已通过完整构建、包校验、API 1 / 功能级别 5 的双插件加载、主窗口开闭、15 项既有检查及 SDK 示例构建；[恢复上传 39401716293](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39401716293) 使用同一批原构建附件，未重新构建。本地设置加密、旧配置迁移、在线翻译、扬声器、中文输入法和性能变化仍须[实机验证](docs/同步打字验证清单.md)；没有测量数据不承诺 CPU 或内存降幅。

## 文档与扩展

[文档索引](docs/README.md) · [代码结构](docs/CODE_STRUCTURE.md) · [插件开发](docs/插件开发指南.md) · [SDK 与可编译示例](docs/PLUGIN_SDK.md) · [贡献规则](CONTRIBUTING.md) · [变更记录](CHANGELOG.md)

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
