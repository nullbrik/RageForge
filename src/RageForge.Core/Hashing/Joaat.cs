/* RAGEFORGE - Jenkins One-At-A-Time Hash */

namespace RageForge.Core.Hashing;

public static class Joaat
{
    public static uint Hash(string text)
    {
        uint h = 0;
        foreach (char ch in text)
        {
            char c = ch is >= 'A' and <= 'Z' ? (char)(ch + 32) : ch;
            h += c;
            h += h << 10;
            h ^= h >> 6;
        }
        h += h << 3;
        h ^= h >> 11;
        h += h << 15;
        return h;
    }
}
