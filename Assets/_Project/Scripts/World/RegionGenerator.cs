using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aelid.World
{
    /// <summary>Answers "how high is the ground here, and which way does it face?"</summary>
    public interface ITerrainSampler
    {
        bool TrySample(Vector2 xz, out float height, out Vector3 normal);
    }

    /// <summary>Samples ground by casting straight down onto one collider.</summary>
    public sealed class ColliderTerrainSampler : ITerrainSampler
    {
        readonly Collider m_Collider;

        public ColliderTerrainSampler(Collider collider) =>
            m_Collider = collider != null ? collider : throw new ArgumentNullException(nameof(collider));

        public bool TrySample(Vector2 xz, out float height, out Vector3 normal)
        {
            Bounds bounds = m_Collider.bounds;
            var ray = new Ray(new Vector3(xz.x, bounds.max.y + 1f, xz.y), Vector3.down);
            if (m_Collider.Raycast(ray, out RaycastHit hit, bounds.size.y + 2f))
            {
                height = hit.point.y;
                normal = hit.normal;
                return true;
            }
            height = 0f;
            normal = Vector3.up;
            return false;
        }
    }

    /// <summary>Plain-data input for one region, independent of scene objects.</summary>
    public sealed class RegionSpec
    {
        public string name = "Region";
        public Vector2[] outline = Array.Empty<Vector2>();
        public float edgeFalloff;
        public int priority;
        public bool overrideLower;
        public int seed;
        public RegionRuleSet rules;
        public ITerrainSampler terrain;

        Rect? m_Bounds;

        public Rect Bounds => m_Bounds ??= RegionGeometry.Bounds(outline);

        public bool Contains(Vector2 p)
        {
            Rect b = Bounds;
            return p.x >= b.xMin && p.x <= b.xMax && p.y >= b.yMin && p.y <= b.yMax && RegionGeometry.Contains(outline, p);
        }

        /// <summary>0 at the outline, rising linearly to 1 at <see cref="edgeFalloff"/> metres inside.</summary>
        public float EdgeWeight(Vector2 p) =>
            edgeFalloff <= 0f ? 1f : Mathf.Clamp01(RegionGeometry.DistanceToOutline(outline, p, closed: true) / edgeFalloff);
    }

    /// <summary>Why a candidate was dropped, in the order the checks run.</summary>
    public enum RegionRejection { Outline, EdgeFalloff, Override, KeepClear, NoGround, Slope, Height, Overlap }

    /// <summary>Per-rule candidate and rejection counts, for tuning ("why does nothing grow here?").</summary>
    public sealed class RegionGenerationStats
    {
        public static readonly int ReasonCount = Enum.GetValues(typeof(RegionRejection)).Length;

        public readonly Dictionary<(int region, int rule), int> candidates = new();
        public readonly Dictionary<(int region, int rule), int[]> rejected = new();

        internal void Candidate(int region, int rule) =>
            candidates[(region, rule)] = candidates.TryGetValue((region, rule), out int n) ? n + 1 : 1;

        internal void Reject(int region, int rule, RegionRejection reason)
        {
            if (!rejected.TryGetValue((region, rule), out var counts))
                rejected.Add((region, rule), counts = new int[ReasonCount]);
            counts[(int)reason]++;
        }
    }

    /// <summary>One generated object: which rule and prefab variant, and where.</summary>
    public struct RegionPlacement
    {
        public int region;
        public int rule;
        public int variant;
        /// <summary>Index of the parent placement in the result list, or -1.</summary>
        public int parent;
        public Vector3 position;
        public Quaternion rotation;
        public float scale;
    }

    /// <summary>
    /// Oblivion-style region generation. Pure and deterministic: the same specs
    /// and exclusions always give the same placements, so a server can re-run
    /// it, and results can be baked into a scene.
    ///
    /// Top-level rules split each region's bounding box into a world-anchored
    /// grid of 2 x radius blocks; each block rolls its rule's density for one
    /// candidate at a random point inside it. Child rules split the ring around
    /// each placed parent the same way. Candidates then pass, in order:
    /// clustering (shift next to the nearest of their kind), the region outline
    /// and edge fall-off, higher-priority override regions, keep-clear zones,
    /// slope and height limits, and finally footprint overlap with everything
    /// placed so far. Regions run by priority (highest first), rules top to
    /// bottom, so earlier rules claim ground first.
    /// </summary>
    public static class RegionGenerator
    {
        public const float MinRadius = 0.05f;
        public const long MaxBlocksPerRule = 4_000_000;

        public static List<RegionPlacement> Generate(IReadOnlyList<RegionSpec> regions, IReadOnlyList<ExclusionShape> exclusions = null,
            RegionGenerationStats stats = null)
        {
            var run = new Run(regions, exclusions ?? Array.Empty<ExclusionShape>(), stats);
            run.Execute();
            return run.placements;
        }

        sealed class Run
        {
            const float CellSize = 2f;

            public readonly List<RegionPlacement> placements = new();

            readonly IReadOnlyList<RegionSpec> m_Regions;
            readonly IReadOnlyList<ExclusionShape> m_Exclusions;
            readonly RegionGenerationStats m_Stats;

            // Occupancy, indexed like placements.
            readonly Dictionary<long, List<int>> m_Grid = new();
            readonly List<Vector2> m_Xz = new();
            readonly List<float> m_Radius = new();
            readonly List<float> m_TreeSpacing = new(); // 0 = not a tree
            readonly List<int> m_Kind = new();
            float m_MaxRadius;
            float m_MaxTreeRadius;
            float m_MaxTreeSpacing = 1f;

            int[] m_RuleKeys;
            List<int>[] m_PlacedByRule;

            public Run(IReadOnlyList<RegionSpec> regions, IReadOnlyList<ExclusionShape> exclusions, RegionGenerationStats stats)
            {
                m_Regions = regions;
                m_Exclusions = exclusions;
                m_Stats = stats;
            }

            public void Execute()
            {
                var order = new List<int>();
                for (int i = 0; i < m_Regions.Count; i++)
                    order.Add(i);
                order.Sort((a, b) =>
                {
                    int byPriority = m_Regions[b].priority.CompareTo(m_Regions[a].priority);
                    return byPriority != 0 ? byPriority : a.CompareTo(b);
                });
                foreach (int region in order)
                    RunRegion(region);
            }

            void RunRegion(int regionIndex)
            {
                var region = m_Regions[regionIndex];
                if (region == null || region.rules == null || region.terrain == null || region.outline == null || region.outline.Length < 3)
                    return;

                var rules = region.rules.rules;
                m_RuleKeys = new int[rules.Count];
                m_PlacedByRule = new List<int>[rules.Count];
                var children = new List<int>[rules.Count];
                var topLevel = new List<int>();
                var seen = new Dictionary<string, int>();

                for (int i = 0; i < rules.Count; i++)
                {
                    var rule = rules[i];
                    if (rule == null)
                        continue;
                    // Keyed by name, so reordering rules keeps each rule's candidate points.
                    seen.TryGetValue(rule.name ?? "", out int duplicates);
                    seen[rule.name ?? ""] = duplicates + 1;
                    m_RuleKeys[i] = RegionRandom.Hash(rule.name) ^ (duplicates * 0x2545F491);

                    int parent = string.IsNullOrEmpty(rule.parent) ? -1 : region.rules.IndexOf(rule.parent);
                    if (parent < 0 || parent == i)
                        topLevel.Add(i);
                    else
                        (children[parent] ??= new List<int>()).Add(i);
                }

                foreach (int rule in topLevel)
                {
                    RunTopLevel(regionIndex, rule);
                    RunChildren(regionIndex, rule, children, 0);
                }
            }

            void RunChildren(int regionIndex, int parentRule, List<int>[] children, int depth)
            {
                if (children[parentRule] == null || depth > 16)
                    return;
                foreach (int child in children[parentRule])
                {
                    RunChild(regionIndex, child, parentRule);
                    RunChildren(regionIndex, child, children, depth + 1);
                }
            }

            void RunTopLevel(int regionIndex, int ruleIndex)
            {
                var region = m_Regions[regionIndex];
                var rule = region.rules.rules[ruleIndex];
                if (rule.density <= 0f)
                    return;

                float block = 2f * Mathf.Max(rule.radius, MinRadius);
                Rect bounds = region.Bounds;
                int x0 = Mathf.FloorToInt(bounds.xMin / block), x1 = Mathf.FloorToInt(bounds.xMax / block);
                int z0 = Mathf.FloorToInt(bounds.yMin / block), z1 = Mathf.FloorToInt(bounds.yMax / block);
                long blocks = (long)(x1 - x0 + 1) * (z1 - z0 + 1);
                if (blocks > MaxBlocksPerRule)
                {
                    Debug.LogWarning($"[Aelid] Region '{region.name}' rule '{rule.name}': radius {rule.radius} m gives {blocks} blocks; skipped.");
                    return;
                }

                int key = m_RuleKeys[ruleIndex];
                float chance = rule.density / 100f;
                var candidates = new List<(ulong order, Vector2 point, ulong seed)>();
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var rng = RegionRandom.For(region.seed, key, x, z);
                        var p = new Vector2((x + rng.Next()) * block, (z + rng.Next()) * block);
                        if (rng.Next() >= chance * Patch(rule, region.seed, key, p))
                            continue;
                        candidates.Add((rng.NextULong(), p, rng.NextULong()));
                    }

                // Hash order instead of row order, so clustering has no directional bias.
                candidates.Sort((a, b) => a.order.CompareTo(b.order));
                foreach (var candidate in candidates)
                {
                    var rng = new RegionRandom(candidate.seed);
                    TryPlace(regionIndex, ruleIndex, candidate.point, ref rng, -1);
                }
            }

            void RunChild(int regionIndex, int ruleIndex, int parentRule)
            {
                var parents = m_PlacedByRule[parentRule];
                var region = m_Regions[regionIndex];
                var rule = region.rules.rules[ruleIndex];
                if (parents == null || rule.density <= 0f || rule.radiusFromParent <= 0f)
                    return;

                float block = 2f * Mathf.Max(rule.radius, MinRadius);
                float inner = Mathf.Max(region.rules.rules[parentRule].radius, MinRadius);
                float outer = inner + rule.radiusFromParent;
                float slots = Mathf.PI * (outer * outer - inner * inner) / (block * block);
                int slotCount = Mathf.CeilToInt(slots);
                float chance = rule.density / 100f;
                int key = m_RuleKeys[ruleIndex];

                foreach (int parentIndex in parents)
                {
                    Vector3 parent = placements[parentIndex].position;
                    int px = Mathf.RoundToInt(parent.x * 100f), pz = Mathf.RoundToInt(parent.z * 100f);
                    for (int k = 0; k < slotCount; k++)
                    {
                        var rng = RegionRandom.For(region.seed, key, px, pz, k);
                        float slotChance = k < slotCount - 1 ? 1f : slots - (slotCount - 1);
                        float angle = rng.Next() * Mathf.PI * 2f;
                        float distance = Mathf.Sqrt(Mathf.Lerp(inner * inner, outer * outer, rng.Next()));
                        var p = new Vector2(parent.x + Mathf.Cos(angle) * distance, parent.z + Mathf.Sin(angle) * distance);
                        if (rng.Next() >= slotChance * chance * Patch(rule, region.seed, key, p))
                            continue;
                        TryPlace(regionIndex, ruleIndex, p, ref rng, parentIndex);
                    }
                }
            }

            bool TryPlace(int regionIndex, int ruleIndex, Vector2 p, ref RegionRandom rng, int parent)
            {
                var region = m_Regions[regionIndex];
                var rule = region.rules.rules[ruleIndex];
                float radius = Mathf.Max(rule.radius, MinRadius);
                float spacing = rule.isTree ? Mathf.Max(1f, region.rules.treeSpacing) : 0f;
                int kind = regionIndex * 65536 + ruleIndex;

                if (rule.clustering > 0f && rng.Next() * 100f < rule.clustering)
                {
                    float gap = 2f * radius * Mathf.Max(spacing, 1f);
                    int nearest = Nearest(p, kind, 4f * gap);
                    if (nearest >= 0)
                    {
                        Vector2 q = m_Xz[nearest];
                        Vector2 away = p - q;
                        if (away.sqrMagnitude < 1e-6f)
                        {
                            float a = rng.Next() * Mathf.PI * 2f;
                            away = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        }
                        p = q + away.normalized * gap * rng.Range(1.02f, 1.3f);
                    }
                }

                m_Stats?.Candidate(regionIndex, ruleIndex);
                bool Reject(RegionRejection reason)
                {
                    m_Stats?.Reject(regionIndex, ruleIndex, reason);
                    return false;
                }

                if (!region.Contains(p))
                    return Reject(RegionRejection.Outline);
                float edge = region.EdgeWeight(p);
                if (edge < 1f && rng.Next() >= edge)
                    return Reject(RegionRejection.EdgeFalloff);
                for (int i = 0; i < m_Regions.Count; i++)
                {
                    var other = m_Regions[i];
                    if (i == regionIndex || other?.rules == null || !other.overrideLower || other.priority <= region.priority)
                        continue;
                    if (other.outline != null && other.outline.Length >= 3 && other.Contains(p) && rng.Next() < other.EdgeWeight(p))
                        return Reject(RegionRejection.Override);
                }
                foreach (var zone in m_Exclusions)
                    if (zone.Distance(p) < radius)
                        return Reject(RegionRejection.KeepClear);
                if (!region.terrain.TrySample(p, out float height, out Vector3 normal))
                    return Reject(RegionRejection.NoGround);
                float slope = Vector3.Angle(normal, Vector3.up);
                if (slope < rule.minSlope || slope > rule.maxSlope)
                    return Reject(RegionRejection.Slope);
                if (height < rule.minHeight || height > rule.maxHeight)
                    return Reject(RegionRejection.Height);
                if (!Fits(p, radius, spacing, parent))
                    return Reject(RegionRejection.Overlap);

                Quaternion rotation = Quaternion.Euler(0f, rng.Signed(rule.yawVariance), 0f);
                if (rule.tiltVariance > 0f)
                {
                    Vector3 axis = Quaternion.Euler(0f, rng.Next() * 360f, 0f) * Vector3.right;
                    rotation = Quaternion.AngleAxis(rng.Next() * rule.tiltVariance, axis) * rotation;
                }
                if (rule.conformToSlope)
                    rotation = Quaternion.FromToRotation(Vector3.up, normal) * rotation;
                float scale = Mathf.Max(0.01f, rule.scale * (1f + rng.Signed(rule.sizeVariance)));
                float sink = rule.sink + rng.Signed(rule.sinkVariance);
                int variant = rng.Index(rule.variants?.Length ?? 0);

                int index = placements.Count;
                placements.Add(new RegionPlacement
                {
                    region = regionIndex,
                    rule = ruleIndex,
                    variant = variant,
                    parent = parent,
                    position = new Vector3(p.x, height - sink, p.y),
                    rotation = rotation,
                    scale = scale,
                });
                Register(index, p, radius, spacing, kind);
                (m_PlacedByRule[ruleIndex] ??= new List<int>()).Add(index);
                return true;
            }

            bool Fits(Vector2 p, float radius, float spacing, int parent)
            {
                float reach = radius + m_MaxRadius;
                if (spacing > 0f)
                    reach = Mathf.Max(reach, (radius + m_MaxTreeRadius) * Mathf.Max(spacing, m_MaxTreeSpacing));

                int cx0 = Cell(p.x - reach), cx1 = Cell(p.x + reach);
                int cz0 = Cell(p.y - reach), cz1 = Cell(p.y + reach);
                for (int cz = cz0; cz <= cz1; cz++)
                    for (int cx = cx0; cx <= cx1; cx++)
                    {
                        if (!m_Grid.TryGetValue(Key(cx, cz), out var cell))
                            continue;
                        foreach (int i in cell)
                        {
                            float need;
                            if (spacing > 0f && m_TreeSpacing[i] > 0f)
                                need = (radius + m_Radius[i]) * Mathf.Max(spacing, m_TreeSpacing[i]);
                            else if (i == parent)
                                need = m_Radius[i]; // children sit just outside their parent's footprint
                            else
                                need = radius + m_Radius[i];
                            if ((m_Xz[i] - p).sqrMagnitude < need * need)
                                return false;
                        }
                    }
                return true;
            }

            int Nearest(Vector2 p, int kind, float reach)
            {
                int best = -1;
                float bestSq = reach * reach;
                int cx0 = Cell(p.x - reach), cx1 = Cell(p.x + reach);
                int cz0 = Cell(p.y - reach), cz1 = Cell(p.y + reach);
                for (int cz = cz0; cz <= cz1; cz++)
                    for (int cx = cx0; cx <= cx1; cx++)
                    {
                        if (!m_Grid.TryGetValue(Key(cx, cz), out var cell))
                            continue;
                        foreach (int i in cell)
                        {
                            if (m_Kind[i] != kind)
                                continue;
                            float sq = (m_Xz[i] - p).sqrMagnitude;
                            if (sq < bestSq)
                            {
                                bestSq = sq;
                                best = i;
                            }
                        }
                    }
                return best;
            }

            void Register(int index, Vector2 p, float radius, float spacing, int kind)
            {
                m_Xz.Add(p);
                m_Radius.Add(radius);
                m_TreeSpacing.Add(spacing);
                m_Kind.Add(kind);
                m_MaxRadius = Mathf.Max(m_MaxRadius, radius);
                if (spacing > 0f)
                {
                    m_MaxTreeRadius = Mathf.Max(m_MaxTreeRadius, radius);
                    m_MaxTreeSpacing = Mathf.Max(m_MaxTreeSpacing, spacing);
                }

                long key = Key(Cell(p.x), Cell(p.y));
                if (!m_Grid.TryGetValue(key, out var cell))
                    m_Grid.Add(key, cell = new List<int>());
                cell.Add(index);
            }

            static int Cell(float v) => Mathf.FloorToInt(v / CellSize);

            static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

            /// <summary>Broad noise that thins and thickens a rule into patches. Averages about 1.</summary>
            static float Patch(RegionObjectRule rule, int seed, int key, Vector2 p)
            {
                if (rule.patchiness <= 0f)
                    return 1f;
                ulong h = RegionRandom.Hash(seed, key, 7, 11);
                float ox = (h & 0xFFFF) * 0.0031f, oz = ((h >> 16) & 0xFFFF) * 0.0031f;
                float size = Mathf.Max(rule.patchSize, 1f);
                float n = Mathf.PerlinNoise(p.x / size + ox, p.y / size + oz);
                float mask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.7f, n));
                return Mathf.Max(0f, 1f + rule.patchiness * (2f * mask - 1f));
            }
        }
    }
}
