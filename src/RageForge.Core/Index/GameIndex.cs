/* RAGEFORGE - Game Index */

using System.Collections.Concurrent;
using RageForge.Core.Carve;

namespace RageForge.Core.Index;

public sealed class IndexEntry
{
    public required string Archive { get; init; }
    public required ulong Offset { get; init; }
    public required ulong Size { get; init; }
    public required ResourceKind Kind { get; init; }
    public required string Sub { get; init; }
    public required string Name { get; init; }
    public string? Tag { get; init; }
    public required AssetCategory Category { get; init; }
    public required ZlibStream[] Streams { get; init; }
    public System.Numerics.Vector4 Bounds { get; init; }
    public (uint Hash, int Drawable)[]? Children { get; init; }
    public World.ArchetypeDef[]? Archetypes { get; init; }

    public bool HasBounds => Bounds.W > 0;

    public bool IsRaw => Streams.Length == 0;

    public string Display => Tag is null ? Name : $"{Name}.#{Tag}";

    public CarvedEntry ToCarved() => new()
    {
        Id = $"{Path.GetFileNameWithoutExtension(Archive)}@{Offset:x}",
        Offset = Offset,
        Csize = Size,
        Usize = Size,
        RawSize = IsRaw ? Size : 0,
        Streams = [.. Streams],
        Kind = Kind,
        Sub = Sub,
        Ext = string.Empty,
        Name = Name,
        RType = Tag,
    };
}

public sealed record ArchiveIndex(string Archive, long Length, long Stamp, List<IndexEntry> Entries);

