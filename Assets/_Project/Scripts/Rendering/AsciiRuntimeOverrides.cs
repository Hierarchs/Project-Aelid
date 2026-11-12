using UnityEngine;

namespace Aelid.Rendering
{
    /// <summary>
    /// Process-wide switches for debugging and player options. Volume settings
    /// stay the source of truth for the look; these only gate or nudge it.
    /// </summary>
    public static class AsciiRuntimeOverrides
    {
        static readonly int k_CleanBaseline = Shader.PropertyToID("_AelidAsciiCleanBaseline");

        /// <summary>When false the ASCII pass is skipped entirely (raw 3D view).</summary>
        public static bool Enabled = true;

        /// <summary>Added to the volume's cell size (density). 0 = no change.</summary>
        public static int CellSizeOffset;

        /// <summary>Fit two-color cells at strong boundaries and group glyphs within surfaces.</summary>
        public static bool FitShapes = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetShapeFitting() => FitShapes = true;

        /// <summary>Diagnostic output of the composite pass.</summary>
        public static AsciiDebugView DebugView = AsciiDebugView.None;

        /// <summary>Suppresses fine glyph and material detail for visual comparisons.</summary>
        public static bool CleanBaseline
        {
            get => Shader.GetGlobalFloat(k_CleanBaseline) > 0.5f;
            set => Shader.SetGlobalFloat(k_CleanBaseline, value ? 1f : 0f);
        }
    }

    public enum AsciiDebugView
    {
        None = 0,
        ScenePassthrough = 1, // input scene color only (checks bindings/post)
        CellColor = 2,        // per-cell average color
        SurfaceClass = 3,     // per-pixel class, color coded
        GlyphColor = 4,       // per-cell glyph color as solid blocks
        GlyphAtlas = 5,       // tiles the whole atlas over the screen (orientation check)
        CellSplits = 6,      // left/right in green, top/bottom in blue
    }
}
