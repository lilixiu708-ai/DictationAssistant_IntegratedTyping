# macOS 安装与构建

[背诵练习 1.4.3 完整整合更新已预发布](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/plugin-synchronous-typing-v1.4.3-macos.1)。沿用 R8 本体，内置背诵练习 1.4.3、Word / Excel 导入 1.2.0 和 .NET 运行环境，首次安装也可直接使用。需要 **macOS 15 及更新版本**；更新时替换整个应用包，无需单独装载插件。

新版可在练习前补充中文同义答案，或生成候选并逐条确认保存后离线判分。Mac 的模型配置与翻译配置都只在当前进程中保留，退出后需重新输入；已确认的答案和校对保存的译文会保存在本地。模型设置位于“插件管理 → 背诵练习 → 配置 → 同义答案模型”。

此前的 [1.4.2 随机词序整合更新](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/plugin-synchronous-typing-v1.4.2)与原 [R8（内置 1.4.1）](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/v4.0.0-typing.8)保留供查阅。Windows 继续使用 R7 加 1.4.3 独立插件补丁。

## 选择与安装

在苹果菜单的“关于本机”查看处理器，按下表选择。两包功能相同；.NET 和部分原生库依赖具体架构，因此分别打包。

| 电脑 | 对应包 |
| --- | --- |
| Intel 处理器 | [x64 整合包](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/plugin-synchronous-typing-v1.4.3-macos.1/DictationAssistant-R8-typing-1.4.3-macos-x64.zip) |
| Apple M 系列芯片 | [arm64 整合包](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/plugin-synchronous-typing-v1.4.3-macos.1/DictationAssistant-R8-typing-1.4.3-macos-arm64.zip) |

先核对 [Release](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/plugin-synchronous-typing-v1.4.3-macos.1) 来源与 [SHA256SUMS](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/plugin-synchronous-typing-v1.4.3-macos.1/SHA256SUMS)。ZIP 可以在 Windows 下载，安装时在 Mac 上解压，打开 `payload` 文件夹，将 `DictationAssistant.app` 拖到“应用程序”，再打开。更新前退出程序并备份用户数据，替换整个应用包。

应用采用 **ad-hoc 签名**，尚未使用 Developer ID 签名，也未经过 Apple 公证。首次打开若被系统拦截，核对下载来源后，在“系统设置 → 隐私与安全性”允许打开这个应用。不要全局关闭 Gatekeeper；若系统仍报告损坏或签名校验失败，保留报错并反馈，勿跳过校验。

## 练习与翻译

从“插件 → 背诵练习”选择文档，选好提示和答案列后，可静音计时或使用系统语音播报；需要随机出题时，在准备页勾选“打乱顺序”，提示与答案一起移动，整轮保持固定；不勾选则按原顺序。练习中可点“结算成绩”提前结束。详细操作见[背诵练习说明](同步打字插件使用说明.md)。

翻译引擎在“插件管理 → 背诵练习 → 配置…”中设置，也可在准备页临时切换。**macOS 的翻译凭据仅用于当前程序会话，退出后需重新输入。** 当前未使用 Keychain 持久保存，也不提供机器码绑定。请下载后在本地输入服务商凭据；提前补全并校对译文，练习过程中不调用翻译服务。

## 验证范围

1.4.3 的[双 Mac 预检 39755710588](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39755710588)与[发布 39756286829](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39756286829)均已成功，源码提交为 `7cf4a8602cfe4ea3e227e6d9b6bee17e6806a7a2`。两个原生 Mac 任务各核对 337 个应用文件、331 个固定 R8 不可变文件、严格签名、双插件加载、15 项既有逻辑检查、系统语音合成、SDL dummy 播放和主窗口正常开闭；另各通过 14 项模型配置观察，确认仅保留在内存、错误或取消保存不替换旧值、退出进程后不保留配置。观察仅使用合成值，未调用模型服务。

公开后的六份附件重新下载，与原 CI 产物逐字节相同，服务器摘要与五条校验和均通过；411 个 Git 导出源码文件、127 份原始许可、152 份原始材料及私有依赖源码核对通过。原有 19 份 Release、93 份附件和 20 个标签引用保持不变。完整人工背诵、真实输入法、物理扬声器和在线模型／翻译服务仍未实测。以下保留 1.4.2 和 R8 的历史验证记录。

1.4.2 的[三平台构建 39437145026](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39437145026)已通过；两个 Mac 任务分别验证固定 R8 来源、331 个不可变应用文件、严格签名、双插件加载、既有逻辑检查、系统语音、SDL dummy 播放和主窗口正常开闭。[发布 39437870903](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39437870903)复用原验证文件，未重新构建本体。以下 R8 记录保留用于说明原本体的来源。

系统语音调用已修正取消清理：取消时终止进程树并等待退出；先读取输出再写入输入，避免管道阻塞。语音列表按语言列识别，保留含空格和括号的完整名称。

[构建 39425977259](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39425977259)在 Intel 与 Apple Silicon 的 macOS runner 上分别通过完整包构建、严格签名校验、双插件加载、15 项既有逻辑检查、系统语音合成、SDL dummy 播放，以及生产主窗口正常打开和关闭。发布对应源码提交 `2c64325cbc14e2699aa0e3ea6f6d71599a25383e`。

