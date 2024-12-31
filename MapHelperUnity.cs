#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using UnityEditor;
using UnityEngine;

public class MapHelperUnity : EditorWindow
{
    private const string OutputFolderName = "ForConsoleTextures";

    private const string CurrentVersion = "1.0.0";
    private const string VersionUrl = "https://raw.githubusercontent.com/Jeefrect/MapHelper/main/version";
    private const string ScriptUrl = "https://raw.githubusercontent.com/Jeefrect/MapHelper/main/MapHelperUnity.cs";
    private static bool updateChecked = false;

    [MenuItem("Tools/Compress Textures in Scene/512px")]
    public static void CompressTexturesInScene512()
    {
        CompressTexturesInScene(512);
    }

    [MenuItem("Tools/Compress Textures in Scene/1024px")]
    public static void CompressTexturesInScene1024()
    {
        CompressTexturesInScene(1024);
    }

    public static void CompressTexturesInScene(int maxSize)
    {
        CheckForScriptUpdateOnce();

        string outputFolderPath = Path.Combine(UnityEngine.Application.dataPath, $"{OutputFolderName}_{maxSize}px");
        if (Directory.Exists(outputFolderPath))
            Directory.Delete(outputFolderPath, true);
        Directory.CreateDirectory(outputFolderPath);

        AssetDatabase.Refresh();

        var textures = FindTexturesInScene();

        foreach (var texturePath in textures)
        {
            Texture2D originalTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (originalTexture == null) continue;

            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null) continue;

            TextureImporterNPOTScale originalNPOTScale = importer.npotScale;
            TextureImporterType originalType = importer.textureType;

            importer.textureType = TextureImporterType.Default;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.isReadable = true;
            importer.SaveAndReimport();

            if (originalTexture.width > maxSize || originalTexture.height > maxSize)
            {
                Texture2D resizedTexture = ResizeTexture(originalTexture, maxSize);
                SaveResizedTexture(resizedTexture, texturePath, outputFolderPath);
                UnityEngine.Object.DestroyImmediate(resizedTexture);
            }

            importer.textureType = originalType;
            importer.npotScale = originalNPOTScale;
            importer.SaveAndReimport();
        }

