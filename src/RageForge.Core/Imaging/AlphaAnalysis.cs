/* RAGEFORGE - Alpha Analysis */

namespace RageForge.Core.Imaging;

public static class AlphaAnalysis
{
    public static bool IsMagentaPlaceholder(byte[] rgba)
    {
        int n = rgba.Length / 4;
        if (n == 0) return false;
        int step = Math.Max(1, n / 4096), hits = 0, samples = 0;
        for (int i = 0; i < n; i += step)
        {
            int o = i * 4;
            samples++;
            if (rgba[o] > 200 && rgba[o + 1] < 60 && rgba[o + 2] > 200)
                hits++;
        }
        return hits * 4 >= samples;
    }

    public static bool IsCutout(ReadOnlySpan<byte> rgba)
    {
        int total = 0, clear = 0, solid = 0;
        for (int i = 3; i < rgba.Length; i += 16)
        {
            byte a = rgba[i];
            total++;
            if (a < 16) clear++;
            else if (a > 239) solid++;
        }
        if (total == 0) return false;
        float clearFrac = (float)clear / total;
        float binaryFrac = (float)(clear + solid) / total;
        return clearFrac > 0.02f && binaryFrac > 0.85f;
    }
}
