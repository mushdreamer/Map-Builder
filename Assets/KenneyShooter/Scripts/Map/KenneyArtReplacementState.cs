using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace KenneyShooter
{
    /// <summary>Scene-persisted rollback data for idempotent reconstruction replacement.</summary>
    public class KenneyArtReplacementState : MonoBehaviour
    {
        public List<Vector3Int> clearedWallCells = new List<Vector3Int>();
        public List<TileBase> clearedWallTiles = new List<TileBase>();
        public List<Vector3Int> replacedFloorCells = new List<Vector3Int>();
        public List<TileBase> replacedFloorTiles = new List<TileBase>();
    }
}
