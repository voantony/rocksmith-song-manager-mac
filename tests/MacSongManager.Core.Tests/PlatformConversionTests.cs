using System.Text;
using MacSongManager.Core.Conversion;

namespace MacSongManager.Core.Tests;

public class PlatformConversionTests
{
    [Fact]
    public void Pc_to_mac_round_trip_rewrites_paths_and_restores_chart_bytes()
    {
        var chart = Encoding.UTF8.GetBytes("public-domain-chart-bytes");
        var graph = Encoding.UTF8.GetBytes("bin/generic dx9 audio/windows");
        var manifest = Encoding.UTF8.GetBytes("{\"path\":\"songs/bin/generic/demo.sng\"}");
        using var temp = new TempFolder();
        var source = temp.Sub("demo_p.psarc");
        var mac = temp.Sub("converted-from-pc", "demo_m.psarc");
        var back = temp.Sub("back_p.psarc");

        PsarcArchive.Write(source, new Dictionary<string, byte[]>
        {
            ["songs/bin/generic/demo.sng"] = chart,
            ["manifests/songs_dlc_demo/demo.json"] = manifest,
            ["songs/arr/demo_aggregategraph.nt"] = graph
        }, encryptCharts: true);

        PlatformPackageConverter.ConvertPcToMac(source, mac);
        Assert.True(File.Exists(source));
        var converted = PsarcArchive.Read(mac, decryptCharts: true);
        Assert.Equal(chart, converted["songs/bin/macos/demo.sng"]);
        Assert.Contains("bin/macos", Encoding.UTF8.GetString(converted["manifests/songs_dlc_demo/demo.json"]), StringComparison.Ordinal);
        Assert.Contains("macos", Encoding.UTF8.GetString(converted["songs/arr/demo_aggregategraph.nt"]), StringComparison.Ordinal);
        Assert.DoesNotContain("dx9", Encoding.UTF8.GetString(converted["songs/arr/demo_aggregategraph.nt"]), StringComparison.Ordinal);

        var restored = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (name, data) in converted)
        {
            var body = name.EndsWith(".json", StringComparison.Ordinal) || name.EndsWith(".nt", StringComparison.Ordinal)
                ? Encoding.UTF8.GetBytes(PlatformPackageConverter.RewritePath(Encoding.UTF8.GetString(data), toMac: false)
                    .Replace("macos", "dx9", StringComparison.Ordinal))
                : data;
            restored[PlatformPackageConverter.RewritePath(name, toMac: false)] = body;
        }

        PsarcArchive.Write(back, restored, encryptCharts: true);
        var roundTrip = PsarcArchive.Read(back, decryptCharts: true);
        Assert.Equal(chart, roundTrip["songs/bin/generic/demo.sng"]);
    }
}
