using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using DictationAssistant.App.Audio;
using DictationAssistant.App.Plugins;
using DictationAssistant.Plugin.Abstractions;

namespace DictationAssistant.App.Importing;

/// <summary>Runs installed extensions without creating an Avalonia application or audio device.</summary>
public static class PluginCommandLine
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public static bool Handles(string[] args) => args.Length > 0 && args[0] is
        "--list-plugin-commands" or "--run-plugin-command" or
        "--list-plugin-voices" or "--synthesize-plugin";

    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = Utf8;
        Console.InputEncoding = Utf8;
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            try { cancellation.Cancel(); }
            catch (Exception ex) { Console.Error.WriteLine($"取消通知发生错误：{ex.Message}"); }
        };
        Console.CancelKeyPress += cancelHandler;
        HeadlessPluginSession? session = null;
        var exitCode = 1;
        try
        {
            var options = ParseArguments(args);
            ValidatePaths(options);
            var text = options.Input is null ? string.Empty
                : await ReadWordListAsync(options.Input, cancellation.Token).ConfigureAwait(false);
            session = new HeadlessPluginSession(cancellation.Token, text);
            await session.Runtime.EnsureLoadedAsync(cancellation.Token).ConfigureAwait(false);
            if (options.Mode == "--synthesize-plugin")
            {
                // Set a concrete provider before lifecycle callbacks can request synthesis.
                session.SetVoice(SelectPlugin(session, options.PluginId!, "voice"), options.ItemId!);
            }
            await session.Runtime.InitializeAsync(cancellation.Token).ConfigureAwait(false);
            switch (options.Mode)
            {
                case "--list-plugin-commands":
                    PrintJson(new
                    {
                        session.Catalog.RootDirectory,
                        Commands = session.Catalog.Entries.Where(entry => entry.CommandPlugin is not null)
                            .Select(entry => new
                            {
                                PluginId = entry.Manifest!.Id,
                                entry.DisplayName,
                                entry.IsLoaded,
                                entry.IsInitialized,
                                entry.Commands,
                                entry.Error
                            }).ToArray(),
                        UnavailablePlugins = GetUnavailablePlugins(session)
                    });
                    break;
                case "--run-plugin-command":
                {
                    var plugin = SelectPlugin(session, options.PluginId!, "command");
                    var command = plugin.Commands.FirstOrDefault(command =>
                        string.Equals(command.Id, options.ItemId, StringComparison.OrdinalIgnoreCase))
                        ?? throw new PluginArgumentException($"插件 {options.PluginId} 没有命令 {options.ItemId}。使用 --list-plugin-commands 查看命令。");
                    await session.Runtime.InvokeAsync(plugin, $"command:{command.Id}", token =>
                        plugin.CommandPlugin!.ExecuteAsync(command.Id, session.Runtime.GetContext(plugin), token),
                        cancellation.Token).ConfigureAwait(false);
                    // Finish cancellation and lifecycle cleanup before taking the final output snapshot.
                    await session.ShutdownAsync().ConfigureAwait(false);
                    await WriteAtomicallyAsync(options.Output!, options.Overwrite, async stream =>
                    {
                        await using var writer = new StreamWriter(stream, Utf8, 64 * 1024, leaveOpen: true);
                        await writer.WriteAsync(session.WordList.Text.AsMemory(), cancellation.Token).ConfigureAwait(false);
                        await writer.FlushAsync(cancellation.Token).ConfigureAwait(false);
                    }, cancellation.Token).ConfigureAwait(false);
                    Console.WriteLine($"命令 {plugin.Manifest!.Id}/{command.Id} 已完成：{options.Output}");
                    break;
                }
                case "--list-plugin-voices":
                {
                    var providers = new List<object>();
                    foreach (var plugin in session.Catalog.Entries.Where(entry => entry.VoiceProvider is not null))
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        IReadOnlyList<PluginVoiceInfo> voices = [];
                        var error = plugin.Error;
                        if (plugin.IsLoaded)
                        {
                            try { voices = await session.GetVoicesAsync(plugin, cancellation.Token).ConfigureAwait(false); }
                            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                            catch (Exception ex) { error = ex.Message; }
                        }
                        providers.Add(new { PluginId = plugin.Manifest!.Id, plugin.DisplayName, Voices = voices, Error = error });
                    }
                    PrintJson(new { Providers = providers, UnavailablePlugins = GetUnavailablePlugins(session) });
                    break;
                }
                case "--synthesize-plugin":
                {
                    var audio = await session.SynthesizeAsync(options.Text!, options.Rate, cancellation.Token)
                        .ConfigureAwait(false);
                    await WriteAtomicallyAsync(options.Output!, options.Overwrite,
                        stream => PluginAudioBridge.WriteWaveAsync(audio, stream, cancellation.Token),
                        cancellation.Token).ConfigureAwait(false);
                    Console.WriteLine($"WAV 已保存：{options.Output}");
                    break;
                }
            }
            exitCode = 0;
        }
        catch (PluginArgumentException ex)
        {
            Console.Error.WriteLine($"参数错误：{ex.Message}");
            Console.Error.WriteLine("使用 --help 查看命令格式。");
            exitCode = 2;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("操作已取消。");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"插件操作失败：{ex.Message}");
        }
        finally
        {
            if (session is not null)
            {
                try { await session.ShutdownAsync().ConfigureAwait(false); }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"插件关闭失败：{ex.Message}");
                    exitCode = 1;
                }
            }
            Console.CancelKeyPress -= cancelHandler;
        }
        return exitCode;
    }

    private static object[] GetUnavailablePlugins(HeadlessPluginSession session) => session.Catalog.Entries
        .Where(entry => !entry.IsLoaded).Select(entry => (object)new
        {
            Id = entry.Manifest?.Id, entry.DisplayName, entry.IsEnabled, entry.Error
        }).ToArray();

    private static void PrintJson(object value) => Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private static CommandArguments ParseArguments(string[] args)
    {
        if (!Handles(args)) throw new PluginArgumentException("未知的插件命令。");
        var options = new CommandArguments { Mode = args[0] };
        if (options.Mode is "--list-plugin-commands" or "--list-plugin-voices")
        {
            if (args.Length != 1) throw new PluginArgumentException($"{options.Mode} 不接受其他参数。");
            return options;
        }
        if (args.Length < 3 || args.Skip(1).Take(2).Any(value =>
                string.IsNullOrWhiteSpace(value) || value.StartsWith("--", StringComparison.Ordinal)))
            throw new PluginArgumentException($"{options.Mode} 需要插件 ID 和命令/语音 ID。");
        options.PluginId = args[1];
        options.ItemId = args[2];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 3; i < args.Length; i++)
        {
            var option = args[i];
            if (!seen.Add(option)) throw new PluginArgumentException($"重复的选项：{option}");
            string ReadValue()
            {
                if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new PluginArgumentException($"{option} 缺少值。");
                return args[i];
            }
            switch (option)
            {
                case "--input" when options.Mode == "--run-plugin-command": options.Input = ReadValue(); break;
                case "--text" when options.Mode == "--synthesize-plugin": options.Text = ReadValue(); break;
                case "--rate" when options.Mode == "--synthesize-plugin":
                    if (!int.TryParse(ReadValue(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rate) || rate is < -10 or > 10)
                        throw new PluginArgumentException("--rate 必须为 -10 至 10 的整数。");
                    options.Rate = rate;
                    break;
                case "--output": options.Output = ReadValue(); break;
                case "--overwrite": options.Overwrite = true; break;
                default: throw new PluginArgumentException($"未知或不适用于当前命令的选项：{option}");
            }
        }
        if (options.Output is null || options.Mode == "--run-plugin-command" && options.Input is null ||
            options.Mode == "--synthesize-plugin" && options.Text is null)
            throw new PluginArgumentException("必须指定 --output，并为词表命令提供 --input，或为合成提供 --text。");
        if (options.Text?.Length > HeadlessPluginSession.MaximumSpeechCharacters)
            throw new PluginArgumentException($"--text 不能超过 {HeadlessPluginSession.MaximumSpeechCharacters} 个字符。");
        return options;
    }

    private static void ValidatePaths(CommandArguments arguments)
    {
        if (arguments.Output is null) return;
        arguments.Output = NormalizePath(arguments.Output);
        var extension = arguments.Mode == "--run-plugin-command" ? ".txt" : ".wav";
        if (!Path.GetExtension(arguments.Output).Equals(extension, StringComparison.OrdinalIgnoreCase))
            throw new PluginArgumentException($"--output 必须使用 {extension} 扩展名。");
        var fileName = Path.GetFileName(arguments.Output);
        if (fileName.EndsWith('.') || fileName.EndsWith(' ') ||
            fileName.Any(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character)))
            throw new PluginArgumentException("输出文件名包含 Windows 不允许的字符。");
        if (!Directory.Exists(Path.GetDirectoryName(arguments.Output)))
            throw new PluginArgumentException("输出文件夹不存在，请先创建。");
        if (Directory.Exists(arguments.Output)) throw new PluginArgumentException("输出路径不能是文件夹。");
        if (File.Exists(arguments.Output) && !arguments.Overwrite)
            throw new PluginArgumentException("输出文件已经存在；请使用新文件名或明确指定 --overwrite。");
        if (arguments.Input is not null)
        {
            arguments.Input = NormalizePath(arguments.Input);
            if (!Path.GetExtension(arguments.Input).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                throw new PluginArgumentException("--input 必须是纯文本 .txt 词表。");
            if (!File.Exists(arguments.Input)) throw new PluginArgumentException($"找不到输入文件：{arguments.Input}");
            if (string.Equals(ResolveLinks(arguments.Input), ResolveLinks(arguments.Output), StringComparison.OrdinalIgnoreCase))
                throw new PluginArgumentException("输入和输出必须是不同的文件路径。");
        }
    }

    private static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new PluginArgumentException($"文件路径无效：{ex.Message}");
        }
    }

    private static string ResolveLinks(string path)
    {
        var current = Path.GetPathRoot(path)!;
        foreach (var part in path[current.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.Exists && info.ResolveLinkTarget(returnFinalTarget: true) is { } target) current = target.FullName;
        }
        return Path.GetFullPath(current);
    }

    private static async Task<string> ReadWordListAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > DocumentImportService.MaximumInputBytes)
            throw new InvalidDataException("词表超过 64 MiB，请先拆分。");
        using var reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: true, 64 * 1024, leaveOpen: true);
        var text = new StringBuilder();
        var buffer = new char[16 * 1024];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            if (text.Length + count > HeadlessPluginSession.MaximumWordListCharacters)
                throw new InvalidDataException("词表超过 1,600 万字符，请先拆分。");
            if (buffer.AsSpan(0, count).Contains('\0')) throw new InvalidDataException("词表含有二进制空字符，请使用纯文本文件。");
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }

    private static PluginEntry SelectPlugin(HeadlessPluginSession session, string id, string capability)
    {
        var plugin = session.Catalog.Entries.FirstOrDefault(entry =>
            string.Equals(entry.Manifest?.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new PluginArgumentException($"没有找到插件 {id}；只加载程序旁 Plugins 目录内的已安装插件。");
        if (!plugin.IsLoaded) throw new InvalidOperationException($"插件 {id} 不可用：{plugin.Error ?? plugin.StatusText}");
        if (capability == "command" && plugin.CommandPlugin is null || capability == "voice" && plugin.VoiceProvider is null)
            throw new PluginArgumentException($"插件 {id} 不提供 {capability} 能力。");
        return plugin;
    }

    internal static async Task WriteAtomicallyAsync(string destination, bool overwrite,
        Func<Stream, Task> writeAsync, CancellationToken cancellationToken)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".dictation-plugin-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             64 * 1024, FileOptions.Asynchronous))
            {
                await writeAsync(stream).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // A complete file is published in one rename; existing leaf symlinks are never opened for writing.
            File.Move(temporary, destination, overwrite);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"未能移除临时文件 {temporary}：{ex.Message}");
            }
        }
    }

    internal static void PrintHelp() => Console.WriteLine("""

        无界面扩展命令（只加载程序旁 Plugins）：
        --list-plugin-commands
        --run-plugin-command PLUGIN COMMAND --input INPUT.txt --output OUTPUT.txt [--overwrite]
        --list-plugin-voices
        --synthesize-plugin PLUGIN VOICE --text "TEXT" --output OUTPUT.wav [--rate -10..10] [--overwrite]

        词表命令使用完整输入词表，输出最终词表；输入与输出必须不同。
        合成使用指定插件语音，并依次应用已启用的音频效果，保存 PCM WAV。
        默认不覆盖文件；输出文件夹需已存在。Ctrl+C 合作取消并等待插件清理。
        无界面模式不提供窗口、消息框或设备播放；插件可处理词表、合成与导出音频。
        """);

    private sealed class CommandArguments
    {
        public required string Mode { get; init; }
        public string? PluginId { get; set; }
        public string? ItemId { get; set; }
        public string? Input { get; set; }
        public string? Output { get; set; }
        public string? Text { get; set; }
        public int Rate { get; set; }
        public bool Overwrite { get; set; }
    }

    internal sealed class PluginArgumentException(string message) : Exception(message);
}

