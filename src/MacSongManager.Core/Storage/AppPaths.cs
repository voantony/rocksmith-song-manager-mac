using System.Text.Json;

namespace MacSongManager.Core.Storage;

public sealed class LibrarySettings
{
    public string LibraryPath { get; set; } = string.Empty;

    /// <summary>
    /// PC-to-Mac conversion stays off until the user turns it on.
    /// It is for custom songs only.
    /// </summary>
    public bool ConvertPcCustomSongs { get; set; }

    /// <summary>
    /// Empty means <see cref="AppPaths.DefaultConversionKeysPath"/>.
    /// </summary>
    public string ConversionKeysPath { get; set; } = string.Empty;
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static LibrarySettings Load(string path)
    {
        if (!File.Exists(path))
            return new LibrarySettings();

        try
        {
            return JsonSerializer.Deserialize<LibrarySettings>(File.ReadAllText(path)) ?? new LibrarySettings();
        }
        catch (JsonException)
        {
            return new LibrarySettings();
        }
    }

    public static void Save(string path, LibrarySettings settings)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}

public static class AppPaths
{
    public const string DisplayName = "Mac Song Manager";

    public static string DefaultDlcFolder(string? home = null)
    {
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(
            home,
            "Library",
            "Application Support",
            "Steam",
            "steamapps",
            "common",
            "Rocksmith2014",
            "dlc");
    }

    public static string SupportFolder(string? home = null)
    {
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
            return Path.Combine(home, "Library", "Application Support", DisplayName);

        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), DisplayName);

        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
            return Path.Combine(xdg, "mac-song-manager");

        return Path.Combine(home, ".local", "share", "mac-song-manager");
    }

    public static string SettingsPath(string? home = null) =>
        Path.Combine(SupportFolder(home), "settings.json");

    public static string ChangeLogPath(string? home = null) =>
        Path.Combine(SupportFolder(home), "change-log.jsonl");

    public static string TrashFolder(string? home = null) =>
        Path.Combine(SupportFolder(home), "Trash");

    public static string TagStorePath(string? home = null) =>
        Path.Combine(SupportFolder(home), "tags.json");

    public static string CatalogBackupPath(string? home = null) =>
        Path.Combine(SupportFolder(home), "catalog-backup.json");

    public static string DefaultConversionKeysPath(string? home = null) =>
        Path.Combine(SupportFolder(home), "conversion-keys.txt");
}
