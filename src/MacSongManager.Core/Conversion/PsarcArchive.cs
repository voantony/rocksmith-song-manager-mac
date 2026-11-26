using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace MacSongManager.Core.Conversion;

// Ported to C# for Mac Song Manager from pyrocksmith rocksmith/psarc.py
// Copyright (c) 2016 0x0L, MIT License.
// https://github.com/0x0L/rocksmith
//
// The table layout also matches rocksmithconvert
// macos/Sources/RSConvertCore/PSARC.swift
// Copyright glebb, GPL-3.0-or-later.
// https://github.com/glebb/rocksmithconvert
//
// Modified by Antony Vo (2026-11-26): encryption keys are passed in by the
// caller and are not stored in this file. Entry order follows the archive
// listing. Earlier in 2026 the Python parser was rewritten in C# without the
// construct dependency; multi-byte integers are read by hand.
public static class PsarcArchive
{
    private const int BlockSize = 65536;
    private const int EntrySize = 30;
    private const uint Version = 65540;
    private const uint ArchiveFlags = 4;

    public static Dictionary<string, byte[]> Read(string path, ConversionKeys keys, bool decryptCharts)
    {
        using var stream = File.OpenRead(path);
        return Read(stream, keys, decryptCharts);
    }

    public static Dictionary<string, byte[]> Read(Stream stream, ConversionKeys keys, bool decryptCharts)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        var magic = reader.ReadBytes(4);
        if (!magic.AsSpan().SequenceEqual("PSAR"u8))
            throw new InvalidDataException("Not a PSARC archive.");

        var version = ReadUInt32Big(reader);
        var compression = reader.ReadBytes(4);
        if (version != Version || !compression.AsSpan().SequenceEqual("zlib"u8))
            throw new InvalidDataException("Unsupported PSARC header.");

        var headerSize = ReadUInt32Big(reader);
        var entrySize = ReadUInt32Big(reader);
        var entryCount = ReadUInt32Big(reader);
        var blockSize = ReadUInt32Big(reader);
        var flags = ReadUInt32Big(reader);
        if (entrySize != EntrySize || blockSize != BlockSize || flags != ArchiveFlags)
            throw new InvalidDataException("Unsupported PSARC table.");

        var bomSize = checked((int)headerSize - 32);
        var bom = PackageCrypto.DecryptToc(reader.ReadBytes(bomSize), keys);
        var (entries, zLengths) = ParseBom(bom, (int)entryCount);
        var blobs = new byte[entryCount][];
        for (var i = 0; i < entryCount; i++)
            blobs[i] = ReadEntry(stream, entries[i], zLengths);

        var listing = Encoding.UTF8.GetString(blobs[0]).Split('\n', StringSplitOptions.None);
        if (listing.Length == 1 && listing[0].Length == 0)
            listing = [];
        if (listing.Length != entryCount - 1)
            throw new InvalidDataException("PSARC name list does not match the table.");

        var content = new Dictionary<string, byte[]>(listing.Length, StringComparer.Ordinal);
        for (var i = 0; i < listing.Length; i++)
            content[listing[i]] = blobs[i + 1];

