namespace MacSongManager.Core.Actions;

public enum ChangeKind
{
    Enable,
    Disable,
    Organize,
    Trash,
    Rename,
    Convert,
    Undo
}

public sealed record PlannedChange(
    ChangeKind Kind,
    string SourcePath,
    string DestinationPath,
    string Description);

public sealed record SkippedChange(string Path, string Reason);

public sealed record ChangePlan(
    ChangeKind Kind,
    IReadOnlyList<PlannedChange> Changes,
    IReadOnlyList<SkippedChange> Skipped)
{
    public bool HasWork => Changes.Count > 0;

    public static ChangePlan Empty(ChangeKind kind) => new(kind, [], []);
}

public sealed record ChangeBatch(
    Guid Id,
    DateTimeOffset Timestamp,
    ChangeKind Kind,
    IReadOnlyList<PlannedChange> Changes);

public sealed record ApplyResult(
    bool DryRun,
    bool Succeeded,
    string Message,
    ChangePlan Plan,
    Guid? BatchId);
