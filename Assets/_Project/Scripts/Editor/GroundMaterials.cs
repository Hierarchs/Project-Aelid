using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Aelid.EditorTools
{
    /// <summary>Updates only ground materials/layout; never regenerates scene objects.</summary>
    public static class GroundMaterials
    {
        public const string LayoutPath = "Assets/_Project/Shaders/GroundLayout.hlsl";
        const string MaterialDir = "Assets/_Project/Generated/Materials/";

        public static string LayoutText(Vector2[][] paths, Vector2 ruins, float halfWidth)
        {
            string F(float x) => x.ToString("0.0###", CultureInfo.InvariantCulture);
            int count = 0;
            foreach (var path in paths) count += path.Length - 1;
            var s = new StringBuilder("// Generated from VerticalSliceBuilder's paths. Use Aelid/Setup/Refresh Ground Materials.\n");
            s.Append("#ifndef AELID_GROUND_LAYOUT_INCLUDED\n#define AELID_GROUND_LAYOUT_INCLUDED\n");
            s.Append($"#define AELID_GROUND_PATH_COUNT {count}\nstatic const float4 kGroundPaths[{count}] =\n{{\n");
            foreach (var path in paths)
                for (int i = 1; i < path.Length; i++)
                    s.Append($"    float4({F(path[i-1].x)}, {F(path[i-1].y)}, {F(path[i].x)}, {F(path[i].y)}),\n");
            s.Append($"}};\nstatic const float2 kGroundRuins = float2({F(ruins.x)}, {F(ruins.y)});\n");
            s.Append($"static const float kGroundPathHalfWidth = {F(halfWidth)};\n#endif\n");
            return s.ToString();
        }

        public static void WriteLayout(Vector2[][] paths, Vector2 ruins, float halfWidth)
        {
            string text = LayoutText(paths, ruins, halfWidth);
            if (File.Exists(LayoutPath) && File.ReadAllText(LayoutPath) == text) return;
            File.WriteAllText(LayoutPath, text);
            AssetDatabase.ImportAsset(LayoutPath);
        }

        public static Material Get(string name)
        {
            Shader shader = Shader.Find("Aelid/Ground");
            if (shader == null) throw new System.InvalidOperationException("Aelid/Ground shader is missing.");
            string path = MaterialDir + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetColor("_BaseColor", Color.white);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
