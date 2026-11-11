namespace Aelid.Rendering
{
    /// <summary>
    /// Surface classes the hybrid ASCII renderer distinguishes.
    /// Each non-default class N is stored on a renderer as Rendering Layer bit N
    /// (bit 0 stays URP's "Default" layer so lighting is unaffected).
    /// The class decides which glyph vocabulary a surface uses and how much
    /// ASCII conversion it receives (Exempt keeps the original 3D pixels).
    /// </summary>
    public enum AsciiSurfaceClass
    {
        Default = 0,
        Exempt = 1,   // player characters, important enemies: readable silhouettes
        Foliage = 2,  // trees, bushes
        Water = 3,
        Stone = 4,    // rocks, ruins, cliffs
        Emissive = 5, // fire, spells, lamps: reduced ASCII for clarity
        Grass = 6,    // grassland terrain
        Wood = 7,     // buildings, fences, timber
    }

    /// <remarks>
    /// Rendering Layers 1-7 are owned by ASCII classes (named in Tags and Layers).
    /// Bits 8+ stay free for URP light layers / decal layers. To add a class,
    /// raise MaxClass, AllClassBits and the matching mask in AsciiSurfaceId.shader.
    /// </remarks>
    public static class AsciiSurfaceLayers
    {
        /// <summary>Highest class index usable as a rendering layer bit.</summary>
        public const int MaxClass = 7;

        /// <summary>Class index reserved for pixels with no geometry (sky).</summary>
        public const int SkyClass = 15;

        /// <summary>Size of the per-class parameter table sent to the shader.</summary>
        public const int TableSize = 16;

        /// <summary>Rendering layer bits 1..7, the bits used to encode classes.</summary>
        public const uint AllClassBits = 0xFEu;

        public static uint BitFor(AsciiSurfaceClass surfaceClass)
        {
            int index = (int)surfaceClass;
            return index <= 0 || index > MaxClass ? 0u : 1u << index;
        }

        /// <summary>Returns the renderer's mask with its ASCII class replaced.</summary>
        public static uint Apply(uint renderingLayerMask, AsciiSurfaceClass surfaceClass)
        {
            return (renderingLayerMask & ~AllClassBits) | BitFor(surfaceClass) | 1u;
        }
    }
}
