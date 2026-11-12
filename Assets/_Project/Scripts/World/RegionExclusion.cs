using System.Collections.Generic;
using UnityEngine;

namespace Aelid.World
{
    /// <summary>
    /// A keep-clear zone for region generation: villages, roads, ponds and
    /// hand-placed landmarks. No generated object's footprint enters it.
    /// </summary>
    public sealed class RegionExclusion : MonoBehaviour
    {
        public enum Shape { Circle, Path }

        public Shape shape = Shape.Circle;

        [Tooltip("Circle radius, or the path's half width, in metres.")]
        [Min(0f)] public float radius = 2f;

        [Tooltip("Path corners in metres, relative to this object (X, Z).")]
        public List<Vector2> points = new();

        public ExclusionShape ToShape()
        {
            var origin = new Vector2(transform.position.x, transform.position.z);
            if (shape == Shape.Circle || points.Count == 0)
                return ExclusionShape.Circle(origin, radius);

            var world = new Vector2[points.Count];
            for (int i = 0; i < world.Length; i++)
                world[i] = origin + points[i];
            return ExclusionShape.Path(world, radius);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.45f, 0.2f, 0.9f);
            float y = transform.position.y + 0.2f;
            var shapePoints = shape == Shape.Circle || points.Count == 0
                ? new List<Vector2> { Vector2.zero }
                : points;
            Vector3 origin = transform.position;
            for (int i = 0; i < shapePoints.Count; i++)
            {
                var c = new Vector3(origin.x + shapePoints[i].x, y, origin.z + shapePoints[i].y);
                DrawCircle(c, radius);
                if (i + 1 < shapePoints.Count)
                    Gizmos.DrawLine(c, new Vector3(origin.x + shapePoints[i + 1].x, y, origin.z + shapePoints[i + 1].y));
            }
        }

        static void DrawCircle(Vector3 centre, float r)
        {
            const int segments = 32;
            Vector3 prev = centre + new Vector3(r, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                Vector3 next = centre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }

    /// <summary>A keep-clear circle or capsule path in world XZ.</summary>
    public readonly struct ExclusionShape
    {
        readonly Vector2[] m_Points;
        readonly float m_Radius;

        ExclusionShape(Vector2[] points, float radius)
        {
            m_Points = points;
            m_Radius = radius;
        }

        public static ExclusionShape Circle(Vector2 centre, float radius) => new(new[] { centre }, radius);

        public static ExclusionShape Path(IReadOnlyList<Vector2> points, float halfWidth)
        {
            var copy = new Vector2[points.Count];
            for (int i = 0; i < copy.Length; i++)
                copy[i] = points[i];
            return new ExclusionShape(copy, halfWidth);
        }

        /// <summary>Distance from <paramref name="p"/> to the zone's edge; negative inside.</summary>
        public float Distance(Vector2 p)
        {
            if (m_Points == null || m_Points.Length == 0)
                return float.MaxValue;
            return RegionGeometry.DistanceToOutline(m_Points, p, closed: false) - m_Radius;
        }
    }
}
