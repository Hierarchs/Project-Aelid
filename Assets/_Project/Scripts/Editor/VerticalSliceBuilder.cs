using System.Collections.Generic;
using System.IO;
using Aelid.Debugging;
using Aelid.Environment;
using Aelid.Gameplay;
using Aelid.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Aelid.EditorTools
{
    /// <summary>
    /// Generates the vertical-slice test area from code: low-poly terrain with
    /// a village, forest, pond and hilltop ruins, plus the player, camera,
    /// lighting and post-processing. Deterministic (fixed seed), so rebuilding
    /// gives the same world. This is a bootstrap tool: once hand-made content
    /// replaces it, stop rebuilding over the scene.
    /// </summary>
    public static class VerticalSliceBuilder
    {
        const string Root = "Assets/_Project";
        const string MeshDir = Root + "/Generated/Meshes";
        const string MaterialDir = Root + "/Generated/Materials";
        const string SettingsDir = Root + "/Settings";
        public const string ScenePath = Root + "/Scenes/VerticalSlice.unity";

        const float WorldHalfSize = 80f;
        const float QuadSize = 2f;
        public const float WaterLevel = -0.6f;
        const float PathHalfWidth = 1.6f;

        static readonly Vector2 k_Village = new(0f, 0f);
        static readonly Vector2 k_Pond = new(26f, -16f);
        static readonly Vector2 k_Ruins = new(-34f, 30f);
        static readonly Vector2 k_Camp = new(12f, 17f);
        static readonly Rect k_Field = new(-27f, -19f, 12f, 12f);

        static readonly Vector2[][] k_Paths =
        {
            new[] { new Vector2(0, 0), new Vector2(-12, 12), new Vector2(-26, 24), new Vector2(-33, 29) },
            new[] { new Vector2(0, 0), new Vector2(10, -7), new Vector2(18, -11) },
            new[] { new Vector2(0, 0), new Vector2(2, -25), new Vector2(0, -58) },
            new[] { new Vector2(0, 0), new Vector2(25, 6), new Vector2(52, 14) },
            new[] { new Vector2(-12, -6), new Vector2(-15, -10) },
        };

        static readonly Dictionary<string, Material> s_Materials = new();
        static System.Random s_Rng;

        [MenuItem("Aelid/Build Vertical Slice Scene")]
        public static void BuildMenu()
        {
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Rebuild vertical slice?", $"This regenerates {ScenePath} and overwrites manual edits in it.", "Rebuild", "Cancel"))
                return;
            Build();
        }

        /// <summary>Batch-mode entry point (-executeMethod).</summary>
        public static void BuildFromCommandLine()
        {
            Build();
        }

        public static void Build()
        {
            ProjectSetup.Apply();
            EnsureFolder(MeshDir);
            EnsureFolder(MaterialDir);
            EnsureFolder(SettingsDir);
            EnsureFolder(Root + "/Scenes");
            s_Materials.Clear();
            s_Rng = new System.Random(1337);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var world = new GameObject("World").transform;
            BuildTerrain(world);
            var waterReflection = BuildWater(world);
            BuildVillage(world);
            BuildField(world);
            BuildRuins(world);
            BuildCampfire(world, Ground(k_Camp.x, k_Camp.y)); // wilderness camp in a meadow
            BuildVegetationAndRocks(world);
            BuildBoundary(world);

            var player = BuildPlayer();
            var (cam, rig, asciiCam) = BuildCamera(player);
            waterReflection.sourceCamera = cam;
            var dayNight = BuildEnvironment();

            var debug = new GameObject("Dev Overlay").AddComponent<DevOverlay>();
            debug.dayNight = dayNight;
            debug.cameraRig = rig;
            debug.asciiCamera = asciiCam;
            debug.player = player.GetComponent<PlayerMotor>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[Aelid] Vertical slice built: {ScenePath}");
        }

        // ------------------------------------------------------------------ terrain

        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        static float Fbm(float x, float z, int octaves)
        {
            float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * Mathf.PerlinNoise(x * freq + 31.7f * i, z * freq + 17.3f * i);
                norm += amp;
                amp *= 0.5f;
                freq *= 2f;
            }
            return sum / norm;
        }

        public static float Height(float x, float z)
        {
            var p = new Vector2(x, z);
            float detail = (Mathf.PerlinNoise(x * 0.11f + 7f, z * 0.11f + 3f) - 0.5f) * 0.7f;
            float h = (Fbm(x * 0.018f + 100f, z * 0.018f + 100f, 4) - 0.5f) * 7f + detail;

            float rim = Smooth(0.58f, 0.97f, p.magnitude / WorldHalfSize);
            h += rim * (14f + Fbm(x * 0.04f + 50f, z * 0.04f + 50f, 3) * 18f);

            h = Mathf.Lerp(h, 0.25f + detail * 0.25f, 1f - Smooth(13f, 24f, Vector2.Distance(p, k_Village)));
            h = Mathf.Lerp(h, 4.2f + detail * 0.3f, 1f - Smooth(7f, 17f, Vector2.Distance(p, k_Ruins)));
            h = Mathf.Lerp(h, 0.6f + detail * 0.2f, 1f - Smooth(7f, 11f, DistanceToRect(p, k_Field)));
            h = Mathf.Lerp(h, -1.05f, 1f - Smooth(5f, 12.5f, Vector2.Distance(p, k_Pond))); // knee-deep wading (~0.45 m)
            return h;
        }

        static float DistanceToRect(Vector2 p, Rect r)
        {
            float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax);
            float dz = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static float DistanceToPaths(Vector2 p)
        {
            float best = float.MaxValue;
            foreach (var path in k_Paths)
                for (int i = 0; i < path.Length - 1; i++)
                    best = Mathf.Min(best, DistanceToSegment(p, path[i], path[i + 1]));
            return best;
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return Vector2.Distance(p, a + ab * t);
        }

        static Vector3 Ground(float x, float z) => new(x, Height(x, z), z);

        static void BuildTerrain(Transform world)
        {
            var root = new GameObject("Terrain").transform;
            root.SetParent(world, false);

            var buckets = new Dictionary<string, LowPolyMesh>
            {
                { "GrassA", new LowPolyMesh() }, { "GrassB", new LowPolyMesh() }, { "Dirt", new LowPolyMesh() },
                { "Rock", new LowPolyMesh() }, { "Sand", new LowPolyMesh() }, { "Snow", new LowPolyMesh() },
            };

            int n = Mathf.RoundToInt(WorldHalfSize * 2f / QuadSize);
            var grid = new Vector3[(n + 1) * (n + 1)];
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                {
                    float x = -WorldHalfSize + i * QuadSize;
                    float z = -WorldHalfSize + j * QuadSize;
                    grid[j * (n + 1) + i] = Ground(x, z);
                }

            var colliderTris = new List<int>();
            void AddTri(int ia, int ib, int ic)
            {
                Vector3 a = grid[ia], b = grid[ib], c = grid[ic];
                buckets[Classify(a, b, c)].Tri(a, b, c);
                colliderTris.Add(ia); colliderTris.Add(ib); colliderTris.Add(ic);
            }

            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int i00 = j * (n + 1) + i, i10 = i00 + 1, i01 = i00 + n + 1, i11 = i01 + 1;
                    if (((i + j) & 1) == 0) { AddTri(i00, i01, i11); AddTri(i00, i11, i10); }
                    else { AddTri(i00, i01, i10); AddTri(i10, i01, i11); }
                }

            AddTerrainPart(root, "Grass A", buckets["GrassA"], Mat("Terrain_GrassA", new Color(0.15f, 0.34f, 0.22f)), AsciiSurfaceClass.Grass);
            AddTerrainPart(root, "Grass B", buckets["GrassB"], Mat("Terrain_GrassB", new Color(0.19f, 0.38f, 0.25f)), AsciiSurfaceClass.Grass);
            AddTerrainPart(root, "Dirt", buckets["Dirt"], Mat("Terrain_Dirt", new Color(0.29f, 0.29f, 0.32f)), AsciiSurfaceClass.Default);
            AddTerrainPart(root, "Rock", buckets["Rock"], Mat("Terrain_Rock", new Color(0.34f, 0.38f, 0.43f)), AsciiSurfaceClass.Stone);
            AddTerrainPart(root, "Sand", buckets["Sand"], Mat("Terrain_Sand", new Color(0.40f, 0.40f, 0.38f)), AsciiSurfaceClass.Default);
            AddTerrainPart(root, "Snow", buckets["Snow"], Mat("Terrain_Snow", new Color(0.88f, 0.90f, 0.95f), 0.3f), AsciiSurfaceClass.Default);

            var collisionMesh = new Mesh { name = "Terrain_Collision", indexFormat = IndexFormat.UInt32 };
            collisionMesh.SetVertices(grid);
            collisionMesh.SetTriangles(colliderTris, 0);
            collisionMesh.RecalculateBounds();
            collisionMesh.RecalculateNormals();
            var collider = new GameObject("Terrain Collider");
            collider.transform.SetParent(root, false);
            collider.AddComponent<MeshCollider>().sharedMesh = SaveMesh(collisionMesh);
        }

        static string Classify(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 centroid = (a + b + c) / 3f;
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            float slope = Vector3.Angle(normal, Vector3.up);
            var p = new Vector2(centroid.x, centroid.z);

            if (centroid.y < WaterLevel + 0.45f && Vector2.Distance(p, k_Pond) < 16f) return "Sand";
            if (centroid.y > 19f && slope < 42f) return "Snow";
            if (slope > 31f || centroid.y > 11f) return "Rock";
            if (DistanceToPaths(p) < PathHalfWidth) return "Dirt";
            if (Vector2.Distance(p, k_Ruins) < 7.5f) return Mathf.PerlinNoise(p.x * 0.6f, p.y * 0.6f) > 0.55f ? "Dirt" : "GrassB";
            return Mathf.PerlinNoise(p.x * 0.07f + 20f, p.y * 0.07f + 40f) > 0.5f ? "GrassA" : "GrassB";
        }

        static void AddTerrainPart(Transform parent, string name, LowPolyMesh mesh, Material material, AsciiSurfaceClass surface)
        {
            if (mesh.TriangleCount == 0)
                return;
            var go = MeshObject(name, parent, SaveMesh(mesh.ToMesh("Terrain_" + name.Replace(" ", ""))), material, surface, Vector3.zero, Quaternion.identity, Vector3.one);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.TwoSided;
        }

        // ------------------------------------------------------------------ water

        static PlanarReflection BuildWater(Transform world)
        {
            var mesh = new LowPolyMesh();
            const float step = 1.5f;
            const float radius = 14f;
            for (float x = -radius; x < radius; x += step)
                for (float z = -radius; z < radius; z += step)
                {
                    if (new Vector2(x + step * 0.5f, z + step * 0.5f).magnitude > radius) continue;
                    var p00 = new Vector3(x, 0, z);
                    var p10 = new Vector3(x + step, 0, z);
                    var p01 = new Vector3(x, 0, z + step);
                    var p11 = new Vector3(x + step, 0, z + step);
                    mesh.Tri(p00, p01, p11);
                    mesh.Tri(p00, p11, p10);
                }
            var water = MeshObject("Pond", world, SaveMesh(mesh.ToMesh("Water_Pond")),
                WaterMaterial(), AsciiSurfaceClass.Water,
                new Vector3(k_Pond.x, WaterLevel, k_Pond.y), Quaternion.identity, Vector3.one);
            water.layer = 4; // built-in "Water" layer; kept out of its own reflection
            var renderer = water.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            water.AddComponent<WaterBody>().radius = 13f;
            return water.AddComponent<PlanarReflection>();
        }

        static Material WaterMaterial()
        {
            // The pre-overhaul pond used a URP/Lit material; remove it so it can't be picked up again.
            if (AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "/Water.mat") != null)
                AssetDatabase.DeleteAsset(MaterialDir + "/Water.mat");

            string path = MaterialDir + "/Water_Pond.mat";
            var shader = Shader.Find("Aelid/Water");
            var fresh = new Material(shader) { name = "Water_Pond" };
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                AssetDatabase.CreateAsset(fresh, path);
                return fresh;
            }
            // Reset to the shader's defaults (keeps the asset GUID), so Water.shader is the
            // single source of truth for the look; tweak the defaults there.
            EditorUtility.CopySerialized(fresh, material);
            Object.DestroyImmediate(fresh);
            EditorUtility.SetDirty(material);
            return material;
        }

        // ------------------------------------------------------------------ village

        static void BuildVillage(Transform world)
        {
            var root = new GameObject("Village").transform;
            root.SetParent(world, false);

            var walls = Mat("House_Walls", new Color(0.46f, 0.42f, 0.36f));
            var timber = Mat("House_Timber", new Color(0.24f, 0.20f, 0.18f));
            var roofRed = Mat("House_Roof_Red", new Color(0.40f, 0.24f, 0.21f));
            var roofSlate = Mat("House_Roof_Slate", new Color(0.22f, 0.27f, 0.34f));
            var stone = Mat("Stone", new Color(0.39f, 0.43f, 0.47f));
            var window = Mat("Window", new Color(0.08f, 0.07f, 0.05f), 0.6f, 0f, Color.black);

            var wallMesh = SaveMesh(Build(m => m.Box(new Vector3(0, 1.6f, 0), new Vector3(6f, 3.2f, 5f)), "House_Walls"));
            var beamMesh = SaveMesh(Build(m =>
            {
                foreach (float x in new[] { -3.02f, 3.02f })
                    foreach (float z in new[] { -2.52f, 2.52f })
                        m.Box(new Vector3(x, 1.6f, z), new Vector3(0.28f, 3.25f, 0.28f));
                m.Box(new Vector3(0, 3.15f, -2.52f), new Vector3(6.3f, 0.25f, 0.2f));
                m.Box(new Vector3(0, 3.15f, 2.52f), new Vector3(6.3f, 0.25f, 0.2f));
                m.Box(new Vector3(0, 1.1f, -2.56f), new Vector3(1.3f, 2.2f, 0.16f)); // door
            }, "House_Timber"));
            var roofMesh = SaveMesh(Build(m => m.GableRoof(new Vector3(0, 3.2f, 0), 6f, 5f, 2.2f, 0.45f, Quaternion.identity), "House_Roof"));
            var chimneyMesh = SaveMesh(Build(m => m.Box(new Vector3(1.6f, 4.5f, 1.2f), new Vector3(0.7f, 2.4f, 0.7f)), "House_Chimney"));
            var windowMesh = SaveMesh(Build(m =>
            {
                m.Box(new Vector3(-1.9f, 1.8f, -2.53f), new Vector3(0.9f, 0.8f, 0.1f));
                m.Box(new Vector3(1.9f, 1.8f, -2.53f), new Vector3(0.9f, 0.8f, 0.1f));
                m.Box(new Vector3(-3.03f, 1.8f, 0f), new Vector3(0.1f, 0.8f, 1.0f));
                m.Box(new Vector3(3.03f, 1.8f, 0f), new Vector3(0.1f, 0.8f, 1.0f));
            }, "House_Windows"));

            float[] angles = { 50f, 95f, 175f, 220f, 300f };
            for (int i = 0; i < angles.Length; i++)
            {
                float a = angles[i] * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 pos = dir * 12.5f;
                pos.y = Height(pos.x, pos.z) - 0.05f;
                var house = new GameObject($"House {i + 1}").transform;
                house.SetParent(root, false);
                house.SetPositionAndRotation(pos, Quaternion.LookRotation(dir, Vector3.up));
                float s = 0.9f + (float)s_Rng.NextDouble() * 0.25f;
                house.localScale = new Vector3(s, s, s);

                MeshObject("Walls", house, wallMesh, walls, AsciiSurfaceClass.Wood, Vector3.zero, Quaternion.identity, Vector3.one);
                MeshObject("Timber", house, beamMesh, timber, AsciiSurfaceClass.Wood, Vector3.zero, Quaternion.identity, Vector3.one);
                MeshObject("Roof", house, roofMesh, i % 2 == 0 ? roofRed : roofSlate, AsciiSurfaceClass.Wood, Vector3.zero, Quaternion.identity, Vector3.one);
                MeshObject("Chimney", house, chimneyMesh, stone, AsciiSurfaceClass.Stone, Vector3.zero, Quaternion.identity, Vector3.one);
                var win = MeshObject("Windows", house, windowMesh, window, AsciiSurfaceClass.Emissive, Vector3.zero, Quaternion.identity, Vector3.one);
                var boxCollider = house.gameObject.AddComponent<BoxCollider>();
                boxCollider.center = new Vector3(0, 1.6f, 0);
                boxCollider.size = new Vector3(6.2f, 3.2f, 5.2f);

                var nightLight = house.gameObject.AddComponent<NightLight>();
                nightLight.emissiveRenderers = new Renderer[] { win.GetComponent<MeshRenderer>() };
                nightLight.emission = new Color(1.8f, 0.72f, 0.20f);
            }

            BuildCampfire(root, new Vector3(0f, Height(0, 0), 0f));
            BuildWell(root, new Vector3(5.5f, Height(5.5f, 3.5f), 3.5f));

            Vector2[] lamps = { new(5f, -5f), new(-5f, -5f), new(-5f, 5f), new(6f, 7.5f), new(9f, -3f) };
            foreach (var l in lamps)
                BuildLampPost(root, new Vector3(l.x, Height(l.x, l.y), l.y));

            // On the pond's north bank, so its light is mirrored in the water at night.
            BuildLampPost(root, Ground(24.5f, -3.2f));
        }

        static void BuildCampfire(Transform parent, Vector3 pos)
        {
            var root = new GameObject("Campfire").transform;
            root.SetParent(parent, false);
            root.position = pos;

            var stones = new LowPolyMesh();
            for (int i = 0; i < 9; i++)
            {
                float a = i * Mathf.PI * 2f / 9f;
                stones.Blob(new Vector3(Mathf.Cos(a) * 0.95f, 0.12f, Mathf.Sin(a) * 0.95f), new Vector3(0.28f, 0.2f, 0.25f), 0, 0.25f, 100 + i);
            }
            MeshObject("Stones", root, SaveMesh(stones.ToMesh("Campfire_Stones")), Mat("Stone", new Color(0.39f, 0.43f, 0.47f)), AsciiSurfaceClass.Stone, Vector3.zero, Quaternion.identity, Vector3.one);

            var logs = new LowPolyMesh();
            for (int i = 0; i < 4; i++)
                logs.Box(new Vector3(0, 0.15f, 0), new Vector3(0.18f, 0.18f, 1.3f), Quaternion.Euler(10f, i * 45f, 0f));
            MeshObject("Logs", root, SaveMesh(logs.ToMesh("Campfire_Logs")), Mat("House_Timber", new Color(0.24f, 0.20f, 0.18f)), AsciiSurfaceClass.Wood, Vector3.zero, Quaternion.identity, Vector3.one);

            var flames = new LowPolyMesh();
            flames.Cone(new Vector3(0, 0.15f, 0), 0.42f, 1.1f, 5, 0f);
            flames.Cone(new Vector3(0.18f, 0.15f, 0.1f), 0.25f, 0.75f, 5, 30f);
            flames.Cone(new Vector3(-0.15f, 0.15f, -0.12f), 0.22f, 0.65f, 5, 60f);
            var flame = MeshObject("Flames", root, SaveMesh(flames.ToMesh("Campfire_Flames")),
                Mat("Fire", new Color(1f, 0.55f, 0.15f), 0f, 0f, new Color(3.9f, 1.1f, 0.22f)), AsciiSurfaceClass.Emissive, Vector3.zero, Quaternion.identity, Vector3.one);
            flame.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            var lightGo = new GameObject("Fire Light");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.localPosition = new Vector3(0, 1.1f, 0);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.56f, 0.27f);
            light.intensity = 24f;
            light.range = 18f;
            lightGo.AddComponent<FlickerLight>().baseIntensity = 24f;
        }

        static void BuildWell(Transform parent, Vector3 pos)
        {
            var root = new GameObject("Well").transform;
            root.SetParent(parent, false);
            root.position = pos;
            var ring = SaveMesh(Build(m => m.Cylinder(Vector3.zero, 1.0f, 1.0f, 0.9f, 8, 22.5f), "Well_Ring"));
            MeshObject("Ring", root, ring, Mat("Stone", Color.gray), AsciiSurfaceClass.Stone, Vector3.zero, Quaternion.identity, Vector3.one);
            var frame = SaveMesh(Build(m =>
            {
                m.Box(new Vector3(-0.9f, 1.2f, 0), new Vector3(0.15f, 2.4f, 0.15f));
                m.Box(new Vector3(0.9f, 1.2f, 0), new Vector3(0.15f, 2.4f, 0.15f));
                m.GableRoof(new Vector3(0, 2.3f, 0), 1.9f, 1.4f, 0.7f, 0.2f, Quaternion.Euler(0, 90, 0));
            }, "Well_Frame"));
            MeshObject("Frame", root, frame, Mat("House_Timber", Color.gray), AsciiSurfaceClass.Wood, Vector3.zero, Quaternion.identity, Vector3.one);
            var col = root.gameObject.AddComponent<CapsuleCollider>();
            col.radius = 1.0f;
            col.height = 2.5f;
            col.center = new Vector3(0, 1.2f, 0);
        }

        static void BuildLampPost(Transform parent, Vector3 pos)
        {
            var root = new GameObject("Lamp Post").transform;
            root.SetParent(parent, false);
            root.position = pos;
            var pole = SaveMesh(Build(m =>
            {
                m.Cylinder(Vector3.zero, 0.1f, 0.08f, 3.0f, 6);
                m.Box(new Vector3(0.25f, 2.95f, 0), new Vector3(0.55f, 0.08f, 0.08f));
            }, "Lamp_Pole"));
            MeshObject("Pole", root, pole, Mat("House_Timber", Color.gray), AsciiSurfaceClass.Wood, Vector3.zero, Quaternion.identity, Vector3.one);
            var head = SaveMesh(Build(m => m.Box(new Vector3(0.48f, 2.72f, 0), new Vector3(0.3f, 0.38f, 0.3f)), "Lamp_Head"));
            var headGo = MeshObject("Lantern", root, head, Mat("Lantern", new Color(1f, 0.8f, 0.45f), 0.2f, 0f, Color.black), AsciiSurfaceClass.Emissive, Vector3.zero, Quaternion.identity, Vector3.one);
            headGo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            var lightGo = new GameObject("Lamp Light");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.localPosition = new Vector3(0.48f, 2.5f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.72f, 0.42f);
            light.intensity = 8f;
            light.range = 12f;

            var night = root.gameObject.AddComponent<NightLight>();
            night.lights = new[] { light };
            night.intensity = 8f;
            night.emissiveRenderers = new Renderer[] { headGo.GetComponent<MeshRenderer>() };
            night.emission = new Color(2.4f, 1.0f, 0.26f);
            var col = root.gameObject.AddComponent<CapsuleCollider>();
            col.radius = 0.15f;
            col.height = 3f;
            col.center = new Vector3(0, 1.5f, 0);
        }

        static void BuildField(Transform world)
        {
            var root = new GameObject("Wheat Field").transform;
            root.SetParent(world, false);
            var crops = new LowPolyMesh();
            int seed = 500;
            for (float x = k_Field.xMin + 1f; x < k_Field.xMax - 0.5f; x += 1.1f)
                for (float z = k_Field.yMin + 1f; z < k_Field.yMax - 0.5f; z += 0.9f)
                {
                    if (((int)(z * 10)) % 3 == 0) continue; // furrows
                    var p = Ground(x, z);
                    crops.Blob(p + Vector3.up * 0.35f, new Vector3(0.35f, 0.45f, 0.3f), 0, 0.3f, seed++);
                }
            MeshObject("Crops", root, SaveMesh(crops.ToMesh("Field_Crops")), Mat("Wheat", new Color(0.59f, 0.51f, 0.32f)), AsciiSurfaceClass.Grass, Vector3.zero, Quaternion.identity, Vector3.one);

            var fence = new LowPolyMesh();
            var corners = new[]
            {
                new Vector2(k_Field.xMin, k_Field.yMin), new Vector2(k_Field.xMax, k_Field.yMin),
                new Vector2(k_Field.xMax, k_Field.yMax), new Vector2(k_Field.xMin, k_Field.yMax),
            };
            for (int side = 0; side < 4; side++)
            {
                Vector2 a = corners[side], b = corners[(side + 1) % 4];
                int posts = Mathf.CeilToInt(Vector2.Distance(a, b) / 2f);
                for (int k = 0; k <= posts; k++)
                {
                    Vector2 p = Vector2.Lerp(a, b, k / (float)posts);
                    if (side == 1 && k == posts / 2) continue; // gate gap
                    fence.Box(Ground(p.x, p.y) + Vector3.up * 0.55f, new Vector3(0.14f, 1.1f, 0.14f));
                    if (k < posts && !(side == 1 && (k == posts / 2 - 1)))
                    {
                        Vector2 q = Vector2.Lerp(a, b, (k + 0.5f) / posts);
                        Vector3 g = Ground(q.x, q.y);
                        float len = Vector2.Distance(a, b) / posts;
                        var rot = Quaternion.LookRotation(new Vector3(b.x - a.x, 0, b.y - a.y));
                        fence.Box(g + Vector3.up * 0.75f, new Vector3(0.07f, 0.1f, len), rot);
                        fence.Box(g + Vector3.up * 0.4f, new Vector3(0.07f, 0.1f, len), rot);
                    }
                }
            }
            MeshObject("Fence", root, SaveMesh(fence.ToMesh("Field_Fence")), Mat("Fence_Wood", new Color(0.45f, 0.32f, 0.20f)), AsciiSurfaceClass.Wood, Vector3.zero, Quaternion.identity, Vector3.one);
        }

        // ------------------------------------------------------------------ ruins

        static void BuildRuins(Transform world)
        {
            var root = new GameObject("Hilltop Ruins").transform;
            root.SetParent(world, false);
            var ruinStone = Mat("Ruin_Stone", new Color(0.42f, 0.47f, 0.52f));
            var mossStone = Mat("Ruin_Moss", new Color(0.29f, 0.38f, 0.33f));

            var pillars = new LowPolyMesh();
            float[] heights = { 5.2f, 0f, 3.6f, 1.2f, 4.8f, 0.7f, 0f, 2.6f, 5.0f, 1.8f };
            for (int i = 0; i < heights.Length; i++)
            {
                if (heights[i] <= 0f) continue;
                float a = i * Mathf.PI * 2f / heights.Length;
                float x = k_Ruins.x + Mathf.Cos(a) * 6.8f;
                float z = k_Ruins.y + Mathf.Sin(a) * 6.8f;
                Vector3 b = Ground(x, z) - Vector3.up * 0.2f;
                pillars.Cylinder(b, 0.75f, 0.75f, 0.4f, 6, 0f);                 // plinth
                pillars.Cylinder(b + Vector3.up * 0.4f, 0.52f, 0.46f, heights[i], 6, 15f);
                if (heights[i] > 4.5f)
                    pillars.Box(b + Vector3.up * (heights[i] + 0.55f), new Vector3(1.3f, 0.3f, 1.3f), Quaternion.Euler(0, i * 23f, 0));
            }
            MeshObject("Pillars", root, SaveMesh(pillars.ToMesh("Ruins_Pillars")), ruinStone, AsciiSurfaceClass.Stone, Vector3.zero, Quaternion.identity, Vector3.one, true);

            var rubble = new LowPolyMesh();
            Vector3 c = Ground(k_Ruins.x, k_Ruins.y);
            rubble.Cylinder(c + new Vector3(3.5f, -0.1f, -2.2f), 0.48f, 0.4f, 1.1f, 6, 0f);    // broken stump
            rubble.Box(c + new Vector3(-4.5f, 1.0f, -3.5f), new Vector3(3.6f, 2.2f, 0.7f), Quaternion.Euler(0, 35f, 0));
            rubble.Box(c + new Vector3(-5.6f, 0.6f, 2.8f), new Vector3(2.6f, 1.3f, 0.7f), Quaternion.Euler(0, -60f, 4f));
            rubble.Box(c + new Vector3(0, 0.35f, 0), new Vector3(2.4f, 0.8f, 1.3f), Quaternion.Euler(0, 12f, 0)); // altar
            rubble.Cylinder(c + new Vector3(0, 0.75f, 0), 0.35f, 0.12f, 2.4f, 4, 45f);        // obelisk
            for (int i = 0; i < 14; i++)
            {
                float a = (float)s_Rng.NextDouble() * Mathf.PI * 2f;
                float r = 1.8f + (float)s_Rng.NextDouble() * 5f;
                Vector3 p = Ground(c.x + Mathf.Cos(a) * r, c.z + Mathf.Sin(a) * r);
                rubble.Box(p + Vector3.up * 0.06f, new Vector3(1.0f, 0.12f, 1.0f), Quaternion.Euler(0, (float)s_Rng.NextDouble() * 90f, 0));
            }
            MeshObject("Rubble", root, SaveMesh(rubble.ToMesh("Ruins_Rubble")), mossStone, AsciiSurfaceClass.Stone, Vector3.zero, Quaternion.identity, Vector3.one, true);

            var flame = SaveMesh(Build(m =>
            {
                m.Cone(new Vector3(0, 0, 0), 0.32f, 0.9f, 5, 0f);
                m.Cone(new Vector3(0.1f, 0, 0.05f), 0.18f, 0.6f, 5, 36f);
            }, "Ruins_SpiritFlame"));
            var flameGo = MeshObject("Spirit Flame", root, flame,
                Mat("Spirit_Fire", new Color(0.35f, 0.6f, 1f), 0f, 0f, new Color(0.6f, 1.6f, 5f)), AsciiSurfaceClass.Emissive,
                c + new Vector3(0, 3.2f, 0), Quaternion.identity, Vector3.one);
            flameGo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            var lightGo = new GameObject("Spirit Light");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.position = c + new Vector3(0, 3.8f, 0);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.35f, 0.55f, 1f);
            light.intensity = 12f;
            light.range = 19f;
            var flicker = lightGo.AddComponent<FlickerLight>();
            flicker.baseIntensity = 12f;
            flicker.speed = 1.5f;
            flicker.intensityVariation = 0.4f;
            var night = root.gameObject.AddComponent<NightLight>();
            night.lights = new[] { light };
            night.dayLevel = 0.1f;
        }

        // ------------------------------------------------------------------ vegetation

        static void BuildVegetationAndRocks(Transform world)
        {
            var trees = new GameObject("Trees").transform;
            trees.SetParent(world, false);
            var rocks = new GameObject("Rocks").transform;
            rocks.SetParent(world, false);
            var bushes = new GameObject("Bushes").transform;
            bushes.SetParent(world, false);

            var trunkMat = Mat("Tree_Trunk", new Color(0.27f, 0.23f, 0.21f));
            var pineMat = Mat("Tree_Pine", new Color(0.13f, 0.27f, 0.23f));
            var oakMats = new[] { Mat("Tree_Oak_A", new Color(0.20f, 0.37f, 0.28f)), Mat("Tree_Oak_B", new Color(0.25f, 0.39f, 0.32f)) };
            var bushMat = Mat("Bush", new Color(0.19f, 0.34f, 0.27f));
            var rockMat = Mat("Rock", new Color(0.35f, 0.40f, 0.44f));

            var trunk = SaveMesh(Build(m => m.Cylinder(Vector3.zero, 0.22f, 0.16f, 1.6f, 5), "Tree_Trunk"));
            var pines = new Mesh[3];
            for (int v = 0; v < pines.Length; v++)
            {
                int vv = v;
                pines[v] = SaveMesh(Build(m =>
                {
                    float y = 1.0f;
                    float r = 1.55f + vv * 0.12f;
                    for (int tier = 0; tier < 3 + (vv == 2 ? 1 : 0); tier++)
                    {
                        m.Cone(new Vector3(0, y, 0), r, 1.9f, 6, tier * 17f + vv * 9f);
                        y += 1.15f;
                        r *= 0.74f;
                    }
                }, $"Tree_Pine_{v}"));
            }
            var oaks = new Mesh[3];
            for (int v = 0; v < oaks.Length; v++)
            {
                int vv = v;
                oaks[v] = SaveMesh(Build(m =>
                {
                    m.Blob(new Vector3(0, 2.6f, 0), new Vector3(1.7f, 1.45f, 1.7f), 1, 0.16f, 900 + vv);
                    m.Blob(new Vector3(0.7f, 2.2f, 0.4f), new Vector3(1.0f, 0.9f, 1.0f), 0, 0.2f, 950 + vv);
                }, $"Tree_Oak_{v}"));
            }
            var bushMeshes = new Mesh[3];
            for (int v = 0; v < 3; v++)
            {
                int vv = v;
                bushMeshes[v] = SaveMesh(Build(m => m.Blob(new Vector3(0, 0.45f, 0), new Vector3(0.85f, 0.6f, 0.8f), 1, 0.22f, 700 + vv), $"Bush_{v}"));
            }
            var rockMeshes = new Mesh[5];
            for (int v = 0; v < 5; v++)
            {
                int vv = v;
                rockMeshes[v] = SaveMesh(Build(m => m.Blob(new Vector3(0, 0.3f, 0), new Vector3(1f, 0.65f, 0.85f), vv % 2, 0.3f, 300 + vv), $"Rock_{v}"));
            }

            int placedTrees = 0;
            for (int attempt = 0; attempt < 6000 && placedTrees < 230; attempt++)
            {
                float x = ((float)s_Rng.NextDouble() * 2f - 1f) * 70f;
                float z = ((float)s_Rng.NextDouble() * 2f - 1f) * 70f;
                var p = new Vector2(x, z);
                if (!IsFree(p, 2.6f)) continue;
                float h = Height(x, z);
                if (h > 13f || h < WaterLevel + 0.6f) continue;
                float forest = Mathf.PerlinNoise(x * 0.035f + 5f, z * 0.035f + 9f) + (z > 15f ? 0.18f : 0f) + (x < -30f ? 0.12f : 0f);
                if (forest < 0.52f && s_Rng.NextDouble() > 0.06) continue;

                bool pine = h > 3.5f || Mathf.PerlinNoise(x * 0.05f + 70f, z * 0.05f) > 0.55f;
                var tree = new GameObject(pine ? "Pine" : "Oak").transform;
                tree.SetParent(trees, false);
                tree.SetPositionAndRotation(new Vector3(x, h - 0.1f, z), Quaternion.Euler(0, (float)s_Rng.NextDouble() * 360f, 0));
                float s = 0.8f + (float)s_Rng.NextDouble() * 0.55f;
                tree.localScale = new Vector3(s, s * (0.9f + (float)s_Rng.NextDouble() * 0.25f), s);
                MeshObject("Trunk", tree, trunk, trunkMat, AsciiSurfaceClass.Wood, Vector3.zero, Quaternion.identity, Vector3.one);
                if (pine)
                    MeshObject("Crown", tree, pines[s_Rng.Next(pines.Length)], pineMat, AsciiSurfaceClass.Foliage, Vector3.zero, Quaternion.identity, Vector3.one);
                else
                    MeshObject("Crown", tree, oaks[s_Rng.Next(oaks.Length)], oakMats[s_Rng.Next(oakMats.Length)], AsciiSurfaceClass.Foliage, Vector3.zero, Quaternion.identity, Vector3.one);
                var col = tree.gameObject.AddComponent<CapsuleCollider>();
                col.radius = 0.3f;
                col.height = 3f;
                col.center = new Vector3(0, 1.5f, 0);
                placedTrees++;
            }

            for (int i = 0, placed = 0; i < 3000 && placed < 90; i++)
            {
                float x = ((float)s_Rng.NextDouble() * 2f - 1f) * 66f;
                float z = ((float)s_Rng.NextDouble() * 2f - 1f) * 66f;
                var p = new Vector2(x, z);
                if (!IsFree(p, 1.6f) || Vector2.Distance(p, k_Village) < 15f) continue;
                float h = Height(x, z);
                if (h > 9f || h < WaterLevel + 0.3f) continue;
                if (Mathf.PerlinNoise(x * 0.06f + 3f, z * 0.06f + 1f) < 0.45f) continue;
                float s = 0.6f + (float)s_Rng.NextDouble() * 0.7f;
                MeshObject("Bush", bushes, bushMeshes[s_Rng.Next(bushMeshes.Length)], bushMat, AsciiSurfaceClass.Foliage,
                    new Vector3(x, h - 0.08f, z), Quaternion.Euler(0, (float)s_Rng.NextDouble() * 360f, 0), new Vector3(s, s, s));
                placed++;
            }

            for (int i = 0, placed = 0; i < 3000 && placed < 70; i++)
            {
                float x = ((float)s_Rng.NextDouble() * 2f - 1f) * 72f;
                float z = ((float)s_Rng.NextDouble() * 2f - 1f) * 72f;
                var p = new Vector2(x, z);
                if (!IsFree(p, 1.4f) || Vector2.Distance(p, k_Village) < 16f) continue;
                float h = Height(x, z);
                if (h < WaterLevel) continue;
                float rim = p.magnitude / WorldHalfSize;
                float s = 0.4f + (float)s_Rng.NextDouble() * (rim > 0.6f ? 2.4f : 1.0f);
                var go = MeshObject("Rock", rocks, rockMeshes[s_Rng.Next(rockMeshes.Length)], rockMat, AsciiSurfaceClass.Stone,
                    new Vector3(x, h - 0.15f * s, z), Quaternion.Euler((float)s_Rng.NextDouble() * 20f, (float)s_Rng.NextDouble() * 360f, 0), new Vector3(s, s, s));
                if (s > 0.8f)
                {
                    var col = go.AddComponent<SphereCollider>();
                    col.radius = 0.7f;
                    col.center = new Vector3(0, 0.3f, 0);
                }
                placed++;
            }
        }

        static bool IsFree(Vector2 p, float pathClearance)
        {
            if (Vector2.Distance(p, k_Village) < 19f) return false;
            if (Vector2.Distance(p, k_Pond) < 13.2f) return false; // trees may stand on the bank and reflect
            if (Vector2.Distance(p, k_Ruins) < 10f) return false;
            if (Vector2.Distance(p, k_Camp) < 7f) return false;
            if (DistanceToRect(p, k_Field) < 2.5f) return false;
            if (DistanceToPaths(p) < pathClearance) return false;
            return true;
        }

        static void BuildBoundary(Transform world)
        {
            var root = new GameObject("World Boundary").transform;
            root.SetParent(world, false);
            const int segments = 48;
            const float radius = 70f;
            for (int i = 0; i < segments; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / segments;
                var wall = new GameObject($"Wall {i}");
                wall.transform.SetParent(root, false);
                wall.transform.SetPositionAndRotation(new Vector3(Mathf.Cos(a) * radius, 10f, Mathf.Sin(a) * radius),
                    Quaternion.Euler(0, -a * Mathf.Rad2Deg + 90f, 0));
                wall.AddComponent<BoxCollider>().size = new Vector3(radius * Mathf.PI * 2f / segments + 0.5f, 60f, 1f);
            }
        }

        // ------------------------------------------------------------------ player

        static GameObject BuildPlayer()
        {
            Vector3 spawn = Ground(0f, -6f) + Vector3.up * 0.05f;
            var player = new GameObject("Player");
            player.transform.position = spawn;

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0, 0.9f, 0);
            cc.stepOffset = 0.4f;
            cc.slopeLimit = 48f;

            player.AddComponent<PlayerMotor>();
            var input = player.AddComponent<PlayerInputReader>();
            var controller = player.AddComponent<PlayerController>();
            controller.input = input;
            player.AddComponent<AsciiSurface>().SurfaceClass = AsciiSurfaceClass.Exempt;
            player.AddComponent<WaterRippleEmitter>();

            var visual = new GameObject("Visual");
            visual.transform.SetParent(player.transform, false);
            var animator = visual.AddComponent<ProceduralHumanoidAnimator>();
            animator.motor = player.GetComponent<PlayerMotor>();
            AttachSwordsman(animator);

            player.GetComponent<AsciiSurface>().Apply();
            foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>())
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            return player;
        }

        static void AttachSwordsman(ProceduralHumanoidAnimator animator)
        {
            const string modelPath = Root + "/Art/Characters/Swordsman/Swordsman.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
                throw new System.InvalidOperationException($"Missing player model at {modelPath}.");

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, animator.transform);
            var root = instance.transform;
            animator.body = root.Find("Body");
            animator.head = root.Find("Body/Head");
            animator.leftArm = root.Find("Body/Left Arm");
            animator.rightArm = root.Find("Body/Right Arm");
            animator.leftLeg = root.Find("Left Leg");
            animator.rightLeg = root.Find("Right Leg");
            if (animator.body == null || animator.head == null || animator.leftArm == null ||
                animator.rightArm == null || animator.leftLeg == null || animator.rightLeg == null)
                throw new System.InvalidOperationException($"Missing locomotion pivots in {modelPath}.");
        }

        // ------------------------------------------------------------------ camera & environment

        static (Camera, IsometricCameraRig, AsciiCamera) BuildCamera(GameObject player)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 220f;
            go.AddComponent<AudioListener>();

            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = true;

            var rig = go.AddComponent<IsometricCameraRig>();
            rig.cam = cam;
            rig.target = player.transform;
            rig.zoomInput = player.GetComponent<PlayerInputReader>();
            rig.lockFraming = true;

            var ascii = go.AddComponent<AsciiCamera>();
            ascii.FocusTarget = player.transform;

            player.GetComponent<PlayerController>().viewTransform = go.transform;
            rig.SnapToTarget();
            return (cam, rig, ascii);
        }

        static DayNightCycle BuildEnvironment()
        {
            var env = new GameObject("Environment");

            var lightGo = new GameObject("Celestial Light");
            lightGo.transform.SetParent(env.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.85f;
            light.intensity = 2f;

            var skybox = AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "/Skybox.mat");
            if (skybox == null)
            {
                skybox = new Material(Shader.Find("Skybox/Procedural")) { name = "Skybox" };
                AssetDatabase.CreateAsset(skybox, MaterialDir + "/Skybox.mat");
            }

            var baseVolume = new GameObject("Global Volume").AddComponent<Volume>();
            baseVolume.transform.SetParent(env.transform, false);
            baseVolume.isGlobal = true;
            baseVolume.priority = 0;
            baseVolume.sharedProfile = BuildBaseProfile();

            var nightVolume = new GameObject("Night Volume").AddComponent<Volume>();
            nightVolume.transform.SetParent(env.transform, false);
            nightVolume.isGlobal = true;
            nightVolume.priority = 1;
            nightVolume.sharedProfile = BuildNightProfile();

            var cycle = env.AddComponent<DayNightCycle>();
            cycle.AssignDefaultCurves();
            cycle.celestialLight = light;
            cycle.skybox = skybox;
            cycle.nightVolume = nightVolume;
            cycle.timeOfDay = 20.5f;
            cycle.sunIntensity = 1.85f;
            cycle.moonColor = new Color(0.75f, 0.86f, 1f);
            cycle.Apply();
            return cycle;
        }

        static VolumeProfile BuildBaseProfile()
        {
            var profile = NewProfile("VolumeProfile_Base");
            // Override every ASCII parameter so the look never depends on the
            // project's default volume profile.
            var ascii = AddComponent<AsciiSettings>(profile, overrideAll: true);
            ascii.intensity.Override(1f);
            ascii.cellSize.Override(8);
            ascii.exposure.Override(1.35f);
            ascii.gamma.Override(1f);
            ascii.glyphBrightness.Override(1.55f);
            ascii.backgroundFill.Override(0.018f);
            ascii.backgroundColor.Override(new Color(0.0035f, 0.005f, 0.009f));
            ascii.saturation.Override(0.90f);
            ascii.sparkleDensity.Override(0.016f);
            ascii.edgeThreshold.Override(0.47f);
            ascii.exemptAscii.Override(0.86f);

            var tonemap = AddComponent<Tonemapping>(profile);
            tonemap.mode.Override(TonemappingMode.Neutral);

            var bloom = AddComponent<Bloom>(profile);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.45f);
            bloom.scatter.Override(0.65f);

            var color = AddComponent<ColorAdjustments>(profile);
            color.saturation.Override(-4f);
            color.contrast.Override(3f);

            var vignette = AddComponent<Vignette>(profile);
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.45f);
            return SaveProfile(profile);
        }

        static VolumeProfile BuildNightProfile()
        {
            var profile = NewProfile("VolumeProfile_Night");
            // Night mood comes mostly from cold moonlight and ambient; the grade
            // stays light so firelight keeps its warmth against the blue.
            var ascii = AddComponent<AsciiSettings>(profile);
            ascii.exposure.Override(2.4f);
            ascii.backgroundFill.Override(0f);
            ascii.glyphBrightness.Override(2.5f);
            ascii.colorNormalize.Override(0.04f);
            // Fireflies: sparse, self-lit, slowly pulsing; HDR so bloom makes them glow.
            ascii.sparkleColor.Override(new Color(1.1f, 1.7f, 1.25f, 1f));
            ascii.sparkleDensity.Override(0.009f);
            ascii.sparkleSelfLit.Override(1f);
            ascii.sparkleTwinkle.Override(1f);

            var color = AddComponent<ColorAdjustments>(profile);
            color.saturation.Override(-14f);
            color.colorFilter.Override(new Color(0.96f, 0.99f, 1.04f));

            var balance = AddComponent<WhiteBalance>(profile);
            balance.temperature.Override(-8f);

            var vignette = AddComponent<Vignette>(profile);
            vignette.intensity.Override(0.36f);

            var bloom = AddComponent<Bloom>(profile);
            bloom.intensity.Override(0.8f);
            return SaveProfile(profile);
        }

        static VolumeProfile NewProfile(string name)
        {
            string path = $"{SettingsDir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (existing != null)
                AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = name;
            AssetDatabase.CreateAsset(profile, path);
            return profile;
        }

        static T AddComponent<T>(VolumeProfile profile, bool overrideAll = false) where T : VolumeComponent
        {
            var component = profile.Add<T>(overrideAll);
            component.name = typeof(T).Name;
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        static VolumeProfile SaveProfile(VolumeProfile profile)
        {
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        // ------------------------------------------------------------------ helpers

        static Mesh Build(System.Action<LowPolyMesh> draw, string name)
        {
            var m = new LowPolyMesh();
            draw(m);
            return m.ToMesh(name);
        }

        static Mesh SaveMesh(Mesh mesh)
        {
            string path = $"{MeshDir}/{mesh.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Material Mat(string name, Color color, float smoothness = 0.08f, float metallic = 0f, Color? emission = null)
        {
            if (s_Materials.TryGetValue(name, out var cached))
                return cached;

            string path = $"{MaterialDir}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            material.enableInstancing = true;
            if (emission.HasValue)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission.Value);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
            }
            EditorUtility.SetDirty(material);
            s_Materials[name] = material;
            return material;
        }

        static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material material, AsciiSurfaceClass surface,
            Vector3 position, Quaternion rotation, Vector3 scale, bool addCollider = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(position, rotation);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderingLayerMask = AsciiSurfaceLayers.Apply(renderer.renderingLayerMask, surface);
            if (addCollider)
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
