using MacSongManager.Core.Actions;
using MacSongManager.Core.Songs;

namespace MacSongManager.Core.Tests;

public class ChangeApplierTests
{
    [Fact]
    public void Dry_run_does_not_move_files_or_write_the_log()
    {
        using var temp = new TempFolder();
        var library = temp.Sub("dlc");
        var trash = temp.Sub("trash");
        var logPath = temp.Sub("log.jsonl");
        var source = Path.Combine(library, "song_m.psarc");
        Fixtures.Touch(source);
        var plan = ChangePlanner.PlanDisable(library, [Fixtures.Song(source, SongFileKind.MacEnabled)]);
        var applier = new ChangeApplier(new JsonlChangeLog(logPath));

        var result = applier.Apply(library, trash, plan, dryRun: true);

        Assert.True(result.Succeeded);
        Assert.True(result.DryRun);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(Path.Combine(library, "song_m.disabled.psarc")));
        Assert.False(File.Exists(logPath));
        Assert.False(Directory.Exists(trash));
    }

    [Fact]
    public void Disable_then_undo_restores_the_file_name()
    {
        using var temp = new TempFolder();
        var library = temp.Sub("dlc");
        var trash = temp.Sub("trash");
        var log = new JsonlChangeLog(temp.Sub("log.jsonl"));
        var applier = new ChangeApplier(log);
        var source = Path.Combine(library, "song_m.psarc");
        Fixtures.Touch(source);
        var disabled = Path.Combine(library, "song_m.disabled.psarc");

        var plan = ChangePlanner.PlanDisable(library, [Fixtures.Song(source, SongFileKind.MacEnabled)]);
        var applied = applier.Apply(library, trash, plan, dryRun: false);

        Assert.True(applied.Succeeded);
        Assert.False(File.Exists(source));
        Assert.Equal(new byte[] { 9, 8, 7, 6 }, File.ReadAllBytes(disabled));

        var undo = ChangePlanner.PlanUndo(log.Latest()!);
        var undone = applier.Apply(library, trash, undo, dryRun: false);

        Assert.True(undone.Succeeded);
        Assert.Equal(new byte[] { 9, 8, 7, 6 }, File.ReadAllBytes(source));
        Assert.False(File.Exists(disabled));
        Assert.Equal(2, log.ReadAll().Count);
    }

    [Fact]
    public void Trash_moves_the_file_and_undo_brings_it_back()
    {
        using var temp = new TempFolder();
        var library = temp.Sub("dlc");
        var trash = temp.Sub("trash");
        var log = new JsonlChangeLog(temp.Sub("log.jsonl"));
        var applier = new ChangeApplier(log);
        var source = Path.Combine(library, "Band", "song_m.psarc");
        Fixtures.Touch(source);

        var plan = ChangePlanner.PlanTrash(library, trash, "batch", [
            Fixtures.Song(source, SongFileKind.MacEnabled)
        ]);
        var applied = applier.Apply(library, trash, plan, dryRun: false);

        var trashed = Path.Combine(trash, "batch", "Band", "song_m.psarc");
        Assert.True(applied.Succeeded);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(trashed));

        var undone = applier.Apply(library, trash, ChangePlanner.PlanUndo(log.Latest()!), dryRun: false);
        Assert.True(undone.Succeeded);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(trashed));
    }

    [Fact]
    public void Apply_refuses_a_destination_outside_the_library()
    {
        using var temp = new TempFolder();
        var library = temp.Sub("dlc");
        var trash = temp.Sub("trash");
        var outside = temp.Sub("outside");
        Directory.CreateDirectory(outside);
        var source = Path.Combine(library, "song_m.psarc");
        Fixtures.Touch(source);
        var escape = Path.Combine(outside, "song_m.disabled.psarc");
        var plan = new ChangePlan(ChangeKind.Disable, [
            new PlannedChange(ChangeKind.Disable, source, escape, "Disable")
        ], []);
        var log = new JsonlChangeLog(temp.Sub("log.jsonl"));

        var result = new ChangeApplier(log).Apply(library, trash, plan, dryRun: false);

        Assert.False(result.Succeeded);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(escape));
        Assert.Null(log.Latest());
    }

    [Fact]
    public void Apply_refuses_a_symlink_whose_target_is_outside_the_library()
    {
        using var temp = new TempFolder();
        var library = temp.Sub("dlc");
        var trash = temp.Sub("trash");
        Directory.CreateDirectory(library);
        var outside = temp.Sub("outside", "song_m.psarc");
        Fixtures.Touch(outside);
        var link = Path.Combine(library, "song_m.psarc");
        File.CreateSymbolicLink(link, outside);
        var destination = Path.Combine(library, "song_m.disabled.psarc");
        var plan = new ChangePlan(ChangeKind.Disable, [
            new PlannedChange(ChangeKind.Disable, link, destination, "Disable")
        ], []);

        var result = new ChangeApplier(new JsonlChangeLog(temp.Sub("log.jsonl")))
            .Apply(library, trash, plan, dryRun: false);

        Assert.False(result.Succeeded);
        Assert.Equal(new byte[] { 9, 8, 7, 6 }, File.ReadAllBytes(outside));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public void Organize_apply_moves_into_the_artist_folder()
    {
        using var temp = new TempFolder();
        var library = temp.Sub("dlc");
        var source = Path.Combine(library, "song_m.psarc");
        Fixtures.Touch(source);
        var plan = ChangePlanner.PlanOrganize(library, [
            Fixtures.Song(source, SongFileKind.MacEnabled, artist: "Metric")
        ]);
        Assert.False(Directory.Exists(Path.Combine(library, "Metric")));

        var result = new ChangeApplier(new JsonlChangeLog(temp.Sub("log.jsonl")))
            .Apply(library, temp.Sub("trash"), plan, dryRun: false);

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(Path.Combine(library, "Metric", "song_m.psarc")));
        Assert.False(File.Exists(source));
    }
}
