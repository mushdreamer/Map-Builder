using System;
using System.Collections.Generic;
using UnityEngine;

namespace KenneyShooter
{
    /// <summary>
    /// Maps a cell-note type string to native cols/rows (and later a prefab).
    /// Native size is the prefab occupancy at 0°.
    /// </summary>
    [CreateAssetMenu(menuName = "Kenney/Placeable Catalog", fileName = "KenneyPlaceableCatalog")]
    public class KenneyPlaceableCatalog : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string type;
            public int nativeWidth = 1;
            public int nativeHeight = 1;
            public int nativeOpening;
            public GameObject prefab;
        }

        public List<Entry> entries = new List<Entry>();

        public bool TryGetNative(string type, out int width, out int height, out int nativeOpening)
        {
            width = 1;
            height = 1;
            nativeOpening = 0;
            if (string.IsNullOrEmpty(type) || entries == null) return false;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null || !string.Equals(e.type, type, StringComparison.Ordinal))
                    continue;
                var placeable = e.prefab != null ? e.prefab.GetComponent<KenneyPlaceable>() : null;
                if (placeable != null)
                {
                    width = Mathf.Max(1, placeable.nativeCols);
                    height = Mathf.Max(1, placeable.nativeRows);
                    nativeOpening = KenneyCellNote.NormalizeRotation(placeable.nativeOpening);
                    return true;
                }
                width = Mathf.Max(1, e.nativeWidth);
                height = Mathf.Max(1, e.nativeHeight);
                nativeOpening = KenneyCellNote.NormalizeRotation(e.nativeOpening);
                return true;
            }
            return false;
        }

        public bool TryGetNative(string type, out int width, out int height)
        {
            int opening;
            return TryGetNative(type, out width, out height, out opening);
        }

        public bool TryGetPrefab(string type, out GameObject prefab)
        {
            prefab = null;
            if (string.IsNullOrEmpty(type) || entries == null) return false;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null || e.prefab == null) continue;
                if (string.Equals(e.type, type, StringComparison.Ordinal)
                    || string.Equals(e.prefab.name, type, StringComparison.Ordinal))
                {
                    prefab = e.prefab;
                    return true;
                }
            }
            return false;
        }

        public void UpsertPrefab(string type, GameObject prefab)
        {
            if (string.IsNullOrEmpty(type) || prefab == null) return;
            int width = 1, height = 1, opening = 0;
            var placeable = prefab.GetComponent<KenneyPlaceable>();
            if (placeable != null)
            {
                width = Mathf.Max(1, placeable.nativeCols);
                height = Mathf.Max(1, placeable.nativeRows);
                opening = KenneyCellNote.NormalizeRotation(placeable.nativeOpening);
            }
            if (entries == null) entries = new List<Entry>();
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null || !string.Equals(e.type, type, StringComparison.Ordinal))
                    continue;
                e.prefab = prefab;
                if (placeable != null)
                {
                    e.nativeWidth = width;
                    e.nativeHeight = height;
                    e.nativeOpening = opening;
                }
                return;
            }
            entries.Add(new Entry
            {
                type = type,
                nativeWidth = width,
                nativeHeight = height,
                nativeOpening = opening,
                prefab = prefab
            });
        }

        public void UpsertNative(string type, int width, int height, int nativeOpening)
        {
            if (string.IsNullOrEmpty(type)) return;
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            nativeOpening = KenneyCellNote.NormalizeRotation(nativeOpening);
            if (entries == null) entries = new List<Entry>();
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null || !string.Equals(e.type, type, StringComparison.Ordinal))
                    continue;
                if (e.prefab != null && e.prefab.GetComponent<KenneyPlaceable>() != null)
                    return;
                e.nativeWidth = width;
                e.nativeHeight = height;
                e.nativeOpening = nativeOpening;
                return;
            }
            entries.Add(new Entry
            {
                type = type,
                nativeWidth = width,
                nativeHeight = height,
                nativeOpening = nativeOpening
            });
        }
    }
}
