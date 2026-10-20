# 插件扩展 SDK

功能级别 2 在原有文档导入和练习接口上增加命令、词表观察、语音提供器及 PCM 音效。共享契约仍为 `DictationAssistant.Plugin.Abstractions`，`apiVersion` 保持 `1`；插件使用新增接口时，在清单中声明 `minimumHostFeatureLevel: 2`。应用版本、插件自身版本、接口版本和宿主功能级别分别管理。

原有 DOCX、`.phrases` 文档结构及导入限制见 [插件开发指南](插件开发指南.md)。本页对应的可编译示例位于 [`sdk/examples/`](../sdk/examples/)，均为可选开发示例，不自动安装到应用，不加入主解决方案或 Windows 发行包。

当前宿主功能级别为 3，新增可选的 `IPracticeWordListMetadata.ExpectedWordCount`。宿主从同一份编辑器快照独立统计非空行，再解析并提供不可变词条；练习插件可在后台逐条读取后核对实际数量。使用此类型的插件声明 `minimumHostFeatureLevel: 3`；仅使用功能级别 2 接口的既有示例仍声明 2。共享程序集和 `apiVersion` 保持 1。

## 选择扩展接口

一个公开无参入口类可以实现多个接口。清单的 `kind` 表示主要功能，宿主同时识别该实例实现的其他能力。

| 接口 | 方法或数据 | 职责 |
| --- | --- | --- |
| `IDocumentImporter` | `ReadAsync(Stream, string, CancellationToken)` → `ImportDocument` | 读取文件，返回分区、列和行；不修改源文件，不关闭宿主传入的流 |
| `IPracticePlugin` | `RunAsync(IPracticeHost, CancellationToken)` | 运行桌面练习；使用词条快照、重复次数、间隔和 `SpeakOnceAsync`，退出前结束播放 |
| `ICommandPlugin` | `Commands`；`ExecuteAsync(commandId, context, token)` | 声明固定命令并按用户操作执行；使用宿主服务处理词表、对话和音频 |
| `IWordListObserverPlugin` | `OnWordListChangedAsync(snapshot, token)` | 后台接收合并后的词表快照，适合统计和索引 |
| `IVoiceProviderPlugin` | `GetVoicesAsync(token)`；`SynthesizeAsync(voiceId, text, options, token)` | 提供语音列表和完整 PCM；不打开音频设备或操作界面 |
| `IAudioEffectPlugin` | `ProcessAsync(audio, token)` | 处理完整 PCM 缓冲区，返回处理结果 |
| `IPluginLifecycle` | `InitializeAsync(context, token)`；`ShutdownAsync(token)` | 可选初始化与关闭，适用于任意主要功能 |

清单支持 `document-importer`、`practice`、`command`、`voice`、`audio-effect` 和 `extension`。主要功能对应的接口必须实现；多功能插件可选择 `extension`。单纯生命周期不是一个可独立安装的功能；入口还须实现至少一个功能接口。

清单省略 `kind` 时按既有文档导入器处理。文档导入能力需要 1–32 个合法文件扩展名；其他示例使用 `extensions: []`。同时实现导入及其他能力的入口可用 `kind: "extension"` 并声明其导入扩展名。插件 ID 不区分大小写，安装目录中不得出现重复 ID。

## 从词表工具示例开始

[`ExtensionToolkit`](../sdk/examples/ExtensionToolkit/) 没有额外 NuGet 依赖，实现 `ICommandPlugin`、`IPluginLifecycle` 和 `IWordListObserverPlugin`。

| 项目 | 插件 ID | 命令 ID |
| --- | --- | --- |
| `sdk/examples/ExtensionToolkit/ExtensionToolkit.csproj` | `example-extension-toolkit` | `normalize-word-list`、`word-list-statistics` |

