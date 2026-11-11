using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Aelid.Environment;
using Aelid.Gameplay;
using Aelid.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Aelid.EditorTools
{
    /// <summary>
    /// Renders review screenshots of the vertical slice and measures glyph
    /// stability under camera motion. Works from the menu or in batch mode:
    ///   Unity -batchmode -projectPath . -executeMethod Aelid.EditorTools.AsciiCaptureTool.CaptureFromCommandLine -quit
    /// Output goes to &lt;project&gt;/Captures.
    /// </summary>
    public static class AsciiCaptureTool
    {
        const int Width = 1920;
        const int Height = 1080;

        struct Shot
        {
            public string name;
            public float hour;
            public Vector2 player;
            public float facing;
            public float zoom;
        }

        static readonly Shot[] k_Shots =
        {
            new() { name = "01_day_village", hour = 10.5f, player = new Vector2(0f, -6f), facing = 20f, zoom = 8f },
            new() { name = "02_golden_hour_pond", hour = 16.9f, player = new Vector2(15f, -9f), facing = 120f, zoom = 9f },
            new() { name = "03_dusk_field", hour = 18.6f, player = new Vector2(-13f, -8f), facing = 230f, zoom = 9f },
            new() { name = "04_night_village", hour = 22.5f, player = new Vector2(1.5f, -3.5f), facing = 0f, zoom = 8f },
            new() { name = "05_night_ruins", hour = 1.0f, player = new Vector2(-30f, 25f), facing = 300f, zoom = 9f },
            new() { name = "06_morning_forest", hour = 7.4f, player = new Vector2(-6f, 30f), facing = 200f, zoom = 9f },
            new() { name = "07_closeup_player", hour = 11f, player = new Vector2(2f, -7f), facing = 160f, zoom = 5f },
        };

        public static string OutputDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures"));

        [MenuItem("Aelid/Capture Review Screenshots")]
        public static void CaptureMenu()
        {
            Capture();
            EditorUtility.RevealInFinder(OutputDirectory);
        }

        public static void CaptureFromCommandLine()
        {
            try
            {
                Capture();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Renders the village shot in every AsciiDebugView, with and without post-processing.</summary>
        public static void CaptureDebugViewsFromCommandLine()
        {
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                Directory.CreateDirectory(OutputDirectory);
                EditorSceneManager.OpenScene(VerticalSliceBuilder.ScenePath, OpenSceneMode.Single);
                var rig = UnityEngine.Object.FindAnyObjectByType<IsometricCameraRig>();
                var asciiCamera = UnityEngine.Object.FindAnyObjectByType<AsciiCamera>();
                var dayNight = UnityEngine.Object.FindAnyObjectByType<DayNightCycle>();
                var player = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();
                Camera cam = rig.cam;
                var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                cam.targetTexture = rt;
                var cameraData = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);

                foreach (bool post in new[] { true, false })
                {
                    cameraData.renderPostProcessing = post;
                    foreach (AsciiDebugView view in Enum.GetValues(typeof(AsciiDebugView)))
                    {
                        AsciiRuntimeOverrides.DebugView = view;
                        Pose(player, rig, asciiCamera, dayNight, k_Shots[0]);
                        RenderToTextureAndDiscard(cam, rt);
                        asciiCamera.ApplySnap();
                        SavePng(RenderToTexture(cam, rt), $"debug_{(int)view}_{view}{(post ? "" : "_nopost")}.png");
                    }
                }
                AsciiRuntimeOverrides.DebugView = AsciiDebugView.None;
                cameraData.renderPostProcessing = true;
                cam.targetTexture = null;
                rt.Release();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Build the scene, then capture (one batch call for CI-style checks).</summary>
        public static void BuildAndCaptureFromCommandLine()
        {
            try
            {
                VerticalSliceBuilder.Build();
                Capture();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        public static void Capture()
        {
            ShaderUtil.allowAsyncCompilation = false;
            Directory.CreateDirectory(OutputDirectory);
            EditorSceneManager.OpenScene(VerticalSliceBuilder.ScenePath, OpenSceneMode.Single);

            var rig = UnityEngine.Object.FindAnyObjectByType<IsometricCameraRig>();
            var asciiCamera = UnityEngine.Object.FindAnyObjectByType<AsciiCamera>();
            var dayNight = UnityEngine.Object.FindAnyObjectByType<DayNightCycle>();
            var player = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();
            if (rig == null || asciiCamera == null || dayNight == null || player == null)
                throw new InvalidOperationException("Vertical slice scene is missing its camera rig, ASCII camera, day/night cycle or player.");

            Camera cam = rig.cam;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "AsciiCapture" };
            rt.Create();
            cam.targetTexture = rt;

            try
            {
                SaveAtlasPreview();
                Pose(player, rig, asciiCamera, dayNight, k_Shots[0]);
                RenderToTextureAndDiscard(cam, rt);

                foreach (var shot in k_Shots)
                {
                    Pose(player, rig, asciiCamera, dayNight, shot);
                    AsciiRuntimeOverrides.Enabled = true;
                    asciiCamera.ApplySnap();
                    RenderToTextureAndDiscard(cam, rt); // let volumes/grading settle on the new time of day
                    SavePng(RenderToTexture(cam, rt), shot.name + ".png");

                    rig.SnapToTarget();
                    AsciiRuntimeOverrides.Enabled = false;
                    SavePng(RenderToTexture(cam, rt), shot.name + "_raw.png");
                    AsciiRuntimeOverrides.Enabled = true;
                }

                string report = StabilityTest(player, rig, asciiCamera, dayNight, cam, rt);
                File.WriteAllText(Path.Combine(OutputDirectory, "stability.txt"), report);
                Debug.Log("[Aelid] " + report);
            }
            finally
            {
                AsciiRuntimeOverrides.Enabled = true;
                cam.targetTexture = null;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
            Debug.Log($"[Aelid] Captures written to {OutputDirectory}");
        }

        static void Pose(PlayerMotor player, IsometricCameraRig rig, AsciiCamera asciiCamera, DayNightCycle dayNight, Shot shot)
        {
            float y = VerticalSliceBuilder.Height(shot.player.x, shot.player.y) + 0.02f;
            player.transform.SetPositionAndRotation(new Vector3(shot.player.x, y, shot.player.y), Quaternion.Euler(0f, shot.facing, 0f));
            rig.viewHalfHeight = shot.zoom;
            rig.SnapToTarget();
            dayNight.SetTime(shot.hour);
            foreach (var night in UnityEngine.Object.FindObjectsByType<NightLight>())
                night.Refresh();
        }

        /// <summary>
        /// The first frame after loading a scene uses a stale post-processing
        /// grade in batch mode; render once and throw it away.
        /// </summary>
        static void RenderToTextureAndDiscard(Camera cam, RenderTexture rt)
        {
            UnityEngine.Object.DestroyImmediate(RenderToTexture(cam, rt));
        }

        static Texture2D RenderToTexture(Camera cam, RenderTexture rt)
        {
            cam.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            return texture;
        }

        static void SavePng(Texture2D texture, string fileName)
        {
            File.WriteAllBytes(Path.Combine(OutputDirectory, fileName), texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }

        static void SaveAtlasPreview()
        {
            var atlas = AsciiGlyphAtlas.Build(readable: true);
            const int scale = 4;
            int size = AsciiGlyphAtlas.Size * scale;
            var preview = new Texture2D(size, size, TextureFormat.RGB24, false);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float v = atlas.GetPixel(x / scale, y / scale).r;
                    bool grid = (x / scale) % AsciiGlyphAtlas.GlyphSize == 0 || (y / scale) % AsciiGlyphAtlas.GlyphSize == 0;
                    byte b = (byte)(v > 0.5f ? 235 : grid ? 40 : 12);
                    pixels[y * size + x] = new Color32(b, b, (byte)(v > 0.5f ? 200 : b), 255);
                }
            preview.SetPixels32(pixels);
            preview.Apply();
            SavePng(preview, "glyph_atlas.png");
            UnityEngine.Object.DestroyImmediate(atlas);

            var sb = new StringBuilder("Glyph ramps (dark -> bright) by atlas row\n");
            foreach (AsciiSurfaceClass c in Enum.GetValues(typeof(AsciiSurfaceClass)))
                sb.AppendLine($"{(int)c,2} {c,-9} [{AsciiGlyphAtlas.GetSortedRamp((int)c)}]");
            sb.AppendLine($"{AsciiSurfaceLayers.SkyClass,2} Sky       [{AsciiGlyphAtlas.GetSortedRamp(AsciiSurfaceLayers.SkyClass)}]");
            File.WriteAllText(Path.Combine(OutputDirectory, "glyph_ramps.txt"), sb.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// Moves the camera by whole pixels per frame and checks how much of the
        /// image changes once the expected scroll is compensated. A perfectly
        /// stable renderer scores ~0% (only screen borders and shadow updates).
        /// </summary>
        static string StabilityTest(PlayerMotor player, IsometricCameraRig rig, AsciiCamera asciiCamera, DayNightCycle dayNight,
            Camera cam, RenderTexture rt)
        {
            const int frames = 9;
            int margin = 4 * AsciiGrid.ResolveCellSize(Height);

            // Characters are drawn at full 3D detail by design and rasterize at
            // sub-pixel offsets; hide them so the test isolates the glyph grid.
            var hidden = new List<Renderer>(player.GetComponentsInChildren<Renderer>());
            foreach (var r in hidden) r.enabled = false;

            Pose(player, rig, asciiCamera, dayNight, new Shot { hour = 11f, player = new Vector2(4f, -2f), zoom = 8f });
            Vector3 baseFocus = player.transform.position + rig.targetOffset;
            float unitsPerPixel = 2f * cam.orthographicSize / Height;
            RenderToTextureAndDiscard(cam, rt);

            var sb = new StringBuilder();
            sb.AppendLine($"Glyph stability test: {frames} frames at {Width}x{Height}, cell {AsciiGrid.ResolveCellSize(Height)} px.");
            sb.AppendLine("Changed pixels after compensating for the scroll (lower = more stable):");

            var modes = new (string label, bool ascii, bool snap, Vector2Int step)[]
            {
                ("raw 3D (reference)  ", false, false, new Vector2Int(3, 2)),
                ("ASCII, snapping OFF ", true, false, new Vector2Int(3, 2)),
                ("ASCII, snapping ON  ", true, true, new Vector2Int(3, 2)),
                ("ASCII, snap ON, X only", true, true, new Vector2Int(3, 0)),
                ("ASCII, snap ON, Y only", true, true, new Vector2Int(0, 2)),
            };

            foreach (var (label, ascii, snap, step) in modes)
            {
                AsciiRuntimeOverrides.Enabled = ascii;
                asciiCamera.PixelSnap = snap;
                Color32[] previous = null;
                Vector2Int previousPixel = default;
                double sum = 0;
                double worst = 0;
                int pairs = 0;
                var trace = new StringBuilder();

                for (int k = 0; k < frames; k++)
                {
                    Vector3 focus = baseFocus
                        + rig.transform.right * (step.x * k * unitsPerPixel)
                        + rig.transform.up * (step.y * k * unitsPerPixel);
                    rig.SetFocus(focus);
                    Vector2Int pixel = new(
                        Mathf.RoundToInt(Vector3.Dot(rig.transform.position, rig.transform.right) / unitsPerPixel),
                        Mathf.RoundToInt(Vector3.Dot(rig.transform.position, rig.transform.up) / unitsPerPixel));
                    asciiCamera.ApplySnap();
                    trace.Append($" [{pixel.x},{pixel.y} off {asciiCamera.SubCellOffsetPixels.x},{asciiCamera.SubCellOffsetPixels.y} org {asciiCamera.CellOrigin.x},{asciiCamera.CellOrigin.y}]");

                    var texture = RenderToTexture(cam, rt);
                    var pixels = texture.GetPixels32();
                    UnityEngine.Object.DestroyImmediate(texture);

                    if (previous != null)
                    {
                        double changed = ChangedFraction(previous, pixels, pixel - previousPixel, margin);
                        sum += changed;
                        worst = Math.Max(worst, changed);
                        pairs++;
                    }
                    previous = pixels;
                    previousPixel = pixel;
                }
                sb.AppendLine($"  {label} step ({step.x},{step.y}): mean {sum / Math.Max(pairs, 1) * 100.0:0.00}%   worst {worst * 100.0:0.00}%");
                if (snap)
                    sb.AppendLine("     trace:" + trace);
            }

            asciiCamera.PixelSnap = true;
            AsciiRuntimeOverrides.Enabled = true;
            foreach (var r in hidden) r.enabled = true;
            return sb.ToString();
        }

        /// <summary>Fraction of interior pixels where B(x,y) differs from A(x+dx, y+dy).</summary>
        static double ChangedFraction(Color32[] a, Color32[] b, Vector2Int shift, int margin)
        {
            int changed = 0, total = 0;
            int m = margin + Mathf.Max(Mathf.Abs(shift.x), Mathf.Abs(shift.y));
            for (int y = m; y < Height - m; y++)
            {
                for (int x = m; x < Width - m; x++)
                {
                    Color32 pb = b[y * Width + x];
                    Color32 pa = a[(y + shift.y) * Width + (x + shift.x)];
                    int d = Math.Max(Math.Abs(pa.r - pb.r), Math.Max(Math.Abs(pa.g - pb.g), Math.Abs(pa.b - pb.b)));
                    if (d > 24) changed++;
                    total++;
                }
            }
            return total > 0 ? changed / (double)total : 0;
        }
    }
}
