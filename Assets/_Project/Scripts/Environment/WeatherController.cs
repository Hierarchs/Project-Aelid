using System.Collections.Generic;
using UnityEngine;

namespace Aelid.Environment
{
    public enum WeatherKind
    {
        Clear,
        Rain,
        Thunderstorm,
        Snow,
        Snowstorm,
    }

    /// <summary>
    /// Blends weather independently of the world clock and shares the same
    /// wind with precipitation, mesh vegetation, and the ASCII grass blades.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    [AddComponentMenu("Aelid/Environment/Weather Controller")]
    public sealed class WeatherController : MonoBehaviour
    {
        public WeatherKind startingWeather = WeatherKind.Clear;
        [Min(0f)] public float transitionSeconds = 3f;
        [Tooltip("Seconds between automatic weather changes. Zero gives manual control.")]
        [Min(0f)] public float automaticChangeSeconds = 180f;
        public Vector2 windDirection = new(0.85f, 0.53f);

        [Header("Precipitation")]
        public Transform followTarget;
        public Shader precipitationShader;

        [Header("Vegetation")]
        public Transform trees;
        public Transform bushes;
        public MeshFilter crops;
        public Renderer[] groundSurfaces;

        public WeatherKind CurrentWeather { get; private set; }
        public float WindStrength { get; private set; }

        static readonly int k_Wind = Shader.PropertyToID("_AelidWeatherWind");
        static readonly int k_BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly Color k_SnowTint = new(0.73f, 0.82f, 0.91f);
        static readonly WeatherKind[] k_WeatherKinds = (WeatherKind[])System.Enum.GetValues(typeof(WeatherKind));

        readonly System.Random m_Random = new();
        VegetationWind m_Vegetation;
        WeatherPrecipitation m_Rain;
        WeatherPrecipitation m_Snow;
        GroundSurface[] m_Ground;
        Conditions m_From;
        Conditions m_To;
        Conditions m_Current;
        float m_Transition;
        float m_UntilChange;
        float m_UntilLightning;
        float m_Lightning;
        float m_LastRain;
        float m_LastSnow;

        readonly struct Conditions
        {
            public readonly float Rain, Snow, Wind, Cloud, Fog, Lightning;

            public Conditions(float rain, float snow, float wind, float cloud, float fog, float lightning = 0f)
            {
                Rain = rain;
                Snow = snow;
                Wind = wind;
                Cloud = cloud;
                Fog = fog;
                Lightning = lightning;
            }

            public static Conditions Blend(Conditions from, Conditions to, float t) => new(
                Mathf.Lerp(from.Rain, to.Rain, t),
                Mathf.Lerp(from.Snow, to.Snow, t),
                Mathf.Lerp(from.Wind, to.Wind, t),
                Mathf.Lerp(from.Cloud, to.Cloud, t),
                Mathf.Lerp(from.Fog, to.Fog, t),
                Mathf.Lerp(from.Lightning, to.Lightning, t));
        }

        sealed class GroundSurface
        {
            readonly Renderer m_Renderer;
            readonly Color m_BaseColor;
            readonly MaterialPropertyBlock m_Original = new();
            readonly MaterialPropertyBlock m_Weather = new();

            public GroundSurface(Renderer renderer)
            {
                m_Renderer = renderer;
                renderer.GetPropertyBlock(m_Original);
                renderer.GetPropertyBlock(m_Weather);
                m_BaseColor = m_Original.HasColor(k_BaseColor)
                    ? m_Original.GetColor(k_BaseColor)
                    : renderer.sharedMaterial.GetColor(k_BaseColor);
            }

            public void Apply(float rain, float snow)
            {
                if (m_Renderer == null)
                    return;

                if (rain <= 0f && snow <= 0f)
                {
                    Restore();
                    return;
                }

                Color wet = Color.Lerp(m_BaseColor, m_BaseColor * 0.7f, rain);
                m_Weather.SetColor(k_BaseColor, Color.Lerp(wet, k_SnowTint, snow * 0.85f));
                m_Renderer.SetPropertyBlock(m_Weather);
            }

            public void Restore()
            {
                if (m_Renderer != null)
                    m_Renderer.SetPropertyBlock(m_Original);
            }
        }

        static Conditions GetConditions(WeatherKind kind) => kind switch
        {
            WeatherKind.Rain => new(rain: 0.48f, snow: 0f, wind: 0.24f, cloud: 0.56f, fog: 0.008f),
            WeatherKind.Thunderstorm => new(rain: 1f, snow: 0f, wind: 0.95f, cloud: 0.9f, fog: 0.023f, lightning: 1f),
            WeatherKind.Snow => new(rain: 0f, snow: 0.38f, wind: 0.16f, cloud: 0.48f, fog: 0.012f),
            WeatherKind.Snowstorm => new(rain: 0f, snow: 1f, wind: 1f, cloud: 0.79f, fog: 0.042f),
            _ => new(rain: 0f, snow: 0f, wind: 0.05f, cloud: 0f, fog: 0f),
        };

