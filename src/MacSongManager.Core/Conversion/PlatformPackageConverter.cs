using MacSongManager.Core.Songs;

namespace MacSongManager.Core.Conversion;

// Adapted from rocksmithconvert by glebb, GPL-3.0-or-later.
// https://github.com/glebb/rocksmithconvert
// macos/Sources/RSConvertCore/Converter.swift (rebuild: re-key charts, rewrite
// the aggregate graph, copy every other entry)
// macos/Sources/RSConvertCore/Platform.swift (mapPath and mapAggregateGraph)
//
// Modified by Antony Vo (2026-11-26): ported from Swift to C#. PC custom songs
// to Mac only. Rename schemes and app-id replacement are left out. Keys come
// from the caller. Official Ubisoft packages are refused.

public static class PlatformPackageConverter
{
    public static string? CheckConvertible(string path, ConversionKeys keys)
    {
        if (OfficialFiles.IsOfficialPackage(path))
            return OfficialContent.OfficialPackageReason;

        Dictionary<string, byte[]> content;
        try
        {
            content = PsarcArchive.Read(path, keys, decryptCharts: false);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or System.Security.Cryptography.CryptographicException)
        {
            return "Could not read the package: " + ex.Message;
        }

        return OfficialContent.Refusal(path, content.Keys) ?? LayoutProblem(content.Keys);
    }

    public static void ConvertPcToMac(string sourcePath, string destinationPath, ConversionKeys keys)
    {
        if (OfficialFiles.IsOfficialPackage(sourcePath))
            throw new ConversionException(OfficialContent.OfficialPackageReason);

        var content = PsarcArchive.Read(sourcePath, keys, decryptCharts: false);
        var problem = OfficialContent.Refusal(sourcePath, content.Keys) ?? LayoutProblem(content.Keys);
        if (problem is not null)
            throw new ConversionException(problem);

        var converted = new Dictionary<string, byte[]>(content.Count, StringComparer.Ordinal);
        foreach (var (name, data) in content)
        {
            var body = data;
            if (name.EndsWith(".sng", StringComparison.Ordinal)
                && name.Contains("songs/bin/generic/", StringComparison.Ordinal)
                && data.Length > 0)
            {
                try
                {
                    body = ChartCipher.Rekey(data, keys.PcChartKey, keys.MacChartKey);
                }
                catch (InvalidDataException ex)
                {
                    throw new ConversionException($"Could not re-key {name}: {ex.Message}");
                }
            }
            else if (name.EndsWith("aggregategraph.nt", StringComparison.Ordinal))
            {
                body = MapAggregateGraph(data);
            }

            converted[MapPath(name)] = body;
        }

        WriteAtomically(destinationPath, converted, keys);
    }

    public static string MapPath(string path) =>
        path.Replace("audio/windows", "audio/mac", StringComparison.Ordinal)
            .Replace("bin/generic", "bin/macos", StringComparison.Ordinal);

    private static string? LayoutProblem(IEnumerable<string> names)
    {
        var mac = 0;
        var pc = 0;
        foreach (var name in names)
        {
            if (name.Contains("bin/macos/", StringComparison.Ordinal) || name.Contains("audio/mac/", StringComparison.Ordinal))
                mac++;
            if (name.Contains("bin/generic/", StringComparison.Ordinal) || name.Contains("audio/windows/", StringComparison.Ordinal))
                pc++;
        }

        if (pc > 0 && mac == 0)
            return null;
        if (mac > 0 && pc == 0)
            return "This package is already a Mac layout.";
        return "Could not tell whether this is a PC package.";
    }

    private static byte[] MapAggregateGraph(byte[] data)
    {
        data = ReplaceAscii(data, "audio/windows", "audio/mac");
        data = ReplaceAscii(data, "bin/generic", "bin/macos");
        data = ReplaceAscii(data, "dx9", "macos");
        return data;
    }

    private static byte[] ReplaceAscii(byte[] data, string target, string replacement)
    {
        var from = System.Text.Encoding.ASCII.GetBytes(target);
        var to = System.Text.Encoding.ASCII.GetBytes(replacement);
        if (from.Length == 0 || data.Length < from.Length)
            return data;

        using var output = new MemoryStream(data.Length);
        var index = 0;
        while (index < data.Length)
        {
            if (index <= data.Length - from.Length && data.AsSpan(index, from.Length).SequenceEqual(from))
            {
                output.Write(to);
                index += from.Length;
            }
            else
            {
                output.WriteByte(data[index]);
                index++;
            }
        }

        return output.ToArray();
    }

    private static void WriteAtomically(string destinationPath, Dictionary<string, byte[]> content, ConversionKeys keys)
    {
        var full = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = Path.Combine(
            directory ?? Directory.GetCurrentDirectory(),
            "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            PsarcArchive.Write(temp, content, keys, encryptCharts: false);
            File.Move(temp, full);
        }
        catch
        {
            if (File.Exists(temp))
                File.Delete(temp);
            throw;
        }
    }
}
