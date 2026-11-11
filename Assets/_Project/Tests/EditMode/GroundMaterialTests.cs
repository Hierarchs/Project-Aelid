using Aelid.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Aelid.Tests
{
    public class GroundMaterialTests
    {
        [TestCase("Terrain_GrassA")]
        [TestCase("Terrain_GrassB")]
        [TestCase("Terrain_Dirt")]
        public void SavedGroundMaterials_UseTheCoverageShader_AndAllRequiredPasses(string name)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>($"Assets/_Project/Generated/Materials/{name}.mat");
            Assert.IsNotNull(material);
            Assert.AreEqual("Aelid/Ground", material.shader.name);
            Assert.AreEqual(Color.white, material.GetColor("_BaseColor"), "Bucket tints would expose triangle seams.");
            foreach (string pass in new[] { "GroundForward", "GroundSurfaceId", "ShadowCaster", "DepthOnly", "DepthNormals" })
                Assert.GreaterOrEqual(material.FindPass(pass), 0, $"Missing {pass} pass.");
        }

        [Test]
        public void LayoutExport_PreservesSegmentsAndWidth_AndUsesInvariantNumbers()
        {
            var previous = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
                string text = GroundMaterials.LayoutText(new[] { new[] { new Vector2(-2.5f, 1f), new Vector2(3f, 4f), Vector2.zero } },
                    new Vector2(-34f, 30f), 1.6f);
                StringAssert.Contains("AELID_GROUND_PATH_COUNT 2", text);
                StringAssert.Contains("float4(-2.5, 1.0, 3.0, 4.0)", text);
                StringAssert.Contains("float4(3.0, 4.0, 0.0, 0.0)", text);
                StringAssert.Contains("kGroundPathHalfWidth = 1.6;", text);
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
        }
    }
}
