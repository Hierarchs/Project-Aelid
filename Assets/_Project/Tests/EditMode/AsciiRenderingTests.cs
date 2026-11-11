using Aelid.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace Aelid.Tests
{
    public class AsciiRenderingTests
    {
        [Test]
        public void GlyphRamps_StartBlank_AndNeverGetLessDense()
        {
            var atlas = AsciiGlyphAtlas.Build(readable: true);
            try
            {
                for (int row = 0; row < AsciiGlyphAtlas.Rows; row++)
                {
                    int previous = -1;
                    for (int col = 0; col < AsciiGlyphAtlas.Levels; col++)
                    {
                        int ink = Ink(atlas, row, col);
                        if (col == 0)
                            Assert.AreEqual(0, ink, $"Row {row} must start with a blank glyph.");
                        Assert.GreaterOrEqual(ink, previous, $"Row {row} ramp gets lighter at column {col}.");
                        previous = ink;
                    }
                    Assert.Greater(Ink(atlas, row, AsciiGlyphAtlas.ColEdgeVertical), 0, $"Row {row} is missing its edge glyphs.");
                }
            }
            finally
            {
                Object.DestroyImmediate(atlas);
            }
        }

        [Test]
        public void Atlas_HasExpectedLayout()
        {
            var atlas = AsciiGlyphAtlas.Build(readable: true);
            try
            {
                Assert.AreEqual(AsciiGlyphAtlas.Size, atlas.width);
                Assert.AreEqual(AsciiGlyphAtlas.Size, atlas.height);
                Assert.AreEqual(FilterMode.Point, atlas.filterMode);
                Assert.AreEqual(64, Ink(atlas, 0, AsciiGlyphAtlas.ColSolid));
            }
            finally
            {
                Object.DestroyImmediate(atlas);
            }
        }

        [Test]
        public void SurfaceLayers_ReplaceClass_AndKeepDefaultLightingBit()
        {
            uint mask = 1u | (1u << 9); // Default + a light layer outside the ASCII class range
            uint foliage = AsciiSurfaceLayers.Apply(mask, AsciiSurfaceClass.Foliage);
            Assert.AreEqual(1u | (1u << 9) | (1u << 2), foliage);

            uint exempt = AsciiSurfaceLayers.Apply(foliage, AsciiSurfaceClass.Exempt);
            Assert.AreEqual(1u | (1u << 9) | (1u << 1), exempt, "Re-tagging must remove the previous class bit.");

            uint plain = AsciiSurfaceLayers.Apply(exempt, AsciiSurfaceClass.Default);
            Assert.AreEqual(1u | (1u << 9), plain);
        }

        [Test]
        public void CellSize_ScalesInWholeMultiplesOf1080p()
        {
            int previousOffset = AsciiRuntimeOverrides.CellSizeOffset;
            try
            {
                AsciiRuntimeOverrides.CellSizeOffset = 0;
                Assert.AreEqual(8, AsciiGrid.ResolveCellSize(null, 1080));
                Assert.AreEqual(8, AsciiGrid.ResolveCellSize(null, 1440));
                Assert.AreEqual(16, AsciiGrid.ResolveCellSize(null, 2160));
                Assert.AreEqual(8, AsciiGrid.ResolveCellSize(null, 720));

                AsciiRuntimeOverrides.CellSizeOffset = 4;
                Assert.AreEqual(12, AsciiGrid.ResolveCellSize(null, 1080));
            }
            finally
            {
                AsciiRuntimeOverrides.CellSizeOffset = previousOffset;
            }
        }

        [Test]
        public void CameraSnap_LandsOnCellGrid_AndOffsetRestoresDesiredView()
        {
            var go = new GameObject("Snap Test Camera");
            var rt = new RenderTexture(1920, 1080, 0);
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 8f;
                cam.targetTexture = rt;
                go.transform.rotation = Quaternion.Euler(52f, 0f, 0f);
                var ascii = go.AddComponent<AsciiCamera>();
                ascii.PixelSnap = true;

                int cell = AsciiGrid.ResolveCellSize(1080);
                float unitsPerPixel = 2f * cam.orthographicSize / 1080f;
                var rng = new System.Random(7);

                for (int i = 0; i < 200; i++)
                {
                    var desired = new Vector3((float)(rng.NextDouble() * 400 - 200), 30f, (float)(rng.NextDouble() * 400 - 200));
                    go.transform.position = desired;
                    ascii.ApplySnap();

                    Vector3 snapped = go.transform.position;
                    Vector2Int offset = ascii.SubCellOffsetPixels;
                    Assert.That(offset.x, Is.InRange(0, cell - 1));
                    Assert.That(offset.y, Is.InRange(0, cell - 1));

                    // The snapped camera sits on whole cells...
                    float sx = Vector3.Dot(snapped, go.transform.right) / (unitsPerPixel * cell);
                    float sy = Vector3.Dot(snapped, go.transform.up) / (unitsPerPixel * cell);
                    Assert.AreEqual(Mathf.Round(sx), sx, 1e-3f);
                    Assert.AreEqual(Mathf.Round(sy), sy, 1e-3f);

                    // ...and snapped + offset reproduces the desired view to within a pixel.
                    Vector3 restored = snapped + (go.transform.right * offset.x + go.transform.up * offset.y) * unitsPerPixel;
                    Vector3 error = restored - desired;
                    Assert.Less(Mathf.Abs(Vector3.Dot(error, go.transform.right)), unitsPerPixel * 0.51f);
                    Assert.Less(Mathf.Abs(Vector3.Dot(error, go.transform.up)), unitsPerPixel * 0.51f);
                    Assert.Less(Mathf.Abs(Vector3.Dot(error, go.transform.forward)), 1e-3f, "Snapping must not change depth.");
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(rt);
            }
        }

        static int Ink(Texture2D atlas, int row, int col)
        {
            int count = 0;
            for (int gy = 0; gy < AsciiGlyphAtlas.GlyphSize; gy++)
                for (int gx = 0; gx < AsciiGlyphAtlas.GlyphSize; gx++)
                {
                    int x = col * AsciiGlyphAtlas.GlyphSize + gx;
                    int y = AsciiGlyphAtlas.Size - 1 - (row * AsciiGlyphAtlas.GlyphSize + gy);
                    if (atlas.GetPixel(x, y).r > 0.5f) count++;
                }
            return count;
        }
    }
}