        return decryptCharts ? PackageCrypto.DecryptCharts(content, keys) : content;
    }

    public static void Write(string path, Dictionary<string, byte[]> content, ConversionKeys keys, bool encryptCharts)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var stream = File.Create(path);
        Write(stream, content, keys, encryptCharts);
    }

    public static void Write(Stream stream, Dictionary<string, byte[]> content, ConversionKeys keys, bool encryptCharts)
    {
        if (encryptCharts)
            content = PackageCrypto.EncryptCharts(content, keys);

        var names = content.Keys.ToList();
        var blobs = new List<byte[]>(names.Count + 1) { Encoding.UTF8.GetBytes(string.Join('\n', names)) };
        foreach (var name in names)
            blobs.Add(content[name]);

        var packed = new List<PackedEntry>(blobs.Count);
        packed.Add(PackEntry("", blobs[0]));
        for (var i = 0; i < names.Count; i++)
            packed.Add(PackEntry(names[i], blobs[i + 1]));

        var zLengths = new List<ushort>();
        foreach (var entry in packed)
            zLengths.AddRange(entry.BlockLengths);

        var headerSize = 32 + (EntrySize * packed.Count) + (2 * zLengths.Count);
        var bom = BuildBom(packed, zLengths, headerSize);
        var encryptedBom = PackageCrypto.EncryptToc(bom, keys);
        if (encryptedBom.Length != headerSize - 32)
            throw new InvalidDataException("PSARC table size mismatch.");

        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("PSAR"u8);
        WriteUInt32Big(writer, Version);
        writer.Write("zlib"u8);
        WriteUInt32Big(writer, (uint)headerSize);
        WriteUInt32Big(writer, EntrySize);
        WriteUInt32Big(writer, (uint)packed.Count);
        WriteUInt32Big(writer, BlockSize);
        WriteUInt32Big(writer, ArchiveFlags);
        writer.Write(encryptedBom);
        foreach (var entry in packed)
            writer.Write(entry.Data);
    }

    private static byte[] BuildBom(List<PackedEntry> packed, List<ushort> zLengths, int headerSize)
    {
        using var bom = new MemoryStream();
        using var writer = new BinaryWriter(bom, Encoding.ASCII, leaveOpen: true);
        long dataOffset = headerSize;
        var zIndex = 0;
        foreach (var entry in packed)
        {
            writer.Write(entry.NameDigest);
            WriteUInt32Big(writer, (uint)zIndex);
            WriteUInt40Big(writer, (ulong)entry.PlainLength);
            WriteUInt40Big(writer, (ulong)dataOffset);
            dataOffset += entry.Data.Length;
            zIndex += entry.BlockLengths.Count;
        }

        foreach (var length in zLengths)
            WriteUInt16Big(writer, length);

        return bom.ToArray();
    }

    private static (Entry[] Entries, ushort[] Lengths) ParseBom(byte[] bom, int entryCount)
    {
        using var stream = new MemoryStream(bom);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        var entries = new Entry[entryCount];
        for (var i = 0; i < entryCount; i++)
        {
            entries[i] = new Entry(
                reader.ReadBytes(16),
                (int)ReadUInt32Big(reader),
                ReadUInt40Big(reader),
                ReadUInt40Big(reader));
        }

        var remaining = (int)(bom.Length - stream.Position);
        if (remaining < 0 || remaining % 2 != 0)
            throw new InvalidDataException("PSARC block table is truncated.");
        var lengths = new ushort[remaining / 2];
        for (var i = 0; i < lengths.Length; i++)
            lengths[i] = ReadUInt16Big(reader);
        return (entries, lengths);
    }

    private static byte[] ReadEntry(Stream stream, Entry entry, ushort[] zLengths)
    {
        // Same rule as rocksmithconvert PSARC.swift: a block whose stored size
        // equals the uncompressed size was written raw. Only a shorter block
        // is zlib data. Trying inflate on raw bytes can succeed by accident.
        stream.Position = entry.Offset;
        using var output = new MemoryStream();
        var produced = 0;
        var blocks = entry.Length == 0 ? 0 : (int)((entry.Length + BlockSize - 1) / BlockSize);
        for (var n = 0; n < blocks; n++)
        {
            var index = entry.ZIndex + n;
            if (index < 0 || index >= zLengths.Length)
                throw new InvalidDataException("PSARC block index is out of range.");

            var expected = (int)Math.Min(BlockSize, entry.Length - produced);
            var stored = zLengths[index] == 0 ? BlockSize : zLengths[index];
            var chunk = new byte[stored];
            var read = stream.Read(chunk, 0, stored);
            if (read != stored)
                throw new EndOfStreamException("PSARC entry is truncated.");

            if (stored == expected)
            {
                output.Write(chunk);
            }
            else
            {
                var inflated = PackageCrypto.Inflate(chunk);
                if (inflated.Length != expected)
                    throw new InvalidDataException("PSARC entry length does not match.");
                output.Write(inflated);
            }

            produced += expected;
        }

        if (output.Length != entry.Length)
            throw new InvalidDataException("PSARC entry length does not match.");
        return output.ToArray();
    }

    private static PackedEntry PackEntry(string name, byte[] plain)
    {
        using var output = new MemoryStream();
        var lengths = new List<ushort>();
        for (var offset = 0; offset < plain.Length || (plain.Length == 0 && lengths.Count == 0); offset += BlockSize)
        {
            if (plain.Length == 0)
                break;
            var count = Math.Min(BlockSize, plain.Length - offset);
            var raw = plain.AsSpan(offset, count).ToArray();
            var compressed = PackageCrypto.Deflate(raw);
            if (compressed.Length < raw.Length)
            {
                output.Write(compressed);
                lengths.Add((ushort)compressed.Length);
            }
            else
            {
                output.Write(raw);
                lengths.Add((ushort)(raw.Length % BlockSize));
            }
        }

        var digest = name.Length == 0 ? new byte[16] : MD5.HashData(Encoding.UTF8.GetBytes(name));
        return new PackedEntry(digest, (int)plain.Length, lengths, output.ToArray());
    }

    private static uint ReadUInt32Big(BinaryReader reader)
    {
        Span<byte> bytes = stackalloc byte[4];
        reader.Read(bytes);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }

    private static ushort ReadUInt16Big(BinaryReader reader)
    {
        Span<byte> bytes = stackalloc byte[2];
        reader.Read(bytes);
        return BinaryPrimitives.ReadUInt16BigEndian(bytes);
    }

    private static long ReadUInt40Big(BinaryReader reader)
    {
        Span<byte> bytes = stackalloc byte[5];
        reader.Read(bytes);
        long value = 0;
        foreach (var part in bytes)
            value = (value << 8) | part;
        return value;
    }

    private static void WriteUInt32Big(BinaryWriter writer, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        writer.Write(bytes);
    }

    private static void WriteUInt16Big(BinaryWriter writer, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        writer.Write(bytes);
    }

    private static void WriteUInt40Big(BinaryWriter writer, ulong value)
    {
        Span<byte> bytes = stackalloc byte[5];
        for (var i = 4; i >= 0; i--)
        {
            bytes[i] = (byte)(value & 0xFF);
            value >>= 8;
        }

        writer.Write(bytes);
    }

    private readonly record struct Entry(byte[] NameDigest, int ZIndex, long Length, long Offset);

    private readonly record struct PackedEntry(byte[] NameDigest, int PlainLength, List<ushort> BlockLengths, byte[] Data);
}
