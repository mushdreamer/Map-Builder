using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using KenneyShooter;

/// <summary>
/// Builds KenneyTileCatalog from TilemapReady and exports KenneySampleMap to StreamingAssets JSON.
/// </summary>
public static class KenneyMapPipelineMenus
{
    const string TilemapReadyTiles = "Assets/Arts/kenney_top-down-shooter/TilemapReady/Tiles";
    const string CatalogPath = "Assets/KenneyShooter/Config/KenneyTileCatalog.asset";
    const string StreamingMaps = "Assets/StreamingAssets/Maps";
    /// <summary>MapBuilder project: also mirror exports here for easy handoff.</summary>
    const string HandoffExport = "Export";

    [MenuItem("Tools/Kenney/Map/Rebuild Tile Catalog")]
    public static void RebuildCatalog()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<KenneyTileCatalog>(CatalogPath);
        if (catalog == null)
        {
            EnsureFolder("Assets/KenneyShooter/Config");
            catalog = ScriptableObject.CreateInstance<KenneyTileCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        var re = new Regex(@"tile_(\d+)$", RegexOptions.IgnoreCase);
        var guids = AssetDatabase.FindAssets("t:Tile", new[] { TilemapReadyTiles });
        var list = new List<KenneyTileCatalog.Entry>();
        var seen = new HashSet<int>();

        foreach (var g in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile == null) continue;
            var m = re.Match(Path.GetFileNameWithoutExtension(path));
            if (!m.Success) continue;
            int id = int.Parse(m.Groups[1].Value);
            if (!seen.Add(id)) continue;
            list.Add(new KenneyTileCatalog.Entry { id = id, tile = tile });
        }

