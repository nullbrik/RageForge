/* RAGEFORGE - Asset Categories */

using System.Text.RegularExpressions;
using RageForge.Core.Carve;

namespace RageForge.Core.Index;

public enum AssetCategory : byte
{
    Vehicles,
    AirSea,
    Weapons,
    Characters,
    Clothing,
    Animals,
    Props,
    World,
    OtherModels,
    TextureDicts,
    Audio,
    Video,
    Configs,
    UiText,
    Resources,
    Binary,
    Shaders,
    Interiors,
    MapPlacements,
}

public static partial class Categorizer
{
    public static string Label(AssetCategory c) => c switch
    {
        AssetCategory.Vehicles => "Vehicles",
        AssetCategory.AirSea => "Air & Sea",
        AssetCategory.Weapons => "Weapons",
        AssetCategory.Characters => "Characters",
        AssetCategory.Clothing => "Clothing",
        AssetCategory.Animals => "Animals",
        AssetCategory.Props => "Props",
        AssetCategory.World => "World / Map",
        AssetCategory.OtherModels => "Other models",
        AssetCategory.TextureDicts => "Texture dicts",
        AssetCategory.Audio => "Audio",
        AssetCategory.Video => "Video",
        AssetCategory.Configs => "Configs (XML/PSO)",
        AssetCategory.UiText => "UI & Text",
        AssetCategory.Resources => "Resources",
        AssetCategory.Shaders => "Shaders (RDNA2)",
        AssetCategory.Interiors => "Interiors",
        AssetCategory.MapPlacements => "Map placements",
        _ => "Binary",
    };

    public static bool IsModel(AssetCategory c) => c <= AssetCategory.OtherModels;

    private static readonly string[] AnimalPrefixes = ["crm_", "crb_", "crf_", "crr_", "cra_", "crc_", "cri_", "a_c_"];
    private static readonly string[] WeaponPrefixes = ["w_", "wpn_", "weapon_", "weap_"];
    private static readonly string[] CharacterPrefixes =
        ["a_m_", "a_f_", "s_m_", "s_f_", "u_m_", "u_f_", "g_m_", "g_f_", "cs_", "ig_", "mp_", "player_", "head_", "hc_", "char_"];
    private static readonly string[] PedRigTags = ["_mr1_", "_fr1_", "_mb1_", "_fb1_", "_mo1_", "_fo1_"];
    private static readonly string[] HairBodyWords = ["hair", "beard", "brow", "eyelash", "teeth", "head", "body", "torso", "face"];
    private static readonly string[] WorldPrefixes = ["int_", "mint_", "veg_", "prefab_", "maptile_", "des_", "glass_", "lo_", "sea_"];
    private static readonly string[] AirSeaWords =
        ["heli", "plane", "boat", "jet", "yacht", "dinghy", "seashark", "maverick", "buzzard", "cargobob", "frogger",
         "lazer", "luxor", "shamal", "dodo", "cuban", "mallard", "duster", "titan", "blimp", "submersible", "tug", "jetski",
         "ship", "helicopter", "airplane", "speedboat", "seaplane"];
    private static readonly string[] VehicleParts =
        ["_bumper", "_bonnet", "_spoiler", "_exhaust", "_grille", "_skirt", "_fender", "_chassis", "_wheel", "_rim",
         "_livery", "_windscreen", "_dash", "_steer"];
    private static readonly string[] VehicleWords =
        ["car", "truck", "bus", "taxi", "police", "van", "bike", "moto", "trailer", "tractor", "ambulance",
         "firetruck", "coach", "sedan", "coupe", "suv", "pickup", "forklift", "tanker"];
    private static readonly string[] PropWords =
        ["chair", "table", "lamp", "sign", "phone", "laptop", "bottle", "crate", "barrel", "bench", "bin", "trash",
         "vending", "atm", "sofa", "couch", "stool", "cabinet", "door", "box", "bag", "cup", "plant", "light"];

    [GeneratedRegex(@"^[a-z]{1,2}_?\d{1,3}[a-z]?_")]
    private static partial Regex MapZone();

    public static AssetCategory Of(ResourceKind kind, string? name, string? tag, AssetCategory? skeletonCategory)
    {
        switch (kind)
        {
            case ResourceKind.Shader: return AssetCategory.Shaders;
            case ResourceKind.Audio: return AssetCategory.Audio;
            case ResourceKind.Video: return AssetCategory.Video;
            case ResourceKind.Xml or ResourceKind.Pso: return AssetCategory.Configs;
            case ResourceKind.Rbf or ResourceKind.Gfx or ResourceKind.Text: return AssetCategory.UiText;
            case ResourceKind.Dds or ResourceKind.Png: return AssetCategory.TextureDicts;
        }

        if (tag is "td") return AssetCategory.TextureDicts;
        if (tag is "it") return AssetCategory.Interiors;
        if (tag is "mp") return AssetCategory.MapPlacements;
        if (tag is not ("dr" or "ft"))
            return kind is ResourceKind.Resource or ResourceKind.RageObj ? AssetCategory.Resources : AssetCategory.Binary;

        return skeletonCategory ?? Model(name ?? string.Empty);
    }

    public static AssetCategory Model(string name)
    {
        var lo = name.ToLowerInvariant();
        bool Starts(string[] p) => p.Any(lo.StartsWith);
        bool Has(string[] p) => p.Any(lo.Contains);

        if (Starts(AnimalPrefixes)) return AssetCategory.Animals;
        if (Starts(WeaponPrefixes) || lo.Contains("_weapon") || lo.Contains("_weap_")) return AssetCategory.Weapons;
        if (lo.StartsWith("prop_")) return AssetCategory.Props;

        bool pedRig = Has(PedRigTags);
        if (lo.StartsWith("gmt_")) return AssetCategory.Clothing;
        if (Starts(CharacterPrefixes)) return AssetCategory.Characters;
        if (pedRig) return Has(HairBodyWords) ? AssetCategory.Characters : AssetCategory.Clothing;

        if (Starts(WorldPrefixes) || MapZone().IsMatch(lo) || lo.Contains("_int_") || lo.EndsWith("_int", StringComparison.Ordinal))
            return AssetCategory.World;

        var tokens = lo.Split('_', '.').Select(t => t.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9')).ToArray();
        if (tokens.Any(t => AirSeaWords.Contains(t))) return AssetCategory.AirSea;
        if (Has(VehicleParts) || VehicleWords.Contains(tokens[0])) return AssetCategory.Vehicles;
        if (Has(PropWords)) return AssetCategory.Props;
        return AssetCategory.OtherModels;
    }

    private static readonly string[] PartTokens =
        ["_dmg", "_livery", "_spoiler", "_exhaust", "_bumper", "_skirt", "_bonnet", "_door_", "_mirror", "_wing_", "_badge", "_trim"];

    public static AssetCategory? VehicleFromSkeleton(ReadOnlySpan<byte> sys, string name)
    {
        var lo = name.ToLowerInvariant();
        if (sys.Length < 28_000 || PartTokens.Any(lo.Contains))
            return null;
        if (sys.IndexOf("rotor_main"u8) >= 0 || sys.IndexOf("rudder"u8) >= 0 || sys.IndexOf("propeller"u8) >= 0)
            return AssetCategory.AirSea;
        if (sys.IndexOf("chassis"u8) >= 0 || sys.IndexOf("wheel_lf"u8) >= 0 || sys.IndexOf("bodyshell"u8) >= 0)
            return AssetCategory.Vehicles;
        return null;
    }
}
