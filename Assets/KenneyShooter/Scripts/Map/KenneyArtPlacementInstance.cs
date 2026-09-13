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
        public float scoreMargin;
        public int candidateRank;
        public string tier;
        public string targetLayer;
        public string reviewState;
        public bool noCollision;
    }
}