public static class GameIndexer
{
    public static IEnumerable<string> FindFiles(string root, string pattern) =>
        Directory.EnumerateFiles(root, pattern, new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true });

    private const int FormatVersion = 9;

    public static string DefaultIndexDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RageForge", "index");

    public static List<IndexEntry> IndexGame(string gameRoot, string indexDir, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(indexDir);
        var archives = GameIndexer.FindFiles(gameRoot, "*.rpf")
            .Select(p => new FileInfo(p))
            .OrderBy(f => f.Length)
            .ToList();

        var all = new List<IndexEntry>();
        for (int i = 0; i < archives.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = archives[i];
            var rel = Path.GetRelativePath(gameRoot, file.FullName).Replace('\\', '/');
            var cache = Path.Combine(indexDir, rel.Replace('/', '_') + ".rfx");
            long stamp = file.LastWriteTimeUtc.Ticks;

            var cached = Load(cache);
            if (cached is not null && cached.Length == file.Length && cached.Stamp == stamp)
            {
                all.AddRange(cached.Entries);
                continue;
            }

            string prefix = $"[{i + 1}/{archives.Count}] {rel}";
            var index = IndexArchive(file.FullName, rel, s => progress?.Report($"{prefix}: {s}"), ct);
            Save(index with { Length = file.Length, Stamp = stamp }, cache);
            all.AddRange(index.Entries);
        }

        progress?.Report($"Indexed {all.Count:N0} entries in {archives.Count} archives.");
        return all;
    }

    public static List<IndexEntry> LoadCached(string gameRoot, string indexDir)
    {
        var all = new List<IndexEntry>();
        if (!Directory.Exists(gameRoot) || !Directory.Exists(indexDir))
            return all;
        foreach (var path in GameIndexer.FindFiles(gameRoot, "*.rpf"))
        {
            var file = new FileInfo(path);
            var rel = Path.GetRelativePath(gameRoot, path).Replace('\\', '/');
            if (Load(Path.Combine(indexDir, rel.Replace('/', '_') + ".rfx")) is { } idx
                && idx.Length == file.Length && idx.Stamp == file.LastWriteTimeUtc.Ticks)
                all.AddRange(idx.Entries);
        }
        return all;
    }

    public static ArchiveIndex IndexArchive(string path, string relative, Action<string>? progress = null, CancellationToken ct = default)
    {
        var entries = Rpf9Carver.ScanArchive(path, (d, t) => progress?.Invoke($"scanning {100.0 * d / t:F0}%"), ct: ct);
        var named = new ConcurrentDictionary<int, (string Name, string Tag, AssetCategory? Skel)>();
        var placements = new ConcurrentDictionary<int, (bool Interior, uint[] Archetypes, System.Numerics.Vector4 Bounds)>();
        var children = new ConcurrentDictionary<int, (uint, int)[]>();
        var archetypes = new ConcurrentDictionary<int, World.ArchetypeDef[]>();
        var resources = Enumerable.Range(0, entries.Count)
            .Where(i => entries[i].Kind is ResourceKind.Resource or ResourceKind.RageObj)
            .ToArray();
        int done = 0;

        Parallel.ForEach(
            resources,
            new ParallelOptions { CancellationToken = ct },
            () => RageResource.OpenShared(path),
            (i, _, fs) =>
            {
                try
                {
                    var res = RageResource.Load(fs, entries[i], withGfx: false);
                    var sys = res.Sys;
                    if (RageResource.FindResourceName(sys) is { } nm)
                    {
                        var skel = nm.Tag == "ft" ? Categorizer.VehicleFromSkeleton(sys, nm.Name) : null;
                        named[i] = (nm.Name, nm.Tag, skel);
                        if (nm.Tag == "dr" && Model.DrawableTree.Dictionary(res) is { Count: > 1 } kids)
                            children[i] = [.. kids];
                    }
                    else if (Model.TextureDictionary.Probe(sys) is { } td)
                    {
                        named[i] = (td.Name, "td", null);
                    }
                    else if (World.MapPlacement.Parse(res) is { } mp)
                    {
                        placements[i] = (mp.IsInterior, mp.Entities.Select(x => x.Archetype).Distinct().ToArray(), mp.Bounds());
                        if (World.ArchetypeDefs.Parse(res) is { } defs)
                            archetypes[i] = defs;
                    }
                    else if (World.ArchetypeDefs.Parse(res) is { } defs)
                    {
                        named[i] = ($"archetypes_{entries[i].Offset:x}", "at", null);
                        archetypes[i] = defs;
                    }
                    else if (Model.DrawableTree.Dictionary(res) is { } kids)
                    {
                        named[i] = ($"drawables_{entries[i].Offset:x}", "dd", null);
                        children[i] = [.. kids];
                    }
                }
                catch { }

                int d = Interlocked.Increment(ref done);
                if (d % 5000 == 0)
                    progress?.Invoke($"naming {d:N0}/{resources.Length:N0}");
                return fs;
            },
            fs => fs.Dispose());

        if (!placements.IsEmpty)
        {
            var models = new Dictionary<uint, string>();
            foreach (var (_, v) in named)
                if (v.Tag is "dr" or "ft")
                    models.TryAdd(Hashing.Joaat.Hash(v.Name), v.Name);
            foreach (var (i, p) in placements)
                named[i] = (PlacementName(p.Archetypes, models, entries[i].Offset), p.Interior ? "it" : "mp", null);
        }

        var bounds = placements.ToDictionary(k => k.Key, k => k.Value.Bounds);
        var result = new List<IndexEntry>(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            string? tag = null;
            AssetCategory? skel = null;
            string name;
            if (named.TryGetValue(i, out var n))
            {
                name = n.Name;
                tag = n.Tag;
                skel = n.Skel;
            }
            else
            {
                name = SyntheticName(e);
            }

            result.Add(new IndexEntry
            {
                Archive = relative,
                Offset = e.Offset,
                Size = e.IsRaw ? e.RawSize : e.Usize,
                Kind = e.Kind,
                Sub = e.Sub,
                Name = name,
                Tag = tag,
                Category = Categorizer.Of(e.Kind, name, tag, skel),
                Streams = [.. e.Streams],
                Bounds = bounds.GetValueOrDefault(i),
                Children = children.GetValueOrDefault(i),
                Archetypes = archetypes.GetValueOrDefault(i),
            });
        }

        return new ArchiveIndex(relative, 0, 0, result);
    }

    private static readonly string[] GenericPrefixes = ["prop_", "mint_", "p_", "v_", "cvar_", "w_", "veg_", "lo_", "decal_", "td_"];

    private static string PlacementName(uint[] archetypes, Dictionary<uint, string> models, ulong offset)
    {
        var names = archetypes
            .Select(h => models.TryGetValue(h, out var n) ? n : null)
            .OfType<string>()
            .ToList();
        var specific = names.Where(n => !GenericPrefixes.Any(g => n.StartsWith(g, StringComparison.Ordinal))).ToList();
        var prefix = (specific.Count > 0 ? specific : names)
            .Select(n => string.Join('_', n.Split('_').Take(3)))
            .GroupBy(p => p)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .FirstOrDefault()?.Key;
        return prefix is null ? $"placement_{offset:x}" : $"{prefix}_{offset:x}";
    }

    private static string SyntheticName(CarvedEntry e) => e.Kind switch
    {
        ResourceKind.Audio => $"sound_{e.Offset:x}_{e.Sub}",
        ResourceKind.Video => $"video_{e.Offset:x}_{e.Sub}",
        ResourceKind.Resource => $"res_{e.Offset:x}_r{e.Sub}",
        _ => $"{e.Ext}_{e.Offset:x}",
    };

    public static void Save(ArchiveIndex index, string path)
    {
        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp))
        using (var w = new BinaryWriter(fs))
        {
            w.Write("RFIX"u8);
            w.Write(FormatVersion);
            w.Write(index.Archive);
            w.Write(index.Length);
            w.Write(index.Stamp);
            w.Write(index.Entries.Count);
            foreach (var e in index.Entries)
            {
                w.Write(e.Offset);
                w.Write(e.Size);
                w.Write((byte)e.Kind);
                w.Write(e.Sub);
                w.Write(e.Name);
                w.Write(e.Tag ?? string.Empty);
                w.Write((byte)e.Category);
                w.Write(e.Bounds.X);
                w.Write(e.Bounds.Y);
                w.Write(e.Bounds.Z);
                w.Write(e.Bounds.W);
                w.Write(e.Streams.Length);
                foreach (var s in e.Streams)
                {
                    w.Write(s.Offset);
                    w.Write(s.Csize);
                    w.Write(s.Usize);
                }
                w.Write(e.Children?.Length ?? 0);
                foreach (var (hash, drawable) in e.Children ?? [])
                {
                    w.Write(hash);
                    w.Write(drawable);
                }
                w.Write(e.Archetypes?.Length ?? 0);
                foreach (var a in e.Archetypes ?? [])
                {
                    w.Write(a.Name);
                    w.Write(a.LodDistance);
                    w.Write(a.Radius);
                }
            }
        }
        File.Move(tmp, path, overwrite: true);
    }

    public static ArchiveIndex? Load(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            using var r = new BinaryReader(fs);
            if (!r.ReadBytes(4).AsSpan().SequenceEqual("RFIX"u8) || r.ReadInt32() != FormatVersion)
                return null;
            string archive = r.ReadString();
            long length = r.ReadInt64();
            long stamp = r.ReadInt64();
            int count = r.ReadInt32();
            var entries = new List<IndexEntry>(count);
            var pool = new Dictionary<string, string>(StringComparer.Ordinal);
            string Shared(string v) => pool.TryGetValue(v, out var s) ? s : pool[v] = v;
            for (int i = 0; i < count; i++)
            {
                ulong off = r.ReadUInt64();
                ulong size = r.ReadUInt64();
                var kind = (ResourceKind)r.ReadByte();
                string sub = Shared(r.ReadString());
                string name = r.ReadString();
                string tag = Shared(r.ReadString());
                var cat = (AssetCategory)r.ReadByte();
                var bnd = new System.Numerics.Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                int ns = r.ReadInt32();
                var streams = new ZlibStream[ns];
                for (int k = 0; k < ns; k++)
                    streams[k] = new ZlibStream(r.ReadUInt64(), r.ReadUInt64(), r.ReadUInt64());
                int nc = r.ReadInt32();
                (uint, int)[]? kids = nc == 0 ? null : new (uint, int)[nc];
                for (int k = 0; k < nc; k++)
                    kids![k] = (r.ReadUInt32(), r.ReadInt32());
                int na = r.ReadInt32();
                World.ArchetypeDef[]? defs = na == 0 ? null : new World.ArchetypeDef[na];
                for (int k = 0; k < na; k++)
                    defs![k] = new World.ArchetypeDef(r.ReadUInt32(), r.ReadSingle(), r.ReadSingle());
                entries.Add(new IndexEntry
                {
                    Archive = archive,
                    Offset = off,
                    Size = size,
                    Kind = kind,
                    Sub = sub,
                    Name = name,
                    Tag = tag.Length == 0 ? null : tag,
                    Category = cat,
                    Streams = streams,
                    Bounds = bnd,
                    Children = kids,
                    Archetypes = defs,
                });
            }
            return new ArchiveIndex(archive, length, stamp, entries);
        }
        catch
        {
            return null;
        }
    }
}
