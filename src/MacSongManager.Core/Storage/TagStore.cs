using System.Text.Json;

namespace MacSongManager.Core.Storage;

public sealed class TagOverride
{
    public string Artist { get; set; } = "";
    public string Title { get; set; } = "";
    public string Source { get; set; } = "user";
}

public sealed class TagFile
{
    public Dictionary<string, TagOverride> Songs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> Setlists { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TagStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly object _gate = new();
    private TagFile _file;

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
            _file.Songs[hash] = new TagOverride { Artist = artist, Title = title, Source = source };
            Persist();
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

    private void Persist()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(_path, JsonSerializer.Serialize(_file, JsonOptions));
    }
}