进程取消及语音名称解析另在 Linux 完成 15 项临时验证，覆盖实际子进程、管道阻塞、正常 / 错误退出和模拟 `say` 调用。扬声器声音、真实翻译 API、中文输入法和完整背诵交互尚未实测；dummy 播放只验证音频调用路径。

[发布 39426632441](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39426632441)直接使用原验证产物，提供两份整合包、完整配套源码、发布说明、披露文件与校验和；6 份公开附件重新下载核验通过。

## 源码构建

当前更新在与目标架构一致的 Mac 上，仅构建练习插件并重新封包签名。Windows 可以提交源码或触发 GitHub Actions；实际打包、签名和运行验证由两个原生 Mac runner 完成。

[plugin-synchronous-typing-v1.4.3-macos.1 对应源码](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/plugin-synchronous-typing-v1.4.3-macos.1/synchronous-typing-1.4.3-macos-source.zip)包含完整 Git 导出、私有依赖源码、两种架构的验证记录及 `PLUGIN_BUILD.md`。只编译插件时按 `PLUGIN_BUILD.md` 操作；完整应用的复现使用该标签的干净 Git checkout，安装 `global.json` 指定的 .NET 10 SDK 和 Python 3.12，依次执行下列三步。Intel 将下列所有 `osx-arm64` 替换为 `osx-x64`。

1. 组装应用并签名：

```bash
git checkout plugin-synchronous-typing-v1.4.3-macos.1
python3 scripts/prepare_typing_macos_patch.py --plugin-release-tag plugin-synchronous-typing-v1.4.3-macos.1 --build --rid osx-arm64 --output-dir artifacts/typing-macos-143/osx-arm64
```

2. 执行[本版工作流](../.github/workflows/release-typing-macos-update.yml)中名为 `Observe session-only model settings in two native Mac processes without calling a service` 的 Python 步骤。去掉 YAML 的统一缩进后，将该步骤的 `run` 内容保存到 `/tmp/typing-model-observation.py`，在仓库根目录运行：

```bash
TASK_RID=osx-arm64 GITHUB_SHA=7cf4a8602cfe4ea3e227e6d9b6bee17e6806a7a2 python3 /tmp/typing-model-observation.py
```

此步骤用临时 .NET 程序调用刚装入应用的真实插件，验证会话配置及新进程清空，生成 `artifacts/typing-macos-143/osx-arm64/ANSWER_MODEL_OBSERVATION.json`，不需要真实服务凭据。该报告是下一步的必需输入，缺失、来源不符或检查未通过都会停止封包。

3. 验证并生成 ZIP：

```bash
python3 scripts/prepare_typing_macos_patch.py --plugin-release-tag plugin-synchronous-typing-v1.4.3-macos.1 --validate --rid osx-arm64 --output-dir artifacts/typing-macos-143/osx-arm64
```

未改动的宿主及原生库对应源码仍在固定的 [R8 完整源码包](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/v4.0.0-typing.8/DictationAssistant-4.0.0-typing.8-macos-source.zip)，地址与哈希同时记录在整合更新的 `HOST_SOURCE_REFERENCES.json`。旧 1.4.2 的复现仍使用其原标签与[三平台补丁工作流](../.github/workflows/release-typing-patch.yml)。

下面的旧构建入口用于 **R8 原版标签 `v4.0.0-typing.8` 的源码**，请勿将它当作 1.4.3 整合更新的复现入口：

```bash
bash build-macos-typing.sh --rid osx-arm64
# Intel Mac 使用：bash build-macos-typing.sh --rid osx-x64
```

脚本构建宿主与两个插件，准备原始许可和 `.app` 目录，并下载已公开、固定哈希的 macOS 无 DNG 原生资产及配套源码。原生依赖已通过两种 Mac 的构建与独立源码复核，完整应用的实际运行范围见上文。`.app/Contents/MacOS/` 中的本机启动器运行 `Contents/Resources/runtime/` 中的 .NET 程序，两个插件保留在该运行目录的 `Plugins/` 下。[原生构建说明](NATIVE_BUILD.md)和[发布规范](RELEASING.md)记录对应材料与交付步骤。

正式 NuGet 依赖已固定：宿主、共享契约、两个插件及既有逻辑检查项目，各有 `osx-x64` 和 `osx-arm64` 锁文件，共 10 份。构建和既有检查均以 `--locked-mode` 还原，依赖变化时直接失败。Linux 上的依赖解析检查不代表 Mac 运行验证。

本次更新使用[双 Mac 整合更新工作流](../.github/workflows/release-typing-macos-update.yml)：先在 main 验证两个架构，再在同一提交创建本版标签，通过 `build_run_id` 复用原成功产物发布，发布阶段不重新构建。流程见[发布规范](RELEASING.md)。以下保留 R8 原版的构建记录：[macOS 工作流](../.github/workflows/build-typing-macos.yml)只接受手动触发（`workflow_dispatch`）。正式发布先在 `main` 触发普通构建，留空 `native_run_id`，使用已公开并固定的原生资产；两个 Mac 任务通过后，核对提交并在同一提交创建 R8 标签，再针对该标签触发工作流，令 `build_run_id` 指向原成功运行。发布直接复用这批已验证应用包，收集完整配套源码、核对附件摘要后公开；推送标签不会重复构建。审核用的 `native_run_id` 不能进入正式发布。

Avalonia 11.3.12 的 MS-PL/AGPL 组合授权事项仍未解决，继续随发行披露；ad-hoc 签名和技术检查不改变这一状态。
