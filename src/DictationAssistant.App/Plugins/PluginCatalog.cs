// Manifest-driven sideloaded importers, 2026-10-19. AGPL-3.0-or-later.
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using DictationAssistant.Plugin.Abstractions;

namespace DictationAssistant.App.Plugins;

/// <summary>
/// Loads one importer from each immediate plugin subdirectory. Call Load on a
/// worker thread before reading the catalog. Results are cached until restart.
/// </summary>
public sealed class PluginCatalog
{
    private const long MaximumManifestBytes = 64 * 1024;
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 16
    };
    private readonly object _loadGate = new();
    private bool _loadAttempted;
    private volatile CatalogSnapshot _snapshot = new([], []);

    public PluginCatalog(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    public string RootDirectory { get; }
    public IReadOnlyList<PluginEntry> Entries => _snapshot.Entries;
    public IReadOnlyList<string> SupportedExtensions => _snapshot.SupportedExtensions;

    /// <summary>
    /// Discovers and loads plugins once. Metadata, type and dependency failures
    /// become entries with Error set instead of preventing other plugins loading.
    /// </summary>
    public void Load()
    {
        lock (_loadGate)
        {
            if (_loadAttempted)
            {
                return;
            }

            _loadAttempted = true;
            var entries = new List<PluginEntry>();
            try
            {
                if (!Directory.Exists(RootDirectory))
                {
                    entries.Add(new PluginEntry(RootDirectory)
                    {
                        Error = "未找到或无法访问插件目录。请把完整插件文件夹放入此目录后重启应用。"
                    });
                }
                else
                {
                    // Read and validate every manifest before executing code, so
                    // duplicate IDs cannot let the first matching folder win.
                    foreach (var folder in Directory.GetDirectories(RootDirectory)
                                 .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                    {
                        entries.Add(ReadManifest(folder));
                    }

                    MarkDuplicateIds(entries);
                    foreach (var entry in entries.Where(entry => entry.Error is null))
                    {
                        LoadPlugin(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PluginCatalog] Cannot read plugin directory: {ex}");
                entries.Add(new PluginEntry(RootDirectory)
                {
                    Error = $"读取插件目录失败：{GetErrorMessage(ex)}"
                });
            }

            var supportedExtensions = entries
                .Where(entry => entry.Importer is not null && entry.IsLoaded)
                .SelectMany(entry => entry.Manifest!.Extensions)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _snapshot = new CatalogSnapshot(entries.AsReadOnly(), Array.AsReadOnly(supportedExtensions));
        }
    }

    /// <summary>Returns successfully loaded importers matching the file extension.</summary>
    public IReadOnlyList<PluginEntry> GetImportersForFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return [];
        }

        var extension = Path.GetExtension(fileName);
        return _snapshot.Entries
            .Where(entry => entry.Importer is not null && entry.IsLoaded && entry.Manifest!.Extensions
                .Contains(extension, StringComparer.OrdinalIgnoreCase))
            .ToArray();
    }

    public PluginEntry? GetPracticePlugin(string pluginId) => _snapshot.Entries.FirstOrDefault(entry =>
        entry.IsLoaded && entry.PracticePlugin is not null &&
        string.Equals(entry.Manifest?.Id, pluginId, StringComparison.OrdinalIgnoreCase));

    private static PluginEntry ReadManifest(string folder)
    {
        var entry = new PluginEntry(folder);
        try
        {
            var manifestPath = Path.Combine(folder, "plugin.json");
            if (!File.Exists(manifestPath))
            {
                throw new InvalidDataException("缺少 plugin.json，请复制完整的插件文件夹。");
            }

            using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumManifestBytes)
            {
                throw new InvalidDataException("plugin.json 超过 64 KiB，请检查插件包。");
            }

            entry.Manifest = JsonSerializer.Deserialize<PluginManifest>(stream, ManifestJsonOptions)
                ?? throw new InvalidDataException("plugin.json 内容为空。");
            entry.Manifest = ValidateAndNormalizeManifest(entry.Manifest);
            entry.AssemblyPath = Path.GetFullPath(Path.Combine(folder, entry.Manifest.EntryAssembly));
            if (!File.Exists(entry.AssemblyPath))
            {
                throw new FileNotFoundException($"找不到入口程序集：{entry.Manifest.EntryAssembly}。请复制完整插件包。");
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[PluginCatalog] Invalid plugin at {folder}: {ex}");
            entry.Error = GetErrorMessage(ex);
        }

        return entry;
    }

    private static PluginManifest ValidateAndNormalizeManifest(PluginManifest manifest)
    {
        if (!IsValidId(manifest.Id))
        {
            throw new InvalidDataException("插件 id 必须为 1–100 位英文字母、数字、点、下划线或短横线，且以字母或数字开头。");
        }

        ValidateLabel(manifest.Name, "name", 200);
        ValidateLabel(manifest.Version, "version", 80);
        ValidateLabel(manifest.EntryType, "entryType", 500);
        if (manifest.ApiVersion != PluginApi.Version)
        {
            throw new InvalidDataException($"插件接口版本为 {manifest.ApiVersion}，当前应用仅支持 v{PluginApi.Version}。");
        }

        if (!PluginLoadContext.IsSimpleFileName(manifest.EntryAssembly) ||
            !manifest.EntryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("entryAssembly 必须是插件目录内的 DLL 文件名，不能包含路径。");
        }

        var kind = string.IsNullOrWhiteSpace(manifest.Kind) ? "document-importer" : manifest.Kind.Trim();
        if (kind is not ("document-importer" or "practice"))
            throw new InvalidDataException("kind 只能是 document-importer 或 practice。");
        if (kind == "document-importer" && (manifest.Extensions is null || manifest.Extensions.Length is < 1 or > 32 ||
            manifest.Extensions.Any(extension => !IsValidExtension(extension))))
            throw new InvalidDataException("导入插件 extensions 需要 1–32 个扩展名，如 .docx。");
        if (kind == "practice" && manifest.Extensions is { Length: > 0 })
            throw new InvalidDataException("练习插件不声明文件扩展名，请设置 extensions 为 []。");

        return new PluginManifest
        {
            Id = manifest.Id,
            Name = manifest.Name.Trim(),
            Version = manifest.Version.Trim(),
            Description = manifest.Description ?? string.Empty,
            Author = manifest.Author,
            ApiVersion = manifest.ApiVersion,
            Extensions = (manifest.Extensions ?? []).Select(extension => extension.ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            EntryAssembly = manifest.EntryAssembly,
            EntryType = manifest.EntryType.Trim(),
            Kind = kind
        };
    }

    private static void MarkDuplicateIds(IReadOnlyList<PluginEntry> entries)
    {
        var duplicateGroups = entries
            .Where(entry => entry.Manifest is not null && IsValidId(entry.Manifest.Id))
            .GroupBy(entry => entry.Manifest!.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1);
        foreach (var group in duplicateGroups)
        {
            foreach (var entry in group)
            {
                var duplicateMessage = $"插件 id 重复：{group.Key}。请只保留一个版本后重启应用。";
                entry.Error = entry.Error is null ? duplicateMessage : $"{entry.Error} {duplicateMessage}";
            }
        }
    }

    private static void LoadPlugin(PluginEntry entry)
    {
        try
        {
            var manifest = entry.Manifest!;
            var context = new PluginLoadContext(manifest.Id, entry.AssemblyPath!);
            entry.LoadContext = context;
            var assembly = context.LoadFromAssemblyPath(entry.AssemblyPath!);
            var type = assembly.GetType(manifest.EntryType, throwOnError: true)!;
            var expectedType = manifest.Kind == "practice" ? typeof(IPracticePlugin) : typeof(IDocumentImporter);
            if (!type.IsClass || !type.IsVisible || type.IsAbstract || type.ContainsGenericParameters ||
                !expectedType.IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) is null)
                throw new InvalidDataException($"入口类型必须是公开、非抽象的 {expectedType.Name} 实现，并有公开无参构造函数。");

            var instance = Activator.CreateInstance(type)!;
            if (manifest.Kind == "practice") entry.PracticePlugin = (IPracticePlugin)instance;
            else entry.Importer = (IDocumentImporter)instance;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[PluginCatalog] Cannot load {entry.FolderPath}: {ex}");
            entry.Importer = null;
            entry.PracticePlugin = null;
            entry.Error = GetErrorMessage(ex);
        }
    }

    private static bool IsValidId(string? value) => value is { Length: >= 1 and <= 100 } &&
        char.IsAsciiLetterOrDigit(value[0]) &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    private static bool IsValidExtension(string? value) => value is { Length: >= 2 and <= 17 } &&
        value[0] == '.' &&
        value.Skip(1).All(char.IsAsciiLetterOrDigit);

    private static void ValidateLabel(string? value, string fieldName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value.Any(char.IsControl))
        {
            throw new InvalidDataException($"plugin.json 的 {fieldName} 不能为空、含控制字符或超过 {maximumLength} 个字符。");
        }
    }

    private static string GetErrorMessage(Exception exception)
    {
        var actual = exception is TargetInvocationException { InnerException: not null } invocation
            ? invocation.InnerException!
            : exception;
        return string.IsNullOrWhiteSpace(actual.Message) ? actual.GetType().Name : actual.Message;
    }

    private sealed record CatalogSnapshot(
        IReadOnlyList<PluginEntry> Entries,
        IReadOnlyList<string> SupportedExtensions);
}
