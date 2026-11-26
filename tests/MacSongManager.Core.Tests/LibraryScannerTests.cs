using MacSongManager.Core.Actions;
using MacSongManager.Core.Library;
using MacSongManager.Core.Safety;
using MacSongManager.Core.Songs;
using MacSongManager.Core.Storage;
using MacSongManager.Import;

namespace MacSongManager.Core.Tests;

public class FilenameMetadataTests
{
    [Theory]
    [InlineData("Artist_Title_v1_m.psarc", "Artist", "Title", "1", null)]
    [InlineData("Artist_Title_v1_m.disabled.psarc", "Artist", "Title", "1", null)]
    [InlineData("Artist_Title_v1.2_p.psarc", "Artist", "Title", "1.2", null)]
    [InlineData("Artist_Title_v1_by_Ada_Lovelace_m.psarc", "Artist", "Title", "1", "Ada Lovelace")]
    [InlineData("Metric - Gold Guns Girls_v4_m.psarc", "Metric", "Gold Guns Girls", "4", null)]
    [InlineData("The_Rolling_Stones__Paint_It_Black_v2_m.PSARC", "The Rolling Stones", "Paint It Black", "2", null)]
    public void Parses_documented_file_names(string name, string artist, string title, string version, string? author)
    {
        var parsed = FilenameMetadataParser.Parse(name);
        Assert.Equal(artist, parsed.Artist);
        Assert.Equal(title, parsed.Title);
        Assert.Equal(version, parsed.Version);
        Assert.Equal(author, parsed.Author);
    }

    [Fact]
    public void A_name_without_a_separator_is_only_a_title()
    {
        var parsed = FilenameMetadataParser.Parse("Untitled_m.psarc");
        Assert.Null(parsed.Artist);
        Assert.Equal("Untitled", parsed.Title);
        Assert.Null(parsed.Version);
    }
}

