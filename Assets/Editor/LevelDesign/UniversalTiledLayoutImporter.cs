#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public static class UniversalTiledLayoutImporter
{
    const string RootName = "KenneySampleMap";

    const int GroundTileId = 401;
    const int FloorTileId = 28;

    // Wall = Orange
    const int WallNW = 109, WallNE = 110, WallH = 111, WallHEndE = 114;
    const int WallSW = 136, WallSE = 137, WallV = 138, WallHEndW = 142;

    // Cover = Cyan
    const int CoverNW = 280, CoverNE = 281, CoverH = 282, CoverHEndE = 285;
    const int CoverSW = 307, CoverSE = 308, CoverV = 309, CoverHEndW = 313;

    static readonly Dictionary<int, TileBase> Cache = new Dictionary<int, TileBase>();

    class TmxData
    {
        public int width, height, wallCount, coverCount;
        public bool[,] wall, cover;
        public bool hasCover;
        public List<string> ignored = new List<string>();
    }

    [MenuItem("Tools/Level Design/Import Tiled Layout")]
    public static void Import()
    {
        string path = EditorUtility.OpenFilePanel("Select Tiled TMX Layout", Application.dataPath, "tmx");
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            TmxData d = ReadTmx(path);

            string ignoredText = d.ignored.Count > 0 ? string.Join(", ", d.ignored.ToArray()) : "None";
            string msg =
                "TMX: " + Path.GetFileName(path) + "\n" +
                "Size: " + d.width + " x " + d.height + "\n" +
                "Wall: " + d.wallCount + "\n" +
                "Cover: " + (d.hasCover ? d.coverCount.ToString() : "None") + "\n" +
                "Ignored: " + ignoredText + "\n\n" +
                "Wall -> Walls / Orange\n" +
                "Cover -> Walls / Cyan\n" +
                "Ground -> full map\n" +
                "Floor -> inferred from Wall\n" +
                "Decor / Props -> cleared\n\n" +
                "This overwrites the five Tilemaps under " + RootName + ".";

            if (!EditorUtility.DisplayDialog("Import Tiled Layout", msg, "Import", "Cancel")) return;

            GameObject root = EnsureRoot();
            Dictionary<string, Tilemap> maps = EnsureLayers(root);

            Undo.RegisterFullObjectHierarchyUndo(root, "Import Tiled Layout");

            foreach (var p in maps) p.Value.ClearAllTiles();

            TileBase ground = GetTile(GroundTileId);
            TileBase floor = GetTile(FloorTileId);
            if (ground == null || floor == null)
                throw new Exception("Ground/Floor tile assets missing. Run the Kenney Tilemap resource preparer first.");

            PaintGround(maps["Ground"], d.width, d.height, ground);

            bool[,] exterior = FloodExterior(d.wall, d.width, d.height);
            PaintFloor(maps["Floor"], d.wall, exterior, d.width, d.height, floor);

            PaintOccupancy(maps["Walls"], d.wall, d.width, d.height, false);
            if (d.hasCover)
                PaintOccupancy(maps["Walls"], d.cover, d.width, d.height, true);

            foreach (var p in maps)
            {
                p.Value.CompressBounds();
                EditorUtility.SetDirty(p.Value);
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            SceneView.RepaintAll();
            Selection.activeGameObject = root;
            Frame(root, d.width, d.height);

            Debug.Log(
                "[UniversalTiledLayoutImporter] Imported " + Path.GetFileName(path) +
                " | " + d.width + "x" + d.height +
                " | Wall=" + d.wallCount +
                " | Cover=" + (d.hasCover ? d.coverCount.ToString() : "None") +
                " | Ignored=" + ignoredText);
        }
        catch (Exception ex)
        {
            Debug.LogError("[UniversalTiledLayoutImporter] " + ex);
            EditorUtility.DisplayDialog("Import Failed", ex.Message, "OK");
        }
    }

    static TmxData ReadTmx(string path)
    {
        XmlDocument doc = new XmlDocument();
        doc.Load(path);
        XmlElement map = doc.DocumentElement;

        if (map == null || map.Name != "map")
            throw new Exception("Not a valid TMX map.");

        if (!string.Equals(map.GetAttribute("orientation"), "orthogonal", StringComparison.OrdinalIgnoreCase))
            throw new Exception("Only orthogonal TMX maps are supported.");

        if (map.GetAttribute("infinite") == "1")
            throw new Exception("Infinite/chunked TMX maps are not supported.");

        int w = PosInt(map.GetAttribute("width"), "map width");
        int h = PosInt(map.GetAttribute("height"), "map height");

        XmlElement wallLayer = FindLayer(map, "Wall");
        if (wallLayer == null)
            throw new Exception("Required tile layer \"Wall\" was not found. Import stopped before changing the scene.");

        int wallCount;
        bool[,] wall = ReadLayer(wallLayer, "Wall", w, h, out wallCount);

        XmlElement coverLayer = FindLayer(map, "Cover");
        bool hasCover = coverLayer != null;
        int coverCount = 0;
        bool[,] cover = new bool[w, h];

        if (hasCover)
            cover = ReadLayer(coverLayer, "Cover", w, h, out coverCount);

        TmxData d = new TmxData();
        d.width = w; d.height = h;
        d.wall = wall; d.wallCount = wallCount;
        d.cover = cover; d.coverCount = coverCount; d.hasCover = hasCover;

        foreach (XmlNode n in map.ChildNodes)
        {
            XmlElement e = n as XmlElement;
            if (e == null) continue;

            string name = e.GetAttribute("name");
            if (string.IsNullOrEmpty(name)) continue;

            if (n.Name == "layer")
            {
                if (string.Equals(name, "Wall", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "Cover", StringComparison.OrdinalIgnoreCase))
                    continue;
                d.ignored.Add(name);
            }
            else if (n.Name == "objectgroup")
            {
                d.ignored.Add(name);
            }
        }

        if (hasCover)
        {
            int overlap = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (wall[x, y] && cover[x, y]) overlap++;

            if (overlap > 0)
                Debug.LogWarning("[UniversalTiledLayoutImporter] Wall/Cover overlap: " + overlap +
                                 " cells. Cyan Cover will visually overwrite Orange Wall in those cells.");
        }

        return d;
    }

    static XmlElement FindLayer(XmlElement map, string name)
    {
        foreach (XmlNode n in map.ChildNodes)
        {
            if (n.Name != "layer") continue;
            XmlElement e = n as XmlElement;
            if (e != null && string.Equals(e.GetAttribute("name"), name, StringComparison.OrdinalIgnoreCase))
                return e;
        }
        return null;
    }

    static bool[,] ReadLayer(XmlElement layer, string name, int w, int h, out int count)
    {
        count = 0;

        if (PosInt(layer.GetAttribute("width"), name + " width") != w ||
            PosInt(layer.GetAttribute("height"), name + " height") != h)
            throw new Exception(name + " layer size does not match the map.");

        XmlElement data = layer.SelectSingleNode("data") as XmlElement;
        if (data == null) throw new Exception(name + " has no <data>.");

        if (!string.Equals(data.GetAttribute("encoding"), "csv", StringComparison.OrdinalIgnoreCase))
            throw new Exception(name + " must use CSV encoding in Tiled.");

        string[] tokens = data.InnerText.Split(
            new[] { ',', '\r', '\n', '\t', ' ' },
            StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length != w * h)
            throw new Exception(name + " cell count mismatch.");

        bool[,] occ = new bool[w, h];

        for (int i = 0; i < tokens.Length; i++)
        {
            uint gid;
            if (!uint.TryParse(tokens[i], out gid))
                throw new Exception("Invalid " + name + " value at index " + i);

            gid &= 0x1FFFFFFF; // strip Tiled flip flags

            if (gid != 0)
            {
                int x = i % w;
                int y = i / w;
                occ[x, y] = true;
                count++;
            }
        }

        return occ;
    }

    static int PosInt(string s, string label)
    {
        int v;
        if (!int.TryParse(s, out v) || v <= 0)
            throw new Exception("Invalid " + label + ": " + s);
        return v;
    }

    static bool[,] FloodExterior(bool[,] wall, int w, int h)
    {
        bool[,] ext = new bool[w, h];
        Queue<Vector2Int> q = new Queue<Vector2Int>();

        Action<int, int> seed = delegate (int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h || wall[x, y] || ext[x, y]) return;
            ext[x, y] = true;
            q.Enqueue(new Vector2Int(x, y));
        };

        for (int x = 0; x < w; x++) { seed(x, 0); seed(x, h - 1); }
        for (int y = 0; y < h; y++) { seed(0, y); seed(w - 1, y); }

        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };

        while (q.Count > 0)
        {
            Vector2Int p = q.Dequeue();
            for (int i = 0; i < 4; i++)
            {
                int nx = p.x + dx[i], ny = p.y + dy[i];
                if (nx < 0 || ny < 0 || nx >= w || ny >= h || wall[nx, ny] || ext[nx, ny]) continue;
                ext[nx, ny] = true;
                q.Enqueue(new Vector2Int(nx, ny));
            }
        }
        return ext;
    }

    static void PaintGround(Tilemap map, int w, int h, TileBase tile)
    {
        for (int ty = 0; ty < h; ty++)
        {
            int uy = h - 1 - ty;
            for (int x = 0; x < w; x++)
                map.SetTile(new Vector3Int(x, uy, 0), tile);
        }
    }

    static void PaintFloor(Tilemap map, bool[,] wall, bool[,] ext, int w, int h, TileBase tile)
    {
        for (int ty = 0; ty < h; ty++)
        {
            int uy = h - 1 - ty;
            for (int x = 0; x < w; x++)
            {
                if (!wall[x, ty] && !ext[x, ty])
                    map.SetTile(new Vector3Int(x, uy, 0), tile);
            }
        }
    }

    static void PaintOccupancy(Tilemap map, bool[,] occ, int w, int h, bool cover)
    {
        int[] ids = cover
            ? new[] { CoverNW, CoverNE, CoverH, CoverHEndE, CoverSW, CoverSE, CoverV, CoverHEndW }
            : new[] { WallNW, WallNE, WallH, WallHEndE, WallSW, WallSE, WallV, WallHEndW };

        foreach (int id in ids)
            if (GetTile(id) == null)
                throw new Exception("Missing tile_" + id + ". Run the Kenney Tilemap resource preparer first.");

        for (int ty = 0; ty < h; ty++)
        {
            int uy = h - 1 - ty;
            for (int x = 0; x < w; x++)
            {
                if (!occ[x, ty]) continue;
                int id = ChooseTile(occ, x, ty, w, h, cover);
                map.SetTile(new Vector3Int(x, uy, 0), GetTile(id));
            }
        }
    }

    static int ChooseTile(bool[,] o, int x, int y, int w, int h, bool c)
    {
        bool n = Has(o, x, y - 1, w, h), s = Has(o, x, y + 1, w, h);
        bool l = Has(o, x - 1, y, w, h), r = Has(o, x + 1, y, w, h);

        int NW = c ? CoverNW : WallNW, NE = c ? CoverNE : WallNE;
        int H = c ? CoverH : WallH, HE = c ? CoverHEndE : WallHEndE;
        int SW = c ? CoverSW : WallSW, SE = c ? CoverSE : WallSE;
        int V = c ? CoverV : WallV, HW = c ? CoverHEndW : WallHEndW;

        int count = (n ? 1 : 0) + (s ? 1 : 0) + (l ? 1 : 0) + (r ? 1 : 0);

        if (count == 2)
        {
            if (r && s) return NW;
            if (l && s) return NE;
            if (r && n) return SW;
            if (l && n) return SE;
            if (l && r) return H;
            if (n && s) return V;
        }

        if (count == 1)
        {
            if (l) return HE;
            if (r) return HW;
            return V;
        }

        if (l && r) return H;
        if (n && s) return V;
        if (l || r) return H;
        return V;
    }

    static bool Has(bool[,] o, int x, int y, int w, int h)
    {
        return x >= 0 && y >= 0 && x < w && y < h && o[x, y];
    }

    static GameObject EnsureRoot()
    {
        GameObject root = GameObject.Find(RootName);
        if (root == null)
        {
            root = new GameObject(RootName);
            root.AddComponent<Grid>().cellSize = Vector3.one;
        }
        else if (root.GetComponent<Grid>() == null)
        {
            root.AddComponent<Grid>().cellSize = Vector3.one;
        }
        return root;
    }

    static Dictionary<string, Tilemap> EnsureLayers(GameObject root)
    {
        var d = new Dictionary<string, Tilemap>();
        d["Ground"] = EnsureLayer(root.transform, "Ground", 0, false);
        d["Floor"] = EnsureLayer(root.transform, "Floor", 1, false);
        d["Walls"] = EnsureLayer(root.transform, "Walls", 2, true);
        d["Decor"] = EnsureLayer(root.transform, "Decor", 3, false);
        d["Props"] = EnsureLayer(root.transform, "Props", 4, false);
        return d;
    }

    static Tilemap EnsureLayer(Transform parent, string name, int order, bool collider)
    {
        Transform t = parent.Find(name);
        GameObject go = t == null ? new GameObject(name) : t.gameObject;
        if (t == null) go.transform.SetParent(parent, false);

        Tilemap map = go.GetComponent<Tilemap>();
        if (map == null) map = go.AddComponent<Tilemap>();

        TilemapRenderer r = go.GetComponent<TilemapRenderer>();
        if (r == null) r = go.AddComponent<TilemapRenderer>();
        r.sortingOrder = order;
        r.mode = TilemapRenderer.Mode.Chunk;

        TilemapCollider2D c = go.GetComponent<TilemapCollider2D>();
        if (collider)
        {
            if (c == null) go.AddComponent<TilemapCollider2D>();
        }
        else if (c != null)
        {
            UnityEngine.Object.DestroyImmediate(c);
        }

        return map;
    }

    static TileBase GetTile(int id)
    {
        TileBase cached;
        if (Cache.TryGetValue(id, out cached) && cached != null) return cached;

        string name = "tile_" + id;
        string[] guids = AssetDatabase.FindAssets(
            name + " t:Tile",
            new[] { "Assets/Arts/kenney_top-down-shooter/TilemapReady/Tiles" });

        TileBase found = FindExact(guids, name);
        if (found == null)
        {
            string legacy = "Assets/Arts/kenney_top-down-shooter/TileAssets/" + name + ".asset";
            found = AssetDatabase.LoadAssetAtPath<TileBase>(legacy);
        }
        if (found == null)
            found = FindExact(AssetDatabase.FindAssets(name + " t:Tile"), name);

        Cache[id] = found;
        return found;
    }

    static TileBase FindExact(string[] guids, string name)
    {
        if (guids == null) return null;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TileBase tile = AssetDatabase.LoadAssetAtPath<TileBase>(path);
            if (tile != null && string.Equals(tile.name, name, StringComparison.OrdinalIgnoreCase))
                return tile;
        }
        return null;
    }

    static void Frame(GameObject root, int w, int h)
    {
        Selection.activeGameObject = root;
        SceneView v = SceneView.lastActiveSceneView;
        if (v == null) return;

        v.orthographic = true;
        v.LookAt(
            new Vector3(w * 0.5f, h * 0.5f, 0f),
            Quaternion.identity,
            Mathf.Max(w, h) * 0.62f);
    }
}
#endif
