namespace MacSongManager.Core.Songs;

/// <summary>
/// Public short file names for official songs. These are display names only.
/// Nothing is read from a game archive.
/// </summary>
public static class OfficialSongNames
{
    private static readonly Dictionary<string, (string Artist, string Title)> Map =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["1979"] = ("The Smashing Pumpkins", "1979"),
            ["babaoriley"] = ("The Who", "Baba O'Riley"),
            ["paintitblack"] = ("The Rolling Stones", "Paint It, Black")
        };

    public static bool TryResolve(string stem, out string artist, out string title)
    {
        var key = new string(stem.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        if (Map.TryGetValue(key, out var song))
        {
            artist = song.Artist;
            title = song.Title;
            return true;
        }

        artist = "";
        title = "";
        return false;
    }
}
