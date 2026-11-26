using MacSongManager.Core.Actions;
using MacSongManager.Core.Songs;

namespace MacSongManager.Core.Tests;

public class ChangePlannerTests
{
    [Fact]
    public void Enable_and_disable_rename_inside_the_same_folder()
    {
        using var temp = new TempFolder();
        var library = temp.Sub("dlc");
        Directory.CreateDirectory(library);
        var disabled = Path.Combine(library, "Nest", "song_m.disabled.psarc");
        var enabled = Path.Combine(library, "song_m.psarc");
        Fixtures.Touch(disabled);
        Fixtures.Touch(enabled);

        var enable = ChangePlanner.PlanEnable(library, [
            Fixtures.Song(disabled, SongFileKind.MacDisabled)
        ]);
        var disable = ChangePlanner.PlanDisable(library, [
            Fixtures.Song(enabled, SongFileKind.MacEnabled)
        ]);

        var enabledChange = Assert.Single(enable.Changes);
        Assert.Equal(Path.Combine(library, "Nest", "song_m.psarc"), enabledChange.DestinationPath);
        Assert.Empty(enable.Skipped);

        var disabledChange = Assert.Single(disable.Changes);
        Assert.Equal(Path.Combine(library, "song_m.disabled.psarc"), disabledChange.DestinationPath);
    }

    [Fact]
    public void Enable_skips_pc_protected_and_name_collisions()
    {
        using var temp = new TempFolder();
        var library = temp.Sub("dlc");
        Directory.CreateDirectory(library);
        var disabled = Path.Combine(library, "song_m.disabled.psarc");
        var collision = Path.Combine(library, "song_m.psarc");
        Fixtures.Touch(disabled);
        Fixtures.Touch(collision);
        var pc = Path.Combine(library, "other_p.psarc");
        Fixtures.Touch(pc);
        var official = Path.Combine(library, "rs1compatibilitydisc_m.disabled.psarc");
        Fixtures.Touch(official);
        var songsArchive = Path.Combine(library, "songs.psarc");
        Fixtures.Touch(songsArchive);

        var plan = ChangePlanner.PlanEnable(library, [
            Fixtures.Song(disabled, SongFileKind.MacDisabled),
            Fixtures.Song(pc, SongFileKind.PcFormat),
            Fixtures.Song(official, SongFileKind.MacDisabled),
            Fixtures.Song(songsArchive, SongFileKind.Unmanaged)
        ]);

        Assert.Empty(plan.Changes);
        Assert.Contains(plan.Skipped, skip => skip.Path == disabled && skip.Reason.Contains("already exists", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Skipped, skip => skip.Path == pc && skip.Reason.Contains("PC", StringComparison.Ordinal));
        Assert.Contains(plan.Skipped, skip => skip.Path == official && skip.Reason.Contains("Official", StringComparison.Ordinal));
        Assert.Contains(plan.Skipped, skip => skip.Path == songsArchive && skip.Reason.Contains("Official", StringComparison.Ordinal));
    }

    [Fact]
    public void Paths_outside_the_library_are_skipped()
    {
        using var temp = new TempFolder();
        var library = temp.Sub("dlc");
        var outside = temp.Sub("other", "song_m.psarc");
        Directory.CreateDirectory(library);
        Fixtures.Touch(outside);

        var plan = ChangePlanner.PlanDisable(library, [
            Fixtures.Song(outside, SongFileKind.MacEnabled)
        ]);

        Assert.Empty(plan.Changes);
        Assert.Contains(plan.Skipped, skip => skip.Reason.Contains("Outside", StringComparison.Ordinal));
    }

    [Fact]
    public void Organize_targets_an_artist_folder_and_skips_a_file_already_there()
    {
        using var temp = new TempFolder();
        var library = temp.Sub("dlc");
        var loose = Path.Combine(library, "Loose", "song_m.psarc");
        var placed = Path.Combine(library, "Metric", "done_m.psarc");
        Fixtures.Touch(loose);
        Fixtures.Touch(placed);

        var plan = ChangePlanner.PlanOrganize(library, [
            Fixtures.Song(loose, SongFileKind.MacEnabled, artist: "Metric"),
            Fixtures.Song(placed, SongFileKind.MacEnabled, artist: "Metric", title: "Done")
        ]);

        var change = Assert.Single(plan.Changes);
        Assert.Equal(Path.Combine(library, "Metric", "song_m.psarc"), change.DestinationPath);
        Assert.Contains(plan.Skipped, skip => skip.Path == placed && skip.Reason.Contains("Already", StringComparison.Ordinal));
        Assert.False(File.Exists(change.DestinationPath));
    }

    [Fact]
    public void Organize_sanitizes_artist_folder_names()
    {
        Assert.Equal("AC DC", ArtistFolderNames.Sanitize("AC/DC"));
        Assert.Equal("Nope", ArtistFolderNames.Sanitize("../Nope"));
        Assert.Equal(ArtistFolderNames.UnknownArtist, ArtistFolderNames.Sanitize("   "));
        Assert.Equal(ArtistFolderNames.UnknownArtist, ArtistFolderNames.Sanitize(".."));
    }

    [Theory]
    [InlineData("AC/DC", "AC DC")]
    [InlineData("Band: Name", "Band Name")]
    public void Organize_uses_the_sanitized_folder(string artist, string folder)
    {
        using var temp = new TempFolder();
        var library = temp.Sub("dlc");
        var source = Path.Combine(library, "song_m.psarc");
        Fixtures.Touch(source);

        var plan = ChangePlanner.PlanOrganize(library, [
            Fixtures.Song(source, SongFileKind.MacEnabled, artist: artist)
        ]);

        Assert.Equal(Path.Combine(library, folder, "song_m.psarc"), Assert.Single(plan.Changes).DestinationPath);
    }
}
