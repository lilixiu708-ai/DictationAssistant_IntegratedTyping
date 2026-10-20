# 发布规范

发行仓库为 [`lilixiu708-ai/DictationAssistant_IntegratedTyping`](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping)。源码、程序版本、标签、二进制和校验文件须指向同一提交。

**当前 `v4.0.0-typing.4` 为源码预览，App 公开发布暂停。** Avalonia 保持 **11.3.12**，不升级。可提交修复及审核资料到 `main`，不推送 App 发布标签。历史 `typing.1`–`typing.3` Release 已暂撤为草稿，保留原标签与资产，未追溯修复。

## 当前核对状态

- **no-DNG 重建与实物检查已通过：** [Windows CI 39355910024](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39355910024) 成功编译同版本替代，1,124 个源码输入及命令/实现检查排除 DNG/PIEX，941 个导出槽与官方逐字段相同、原生版本为 88.1，上游 PNG 工具及 production App 窗口开闭通过。实物哈希与原记录见 [NATIVE_BUILD.md](NATIVE_BUILD.md)。原 NuGet 含 DNG DLL 与历史归属仍单独保留。
- **Avalonia 组合授权路径尚未解决：** 固定源码中 19 个 C# 文件有 Microsoft/MS-PL 头，实际 `Avalonia.Controls.dll` 含 22 个相关 TypeDef 及真实 IL，App 直接引用该程序集。[MS-PL §3(D)](../LICENSES/third-party/avalonia-11.3.12/NOTICE.md#silverlight-toolkit) 的二进制许可条件不等于这些源文件已允许 AGPL 再许可；未找到特定部分的双授权或链接例外，见[固定核查记录](../third-party/evidence/avalonia-mspl-11.3.12/REVIEW.md)。MIT 顶层标签或 DNG 替换成功不能解除这一独立事项；这不是宣称全部 Avalonia 违法。
- **HarfBuzz/ANGLE 源码覆盖已补齐：** Windows 编译与修改所需的固定来源、配置/生成输入和交付清单见 [HarfBuzz 输入说明](../third-party/sources/harfbuzzsharp-8.3.1.1-windows-inputs/README.md)、[ANGLE 交付说明](../third-party/sources/angle-cb8b4e1-source-directions/README.md)及[组件目录](../third-party/components.json)。该结论不声称复现历史 DLL 的全部编译器版本或逐字节输出；最终源码包仍须实际包含匹配材料。

独立 Skia 原生包不含 Avalonia，技术 CI 已成功，源码闭包仍在独立复核；计划发布 `native-skia-2.88.9-no-dng.1`，当前未公开该 Release。App 仍仅因上述 Avalonia/MS-PL 组合授权路径未解决而保持发布阻断，原生资产准备不替代该处理。

## 构建与审核

使用 `global.json` 允许的 .NET 10 SDK、Python 3.11+（CI 为 3.12）。Windows 根目录 CMD 入口：

```bat
call build-windows-typing.cmd
python scripts\verify_build.py
```

App 构建只接受目录中已固定地址、来源和哈希的审核原生资产；尚无有效 no-DNG 资产时停止正式打包，不静默退回旧 DLL。同一 CI 的本地注入仅供审核，不能被描述为正式发行配置。

`prepare_release.py --draft` 可从干净 HEAD 准备本地审核资料，无需创建版本标签；它不上传或发布。正式路径仍由 `release_review` 阻断。缺原始通知、授权依据、实际源码交付、未知/变化二进制或校验不符时须停止，不用占位哈希、浮动分支、关闭 TLS 或免责声明绕过。

## 同版本源码与许可交付

- 提交版本号、`CHANGELOG.md`、`NOTICE.md`、`MODIFICATIONS.md`、第三方材料与固定目录；`git status --short` 应干净。未跟踪文件不会进入 `git archive`，未提交修改不能归到较早标签。
- 程序包保留项目声明、`LICENSES/`、实际 .NET runtime pack 通知，以及 `THIRD_PARTY_COMPONENTS.json` 和 `THIRD_PARTY_SOURCES.json`。第一方 AGPL 不替代第三方原始许可。
- 应用源码从对应提交导出，再追加实际下载且验证的第三方归档、内联源码、生成/构建材料和匹配的运行时通知。独立 no-DNG 原生包应附其**实际修改后源码树**、原始归档、补丁、配置、构建脚本、必要依赖与工具来源；只给包装仓库 ZIP 或持续变化的 `main` 不够。
- `third-party/components.json` 的 `native_source_provenance`、`source_coverage` 和 `native_overrides` 记录来源及范围；包内 `third-party-sources/SOURCE_ARCHIVES.json` 记录实际归档及哈希。目录中的 `delivery=bundled` 条目须真的入包，引用索引不等于已经交付。
- 下载字节按固定 SHA-256 验证；Gitiles 有动态归档元数据的条目按目录规定核验固定 canonical `tar-members-v1` 内容树哈希（路径、类型、权限、链接和文件字节），并记录该次归档的实际 SHA-256。不能因压缩字节不同跳过内容验证。
- GitHub 自动源码 ZIP 不执行上述追加流程，不能称它与准备脚本产出的对应源码包相同。核对每个实际组件要求的交付内容，勿将所有原生源码覆盖一概标成未补齐，也勿把部分通过扩大为整个 App 已合规。

## App 恢复公开发布时

以下步骤当前暂停，只有未解决项已有可复核依据且实际产物齐全时才执行：

1. 从干净提交重新构建、校验；核对新原生 DLL 的实际 DNG 排除、ABI/版本、上游 PNG 工具及 Windows 窗口检查结果，连同 App 其他许可问题分别记录。既有构建成功不替代该轮实物验证。
2. 令 `v<Version>` 标签与项目版本和 HEAD 精确匹配；应用版本与插件版本/API 分别管理。不得移动或覆盖既有标签、资产，不误公开历史草稿。修正已发行内容使用新版本。
3. 交付版本化 Windows ZIP、匹配 source ZIP 与 `SHA256SUMS`，最终字节定稿后生成校验；记录完整提交 SHA、工具版本、未解决告警与实机验证范围。没有实测数据不承诺 CPU/内存降幅。
4. 最后才推 App 发布标签，并核对工作流实际提交与 Release 状态；下载最终 ZIP 校验并打开声明/来源清单复核。验证完成前不将首页改为已发布，私有仓库链接仍受仓库权限控制。

普通分支构建不应在阻断期间上传 App 二进制。构建输出、独立原生审核或许可证收集成功均不构成全面法律合规认证。
