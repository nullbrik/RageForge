/* RAGEFORGE - Bink 2 Header */

using System.Buffers.Binary;

namespace RageForge.Core.Video;

public readonly record struct Bk2Info(uint Width, uint Height, uint Frames, uint FpsNum, uint FpsDen, ulong TotalSize)
{
    public double Fps => FpsDen == 0 ? 0 : (double)FpsNum / FpsDen;
    public double Seconds => Fps <= 0 ? 0 : Frames / Fps;

    public static Bk2Info? Parse(ReadOnlySpan<byte> b, ulong remaining)
    {
        if (b.Length < 44 || b[0] != (byte)'K' || b[1] != (byte)'B' || b[2] != (byte)'2' || b[3] is < (byte)'a' or > (byte)'z')
            return null;
        ulong total = (ulong)U(b, 4) + 8;
        uint frames = U(b, 8), frames2 = U(b, 16), w = U(b, 20), h = U(b, 24), fn = U(b, 28), fd = U(b, 32);
        bool valid = frames is >= 1 and <= 500_000 && frames == frames2
            && w is >= 64 and <= 8192 && h is >= 64 and <= 8192
            && fn > 0 && fd > 0 && total >= 1024 && total <= remaining;
        return valid ? new Bk2Info(w, h, frames, fn, fd, total) : null;
    }

    private static uint U(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b[o..]);
}
