using System.Collections.Generic;
using Aelid.World;
using NUnit.Framework;
using UnityEngine;

namespace Aelid.Tests
{
    public class RegionGeneratorTests
    {
        /// <summary>A plane at <c>height</c>, tilted up towards +Z by <c>slope</c> degrees.</summary>
        sealed class PlaneTerrain : ITerrainSampler
        {
            readonly float m_Height, m_Rise;

            public PlaneTerrain(float height = 0f, float slope = 0f)
            {
                m_Height = height;
                m_Rise = Mathf.Tan(slope * Mathf.Deg2Rad);
            }

            public bool TrySample(Vector2 xz, out float height, out Vector3 normal)
            {
                height = m_Height + xz.y * m_Rise;
                normal = new Vector3(0f, 1f, -m_Rise).normalized;
                return true;
            }
        }

        readonly List<Object> m_Created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in m_Created)
                Object.DestroyImmediate(o);
            m_Created.Clear();
        }

        RegionRuleSet Rules(params RegionObjectRule[] rules)
        {
            var set = ScriptableObject.CreateInstance<RegionRuleSet>();
            set.rules = new List<RegionObjectRule>(rules);
            m_Created.Add(set);
            return set;
        }

        static RegionObjectRule Rule(string name, float density, float radius, string parent = "", float fromParent = 0f) => new()
        {
            name = name,
            density = density,
            radius = radius,
            parent = parent,
            radiusFromParent = fromParent,
            sizeVariance = 0f,
        };

        static RegionSpec Square(float half, RegionRuleSet rules, int seed = 1, float falloff = 0f, ITerrainSampler terrain = null,
            Vector2 centre = default, int priority = 0, bool overrideLower = false) => new()
        {
            outline = new[]
            {
                centre + new Vector2(-half, -half), centre + new Vector2(half, -half),
                centre + new Vector2(half, half), centre + new Vector2(-half, half),
            },
            rules = rules,
            seed = seed,
            edgeFalloff = falloff,
            terrain = terrain ?? new PlaneTerrain(),
            priority = priority,
            overrideLower = overrideLower,
        };

        static Vector2 XZ(RegionPlacement p) => new(p.position.x, p.position.z);

        [Test]
        public void SameInputsGiveTheSamePlacements()
        {
            var rules = Rules(Rule("Tree", 6f, 0.7f), Rule("Fern", 20f, 0.4f, "Tree", 2f));
            rules.rules[0].clustering = 40f;
            rules.rules[0].patchiness = 0.6f;

            var first = RegionGenerator.Generate(new[] { Square(25f, rules) });
            var second = RegionGenerator.Generate(new[] { Square(25f, rules) });

            Assert.Greater(first.Count, 20);
            Assert.AreEqual(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i].position, second[i].position);
                Assert.AreEqual(first[i].rotation, second[i].rotation);
                Assert.AreEqual(first[i].scale, second[i].scale);
            }

            var reseeded = RegionGenerator.Generate(new[] { Square(25f, rules, seed: 2) });
            Assert.AreNotEqual(first[0].position, reseeded[0].position, "A different region seed should give a different layout.");
        }

        [Test]
        public void CountFollowsRadiusAndDensity()
        {
            // 40 x 40 m in 2 m blocks = 400 blocks; 25% density is about 100 objects, minus overlap rejections.
            var placements = RegionGenerator.Generate(new[] { Square(20f, Rules(Rule("Rock", 25f, 1f))) });
            Assert.That(placements.Count, Is.InRange(60, 105));
        }

        [Test]
        public void SlopeAndHeightLimitsFilterPlacements()
        {
            var steep = new PlaneTerrain(slope: 30f);
            var flatOnly = Rule("Tree", 20f, 0.5f);
            flatOnly.maxSlope = 20f;
            Assert.AreEqual(0, RegionGenerator.Generate(new[] { Square(15f, Rules(flatOnly), terrain: steep) }).Count);

            var slopeLover = Rule("Slab", 20f, 0.5f);
            slopeLover.minSlope = 25f;
            slopeLover.maxSlope = 35f;
            Assert.Greater(RegionGenerator.Generate(new[] { Square(15f, Rules(slopeLover), terrain: steep) }).Count, 0);

            var raised = new PlaneTerrain(height: 2f);
            var high = Rule("Crag", 20f, 0.5f);
            high.minHeight = 3f;
            Assert.AreEqual(0, RegionGenerator.Generate(new[] { Square(15f, Rules(high), terrain: raised) }).Count);
            high.minHeight = 1f;
            Assert.Greater(RegionGenerator.Generate(new[] { Square(15f, Rules(high), terrain: raised) }).Count, 0);
        }

        [Test]
        public void ChildrenAppearInARingAroundTheirParent()
        {
            var rules = Rules(Rule("Tree", 10f, 1f), Rule("Mushroom", 30f, 0.3f, "Tree", 2f));
            var placements = RegionGenerator.Generate(new[] { Square(30f, rules) });

            int children = 0;
            foreach (var p in placements)
            {
                if (p.rule != 1)
                {
                    Assert.AreEqual(-1, p.parent);
                    continue;
                }
                children++;
                Assert.GreaterOrEqual(p.parent, 0);
                var parent = placements[p.parent];
                Assert.AreEqual(0, parent.rule);
                float distance = Vector2.Distance(XZ(p), XZ(parent));
                Assert.That(distance, Is.InRange(1f - 1e-3f, 3f + 1e-3f), "Child should sit in the ring outside the parent's radius.");
            }
            Assert.Greater(children, 10);
        }

        [Test]
        public void FootprintsNeverOverlapAndTreesKeepCrownSpacing()
        {
            var oak = Rule("Oak", 8f, 0.7f);
            oak.isTree = true;
            oak.clustering = 50f;
            var bigOak = Rule("Big Oak", 3f, 1f);
            bigOak.isTree = true;
            var bush = Rule("Bush", 10f, 0.6f);
            bush.clustering = 60f;
            var rules = Rules(oak, Rule("Fern", 25f, 0.4f, "Oak", 2f), bigOak, bush);
            rules.treeSpacing = 3f;

            var placements = RegionGenerator.Generate(new[] { Square(30f, rules) });
            Assert.Greater(placements.Count, 50);

            for (int i = 0; i < placements.Count; i++)
                for (int j = i + 1; j < placements.Count; j++)
                {
                    var a = rules.rules[placements[i].rule];
                    var b = rules.rules[placements[j].rule];
                    float need = a.isTree && b.isTree ? (a.radius + b.radius) * rules.treeSpacing
                        : placements[j].parent == i ? a.radius
                        : a.radius + b.radius;
                    float distance = Vector2.Distance(XZ(placements[i]), XZ(placements[j]));
                    Assert.GreaterOrEqual(distance, need - 1e-3f, $"{a.name} #{i} and {b.name} #{j} overlap.");
                }
        }

        [Test]
        public void EdgeFalloffThinsTheBorder()
        {
            var placements = RegionGenerator.Generate(new[] { Square(30f, Rules(Rule("Grass", 30f, 0.5f)), falloff: 10f) });

            int border = 0, inner = 0;
            foreach (var p in placements)
            {
                float toEdge = 30f - Mathf.Max(Mathf.Abs(p.position.x), Mathf.Abs(p.position.z));
                if (toEdge < 2.5f) border++;
                else if (toEdge > 10f) inner++;
            }
            float borderDensity = border / (60f * 60f - 55f * 55f);
            float innerDensity = inner / (40f * 40f);
            Assert.Greater(innerDensity, 0f);
            Assert.Less(borderDensity, innerDensity * 0.4f);
        }

        [Test]
        public void KeepClearZonesStayEmpty()
        {
            var zones = new[]
            {
                ExclusionShape.Circle(Vector2.zero, 8f),
                ExclusionShape.Path(new[] { new Vector2(-20f, 15f), new Vector2(20f, 15f) }, 2f),
            };
            var placements = RegionGenerator.Generate(new[] { Square(20f, Rules(Rule("Bush", 30f, 0.5f))) }, zones);

            Assert.Greater(placements.Count, 50);
            foreach (var p in placements)
            {
                Assert.GreaterOrEqual(XZ(p).magnitude, 8.5f - 1e-3f);
                Assert.GreaterOrEqual(Mathf.Abs(p.position.z - 15f), 2.5f - 1e-3f);
            }
        }

        [Test]
        public void OverrideRegionReplacesLowerPriorityContent()
        {
            var meadow = Square(20f, Rules(Rule("Grass", 25f, 0.5f)));
            var grove = Square(8f, Rules(Rule("Fern", 25f, 0.5f)), seed: 9, priority: 5, overrideLower: true);
            var placements = RegionGenerator.Generate(new[] { meadow, grove });

            int ferns = 0;
            foreach (var p in placements)
            {
                bool insideGrove = Mathf.Abs(p.position.x) < 8f && Mathf.Abs(p.position.z) < 8f;
                if (p.region == 0)
                    Assert.IsFalse(insideGrove, "The overriding region should replace the meadow inside it.");
                else
                    ferns++;
            }
            Assert.Greater(ferns, 10);
        }

        [Test]
        public void ClusteringPullsObjectsTogether()
        {
            float MeanNearest(float clustering)
            {
                var rule = Rule("Bush", 8f, 0.5f);
                rule.clustering = clustering;
                var placements = RegionGenerator.Generate(new[] { Square(30f, Rules(rule)) });
                float sum = 0f;
                foreach (var a in placements)
                {
                    float best = float.MaxValue;
                    foreach (var b in placements)
                        if (XZ(a) != XZ(b))
                            best = Mathf.Min(best, Vector2.Distance(XZ(a), XZ(b)));
                    sum += best;
                }
                return sum / placements.Count;
            }

            Assert.Less(MeanNearest(100f), MeanNearest(0f) * 0.8f);
        }
    }
}
