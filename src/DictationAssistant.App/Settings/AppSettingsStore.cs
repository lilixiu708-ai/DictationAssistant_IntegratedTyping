namespace DictationAssistant.App.Settings;

/// <summary>
/// Application settings store backed by a JSON file in the user's AppData directory.
/// </summary>
public sealed class AppSettingsStore : ReactiveStoreOnFile<AppSettings>
{
    public AppSettingsStore() : base(GetSettingsFilePath())
    {
    }

    private static string GetSettingsFilePath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        return Path.Combine(appDataPath, "DictationAssistant", "settings.json");
    }
}
