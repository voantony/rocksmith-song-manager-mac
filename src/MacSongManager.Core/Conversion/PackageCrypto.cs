using System.Buffers.Binary;
using System.Security.Cryptography;
using System.IO.Compression;

namespace MacSongManager.Core.Conversion;

// Adapted from rocksmithconvert by glebb, GPL-3.0-or-later.
// https://github.com/glebb/rocksmithconvert
// macos/Sources/RSConvertCore/Crypto.swift
//   AES-256 built on an ECB primitive. CFB128 encrypts the archive table.
//   CTR encrypts chart entries. The counter is a 128-bit big-endian integer.
// macos/Sources/RSConvertCore/Platform.swift
//   Chart container: magic 74, flags 3, 16-byte IV, ciphertext, 56-byte signature.
//   Re-key xors the source keystream off and the target keystream on.
//   The compressed chart, IV, and signature stay in place.
//
// Modified by Antony Vo (2026-11-26): ported from Swift to C#. The key and IV
// bytes that Crypto.swift embeds are not in this file. Callers pass
// ConversionKeys loaded from a file the user supplies.

internal static class PackageCrypto
{
    public static byte[] DecryptToc(byte[] data, ConversionKeys keys)
    {
        var copy = (byte[])data.Clone();
        Cfb(copy, keys.TocKey, keys.TocIv, decrypt: true);
        return copy;
    }

    public static byte[] EncryptToc(byte[] data, ConversionKeys keys)
    {
        var copy = (byte[])data.Clone();
        Cfb(copy, keys.TocKey, keys.TocIv, decrypt: false);
        return copy;
    }

    public static Dictionary<string, byte[]> DecryptCharts(Dictionary<string, byte[]> content, ConversionKeys keys)
    {
        var copy = new Dictionary<string, byte[]>(content.Count, StringComparer.Ordinal);
        foreach (var (name, data) in content)
            copy[name] = DecryptChartIfNeeded(name, data, keys);
        return copy;
    }

    public static Dictionary<string, byte[]> EncryptCharts(Dictionary<string, byte[]> content, ConversionKeys keys)
    {
        var copy = new Dictionary<string, byte[]>(content.Count, StringComparer.Ordinal);
        foreach (var (name, data) in content)
            copy[name] = EncryptChartIfNeeded(name, data, keys);
        return copy;
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

    internal static void Cfb(byte[] data, byte[] key, byte[] iv, bool decrypt)
    {
        using var aes = NewEcb(key);
        using var encryptor = aes.CreateEncryptor();
        var feedback = (byte[])iv.Clone();
        var keystream = new byte[16];
        var offset = 0;
        while (offset < data.Length)
        {
            if (encryptor.TransformBlock(feedback, 0, 16, keystream, 0) != 16)
                throw new InvalidDataException("AES feedback block failed.");

            var count = Math.Min(16, data.Length - offset);
            for (var i = 0; i < count; i++)
            {
                var current = data[offset + i];
                var plain = (byte)(current ^ keystream[i]);
                data[offset + i] = plain;
                feedback[i] = decrypt ? current : plain;
            }

            offset += count;
        }
    }

    internal static void Ctr(Span<byte> data, byte[] key, ReadOnlySpan<byte> iv)
    {
        if (iv.Length != 16)
            throw new InvalidDataException("AES counter IV must be 16 bytes.");

        using var aes = NewEcb(key);
        using var encryptor = aes.CreateEncryptor();
        Span<byte> counter = stackalloc byte[16];
        iv.CopyTo(counter);
        var counterBlock = new byte[16];
        var keystream = new byte[16];
        for (var offset = 0; offset < data.Length; offset += 16)
        {
            counter.CopyTo(counterBlock);
            if (encryptor.TransformBlock(counterBlock, 0, 16, keystream, 0) != 16)
                throw new InvalidDataException("AES counter block failed.");

            var count = Math.Min(16, data.Length - offset);
            for (var i = 0; i < count; i++)
                data[offset + i] ^= keystream[i];

            Increment(counter);
        }
    }

    private static byte[] DecryptChartIfNeeded(string name, byte[] data, ConversionKeys keys)
    {
        if (data.Length == 0)
            return data;
        if (name.Contains("songs/bin/macos/", StringComparison.Ordinal))
            return ChartCipher.Decrypt(data, keys.MacChartKey);
        if (name.Contains("songs/bin/generic/", StringComparison.Ordinal))
            return ChartCipher.Decrypt(data, keys.PcChartKey);
        return data;
    }

    private static byte[] EncryptChartIfNeeded(string name, byte[] data, ConversionKeys keys)
    {
        if (data.Length == 0)
            return data;
        if (name.Contains("songs/bin/macos/", StringComparison.Ordinal))
            return ChartCipher.Encrypt(data, keys.MacChartKey);
        if (name.Contains("songs/bin/generic/", StringComparison.Ordinal))
            return ChartCipher.Encrypt(data, keys.PcChartKey);
        return data;
    }

    private static Aes NewEcb(byte[] key)
    {
        var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        return aes;
    }

    private static void Increment(Span<byte> counter)
    {
        for (var i = counter.Length - 1; i >= 0; i--)
        {
            counter[i]++;
            if (counter[i] != 0)
                break;
        }
    }
}

/// <summary>
/// Chart containers inside a custom song package.
/// </summary>
public static class ChartCipher
{
    public const int HeaderSize = 24;
    public const int SignatureSize = 56;
    public const int MaxPlainBytes = 64 << 20;

