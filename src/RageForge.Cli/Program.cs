/* RAGEFORGE - CLI Entry Point */

using RageForge.Core.Rpf9;
using RageForge.Core.Ttpf;
using RageForge.Core.Imaging;
using RageForge.Core.Carve;
using RageForge.Core.Index;
using RageForge.Core.Model;

if (UserCommands.Run(args) is { } userResult)
    return userResult;

if (args is ["index", var gameRoot])
{
    if (!Directory.Exists(gameRoot)) { Console.Error.WriteLine($"Folder not found: {gameRoot}"); return 1; }
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var progress = new SyncProgress(s => Console.Error.Write($"\r{s,-90}"));
    var all = GameIndexer.IndexGame(gameRoot, GameIndexer.DefaultIndexDir, progress);
    Console.Error.WriteLine();
    foreach (var g in all.GroupBy(e => e.Category).OrderBy(g => g.Key))
        Console.WriteLine($"  {Categorizer.Label(g.Key),-20} {g.Count(),9:N0}");
    Console.WriteLine($"{all.Count:N0} entries in {sw.Elapsed.TotalMinutes:F1} min");
    return 0;
}

if (args is ["list", var lRoot, ..])
{
    var all = GameIndexer.LoadCached(lRoot, GameIndexer.DefaultIndexDir);
    IEnumerable<IndexEntry> rows = all;
    if (args.Length >= 3 && Enum.TryParse<AssetCategory>(args[2], true, out var lCat))
        rows = rows.Where(e => e.Category == lCat);
    int take = args.Length >= 4 && int.TryParse(args[3], out var lt) ? lt : 50;
    foreach (var e in rows.Take(take))
        Console.WriteLine($"{e.Display,-50} {Categorizer.Label(e.Category),-18} {e.Archive}");
    return 0;
}

if (args is ["materials", var mRoot, var mName])
{
    var all = GameIndexer.LoadCached(mRoot, GameIndexer.DefaultIndexDir);
    var hit = all.FirstOrDefault(e => e.Name.Equals(mName, StringComparison.OrdinalIgnoreCase) && Categorizer.IsModel(e.Category));
    if (hit is null) { Console.Error.WriteLine($"'{mName}' not in the index."); return 1; }
    var catalog = TextureCatalog.Build(mRoot);
    var res = RageResource.Load(Path.Combine(mRoot, hit.Archive), hit.ToCarved(), withGfx: true);
    foreach (var line in Materials.Describe(res, catalog))
        Console.WriteLine(line);
    return 0;
}

if (args is ["shaders", var sArchive, ..])
{
    var sEntry = Rpf9Carver.ScanArchive(sArchive, null).FirstOrDefault(e => e.Kind == ResourceKind.Shader);
    if (sEntry is null) { Console.Error.WriteLine("No PS09 pipeline cache found."); return 1; }
    var cache = RageForge.Core.Shaders.Ps09Cache.Parse(RageResource.ReadAll(sArchive, sEntry));
    if (cache is null) { Console.Error.WriteLine("Invalid PS09 cache."); return 1; }
    if (args.Length >= 3 && int.TryParse(args[2], out var si) && si < cache.Shaders.Count)
    {
        var sh = cache.Shaders[si];
        Console.WriteLine(args is [_, _, _, "--hlsl"]
            ? RageForge.Core.Shaders.Rdna2Disassembler.PseudoHlsl(sh, cache.Code(sh))
            : RageForge.Core.Shaders.Rdna2Disassembler.Listing(sh, cache.Code(sh)));
        return 0;
    }
    if (args is [_, _, "--coverage"])
    {
        var unknown = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
        long total = 0;
        Parallel.ForEach(cache.Shaders, sh =>
        {
            var insns = RageForge.Core.Shaders.Rdna2Disassembler.Decode(RageForge.Core.Shaders.Rdna2Disassembler.SkipPrologue(cache.Code(sh)));
            Interlocked.Add(ref total, insns.Count);
            foreach (var i in insns.Where(i => i.Mnemonic.Contains("_op") || i.Mnemonic.Contains("_sop") || i.Mnemonic.Contains("_vop") || i.Mnemonic == ".word"))
                unknown.AddOrUpdate($"{i.Encoding} {i.Mnemonic}", 1, (_, c) => c + 1);
        });
        long bad = unknown.Values.Sum();
        Console.WriteLine($"{total:N0} instructions, {bad:N0} unresolved ({100.0 * bad / Math.Max(1, total):F3}%)");
        foreach (var kv in unknown.OrderByDescending(k => k.Value).Take(30))
            Console.WriteLine($"  {kv.Value,9:N0}  {kv.Key}");
        return 0;
    }
    Console.WriteLine($"id {cache.Id}  driver {cache.Driver}  graphics PSOs {cache.GraphicsPsos:N0}  compute PSOs {cache.ComputePsos:N0}  code base 0x{cache.CodeBase:x}");
    foreach (var g in cache.Shaders.GroupBy(s => s.Stage).OrderBy(g => g.Key))
        Console.WriteLine($"  {g.Key,-12} {g.Count(),7:N0}");
    Console.WriteLine($"{cache.Shaders.Count:N0} shaders");
    return 0;
}

if (args is ["ttpf", "list", var storePath])
{
    if (!File.Exists(storePath))
    {
        Console.Error.WriteLine($"File not found: {storePath}");
        return 1;
    }

    var store = TtpfStore.Open(storePath);
    Console.WriteLine($"TTPF store: {Path.GetFileName(storePath)}  ({store.Textures.Count} textures)");
    foreach (var t in store.Textures)
    {
        var flag = t.Decodable ? " " : "!";
        Console.WriteLine($"  {flag} {t.HashHex}  {t.Width,5}x{t.Height,-5} {t.FormatName,-16} mips={t.Mips} tile={t.TileMode} size={t.Size}");
    }
    return 0;
}

if (args is ["info", var path])
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"File not found: {path}");
        return 1;
    }

    var buffer = new byte[Rpf9Header.Size];
    using (var fs = File.OpenRead(path))
    {
        int read = fs.Read(buffer);
        if (read < Rpf9Header.Size)
        {
            Console.Error.WriteLine("File too small to contain an RPF9 header.");
            return 1;
        }
    }

    Rpf9Header h;
    try
    {
        h = Rpf9Header.Read(buffer);
    }
    catch (InvalidDataException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    Console.WriteLine($"RPF9 archive: {Path.GetFileName(path)}");
    Console.WriteLine($"  count       : {h.Count}");
    Console.WriteLine($"  shape       : {(h.Shape == 2 ? "hierarchical" : "flat")} (flags byte0=0x{h.Shape:x2})");
    Console.WriteLine($"  key_id      : {h.KeyId:x8}");
    Console.WriteLine($"  toc_end     : {h.TocEnd}");
    Console.WriteLine($"  toc_field_b : {h.TocFieldB}");
    Console.WriteLine($"  names_size  : {h.NamesSize} (decompressed)");
    Console.WriteLine($"  tail_size   : {h.TailSize}");
    Console.WriteLine($"  toc_hash    : {h.TocHashHex}");
    Console.WriteLine($"  layout ok   : {h.ConstantsMatch}");
    Console.WriteLine("  toc         : encrypted, not decrypted by RageForge (entries are located by scanning; paths and file names from the table of contents are unavailable, displayed names come from resource data or are generated)");
    return 0;
}

UserCommands.PrintUsage();
return args.Length == 0 ? 0 : 1;

sealed class SyncProgress(Action<string> report) : IProgress<string>
{
    private readonly object _gate = new();

    public void Report(string value)
    {
        lock (_gate)
            report(value);
    }
}
