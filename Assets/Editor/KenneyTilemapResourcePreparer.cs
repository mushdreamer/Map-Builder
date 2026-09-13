using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Prepares Kenney tiles for Unity's built-in Tilemap / Tile Palette workflow.
/// Menu: Tools/Kenney/Prepare Tilemap Resources For Editors
/// </summary>
public static class KenneyTilemapResourcePreparer
{
    const string PngFolder = "Assets/Arts/kenney_top-down-shooter/PNG/Tiles";
    const string ReadyRoot = "Assets/Arts/kenney_top-down-shooter/TilemapReady";
    const string TilesRoot = ReadyRoot + "/Tiles";
    const string PalettesRoot = ReadyRoot + "/Palettes";
    const string MapRootName = "KenneySampleMap";

    struct Cat
    {
        public string Folder;
        public string PaletteName;
        public int[] Ids;
        public bool IsWall;
        public Cat(string folder, string paletteName, int[] ids, bool isWall = false)
        {
            Folder = folder;
            PaletteName = paletteName;
            Ids = ids;
            IsWall = isWall;
        }
    }

    [MenuItem("Tools/Kenney/Prepare Tilemap Resources For Editors")]
    public static void Prepare()
    {
        PrepareInternal(true);
    }

    [MenuItem("Tools/Kenney/Create Tile Palettes Only")]
    public static void CreatePalettesOnly()
    {
        PrepareInternal(false);
    }

    static void PrepareInternal(bool createTiles)
    {
        EnsureFolder(ReadyRoot);
        EnsureFolder(TilesRoot);
        EnsureFolder(PalettesRoot);

        var cats = BuildCategories();
        int importFixed = 0;
        var allIds = new HashSet<int>();

        if (createTiles)
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { PngFolder });
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var g in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(g);
                    if (MarkSpriteImportDirty(path))
                        importFixed++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            if (importFixed > 0)
                AssetDatabase.Refresh();

            foreach (var cat in cats)
            {
                string folder = TilesRoot + "/" + cat.Folder;
                EnsureFolder(folder);
                foreach (var id in cat.Ids)
                {
                    if (CreateOrUpdateTile(folder, id, cat.IsWall))
                        allIds.Add(id);
                }
            }

            var miscIds = CollectAllTileIds();
            miscIds.RemoveAll(id => allIds.Contains(id));
            if (miscIds.Count > 0)
            {
                string miscFolder = TilesRoot + "/99_Misc";
                EnsureFolder(miscFolder);
                foreach (var id in miscIds)
                {
                    if (CreateOrUpdateTile(miscFolder, id, false))
                        allIds.Add(id);
                }
                cats.Add(new Cat("99_Misc", "Kenney_Misc", miscIds.ToArray(), false));
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        else
        {
            // Rebuild cat list including Misc from disk
            var miscFolder = TilesRoot + "/99_Misc";
            if (AssetDatabase.IsValidFolder(miscFolder))
            {
                var misc = new List<int>();
                var guids = AssetDatabase.FindAssets("t:Tile", new[] { miscFolder });
                foreach (var g in guids)
                {
                    var name = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g));
                    if (name.StartsWith("tile_"))
                    {
                        int id;
                        if (int.TryParse(name.Substring(5), out id))
                            misc.Add(id);
                    }
                }
                misc.Sort();
                if (misc.Count > 0)
                    cats.Add(new Cat("99_Misc", "Kenney_Misc", misc.ToArray(), false));
            }

