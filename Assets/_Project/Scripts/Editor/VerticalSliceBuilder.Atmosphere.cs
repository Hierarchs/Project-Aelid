using Aelid.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aelid.EditorTools
{
    // Volumetric fog settings for the slice's day and night volume profiles.
    // Morning mist needs no profile of its own: the shader reads DayNightCycle's
    // _AelidMorningMist and adds Morning Mist Density on top.
    public static partial class VerticalSliceBuilder
    {
        [MenuItem("Aelid/Setup/Add Volumetric Fog to Slice Profiles")]
        static void AddVolumetricFogMenu()
        {
            AddVolumetricFog();
            Debug.Log("[Aelid] Volumetric fog added to the day and night volume profiles.");
        }

        /// <summary>
        /// Registers the fog renderer feature and adds fog to the existing day and night
        /// profiles in place (same assets, so scenes that use them need no change).
        /// Batch-mode entry point too.
        /// </summary>
        public static void AddVolumetricFog()
        {
            ProjectSetup.Apply();
            var day = AssetDatabase.LoadAssetAtPath<VolumeProfile>($"{SettingsDir}/VolumeProfile_Base.asset");
            var night = AssetDatabase.LoadAssetAtPath<VolumeProfile>($"{SettingsDir}/VolumeProfile_Night.asset");
            if (day == null || night == null)
                throw new System.InvalidOperationException($"Day or night volume profile missing in {SettingsDir}.");
            ConfigureDayFog(FogOf(day));
            ConfigureNightFog(FogOf(night));
            EditorUtility.SetDirty(day);
            EditorUtility.SetDirty(night);
            AssetDatabase.SaveAssets();
        }

        static VolumetricFog FogOf(VolumeProfile profile) =>
            profile.TryGet(out VolumetricFog fog) ? fog : AddComponent<VolumetricFog>(profile);

        /// <summary>
        /// Daylight: fog lives at canopy height, where tree shadows cut it into shafts.
        /// Almost no haze above the trees (evenly lit haze would only veil the shafts).
        /// Morning mist thickens it until mid-morning.
        /// </summary>
        static void ConfigureDayFog(VolumetricFog fog)
        {
            fog.active = true;
            fog.intensity.Override(1f);
            fog.density.Override(0.001f);
            fog.heightDensity.Override(0.012f);
            fog.baseHeight.Override(-1f);
            fog.heightFalloff.Override(4f);
            fog.top.Override(14f);
            fog.morningMistDensity.Override(0.045f);
            fog.noise.Override(0.25f);
            fog.noiseScale.Override(14f);
            fog.wind.Override(new Vector2(0.5f, 0.25f));
            fog.tint.Override(new Color(1f, 0.97f, 0.92f));
            fog.anisotropy.Override(0.3f);
            fog.sunScattering.Override(0.8f);
            fog.ambientScattering.Override(0.35f);
            fog.localLightScattering.Override(0.35f);
            fog.lightClearing.Override(0.85f);
            fog.clearingRadius.Override(0.5f);
            fog.maxOpacity.Override(0.3f);
            fog.steps.Override(32);
        }

        /// <summary>
        /// Night (blended in by the night volume's weight): denser, cold ground fog with
        /// moonlight shafts and soft halos around lanterns and fires.
        /// </summary>
        static void ConfigureNightFog(VolumetricFog fog)
        {
            fog.active = true;
            fog.density.Override(0.004f);
            fog.heightDensity.Override(0.06f);
            fog.heightFalloff.Override(3.5f);
            fog.tint.Override(new Color(0.8f, 0.87f, 1f));
            fog.sunScattering.Override(2f);
            fog.ambientScattering.Override(0.5f);
            fog.localLightScattering.Override(0.35f);
            fog.maxOpacity.Override(0.6f);
        }
    }
}
