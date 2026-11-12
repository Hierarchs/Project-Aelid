using System.Linq;
using Aelid.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Aelid.EditorTools
{
    /// <summary>
    /// Idempotent project configuration: URP asset settings the ASCII look
    /// depends on, and registration of the ASCII renderer feature.
    /// </summary>
    public static class ProjectSetup
    {
        static readonly string[] k_RendererPaths = { "Assets/Settings/PC_Renderer.asset", "Assets/Settings/Mobile_Renderer.asset" };
        static readonly string[] k_PipelinePaths = { "Assets/Settings/PC_RPAsset.asset", "Assets/Settings/Mobile_RPAsset.asset" };

        [MenuItem("Aelid/Setup/Apply Project Settings")]
        public static void Apply()
        {
            foreach (string path in k_PipelinePaths)
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (asset == null) continue;
                asset.msaaSampleCount = 1;          // ASCII quantizes anyway; MSAA would complicate the surface-ID pass
                asset.supportsHDR = true;           // bloom on bright glyphs (fire, magic)
                asset.shadowDistance = 90f;         // camera sits ~40 m from the action
                asset.shadowCascadeCount = 2;
                asset.supportsCameraDepthTexture = true;   // water shoreline / depth colour, even with ASCII off
                asset.supportsCameraOpaqueTexture = true;  // water refraction of the bed
                EditorUtility.SetDirty(asset);
            }

            var asciiShader = Shader.Find("Hidden/Aelid/Ascii");
            var idShader = Shader.Find("Hidden/Aelid/AsciiSurfaceId");
            if (asciiShader == null || idShader == null)
                Debug.LogError("[Aelid] ASCII shaders not found. Check Assets/_Project/Shaders for compile errors.");

            var fogShader = Shader.Find("Hidden/Aelid/VolumetricFog");
            if (fogShader == null)
                Debug.LogError("[Aelid] Volumetric fog shader not found. Check Assets/_Project/Shaders for compile errors.");

            foreach (string path in k_RendererPaths)
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (data == null) continue;

                // The fog pass runs after transparents, before the ASCII pass, whatever the list order.
                var fog = EnsureFeature<VolumetricFogFeature>(data, "Aelid Volumetric Fog");
                fog.settings.shader = fogShader;
                fog.SetActive(true);
                EditorUtility.SetDirty(fog);

                var feature = EnsureFeature<AsciiRendererFeature>(data, "Aelid ASCII");
                feature.settings.asciiShader = asciiShader;
                feature.settings.surfaceIdShader = idShader;
                feature.SetActive(true);
                EditorUtility.SetDirty(feature);
                EditorUtility.SetDirty(data);
            }

            PlayerSettings.productName = "Aelid";
            AssetDatabase.SaveAssets();
        }

        static T EnsureFeature<T>(UniversalRendererData data, string name) where T : ScriptableRendererFeature
        {
            var feature = data.rendererFeatures.OfType<T>().FirstOrDefault();
            if (feature != null)
                return feature;

            feature = ScriptableObject.CreateInstance<T>();
            feature.name = name;
            AssetDatabase.AddObjectToAsset(feature, data);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);

            var so = new SerializedObject(data);
            var features = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedPropertiesWithoutUndo();
            return feature;
        }
    }
}
