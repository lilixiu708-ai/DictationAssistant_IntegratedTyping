# 发布规范

发行仓库为 [`lilixiu708-ai/DictationAssistant_IntegratedTyping`](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping)。源码、应用版本、Git 标签、程序包和校验文件必须追溯到同一个提交。

**当前状态：`v4.0.0-typing.4` 为源码预览，公开发布暂停。** 本次可完成源码、构建和许可资料，并提交到 `main`；不推送该版本标签，不把本地审核产物公开为 Release。BASS/AAC 已从新版移除，但剩余原生组件的兼容性和对应源码事项尚未解除，不能将普通构建成功称为获得许可认证。历史 `typing.1`、`typing.2`、`typing.3` 已暂时撤回为草稿，保留原标签与资产，未被追溯修复。

## 公开发布的前置条件

发布前核对[版权与许可审计](版权与许可审计.md)、[第三方声明](../THIRD_PARTY_NOTICES.md)及 [`third-party/components.json`](../third-party/components.json)。缺少必要授权、原始版权/许可、固定版本源码、构建材料或可靠的交付依据时，停止相关组合的发行；不得删除检查、填入占位数据或追加免责声明后继续。

目前尚未解除：

- `libSkiaSharp.dll` 实际包含 Adobe DNG SDK：对应 SkiaSharp Windows 构建参数启用 `skia_use_dng_sdk=true`，二进制中亦有 DNG 实现证据。[`LICENSE.source_code` 第 5 条](../LICENSES/third-party/adobe-dng-sdk-1.4.0/LICENSE.source_code) 对随商业产品分发规定为 Adobe 辩护、赔偿的义务。尚未取得权利人书面例外或足以支持其与 AGPL 组合兼容的权威结论，不能按普通 BSD/MIT 处理，也不能仅因本程序免费就宣布没有问题。应在取得有效依据、替换组件或重建并验证不含相关代码之前暂停该组合公开发布。
- 固定源码归档与索引不等于已经独立验证全部原生库的完整对应源码。Skia/SkiaSharp、HarfBuzz 与 ANGLE 含子模块或 `DEPS` 下载项，上游源码 ZIP 通常不自动包含这些内容。实际原生构建源码和材料尚未全部随应用源码 ZIP 交付，不能声称单个 ZIP 已包含所有原生组件的完整 Corresponding Source。
- Avalonia 原通知含 MS-PL 等其他条款；顶层 MIT 标签不能替代内嵌组件的原许可或具体兼容性判断。收集文本、哈希校验和自动构建通过不是全面法律合规认证。

须为每个未解除项留下可核对的处理依据，并核验最终交付材料与实际包一致，才能重新考虑公开发布。`prepare_release.py` 的默认路径阻断当前公开发行；`--draft` 仅用于本地审核产物，不授予分发许可，不上传或发布。

## 版本、提交与构建

- 本次源码版本为 `4.0.0-typing.4`；未来许可与交付前置条件解除后，发行标签使用 `v4.0.0-typing.4`。标签去掉 `v` 后必须与主程序 `Version` 相同。
- 应用版本、插件包版本、插件 ID 与 `apiVersion` 分别管理；打包或声明修改不自动增加插件 API 版本。
- 已发行标签和资产不原地覆盖。修正交付时使用新编号、重新构建并保留历史真实溯源。没有 Windows 实机验证的版本不得写成已经通过实机验证。
- 先提交版本、`CHANGELOG.md`、使用说明、`NOTICE.md`、`MODIFICATIONS.md`、第三方材料及目录，再核对 `git status --short` 与完整 `git rev-parse HEAD`。未跟踪的许可文件不会进入 `git archive`，未提交源码的构建不能归属于较早标签。
- 云任务沿用现有检出；除非用户明确要求，不创建额外 Git worktree。输出放在忽略的 `artifacts/`，勿把个人文件放入重建目录。

使用 `global.json` 允许的 .NET 10 SDK，以及 Python 3.11 或更高版本。CI 固定使用 Python 3.12。Windows 根目录 CMD 构建入口：

```bat
call build-windows-typing.cmd
python scripts\verify_build.py
```

构建脚本锁定还原 Windows 依赖，发布 `win-x64` 自包含宿主及两个插件，运行既有检查，调用 `scripts/prepare_licenses.py` 为实际输出准备原始许可、组件和来源清单，再生成本地 Windows ZIP。运行时许可来自实际 .NET runtime pack，不用应用 AGPL 文本替代。

许可准备按实际 `.deps.json`、输出文件与组件目录核对。新增/变更二进制必须有固定版本来源、必要原文及实际哈希；缺失或不匹配即停止并修复目录，不复用旧输出绕过失败。仅允许的草稿路径可继续准备审核资料；审核文件不得误标为已公开且获许可认证的发行包。

## 固定源码与原生依赖索引

仓库组件目录记录固定提交、原始许可路径和证据哈希；其中 `native_source_provenance` 保存 Skia/SkiaSharp 与 ANGLE 的固定 `DEPS`/gitlink 来源链，`release_review` 保存当前发行阻断原因。构建输出的 `THIRD_PARTY_COMPONENTS.json` 记录实际组件与文件，`THIRD_PARTY_SOURCES.json` 记录其源码定位和交付方式。来源收集脚本按清单下载 `delivery=bundled` 的固定归档，在 `SOURCE_ARCHIVES.json` 中记录实际字节和 SHA-256；需要与上游预期哈希比较的条目还须匹配该值。下载、归档验证或哈希失败即停止，不关闭 TLS，也不改成 `main`、`HEAD` 等浮动入口。

