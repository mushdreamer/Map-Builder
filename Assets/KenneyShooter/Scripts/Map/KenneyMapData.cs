using System;
using System.Collections.Generic;
using UnityEngine;

namespace KenneyShooter
{
    /// <summary>
    /// Cross-project map delivery format (designer export → game local load).
    /// Tile identity is Kenney numeric id (tile_109 → 109), not Unity GUID.
    /// </summary>
    [Serializable]
    public class KenneyMapFile
    {
        public int formatVersion = KenneyMapFormat.CurrentVersion;
        public string name;
        public int width;
        public int height;
        public float cellSize = 1f;
        public List<KenneyMapLayer> layers = new List<KenneyMapLayer>();
        public List<KenneyCellNote> cellNotes = new List<KenneyCellNote>();
        public KenneyMapMeta meta = new KenneyMapMeta();
    }

    [Serializable]
    public class KenneyMapLayer
    {
        public string name;
        public int sortingOrder;
        public bool hasCollider;
        public List<KenneyMapTile> tiles = new List<KenneyMapTile>();
    }

    [Serializable]
    public class KenneyMapTile
    {
        public int x;
        public int y;
        public int id;
    }

    /// <summary>
    /// Per-cell designer note. Anchor is the bottom-left cell; footprint grows +X / +Y.
    /// width/height is world occupancy. rotation is Z degrees from the authored prefab (0 = as drawn).
    /// Native 1x3 and native 3x1 are both valid at 0°. 90/270 swap occupancy axes vs 0/180.
    /// </summary>
    [Serializable]
    public class KenneyCellNote
    {
        public string layer;
        public int x;
        public int y;
        public int width = 1;
        public int height = 1;
        public int rotation;
        public int opening;
        public string type = "";
        public string description = "";

        public bool MatchesAnchor(string layerName, int ax, int ay)
        {
            return x == ax && y == ay
                && string.Equals(layer, layerName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Snaps any degree value to 0, 90, 180, or 270.
        /// </summary>
        public static int NormalizeRotation(int degrees)
        {
            int r = degrees % 360;
            if (r < 0) r += 360;
            return ((r + 45) / 90) * 90 % 360;
        }

        public static bool IsAxisSwappedRotation(int degrees)
        {
            int r = NormalizeRotation(degrees);
            return r == 90 || r == 270;
        }

        /// <summary>
        /// 0/180 keep native axes; 90/270 swap them. Changing between those groups swaps width/height.
        /// </summary>
        public static void SwapSizeIfRotationTurnsAxes(
            ref int width, ref int height, int fromRotation, int toRotation)
        {
            if (width < 1) width = 1;
            if (height < 1) height = 1;
            if (width == height) return;
            if (IsAxisSwappedRotation(fromRotation) == IsAxisSwappedRotation(toRotation))
                return;
            int tmp = width;
            width = height;
            height = tmp;
        }

        public static void NativeFromWorld(
            int worldWidth, int worldHeight, int rotation, out int nativeWidth, out int nativeHeight)
        {
            worldWidth = Mathf.Max(1, worldWidth);
            worldHeight = Mathf.Max(1, worldHeight);
            if (IsAxisSwappedRotation(rotation))
            {
                nativeWidth = worldHeight;
                nativeHeight = worldWidth;
            }
            else
            {
                nativeWidth = worldWidth;
                nativeHeight = worldHeight;
            }
        }

        /// <summary>
        /// Infers 0/90/180/270 from world occupancy vs prefab native size.
        /// Same axes → 0 or 180; swapped axes → 90 or 270. Preserves flip when possible.
        /// Returns false if occupancy does not match native or swapped-native.
        /// </summary>
        public static bool TryRotationFromFootprint(
            int nativeWidth, int nativeHeight, int worldWidth, int worldHeight,
            int currentRotation, out int rotation)
        {
            rotation = NormalizeRotation(currentRotation);
            nativeWidth = Mathf.Max(1, nativeWidth);
            nativeHeight = Mathf.Max(1, nativeHeight);
            worldWidth = Mathf.Max(1, worldWidth);
            worldHeight = Mathf.Max(1, worldHeight);

            bool same = worldWidth == nativeWidth && worldHeight == nativeHeight;
            bool swapped = nativeWidth != nativeHeight
                && worldWidth == nativeHeight && worldHeight == nativeWidth;
            if (same)
            {
                if (IsAxisSwappedRotation(rotation))
                    rotation = rotation == 270 ? 180 : 0;
                return true;
            }
            if (swapped)
            {
                if (!IsAxisSwappedRotation(rotation))
                    rotation = rotation == 180 ? 270 : 90;
                return true;
            }
            return false;
        }

        public static int RotationFromOpening(int nativeOpening, int worldOpening)
        {
            return NormalizeRotation(worldOpening - nativeOpening);
        }

        public static int OpeningFromRotation(int nativeOpening, int rotation)
        {
            return NormalizeRotation(nativeOpening + rotation);
        }

        public Quaternion PrefabRotation()
        {
            return Quaternion.Euler(0f, 0f, NormalizeRotation(rotation));
        }
    }

    [Serializable]
    public class KenneyMapMeta
    {
        public List<KenneyMapSpawnPoint> spawnPoints = new List<KenneyMapSpawnPoint>();
    }

    [Serializable]
    public class KenneyMapSpawnPoint
    {
        public float x;
        public float y;
        public string tag = "player";
    }

    public static class KenneyMapFormat
    {
        public const int CurrentVersion = 2;

        public const string RootObjectName = "KenneySampleMap";
        public static readonly string[] LayerOrder =
        {
            "Ground", "Floor", "Walls", "Decor", "Props"
        };

        public static readonly int[] LayerSorting = { 0, 1, 2, 3, 4 };

        public static bool LayerShouldCollide(string layerName)
        {
            return string.Equals(layerName, "Walls", StringComparison.Ordinal);
        }

        /// <summary>StreamingAssets relative folder for delivered maps.</summary>
        public const string StreamingMapsFolder = "Maps";

        /// <summary>Default JSON used by PlayScene / KenneyMapLoader.</summary>
        public const string DefaultMapFile = "sample_large.map.json";

        /// <summary>地编与运行时共用的标注 Prefab 目录。类型名 = Prefab 文件名。</summary>
        public const string PlaceablePrefabFolder = "Assets/KenneyShooter/Prefabs";

        public const string PlaceableCatalogAssetPath = "Assets/KenneyShooter/Config/KenneyPlaceableCatalog.asset";
    }
}
