using System.Text.Json;
using System.Text.Json.Serialization;
using MacSongManager.Core.Safety;
using MacSongManager.Core.Songs;

namespace MacSongManager.Core.Actions;

public interface IChangeLog
{
    void Append(ChangeBatch batch);
    ChangeBatch? Latest();
    IReadOnlyList<ChangeBatch> ReadAll();
}

public sealed class JsonlChangeLog : IChangeLog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly object _gate = new();

    public JsonlChangeLog(string path)
    {
        _path = path;
    }

    public void Append(ChangeBatch batch)
    {
        lock (_gate)
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            var line = JsonSerializer.Serialize(batch, JsonOptions);
            File.AppendAllText(_path, line + "\n");
        }
    }

    public ChangeBatch? Latest()
    {
        var all = ReadAll();
        return all.Count == 0 ? null : all[^1];
    }

    public IReadOnlyList<ChangeBatch> ReadAll()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
                return [];

            var batches = new List<ChangeBatch>();
            foreach (var line in File.ReadLines(_path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                try
                {
                    var batch = JsonSerializer.Deserialize<ChangeBatch>(line, JsonOptions);
                    if (batch is not null)
                        batches.Add(batch);
                }
                catch (JsonException)
                {
                    // Keep the rest of the log usable if one line is damaged.
                }
            }

            return batches;
        }
    }
}

/// <summary>
/// Applies a plan by renaming or moving files. There is no delete path.
/// A failed batch moves completed files back before it returns.
/// </summary>
public sealed class ChangeApplier
{
    private readonly IChangeLog _log;

    public ChangeApplier(IChangeLog log)
    {
        _log = log;
    }

    public ApplyResult Apply(string libraryRoot, string trashRoot, ChangePlan plan, bool dryRun)
    {
        if (!plan.HasWork)
        {
            return new ApplyResult(dryRun, true, "No files to change.", plan, null);
        }

        string root;
        string trash;
        try
        {
            root = Path.GetFullPath(libraryRoot);
            trash = Path.GetFullPath(trashRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new ApplyResult(dryRun, false, ex.Message, plan, null);
        }

        var problems = new List<string>();
        foreach (var change in plan.Changes)
            Validate(root, trash, plan.Kind, change, problems);

        if (problems.Count > 0)
        {
            return new ApplyResult(
                dryRun,
                false,
                string.Join(" ", problems.Distinct()),
                plan,
                null);
        }

        if (dryRun)
        {
            return new ApplyResult(
                true,
                true,
                $"Dry run: {plan.Changes.Count} change(s). No files were written.",
                plan,
                null);
        }

        var done = new List<PlannedChange>();
        try
        {
            foreach (var change in plan.Changes)
            {
                var parent = Path.GetDirectoryName(change.DestinationPath);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
                File.Move(change.SourcePath, change.DestinationPath);
                done.Add(change);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var rollbackNote = Rollback(done);
            var message = $"Stopped before finishing the batch: {ex.Message}";
            if (rollbackNote.Length > 0)
                message += " " + rollbackNote;
            return new ApplyResult(false, false, message, plan, null);
        }

        var batch = new ChangeBatch(Guid.NewGuid(), DateTimeOffset.UtcNow, plan.Kind, done);
        try
        {
            _log.Append(batch);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ApplyResult(
                false,
                false,
                $"Files were moved, but the undo log could not be written: {ex.Message}",
                plan,
                null);
        }

        return new ApplyResult(
            false,
            true,
            $"Applied {done.Count} change(s). Undo is available.",
            plan,
            batch.Id);
    }

    private static void Validate(
        string libraryRoot,
        string trashRoot,
        ChangeKind planKind,
        PlannedChange change,
        List<string> problems)
    {
        if (!File.Exists(change.SourcePath))
        {
            problems.Add($"Missing source file: {change.SourcePath}");
            return;
        }

        if (Directory.Exists(change.SourcePath) && File.GetAttributes(change.SourcePath).HasFlag(FileAttributes.Directory))
        {
            problems.Add($"Refusing to move a directory: {change.SourcePath}");
            return;
        }

        if (PathGuard.SamePath(change.SourcePath, change.DestinationPath))
        {
            problems.Add($"Source and destination are the same file: {change.SourcePath}");
            return;
        }

        if (File.Exists(change.DestinationPath))
        {
            problems.Add($"Destination already exists: {change.DestinationPath}");
            return;
        }

        if (!OfficialFiles.IsManagedCustomName(change.SourcePath)
            || !OfficialFiles.IsManagedCustomName(change.DestinationPath))
        {
            problems.Add("Official game files and songs.psarc are left unchanged.");
            return;
        }

        var kind = change.Kind == ChangeKind.Undo ? planKind : change.Kind;
        if (!PathsAllowed(libraryRoot, trashRoot, kind, change.SourcePath, change.DestinationPath))
            problems.Add($"Refusing to touch a path outside the library or trash folder: {change.SourcePath}");
    }

    private static bool PathsAllowed(
        string libraryRoot,
        string trashRoot,
        ChangeKind kind,
        string source,
        string destination)
    {
        bool InLibrary(string path) => PathGuard.IsStrictlyInside(libraryRoot, path);
        bool InTrash(string path) => PathGuard.IsStrictlyInside(trashRoot, path);

        return kind switch
        {
            ChangeKind.Enable or ChangeKind.Disable or ChangeKind.Organize =>
                InLibrary(source) && InLibrary(destination),
            ChangeKind.Trash => InLibrary(source) && InTrash(destination),
            ChangeKind.Undo => (InLibrary(source) || InTrash(source)) && (InLibrary(destination) || InTrash(destination)),
            _ => false
        };
    }

    private static string Rollback(List<PlannedChange> done)
    {
        var failed = new List<string>();
        for (var i = done.Count - 1; i >= 0; i--)
        {
            var change = done[i];
            try
            {
                if (File.Exists(change.DestinationPath) && !File.Exists(change.SourcePath))
                {
                    var parent = Path.GetDirectoryName(change.SourcePath);
                    if (!string.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);
                    File.Move(change.DestinationPath, change.SourcePath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(change.DestinationPath + " (" + ex.Message + ")");
            }
        }

        return failed.Count == 0
            ? "Files already moved were moved back."
            : "Some files could not be moved back: " + string.Join(", ", failed);
    }
}