当前原生来源链至少包括：

| 组件 | 固定来源与继续核对范围 |
| --- | --- |
| SkiaSharp 2.88.9 | `mono/SkiaSharp@9699cdf046a989423a5e5eca68e7fd813486d81c`，Skia 子模块为 `mono/skia@4bed689c9c9eb77a120c6a9d54af6a572c85d1c2`；继续保留该提交 `DEPS` 与实际构建依赖的固定版本。 |
| libgifcodec | Skia `DEPS` 指向 `d06d2a6d42baf6c0c91cacc28df2542a911d05fe`，原始九文件纳入 [`third-party/sources/`](../third-party/sources/)；这些文件不等于包含它的 Skia 库全部源码。原多重许可保留，实际选项见组件目录。 |
| Adobe DNG SDK | Skia `DEPS` 指向 `platform/external/dng_sdk@c8d0c9b1d16bfda56f15165d39e0ffa360a11123`；[原始材料](../LICENSES/third-party/adobe-dng-sdk-1.4.0/)保留软件/文档/技术条款，源码可下载不等于兼容性已解决。 |
| HarfBuzzSharp 8.3.1.1 | `mono/SkiaSharp@64f24b1cddb68d30ec0ac6b661964178fc21d5ec` 经 Skia `30abf4a0dcd3006f3a555e46906151e486f87b6e` 的 `DEPS` 定位 HarfBuzz `2b3631a866b3077d9d675caa4ec9010b342b5a7c`。 |
| Avalonia ANGLE | `AvaloniaUI/angle@cb8b4e1307a9d8f5ff56b8c5973bea4158ffead8`；继续核对该提交 `DEPS`、子模块和实际 Windows 原生构建依赖，不能只提供包装仓库主页。 |

以上是固定来源索引，不表示全部原生源码已经收集或可复现构建已经成功。核对最终目录中的新增索引、实际归档和交付方式，明确哪些内容随包、哪些仅提供固定定位。若适用许可要求更多对应源码或构建材料，须实际补齐；仅列 URL 不自动满足全部交付义务。

## 审核资产与未来发行资产

`prepare_release.py` 核对版本、提交与构建元数据，并执行当前 ZIP 校验。`--draft` 可从当前干净 HEAD 准备本地审核资料，无须为此创建版本标签；具体命令见脚本 `--help`。按当前阻断状态，只允许该审核路径。未来正式路径须等待前置条件解除，并要求精确匹配的标签与 HEAD，从该干净提交重新生成。

| 资料 | 内容与边界 |
| --- | --- |
| Windows ZIP | 程序、两个插件、运行时、项目声明、适用的第三方原始许可和实际组件/来源清单；本地审核产物不等于公开发行许可。 |
| source ZIP | 同提交应用源码、构建脚本、锁文件、文档、许可材料，以及实际收集的固定第三方归档和索引；不宣称单独包含所有原生库的完整构建源码。 |
| `SHA256SUMS` | 最终 ZIP 字节的 SHA-256。归档内容变化后重新生成，不能沿用先前校验值。 |

应用源码从对应干净提交导出，再按匹配清单追加已实际收集的第三方归档、内联源码校验及运行时通知。GitHub 自动生成的 Source code ZIP 不执行该追加流程，不能称它与审核脚本的源码包相同。审核资料应标明来源提交和未解除事项，不能用较新 `main` 冒充旧二进制对应源码。

程序包应保留 `LICENSE.md`、`NOTICE.md`、`MODIFICATIONS.md`、`THIRD_PARTY_NOTICES.md`、`LICENSES/`、`EULA.txt`、使用说明和匹配清单；源码交付还须保留组件目录、构建材料及实际必须交付的第三方源码。源码包中的第三方源码/二进制同样受各自再分发条件约束，不将它们统一重标为 AGPL。

## 公开发布恢复后的验证与步骤

以下步骤当前暂停，前置条件满足后才能执行：

1. 核对必要授权、兼容性依据及对应源码材料。结构/哈希校验通过不能替代此判断，不宣称“100% 合规”。
2. 从干净提交构建；现有检查、ZIP 校验通过，组件、原生依赖、插件与运行时均有匹配的清单和原始材料。确认未把旧 BASS/AAC 残留带回。
3. 核对两个 ZIP 的同一标签/提交、每项实际交付内容与 `DEPS`/子模块来源；缺失即停止。文件名存在不等于内容齐全。
4. 最终资产定稿后生成校验文件；记录完整提交 SHA、工具版本、大小、告警及实机验证范围，不承诺未经测量的 CPU/内存降幅。
5. 确认所有资料已提交后才创建和推送 `v*` 发行标签。现有工作流会自动构建、上传和发布，因此当前仅推 `main`，不要推 `v4.0.0-typing.4`。核对远端标签与任务实际 SHA，不移动标签重试，不误公开历史问题草稿。
6. 公开发布后，重新读取 Release 状态并下载 ZIP/`SHA256SUMS` 校验，打开包内声明和来源索引复核。届时才更新首页的暂停状态和最终链接；私有仓库链接仍受仓库权限控制。

新的授权或源码问题出现时，保留事实记录并停止涉及的发行，不以继续保留下载链接或文字声明代替处理。
