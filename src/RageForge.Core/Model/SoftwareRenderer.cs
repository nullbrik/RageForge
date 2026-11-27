/* RAGEFORGE - Software Renderer */

using RageForge.Core.Ttpf;

namespace RageForge.Core.Model;

public static class SoftwareRenderer
{
    private const byte BgR = 30, BgG = 30, BgB = 34;

    [ThreadStatic] private static float[]? t_depth;

    public static byte[] Render(IReadOnlyList<DecodedMesh> meshes, int width, int height, float yaw, float pitch, float zoom = 1f, byte[]? target = null)
    {
        int pixels = width * height;
        var rgba = target is { } tg && tg.Length == pixels * 4 ? tg : new byte[pixels * 4];
        System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(rgba.AsSpan())
            .Fill(BgR | (uint)BgG << 8 | (uint)BgB << 16 | 0xFF00_0000u);
        var depth = t_depth is { } dp && dp.Length >= pixels ? dp : t_depth = new float[pixels];
        depth.AsSpan(0, pixels).Fill(float.MaxValue);

        float cy = MathF.Cos(yaw), sy = MathF.Sin(yaw);
        float cx = MathF.Cos(pitch), sx = MathF.Sin(pitch);

        Vec3 View(Vec3 p)
        {
            float x = p.X, y = p.Z, z = -p.Y;
            (x, z) = (x * cy + z * sy, -x * sy + z * cy);
            (y, z) = (y * cx + z * sx, -y * sx + z * cx);
            return new Vec3(x, y, z);
        }

        var viewed = new Vec3[meshes.Count][];
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int mi = 0; mi < meshes.Count; mi++)
        {
            var src = meshes[mi].Positions;
            var dst = new Vec3[src.Count];
            for (int i = 0; i < src.Count; i++)
            {
                var v = View(src[i]);
                dst[i] = v;
                if (v.X < minX) minX = v.X;
                if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.Y > maxY) maxY = v.Y;
            }
            viewed[mi] = dst;
        }
        if (minX > maxX) return rgba;

        const int pad = 30;
        float scale = zoom * MathF.Min((width - 2 * pad) / (maxX - minX + 1e-6f), (height - 2 * pad) / (maxY - minY + 1e-6f));
        float ox = (width - (maxX - minX) * scale) / 2 - minX * scale;
        float oy = (height - (maxY - minY) * scale) / 2 - minY * scale;

        var light = Norm(new Vec3(0.4f, 0.7f, 0.6f));

        for (int mi = 0; mi < meshes.Count; mi++)
        {
            var m = meshes[mi];
            var pv = viewed[mi];
            var tex = m.Texture;
            var uv = m.Uvs.Count > 0 && m.Uvs[0].Uv.Count == pv.Length ? m.Uvs[0].Uv : null;
            if (uv is null) tex = null;

            for (int t = 0; t + 2 < m.Indices.Count; t += 3)
            {
                int i0 = (int)m.Indices[t], i1 = (int)m.Indices[t + 1], i2 = (int)m.Indices[t + 2];
                var a = pv[i0]; var b = pv[i1]; var c = pv[i2];
                var fn = Norm(Cross(Sub(b, a), Sub(c, a)));
                float sh = MathF.Max(0.25f, MathF.Abs(fn.X * light.X + fn.Y * light.Y + fn.Z * light.Z) * 0.8f + 0.25f);

                var sa = new P(a.X * scale + ox, height - (a.Y * scale + oy), a.Z);
                var sb = new P(b.X * scale + ox, height - (b.Y * scale + oy), b.Z);
                var sc = new P(c.X * scale + ox, height - (c.Y * scale + oy), c.Z);

                if (tex is { } tx && uv is not null)
                    Fill(rgba, depth, width, height, sa, sb, sc, sh, tx, m.TextureCutout, uv[i0], uv[i1], uv[i2]);
                else
                    Fill(rgba, depth, width, height, sa, sb, sc, sh, null, false, default, default, default);
            }
        }

        return rgba;
    }

    private readonly record struct P(float X, float Y, float Z);

    private static void Fill(byte[] rgba, float[] depth, int w, int h, P a, P b, P c, float shade,
        DecodedTexture? tex, bool cutout, Vec2 ua, Vec2 ub, Vec2 uc)
    {
        float area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (MathF.Abs(area) < 1e-6f) return;
        float inv = 1f / area;

        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))));
        int maxX = Math.Min(w - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))));
        int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))));
        int maxY = Math.Min(h - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))));

        byte fr = (byte)(150 * shade), fg = (byte)(160 * shade), fb = (byte)(175 * shade);
        byte[]? px = tex?.Rgba;
        int tw = tex is null ? 0 : (int)tex.Value.Width;
        int th = tex is null ? 0 : (int)tex.Value.Height;

        for (int y = minY; y <= maxY; y++)
        {
            float py = y + 0.5f;
            for (int x = minX; x <= maxX; x++)
            {
                float fx = x + 0.5f;
                float wa = ((b.X - fx) * (c.Y - py) - (b.Y - py) * (c.X - fx)) * inv;
                float wb = ((c.X - fx) * (a.Y - py) - (c.Y - py) * (a.X - fx)) * inv;
                float wc = 1 - wa - wb;
                if (wa < 0 || wb < 0 || wc < 0) continue;
                float z = wa * a.Z + wb * b.Z + wc * c.Z;
                int di = y * w + x;
                if (z >= depth[di]) continue;
                int o = di * 4;

                if (px is null)
                {
                    depth[di] = z;
                    rgba[o] = fr; rgba[o + 1] = fg; rgba[o + 2] = fb;
                    continue;
                }

                float u = wa * ua.U + wb * ub.U + wc * uc.U;
                float v = wa * ua.V + wb * ub.V + wc * uc.V;
                int tx = Math.Clamp((int)((u - MathF.Floor(u)) * tw), 0, tw - 1);
                int ty = Math.Clamp((int)((v - MathF.Floor(v)) * th), 0, th - 1);
                int si = (ty * tw + tx) * 4;
                if (cutout && px[si + 3] < 128) continue;
                depth[di] = z;
                rgba[o] = (byte)Math.Min(255f, px[si] * shade);
                rgba[o + 1] = (byte)Math.Min(255f, px[si + 1] * shade);
                rgba[o + 2] = (byte)Math.Min(255f, px[si + 2] * shade);
            }
        }
    }

    private static Vec3 Sub(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static Vec3 Cross(Vec3 a, Vec3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    private static Vec3 Norm(Vec3 a)
    {
        float l = MathF.Sqrt(a.X * a.X + a.Y * a.Y + a.Z * a.Z);
        return l < 1e-9f ? new Vec3(0, 0, 1) : new Vec3(a.X / l, a.Y / l, a.Z / l);
    }
}
