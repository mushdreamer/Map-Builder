using UnityEngine;

namespace KenneyShooter
{
    /// <summary>Links an editable scene prefab instance back to its reconstruction record.</summary>
    public class KenneyArtPlacementInstance : MonoBehaviour
    {
        public string instanceId;
        public string assetKey;
        public string layerPath;
        public float confidence;
        public string reviewState;
        public bool noCollision;
    }
}
