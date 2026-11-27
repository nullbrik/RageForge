/* RAGEFORGE - Ped Assembler */

using RageForge.Core.Carve;
using RageForge.Core.Model;
using RageForge.Core.Ttpf;

namespace RageForge.Core.Peds;

public sealed record AssembledPed(PedCharacter Character, IReadOnlyList<IReadOnlyList<DecodedMesh>> Pieces, string? FaceSource)
{
    public IReadOnlyList<DecodedMesh> Meshes { get; } = [.. Pieces.SelectMany(p => p)];

    public int Textured => Meshes.Count(m => m.Texture is not null);
}

public static class PedAssembler
{
    public const uint MaterialMaskParam = 0x739e_3d0d;
    private const ulong SkinKey = 0x534b_494e_0000_0000;
    private const ulong ClothKey = 0x434c_4f54_0000_0000;
    private const int SkinTexels = 4;
    private const int ClothFloor = 48;
    private const float ClothRange = 0.62f;
    private const float RootExtent = 0.12f;
    private const float RootHeight = -0.85f;

    public static AssembledPed Assemble(string gameRoot, PedCharacter character, TextureCatalog catalog, FacePresets faces)
    {
        var pieces = new IReadOnlyList<DecodedMesh>[character.Pieces.Count];
        Parallel.For(0, pieces.Length, i =>
        {
            var e = character.Pieces[i].Entry;
            try
            {
                var meshes = Drawable.ExtractMeshes(RageResource.Load(Path.Combine(gameRoot, e.Archive), e.ToCarved(), withGfx: true), catalog);
                pieces[i] = character.Pieces[i].Slot == PedSlot.Clothing && AtRoot(meshes) ? [] : meshes;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException)
            {
                pieces[i] = [];
            }
        });

        var (face, source) = character.Head is { } head ? faces.For(head.Name) : (null, null);
        var faceTexture = face is { } f ? catalog.Decode(f.Albedo) : null;
        var faceNormal = face is { Normal: not 0 } fn ? catalog.Decode(fn.Normal) : null;
        var skin = faceTexture is { } ft ? SkinTone(ft) : (DecodedTexture?)null;
        var cloth = new Dictionary<ulong, DecodedTexture?>();

        for (int i = 0; i < pieces.Length; i++)
        {
            var slot = character.Pieces[i].Slot;
            foreach (var m in pieces[i])
            {
                if (m.Texture is not null) continue;
                if (slot == PedSlot.Head && faceTexture is { } tex)
                {
                    Apply(m, tex, face!.Value.Albedo);
                    if (faceNormal is { } n) m.NormalMap = n;
                }
                else if (slot == PedSlot.Skin && skin is { } s)
                    Apply(m, s, SkinKey ^ face!.Value.Albedo);
                else if (m.TextureParams.FirstOrDefault(p => p.Param == MaterialMaskParam) is { Hash: not 0 } mask)
                {
                    if (!cloth.TryGetValue(mask.Hash, out var shade))
                        cloth[mask.Hash] = shade = catalog.Decode(mask.Hash) is { } raw ? Cloth(raw, ClothKey ^ mask.Hash) : null;
                    if (shade is { } c)
                        Apply(m, c, ClothKey ^ mask.Hash);
                }
            }
        }
        return new AssembledPed(character, pieces, source);
    }

    private static bool AtRoot(List<DecodedMesh> meshes)
    {
        var points = meshes.SelectMany(m => m.Positions).ToList();
        if (points.Count == 0) return false;
        float minX = points.Min(p => p.X), maxX = points.Max(p => p.X);
        float minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        float minZ = points.Min(p => p.Z), maxZ = points.Max(p => p.Z);
        float extent = MathF.Max(maxX - minX, MathF.Max(maxY - minY, maxZ - minZ));
        return extent < RootExtent && (minZ + maxZ) * 0.5f < RootHeight && MathF.Abs(minX + maxX) * 0.5f < RootExtent;
    }

    private static void Apply(DecodedMesh m, DecodedTexture texture, ulong key)
    {
        m.Texture = texture;
        m.TextureHash = key;
        m.TextureCutout = false;
        m.TextureFallback = false;
    }

    public static DecodedTexture SkinTone(DecodedTexture face)
    {
        int w = (int)face.Width, h = (int)face.Height;
        var r = new List<byte>();
        var g = new List<byte>();
        var b = new List<byte>();
        for (int y = h * 2 / 10; y < h * 4 / 10; y++)
            for (int x = w * 3 / 10; x < w * 7 / 10; x++)
            {
                int o = 4 * (y * w + x);
                r.Add(face.Rgba[o]);
                g.Add(face.Rgba[o + 1]);
                b.Add(face.Rgba[o + 2]);
            }
        byte Median(List<byte> v)
        {
            if (v.Count == 0) return 160;
            v.Sort();
            return v[v.Count / 2];
        }
        var rgba = new byte[4 * SkinTexels * SkinTexels];
        byte mr = Median(r), mg = Median(g), mb = Median(b);
        for (int k = 0; k < SkinTexels * SkinTexels; k++)
        {
            rgba[4 * k] = mr;
            rgba[4 * k + 1] = mg;
            rgba[4 * k + 2] = mb;
            rgba[4 * k + 3] = 255;
        }
        return new DecodedTexture { Rgba = rgba, Width = SkinTexels, Height = SkinTexels, Mip = 0, Key = SkinKey ^ face.Key };
    }

    public static DecodedTexture Cloth(DecodedTexture mask, ulong key)
    {
        var rgba = new byte[mask.Rgba.Length];
        for (int o = 0; o + 3 < rgba.Length; o += 4)
        {
            byte v = (byte)Math.Clamp(ClothFloor + mask.Rgba[o + 1] * ClothRange, 0, 255);
            rgba[o] = v;
            rgba[o + 1] = v;
            rgba[o + 2] = v;
            rgba[o + 3] = 255;
        }
        return new DecodedTexture { Rgba = rgba, Width = mask.Width, Height = mask.Height, Mip = 0, Key = key };
    }
}
