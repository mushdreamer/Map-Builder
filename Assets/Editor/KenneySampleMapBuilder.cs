using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Builds a large (~5x area), multi-style Kenney tilemap into the active scene.
/// Non-rectangular buildings, mixed floors, outdoor paths, pond, heavy prop usage.
/// </summary>
public static class KenneySampleMapBuilder
{
    const string TilePngFolder = "Assets/Arts/kenney_top-down-shooter/PNG/Tiles";
    const string TileAssetFolder = "Assets/Arts/kenney_top-down-shooter/TileAssets";
    const string RootName = "KenneySampleMap";

    const int MapW = 60;
    const int MapH = 40;

    struct WallKit
    {
        public int NW, NE, SW, SE, H, V, HEndE, HEndW;
        public static WallKit Orange()
        {
            return new WallKit { NW = 109, NE = 110, SW = 136, SE = 137, H = 111, V = 138, HEndE = 114, HEndW = 142 };
        }
        public static WallKit Cyan()
        {
            return new WallKit { NW = 280, NE = 281, SW = 307, SE = 308, H = 282, V = 309, HEndE = 285, HEndW = 313 };
        }
        public static WallKit Tan()
        {
            return new WallKit { NW = 118, NE = 119, SW = 145, SE = 146, H = 120, V = 147, HEndE = 123, HEndW = 151 };
        }
    }

    static readonly Dictionary<int, Tile> TileCache = new Dictionary<int, Tile>();
    static System.Random Rng;

    [MenuItem("Tools/Kenney/Build Large Varied Map")]
    public static void BuildLarge()
    {
        BuildInternal();
    }

    [MenuItem("Tools/Kenney/Build Sample-Style Map")]
    public static void Build()
    {
        BuildInternal();
    }

    static void BuildInternal()
    {
        TileCache.Clear();
        Rng = new System.Random(20260821);
        EnsureFolders();

        var existing = GameObject.Find(RootName);
        if (existing != null)
            Object.DestroyImmediate(existing);

        var root = new GameObject(RootName);
        var grid = root.AddComponent<Grid>();
        grid.cellSize = new Vector3(1f, 1f, 0f);

        var ground = CreateLayer(root.transform, "Ground", 0);
        var floor = CreateLayer(root.transform, "Floor", 1);
        var walls = CreateLayer(root.transform, "Walls", 2);
        walls.gameObject.AddComponent<TilemapCollider2D>();
        var decor = CreateLayer(root.transform, "Decor", 3);
        var props = CreateLayer(root.transform, "Props", 4);

        PaintOutdoorGround(ground);
        PaintDirtPaths(ground);
        PaintPond(ground, decor, 42, 4, 54, 14);

        // 1) Orange L-shaped cabin (wood + checker) — top-leftish
        BuildLCabin(floor, walls, decor, props, WallKit.Orange(), 6, 22, 26, 37);

        // 2) Cyan tech lab — irregular stepped footprint — right side
        BuildSteppedLab(floor, walls, decor, props, WallKit.Cyan(), 34, 18, 55, 35);

        // 3) Tan warehouse + dogleg corridor — bottom-left
        BuildWarehouse(floor, walls, decor, props, WallKit.Tan(), 4, 3, 28, 15);

        // 4) Small outdoor ruins / patio (partial walls, mixed floors)
        BuildRuinsPatio(floor, walls, decor, props, 30, 6, 40, 14);

        // 5) Scatter outdoor nature + clutter heavily
        ScatterOutdoor(ground, decor, props);

        SetupCamera();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        FrameMap(root);
        Debug.Log(string.Format("[KenneySampleMapBuilder] Large map {0}x{1} built into {2}", MapW, MapH, RootName));
    }

    // ---------- Outdoor ----------

