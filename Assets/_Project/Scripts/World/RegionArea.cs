using System.Collections.Generic;
using UnityEngine;

namespace Aelid.World
{
    /// <summary>
    /// A region drawn on the world, like a region shape in Oblivion's Region
    /// Editor: an outline, a rule set saying what grows inside it, and how it
    /// blends with its neighbours. Generated objects live under the child named
    /// <see cref="GeneratedRootName"/>; anything else in the scene counts as
    /// hand-placed and is never touched by Generate or Clear.
    /// </summary>
    public sealed class RegionArea : MonoBehaviour
    {
        public const string GeneratedRootName = "Generated";

        public RegionRuleSet rules;

        [Tooltip("Ground to sample for height and slope.")]
        public Collider terrain;

        [Tooltip("Outline corners in metres, relative to this object (X, Z).")]
        public List<Vector2> outline = new();

        [Tooltip("Metres inside the outline over which density fades in from zero. Lets regions blend instead of meeting at a hard line.")]
        [Min(0f)] public float edgeFalloff = 8f;

        [Tooltip("Higher-priority regions generate first and claim ground first.")]
        public int priority;

        [Tooltip("Where this region overlaps a lower-priority region, the lower region's objects are suppressed " +
                 "(fading with this region's edge fall-off).")]
        public bool overrideLower;

        public int seed = 1;

        public Color mapColor = new(0.35f, 0.8f, 0.45f, 1f);

        public Transform GeneratedRoot => transform.Find(GeneratedRootName);

        public Vector2[] WorldOutline()
        {
            var origin = new Vector2(transform.position.x, transform.position.z);
            var world = new Vector2[outline.Count];
            for (int i = 0; i < world.Length; i++)
                world[i] = origin + outline[i];
            return world;
        }

        public RegionSpec ToSpec() => new()
        {
            name = name,
            outline = WorldOutline(),
            edgeFalloff = edgeFalloff,
            priority = priority,
            overrideLower = overrideLower,
            seed = seed,
            rules = rules,
            terrain = terrain != null ? new ColliderTerrainSampler(terrain) : null,
        };

        void OnDrawGizmos() => DrawOutline(0.35f);

        void OnDrawGizmosSelected() => DrawOutline(1f);

        void DrawOutline(float alpha)
        {
            if (outline.Count < 2)
                return;
            Gizmos.color = new Color(mapColor.r, mapColor.g, mapColor.b, alpha);
            Vector3 origin = transform.position;
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 a = outline[i], b = outline[(i + 1) % outline.Count];
                Gizmos.DrawLine(origin + new Vector3(a.x, 0f, a.y), origin + new Vector3(b.x, 0f, b.y));
            }
        }
    }
}
