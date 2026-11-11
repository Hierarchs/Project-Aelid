using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aelid.EditorTools
{
    /// <summary>
    /// Flat-shaded mesh builder (every triangle owns its vertices, so normals
    /// are per-face: the low-poly look, and crisp normal edges for the ASCII pass).
    /// Front faces wind clockwise as seen from outside.
    /// </summary>
    public sealed class LowPolyMesh
    {
        readonly List<Vector3> m_Vertices = new();
        readonly List<Vector3> m_Normals = new();
        readonly List<int> m_Indices = new();

        public int TriangleCount => m_Indices.Count / 3;

        public void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f)
                return;
            n.Normalize();
            int i = m_Vertices.Count;
            m_Vertices.Add(a); m_Vertices.Add(b); m_Vertices.Add(c);
            m_Normals.Add(n); m_Normals.Add(n); m_Normals.Add(n);
            m_Indices.Add(i); m_Indices.Add(i + 1); m_Indices.Add(i + 2);
        }

        /// <summary>Quad a-b-c-d, clockwise from the visible side.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Tri(a, b, c);
            Tri(a, c, d);
        }

        public void Box(Vector3 center, Vector3 size, Quaternion rotation)
        {
            Vector3 h = size * 0.5f;
            Vector3 P(float x, float y, float z) => center + rotation * new Vector3(x * h.x, y * h.y, z * h.z);
            Vector3 p000 = P(-1, -1, -1), p100 = P(1, -1, -1), p010 = P(-1, 1, -1), p110 = P(1, 1, -1);
            Vector3 p001 = P(-1, -1, 1), p101 = P(1, -1, 1), p011 = P(-1, 1, 1), p111 = P(1, 1, 1);
            Quad(p000, p010, p110, p100); // -Z
            Quad(p101, p111, p011, p001); // +Z
            Quad(p001, p011, p010, p000); // -X
            Quad(p100, p110, p111, p101); // +X
            Quad(p010, p011, p111, p110); // +Y
            Quad(p001, p000, p100, p101); // -Y
        }

        public void Box(Vector3 center, Vector3 size) => Box(center, size, Quaternion.identity);

        /// <summary>n-sided prism standing on <paramref name="baseCenter"/>.</summary>
        public void Cylinder(Vector3 baseCenter, float radiusBottom, float radiusTop, float height, int sides, float twistDegrees = 0f, bool caps = true)
        {
            var bottom = new Vector3[sides];
            var top = new Vector3[sides];
            float twist = twistDegrees * Mathf.Deg2Rad;
            for (int i = 0; i < sides; i++)
            {
                float a = twist + i * Mathf.PI * 2f / sides;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                bottom[i] = baseCenter + dir * radiusBottom;
                top[i] = baseCenter + Vector3.up * height + dir * radiusTop;
            }
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                Quad(bottom[i], top[i], top[j], bottom[j]);
            }
            if (!caps) return;
            Vector3 topCenter = baseCenter + Vector3.up * height;
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                if (radiusTop > 1e-4f) Tri(topCenter, top[j], top[i]);
                Tri(baseCenter, bottom[i], bottom[j]);
            }
        }

        public void Cone(Vector3 baseCenter, float radius, float height, int sides, float twistDegrees = 0f)
        {
            Vector3 apex = baseCenter + Vector3.up * height;
            float twist = twistDegrees * Mathf.Deg2Rad;
            for (int i = 0; i < sides; i++)
            {
                float a0 = twist + i * Mathf.PI * 2f / sides;
                float a1 = twist + (i + 1) * Mathf.PI * 2f / sides;
                Vector3 p0 = baseCenter + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
                Vector3 p1 = baseCenter + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
                Tri(p0, apex, p1);
                Tri(baseCenter, p0, p1);
            }
        }

        /// <summary>Gable roof over a w x d footprint at height y0, ridge along local Z.</summary>
        public void GableRoof(Vector3 center, float width, float depth, float rise, float overhang, Quaternion rotation)
        {
            float hw = width * 0.5f + overhang;
            float hd = depth * 0.5f + overhang;
            Vector3 P(float x, float y, float z) => center + rotation * new Vector3(x, y, z);
            Vector3 l0 = P(-hw, 0, -hd), l1 = P(-hw, 0, hd), r0 = P(hw, 0, -hd), r1 = P(hw, 0, hd);
            Vector3 t0 = P(0, rise, -hd), t1 = P(0, rise, hd);
            Quad(l0, l1, t1, t0); // left slope
            Quad(r1, r0, t0, t1); // right slope
            Tri(l0, t0, r0);      // front gable
            Tri(r1, t1, l1);      // back gable
            Quad(l1, l0, r0, r1); // underside
        }

        /// <summary>Jittered icosphere blob (tree crowns, bushes, rocks).</summary>
        public void Blob(Vector3 center, Vector3 radii, int subdivisions, float jitter, int seed)
        {
            var rng = new System.Random(seed);
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var verts = new List<Vector3>
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
                new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
                new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
            };
            var faces = new List<(int, int, int)>
            {
                (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11),
                (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
                (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9),
                (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1),
            };
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;

            for (int s = 0; s < subdivisions; s++)
            {
                var cache = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (cache.TryGetValue(key, out int idx)) return idx;
                    verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
                    cache[key] = verts.Count - 1;
                    return verts.Count - 1;
                }
                var next = new List<(int, int, int)>();
                foreach (var (a, b, c) in faces)
                {
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    next.Add((a, ab, ca)); next.Add((b, bc, ab)); next.Add((c, ca, bc)); next.Add((ab, bc, ca));
                }
                faces = next;
            }

            var shaped = new Vector3[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                float k = 1f + ((float)rng.NextDouble() * 2f - 1f) * jitter;
                Vector3 v = verts[i] * k;
                shaped[i] = center + new Vector3(v.x * radii.x, v.y * radii.y, v.z * radii.z);
            }
            // This face order already winds clockwise from outside in Unity's left-handed space.
            foreach (var (a, b, c) in faces)
                Tri(shaped[a], shaped[b], shaped[c]);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.indexFormat = m_Vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(m_Vertices);
            mesh.SetNormals(m_Normals);
            mesh.SetTriangles(m_Indices, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
