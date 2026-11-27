/* RAGEFORGE - TTPF Texture */

namespace RageForge.Core.Ttpf;

public sealed class TtpfTexture
{
    public required ulong Hash { get; init; }
    public required uint Width { get; init; }
    public required uint Height { get; init; }
    public required uint DxgiFormat { get; init; }
    public required ulong Virt { get; init; }
    public required uint D0 { get; init; }
    public required uint D1 { get; init; }
    public required ulong Loc { get; init; }

    public ulong Size { get; set; }
    public ulong? CubeBase { get; set; }

    private Gfx10Layout? _layout;
    private bool _layoutBuilt;

    public uint TileMode => (D1 >> 24) & 0xF;

    public uint DeclaredMips => TileMode is >= 9 and <= 12
        ? Math.Max(D0 >> 28, 1)
        : (D0 >> 28) + 1;

    public uint Mips => _mips ?? DeclaredMips;

    private uint? _mips;

    public uint FullMipChain => 32 - (uint)System.Numerics.BitOperations.LeadingZeroCount(Math.Max(Width, Height));

    public bool FitMipsTo(ulong span)
    {
        if (TileMode > 3 || Layout is not { } current || current.Total == span) return false;
        var sw = GetSwizzle();
        if (sw is null) return false;
        var fmt = Format;
        uint bd = fmt.BlockDim();
        uint? fit = null;
        for (uint m = 1; m <= FullMipChain; m++)
        {
            if (Gfx10Layout.Create(sw.Value, DivCeil(Width, bd), DivCeil(Height, bd), m, fmt.BytesPerElem()) is not { } l) continue;
            if (l.WithSlices(Slices).Total != span) continue;
            if (fit is not null) return false;
            fit = m;
        }
        if (fit is not { } mips || mips == Mips) return false;
        _mips = mips;
        _layoutBuilt = false;
        Size = Layout?.Total ?? 0;
        return true;
    }

    public uint Slices => ((D1 >> 10) & 0x3FF) + 1;

    public TtpfFormat Format => TtpfFormats.FromDxgi(DxgiFormat);

    public string FormatName => TtpfFormats.DxgiName(DxgiFormat);

    public string HashHex => Hash.ToString("x16");

    public Swizzle? GetSwizzle() => TileMode switch
    {
        0 => Swizzle.D64K,
        >= 1 and <= 3 or >= 9 and <= 12 => Swizzle.S4K,
        _ => null,
    };

    public Gfx10Layout? Layout
    {
        get
        {
            if (!_layoutBuilt)
            {
                _layout = BuildLayout();
                _layoutBuilt = true;
            }
            return _layout;
        }
    }

    private Gfx10Layout? BuildLayout()
    {
        var fmt = Format;
        if (fmt == TtpfFormat.Unknown)
            return null;
        var sw = GetSwizzle();
        if (sw is null)
            return null;

        uint bd = fmt.BlockDim();
        var l = Gfx10Layout.Create(sw.Value, DivCeil(Width, bd), DivCeil(Height, bd), Mips, fmt.BytesPerElem());
        if (l is null)
            return null;

        return TileMode is >= 9 and <= 12
            ? l.WithCubeCopies(Slices, TileMode - 8, CubeBase)
            : l.WithSlices(Slices);
    }

    public bool Decodable => Layout is not null;

    private static uint DivCeil(uint a, uint b) => (a + b - 1) / b;
}
