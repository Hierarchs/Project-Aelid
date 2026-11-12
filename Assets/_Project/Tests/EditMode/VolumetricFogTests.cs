using System.Linq;
using Aelid.Rendering;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Aelid.Tests
{
    public class VolumetricFogTests
    {
        [Test]
        public void ShaderCompilesWithoutErrors()
        {
            var shader = Shader.Find("Hidden/Aelid/VolumetricFog");
            Assert.IsNotNull(shader, "Hidden/Aelid/VolumetricFog not found.");
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "The volumetric fog shader has compile errors.");
            Assert.AreEqual(2, shader.passCount, "March and composite passes.");
        }

        [TestCase(0f)]
        [TestCase(0.3f)]
        [TestCase(-0.5f)]
        [TestCase(0.8f)]
        public void PhaseFunctionConservesEnergy(float g)
        {
            // Averaged over the sphere, the scaled Henyey-Greenstein phase is 1.
            const int steps = 20000;
            double sum = 0;
            for (int i = 0; i < steps; i++)
            {
                float mu = -1f + (i + 0.5f) * 2f / steps;
                sum += VolumetricFogFeature.Phase(mu, g) * (2.0 / steps);
            }
            Assert.That(sum / 2.0, Is.EqualTo(1.0).Within(0.01));
        }

        [Test]
        public void ForwardScatteringIsBrightestTowardsTheLight()
        {
            Assert.Greater(VolumetricFogFeature.Phase(1f, 0.3f), VolumetricFogFeature.Phase(0f, 0.3f));
            Assert.Greater(VolumetricFogFeature.Phase(0f, 0.3f), VolumetricFogFeature.Phase(-1f, 0.3f));
            Assert.That(VolumetricFogFeature.Phase(0.4f, 0f), Is.EqualTo(1f).Within(1e-5f), "Isotropic scattering is 1.");
        }

        [Test]
        public void LitLightsClearFogAroundThemselves()
        {
            Assert.That(VolumetricFogFeature.Clearing(0f, 10f, 0.5f, 1f, 0.85f), Is.EqualTo(0.85f).Within(1e-5f), "Full clearing at the light.");
            Assert.Greater(VolumetricFogFeature.Clearing(2f, 10f, 0.5f, 1f, 0.85f), VolumetricFogFeature.Clearing(4f, 10f, 0.5f, 1f, 0.85f));
            Assert.AreEqual(0f, VolumetricFogFeature.Clearing(5f, 10f, 0.5f, 1f, 0.85f), "Fog returns at the pocket's edge.");
            Assert.AreEqual(0f, VolumetricFogFeature.Clearing(0f, 10f, 0.5f, 0f, 0.85f), "A lamp that is off clears nothing.");
        }

        [Test]
        public void FogIsOffUntilAProfileTurnsItOn()
        {
            var fog = ScriptableObject.CreateInstance<VolumetricFog>();
            try
            {
                Assert.IsFalse(fog.IsActive(), "Default intensity is 0.");
                fog.intensity.Override(1f);
                Assert.IsTrue(fog.IsActive());
                fog.density.Override(0f);
                fog.heightDensity.Override(0f);
                fog.morningMistDensity.Override(0f);
                Assert.IsFalse(fog.IsActive(), "No medium, no pass.");
            }
            finally
            {
                Object.DestroyImmediate(fog);
            }
        }

        [TestCase("Assets/Settings/PC_Renderer.asset")]
        [TestCase("Assets/Settings/Mobile_Renderer.asset")]
        public void RendererHasFogAndAsciiFeatures(string path)
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            Assert.IsNotNull(data, path);
            var fog = data.rendererFeatures.OfType<VolumetricFogFeature>().SingleOrDefault();
            Assert.IsNotNull(fog, "Run Aelid > Setup > Apply Project Settings.");
            Assert.IsTrue(fog.isActive);
            Assert.IsNotNull(fog.settings.shader);
            Assert.IsNotNull(data.rendererFeatures.OfType<AsciiRendererFeature>().SingleOrDefault());
        }

        [Test]
        public void DayAndNightProfilesCarryFog()
        {
            var day = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/_Project/Settings/VolumeProfile_Base.asset");
            var night = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/_Project/Settings/VolumeProfile_Night.asset");
            Assert.IsTrue(day.TryGet(out VolumetricFog dayFog), "Day profile has no volumetric fog.");
            Assert.IsTrue(dayFog.intensity.overrideState && dayFog.intensity.value > 0f);
            Assert.IsTrue(night.TryGet(out VolumetricFog nightFog), "Night profile has no volumetric fog.");
            Assert.Greater(nightFog.heightDensity.value, dayFog.heightDensity.value, "Nights should be foggier than days.");
        }
    }
}
