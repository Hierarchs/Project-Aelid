using Aelid.Environment;
using NUnit.Framework;
using UnityEngine;

namespace Aelid.Tests
{
    public class WaterTests
    {
        const float Surface = -0.6f;

        GameObject m_CameraObject;
        GameObject m_Water;
        RenderTexture m_Target;
        Camera m_Source;
        PlanarReflection m_Reflection;

        [SetUp]
        public void SetUp()
        {
            m_Target = new RenderTexture(1920, 1080, 24);
            m_CameraObject = new GameObject("Test Game Camera");
            m_Source = m_CameraObject.AddComponent<Camera>();
            m_Source.orthographic = true;
            m_Source.orthographicSize = 7f;
            m_Source.targetTexture = m_Target;
            m_CameraObject.transform.SetPositionAndRotation(new Vector3(3f, 30f, -20f), Quaternion.Euler(52f, 0f, 0f));

            m_Water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            m_Water.transform.position = new Vector3(0f, Surface, 0f);
            m_Reflection = m_Water.AddComponent<PlanarReflection>();
            m_Reflection.sourceCamera = m_Source;
            Assert.IsTrue(m_Reflection.UpdateReflection());
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_Water);
            Object.DestroyImmediate(m_CameraObject);
            Object.DestroyImmediate(m_Target);
        }

        [Test]
        public void PointsOnTheSurface_MapToTheSameRow_MirroredColumn()
        {
            Camera mirror = m_Reflection.ReflectionCamera;
            for (int i = 0; i < 25; i++)
            {
                var onSurface = new Vector3(-6f + i * 0.5f, Surface, 2f + (i % 5) * 1.5f);
                Vector3 main = m_Source.WorldToViewportPoint(onSurface);
                Vector3 reflected = mirror.WorldToViewportPoint(onSurface);
                Assert.AreEqual(1f - main.x, reflected.x, 1e-3f, "Water shader samples u = 1 - u.");
                Assert.AreEqual(main.y, reflected.y, 1e-3f);
            }
        }

        [Test]
        public void ObjectsAboveWater_AppearWhereTheirMirrorImageWouldBe()
        {
            Camera mirror = m_Reflection.ReflectionCamera;
            var above = new Vector3(1.5f, 2.5f, 6f);
            var mirrorImage = new Vector3(above.x, 2f * Surface - above.y, above.z);
            Vector3 main = m_Source.WorldToViewportPoint(mirrorImage);
            Vector3 reflected = mirror.WorldToViewportPoint(above);
            Assert.AreEqual(1f - main.x, reflected.x, 1e-3f);
            Assert.AreEqual(main.y, reflected.y, 1e-3f);
        }

        [Test]
        public void MirrorCamera_IsAReflectionCamera_ThatSkipsTheWaterLayer()
        {
            Camera mirror = m_Reflection.ReflectionCamera;
            Assert.AreEqual(CameraType.Reflection, mirror.cameraType, "ASCII pass must skip the reflection camera.");
            Assert.AreEqual(0, mirror.cullingMask & (1 << 4), "Water must not reflect itself.");
            Assert.AreEqual(m_Reflection.Texture, mirror.targetTexture);
            Assert.Less(mirror.depth, m_Source.depth, "Reflection renders before the game camera.");
        }

        [Test]
        public void Ripples_ExpireAfterTheirLifetime()
        {
            WaterRipples.TimeOverride = 100f;
            try
            {
                WaterRipples.Clear();
                WaterRipples.Emit(Vector3.zero, 1f);
                WaterRipples.Emit(Vector3.one, 1f, age: 5f);
                Assert.AreEqual(1, WaterRipples.CountActive(2.6f));
            }
            finally
            {
                WaterRipples.Clear();
                WaterRipples.TimeOverride = null;
            }
        }
    }
}
