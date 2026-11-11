using UnityEngine;

namespace Aelid.Environment
{
    /// <summary>
    /// Animates a flat-shaded water mesh on the CPU (a few hundred vertices),
    /// so facets catch the light differently over time. The ASCII pass adds
    /// its own glyph ripple on top. Placeholder until a dedicated water shader
    /// with reflections lands.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [AddComponentMenu("Aelid/Environment/Water Waves")]
    public sealed class WaterWaves : MonoBehaviour
    {
        public float amplitude = 0.08f;
        public float wavelength = 4f;
        public float speed = 0.8f;

        Mesh m_Mesh;
        Vector3[] m_Base;
        Vector3[] m_Work;

        void Start()
        {
            var filter = GetComponent<MeshFilter>();
            m_Mesh = Instantiate(filter.sharedMesh);
            m_Mesh.name = filter.sharedMesh.name + " (Animated)";
            m_Mesh.MarkDynamic();
            filter.sharedMesh = m_Mesh;
            m_Base = m_Mesh.vertices;
            m_Work = new Vector3[m_Base.Length];
        }

        void Update()
        {
            if (m_Mesh == null)
                return;
            float t = Time.time * speed;
            float k = 2f * Mathf.PI / Mathf.Max(0.1f, wavelength);
            for (int i = 0; i < m_Base.Length; i++)
            {
                Vector3 v = m_Base[i];
                Vector3 w = transform.TransformPoint(v);
                v.y += amplitude * (Mathf.Sin(w.x * k + t) * 0.6f + Mathf.Sin((w.x * 0.4f + w.z) * k * 0.8f - t * 1.3f) * 0.4f);
                m_Work[i] = v;
            }
            m_Mesh.vertices = m_Work;
            m_Mesh.RecalculateNormals(); // vertices are unshared, so normals stay faceted
        }

        void OnDestroy()
        {
            if (m_Mesh != null)
                Destroy(m_Mesh);
        }
    }
}
