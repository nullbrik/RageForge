/* RAGEFORGE - Drawable */

using RageForge.Core.Carve;
using RageForge.Core.Ttpf;

namespace RageForge.Core.Model;

public static class Drawable
{
    public static List<DecodedMesh> ExtractMeshes(RageResource r, TextureCatalog? catalog = null, int? drawable = null)
    {
        var bufs = MeshBlob.FindBuffers(r);
        var geoms = Geometries.Find(r, bufs);
        int? shaderGroup = null;
        if (drawable is { } d)
        {
            var allowed = DrawableTree.GeometryStructs(r, d) ?? [];
            geoms = geoms.Where(g => allowed.Contains((int)g.RecordOff - 8)).ToList();
            shaderGroup = DrawableTree.ShaderGroup(r, d);
        }
        var materials = Materials.Resolve(r, geoms, catalog, shaderGroup);
        var decoded = new DecodedMesh?[geoms.Count];

        Parallel.For(0, geoms.Count, gi =>
        {
            var g = geoms[gi];
            var vb = bufs[g.VertexBuffer];
            var ibb = bufs[g.IndexBuffer];
            if (vb.MeshBlob is not { } header)
                return;
            var mesh = MeshDecode.Decode(Span(r.Gfx, vb), header, Span(r.Gfx, ibb));
            if (mesh.Positions.Count == 0 || mesh.Indices.Count == 0)
                return;
            var mat = materials[gi];
            mesh.TextureHashes = mat.Hashes;
            mesh.TextureHash = mat.StreamHash;
            mesh.TextureName = mat.EmbeddedName;
            mesh.Texture = mat.Texture;
            mesh.TextureCutout = mat.Cutout;
            mesh.Pass = mat.Pass;
            mesh.NormalMap = mat.Normal;
            mesh.NormalHash = mat.NormalHash;
            mesh.TerrainMaterial = mat.Terrain;
            mesh.Shader = mat.Shader;
            mesh.TextureFallback = mat.Fallback;
            mesh.TextureParams = mat.Params;
            if (mat.TintKey is { } tintKey) mesh.Tint = TintPalette.Find(r, tintKey);
            decoded[gi] = mesh;
        });

        var meshes = decoded.OfType<DecodedMesh>().ToList();
        if (catalog is null)
            return meshes;

        var hashes = meshes.Where(m => m.Texture is null && m.TextureHash is not null).Select(m => m.TextureHash!.Value)
            .Concat(meshes.Where(m => m.NormalMap is null && m.NormalHash is not null).Select(m => m.NormalHash!.Value))
            .Concat(meshes.Where(m => m.TerrainMaterial is not null).SelectMany(m => m.TerrainMaterial!.Layers.Append(m.TerrainMaterial.Mask)))
            .Distinct().ToArray();
        var results = new DecodedTexture?[hashes.Length];
        Parallel.For(0, hashes.Length, i => results[i] = catalog.Decode(hashes[i]));
        var textures = new Dictionary<ulong, DecodedTexture?>(hashes.Length);
        for (int i = 0; i < hashes.Length; i++)
            textures[hashes[i]] = results[i];

        foreach (var m in meshes)
        {
            if (m.Texture is null && m.TextureHash is { } h && textures.TryGetValue(h, out var t) && t is { } tex)
            {
                m.Texture = tex;
                m.TextureCutout = Materials.IsCutout(m.Pass, tex);
            }
            if (m.NormalMap is null && m.NormalHash is { } nh && textures.TryGetValue(nh, out var nt) && nt is { } nmap)
                m.NormalMap = nmap;
            if (m.TerrainMaterial is { } tm && textures.TryGetValue(tm.Mask, out var mt) && mt is { } mask)
            {
                var layers = tm.Layers.Select(h => textures.TryGetValue(h, out var lt) ? lt : null).ToArray();
                if (layers.Any(l => l is not null))
                {
                    var layerUv = m.Uvs.FirstOrDefault(u => u.Slot == 4).Uv ?? (m.Uvs.Count > 1 ? m.Uvs[1].Uv : null);
                    m.Terrain = new TerrainLayers(mask, layers, layerUv);
                }
            }
        }
        meshes.RemoveAll(m => m.Pass == MaterialPass.Decal && m.Texture is { } t && Imaging.AlphaAnalysis.IsMagentaPlaceholder(t.Rgba));
        foreach (var m in meshes)
            if (m.Texture is { } t && Imaging.AlphaAnalysis.IsMagentaPlaceholder(t.Rgba))
            {
                m.Texture = null;
                m.TextureCutout = false;
            }
        return meshes;
    }

    private static ReadOnlySpan<byte> Span(byte[] gfx, GfxBuffer buf)
    {
        int start = buf.GfxOff;
        if (start < 0 || start > gfx.Length)
            return ReadOnlySpan<byte>.Empty;
        int len = (int)Math.Min(buf.Bytes, (ulong)(gfx.Length - start));
        return gfx.AsSpan(start, len);
    }
}
