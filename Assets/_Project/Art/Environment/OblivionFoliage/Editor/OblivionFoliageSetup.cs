// Oblivion-style foliage pack: one-click Unity setup.
// Menu: Tools > Oblivion Foliage > Set Up Materials and Prefabs
// Works with URP, HDRP and the Built-in pipeline (detected automatically).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class OblivionFoliageSetup
{
    enum Pipeline { BuiltIn, URP, HDRP }

    static string FindRoot()
    {
        var guid = AssetDatabase.FindAssets("OblivionFoliageSetup t:MonoScript").FirstOrDefault();
        if (guid == null) return "Assets/OblivionFoliage";
        var p = AssetDatabase.GUIDToAssetPath(guid);
        return Path.GetDirectoryName(Path.GetDirectoryName(p)).Replace('\\', '/');
    }

    static Pipeline Detect()
    {
        var rp = GraphicsSettings.currentRenderPipeline;
        if (rp == null) return Pipeline.BuiltIn;
        var n = rp.GetType().FullName ?? "";
        if (n.Contains("HighDefinition")) return Pipeline.HDRP;
        return Pipeline.URP;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    [MenuItem("Tools/Oblivion Foliage/Set Up Materials and Prefabs")]
    public static void Setup()
    {
        string root = FindRoot();
        var pipe = Detect();
        Debug.Log($"[OblivionFoliage] Setting up in {root} for {pipe} pipeline");
        try
        {
            AssetDatabase.StartAssetEditing();
            ConfigureTextures(root);
        }
        finally { AssetDatabase.StopAssetEditing(); }

        if (pipe == Pipeline.URP) InstallURPShader(root);
        var mats = CreateMaterials(root, pipe);
        ConfigureModels(root, mats);
        int n = CreatePrefabs(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[OblivionFoliage] Done: {mats.Count} materials, {n} prefabs in {root}/Prefabs");
    }

    // The URP wind shader ships as .txt so Built-in/HDRP projects don't try to compile URP includes.
    static void InstallURPShader(string root)
    {
        string src = Path.Combine(Directory.GetCurrentDirectory(), root, "Shaders/URPFoliageWind.shader.txt");
        string dst = root + "/Shaders/URPFoliageWind.shader";
        if (!File.Exists(src)) { Debug.LogWarning("[OblivionFoliage] URPFoliageWind.shader.txt missing; wind disabled"); return; }
        string text = File.ReadAllText(src);
        string full = Path.Combine(Directory.GetCurrentDirectory(), dst);
        if (!File.Exists(full) || File.ReadAllText(full) != text)
        {
            File.WriteAllText(full, text);
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
        }
    }

    // ------------------------------------------------------------------------------------------
    static void ConfigureTextures(string root)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { root + "/Textures" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) continue;
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.EndsWith("_Normal"))
            {
                ti.textureType = TextureImporterType.NormalMap;
            }
            else
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
            }
            if (name == "T_Foliage_Atlas")
            {
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = true;
                ti.mipMapsPreserveCoverage = true;   // keeps leaves from thinning out at distance
                ti.alphaTestReferenceValue = 0.5f;
                ti.wrapMode = TextureWrapMode.Clamp;
            }
            ti.SaveAndReimport();
        }
    }

    // ------------------------------------------------------------------------------------------
    static Dictionary<string, Material> CreateMaterials(string root, Pipeline pipe)
    {
        string dir = root + "/Materials";
        EnsureFolder(dir);
        var result = new Dictionary<string, Material>();
        Texture2D Tex(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{root}/Textures/{n}.png");

        result["M_Foliage"] = Make(dir, "M_Foliage", pipe, Tex("T_Foliage_Atlas"), null, Color.white, true, 0.2f, wind: true);
        result["M_Twig"] = Make(dir, "M_Twig", pipe, null, null, new Color(0.407f, 0.324f, 0.258f), false, 0.1f, wind: true); // Oak | Bark 2
        result["M_Mushroom_Cap"] = Make(dir, "M_Mushroom_Cap", pipe, null, null, new Color(0.62f, 0.33f, 0.18f), false, 0.4f);
        result["M_Mushroom_Stem"] = Make(dir, "M_Mushroom_Stem", pipe, null, null, new Color(0.80f, 0.76f, 0.62f), false, 0.15f);

        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { root + "/Textures" }))
        {
            var name = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
            if (name == "T_Foliage_Atlas" || name.EndsWith("_Normal") || !name.StartsWith("T_")) continue;
            var asset = name.Substring(2);
            result["M_" + asset] = Make(dir, "M_" + asset, pipe, Tex(name), Tex(name + "_Normal"), Color.white, false, 0.08f);
        }
        return result;
    }

    static Material Make(string dir, string name, Pipeline pipe, Texture2D albedo, Texture2D normal, Color color,
                         bool cutout, float smoothness, bool wind = false)
    {
        Shader shader = null;
        bool windShader = false;
        switch (pipe)
        {
            case Pipeline.URP:
                if (wind) shader = Shader.Find("OblivionFoliage/URP Foliage Wind");
                windShader = shader != null;
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
                break;
            case Pipeline.HDRP: shader = Shader.Find("HDRP/Lit"); break;
            default:
                shader = (cutout || wind) ? Shader.Find("OblivionFoliage/Builtin Leaves Two-Sided") : Shader.Find("Standard");
                break;
        }
        if (shader == null) shader = Shader.Find("Standard");

        string path = $"{dir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool isNew = mat == null;
        if (isNew) mat = new Material(shader);
        else mat.shader = shader;
        mat.name = name;

        switch (pipe)
        {
            case Pipeline.URP:
                mat.SetTexture("_BaseMap", albedo);
                mat.SetTexture("_MainTex", albedo);
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Smoothness", smoothness);
                mat.SetFloat("_Metallic", 0f);
                mat.SetTexture("_BumpMap", normal);   // null clears maps left by older builds
                if (normal) mat.EnableKeyword("_NORMALMAP"); else mat.DisableKeyword("_NORMALMAP");
                if (windShader)
                {
                    mat.SetFloat("_AlphaClip", cutout ? 1f : 0f);
                    mat.SetFloat("_Cutoff", 0.5f);
                    if (cutout) mat.EnableKeyword("_ALPHATEST_ON"); else mat.DisableKeyword("_ALPHATEST_ON");
                    mat.SetFloat("_Cull", (float)(cutout ? CullMode.Off : CullMode.Back));
                    mat.renderQueue = (int)(cutout ? RenderQueue.AlphaTest : RenderQueue.Geometry);
                    mat.doubleSidedGI = cutout;
                }
                else if (cutout)
                {
                    mat.SetFloat("_Surface", 0f);
                    mat.SetFloat("_AlphaClip", 1f);
                    mat.SetFloat("_Cutoff", 0.5f);
                    mat.EnableKeyword("_ALPHATEST_ON");
                    mat.SetFloat("_Cull", (float)CullMode.Off);
                    mat.SetOverrideTag("RenderType", "TransparentCutout");
                    mat.renderQueue = (int)RenderQueue.AlphaTest;
                    mat.doubleSidedGI = true;
                }
                else mat.SetFloat("_Cull", (float)CullMode.Back);
                break;

            case Pipeline.HDRP:
                mat.SetTexture("_BaseColorMap", albedo);
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Smoothness", smoothness);
                mat.SetFloat("_Metallic", 0f);
                mat.SetTexture("_NormalMap", normal);
                if (cutout)
                {
                    mat.SetFloat("_AlphaCutoffEnable", 1f);
                    mat.SetFloat("_AlphaCutoff", 0.5f);
                    mat.SetFloat("_DoubleSidedEnable", 1f);
                    mat.SetFloat("_DoubleSidedNormalMode", 2f); // None: keep the pack's rounded custom normals
                    mat.doubleSidedGI = true;
                }
                // Let HDRP rebuild keywords/passes from the properties above.
                var t = Type.GetType("UnityEditor.Rendering.HighDefinition.HDShaderUtils, Unity.RenderPipelines.HighDefinition.Editor");
                t?.GetMethod("ResetMaterialKeywords", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { mat });
                break;

            default:
                mat.mainTexture = albedo;
                mat.color = color;
                if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.5f);
                if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
                if (mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", normal);
                if (normal) mat.EnableKeyword("_NORMALMAP"); else mat.DisableKeyword("_NORMALMAP");
                if (cutout) mat.renderQueue = (int)RenderQueue.AlphaTest;
                break;
        }

        if (isNew) AssetDatabase.CreateAsset(mat, path);
        else EditorUtility.SetDirty(mat);
        return mat;
    }

    // ------------------------------------------------------------------------------------------
    static void ConfigureModels(string root, Dictionary<string, Material> mats)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { root + "/Models" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) continue;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importNormals = ModelImporterNormals.Import;      // keep the rounded foliage normals
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.importAnimation = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            foreach (var kv in mats)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            mi.SaveAndReimport();
        }
    }

    // ------------------------------------------------------------------------------------------
    static int CreatePrefabs(string root)
    {
        string dir = root + "/Prefabs";
        EnsureFolder(dir);
        int count = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { root + "/Models" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) continue;
            var name = Path.GetFileNameWithoutExtension(path);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                BuildPrefab(inst, name);
                PrefabUtility.SaveAsPrefabAsset(inst, $"{dir}/{name}.prefab");
                count++;
            }
            finally { Object.DestroyImmediate(inst); }
        }
        return count;
    }

    static void BuildPrefab(GameObject inst, string name)
    {
        Transform lod0 = null, lod1 = null, col = null;
        foreach (var t in inst.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.EndsWith("_LOD0")) lod0 = t;
            else if (t.name.EndsWith("_LOD1")) lod1 = t;
            else if (t.name.EndsWith("_Collider")) col = t;
        }

        if (col != null)
        {
            var mf = col.GetComponent<MeshFilter>();
            var mc = col.GetComponent<MeshCollider>();
            if (mc == null) mc = col.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf != null ? mf.sharedMesh : null;
            mc.convex = true;
            var r = col.GetComponent<MeshRenderer>();
            if (r != null) r.enabled = false;
        }

        bool small = name.Contains("Pebbles") || name.Contains("Grass") || name.Contains("Flower") ||
                     name.Contains("Debris") || name.Contains("Mushroom") || name.Contains("Fern") ||
                     name.Contains("Creeper");
        float cull = small ? 0.03f : 0.008f;

        var r0 = lod0 != null
            ? lod0.GetComponentsInChildren<Renderer>()
            : inst.GetComponentsInChildren<Renderer>().Where(r => col == null || r.transform != col).ToArray();
        var lods = new List<LOD>();
        if (lod1 != null)
        {
            lods.Add(new LOD(small ? 0.12f : 0.15f, r0));
            lods.Add(new LOD(cull, lod1.GetComponentsInChildren<Renderer>()));
        }
        else lods.Add(new LOD(cull, r0));

        // Project Aelid: tag the prefab with its ASCII surface class (Rendering Layer bits 1-7).
        // Looked up by reflection so the pack still compiles in projects without Aelid.Rendering.
        if (AsciiSurfaceType != null)
        {
            var surface = inst.GetComponent(AsciiSurfaceType);
            if (surface == null) surface = inst.AddComponent(AsciiSurfaceType);
            var prop = AsciiSurfaceType.GetProperty("SurfaceClass");
            if (prop != null) prop.SetValue(surface, Enum.Parse(prop.PropertyType, AsciiClassFor(name)));
        }

        var lg = inst.GetComponent<LODGroup>();
        if (lg == null) lg = inst.AddComponent<LODGroup>();
        lg.fadeMode = LODFadeMode.CrossFade;
        lg.animateCrossFading = true;
        lg.SetLODs(lods.ToArray());
        lg.RecalculateBounds();
    }

    static readonly Type AsciiSurfaceType = AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType("Aelid.Rendering.AsciiSurface", false))
        .FirstOrDefault(t => t != null);

    // Names of Aelid.Rendering.AsciiSurfaceClass values.
    static string AsciiClassFor(string name)
    {
        if (name.Contains("Grass")) return "Grass";
        if (name.Contains("Rock")) return "Stone";
        if (name.Contains("Log") || name.Contains("Stump")) return "Wood";
        if (name.Contains("Bush") || name.Contains("Vine") || name.Contains("Fern") || name.Contains("Flower"))
            return "Foliage";
        return "Default"; // mushrooms, leaf litter
    }

    // ------------------------------------------------------------------------------------------
    [MenuItem("Tools/Oblivion Foliage/Create Demo Scene")]
    public static void CreateDemoScene()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string root = FindRoot();
        if (AssetDatabase.FindAssets("t:Prefab", new[] { root + "/Prefabs" }).Length == 0) Setup();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var rows = new[] { "Plants", "Rocks", "Logs", "Bushes", "Vines" };
        string Cat(string n) =>
            n.StartsWith("SM_Bush") ? "Bushes" : n.StartsWith("SM_Rock") ? "Rocks" :
            n.StartsWith("SM_Log") || n.StartsWith("SM_Stump") ? "Logs" : n.StartsWith("SM_Vine") ? "Vines" : "Plants";

        var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { root + "/Prefabs" })
            .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
            .OrderBy(p => p.name).ToList();

        var stone = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        stone.color = new Color(0.36f, 0.34f, 0.31f);
        if (stone.HasProperty("_BaseColor")) stone.SetColor("_BaseColor", stone.color);
        var grass = new Material(stone) { color = new Color(0.25f, 0.30f, 0.12f) };
        if (grass.HasProperty("_BaseColor")) grass.SetColor("_BaseColor", grass.color);
        EnsureFolder(root + "/Demo");
        AssetDatabase.CreateAsset(stone, root + "/Demo/M_Demo_Stone.mat");
        AssetDatabase.CreateAsset(grass, root + "/Demo/M_Demo_Ground.mat");

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(10, 1, 10);
        ground.transform.position = new Vector3(0, 0, 12);
        ground.GetComponent<Renderer>().sharedMaterial = grass;

        for (int ri = 0; ri < rows.Length; ri++)
        {
            var items = prefabs.Where(p => Cat(p.name) == rows[ri]).ToList();
            var widths = items.Select(p => Bounds(p).size.x).ToList();
            float total = widths.Sum() + (items.Count - 1) * 1.0f;
            float x = -total / 2f;
            for (int i = 0; i < items.Count; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(items[i]);
                var b = Bounds(items[i]);
                float z = ri * 7f;
                go.transform.position = new Vector3(x - b.min.x, 0, z);
                // Wall/ledge vines face +Z (their forward points away from the wall).
                // Turn them toward the demo camera, which looks down +Z.
                bool wallVine = items[i].name == "SM_Vine_Hanging" || items[i].name == "SM_Vine_Ivy_Wall";
                if (wallVine)
                {
                    go.transform.rotation = Quaternion.Euler(0, 180, 0);
                    go.transform.position = new Vector3(x + b.max.x, 0, z);
                }
                if (items[i].name == "SM_Vine_Hanging")
                {
                    go.transform.position += Vector3.up * 2.6f;
                    Box("Ledge", new Vector3(go.transform.position.x, 1.3f, z + 0.5f), new Vector3(2.2f, 2.6f, 1f), stone);
                }
                if (items[i].name == "SM_Vine_Ivy_Wall")
                    Box("Wall", new Vector3(go.transform.position.x, 1.5f, z + 0.15f), new Vector3(2.8f, 3f, 0.3f), stone);
                x += widths[i] + 1.0f;
            }
        }

        var cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(0, 9, -14);
            cam.transform.rotation = Quaternion.Euler(28, 0, 0);
        }
        var windGo = new GameObject("Foliage Wind");
        windGo.AddComponent<OblivionFoliageWind>();
        var sun = Object.FindAnyObjectByType<Light>();
        if (sun != null) sun.transform.rotation = Quaternion.Euler(50, -30, 0);

        EditorSceneManager.SaveScene(scene, root + "/Demo/OblivionFoliage_Demo.unity");
        Debug.Log("[OblivionFoliage] Demo scene saved to " + root + "/Demo/OblivionFoliage_Demo.unity");
    }

    static Bounds Bounds(GameObject prefab)
    {
        var rs = prefab.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToArray();
        if (rs.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static void Box(string name, Vector3 center, Vector3 size, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = center;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = m;
    }
}
