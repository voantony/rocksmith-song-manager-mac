using System.Text;
using MacSongManager.Core.Actions;
using MacSongManager.Core.Conversion;
using MacSongManager.Core.Songs;
using MacSongManager.Core.Storage;

namespace MacSongManager.Core.Tests;

public class PlatformConversionTests
{
    [Fact]
    public void Chart_rekey_round_trips_with_generated_keys_and_keeps_the_signature()
    {
        var keys = GeneratedKeys();
        var chart = Encoding.UTF8.GetBytes("public-domain-chart-bytes-that-are-long-enough-to-span-blocks");
        var iv = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        var wrapped = ChartCipher.Encrypt(chart, keys.PcChartKey, iv);
        wrapped[^1] = 0x5A;
        Assert.Equal(iv, wrapped.AsSpan(8, 16).ToArray());

        var mac = ChartCipher.Rekey(wrapped, keys.PcChartKey, keys.MacChartKey);
        Assert.Equal(0x5A, mac[^1]);
        Assert.Equal(chart, ChartCipher.Decrypt(mac, keys.MacChartKey));

        var back = ChartCipher.Rekey(mac, keys.MacChartKey, keys.PcChartKey);
        Assert.Equal(wrapped, back);
        Assert.Equal(chart, ChartCipher.Decrypt(back, keys.PcChartKey));
    }

    [Fact]
    public void Pc_to_mac_round_trip_rewrites_paths_and_restores_chart_bytes()
    {
        var keys = GeneratedKeys();
        var chart = Encoding.UTF8.GetBytes("public-domain-chart-bytes");
        var graph = Encoding.UTF8.GetBytes("\uFEFF<tag> \"dx9\".\n/songs/bin/generic/demo.sng\naudio/windows\n");
        var manifest = Encoding.UTF8.GetBytes("{\"path\":\"songs/bin/generic/demo.sng\"}");
        var audio = new byte[] { 1, 2, 3, 4, 5, 9, 8, 7 };
        using var temp = new TempFolder();
        var source = temp.Sub("demo_p.psarc");
        var mac = temp.Sub("converted-from-pc", "demo_m.psarc");

        WriteSong(source, keys, chart, graph, manifest, audio, includeToolkit: true);
        var before = File.ReadAllBytes(source);

        PlatformPackageConverter.ConvertPcToMac(source, mac, keys);
        Assert.Equal(before, File.ReadAllBytes(source));

        var converted = PsarcArchive.Read(mac, keys, decryptCharts: true);
        Assert.Equal(chart, converted["songs/bin/macos/demo.sng"]);
        Assert.Equal(audio, converted["audio/mac/1.wem"]);
        Assert.Equal(manifest, converted["manifests/songs_dlc_demo/demo.hsan"]);
        var graphText = Encoding.UTF8.GetString(converted["songs/arr/demo_aggregategraph.nt"]);
        Assert.StartsWith("\uFEFF", graphText, StringComparison.Ordinal);
        Assert.Contains("bin/macos", graphText, StringComparison.Ordinal);
        Assert.Contains("audio/mac", graphText, StringComparison.Ordinal);
        Assert.DoesNotContain("dx9", graphText, StringComparison.Ordinal);
        Assert.DoesNotContain("generic", graphText, StringComparison.Ordinal);
        Assert.DoesNotContain("windows", graphText, StringComparison.Ordinal);

        var restored = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (name, data) in converted)
        {
            var body = name.EndsWith("aggregategraph.nt", StringComparison.Ordinal)
                ? Encoding.UTF8.GetBytes(graphText
                    .Replace("audio/mac", "audio/windows", StringComparison.Ordinal)
                    .Replace("bin/macos", "bin/generic", StringComparison.Ordinal)
                    .Replace("macos", "dx9", StringComparison.Ordinal))
                : data;
            restored[name
                .Replace("audio/mac", "audio/windows", StringComparison.Ordinal)
                .Replace("bin/macos", "bin/generic", StringComparison.Ordinal)] = body;
        }

