using System.Text.Json;
using MacSongManager.Core.Library;

namespace MacSongManager.Core.Storage;

public sealed class TagOverride
{
    public string Artist { get; set; } = "";
    public string Title { get; set; } = "";
    public string Source { get; set; } = "user";
    public string? ManifestArtist { get; set; }
    public string? ManifestTitle { get; set; }
    public string? Album { get; set; }
    public int? Year { get; set; }
    public string? Arrangements { get; set; }
    public string? Tuning { get; set; }
    public string? DlcKey { get; set; }
    public bool ManifestCached { get; set; }
}

public sealed class FileStamp
{
    public long Length { get; set; }
    public long LastWriteUtcTicks { get; set; }
    public string Hash { get; set; } = "";
}

public sealed class TagFile
{
    public Dictionary<string, TagOverride> Songs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, FileStamp> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> Setlists { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TagStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly object _gate = new();
    private TagFile _file;
    private bool _dirty;

    public TagStore(string path)
    {
        _path = path;
        _file = Load();
    }

    public TagOverride? Find(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return null;
        lock (_gate)
            return _file.Songs.TryGetValue(hash, out var tag) ? tag : null;
    }

    public void Save(string hash, string artist, string title, string source)
    {
        lock (_gate)
        {
            if (!_file.Songs.TryGetValue(hash, out var tag))
                tag = new TagOverride();
            tag.Artist = artist;
            tag.Title = title;
            tag.Source = source;
            _file.Songs[hash] = tag;
            Persist();
            _dirty = false;
        }
    }

    public void RememberManifest(string hash, PackageManifest manifest)
    {
        lock (_gate)
        {
            if (!_file.Songs.TryGetValue(hash, out var tag))
                tag = new TagOverride { Source = "" };
            tag.ManifestArtist = manifest.Artist;
            tag.ManifestTitle = manifest.Title;
            tag.Album = manifest.Album;
            tag.Year = manifest.Year;
            tag.Arrangements = manifest.Arrangements;
            tag.Tuning = manifest.Tuning;
            tag.DlcKey = manifest.DlcKey;
            tag.ManifestCached = true;
            _file.Songs[hash] = tag;
            _dirty = true;
        }
    }

    public FileStamp? FindStamp(string relativePath)
    {
        lock (_gate)
            return _file.Files.TryGetValue(Normalize(relativePath), out var stamp) ? stamp : null;
    }

    public void RememberStamp(string relativePath, long length, long lastWriteUtcTicks, string hash)
    {
        lock (_gate)
        {
            _file.Files[Normalize(relativePath)] = new FileStamp
            {
                Length = length,
                LastWriteUtcTicks = lastWriteUtcTicks,
                Hash = hash
            };
            _dirty = true;
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            if (!_dirty)
                return;
            Persist();
            _dirty = false;
        }
    }

    public IReadOnlyList<string> SetlistNames()
    {
        lock (_gate)
            return _file.Setlists.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<string> Setlist(string name)
    {
        lock (_gate)
            return _file.Setlists.TryGetValue(name, out var hashes) ? hashes.ToList() : [];
    }

    public void SaveSetlist(string name, IEnumerable<string> hashes)
    {
        lock (_gate)
        {
            _file.Setlists[name.Trim()] = hashes.Where(hash => !string.IsNullOrWhiteSpace(hash)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Persist();
        }
    }

    private TagFile Load()
    {
        if (!File.Exists(_path))
            return new TagFile();
        try
        {
            return JsonSerializer.Deserialize<TagFile>(File.ReadAllText(_path)) ?? new TagFile();
        }
        catch (JsonException)
        {
            return new TagFile();
        }
    }

    private static string Normalize(string relativePath) =>
        relativePath.Replace('\\', '/');

    private void Persist()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(_path, JsonSerializer.Serialize(_file, JsonOptions));
    }
}
