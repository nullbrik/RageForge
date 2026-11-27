/* RAGEFORGE - Embedded Textures */

using RageForge.Core.Carve;
using RageForge.Core.Ttpf;

namespace RageForge.Core.Model;

public sealed class EmbeddedTexture
{
    private readonly RageResource _res;
    private readonly int _gfxOff;
    private readonly int _gfxLen;
    private readonly bool _swizzled;
    private readonly uint _mips;

    internal EmbeddedTexture(RageResource res, string name, uint dxgi, uint width, uint height, int gfxOff, int gfxLen, bool swizzled, uint mips)
    {
        _res = res;
        Name = name;
        Dxgi = dxgi;
        Width = width;
        Height = height;
        _gfxOff = gfxOff;
        _gfxLen = gfxLen;
        _swizzled = swizzled;
        _mips = mips;
    }

    public string Name { get; }
    public uint Dxgi { get; }
    public uint Width { get; }
    public uint Height { get; }

    public DecodedTexture? Decode(uint maxDim = 0)
    {
        try
        {
            return DecodeCore(maxDim);
        }
        catch
        {
            return null;
        }
    }

    private DecodedTexture? DecodeCore(uint maxDim)
    {
        var fmt = TtpfFormats.FromDxgi(Dxgi);
        uint bd = fmt.BlockDim(), bpe = fmt.BytesPerElem();
        uint ew = (Width + bd - 1) / bd, eh = (Height + bd - 1) / bd;
        var alloc = _res.Gfx.AsSpan(_gfxOff, _gfxLen);

        if (!_swizzled)
        {
            var linear = new byte[(long)ew * eh * bpe];
            alloc[..Math.Min(alloc.Length, linear.Length)].CopyTo(linear);
            return new DecodedTexture { Rgba = TtpfDecoder.ToRgba(fmt, linear, Width, Height), Width = Width, Height = Height, Mip = 0 };
        }

        var layout = Gfx10Layout.Create(Swizzle.S4K, ew, eh, _mips, bpe);
        if (layout is null || (ulong)alloc.Length < layout.Total)
            layout = Gfx10Layout.Create(Swizzle.S4K, ew, eh, 1, bpe);
        if (layout is null) return null;

        int mip = 0;
        if (maxDim > 0)
            while (mip + 1 < layout.Mips.Length && Math.Max(Width >> mip, Height >> mip) > maxDim)
                mip++;

        var src = alloc;
        if ((ulong)src.Length < layout.Total)
        {
            var padded = new byte[layout.Total];
            src.CopyTo(padded);
            src = padded;
        }
        uint w = Math.Max(Width >> mip, 1), h = Math.Max(Height >> mip, 1);
        var lin = layout.UntileSlice(src, mip, 0);
        return new DecodedTexture { Rgba = TtpfDecoder.ToRgba(fmt, lin, w, h), Width = w, Height = h, Mip = (uint)mip };
    }
}

public static class EmbeddedTextures
{
    public static Dictionary<int, EmbeddedTexture> Read(RageResource r, int dictOff)
    {
        var result = new Dictionary<int, EmbeddedTexture>();
        if (dictOff < 0 || dictOff + 0x3a > r.Sys.Length || r.SysOff(r.U64(dictOff + 0x30)) is not { } arr)
            return result;
        int count = r.U16(dictOff + 0x38);
        if (count is 0 or > 2048 || arr + (long)count * 8 > r.Sys.Length)
            return result;

        var items = new List<(int Tp, int GfxOff, uint W, uint H, uint Dxgi, bool Swizzled, uint Mips, string Name)>();
        for (int i = 0; i < count; i++)
        {
            if (r.SysOff(r.U64(arr + 8 * i)) is not { } tp || tp + 0x48 > r.Sys.Length) continue;
            if (r.GfxOff(r.U64(tp + 0x18)) is not { } goff || goff >= r.Gfx.Length) continue;
            uint w = r.U16(tp + 0x38), h = r.U16(tp + 0x3a);
            uint dxgi = r.Sys[tp + 0x3f];
            if (w is < 4 or > 8192 || h is < 4 or > 8192 || TtpfFormats.FromDxgi(dxgi) == TtpfFormat.Unknown) continue;
            items.Add((tp, goff, w, h, dxgi, r.Sys[tp + 0x40] != 0, Math.Max(1u, r.Sys[tp + 0x42]), NameAt(r, tp) ?? $"tex_{i:D3}"));
        }

        var starts = items.Select(t => t.GfxOff).Distinct().Order().ToArray();
        foreach (var t in items)
        {
            int k = Array.BinarySearch(starts, t.GfxOff);
            int next = k >= 0 && k + 1 < starts.Length ? starts[k + 1] : r.Gfx.Length;
            result[t.Tp] = new EmbeddedTexture(r, t.Name, t.Dxgi, t.W, t.H, t.GfxOff, next - t.GfxOff, t.Swizzled, t.Mips);
        }
        return result;
    }

    private static string? NameAt(RageResource r, int tp)
    {
        if (r.SysOff(r.U64(tp + 0x30)) is not { } no) return null;
        var s = r.Sys.AsSpan(no);
        if (s.Length >= 8 && s[..8].IndexOfAnyExcept((byte)0) < 0) s = s[8..];
        int end = s.IndexOf((byte)0);
        if (end is <= 0 or >= 128) return null;
        foreach (var c in s[..end])
            if (c is < 0x20 or > 0x7e) return null;
        return System.Text.Encoding.ASCII.GetString(s[..end]);
    }
}