    public static byte[] Encrypt(byte[] plain, byte[] key, byte[]? iv = null)
    {
        if (plain.Length > MaxPlainBytes)
            throw new InvalidDataException("Chart is too large.");

        var compressed = PackageCrypto.Deflate(plain);
        var payload = new byte[4 + compressed.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)plain.Length);
        compressed.CopyTo(payload.AsSpan(4));
        iv ??= new byte[16];
        if (iv.Length != 16)
            throw new InvalidDataException("Chart IV must be 16 bytes.");
        PackageCrypto.Ctr(payload, key, iv);

        var output = new byte[HeaderSize + payload.Length + SignatureSize];
        BinaryPrimitives.WriteUInt32LittleEndian(output, 74);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(4), 3);
        iv.CopyTo(output.AsSpan(8));
        payload.CopyTo(output.AsSpan(HeaderSize));
        return output;
    }

    public static byte[] Decrypt(byte[] chart, byte[] key)
    {
        var (payload, _) = Open(chart, key);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        if (length > MaxPlainBytes)
            throw new InvalidDataException("Chart declares too many bytes.");

        var plain = PackageCrypto.Inflate(payload.AsSpan(4));
        if (plain.Length != length)
            throw new InvalidDataException("Chart length does not match.");
        return plain;
    }

    /// <summary>
    /// Swaps the platform keystream. The compressed bytes, IV, and signature
    /// are unchanged, matching rocksmithconvert's SNG.rekey.
    /// </summary>
    public static byte[] Rekey(byte[] chart, byte[] fromKey, byte[] toKey)
    {
        if (chart.Length == 0)
            return chart;
        if (fromKey.AsSpan().SequenceEqual(toKey))
            return (byte[])chart.Clone();

        var payloadLength = chart.Length - HeaderSize - SignatureSize;
        _ = Decrypt(chart, fromKey);
        var output = (byte[])chart.Clone();
        var iv = output.AsSpan(8, 16);
        PackageCrypto.Ctr(output.AsSpan(HeaderSize, payloadLength), fromKey, iv);
        PackageCrypto.Ctr(output.AsSpan(HeaderSize, payloadLength), toKey, iv);
        return output;
    }

    private static (byte[] Payload, int Start) Open(byte[] chart, byte[] key)
    {
        if (chart.Length < HeaderSize + SignatureSize + 4)
            throw new InvalidDataException("Chart entry is too short.");

        var start = HeaderSize;
        var length = chart.Length - HeaderSize - SignatureSize;
        var payload = chart.AsSpan(start, length).ToArray();
        PackageCrypto.Ctr(payload, key, chart.AsSpan(8, 16));
        _ = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        return (payload, start);
    }
}
