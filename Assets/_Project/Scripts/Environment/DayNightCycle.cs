using UnityEngine;
using UnityEngine.Rendering;

namespace Aelid.Environment
{
    /// <summary>
    /// Central time-of-day controller: moves one celestial directional light
    /// (sun by day, moon by night), and drives ambient light, fog, skybox and a
    /// night post-processing volume. Everything lights the 3D scene *before*
    /// ASCII conversion, so the glyphs inherit the mood.
    ///
    /// URP shades only one main directional light with shadows, so sun and moon
    /// share a light; the hand-over happens at the horizon where both are dark.
    /// Weather will later layer on top of this controller rather than replace it.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Aelid/Environment/Day Night Cycle")]
    public sealed class DayNightCycle : MonoBehaviour
    {
        [Header("Time")]
        [Range(0f, 24f)] public float timeOfDay = 9f;
        [Tooltip("Real-time minutes for one full in-game day.")]
        [Min(0.1f)] public float dayLengthMinutes = 24f;
        public bool progressTime = true;
        [Tooltip("Debug multiplier on time progression.")]
        [Min(0f)] public float timeScale = 1f;

        [Header("Sky path")]
        public Light celestialLight;
        [Tooltip("Rotates the whole sun/moon path around the vertical axis (degrees).")]
        public float sunPathYaw = 20f;
        [Tooltip("Tilt of the sun's arc from vertical; noon elevation = 90 - tilt.")]
        [Range(0f, 60f)] public float sunPathTilt = 30f;

        [Header("Sun")]
        public Gradient sunColor;
        public float sunIntensity = 2.2f;

        [Header("Moon")]
        public Color moonColor = new(0.58f, 0.70f, 1f);
        public float moonIntensity = 0.85f;

        [Header("Ambient & fog (evaluated over 0-24h)")]
        public Gradient ambientSky;
        public Gradient ambientEquator;
        public Gradient ambientGround;
        public Gradient fogColor;
        public AnimationCurve fogDensity;

        [Header("Dawn")]
        public Color dawnTint = new(0.49f, 0.31f, 0.65f);
        [Tooltip("Fog density at the peak of the morning mist.")]
        [Min(0f)] public float dawnFogDensity = 0.015f;

        [Header("Sky & post")]
        public Material skybox;
        [Tooltip("Volume whose weight follows the night factor (cold grading, vignette).")]
        public Volume nightVolume;

        /// <summary>0 in full daylight, 1 in full night.</summary>
        public float NightFactor { get; private set; }

        /// <summary>Sun elevation in degrees (negative below the horizon).</summary>
        public float SunElevation { get; private set; }

        public Vector3 MoonDirection { get; private set; }
        public float MoonVisibility { get; private set; }

        public static DayNightCycle Active { get; private set; }

        Material m_SkyInstance;
        Material m_SkySource;

        static readonly int k_SkyExposure = Shader.PropertyToID("_Exposure");
        static readonly int k_SkyTint = Shader.PropertyToID("_SkyTint");
        static readonly int k_MorningMist = Shader.PropertyToID("_AelidMorningMist");

        void Reset() => AssignDefaultCurves();

        void OnEnable()
        {
            Active = this;
            if (sunColor == null || fogDensity == null || fogDensity.length == 0)
                AssignDefaultCurves();
            Apply();
        }

        void OnDisable()
        {
            if (Active == this)
            {
                Active = null;
                Shader.SetGlobalFloat(k_MorningMist, 0f);
            }
            if (Application.isPlaying && RenderSettings.skybox == m_SkyInstance && m_SkySource != null)
                RenderSettings.skybox = m_SkySource;
            DestroySkyInstance();
        }

        void DestroySkyInstance()
        {
            if (m_SkyInstance == null)
                return;
            if (Application.isPlaying) Destroy(m_SkyInstance);
            else DestroyImmediate(m_SkyInstance);
            m_SkyInstance = null;
        }

        void Update()
        {
            if (Application.isPlaying && progressTime && dayLengthMinutes > 0f)
            {
                timeOfDay += Time.deltaTime * timeScale * 24f / (dayLengthMinutes * 60f);
                timeOfDay = Mathf.Repeat(timeOfDay, 24f);
            }
            Apply();
        }

        void OnValidate() => Apply();

        public void SetTime(float hours)
        {
            timeOfDay = Mathf.Repeat(hours, 24f);
            Apply();
        }

