using System.Text;

namespace MacSongManager.Core.Songs;

public static class ArtistFolderNames
{
    public const string UnknownArtist = "Unknown Artist";

    public static string Sanitize(string? artist)
    {
        if (string.IsNullOrWhiteSpace(artist))
            return UnknownArtist;

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(artist.Length);
        var pendingSpace = false;
        foreach (var character in artist.Trim())
        {
            var blocked = character is '/' or '\\' or ':' or '\0'
                || char.IsControl(character)
                || invalid.Contains(character);
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

        var name = builder.ToString().Trim(' ', '.');
        if (name.Length == 0 || name is "." or "..")
            return UnknownArtist;
        if (name.Length > 80)
            name = name[..80].Trim(' ', '.');
        return name.Length == 0 ? UnknownArtist : name;
    }
}
