using System.Diagnostics;
using System.Text.Json;

namespace DictationAssistant.App.Settings;

/// <summary>
/// A <see cref="ReactiveStore{T}"/> backed by a JSON file.
/// Loads on construction, auto-saves after every <see cref="ReactiveStore{T}.Update"/>.
/// </summary>
public class ReactiveStoreOnFile<T> : ReactiveStore<T> where T : notnull, new()
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;

    public ReactiveStoreOnFile(string filePath) : base(Load(filePath))
    {
        _filePath = filePath;
        Changed += (_, _) => Save();
    }

    private static T Load(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new T();
            }

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new T();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to load from '{filePath}': {ex}");
            return new T();
        }
    }

    private void Save()
    {
        try
        {
            var directoryPath = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            var json = JsonSerializer.Serialize(Value, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to save to '{_filePath}': {ex}");
        }
    }
}
