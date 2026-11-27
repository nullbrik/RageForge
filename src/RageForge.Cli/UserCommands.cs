/* RAGEFORGE - User Commands */

using System.Numerics;
using System.Text;
using RageForge.Core.Audio;
using RageForge.Core.Carve;
using RageForge.Core.Imaging;
using RageForge.Core.Index;
using RageForge.Core.Model;
using RageForge.Core.Peds;
using RageForge.Core.Ttpf;
using RageForge.Core.Video;
using RageForge.Core.World;

static class UserCommands
{
    private const int MaxTextBytes = 64 * 1024;

    public static int? Run(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        return Dispatch(args);
    }

    private static int? Dispatch(string[] args) => args switch
    {
        ["categories", var root] => Categories(root),
        ["archives", var root] => Archives(root),
        ["search", var root, var text, ..] => Search(root, text, args.Length >= 4 && int.TryParse(args[3], out var n) ? n : 50),
        ["show", var root, var name] => Show(root, name),
        ["peds", var root, ..] => Peds(root, args.Length >= 3 ? args[2] : null),
        _ => null,
    };

    public static void PrintUsage()
    {
        Console.WriteLine("RageForge - RAGE engine archive toolkit (RPF9 / TTPF)");
        Console.WriteLine();
        Console.WriteLine("Browse (same views as the Explorer):");
        Console.WriteLine("  rageforge index <gameRoot>                       Index every archive (cached)");
        Console.WriteLine("  rageforge categories <gameRoot>                  Categories with item counts");
        Console.WriteLine("  rageforge archives <gameRoot>                    Archives with item counts");
        Console.WriteLine("  rageforge list <gameRoot> [category] [n]         List indexed files of a category");
        Console.WriteLine("  rageforge search <gameRoot> <text> [n]           Search every archive by name");
        Console.WriteLine("  rageforge show <gameRoot> <name>                 Preview: model, textures, audio, video, place, text or hex");
        Console.WriteLine("  rageforge peds <gameRoot> [filter]               Characters and animals (same list as Peds)");
        Console.WriteLine();
        Console.WriteLine("Formats:");
        Console.WriteLine("  rageforge info <archive.rpf>                     Show the RPF9 header of an archive");
        Console.WriteLine("  rageforge ttpf list <store.ttpf>                 List textures in a store");
        Console.WriteLine("  rageforge shaders <shaders.rpf> [n] [--hlsl]     Shader cache summary or one shader's disassembly");
        Console.WriteLine("  rageforge materials <gameRoot> <model>           Shader parameters and textures of a model");
        Console.WriteLine();
        Console.WriteLine($"Categories: {string.Join(", ", Enum.GetNames<AssetCategory>())}");
    }

    private static List<IndexEntry> Load(string root)
    {
        var all = GameIndexer.LoadCached(root, GameIndexer.DefaultIndexDir);
        if (all.Count == 0)
            Console.Error.WriteLine($"No index for '{root}'. Run 'rageforge index {root}' first.");
        return all;
    }

    private static IndexEntry? Find(List<IndexEntry> all, string name)
    {
        var hit = all.FirstOrDefault(e => e.Display.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && Categorizer.IsModel(e.Category))
            ?? all.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (hit is null)
            Console.Error.WriteLine($"'{name}' is not in the index. Try 'rageforge search'.");
        return hit;
    }

    private static IReadOnlyDictionary<uint, ModelRef> Models(List<IndexEntry> all) => ModelLookup.Build(all).Models;

