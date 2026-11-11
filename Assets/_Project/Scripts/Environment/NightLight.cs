using UnityEngine;

namespace Aelid.Environment
{
    /// <summary>
    /// Fades lights and emissive surfaces in as night falls (street lamps,
    /// lit windows, eerie ruins). Emission is driven per-renderer through a
    /// property block so materials stay shared. Lights with a FlickerLight are
    /// scaled through it instead of having their intensity overwritten.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Aelid/Environment/Night Light")]
    public sealed class NightLight : MonoBehaviour
    {
        public Light[] lights;
        [Tooltip("Full-night intensity for lights without a FlickerLight.")]
        public float intensity = 3.5f;
        public Renderer[] emissiveRenderers;
        [ColorUsage(false, true)] public Color emission = new(4f, 2.2f, 0.9f);
        [Tooltip("Fraction of brightness kept during the day.")]
        [Range(0f, 1f)] public float dayLevel = 0f;

        MaterialPropertyBlock m_Block;
        static readonly int k_EmissionColor = Shader.PropertyToID("_EmissionColor");

        void LateUpdate() => Refresh();

        /// <summary>Applies the current night level (also used by editor capture tools).</summary>
        public void Refresh()
        {
            var cycle = DayNightCycle.Active;
            float night = cycle != null ? cycle.NightFactor : 1f;
            float level = Mathf.Lerp(dayLevel, 1f, night);

            if (lights != null)
            {
                foreach (var light in lights)
                {
                    if (light == null) continue;
                    if (light.TryGetComponent(out FlickerLight flicker))
                    {
                        flicker.externalScale = level;
                        if (!Application.isPlaying)
                            light.intensity = flicker.baseIntensity * level;
                    }
                    else
                    {
                        light.intensity = intensity * level;
                    }
                    light.enabled = level > 0.01f;
                }
            }

            if (emissiveRenderers != null)
            {
                m_Block ??= new MaterialPropertyBlock();
                foreach (var r in emissiveRenderers)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(m_Block);
                    m_Block.SetColor(k_EmissionColor, emission * level);
                    r.SetPropertyBlock(m_Block);
                }
            }
        }
    }
}
