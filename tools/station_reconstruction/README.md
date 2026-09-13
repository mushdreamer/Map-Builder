# Station reconstruction audit / offline PoC

This command performs Milestone 0 without touching the Unity project. It accepts
either the delivered ZIP or its extracted directory, inventories TMX, PSD and PNG
inputs, verifies hashes and dimensions, and tries to associate split PNG files
with PSD leaf layers by alpha-aware pixel identity.

```bash
python3 -m pip install -r tools/station_reconstruction/requirements.txt
python3 tools/station_reconstruction/audit.py \
  Station_Reconstruction_Sample.zip \
  --output ReconstructionReports/station
```

Generated files:

- `reconstruction.manifest.json`: machine-readable source inventory, TMX masks,
  PSD tree, PNG metadata and top matching candidates;
- `unity-placements.json`: every split PNG plus editable placements for PSD leaf
  layers/groups with a high-confidence, unambiguous deterministic match;
- `audit-report.md`: human-readable health report and Go/No-Go issues;
- `previews/psd-composite.png`: PSD composite used by the audit (when the PSD
  decoder supports the document).

The output directory is replaced atomically. ZIP extraction rejects absolute
paths and `..` traversal. Source files are only read. A candidate is marked
`confirmed` only after transparent trimming, rotation/flip variants, bounded
scaling and alpha-aware resampling pass both a confidence threshold and a
runner-up margin. Similarly sized or named assets are never silently accepted.

In addition to the right-angle rotations and mirrors, the bounded transform set
includes `±15°`, `±30°`, and `±45°` rotations; accepted angles are written to
Unity as `rotationDeg`. Filename/category words shared with a PSD group or layer
path, a `1x1`/`1x2`-style footprint whose orientation agrees with the candidate
rotation, and already confirmed repeated PSD instances may break a close visual
tie. These signals do **not** increase visual confidence: every auto-accepted
candidate must still independently clear the `0.90` visual threshold, the alpha
and color safety gates, and the `0.025` candidate margin after deterministic
tie-breaking. Anonymous repeats therefore benefit only after a visually safe,
unambiguous prototype has been established.

Composite diagnostics report both document-canvas sizes and transparent-trimmed
content sizes. PSD layer inventory also records whether each rendered composite
size agrees with the layer bounds, which makes canvas-versus-content size
mismatches actionable without changing matching behavior.

The PNG inventory also assigns a lightweight `assetRole` for reporting. Current
filename rules classify `NoColition_Tile_*` as `floor`,
`NoColition_EdgeTile_*` as `edge`, `NoColition_dec_*`,
`NoColition_stain_*`, and `NoColition_snow_*` as `decal`, and
`NoColition_1X1_S_*` as an `overlay` candidate; everything else remains `base`.
The audit report breaks Confirmed / Review / Unmatched totals down by these five
roles. Classification is diagnostic metadata only: it does not alter candidate
scores, confirmation decisions, placements, prefab construction, or the Unity
loader. In particular, the audit does not yet attempt base-plus-overlay composite
matching.

Then open the map scene in Unity and use
`Tools → Kenney → Art Reconstruction → PNG to Prefabs and Place`:

1. Select `unity-placements.json` and run **PNG → Prefab**. Every delivered
   split PNG becomes a stable prefab; `NoColition_` files omit colliders.
2. Run **Prefab → 当前场景**. Confirmed PSD matches are instantiated below
   `KenneySampleMap/ArtPlacements` with document position, rotation, scale and
   layer order. They remain ordinary prefab instances and can be edited.
3. Use the existing map export command. Format v3 serializes the edited
   transforms and the runtime loader restores them without deleting gameplay
   Tilemap cells.

The current first-stage safety rule is intentionally strict: low-scoring layers
and candidates without enough separation from the runner-up stay in the audit
report for manual work instead of being guessed.