public class LibraryScannerTests
{
    [Fact]
    public async Task Scan_uses_file_names_and_hashes_and_leaves_official_files_alone()
    {
        using var temp = new TempFolder();
        var library = temp.Sub("dlc");
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        Fixtures.Touch(Path.Combine(library, "Loose", "Metric_Gold_Guns_Girls_v4_by_Ada_m.psarc"), bytes);
        Fixtures.Touch(Path.Combine(library, "Copy", "Metric_Gold_Guns_Girls_v1_m.disabled.psarc"), bytes);
        Fixtures.Touch(Path.Combine(library, "The_National__Bloodbuzz_Ohio_v2_p.psarc"), [9, 9, 9]);
        var official = Path.Combine(library, "songs.psarc");
        var compatibility = Path.Combine(library, "rs1compatibilitydisc_m.psarc");
        Fixtures.Touch(official, [7, 7, 7, 7]);
        Fixtures.Touch(compatibility, [8, 8]);
        Fixtures.Touch(Path.Combine(library, "notes.psarc"), [3]);

        var scan = await new LibraryScanner().ScanAsync(library, CancellationToken.None);

        Assert.Equal(3, scan.Songs.Count);
        Assert.Equal(2, scan.SkippedOfficial);
        Assert.Equal(1, scan.IgnoredOther);
        Assert.Equal(new byte[] { 7, 7, 7, 7 }, await File.ReadAllBytesAsync(official));
        Assert.Equal(new byte[] { 8, 8 }, await File.ReadAllBytesAsync(compatibility));

        var loose = scan.Songs.Single(song => song.FileName.StartsWith("Metric_Gold", StringComparison.Ordinal) && song.FileKind == SongFileKind.MacEnabled);
        Assert.Equal("Metric", loose.Artist);
        Assert.Equal("Gold Guns Girls", loose.Title);
        Assert.Equal("4", loose.Version);
        Assert.Equal("Ada", loose.Author);
        Assert.False(string.IsNullOrWhiteSpace(loose.ContentHash));
        Assert.True(loose.IsDuplicate);
        Assert.Contains(DuplicateReason.ArtistTitle, loose.DuplicateReasons);
        Assert.Contains(DuplicateReason.ContentHash, loose.DuplicateReasons);
        Assert.Equal(Path.Combine("Loose", "Metric_Gold_Guns_Girls_v4_by_Ada_m.psarc"), loose.RelativePath);

        var pc = scan.Songs.Single(song => song.FileKind == SongFileKind.PcFormat);
        Assert.Equal("The National", pc.Artist);
        Assert.Equal("Bloodbuzz Ohio", pc.Title);
        Assert.DoesNotContain(scan.Songs, song => song.FileName.Contains("songs", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(scan.Songs, song => song.FileName.Contains("compatibility", StringComparison.OrdinalIgnoreCase));

        var plan = ChangePlanner.PlanDisable(library, scan.Songs);
        Assert.Contains(plan.Changes, change => change.SourcePath == loose.FullPath);
        Assert.DoesNotContain(plan.Changes, change => change.SourcePath == official || change.DestinationPath == official);
    }

    [Fact]
    public async Task Scan_does_not_follow_a_symlink_out_of_the_library()
    {
        using var temp = new TempFolder();
        var library = temp.Sub("dlc");
        Directory.CreateDirectory(library);
        var outside = temp.Sub("outside", "Secret_Hidden_v1_m.psarc");
        Fixtures.Touch(outside);
        Directory.CreateSymbolicLink(Path.Combine(library, "linked"), Path.GetDirectoryName(outside)!);

        var scan = await new LibraryScanner().ScanAsync(library, CancellationToken.None);

        Assert.DoesNotContain(scan.Songs, song => song.Title == "Hidden");
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void Real_path_rejects_a_prefix_lookalike_and_a_file_symlink()
    {
        using var temp = new TempFolder();
        var library = temp.Sub("lib");
        var lookalike = temp.Sub("lib2");
        Directory.CreateDirectory(library);
        Directory.CreateDirectory(lookalike);
        var outside = Path.Combine(lookalike, "song_m.psarc");
        Fixtures.Touch(outside);
        var link = Path.Combine(library, "song_m.psarc");
        File.CreateSymbolicLink(link, outside);

        Assert.False(PathGuard.IsStrictlyInside(library, outside));
        Assert.False(PathGuard.IsStrictlyInside(library, link));
        Assert.True(PathGuard.IsStrictlyInside(library, Path.Combine(library, "Band", "new_m.psarc")));
    }

    [Fact]
    public void Apply_refuses_to_rename_songs_psarc()
    {
        using var temp = new TempFolder();
        var library = temp.Sub("dlc");
        var source = Path.Combine(library, "songs.psarc");
        Fixtures.Touch(source, [4, 4, 4]);
        var destination = Path.Combine(library, "songs_m.disabled.psarc");
        var plan = new ChangePlan(ChangeKind.Disable, [
            new PlannedChange(ChangeKind.Disable, source, destination, "Disable")
        ], []);

        var result = new ChangeApplier(new JsonlChangeLog(temp.Sub("log.jsonl")))
            .Apply(library, temp.Sub("trash"), plan, dryRun: false);

        Assert.False(result.Succeeded);
        Assert.Equal(new byte[] { 4, 4, 4 }, File.ReadAllBytes(source));
        Assert.False(File.Exists(destination));
    }
}

public class SupportTests
{
    [Fact]
    public void Default_library_path_matches_the_steam_dlc_folder()
    {
        var home = Path.Combine("Users", "ada");
        Assert.Equal(
            Path.Combine(home, "Library", "Application Support", "Steam", "steamapps", "common", "Rocksmith2014", "dlc"),
            AppPaths.DefaultDlcFolder(home));
    }

    [Fact]
    public async Task Audio_import_boundary_is_present_and_unavailable()
    {
        var importer = ChartImporters.Default;
        Assert.False(importer.IsAvailable);
        Assert.Contains(".flac", importer.SupportedExtensions);
        var result = await importer.ImportAsync(
            new ChartImportRequest("song.flac", "/dlc"),
            progress: null,
            CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Null(result.ProducedPackagePath);
    }
}
