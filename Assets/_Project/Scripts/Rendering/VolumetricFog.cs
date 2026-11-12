using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Aelid.Rendering
{
    /// <summary>
    /// Height fog with light shafts, rendered by <see cref="VolumetricFogFeature"/>
    /// before the ASCII pass. A VolumeComponent, so the day, night and weather
    /// volumes blend it. Morning mist adds density through the global
    /// <c>_AelidMorningMist</c> that DayNightCycle publishes.
    /// </summary>
    [Serializable, VolumeComponentMenu("Aelid/Volumetric Fog")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class VolumetricFog : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Master amount: scales all fog. 0 turns the pass off.")]
        public ClampedFloatParameter intensity = new(0f, 0f, 1f);

        [Header("Medium (extinction per metre)")]
        [Tooltip("Thin haze everywhere below Top. Carries the light shafts above the canopy.")]
        public MinFloatParameter density = new(0.01f, 0f);

        [Tooltip("Ground mist at Base Height. It thins out with height, so it pools in low ground.")]
        public MinFloatParameter heightDensity = new(0.06f, 0f);

        [Tooltip("World height (m) where ground mist is densest.")]
        public FloatParameter baseHeight = new(-1f);

        [Tooltip("Metres over which ground mist thins to about a third.")]
        public MinFloatParameter heightFalloff = new(2.5f, 0.1f);

        [Tooltip("World height (m) above which there is no fog. Hills that rise above it stand clear.")]
        public FloatParameter top = new(16f);

        [Tooltip("Extra ground mist at the peak of the morning mist (scaled by DayNightCycle's mist factor).")]
        public MinFloatParameter morningMistDensity = new(0.12f, 0f);

        [Tooltip("Breaks the fog into drifting banks. 0 = even.")]
        public ClampedFloatParameter noise = new(0.35f, 0f, 1f);

        [Tooltip("Typical size of a fog bank in metres.")]
        public MinFloatParameter noiseScale = new(14f, 1f);

        [Tooltip("Fog bank drift in metres per second (X, Z). Play mode only.")]
        public Vector2Parameter wind = new(new Vector2(0.5f, 0.25f));

        [Header("Light")]
        [Tooltip("Colour of the fog's scattering. White lets the light colour through.")]
        public ColorParameter tint = new(Color.white, false, false, true);

        [Tooltip("Forward scattering: positive values glow when looking towards the light.")]
        public ClampedFloatParameter anisotropy = new(0.3f, -0.9f, 0.9f);

        [Tooltip("Sun and moon light scattered by the fog. Shadows from trees and buildings cut it into shafts.")]
        public MinFloatParameter sunScattering = new(1f, 0f);

        [Tooltip("Sky light scattered by the fog. Keeps shadowed fog from going black.")]
        public MinFloatParameter ambientScattering = new(0.6f, 0f);

        [Tooltip("Lanterns, windows and fires scattered by the fog (halos).")]
        public MinFloatParameter localLightScattering = new(1f, 0f);

        [Tooltip("How much lanterns, windows and fires burn the fog off around them. 0 = no clearing.")]
        public ClampedFloatParameter lightClearing = new(0.85f, 0f, 1f);

        [Tooltip("Size of the clear pocket around each light, as a fraction of the light's range.")]
        public ClampedFloatParameter clearingRadius = new(0.5f, 0.05f, 1f);

        [Tooltip("Most of the scene the fog may hide, so characters stay readable.")]
        public ClampedFloatParameter maxOpacity = new(0.75f, 0f, 1f);

        [Header("Quality")]
        [Tooltip("Ray-march steps through the fog layer, at half resolution.")]
        public ClampedIntParameter steps = new(24, 8, 64);

        public bool IsActive() => active && intensity.value > 0f &&
            (density.value > 0f || heightDensity.value > 0f || morningMistDensity.value > 0f);
    }
}
