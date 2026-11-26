using System.Text;
using MacSongManager.Core.Actions;
using MacSongManager.Core.Conversion;
using MacSongManager.Core.Library;
using MacSongManager.Core.Songs;
using MacSongManager.Core.Storage;

namespace MacSongManager.Core.Tests;

public class ManifestMetadataTests
{
    [Fact]
    public async Task Scan_prefers_manifest_attributes_over_a_short_codename()
    {
        using var temp = new TempFolder();
        var library = temp.Sub("dlc");
        var path = Path.Combine(library, "bach_m.psarc");
        var json = """
            {
              "Entries": {
                "lead": {
                  "Attributes": {
                    "ArtistName": "Johann Sebastian Bach",
                    "SongName": "Jesu, Joy of Man's Desiring",
                    "AlbumName": "Anniversaries",
                    "SongYear": 1723,
                    "ArrangementName": "Lead",
                    "DLCKey": "bach",
                    "Tuning": { "string0": 0, "string1": 0, "string2": 0, "string3": 0, "string4": 0, "string5": 0 }
                  }
                },
                "bass": {
                  "Attributes": {
                    "ArtistName": "Johann Sebastian Bach",
                    "SongName": "Jesu, Joy of Man's Desiring",
                    "ArrangementName": "Bass",
                    "Tuning": { "string0": -2, "string1": 0, "string2": 0, "string3": 0, "string4": 0, "string5": 0 }
                  }
                }
              }
            }
            """;
        PsarcArchive.Write(path, new Dictionary<string, byte[]>
        {
            ["manifests/songs_dlc_bach/bach_lead.json"] = Encoding.UTF8.GetBytes(json),
            ["songs/bin/generic/bach.sng"] = Encoding.UTF8.GetBytes("not-a-chart")
        }, encryptCharts: false);

        var tagPath = temp.Sub("tags.json");
        var tags = new TagStore(tagPath);
        var scan = await new LibraryScanner(tags).ScanAsync(library, CancellationToken.None);
        var song = Assert.Single(scan.Songs);
        Assert.Equal("Johann Sebastian Bach", song.Artist);
        Assert.Equal("Jesu, Joy of Man's Desiring", song.Title);
        Assert.Equal("Anniversaries", song.Album);
        Assert.Equal(1723, song.Year);
        Assert.Equal("Lead, Bass", song.Arrangements);
        Assert.Equal("E Standard", song.Tuning);
        Assert.Equal("bach", song.DlcKey);
        Assert.Equal("manifest", song.TagSource);

        var cachedText = File.ReadAllText(tagPath).Replace(
            "\"ManifestArtist\": \"Johann Sebastian Bach\"",
            "\"ManifestArtist\": \"Cached Artist\"",
            StringComparison.Ordinal);
        File.WriteAllText(tagPath, cachedText);
        var cachedScan = await new LibraryScanner(new TagStore(tagPath)).ScanAsync(library, CancellationToken.None);
        var cachedSong = Assert.Single(cachedScan.Songs);
        Assert.Equal("Cached Artist", cachedSong.Artist);
        Assert.Equal("Anniversaries", cachedSong.Album);

        tags.Save(song.ContentHash!, "Edited Artist", "Edited Title", "user");
        var edited = await new LibraryScanner(tags).ScanAsync(library, CancellationToken.None);
        var editedSong = Assert.Single(edited.Songs);
        Assert.Equal("Edited Artist", editedSong.Artist);
        Assert.Equal("Edited Title", editedSong.Title);
        Assert.Equal("Anniversaries", editedSong.Album);
        Assert.Equal("user", editedSong.TagSource);

        var renamed = ChangePlanner.PlanFriendlyRename(library, [song]);
        Assert.Equal(
            Path.Combine(library, "Johann Sebastian Bach - Jesu, Joy of Man's Desiring_m.psarc"),
            Assert.Single(renamed.Changes).DestinationPath);
    }

    [Fact]
    public void User_edits_beat_the_manifest_and_musicbrainz_is_last()
    {
        var parsed = FilenameMetadataParser.Parse("bach_m.psarc");
        var manifest = new PackageManifest("Johann Sebastian Bach", "Jesu, Joy of Man's Desiring", null, null, null, null, "bach");
        var musicBrainz = new TagOverride { Artist = "J.S. Bach", Title = "Jesu", Source = "musicbrainz" };
        var fromManifest = LibraryScanner.Resolve(parsed, musicBrainz, manifest);
        Assert.Equal("Johann Sebastian Bach", fromManifest.Artist);
        Assert.Equal("manifest", fromManifest.Source);

        var user = new TagOverride { Artist = "Edited Artist", Title = "Edited Title", Source = "user" };
        var fromUser = LibraryScanner.Resolve(parsed, user, manifest);
        Assert.Equal("Edited Artist", fromUser.Artist);
        Assert.Equal("user", fromUser.Source);

        var suggestionOnly = LibraryScanner.Resolve(parsed, musicBrainz, manifest: null);
        Assert.Equal("J.S. Bach", suggestionOnly.Artist);
        Assert.Equal("musicbrainz", suggestionOnly.Source);
    }
}
