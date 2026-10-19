# 先读 — 原版桌面程序 + 真 DLL 同步打字插件

**本压缩包是源码，不是已预装可运行版。** 

为了避免再误交付本地网页版本，本次所有实现都是原版 DictationAssistant 的 C#/Avalonia 桌面程序 + 独立 DLL 插件；原来的 Word 插件和打乱词序功能保留。

现有执行环境没有 .NET 10 SDK 且无法下载安装，因此不能在这里生成经过验证的 Windows x64 EXE/DLL。源代码和自动构建工作流已准备好，在 GitHub Actions 的 Windows 机器构建。

**不需要 PowerShell 的最简方式**：使用 `DictationAssistant_IntegratedTyping_GitHubReady.zip` 中的 Git 仓库，通过 GitHub Desktop 发布，然后在 Actions 下载 Windows 完整包（见 `docs/同步打字插件使用说明.md`）。构建是否成功以 GitHub Actions 实际运行结果为准。

有本地 .NET 10 SDK 时也可直接运行 `build-windows-typing.cmd`，纯 CMD、不调用 PowerShell。

详细功能与限制请阅读 `docs/同步打字插件使用说明.md`。许可证见 `LICENSE.md`。
