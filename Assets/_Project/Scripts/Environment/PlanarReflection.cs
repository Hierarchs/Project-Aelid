using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Aelid.Environment
{
    /// <summary>
    /// Planar reflection for a horizontal water surface (this transform's height).
    ///
    /// A hidden camera mirrors the game camera across the water plane and renders
    /// the world above it into a texture, which the water shader samples in screen
    /// space. A true mirror flips handedness; instead of a mirrored view matrix
    /// (which needs inverted culling), the camera uses an ordinary rotation and
    /// the shader flips the horizontal screen axis when sampling. An oblique near
    /// plane clips everything below the surface.
    ///
    /// Because the game camera only moves in whole ASCII cells, the mirrored camera
    /// moves in whole texels too, so reflections stay glyph-stable.
    /// The camera is CameraType.Reflection, so the ASCII pass skips it: the
    /// reflection is raw 3D and gets converted together with the water surface.
    /// </summary>
    [ExecuteAlways, RequireComponent(typeof(Renderer))]
    [AddComponentMenu("Aelid/Environment/Planar Reflection")]
    public sealed class PlanarReflection : MonoBehaviour
    {
        [Tooltip("Camera to mirror. Defaults to Camera.main.")]
        public Camera sourceCamera;
        [Range(0.25f, 1f)] public float resolutionScale = 0.5f;
        [Tooltip("Layers that appear in the reflection (the water layer itself is excluded by default).")]
        public LayerMask reflectLayers = ~(1 << 4);
        [Tooltip("Lifts the clip plane slightly to avoid seams at the shoreline.")]
        public float clipPlaneOffset = 0.03f;
        [Tooltip("Brightness of the open sky in the reflection. Below 1 keeps water dark so reflected objects stand out.")]
        [Range(0f, 1f)] public float skyBrightness = 0.4f;

        static readonly int k_Texture = Shader.PropertyToID("_PlanarReflectionTexture");
        static readonly int k_MoonDisc = Shader.PropertyToID("_WaterMoonDisc");

        Camera m_Camera;
        RenderTexture m_Texture;
        Renderer m_Renderer;
        MaterialPropertyBlock m_Block;

        public Camera ReflectionCamera => m_Camera;
        public RenderTexture Texture => m_Texture;

        void OnEnable()
        {
            m_Renderer = GetComponent<Renderer>();
            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            Cleanup();
        }

        void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras) => UpdateReflection();

        /// <summary>Updates and renders the reflection now (editor tools, captures).</summary>
        public void RenderImmediate()
        {
            if (UpdateReflection())
                m_Camera.Render();
        }

        /// <summary>Positions the mirror camera and binds its texture without rendering.</summary>
        public bool UpdateReflection()
        {
            Camera src = sourceCamera != null ? sourceCamera : Camera.main;
            if (src == null || m_Renderer == null || src == m_Camera)
                return false;

            int width = Mathf.Max(16, Mathf.RoundToInt(src.pixelWidth * resolutionScale));
            int height = Mathf.Max(16, Mathf.RoundToInt(src.pixelHeight * resolutionScale));
            EnsureCamera();
            EnsureTexture(width, height);

            // Mirror position and axes across the plane y = surface.
            float surface = transform.position.y;
            Transform s = src.transform;
            Vector3 position = s.position;
            position.y = 2f * surface - position.y;
            Vector3 forward = s.forward;
            forward.y = -forward.y;
            Vector3 up = s.up;
            up.y = -up.y;
            m_Camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, up));

            m_Camera.orthographic = src.orthographic;
            m_Camera.orthographicSize = src.orthographicSize;
            m_Camera.aspect = src.aspect;
            m_Camera.fieldOfView = src.fieldOfView;
            m_Camera.nearClipPlane = src.nearClipPlane;
            m_Camera.farClipPlane = src.farClipPlane;
            m_Camera.depth = src.depth - 1f; // render before the game camera
            m_Camera.cullingMask = reflectLayers;
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = RenderSettings.ambientSkyColor * skyBrightness; // sky tone follows the day/night cycle
            m_Camera.targetTexture = m_Texture;

            m_Camera.ResetWorldToCameraMatrix();
            m_Camera.ResetProjectionMatrix();
            Vector4 clipPlane = CameraSpacePlane(m_Camera, new Vector3(0f, surface + clipPlaneOffset, 0f), Vector3.up);
            m_Camera.projectionMatrix = m_Camera.CalculateObliqueMatrix(clipPlane);

            // Skip the cost while the water is off screen (always render outside play mode).
            m_Camera.enabled = !Application.isPlaying || m_Renderer.isVisible;

            m_Block ??= new MaterialPropertyBlock();
            m_Renderer.GetPropertyBlock(m_Block);
            m_Block.SetTexture(k_Texture, m_Texture);
            var cycle = DayNightCycle.Active;
            Vector4 moon = Vector4.zero;
            if (cycle != null && cycle.MoonVisibility > 0f)
            {
                Bounds bounds = m_Renderer.bounds;
                float span = Mathf.Min(bounds.extents.x, bounds.extents.z);
                Vector3 center = bounds.center + cycle.MoonDirection * (span * 0.29f);
                moon = new Vector4(center.x, center.z, span * 0.085f, cycle.MoonVisibility);
            }
            m_Block.SetVector(k_MoonDisc, moon);
            m_Renderer.SetPropertyBlock(m_Block);
            return true;
        }

        static Vector4 CameraSpacePlane(Camera cam, Vector3 point, Vector3 normal)
        {
            Matrix4x4 view = cam.worldToCameraMatrix;
            Vector3 p = view.MultiplyPoint(point);
            Vector3 n = view.MultiplyVector(normal).normalized;
            return new Vector4(n.x, n.y, n.z, -Vector3.Dot(p, n));
        }

        void EnsureCamera()
        {
            if (m_Camera != null)
                return;
            var go = new GameObject("Water Reflection Camera") { hideFlags = HideFlags.HideAndDontSave };
            m_Camera = go.AddComponent<Camera>();
            m_Camera.cameraType = CameraType.Reflection;
            m_Camera.allowMSAA = false;
            m_Camera.allowHDR = true;
            var data = m_Camera.GetUniversalAdditionalCameraData();
            data.renderShadows = false;
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
        }

        void EnsureTexture(int width, int height)
        {
            if (m_Texture != null && m_Texture.width == width && m_Texture.height == height)
                return;
            ReleaseTexture();
            m_Texture = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR)
            {
                name = "Water Planar Reflection",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            m_Texture.Create();
        }

        void ReleaseTexture()
        {
            if (m_Texture == null)
                return;
            if (m_Camera != null && m_Camera.targetTexture == m_Texture)
                m_Camera.targetTexture = null;
            m_Texture.Release();
            DestroyObject(m_Texture);
            m_Texture = null;
        }

        void Cleanup()
        {
            ReleaseTexture();
            if (m_Camera != null)
                DestroyObject(m_Camera.gameObject);
            m_Camera = null;
        }

        static void DestroyObject(Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
