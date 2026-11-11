using System;
using UnityEngine;

namespace Aelid.Environment
{
    /// <summary>
    /// Organic firelight flicker (intensity + slight positional wobble).
    /// Intensity = baseIntensity x externalScale x flicker, so other systems
    /// (e.g. NightLight) scale it without fighting over Light.intensity.
    /// </summary>
    [RequireComponent(typeof(Light))]
    [AddComponentMenu("Aelid/Environment/Flicker Light")]
    public sealed class FlickerLight : MonoBehaviour
    {
        public float baseIntensity = 4f;
        [Range(0f, 1f)] public float intensityVariation = 0.3f;
        public float speed = 7f;
        public float wobble = 0.05f;

        /// <summary>Multiplier owned by other systems (night fade, weather).</summary>
        [NonSerialized] public float externalScale = 1f;

        Light m_Light;
        Vector3 m_BasePosition;
        float m_Seed;

        void Awake()
        {
            m_Light = GetComponent<Light>();
            m_BasePosition = transform.localPosition;
            m_Seed = UnityEngine.Random.value * 100f;
        }

        void Update()
        {
            float t = Time.time * speed + m_Seed;
            float n = Mathf.PerlinNoise(t, m_Seed) * 0.7f + Mathf.PerlinNoise(t * 2.3f, m_Seed + 7f) * 0.3f;
            m_Light.intensity = baseIntensity * externalScale * (1f - intensityVariation + intensityVariation * 2f * n);
            m_Light.enabled = externalScale > 0.01f;
            transform.localPosition = m_BasePosition + new Vector3(
                (Mathf.PerlinNoise(t * 0.5f, 3f) - 0.5f) * wobble,
                (Mathf.PerlinNoise(t * 0.5f, 5f) - 0.5f) * wobble,
                (Mathf.PerlinNoise(t * 0.5f, 9f) - 0.5f) * wobble);
        }
    }
}
