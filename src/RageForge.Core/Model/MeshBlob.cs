/* RAGEFORGE - Mesh Blob */

using System.Buffers.Binary;
using RageForge.Core.Carve;

namespace RageForge.Core.Model;

public sealed class MeshBlobHeader
{
    public required List<(uint Slot, uint Offset, uint Format)> Sections { get; init; }
    public required float[] PositionBase { get; init; }
    public required float[] PositionStep { get; init; }
    public uint? VertexCount { get; init; }
    public bool FloatPositions { get; init; }
}

public sealed class GfxBuffer
{
    public required ulong StructOff { get; init; }
    public required int GfxOff { get; init; }
    public required ulong Bytes { get; init; }
    public required ushort KindHint { get; init; }
    public MeshBlobHeader? MeshBlob { get; init; }
}

public static class MeshBlob
{
    private static uint U32(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b[o..]);
    private static float F32(ReadOnlySpan<byte> b, int o) => BitConverter.UInt32BitsToSingle(U32(b, o));

    public static MeshBlobHeader? Parse(ReadOnlySpan<byte> b)
    {
        if (b.Length < 0x80)
            return null;
        bool floatPositions = U32(b, 0) == 0x3063;
        if (U32(b, 0) != 0x4042 && !floatPositions)
            return null;

        (uint p0, uint stride) = floatPositions ? (0x60u, 12u) : (0x80u, 8u);
        var sections = new List<(uint, uint, uint)>();
        for (uint slot = 0; slot < 23; slot++)
        {
            uint v = U32(b, (int)(slot * 4));
            if (v != 0 && slot * 4 < 0x5c)
                sections.Add((slot, (v >> 8) * 2, v & 0xff));
        }

        uint? vcount = null;
        uint best = uint.MaxValue;
        foreach (var (_, off, _) in sections)
            if (off > p0 && off <= b.Length && off < best)
                best = off;
        if (best != uint.MaxValue)
            vcount = (best - p0) / stride;

        return new MeshBlobHeader
        {
            Sections = sections,
            PositionBase = floatPositions ? [0, 0, 0] : [F32(b, 0x60), F32(b, 0x64), F32(b, 0x68)],
            PositionStep = floatPositions ? [0, 0, 0] : [F32(b, 0x70), F32(b, 0x74), F32(b, 0x78)],
            VertexCount = vcount,
            FloatPositions = floatPositions,
        };
    }

    public static List<GfxBuffer> FindBuffers(RageResource r)
    {
        var v = new List<GfxBuffer>();
        int p = 0;
        while (p + 0x20 <= r.Sys.Length)
        {
            ulong ptr = r.U64(p + 0x18);
            ushort k = r.U16(p + 0x0E);
            if (ptr >> 28 == 6 && r.U16(p + 0x0C) == 4 && (k == 8 || k == 9))
            {
                ulong dw = r.U32(p + 8);
                var go = r.GfxOff(ptr);
                if (go is { } gfxOff)
                {
                    ulong bytes = dw * 4;
                    int end = (int)Math.Min((ulong)gfxOff + bytes, (ulong)r.Gfx.Length);
                    MeshBlobHeader? blob = gfxOff <= end ? Parse(r.Gfx.AsSpan(gfxOff, end - gfxOff)) : null;
                    v.Add(new GfxBuffer
                    {
                        StructOff = (ulong)p,
                        GfxOff = gfxOff,
                        Bytes = bytes,
                        KindHint = k,
                        MeshBlob = blob,
                    });
                    p += 0x20;
                    continue;
                }
            }
            p += 8;
        }
        return v;
    }
}
