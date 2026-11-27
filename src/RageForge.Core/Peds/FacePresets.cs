/* RAGEFORGE - Face Presets */

using System.Buffers.Binary;
using System.Collections.Concurrent;
using RageForge.Core.Carve;
using RageForge.Core.Hashing;
using RageForge.Core.Index;

namespace RageForge.Core.Peds;

public readonly record struct FaceTextures(ulong Albedo, ulong Normal);

public sealed class FacePresets
{
    public const string ResourceSub = "7a";
    public const uint HashPool = 0x4a;
    public const uint TagTable = 0x101;
    public const uint PresetTable = 0xf795_e8ba;
    public const uint DrawableTag = 0x4bfd_27dd;

    private const ulong SysBase = 0x5000_0000;
    private const int DirectoryPointer = 0x38;
    private const int TagStride = 0x18;
    private const int TagWords = TagStride / 4;
    private const int RecordStride = 0xb8;
    private const ulong RefTypeMask = 0x3f_ffff;
    private const int RefIndexShift = 22;
    private const ulong PoolRef = 7;
    private const ulong RecordRef = 8;
    private const ulong TagRef = 13;
    private const int RecordLookBack = 0x40;

    private readonly Dictionary<uint, FaceTextures> _byDrawable;
    private readonly Dictionary<(string Body, string Ethnicity), string> _donors;

    private FacePresets(Dictionary<uint, FaceTextures> byDrawable, IEnumerable<string> names)
    {
        _byDrawable = byDrawable;
        var linked = names.Where(n => byDrawable.ContainsKey(Joaat.Hash(n)) && HeadKey(n) is not null)
            .OrderBy(n => n.StartsWith("head_", StringComparison.Ordinal) ? 0 : 1).ThenBy(n => n, StringComparer.Ordinal).ToList();
        _donors = [];
        foreach (var n in linked)
        {
            var key = HeadKey(n)!.Value;
            _donors.TryAdd(key, n);
        }
    }

    public static FacePresets Empty { get; } = new([], []);

    public (FaceTextures? Textures, string? Source) For(string headName)
    {
        if (_byDrawable.TryGetValue(Joaat.Hash(headName), out var own)) return (own, headName);
        if (HeadKey(headName) is not { } key) return (null, null);
        var donor = _donors.GetValueOrDefault(key);
        return donor is not null && _byDrawable.TryGetValue(Joaat.Hash(donor), out var borrowed) ? (borrowed, donor) : (null, null);
    }

    public static (string Body, string Ethnicity)? HeadKey(string name)
    {
        var t = name.Split('_');
        for (int i = 0; i + 2 < t.Length; i++)
            if (t[i] == "head" && t[i + 1].Length is >= 3 and <= 4 && t[i + 1][0] is 'm' or 'f' && char.IsDigit(t[i + 1][^1]))
                return (t[i + 1], char.IsDigit(t[i + 2][0]) ? "gen" : t[i + 2]);
        return null;
    }

    public int Count => _byDrawable.Count;

    public bool TryGet(uint drawableHash, out FaceTextures textures) => _byDrawable.TryGetValue(drawableHash, out textures);

    public static FacePresets Load(string gameRoot, IReadOnlyList<IndexEntry> index, CancellationToken ct = default)
    {
        var found = new ConcurrentDictionary<uint, FaceTextures>();
        var groups = index.Where(e => e.Tag is null && e.Sub == ResourceSub && e.Kind == ResourceKind.Resource).GroupBy(e => e.Archive);
        foreach (var g in groups)
        {
            var path = Path.Combine(gameRoot, g.Key);
            if (!File.Exists(path)) continue;
            Parallel.ForEach(g, new ParallelOptions { CancellationToken = ct }, () => RageResource.OpenShared(path), (e, _, fs) =>
            {
                try
                {
                    foreach (var (drawable, tex) in Parse(RageResource.Load(fs, e.ToCarved(), withGfx: false).Sys))
                        found.TryAdd(drawable, tex);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException)
                {
                }
                return fs;
            }, fs => fs.Dispose());
        }
        return new FacePresets(new Dictionary<uint, FaceTextures>(found), index.Where(e => e.Tag == "dr").Select(e => e.Name).Distinct());
    }

