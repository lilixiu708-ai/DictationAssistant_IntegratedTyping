# 发布规范

发行仓库为 [`lilixiu708-ai/DictationAssistant_IntegratedTyping`](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping)。源码、程序版本、标签、二进制和校验文件须指向同一提交。

**[Word / Excel 导入插件 1.1.0 已独立公开](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/plugin-docx-vocabulary-v1.1.0)。** 标签对应源码提交 `93631f4e7330a0ee8a89e903c127bfbd929ddf3f`，[Windows CI 39392552810](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39392552810) 构建与发布成功：固定 R6 加载导入 1.1.0 与原同步打字 1.2.0，双工作表 / 四列 Unicode 合成样本 CLI、15 项既有检查及窗口正常开闭通过。本体、同步打字、原生依赖与历史 Word 导入 1.0.1 的资产保持不变。

公开后五份附件的服务器摘要、四项 `SHA256SUMS`、插件版本、源码提交、DLL 哈希及 R6 兼容记录均核对通过。独立源码闭包复核覆盖 151 个 Git 文件、125 份许可证、4 份项目声明、ExcelDataReader 原始许可及一份 20,105,239 字节的私有依赖源码归档；下载源码包的独立还原与 Release 发布构建通过。源码包不含用户工作簿；10 个旧 Release、43 份旧附件和 15 个旧标签引用保持不变。

**`v4.0.0-typing.6`（R6）本体与同步打字插件 1.2.0 预发布已公开。** Avalonia 保持 **11.3.12**，仍按既有发布授权披露未解决的 MS-PL/AGPL 组合事项，不将其表述为已获额外授权。原始许可、对应源码及完整性检查继续保留；R5 与同步打字 1.1.0 保留为历史版本。

**[同步打字插件 1.3.1 已独立公开发布](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/plugin-synchronous-typing-v1.3.1)。** 本次只修改和发布插件，复用已公开的 R6 本体，Word 导入仍为 1.0.1；不增加本体版本，不覆盖 1.2.0 或其他历史 Release。新增官方翻译 API 适配没有引入新的 NuGet 依赖；本机凭据保护及宿主既有 Avalonia 披露保持不变，真实服务账户翻译仍待验证。

插件标签对应提交 `418501c1a7be64bd1e81dd4dd9816e4ce822c4e2`，[独立 Windows 工作流 39381852561](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39381852561) 的构建与发布均成功：暂存的公开 R6 宿主加载新插件、15 项既有检查和主窗口开闭通过。五份发行附件已重新下载，哈希、插件版本、DLL 提交及 R6 兼容记录核对通过；9 个既有 Release、38 份附件及旧标签保持不变。Windows DPAPI、旧配置迁移和真实账户翻译仍未验证。

首次候选 1.3.0 在准备兼容宿主时，因既有 R6 ZIP 中一个说明文件名含 Windows 非法字符 `?` 而失败，未公开任何附件。保留该候选标签，不移动标签重试，改用 1.3.1；修复仅在暂存副本中更名该说明文件并保留原字节，不修改已公开的 R6 ZIP。

下载的插件源码包另经独立复核：166 个选定文件与标签导出逐字节一致，124 份原始许可证及一份私有依赖源码归档保持完整；从下载源码包独立还原、Release 发布构建成功。

R6 的发行标签为 [`v4.0.0-typing.6`](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/v4.0.0-typing.6) 与 [`plugin-synchronous-typing-v1.2.0`](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/plugin-synchronous-typing-v1.2.0)。插件要求宿主功能级别 4，须与 R6 配套；Word 导入功能未变，继续复用已公开的 `plugin-docx-vocabulary-v1.0.1`，不移动该标签、不覆盖既有资产。

R6 标签指向提交 `0f008c72983b9d567612855498800886201572e6`，[发行构建记录 39377359747](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39377359747)成功完成：程序与插件构建、包及 DLL 校验、15 项既有检查、生产主窗口正常开闭和 SDK 示例构建通过，两个新 Release 已公开。后续文档更新不移动发行标签，也不替换已公开资产。

