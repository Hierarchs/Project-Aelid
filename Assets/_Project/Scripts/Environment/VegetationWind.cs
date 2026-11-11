using System.Collections.Generic;
using UnityEngine;

namespace Aelid.Environment
{
    /// <summary>
    /// Moves visual foliage while leaving trunks, colliders, and source meshes in place.
    /// </summary>
    sealed class VegetationWind
    {
        readonly List<Plant> m_Plants = new();
        readonly MeshFilter m_Crops;
        readonly Mesh m_SourceMesh;
        readonly Mesh m_CropMesh;
        readonly Vector3[] m_RestVertices;
        readonly Vector3[] m_WindVertices;
        readonly float[] m_CropWeights;
        readonly float[] m_CropPhases;
        float m_NextCropUpdate;

        readonly struct Plant
        {
            public readonly Transform Transform;
            public readonly Quaternion RestRotation;
            public readonly float Phase;
            public readonly float BendDegrees;

            public Plant(Transform transform, float bendDegrees)
            {
                Transform = transform;
                RestRotation = transform.localRotation;
                Phase = PhaseAt(transform.position);
                BendDegrees = bendDegrees;
            }
        }

        public VegetationWind(Transform trees, Transform bushes, MeshFilter crops)
        {
            if (trees != null)
                foreach (Transform tree in trees)
                {
                    var crown = tree.Find("Crown");
                    if (crown != null)
                        m_Plants.Add(new Plant(crown, 8f));
                }

            if (bushes != null)
                foreach (Transform bush in bushes)
                    m_Plants.Add(new Plant(bush, 18f));

            if (crops == null || crops.sharedMesh == null || !crops.sharedMesh.isReadable)
                return;

            m_Crops = crops;
            m_SourceMesh = crops.sharedMesh;
            m_CropMesh = Object.Instantiate(m_SourceMesh);
            m_CropMesh.name = m_SourceMesh.name + " (Wind)";
            m_CropMesh.MarkDynamic();
            crops.sharedMesh = m_CropMesh;

            m_RestVertices = m_SourceMesh.vertices;
            m_WindVertices = new Vector3[m_RestVertices.Length];
            m_CropWeights = new float[m_RestVertices.Length];
            m_CropPhases = new float[m_RestVertices.Length];
            Bounds bounds = m_SourceMesh.bounds;
            for (int i = 0; i < m_RestVertices.Length; i++)
            {
                Vector3 vertex = m_RestVertices[i];
                float height = Mathf.InverseLerp(bounds.min.y, bounds.max.y, vertex.y);
                m_CropWeights[i] = height * height;
                m_CropPhases[i] = PhaseAt(crops.transform.TransformPoint(vertex));
            }
        }

        public void Apply(float seconds, Vector2 direction, float strength)
        {
            Vector3 worldDirection = new(direction.x, 0f, direction.y);
            Vector3 axis = Vector3.Cross(Vector3.up, worldDirection);
            float time = seconds * 3.4f;
            foreach (var plant in m_Plants)
            {
                if (plant.Transform == null)
                    continue;

                var parent = plant.Transform.parent;
                Vector3 localAxis = parent != null ? parent.InverseTransformDirection(axis) : axis;
                float bend = Sway(time, plant.Phase) * strength * plant.BendDegrees;
                plant.Transform.localRotation = Quaternion.AngleAxis(bend, localAxis) * plant.RestRotation;
            }

            // The field is one combined flat-shaded mesh. Update a private copy
            // at a lower rate than transform-driven bushes and crowns.
            if (m_Crops == null || m_CropMesh == null || seconds < m_NextCropUpdate)
                return;

            m_NextCropUpdate = seconds + 1f / 24f;
            Vector3 cropWind = m_Crops.transform.InverseTransformVector(worldDirection * (strength * 0.36f));
            for (int i = 0; i < m_RestVertices.Length; i++)
                m_WindVertices[i] = m_RestVertices[i] + cropWind * (m_CropWeights[i] * Sway(time, m_CropPhases[i]));
            m_CropMesh.SetVertices(m_WindVertices);
            m_CropMesh.RecalculateNormals();
            m_CropMesh.RecalculateBounds();
        }

        public void Dispose()
        {
            foreach (var plant in m_Plants)
                if (plant.Transform != null)
                    plant.Transform.localRotation = plant.RestRotation;
            if (m_Crops != null && m_Crops.sharedMesh == m_CropMesh)
                m_Crops.sharedMesh = m_SourceMesh;
            if (m_CropMesh != null)
                Object.Destroy(m_CropMesh);
        }

        static float PhaseAt(Vector3 position) => position.x * 0.24f + position.z * 0.19f;

        static float Sway(float time, float phase) => 0.55f + 0.45f * Mathf.Sin(time - phase);
    }
}
