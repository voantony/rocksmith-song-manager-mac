using MacSongManager.Core.Songs;

namespace MacSongManager.Core.Tests;

public class DuplicateDetectorTests
{
    [Fact]
    public void Groups_normalized_artist_and_title()
    {
        var songs = new[]
        {
            new SongIdentity("/dlc/a_m.psarc", "The Rolling Stones", "Paint It, Black", "hash-a"),
            new SongIdentity("/dlc/b_m.psarc", "Rolling Stones", "Paint It Black", "hash-b"),
            new SongIdentity("/dlc/c_m.psarc", "José González", "Heartbeats", "hash-c"),
            new SongIdentity("/dlc/d_m.psarc", "Jose Gonzalez", "Heartbeats", "hash-d")
        };

        var groups = DuplicateDetector.Find(songs)
            .Where(group => group.Reason == DuplicateReason.ArtistTitle)
            .ToList();

        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, group => group.Paths.SequenceEqual(new[] { "/dlc/a_m.psarc", "/dlc/b_m.psarc" }));
        Assert.Contains(groups, group => group.Paths.SequenceEqual(new[] { "/dlc/c_m.psarc", "/dlc/d_m.psarc" }));
    }

    [Fact]
    public void Groups_identical_file_hashes()
    {
        var songs = new[]
        {
            new SongIdentity("/dlc/a_m.psarc", "One Band", "One Song", "abc123"),
            new SongIdentity("/dlc/b_m.psarc", "Other", "Different", "ABC123"),
            new SongIdentity("/dlc/c_m.psarc", "Third", "Track", "fff")
        };

        var group = Assert.Single(DuplicateDetector.Find(songs), item => item.Reason == DuplicateReason.ContentHash);
        Assert.Equal(new[] { "/dlc/a_m.psarc", "/dlc/b_m.psarc" }, group.Paths);
    }

    [Fact]
    public void Punctuation_and_band_spelling_share_a_key()
    {
        Assert.Equal(TextNormalizer.Normalize("AC/DC"), TextNormalizer.Normalize("A.C.D.C"));
        Assert.Equal(TextNormalizer.Normalize("Guns N' Roses"), TextNormalizer.Normalize("Guns N Roses"));
        Assert.Equal(
            TextNormalizer.ArtistTitleKey("The Beatles", "Let It Be"),
            TextNormalizer.ArtistTitleKey("Beatles", "Let It Be"));
    }

    [Fact]
    public void Blank_names_and_blank_hashes_do_not_form_a_group()
    {
        var songs = new[]
        {
            new SongIdentity("/dlc/a_m.psarc", null, " ", null),
            new SongIdentity("/dlc/b_m.psarc", "", "", "  "),
            new SongIdentity("/dlc/c_m.psarc", "Solo", "Song", "only")
        };

        Assert.Empty(DuplicateDetector.Find(songs));
    }

    [Fact]
    public void A_song_can_carry_both_reasons()
    {
        var songs = new[]
        {
            new SongIdentity("/dlc/a_m.psarc", "Band", "Title", "same"),
            new SongIdentity("/dlc/b_m.psarc", "Band", "Title", "same")
        };

        var reasons = DuplicateDetector.ReasonsByPath(songs);
        Assert.Equal(
            new[] { DuplicateReason.ArtistTitle, DuplicateReason.ContentHash },
            reasons["/dlc/a_m.psarc"]);
    }
}
