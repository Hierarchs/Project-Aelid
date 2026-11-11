using UnityEngine;
using UnityEngine.Rendering;

namespace Aelid.Environment
{
    /// <summary>
    /// Keeps a bounded emitter near the player. Particles already in flight
    /// stay in world space as the player moves.
    /// </summary>
    sealed class WeatherPrecipitation
    {
        readonly ParticleSystem m_Particles;
        readonly Material m_Material;
        readonly float m_Rate;
        readonly float m_Drift;
        readonly float m_LeadTime;

        public WeatherPrecipitation(Transform parent, Shader shader, bool snow)
        {
            var emitter = new GameObject(snow ? "Snowfall" : "Rainfall");
            emitter.transform.SetParent(parent, false);
            m_Particles = emitter.AddComponent<ParticleSystem>();
            m_Particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = m_Particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = snow ? 2000 : 1400;
            main.startSpeed = 0f;
            main.startLifetime = snow
                ? new ParticleSystem.MinMaxCurve(3.5f, 5.2f)
                : new ParticleSystem.MinMaxCurve(0.95f, 1.35f);
            main.startSize = snow
                ? new ParticleSystem.MinMaxCurve(0.055f, 0.14f)
                : new ParticleSystem.MinMaxCurve(0.04f, 0.075f);
            m_Rate = snow ? 370f : 920f;
            m_Drift = snow ? 6.5f : 9f;
            m_LeadTime = snow ? 2.5f : 0.55f;

            var emission = m_Particles.emission;
            emission.rateOverTime = 0f;

            var shape = m_Particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(44f, 0.25f, 44f);

            var velocity = m_Particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.y = snow
                ? new ParticleSystem.MinMaxCurve(-4.2f, -2.8f)
                : new ParticleSystem.MinMaxCurve(-20f, -15f);

            m_Material = new Material(shader);
            m_Material.SetColor("_WeatherTint", snow
                ? new Color(1.05f, 1.25f, 1.55f, 0.90f)
                : new Color(0.62f, 0.81f, 1.05f, 0.74f));

            var renderer = emitter.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = m_Material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.renderMode = snow ? ParticleSystemRenderMode.Billboard : ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 1.5f;
            renderer.velocityScale = 0.038f;
        }

        public void Apply(Vector3 target, float intensity, Vector2 wind)
        {
            var drift = new Vector3(wind.x, 0f, wind.y) * m_Drift;
            m_Particles.transform.position = target + Vector3.up * 9f - drift * m_LeadTime;

            var emission = m_Particles.emission;
            emission.rateOverTime = m_Rate * intensity;

            var velocity = m_Particles.velocityOverLifetime;
            velocity.x = drift.x;
            velocity.z = drift.z;

            if (intensity > 0f && !m_Particles.isPlaying)
                m_Particles.Play();
            else if (intensity <= 0f && m_Particles.isPlaying && m_Particles.particleCount == 0)
                m_Particles.Stop();
        }

        public void Dispose()
        {
            if (m_Particles != null)
            {
                m_Particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                Object.Destroy(m_Particles.gameObject);
            }
            Object.Destroy(m_Material);
        }
    }
}
