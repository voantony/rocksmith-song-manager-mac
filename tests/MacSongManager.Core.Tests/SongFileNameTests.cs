using MacSongManager.Core.Songs;

namespace MacSongManager.Core.Tests;

public class SongFileNameTests
{
    [Theory]
    [InlineData("artist_title_m.psarc", SongFileKind.MacEnabled)]
    [InlineData("artist_title_m.PSARC", SongFileKind.MacEnabled)]
    [InlineData("artist_title_m.disabled.psarc", SongFileKind.MacDisabled)]
    [InlineData("artist_title_m.DISABLED.PSARC", SongFileKind.MacDisabled)]
    [InlineData("artist_title_p.psarc", SongFileKind.PcFormat)]
    [InlineData("artist_title_p.disabled.psarc", SongFileKind.PcFormat)]
    [InlineData("artist_title_p.disabled.PSARC", SongFileKind.PcFormat)]
    [InlineData("notes.txt", SongFileKind.Unmanaged)]
    [InlineData("artist_title_m.psarc.bak", SongFileKind.Unmanaged)]
    [InlineData("songs.psarc", SongFileKind.Unmanaged)]
    public void Classify_reads_platform_and_enabled_state(string name, SongFileKind expected)
    {
        Assert.Equal(expected, SongFileNames.Classify(name));
        Assert.Equal(expected, SongFileNames.Classify(Path.Combine("dlc", "Band", name)));
    }

    [Fact]
    public void Disabled_suffix_is_not_treated_as_enabled()
    {
        Assert.NotEqual(SongFileKind.MacEnabled, SongFileNames.Classify("song_m.disabled.psarc"));
        Assert.Equal("song_m.disabled.psarc", SongFileNames.DisabledFileName("song_m.disabled.psarc"));
        Assert.Equal("song_m.psarc", SongFileNames.EnabledFileName("song_m.disabled.psarc"));
    }

    [Fact]
    public void Rename_preserves_stem_and_extension_case()
    {
        Assert.Equal("Foo_m.disabled.PSARC", SongFileNames.DisabledFileName("Foo_m.PSARC"));
        Assert.Equal("Foo_m.psarc", SongFileNames.EnabledFileName("Foo_m.disabled.psarc"));
    }

    [Fact]
    public void Pc_names_are_not_rewritten_as_mac_packages()
    {
        Assert.Null(SongFileNames.EnabledFileName("song_p.psarc"));
        Assert.Null(SongFileNames.DisabledFileName("song_p.psarc"));
        Assert.Null(SongFileNames.EnabledFileName("song_p.disabled.psarc"));
        Assert.Null(SongFileNames.DisabledFileName("readme.txt"));
    }

    [Fact]
    public void Already_in_the_requested_state_returns_the_same_file_name()
    {
        Assert.Equal("song_m.psarc", SongFileNames.EnabledFileName("song_m.psarc"));
        Assert.Equal("song_m.disabled.psarc", SongFileNames.DisabledFileName("song_m.disabled.psarc"));
    }
}
