using System.Text.Json;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Globalization;

namespace CodexBeacon;

public static class Localization
{
    public const string SystemLanguage = "system";
    private static ResourceLoader? _loader;

    public static string CurrentLanguage { get; private set; } = SystemLanguage;

    public static void Initialize()
    {
        CurrentLanguage = ReadSavedLanguage();
        ApplicationLanguages.PrimaryLanguageOverride = CurrentLanguage == SystemLanguage ? "" : CurrentLanguage;
        _loader = new ResourceLoader();
    }

    public static void ApplyLanguage(string language)
    {
        CurrentLanguage = language is "en-US" or "zh-CN" ? language : SystemLanguage;
        ApplicationLanguages.PrimaryLanguageOverride = CurrentLanguage == SystemLanguage ? "" : CurrentLanguage;
        _loader = new ResourceLoader();
    }

    public static string Get(string key)
    {
        try
        {
            var value = (_loader ??= new ResourceLoader()).GetString(key);
            return string.IsNullOrWhiteSpace(value) ? key : value;
        }
        catch
        {
            return key;
        }
    }

    public static string Format(string key, params object[] args) => string.Format(Get(key), args);

    private static string ReadSavedLanguage()
    {
        try
        {
            var path = SystemService.SettingsPath;
            if (!File.Exists(path)) return SystemLanguage;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("Language", out var value))
            {
                var language = value.GetString();
                if (language is "en-US" or "zh-CN") return language;
            }
        }
        catch { }
        return SystemLanguage;
    }
}
