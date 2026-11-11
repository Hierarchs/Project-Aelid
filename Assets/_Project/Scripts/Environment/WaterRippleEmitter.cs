using UnityEngine;

namespace Aelid.Environment
{
    /// <summary>
    /// Emits ripples while this transform (the feet of a character) is in water:
    /// a splash on entering, one ring per stride, and soft rings while standing.
    /// Purely presentational; place it on any wading character.
    /// </summary>
    [AddComponentMenu("Aelid/Environment/Water Ripple Emitter")]
    public sealed class WaterRippleEmitter : MonoBehaviour
    {
        [Tooltip("Metres walked between ripples.")] public float strideSpacing = 0.7f;
        [Tooltip("Seconds between soft ripples while standing still.")] public float idleInterval = 1.4f;
        public float strength = 1f;
        [Tooltip("How deep the feet must be before ripples start (m).")] public float minDepth = 0.03f;

        Vector3 m_LastPosition;
        float m_Travelled;
        float m_NextIdle;
        bool m_InWater;

        public bool InWater => m_InWater;

        void OnEnable() => m_LastPosition = transform.position;

        void Update()
        {
            Vector3 p = transform.position;
            if (!WaterBody.TryGetSurface(p, out float surface) || surface - p.y < minDepth)
            {
                m_InWater = false;
                m_LastPosition = p;
                return;
            }

            var at = new Vector3(p.x, surface, p.z);
            float now = WaterRipples.Now;
            if (!m_InWater)
            {
                WaterRipples.Emit(at, strength * 1.3f); // entering splash
                m_InWater = true;
                m_Travelled = 0f;
                m_NextIdle = now + idleInterval;
            }
            else
            {
                m_Travelled += new Vector2(p.x - m_LastPosition.x, p.z - m_LastPosition.z).magnitude;
                if (m_Travelled >= strideSpacing)
                {
                    WaterRipples.Emit(at, strength);
                    m_Travelled = 0f;
                    m_NextIdle = now + idleInterval;
                }
                else if (now >= m_NextIdle)
                {
                    WaterRipples.Emit(at, strength * 0.5f);
                    m_NextIdle = now + idleInterval;
                }
            }
            m_LastPosition = p;
        }
    }
}
