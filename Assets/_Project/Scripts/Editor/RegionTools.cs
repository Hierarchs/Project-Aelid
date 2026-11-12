using System.Collections.Generic;
using System.Text;
using Aelid.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Aelid.EditorTools
{
    /// <summary>
    /// Generate and Clear for every <see cref="RegionArea"/> in a scene, like the
    /// Region Editor's Generate Now and Obliterate Now. Regions share ground, so
    /// they are always generated together. Only objects under each region's
    /// Generated child are created or removed; hand-placed objects are left alone
    /// and can block generation with a <see cref="RegionExclusion"/>.
    /// </summary>
    public static class RegionTools
    {
        [MenuItem("Aelid/World/Generate Regions")]
        static void GenerateMenu() => Debug.Log(Generate(SceneManager.GetActiveScene()));

        [MenuItem("Aelid/World/Clear Generated Objects")]
        static void ClearMenu() => Debug.Log($"[Aelid] Cleared {Clear(SceneManager.GetActiveScene())} generated objects.");

        public static List<RegionArea> FindAreas(Scene scene)
        {
            var areas = new List<RegionArea>();
            foreach (var root in scene.GetRootGameObjects())
                areas.AddRange(root.GetComponentsInChildren<RegionArea>());
            return areas;
        }

        public static int Clear(Scene scene)
        {
            int removed = 0;
            foreach (var area in FindAreas(scene))
            {
                var generated = area.GeneratedRoot;
                if (generated == null)
                    continue;
                removed += CountGenerated(generated);
                Object.DestroyImmediate(generated.gameObject);
            }
            if (removed > 0)
                EditorSceneManager.MarkSceneDirty(scene);
            return removed;
        }

        /// <summary>Clears and regenerates all regions in the scene. Returns a per-rule summary.</summary>
        public static string Generate(Scene scene)
        {
            var areas = FindAreas(scene);
            Clear(scene);

            var exclusions = new List<ExclusionShape>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var zone in root.GetComponentsInChildren<RegionExclusion>())
                    exclusions.Add(zone.ToShape());

            var specs = new List<RegionSpec>();
            foreach (var area in areas)
            {
                if (area.rules == null || area.terrain == null)
                    Debug.LogWarning($"[Aelid] Region '{area.name}' needs a rule set and a terrain collider; skipped.", area);
                specs.Add(area.ToSpec());
            }

            var stats = new RegionGenerationStats();
            var placements = RegionGenerator.Generate(specs, exclusions, stats);

            var groups = new Dictionary<(int region, int rule), Transform>();
            var counts = new Dictionary<(int region, int rule), int>();
            int missing = 0;
            foreach (var placement in placements)
            {
                var area = areas[placement.region];
                var rule = area.rules.rules[placement.rule];
                var prefab = rule.variants != null && rule.variants.Length > 0 ? rule.variants[placement.variant] : null;
                if (prefab == null)
                {
                    missing++;
                    continue;
                }

                var groupKey = (placement.region, placement.rule);
                if (!groups.TryGetValue(groupKey, out var group))
                    groups.Add(groupKey, group = GroupFor(area, rule.name));
                counts[groupKey] = counts.TryGetValue(groupKey, out int n) ? n + 1 : 1;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
                instance.transform.SetPositionAndRotation(placement.position, placement.rotation * prefab.transform.localRotation);
                instance.transform.localScale = prefab.transform.localScale * placement.scale;
            }

            EditorSceneManager.MarkSceneDirty(scene);

            var summary = new StringBuilder();
            summary.Append($"[Aelid] Generated {placements.Count - missing} objects in {areas.Count} regions " +
                           "(placed / candidates, then the main reasons candidates were dropped).");
            for (int r = 0; r < areas.Count; r++)
            {
                if (areas[r].rules == null)
                    continue;
                summary.Append($"\n  {areas[r].name}");
                for (int i = 0; i < areas[r].rules.rules.Count; i++)
                {
                    counts.TryGetValue((r, i), out int placed);
                    stats.candidates.TryGetValue((r, i), out int tried);
                    summary.Append($"\n    {areas[r].rules.rules[i].name}: {placed} / {tried}");
                    if (stats.rejected.TryGetValue((r, i), out var reasons))
                    {
                        string separator = " (";
                        for (int k = 0; k < reasons.Length; k++)
                            if (reasons[k] > 0)
                            {
                                summary.Append($"{separator}{(RegionRejection)k} {reasons[k]}");
                                separator = ", ";
                            }
                        summary.Append(')');
                    }
                }
            }
            if (missing > 0)
                summary.Append($"\n  {missing} placements skipped: their rules have no prefab variants.");
            return summary.ToString();
        }

        public static int CountGenerated(Transform generatedRoot)
        {
            int count = 0;
            foreach (Transform group in generatedRoot)
                count += group.childCount;
            return count;
        }

        static Transform GroupFor(RegionArea area, string ruleName)
        {
            var root = area.GeneratedRoot;
            if (root == null)
            {
                root = new GameObject(RegionArea.GeneratedRootName).transform;
                root.SetParent(area.transform, false);
            }
            var group = root.Find(ruleName);
            if (group == null)
            {
                group = new GameObject(ruleName).transform;
                group.SetParent(root, false);
            }
            return group;
        }
    }

    [CustomEditor(typeof(RegionArea))]
    sealed class RegionAreaEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var area = (RegionArea)target;
            var generated = area.GeneratedRoot;
            int count = generated != null ? RegionTools.CountGenerated(generated) : 0;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                $"{count} generated objects in this region.\n" +
                "Generate rebuilds every region in the scene, because regions share ground: higher-priority regions " +
                "and earlier rules claim it first. Only objects under 'Generated' are replaced. Drag the outline " +
                "corners in the Scene view.",
                MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate All Regions"))
                    Debug.Log(RegionTools.Generate(area.gameObject.scene));
                if (GUILayout.Button("Clear Generated"))
                    RegionTools.Clear(area.gameObject.scene);
            }
        }

        void OnSceneGUI()
        {
            var area = (RegionArea)target;
            Vector3 origin = area.transform.position;
            Handles.color = area.mapColor;
            for (int i = 0; i < area.outline.Count; i++)
            {
                var corner = origin + new Vector3(area.outline[i].x, 0f, area.outline[i].y);
                float size = HandleUtility.GetHandleSize(corner) * 0.08f;
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.Slider2D(corner, Vector3.up, Vector3.right, Vector3.forward, size, Handles.DotHandleCap, Vector2.zero);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(area, "Move Region Corner");
                    area.outline[i] = new Vector2(moved.x - origin.x, moved.z - origin.z);
                }
            }
        }
    }
}
