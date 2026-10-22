# macOS 安装与构建

背诵练习 1.4.3 的完整整合更新正在准备，尚未发布。两个架构均内置新插件与原 Word / Excel 导入插件，更新时替换整个应用包；下方下载仍指向已发布的 1.4.2。

新版可在练习前补充中文同义答案，或生成候选并逐条确认保存后离线判分。Mac 的模型配置与翻译配置都只在当前进程中保留，退出后需重新输入；已确认的答案会保存在本地。模型设置位于“插件管理 → 背诵练习 → 配置 → 同义答案模型”。

[背诵练习 1.4.2 整合补丁已预发布](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/plugin-synchronous-typing-v1.4.2)。沿用 R8 本体，只更新练习插件，加入“打乱顺序”。需要 **macOS 15 及更新版本**，自带 Word / Excel 导入 1.2.0 和 .NET，首次安装也可直接使用。原 [R8（内置 1.4.1）](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/v4.0.0-typing.8)保留供查阅；Windows 使用 R7 加 1.4.3 插件补丁。

## 选择与安装

在苹果菜单的“关于本机”查看处理器，按下表选择。两包功能相同；.NET 和部分原生库依赖具体架构，因此分别打包。

| 电脑 | 对应包 |
| --- | --- |
| Intel 处理器 | [x64 整合包](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/plugin-synchronous-typing-v1.4.2/DictationAssistant-R8-typing-1.4.2-macos-x64.zip) |
| Apple M 系列芯片 | [arm64 整合包](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/plugin-synchronous-typing-v1.4.2/DictationAssistant-R8-typing-1.4.2-macos-arm64.zip) |

先核对 [Release](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/plugin-synchronous-typing-v1.4.2) 来源与 [SHA256SUMS](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/plugin-synchronous-typing-v1.4.2/SHA256SUMS)，解压后打开 `payload` 文件夹，将 `DictationAssistant.app` 拖到“应用程序”，再打开。更新前退出程序并备份用户数据，替换整个应用包。

应用采用 **ad-hoc 签名**，尚未使用 Developer ID 签名，也未经过 Apple 公证。首次打开若被系统拦截，核对下载来源后，在“系统设置 → 隐私与安全性”允许打开这个应用。不要全局关闭 Gatekeeper；若系统仍报告损坏或签名校验失败，保留报错并反馈，勿跳过校验。

## 练习与翻译

从“插件 → 背诵练习”选择文档，选好提示和答案列后，可静音计时或使用系统语音播报；需要随机出题时，在准备页勾选“打乱顺序”，提示与答案一起移动，整轮保持固定；不勾选则按原顺序。练习中可点“结算成绩”提前结束。详细操作见[背诵练习说明](同步打字插件使用说明.md)。

翻译引擎在“插件管理 → 背诵练习 → 配置…”中设置，也可在准备页临时切换。**macOS 的翻译凭据仅用于当前程序会话，退出后需重新输入。** 当前未使用 Keychain 持久保存，也不提供机器码绑定。请下载后在本地输入服务商凭据；提前补全并校对译文，练习过程中不调用翻译服务。

## 验证范围

1.4.2 的[三平台构建 39437145026](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39437145026)已通过；两个 Mac 任务分别验证固定 R8 来源、331 个不可变应用文件、严格签名、双插件加载、既有逻辑检查、系统语音、SDL dummy 播放和主窗口正常开闭。[发布 39437870903](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39437870903)复用原验证文件，未重新构建本体。以下 R8 记录保留用于说明原本体的来源。

系统语音调用已修正取消清理：取消时终止进程树并等待退出；先读取输出再写入输入，避免管道阻塞。语音列表按语言列识别，保留含空格和括号的完整名称。

[构建 39425977259](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39425977259)在 Intel 与 Apple Silicon 的 macOS runner 上分别通过完整包构建、严格签名校验、双插件加载、15 项既有逻辑检查、系统语音合成、SDL dummy 播放，以及生产主窗口正常打开和关闭。发布对应源码提交 `2c64325cbc14e2699aa0e3ea6f6d71599a25383e`。

进程取消及语音名称解析另在 Linux 完成 15 项临时验证，覆盖实际子进程、管道阻塞、正常 / 错误退出和模拟 `say` 调用。扬声器声音、真实翻译 API、中文输入法和完整背诵交互尚未实测；dummy 播放只验证音频调用路径。

[发布 39426632441](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39426632441)直接使用原验证产物，提供两份整合包、完整配套源码、发布说明、披露文件与校验和；6 份公开附件重新下载核验通过。

## 源码构建

当前补丁在与目标架构一致的 Mac 上，仅构建练习插件并重新封包签名。安装 .NET 10 SDK（遵循 `global.json`）和 Python 3.11+，在干净的 Git checkout 中切到 `plugin-synchronous-typing-v1.4.2` 标签后运行（Intel 将两条命令中的 `osx-arm64` 都改为 `osx-x64`）：

```bash
python3 scripts/prepare_typing_macos_patch.py --build --rid osx-arm64 --output-dir artifacts/typing-patch/osx-arm64
python3 scripts/prepare_typing_macos_patch.py --validate --rid osx-arm64 --output-dir artifacts/typing-patch/osx-arm64
```

补丁源码包含这次完整 Git 版本和私有依赖源码；未改动的宿主及原生库对应源码仍在固定的 [R8 完整源码包](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/v4.0.0-typing.8/DictationAssistant-4.0.0-typing.8-macos-source.zip)，地址与哈希同时记录在补丁的 `HOST_SOURCE_REFERENCES.json`。

下面的旧构建入口用于 **R8 原版标签 `v4.0.0-typing.8` 的源码**，请勿在 1.4.2 补丁源码中运行它：

```bash
bash build-macos-typing.sh --rid osx-arm64
# Intel Mac 使用：bash build-macos-typing.sh --rid osx-x64
```

脚本构建宿主与两个插件，准备原始许可和 `.app` 目录，并下载已公开、固定哈希的 macOS 无 DNG 原生资产及配套源码。原生依赖已通过两种 Mac 的构建与独立源码复核，完整应用的实际运行范围见上文。`.app/Contents/MacOS/` 中的本机启动器运行 `Contents/Resources/runtime/` 中的 .NET 程序，两个插件保留在该运行目录的 `Plugins/` 下。[原生构建说明](NATIVE_BUILD.md)和[发布规范](RELEASING.md)记录对应材料与交付步骤。

正式 NuGet 依赖已固定：宿主、共享契约、两个插件及既有逻辑检查项目，各有 `osx-x64` 和 `osx-arm64` 锁文件，共 10 份。构建和既有检查均以 `--locked-mode` 还原，依赖变化时直接失败。Linux 上的依赖解析检查不代表 Mac 运行验证。

本次补丁使用[三平台补丁工作流](../.github/workflows/release-typing-patch.yml)，流程见[发布规范](RELEASING.md)。以下保留 R8 原版的构建记录：[macOS 工作流](../.github/workflows/build-typing-macos.yml)只接受手动触发（`workflow_dispatch`）。正式发布先在 `main` 触发普通构建，留空 `native_run_id`，使用已公开并固定的原生资产；两个 Mac 任务通过后，核对提交并在同一提交创建 R8 标签，再针对该标签触发工作流，令 `build_run_id` 指向原成功运行。发布直接复用这批已验证应用包，收集完整配套源码、核对附件摘要后公开；推送标签不会重复构建。审核用的 `native_run_id` 不能进入正式发布。

Avalonia 11.3.12 的 MS-PL/AGPL 组合授权事项仍未解决，继续随发行披露；ad-hoc 签名和技术检查不改变这一状态。
