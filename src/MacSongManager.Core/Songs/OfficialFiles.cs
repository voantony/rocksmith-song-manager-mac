namespace MacSongManager.Core.Songs;

/// <summary>
/// Names of official game packages that must never be renamed or moved.
/// Identification is by file name only.
/// </summary>
public static class OfficialFiles
{
    public static bool IsOfficialPackage(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        if (name.Length == 0)
            return false;

        if (name is "songs.psarc" or "gears.psarc")
            return true;
        if (name.StartsWith("songs.", StringComparison.Ordinal) || name.StartsWith("songs_", StringComparison.Ordinal))
            return true;
        if (name.StartsWith("gears.", StringComparison.Ordinal) || name.StartsWith("gears_", StringComparison.Ordinal))
            return true;
        if (name.Contains("compatibility", StringComparison.Ordinal))
            return true;

        return false;
    }

    public static bool IsManagedCustomName(string path) =>
        !IsOfficialPackage(path)
        && SongFileNames.Classify(path) is SongFileKind.MacEnabled or SongFileKind.MacDisabled or SongFileKind.PcFormat;
}