        CreateZipArchive(outputFolderPath);

        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Complete", $"Textures have been compressed and saved in folder: {OutputFolderName}_{maxSize}px.", "OK");
    }

    private static void CheckForScriptUpdateOnce()
    {
        if (updateChecked) return;
        updateChecked = true;

        try
        {
            string latestVersion = GetLatestVersion();
            if (latestVersion != CurrentVersion)
            {
                if (EditorUtility.DisplayDialog("Update Available",
                    $"A new version ({latestVersion}) of the script is available. Do you want to update?",
                    "Yes", "No"))
                {
                    DownloadLatestScript();
                    EditorUtility.DisplayDialog("Update Complete", "The script has been updated. Please restart Unity to apply changes if the script has not changed :).", "OK");
                }
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"Error checking for updates: {ex.Message}");
        }
    }

    private static string GetLatestVersion()
    {
        using (WebClient client = new WebClient())
        {
            return client.DownloadString(VersionUrl).Trim();
        }
    }

    private static void DownloadLatestScript()
    {
        try
        {
            string executingScriptPath = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(CreateInstance<MapHelperUnity>()));

            if (string.IsNullOrEmpty(executingScriptPath))
            {
                UnityEngine.Debug.LogError("Unable to determine the path of the current script.");
                return;
            }

            using (WebClient client = new WebClient())
            {
                string scriptContent = client.DownloadString(ScriptUrl);
                string fullPath = Path.Combine(UnityEngine.Application.dataPath, executingScriptPath.Substring("Assets/".Length));

                File.WriteAllText(fullPath, scriptContent);
                UnityEngine.Debug.Log($"Script updated at path: {fullPath}");
            }

            AssetDatabase.Refresh();
            UnityEngine.Debug.Log("Script updated and Unity refreshed without restart.");
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"Error updating script: {ex.Message}");
        }
    }

    private static List<string> FindTexturesInScene()
    {
        var textures = new HashSet<string>();
        var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);

        foreach (var renderer in renderers)
        {
            foreach (var mat in renderer.sharedMaterials)
            {
                if (mat == null) continue;

                foreach (var textureName in mat.GetTexturePropertyNames())
                {
                    var texture = mat.GetTexture(textureName);
                    if (texture is Texture2D texture2D)
                    {
                        string path = AssetDatabase.GetAssetPath(texture2D);
                        if (!string.IsNullOrEmpty(path))
                            textures.Add(path);
                    }
                }
            }
        }

        return new List<string>(textures);
    }

    private static Texture2D ResizeTexture(Texture2D originalTexture, int maxSize)
    {
        int newWidth, newHeight;

        if (originalTexture.width > originalTexture.height)
        {
            newWidth = maxSize;
            newHeight = Mathf.CeilToInt((float)originalTexture.height * maxSize / originalTexture.width);
        }
        else
        {
            newHeight = maxSize;
            newWidth = Mathf.CeilToInt((float)originalTexture.width * maxSize / originalTexture.height);
        }

        RenderTexture renderTexture = RenderTexture.GetTemporary(newWidth, newHeight, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Default);
        renderTexture.filterMode = FilterMode.Bilinear;

        RenderTexture.active = renderTexture;
        Graphics.Blit(originalTexture, renderTexture);

        Texture2D resizedTexture = new Texture2D(newWidth, newHeight, TextureFormat.RGBA32, false);
        resizedTexture.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
        resizedTexture.Apply();

        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(renderTexture);

        return resizedTexture;
    }

    private static void SaveResizedTexture(Texture2D resizedTexture, string originalPath, string outputFolderPath)
    {
        string relativePath = originalPath.Substring("Assets/".Length);
        string newPath = Path.Combine(outputFolderPath, relativePath);
        string directory = Path.GetDirectoryName(newPath);

        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        byte[] pngData = resizedTexture.EncodeToPNG();
        File.WriteAllBytes(newPath, pngData);
    }

    private static void CreateZipArchive(string folderPath)
    {
        string zipPath = folderPath + ".zip";
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        ZipFile.CreateFromDirectory(folderPath, zipPath);
        UnityEngine.Debug.Log("Zip archive created at: " + zipPath);
    }

    [MenuItem("Tools/Run Auto LODs for Selected Objects")]
    private static void AddLodGroupsToSelectedObjects()
    {
        GameObject[] selectedObjects = Selection.gameObjects;

        if (selectedObjects.Length == 0)
        {
            Debug.LogWarning("No objects selected. Please select objects in the hierarchy.");
            return;
        }

        foreach (GameObject obj in selectedObjects)
        {
            Undo.RegisterFullObjectHierarchyUndo(obj, "Add LOD Group");

            if (obj.transform.localScale != Vector3.one)
            {
                bool normalize = EditorUtility.DisplayDialog(
                    "Scale different from (1, 1, 1) detected",
                    $"Object '{obj.name}' has a scale of {obj.transform.localScale}. Do you want to normalize the scale (set it to (1, 1, 1))?",
                    "Yes", "No"
                );

                if (normalize)
                {
                    NormalizeScale(obj);
                }
                else
                {
                    Debug.LogWarning($"Skipping object '{obj.name}' due to non-normalized scale.");
                    continue;
                }
            }

            if (obj.GetComponent<LODGroup>() == null)
            {
                LODGroup lodGroup = obj.AddComponent<LODGroup>();

                LOD[] lods = new LOD[1];
                Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
                lods[0] = new LOD(0.009f, renderers);

                lodGroup.SetLODs(lods);
                lodGroup.RecalculateBounds();
                lodGroup.size = 1;
            }
        }
    }

    [MenuItem("Tools/Normalize Scale to (1, 1, 1)")]
    private static void NormalizeScale()
    {
        GameObject[] selectedObjects = Selection.gameObjects;

        if (selectedObjects.Length == 0)
        {
            Debug.LogWarning("No objects selected. Please select objects in the hierarchy.");
            return;
        }

        foreach (GameObject obj in selectedObjects)
        {
            NormalizeScale(obj);
        }
    }

    private static void NormalizeScale(GameObject obj)
    {
        Undo.RegisterFullObjectHierarchyUndo(obj, "Normalize Scale");

        if (obj.transform.localScale == Vector3.one)
        {
            Debug.Log($"Object '{obj.name}' already has a scale of (1, 1, 1). Skipping.");
            return;
        }

        Vector3 originalScale = obj.transform.localScale;

        MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            Mesh mesh = Instantiate(meshFilter.sharedMesh);
            Undo.RegisterCreatedObjectUndo(mesh, "Modify Mesh");
            meshFilter.sharedMesh = mesh;

            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = Vector3.Scale(vertices[i], originalScale);
            }

            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
        }

        foreach (Transform child in obj.transform)
        {
            Undo.RecordObject(child, "Normalize Child Position");
            child.localPosition = Vector3.Scale(child.localPosition, originalScale);
        }

        Undo.RecordObject(obj.transform, "Normalize Scale");
        obj.transform.localScale = Vector3.one;

        Debug.Log($"Object '{obj.name}' scale normalized to (1, 1, 1).");
    }
}
#endif
