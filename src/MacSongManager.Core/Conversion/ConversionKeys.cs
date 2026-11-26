namespace MacSongManager.Core.Conversion;

/// <summary>
/// AES material for a PC-to-Mac custom-song conversion.
/// The bytes are loaded from a file the user supplies. This program does not
/// include them. The names match rocksmithconvert's Crypto.swift Keys members:
/// tocKey, tocIV, sngMac, and sngPC.
/// </summary>
public sealed class ConversionKeys
{
    public const string DisabledMessage =
        "PC-to-Mac conversion is turned off. It is for custom songs only.";

    public ConversionKeys(byte[] tocKey, byte[] tocIv, byte[] macChartKey, byte[] pcChartKey)
    {
        TocKey = Require(tocKey, 32, "tocKey");
        TocIv = Require(tocIv, 16, "tocIV");
        MacChartKey = Require(macChartKey, 32, "sngMac");
        PcChartKey = Require(pcChartKey, 32, "sngPC");
    }

    public byte[] TocKey { get; }
    public byte[] TocIv { get; }
    public byte[] MacChartKey { get; }
    public byte[] PcChartKey { get; }

    public static string ResolvePath(string? configured, string? home = null) =>
        string.IsNullOrWhiteSpace(configured)
            ? Storage.AppPaths.DefaultConversionKeysPath(home)
            : configured;

    public static ConversionKeys Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new ConversionException(
                $"Conversion keys file was not found at {path}. Mac Song Manager does not include these keys. "
                + "Create a text file with tocKey, tocIV, sngMac, and sngPC as hexadecimal, "
                + "then choose it in the sidebar or save it at that path.");
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            throw new ConversionException($"Conversion keys file could not be read: {ex.Message}");
        }

        var values = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var lineNumber = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNumber++;
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var split = line.IndexOfAny(['=', ':']);
            if (split <= 0)
                throw new ConversionException($"Conversion keys file {path} line {lineNumber} is not name=hex.");

            var name = line[..split].Trim();
            var hex = line[(split + 1)..].Trim();
            if (!Known(name))
                throw new ConversionException($"Conversion keys file {path} has an unknown entry \"{name}\". Expected tocKey, tocIV, sngMac, and sngPC.");

            if (values.ContainsKey(name))
                throw new ConversionException($"Conversion keys file {path} repeats {name}.");

            values[name] = ParseHex(path, name, hex);
        }

        return new ConversionKeys(
            Take(values, "tocKey", 32, path),
            Take(values, "tocIV", 16, path),
            Take(values, "sngMac", 32, path),
            Take(values, "sngPC", 32, path));
    }

    public void Write(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var body = string.Join('\n',
        [
            "# Generated test or local keys. Mac Song Manager does not ship these values.",
            "tocKey=" + Convert.ToHexString(TocKey),
            "tocIV=" + Convert.ToHexString(TocIv),
            "sngMac=" + Convert.ToHexString(MacChartKey),
            "sngPC=" + Convert.ToHexString(PcChartKey),
            ""
        ]);
        File.WriteAllText(path, body);
    }

    private static byte[] Take(Dictionary<string, byte[]> values, string name, int expected, string path)
    {
        if (!values.TryGetValue(name, out var bytes))
            throw new ConversionException($"Conversion keys file {path} is missing {name}.");
        if (bytes.Length != expected)
            throw new ConversionException($"Conversion keys file {path} has {name} of {bytes.Length} bytes; expected {expected}.");
        return bytes;
    }

    private static byte[] ParseHex(string path, string name, string hex)
    {
        var cleaned = hex.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        if (cleaned.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[2..];
        if (cleaned.Length == 0 || cleaned.Length % 2 != 0)
            throw new ConversionException($"Conversion keys file {path} has an unreadable {name} value. Expected an even number of hex digits.");

        try
        {
            return Convert.FromHexString(cleaned);
        }
        catch (FormatException)
        {
            throw new ConversionException($"Conversion keys file {path} has an unreadable {name} value. Expected hexadecimal.");
        }
    }

    private static bool Known(string name) =>
        name.Equals("tocKey", StringComparison.OrdinalIgnoreCase)
        || name.Equals("tocIV", StringComparison.OrdinalIgnoreCase)
        || name.Equals("sngMac", StringComparison.OrdinalIgnoreCase)
        || name.Equals("sngPC", StringComparison.OrdinalIgnoreCase);

    private static byte[] Require(byte[] value, int expected, string name)
    {
        if (value.Length != expected)
            throw new ArgumentException($"{name} must be {expected} bytes.", nameof(value));
        return (byte[])value.Clone();
    }
}

public sealed class ConversionException : Exception
{
    public ConversionException(string message) : base(message)
    {
    }
}
