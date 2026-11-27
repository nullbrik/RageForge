/* RAGEFORGE - Mesh Normals */

using System.Numerics;

namespace RageForge.Core.Model;

public static class MeshNormals
{
    public static Vector3[] Compute(DecodedMesh m)
    {
        int vc = m.Positions.Count;
        var pos = m.Positions;
        var idx = m.Indices;
        var faces = new Vector3[vc];
        for (int t = 0; t + 2 < idx.Count; t += 3)
        {
            int a = (int)idx[t], b = (int)idx[t + 1], c = (int)idx[t + 2];
            if ((uint)a >= vc || (uint)b >= vc || (uint)c >= vc) continue;
            var pa = new Vector3(pos[a].X, pos[a].Y, pos[a].Z);
            var f = Vector3.Cross(new Vector3(pos[b].X, pos[b].Y, pos[b].Z) - pa, new Vector3(pos[c].X, pos[c].Y, pos[c].Z) - pa);
            faces[a] += f;
            faces[b] += f;
            faces[c] += f;
        }

        if (m.Normals is { } given && given.Count == vc)
        {
            int agree = 0, total = 0;
            for (int i = 0; i < vc && total < 4000; i += Math.Max(1, vc / 4000))
            {
                if (faces[i].LengthSquared() < 1e-20f) continue;
                total++;
                if (Vector3.Dot(faces[i], new Vector3(given[i].X, given[i].Y, given[i].Z)) > 0) agree++;
            }
            if (total > 0 && agree * 10 >= total * 7)
            {
                var result = new Vector3[vc];
                for (int i = 0; i < vc; i++)
                {
                    var n = new Vector3(given[i].X, given[i].Y, given[i].Z);
                    result[i] = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : SafeNormalize(faces[i]);
                }
                return result;
            }
        }

        for (int i = 0; i < vc; i++)
            faces[i] = SafeNormalize(faces[i]);
        return faces;
    }

    private static Vector3 SafeNormalize(Vector3 v) => v.LengthSquared() > 1e-20f ? Vector3.Normalize(v) : Vector3.UnitZ;
}
