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

        [Header("Color")]
        [Tooltip("Brightness multiplier for glyph strokes (compensates for glyph coverage).")]
        public ClampedFloatParameter glyphBrightness = new(1.5f, 0.25f, 6f);

        [Tooltip("How much of the cell color fills the space behind the glyph.")]
        public ClampedFloatParameter backgroundFill = new(0.22f, 0f, 1f);

        [Tooltip("Saturation of glyph colors.")]
        public ClampedFloatParameter saturation = new(1.25f, 0f, 2.5f);

        [Tooltip("Pushes dark glyph colors toward a common brightness so hue stays readable in shadow.")]
        public ClampedFloatParameter colorNormalize = new(0.3f, 0f, 1f);

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
