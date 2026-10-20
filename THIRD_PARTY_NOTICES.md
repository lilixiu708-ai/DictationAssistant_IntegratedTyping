# 第三方组件与许可说明 / Third-party notices

本文件是许可索引。第三方组件保留各自的版权和许可；本项目的 AGPL-3.0-or-later 不会把它们重新许可。原始 LICENSE、NOTICE、附加条款及免责声明保存在 `LICENSES/third-party/`，原文件字节与 SHA-256 记录在 [组件目录](third-party/components.json)。摘要不能代替原文，也不表示自动完成了法律兼容性认证。

当前 App 为源码预览，尚未公开发布。下表索引拟用的 Windows 依赖、原 NuGet 包及历史审计材料；原 NuGet DLL 与已构建验证通过的无 DNG 替代按实际哈希区分。正式包的实际依赖与所带原文以 `THIRD_PARTY_COMPONENTS.json` 为准，固定源码和内联源码以匹配的 `THIRD_PARTY_SOURCES.json` 与源码交付内容为准；目录列出不等于已经发行。

## 应用依赖

| 组件 / 版本 | 原始版权归属摘要 | 主许可及完整原文 | 固定源码 |
| --- | --- | --- | --- |
| Avalonia 系列 11.3.12 | AvaloniaUI OÜ；NOTICE 中的其他贡献者 | MIT；附加 MS-PL、Apache-2.0 等见 NOTICE；[LICENSE.md](LICENSES/third-party/avalonia-11.3.12/LICENSE.md)、[NOTICE.md](LICENSES/third-party/avalonia-11.3.12/NOTICE.md)、[LICENSE.txt](LICENSES/third-party/apache-2.0/LICENSE.txt) | [37fbd9655cc5](https://github.com/AvaloniaUI/Avalonia/tree/37fbd9655cc581ff5b1c6b1fb1be4e3118c889d0) |
| Avalonia.AvaloniaEdit 11.3.0 | 2017 Eli Arbel | MIT；[LICENSE.txt](LICENSES/third-party/avaloniaedit-11.3.0/LICENSE.txt) | [583f2f8279cf](https://github.com/AvaloniaUI/AvaloniaEdit/tree/583f2f8279cf50830eb7fd79c73dec554a232abf) |
| ALampy.XamlFontPicker.Avalonia 2.0.1 | 2019 QIQI | MIT；[LICENSE.txt](LICENSES/third-party/xamlfontpicker-2.0.1/LICENSE.txt) | [6925f9aa7ce6](https://github.com/ArcticLampyrid/XamlFontPicker/tree/6925f9aa7ce689b8c70bfacb288de0b362b1494c) |
| CommunityToolkit.Mvvm 8.4.0 | .NET Foundation and Contributors | MIT；另保留 ThirdPartyNotices；[License.md](LICENSES/third-party/communitytoolkit-mvvm-8.4.0/License.md)、[ThirdPartyNotices.txt](LICENSES/third-party/communitytoolkit-mvvm-8.4.0/ThirdPartyNotices.txt) | [638b41dad30d](https://github.com/CommunityToolkit/dotnet/tree/638b41dad30dffabb123a39aa38eabc7e3721371) |
| DialogHost.Avalonia 0.8.1 | 2021 SKProCH | MIT；[LICENSE.txt](LICENSES/third-party/dialoghost-0.8.1/LICENSE.txt) | [61f074f96675](https://github.com/AvaloniaUtils/DialogHost.Avalonia/tree/61f074f96675240a3cbc99005f7c67c4dc4e48ec) |
| EdgeTTS.DotNet 0.4.0 | 2025 Curry | MIT；[LICENSE.txt](LICENSES/third-party/edgetts-dotnet-0.4.0/LICENSE.txt) | [bf43ca26d5db](https://github.com/twn39/EdgeTTS.DotNet/tree/bf43ca26d5db85e856fb16f4fc0e1b83dab424e3) |
| ExtendedNumerics.BigDecimal 3003.0.0.346 | 2024 Adam White | MIT；[LICENSE.txt](LICENSES/third-party/bigdecimal-3003.0.0.346/LICENSE.txt) | [6a2759de671b](https://github.com/AdamWhiteHat/BigDecimal/tree/6a2759de671b8a909ae6706f49fc869ae856b249) |
| MessageBox.Avalonia 3.3.1.1 | 2019 CreateLab | MIT；[LICENSE.txt](LICENSES/third-party/messagebox-3.3.1.1/LICENSE.txt) | [c0cb96704e2b](https://github.com/CreateLab/MessageBox.Avalonia/tree/c0cb96704e2b5db62bfdfa043dcaf3e177475716) |
| MicroCom.Runtime 0.11.0 | 2021 Nikita Tsukanov | MIT；[LICENSE.txt](LICENSES/third-party/microcom-runtime-0.11.0/LICENSE.txt) | [7e773759cfc1](https://github.com/kekekeks/MicroCom/tree/7e773759cfc1012a5d080d4129c4718a662884b0) |
| NCalc.Core / NCalcSync 5.12.0 | 2011 Sebastien Ros | MIT；[LICENSE.txt](LICENSES/third-party/ncalc-5.12.0/LICENSE.txt) | [bd0afe72c1b5](https://github.com/ncalc/ncalc/tree/bd0afe72c1b5a5679255e91f902f35021acbb48a) |
| Parlot 1.5.7 | 2020 Sébastien Ros | BSD-3-Clause；[LICENSE.txt](LICENSES/third-party/parlot-1.5.7/LICENSE.txt) | [c268ebf6b794](https://github.com/sebastienros/parlot/tree/c268ebf6b794de6bb11d64661fd080e8fc379b9b) |
| Tmds.DBus.Protocol 0.21.2 | 2006 Alp Toker；2010 Other Contributors；2016 Tom Deseyn | MIT；[LICENSE.txt](LICENSES/third-party/tmds-dbus-0.21.2/LICENSE.txt) | [f0b2c29f57bd](https://github.com/tmds/Tmds.DBus/tree/f0b2c29f57bdc7efaeccaa61ca9f13e9a0eebff7) |
| HexaGen.Runtime 1.1.24 | 2023 Juna Meinhold | MIT；[LICENSE.txt](LICENSES/third-party/hexagen-runtime-1.1.24/LICENSE.txt) | [e8cde045ae0b](https://github.com/JunaMeinhold/HexaGen/tree/e8cde045ae0b15284cddd8f79b491af4a8afc420) |
| Hexa.NET.SDL2 1.2.17（托管绑定） | 2024 Juna Meinhold | MIT；SDL2 本体另列；[LICENSE.txt](LICENSES/third-party/hexa-net-sdl2-1.2.17/LICENSE.txt) | [15fc3406e473](https://github.com/HexaEngine/Hexa.NET.SDL/tree/15fc3406e473759a2bcddd9c3ea24b8006916fbd) |
| Avalonia.Angle.Windows.Natives 2.1.25547.20250602 | 2018 The ANGLE Project Authors | BSD-3-Clause；子组件保留各自原文；[LICENSE](LICENSES/third-party/avalonia-angle-2.1.25547.20250602/LICENSE) | [cb8b4e1307a9](https://github.com/AvaloniaUI/angle/tree/cb8b4e1307a9d8f5ff56b8c5973bea4158ffead8) |
| SkiaSharp / NativeAssets.Win32 2.88.9（原包与同版本 no-DNG 替代分别记录） | 2015–2016 Xamarin, Inc.；2017–2018 Microsoft Corporation；原文中的其他贡献者 | MIT（绑定）；原包原生组件另列，替代实物与适用通知见 [NATIVE_BUILD.md](docs/NATIVE_BUILD.md)；[LICENSE.txt](LICENSES/third-party/skiasharp-2.88.9/LICENSE.txt)、[THIRD-PARTY-NOTICES.txt](LICENSES/third-party/skiasharp-2.88.9/THIRD-PARTY-NOTICES.txt) | [9699cdf046a9](https://github.com/mono/SkiaSharp/tree/9699cdf046a989423a5e5eca68e7fd813486d81c) |
| HarfBuzzSharp / NativeAssets.Win32 8.3.1.1 | 2015–2016 Xamarin, Inc.；2017–2018 Microsoft Corporation；HarfBuzz 贡献者 | MIT（绑定）；HarfBuzz 本体另列；[LICENSE.txt](LICENSES/third-party/harfbuzzsharp-8.3.1.1/LICENSE.txt) | [64f24b1cddb6](https://github.com/mono/SkiaSharp/tree/64f24b1cddb68d30ec0ac6b661964178fc21d5ec) |
| Microsoft.Extensions.* 10.0.2 | .NET Foundation and Contributors；原文中的其他贡献者 | MIT；保留原包 THIRD-PARTY-NOTICES；[LICENSE.txt](LICENSES/third-party/dotnet-extensions-10.0.2/LICENSE.txt)、[THIRD-PARTY-NOTICES.TXT](LICENSES/third-party/dotnet-extensions-10.0.2/THIRD-PARTY-NOTICES.TXT) | [9ffface2f3fa](https://github.com/dotnet/runtime/tree/9ffface2f3fa6fbbb427793c3230b1626a1fdd84) |
| NAudio.Core / NAudio.Wasapi 2.2.1 | 2020 Mark Heath | MIT；[LICENSE.txt](LICENSES/third-party/naudio-2.2.1/LICENSE.txt) | [b5d5ff83fd37](https://github.com/naudio/NAudio/tree/b5d5ff83fd378f046398891fe5cd99426ce44732) |
| NLayer / NLayer.NAudioSupport 2.0.1 | 2018 Mark Heath, Andrew Ward & Contributors | MIT；[LICENSE.txt](LICENSES/third-party/nlayer-2.0.1/LICENSE.txt) | [191c104a9fef](https://github.com/naudio/NLayer/tree/191c104a9fef8be13521d3956e537599b493a573) |

Microsoft.Extensions 的 NuGet 包声明来自 `dotnet/dotnet@44525024595742ebe09023abe709df51de65009b`；其固定 `src/source-manifest.json` 将 `runtime` 映射到 `dotnet/runtime@9ffface2f3fa6fbbb427793c3230b1626a1fdd84`。组件目录保留这条映射及对应证据，所供源归档使用该 runtime 修订。MicroCom 的固定来源依据官方 0.11.0 版本提交与紧随其后的 NuGet 发布记录；NAudio 使用官方 `v2.2.1` 标签。

## 原生组件及附加条件

托管绑定的 MIT 许可不能代替其原生二进制内组件的许可。同一个原生 DLL 可以包含多个分别授权的组件。

| 组件 | 版权与许可 | 完整原文 / 固定源码 |
| --- | --- | --- |
| SDL2 2.32.10 | 1997–2025 Sam Lantinga；zlib 许可 | [LICENSE.txt](LICENSES/third-party/SDL2-2.32.10/LICENSE.txt)；[源码](https://github.com/libsdl-org/SDL/tree/5d249570393f7a37e037abf22cd6012a4cc56a71) |
| HarfBuzz 8.3.1 | Google, Inc.、Ebrahim Byagowi、Facebook, Inc.、Mozilla Foundation 等，完整名单见 COPYING；Old MIT 及各子目录原文 | [HarfBuzz-COPYING](LICENSES/third-party/harfbuzzsharp-8.3.1.1/HarfBuzz-COPYING)；[源码](https://github.com/harfbuzz/harfbuzz/tree/2b3631a866b3077d9d675caa4ec9010b342b5a7c) |
| libgifcodec d06d2a6d42ba | 原文所列 Netscape、Google、Apple 等版权持有人；部分文件三选一 MPL-1.1 / GPL-2.0-or-later / LGPL-2.1-or-later，其他部分 BSD | [LICENSE.md](LICENSES/third-party/libgifcodec-d06d2a6d42baf6c0c91cacc28df2542a911d05fe/LICENSE.md)、[COPYING.GPL-3.0.txt](LICENSES/third-party/libgifcodec-d06d2a6d42baf6c0c91cacc28df2542a911d05fe/COPYING.GPL-3.0.txt)；[源码](https://skia.googlesource.com/libgifcodec/+/d06d2a6d42baf6c0c91cacc28df2542a911d05fe/) |
| Skia 4bed689c9c9e | 2011 Google Inc.；BSD-3-Clause；另含原文 THIRD-PARTY-NOTICES 列出的组件 | [LICENSE](LICENSES/third-party/skia-4bed689c9c9eb77a120c6a9d54af6a572c85d1c2/LICENSE)、[THIRD-PARTY-NOTICES.txt](LICENSES/third-party/skiasharp-2.88.9/THIRD-PARTY-NOTICES.txt)；[源码](https://github.com/mono/skia/tree/4bed689c9c9eb77a120c6a9d54af6a572c85d1c2) |
| Adobe DNG SDK 1.4.0（原 NuGet DLL/历史组合） | Adobe Systems Incorporated；附加 DNG SDK 软件、文档、技术及专利许可，非 MIT；商用分发有 indemnification 条款 | [LICENSE](LICENSES/third-party/adobe-dng-sdk-1.4.0/LICENSE)、[LICENSE.source_code](LICENSES/third-party/adobe-dng-sdk-1.4.0/LICENSE.source_code)、[LICENSE.technology](LICENSES/third-party/adobe-dng-sdk-1.4.0/LICENSE.technology)、[README.version](LICENSES/third-party/adobe-dng-sdk-1.4.0/README.version)、[NOTICE](LICENSES/third-party/adobe-dng-sdk-1.4.0/NOTICE)、[PATENTS](LICENSES/third-party/adobe-dng-sdk-1.4.0/PATENTS)；[源码](https://android.googlesource.com/platform/external/dng_sdk/+/c8d0c9b1d16bfda56f15165d39e0ffa360a11123/) |

以下仅保留原 NuGet DLL/历史含 DNG 组合所适用的归属原文，新替代不含该组件：

> This product includes DNG technology under license by Adobe Systems Incorporated.

no-RAW/no-DNG 替代已通过 [Windows CI 39355910024](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39355910024) 的实际编译、1,124 输入与实现排除检查、941 导出/88.1 版本核对、上游 PNG 和 Windows App 窗口开闭。实际 DLL SHA-256 为 `e328c8b0a58cd66dace98c0597b404ceb0b585daadf9f8451089a8c09258c95e`，完整记录及两 ZIP 哈希见 [NATIVE_BUILD.md](docs/NATIVE_BUILD.md)。旧 DLL 归属和历史证据仍保留；独立 Skia 资产不含 Avalonia，源码闭包复核后拟单独发布，当前未公开。App 仍因 Avalonia/MS-PL 组合授权路径未解决而暂停发布。

libgifcodec 的原始多选许可和各版权声明完整保留。本次分发对其中可选的 `SkGifImageReader` 部分选择 GPL-2.0-or-later 的第 3 版，其他 BSD 部分继续适用其原许可；GPLv3 与 AGPLv3 组合的规则见 GPLv3 第 13 节。对应固定修订的 9 个上游文件以原字节保存在 [内联源码目录](third-party/sources/libgifcodec-d06d2a6d42baf6c0c91cacc28df2542a911d05fe/)，附有 Git blob / SHA-256 校验及单独的 downstream notice。

Avalonia 保持 11.3.12；原始 NOTICE 含 Silverlight Toolkit 的完整 MS-PL，19 个固定 C# 文件有 Microsoft/MS-PL 头，实际 DLL 含 22 个相关类型及真实 IL，App 直接引用该程序集。其[源文件](https://github.com/AvaloniaUI/Avalonia/blob/37fbd9655cc581ff5b1c6b1fb1be4e3118c889d0/src/Avalonia.Controls/AutoCompleteBox/AutoCompleteBox.cs#L1)继续保留 MS-PL；[§3(D)](LICENSES/third-party/avalonia-11.3.12/NOTICE.md#silverlight-toolkit) 的二进制条件不等于允许源文件 AGPL 再许可，未找到特定部分的双授权/链接例外。[App 的组合授权路径尚未解决](third-party/evidence/avalonia-mspl-11.3.12/REVIEW.md)，不能以顶层 MIT、未直接使用控件或 no-DNG 构建通过替代；这不是对整个 Avalonia 的违法结论。RichTextKit 的 Apache-2.0 全文另随 LICENSES 提供。

HarfBuzz 与 ANGLE 的 Windows 编译/修改源码覆盖已补齐，见 [HarfBuzz 输入说明](third-party/sources/harfbuzzsharp-8.3.1.1-windows-inputs/README.md)与 [ANGLE 交付说明](third-party/sources/angle-cb8b4e1-source-directions/README.md)。固定源码、生成输入及相应原始通知按实际包交付；不声称逐字节复现历史 DLL，也不以整个上游 `DEPS` 的所有条件分支冒充实际 Windows 依赖。无 DNG Skia 的修改源码、原始通知及实物检查范围见 [NATIVE_BUILD.md](docs/NATIVE_BUILD.md)；尚未单独执行 JPEG/WebP/GIF 解码检查。

## .NET 运行时与仅构建 / 历史依赖

自包含包使用的 .NET 运行时版本在构建时从实际 `.deps.json` 解析。该版本 NuGet runtimepack 的 `LICENSE.TXT` 与 `THIRD-PARTY-NOTICES.TXT` 随包复制到 LICENSES，实际版本、路径、版权通知和源码引用记录在 `THIRD_PARTY_COMPONENTS.json` 与 `THIRD_PARTY_SOURCES.json`。此文件不把某次本机恢复的版本当作所有发行版本。

Microsoft.SourceLink.Common / GitHub 和 Microsoft.Build.Tasks.Git 8.0.0、Avalonia.BuildServices 11.3.2 属于构建工具；它们的原文与固定来源记录在源码仓库的组件目录中，是否进入某个发行包由实际资产决定。Linux、macOS、WebAssembly 的 NativeAssets 恢复条目也不代表 Windows 包包含了那些二进制。

ManagedBass 3.0.0 的 MIT 通知仅为历史审计记录，保留在源码仓库。新的发行依赖不含 ManagedBass / BASS 原生库；ManagedBass 的 MIT 许可从来不能替代 BASS 原生库的独立授权。

完整、可校验的逐包目录见 [third-party/components.json](third-party/components.json)。摘要中的版权归属不替代上游原文的完整贡献者名单、许可条件与免责声明。
