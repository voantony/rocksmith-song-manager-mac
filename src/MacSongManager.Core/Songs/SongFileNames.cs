namespace MacSongManager.Core.Songs;

public static class SongFileNames
{
    public const string MacEnabledSuffix = "_m.psarc";
    public const string MacDisabledSuffix = "_m.disabled.psarc";
    public const string PcSuffix = "_p.psarc";
    public const string PcDisabledSuffix = "_p.disabled.psarc";

    public static SongFileKind Classify(string path)
    {
        var name = Path.GetFileName(path);
        if (EndsWith(name, MacDisabledSuffix))
            return SongFileKind.MacDisabled;
        if (EndsWith(name, PcDisabledSuffix) || EndsWith(name, PcSuffix))
            return SongFileKind.PcFormat;
        if (EndsWith(name, MacEnabledSuffix))
            return SongFileKind.MacEnabled;
        return SongFileKind.Unmanaged;
    }

    public static bool IsMacPackage(SongFileKind kind) =>
        kind is SongFileKind.MacEnabled or SongFileKind.MacDisabled;

    /// <summary>
    /// File name with the platform suffix removed. The archive itself is not opened.
    /// </summary>
    public static string Stem(string path)
    {
        var name = Path.GetFileName(path);
        foreach (var suffix in new[] { MacDisabledSuffix, PcDisabledSuffix, MacEnabledSuffix, PcSuffix })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return name[..^suffix.Length];
        }

        return Path.GetFileNameWithoutExtension(name);
    }

    /// <summary>
    /// File name to use when the Mac package should load. Returns null unless
    /// the file is already a Mac package name.
    /// </summary>
    public static string? EnabledFileName(string path)
    {
        var name = Path.GetFileName(path);
        return Classify(name) switch
        {
            SongFileKind.MacDisabled => SwapStem(name, MacDisabledSuffix, "_m"),
            SongFileKind.MacEnabled => name,
            _ => null
        };
    }

    /// <summary>
    /// File name to use when the Mac package should stay on disk but not load.
    /// Returns null unless the file is already a Mac package name.
    /// </summary>
    public static string? DisabledFileName(string path)
    {
        var name = Path.GetFileName(path);
        return Classify(name) switch
        {
            SongFileKind.MacEnabled => SwapStem(name, MacEnabledSuffix, "_m.disabled"),
            SongFileKind.MacDisabled => name,
            _ => null
        };
    }

    private static string SwapStem(string fileName, string oldSuffix, string newStem)
    {
        var stem = fileName[..^oldSuffix.Length];
        var extension = fileName[^".psarc".Length..];
        return stem + newStem + extension;
    }

    private static bool EndsWith(string name, string suffix) =>
        name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
}