/// <summary>Shared lifecycle and host services for extraction and extension commands.</summary>
internal sealed class HeadlessPluginSession
{
    internal const int MaximumWordListCharacters = 16_000_000;
    internal const int MaximumSpeechCharacters = 100_000;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime;
    private readonly CancellationToken _lifetimeToken;
    private PluginWordListSnapshot _wordList;
    private PluginEntry? _voiceProvider;
    private string? _voiceId;
    private Task? _shutdown;

    public HeadlessPluginSession(CancellationToken lifetime, string text = "")
    {
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        _lifetimeToken = _lifetime.Token;
        _wordList = new PluginWordListSnapshot(text, string.Empty, 0);
        Catalog = new PluginCatalog(Path.Combine(AppContext.BaseDirectory, "Plugins"));
        Runtime = new PluginRuntime(Catalog, entry => new HeadlessPluginContext(this, entry), _lifetimeToken);
    }

    public PluginCatalog Catalog { get; }
    public PluginRuntime Runtime { get; }
    public PluginWordListSnapshot WordList { get { lock (_gate) return _wordList; } }

    public void SetVoice(PluginEntry provider, string voiceId)
    {
        _voiceProvider = provider;
        _voiceId = voiceId;
    }

    public async Task<IReadOnlyList<PluginVoiceInfo>> GetVoicesAsync(PluginEntry provider, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        cancellationToken = linked.Token;
        var voices = await Runtime.InvokeAsync(provider, "list-voices",
            token => provider.VoiceProvider!.GetVoicesAsync(token), cancellationToken, waitForTurn: false).ConfigureAwait(false);
        if (voices is null || voices.Count > 4096) throw new InvalidDataException("语音插件返回了无效或超过 4096 条的语音列表。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var snapshot = new List<PluginVoiceInfo>(voices.Count);
        foreach (var voice in voices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (voice is null || !ValidLabel(voice.Id, 200) || !ValidLabel(voice.Name, 200) ||
                voice.Locale?.Length > 100 || voice.Locale?.Any(char.IsControl) == true || !ids.Add(voice.Id))
                throw new InvalidDataException("语音插件返回了无效或重复的语音标识。");
            snapshot.Add(voice);
        }
        return snapshot.AsReadOnly();
    }

    public async Task<PluginAudioData> SynthesizeAsync(string text, int rate, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        cancellationToken = linked.Token;
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumSpeechCharacters || text.Contains('\0') || rate is < -10 or > 10)
            throw new ArgumentException("合成文本必须非空且不超过 100,000 字符，语速为 -10 至 10。");
        // Never await InitializeAsync here: this service can be called from a lifecycle initializer.
        var provider = _voiceProvider ?? Catalog.Entries.Where(entry => entry.IsLoaded && entry.VoiceProvider is not null)
            .OrderBy(entry => entry.Manifest!.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault()
            ?? throw new InvalidOperationException("没有可用的语音插件，请先安装语音插件并使用 --list-plugin-voices 核对。");
        var voices = await GetVoicesAsync(provider, cancellationToken).ConfigureAwait(false);
        var voice = _voiceId is null ? voices.FirstOrDefault()
            : voices.FirstOrDefault(candidate => string.Equals(candidate.Id, _voiceId, StringComparison.Ordinal));
        if (voice is null) throw new PluginCommandLine.PluginArgumentException($"插件 {provider.Manifest!.Id} 不提供语音 {_voiceId ?? "（默认）"}。");
        var audio = await Runtime.InvokeAsync(provider, "synthesize-audio", token => provider.VoiceProvider!
            .SynthesizeAsync(voice.Id, text, new PluginSpeechOptions(rate), token), cancellationToken, waitForTurn: false).ConfigureAwait(false);
        return await PluginAudioBridge.ProcessAsync(Runtime, audio, cancellationToken).ConfigureAwait(false);
    }

    private void UpdateWordList(PluginWordListUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        _lifetimeToken.ThrowIfCancellationRequested();
        cancellationToken.ThrowIfCancellationRequested();
        if (update.Text is null || update.Text.Length > MaximumWordListCharacters || update.Text.Contains('\0'))
            throw new InvalidDataException("更新词表必须为纯文本且不能超过 1,600 万字符。");
        PluginWordListSnapshot snapshot;
        lock (_gate)
        {
            _lifetimeToken.ThrowIfCancellationRequested();
            cancellationToken.ThrowIfCancellationRequested();
            if (update.ExpectedRevision is { } expected && expected != _wordList.Revision)
                throw new InvalidOperationException("词表已更改，请重新读取快照后再更新。");
            var text = update.Append && _wordList.Text.Length > 0 && update.Text.Length > 0
                ? _wordList.Text + (_wordList.Text.EndsWith('\n') ? string.Empty : Environment.NewLine) + update.Text
                : update.Append && update.Text.Length == 0 ? _wordList.Text : update.Text;
            if (text.Length > MaximumWordListCharacters) throw new InvalidDataException("更新词表超过 1,600 万字符。");
            if (text == _wordList.Text) return;
            snapshot = _wordList = new PluginWordListSnapshot(text, string.Empty, checked(_wordList.Revision + 1));
        }
        Runtime.NotifyWordListChanged(snapshot);
    }

    public Task ShutdownAsync()
    {
        lock (_gate) return _shutdown ??= Task.Run(ShutdownCoreAsync);
    }

    private async Task ShutdownCoreAsync()
    {
        try
        {
            try { _lifetime.Cancel(); }
            catch (Exception ex) { Console.Error.WriteLine($"插件取消通知发生错误：{ex.Message}"); }
            await Runtime.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _lifetime.Dispose();
            foreach (var item in Runtime.Logs)
                Console.Error.WriteLine($"[{item.PluginId}] {item.Level}: {item.Message}");
        }
    }

    private static bool ValidLabel(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);

    private sealed class HeadlessPluginContext(HeadlessPluginSession session, PluginEntry entry) : IPluginContext
    {
        public string PluginId => entry.Manifest!.Id;
        public string HostVersion => typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
        public string DataDirectory
        {
            get
            {
                session._lifetimeToken.ThrowIfCancellationRequested();
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
                        Environment.SpecialFolderOption.DoNotVerify),
                    "DictationAssistant", "Plugins", PluginId, "Data");
                Directory.CreateDirectory(directory);
                return directory;
            }
        }
        public object? OwnerWindow => null;
        public Task<PluginWordListSnapshot> GetWordListAsync(CancellationToken cancellationToken)
        {
            session._lifetimeToken.ThrowIfCancellationRequested();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(session.WordList);
        }
        public Task UpdateWordListAsync(PluginWordListUpdate update, CancellationToken cancellationToken)
        {
            session.UpdateWordList(update, cancellationToken);
            return Task.CompletedTask;
        }
        public Task InvokeOnUiAsync(Func<Task> action, CancellationToken cancellationToken) => Unsupported("UI 调用", cancellationToken);
        public Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken) => Unsupported("消息窗口", cancellationToken);
        public Task SpeakAsync(string text, CancellationToken cancellationToken) => Unsupported("设备播报；请使用 SynthesizeAsync 和 ExportWaveAsync", cancellationToken);
        public Task PlayAudioAsync(PluginAudioData audio, CancellationToken cancellationToken) => Unsupported("音频设备播放", cancellationToken);
        public Task<PluginAudioData> SynthesizeAsync(string text, int rate, CancellationToken cancellationToken) =>
            session.SynthesizeAsync(text, rate, cancellationToken);
        public async Task<PluginAudioData> ProcessAudioAsync(PluginAudioData audio, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session._lifetimeToken);
            return await PluginAudioBridge.ProcessAsync(session.Runtime, audio, linked.Token).ConfigureAwait(false);
        }
        public async Task ExportWaveAsync(PluginAudioData audio, Stream destination, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session._lifetimeToken);
            await PluginAudioBridge.WriteWaveAsync(audio, destination, linked.Token).ConfigureAwait(false);
        }
        public void Log(PluginLogLevel level, string message) => session.Runtime.Log(entry, level, message);
        private Task Unsupported(string operation, CancellationToken cancellationToken)
        {
            session._lifetimeToken.ThrowIfCancellationRequested();
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotSupportedException($"无界面命令行宿主不支持{operation}。");
        }
    }
}
