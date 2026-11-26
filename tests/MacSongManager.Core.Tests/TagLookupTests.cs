using MacSongManager.Core.Actions;
using MacSongManager.Core.Songs;
using MacSongManager.Core.Tags;

namespace MacSongManager.Core.Tests;

public class TagLookupTests
{
    [Fact]
    public async Task Pacer_spaces_calls_one_second_apart()
    {
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var delays = new List<TimeSpan>();
        var pacer = new RequestPacer(TimeSpan.FromSeconds(1), () => now, (span, _) =>
        {
            delays.Add(span);
            now = now.Add(span);
            return Task.CompletedTask;
        });

        await pacer.WaitTurnAsync(CancellationToken.None);
        await pacer.WaitTurnAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(delays));
    }

    [Fact]
    public void Friendly_rename_skips_low_confidence_until_tags_are_edited()
    {
        using var temp = new TempFolder();
        var library = temp.Sub("dlc");
        var source = Path.Combine(library, "notasongname_m.psarc");
        Fixtures.Touch(source);
        var low = Fixtures.Song(source, SongFileKind.MacEnabled, artist: null, title: "notasongname") with
        {
            Confidence = ParseConfidence.Low,
            TagSource = "filename"
        };
        var skipped = ChangePlanner.PlanFriendlyRename(library, [low]);
        Assert.Empty(skipped.Changes);

        var edited = low with { Artist = "Ada", Title = "Song", TagSource = "user", Confidence = ParseConfidence.High };
        var plan = ChangePlanner.PlanFriendlyRename(library, [edited]);
        Assert.Equal(Path.Combine(library, "Ada - Song_m.psarc"), Assert.Single(plan.Changes).DestinationPath);
    }
}
