using MacSongManager.Core.Songs;

namespace MacSongManager.Core.Tests;

internal static class Fixtures
{
    public static SongRecord Song(
        string fullPath,
        SongFileKind kind,
        string? artist = "Metric",
        string? title = "Help I'm Alive",
        string? readError = null)
    {
        return new SongRecord
        {
            FullPath = fullPath,
            RelativePath = Path.GetFileName(fullPath),
            FileName = Path.GetFileName(fullPath),
            SizeBytes = 32,
            FileKind = kind,
            ReadError = readError,
            Artist = artist,
            Title = title,
            Version = "1",
            ContentHash = "abc"
        };
    }

    public static void Touch(string path, byte[]? bytes = null)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);
        File.WriteAllBytes(path, bytes ?? [9, 8, 7, 6]);
    }
}

internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = Directory.CreateTempSubdirectory("mac-song-manager-").FullName;
    }

    public string Path { get; }

    public string Sub(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
