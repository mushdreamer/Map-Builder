using System.Collections.Generic;
using UnityEngine;

namespace KenneyShooter
{
    /// <summary>Scene-owned source of truth for editable PSD-derived prefab instances.</summary>
    public class KenneyArtPlacementStore : MonoBehaviour
    {
        public List<KenneyArtPlacement> placements = new List<KenneyArtPlacement>();

        public void ReplaceAll(List<KenneyArtPlacement> value)
        {
            placements = value != null
                ? new List<KenneyArtPlacement>(value)
                : new List<KenneyArtPlacement>();
        }
    }
}
