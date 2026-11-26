using System.Globalization;
using System.Text;

namespace MacSongManager.Core.Songs;

public static class TextNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && builder.Length > 0)
                    builder.Append(' ');
                pendingSpace = false;
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (char.IsWhiteSpace(character))
            {
                pendingSpace = true;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Key for "same song" comparisons. Empty when both sides are blank so
    /// untitled files are not treated as duplicates of each other.
    /// </summary>
    public static string ArtistTitleKey(string? artist, string? title)
    {
        var normalizedArtist = StripLeadingArticle(Normalize(artist));
        var normalizedTitle = Normalize(title);
        if (normalizedArtist.Length == 0 && normalizedTitle.Length == 0)
            return string.Empty;
        return normalizedArtist + "\u001f" + normalizedTitle;
    }

    private static string StripLeadingArticle(string artist)
    {
        const string article = "the ";
        return artist.StartsWith(article, StringComparison.Ordinal)
            ? artist[article.Length..]
            : artist;
    }
}