公开后已重新下载两组各 5 份附件，`SHA256SUMS` 全部通过。本体包的 390 个文件、68 个组件及 40 个依赖源码归档通过复核；插件的 1 个私有依赖源码归档、源码独立构建及内置/独立 DLL 一致性均通过。源码逐项对应上述标签导出，原始声明保留原字节；7 个既有 Release 的页面、附件及原标签未被改动。

此前代码提交 `0e7ecae` 的 [Windows CI 39375979975](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39375979975) 保留为主分支验证记录。Windows DPAPI、旧配置迁移、跨机器/账户拒绝读取和真实在线翻译仍待验证，不能由构建及主窗口开闭结果推定通过。

R5 本体与同步打字 1.1.0、Word 导入 1.0.1 已公开，来自提交 `ae1a23210df3f40c2527832bc1a0c55990f08b3f`；其 [Windows 构建 39363409555](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39363409555) 记录仅作为该版本证据。后续文档和 R6 发布不改变历史标签、资产或 `typing.1`–`typing.3` 草稿状态。

## 当前核对状态

- **no-DNG 重建与实物检查已通过：** [Windows CI 39355910024](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39355910024) 成功编译同版本替代，1,124 个源码输入及命令/实现检查排除 DNG/PIEX，941 个导出槽与官方逐字段相同、原生版本为 88.1，上游 PNG 工具及 production App 窗口开闭通过。实物哈希与原记录见 [NATIVE_BUILD.md](NATIVE_BUILD.md)。原 NuGet 含 DNG DLL 与历史归属仍单独保留。
- **Avalonia 组合授权路径尚未解决：** 固定源码中 19 个 C# 文件有 Microsoft/MS-PL 头，实际 `Avalonia.Controls.dll` 含 22 个相关 TypeDef 及真实 IL，App 直接引用该程序集。[MS-PL §3(D)](../LICENSES/third-party/avalonia-11.3.12/NOTICE.md#silverlight-toolkit) 的二进制许可条件不等于这些源文件已允许 AGPL 再许可；未找到特定部分的双授权或链接例外，见[固定核查记录](../third-party/evidence/avalonia-mspl-11.3.12/REVIEW.md)。MIT 顶层标签或 DNG 替换成功不能解除这一独立事项；这不是宣称全部 Avalonia 违法。
- **HarfBuzz/ANGLE 源码覆盖已补齐：** Windows 编译与修改所需的固定来源、配置/生成输入和交付清单见 [HarfBuzz 输入说明](../third-party/sources/harfbuzzsharp-8.3.1.1-windows-inputs/README.md)、[ANGLE 交付说明](../third-party/sources/angle-cb8b4e1-source-directions/README.md)及[组件目录](../third-party/components.json)。该结论不声称复现历史 DLL 的全部编译器版本或逐字节输出；最终源码包仍须实际包含匹配材料。

独立 Skia 原生包不含 Avalonia，已完成固定资产与源码闭包核对并公开为 [`native-skia-2.88.9-no-dng.1`](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/native-skia-2.88.9-no-dng.1)。这不解决 App 的 Avalonia/MS-PL 组合项；本体发行资产和说明继续保留该未解决事项。

## 构建与审核

使用 `global.json` 允许的 .NET 10 SDK、Python 3.11+（CI 为 3.12）。Windows 根目录 CMD 入口：

```bat
call build-windows-typing.cmd
python scripts\verify_build.py
```

App 构建只接受目录中已固定地址、来源和哈希的审核原生资产；尚无有效 no-DNG 资产时停止正式打包，不静默退回旧 DLL。同一 CI 的本地注入仅供审核，不能被描述为正式发行配置。

