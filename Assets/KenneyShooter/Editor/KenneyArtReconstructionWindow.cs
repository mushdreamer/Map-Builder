using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KenneyShooter;

public class KenneyArtReconstructionWindow : EditorWindow
{
    const string PrefabFolder = "Assets/KenneyShooter/Prefabs/StationArt";
    string manifestPath = "ReconstructionReports/station/unity-placements.json";

    [Serializable] class Manifest
    {
        public int pixelsPerUnit;
        public List<AssetRecord> assets;
        public List<Record> placements;
    }
    [Serializable] class AssetRecord
    {
        public string assetKey, assetPath;
        public bool noCollision;
        public Footprint footprint;
    }
    [Serializable] class Record
    {
        public string instanceId, assetKey, assetPath, layerPath, reviewState;
        public Vector3 position;
        public float rotationDeg, confidence;
        public Vector2 scale;
        public int sortingOrder;
        public bool noCollision;
        public Footprint footprint;
    }
    [Serializable] class Footprint { public int cols = 1, rows = 1; }

    [MenuItem("Tools/Kenney/Art Reconstruction/PNG to Prefabs and Place")]
    static void Open() { GetWindow<KenneyArtReconstructionWindow>("Art Reconstruction"); }

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "先运行 Python audit 生成 unity-placements.json。这里仅自动接受唯一的逐像素 PSD 匹配；" +
            "歧义项会保留在报告中，不会猜测。", MessageType.Info);
        manifestPath = EditorGUILayout.TextField("Placement Manifest", manifestPath);
        if (GUILayout.Button("选择 Manifest…"))
        {
            string selected = EditorUtility.OpenFilePanel("Unity placements", Path.GetDirectoryName(manifestPath), "json");
            if (!string.IsNullOrEmpty(selected)) manifestPath = selected;
        }
        if (GUILayout.Button("1. PNG → Prefab")) CreatePrefabs();
        if (GUILayout.Button("2. Prefab → 当前场景")) PlaceInScene();
    }

    Manifest ReadManifest()
    {
        string absolute = Path.GetFullPath(manifestPath);
        if (!File.Exists(absolute)) throw new FileNotFoundException("Manifest not found", absolute);
        var value = JsonUtility.FromJson<Manifest>(File.ReadAllText(absolute));
        if (value == null || value.placements == null) throw new InvalidDataException("Invalid placement manifest");
        return value;
    }

    void CreatePrefabs()
    {
        try
        {
            var manifest = ReadManifest();
            EnsureFolder(PrefabFolder);
            var assets = manifest.assets ?? new List<AssetRecord>();
            foreach (var record in assets)
            {
                string assetPath = record.assetPath.Replace('\\', '/');
                if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    string[] matches = AssetDatabase.FindAssets(
                        Path.GetFileNameWithoutExtension(assetPath) + " t:Texture2D", new[] { "Assets/Station" });
                    assetPath = matches.Length > 0 ? AssetDatabase.GUIDToAssetPath(matches[0]) : assetPath;
                }
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer == null) { Debug.LogWarning("[Art Reconstruction] PNG is outside Assets: " + assetPath); continue; }
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = Mathf.Max(1, manifest.pixelsPerUnit);
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                var root = new GameObject(record.assetKey);
                root.AddComponent<SpriteRenderer>().sprite = sprite;
                var placeable = root.AddComponent<KenneyPlaceable>();
                placeable.nativeCols = record.footprint != null ? Mathf.Max(1, record.footprint.cols) : 1;
                placeable.nativeRows = record.footprint != null ? Mathf.Max(1, record.footprint.rows) : 1;
                if (!record.noCollision)
                {
                    var collider = root.AddComponent<BoxCollider2D>();
                    collider.size = new Vector2(placeable.nativeCols, placeable.nativeRows);
                }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/" + SafeName(record.assetKey) + ".prefab");
                DestroyImmediate(root);
            }
            KenneyMapPipelineMenus.RebuildPlaceableCatalog();
            Debug.Log("[Art Reconstruction] Prefabs updated: " + assets.Count);
        }
        catch (Exception e) { Debug.LogException(e); }
    }

    void PlaceInScene()
    {
        try
        {
            var manifest = ReadManifest();
            var mapRoot = GameObject.Find(KenneyMapFormat.RootObjectName);
            if (mapRoot == null) throw new InvalidOperationException("Current scene has no " + KenneyMapFormat.RootObjectName);
            var old = mapRoot.transform.Find("ArtPlacements");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            var parent = new GameObject("ArtPlacements");
            Undo.RegisterCreatedObjectUndo(parent, "Import art placements");
            parent.transform.SetParent(mapRoot.transform, false);
            foreach (var record in manifest.placements)
            {
                string path = PrefabFolder + "/" + SafeName(record.assetKey) + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { Debug.LogWarning("Missing prefab: " + path); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
                go.transform.position = record.position;
                go.transform.rotation = Quaternion.Euler(0, 0, record.rotationDeg);
                go.transform.localScale = new Vector3(record.scale.x, record.scale.y, 1);
                go.GetComponent<SpriteRenderer>().sortingOrder = record.sortingOrder;
                var link = go.AddComponent<KenneyArtPlacementInstance>();
                link.instanceId = record.instanceId; link.assetKey = record.assetKey;
                link.layerPath = record.layerPath; link.confidence = record.confidence;
                link.reviewState = record.reviewState; link.noCollision = record.noCollision;
            }
            var store = mapRoot.GetComponent<KenneyArtPlacementStore>();
            if (store == null) store = Undo.AddComponent<KenneyArtPlacementStore>(mapRoot);
            EditorUtility.SetDirty(mapRoot);
            EditorSceneManager.MarkSceneDirty(mapRoot.scene);
            Selection.activeGameObject = parent;
            Debug.Log("[Art Reconstruction] Editable prefab instances placed: " + parent.transform.childCount);
        }
        catch (Exception e) { Debug.LogException(e); }
    }

    static string SafeName(string value)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        return value;
    }

    static void EnsureFolder(string path)
    {
        string current = "Assets";
        foreach (string part in path.Substring("Assets/".Length).Split('/'))
        {
            string next = current + "/" + part;
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, part);
            current = next;
        }
    }
}
