using System.Text;
using System.Text.RegularExpressions;

namespace MacSongManager.Core.Songs;

public sealed record FilenameMetadata(
    string? Artist,
    string? Title,
    string? Version,
    string? Author,
    ParseConfidence Confidence);

/// <summary>
/// Reads artist and title from a custom-song file name.
/// Handles CamelCase hyphen names, spaced "Artist - Title", double underscores,
/// and toolkit-style Artist_Title_v1_DD names. The archive is not opened.
/// </summary>
public static partial class FilenameMetadataParser
{
    private static readonly string[] GlueWords =
    [
        "of", "the", "and", "van", "von", "for"
    ];

    private static readonly HashSet<string> SmallWords = new(StringComparer.Ordinal)
    {
        "a", "an", "of", "the", "to", "van", "von", "de", "da", "di",
        "la", "le", "du", "and", "or", "in", "on", "for", "und"
    };

    public static FilenameMetadata Parse(string path)
    {
        var stem = SongFileNames.Stem(path).Trim();
        string? author = null;
        string? version = null;
        StripTail(ref stem, ref author, ref version);
        if (stem.Length == 0)
            return new FilenameMetadata(null, null, version, author, ParseConfidence.Low);

        if (!stem.Contains('_') && !stem.Contains('-') && !stem.Contains(' ')
            && OfficialSongNames.TryResolve(stem, out var officialArtist, out var officialTitle))
        {
            return new FilenameMetadata(officialArtist, officialTitle, version, author, ParseConfidence.High);
        }

        var spaced = stem.IndexOf(" - ", StringComparison.Ordinal);
        if (spaced >= 0)
        {
            return Finish(
                SplitWords(stem[..spaced]),
                SplitWords(stem[(spaced + 3)..]),
                version,
                author,
                ParseConfidence.High);
        }

        var doubled = stem.IndexOf("__", StringComparison.Ordinal);
        if (doubled >= 0)
        {
            return Finish(
                SplitWords(stem[..doubled]),
                SplitWords(stem[(doubled + 2)..]),
                version,
                author,
                ParseConfidence.High);
        }

        if (version is not null && stem.Contains('_'))
        {
            var underscore = stem.IndexOf('_');
            return Finish(
                SplitWords(stem[..underscore]),
                SplitWords(stem[(underscore + 1)..]),
                version,
                author,
                ParseConfidence.High);
        }

        var hyphens = stem.Count(character => character == '-');
        if (hyphens >= 1)
        {
            var hyphen = stem.IndexOf('-');
            var confidence = hyphens == 1 ? ParseConfidence.Medium : ParseConfidence.Low;
            return Finish(
                SplitWords(stem[..hyphen]),
                SplitWords(stem[(hyphen + 1)..]),
                version,
                author,
                confidence);
        }

        if (stem.Contains('_'))
        {
            var underscore = stem.IndexOf('_');
            return Finish(
                SplitWords(stem[..underscore]),
                SplitWords(stem[(underscore + 1)..]),
                version,
                author,
                ParseConfidence.Medium);
        }

        return Finish(null, SplitWords(stem), version, author, ParseConfidence.Low);
    }

    public static string? SplitWords(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var camel = InsertCamelSpaces(raw.Replace('_', ' '));
        var glued = string.Join(' ', camel.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(SplitGlue));
        var words = glued.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return null;

        if (words[0] is "Im" && words.Length > 1)
            words[0] = "I'm";

        for (var i = 0; i < words.Length; i++)
        {
            if (i > 0 && SmallWords.Contains(words[i].ToLowerInvariant()))
                words[i] = words[i].ToLowerInvariant();
        }

        var text = Whitespace().Replace(string.Join(' ', words), " ").Trim();
        return text.Length == 0 ? null : text;
    }

    private static void StripTail(ref string stem, ref string? author, ref string? version)
    {
        while (stem.Length > 0)
        {
            if (stem.EndsWith("_DD", StringComparison.OrdinalIgnoreCase))
            {
                stem = stem[..^3];
                continue;
            }

            var by = ByAuthorPattern().Match(stem);
            if (by.Success)
            {
                author ??= SplitWords(by.Groups[1].Value);
                stem = stem[..by.Index];
                continue;
            }

            var versionMatch = VersionPattern().Match(stem);
            if (versionMatch.Success)
            {
                version ??= versionMatch.Groups[1].Value;
                stem = stem[..versionMatch.Index];
                continue;
            }

            break;
        }

        stem = stem.Trim(' ', '_', '-');
    }

    private static FilenameMetadata Finish(
        string? artist,
        string? title,
        string? version,
        string? author,
        ParseConfidence confidence)
    {
        if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(title))
            confidence = ParseConfidence.Low;
        else if (confidence != ParseConfidence.Low && LooksUnsplit(artist, title))
            confidence = ParseConfidence.Low;

        return new FilenameMetadata(Blank(artist), Blank(title), version, author, confidence);
    }

    private static bool LooksUnsplit(string? artist, string? title)
    {
        static bool Blob(string? value) =>
            value is { Length: > 18 } && !value.Contains(' ') && value.All(char.IsLetter);

        return Blob(artist) || Blob(title);
    }

    private static string InsertCamelSpaces(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (i > 0 && builder.Length > 0 && builder[^1] != ' ')
            {
                var previous = value[i - 1];
                var next = i + 1 < value.Length ? value[i + 1] : '\0';
                var boundary =
                    (char.IsUpper(current) && char.IsLower(previous))
                    || (char.IsUpper(current) && char.IsUpper(previous) && char.IsLower(next))
                    || (char.IsDigit(current) && char.IsLetter(previous))
                    || (char.IsLetter(current) && char.IsDigit(previous));
                if (boundary)
                    builder.Append(' ');
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private static string SplitGlue(string token)
    {
        var parts = new List<string>();
        var rest = token;
        while (rest.Length > 0)
        {
            string? glue = null;
            foreach (var word in GlueWords)
            {
                if (rest.Length >= word.Length + 6
                    && rest.EndsWith(word, StringComparison.OrdinalIgnoreCase)
                    && char.IsLetter(rest[rest.Length - word.Length - 1]))
                {
                    glue = word;
                    break;
                }
            }

            if (glue is null)
            {
                parts.Insert(0, rest);
                break;
            }

            parts.Insert(0, glue);
            rest = rest[..^glue.Length];
        }

        return string.Join(' ', parts);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"_by_(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ByAuthorPattern();

    [GeneratedRegex(@"_v(\d+(?:\.\d+)*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
