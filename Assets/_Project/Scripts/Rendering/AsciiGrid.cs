using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Aelid.Rendering
{
    /// <summary>
    /// Shared cell-grid math so the camera snapper and the render pass always
    /// agree on cell size.
    /// </summary>
    public static class AsciiGrid
    {
        public const int ReferenceHeight = 1080;
        public const int MinCellSize = 4;
        public const int MaxCellSize = 64;

        public static AsciiSettings CurrentSettings =>
            VolumeManager.instance.stack != null ? VolumeManager.instance.stack.GetComponent<AsciiSettings>() : null;

        /// <summary>Size of the game camera's URP raster before upscaling.</summary>
        public static Vector2Int RasterSize(Camera camera)
        {
            float scale = camera.cameraType == CameraType.Game &&
                GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline
                ? pipeline.renderScale : 1f;
            // URP treats scales within 5% of native as 1x.
            if (Mathf.Abs(1f - scale) < 0.05f)
                scale = 1f;
            return new Vector2Int(Mathf.Max(1, (int)(camera.pixelWidth * scale)),
                Mathf.Max(1, (int)(camera.pixelHeight * scale)));
        }

        public static int ResolveCellSize(int targetHeight) => ResolveCellSize(CurrentSettings, targetHeight);

        public static int ResolveCellSize(AsciiSettings settings, int targetHeight)
        {
            int baseSize = settings != null ? settings.cellSize.value : 8;
            bool scale = settings == null || settings.scaleWithResolution.value;
            baseSize += AsciiRuntimeOverrides.CellSizeOffset;

            int multiplier = scale ? Mathf.Max(1, Mathf.RoundToInt(targetHeight / (float)ReferenceHeight)) : 1;
            return Mathf.Clamp(baseSize * multiplier, MinCellSize, MaxCellSize);
        }
    }
}
