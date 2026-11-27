/* RAGEFORGE - RPF9 Header */

using System.Buffers.Binary;

namespace RageForge.Core.Rpf9;

public readonly struct Rpf9Header
{
    public const int Size = 64;

    public static ReadOnlySpan<byte> Magic => [0x39, 0x46, 0x50, 0x52];

    public const uint ExpectedConst18 = 293376;
    public const uint ExpectedConst1C = 336;
    public const uint ExpectedConst30 = 16;

    public uint TocEnd { get; init; }
    public uint TocFieldB { get; init; }
    public uint NamesSize { get; init; }
    public ulong TocHash { get; init; }
    public uint Const18 { get; init; }
    public uint Const1C { get; init; }
    public ushort UnkA { get; init; }
    public ushort UnkB { get; init; }
    public uint Count { get; init; }
    public uint KeyId { get; init; }
    public uint Flags { get; init; }
    public uint Const30 { get; init; }

    public byte Shape => (byte)(Flags & 0xFF);
    public uint TailSize => TocEnd - TocFieldB;
    public string TocHashHex => TocHash.ToString("x16");

    public bool ConstantsMatch =>
        Const18 == ExpectedConst18 && Const1C == ExpectedConst1C && Const30 == ExpectedConst30;

    public static Rpf9Header Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size)
            throw new ArgumentException($"RPF9 header needs {Size} bytes, got {data.Length}.", nameof(data));
        if (!data[..4].SequenceEqual(Magic))
            throw new InvalidDataException("Not an RPF9 archive: magic mismatch at offset 0.");

        return new Rpf9Header
        {
            TocEnd = BinaryPrimitives.ReadUInt32LittleEndian(data[0x04..]),
            TocFieldB = BinaryPrimitives.ReadUInt32LittleEndian(data[0x08..]),
            NamesSize = BinaryPrimitives.ReadUInt32LittleEndian(data[0x0C..]),
            TocHash = BinaryPrimitives.ReadUInt64BigEndian(data[0x10..]),
            Const18 = BinaryPrimitives.ReadUInt32LittleEndian(data[0x18..]),
            Const1C = BinaryPrimitives.ReadUInt32LittleEndian(data[0x1C..]),
            UnkA = BinaryPrimitives.ReadUInt16LittleEndian(data[0x20..]),
            UnkB = BinaryPrimitives.ReadUInt16LittleEndian(data[0x22..]),
            Count = BinaryPrimitives.ReadUInt32LittleEndian(data[0x24..]),
            KeyId = BinaryPrimitives.ReadUInt32LittleEndian(data[0x28..]),
            Flags = BinaryPrimitives.ReadUInt32LittleEndian(data[0x2C..]),
            Const30 = BinaryPrimitives.ReadUInt32LittleEndian(data[0x30..]),
        };
    }
}