        var back = temp.Sub("back_p.psarc");
        PsarcArchive.Write(back, restored, keys, encryptCharts: true);
        var roundTrip = PsarcArchive.Read(back, keys, decryptCharts: true);
        Assert.Equal(chart, roundTrip["songs/bin/generic/demo.sng"]);
        Assert.Equal(audio, roundTrip["audio/windows/1.wem"]);
    }

    [Fact]
    public void Conversion_is_off_unless_the_setting_is_turned_on()
    {
        var fresh = new LibrarySettings();
        Assert.False(fresh.ConvertPcCustomSongs);
        Assert.Equal(string.Empty, fresh.ConversionKeysPath);

        using var temp = new TempFolder();
        var settingsPath = temp.Sub("settings.json");
        SettingsStore.Save(settingsPath, fresh);
        Assert.False(SettingsStore.Load(settingsPath).ConvertPcCustomSongs);

        File.WriteAllText(settingsPath, """{"LibraryPath":"/tmp/dlc"}""");
        Assert.False(SettingsStore.Load(settingsPath).ConvertPcCustomSongs);

        var keys = GeneratedKeys();
        var library = temp.Sub("dlc");
        var source = Path.Combine(library, "Demo_p.psarc");
        WriteSong(source, keys, Encoding.UTF8.GetBytes("chart"), "dx9 bin/generic audio/windows"u8.ToArray(), "plain"u8.ToArray(), [9], includeToolkit: true);
        var song = Fixtures.Song(source, SongFileKind.PcFormat);
        var off = ChangePlanner.PlanConvert(library, [song], conversionEnabled: false, keys);
        Assert.Empty(off.Changes);
        Assert.Contains("custom songs only", Assert.Single(off.Skipped).Reason, StringComparison.OrdinalIgnoreCase);

        var destination = Path.Combine(library, "converted-from-pc", "Demo_m.psarc");
        var forced = new ChangePlan(
            ChangeKind.Convert,
            [new PlannedChange(ChangeKind.Convert, source, destination, "Convert")],
            []);
        var applied = new ChangeApplier(new JsonlChangeLog(temp.Sub("log.jsonl")))
            .Apply(library, temp.Sub("trash"), forced, dryRun: false, convertPcCustomSongs: false, keys);
        Assert.False(applied.Succeeded);
        Assert.Contains("custom songs only", applied.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(destination));

        fresh.ConvertPcCustomSongs = true;
        SettingsStore.Save(settingsPath, fresh);
        Assert.True(SettingsStore.Load(settingsPath).ConvertPcCustomSongs);
    }

    [Fact]
    public void Convert_refuses_songs_psarc_and_packages_that_look_official()
    {
        var keys = GeneratedKeys();
        var chart = Encoding.UTF8.GetBytes("chart");
        var graph = "dx9 bin/generic audio/windows"u8.ToArray();
        using var temp = new TempFolder();
        var library = temp.Sub("dlc");

        var songsArchive = Path.Combine(library, "songs.psarc");
        WriteSong(songsArchive, keys, chart, graph, "plain"u8.ToArray(), [1], includeToolkit: true);
        var songsError = Assert.Throws<ConversionException>(() =>
            PlatformPackageConverter.ConvertPcToMac(songsArchive, Path.Combine(library, "out-songs.psarc"), keys));
        Assert.Contains("songs.psarc", songsError.Message, StringComparison.OrdinalIgnoreCase);

        var gears = Path.Combine(library, "gears.psarc");
        WriteSong(gears, keys, chart, graph, "plain"u8.ToArray(), [1], includeToolkit: true);
        Assert.Throws<ConversionException>(() =>
            PlatformPackageConverter.ConvertPcToMac(gears, Path.Combine(library, "out-gears.psarc"), keys));

        var noMarker = Path.Combine(library, "Artist_Title_p.psarc");
        WriteSong(noMarker, keys, chart, graph, "plain"u8.ToArray(), [1], includeToolkit: false);
        var markerError = Assert.Throws<ConversionException>(() =>
            PlatformPackageConverter.ConvertPcToMac(noMarker, Path.Combine(library, "out-marker.psarc"), keys));
        Assert.Contains("toolkit.version", markerError.Message, StringComparison.OrdinalIgnoreCase);

        var compat = Path.Combine(library, "Custom_Song_p.psarc");
        WriteSong(compat, keys, chart, graph, "plain"u8.ToArray(), [1], includeToolkit: true, entryName: "audio/windows/compatibility/1.wem");
        var compatError = Assert.Throws<ConversionException>(() =>
            PlatformPackageConverter.ConvertPcToMac(compat, Path.Combine(library, "out-compat.psarc"), keys));
        Assert.Contains("Compatibility", compatError.Message, StringComparison.Ordinal);

        var song = Fixtures.Song(noMarker, SongFileKind.PcFormat);
        var plan = ChangePlanner.PlanConvert(library, [song], conversionEnabled: true, keys);
        Assert.Empty(plan.Changes);
        Assert.Contains("toolkit.version", Assert.Single(plan.Skipped).Reason, StringComparison.OrdinalIgnoreCase);

        var official = Fixtures.Song(songsArchive, SongFileKind.Unmanaged);
        var officialPlan = ChangePlanner.PlanConvert(library, [official], conversionEnabled: true, keys);
        Assert.Empty(officialPlan.Changes);
        Assert.Contains("Official", Assert.Single(officialPlan.Skipped).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_keys_file_explains_that_the_app_does_not_include_them()
    {
        using var temp = new TempFolder();
        var missing = temp.Sub("conversion-keys.txt");
        var error = Assert.Throws<ConversionException>(() => ConversionKeys.Load(missing));
        Assert.Contains("does not include these keys", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(missing, error.Message, StringComparison.Ordinal);

        var path = temp.Sub("keys.txt");
        GeneratedKeys().Write(path);
        var loaded = ConversionKeys.Load(path);
        Assert.Equal(GeneratedKeys().TocKey, loaded.TocKey);
        Assert.Equal(GeneratedKeys().PcChartKey, loaded.PcChartKey);
        Assert.Equal(GeneratedKeys().MacChartKey, loaded.MacChartKey);
    }

    private static ConversionKeys GeneratedKeys() => new(
        tocKey: Enumerable.Range(0, 32).Select(i => (byte)(i + 1)).ToArray(),
        tocIv: Enumerable.Range(0, 16).Select(i => (byte)(i + 40)).ToArray(),
        macChartKey: Enumerable.Range(0, 32).Select(i => (byte)(i + 70)).ToArray(),
        pcChartKey: Enumerable.Range(0, 32).Select(i => (byte)(i + 110)).ToArray());

    private static void WriteSong(
        string path,
        ConversionKeys keys,
        byte[] chart,
        byte[] graph,
        byte[] manifest,
        byte[] audio,
        bool includeToolkit,
        string entryName = "audio/windows/1.wem")
    {
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["songs/bin/generic/demo.sng"] = chart,
            ["manifests/songs_dlc_demo/demo.hsan"] = manifest,
            ["songs/arr/demo_aggregategraph.nt"] = graph,
            [entryName] = audio
        };
        if (includeToolkit)
            entries["toolkit.version"] = "custom-song"u8.ToArray();
        PsarcArchive.Write(path, entries, keys, encryptCharts: true);
    }
}
