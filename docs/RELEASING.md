# 发布规范

发行仓库为 [`lilixiu708-ai/DictationAssistant_IntegratedTyping_GitHubReady`](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping_GitHubReady)。发行内容应能追溯到同一个 Git 提交：源码、应用版本、标签、Windows 程序包和校验文件必须对应。

## 版本与标签

- 使用 SemVer。稳定版本示例为 `4.0.0`；同步打字定制版预发布示例为 `4.0.0-typing.1`，后续预发布依次增加末尾数字。
- Git 标签必须带 `v`，如 `v4.0.0-typing.1`。标签去掉 `v` 后，应与 `src/DictationAssistant.App/DictationAssistant.App.csproj` 的 `Version` 完全相同。
- 应用发行版本与插件版本分别管理。仅发布或调整打包方式，不要求改变插件清单的 `version`、`apiVersion` 或共享接口程序集版本。接口兼容性变更须单独评估。
- 本次发行使用 `v4.0.0-typing.1`，工作流根据版本中的预发布后缀自动设置 GitHub Release 为预发布。尚未完成 Windows 实机验证的版本不得描述为已经通过实机验证。
- 已发布标签和资产不原地覆盖。修正程序或交付内容时，使用新的预发布编号或补丁版本，并重新构建、校验。

## 提交与构建

1. 完成版本号、`CHANGELOG.md`、使用说明及必要的发布配置调整，并提交到仓库。
2. 使用 `git status --short` 确认工作区干净，记录 `git rev-parse HEAD` 的完整提交 SHA。不得把未提交源码构建的程序归属于一个较早标签。
3. 在该提交上创建标签，再从标签指向的提交构建。已有标签时先核对其目标，不移动标签以迁就旧包。
4. 云任务使用现有隔离环境和仓库检出；除非用户明确要求，不创建额外 Git worktree。构建输出保存在忽略的 `artifacts/` 目录。

首次准备本次标签的 Git 命令如下，应在版本和文档提交后执行：

```text
git status --short
git rev-parse HEAD
git tag -a v4.0.0-typing.1 -m "DictationAssistant 4.0.0-typing.1"
git rev-parse v4.0.0-typing.1^{commit}
```

使用 `global.json` 允许的 .NET 10 SDK。Windows 可在仓库根目录打开 CMD 执行，无需 PowerShell：

```bat
call build-windows-typing.cmd
python scripts/prepare_release.py v4.0.0-typing.1
```

构建脚本以锁定模式还原 Windows 宿主依赖，发布 `win-x64` 自包含宿主和两个插件，运行既有打字逻辑检查，复制许可证及说明，再生成 `artifacts/DictationAssistant_IntegratedTyping_Windows_x64.zip`。脚本会重建输出目录，勿将个人文件放入该目录。

`prepare_release.py` 检查标签与应用版本一致、HEAD 等于标签目标、已跟踪文件无修改，并运行 `scripts/verify_build.py` 校验 ZIP。它还核对包内应用版本和提交元数据，再生成版本化程序包、同标签源码包和校验文件。该脚本只准备本地资产，不会上传或发布。

云环境可执行等价的 `dotnet restore`、`dotnet publish`、既有检查及 ZIP 打包步骤；具体安装与启动操作以保存的环境说明为准。保持自包含发布、禁止裁剪和 AOT、保留插件目录布局，不以关闭 TLS、签名或校验来绕过还原失败。无论采用哪种构建方式，都从本次标签指向的干净提交构建，并检查应用的 `InformationalVersion` 所记录的版本和提交与之对应。

推送 `v*` 标签会触发 Windows Actions 工作流，自动执行构建、校验和资产准备；后续 `publish-release` 任务上传资产并发布 GitHub Release。普通分支构建及手动运行仍提供临时 Artifact。应核对工作流实际使用的提交 SHA；来自其他提交的绿色结果不能代表本次发行。

## 发行资产

运行准备脚本后，以下三个文件位于 `artifacts/release/v4.0.0-typing.1/`：

| 文件 | 内容 |
| --- | --- |
| `DictationAssistant-4.0.0-typing.1-win-x64-portable.zip` | 完整 Windows x64 便携程序及两个预装插件，自带 .NET 运行时 |
| `DictationAssistant-4.0.0-typing.1-source.zip` | 同标签完整源码、构建脚本、锁文件、文档和许可证 |
| `SHA256SUMS` | 上述两个 ZIP 最终上传字节的 SHA-256，使用资产文件名 |

准备脚本保留构建 ZIP，另复制出版本化资产；使用 `git archive` 从标签导出源码，源码包顶层目录为 `DictationAssistant-4.0.0-typing.1-source/`，不包含未跟踪文件、缓存和构建产物；随后对两个最终 ZIP 生成 `SHA256SUMS`。

程序包保留 `LICENSE.md`、`EULA.txt`、使用说明及第三方必要声明；不得把全部第三方依赖统一标记为 AGPL。保留上游来源 <https://github.com/ArcticLampyrid/DictationAssistant> 和修改说明，BASS 等组件遵循各自授权。

## 发布前验证

- 构建与既有检查全部成功，`scripts/verify_build.py` 对当前构建 ZIP 校验通过；核对两个插件清单、DLL、Windows 原生依赖和自带运行时。
- 版本化 Windows ZIP 与构建 ZIP 内容一致；源码 ZIP 来自相同标签，SHA-256 在所有文件最终定稿后生成。
- 构建后再次检查 Git 状态。只接受预期的忽略输出，不把锁文件意外改动混入发行。
- 发布说明分开列出实际完成的自动检查和待完成的 Windows 窗口、音频、输入法及资源占用检查。没有实测数据时，不承诺具体 CPU 或内存降幅。
- 记录资产大小、完整提交 SHA、构建工具版本及任何未解决的构建告警。更新包建议解压到新空文件夹，避免残留旧版依赖。

## GitHub Release 发布与复核

1. 确认版本、文档和发布工作流已提交并推送。发布说明应清楚交代对应源码、AGPL-3.0-or-later、上游来源和验证限制。
2. 推送本次标签，例如 `git push origin v4.0.0-typing.1`，即授权工作流自动构建并发布。核对远端标签确实指向预定提交，不通过移动标签重试。
3. 查看 Actions 中该标签的 `windows-x64` 和 `publish-release` 任务，两者都需成功。工作流使用 GitHub 提供的授权上传，无需用户额外安装发布工具或提供令牌。
4. 发布任务复用同标签草稿，或新建带 `CHANGELOG.md` 说明的草稿，上传三个资产后发布。本次标签自动标记为预发布；若同标签 Release 已发布，任务会拒绝覆盖，修正应使用新版本。复用草稿时，先核对其说明与本次内容一致。
5. 发布后读取 Release，确认不再是草稿、标签和预发布状态正确、三个资产齐全且大小符合预期；下载两个 ZIP，与下载的 `SHA256SUMS` 比对。
6. 核对最终 Release 页面链接、源码链接和使用说明可访问。私有仓库的 Release 和源码仍受仓库权限控制，不应把链接描述为公众可下载。后续发现错误时使用新版本修正，保留已发行版本的溯源信息。
