/* RAGEFORGE - TTPF Mip Chain */

namespace RageForge.Core.Ttpf;

public sealed class MipChain
{
    public required TtpfFormat Format { get; init; }
    public required uint Width { get; init; }
    public required uint Height { get; init; }
    public required byte[][] Levels { get; init; }

    public long Bytes
    {
        get
        {
            long n = 0;
            foreach (var level in Levels)
                n += level.Length;
            return n;
        }
    }
}
