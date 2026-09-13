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
    const string GeneratedRootName = "ReconstructedPrefabs";
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
            "只应用严格 autoAccepted placement。碰撞实体进入 Walls/Props，NoCollision 进入 Decor；" +
            "Floor occupancy 会直接换成基础真实 floor art。Ground 保持不变。", MessageType.Info);
        manifestPath = EditorGUILayout.TextField("Placement Manifest", manifestPath);
        if (GUILayout.Button("选择 Manifest…"))
        {
            string selected = EditorUtility.OpenFilePanel("Unity placements", Path.GetDirectoryName(manifestPath), "json");
            if (!string.IsNullOrEmpty(selected)) manifestPath = selected;
        }
        if (GUILayout.Button("1. PNG → Prefab")) CreatePrefabs();
        if (GUILayout.Button("2. Apply Reconstruction to Layout")) ApplyToLayout();
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
                string assetPath = ResolveAssetPath(record.assetPath);
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

    void ApplyToLayout()
    {
        try
        {
            var manifest = ReadManifest();
            var mapRoot = GameObject.Find(KenneyMapFormat.RootObjectName);
            if (mapRoot == null) throw new InvalidOperationException("Current scene has no " + KenneyMapFormat.RootObjectName);
            var walls = RequiredTilemap(mapRoot.transform, "Walls");
            var floor = RequiredTilemap(mapRoot.transform, "Floor");
            var decor = RequiredTilemap(mapRoot.transform, "Decor");
            var props = RequiredTilemap(mapRoot.transform, "Props");
            var state = mapRoot.GetComponent<KenneyArtReplacementState>();
            if (state == null) state = Undo.AddComponent<KenneyArtReplacementState>(mapRoot);
            Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { walls, floor, state }, "Apply art reconstruction");
            RestoreReplacements(walls, floor, state);
            ClearGeneratedChildren(walls.transform, decor.transform, props.transform);
            var wallParent = CreateGeneratedParent(walls.transform);
            var decorParent = CreateGeneratedParent(decor.transform);
            var propsParent = CreateGeneratedParent(props.transform);

            int wallInstances = 0, decorInstances = 0, propInstances = 0;
            foreach (var record in manifest.placements)
            {
                if (record.reviewState != "autoAccepted") continue;
                string prefabPath = PrefabFolder + "/" + SafeName(record.assetKey) + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) { Debug.LogWarning("Missing prefab: " + prefabPath); continue; }
                int cleared = !record.noCollision ? ClearCoveredWalls(walls, state, record) : 0;
                string targetLayer = record.noCollision ? "Decor" : (cleared > 0 ? "Walls" : "Props");
                Transform parent = targetLayer == "Walls" ? wallParent : (targetLayer == "Decor" ? decorParent : propsParent);
                InstantiateRecord(prefab, parent, record, targetLayer);
                if (targetLayer == "Walls") wallInstances++;
                else if (targetLayer == "Decor") decorInstances++;
                else propInstances++;
            }
            int floorCells = ReplaceFloorArt(floor, state);
            var obsolete = mapRoot.transform.Find("ArtPlacements");
            if (obsolete != null) Undo.DestroyObjectImmediate(obsolete.gameObject);
            obsolete = mapRoot.transform.Find("ArtFloorReplacement");
            if (obsolete != null) Undo.DestroyObjectImmediate(obsolete.gameObject);
            EditorUtility.SetDirty(mapRoot); EditorUtility.SetDirty(state);
            EditorSceneManager.MarkSceneDirty(mapRoot.scene);
            Selection.activeGameObject = mapRoot;
            Debug.Log("[Art Reconstruction] Applied to formal layout | Walls=" + wallInstances +
                      ", Props=" + propInstances + ", Decor=" + decorInstances +
                      ", Floor cells=" + floorCells + ", cleared Wall cells=" + state.clearedWallCells.Count);
        }
        catch (Exception e) { Debug.LogException(e); }
    }

    static void InstantiateRecord(GameObject prefab, Transform parent, Record record, string targetLayer)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.position = record.position;
        go.transform.rotation = Quaternion.Euler(0, 0, record.rotationDeg);
        go.transform.localScale = new Vector3(record.scale.x, record.scale.y, 1);
        go.GetComponent<SpriteRenderer>().sortingOrder = record.sortingOrder;
        var link = go.AddComponent<KenneyArtPlacementInstance>();
        link.instanceId = record.instanceId; link.assetKey = record.assetKey;
        link.layerPath = record.layerPath; link.confidence = record.confidence;
        link.scoreMargin = record.scoreMargin; link.candidateRank = record.candidateRank;
        link.tier = "A"; link.reviewState = record.reviewState;
        link.noCollision = record.noCollision; link.targetLayer = targetLayer;
    }

    static int ClearCoveredWalls(Tilemap walls, KenneyArtReplacementState state, Record record)
    {
        int cols = record.footprint != null ? Mathf.Max(1, record.footprint.cols) : 1;
        int rows = record.footprint != null ? Mathf.Max(1, record.footprint.rows) : 1;
        float angle = Mathf.Repeat(record.rotationDeg, 360f);
        if (Mathf.Abs(Mathf.DeltaAngle(angle, 90f)) < 0.1f || Mathf.Abs(Mathf.DeltaAngle(angle, 270f)) < 0.1f)
        { int swap = cols; cols = rows; rows = swap; }
        int left = Mathf.FloorToInt(record.position.x - cols * 0.5f + 0.0001f);
        int bottom = Mathf.FloorToInt(record.position.y - rows * 0.5f + 0.0001f);
        int cleared = 0;
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < cols; x++)
        {
            var cell = new Vector3Int(left + x, bottom + y, 0);
            var tile = walls.GetTile(cell);
            if (tile == null || state.clearedWallCells.Contains(cell)) continue;
            state.clearedWallCells.Add(cell); state.clearedWallTiles.Add(tile);
            walls.SetTile(cell, null); cleared++;
        }
        return cleared;
    }

    static int ReplaceFloorArt(Tilemap floor, KenneyArtReplacementState state)
    {
        var artTile = AssetDatabase.LoadAssetAtPath<TileBase>(FloorTileAssetPath);
        if (artTile == null) { Debug.LogWarning("Missing floor art Tile; run PNG → Prefab first."); return 0; }
        var occupied = new List<Vector3Int>();
        foreach (var cell in floor.cellBounds.allPositionsWithin) if (floor.HasTile(cell)) occupied.Add(cell);
        foreach (var cell in occupied)
        {
            state.replacedFloorCells.Add(cell);
            state.replacedFloorTiles.Add(floor.GetTile(cell));
            floor.SetTile(cell, artTile);
        }
        return occupied.Count;
    }

    static void RestoreReplacements(Tilemap walls, Tilemap floor, KenneyArtReplacementState state)
    {
        int wallCount = Mathf.Min(state.clearedWallCells.Count, state.clearedWallTiles.Count);
        for (int i = 0; i < wallCount; i++) walls.SetTile(state.clearedWallCells[i], state.clearedWallTiles[i]);
        int floorCount = Mathf.Min(state.replacedFloorCells.Count, state.replacedFloorTiles.Count);
        for (int i = 0; i < floorCount; i++) floor.SetTile(state.replacedFloorCells[i], state.replacedFloorTiles[i]);
        state.clearedWallCells.Clear(); state.clearedWallTiles.Clear();
        state.replacedFloorCells.Clear(); state.replacedFloorTiles.Clear();
    }

    static void ClearGeneratedChildren(params Transform[] layers)
    {
        foreach (var layer in layers)
        {
            var generated = layer.Find(GeneratedRootName);
            if (generated != null) Undo.DestroyObjectImmediate(generated.gameObject);
        }
    }

    static Transform CreateGeneratedParent(Transform layer)
    {
        var child = new GameObject(GeneratedRootName);
        Undo.RegisterCreatedObjectUndo(child, "Create reconstructed layer content");
        child.transform.SetParent(layer, false);
        return child.transform;
    }

    static Tilemap RequiredTilemap(Transform root, string name)
    {
        var child = root.Find(name);
        var map = child != null ? child.GetComponent<Tilemap>() : null;
        if (map == null) throw new InvalidOperationException("Missing formal Tilemap layer: " + name);
        return map;
    }

    static string ResolveAssetPath(string value)
    {
        string assetPath = value.Replace('\\', '/');
        if (assetPath.StartsWith("Assets/", StringComparison.Ordinal)) return assetPath;
        string[] matches = AssetDatabase.FindAssets(
            Path.GetFileNameWithoutExtension(assetPath) + " t:Texture2D", new[] { "Assets/Station" });
        return matches.Length > 0 ? AssetDatabase.GUIDToAssetPath(matches[0]) : assetPath;
    }

    static void CreateOrUpdateFloorTile(Sprite sprite)
    {
        var tile = AssetDatabase.LoadAssetAtPath<Tile>(FloorTileAssetPath);
        if (tile == null)
        {
            tile = ScriptableObject.CreateInstance<Tile>();
            AssetDatabase.CreateAsset(tile, FloorTileAssetPath);
        }
        // Keep a parseable gameplay tile id so map export preserves Floor
        // occupancy even though the editor scene uses the station-art sprite.
        tile.name = "tile_28_station_floor_art";
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