            foreach (var cat in cats)
            {
                foreach (var id in cat.Ids)
                    allIds.Add(id);
            }
        }

        int paletteCount = 0;
        foreach (var cat in cats)
        {
            if (cat.Ids == null || cat.Ids.Length == 0) continue;
            if (CreatePalette(cat))
                paletteCount++;
        }
        CreateMasterPalette(allIds);

        if (createTiles)
            EnsureEditableMapLayers();

        WriteEditorReadme(allIds.Count, paletteCount);

        Debug.Log(string.Format(
            "[KenneyTilemapResourcePreparer] Done. tiles~={0}, palettes={1}+Master, importFixed={2}, root={3}. Open Window→2D→Tile Palette.",
            allIds.Count, paletteCount, importFixed, ReadyRoot));
    }

    static void WriteEditorReadme(int tileCount, int paletteCount)
    {
        string path = ReadyRoot + "/给地图编辑的使用说明.txt";
        string abs = ToAbsolute(path);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Kenney Tilemap 资源（给关卡编辑用）");
        sb.AppendLine("================================");
        sb.AppendLine();
        sb.AppendLine("已准备：");
        sb.AppendLine("- Tiles/  分类 Tile 资产（地面/地板/三色墙/水/自然/家具/道具/Misc）");
        sb.AppendLine("- Palettes/  Tile Palette（在 Window → 2D → Tile Palette 下拉选择 Kenney_*）");
        sb.AppendLine("- 场景 MapScene 中 KenneySampleMap 含 Ground/Floor/Walls/Decor/Props 层");
        sb.AppendLine();
        sb.AppendLine("推荐流程：");
        sb.AppendLine("1. 打开 Assets/Scenes/MapScene.unity");
        sb.AppendLine("2. Window → 2D → Tile Palette");
        sb.AppendLine("3. 下拉选 Kenney_Ground / Kenney_Walls_Orange 等");
        sb.AppendLine("4. Hierarchy 选中 KenneySampleMap 下对应 Tilemap 层（如 Walls）");
        sb.AppendLine("5. 用笔刷在现有大地图基础上继续绘制");
        sb.AppendLine();
        sb.AppendLine("分层建议：");
        sb.AppendLine("- Ground = 草地/泥土");
        sb.AppendLine("- Floor  = 室内地板");
        sb.AppendLine("- Walls  = 墙体（已带 TilemapCollider2D）");
        sb.AppendLine("- Decor  = 地毯/玻璃条/杂物");
        sb.AppendLine("- Props  = 家具/箱子/植被");
        sb.AppendLine();
        sb.AppendLine("若 Palette 下拉为空：菜单 Tools → Kenney → Create Tile Palettes Only");
        sb.AppendLine(string.Format("当前约 {0} 个分类 Tile，{1} 个分类 Palette + Master。", tileCount, paletteCount));
        File.WriteAllText(abs, sb.ToString(), System.Text.Encoding.UTF8);
        AssetDatabase.ImportAsset(path);
    }

    static List<Cat> BuildCategories()
    {
        return new List<Cat>
        {
            new Cat("01_Ground", "Kenney_Ground", new[]
            {
                401, 1, 2, 3, 4, 17, 18, 5, 6, 13, 14, 15, 16, 69, 70, 71, 72, 73, 74
            }),
            new Cat("02_Floors", "Kenney_Floors", new[]
            {
                7, 8, 9, 10, 11, 12, 42, 43, 44, 45, 46, 47,
                28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41,
                271, 272, 273, 274, 275, 276, 277, 278, 279, 325, 326, 327, 328, 329, 330
            }),
            new Cat("03_Walls_Orange", "Kenney_Walls_Orange", new[]
            {
                109, 110, 111, 112, 113, 114, 115, 116, 117,
                136, 137, 138, 139, 140, 141, 142, 143, 144,
                196, 197, 198
            }, true),
            new Cat("04_Walls_Cyan", "Kenney_Walls_Cyan", new[]
            {
                280, 281, 282, 283, 284, 285, 286, 287, 288,
                307, 308, 309, 310, 311, 312, 313, 314, 315,
                298, 299
            }, true),
            new Cat("05_Walls_Tan", "Kenney_Walls_Tan", new[]
            {
                118, 119, 120, 121, 122, 123, 124, 125, 126,
                145, 146, 147, 148, 149, 150, 151, 152, 153
            }, true),
            new Cat("06_Water_Glass", "Kenney_Water_Glass", new[]
            {
                19, 20, 131, 159, 160, 333,
                433, 434, 435, 436, 437, 438,
                460, 461, 462, 463, 464, 465, 489, 490, 491, 492
            }),
            new Cat("07_Nature", "Kenney_Nature", new[]
            {
                21, 22, 23, 48, 49, 50, 51, 52, 53, 75, 76, 77,
                134, 158, 181, 182, 183, 208, 209, 210, 213, 235, 240
            }),
            new Cat("08_Furniture", "Kenney_Furniture", new[]
            {
                214, 215, 269, 270,
                447, 448, 449, 450,
                474, 475, 476, 477, 478, 479,
                501, 502, 503, 504, 505,
                506, 507, 508, 509, 510, 511,
                528, 529,
                132, 133, 318
            }),
            new Cat("09_Props", "Kenney_Props", new[]
            {
                24, 25, 26, 129, 130, 156, 157, 171, 180,
                224, 225, 251, 252, 260, 261, 262, 263, 264, 265, 266, 267, 268,
                289, 290, 291, 292, 293
            }),
        };
    }

    static List<int> CollectAllTileIds()
    {
        var ids = new List<int>();
        var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { PngFolder });
        var re = new Regex(@"tile_(\d+)\.png$", RegexOptions.IgnoreCase);
        foreach (var g in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            var m = re.Match(Path.GetFileName(path));
            if (m.Success)
                ids.Add(int.Parse(m.Groups[1].Value));
        }
        ids.Sort();
        return ids;
    }

    static bool CreateOrUpdateTile(string folder, int id, bool isWall)
    {
        string png = PngPath(id);
        if (!File.Exists(ToAbsolute(png)))
            return false;

        var sprite = LoadSprite(png);
        if (sprite == null)
            return false;

        string assetPath = folder + "/tile_" + id + ".asset";
        var tile = AssetDatabase.LoadAssetAtPath<Tile>(assetPath);
        bool created = false;
        if (tile == null)
        {
            tile = ScriptableObject.CreateInstance<Tile>();
            AssetDatabase.CreateAsset(tile, assetPath);
            created = true;
        }

        tile.sprite = sprite;
        tile.name = "tile_" + id;
        tile.colliderType = isWall ? Tile.ColliderType.Grid : Tile.ColliderType.None;
        tile.color = Color.white;
        EditorUtility.SetDirty(tile);
        return created || true;
    }

    static bool CreatePalette(Cat cat)
    {
        string folder = TilesRoot + "/" + cat.Folder;
        var tiles = new List<Tile>();
        var guids = AssetDatabase.FindAssets("t:Tile", new[] { folder });
        foreach (var g in guids)
        {
            var t = AssetDatabase.LoadAssetAtPath<Tile>(AssetDatabase.GUIDToAssetPath(g));
            if (t != null) tiles.Add(t);
        }
        tiles.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        if (tiles.Count == 0) return false;
        return CreatePaletteFromTiles(cat.PaletteName, tiles, 16);
    }

    static void CreateMasterPalette(HashSet<int> ids)
    {
        var tiles = new List<Tile>();
        var sorted = new List<int>(ids);
        sorted.Sort();
        foreach (var id in sorted)
        {
            // search categorized folders
            var guids = AssetDatabase.FindAssets("tile_" + id + " t:Tile", new[] { TilesRoot });
            if (guids.Length == 0) continue;
            var t = AssetDatabase.LoadAssetAtPath<Tile>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (t != null) tiles.Add(t);
        }
        CreatePaletteFromTiles("Kenney_Master_All", tiles, 32);
    }

    static bool CreatePaletteFromTiles(string paletteName, List<Tile> tiles, int columns)
    {
        if (tiles == null || tiles.Count == 0) return false;

        // Prefer official API when 2D Tilemap package is present
        string paletteAssetPath = null;
        var utilType = FindType("UnityEditor.Tilemaps.GridPaletteUtility");
        if (utilType != null)
        {
            MethodInfo method = null;
            foreach (var cand in utilType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (cand.Name != "CreateNewPalette") continue;
                var ps = cand.GetParameters();
                if (ps.Length == 6 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(string))
                {
                    method = cand;
                    break;
                }
            }
            if (method != null)
            {
                try
                {
                    // Delete existing palette assets with same name to avoid duplicates
                    var existing = AssetDatabase.FindAssets(paletteName, new[] { PalettesRoot });
                    foreach (var g in existing)
                    {
                        var p = AssetDatabase.GUIDToAssetPath(g);
                        if (Path.GetFileNameWithoutExtension(p) == paletteName)
                            AssetDatabase.DeleteAsset(p);
                    }

                    object result = method.Invoke(null, new object[]
                    {
                        PalettesRoot,
                        paletteName,
                        GridLayout.CellLayout.Rectangle,
                        GetCellSizingAutomatic(),
                        Vector3.one,
                        GridLayout.CellSwizzle.XYZ
                    });
                    paletteAssetPath = result as string;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[KenneyTilemapResourcePreparer] CreateNewPalette failed: " + e.Message);
                }
            }
        }

        GameObject paletteGo = null;
        string prefabPath;

        if (!string.IsNullOrEmpty(paletteAssetPath))
        {
            // Palette asset sits beside a prefab of same name
            prefabPath = Path.ChangeExtension(paletteAssetPath, ".prefab");
            if (!File.Exists(ToAbsolute(prefabPath)))
            {
                // Some Unity versions store palette as the prefab itself
                prefabPath = paletteAssetPath;
            }
            paletteGo = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (paletteGo == null)
            {
                // Find child prefab in folder by name
                var guids = AssetDatabase.FindAssets(paletteName + " t:Prefab", new[] { PalettesRoot });
                foreach (var g in guids)
                {
                    var p = AssetDatabase.GUIDToAssetPath(g);
                    if (Path.GetFileNameWithoutExtension(p) == paletteName)
                    {
                        prefabPath = p;
                        paletteGo = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                        break;
                    }
                }
            }
        }
        else
        {
            // Fallback: manual palette prefab (still usable by dragging tiles; Palette window needs package)
            prefabPath = PalettesRoot + "/" + paletteName + ".prefab";
            var temp = new GameObject(paletteName);
            temp.AddComponent<Grid>().cellSize = Vector3.one;
            var layer = new GameObject("Layer1");
            layer.transform.SetParent(temp.transform, false);
            layer.AddComponent<Tilemap>();
            layer.AddComponent<TilemapRenderer>();
            PrefabUtility.SaveAsPrefabAsset(temp, prefabPath);
            Object.DestroyImmediate(temp);
            paletteGo = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        }

        if (paletteGo == null)
        {
            Debug.LogWarning("[KenneyTilemapResourcePreparer] Could not create palette: " + paletteName);
            return false;
        }

        // Edit prefab contents
        string assetPath = AssetDatabase.GetAssetPath(paletteGo);
        var contents = PrefabUtility.LoadPrefabContents(assetPath);
        try
        {
            var map = contents.GetComponentInChildren<Tilemap>();
            if (map == null)
            {
                Debug.LogWarning("No Tilemap in palette prefab: " + paletteName);
                return false;
            }
            map.ClearAllTiles();
            for (int i = 0; i < tiles.Count; i++)
            {
                int x = i % columns;
                int y = -(i / columns);
                map.SetTile(new Vector3Int(x, y, 0), tiles[i]);
            }
            PrefabUtility.SaveAsPrefabAsset(contents, assetPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }

        return true;
    }

    static object GetCellSizingAutomatic()
    {
        // GridPalette.CellSizing.Automatic enum lives in Unity.2D.Tilemap.Editor
        var t = FindType("UnityEditor.Tilemaps.GridPalette");
        if (t != null)
        {
            var nested = t.GetNestedType("CellSizing");
            if (nested != null && nested.IsEnum)
                return System.Enum.Parse(nested, "Automatic");
        }
        return 0; // Automatic = 0 in typical Unity versions
    }

    static void EnsureEditableMapLayers()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.isLoaded || scene.path.IndexOf("MapScene") < 0)
        {
            var mapScene = "Assets/Scenes/MapScene.unity";
            if (File.Exists(ToAbsolute(mapScene)))
                EditorSceneManager.OpenScene(mapScene);
        }

        var root = GameObject.Find(MapRootName);
        if (root == null)
        {
            root = new GameObject(MapRootName);
            root.AddComponent<Grid>().cellSize = Vector3.one;
        }

        if (root.GetComponent<Grid>() == null)
            root.AddComponent<Grid>().cellSize = Vector3.one;

        EnsureLayer(root.transform, "Ground", 0);
        EnsureLayer(root.transform, "Floor", 1);
        EnsureLayer(root.transform, "Walls", 2, true);
        EnsureLayer(root.transform, "Decor", 3);
        EnsureLayer(root.transform, "Props", 4);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
    }

    static void EnsureLayer(Transform parent, string name, int order, bool collider = false)
    {
        var t = parent.Find(name);
        GameObject go;
        if (t == null)
        {
            go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<Tilemap>();
            var r = go.AddComponent<TilemapRenderer>();
            r.sortingOrder = order;
        }
        else
        {
            go = t.gameObject;
            var r = go.GetComponent<TilemapRenderer>();
            if (r != null) r.sortingOrder = order;
        }

        if (collider && go.GetComponent<TilemapCollider2D>() == null)
            go.AddComponent<TilemapCollider2D>();
    }

    /// <summary>
    /// Marks importer dirty without SaveAndReimport. Call inside StartAssetEditing/StopAssetEditing.
    /// </summary>
    static bool MarkSpriteImportDirty(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return false;
        bool dirty = false;
        if (importer.textureType != TextureImporterType.Sprite) { importer.textureType = TextureImporterType.Sprite; dirty = true; }
        if (importer.spriteImportMode != SpriteImportMode.Single) { importer.spriteImportMode = SpriteImportMode.Single; dirty = true; }
        if (System.Math.Abs(importer.spritePixelsPerUnit - 64f) > 0.01f) { importer.spritePixelsPerUnit = 64f; dirty = true; }
        if (importer.filterMode != FilterMode.Point) { importer.filterMode = FilterMode.Point; dirty = true; }
        if (importer.mipmapEnabled) { importer.mipmapEnabled = false; dirty = true; }
        if (importer.wrapMode != TextureWrapMode.Clamp) { importer.wrapMode = TextureWrapMode.Clamp; dirty = true; }
        // Do NOT call SaveAndReimport here — StartAssetEditing batches it.
        return dirty;
    }

    static bool EnsureSpriteImportQueued(string path)
    {
        return MarkSpriteImportDirty(path);
    }

    static Sprite LoadSprite(string path)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite != null) return sprite;
        var assets = AssetDatabase.LoadAllAssetsAtPath(path);
        if (assets == null) return null;
        foreach (var a in assets)
        {
            sprite = a as Sprite;
            if (sprite != null) return sprite;
        }
        return null;
    }

    static string PngPath(int id)
    {
        return id < 10
            ? string.Format("{0}/tile_{1:00}.png", PngFolder, id)
            : string.Format("{0}/tile_{1}.png", PngFolder, id);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parts = path.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }

    static string ToAbsolute(string assetPath)
    {
        if (assetPath.StartsWith("Assets"))
            return Path.Combine(Directory.GetCurrentDirectory(), assetPath.Replace('/', Path.DirectorySeparatorChar));
        return assetPath;
    }

    static System.Type FindType(string fullName)
    {
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(fullName);
            if (t != null) return t;
        }
        return null;
    }
}