    public static List<(uint Drawable, FaceTextures Textures)> Parse(byte[] sys)
    {
        var result = new List<(uint, FaceTextures)>();
        if (sys.Length < DirectoryPointer + 8) return result;
        var dirs = Directory(sys);
        var pool = dirs.Find(d => d.Type == HashPool);
        var tags = dirs.Find(d => d.Type == TagTable);
        var presets = dirs.Find(d => d.Type == PresetTable);
        if (pool.Length == 0 || tags.Length == 0 || presets.Length == 0) return result;
        int tagDir = dirs.IndexOf(tags);

        for (int ent = tags.Offset; ent + TagStride <= tags.Offset + tags.Length; ent += TagStride)
        {
            if (U32(sys, ent) != DrawableTag) continue;
            ulong value = U64(sys, ent + 16);
            long poolAt = pool.Offset + (long)(value >> RefIndexShift) * 4;
            if ((value & RefTypeMask) != PoolRef || poolAt + 4 > pool.Offset + pool.Length) continue;
            uint drawable = U32(sys, (int)poolAt);
            int tagWord = (ent - tags.Offset) / 4;
            if (FindRecord(sys, dirs, presets, tagDir, tagWord) is { } tex)
                result.Add((drawable, tex));
        }
        return result;
    }

    private static FaceTextures? FindRecord(byte[] sys, List<(uint Type, int Offset, int Length)> dirs, (uint Type, int Offset, int Length) presets, int tagDir, int tagWord)
    {
        for (int q = presets.Offset; q + 16 <= presets.Offset + presets.Length; q += 8)
        {
            ulong v = U64(sys, q);
            if ((v & RefTypeMask) != TagRef || Target(dirs, v) != tagDir) continue;
            int first = (int)(v >> RefIndexShift), count = (int)(U32(sys, q + 8) & 0xffff);
            if (tagWord < first || tagWord >= first + Math.Max(count, 1) * TagWords) continue;
            for (int r = Math.Max(presets.Offset + 8, q - RecordLookBack); r < q; r += 8)
            {
                ulong rv = U64(sys, r);
                if ((rv & RefTypeMask) != RecordRef || Target(dirs, rv) is not { } rd) continue;
                var records = dirs[rd];
                int n = Math.Max(1, (int)U32(sys, r - 8));
                long at = records.Offset + (long)(rv >> RefIndexShift) * 4;
                for (int k = 0; k < n; k++, at += RecordStride)
                {
                    if (at + 0x20 > records.Offset + records.Length || at + 0x20 > sys.Length) break;
                    ulong albedo = U64(sys, (int)at + 0x10), normal = U64(sys, (int)at + 0x18);
                    if (albedo != 0) return new FaceTextures(albedo, normal);
                }
            }
        }
        return null;
    }

    private static int? Target(List<(uint Type, int Offset, int Length)> dirs, ulong reference)
    {
        int k = (int)(reference & RefTypeMask) - 1;
        return k >= 0 && k < dirs.Count ? k : null;
    }

    private static List<(uint Type, int Offset, int Length)> Directory(byte[] sys)
    {
        var dirs = new List<(uint, int, int)>();
        ulong head = U64(sys, DirectoryPointer);
        if (head >> 28 != SysBase >> 28) return dirs;
        int o = (int)(head - SysBase);
        while (o >= 0 && o + 16 <= sys.Length)
        {
            ulong p = U64(sys, o + 8);
            if (p >> 28 != SysBase >> 28) break;
            int off = (int)(p - SysBase), len = (int)U32(sys, o + 4);
            if (off < 0 || off + len > sys.Length) break;
            dirs.Add((U32(sys, o), off, len));
            o += 16;
        }
        return dirs;
    }

    private static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));

    private static ulong U64(byte[] b, int o) => BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(o));
}
