using Aelid.Environment;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Aelid.Tests
{
    public class CloudShadowTests
    {
        const int Size = 64;

        static float MeanColumnStep(float[] noise, int from, int to)
        {
            float sum = 0f;
            for (int y = 0; y < Size; y++)
                sum += Mathf.Abs(noise[y * Size + from] - noise[y * Size + to]);
            return sum / Size;
        }

        static float MeanRowStep(float[] noise, int from, int to)
        {
            float sum = 0f;
            for (int x = 0; x < Size; x++)
                sum += Mathf.Abs(noise[from * Size + x] - noise[to * Size + x]);
            return sum / Size;
        }

        [Test]
        public void NoiseTilesWithoutASeam()
        {
            var noise = CloudShadows.TileableNoise(3, Size);
            float interior = 0f;
            for (int x = 0; x < Size - 1; x++)
                interior += MeanColumnStep(noise, x, x + 1);
            interior /= Size - 1;

            Assert.Less(MeanColumnStep(noise, Size - 1, 0), interior * 2.5f, "Left and right edges should continue each other.");
            Assert.Less(MeanRowStep(noise, Size - 1, 0), interior * 2.5f, "Top and bottom edges should continue each other.");
        }

        [Test]
        public void SameSeedGivesTheSameClouds()
        {
            Assert.AreEqual(CloudShadows.TileableNoise(5, Size), CloudShadows.TileableNoise(5, Size));
            Assert.AreNotEqual(CloudShadows.TileableNoise(5, Size), CloudShadows.TileableNoise(6, Size));
        }

        [TestCase(0.2f)]
        [TestCase(0.45f)]
        [TestCase(0.7f)]
        public void CoverageIsTheShadedFraction(float coverage)
        {
            var cookie = CloudShadows.Cookie(CloudShadows.TileableNoise(9, 128), coverage, 0.02f, 1f);
            int shaded = 0;
            foreach (float light in cookie)
                if (light < 0.5f)
                    shaded++;
            Assert.That(shaded / (float)cookie.Length, Is.EqualTo(coverage).Within(0.03f));
        }

        [Test]
        public void OpacityOnlyDimsDirectLightPartway()
        {
            var cookie = CloudShadows.Cookie(CloudShadows.TileableNoise(9, Size), 0.5f, 0.1f, 0.6f);
            float min = 1f, max = 0f;
            foreach (float light in cookie)
            {
                min = Mathf.Min(min, light);
                max = Mathf.Max(max, light);
            }
            Assert.That(max, Is.EqualTo(1f).Within(1e-4f), "Clear sky leaves the sun untouched.");
            Assert.That(min, Is.EqualTo(0.4f).Within(1e-3f), "Full cloud blocks 'opacity' of the direct light.");
        }

        [Test]
        public void AppliesAndRemovesTheLightCookie()
        {
            var go = new GameObject("Test Sun");
            try
            {
                var light = go.AddComponent<Light>();
                light.type = LightType.Directional;
                var clouds = go.AddComponent<CloudShadows>();
                clouds.targetLight = light;
                clouds.tileSize = 150f;
                clouds.Apply();

                Assert.IsNotNull(clouds.Texture);
                Assert.AreSame(clouds.Texture, light.cookie);
                Assert.AreEqual(TextureWrapMode.Repeat, clouds.Texture.wrapMode);
                Assert.AreEqual(new Vector2(150f, 150f), light.GetUniversalAdditionalLightData().lightCookieSize);

                clouds.enabled = false;
                Assert.IsNull(light.cookie, "Disabling cloud shadows should clear the cookie.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
