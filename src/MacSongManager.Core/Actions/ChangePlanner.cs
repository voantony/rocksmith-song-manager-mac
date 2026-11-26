using MacSongManager.Core.Safety;
using MacSongManager.Core.Songs;

namespace MacSongManager.Core.Actions;

public static class ChangePlanner
{
    public static ChangePlan PlanEnable(string libraryRoot, IEnumerable<SongRecord> songs) =>
        PlanMacRename(libraryRoot, songs, enable: true);

    public static ChangePlan PlanDisable(string libraryRoot, IEnumerable<SongRecord> songs) =>
        PlanMacRename(libraryRoot, songs, enable: false);

    public static ChangePlan PlanOrganize(string libraryRoot, IEnumerable<SongRecord> songs)
    {
        var root = Path.GetFullPath(libraryRoot);
        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedChange>();
        var reserved = new HashSet<string>(PathComparer);

        foreach (var song in songs)
        {
            if (!TryAcceptCustom(root, song, skipped, out var source))
                continue;
            if (song.FileKind is not (SongFileKind.MacEnabled or SongFileKind.MacDisabled or SongFileKind.PcFormat))
            {
                skipped.Add(new SkippedChange(song.FullPath, "Not a song package this app organizes."));
                continue;
            }

            var folder = ArtistFolderNames.Sanitize(song.Artist);
            var destination = Path.GetFullPath(Path.Combine(root, folder, song.FileName));
            if (!PathGuard.IsStrictlyInside(root, destination))
            {
                skipped.Add(new SkippedChange(song.FullPath, "Artist folder would leave the library."));
                continue;
            }

            if (PathGuard.SamePath(source, destination))
            {
                skipped.Add(new SkippedChange(song.FullPath, "Already in the artist folder."));
                continue;
            }

            if (!Reserve(destination, source, reserved, skipped, song.FullPath, "A file with that name is already in the artist folder."))
                continue;

            changes.Add(new PlannedChange(
                ChangeKind.Organize,
                source,
                destination,
                $"Move to {folder}"));
        }

        return new ChangePlan(ChangeKind.Organize, changes, skipped);
    }

    public static ChangePlan PlanTrash(
        string libraryRoot,
        string trashRoot,
        string batchFolder,
        IEnumerable<SongRecord> songs)
    {
        var root = Path.GetFullPath(libraryRoot);
        var trash = Path.GetFullPath(trashRoot);
        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedChange>();
        var reserved = new HashSet<string>(PathComparer);

        foreach (var song in songs)
        {
            if (!TryAcceptCustom(root, song, skipped, out var source))
                continue;

            var relative = Path.GetRelativePath(root, source);
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                skipped.Add(new SkippedChange(song.FullPath, "Outside the library folder."));
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(trash, batchFolder, relative));
            if (!PathGuard.IsStrictlyInside(trash, destination))
            {
                skipped.Add(new SkippedChange(song.FullPath, "Trash destination would leave the trash folder."));
                continue;
            }

            if (!Reserve(destination, source, reserved, skipped, song.FullPath, "Already reserved in this trash batch."))
                continue;

            changes.Add(new PlannedChange(ChangeKind.Trash, source, destination, "Move to trash"));
        }

        return new ChangePlan(ChangeKind.Trash, changes, skipped);
    }

    public static ChangePlan PlanUndo(ChangeBatch batch)
    {
        var changes = new List<PlannedChange>(batch.Changes.Count);
        for (var i = batch.Changes.Count - 1; i >= 0; i--)
        {
            var change = batch.Changes[i];
            changes.Add(new PlannedChange(
                ChangeKind.Undo,
                change.DestinationPath,
                change.SourcePath,
                "Undo " + change.Description));
        }

        return new ChangePlan(ChangeKind.Undo, changes, []);
    }

    private static ChangePlan PlanMacRename(string libraryRoot, IEnumerable<SongRecord> songs, bool enable)
    {
        var root = Path.GetFullPath(libraryRoot);
        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedChange>();
        var reserved = new HashSet<string>(PathComparer);
        var kind = enable ? ChangeKind.Enable : ChangeKind.Disable;

        foreach (var song in songs)
        {
            if (!TryAcceptCustom(root, song, skipped, out var source))
                continue;

            if (song.FileKind == SongFileKind.PcFormat)
            {
                skipped.Add(new SkippedChange(song.FullPath, "PC packages are not loaded by the Mac game."));
                continue;
            }

            if (enable && song.FileKind == SongFileKind.MacEnabled)
            {
                skipped.Add(new SkippedChange(song.FullPath, "Already enabled."));
                continue;
            }

            if (!enable && song.FileKind == SongFileKind.MacDisabled)
            {
                skipped.Add(new SkippedChange(song.FullPath, "Already disabled."));
                continue;
            }

            var newName = enable
                ? SongFileNames.EnabledFileName(song.FileName)
                : SongFileNames.DisabledFileName(song.FileName);
            if (newName is null)
            {
                skipped.Add(new SkippedChange(song.FullPath, "Not a Mac song package."));
                continue;
            }

            var directory = Path.GetDirectoryName(source) ?? root;
            var destination = Path.GetFullPath(Path.Combine(directory, newName));
            if (!PathGuard.IsStrictlyInside(root, destination))
            {
                skipped.Add(new SkippedChange(song.FullPath, "Rename would leave the library folder."));
                continue;
            }

            var conflict = enable
                ? "The enabled file name already exists."
                : "The disabled file name already exists.";
            if (!Reserve(destination, source, reserved, skipped, song.FullPath, conflict))
                continue;

            changes.Add(new PlannedChange(
                kind,
                source,
                destination,
                enable ? "Enable" : "Disable"));
        }

        return new ChangePlan(kind, changes, skipped);
    }

    private static bool TryAcceptCustom(
        string root,
        SongRecord song,
        List<SkippedChange> skipped,
        out string source)
    {
        source = Path.GetFullPath(song.FullPath);
        if (!PathGuard.IsStrictlyInside(root, source))
        {
            skipped.Add(new SkippedChange(song.FullPath, "Outside the library folder."));
            return false;
        }

        if (!OfficialFiles.IsManagedCustomName(source))
        {
            skipped.Add(new SkippedChange(song.FullPath, "Official game files and songs.psarc are left unchanged."));
            return false;
        }

        if (song.ReadError is not null)
        {
            skipped.Add(new SkippedChange(song.FullPath, "Unreadable file. Left alone."));
            return false;
        }

        return true;
    }

    private static bool Reserve(
        string destination,
        string source,
        HashSet<string> reserved,
        List<SkippedChange> skipped,
        string originalPath,
        string conflictReason)
    {
        if (PathGuard.SamePath(source, destination))
        {
            skipped.Add(new SkippedChange(originalPath, "Already in place."));
            return false;
        }

        if (File.Exists(destination) && !PathGuard.SamePath(source, destination))
        {
            skipped.Add(new SkippedChange(originalPath, conflictReason));
            return false;
        }

        if (!reserved.Add(destination))
        {
            skipped.Add(new SkippedChange(originalPath, conflictReason));
            return false;
        }

        return true;
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsMacOS() || OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
}
