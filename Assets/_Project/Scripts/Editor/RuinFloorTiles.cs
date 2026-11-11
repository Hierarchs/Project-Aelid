using UnityEngine;

namespace Aelid.EditorTools
{
    /// <summary>Weathered paving, kept in the existing ruins mesh and collider.</summary>
    public static class RuinFloorTiles
    {
        public static void AddTo(LowPolyMesh mesh, Vector3 ground, float yaw, int index)
        {
            Vector3 size;
            Vector3 tilt;
            float height, chip;
            int style = index % 6;
            switch (style)
            {
                case 1: // Settled into the soil: only the uneven upper edge remains exposed.
                    size = new Vector3(0.96f, 0.20f, 1.06f);
                    tilt = new Vector3(0.8f, yaw, -1.2f);
                    height = -0.09f; chip = 0.08f;
                    break;
                case 2: // One edge has lifted; keep the step below the player's 0.4 m limit.
                    size = new Vector3(1.10f, 0.20f, 0.94f);
                    tilt = new Vector3(7.5f, yaw, -4f);
                    height = 0.05f; chip = 0.06f;
                    break;
                case 3: // A longer, narrow paving stone breaks up the square silhouettes.
                    size = new Vector3(1.38f, 0.18f, 0.72f);
                    tilt = new Vector3(-2f, yaw, 3f);
                    height = 0.01f; chip = 0.08f;
                    break;
                case 4: // Heavily chipped corners and a different direction of lean.
                    size = new Vector3(1.15f, 0.20f, 1.02f);
                    tilt = new Vector3(-4f, yaw, 6.5f);
                    height = 0.025f; chip = 0.22f;
                    break;
                case 5: // Two solid fragments with a real gap, rather than a painted crack.
                    size = new Vector3(1.08f, 0.18f, 1f);
                    tilt = new Vector3(3f, yaw, -2f);
                    height = 0.015f; chip = 0f;
                    break;
                default:
                    size = new Vector3(1.05f, 0.18f, 1f);
                    tilt = new Vector3(2f, yaw, -1f);
                    height = 0f; chip = 0.04f;
                    break;
            }

            // Small, repeatable differences between stones of the same type; no shared RNG state.
            size.x *= 1f + 0.035f * (index / 6 % 3 - 1);
            Vector3 center = ground + Vector3.up * height;
            Quaternion rotation = Quaternion.Euler(tilt);
            float w = size.x * 0.5f, d = size.z * 0.5f;
            if (style == 5)
            {
                Vector3 offset = rotation * new Vector3(-0.045f, -0.02f, 0.045f);
                Slab(mesh, center + offset, rotation, size.y,
                    new[] { new Vector2(-w, -d), new Vector2(-w, d), new Vector2(w, d) });
                Slab(mesh, center - offset, rotation, size.y,
                    new[] { new Vector2(-w, -d), new Vector2(w, d), new Vector2(w, -d) });
                return;
            }

            Slab(mesh, center, rotation, size.y, new[]
            {
                new Vector2(-w + chip, -d), new Vector2(-w, -d + chip),
                new Vector2(-w, d - chip), new Vector2(-w + chip, d),
                new Vector2(w - chip, d), new Vector2(w, d - chip),
                new Vector2(w, -d + chip), new Vector2(w - chip, -d),
            });
        }

        // Convex outline, clockwise in XZ: caps face up/down and the sides face out.
        static void Slab(LowPolyMesh mesh, Vector3 center, Quaternion rotation, float thickness, Vector2[] outline)
        {
            Vector2 centroid = Vector2.zero;
            foreach (Vector2 point in outline) centroid += point;
            centroid /= outline.Length;
            Vector3 P(Vector2 point, float y) => center + rotation * new Vector3(point.x, y, point.y);
            float h = thickness * 0.5f;
            Vector3 top = P(centroid, h), bottom = P(centroid, -h);
            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 a = outline[i], b = outline[(i + 1) % outline.Length];
                Vector3 ta = P(a, h), tb = P(b, h), ba = P(a, -h), bb = P(b, -h);
                mesh.Tri(top, ta, tb);
                mesh.Tri(bottom, bb, ba);
                mesh.Quad(ba, bb, tb, ta);
            }
        }
    }
}
