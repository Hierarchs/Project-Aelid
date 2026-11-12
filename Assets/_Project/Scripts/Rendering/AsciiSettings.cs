using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Aelid.Rendering
{
    /// <summary>
    /// Volume-driven look of the hybrid ASCII renderer. Because it is a
    /// VolumeComponent, day/night, weather and local areas can blend it.
    /// </summary>
    [Serializable, VolumeComponentMenu("Aelid/ASCII")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class AsciiSettings : VolumeComponent, IPostProcessComponent
    {
        [Header("Coverage")]
        [Tooltip("Blend between the raw 3D image (0) and full ASCII (1).")]
        public ClampedFloatParameter intensity = new(1f, 0f, 1f);

        [Tooltip("Cell size in pixels at 1080p. Multiples of 8 keep glyphs pixel-crisp.")]
        public ClampedIntParameter cellSize = new(8, 4, 32);

        [Tooltip("Scale the cell size by whole multiples of 1080p so density is resolution independent.")]
        public BoolParameter scaleWithResolution = new(true);

        [Header("Tone (glyph density)")]
        [Tooltip("Exposure applied before choosing glyph density.")]
        public ClampedFloatParameter exposure = new(1.6f, 0.1f, 8f);

        [Tooltip("Contrast of the luminance-to-glyph ramp.")]
        public ClampedFloatParameter contrast = new(1.15f, 0.25f, 3f);

        [Tooltip("Gamma of the luminance-to-glyph ramp (<1 = denser glyphs in shadows).")]
        public ClampedFloatParameter gamma = new(0.85f, 0.25f, 3f);

        [Tooltip("World-anchored dithering between adjacent glyph levels. Breaks up flat areas without flicker.")]
        public ClampedFloatParameter dither = new(0.7f, 0f, 2f);

        [Tooltip("Scales each surface's minimum glyph density. Above 0, shadows stay textured with dim glyphs " +
                 "instead of going empty, so light reads through glyph color rather than glyph count.")]
        public ClampedFloatParameter densityFloor = new(1f, 0f, 1.5f);

        [Header("Color")]
        [Tooltip("Brightness multiplier for glyph strokes (compensates for glyph coverage).")]
        public ClampedFloatParameter glyphBrightness = new(1.9f, 0.25f, 6f);

        [Tooltip("How much of the lit cell color fills the space behind the glyph.")]
        public ClampedFloatParameter backgroundFill = new(0.05f, 0f, 1f);

        [Tooltip("Minimum color behind the glyphs, in linear RGB.")]
        public ColorParameter backgroundColor = new(Color.black, false, false, true);

        [Tooltip("Saturation of glyph colors.")]
        public ClampedFloatParameter saturation = new(1.1f, 0f, 2.5f);

        [Tooltip("Pushes dark glyph colors toward a common brightness so hue stays readable in shadow.")]
        public ClampedFloatParameter colorNormalize = new(0f, 0f, 1f);

        [Tooltip("World-anchored per-glyph brightness variation, so dense fields read as texture.")]
        public ClampedFloatParameter colorJitter = new(0.28f, 0f, 0.6f);

        [Header("Sparkles (grass accents)")]
        [Tooltip("Fraction of grass cells that show a sparkle: flowers by day, fireflies by night.")]
        public ClampedFloatParameter sparkleDensity = new(0.025f, 0f, 0.2f);

        [Tooltip("Sparkle color. HDR values glow through bloom.")]
        public ColorParameter sparkleColor = new(new Color(1f, 0.96f, 0.86f, 1f), true, false, true);

        [Tooltip("0 = lit by the scene like a flower, 1 = emits its own light like a firefly.")]
        public ClampedFloatParameter sparkleSelfLit = new(0f, 0f, 1f);

        [Tooltip("Slow pulsing of sparkles (intended motion, e.g. fireflies).")]
        public ClampedFloatParameter sparkleTwinkle = new(0f, 0f, 1f);

        [Header("Edges")]
        [Tooltip("Replace glyphs on silhouettes and creases with line glyphs ( | - / \\ ).")]
        public BoolParameter edges = new(true);

        [Tooltip("Edge detection threshold. Lower = more line glyphs.")]
        public ClampedFloatParameter edgeThreshold = new(0.35f, 0.01f, 2f);

        [Tooltip("Sensitivity to depth discontinuities (silhouettes).")]
        public ClampedFloatParameter depthEdgeSensitivity = new(6f, 0f, 50f);

        [Tooltip("Sensitivity to normal discontinuities (creases, corners).")]
        public ClampedFloatParameter normalEdgeSensitivity = new(0.9f, 0f, 5f);

        [Header("Hybrid surfaces")]
        [Tooltip("ASCII amount applied to Exempt surfaces (characters). 0 keeps them fully 3D.")]
        public ClampedFloatParameter exemptAscii = new(0f, 0f, 1f);

        [Tooltip("ASCII amount applied to Emissive surfaces (fire, spells, lamps).")]
        public ClampedFloatParameter emissiveAscii = new(0.45f, 0f, 1f);

        [Tooltip("Outline drawn around Exempt silhouettes so characters read against dense glyphs.")]
        public ClampedFloatParameter exemptOutline = new(0.85f, 0f, 1f);

        public ColorParameter exemptOutlineColor = new(new Color(0.02f, 0.02f, 0.03f, 1f), false, false, true);

        [Header("Animation")]
        [Tooltip("Speed of the glyph ripple on Water surfaces. 0 = static.")]
        public ClampedFloatParameter waterRippleSpeed = new(1.2f, 0f, 6f);

        public bool IsActive() => active && intensity.value > 0f;
    }
}
