/* RAGEFORGE - Geometry */

using RageForge.Core.Carve;

namespace RageForge.Core.Model;

public sealed class Geometry
{
    public required int VertexBuffer { get; init; }
    public required int IndexBuffer { get; init; }
    public required ulong RecordOff { get; init; }
}

public static class Geometries
{
    public static List<Geometry> Find(RageResource r, List<GfxBuffer> bufs)
    {
        var byStruct = new Dictionary<ulong, int>();
        for (int i = 0; i < bufs.Count; i++)
            byStruct[bufs[i].StructOff] = i;

        int? Target(ulong ptr)
        {
            var o = r.SysOff(ptr);
            return o is { } so && byStruct.TryGetValue((ulong)so, out int idx) ? idx : null;
        }

        var outList = new List<Geometry>();
        int off = 0;
        while (off + 0x40 <= r.Sys.Length)
        {
            var vb = Target(r.U64(off));
            if (vb is { } vbi && bufs[vbi].MeshBlob is not null)
            {
                var holder = r.SysOff(r.U64(off + 0x38));
                if (holder is { } h)
                {
                    var ib = Target(r.U64(h));
                    if (ib is { } ibi && bufs[ibi].MeshBlob is null)
                        outList.Add(new Geometry { VertexBuffer = vbi, IndexBuffer = ibi, RecordOff = (ulong)off });
                }
            }
            off += 8;
        }

        return outList
            .GroupBy(g => g.VertexBuffer)
            .Select(g => g.First())
            .OrderBy(g => g.VertexBuffer)
            .ToList();
    }
}
