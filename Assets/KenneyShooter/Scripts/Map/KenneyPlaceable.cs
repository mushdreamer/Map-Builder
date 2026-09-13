using UnityEngine;

namespace KenneyShooter
{
    /// <summary>
    /// Put on a placeable prefab: native footprint and opening at 0° (as authored).
    /// Opening 0=up, 90=left, 180=down, 270=right. Sofa example: 3x1, opening up.
    /// </summary>
    public class KenneyPlaceable : MonoBehaviour
    {
        [Min(1)] public int nativeCols = 1;
        [Min(1)] public int nativeRows = 1;
        [Tooltip("0=上, 90=左, 180=下, 270=右")]
        public int nativeOpening;
    }
}
