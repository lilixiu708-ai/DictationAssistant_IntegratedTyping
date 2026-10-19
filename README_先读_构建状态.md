# 先读 — 原版桌面程序 + 真 DLL 同步打字插件

**本压缩包是源码，不是已预装可运行版。** 

为了避免再误交付本地网页版本，本次所有实现都是原版 DictationAssistant 的 C#/Avalonia 桌面程序 + 独立 DLL 插件；原来的 Word 插件和打乱词序功能保留。

云环境已安装 .NET 10 SDK，完成 Windows x64 自包含包交叉发布、包校验和现有打字逻辑检查；真实 Windows 窗口、音频和输入法仍需实机验证。轻量化后的插件共享主程序图形库，安装包仍自带 .NET 运行环境。

**不需要 PowerShell 的最简方式**：使用 `DictationAssistant_IntegratedTyping_GitHubReady.zip` 中的 Git 仓库，通过 GitHub Desktop 发布，然后在 Actions 下载 Windows 完整包（见 `docs/同步打字插件使用说明.md`）。构建是否成功以 GitHub Actions 实际运行结果为准。

有本地 .NET 10 SDK 时也可直接运行 `build-windows-typing.cmd`，纯 CMD、不调用 PowerShell。

详细功能与限制请阅读 `docs/同步打字插件使用说明.md`。许可证见 `LICENSE.md`。