        public void Apply()
        {
            float t01 = Mathf.Repeat(timeOfDay, 24f) / 24f;

            // Sun travels east (06:00) -> high in the south (12:00) -> west (18:00),
            // so midday light comes from behind the camera and lights the faces we see.
            float sunAngle = (Mathf.Repeat(timeOfDay, 24f) - 6f) / 12f * 180f;
            Vector3 towardSun = BodyDirection(sunAngle);
            MoonDirection = -towardSun;

            SunElevation = Mathf.Asin(Mathf.Clamp(towardSun.y, -1f, 1f)) * Mathf.Rad2Deg;
            float moonElevation = -SunElevation;
            NightFactor = 1f - Smooth01(-6f, 6f, SunElevation);
            MoonVisibility = Smooth01(0f, 7f, moonElevation);

            if (celestialLight != null)
            {
                // Hand-over at the horizon, where both bodies are dark.
                bool isDay = SunElevation > 0f;
                if (isDay)
                {
                    celestialLight.transform.rotation = Quaternion.LookRotation(-towardSun, Vector3.up);
                    celestialLight.color = sunColor != null ? sunColor.Evaluate(t01) : Color.white;
                    celestialLight.intensity = sunIntensity * Smooth01(-1f, 7f, SunElevation);
                }
                else
                {
                    celestialLight.transform.rotation = Quaternion.LookRotation(-MoonDirection, Vector3.up);
                    celestialLight.color = moonColor;
                    celestialLight.intensity = moonIntensity * Smooth01(-1f, 7f, moonElevation);
                }
                celestialLight.shadows = celestialLight.intensity > 0.01f ? LightShadows.Soft : LightShadows.None;
                RenderSettings.sun = celestialLight;
            }

            float dawn = Smooth01(4.8f, 6.1f, timeOfDay) * (1f - Smooth01(6.6f, 9.1f, timeOfDay));
            float mist = Smooth01(5f, 6.6f, timeOfDay) * (1f - Smooth01(7f, 10.5f, timeOfDay));
            RenderSettings.ambientMode = AmbientMode.Trilight;
            if (ambientSky != null) RenderSettings.ambientSkyColor = TintAtDawn(ambientSky.Evaluate(t01), 0.65f * dawn);
            if (ambientEquator != null) RenderSettings.ambientEquatorColor = TintAtDawn(ambientEquator.Evaluate(t01), 0.5f * dawn);
            if (ambientGround != null) RenderSettings.ambientGroundColor = TintAtDawn(ambientGround.Evaluate(t01), 0.35f * dawn);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            if (fogColor != null) RenderSettings.fogColor = Color.Lerp(fogColor.Evaluate(t01), dawnTint, dawn);
            if (fogDensity != null && fogDensity.length > 0)
                RenderSettings.fogDensity = Mathf.Lerp(fogDensity.Evaluate(timeOfDay), dawnFogDensity, mist);
            Shader.SetGlobalFloat(k_MorningMist, mist);

            if (skybox != null)
            {
                // Animate a runtime copy so the shared material asset is never modified.
                if (m_SkyInstance == null || m_SkySource != skybox)
                {
                    DestroySkyInstance();
                    m_SkySource = skybox;
                    m_SkyInstance = new Material(skybox) { name = skybox.name + " (Runtime)", hideFlags = HideFlags.DontSave };
                }
                RenderSettings.skybox = Application.isPlaying ? m_SkyInstance : skybox;
                if (m_SkyInstance.HasProperty(k_SkyExposure))
                    m_SkyInstance.SetFloat(k_SkyExposure, Mathf.Lerp(1.1f, 0.08f, NightFactor));
                if (m_SkyInstance.HasProperty(k_SkyTint))
                {
                    Color skyTint = Color.Lerp(new Color(0.55f, 0.6f, 0.7f), new Color(0.12f, 0.15f, 0.3f), NightFactor);
                    m_SkyInstance.SetColor(k_SkyTint, TintAtDawn(skyTint, dawn));
                }
            }

            if (nightVolume != null)
                nightVolume.weight = NightFactor;
        }

