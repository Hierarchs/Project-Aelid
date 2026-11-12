using System.Collections.Generic;
using UnityEngine;

namespace Aelid.World
{
    /// <summary>2D (XZ) polygon and polyline helpers for region outlines and keep-clear zones.</summary>
    public static class RegionGeometry
    {
        /// <summary>Even-odd point-in-polygon test. The outline closes back to its first point.</summary>
        public static bool Contains(IReadOnlyList<Vector2> polygon, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Vector2 a = polygon[i], b = polygon[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>Distance from <paramref name="p"/> to the nearest edge of the outline.</summary>
        public static float DistanceToOutline(IReadOnlyList<Vector2> points, Vector2 p, bool closed)
        {
            if (points.Count == 0)
                return float.MaxValue;
            if (points.Count == 1)
                return Vector2.Distance(p, points[0]);

            float best = float.MaxValue;
            int segments = closed ? points.Count : points.Count - 1;
            for (int i = 0; i < segments; i++)
                best = Mathf.Min(best, DistanceToSegment(p, points[i], points[(i + 1) % points.Count]));
            return best;
        }

        public static Rect Bounds(IReadOnlyList<Vector2> points)
        {
            if (points.Count == 0)
                return Rect.zero;
            Vector2 min = points[0], max = points[0];
            foreach (var p in points)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
