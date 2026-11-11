using System.Collections.Generic;
using Aelid.EditorTools;
using NUnit.Framework;
using UnityEngine;

namespace Aelid.Tests
{
    public class RuinFloorTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void Tiles_AreClosedSolidMeshes_WithWalkableHeights(int style)
        {
            var builder = new LowPolyMesh();
            RuinFloorTiles.AddTo(builder, Vector3.zero, 37f, style);
            Mesh mesh = builder.ToMesh("Floor Test");
            try
            {
                var vertices = mesh.vertices;
                var indices = mesh.triangles;
                var edges = new Dictionary<(Vector3, Vector3), int>();
                void Edge(Vector3 a, Vector3 b)
                {
                    var key = (a, b);
                    edges.TryGetValue(key, out int count);
                    edges[key] = count + 1;
                }

                float volume = 0f;
                for (int i = 0; i < indices.Length; i += 3)
                {
                    Vector3 a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]];
                    Assert.Greater(Vector3.Cross(b - a, c - a).sqrMagnitude, 1e-10f, "Degenerate triangle.");
                    volume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6f;
                    Edge(a, b); Edge(b, c); Edge(c, a);
                }
                foreach (var edge in edges)
                {
                    Assert.AreEqual(1, edge.Value, "Overlapping faces.");
                    Assert.IsTrue(edges.ContainsKey((edge.Key.Item2, edge.Key.Item1)), "Open or inverted edge.");
                }
                Assert.Greater(volume, 0.01f, "Faces must point out of a solid stone.");
                Assert.Less(mesh.bounds.max.y, 0.3f, "Raised edges must stay below the player step height.");
                Assert.Greater(mesh.bounds.max.y, 0f, "Sunken slabs must retain a visible edge.");
                Assert.Greater(mesh.bounds.min.y, -0.3f);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void TileVariation_IsRepeatable_AndDoesNotConsumeUnityRandom()
        {
            Random.State previous = Random.state;
            Mesh first = null, second = null;
            try
            {
                var a = new LowPolyMesh();
                var b = new LowPolyMesh();
                Random.InitState(817);
                Random.State initial = Random.state;
                for (int i = 0; i < 14; i++)
                {
                    Vector3 ground = new(i * 2f, 4f, 0f);
                    RuinFloorTiles.AddTo(a, ground, i * 17f, i);
                    RuinFloorTiles.AddTo(b, ground, i * 17f, i);
                }
                Assert.AreEqual(initial, Random.state);
                first = a.ToMesh("First");
                second = b.ToMesh("Second");
                CollectionAssert.AreEqual(first.vertices, second.vertices);
                CollectionAssert.AreEqual(first.normals, second.normals);
                CollectionAssert.AreEqual(first.triangles, second.triangles);
            }
            finally
            {
                if (first != null) Object.DestroyImmediate(first);
                if (second != null) Object.DestroyImmediate(second);
                Random.state = previous;
            }
        }
    }
}
