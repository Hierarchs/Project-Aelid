using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Aelid.Rendering
{
    /// <summary>
    /// URP renderer feature that turns the lit 3D frame into the hybrid ASCII image.
    ///
    /// Frame flow (all inside one ScriptableRenderPass, RenderGraph API):
    ///   1. Surface ID   - redraw renderers carrying an ASCII class bit into an R8 mask
    ///   2. Reduce       - per cell: average color, class, depth, normal  (cell res)
    ///   3. Select       - per cell: glyph index + glyph color            (cell res)
    ///   4. Composite    - per pixel: draw glyph, keep Exempt pixels 3D  (full res)
    /// Passes 2-3 run at 1/64th of the pixel count, so the cost is dominated by
    /// one full-res read in Reduce and one full-res write in Composite.
    /// </summary>
    public sealed class AsciiRendererFeature : ScriptableRendererFeature
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Before post-processing lets bloom and color grading act on the glyphs.")]
            public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingPostProcessing;

            [Tooltip("Show the ASCII look in Scene view cameras too.")]
            public bool applyInSceneView = true;

            [Tooltip("Object layers considered by the surface-ID pass.")]
            public LayerMask surfaceLayerMask = ~0;

            public Shader asciiShader;
            public Shader surfaceIdShader;

            [Tooltip("Optional replacement atlas (128x128, 16x16 glyphs of 8x8). Empty = built-in glyphs.")]
            public Texture2D glyphAtlasOverride;
        }

        public Settings settings = new();

        AsciiRenderPass m_Pass;
        Material m_AsciiMaterial;
        Material m_SurfaceIdMaterial;

        public override void Create()
        {
            m_Pass = new AsciiRenderPass { renderPassEvent = settings.passEvent };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
                return;
            if (cameraType == CameraType.SceneView && !settings.applyInSceneView)
                return;
            if (!AsciiRuntimeOverrides.Enabled)
                return;

            AsciiSettings volume = AsciiGrid.CurrentSettings;
            if (volume == null || !volume.IsActive())
                return;

            if (!EnsureMaterials())
                return;

            Texture atlas = settings.glyphAtlasOverride != null ? settings.glyphAtlasOverride : AsciiGlyphAtlas.Default;
            m_Pass.renderPassEvent = settings.passEvent;
            m_Pass.Setup(m_AsciiMaterial, m_SurfaceIdMaterial, atlas, settings.surfaceLayerMask, volume);
            m_Pass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
            renderer.EnqueuePass(m_Pass);
        }

        bool EnsureMaterials()
        {
            if (settings.asciiShader == null)
                settings.asciiShader = Shader.Find("Hidden/Aelid/Ascii");
            if (settings.surfaceIdShader == null)
                settings.surfaceIdShader = Shader.Find("Hidden/Aelid/AsciiSurfaceId");
            if (settings.asciiShader == null || settings.surfaceIdShader == null)
                return false;

            if (m_AsciiMaterial == null || m_AsciiMaterial.shader != settings.asciiShader)
                m_AsciiMaterial = CoreUtils.CreateEngineMaterial(settings.asciiShader);
            if (m_SurfaceIdMaterial == null || m_SurfaceIdMaterial.shader != settings.surfaceIdShader)
                m_SurfaceIdMaterial = CoreUtils.CreateEngineMaterial(settings.surfaceIdShader);
            return m_AsciiMaterial != null && m_SurfaceIdMaterial != null;
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_AsciiMaterial);
            CoreUtils.Destroy(m_SurfaceIdMaterial);
            m_AsciiMaterial = null;
            m_SurfaceIdMaterial = null;
        }

        sealed class AsciiRenderPass : ScriptableRenderPass
        {
            const int k_PassReduce = 0;
            const int k_PassSelect = 1;
            const int k_PassComposite = 2;

            static class Ids
            {
                public static readonly int SceneColor = Shader.PropertyToID("_AsciiSceneColor");
                public static readonly int Depth = Shader.PropertyToID("_AsciiDepth");
                public static readonly int Normals = Shader.PropertyToID("_AsciiNormals");
                public static readonly int HasNormals = Shader.PropertyToID("_AsciiHasNormals");
                public static readonly int SurfaceId = Shader.PropertyToID("_AsciiSurfaceId");
                public static readonly int CellColor = Shader.PropertyToID("_AsciiCellColor");
                public static readonly int CellGeometry = Shader.PropertyToID("_AsciiCellGeometry");
                public static readonly int CellGlyph = Shader.PropertyToID("_AsciiCellGlyph");
                public static readonly int GlyphAtlas = Shader.PropertyToID("_AsciiGlyphAtlas");
                public static readonly int Cell = Shader.PropertyToID("_AsciiCell");
                public static readonly int Source = Shader.PropertyToID("_AsciiSource");
                public static readonly int Offset = Shader.PropertyToID("_AsciiOffset");
                public static readonly int Tone = Shader.PropertyToID("_AsciiTone");
                public static readonly int Color = Shader.PropertyToID("_AsciiColor");
                public static readonly int Edge = Shader.PropertyToID("_AsciiEdge");
                public static readonly int Misc = Shader.PropertyToID("_AsciiMisc");
                public static readonly int OutlineColor = Shader.PropertyToID("_AsciiOutlineColor");
                public static readonly int ClassParams = Shader.PropertyToID("_AsciiClassParams");
                public static readonly int Debug = Shader.PropertyToID("_AsciiDebug");
                public static readonly int Orientation = Shader.PropertyToID("_AsciiOrientation");
            }

            /// <summary>1 when pixel row 0 of the camera texture is the top of the screen.</summary>
            static Vector4 Orientation(in RasterGraphContext ctx, in TextureHandle cameraTexture)
            {
                bool rowsGoDown = ctx.GetTextureUVOrigin(cameraTexture) == TextureUVOrigin.TopLeft;
                return new Vector4(rowsGoDown ? 1f : 0f, 0f, 0f, 0f);
            }

            static readonly List<ShaderTagId> k_ShaderTags = new()
            {
                new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("SRPDefaultUnlit"),
            };

            /// <summary>Uniforms shared by all three full-screen passes.</summary>
            sealed class Uniforms
            {
                public Vector4 cell, source, offset, tone, color, edge, misc, outlineColor, debug;
                public readonly Vector4[] classParams = new Vector4[AsciiSurfaceLayers.TableSize];

                public void ApplyTo(MaterialPropertyBlock block)
                {
                    block.SetVector(Ids.Cell, cell);
                    block.SetVector(Ids.Source, source);
                    block.SetVector(Ids.Offset, offset);
                    block.SetVector(Ids.Tone, tone);
                    block.SetVector(Ids.Color, color);
                    block.SetVector(Ids.Edge, edge);
                    block.SetVector(Ids.Misc, misc);
                    block.SetVector(Ids.OutlineColor, outlineColor);
                    block.SetVector(Ids.Debug, debug);
                    block.SetVectorArray(Ids.ClassParams, classParams);
                }
            }

            sealed class SurfaceIdPassData
            {
                public RendererListHandle rendererList;
            }

            sealed class ReducePassData
            {
                public Material material;
                public MaterialPropertyBlock block;
                public Uniforms uniforms;
                public TextureHandle color, depth, normals, surfaceId;
                public bool hasNormals;
            }

            sealed class SelectPassData
            {
                public Material material;
                public MaterialPropertyBlock block;
                public Uniforms uniforms;
                public TextureHandle cellColor, cellGeometry, cameraColor;
            }

            sealed class CompositePassData
            {
                public Material material;
                public MaterialPropertyBlock block;
                public Uniforms uniforms;
                public TextureHandle color, surfaceId, cellColor, cellGlyph;
                public Texture atlas;
            }

            readonly Uniforms m_Uniforms = new();
            readonly MaterialPropertyBlock m_ReduceBlock = new();
            readonly MaterialPropertyBlock m_SelectBlock = new();
            readonly MaterialPropertyBlock m_CompositeBlock = new();

            Material m_AsciiMaterial;
            Material m_SurfaceIdMaterial;
            Texture m_Atlas;
            LayerMask m_LayerMask;
            AsciiSettings m_Settings;
            bool m_WarnedMsaa;

            public AsciiRenderPass()
            {
                profilingSampler = new ProfilingSampler("Aelid ASCII");
                requiresIntermediateTexture = true;
            }

            public void Setup(Material ascii, Material surfaceId, Texture atlas, LayerMask layerMask, AsciiSettings settings)
            {
                m_AsciiMaterial = ascii;
                m_SurfaceIdMaterial = surfaceId;
                m_Atlas = atlas;
                m_LayerMask = layerMask;
                m_Settings = settings;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();

                if (resourceData.isActiveTargetBackBuffer)
                    return;

                TextureHandle source = resourceData.activeColorTexture;
                TextureDesc sourceDesc = renderGraph.GetTextureDesc(source);
                int width = sourceDesc.width;
                int height = sourceDesc.height;
                if (width <= 0 || height <= 0)
                    return;

                int cellSize = AsciiGrid.ResolveCellSize(m_Settings, height);
                int cellsX = (width + cellSize - 1) / cellSize;
                int cellsY = (height + cellSize - 1) / cellSize;

                FillUniforms(cameraData.camera, width, height, cellSize, cellsX, cellsY);

                // ---- 1. Surface ID ----------------------------------------------------
                var idDesc = new TextureDesc(width, height)
                {
                    name = "_AsciiSurfaceId",
                    format = GraphicsFormat.R8_UNorm,
                    clearBuffer = true,
                    clearColor = Color.clear,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                TextureHandle surfaceId = renderGraph.CreateTexture(idDesc);

                TextureHandle activeDepth = resourceData.activeDepthTexture;
                bool canDrawIds = activeDepth.IsValid() && renderGraph.GetTextureDesc(activeDepth).msaaSamples == MSAASamples.None;
                if (!canDrawIds && !m_WarnedMsaa)
                {
                    m_WarnedMsaa = true;
                    Debug.LogWarning("[Aelid ASCII] MSAA is enabled; surface classes (Exempt/Water/...) are disabled. Turn MSAA off in the URP asset.");
                }

                using (var builder = renderGraph.AddRasterRenderPass<SurfaceIdPassData>("ASCII Surface ID", out var data, profilingSampler))
                {
                    builder.SetRenderAttachment(surfaceId, 0, AccessFlags.Write);
                    if (canDrawIds)
                    {
                        var renderingData = frameData.Get<UniversalRenderingData>();
                        var lightData = frameData.Get<UniversalLightData>();
                        DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(k_ShaderTags, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);
                        drawing.overrideMaterial = m_SurfaceIdMaterial;
                        drawing.overrideMaterialPassIndex = 0;
                        var filtering = new FilteringSettings(RenderQueueRange.opaque, m_LayerMask, AsciiSurfaceLayers.AllClassBits);
                        data.rendererList = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, drawing, filtering));
                        builder.UseRendererList(data.rendererList);
                        builder.SetRenderAttachmentDepth(activeDepth, AccessFlags.Read);
                    }
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (SurfaceIdPassData d, RasterGraphContext ctx) =>
                    {
                        if (d.rendererList.IsValid())
                            ctx.cmd.DrawRendererList(d.rendererList);
                    });
                }

                // ---- 2. Reduce --------------------------------------------------------
                TextureHandle cellColor = CreateCellTexture(renderGraph, cellsX, cellsY, "_AsciiCellColor");
                TextureHandle cellGeometry = CreateCellTexture(renderGraph, cellsX, cellsY, "_AsciiCellGeometry");

                using (var builder = renderGraph.AddRasterRenderPass<ReducePassData>("ASCII Reduce", out var data, profilingSampler))
                {
                    data.material = m_AsciiMaterial;
                    data.block = m_ReduceBlock;
                    data.uniforms = m_Uniforms;
                    data.color = source;
                    data.depth = resourceData.cameraDepthTexture;
                    data.normals = resourceData.cameraNormalsTexture;
                    data.hasNormals = data.normals.IsValid();
                    data.surfaceId = surfaceId;

                    builder.UseTexture(data.color);
                    builder.UseTexture(data.depth);
                    if (data.hasNormals)
                        builder.UseTexture(data.normals);
                    builder.UseTexture(data.surfaceId);
                    builder.SetRenderAttachment(cellColor, 0, AccessFlags.Write);
                    builder.SetRenderAttachment(cellGeometry, 1, AccessFlags.Write);

                    builder.SetRenderFunc(static (ReducePassData d, RasterGraphContext ctx) =>
                    {
                        MaterialPropertyBlock block = d.block;
                        block.Clear();
                        d.uniforms.ApplyTo(block);
                        block.SetTexture(Ids.SceneColor, d.color);
                        block.SetTexture(Ids.Depth, d.depth);
                        if (d.hasNormals)
                            block.SetTexture(Ids.Normals, d.normals);
                        else
                            block.SetTexture(Ids.Normals, Texture2D.blackTexture);
                        block.SetFloat(Ids.HasNormals, d.hasNormals ? 1f : 0f);
                        block.SetTexture(Ids.SurfaceId, d.surfaceId);
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, k_PassReduce, MeshTopology.Triangles, 3, 1, block);
                    });
                }

                // ---- 3. Select --------------------------------------------------------
                TextureHandle cellGlyph = CreateCellTexture(renderGraph, cellsX, cellsY, "_AsciiCellGlyph");

                using (var builder = renderGraph.AddRasterRenderPass<SelectPassData>("ASCII Select", out var data, profilingSampler))
                {
                    data.material = m_AsciiMaterial;
                    data.block = m_SelectBlock;
                    data.uniforms = m_Uniforms;
                    data.cellColor = cellColor;
                    data.cellGeometry = cellGeometry;
                    data.cameraColor = source;

                    builder.UseTexture(cellColor);
                    builder.UseTexture(cellGeometry);
                    builder.SetRenderAttachment(cellGlyph, 0, AccessFlags.Write);

                    builder.SetRenderFunc(static (SelectPassData d, RasterGraphContext ctx) =>
                    {
                        MaterialPropertyBlock block = d.block;
                        block.Clear();
                        d.uniforms.ApplyTo(block);
                        block.SetVector(Ids.Orientation, Orientation(ctx, d.cameraColor));
                        block.SetTexture(Ids.CellColor, d.cellColor);
                        block.SetTexture(Ids.CellGeometry, d.cellGeometry);
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, k_PassSelect, MeshTopology.Triangles, 3, 1, block);
                    });
                }

                // ---- 4. Composite -----------------------------------------------------
                TextureDesc outputDesc = sourceDesc;
                outputDesc.name = "_AsciiOutput";
                outputDesc.clearBuffer = false;
                TextureHandle output = renderGraph.CreateTexture(outputDesc);

                using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>("ASCII Composite", out var data, profilingSampler))
                {
                    data.material = m_AsciiMaterial;
                    data.block = m_CompositeBlock;
                    data.uniforms = m_Uniforms;
                    data.color = source;
                    data.surfaceId = surfaceId;
                    data.cellColor = cellColor;
                    data.cellGlyph = cellGlyph;
                    data.atlas = m_Atlas;

                    builder.UseTexture(source);
                    builder.UseTexture(surfaceId);
                    builder.UseTexture(cellColor);
                    builder.UseTexture(cellGlyph);
                    builder.SetRenderAttachment(output, 0, AccessFlags.Write);

                    builder.SetRenderFunc(static (CompositePassData d, RasterGraphContext ctx) =>
                    {
                        MaterialPropertyBlock block = d.block;
                        block.Clear();
                        d.uniforms.ApplyTo(block);
                        block.SetVector(Ids.Orientation, Orientation(ctx, d.color));
                        block.SetTexture(Ids.SceneColor, d.color);
                        block.SetTexture(Ids.SurfaceId, d.surfaceId);
                        block.SetTexture(Ids.CellColor, d.cellColor);
                        block.SetTexture(Ids.CellGlyph, d.cellGlyph);
                        block.SetTexture(Ids.GlyphAtlas, d.atlas);
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, k_PassComposite, MeshTopology.Triangles, 3, 1, block);
                    });
                }

                // Later passes (post-processing) now read the ASCII image.
                resourceData.cameraColor = output;
            }

            static TextureHandle CreateCellTexture(RenderGraph renderGraph, int w, int h, string name)
            {
                var desc = new TextureDesc(w, h)
                {
                    name = name,
                    format = GraphicsFormat.R16G16B16A16_SFloat,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    clearBuffer = false,
                };
                return renderGraph.CreateTexture(desc);
            }

            void FillUniforms(Camera camera, int width, int height, int cellSize, int cellsX, int cellsY)
            {
                AsciiSettings s = m_Settings;
                Vector2Int offset = Vector2Int.zero;
                Vector2Int origin = Vector2Int.zero;
                if (camera != null && camera.cameraType == CameraType.Game && camera.TryGetComponent(out AsciiCamera asciiCamera))
                {
                    offset = asciiCamera.SubCellOffsetPixels;
                    origin = asciiCamera.CellOrigin;
                }

                var u = m_Uniforms;
                u.cell = new Vector4(cellSize, 1f / cellSize, cellsX, cellsY);
                u.source = new Vector4(width, height, 1f / width, 1f / height);
                u.offset = new Vector4(offset.x, offset.y, origin.x, origin.y);
                u.tone = new Vector4(s.exposure.value, s.contrast.value, s.gamma.value, s.dither.value);
                u.color = new Vector4(s.glyphBrightness.value, s.backgroundFill.value, s.saturation.value, s.colorNormalize.value);
                u.edge = new Vector4(s.edgeThreshold.value, s.depthEdgeSensitivity.value, s.normalEdgeSensitivity.value, s.edges.value ? 1f : 0f);
                u.misc = new Vector4(Time.time, s.waterRippleSpeed.value, s.exemptOutline.value, s.intensity.value);
                u.outlineColor = s.exemptOutlineColor.value;
                u.debug = new Vector4((int)AsciiRuntimeOverrides.DebugView, 0f, 0f, 0f);

                // Per-class table: x = ASCII amount, y = atlas row, z = edges on, w = dither scale.
                for (int i = 0; i < u.classParams.Length; i++)
                    u.classParams[i] = new Vector4(1f, i, 1f, 1f);
                u.classParams[(int)AsciiSurfaceClass.Default] = new Vector4(1f, 0f, 1f, 1f);
                u.classParams[(int)AsciiSurfaceClass.Exempt] = new Vector4(s.exemptAscii.value, 1f, 0f, 1f);
                u.classParams[(int)AsciiSurfaceClass.Foliage] = new Vector4(1f, 2f, 1f, 1.25f);
                u.classParams[(int)AsciiSurfaceClass.Water] = new Vector4(1f, 3f, 0f, 0.6f);
                u.classParams[(int)AsciiSurfaceClass.Stone] = new Vector4(1f, 4f, 1f, 1f);
                u.classParams[(int)AsciiSurfaceClass.Emissive] = new Vector4(s.emissiveAscii.value, 5f, 0f, 0.5f);
                u.classParams[(int)AsciiSurfaceClass.Grass] = new Vector4(1f, 6f, 1f, 1.4f);
                u.classParams[(int)AsciiSurfaceClass.Wood] = new Vector4(1f, 7f, 1f, 0.6f);
                u.classParams[AsciiSurfaceLayers.SkyClass] = new Vector4(1f, AsciiSurfaceLayers.SkyClass, 0f, 0.5f);
            }
        }
    }
}
