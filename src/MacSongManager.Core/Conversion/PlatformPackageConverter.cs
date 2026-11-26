using System.Security.Cryptography;
using System.Text;

namespace MacSongManager.Core.Conversion;

// Adapted in 2026 for Mac Song Manager from rocksmithconvert by glebb
// (GPL-3.0-or-later, https://github.com/glebb/rocksmithconvert), services.py
// _convert / _do_conversion / _swap_platform.
// Modifications: C# port; PC to Mac only; app-id rewrite and the short rename
// scheme are not used. Text path updates also cover JSON, HSAN, and XML
// entries, because those carry the same platform paths as aggregategraph.nt.
public static class PlatformPackageConverter
{
    public static void ConvertPcToMac(string sourcePath, string destinationPath)
    {
        Dictionary<string, byte[]> content;
        try
        {
            content = PsarcArchive.Read(sourcePath, decryptCharts: true);
        }
        catch (Exception first) when (first is InvalidDataException or IOException or CryptographicException)
        {
            content = PsarcArchive.Read(sourcePath, decryptCharts: false);
        }

        var converted = new Dictionary<string, byte[]>(content.Count, StringComparer.Ordinal);
        foreach (var (name, data) in content)
        {
            var body = ShouldRewriteText(name) ? RewriteText(data, toMac: true) : data;
            converted[RewritePath(name, toMac: true)] = body;
        }

        PsarcArchive.Write(destinationPath, converted, encryptCharts: true);
    }

    public static string RewritePath(string path, bool toMac) =>
        toMac
            ? path.Replace("audio/windows", "audio/mac", StringComparison.Ordinal)
                .Replace("bin/generic", "bin/macos", StringComparison.Ordinal)
            : path.Replace("audio/mac", "audio/windows", StringComparison.Ordinal)
                .Replace("bin/macos", "bin/generic", StringComparison.Ordinal);

    private static bool ShouldRewriteText(string name) =>
        name.EndsWith("aggregategraph.nt", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".hsan", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    private static byte[] RewriteText(byte[] data, bool toMac)
    {
        var text = Encoding.UTF8.GetString(data);
        text = RewritePath(text, toMac);
        text = toMac
            ? text.Replace("dx9", "macos", StringComparison.Ordinal)
            : text.Replace("macos", "dx9", StringComparison.Ordinal);
        return Encoding.UTF8.GetBytes(text);
    }
}