`normalize-word-list` 删除空行、修剪行首尾空白，并以大小写敏感规则保留第一次出现的词条；保持词条顺序和词条内部空格。它只在用户执行命令时修改词表。`word-list-statistics` 统计每行一个词条的数量、唯一数量和重复数量，不做自然语言分词。观察回调只记录数量，不保存词条内容，也不自动写回。

在源码根目录执行：

```text
dotnet publish sdk/examples/ExtensionToolkit/ExtensionToolkit.csproj -c Release -o artifacts/sdk-examples/ExtensionToolkit
```

发布后，将该输出文件夹中的文件复制到应用的 `Plugins/example-extension-toolkit/`，确保 `plugin.json` 直接位于此文件夹内，重启应用再使用。不要复制进 `Plugins` 根目录或创建额外的嵌套目录。只编译示例不会安装它。

示例清单如下：

```json
{
  "id": "example-extension-toolkit",
  "name": "词表工具示例",
  "version": "1.0.0",
  "description": "演示带修订保护的词表规范、统计、生命周期及后台观察；不会自动修改词表。",
  "apiVersion": 1,
  "minimumHostFeatureLevel": 2,
  "kind": "command",
  "extensions": [],
  "entryAssembly": "Example.ExtensionToolkit.dll",
  "entryType": "Example.ExtensionToolkit.ExtensionToolkit"
}
```

`PluginCommand` 包含 `Id`、`Name` 和可选 `Description`。命令 ID 应在同一插件内唯一且保持稳定，入口根据 ID 分派，未知 ID 应报错。初始化完成后宿主固定命令列表，不要通过后台修改 `Commands` 动态增删菜单。

## 无界面命令行验证

先把需要的示例安装到待验证程序旁的 `Plugins` 子目录，再在该程序目录执行。以下以 Windows EXE 为例；开发环境也可通过 `dotnet <构建目录>/DictationAssistant.App.dll` 传入相同参数。

```text
DictationAssistant.App.exe --list-plugin-commands
DictationAssistant.App.exe --run-plugin-command example-extension-toolkit normalize-word-list --input word-list.txt --output normalized.txt
DictationAssistant.App.exe --list-plugin-voices
DictationAssistant.App.exe --synthesize-plugin example-tone-voice tone-440 --text "PCM demonstration" --output tone.wav
```

命令输入、输出为文本文件；合成命令输出 WAV，不启动窗口或打开播放设备。输出文件已存在时默认拒绝覆盖，需要明确添加 `--overwrite`。合成可选 `--rate -10..10`；ToneVoice 仍只生成提示音，不会朗读文本。CLI 仅加载程序旁的插件目录，并不搜索任意示例输出目录。

Ctrl+C 请求合作取消，宿主等待在途调用结束和插件关闭。退出码为 `0` 成功、`1` 执行失败或取消、`2` 参数错误。无界面命令通过日志和输出文件提供结果；`ShowMessageAsync` 等界面能力不受支持，调用会明确报错。示例仅在 `OwnerWindow` 非空时显示消息，代码不能依赖窗口存在。

## 词表快照与修订保护

`PluginWordListSnapshot` 包含完整 `Text`、`SelectedText` 和递增 `Revision`。计算结果后写回时，传入取得快照时的修订号：

```csharp
var snapshot = await context.GetWordListAsync(cancellationToken);
var replacement = Transform(snapshot.Text);
await context.UpdateWordListAsync(
    new PluginWordListUpdate(replacement, ExpectedRevision: snapshot.Revision),
    cancellationToken);
```

这段是接口用法，`Transform` 由插件实现。用户可能在计算期间继续输入；修订不匹配时宿主拒绝更新，避免覆盖新输入。此时应让用户重试，或重新获取快照并重新计算。不要移除 `ExpectedRevision` 后盲目重试。`Append` 默认为 `false`，可设置为追加；同样应保留修订保护。

插件更新后的完整词表最多 1,600 万字符，追加也按合并后的总量校验。读取文件的上限为 64 MiB；不要为了少量词条修改长期保留多个完整快照。

