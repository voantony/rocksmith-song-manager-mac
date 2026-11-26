using MacSongManager.Core.Songs;

namespace MacSongManager.Core.Conversion;

/// <summary>
/// Refuses packages that look like official Ubisoft content.
/// Custom songs written by the community toolkit carry a toolkit.version entry.
/// Official packages, including songs.psarc and compatibility packs, do not.
/// </summary>
public static class OfficialContent
{
    public const string OfficialPackageReason =
        "Official game files, including songs.psarc, are not converted.";

    public const string MissingMarkerReason =
        "This package has no custom-song marker (toolkit.version). Official Ubisoft content is not converted.";

    public static string? Refusal(string path, IEnumerable<string> entryNames)
    {
        if (OfficialFiles.IsOfficialPackage(path))
            return OfficialPackageReason;

        foreach (var name in entryNames)
        {
            var normalized = name.Replace('\\', '/');
            if (normalized.Contains("compatibility", StringComparison.OrdinalIgnoreCase))
                return "Compatibility packs are official content and are not converted.";

            var slash = normalized.LastIndexOf('/');
            var file = slash >= 0 ? normalized[(slash + 1)..] : normalized;
            if (file.Equals("songs.psarc", StringComparison.OrdinalIgnoreCase)
                || file.Equals("gears.psarc", StringComparison.OrdinalIgnoreCase))
            {
                return "This package contains an official game archive and is not converted.";
            }
        }

        foreach (var name in entryNames)
        {
            if (name.EndsWith("toolkit.version", StringComparison.OrdinalIgnoreCase))
                return null;
        }

        return MissingMarkerReason;
    }
}