`prepare_release.py --draft` 可从干净 HEAD 准备本地审核资料，无需创建版本标签；它不上传或发布。普通正式路径仍检查 `release_review`。R6 沿用明确的 `--acknowledge-license-review` 参数，仅承认并披露已记录的 `avalonia-mspl-agpl-compatibility` 项；原清单的未解决状态保留，不改为已获授权。

该参数不能放行新的未知事项、缺少原始通知或实际源码、未知/变化二进制、无有效原生替代或校验不符。此类失败仍须修复；不得用占位哈希、浮动来源或关闭 TLS 使构建通过。

## 同版本源码与许可交付

- 提交版本号、`CHANGELOG.md`、`NOTICE.md`、`MODIFICATIONS.md`、第三方材料与固定目录；`git status --short` 应干净。未跟踪文件不会进入 `git archive`，未提交修改不能归到较早标签。
- 程序包保留项目声明、`LICENSES/`、实际 .NET runtime pack 通知，以及 `THIRD_PARTY_COMPONENTS.json` 和 `THIRD_PARTY_SOURCES.json`。第一方 AGPL 不替代第三方原始许可。
- 应用源码从对应提交导出，再追加实际下载且验证的第三方归档、内联源码、生成/构建材料和匹配的运行时通知。独立 no-DNG 原生包应附其**实际修改后源码树**、原始归档、补丁、配置、构建脚本、必要依赖与工具来源；只给包装仓库 ZIP 或持续变化的 `main` 不够。
- `third-party/components.json` 的 `native_source_provenance`、`source_coverage` 和 `native_overrides` 记录来源及范围；包内 `third-party-sources/SOURCE_ARCHIVES.json` 记录实际归档及哈希。目录中的 `delivery=bundled` 条目须真的入包，引用索引不等于已经交付。
- 下载字节按固定 SHA-256 验证；Gitiles 有动态归档元数据的条目按目录规定核验固定 canonical `tar-members-v1` 内容树哈希（路径、类型、权限、链接和文件字节），并记录该次归档的实际 SHA-256。不能因压缩字节不同跳过内容验证。
- GitHub 自动源码 ZIP 不执行上述追加流程，不能称它与准备脚本产出的对应源码包相同。核对每个实际组件要求的交付内容，勿将所有原生源码覆盖一概标成未补齐，也勿把部分通过扩大为整个 App 已合规。

## 仅发布 Word / Excel 导入插件 1.1.0

使用 `.github/workflows/release-importer-plugin.yml`，由标签 `plugin-docx-vocabulary-v1.1.0` 触发：

1. 将插件 1.1.0、固定依赖、原始许可、对应源码材料与验证记录提交到同一版本，保持插件 ID `docx-vocabulary`、入口及 API 1；标签指向该实际提交。
2. 下载并核验固定的公开 R6，暂存后只替换导入插件及其私有依赖；在 Windows 核验 R6 插件装载、合成 Excel CLI、15 项既有检查和程序窗口，不重建或重新发布 App、同步打字及原生资产。
3. 通过后准备插件 ZIP、对应源码、`SHA256SUMS` 与披露文件，仅创建新的插件 Release；核对草稿资产再公开，不覆盖旧标签或附件。
4. 公开后重新下载并核对哈希、源码提交、DLL 与 R6 兼容记录，再切换首页的当前 Word / Excel 下载入口。

本轮私人 `.xlsx` 的 19 页、2,683 个非空文本、56 列预览、112 处标题 / 表头及默认 95 条已在 Linux 核对；Windows 仅使用合成样本验证 CLI 读取。私有文件不上传 CI；其他格式、公式缓存与 Windows 真实 Excel GUI 导入仍未验证，详见 [Excel 导入验证](Excel导入验证.md)。

## 仅发布同步打字插件 1.3.1

使用 `.github/workflows/release-typing-plugin.yml` 的独立路径，触发标签为 `plugin-synchronous-typing-v1.3.1`。此流程不推送新的 App 标签，也不重发 R6、Word 或原生依赖资产。

