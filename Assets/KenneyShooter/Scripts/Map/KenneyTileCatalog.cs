using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace KenneyShooter
{
    /// <summary>
    /// Runtime lookup: Kenney tile id → Tile asset. Built by editor menu from TilemapReady.
    /// </summary>
    [CreateAssetMenu(menuName = "Kenney/Tile Catalog", fileName = "KenneyTileCatalog")]
    public class KenneyTileCatalog : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public int id;
            public Tile tile;
        }

        public Entry[] entries = Array.Empty<Entry>();

        [NonSerialized] private Dictionary<int, Tile> _lookup;

        public int Count => entries != null ? entries.Length : 0;

        public void RebuildLookup()
        {
            _lookup = new Dictionary<int, Tile>(entries != null ? entries.Length : 0);
            if (entries == null) return;
            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                if (e == null || e.tile == null) continue;
                _lookup[e.id] = e.tile;
            }
        }

        public bool TryGet(int id, out Tile tile)
        {
            if (_lookup == null)
                RebuildLookup();
            return _lookup.TryGetValue(id, out tile);
        }
    }
}
