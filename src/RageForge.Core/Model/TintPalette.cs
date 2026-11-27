/* RAGEFORGE - Tint Palette */

using System.Runtime.CompilerServices;
using RageForge.Core.Carve;

namespace RageForge.Core.Model;

public sealed class TintPalette
{
    public const uint Param = 0x2c8d_7182;

    private static readonly ConditionalWeakTable<RageResource, Dictionary<ulong, TintPalette>> Cache = new();

    private TintPalette(byte[] rgba, int width)
    {
        Rgba = rgba;
        Width = width;
        int whole = 1 << (31 - System.Numerics.BitOperations.LeadingZeroCount((uint)width));
        int extra = width - whole;
        RowLength = Math.Clamp(256 >> Math.Min(extra, 8), 1, whole);
        Rows = Math.Max(1, whole / RowLength);
    }

    public byte[] Rgba { get; }

    public int Width { get; }

    public int RowLength { get; }

    public int Rows { get; }

    public uint Colour(int row, int index)
    {
        int entry = Math.Clamp(row, 0, Rows - 1) * RowLength + index;
        if (entry < 0 || entry >= Width) entry = Math.Clamp(index, 0, Width - 1);
        return BitConverter.ToUInt32(Rgba, entry * 4) | 0xff00_0000;
    }

    public static TintPalette FromRgba(byte[] rgba) => new(rgba, rgba.Length / 4);

    public static TintPalette? Find(RageResource r, ulong key) =>
        Cache.GetValue(r, Scan).GetValueOrDefault(key);

    private static Dictionary<ulong, TintPalette> Scan(RageResource r)
    {
        var found = new Dictionary<ulong, TintPalette>();
        for (int tp = 0; tp + 0x48 <= r.Sys.Length; tp += 8)
        {
            if (r.U16(tp + 0x3a) != 1) continue;
            uint width = r.U16(tp + 0x38);
            if (width is 0 or > 4096 || r.Sys[tp + 0x3f] is not (28 or 29)) continue;
            if (r.GfxOff(r.U64(tp + 0x18)) is not { } gfx || gfx + width * 4 > r.Gfx.Length) continue;
            if (r.SysOff(r.U64(tp + 0x30)) is not { } reference || reference + 0x10 > r.Sys.Length) continue;
            ulong key = r.U64(reference + 8);
            if (key == 0 || found.ContainsKey(key)) continue;
            found[key] = new TintPalette(r.Gfx.AsSpan(gfx, (int)width * 4).ToArray(), (int)width);
        }
        return found;
    }
}
