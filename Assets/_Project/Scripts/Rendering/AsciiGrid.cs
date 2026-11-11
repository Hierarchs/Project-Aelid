using UnityEngine;
using UnityEngine.Rendering;

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