    static void PaintOutdoorGround(Tilemap ground)
    {
        int[] grass = { 401, 1, 2, 3, 4, 17, 18 };
        for (int y = 0; y < MapH; y++)
        {
            for (int x = 0; x < MapW; x++)
            {
                int g = grass[(x * 17 + y * 31 + Rng.Next(3)) % grass.Length];
                // Prefer flat grass most of the time
                if (Rng.Next(100) < 70) g = 401;
                else if (Rng.Next(100) < 50) g = grass[Rng.Next(1, grass.Length)];
                Set(ground, x, y, g);
            }
        }
    }

    static void PaintDirtPaths(Tilemap ground)
    {
        int[] dirt = { 13, 14, 15, 16, 5, 6, 69, 70 };
        // Main winding path (south-west → north-east)
        DrawWindingStrip(ground, dirt, 3, 8, 50, 32, 2);
        // Branch toward cabin
        DrawWindingStrip(ground, dirt, 18, 14, 16, 22, 1);
        // Branch toward lab
        DrawWindingStrip(ground, dirt, 40, 16, 44, 20, 1);
        // Branch toward pond
        DrawWindingStrip(ground, dirt, 36, 10, 42, 8, 1);
    }

    static void DrawWindingStrip(Tilemap map, int[] tiles, int x0, int y0, int x1, int y1, int radius)
    {
        int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0)) * 2 + 8;
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float wobble = Mathf.Sin(t * 6.2f) * 2.2f + Mathf.Sin(t * 13f) * 0.8f;
            int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t));
            int y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t) + wobble);
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > radius * radius + 1) continue;
                    int px = x + dx, py = y + dy;
                    if (!InBounds(px, py)) continue;
                    Set(map, px, py, tiles[Rng.Next(tiles.Length)]);
                }
            }
        }
    }

    static void PaintPond(Tilemap ground, Tilemap decor, int x0, int y0, int x1, int y1)
    {
        int[] water = { 19, 20 };
        int[] rim = { 159, 160 };
        int cx = (x0 + x1) / 2;
        int cy = (y0 + y1) / 2;
        float rx = (x1 - x0) * 0.5f;
        float ry = (y1 - y0) * 0.5f;
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float nx = (x - cx) / rx;
                float ny = (y - cy) / ry;
                float d = nx * nx + ny * ny;
                if (d <= 1f)
                {
                    Set(ground, x, y, water[Rng.Next(water.Length)]);
                    if (d > 0.72f && Rng.Next(100) < 55)
                        Set(decor, x, y, rim[Rng.Next(rim.Length)]);
                }
            }
        }
        // Reeds / plants near shore
        int[] shore = { 210, 213, 235, 134, 240 };
        for (int i = 0; i < 18; i++)
        {
            int x = Rng.Next(x0 - 1, x1 + 2);
            int y = Rng.Next(y0 - 1, y1 + 2);
            if (!InBounds(x, y)) continue;
            float nx = (x - cx) / rx;
            float ny = (y - cy) / ry;
            float d = nx * nx + ny * ny;
            if (d > 0.85f && d < 1.35f)
                Set(decor, x, y, shore[Rng.Next(shore.Length)]);
        }
    }

    static void ScatterOutdoor(Tilemap ground, Tilemap decor, Tilemap props)
    {
        int[] bushes = { 181, 182, 183, 208, 209, 210, 48, 49, 50, 75, 76, 77 };
        int[] trees = { 158, 183, 208, 209 };
        int[] debris = { 262, 263, 264, 265, 266, 267, 268, 24, 25, 26 };
        int[] crates = { 129, 130, 156, 157, 171, 180, 224, 225, 251, 252 };
        int[] barrels = { 318, 196, 197, 298, 299 };
        int[] rocks = { 21, 22, 23, 51, 52, 53 };

        for (int i = 0; i < 90; i++)
        {
            int x = Rng.Next(0, MapW);
            int y = Rng.Next(0, MapH);
            if (IsBusyInterior(x, y)) continue;
            Set(props, x, y, bushes[Rng.Next(bushes.Length)]);
        }
        for (int i = 0; i < 35; i++)
        {
            int x = Rng.Next(0, MapW);
            int y = Rng.Next(0, MapH);
            if (IsBusyInterior(x, y)) continue;
            Set(props, x, y, trees[Rng.Next(trees.Length)]);
        }
        for (int i = 0; i < 55; i++)
        {
            int x = Rng.Next(0, MapW);
            int y = Rng.Next(0, MapH);
            if (IsBusyInterior(x, y)) continue;
            Set(decor, x, y, debris[Rng.Next(debris.Length)]);
        }
        for (int i = 0; i < 28; i++)
        {
            int x = Rng.Next(0, MapW);
            int y = Rng.Next(0, MapH);
            if (IsBusyInterior(x, y)) continue;
            Set(props, x, y, crates[Rng.Next(crates.Length)]);
        }
        for (int i = 0; i < 16; i++)
        {
            int x = Rng.Next(0, MapW);
            int y = Rng.Next(0, MapH);
            if (IsBusyInterior(x, y)) continue;
            Set(props, x, y, rocks[Rng.Next(rocks.Length)]);
        }
        for (int i = 0; i < 10; i++)
        {
            int x = Rng.Next(0, MapW);
            int y = Rng.Next(0, MapH);
            if (IsBusyInterior(x, y)) continue;
            Set(props, x, y, barrels[Rng.Next(barrels.Length)]);
        }
    }

    static bool IsBusyInterior(int x, int y)
    {
        // Rough AABBs of buildings / pond to reduce clutter overlap
        if (x >= 6 && x <= 26 && y >= 22 && y <= 37) return true;
        if (x >= 34 && x <= 55 && y >= 18 && y <= 35) return true;
        if (x >= 4 && x <= 28 && y >= 3 && y <= 20) return true; // warehouse + annex
        if (x >= 30 && x <= 40 && y >= 6 && y <= 14) return true; // ruins patio
        if (x >= 42 && x <= 54 && y >= 4 && y <= 14) return true; // pond
        return false;
    }

    // ---------- Buildings ----------

    /// <summary>L-shape: wide bottom bar + tall left stem.</summary>
    static void BuildLCabin(Tilemap floor, Tilemap walls, Tilemap decor, Tilemap props, WallKit kit, int x0, int y0, int x1, int y1)
    {
        int midX = x0 + 10;
        int midY = y0 + 7;

        // Floor: wood in stem+bottom-left, checker in bottom-right wing
        FillFloor(floor, x0, midY, midX, y1, new[] { 42, 43, 44, 45 });
        FillFloor(floor, x0, y0, x1, midY, new[] { 42, 43, 46, 47 });
        FillFloor(floor, midX, y0, x1, midY, new[] { 7, 8, 9, 10, 11 });

        // Outer L outline (manual so corners meet correctly)
        // Stem (left tall) rectangle outline + wing (bottom right) sharing left wall of wing with stem bottom
        DrawRectOutline(walls, kit, x0, midY, midX, y1, true, true, true, true);
        DrawRectOutline(walls, kit, midX, y0, x1, midY, true, true, false, true);
        // Open connection between stem and wing along midY inside midX.. — remove shared wall stubs
        ClearHLine(walls, x0 + 1, midX - 1, midY);
        // Door south on wing
        CutSouthDoor(walls, kit, midX + 3, midX + 5, y0);
        // Door east on wing
        CutEastDoor(walls, kit, x1, y0 + 2, y0 + 3);
        // Door west on stem
        CutWestDoor(walls, kit, x0, midY + 2, midY + 3);

        // Interior partition in wing
        for (int y = y0 + 1; y < midY; y++)
        {
            if (y == y0 + 3) continue;
            Set(walls, midX + 4, y, kit.V);
        }

        // Windows accents
        Set(decor, x0 + 3, y1, 436);
        Set(decor, x0 + 4, y1, 436);
        Set(decor, x1, y0 + 4, 433);

        // Furniture — living
        PlaceSofa(props, x0 + 2, midY + 3, 447, 448, 449);
        Set(props, x0 + 3, midY + 1, 507);
        Set(props, x0 + 2, midY + 1, 450);
        Set(props, x0 + 4, midY + 1, 450);
        Set(decor, x0 + 3, midY + 2, 511);
        Set(props, x0 + 1, y1 - 1, 210);
        Set(props, midX - 1, midY + 1, 528);

        // Checker wing office / kitchen
        Set(props, midX + 2, y0 + 2, 506);
        Set(props, midX + 3, y0 + 1, 450);
        Set(props, midX + 6, y0 + 4, 270);
        Set(props, midX + 7, y0 + 4, 269);
        Set(props, midX + 6, y0 + 2, 214);
        Set(props, x1 - 2, midY - 2, 129);
        Set(props, x1 - 1, midY - 1, 180);
    }

    /// <summary>Stepped / terraced cyan lab (not a plain rectangle).</summary>
    static void BuildSteppedLab(Tilemap floor, Tilemap walls, Tilemap decor, Tilemap props, WallKit kit, int x0, int y0, int x1, int y1)
    {
        // Three stacked rectangles of different widths
        int a1 = x0 + 6;   // upper terrace left
        int b0 = y0 + 6;   // mid band
        int b1 = y0 + 12;  // upper band

        int[] techFloor = { 271, 272, 273, 274, 275, 276, 277, 278, 279, 325, 326 };
        int[] metal = { 28, 29, 30, 35, 36, 37, 38, 39 };

        FillFloor(floor, x0, y0, x1 - 4, b0, techFloor);
        FillFloor(floor, x0 + 3, b0, x1, b1, techFloor);
        FillFloor(floor, a1, b1, x1, y1, metal);

        // Outlines of each terrace (leave open where they connect)
        DrawRectOutline(walls, kit, x0, y0, x1 - 4, b0, true, true, true, false);
        DrawRectOutline(walls, kit, x0 + 3, b0, x1, b1, false, true, true, false);
        DrawRectOutline(walls, kit, a1, b1, x1, y1, false, true, true, true);

        // Open seams between terraces
        ClearHLine(walls, x0 + 4, x1 - 5, b0);
        ClearHLine(walls, a1 + 1, x1 - 1, b1);

        CutSouthDoor(walls, kit, x0 + 8, x0 + 10, y0);
        CutEastDoor(walls, kit, x1, b0 + 2, b0 + 3);

        // Interior baffles
        for (int y = y0 + 1; y < b0; y++)
        {
            if (y == y0 + 3) continue;
            Set(walls, x0 + 10, y, kit.V);
        }

        // Glow / glass accents
        int[] glow = { 460, 461, 462, 463, 464, 465, 489, 490 };
        for (int i = 0; i < 12; i++)
            Set(decor, Rng.Next(x0 + 1, x1 - 1), Rng.Next(y0 + 1, y1 - 1), glow[Rng.Next(glow.Length)]);

        Set(decor, x0 + 5, y0, 436);
        Set(decor, x1, b1 + 2, 433);
        Set(decor, x1, b1 + 3, 433);

        // Tech props
        Set(props, x0 + 2, y0 + 2, 270);
        Set(props, x0 + 4, y0 + 2, 271);
        Set(props, x0 + 6, y0 + 4, 506);
        Set(props, x0 + 12, y0 + 2, 501);
        Set(props, x0 + 13, y0 + 2, 502);
        Set(props, x0 + 14, y0 + 2, 503);
        Set(props, x1 - 3, b0 + 3, 318);
        Set(props, x1 - 2, b0 + 4, 129);
        Set(props, a1 + 2, b1 + 2, 507);
        Set(props, a1 + 4, b1 + 2, 528);
        Set(props, a1 + 3, y1 - 2, 214);
        Set(props, x1 - 4, y1 - 2, 180);
    }

    /// <summary>Long warehouse with dogleg annex.</summary>
    static void BuildWarehouse(Tilemap floor, Tilemap walls, Tilemap decor, Tilemap props, WallKit kit, int x0, int y0, int x1, int y1)
    {
        int annexX0 = x1 - 8;
        int annexY1 = y1 + 5;
        if (annexY1 >= MapH) annexY1 = MapH - 2;

        int[] concrete = { 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41 };
        int[] wood = { 69, 70, 71, 72, 73, 74 };

        FillFloor(floor, x0, y0, x1, y1, concrete);
        FillFloor(floor, annexX0, y1, x1, annexY1, wood);

        DrawRectOutline(walls, kit, x0, y0, x1, y1, true, true, true, false);
        DrawRectOutline(walls, kit, annexX0, y1, x1, annexY1, false, true, true, true);
        ClearHLine(walls, annexX0 + 1, x1 - 1, y1);

        CutSouthDoor(walls, kit, x0 + 4, x0 + 6, y0);
        CutWestDoor(walls, kit, x0, y0 + 5, y0 + 6);
        CutEastDoor(walls, kit, x1, annexY1 - 3, annexY1 - 2);

        // Internal storage rows
        for (int x = x0 + 3; x < x1 - 2; x += 4)
        {
            for (int y = y0 + 2; y < y1 - 1; y += 3)
            {
                if (Rng.Next(100) < 70) Set(props, x, y, 129 + Rng.Next(0, 2));
                if (Rng.Next(100) < 50) Set(props, x + 1, y, 156 + Rng.Next(0, 2));
            }
        }

        // Annex lounge
        PlaceSofa(props, annexX0 + 1, annexY1 - 2, 474, 475, 476);
        Set(props, annexX0 + 2, annexY1 - 4, 507);
        Set(props, annexX0 + 4, annexY1 - 3, 477);
        Set(decor, annexX0 + 2, annexY1 - 3, 511);
        Set(props, x1 - 2, annexY1 - 2, 210);

        Set(decor, x0 + 10, y1, 436);
        Set(decor, x0 + 11, y1, 436);
    }

    /// <summary>Broken patio — incomplete walls, mixed floor patches.</summary>
    static void BuildRuinsPatio(Tilemap floor, Tilemap walls, Tilemap decor, Tilemap props, int x0, int y0, int x1, int y1)
    {
        var kit = WallKit.Orange();
        int[] patio = { 12, 272, 273, 42, 7 };
        FillFloor(floor, x0, y0, x1, y1, patio);

        // Only three sides + broken corners
        Set(walls, x0, y1, kit.NW);
        for (int x = x0 + 1; x <= x1 - 1; x++)
        {
            if (x == x0 + 3 || x == x0 + 4) continue; // gap
            Set(walls, x, y1, kit.H);
        }
        Set(walls, x1, y1, kit.NE);
        for (int y = y0 + 1; y <= y1 - 1; y++)
        {
            if (y == y0 + 2) continue;
            Set(walls, x0, y, kit.V);
        }
        Set(walls, x0, y0, kit.SW);
        for (int x = x0 + 1; x <= x0 + 4; x++)
            Set(walls, x, y0, kit.H);

        Set(props, x0 + 2, y0 + 3, 506);
        Set(props, x0 + 4, y0 + 2, 450);
        Set(props, x0 + 5, y0 + 4, 183);
        Set(decor, x0 + 3, y0 + 1, 262);
        Set(decor, x0 + 6, y0 + 3, 265);
        Set(props, x1 - 1, y1 - 1, 196); // pillar block
        Set(props, x1 - 2, y0 + 1, 197);
    }

    // ---------- Wall helpers ----------

    static void DrawRectOutline(Tilemap walls, WallKit kit, int x0, int y0, int x1, int y1,
        bool south, bool north, bool west, bool east)
    {
        if (north)
        {
            Set(walls, x0, y1, kit.NW);
            Set(walls, x1, y1, kit.NE);
            for (int x = x0 + 1; x <= x1 - 1; x++) Set(walls, x, y1, kit.H);
        }
        if (south)
        {
            Set(walls, x0, y0, kit.SW);
            Set(walls, x1, y0, kit.SE);
            for (int x = x0 + 1; x <= x1 - 1; x++) Set(walls, x, y0, kit.H);
        }
        if (west)
        {
            for (int y = y0 + 1; y <= y1 - 1; y++) Set(walls, x0, y, kit.V);
            if (!south) Set(walls, x0, y0, kit.V);
            if (!north) Set(walls, x0, y1, kit.V);
        }
        if (east)
        {
            for (int y = y0 + 1; y <= y1 - 1; y++) Set(walls, x1, y, kit.V);
            if (!south) Set(walls, x1, y0, kit.V);
            if (!north) Set(walls, x1, y1, kit.V);
        }
    }

    static void ClearHLine(Tilemap walls, int x0, int x1, int y)
    {
        for (int x = x0; x <= x1; x++)
            walls.SetTile(new Vector3Int(x, y, 0), null);
    }

    static void CutSouthDoor(Tilemap walls, WallKit kit, int doorL, int doorR, int y)
    {
        for (int x = doorL; x <= doorR; x++)
            walls.SetTile(new Vector3Int(x, y, 0), null);
        Set(walls, doorL - 1, y, kit.HEndE);
        Set(walls, doorR + 1, y, kit.HEndW);
    }

    static void CutEastDoor(Tilemap walls, WallKit kit, int x, int doorB, int doorT)
    {
        for (int y = doorB; y <= doorT; y++)
            walls.SetTile(new Vector3Int(x, y, 0), null);
        // keep vertical neighbors as V (no dedicated V-end in kit)
        if (walls.HasTile(new Vector3Int(x, doorB - 1, 0))) Set(walls, x, doorB - 1, kit.V);
        if (walls.HasTile(new Vector3Int(x, doorT + 1, 0))) Set(walls, x, doorT + 1, kit.V);
    }

    static void CutWestDoor(Tilemap walls, WallKit kit, int x, int doorB, int doorT)
    {
        for (int y = doorB; y <= doorT; y++)
            walls.SetTile(new Vector3Int(x, y, 0), null);
        if (walls.HasTile(new Vector3Int(x, doorB - 1, 0))) Set(walls, x, doorB - 1, kit.V);
        if (walls.HasTile(new Vector3Int(x, doorT + 1, 0))) Set(walls, x, doorT + 1, kit.V);
    }

    static void FillFloor(Tilemap floor, int x0, int y0, int x1, int y1, int[] tiles)
    {
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (InBounds(x, y))
                    Set(floor, x, y, tiles[Rng.Next(tiles.Length)]);
    }

    static void PlaceSofa(Tilemap props, int x, int y, int l, int m, int r)
    {
        Set(props, x, y, l);
        Set(props, x + 1, y, m);
        Set(props, x + 2, y, r);
    }

    // ---------- Core ----------

    static bool InBounds(int x, int y)
    {
        return x >= 0 && y >= 0 && x < MapW && y < MapH;
    }

    static Tilemap CreateLayer(Transform parent, string name, int sortingOrder)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var tm = go.AddComponent<Tilemap>();
        var r = go.AddComponent<TilemapRenderer>();
        r.sortingOrder = sortingOrder;
        r.mode = TilemapRenderer.Mode.Chunk;
        return tm;
    }

    static void Set(Tilemap map, int x, int y, int tileId)
    {
        if (!InBounds(x, y)) return;
        var tile = GetTile(tileId);
        if (tile == null) return;
        map.SetTile(new Vector3Int(x, y, 0), tile);
    }

    static Tile GetTile(int id)
    {
        Tile tile;
        if (TileCache.TryGetValue(id, out tile) && tile != null)
            return tile;

        string assetPath = TileAssetFolder + "/tile_" + id + ".asset";
        tile = AssetDatabase.LoadAssetAtPath<Tile>(assetPath);
        if (tile == null)
        {
            var sprite = LoadSprite(id);
            if (sprite == null)
            {
                Debug.LogWarning("[KenneySampleMapBuilder] Missing sprite tile_" + id);
                TileCache[id] = null;
                return null;
            }

            tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            tile.name = "tile_" + id;
            tile.colliderType = IsWallId(id) ? Tile.ColliderType.Grid : Tile.ColliderType.None;
            AssetDatabase.CreateAsset(tile, assetPath);
        }

        TileCache[id] = tile;
        return tile;
    }

    static bool IsWallId(int id)
    {
        var o = WallKit.Orange();
        var c = WallKit.Cyan();
        var t = WallKit.Tan();
        return id == o.NW || id == o.NE || id == o.SW || id == o.SE || id == o.H || id == o.V || id == o.HEndE || id == o.HEndW
            || id == c.NW || id == c.NE || id == c.SW || id == c.SE || id == c.H || id == c.V || id == c.HEndE || id == c.HEndW
            || id == t.NW || id == t.NE || id == t.SW || id == t.SE || id == t.H || id == t.V || id == t.HEndE || id == t.HEndW
            || id == 196 || id == 197;
    }

    static Sprite LoadSprite(int id)
    {
        string path = id < 10
            ? string.Format("{0}/tile_{1:00}.png", TilePngFolder, id)
            : string.Format("{0}/tile_{1}.png", TilePngFolder, id);

        EnsureSpriteImport(path);
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite != null) return sprite;

        var assets = AssetDatabase.LoadAllAssetsAtPath(path);
        if (assets == null) return null;
        for (int i = 0; i < assets.Length; i++)
        {
            var s = assets[i] as Sprite;
            if (s != null) return s;
        }
        return null;
    }

    static void EnsureSpriteImport(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;
        bool dirty = false;
        if (importer.textureType != TextureImporterType.Sprite) { importer.textureType = TextureImporterType.Sprite; dirty = true; }
        if (importer.spriteImportMode != SpriteImportMode.Single) { importer.spriteImportMode = SpriteImportMode.Single; dirty = true; }
        if (System.Math.Abs(importer.spritePixelsPerUnit - 64f) > 0.01f) { importer.spritePixelsPerUnit = 64f; dirty = true; }
        if (importer.filterMode != FilterMode.Point) { importer.filterMode = FilterMode.Point; dirty = true; }
        if (importer.mipmapEnabled) { importer.mipmapEnabled = false; dirty = true; }
        if (dirty) importer.SaveAndReimport();
    }

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Arts/kenney_top-down-shooter/TileAssets"))
            AssetDatabase.CreateFolder("Assets/Arts/kenney_top-down-shooter", "TileAssets");
    }

    static void SetupCamera()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var cams = Object.FindObjectsOfType<Camera>();
            if (cams.Length > 0) cam = cams[0];
        }
        if (cam == null) return;
        cam.orthographic = true;
        cam.orthographicSize = 22f;
        cam.transform.position = new Vector3(MapW * 0.5f - 0.5f, MapH * 0.5f - 0.5f, -10f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.14f, 0.16f, 1f);
    }

    static void FrameMap(GameObject root)
    {
        Selection.activeGameObject = root;
        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.orthographic = true;
            SceneView.lastActiveSceneView.LookAt(
                new Vector3(MapW * 0.5f, MapH * 0.5f, 0f),
                Quaternion.identity,
                42f);
        }
    }
}