    private static string Size(ulong bytes) => bytes switch
    {
        >= 1UL << 30 => $"{bytes / (double)(1UL << 30):F1} GB",
        >= 1UL << 20 => $"{bytes / (double)(1UL << 20):F1} MB",
        >= 1UL << 10 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes} B",
    };

    private static int Categories(string root)
    {
        var all = Load(root);
        foreach (var g in all.GroupBy(e => e.Category).OrderBy(g => Categorizer.Label(g.Key)))
            Console.WriteLine($"{Categorizer.Label(g.Key),-22} {g.Count(),10:N0}   ({g.Key})");
        return all.Count == 0 ? 1 : 0;
    }

    private static int Archives(string root)
    {
        var all = Load(root);
        foreach (var g in all.GroupBy(e => e.Archive).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            Console.WriteLine($"{g.Key,-40} {g.Count(),10:N0}");
        return all.Count == 0 ? 1 : 0;
    }

    private static int Search(string root, string text, int take)
    {
        var all = Load(root);
        var hits = all.Where(e => e.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var e in hits.Take(take))
            Console.WriteLine($"{e.Display,-50} {Categorizer.Label(e.Category),-18} {Size(e.Size),10} {e.Archive}");
        Console.WriteLine(hits.Count > take ? $"{hits.Count:N0} matches ({take} shown)" : $"{hits.Count:N0} matches");
        return 0;
    }

    private static int Show(string root, string name)
    {
        var all = Load(root);
        if (Find(all, name) is not { } e) return 1;
        var path = Path.Combine(root, e.Archive);
        Console.WriteLine($"{e.Display}    {Categorizer.Label(e.Category)}    {Size(e.Size)}    {e.Archive} @ 0x{e.Offset:x}");

        if (e.Tag is "it" or "mp" && MapPlacement.Parse(RageResource.Load(path, e.ToCarved(), withGfx: false)) is { } mp)
        {
            var models = Models(all);
            Console.WriteLine($"{(mp.IsInterior ? "Interior" : "Map block")}: {mp.Entities.Count:N0} objects, {mp.Entities.Select(x => x.Archetype).Distinct().Count():N0} models"
                + $" ({mp.Entities.Select(x => x.Archetype).Distinct().Count(models.ContainsKey):N0} found), {mp.Rooms.Count} rooms, {mp.PortalCount} portals, {mp.EntitySets.Count} entity sets");
            if (e.HasBounds) Console.WriteLine($"Centre ({e.Bounds.X:F1}, {e.Bounds.Y:F1}, {e.Bounds.Z:F1}), radius {e.Bounds.W:F0} m");
            foreach (var g in mp.Entities.GroupBy(x => x.Archetype).OrderByDescending(g => g.Count()).Take(15))
                Console.WriteLine($"  {g.Count(),5} x {(models.TryGetValue(g.Key, out var m) ? m.Name : $"0x{g.Key:x8}")}");
            return 0;
        }
        if (e.Tag == "td")
        {
            var textures = TextureDictionary.Read(RageResource.Load(path, e.ToCarved(), withGfx: true)).Values.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
            Console.WriteLine($"Texture dictionary: {textures.Count} textures");
            foreach (var t in textures)
                Console.WriteLine($"  {t.Name,-40} {t.Width,5} x {t.Height,-5} {TtpfFormats.DxgiName(t.Dxgi)}");
            return 0;
        }
        if (Categorizer.IsModel(e.Category))
        {
            var meshes = Drawable.ExtractMeshes(RageResource.Load(path, e.ToCarved(), withGfx: true), TextureCatalog.Build(root));
            Console.WriteLine($"Model: {meshes.Count} meshes ({meshes.Count(m => m.Texture is not null)} textured), "
                + $"{meshes.Sum(m => (long)m.Positions.Count):N0} vertices, {meshes.Sum(m => (long)m.Indices.Count / 3):N0} triangles");
            foreach (var m in meshes)
            {
                var src = m.TextureName ?? (m.TextureHash is { } h ? $"{h:x16}" : "none");
                Console.WriteLine($"  {m.Pass,-7} {m.Positions.Count,8:N0} vertices   texture {src} {(m.Texture is { } t ? $"{t.Width}x{t.Height}" : "-")}");
            }
            return 0;
        }

        var data = RageResource.ReadAll(path, e.ToCarved());
        switch (e.Kind)
        {
            case ResourceKind.Audio:
                var tracks = AwcDecoder.Decode(data);
                Console.WriteLine($"Audio: {tracks.Count} track(s)");
                return 0;
            case ResourceKind.Video:
                Console.WriteLine(Bk2Info.Parse(data, (ulong)data.Length) is { } bk
                    ? $"Bink 2 video: {bk.Width} x {bk.Height}, {bk.Frames:N0} frames, {bk.Fps:0.##} fps, {bk.Seconds:F1} s"
                    : "Bink 2 video (header not recognised)");
                return 0;
            case ResourceKind.Xml or ResourceKind.Text:
                Console.WriteLine(Encoding.UTF8.GetString(data, 0, Math.Min(data.Length, MaxTextBytes)));
                return 0;
        }
        for (int o = 0; o < Math.Min(data.Length, 512); o += 16)
        {
            var row = data.AsSpan(o, Math.Min(16, data.Length - o));
            var ascii = new string([.. row.ToArray().Select(b => b is >= 32 and < 127 ? (char)b : '.')]);
            Console.WriteLine($"  {o:x8}  {Convert.ToHexString(row).ToLowerInvariant(),-32}  {ascii}");
        }
        return 0;
    }

    private static int Peds(string root, string? filter)
    {
        var all = Load(root);
        var peds = PedLibrary.Build(all).Where(c => filter is null || c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var c in peds)
            Console.WriteLine($"{c.Name,-44} {c.KindLabel,-10} {c.Body,-5} {c.Pieces.Count,3} pieces   {string.Join(", ", c.Pieces.Select(p => p.Name))}");
        Console.WriteLine($"{peds.Count:N0} characters");
        return all.Count == 0 ? 1 : 0;
    }
}
