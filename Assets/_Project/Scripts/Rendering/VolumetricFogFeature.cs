using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Aelid.Rendering
{
    /// <summary>
    /// Volumetric height fog with light shafts (RenderGraph).
    ///
    ///   1. March     - half resolution: ray-march each pixel's view ray through the
    ///                  fog layer, sampling the main-light shadow map (shafts), the
    ///                  cloud-shadow cookie, sky ambient and up to 8 local lights.
    ///                  Output: in-scattered light (rgb) and transmittance (a).
    ///   2. Composite - full resolution, depth-aware upsample, blended onto the
    ///                  camera colour as colour * transmittance + in-scatter.
    ///
    /// Runs after transparents, so the ASCII pass quantises the fogged image.
    /// No temporal jitter: march offsets are hashed from world position and the
    /// half-resolution grid lines up with the snapped ASCII cells, so the fog adds
    /// no glyph shimmer.
    /// </summary>
    public sealed class VolumetricFogFeature : ScriptableRendererFeature
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Show the fog in Scene view cameras too.")]
            public bool applyInSceneView = true;

            public Shader shader;
        }

        public const int MaxLocalLights = 8;

        public Settings settings = new();

        FogPass m_Pass;
        Material m_Material;

        public override void Create()
        {
            m_Pass = new FogPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
                return;
            if (cameraType == CameraType.SceneView && !settings.applyInSceneView)
                return;

            VolumetricFog fog = VolumeManager.instance.stack?.GetComponent<VolumetricFog>();
            if (fog == null || !fog.IsActive())
                return;

            if (settings.shader == null)
                settings.shader = Shader.Find("Hidden/Aelid/VolumetricFog");
            if (settings.shader == null)
                return;
            if (m_Material == null || m_Material.shader != settings.shader)
                m_Material = CoreUtils.CreateEngineMaterial(settings.shader);

            m_Pass.Setup(m_Material, fog);
            m_Pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_Material);
            m_Material = null;
        }

        /// <summary>
        /// Fraction of fog a local light removes at <paramref name="distance"/> (matches the shader):
        /// full clearing at the light, easing to none at radiusFraction x range.
        /// <paramref name="lit"/> is how brightly the light is on (0 when a lamp is off by day).
        /// </summary>
        public static float Clearing(float distance, float range, float radiusFraction, float lit, float strength)
        {
            float radius = Mathf.Max(range * radiusFraction, 1e-3f);
            float x = Mathf.Clamp01(1f - distance * distance / (radius * radius));
            return strength * Mathf.Clamp01(lit) * x * x * (3f - 2f * x);
        }

        /// <summary>Henyey-Greenstein phase, scaled so isotropic scattering is 1 (matches the shader).</summary>
        public static float Phase(float cosTheta, float g)
        {
            float g2 = g * g;
            float denom = Mathf.Max(1f + g2 - 2f * g * cosTheta, 1e-4f);
            return (1f - g2) / (denom * Mathf.Sqrt(denom));
        }

        sealed class FogPass : ScriptableRenderPass
        {
            const int k_PassMarch = 0;
            const int k_PassComposite = 1;

            static class Ids
            {
                public static readonly int Depth = Shader.PropertyToID("_FogDepthTexture");
                public static readonly int Fog = Shader.PropertyToID("_FogTexture");
                public static readonly int InvViewProj = Shader.PropertyToID("_FogInvViewProj");
                public static readonly int Camera = Shader.PropertyToID("_FogCamera");
                public static readonly int CameraForward = Shader.PropertyToID("_FogCameraForward");
                public static readonly int Size = Shader.PropertyToID("_FogSize");
                public static readonly int Orientation = Shader.PropertyToID("_FogOrientation");
                public static readonly int Medium = Shader.PropertyToID("_FogMedium");
                public static readonly int Medium2 = Shader.PropertyToID("_FogMedium2");
                public static readonly int March = Shader.PropertyToID("_FogMarch");
                public static readonly int Light = Shader.PropertyToID("_FogLight");
                public static readonly int Tint = Shader.PropertyToID("_FogTint");
                public static readonly int Ambient = Shader.PropertyToID("_FogAmbient");
                public static readonly int Lights = Shader.PropertyToID("_FogLights");
                public static readonly int LightColors = Shader.PropertyToID("_FogLightColors");
                public static readonly int LightCount = Shader.PropertyToID("_FogLightCount");
                public static readonly int Clear = Shader.PropertyToID("_FogClear");
            }

            sealed class Uniforms
            {
                public Matrix4x4 invViewProj;
                public Vector4 camera, cameraForward, size, medium, medium2, march, light, tint, ambient, clear;
                public readonly Vector4[] lights = new Vector4[MaxLocalLights];
                public readonly Vector4[] lightColors = new Vector4[MaxLocalLights];
                public int lightCount;

                public void ApplyTo(MaterialPropertyBlock block)
                {
                    block.SetMatrix(Ids.InvViewProj, invViewProj);
                    block.SetVector(Ids.Camera, camera);
                    block.SetVector(Ids.CameraForward, cameraForward);
                    block.SetVector(Ids.Size, size);
                    block.SetVector(Ids.Medium, medium);
                    block.SetVector(Ids.Medium2, medium2);
                    block.SetVector(Ids.March, march);
                    block.SetVector(Ids.Light, light);
                    block.SetVector(Ids.Tint, tint);
                    block.SetVector(Ids.Ambient, ambient);
                    block.SetVector(Ids.Clear, clear);
                    block.SetVectorArray(Ids.Lights, lights);
                    block.SetVectorArray(Ids.LightColors, lightColors);
                    block.SetInt(Ids.LightCount, lightCount);
                }
            }

            sealed class MarchData
            {
                public Material material;
                public MaterialPropertyBlock block;
                public Uniforms uniforms;
                public TextureHandle depth;
            }

            sealed class CompositeData
            {
                public Material material;
                public MaterialPropertyBlock block;
                public Uniforms uniforms;
                public TextureHandle depth, fog, color;
            }

            readonly Uniforms m_Uniforms = new();
            readonly MaterialPropertyBlock m_MarchBlock = new();
            readonly MaterialPropertyBlock m_CompositeBlock = new();
            readonly float[] m_LightScores = new float[MaxLocalLights];
            readonly Vector3[] m_ProbeDirections = { Vector3.up, Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            readonly Color[] m_ProbeResults = new Color[5];

            Material m_Material;
            VolumetricFog m_Fog;

            public FogPass()
            {
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
                profilingSampler = new ProfilingSampler("Aelid Volumetric Fog");
                requiresIntermediateTexture = true;
            }

            public void Setup(Material material, VolumetricFog fog)
            {
                m_Material = material;
                m_Fog = fog;
            }

            static Vector4 Orientation(in RasterGraphContext ctx, in TextureHandle depth, in TextureHandle color)
            {
                bool depthTop = ctx.GetTextureUVOrigin(depth) == TextureUVOrigin.TopLeft;
                bool colorTop = color.IsValid() ? ctx.GetTextureUVOrigin(color) == TextureUVOrigin.TopLeft : depthTop;
                // x: row 0 of the depth texture is the top of the screen; y: colour rows run the other way.
                return new Vector4(depthTop ? 1f : 0f, depthTop != colorTop ? 1f : 0f, 0f, 0f);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                var lightData = frameData.Get<UniversalLightData>();

                if (resourceData.isActiveTargetBackBuffer)
                    return;
                TextureHandle color = resourceData.activeColorTexture;
                TextureHandle depth = resourceData.cameraDepthTexture;
                if (!color.IsValid() || !depth.IsValid())
                    return;

                TextureDesc depthDesc = renderGraph.GetTextureDesc(depth);
                int width = depthDesc.width, height = depthDesc.height;
                if (width <= 0 || height <= 0)
                    return;
                int halfWidth = (width + 1) / 2, halfHeight = (height + 1) / 2;

                FillUniforms(cameraData, lightData, width, height, halfWidth, halfHeight);

                var fogDesc = new TextureDesc(halfWidth, halfHeight)
                {
                    name = "_FogTexture",
                    format = GraphicsFormat.R16G16B16A16_SFloat,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    clearBuffer = false,
                };
                TextureHandle fogTexture = renderGraph.CreateTexture(fogDesc);

                using (var builder = renderGraph.AddRasterRenderPass<MarchData>("Fog March", out var data, profilingSampler))
                {
                    data.material = m_Material;
                    data.block = m_MarchBlock;
                    data.uniforms = m_Uniforms;
                    data.depth = depth;
                    builder.UseTexture(depth);
                    builder.UseAllGlobalTextures(true); // main-light shadow map and cloud cookie
                    builder.SetRenderAttachment(fogTexture, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (MarchData d, RasterGraphContext ctx) =>
                    {
                        MaterialPropertyBlock block = d.block;
                        block.Clear();
                        d.uniforms.ApplyTo(block);
                        block.SetVector(Ids.Orientation, Orientation(ctx, d.depth, TextureHandle.nullHandle));
                        block.SetTexture(Ids.Depth, d.depth);
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, k_PassMarch, MeshTopology.Triangles, 3, 1, block);
                    });
                }

                using (var builder = renderGraph.AddRasterRenderPass<CompositeData>("Fog Composite", out var data, profilingSampler))
                {
                    data.material = m_Material;
                    data.block = m_CompositeBlock;
                    data.uniforms = m_Uniforms;
                    data.depth = depth;
                    data.fog = fogTexture;
                    data.color = color;
                    builder.UseTexture(depth);
                    builder.UseTexture(fogTexture);
                    builder.SetRenderAttachment(color, 0, AccessFlags.ReadWrite);
                    builder.SetRenderFunc(static (CompositeData d, RasterGraphContext ctx) =>
                    {
                        MaterialPropertyBlock block = d.block;
                        block.Clear();
                        d.uniforms.ApplyTo(block);
                        block.SetVector(Ids.Orientation, Orientation(ctx, d.depth, d.color));
                        block.SetTexture(Ids.Depth, d.depth);
                        block.SetTexture(Ids.Fog, d.fog);
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, k_PassComposite, MeshTopology.Triangles, 3, 1, block);
                    });
                }
            }

            void FillUniforms(UniversalCameraData cameraData, UniversalLightData lightData, int width, int height, int halfWidth, int halfHeight)
            {
                VolumetricFog f = m_Fog;
                var u = m_Uniforms;
                Camera camera = cameraData.camera;

                Matrix4x4 view = cameraData.GetViewMatrix();
                Matrix4x4 projection = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), false);
                u.invViewProj = (projection * view).inverse;
                Vector3 position = camera.transform.position, forward = camera.transform.forward;
                u.camera = new Vector4(position.x, position.y, position.z, camera.orthographic ? 1f : 0f);
                u.cameraForward = new Vector4(forward.x, forward.y, forward.z, 0f);
                u.size = new Vector4(width, height, halfWidth, halfHeight);

                float amount = f.intensity.value;
                u.medium = new Vector4(f.density.value * amount, f.heightDensity.value * amount, f.baseHeight.value, 1f / f.heightFalloff.value);
                u.medium2 = new Vector4(f.top.value, f.morningMistDensity.value * amount, f.noise.value, 1f / f.noiseScale.value);
                float time = Application.isPlaying ? Time.time : 0f;
                Vector2 drift = f.wind.value * time;
                u.march = new Vector4(drift.x, drift.y, f.steps.value, f.maxOpacity.value);
                u.light = new Vector4(f.anisotropy.value, f.sunScattering.value, f.ambientScattering.value, f.localLightScattering.value);
                Color tint = f.tint.value;
                u.tint = new Vector4(tint.r, tint.g, tint.b, 0f);
                Color ambient = AmbientColor();
                u.ambient = new Vector4(ambient.r, ambient.g, ambient.b, 0f);
                u.clear = new Vector4(f.lightClearing.value, f.clearingRadius.value, 0f, 0f);

                CollectLocalLights(lightData);
            }

            Color AmbientColor()
            {
                switch (RenderSettings.ambientMode)
                {
                    case UnityEngine.Rendering.AmbientMode.Trilight:
                        return (RenderSettings.ambientSkyColor + RenderSettings.ambientEquatorColor) * 0.5f;
                    case UnityEngine.Rendering.AmbientMode.Flat:
                        return RenderSettings.ambientLight;
                    default:
                        RenderSettings.ambientProbe.Evaluate(m_ProbeDirections, m_ProbeResults);
                        Color sum = Color.black;
                        foreach (var c in m_ProbeResults)
                            sum += c;
                        return sum / m_ProbeResults.Length;
                }
            }

            /// <summary>Brightest visible point and spot lights, for halos in the fog.</summary>
            void CollectLocalLights(UniversalLightData lightData)
            {
                var u = m_Uniforms;
                u.lightCount = 0;
                var visible = lightData.visibleLights;
                for (int i = 0; i < visible.Length; i++)
                {
                    if (i == lightData.mainLightIndex)
                        continue;
                    VisibleLight light = visible[i];
                    if (light.lightType != LightType.Point && light.lightType != LightType.Spot)
                        continue;
                    Color c = light.finalColor;
                    float score = c.maxColorComponent * light.range;
                    if (score <= 0f)
                        continue;

                    int slot = u.lightCount < MaxLocalLights ? u.lightCount++ : -1;
                    if (slot < 0)
                    {
                        // Replace the dimmest kept light if this one is brighter.
                        int dimmest = 0;
                        for (int k = 1; k < MaxLocalLights; k++)
                            if (m_LightScores[k] < m_LightScores[dimmest])
                                dimmest = k;
                        if (score <= m_LightScores[dimmest])
                            continue;
                        slot = dimmest;
                    }
                    Vector4 p = light.localToWorldMatrix.GetColumn(3);
                    float range = Mathf.Max(light.range, 0.01f);
                    u.lights[slot] = new Vector4(p.x, p.y, p.z, 1f / (range * range));
                    u.lightColors[slot] = new Vector4(c.r, c.g, c.b, Mathf.Clamp01(c.maxColorComponent)); // w: how lit, for clearing
                    m_LightScores[slot] = score;
                }
            }
        }
    }
}
