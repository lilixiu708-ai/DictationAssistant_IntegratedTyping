# DictationAssistant

> 2026-10-19 定制版：基于上传的 4.x 源码加入 DOCX 词表提取、随机顺序和独立同步打字 DLL 侧载接口。当前交付为源码 + GitHub Actions Windows 构建工作流，**尚未在本环境成功编译为可运行 Windows EXE**。可直接使用 `build-windows-typing.cmd`（不调用 PowerShell），详见 [同步打字插件使用说明](docs/同步打字插件使用说明.md)。

自动默写（DictationAssistant）是一个用于辅助学习的工具，它可以利用自动化手段帮助您默写单词、短语、句子等。软件通过语音合成技术以给定的间隔、重复次数朗读指定的文本，以帮助您在传统纸张或电子设备上默写。

此分支包含 DictationAssistant 的 4.x 实现。

## 历史版本

| 版本   | 分支   | 技术方案         |
| :----- | :----- | :--------------- |
| 4.x    | 4.x    | C# + Avalonia    |
| 3.x    | 3.x    | C# + WPF         |
| 2.x    | 2.x    | C# + WinForm     |
| 1.x    | 1.x    | VB.NET + WinForm |
| Legacy | legacy | VB6              |

## 开发
DictationAssistant 的 v4.x 使用 .NET 10 + Avalonia，推荐在 Linux/macOS/Windows 使用 `dotnet` CLI 进行构建和运行：

```bash
dotnet restore DictationAssistant.slnx
dotnet build DictationAssistant.slnx
dotnet run --project src/DictationAssistant.App/DictationAssistant.App.csproj
```

同时推荐使用 VSCode 作为编辑环境。VSCode 配置已经包含在项目中，可以开箱即用。

## 贡献
欢迎提交 Pull Request 或 Issue。

## 许可证
    Copyright (C) 2013-2026 alampy.com

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as published
    by the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.

本项目根据 AGPL-3.0 或更高版本的许可证授权。请参阅 [LICENSE](LICENSE.md) 文件以获取详尽的信息。
