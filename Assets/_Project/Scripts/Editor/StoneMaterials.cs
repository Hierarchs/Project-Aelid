using UnityEditor;
using UnityEngine;

namespace Aelid.EditorTools
{
    /// <summary>Stone material presets; refresh never replaces terrain, ruins or scene props.</summary>
    public static class StoneMaterials
    {
        public static readonly string[] Names = { "Terrain_Rock", "Rock", "Stone", "Ruin_Stone", "Ruin_Moss" };

        // Layering, joint contrast, lichen, fracture frequency (per metre).
        public static Vector4 Defaults(string name) => name switch
        {
            "Terrain_Rock" => new Vector4(0.72f, 0.65f, 0.18f, 1.1f),
            "Rock" => new Vector4(0.35f, 0.55f, 0.16f, 1.6f),
            "Ruin_Moss" => new Vector4(0f, 0.28f, 0.38f, 2.8f),
            "Ruin_Stone" => new Vector4(0f, 0.28f, 0.06f, 2.8f),
            _ => new Vector4(0f, 0.3f, 0.04f, 2.4f),
        };

        public static void Apply(Material material)
        {
            Shader shader = Shader.Find("Aelid/Stone");
            if (shader == null) throw new System.InvalidOperationException("Aelid/Stone shader is missing.");
            if (material.shader != shader)
            {
                material.shader = shader;
                Vector4 preset = Defaults(material.name);
                material.SetFloat("_LayerStrength", preset.x);
                material.SetFloat("_JointStrength", preset.y);
                material.SetFloat("_MossAmount", preset.z);
                material.SetFloat("_FractureScale", preset.w);
                material.SetFloat("_ReliefStrength", 0.012f);
            }
            // Keep existing base colours and any subsequent artist tuning.
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
        }

        [MenuItem("Aelid/Setup/Refresh Stone Materials")]
        public static void Refresh()
        {
            foreach (string name in Names)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>($"Assets/_Project/Generated/Materials/{name}.mat");
                if (material != null) Apply(material);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
