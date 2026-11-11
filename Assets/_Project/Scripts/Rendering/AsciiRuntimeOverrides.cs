namespace Aelid.Rendering
{
    /// <summary>
    /// Process-wide switches for debugging and player options. Volume settings
    /// stay the source of truth for the look; these only gate or nudge it.
    /// </summary>
    public static class AsciiRuntimeOverrides
    {
        /// <summary>When false the ASCII pass is skipped entirely (raw 3D view).</summary>
        public static bool Enabled = true;

        /// <summary>Added to the volume's cell size (density). 0 = no change.</summary>
        public static int CellSizeOffset;

        /// <summary>Diagnostic output of the composite pass.</summary>
        public static AsciiDebugView DebugView = AsciiDebugView.None;
    }

    public enum AsciiDebugView
    {
        None = 0,
        ScenePassthrough = 1, // input scene color only (checks bindings/post)
        CellColor = 2,        // per-cell average color
        SurfaceClass = 3,     // per-pixel class, color coded
        GlyphColor = 4,       // per-cell glyph color as solid blocks
        GlyphAtlas = 5,       // tiles the whole atlas over the screen (orientation check)
    }
}
