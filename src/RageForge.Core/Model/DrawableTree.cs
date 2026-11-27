/* RAGEFORGE - Drawable Tree */

using RageForge.Core.Carve;

namespace RageForge.Core.Model;

public static class DrawableTree
{
    private const int ShaderGroupOffset = 0x10;
    private const int LodOffset = 0x50;
    private const int MaxDictionary = 8192;

    public static IReadOnlyList<(uint Hash, int Drawable)>? Dictionary(RageResource r)
    {
        if (r.Sys.Length < 0x40) return null;
        int n = r.U16(0x28);
        if (n == 0 || n > MaxDictionary || n != r.U16(0x2a) || n != r.U16(0x38) || n != r.U16(0x3a)) return null;
        if (r.SysOff(r.U64(0x20)) is not { } hashes || r.SysOff(r.U64(0x30)) is not { } items) return null;
        if (hashes + 4L * n > r.Sys.Length || items + 8L * n > r.Sys.Length) return null;

        var result = new List<(uint, int)>(n);
        for (int k = 0; k < n; k++)
        {
            if (r.SysOff(r.U64(items + 8 * k)) is not { } d || !IsDrawable(r, d)) return null;
            result.Add((r.U32(hashes + 4 * k), d));
        }
        return result;
    }

    public static bool IsDrawable(RageResource r, int d) =>
        d >= 0 && d + LodOffset + 0x20 <= r.Sys.Length
        && r.SysOff(r.U64(d + ShaderGroupOffset)) is not null
        && r.SysOff(r.U64(d + LodOffset)) is not null;

    public static int? ShaderGroup(RageResource r, int d) => r.SysOff(r.U64(d + ShaderGroupOffset));

    public static HashSet<int>? GeometryStructs(RageResource r, int d, int lod = 0)
    {
        if (lod is < 0 or > 3 || r.SysOff(r.U64(d + LodOffset + 8 * lod)) is not { } collection || collection + 0x10 > r.Sys.Length)
            return null;
        if (r.SysOff(r.U64(collection)) is not { } models) return null;
        int count = r.U16(collection + 8);
        var result = new HashSet<int>();
        for (int k = 0; k < count && models + 8 * k + 8 <= r.Sys.Length; k++)
        {
            if (r.SysOff(r.U64(models + 8 * k)) is not { } model || model + 0x18 > r.Sys.Length) continue;
            if (r.SysOff(r.U64(model + 8)) is not { } geoms) continue;
            int n = r.U16(model + 0x10);
            for (int j = 0; j < n && geoms + 8 * j + 8 <= r.Sys.Length; j++)
                if (r.SysOff(r.U64(geoms + 8 * j)) is { } g)
                    result.Add(g);
        }
        return result.Count > 0 ? result : null;
    }
}
