namespace MacSongManager.Core.Songs;

/// <summary>
/// One custom song file. Artist and title prefer a user edit, then the
/// package manifest, then the file name.
/// </summary>
public sealed record SongRecord
{
    public required string FullPath { get; init; }
    public required string RelativePath { get; init; }
    public required string FileName { get; init; }
    public required long SizeBytes { get; init; }
    public required SongFileKind FileKind { get; init; }
    public string? ReadError { get; init; }
    public string? Artist { get; init; }
    public string? Title { get; init; }
    public string? Version { get; init; }
    public string? Author { get; init; }
    public string? Album { get; init; }
    public int? Year { get; init; }
    public string? Arrangements { get; init; }
    public string? Tuning { get; init; }
    public string? DlcKey { get; init; }
    public string? ContentHash { get; init; }
    public ParseConfidence Confidence { get; init; } = ParseConfidence.Medium;
    public string TagSource { get; init; } = "filename";
    public bool HasTagOverride => TagSource is "user" or "musicbrainz";
    public bool IsDuplicate { get; init; }
    public IReadOnlyList<DuplicateReason> DuplicateReasons { get; init; } = [];

    public bool IsCustomPackage =>
        ReadError is null && OfficialFiles.IsManagedCustomName(FullPath);
}
