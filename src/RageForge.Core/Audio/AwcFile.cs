/* RAGEFORGE - AWC Container */

using System.Buffers.Binary;

namespace RageForge.Core.Audio;

public readonly record struct AwcChunk(byte Tag, int Offset, int Size);

public sealed class AwcFile
{
    public const byte TagData = 0x55;
    public const byte TagStreamFormat = 0x48;
    public const byte TagFormat = 0x76;

    public required ushort Flags { get; init; }
    public required List<List<AwcChunk>> Streams { get; init; }
    public required int Extent { get; init; }

    public bool Multichannel => (Flags & 0x04) != 0;

    public static bool HasMagic(ReadOnlySpan<byte> b) => b.Length >= 16 && b[..4].SequenceEqual("ADAT"u8);

    public static AwcFile? Parse(ReadOnlySpan<byte> b, long available = long.MaxValue)
    {
        if (!HasMagic(b))
            return null;
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(b[6..]);
        int sc = (int)BinaryPrimitives.ReadUInt32LittleEndian(b[8..]);
        int doff = (int)BinaryPrimitives.ReadUInt32LittleEndian(b[12..]);
        if (sc <= 0 || sc > 4096 || doff < 16 || doff > b.Length)
            return null;

        var counts = new int[sc];
        for (int x = Math.Max(16, (doff - sc * 60) & ~1); x + sc * 4 <= doff; x += 2)
        {
            int total = 0;
            for (int i = 0; i < sc; i++)
            {
                counts[i] = (int)(BinaryPrimitives.ReadUInt32LittleEndian(b[(x + i * 4)..]) >> 29);
                total += counts[i];
            }
            if (total == 0 || x + sc * 4 + total * 8 != doff)
                continue;

            int off = x + sc * 4;
            int extent = doff;
            bool ok = true;
            var streams = new List<List<AwcChunk>>(sc);
            foreach (int n in counts)
            {
                var chunks = new List<AwcChunk>(n);
                for (int k = 0; k < n; k++)
                {
                    ulong v = BinaryPrimitives.ReadUInt64LittleEndian(b[off..]);
                    off += 8;
                    var c = new AwcChunk((byte)(v >> 56), (int)(v & 0x0FFF_FFFF), (int)((v >> 28) & 0x0FFF_FFFF));
                    if ((long)c.Offset + c.Size > available) ok = false;
                    extent = Math.Max(extent, c.Offset + c.Size);
                    chunks.Add(c);
                }
                streams.Add(chunks);
            }
            if (ok)
                return new AwcFile { Flags = flags, Streams = streams, Extent = extent };
        }
        return null;
    }
}
