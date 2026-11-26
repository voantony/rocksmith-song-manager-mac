namespace MacSongManager.Core.Songs;

public enum DuplicateReason
{
    ArtistTitle,
    ContentHash
}

public sealed record DuplicateGroup(
    DuplicateReason Reason,
    string Key,
    IReadOnlyList<string> Paths);

public sealed record SongIdentity(string Path, string? Artist, string? Title, string? ContentHash);

public static class DuplicateDetector
{
    public static IReadOnlyList<DuplicateGroup> Find(IEnumerable<SongIdentity> songs)
    {
        var list = songs.ToList();
        var groups = new List<DuplicateGroup>();
        groups.AddRange(Group(list, DuplicateReason.ArtistTitle, static song =>
        {
            var key = TextNormalizer.ArtistTitleKey(song.Artist, song.Title);
            return key.Length == 0 ? null : key;
        }));
        groups.AddRange(Group(list, DuplicateReason.ContentHash, static song =>
            string.IsNullOrWhiteSpace(song.ContentHash) ? null : song.ContentHash.Trim().ToLowerInvariant()));
        return groups;
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<DuplicateReason>> ReasonsByPath(
        IEnumerable<SongIdentity> songs)
    {
        var map = new Dictionary<string, List<DuplicateReason>>(StringComparer.Ordinal);
        foreach (var group in Find(songs))
        {
            foreach (var path in group.Paths)
            {
                if (!map.TryGetValue(path, out var reasons))
                {
                    reasons = [];
                    map[path] = reasons;
                }

                if (!reasons.Contains(group.Reason))
                    reasons.Add(group.Reason);
            }
        }

        return map.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<DuplicateReason>)pair.Value,
            StringComparer.Ordinal);
    }

    private static IEnumerable<DuplicateGroup> Group(
        IReadOnlyList<SongIdentity> songs,
        DuplicateReason reason,
        Func<SongIdentity, string?> keySelector)
    {
        var buckets = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var song in songs)
        {
            var key = keySelector(song);
            if (key is null)
                continue;
            if (!buckets.TryGetValue(key, out var paths))
            {
                paths = [];
                buckets[key] = paths;
            }

            if (!paths.Contains(song.Path, StringComparer.Ordinal))
                paths.Add(song.Path);
        }

        foreach (var (key, paths) in buckets.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (paths.Count < 2)
                continue;
            yield return new DuplicateGroup(reason, key, paths.OrderBy(path => path, StringComparer.Ordinal).ToList());
        }
    }
}
