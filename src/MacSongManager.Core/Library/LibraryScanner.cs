using MacSongManager.Core.Safety;
using MacSongManager.Core.Songs;

namespace MacSongManager.Core.Library;

public readonly record struct ScanProgress(int Completed, int Total, string FileName);

public sealed record ScanResult(
    string LibraryRoot,
    IReadOnlyList<SongRecord> Songs,
    IReadOnlyList<string> Warnings,
    int SkippedOfficial,
    int IgnoredOther);

/// <summary>
/// Lists custom song files by name. File bytes are hashed only.
/// Packages are not opened, and official game files are not modified.
/// </summary>
public sealed class LibraryScanner
{
    public async Task<ScanResult> ScanAsync(
        string libraryRoot,
        CancellationToken cancellationToken,
        IProgress<ScanProgress>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot))
            return new ScanResult(string.Empty, [], ["Choose a library folder."], 0, 0);

        string root;
        try
        {
            root = Path.GetFullPath(libraryRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new ScanResult(libraryRoot, [], [$"The library path is not valid: {ex.Message}"], 0, 0);
        }

        if (!Directory.Exists(root))
            return new ScanResult(root, [], ["Library folder was not found."], 0, 0);

        var files = EnumerateSongFiles(root);
        var warnings = new List<string>();
        var records = new List<SongRecord>();
        var skippedOfficial = 0;
        var ignoredOther = 0;

        for (var index = 0; index < files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[index];
            progress?.Report(new ScanProgress(index + 1, files.Count, Path.GetFileName(file)));

            if (!PathGuard.IsStrictlyInside(root, file))
            {
                warnings.Add($"Skipped {file} because it is outside the library folder.");
                continue;
            }

            if (OfficialFiles.IsOfficialPackage(file))
            {
                skippedOfficial++;
                continue;
            }

            var kind = SongFileNames.Classify(file);
            if (kind == SongFileKind.Unmanaged)
            {
                ignoredOther++;
                continue;
            }

            var record = await ReadFileAsync(root, file, kind, cancellationToken).ConfigureAwait(false);
            records.Add(record);
        }

        if (skippedOfficial > 0)
            warnings.Add($"Left {skippedOfficial} official file(s) unchanged, including songs.psarc when present.");

        var withDuplicates = ApplyDuplicates(records);
        var ordered = withDuplicates
            .OrderBy(song => song.Artist ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(song => song.Title ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(song => song.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ScanResult(root, ordered, warnings, skippedOfficial, ignoredOther);
    }

    private static async Task<SongRecord> ReadFileAsync(
        string root,
        string file,
        SongFileKind kind,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(file);
        var parsed = FilenameMetadataParser.Parse(file);
        string? hash = null;
        string? error = null;
        try
        {
            hash = await FileHasher.Sha256Async(file, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
        }

        return new SongRecord
        {
            FullPath = Path.GetFullPath(file),
            RelativePath = Path.GetRelativePath(root, file),
            FileName = info.Name,
            SizeBytes = info.Exists ? info.Length : 0,
            FileKind = kind,
            ReadError = error,
            Artist = parsed.Artist,
            Title = parsed.Title,
            Version = parsed.Version,
            Author = parsed.Author,
            ContentHash = hash
        };
    }

    private static List<string> EnumerateSongFiles(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        return Directory.EnumerateFiles(root, "*", options)
            .Where(path => path.EndsWith(".psarc", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<SongRecord> ApplyDuplicates(List<SongRecord> records)
    {
        var reasons = DuplicateDetector.ReasonsByPath(records.Select(song =>
            new SongIdentity(song.FullPath, song.Artist, song.Title, song.ContentHash)));

        var updated = new List<SongRecord>(records.Count);
        foreach (var song in records)
        {
            if (reasons.TryGetValue(song.FullPath, out var songReasons) && songReasons.Count > 0)
                updated.Add(song with { IsDuplicate = true, DuplicateReasons = songReasons });
            else
                updated.Add(song);
        }

        return updated;
    }
}
