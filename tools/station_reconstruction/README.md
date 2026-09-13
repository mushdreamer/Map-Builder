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
runner-up margin. Common small free-rotation candidates are checked only as a
deterministic fallback. Filename/category tokens shared with the PSD group path
can break a close visual tie, but cannot make a low visual score pass. Similarly
sized or named assets are never silently accepted. Confirmed named instances
also act as pixel-identical prototypes for repeated anonymous PSD copies, and
footprint aspect consistency is used only as additional tie-breaking evidence.

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
