using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Aelid.Environment
{
    /// <summary>
    /// Drifting cloud shadows. Generates a tiling cloud texture and sets it as the
    /// celestial light's URP light cookie, so only direct sun (or moon) light is
    /// dimmed and ambient light is left alone. The cookie is projected in world
    /// space, so shadows stay locked to the ground and glyphs stay stable while
    /// the camera moves. In play mode the pattern drifts with <see cref="wind"/>;
    /// in edit mode (and therefore in batch captures) it holds still.
    /// <see cref="coverage"/> is the hook for a future weather controller.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class CloudShadows : MonoBehaviour
    {
        public const int Resolution = 256;

        [Tooltip("The sun/moon light (DayNightCycle's celestial light).")]
        public Light targetLight;

        [Tooltip("Fraction of the ground under cloud shadow.")]
        [Range(0f, 1f)] public float coverage = 0.4f;

        [Tooltip("Width of the shadow edge. Low is crisp, high is soft.")]
        [Range(0.01f, 0.5f)] public float softness = 0.08f;

        [Tooltip("How much direct light a cloud blocks. Ambient light is never affected.")]
        [Range(0f, 1f)] public float opacity = 0.75f;

        [Tooltip("Metres before the cloud pattern repeats.")]
        [Min(16f)] public float tileSize = 100f;

        [Tooltip("Cloud drift in metres per second over the ground (X, Z).")]
        public Vector2 wind = new(2.4f, 1.2f);

        public int seed = 11;

        Texture2D m_Texture;
        float[] m_Noise;
        int m_NoiseSeed;
        (float coverage, float softness, float opacity) m_Built = (-1f, -1f, -1f);
        Vector2 m_Offset;
        float m_LastTime;

        /// <summary>The generated cookie (null until enabled).</summary>
        public Texture2D Texture => m_Texture;

        void OnEnable()
        {
            m_LastTime = CurrentTime();
            m_Offset = Vector2.zero;
            RenderPipelineManager.beginContextRendering += OnBeginRendering;
            Apply();
        }

        void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginRendering;
            if (targetLight != null && targetLight.cookie == m_Texture)
                targetLight.cookie = null;
            if (m_Texture != null)
            {
                if (Application.isPlaying) Destroy(m_Texture);
                else DestroyImmediate(m_Texture);
            }
            m_Texture = null;
            m_Built = (-1f, -1f, -1f);
        }

        void OnBeginRendering(ScriptableRenderContext context, List<Camera> cameras) => Apply();

        static float CurrentTime() => Application.isPlaying ? Time.time : 0f;

        /// <summary>Refreshes the texture if a setting changed, assigns the cookie, and advances the drift.</summary>
        public void Apply()
        {
            if (targetLight == null)
                return;

            if (m_Texture == null)
            {
                m_Texture = new Texture2D(Resolution, Resolution, TextureFormat.R8, false, true)
                {
                    name = "Cloud Shadows (Runtime)",
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave,
                };
                m_Built = (-1f, -1f, -1f);
            }
            if (m_Noise == null || m_NoiseSeed != seed)
            {
                m_Noise = TileableNoise(seed, Resolution);
                m_NoiseSeed = seed;
                m_Built = (-1f, -1f, -1f);
            }
            if (m_Built != (coverage, softness, opacity))
            {
                var cookie = Cookie(m_Noise, coverage, softness, opacity);
                var bytes = new byte[cookie.Length];
                for (int i = 0; i < cookie.Length; i++)
                    bytes[i] = (byte)Mathf.RoundToInt(Mathf.Clamp01(cookie[i]) * 255f);
                m_Texture.SetPixelData(bytes, 0);
                m_Texture.Apply(false, false);
                m_Built = (coverage, softness, opacity);
            }

            // Drift: move the pattern by the wind, expressed in the light's own axes
            // (directional cookies are projected along the light).
            float now = CurrentTime();
            float dt = Mathf.Max(0f, now - m_LastTime);
            m_LastTime = now;
            Transform t = targetLight.transform;
            var drift = new Vector3(wind.x, 0f, wind.y) * dt;
            m_Offset += new Vector2(Vector3.Dot(drift, t.right), Vector3.Dot(drift, t.up));
            m_Offset.x = Mathf.Repeat(m_Offset.x, tileSize);
            m_Offset.y = Mathf.Repeat(m_Offset.y, tileSize);

            targetLight.cookie = m_Texture;
            var data = targetLight.GetUniversalAdditionalLightData();
            data.lightCookieSize = new Vector2(tileSize, tileSize);
            data.lightCookieOffset = m_Offset;
        }

        /// <summary>
        /// Light multiplier per texel: 1 in clear sky, 1 - opacity under full cloud.
        /// The threshold is a percentile of the noise, so coverage is the shaded fraction.
        /// </summary>
        public static float[] Cookie(float[] noise, float coverage, float softness, float opacity)
        {
            var sorted = (float[])noise.Clone();
            Array.Sort(sorted);
            float threshold = coverage <= 0f ? float.MaxValue
                : coverage >= 1f ? float.MinValue
                : sorted[Mathf.Clamp(Mathf.RoundToInt((1f - coverage) * (sorted.Length - 1)), 0, sorted.Length - 1)];
            float half = Mathf.Max(softness, 1e-3f) * 0.5f;

            var cookie = new float[noise.Length];
            for (int i = 0; i < noise.Length; i++)
            {
                float cloud = coverage <= 0f ? 0f : coverage >= 1f ? 1f
                    : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(threshold - half, threshold + half, noise[i]));
                cookie[i] = 1f - opacity * cloud;
            }
            return cookie;
        }

        /// <summary>
        /// Seamlessly tiling fBm value noise in [0, 1], row-major. Five octaves on
        /// lattices of 4 to 64 cells, so the largest clouds are a quarter tile wide.
        /// </summary>
        public static float[] TileableNoise(int seed, int resolution)
        {
            var noise = new float[resolution * resolution];
            float amplitude = 0.5f;
            for (int octave = 0; octave < 5; octave++)
            {
                int period = 4 << octave;
                for (int y = 0; y < resolution; y++)
                {
                    float fy = (float)y / resolution * period;
                    int y0 = Mathf.FloorToInt(fy);
                    float ty = Smooth(fy - y0);
                    for (int x = 0; x < resolution; x++)
                    {
                        float fx = (float)x / resolution * period;
                        int x0 = Mathf.FloorToInt(fx);
                        float tx = Smooth(fx - x0);
                        float a = Lattice(seed, octave, x0, y0, period), b = Lattice(seed, octave, x0 + 1, y0, period);
                        float c = Lattice(seed, octave, x0, y0 + 1, period), d = Lattice(seed, octave, x0 + 1, y0 + 1, period);
                        noise[y * resolution + x] += amplitude * Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
                    }
                }
                amplitude *= 0.5f;
            }

            float min = float.MaxValue, max = float.MinValue;
            foreach (float v in noise)
            {
                min = Mathf.Min(min, v);
                max = Mathf.Max(max, v);
            }
            float range = Mathf.Max(max - min, 1e-6f);
            for (int i = 0; i < noise.Length; i++)
                noise[i] = (noise[i] - min) / range;
            return noise;
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        /// <summary>Hashed lattice value in [0, 1), wrapped so the noise tiles.</summary>
        static float Lattice(int seed, int octave, int x, int y, int period)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u;
                h ^= (uint)octave * 0x85EBCA77u;
                h ^= (uint)((x % period + period) % period) * 0xC2B2AE3Du;
                h ^= (uint)((y % period + period) % period) * 0x27D4EB2Fu;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }
    }
}