桌面宿主负责在界面线程应用一次可撤销编辑，并在更新词表前停止当前播放。插件不要直接寻找或修改编辑器控件。

词表观察通知会合并，回调在后台运行，不保证每次按键都有一次通知，也不保证修订号连续。用快照内容计算结果，避免保留完整历史或重复建立大数组。不要从观察器无条件写回词表，否则可能产生通知循环；示例只记录统计值。

## 宿主上下文

`IPluginContext` 把词表、界面和音频操作交给宿主管理。

| 成员 | 使用方式 |
| --- | --- |
| `PluginId`、`HostVersion` | 标识与诊断信息，功能兼容要求仍通过清单声明 |
| `DataDirectory` | 本插件可写数据目录，适合设置和缓存；不要在安装 DLL 的目录写用户数据 |
| `OwnerWindow` | 桌面宿主中的 Avalonia 所有者窗口；无界面宿主为 `null` |
| `GetWordListAsync` | 取得文本、选区和修订号的一致快照 |
| `UpdateWordListAsync` | 替换或追加词表；优先提供 `ExpectedRevision` |
| `InvokeOnUiAsync` | 需要自定义 Avalonia 界面时，调度到宿主 UI 线程；无界面宿主不支持 |
| `ShowMessageAsync` | 由桌面宿主呈现消息；先检查 `OwnerWindow`，无界面命令使用日志或文件 |
| `SpeakAsync` | 使用当前语音及宿主播放链路，等待操作结束 |
| `SynthesizeAsync` | 使用选定语音生成 PCM，并经过注册的音效处理 |
| `PlayAudioAsync` | 原样播放给定 PCM，缓冲区所有权交给宿主；不再次应用音效 |
| `ProcessAudioAsync` | 将给定 PCM 送入宿主音效链 |
| `ExportWaveAsync` | 将给定 PCM 写为 WAV，保留目标流打开；不再次应用音效 |
| `Log` | 记录 Information、Warning 或 Error 诊断，避免打印凭据和完整用户词表 |

所有异步操作都应传递宿主提供的取消令牌。音频先 `SynthesizeAsync` 再 `ExportWaveAsync` 可导出含当前音效的 WAV；不要再次调用处理造成效果叠加。直接播放自有 PCM 时，如确实需要音效，先明确调用 `ProcessAudioAsync` 再播放。

命令和观察器必须支持 `OwnerWindow == null`，不要直接强制转换窗口。只有需要显示自定义窗口且所有者存在时，才在 `InvokeOnUiAsync` 内创建、显示或修改 Avalonia 控件。无界面宿主明确拒绝 UI 调度和消息框，通用功能应通过日志或文件输出提供结果。既有 `IPracticeHost` 是桌面模态练习契约，不要把它当作命令行窗口服务。

## 生命周期与取消

构造函数只建立轻量状态，不联网、不打开设备、不启动长期任务。宿主加载后调用可选的 `InitializeAsync`，再固定命令列表并执行功能。初始化失败会显示插件错误，而不是让插件静默处于半初始化状态。

宿主在后台调度插件工作，并对同一实例的常规调用进行串行管理。插件自己启动的任务仍须自行管理，不要依赖 UI 线程或同步上下文；长循环和分块 I/O 定期检查取消。不要使用 `async void`、阻塞等待 `.Result`，或启动不跟踪的无限后台任务。

语音枚举、合成和音效调用采用不等待空闲的调度方式：目标插件仍在初始化或忙于另一项操作时，会立即报告忙碌错误，避免音频链一直等待插件自己释放。`waitForTurn: false` 是宿主运行时的调度选项，不是插件要传给上下文的新参数。

同时提供界面与语音能力的插件应在同一调用链中顺序 `await` 宿主操作。独立的 GUI 点击事件不一定保留原调用的上下文；如果长时间模态练习或命令仍占用该插件，再从点击事件请求其语音或音效会被拒绝，不能阻塞等待自己完成。通常将语音、音效实现为独立插件更简单；否则先结束当前长操作，再重试被拒绝的音频操作。

