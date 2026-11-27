/* RAGEFORGE - Ped Library */

using System.Text.RegularExpressions;
using RageForge.Core.Index;

namespace RageForge.Core.Peds;

public enum PedKind
{
    Cutscene,
    Story,
    Unique,
    Special,
    Player,
    World,
    Animal,
}

public enum PedSlot
{
    Head,
    Eyes,
    Clothing,
    Skin,
    Part,
}

public sealed record PedPiece(IndexEntry Entry, PedSlot Slot)
{
    public string Name => Entry.Name;
}

public sealed record PedCharacter(string Name, PedKind Kind, string Body, IReadOnlyList<PedPiece> Pieces)
{
    public string KindLabel => Kind switch
    {
        PedKind.Cutscene => "Cutscene",
        PedKind.Story => "Story",
        PedKind.Unique => "Unique",
        PedKind.Special => "Special",
        PedKind.Player => "Player",
        PedKind.World => "World ped",
        _ => "Animal",
    };

    public PedPiece? Head => Pieces.FirstOrDefault(p => p.Slot == PedSlot.Head);
}

public static partial class PedLibrary
{
    public const ulong StubSize = 4096;

    [GeneratedRegex(@"^[mf][a-z]{1,2}\d$")]
    private static partial Regex BodyToken();

    [GeneratedRegex(@"^(cr[a-z](?:_[a-z0-9]+)+?_[mf]_\d\d)_([a-z]+)_00$")]
    private static partial Regex AnimalPart();

    [GeneratedRegex(@"^legacypedworld_([mf][a-z]\d)_\d+_fullbody_ws00_full$")]
    private static partial Regex WorldPed();

    private static readonly string[] SkippedTypes = ["hair", "beard", "brows", "lashes", "armpithair", "bodyhair", "stubble", "eyecap"];

    private static readonly (string Family, string[] Prefixes)[] Families =
    [
        ("outer", ["jacket", "coat", "hoodie", "blazer", "parka", "cardigan", "poncho", "cape", "trenchcoat", "raincoat"]),
        ("top", ["shirt", "tshirt", "tanktop", "sweatshirt", "sweater", "polo", "blouse", "top", "crop", "tube", "corset", "bodysuit"]),
        ("bottom", ["pants", "shorts", "skirt", "jeans", "leggings", "trousers", "joggers", "chinos", "basebottoms"]),
        ("full", ["dress", "fullbody", "jumpsuit", "overalls", "romper", "robe", "gown", "boilersuit", "swimsuit", "onesie"]),
        ("bra", ["bra"]),
        ("feet", ["shoes", "boots", "sneakers", "sandals", "heels", "slippers", "flipflops", "trainers", "loafers"]),
    ];

