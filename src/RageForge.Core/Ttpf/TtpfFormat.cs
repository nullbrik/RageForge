/* RAGEFORGE - TTPF Texture Format */

namespace RageForge.Core.Ttpf;

public enum TtpfFormat
{
    Bc1,
    Bc2,
    Bc3,
    Bc4,
    Bc5,
    Bc6U,
    Bc6S,
    Bc7,
    Rgba8,
    Bgra8,
    R8,
    Rg8,
    R16F,
    Rgba16F,
    Rgba16,
    Unknown,
}

public static class TtpfFormats
{
    public static TtpfFormat FromDxgi(uint f) => f switch
    {
        >= 70 and <= 72 => TtpfFormat.Bc1,
        >= 73 and <= 75 => TtpfFormat.Bc2,
        >= 76 and <= 78 => TtpfFormat.Bc3,
        >= 79 and <= 81 => TtpfFormat.Bc4,
        >= 82 and <= 84 => TtpfFormat.Bc5,
        94 or 95 => TtpfFormat.Bc6U,
        96 => TtpfFormat.Bc6S,
        >= 97 and <= 99 => TtpfFormat.Bc7,
        >= 27 and <= 32 => TtpfFormat.Rgba8,
        87 or 88 or 90 or 91 => TtpfFormat.Bgra8,
        >= 60 and <= 65 => TtpfFormat.R8,
        >= 48 and <= 52 => TtpfFormat.Rg8,
        54 => TtpfFormat.R16F,
        10 => TtpfFormat.Rgba16F,
        11 => TtpfFormat.Rgba16,
        _ => TtpfFormat.Unknown,
    };

    public static uint BlockDim(this TtpfFormat f) => f switch
    {
        TtpfFormat.Rgba8 or TtpfFormat.Bgra8 or TtpfFormat.R8 or TtpfFormat.Rg8 or TtpfFormat.R16F
            or TtpfFormat.Rgba16F or TtpfFormat.Rgba16 or TtpfFormat.Unknown => 1,
        _ => 4,
    };

    public static uint BytesPerElem(this TtpfFormat f) => f switch
    {
        TtpfFormat.Bc1 or TtpfFormat.Bc4 => 8,
        TtpfFormat.Bc2 or TtpfFormat.Bc3 or TtpfFormat.Bc5 or TtpfFormat.Bc6U or TtpfFormat.Bc6S or TtpfFormat.Bc7 => 16,
        TtpfFormat.Rgba8 or TtpfFormat.Bgra8 => 4,
        TtpfFormat.Rg8 or TtpfFormat.R16F => 2,
        TtpfFormat.Rgba16F or TtpfFormat.Rgba16 => 8,
        TtpfFormat.R8 => 1,
        _ => 0,
    };

    public static string DxgiName(uint f) => f switch
    {
        10 => "R16G16B16A16_FLOAT",
        11 => "R16G16B16A16_UNORM",
        28 => "R8G8B8A8_UNORM",
        29 => "R8G8B8A8_UNORM_SRGB",
        49 => "R8G8_UNORM",
        54 => "R16_FLOAT",
        61 => "R8_UNORM",
        65 => "A8_UNORM",
        71 => "BC1_UNORM",
        72 => "BC1_UNORM_SRGB",
        74 => "BC2_UNORM",
        75 => "BC2_UNORM_SRGB",
        77 => "BC3_UNORM",
        78 => "BC3_UNORM_SRGB",
        80 => "BC4_UNORM",
        81 => "BC4_SNORM",
        83 => "BC5_UNORM",
        84 => "BC5_SNORM",
        87 => "B8G8R8A8_UNORM",
        91 => "B8G8R8A8_UNORM_SRGB",
        95 => "BC6H_UF16",
        96 => "BC6H_SF16",
        98 => "BC7_UNORM",
        99 => "BC7_UNORM_SRGB",
        _ => $"DXGI_{f}",
    };
}