关闭时宿主请求取消，等待在途操作结束，再调用 `ShutdownAsync` 及可选的 `IAsyncDisposable`/`IDisposable`。关闭方法应能处理部分初始化；停止自建任务、释放资源并退订事件。取消是协作式的，进程内宿主不能安全强制终止任意插件代码，忽略令牌可能拖延退出。

避免通过宿主服务递归调用自己。例如语音提供器不要在合成方法内再次调用当前选中语音的合成，音效不要在处理回调内再次运行同一音效链；初始化期间也不要经宿主服务触发自己的初始化。宿主会报告检测到的递归调用。

## PCM、语音与音效示例

`PluginAudioData` 是完整、未压缩 PCM：`byte[] Data`、`SampleRate`、`Channels` 和 `SampleFormat`。支持 `U8` 与 `S16LE`：前者无符号、静音中点为 128，后者为小端有符号 16 位、静音为 0。采样率范围为 8–192 kHz，声道为 1–8；返回长度必须包含完整采样帧，并匹配声明的声道和采样率；单个结果上限为 64 MiB。

返回后不要修改 PCM 缓冲区，不要把插件流直接塞入原生音频回调。语音提供器和音效只生成或处理数据，设备由宿主管理。处理器优先返回新缓冲区，保留输入格式元数据；浮点或压缩音频需先自行转换成契约支持的 PCM。

| 示例 | 清单类型 | 行为 |
| --- | --- | --- |
| [`ToneVoice`](../sdk/examples/ToneVoice/) | `voice` | 提供 `tone-440`，离线生成 24 kHz 单声道 S16LE 的 440 Hz 正弦提示音；文本仅影响时长，最长 2 秒，**不是 TTS，不朗读文本** |
| [`GainEffect`](../sdk/examples/GainEffect/) | `audio-effect` | 把 U8/S16LE 幅度减半，输出新缓冲区；演示整数限幅，修改为大于 1 的增益时仍避免整数溢出 |

可选编译命令：

```text
dotnet publish sdk/examples/ToneVoice/ToneVoice.csproj -c Release -o artifacts/sdk-examples/ToneVoice
dotnet publish sdk/examples/GainEffect/GainEffect.csproj -c Release -o artifacts/sdk-examples/GainEffect
```

它们不会自动安装。需要演示时才分别复制到 `Plugins/example-tone-voice/` 和 `Plugins/example-gain-effect/`，重启后启用。安装音效示例会改变经过宿主音效链的音量；提示音示例只用于检查 PCM 通路，不能用于听写朗读。移除或禁用后重启恢复原有配置。

## 发布依赖与排错

所有新示例使用 `net10.0`、`EnableDynamicLoading=true`，通过 `ProjectReference` 引用源码契约，配置 `Private=false` 和 `ExcludeAssets=runtime`。无需额外 NuGet 依赖。完整示例发布输出应包含入口 DLL、`plugin.json` 以及实际生成的依赖描述和私有依赖。

插件不要携带另一份共享 `DictationAssistant.Plugin.Abstractions.dll`，也不要重新发布宿主的 Avalonia、原生图形库和整套 .NET 运行时。有自定义界面时，针对宿主相同 Avalonia 版本编译并共享其程序集；真实私有依赖应随插件保留，原生依赖必须匹配宿主平台和架构。

在插件管理中查看清单、加载和初始化错误，以及运行错误及日志。日志有容量限制，只适合近期诊断，不作为永久审计或词表存储。修改启用状态只是保存下次启动的偏好，禁用、升级或删除插件均应重启应用；当前运行中的 DLL 不支持热替换和强制卸载。

插件在宿主进程中以应用的权限运行，可以访问文件、网络及原生代码。依赖加载上下文、清单校验和取消令牌都不构成安全沙箱。只安装信任来源的插件，并遵守项目 AGPL-3.0-or-later 及所用第三方组件的许可证。
