# 贡献与仓库规则

提交修改前请先阅读 [AGENTS.md](AGENTS.md)，其中的架构与开发约定继续适用。本项目保留主程序的单体架构，通过共享契约扩展插件；不要为整理目录而拆分 Core 与 App。

## 命名与目录

| 对象 | 规则与示例 |
| --- | --- |
| GitHub 仓库 | 当前保留 `lilixiu708-ai/DictationAssistant_IntegratedTyping`；新仓库通常采用小写短横线（lower-kebab-case） |
| 产品名称 | 文档与发布说明使用 `DictationAssistant Typing` |
| C# 项目、程序集、命名空间 | 保持 `DictationAssistant.*`；按职责命名，例如 `DictationAssistant.Practices.Typing` |
| C# 类型、公开成员与文件 | 使用现有 PascalCase 风格；文件名与主要类型一致 |
| 参数与局部变量 | camelCase；私有字段遵循所在文件现有约定 |
| 插件 ID | 小写短横线，例如 `synchronous-typing`；已发布 ID 不随显示名称变化 |
| 插件入口与契约 | 维持 `plugin.json` 与共享契约的一致性，具体字段见插件开发指南 |
| 分支 | `feat/<主题>`、`fix/<主题>`、`docs/<主题>`、`chore/<主题>` |
| 新增文档 | 面向使用者放在 `docs/`，使用能说明内容的名称，并加入文档索引 |

应用发布标签使用 `v<SemVer>`，当前系列为 `v4.0.0-typing.N` 预览版。插件包版本、插件 ID 和 `apiVersion` 分别管理；应用升级不自动增加插件 API 版本，也不将应用版本写成插件版本。

主程序放在 `src/DictationAssistant.App/`，共享接口放在 `src/DictationAssistant.Plugin.Abstractions/`，独立插件放在 `plugins/`，示例放在 `sdk/examples/`。不要将构建输出、便携 ZIP、个人词表、账号信息或本机配置提交到仓库；生成物使用已忽略的 `artifacts/`、`bin/`、`obj/`。

## 修改原则

- 一次修改解决一个明确问题，保持数据流和取消、释放逻辑清楚。
- UI 线程只处理界面状态；文件读取、语音初始化和大词表计算使用合适的异步或后台流程。
- 长列表优先使用虚拟化与控件复用；避免每次按键或计时器回调重新创建整片界面、重复计算或复制完整词表。
- 优先使用 .NET 绑定库与 API，遵守 AGENTS.md 中的 P/Invoke 原则。
- 插件只携带需要的私有依赖，复用宿主提供的契约和图形库；保持动态加载需要的 `PublishTrimmed=false`、`PublishAot=false`。
- 不在没有需求时增加兼容分支。依照 AGENTS.md，不新增测试代码；使用现有检查程序和有针对性的运行验证。
- 遵循 `.editorconfig` 与相邻代码风格；避免夹带全仓库格式化、依赖升级或文件重命名。

## 构建与检查

SDK 选择以 `global.json` 为准。Windows 完整包的标准入口是：

```bat
build-windows-typing.cmd
python scripts\verify_build.py
```

脚本使用已提交的锁文件还原 `win-x64` 主程序，发布宿主和两个插件，并运行现有打字逻辑检查。不要在日常构建时顺便更新依赖声明或锁文件；确需升级时，将原因和影响作为单独变更说明。

Linux 开发按 [README 的本地锁文件命令](README.md#开发与构建) 执行。主程序锁文件包含 Windows RID 解析结果，普通 Linux 还原应将 `NuGetLockFilePath` 指向各项目 `obj/` 中的本地文件，避免覆盖仓库锁文件。

只运行已有打字逻辑检查时，可在完成相同配置的 restore/build 后使用：

```bash
dotnet run --project tests/TypingPlugin.SmokeTests/TypingPlugin.SmokeTests.csproj -c Release --no-build --no-restore
```

涉及界面、音频、输入法或性能的修改，还需执行 [Windows 验证清单](docs/同步打字验证清单.md) 中对应步骤。说明实际执行的平台、步骤、结果和未验证项目；CPU 或内存改善应给出可复现的对照条件与数据，未测量时不要写成已确认的降幅。

## Issue、提交与 Pull Request

缺陷报告应包含版本、Windows 版本和架构、复现步骤、预期与实际结果。上传最小示例词表或日志前，移除个人资料、语音服务凭据和其他敏感内容。

新提交建议使用 `fix: 降低倒计时刷新频率`、`docs: 更新安装说明` 等具体描述，不重写已有提交历史。PR 说明需要写清解决的问题、最终行为、验证结果和限制；如果改动影响用户操作或插件接口，同时更新对应文档。

## 发布与许可

Release 指向已验证的具体提交；预览版本勾选 pre-release。上传完整 Windows 便携 ZIP、对应源码和 SHA-256 校验文件；包内应保留许可证与使用说明。应用版本、标签及发布说明应一致，不能把 Linux 交叉构建写成 Windows 实机验证。

发布资产使用包含应用名、版本、平台与架构的名称；本地构建脚本可保留固定输出名，由发布步骤生成版本化资产。历史报告只作记录，不覆盖当前验证状态；新增记录注明版本和对应提交。具体发布流程见 [发布说明](docs/RELEASING.md)。

第一方贡献沿用项目的 [AGPL-3.0-or-later](LICENSE.md) 许可，并保留 [NOTICE.md](NOTICE.md) 中的上游 `Copyright (C) 2013-2026 alampy.com`、授权和无担保通知。记录真实修改日期及范围，更新 [MODIFICATIONS.md](MODIFICATIONS.md) 或对应修改记录；不要凭空添加作者、版权转让或独占权声明，也不要把未改动的上游文件标为本 fork 原创。

引入第三方代码、二进制、图标、字体或示例素材前，应记录固定版本、原始来源、版权通知、完整许可证及实际分发范围，更新 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)、[`LICENSES/`](LICENSES/) 和 [`third-party/components.json`](third-party/components.json)。构建依赖与随包交付组件应分别标明；原始第三方材料保留自身许可，不得统一重新授权为 AGPL。收集许可证文本并不自动解决许可证兼容性、商业授权或对应源码义务。

分发二进制时，提供同一提交/标签的对应源码、构建脚本、项目与锁文件，并保留必要的第三方源码或固定版本来源入口。Skia/ANGLE 等 `DEPS` 和子模块需按实际构建继续核对；应用源码 ZIP 与来源索引不自动等于完整原生对应源码。当前 `typing.4` 公开发布暂停，DNG 商业赔偿条款与 AGPL 的组合兼容性尚无权利人例外或权威结论。按[版权与许可审计](docs/版权与许可审计.md)和[发布规范](docs/RELEASING.md)处理未解除事项，不能以免责声明或自动构建通过替代必要授权与交付材料。

使用 AI 辅助生成、修改或构建时，可如实披露本 fork 的使用范围。不要把上游原作或第三方材料说成 AI 生成，不声明没有完成的人工审查、实机验证或素材授权。