        list.Sort((a, b) => a.id.CompareTo(b.id));
        catalog.entries = list.ToArray();
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        Debug.Log("[KenneyMap] Tile catalog rebuilt: " + list.Count + " entries → " + CatalogPath);
    }

    [MenuItem("Tools/Kenney/Map/Rebuild Placeable Catalog")]
    public static void RebuildPlaceableCatalog()
    {
        string folder = KenneyMapFormat.PlaceablePrefabFolder;
        EnsureFolder(folder);

        var catalog = AssetDatabase.LoadAssetAtPath<KenneyPlaceableCatalog>(
            KenneyMapFormat.PlaceableCatalogAssetPath);
        if (catalog == null)
        {
            EnsureFolder("Assets/KenneyShooter/Config");
            catalog = ScriptableObject.CreateInstance<KenneyPlaceableCatalog>();
            AssetDatabase.CreateAsset(catalog, KenneyMapFormat.PlaceableCatalogAssetPath);
        }

        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
        int count = 0;
        foreach (var g in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            string type = Path.GetFileNameWithoutExtension(path);
            catalog.UpsertPrefab(type, prefab);
            count++;
        }

        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        Debug.Log("[KenneyMap] Placeable catalog rebuilt: " + count + " prefabs from " + folder);
    }

    [MenuItem("Tools/Kenney/Map/Play Exported Map")]
    public static void PlayExportedMap()
    {
        const string scenePath = "Assets/Scenes/PlayScene.unity";
        if (!File.Exists(Path.GetFullPath(scenePath)))
        {
            EditorUtility.DisplayDialog(
                "Kenney Map Preview",
                "未找到 PlayScene：\n" + scenePath,
                "OK");
            return;
        }

        string exportAbs = Path.GetFullPath(HandoffExport);
        if (!Directory.Exists(exportAbs))
            Directory.CreateDirectory(exportAbs);

        string[] jsonFiles = Directory.GetFiles(exportAbs, "*.json");
        if (jsonFiles.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "Kenney Map Preview",
                "Export 文件夹里还没有 .json。\n请先用 Export Active Scene Map To JSON 导出。\n\n路径：\n" + exportAbs,
                "OK");
            return;
        }

        string chosen = EditorUtility.OpenFilePanel(
            "选择要预览的地图 JSON（Export）",
            exportAbs,
            "json");
        if (string.IsNullOrEmpty(chosen))
            return;

        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(chosen)), exportAbs,
                System.StringComparison.OrdinalIgnoreCase))
        {
            // Still allow picking outside Export, but warn once
            if (!EditorUtility.DisplayDialog(
                    "Kenney Map Preview",
                    "所选文件不在 Export 目录。\n仍要用它预览吗？\n\n" + chosen,
                    "继续",
                    "取消"))
                return;
        }

        string fileName = Path.GetFileName(chosen);
        EnsureFolder(StreamingMaps);
        string streamingAbs = Path.GetFullPath(Path.Combine(StreamingMaps, fileName));
        File.Copy(chosen, streamingAbs, true);
        AssetDatabase.Refresh();

        EditorPrefs.SetString(KenneyMapPreview.PendingMapFilePrefKey, fileName);

        if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath);
        EditorApplication.isPlaying = true;
        Debug.Log("[KenneyMap] Preview map selected: " + fileName + " → PlayScene");
    }

    [MenuItem("Tools/Kenney/Map/Export Active Scene Map To JSON")]
    public static void ExportActiveSceneMap()
    {
        RebuildCatalog();

        var root = GameObject.Find(KenneyMapFormat.RootObjectName);
        if (root == null)
        {
            EditorUtility.DisplayDialog(
                "Kenney Map Export",
                "场景中未找到 " + KenneyMapFormat.RootObjectName + "。\n请打开 MapScene。",
                "OK");
            return;
        }

        string defaultName = "level_01";
        string exportAbs = Path.GetFullPath(HandoffExport);
        if (!Directory.Exists(exportAbs))
            Directory.CreateDirectory(exportAbs);

        string fileName = EditorUtility.SaveFilePanel(
            "Export Kenney Map JSON",
            exportAbs,
            defaultName + ".map.json",
            "json");
        if (string.IsNullOrEmpty(fileName))
            return;

        var data = BuildMapFileFromScene(root, Path.GetFileNameWithoutExtension(fileName).Replace(".map", ""));
        string json = JsonUtility.ToJson(data, true);

        EnsureFolder(StreamingMaps);
        File.WriteAllText(fileName, json);

        // Always mirror into StreamingAssets/Maps for local preview consistency
        string streamingPath = Path.Combine(StreamingMaps, Path.GetFileName(fileName));
        string streamingAbs = Path.GetFullPath(streamingPath);
        if (!string.Equals(Path.GetFullPath(fileName), streamingAbs, System.StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(streamingAbs));
            File.WriteAllText(streamingAbs, json);
        }

        // Mirror into project Export/ if saved elsewhere
        string handoffPath = Path.Combine(exportAbs, Path.GetFileName(fileName));
        if (!string.Equals(Path.GetFullPath(fileName), handoffPath, System.StringComparison.OrdinalIgnoreCase))
            File.WriteAllText(handoffPath, json);

        AssetDatabase.Refresh();
        int noteCount = data.cellNotes != null ? data.cellNotes.Count : 0;
        Debug.Log("[KenneyMap] Exported map → " + fileName
            + " (layers=" + data.layers.Count + ", notes=" + noteCount
            + "; mirrored to StreamingAssets/Maps & Export/)");
        EditorUtility.DisplayDialog(
            "Kenney Map Export",
            "已导出 JSON：\n" + fileName
            + "\n备注 " + noteCount + " 条"
            + "\n\n同时镜像到：\n- Assets/StreamingAssets/Maps/\n- 项目根目录 Export/"
            + "\n\n本工程验图：Tools → Kenney → Map → Play Exported Map",
            "OK");
    }

    [MenuItem("Tools/Kenney/Map/Export MapScene Sample (StreamingAssets)")]
    public static void ExportMapSceneSample()
    {
        RebuildCatalog();

        var scenePath = "Assets/Scenes/MapScene.unity";
        if (!File.Exists(Path.GetFullPath(scenePath)))
        {
            Debug.LogError("[KenneyMap] MapScene not found.");
            return;
        }

        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath);
        var root = GameObject.Find(KenneyMapFormat.RootObjectName);
        if (root == null)
        {
            Debug.LogError("[KenneyMap] KenneySampleMap missing in MapScene.");
            return;
        }

        EnsureFolder(StreamingMaps);
        var data = BuildMapFileFromScene(root, "sample_large");
        string json = JsonUtility.ToJson(data, true);
        string outPath = Path.Combine(StreamingMaps, "sample_large.map.json");
        File.WriteAllText(Path.GetFullPath(outPath), json);
        AssetDatabase.Refresh();

        int total = 0;
        foreach (var layer in data.layers)
            total += layer.tiles != null ? layer.tiles.Count : 0;

        Debug.Log(string.Format(
            "[KenneyMap] Sample exported: {0} ({1}x{2}, {3} tiles) scene={4}",
            outPath, data.width, data.height, total, scene.path));
    }

    public static KenneyMapFile BuildMapFileFromScene(GameObject root, string mapName)
    {
        var data = new KenneyMapFile
        {
            formatVersion = KenneyMapFormat.CurrentVersion,
            name = mapName,
            cellSize = 1f,
            layers = new List<KenneyMapLayer>(),
            meta = new KenneyMapMeta()
        };

        var placementStore = root.GetComponent<KenneyArtPlacementStore>();
        if (placementStore != null && placementStore.placements != null)
            data.placements = new List<KenneyArtPlacement>(placementStore.placements);
        var placementRoot = root.transform.Find("ArtPlacements");
        if (placementRoot != null)
        {
            data.placements.Clear();
            foreach (var link in placementRoot.GetComponentsInChildren<KenneyArtPlacementInstance>(true))
            {
                // Tentative reconstruction is an editor preview only. Keeping it
                // out of map JSON prevents prefab colliders from becoming active
                // when the unchanged runtime loader instantiates placements.
                if (link.reviewState != "autoAccepted" && link.reviewState != "manuallyConfirmed")
                    continue;
                var renderer = link.GetComponentInChildren<SpriteRenderer>(true);
                data.placements.Add(new KenneyArtPlacement
                {
                    instanceId = link.instanceId,
                    assetKey = link.assetKey,
                    layerPath = link.layerPath,
                    position = link.transform.position,
                    rotationDeg = link.transform.eulerAngles.z,
                    scale = new Vector2(link.transform.localScale.x, link.transform.localScale.y),
                    sortingOrder = renderer != null ? renderer.sortingOrder : 0,
                    confidence = link.confidence,
                    reviewState = link.reviewState,
                    noCollision = link.noCollision
                });
            }
        }

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        bool any = false;

        for (int i = 0; i < KenneyMapFormat.LayerOrder.Length; i++)
        {
            string layerName = KenneyMapFormat.LayerOrder[i];
            var t = root.transform.Find(layerName);
            if (t == null) continue;
            var map = t.GetComponent<Tilemap>();
            if (map == null) continue;

            var layer = new KenneyMapLayer
            {
                name = layerName,
                sortingOrder = KenneyMapFormat.LayerSorting[i],
                hasCollider = KenneyMapFormat.LayerShouldCollide(layerName)
                    || map.GetComponent<TilemapCollider2D>() != null,
                tiles = new List<KenneyMapTile>()
            };

            foreach (var pos in map.cellBounds.allPositionsWithin)
            {
                if (!map.HasTile(pos)) continue;
                var tile = map.GetTile(pos);
                int id = ParseTileId(tile);
                if (id < 0) continue;

                layer.tiles.Add(new KenneyMapTile { x = pos.x, y = pos.y, id = id });
                any = true;
                if (pos.x < minX) minX = pos.x;
                if (pos.y < minY) minY = pos.y;
                if (pos.x > maxX) maxX = pos.x;
                if (pos.y > maxY) maxY = pos.y;
            }

            data.layers.Add(layer);
        }

        if (any)
        {
            data.width = maxX - minX + 1;
            data.height = maxY - minY + 1;
        }
        else
        {
            data.width = 0;
            data.height = 0;
        }

        // Optional: collect spawn markers under root/Spawns
        var spawns = root.transform.Find("Spawns");
        if (spawns != null)
        {
            for (int i = 0; i < spawns.childCount; i++)
            {
                var c = spawns.GetChild(i);
                data.meta.spawnPoints.Add(new KenneyMapSpawnPoint
                {
                    x = c.position.x,
                    y = c.position.y,
                    tag = c.name
                });
            }
        }

        var noteStore = root.GetComponent<KenneyCellNoteStore>();
        if (noteStore != null && noteStore.notes != null && noteStore.notes.Count > 0)
            data.cellNotes = new List<KenneyCellNote>(noteStore.notes);

        return data;
    }

    static int ParseTileId(TileBase tile)
    {
        if (tile == null) return -1;
        var m = Regex.Match(tile.name, @"tile_(\d+)", RegexOptions.IgnoreCase);
        if (!m.Success) return -1;
        return int.Parse(m.Groups[1].Value);
    }

    static void EnsureFolder(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath)) return;
        var parts = assetPath.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }

        // StreamingAssets may need physical dir before AssetDatabase sees it
        string abs = Path.GetFullPath(assetPath);
        if (!Directory.Exists(abs))
            Directory.CreateDirectory(abs);
    }
}
