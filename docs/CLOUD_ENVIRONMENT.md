# Linux 云环境开发

本仓库将可复用安装步骤保存在 [.codex/setup.sh](../.codex/setup.sh)，供云任务或本机 Linux checkout 使用。复用当前隔离任务中的已有 checkout；除非明确要求，不另建 Git worktree。

## 安装与验证

需要 Bash、Python 3.11+、标准 Linux .NET 运行依赖；首次下载 SDK 时还需 `curl` 和可访问的官方 HTTPS 来源。在仓库根目录执行：

```bash
bash .codex/setup.sh
```

脚本根据自身位置定位仓库，按 `global.json` 选择 SDK（当前为 **10.0.100**）。优先复用已安装的匹配 SDK；缺少时通过 Microsoft 官方安装脚本安装，不关闭 TLS 或包校验。`/workspace` 内的 checkout 复用 `/workspace/.tools/` 的 SDK、CLI 与 NuGet 缓存；其他位置使用仓库父目录的 `.tools/`，不会把缓存写入源码树。

脚本依次执行 Linux restore、双并行 Release 构建、两插件发布、现有 15 项打字逻辑检查及无界面插件加载检查。Linux 锁文件写入各项目的 `obj/cloud.packages.lock.json`，保留已提交的 Windows RID 锁文件；插件放入宿主 `bin/Release/net10.0/Plugins/`。CLI 结果保存在已忽略的 `artifacts/cloud-validation/plugins.json`，必须成功加载 Word 与同步打字两个插件。

脚本不修改应用源码、依赖声明、已提交锁文件或个人服务配置，不自动清理已有缓存和用户文件。已知依赖告警仍按构建输出记录；成功安装不等于 Windows 桌面或在线服务已经验证。

## 新任务与启动

SDK 与包缓存可随环境文件保留，当前 shell 的工具激活需要重新执行。在仓库根目录运行：

```bash
eval "$(bash .codex/setup.sh --print-env)"
dotnet --version
```

`--print-env` 仅输出经过 shell 转义的工具路径，不下载、构建或读取个人设置。源码或依赖变化后，重新运行 `bash .codex/setup.sh`。当前无界面开发流程不需要后台服务。

有图形会话及本机音频依赖时，可启动已构建的桌面程序：

```bash
dotnet src/DictationAssistant.App/bin/Release/net10.0/DictationAssistant.App.dll
```

没有 X11 图形会话时只执行构建、现有逻辑检查和 CLI 验证；不要把无法打开窗口写成桌面功能已通过。Windows 包及实机交互按[验证清单](同步打字验证清单.md)另行执行。翻译服务仅在本机程序的插件配置窗口中管理，脚本不提供或注入个人凭据。

## 仓库文件与云环境设置

这些文件通过 Git 版本管理，提交到 GitHub 后可供后续任务按上述命令执行。保存或编辑仓库文件不会自动激活云平台的安装指令、发布环境快照，也不能移除平台中已有的服务绑定。平台配置的修改和绑定移除仍须在环境设置中完成；仓库脚本与平台设置是分别保存的内容。
