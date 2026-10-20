# 版权、来源与许可声明

本文件随源码与发行资料提供，用于保留归属和说明适用范围；它不授予超出原始许可证的权利，也不修改 [LICENSE.md](LICENSE.md) 的原文。

## 上游原始通知

上游项目：[ArcticLampyrid/DictationAssistant](https://github.com/ArcticLampyrid/DictationAssistant)。核对版本为 `v4.0.0`，提交 `324cd043fd13c0b9d50e287d0301cdbf85ea7c2c`。以下通知原文取自该版本的 README：

```text
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
```

上游提供的源码、图标、图片及其他原有材料保留原有版权和声明。本次审计确认 [LICENSE.md](LICENSE.md) 与上述上游版本逐字相同；许可证文本中 Free Software Foundation 的版权属于许可证文档本身，不替代应用作者的版权通知。

## 本 fork 的修改

发行仓库：<https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping>。本 fork 由 `lilixiu708-ai` 维护；2026-10-19、2026-10-20 的修改包括文档导入、词表打乱、同步打字插件、插件 SDK、界面与资源释放调整、构建及发行流程。逐文件范围和日期见 [MODIFICATIONS.md](MODIFICATIONS.md)，功能细节见 [CHANGELOG.md](CHANGELOG.md)。这些修改说明不是新的版权转让或对未核实权属的声明。

本 fork 使用 AI 工具辅助代码生成、修改和构建。该说明仅针对本 fork 的修改；上游代码与第三方材料仍归各自权利人，并继续受相应许可证约束。AI 使用说明不表示所有代码经过人工编写或全面验证。

## 第三方材料与未解决事项

第一方源码按 AGPL-3.0-or-later 提供。第三方 NuGet 包、原生库、运行时及其他材料各自的授权和归属应查阅 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)、[`LICENSES/`](LICENSES/) 与 [`third-party/components.json`](third-party/components.json)。它们不因出现在本仓库或程序包内而自动改用 AGPL。

2026-10-20 的许可修复从新版移除全部随附 BASS/AAC 原生二进制，并替换解码实现。历史 `v4.0.0-typing.1`、`v4.0.0-typing.2`、`v4.0.0-typing.3` 仍包含原 BASS 组件，相关发行已暂时撤回为草稿并保留 Git 标签及原资产。其版权材料缺失及授权、兼容性问题没有因本次源码修改而被追溯解决；保留历史真实溯源，不把新版修复套用于旧包。

第三方原始许可、版权与来源资料单独列出。当前正在准备 `v4.0.0-typing.5`（R5）本体及独立插件预发布；维护者已指示公开披露仍未解决的 Avalonia/MS-PL 组合授权路径，保留其原始许可、核查记录和实际未解决状态，不把发布决定视为额外授权。新版使用已核验的不含 DNG/PIEX 的 Skia 替代，固定二进制及匹配源码已独立发布；原 NuGet DLL 与历史含 DNG 组合的限制仍按各自材料保留。本文件不构成所有直接、间接依赖均已穷尽核验或可以无条件再分发的结论；具体证据、实际分发范围和仍待核对项目以组件清单及[版权与许可审计](docs/版权与许可审计.md)为准。

`docs/W02_听力词汇_英文示例.txt` 是仓库已有的 40 项普通英语词汇格式示例，版本历史未记录选词来源。本次保留内容且不虚构版权人或教材授权；不得将该示例解释为已取得任何第三方教材、题库或用户导入材料的通用使用许可。用户导入的词表与文档不因本程序的许可证而自动改用 AGPL。

## 对应源码与构建材料

原始第一方项目、源码改动、项目文件、依赖锁文件、构建与发布脚本属于版本溯源资料。再分发时，应提供与程序包相同版本和提交的源码，并按适用许可证提供必要的第三方对应源码；在相应许可证允许通过网络提供时，应附同一版本的有效固定来源入口。Skia/ANGLE 等子模块和 `DEPS` 链按实际构建记录核对，最终交付内容以匹配的 `THIRD_PARTY_SOURCES.json`、源码归档清单和包内实际文件为准；仅有应用源码 ZIP、项目主页或固定定位不自动满足完整源码交付义务。发行规则见 [docs/RELEASING.md](docs/RELEASING.md)；相关资料不能用与该二进制无关的最新分支或纯口头承诺替代。

本文件与其他说明冲突时，以相关原始许可证的有效条款为准；不得据此删除版权通知、缩减已授予的自由软件权利，或声称获得尚未取得的商业、专利、商标或第三方再分发许可。
