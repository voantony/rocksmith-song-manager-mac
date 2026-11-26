using System.Text;

namespace MacSongManager.Core.Songs;

public static class FriendlyFileNames
{
    public static string? Build(string? artist, string? title, string originalFileName)
    {
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title))
            return null;

        var suffix = PlatformSuffix(originalFileName);
        if (suffix is null)
            return null;

        var cleanedArtist = Sanitize(artist);
        var cleanedTitle = Sanitize(title);
        if (cleanedArtist.Length == 0 || cleanedTitle.Length == 0)
            return null;

        var name = $"{cleanedArtist} - {cleanedTitle}{suffix}";
        return name.Length > 180 ? name[..180].Trim() + suffix : name;
    }

    public static string? PlatformSuffix(string fileName)
    {
        var name = Path.GetFileName(fileName);
        foreach (var suffix in new[]
        {
            SongFileNames.MacDisabledSuffix,
            SongFileNames.PcDisabledSuffix,
            SongFileNames.MacEnabledSuffix,
            SongFileNames.PcSuffix
        })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return name[^suffix.Length..];
        }

        return null;
    }

    public static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value.Trim())
        {
            var blocked = character is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' or '\0'
                || char.IsControl(character);
            if (blocked || char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
                builder.Append(' ');
            pendingSpace = false;
            builder.Append(character);
        }

        return builder.ToString().Trim(' ', '.');
    }
}
