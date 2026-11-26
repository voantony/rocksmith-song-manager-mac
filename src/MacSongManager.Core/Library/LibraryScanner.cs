using MacSongManager.Core.Conversion;
using MacSongManager.Core.Safety;
using MacSongManager.Core.Songs;
using MacSongManager.Core.Storage;

namespace MacSongManager.Core.Library;

public readonly record struct ScanProgress(int Completed, int Total, string FileName);

public sealed record ScanResult(
    string LibraryRoot,
    IReadOnlyList<SongRecord> Songs,
    IReadOnlyList<string> Warnings,
    int SkippedOfficial,
    int IgnoredOther);

/// <summary>
/// Lists song files. Manifest JSON is inflated on a cache miss.
/// Official game archives such as songs.psarc are not modified.
/// </summary>
public sealed class LibraryScanner
{
    private readonly TagStore? _tags;

    public LibraryScanner(TagStore? tags = null)
    {
        _tags = tags;
    }

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

            var record = await ReadFileAsync(root, file, kind, _tags, cancellationToken).ConfigureAwait(false);
            records.Add(record);
        }

        _tags?.Flush();

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
        TagStore? tags,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(file);
        var parsed = FilenameMetadataParser.Parse(file);
        var relativePath = Path.GetRelativePath(root, file);
        var length = info.Exists ? info.Length : 0;
        var writeTicks = info.Exists ? info.LastWriteTimeUtc.Ticks : 0;
        var stamp = tags?.FindStamp(relativePath);
        var stampFresh = stamp is not null
            && stamp.Length == length
            && stamp.LastWriteUtcTicks == writeTicks
            && !string.IsNullOrWhiteSpace(stamp.Hash);
        string? hash = stampFresh ? stamp!.Hash : null;
        string? error = null;
        if (!stampFresh)
        {
            try
            {
                hash = await FileHasher.Sha256Async(file, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = ex.Message;
            }
        }

        var saved = tags?.Find(hash);
        PackageManifest? manifest = null;
        if (saved is { ManifestCached: true })
        {
            manifest = new PackageManifest(
                saved.ManifestArtist, saved.ManifestTitle, saved.Album, saved.Year,
                saved.Arrangements, saved.Tuning, saved.DlcKey);
        }
        else if (error is null)
        {
            try
            {
                var documents = PsarcArchive.ReadManifests(file);
                manifest = ManifestMetadata.Read(documents.Values);
                if (hash is not null)
                    tags?.RememberManifest(hash, manifest ?? new PackageManifest(null, null, null, null, null, null, null));
            }
            catch (InvalidDataException ex) when (ex.Message == "Not a PSARC archive.")
            {
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Security.Cryptography.CryptographicException)
            {
                error ??= ex.Message;
            }
        }

        if (!stampFresh && hash is not null)
            tags?.RememberStamp(relativePath, length, writeTicks, hash);

        var (artist, title, confidence, source) = Resolve(parsed, saved, manifest);
        return new SongRecord
        {
            FullPath = Path.GetFullPath(file),
            RelativePath = relativePath,
            FileName = info.Name,
            SizeBytes = info.Exists ? info.Length : 0,
            FileKind = kind,
            ReadError = error,
            Artist = artist,
            Title = title,
            Album = manifest?.Album ?? saved?.Album,
            Year = manifest?.Year ?? saved?.Year,
            Arrangements = manifest?.Arrangements ?? saved?.Arrangements,
            Tuning = manifest?.Tuning ?? saved?.Tuning,
            DlcKey = manifest?.DlcKey ?? saved?.DlcKey,
            Version = parsed.Version,
            Author = parsed.Author,
            ContentHash = hash,
            Confidence = confidence,
            TagSource = source
        };
    }

    public static (string? Artist, string? Title, ParseConfidence Confidence, string Source) Resolve(
        FilenameMetadata parsed,
        TagOverride? saved,
        PackageManifest? manifest)
    {
        if (saved?.Source == "user" && !string.IsNullOrWhiteSpace(saved.Artist) && !string.IsNullOrWhiteSpace(saved.Title))
            return (saved.Artist, saved.Title, ParseConfidence.High, "user");

        if (!string.IsNullOrWhiteSpace(manifest?.Artist) || !string.IsNullOrWhiteSpace(manifest?.Title))
            return (manifest!.Artist, manifest.Title, ParseConfidence.High, "manifest");

        if (parsed.Confidence != ParseConfidence.Low && !string.IsNullOrWhiteSpace(parsed.Title))
            return (parsed.Artist, parsed.Title, parsed.Confidence, "filename");

        if (saved?.Source == "musicbrainz" && !string.IsNullOrWhiteSpace(saved.Artist) && !string.IsNullOrWhiteSpace(saved.Title))
            return (saved.Artist, saved.Title, ParseConfidence.High, "musicbrainz");

        return (parsed.Artist, parsed.Title, parsed.Confidence, "filename");
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
