using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aelid.Environment
{
    /// <summary>
    /// A body of water whose surface is this transform's height. Answers
    /// "is this point in water, and where is the surface?" for gameplay
    /// (wading ripples now; swimming, footstep sounds, fire extinguishing later)
    /// and publishes ripple/time globals before rendering.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("Aelid/Environment/Water Body")]
    public sealed class WaterBody : MonoBehaviour
    {
        [Tooltip("Radius of a round body of water around this transform. 0 = use the renderer bounds.")]
        public float radius;

        static readonly List<WaterBody> s_Active = new();
        static int s_Subscribers;
        Renderer m_Renderer;

        public float SurfaceHeight => transform.position.y;

        void OnEnable()
        {
            m_Renderer = GetComponent<Renderer>();
            s_Active.Add(this);
            if (s_Subscribers++ == 0)
                RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
        }

        void OnDisable()
        {
            s_Active.Remove(this);
            if (--s_Subscribers == 0)
                RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
        }

        static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras) => WaterRipples.Publish();

        public bool ContainsXZ(Vector3 position)
        {
            Vector3 c = transform.position;
            if (radius > 0f)
                return new Vector2(position.x - c.x, position.z - c.z).sqrMagnitude <= radius * radius;
            if (m_Renderer == null)
                return false;
            Bounds b = m_Renderer.bounds;
            return position.x >= b.min.x && position.x <= b.max.x && position.z >= b.min.z && position.z <= b.max.z;
        }

        /// <summary>Surface height of the water containing <paramref name="position"/> (in XZ), if any.</summary>
        public static bool TryGetSurface(Vector3 position, out float height)
        {
            foreach (var body in s_Active)
            {
                if (body != null && body.ContainsXZ(position))
                {
                    height = body.SurfaceHeight;
                    return true;
                }
            }
            height = 0f;
            return false;
        }
    }
}
