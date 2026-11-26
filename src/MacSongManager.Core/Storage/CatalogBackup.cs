using System.Text.Json;

namespace MacSongManager.Core.Storage;

public sealed class CatalogEntry
{
    public string RelativePath { get; set; } = "";
    public string? ContentHash { get; set; }
    public string FileName { get; set; } = "";
}

public sealed class CatalogSnapshot
{
    public DateTimeOffset CreatedAt { get; set; }
    public string LibraryPath { get; set; } = "";
    public List<CatalogEntry> Files { get; set; } = [];
}

public static class CatalogBackup
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Write(string path, CatalogSnapshot snapshot)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    public static CatalogSnapshot? Read(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<CatalogSnapshot>(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
