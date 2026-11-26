using MacSongManager.Core.Songs;

namespace MacSongManager.Core.Tests;

public class FilenameParsingTests
{
    [Theory]
    [InlineData("Creed-OneLastBreath_m.psarc", "Creed", "One Last Breath")]
    [InlineData("TheProclaimers-ImGonnaBe500Miles_m.psarc", "The Proclaimers", "I'm Gonna Be 500 Miles")]
    [InlineData("ThePresidentsoftheUnitedStatesofAmerica-Candy_m.psarc", "The Presidents of the United States of America", "Candy")]
    [InlineData("LudwigvanBeethoven-MoonlightSonataAdagioSostenuto_m.psarc", "Ludwig van Beethoven", "Moonlight Sonata Adagio Sostenuto")]
    public void Hyphenated_camel_case_names_gain_spaces(string name, string artist, string title)
    {
        var parsed = FilenameMetadataParser.Parse(name);
        Assert.Equal(artist, parsed.Artist);
        Assert.Equal(title, parsed.Title);
        Assert.Equal(ParseConfidence.Medium, parsed.Confidence);
    }

    [Fact]
    public void Official_short_names_use_the_built_in_map()
    {
        var pumpkin = FilenameMetadataParser.Parse("1979_m.psarc");
        Assert.Equal("The Smashing Pumpkins", pumpkin.Artist);
        Assert.Equal("1979", pumpkin.Title);
        Assert.Equal(ParseConfidence.High, pumpkin.Confidence);

        var who = FilenameMetadataParser.Parse("babaoriley_m.psarc");
        Assert.Equal("The Who", who.Artist);
        Assert.Equal("Baba O'Riley", who.Title);
    }

    [Fact]
    public void Toolkit_names_keep_version_and_drop_the_dd_marker()
    {
        var parsed = FilenameMetadataParser.Parse("Artist_Title_v1_DD_m.psarc");
        Assert.Equal("Artist", parsed.Artist);
        Assert.Equal("Title", parsed.Title);
        Assert.Equal("1", parsed.Version);
        Assert.Equal(ParseConfidence.High, parsed.Confidence);
    }

    [Fact]
    public void A_blob_without_a_separator_is_low_confidence()
    {
        var parsed = FilenameMetadataParser.Parse("notasongname_m.psarc");
        Assert.Null(parsed.Artist);
        Assert.Equal(ParseConfidence.Low, parsed.Confidence);
    }

    [Fact]
    public void Friendly_rename_keeps_the_platform_suffix()
    {
        Assert.Equal(
            "Creed - One Last Breath_m.disabled.psarc",
            FriendlyFileNames.Build("Creed", "One Last Breath", "Creed-OneLastBreath_m.disabled.psarc"));
        Assert.Equal("AC DC", FriendlyFileNames.Sanitize("AC/DC"));
    }
}
