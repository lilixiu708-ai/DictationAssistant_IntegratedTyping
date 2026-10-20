# DictationAssistant Typing · 同步打字练习版

面向单词、短语和句子听写的 Windows x64 桌面程序，基于 C#、.NET 10 与 Avalonia 11，在上游 DictationAssistant v4.0.0 上扩展。

**当前为 `v4.0.0-typing.4` 源码预览，公开发布暂停。** BASS/AAC 已替换；不含 Adobe DNG SDK 的原生依赖仍在重建，尚未完成构建及许可、对应源码交付核对。历史 `typing.1`–`typing.3` 已暂撤为草稿，保留标签与原资产。进展与剩余事项见[许可审计](docs/版权与许可审计.md)。

## 功能

- 词表：文本编辑、DOCX 表格/正文导入、选择列、预览、去重和打乱顺序。
- 同步打字：播报结束后开放输入，限时作答、评分和 CSV 导出；后台核对实际/理论词条数，异常保存日志。
- 界面与资源：倒计时减少重复刷新，成绩列表按可见范围加载，统一取消与资源释放。
- 插件 SDK：文档导入、自定义练习、通用命令、词表观察、语音引擎、音效及生命周期管理。

## 使用

发布恢复后，从 [Releases](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases) 下载 Windows x64 便携 ZIP，解压到新文件夹并运行 `DictationAssistant.App.exe`。便携包自带 .NET 和两个插件；GitHub 自动源码 ZIP 用于开发。

1. 编辑每行一条的词表，或通过「插件 → 从文档提取词表」导入 `.docx`。
2. 设置语音、播报次数及间隔，需要时打乱顺序。
3. 打开「插件 → 同步打字练习」，设置时限开始练习；播报完毕后输入，整轮结束查看成绩或导出 CSV。

详细步骤见[使用说明](docs/同步打字插件使用说明.md)。报告问题请到 [Issues](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/issues)，附版本、系统及复现步骤。

## 开发与构建

需要 .NET 10 SDK（遵循 `global.json`）及 Python 3.11+；首次还原需联网。Windows 根目录 CMD 入口：

```bat
build-windows-typing.cmd
python scripts\verify_build.py
```

脚本构建宿主和两个插件、运行既有检查并准备许可材料，输出位于 `artifacts/`。未配置已核验的无 DNG 原生资产时，Windows 打包会停止；先阅读[原生构建说明](docs/NATIVE_BUILD.md)与[发布规范](docs/RELEASING.md)。

Linux 开发将锁文件放在各项目 `obj/`，避免覆盖已提交的 Windows RID 锁文件：

```bash
dotnet restore DictationAssistant.slnx -p:NuGetLockFilePath=obj/cloud.packages.lock.json
dotnet build DictationAssistant.slnx -c Release --no-restore
dotnet run --project src/DictationAssistant.App/DictationAssistant.App.csproj -c Release --no-build --no-restore
```

窗口运行需要图形会话及本机音频依赖。真实 Windows 窗口、扬声器、中文输入法和性能变化仍须[实机验证](docs/同步打字验证清单.md)；没有测量数据不承诺 CPU 或内存降幅。

## 文档与扩展

[文档索引](docs/README.md) · [插件开发](docs/插件开发指南.md) · [SDK 与可编译示例](docs/PLUGIN_SDK.md) · [贡献规则](CONTRIBUTING.md) · [代理约定](AGENTS.md) · [变更记录](CHANGELOG.md)

主程序在 `src/DictationAssistant.App/`，共享契约在 `src/DictationAssistant.Plugin.Abstractions/`，插件在 `plugins/`，示例在 `sdk/examples/`。程序集保留 `DictationAssistant.*`，插件 ID/API 版本独立于应用版本。

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
