// Exports the foliage kit (models, textures, materials, prefabs, shaders, scripts) as a .unitypackage.
// Menu: Tools > Oblivion Foliage > Export Package
// Batch: Unity -batchmode -projectPath <proj> -executeMethod OblivionFoliageExport.ExportFromCommandLine [-exportPath <file>]
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class OblivionFoliageExport
{
    const string DefaultFile = "ArtSource/OblivionFoliage/Exports/OblivionFoliage.unitypackage";

    static string Root()
    {
        var guid = AssetDatabase.FindAssets("OblivionFoliageSetup t:MonoScript").First();
        var p = AssetDatabase.GUIDToAssetPath(guid);
        return Path.GetDirectoryName(Path.GetDirectoryName(p)).Replace('\\', '/');
    }

    [MenuItem("Tools/Oblivion Foliage/Export Package")]
    static void ExportMenu()
    {
        var file = EditorUtility.SaveFilePanel("Export Oblivion Foliage", "", "OblivionFoliage", "unitypackage");
        if (!string.IsNullOrEmpty(file)) Export(file);
    }

    public static void Export(string file)
    {
        string root = Root();
        OblivionFoliageSetup.Setup(); // so the materials and prefabs in the package are current
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
        AssetDatabase.ExportPackage(root, file, ExportPackageOptions.Recurse);
        Debug.Log($"[OblivionFoliage] Exported {root} to {Path.GetFullPath(file)}");
    }

    public static void ExportFromCommandLine()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-exportPath");
            Export(i >= 0 && i + 1 < args.Length ? args[i + 1] : DefaultFile);
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }
}
