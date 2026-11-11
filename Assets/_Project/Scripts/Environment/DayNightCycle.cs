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
        public float moonIntensity = 0.75f;

        [Header("Ambient & fog (evaluated over 0-24h)")]
        public Gradient ambientSky;
        public Gradient ambientEquator;
        public Gradient ambientGround;
        public Gradient fogColor;
        public AnimationCurve fogDensity;

        [Header("Sky & post")]
        public Material skybox;
        [Tooltip("Volume whose weight follows the night factor (cold grading, vignette).")]
        public Volume nightVolume;

        /// <summary>0 in full daylight, 1 in full night.</summary>
        public float NightFactor { get; private set; }

        /// <summary>Sun elevation in degrees (negative below the horizon).</summary>
        public float SunElevation { get; private set; }

        public static DayNightCycle Active { get; private set; }

        static readonly int k_SkyExposure = Shader.PropertyToID("_Exposure");
        static readonly int k_SkyTint = Shader.PropertyToID("_SkyTint");

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
                Active = null;
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
            Vector3 towardMoon = -towardSun;

            SunElevation = Mathf.Asin(Mathf.Clamp(towardSun.y, -1f, 1f)) * Mathf.Rad2Deg;
            float moonElevation = -SunElevation;
            NightFactor = 1f - Smooth01(-6f, 6f, SunElevation);

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
                    celestialLight.transform.rotation = Quaternion.LookRotation(-towardMoon, Vector3.up);
                    celestialLight.color = moonColor;
                    celestialLight.intensity = moonIntensity * Smooth01(-1f, 7f, moonElevation);
                }
                celestialLight.shadows = celestialLight.intensity > 0.01f ? LightShadows.Soft : LightShadows.None;
                RenderSettings.sun = celestialLight;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            if (ambientSky != null) RenderSettings.ambientSkyColor = ambientSky.Evaluate(t01);
            if (ambientEquator != null) RenderSettings.ambientEquatorColor = ambientEquator.Evaluate(t01);
            if (ambientGround != null) RenderSettings.ambientGroundColor = ambientGround.Evaluate(t01);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            if (fogColor != null) RenderSettings.fogColor = fogColor.Evaluate(t01);
            if (fogDensity != null && fogDensity.length > 0) RenderSettings.fogDensity = fogDensity.Evaluate(timeOfDay);

            if (skybox != null)
            {
                RenderSettings.skybox = skybox;
                if (skybox.HasProperty(k_SkyExposure))
                    skybox.SetFloat(k_SkyExposure, Mathf.Lerp(1.1f, 0.08f, NightFactor));
                if (skybox.HasProperty(k_SkyTint))
                    skybox.SetColor(k_SkyTint, Color.Lerp(new Color(0.55f, 0.6f, 0.7f), new Color(0.12f, 0.15f, 0.3f), NightFactor));
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

            ambientSky = MakeGradient(
                (0.00f, new Color(0.14f, 0.18f, 0.34f)),
                (0.22f, new Color(0.22f, 0.20f, 0.38f)),
                (0.29f, new Color(0.66f, 0.54f, 0.56f)),
                (0.40f, new Color(0.55f, 0.68f, 0.90f)),
                (0.65f, new Color(0.55f, 0.68f, 0.90f)),
                (0.75f, new Color(0.74f, 0.52f, 0.46f)),
                (0.83f, new Color(0.22f, 0.20f, 0.38f)),
                (1.00f, new Color(0.14f, 0.18f, 0.34f)));

            ambientEquator = MakeGradient(
                (0.00f, new Color(0.08f, 0.10f, 0.20f)),
                (0.25f, new Color(0.16f, 0.14f, 0.24f)),
                (0.30f, new Color(0.60f, 0.44f, 0.34f)),
                (0.40f, new Color(0.46f, 0.50f, 0.48f)),
                (0.65f, new Color(0.46f, 0.50f, 0.48f)),
                (0.75f, new Color(0.66f, 0.42f, 0.30f)),
                (0.83f, new Color(0.13f, 0.12f, 0.24f)),
                (1.00f, new Color(0.08f, 0.10f, 0.20f)));

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
                (0.30f, new Color(0.85f, 0.66f, 0.52f)),
                (0.40f, new Color(0.72f, 0.82f, 0.92f)),
                (0.65f, new Color(0.72f, 0.82f, 0.92f)),
                (0.76f, new Color(0.88f, 0.52f, 0.36f)),
                (0.84f, new Color(0.10f, 0.10f, 0.18f)),
                (1.00f, new Color(0.05f, 0.07f, 0.13f)));

            fogDensity = new AnimationCurve(
                new Keyframe(0f, 0.016f),
                new Keyframe(5f, 0.018f),     // pre-dawn mist
                new Keyframe(6.5f, 0.010f),
                new Keyframe(7.5f, 0.005f),
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
