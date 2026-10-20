# 插件 SDK 示例

开发入口见 [插件扩展 SDK](../docs/PLUGIN_SDK.md)，既有文件导入格式见 [插件开发指南](../docs/插件开发指南.md)。所有示例遵循仓库根目录的 [AGPL-3.0-or-later 许可证](../LICENSE.md)。

源码开发中的宿主 `4.0.0-typing.6` 新增功能级别 4 的可选 `IConfigurablePlugin.ConfigureAsync(object ownerWindow, CancellationToken)`，供插件管理逐插件打开「配置…」。共享 `apiVersion` 仍为 1，使用新接口或 `kind: "configuration"` 时须声明 `minimumHostFeatureLevel: 4`。该开发版本尚未发布，公开 R5 为功能级别 3；下列既有示例仍保留各自的最低功能级别。

| 示例 | 功能 |
| --- | --- |
| [`PhrasesImporter`](examples/PhrasesImporter/) | 原有 API 1 文档导入：每行一个短语 |
| [`ExtensionToolkit`](examples/ExtensionToolkit/) | 功能级别 2：词表命令、修订保护、观察器和生命周期 |
| [`ToneVoice`](examples/ToneVoice/) | 功能级别 2：离线 PCM 提示音，不朗读文本，不是 TTS |
| [`GainEffect`](examples/GainEffect/) | 功能级别 2：U8/S16LE 增益和限幅 |

在源码根目录单独执行 `dotnet publish <示例项目路径> -c Release -o artifacts/sdk-examples/<示例名>`。新示例不加入主解决方案或默认发行包，编译后也不会自动安装。只在需要开发演示时，把发布文件完整复制到应用的一个 `Plugins/<插件ID>/` 直接子文件夹，然后重启应用。
