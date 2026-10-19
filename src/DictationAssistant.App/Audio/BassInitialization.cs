using ManagedBass;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DictationAssistant.App.Audio;

public static class BassInitialization
{
    [DllImport("bass", EntryPoint = "BASS_GetConfigPtr")]
    private static extern nint BASS_GetConfigPtr(int Option);

    private static bool _initialized;
    private static readonly object _initLock = new();

    public static void EnsureInitialized()
    {
        lock (_initLock)
        {
            if (!_initialized)
            {
                if (!Bass.Init(0))
                {
                    throw new InvalidOperationException("Failed to initialize BASS audio decoder");
                }
                LoadPlugins();
                _initialized = true;
            }
        }
    }

    private static string GetBassLibraryPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // On Windows, the BASS_UNICODE flag can be used to get a UTF-16 string ("wchar_t" rather than "char").
            nint configPtr = BASS_GetConfigPtr(75 /* BASS_CONFIG_FILENAME */ | unchecked((int)0x80000000) /* BASS_UNICODE */);
            if (configPtr == nint.Zero)
            {
                throw new InvalidOperationException("Failed to retrieve BASS library path: " + Bass.LastError);
            }
            return Marshal.PtrToStringUni(configPtr) ?? throw new InvalidOperationException("Failed to retrieve BASS library path: null string");
        }
        else
        {
            nint configPtr = BASS_GetConfigPtr(75 /* BASS_CONFIG_FILENAME */);
            if (configPtr == nint.Zero)
            {
                throw new InvalidOperationException("Failed to retrieve BASS library path: " + Bass.LastError);
            }
            return Marshal.PtrToStringAnsi(configPtr) ?? throw new InvalidOperationException("Failed to retrieve BASS library path: null string");
        }
    }

    private static void LoadPlugins()
    {
        try
        {
            var bassPath = GetBassLibraryPath();
            Trace.WriteLine($"[BassAudioDecoder] BASS library path: {bassPath}");

            var baseDir = Path.GetDirectoryName(bassPath);
            if (baseDir == null)
            {
                Trace.WriteLine("[BassAudioDecoder] Failed to determine BASS library directory");
                return;
            }

            // Read text file `bass_plugin` to fetch plugin list
            var pluginListPath = Path.Combine(baseDir, "bass_plugin");
            if (!File.Exists(pluginListPath))
            {
                Trace.WriteLine($"[BassAudioDecoder] Plugin list file not found: {pluginListPath}");
                return;
            }
            var pluginNames = File.ReadAllLines(pluginListPath)
                                  .Select(line => line.Trim())
                                  .Where(line => !string.IsNullOrEmpty(line) && !line.StartsWith("#"))
                                  .ToList();

            foreach (var pluginName in pluginNames)
            {
                var pluginPath = Path.Combine(baseDir, pluginName);
                if (!File.Exists(pluginPath))
                {
                    Trace.WriteLine($"[BassAudioDecoder] Plugin file not found: {pluginPath}");
                    continue;
                }
                var handle = Bass.PluginLoad(pluginPath);
                if (handle != 0)
                {
                    Trace.WriteLine($"[BassAudioDecoder] Loaded plugin: {pluginName} (Handle: {handle})");
                }
                else
                {
                    Trace.WriteLine($"[BassAudioDecoder] Failed to load plugin: {pluginName} ({Bass.LastError})");
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[BassAudioDecoder] Plugin loading error: {ex.Message}");
        }
    }
}
