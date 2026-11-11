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
                EditorUtility.SetDirty(asset);
            }

            var asciiShader = Shader.Find("Hidden/Aelid/Ascii");
            var idShader = Shader.Find("Hidden/Aelid/AsciiSurfaceId");
            if (asciiShader == null || idShader == null)
                Debug.LogError("[Aelid] ASCII shaders not found. Check Assets/_Project/Shaders for compile errors.");

            foreach (string path in k_RendererPaths)
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (data == null) continue;

                var feature = data.rendererFeatures.OfType<AsciiRendererFeature>().FirstOrDefault();
                if (feature == null)
                {
                    feature = ScriptableObject.CreateInstance<AsciiRendererFeature>();
                    feature.name = "Aelid ASCII";
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
                }

                feature.settings.asciiShader = asciiShader;
                feature.settings.surfaceIdShader = idShader;
                feature.SetActive(true);
                EditorUtility.SetDirty(feature);
                EditorUtility.SetDirty(data);
            }

            PlayerSettings.productName = "Aelid";
            AssetDatabase.SaveAssets();
        }
    }
}