        void OnEnable()
        {
            CurrentWeather = startingWeather;
            m_Current = m_From = m_To = GetConditions(startingWeather);
            m_Transition = 1f;
            m_UntilChange = automaticChangeSeconds;
            m_UntilLightning = 1.5f;
            m_Lightning = 0f;
            m_LastRain = m_LastSnow = -1f;

            m_Vegetation = new VegetationWind(trees, bushes, crops);
            if (precipitationShader != null && followTarget != null)
            {
                m_Rain = new WeatherPrecipitation(transform, precipitationShader, snow: false);
                m_Snow = new WeatherPrecipitation(transform, precipitationShader, snow: true);
            }

            var surfaces = new List<GroundSurface>();
            if (groundSurfaces != null)
            {
                foreach (var renderer in groundSurfaces)
                    if (renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty(k_BaseColor))
                        surfaces.Add(new GroundSurface(renderer));
            }
            m_Ground = surfaces.ToArray();
        }

        void OnDisable()
        {
            m_Vegetation?.Dispose();
            m_Rain?.Dispose();
            m_Snow?.Dispose();
            m_Vegetation = null;
            m_Rain = m_Snow = null;
            if (m_Ground != null)
                foreach (var surface in m_Ground)
                    surface.Restore();
            m_Ground = null;
            WindStrength = 0f;
            Shader.SetGlobalVector(k_Wind, Vector4.zero);
        }

        public void SetWeather(WeatherKind kind)
        {
            if (kind != CurrentWeather)
            {
                CurrentWeather = kind;
                m_From = m_Current;
                m_To = GetConditions(kind);
                m_Transition = transitionSeconds > 0f ? 0f : 1f;
                if (kind == WeatherKind.Thunderstorm)
                    m_UntilLightning = 1.5f;
            }
            m_UntilChange = automaticChangeSeconds;
        }

        public void CycleWeather() => SetWeather(k_WeatherKinds[((int)CurrentWeather + 1) % k_WeatherKinds.Length]);

        void Update()
        {
            float dt = Time.deltaTime;
            if (automaticChangeSeconds > 0f)
            {
                m_UntilChange -= dt;
                if (m_UntilChange <= 0f)
                {
                    int next = m_Random.Next(k_WeatherKinds.Length - 1);
                    if (next >= (int)CurrentWeather)
                        next++;
                    SetWeather(k_WeatherKinds[next]);
                }
            }

            m_Transition = transitionSeconds > 0f ? Mathf.Clamp01(m_Transition + dt / transitionSeconds) : 1f;
            m_Current = Conditions.Blend(m_From, m_To, Mathf.SmoothStep(0f, 1f, m_Transition));
            m_Lightning = Mathf.MoveTowards(m_Lightning, 0f, dt * 4f);
            if (CurrentWeather == WeatherKind.Thunderstorm)
            {
                m_UntilLightning -= dt;
                if (m_UntilLightning <= 0f)
                {
                    m_Lightning = 1f;
                    m_UntilLightning = 8f + (float)m_Random.NextDouble() * 7f;
                }
            }
        }

        void LateUpdate()
        {
            float seconds = Time.time;
            float gust = Mathf.PerlinNoise(seconds * 0.73f, 4.1f);
            WindStrength = m_Current.Wind * Mathf.Lerp(0.62f, 1f, gust);
            Vector2 direction = windDirection.normalized;
            if (direction == Vector2.zero)
                direction = Vector2.right;
            Vector2 wind = direction * WindStrength;
            Shader.SetGlobalVector(k_Wind, new Vector4(wind.x, wind.y, seconds, WindStrength));
            m_Vegetation.Apply(seconds, direction, WindStrength);

            if (followTarget != null)
            {
                m_Rain?.Apply(followTarget.position, m_Current.Rain, wind);
                m_Snow?.Apply(followTarget.position, m_Current.Snow, wind);
            }

            if (m_Current.Rain == m_LastRain && m_Current.Snow == m_LastSnow)
                return;
            foreach (var surface in m_Ground)
                surface.Apply(m_Current.Rain, m_Current.Snow);
            m_LastRain = m_Current.Rain;
            m_LastSnow = m_Current.Snow;
        }

        // DayNightCycle calls this after restoring its time-of-day baseline.
        internal void ApplyLighting(DayNightCycle cycle)
        {
            float cloud = m_Current.Cloud;
            if (cloud <= 0f)
                return;

            Color fog = Color.Lerp(new Color(0.47f, 0.55f, 0.64f), new Color(0.09f, 0.14f, 0.24f), cycle.NightFactor);
            RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, fog, cloud);
            RenderSettings.fogDensity += m_Current.Fog;
            RenderSettings.ambientSkyColor = Color.Lerp(RenderSettings.ambientSkyColor, fog * 0.65f, cloud * 0.65f);

            Light light = cycle.celestialLight;
            if (light != null)
            {
                float flash = m_Lightning * m_Current.Lightning;
                light.color = Color.Lerp(light.color, new Color(0.77f, 0.86f, 1f), Mathf.Max(cloud * 0.45f, flash));
                light.intensity = light.intensity * (1f - cloud * 0.7f) + flash * 3f;
            }
        }
    }
}
