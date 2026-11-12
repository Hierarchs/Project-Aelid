using System.Collections.Generic;
using Aelid.Rendering;
using Aelid.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Aelid.EditorTools
{
    // Region generation for the vertical slice, modelled on Oblivion's Region Editor:
    // regions are outlines with rule tables, and everything they place is baked into
    // the scene under each region's "Generated" child.
    public static partial class VerticalSliceBuilder
    {
        const string RegionRulesDir = Root + "/World/Regions";
        const string PrefabDir = Root + "/Generated/Prefabs";
        const string FoliagePrefabDir = Root + "/Art/Environment/OblivionFoliage/Prefabs";

        const float TreeLine = 13f;                    // no trees above this height (m)

        static readonly Vector2[] k_Northwood =
        {
            new(-76f, -48f), new(-46f, -44f), new(-40f, -28f), new(-36f, -4f), new(-24f, 10f), new(-12f, 20f),
            new(0f, 26f), new(20f, 26f), new(34f, 21f), new(52f, 21f), new(76f, 23f), new(76f, 76f), new(-76f, 76f),
        };

        static readonly Vector2[] k_WholeMap = { new(-76f, -76f), new(76f, -76f), new(76f, 76f), new(-76f, 76f) };

        [MenuItem("Aelid/World/Reset Slice Region Rules")]
        static void ResetRegionRulesMenu()
        {
            if (!EditorUtility.DisplayDialog("Reset region rules?",
                    $"This overwrites the rule sets in {RegionRulesDir} with the code defaults.", "Reset", "Cancel"))
                return;
            EnsureRegionRules(reset: true);
            AssetDatabase.SaveAssets();
        }

        // Groups made by the scatter that regions replace.
        static readonly string[] k_LegacyScatter = { "Trees", "Rocks", "Bushes" };

        [MenuItem("Aelid/World/Install Slice Regions in Open Scene")]
        static void InstallRegionsMenu()
        {
            var scene = SceneManager.GetActiveScene();
            if (!EditorUtility.DisplayDialog("Install slice regions?",
                    $"In '{scene.name}', this removes the old random scatter ({string.Join(", ", k_LegacyScatter)}) and any " +
                    "existing Regions, then adds and generates Northwood, Heath and Highlands. Everything else in the scene " +
                    "stays as it is.", "Install", "Cancel"))
                return;
            InstallRegions(scene);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>Batch-mode entry point: swaps the scatter for regions in the saved slice scene, without a rebuild.</summary>
        public static void InstallRegionsFromCommandLine()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            InstallRegions(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        static void InstallRegions(Scene scene)
        {
            Transform world = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "World")
                    world = root.transform;
            if (world == null)
                throw new System.InvalidOperationException($"Scene '{scene.name}' has no World root.");

            foreach (var name in k_LegacyScatter)
            {
                var legacy = world.Find(name);
                if (legacy != null)
                    Object.DestroyImmediate(legacy.gameObject);
            }
            var previous = world.Find("Regions");
            if (previous != null)
                Object.DestroyImmediate(previous.gameObject);

            BuildRegions(world);
            var boundary = world.Find("World Boundary");
            if (boundary != null)
                world.Find("Regions").SetSiblingIndex(boundary.GetSiblingIndex());
        }

        static void BuildRegions(Transform world)
        {
            var ground = world.Find("Terrain/Terrain Collider").GetComponent<MeshCollider>();
            var (northwood, heath, highlands) = EnsureRegionRules(reset: false);

            var root = new GameObject("Regions").transform;
            root.SetParent(world, false);
            AddRegion(root, "Highlands", highlands, ground, k_WholeMap, priority: 20, falloff: 0f, overrideLower: false, seed: 303, new Color(0.7f, 0.7f, 0.75f));
            AddRegion(root, "Northwood", northwood, ground, k_Northwood, priority: 10, falloff: 6f, overrideLower: true, seed: 101, new Color(0.2f, 0.6f, 0.3f));
            AddRegion(root, "Heath", heath, ground, k_WholeMap, priority: 0, falloff: 0f, overrideLower: false, seed: 202, new Color(0.75f, 0.7f, 0.3f));

            // Keep-clear zones: the same areas the old scatter avoided, plus everything hand-placed.
            var keepClear = new GameObject("Keep Clear").transform;
            keepClear.SetParent(root, false);
            AddCircleZone(keepClear, "Village", k_Village, 19f);
            AddCircleZone(keepClear, "Pond", k_Pond, 13.2f); // trees may stand on the bank and reflect
            AddCircleZone(keepClear, "Ruins", k_Ruins, 10f);
            AddCircleZone(keepClear, "Camp", k_Camp, 7f);
            AddPathZone(keepClear, "Field",
                new[] { new Vector2(k_Field.xMin, k_Field.center.y), new Vector2(k_Field.xMax, k_Field.center.y) },
                k_Field.height * 0.5f + 2.5f);
            for (int i = 0; i < k_Paths.Length; i++)
                AddPathZone(keepClear, $"Path {i}", k_Paths[i], PathHalfWidth + 0.6f);

            // Older scenes may lack some of these groups.
            var grove = world.Find("Oak Grove Trees");
            if (grove != null)
                foreach (Transform oak in grove)
                    KeepClear(oak.gameObject, 2.6f);
            var cloister = world.Find("Broken Cloister");
            if (cloister != null)
                KeepClear(cloister.gameObject, FootprintRadius(cloister.gameObject, corners: true) + 0.5f);
            foreach (var groupName in new[] { "Woodland Props", "Woodland Understory" })
            {
                var props = world.Find(groupName);
                if (props == null)
                    continue;
                foreach (Transform group in props)
                    foreach (Transform prop in group)
                        KeepClear(prop.gameObject, FootprintRadius(prop.gameObject, corners: false));
            }

            Debug.Log(RegionTools.Generate(world.gameObject.scene));
        }

        static void AddRegion(Transform parent, string name, RegionRuleSet rules, Collider ground, Vector2[] outline,
            int priority, float falloff, bool overrideLower, int seed, Color color)
        {
            var area = new GameObject(name).AddComponent<RegionArea>();
            area.transform.SetParent(parent, false);
            area.rules = rules;
            area.terrain = ground;
            area.outline = new List<Vector2>(outline);
            area.priority = priority;
            area.edgeFalloff = falloff;
            area.overrideLower = overrideLower;
            area.seed = seed;
            area.mapColor = color;
        }

        static void AddCircleZone(Transform parent, string name, Vector2 centre, float radius)
        {
            var zone = new GameObject(name).AddComponent<RegionExclusion>();
            zone.transform.SetParent(parent, false);
            zone.transform.position = new Vector3(centre.x, 0f, centre.y);
            zone.radius = radius;
        }

        static void AddPathZone(Transform parent, string name, Vector2[] points, float halfWidth)
        {
            var zone = new GameObject(name).AddComponent<RegionExclusion>();
            zone.transform.SetParent(parent, false);
            zone.shape = RegionExclusion.Shape.Path;
            zone.points = new List<Vector2>(points);
            zone.radius = halfWidth;
        }

        static void KeepClear(GameObject target, float radius)
        {
            if (!target.TryGetComponent<RegionExclusion>(out var zone)) // re-installs reuse the existing zone
                zone = target.AddComponent<RegionExclusion>();
            zone.shape = RegionExclusion.Shape.Circle;
            zone.radius = radius;
        }

        /// <summary>Horizontal radius of a scene object's visible meshes around its pivot, in world metres.</summary>
        static float FootprintRadius(GameObject go, bool corners)
        {
            // Mesh bounds, not Renderer.bounds: a disabled renderer reports an empty box at the
            // world origin, which made every prop's keep-clear zone reach back to (0, 0).
            Vector3 pivot = go.transform.position;
            float best = 0f, sumSq = 0f;
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null || !filter.TryGetComponent<MeshRenderer>(out var renderer) || !renderer.enabled)
                    continue;
                Bounds b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = filter.transform.TransformPoint(Corner(b, i)) - pivot;
                    best = Mathf.Max(best, Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z)));
                    sumSq = Mathf.Max(sumSq, p.x * p.x + p.z * p.z);
                }
            }
            if (best <= 0f)
                return 0.5f;
            return corners ? Mathf.Sqrt(sumSq) : best;
        }

        /// <summary>
        /// Horizontal radius of a prefab's visible meshes at scale 1, like the Region Editor's
        /// "Reset Radius to Size". Rule defaults scale it so related props can nestle.
        /// </summary>
        static float PrefabFootprint(GameObject prefab)
        {
            float best = 0f;
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null || !filter.TryGetComponent<MeshRenderer>(out var renderer) || !renderer.enabled)
                    continue;
                Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Bounds b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = toRoot.MultiplyPoint3x4(Corner(b, i));
                    best = Mathf.Max(best, new Vector2(p.x, p.z).magnitude);
                }
            }
            return best > 0f ? best : 0.5f;
        }

        static Vector3 Corner(Bounds b, int i) =>
            new((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);

        // ------------------------------------------------------------------ prefabs

        static GameObject Kit(string asset) => LoadWoodlandPrefab($"{FoliagePrefabDir}/SM_{asset}.prefab");

        /// <summary>A forest oak: the Oak Grove model at half size with ASCII classes and a trunk collider.</summary>
        static GameObject ForestOakPrefab(string variant)
        {
            var root = new GameObject($"Oak_{variant}");
            AttachOakModel(root.transform, variant, 0.5f);
            var trunk = root.AddComponent<CapsuleCollider>();
            trunk.radius = 0.3f;
            trunk.height = 3f;
            trunk.center = new Vector3(0f, 1.5f, 0f);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/Forest_Oak_{variant}.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>The faceted cliff boulders, using the Rock material's Stone shader preset.</summary>
        static GameObject[] CragBoulderPrefabs()
        {
            var material = Mat("Rock", new Color(0.35f, 0.40f, 0.44f));
            var prefabs = new GameObject[5];
            for (int v = 0; v < prefabs.Length; v++)
            {
                int variant = v;
                var mesh = SaveMesh(Build(m => m.Blob(new Vector3(0, 0.3f, 0), new Vector3(1f, 0.65f, 0.85f), variant % 2, 0.3f, 300 + variant), $"Rock_{variant}"));
                var root = MeshObject($"Crag_Boulder_{variant}", null, mesh, material, AsciiSurfaceClass.Stone, Vector3.zero, Quaternion.identity, Vector3.one);
                var collider = root.AddComponent<SphereCollider>();
                collider.radius = 0.7f;
                collider.center = new Vector3(0f, 0.3f, 0f);
                prefabs[v] = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/Crag_Boulder_{variant}.prefab");
                Object.DestroyImmediate(root);
            }
            return prefabs;
        }

        // ------------------------------------------------------------------ rule tables

        static RegionRuleSet RuleSet(string name, bool reset, System.Action<RegionRuleSet> fill)
        {
            string path = $"{RegionRulesDir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<RegionRuleSet>(path);
            if (existing != null && !reset)
                return existing;

            var fresh = ScriptableObject.CreateInstance<RegionRuleSet>();
            fill(fresh);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(fresh, path);
                return fresh;
            }
            EditorUtility.CopySerialized(fresh, existing);
            Object.DestroyImmediate(fresh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        /// <summary>
        /// Prefabs are rebuilt every time (they come from code); rule sets are created
        /// once and then left alone, so Inspector tuning survives scene rebuilds.
        /// </summary>
        static (RegionRuleSet northwood, RegionRuleSet heath, RegionRuleSet highlands) EnsureRegionRules(bool reset)
        {
            EnsureFolder(RegionRulesDir);
            EnsureFolder(PrefabDir);

            GameObject broadcrown = ForestOakPrefab("Broadcrown"), splitfork = ForestOakPrefab("Splitfork");
            GameObject windlean = ForestOakPrefab("Windlean"), ancient = ForestOakPrefab("Ancient");
            var crags = CragBoulderPrefabs();
            GameObject fern = Kit("Fern"), mushrooms = Kit("Mushroom_Cluster"), litter = Kit("Ground_Debris");
            GameObject grass = Kit("Grass_Clump"), flowers = Kit("Flower_Patch"), creeper = Kit("Vine_Ground_Creeper");
            GameObject broadleaf = Kit("Bush_Broadleaf_Round"), tallBush = Kit("Bush_SmallLeaf_Tall");
            GameObject flowering = Kit("Bush_Flowering"), bramble = Kit("Bush_Bramble_Dry");
            GameObject logLong = Kit("Log_Fallen_Long"), logHollow = Kit("Log_Hollow"), logShort = Kit("Log_Short"), stump = Kit("Stump");
            GameObject boulder = Kit("Rock_Boulder_Large"), rockMedium = Kit("Rock_Medium"), rockSmall = Kit("Rock_Small");
            GameObject rockCluster = Kit("Rock_Cluster"), pebbles = Kit("Rock_Pebbles"), slab = Kit("Rock_Slab");

            // Footprints: plants overlap their neighbours' leaves a little; rocks and logs don't.
            float Plant(GameObject p) => Mathf.Round(PrefabFootprint(p) * 0.7f * 100f) / 100f;
            float Solid(GameObject p) => Mathf.Round(PrefabFootprint(p) * 0.9f * 100f) / 100f;
            float Long(GameObject p) => Mathf.Round(PrefabFootprint(p) * 0.8f * 100f) / 100f; // logs: most of their half-length

            var northwood = RuleSet("Northwood", reset, set =>
            {
                set.treeSpacing = 3f;
                set.rules = new List<RegionObjectRule>
                {
                    // The canopy. Clustering pulls oaks into groves; patchiness opens glades.
                    new() { name = "Oak", variants = new[] { broadcrown, splitfork, windlean }, isTree = true,
                        density = 9f, radius = 0.7f, clustering = 35f, patchiness = 0.55f, patchSize = 22f,
                        maxSlope = 40f, maxHeight = TreeLine,
                        sink = 0.1f, sinkVariance = 0.05f, sizeVariance = 0.3f, tiltVariance = 3f },
                    new() { name = "Oak Ferns", parent = "Oak", variants = new[] { fern },
                        density = 8f, radius = Plant(fern), radiusFromParent = 2.6f, maxSlope = 40f, sizeVariance = 0.25f },
                    new() { name = "Oak Mushrooms", parent = "Oak", variants = new[] { mushrooms },
                        density = 5f, radius = Plant(mushrooms), radiusFromParent = 1f, sizeVariance = 0.25f },
                    new() { name = "Oak Leaf Litter", parent = "Oak", variants = new[] { litter },
                        density = 6f, radius = Plant(litter), radiusFromParent = 2f, conformToSlope = true, sink = 0.02f },

                    // A rare old giant, ringed by ferns.
                    new() { name = "Ancient Oak", variants = new[] { ancient }, isTree = true,
                        density = 1f, radius = 1f, maxSlope = 25f, maxHeight = 12f,
                        scale = 1.1f, sizeVariance = 0.15f, sink = 0.12f },
                    new() { name = "Ancient Ferns", parent = "Ancient Oak", variants = new[] { fern },
                        density = 20f, radius = Plant(fern), radiusFromParent = 3f, sizeVariance = 0.25f },

                    // Deadfall, with mushrooms and creeper on it.
                    new() { name = "Fallen Log", variants = new[] { logLong, logHollow, logShort },
                        density = 10f, radius = Long(logLong), maxSlope = 30f,
                        conformToSlope = true, sink = 0.05f, sizeVariance = 0.2f },
                    new() { name = "Log Mushrooms", parent = "Fallen Log", variants = new[] { mushrooms },
                        density = 6f, radius = Plant(mushrooms), radiusFromParent = 1f },
                    new() { name = "Log Creeper", parent = "Fallen Log", variants = new[] { creeper },
                        density = 10f, radius = Plant(creeper), radiusFromParent = 1f, conformToSlope = true },
                    new() { name = "Stump", variants = new[] { stump },
                        density = 0.8f, radius = Solid(stump), maxSlope = 30f, sizeVariance = 0.2f },

                    // Understory.
                    new() { name = "Understory Bush", variants = new[] { broadleaf, tallBush },
                        density = 6f, radius = Plant(broadleaf), clustering = 45f, patchiness = 0.6f, patchSize = 14f,
                        maxSlope = 40f, sink = 0.05f, sizeVariance = 0.25f },
                    new() { name = "Fern Bed", variants = new[] { fern },
                        density = 6f, radius = Plant(fern), clustering = 60f, patchiness = 0.8f, patchSize = 10f,
                        maxSlope = 40f, sizeVariance = 0.25f },

                    // Stones, mossy with ferns.
                    new() { name = "Forest Rock", variants = new[] { rockMedium, rockSmall, rockCluster },
                        density = 2f, radius = Solid(rockMedium), conformToSlope = true, tiltVariance = 8f,
                        sink = 0.08f, sinkVariance = 0.05f, sizeVariance = 0.3f },
                    new() { name = "Rock Ferns", parent = "Forest Rock", variants = new[] { fern },
                        density = 18f, radius = Plant(fern), radiusFromParent = 1.2f },
                    new() { name = "Pebbles", variants = new[] { pebbles },
                        density = 1.5f, radius = Solid(pebbles), conformToSlope = true },
                };
            });

            var heath = RuleSet("Heath", reset, set =>
            {
                set.treeSpacing = 3f;
                set.rules = new List<RegionObjectRule>
                {
                    // Open country with copses: few oaks, strongly clustered, with bushes at their feet.
                    new() { name = "Copse Oak", variants = new[] { broadcrown, windlean, splitfork }, isTree = true,
                        density = 2.2f, radius = 0.7f, clustering = 70f, patchiness = 0.8f, patchSize = 26f,
                        maxSlope = 40f, maxHeight = TreeLine,
                        sink = 0.1f, sinkVariance = 0.05f, sizeVariance = 0.3f, tiltVariance = 3f },
                    new() { name = "Copse Bush", parent = "Copse Oak", variants = new[] { broadleaf, flowering },
                        density = 18f, radius = Plant(broadleaf), radiusFromParent = 3f, sink = 0.05f, sizeVariance = 0.25f },
                    new() { name = "Copse Ferns", parent = "Copse Oak", variants = new[] { fern },
                        density = 10f, radius = Plant(fern), radiusFromParent = 2.5f, sizeVariance = 0.25f },

                    new() { name = "Heath Bush", variants = new[] { flowering, tallBush, broadleaf },
                        density = 3.5f, radius = Plant(broadleaf), clustering = 50f, patchiness = 0.5f, patchSize = 18f,
                        maxSlope = 40f, sink = 0.05f, sizeVariance = 0.25f },
                    new() { name = "Bramble", variants = new[] { bramble },
                        density = 1.2f, radius = Plant(bramble), minHeight = 0.5f, maxHeight = 14f, sizeVariance = 0.2f },

                    // Glacial boulders with rubble and grass around them.
                    new() { name = "Boulder", variants = new[] { boulder },
                        density = 0.8f, radius = Solid(boulder), maxSlope = 30f, conformToSlope = true, tiltVariance = 6f,
                        sink = 0.15f, sinkVariance = 0.05f, sizeVariance = 0.25f },
                    new() { name = "Boulder Rubble", parent = "Boulder", variants = new[] { rockSmall, pebbles },
                        density = 8f, radius = Solid(rockSmall), radiusFromParent = 1.6f, conformToSlope = true, sizeVariance = 0.3f },
                    new() { name = "Boulder Grass", parent = "Boulder", variants = new[] { grass },
                        density = 18f, radius = Plant(grass), radiusFromParent = 1.4f },
                    new() { name = "Field Stone", variants = new[] { slab, rockMedium },
                        density = 1.2f, radius = Solid(slab), conformToSlope = true, sink = 0.1f, sizeVariance = 0.25f },

                    // Meadow plants in drifts.
                    new() { name = "Wildflowers", variants = new[] { flowers },
                        density = 3.5f, radius = Plant(flowers), clustering = 60f, patchiness = 0.85f, patchSize = 12f,
                        maxSlope = 30f, sizeVariance = 0.2f },
                    new() { name = "Tall Grass", variants = new[] { grass },
                        density = 3.5f, radius = Plant(grass), clustering = 50f, patchiness = 0.6f, patchSize = 9f,
                        sizeVariance = 0.25f },
                    new() { name = "Lone Log", variants = new[] { logShort, logLong },
                        density = 1.2f, radius = Long(logLong), maxSlope = 20f, conformToSlope = true },
                };
            });

            var highlands = RuleSet("Highlands", reset, set =>
            {
                set.treeSpacing = 3f;
                set.rules = new List<RegionObjectRule>
                {
                    // Height-gated: this region covers the whole map but only acts on the rising rim.
                    new() { name = "Crag Boulder", variants = crags,
                        density = 5f, radius = 1.3f, minHeight = 5f, conformToSlope = true, tiltVariance = 15f,
                        scale = 1.5f, sizeVariance = 0.45f, sink = 0.25f, sinkVariance = 0.15f },
                    new() { name = "Scree", parent = "Crag Boulder", variants = new[] { rockSmall, pebbles, rockCluster },
                        density = 2f, radius = Solid(rockSmall), radiusFromParent = 2f, conformToSlope = true, sizeVariance = 0.3f },
                    new() { name = "Ledge Slab", variants = new[] { slab },
                        density = 4f, radius = Solid(slab), minSlope = 14f, minHeight = 3f, conformToSlope = true, sizeVariance = 0.3f },
                    new() { name = "Upland Bramble", variants = new[] { bramble },
                        density = 4f, radius = Plant(bramble), clustering = 40f, minHeight = 4f, maxHeight = TreeLine, sizeVariance = 0.2f },
                    new() { name = "Windswept Oak", variants = new[] { windlean }, isTree = true,
                        density = 0.8f, radius = 0.7f, minHeight = 6f, maxHeight = 12.5f,
                        sink = 0.1f, sizeVariance = 0.2f, tiltVariance = 6f },
                };
            });

            return (northwood, heath, highlands);
        }
    }
}