    public static List<PedCharacter> Build(IReadOnlyList<IndexEntry> index)
    {
        var drawables = index.Where(e => e.Tag == "dr")
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(e => e.Size).First())
            .Where(e => e.Size >= StubSize)
            .ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);

        var result = new List<PedCharacter>();
        result.AddRange(Humans(drawables));
        result.AddRange(WorldPeds(drawables));
        result.AddRange(Animals(drawables));
        return [.. result.OrderBy(c => c.Kind).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public static (string Owner, string Type, string Body, string Tail)? Split(string name)
    {
        var t = name.Split('_');
        for (int b = 1; b < t.Length; b++)
        {
            if (!BodyToken().IsMatch(t[b])) continue;
            if (b < 2) return null;
            return (string.Join('_', t[..(b - 1)]), t[b - 1], t[b], string.Join('_', t[(b + 1)..]));
        }
        return null;
    }

    private static PedKind? KindOf(string owner) => owner switch
    {
        _ when owner.StartsWith("cs_", StringComparison.Ordinal) => PedKind.Cutscene,
        _ when owner.StartsWith("player_", StringComparison.Ordinal) => PedKind.Player,
        _ when owner.StartsWith("u_", StringComparison.Ordinal) => PedKind.Unique,
        _ when owner.StartsWith("sc_", StringComparison.Ordinal) => PedKind.Special,
        _ when owner.StartsWith("hc_", StringComparison.Ordinal) => PedKind.Story,
        _ => null,
    };

    private static IEnumerable<PedCharacter> Humans(Dictionary<string, IndexEntry> drawables)
    {
        var groups = new Dictionary<string, List<(IndexEntry Entry, string Type, string Body, string Tail)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in drawables.Values)
        {
            if (Split(e.Name) is not { } s || KindOf(s.Owner) is null) continue;
            if (!groups.TryGetValue(s.Owner, out var list)) groups[s.Owner] = list = [];
            list.Add((e, s.Type, s.Body, s.Tail));
        }

        foreach (var (owner, parts) in groups)
        {
            if (owner.StartsWith("hc_cs_", StringComparison.Ordinal) && groups.ContainsKey(owner[3..])) continue;
            var heads = parts.Where(p => p.Type == "head" && !p.Tail.Contains("eyes") && !p.Tail.Contains("eyecap")).ToList();
            var eyes = parts.Where(p => p.Type == "head" && p.Tail.Contains("eyes")).ToList();
            if (owner.StartsWith("cs_", StringComparison.Ordinal) && groups.TryGetValue("hc_" + owner, out var donor))
            {
                if (heads.Count == 0) heads.AddRange(donor.Where(p => p.Type == "head" && !p.Tail.Contains("eyes") && !p.Tail.Contains("eyecap")));
                if (eyes.Count == 0) eyes.AddRange(donor.Where(p => p.Type == "head" && p.Tail.Contains("eyes")));
            }
            var clothing = parts.Where(p => p.Type != "head" && !SkippedTypes.Any(p.Type.StartsWith)).ToList();
            if (clothing.Count == 0) continue;

            var pieces = new List<PedPiece>();
            var head = heads.OrderBy(p => Preference(p.Entry.Name)).ThenBy(p => p.Entry.Name, StringComparer.Ordinal).FirstOrDefault();
            if (head.Entry is not null)
            {
                pieces.Add(new PedPiece(head.Entry, PedSlot.Head));
                var stem = head.Entry.Name.EndsWith("_full", StringComparison.Ordinal) ? head.Entry.Name[..^5] : head.Entry.Name;
                var eye = eyes.OrderBy(p => p.Entry.Name.StartsWith(stem + "_eyes", StringComparison.Ordinal) ? 0 : 1).ThenBy(p => Preference(p.Entry.Name)).FirstOrDefault();
                if (eye.Entry is not null) pieces.Add(new PedPiece(eye.Entry, PedSlot.Eyes));
            }

            var chosen = clothing
                .OrderBy(p => Preference(p.Entry.Name))
                .ThenBy(p => p.Entry.Name, StringComparer.Ordinal)
                .GroupBy(p => FamilyOf(p.Type))
                .ToDictionary(g => g.Key, g => g.First());
            if (chosen.ContainsKey("full") && (chosen.ContainsKey("top") || chosen.ContainsKey("bottom")))
                chosen.Remove("full");
            foreach (var p in chosen.Values.OrderBy(p => p.Entry.Name, StringComparer.Ordinal))
                pieces.Add(new PedPiece(p.Entry, p.Type.StartsWith("body", StringComparison.Ordinal) ? PedSlot.Skin : PedSlot.Clothing));

            var body = (head.Entry is not null ? head.Body : null) ?? parts.GroupBy(p => p.Body).OrderByDescending(g => g.Count()).First().Key;
            if (!chosen.Values.Any(p => p.Type.StartsWith("body", StringComparison.Ordinal)))
            {
                if (!chosen.Values.Any(p => p.Type is "hand" or "hands" || p.Type.StartsWith("glove", StringComparison.Ordinal)) && BaseSkin(drawables, "hand", body) is { } hand)
                    pieces.Add(new PedPiece(hand, PedSlot.Skin));
                if (!chosen.Values.Any(p => p.Type == "feet") && BaseSkin(drawables, "feet", body) is { } feet)
                    pieces.Add(new PedPiece(feet, PedSlot.Skin));
            }
            yield return new PedCharacter(owner, KindOf(owner)!.Value, body, pieces);
        }
    }

    private static int Preference(string name) =>
        (name.EndsWith("_full", StringComparison.Ordinal) ? 0 : 2) + (name.Contains("_alt_") || name.Contains("_cull_") ? 1 : 0);

    private static string FamilyOf(string type)
    {
        foreach (var (family, prefixes) in Families)
            if (prefixes.Any(type.StartsWith))
                return family;
        return type;
    }

    private static IndexEntry? BaseSkin(Dictionary<string, IndexEntry> drawables, string part, string body)
    {
        foreach (var suffix in new[] { "nude_cull", "nude_full" })
            if (drawables.TryGetValue($"{part}_{body}_000_{suffix}", out var e))
                return e;
        return null;
    }

    private static IEnumerable<PedCharacter> WorldPeds(Dictionary<string, IndexEntry> drawables)
    {
        foreach (var e in drawables.Values)
            if (WorldPed().Match(e.Name) is { Success: true } m)
                yield return new PedCharacter(e.Name[..e.Name.IndexOf("_fullbody", StringComparison.Ordinal)], PedKind.World, m.Groups[1].Value, [new PedPiece(e, PedSlot.Part)]);
    }

    private static IEnumerable<PedCharacter> Animals(Dictionary<string, IndexEntry> drawables)
    {
        var groups = new Dictionary<string, List<PedPiece>>(StringComparer.Ordinal);
        foreach (var e in drawables.Values)
        {
            if (AnimalPart().Match(e.Name) is not { Success: true } m) continue;
            if (!groups.TryGetValue(m.Groups[1].Value, out var list)) groups[m.Groups[1].Value] = list = [];
            list.Add(new PedPiece(e, PedSlot.Part));
        }
        foreach (var (name, pieces) in groups)
            if (pieces.Any(p => p.Name.EndsWith("_body_00", StringComparison.Ordinal)))
                yield return new PedCharacter(name, PedKind.Animal, string.Empty, [.. pieces.OrderBy(p => p.Name, StringComparer.Ordinal)]);
    }

    public static PedCharacter? Find(IReadOnlyList<PedCharacter> all, string name) =>
        all.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? all.FirstOrDefault(c => c.Name.Equals(name.TrimEnd('_'), StringComparison.OrdinalIgnoreCase));
}
