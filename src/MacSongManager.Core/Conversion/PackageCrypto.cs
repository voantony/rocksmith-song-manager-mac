using System.Buffers.Binary;
using System.Security.Cryptography;
using System.IO.Compression;

namespace MacSongManager.Core.Conversion;

// Ported to C# in 2026 for Mac Song Manager from pyrocksmith
// Copyright (c) 2016 0x0L, MIT License.
// rocksmithconvert (Copyright glebb, GPL-3.0-or-later) calls this layer when
// it rebuilds a package after swapping PC and Mac paths.
// Modifications: C# instead of Python; AES-CTR is implemented with AES-ECB
// because .NET has no CTR mode. See NOTICE.
internal static class PackageCrypto
{
    private static readonly byte[] ArcKey = Convert.FromHexString(
        "C53DB23870A1A2F71CAE64061FDD0E1157309DC85204D4C5BFDF25090DF2572C");
    private static readonly byte[] ArcIv = Convert.FromHexString("E915AA018FEF71FC508132E4BB4CEB42");
    private static readonly byte[] MacKey = Convert.FromHexString(
        "9821330E34B91F70D0A48CBD625993126970CEA09192C0E6CDA676CC9838289D");
    private static readonly byte[] WinKey = Convert.FromHexString(
        "CB648DF3D12A16BF71701414E69619EC171CCA5D2A142E3E59DE7ADDA18A3A30");

    public static byte[] DecryptBom(byte[] data) => AesCfb(data, encrypt: false);

    public static byte[] EncryptBom(byte[] data) => AesCfb(data, encrypt: true);

    public static Dictionary<string, byte[]> DecryptContents(Dictionary<string, byte[]> content)
    {
        var copy = new Dictionary<string, byte[]>(content, StringComparer.Ordinal);
        foreach (var key in copy.Keys.ToList())
        {
            if (key.Contains("songs/bin/macos/", StringComparison.Ordinal))
                copy[key] = DecryptSng(copy[key], MacKey);
            else if (key.Contains("songs/bin/generic/", StringComparison.Ordinal))
                copy[key] = DecryptSng(copy[key], WinKey);
        }

        return copy;
    }

    public static Dictionary<string, byte[]> EncryptContents(Dictionary<string, byte[]> content)
    {
        var copy = new Dictionary<string, byte[]>(content, StringComparer.Ordinal);
        foreach (var key in copy.Keys.ToList())
        {
            if (key.Contains("songs/bin/macos/", StringComparison.Ordinal))
                copy[key] = EncryptSng(copy[key], MacKey);
            else if (key.Contains("songs/bin/generic/", StringComparison.Ordinal))
                copy[key] = EncryptSng(copy[key], WinKey);
        }

        return copy;
    }

    private static byte[] DecryptSng(byte[] data, byte[] key)
    {
        if (data.Length < 24)
            throw new InvalidDataException("Chart entry is too short.");

        var iv = data.AsSpan(8, 16).ToArray();
        var encrypted = data.AsSpan(24).ToArray();
        var decrypted = AesCtr(encrypted, key, iv);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(decrypted);
        var payload = decrypted.AsSpan(4);
        var plain = Inflate(payload);
        if (plain.Length != length)
            throw new InvalidDataException("Chart length does not match.");
        return plain;
    }

    private static byte[] EncryptSng(byte[] data, byte[] key)
    {
        var compressed = Deflate(data);
        var payload = new byte[4 + compressed.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)data.Length);
        compressed.CopyTo(payload.AsSpan(4));
        var iv = new byte[16];
        var encrypted = AesCtr(payload, key, iv);
        var output = new byte[8 + 16 + encrypted.Length + 56];
        BinaryPrimitives.WriteUInt32LittleEndian(output, 74);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(4), 3);
        iv.CopyTo(output.AsSpan(8));
        encrypted.CopyTo(output.AsSpan(24));
        return output;
    }

    private static byte[] AesCfb(byte[] data, bool encrypt)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CFB;
        aes.Padding = PaddingMode.None;
        aes.FeedbackSize = 128;
        aes.Key = ArcKey;
        aes.IV = ArcIv;
        using var transform = encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor();
        return transform.TransformFinalBlock(data, 0, data.Length);
    }

    private static byte[] AesCtr(byte[] data, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var encryptor = aes.CreateEncryptor(key, null);
        var counter = (byte[])iv.Clone();
        var output = new byte[data.Length];
        var keystream = new byte[16];
        for (var offset = 0; offset < data.Length; offset += 16)
        {
            if (encryptor.TransformBlock(counter, 0, 16, keystream, 0) != 16)
                throw new InvalidDataException("AES counter block failed.");
            var count = Math.Min(16, data.Length - offset);
            for (var i = 0; i < count; i++)
                output[offset + i] = (byte)(data[offset + i] ^ keystream[i]);
            Increment(counter);
        }

        return output;
    }

    private static void Increment(byte[] counter)
    {
        for (var i = counter.Length - 1; i >= 0; i--)
        {
            counter[i]++;
            if (counter[i] != 0)
                break;
        }
    }

    internal static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(data);
        return output.ToArray();
    }

    internal static byte[] Inflate(ReadOnlySpan<byte> data)
    {
        using var input = new MemoryStream(data.ToArray());
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }
}