1. 在干净提交中仅更新插件及其构建、文档材料，保持插件 API 1、最低宿主功能级别 4。标签须与插件版本和实际提交一致，宿主继续使用 R6 原标签。
2. 工作流从固定的公开 R6 下载地址取得宿主，校验固定哈希、`SHA256SUMS` 与披露材料后解压暂存；不从当前源码重建宿主。下载准备入口为：

   ```text
   python scripts/prepare_plugin_release.py --prepare-host --host-release-tag v4.0.0-typing.6
   ```

3. 在暂存 R6 中只替换同步打字插件，执行 Windows 兼容检查：既有 15 项逻辑检查、API 1 / 功能级别 4 下的插件加载、Word 1.0.1 保留及生产主窗口正常开闭。除上述说明文件的暂存更名外，宿主和其他目录的内容哈希须保持不变；失败不进入发布。
4. 兼容结果与当前插件 DLL 逐项匹配后，准备插件二进制 ZIP、对应源码、校验和及披露文件，共五份附件：

   ```text
   python scripts/prepare_plugin_release.py --standalone --host-release-tag v4.0.0-typing.6 --plugin-release-tag plugin-synchronous-typing-v1.3.1
   ```

   此命令依赖前述暂存宿主及 Windows 兼容结果，不是跳过检查的快捷方式。插件源码及实际私有依赖材料仍须完整交付；当前真实账户鉴权、翻译与未执行的 Windows 交互场景如实记录。
5. 仅创建插件的新草稿，五份附件完整核对后公开；遇到已存在的同名 Release 或草稿时拒绝覆盖。公开后重新下载所有附件、验证 `SHA256SUMS`、源码与 DLL，再更新首页的当前插件下载。本体和 Word 的标签、版本及附件不变。

## 本体版本更新时的发布步骤

以下是本体版本更新流程，保留 R6 的实际命令作为记录；本次插件 1.3.1 使用上节独立路径。命令成功不等于 Release 已公开。

1. 从干净提交重新构建、校验；核对新原生 DLL 的实际 DNG 排除、ABI/版本、上游 PNG 工具及 Windows 窗口检查结果，连同 App 其他许可问题分别记录。既有构建成功不替代该轮实物验证。
2. 令 `v<Version>` 标签与项目版本和 HEAD 精确匹配；应用版本与插件版本/API 分别管理。不得移动或覆盖既有标签、资产，不误公开历史草稿。修正已发行内容使用新版本。
3. 交付版本化 Windows ZIP、匹配 source ZIP 与 `SHA256SUMS`，最终字节定稿后生成校验；记录完整提交 SHA、工具版本、未解决告警与实机验证范围。没有实测数据不承诺 CPU/内存降幅。
4. 用 `python scripts/prepare_release.py v4.0.0-typing.6 --acknowledge-license-review` 准备本体资产，再用 `python scripts/prepare_plugin_release.py --app-release-tag v4.0.0-typing.6` 准备独立插件的 ZIP、对应源码、校验和及许可披露文件。本次只发布新的同步打字 1.2.0；Word 导入 1.0.1 复用现有 Release，即使本体重新构建了该插件，也不覆盖其独立发行资产。
5. 推送 App 新标签后核对该次工作流的提交、构建与 Release 状态。R6 的披露路径应绑定精确的新标签，其他引用保留审核条件；发行说明列出未解决项和实际验证范围。不得覆盖已公开标签或资产，也不得误公开历史草稿。
6. 重新下载本体和插件的最终附件，核对 `SHA256SUMS`、源码及声明清单，再将首页更新为已发布。私有仓库链接仍受仓库权限控制。

`RELEASE_REVIEW.json` 和 `RELEASE_NOTES.md` 记录本次披露决定及核对边界；不将“已声明”写成“已解决”。普通分支构建继续遵守审核条件。构建输出、独立原生审核或许可证收集成功均不构成全面法律合规认证。
