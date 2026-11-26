namespace MacSongManager.Core.Songs;

/// <summary>
/// One custom song file. Fields come from the file name and the file bytes.
/// The package is never opened.
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
    public string? ContentHash { get; init; }
    public ParseConfidence Confidence { get; init; } = ParseConfidence.Medium;
    public string TagSource { get; init; } = "filename";
    public bool HasTagOverride => TagSource is "user" or "musicbrainz";
    public bool IsDuplicate { get; init; }
    public IReadOnlyList<DuplicateReason> DuplicateReasons { get; init; } = [];

    public bool IsCustomPackage =>
        ReadError is null && OfficialFiles.IsManagedCustomName(FullPath);
}
