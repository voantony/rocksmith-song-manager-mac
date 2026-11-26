using System.Text.RegularExpressions;

namespace MacSongManager.Core.Songs;

public sealed record FilenameMetadata(string? Artist, string? Title, string? Version, string? Author);

/// <summary>
/// Reads artist, title, version, and author from a custom song file name.
/// Conventions, after the _m/_p suffix is removed:
/// Artist_Title_v1, Artist_Title_v1_by_Author, Artist - Title_v1,
/// and Artist__Title when the artist itself contains underscores.
/// </summary>
public static partial class FilenameMetadataParser
{
    public static FilenameMetadata Parse(string path)
    {
        var stem = SongFileNames.Stem(path).Trim();
        if (stem.Length == 0)
            return new FilenameMetadata(null, null, null, null);

        string? author = null;
        var by = ByAuthorPattern().Match(stem);
        if (by.Success)
        {
            author = Words(by.Groups[1].Value);
            stem = stem[..by.Index];
        }

        string? version = null;
        var versionMatch = VersionPattern().Match(stem);
        if (versionMatch.Success)
        {
            version = versionMatch.Groups[1].Value;
            stem = stem[..versionMatch.Index];
        }

        stem = stem.Trim(' ', '_', '-');
        if (stem.Length == 0)
            return new FilenameMetadata(null, null, version, author);

        var dash = stem.IndexOf(" - ", StringComparison.Ordinal);
        if (dash >= 0)
        {
            return new FilenameMetadata(
                Words(stem[..dash]),
                Words(stem[(dash + 3)..]),
                version,
                author);
        }

        var doubleBreak = stem.IndexOf("__", StringComparison.Ordinal);
        if (doubleBreak >= 0)
        {
            return new FilenameMetadata(
                Words(stem[..doubleBreak]),
                Words(stem[(doubleBreak + 2)..]),
                version,
                author);
        }

        var split = stem.IndexOf('_');
        if (split >= 0)
        {
            return new FilenameMetadata(
                Words(stem[..split]),
                Words(stem[(split + 1)..]),
                version,
                author);
        }

        return new FilenameMetadata(null, Words(stem), version, author);
    }

    private static string? Words(string value)
    {
        var text = Whitespace().Replace(value.Replace('_', ' '), " ").Trim();
        return text.Length == 0 ? null : text;
    }

    [GeneratedRegex(@"_by_(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ByAuthorPattern();

    [GeneratedRegex(@"_v(\d+(?:\.\d+)*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
