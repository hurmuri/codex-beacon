using System.Reflection;
using System.Text.Json;
using Microsoft.Windows.ApplicationModel.Resources;

namespace CodexBeacon;

/// <summary>
/// Single entry point for every user-visible string. Nothing outside this class
/// should build a sentence, and no literal UI text belongs in XAML or in the
/// PowerShell scripts.
///
/// Resolution goes through the WinAppSDK MRT Core resource map rather than
/// <c>ResourceLoader</c>: this version of the projection only exposes
/// <c>ResourceLoader()</c>, <c>ResourceLoader(string)</c> and
/// <c>ResourceLoader(string, string)</c>, so an explicit language cannot be
/// passed through a loader. The resource map, in contrast, accepts a
/// <see cref="ResourceContext"/> per lookup, which is what makes runtime
/// language switching deterministic.
/// </summary>
public static class Localization
{
    public const string SystemLanguage = "system";

    /// <summary>Subtree name derived from the resw file name (Resources.resw).</summary>
    private const string ResourceSubtree = "Resources";

    private const char ArgumentSeparator = '\u001F';

    private static readonly string[] SupportedLanguages = ["en-US", "zh-CN"];

    private static ResourceContext? _context;
    private static ResourceMap? _map;

    public static string CurrentLanguage { get; private set; } = SystemLanguage;

    public static void Initialize()
    {
        CurrentLanguage = ReadSavedLanguage();
        ApplyCulture(CurrentLanguage);
        Reload();
    }

    public static void ApplyLanguage(string language)
    {
        CurrentLanguage = IsSupported(language) ? language : SystemLanguage;
        ApplyCulture(CurrentLanguage);
        Reload();
    }

    public static string Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        var value = Lookup(key);
        return string.IsNullOrWhiteSpace(value) ? key : value;
    }

    public static string Format(string key, params object[] args)
    {
        var template = Get(key);
        try { return string.Format(template, args); }
        catch (FormatException) { return template; }
    }

    /// <summary>
    /// Resolves a string that is normally bound to a control through <c>x:Uid</c>.
    /// A dotted resw name such as <c>Foo.Content</c> is compiled into the resource
    /// hierarchy <c>Resources/Foo/Content</c>, not into one flat key, so the flat
    /// <see cref="Get"/> path can never reach it. Code that needs the same value
    /// the XAML already renders - for example a label whose text switches at
    /// runtime - must come through here so the string stays single-sourced.
    /// </summary>
    public static string GetXUid(string uid, string property)
    {
        if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(property)) return string.Empty;
        var value = Lookup($"{uid}/{property}");
        return string.IsNullOrWhiteSpace(value) ? uid : value;
    }

    /// <summary>
    /// Renders a resource key plus the argument payload produced by the status
    /// collector. Arguments prefixed with '@' are themselves resource keys.
    /// </summary>
    public static string Compose(string key, string arguments)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        var values = SplitArguments(arguments);
        if (values.Length == 0) return Get(key);
        var resolved = new object[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            var value = values[index];
            resolved[index] = value.StartsWith('@') ? Get(value[1..]) : value;
        }
        return Format(key, resolved);
    }

    /// <summary>Renders a bare list of raw values, joining them for display.</summary>
    public static string Join(IEnumerable<string> values, string separator = " · ")
        => string.Join(separator, values.Where(v => !string.IsNullOrWhiteSpace(v)));

    private static bool IsSupported(string? language)
        => language is not null && Array.IndexOf(SupportedLanguages, language) >= 0;

    private static string[] SplitArguments(string arguments)
        => string.IsNullOrEmpty(arguments)
            ? []
            : arguments.Split(ArgumentSeparator, StringSplitOptions.None);

    private static string? Lookup(string key)
    {
        try
        {
            var map = _map ??= CreateManager()?.MainResourceMap;
            if (map is null) return null;

            var path = $"{ResourceSubtree}/{key}";
            var candidate = _context is null
                ? map.TryGetValue(path)
                : map.TryGetValue(path, _context);
            return candidate?.ValueAsString;
        }
        catch
        {
            return null;
        }
    }

    private static void Reload()
    {
        _context = null;
        _map = null;
        try
        {
            var manager = CreateManager();
            if (manager is null) return;

            var context = manager.CreateResourceContext();
            if (IsSupported(CurrentLanguage))
            {
                // The context inherits the application language already, but the
                // explicit qualifier keeps code-resolved strings and x:Uid strings
                // in agreement even when the language override is not propagated.
                try { context.QualifierValues["language"] = CurrentLanguage; } catch { }
            }

            _context = context;
            _map = manager.MainResourceMap;
        }
        catch
        {
            _context = null;
            _map = null;
        }
    }

    /// <summary>
    /// Loads the compiled resource index. Publishing renames the project pri to
    /// <c>resources.pri</c>, while a local bin run keeps the assembly name, so both
    /// spellings are probed before falling back to the framework default.
    /// </summary>
    private static ResourceManager? CreateManager()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var assemblyName = Assembly.GetEntryAssembly()?.GetName().Name;
        var names = new List<string> { "resources.pri" };
        if (!string.IsNullOrEmpty(assemblyName)) names.Add($"{assemblyName}.pri");

        foreach (var name in names)
        {
            if (!File.Exists(Path.Combine(baseDirectory, name))) continue;
            try { return new ResourceManager(Path.Combine(baseDirectory, name)); } catch { }
            try { return new ResourceManager(name); } catch { }
        }

        try { return new ResourceManager(); } catch { return null; }
    }

    private static void ApplyCulture(string language)
    {
        var overrideValue = IsSupported(language) ? language : string.Empty;

        // The WinAppSDK surface works without package identity and is what MRT
        // Core - and therefore XAML x:Uid resolution - actually honours for an
        // unpackaged app. Verified at runtime: after setting it, the default
        // resource context resolves the requested language.
        try { Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = overrideValue; }
        catch { }

        // The OS-level API only drives packaged builds; it throws when the process
        // has no package identity, so it stays a secondary path.
        try { Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = overrideValue; }
        catch { }
    }

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
                if (IsSupported(language)) return language!;
            }
        }
        catch { }
        return SystemLanguage;
    }
}