        /// <summary>Unit vector toward a body at <paramref name="angleDegrees"/> along the tilted sky arc (0 = east horizon).</summary>
        Vector3 BodyDirection(float angleDegrees)
        {
            float a = angleDegrees * Mathf.Deg2Rad;
            float tilt = sunPathTilt * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a) * Mathf.Cos(tilt), -Mathf.Sin(a) * Mathf.Sin(tilt));
            return Quaternion.Euler(0f, sunPathYaw, 0f) * dir;
        }

        static float Smooth01(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        Color TintAtDawn(Color color, float amount)
        {
            Color tint = dawnTint * (color.maxColorComponent / Mathf.Max(dawnTint.maxColorComponent, 0.001f));
            tint.a = color.a;
            return Color.Lerp(color, tint, amount);
        }

        /// <summary>Oblivion-bright days, Berserk-cold nights.</summary>
        public void AssignDefaultCurves()
        {
            sunColor = MakeGradient(
                (0.00f, new Color(1.00f, 0.45f, 0.25f)),
                (0.27f, new Color(1.00f, 0.55f, 0.28f)), // 06:30 dawn
                (0.33f, new Color(1.00f, 0.82f, 0.58f)), // 08:00 golden
                (0.42f, new Color(1.00f, 0.95f, 0.86f)),
                (0.58f, new Color(1.00f, 0.96f, 0.88f)),
                (0.69f, new Color(1.00f, 0.80f, 0.52f)), // 16:30
                (0.75f, new Color(1.00f, 0.50f, 0.24f)), // 18:00 sunset
                (1.00f, new Color(1.00f, 0.45f, 0.25f)));

            // Night ambient stays low so fires and lamps form distinct pools of light.
            ambientSky = MakeGradient(
                (0.00f, new Color(0.11f, 0.15f, 0.29f)),
                (0.22f, new Color(0.20f, 0.18f, 0.35f)),
                (0.29f, new Color(0.66f, 0.54f, 0.56f)),
                (0.40f, new Color(0.55f, 0.68f, 0.90f)),
                (0.65f, new Color(0.55f, 0.68f, 0.90f)),
                (0.75f, new Color(0.74f, 0.52f, 0.46f)),
                (0.83f, new Color(0.19f, 0.18f, 0.34f)),
                (1.00f, new Color(0.11f, 0.15f, 0.29f)));

            ambientEquator = MakeGradient(
                (0.00f, new Color(0.065f, 0.085f, 0.17f)),
                (0.25f, new Color(0.16f, 0.14f, 0.24f)),
                (0.30f, new Color(0.60f, 0.44f, 0.34f)),
                (0.40f, new Color(0.46f, 0.50f, 0.48f)),
                (0.65f, new Color(0.46f, 0.50f, 0.48f)),
                (0.75f, new Color(0.66f, 0.42f, 0.30f)),
                (0.83f, new Color(0.11f, 0.10f, 0.21f)),
                (1.00f, new Color(0.065f, 0.085f, 0.17f)));

            ambientGround = MakeGradient(
                (0.00f, new Color(0.04f, 0.05f, 0.09f)),
                (0.30f, new Color(0.20f, 0.16f, 0.12f)),
                (0.40f, new Color(0.24f, 0.22f, 0.16f)),
                (0.65f, new Color(0.24f, 0.22f, 0.16f)),
                (0.80f, new Color(0.08f, 0.06f, 0.08f)),
                (1.00f, new Color(0.03f, 0.04f, 0.07f)));

            fogColor = MakeGradient(
                (0.00f, new Color(0.05f, 0.07f, 0.13f)),
                (0.25f, new Color(0.22f, 0.20f, 0.30f)),
                (0.30f, new Color(0.57f, 0.62f, 0.68f)),
                (0.40f, new Color(0.72f, 0.82f, 0.92f)),
                (0.65f, new Color(0.72f, 0.82f, 0.92f)),
                (0.76f, new Color(0.88f, 0.52f, 0.36f)),
                (0.84f, new Color(0.10f, 0.10f, 0.18f)),
                (1.00f, new Color(0.05f, 0.07f, 0.13f)));

            fogDensity = new AnimationCurve(
                new Keyframe(0f, 0.016f),
                new Keyframe(5f, 0.020f),
                new Keyframe(6.6f, 0.023f),   // mist peaks shortly after sunrise
                new Keyframe(7.8f, 0.016f),
                new Keyframe(9f, 0.008f),
                new Keyframe(10.5f, 0.004f),
                new Keyframe(16f, 0.004f),
                new Keyframe(19.5f, 0.010f),
                new Keyframe(24f, 0.016f));
        }

        static Gradient MakeGradient(params (float time, Color color)[] keys)
        {
            var colorKeys = new GradientColorKey[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                colorKeys[i] = new GradientColorKey(keys[i].color, keys[i].time);
            var gradient = new Gradient();
            gradient.SetKeys(colorKeys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }
    }
}
