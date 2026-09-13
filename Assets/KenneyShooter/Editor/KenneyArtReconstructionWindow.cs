using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;
using KenneyShooter;

public class KenneyArtReconstructionWindow : EditorWindow
{
    const string PrefabFolder = "Assets/KenneyShooter/Prefabs/StationArt";
    const string FloorTileAssetPath = PrefabFolder + "/NoColition_Tile_basic_FloorArt.asset";
    string manifestPath = "ReconstructionReports/station/unity-placements.json";
    readonly bool[] tierVisible = { true, true, true, true, true, true };

    [Serializable] class Manifest { public int pixelsPerUnit; public List<AssetRecord> assets; public List<Record> placements; }
    [Serializable] class AssetRecord
    {
        public string assetKey, assetPath, assetRole;
        public bool noCollision;
        public Footprint footprint;
    }
    [Serializable] class Record
    {
        public string instanceId, assetKey, assetPath, layerPath, reviewState, tier;
        public Vector3 position;
        public float rotationDeg, confidence, scoreMargin;
        public Vector2 scale;
        public int sortingOrder, candidateRank;
        public bool noCollision;
        public Footprint footprint;
    }
    [Serializable] class Footprint { public int cols = 1, rows = 1; }

    [MenuItem("Tools/Kenney/Art Reconstruction/PNG to Prefabs and Place")]
    static void Open() { GetWindow<KenneyArtReconstructionWindow>("Art Reconstruction"); }

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Tier A 保持严格 autoAccepted；Tier B-F 是可视化预览，不参与 Wall 替换且禁用碰撞。" +
            "Floor occupancy 使用基础 floor 美术显示，但不改变 gameplay tiles。", MessageType.Info);
        manifestPath = EditorGUILayout.TextField("Placement Manifest", manifestPath);
        if (GUILayout.Button("选择 Manifest…"))
        {
            string selected = EditorUtility.OpenFilePanel("Unity placements", Path.GetDirectoryName(manifestPath), "json");
            if (!string.IsNullOrEmpty(selected)) manifestPath = selected;
        }
        if (GUILayout.Button("1. PNG → Prefab")) CreatePrefabs();
        if (GUILayout.Button("2. Prefab → 当前场景")) PlaceInScene();
        EditorGUILayout.LabelField("Confidence tiers", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        for (int i = 0; i < tierVisible.Length; i++)
            tierVisible[i] = GUILayout.Toggle(tierVisible[i], "Tier " + (char)('A' + i));
        EditorGUILayout.EndHorizontal();
        if (GUILayout.Button("应用 Tier 可见性")) ApplyTierVisibility();
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
                if (string.Equals(record.assetKey, "NoColition_Tile_basic", StringComparison.OrdinalIgnoreCase))
                    CreateOrUpdateFloorTile(sprite);
            }
            AssetDatabase.SaveAssets();
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
            var state = mapRoot.GetComponent<KenneyArtReplacementState>();
            if (state == null) state = Undo.AddComponent<KenneyArtReplacementState>(mapRoot);
            RestoreReplacement(mapRoot, state);
            var old = mapRoot.transform.Find("ArtPlacements");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            var parent = new GameObject("ArtPlacements");
            Undo.RegisterCreatedObjectUndo(parent, "Import art placements");
            parent.transform.SetParent(mapRoot.transform, false);
            var tierParents = CreateTierParents(parent.transform);
            var walls = FindTilemap(mapRoot.transform, "Walls", "Wall");

            foreach (var record in manifest.placements)
            {
                string path = PrefabFolder + "/" + SafeName(record.assetKey) + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { Debug.LogWarning("Missing prefab: " + path); continue; }
                string tier = string.IsNullOrEmpty(record.tier) ? "A" : record.tier;
                Transform tierParent;
                if (!tierParents.TryGetValue(tier, out tierParent)) tierParent = parent.transform;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, tierParent);
                go.transform.position = record.position;
                go.transform.rotation = Quaternion.Euler(0, 0, record.rotationDeg);
                go.transform.localScale = new Vector3(record.scale.x, record.scale.y, 1);
                go.GetComponent<SpriteRenderer>().sortingOrder = record.sortingOrder;
                var link = go.AddComponent<KenneyArtPlacementInstance>();
                link.instanceId = record.instanceId; link.assetKey = record.assetKey;
                link.layerPath = record.layerPath; link.confidence = record.confidence;
                link.scoreMargin = record.scoreMargin; link.candidateRank = record.candidateRank;
                link.tier = tier; link.reviewState = record.reviewState; link.noCollision = record.noCollision;
                bool autoAccepted = record.reviewState == "autoAccepted";
                if (!autoAccepted)
                {
                    foreach (var collider in go.GetComponentsInChildren<Collider2D>()) collider.enabled = false;
                }
                else if (!record.noCollision && walls != null)
                {
                    ClearCoveredWalls(walls, state, record);
                }
            }
            BuildFloorArt(mapRoot, state);
            var store = mapRoot.GetComponent<KenneyArtPlacementStore>();
            if (store == null) store = Undo.AddComponent<KenneyArtPlacementStore>(mapRoot);
            EditorUtility.SetDirty(mapRoot);
            EditorSceneManager.MarkSceneDirty(mapRoot.scene);
            Selection.activeGameObject = parent;
            Debug.Log("[Art Reconstruction] Tiered instances: " + manifest.placements.Count +
                      "; cleared Wall cells: " + state.clearedWallCells.Count);
        }
        catch (Exception e) { Debug.LogException(e); }
    }

    Dictionary<string, Transform> CreateTierParents(Transform parent)
    {
        var result = new Dictionary<string, Transform>();
        for (int i = 0; i < tierVisible.Length; i++)
        {
            string tier = ((char)('A' + i)).ToString();
            var child = new GameObject("Tier" + tier);
            child.transform.SetParent(parent, false);
            child.SetActive(tierVisible[i]);
            result[tier] = child.transform;
        }
        return result;
    }

    void ApplyTierVisibility()
    {
        var root = GameObject.Find(KenneyMapFormat.RootObjectName);
        var placements = root != null ? root.transform.Find("ArtPlacements") : null;
        if (placements == null) return;
        for (int i = 0; i < tierVisible.Length; i++)
        {
            var child = placements.Find("Tier" + (char)('A' + i));
            if (child != null) child.gameObject.SetActive(tierVisible[i]);
        }
        EditorSceneManager.MarkSceneDirty(root.scene);
    }

    static Tilemap FindTilemap(Transform root, params string[] names)
    {
        foreach (string name in names)
        {
            var child = root.Find(name);
            if (child != null && child.GetComponent<Tilemap>() != null) return child.GetComponent<Tilemap>();
        }
        return null;
    }

    static void RestoreReplacement(GameObject root, KenneyArtReplacementState state)
    {
        var walls = FindTilemap(root.transform, "Walls", "Wall");
        if (walls != null)
        {
            int count = Mathf.Min(state.clearedWallCells.Count, state.clearedWallTiles.Count);
            for (int i = 0; i < count; i++) walls.SetTile(state.clearedWallCells[i], state.clearedWallTiles[i]);
        }
        state.clearedWallCells.Clear();
        state.clearedWallTiles.Clear();
        var floor = FindTilemap(root.transform, "Floor");
        var floorRenderer = floor != null ? floor.GetComponent<TilemapRenderer>() : null;
        if (floorRenderer != null && state.floorRendererHidden) floorRenderer.enabled = true;
        state.floorRendererHidden = false;
        var oldFloor = root.transform.Find("ArtFloorReplacement");
        if (oldFloor != null) Undo.DestroyObjectImmediate(oldFloor.gameObject);
    }

    static void ClearCoveredWalls(Tilemap walls, KenneyArtReplacementState state, Record record)
    {
        int cols = record.footprint != null ? Mathf.Max(1, record.footprint.cols) : 1;
        int rows = record.footprint != null ? Mathf.Max(1, record.footprint.rows) : 1;
        float angle = Mathf.Repeat(record.rotationDeg, 360f);
        if (Mathf.Abs(Mathf.DeltaAngle(angle, 90f)) < 0.1f ||
            Mathf.Abs(Mathf.DeltaAngle(angle, 270f)) < 0.1f)
        { int swap = cols; cols = rows; rows = swap; }
        int left = Mathf.FloorToInt(record.position.x - cols * 0.5f + 0.0001f);
        int bottom = Mathf.FloorToInt(record.position.y - rows * 0.5f + 0.0001f);
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < cols; x++)
        {
            var cell = new Vector3Int(left + x, bottom + y, 0);
            var tile = walls.GetTile(cell);
            if (tile == null || state.clearedWallCells.Contains(cell)) continue;
            state.clearedWallCells.Add(cell); state.clearedWallTiles.Add(tile);
            walls.SetTile(cell, null);
        }
        EditorUtility.SetDirty(walls); EditorUtility.SetDirty(state);
    }

    static void BuildFloorArt(GameObject root, KenneyArtReplacementState state)
    {
        var source = FindTilemap(root.transform, "Floor");
        var artTile = AssetDatabase.LoadAssetAtPath<TileBase>(FloorTileAssetPath);
        if (source == null || artTile == null) return;
        var artObject = new GameObject("ArtFloorReplacement");
        Undo.RegisterCreatedObjectUndo(artObject, "Create art floor replacement");
        artObject.transform.SetParent(root.transform, false);
        var art = artObject.AddComponent<Tilemap>();
        var renderer = artObject.AddComponent<TilemapRenderer>();
        var sourceRenderer = source.GetComponent<TilemapRenderer>();
        if (sourceRenderer == null) { Undo.DestroyObjectImmediate(artObject); return; }
        renderer.sortingLayerID = sourceRenderer.sortingLayerID;
        renderer.sortingOrder = sourceRenderer.sortingOrder;
        foreach (var cell in source.cellBounds.allPositionsWithin) if (source.HasTile(cell)) art.SetTile(cell, artTile);
        sourceRenderer.enabled = false;
        state.floorRendererHidden = true;
        EditorUtility.SetDirty(source); EditorUtility.SetDirty(state);
    }

    static void CreateOrUpdateFloorTile(Sprite sprite)
    {
        var tile = AssetDatabase.LoadAssetAtPath<Tile>(FloorTileAssetPath);
        if (tile == null)
        {
            tile = ScriptableObject.CreateInstance<Tile>();
            AssetDatabase.CreateAsset(tile, FloorTileAssetPath);
        }
        tile.sprite = sprite; tile.colliderType = Tile.ColliderType.None;
        EditorUtility.SetDirty(tile);
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
