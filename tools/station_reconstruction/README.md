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
  layers with an unambiguous exact pixel match;
- `audit-report.md`: human-readable health report and Go/No-Go issues;
- `previews/psd-composite.png`: PSD composite used by the audit (when the PSD
  decoder supports the document).

The output directory is replaced atomically. ZIP extraction rejects absolute
paths and `..` traversal. Source files are only read. A candidate is marked
`exact` only when trimmed RGBA pixels are identical; similarly sized or named
assets are never silently accepted.

Then open the map scene in Unity and use
`Tools → Kenney → Art Reconstruction → PNG to Prefabs and Place`:

1. Select `unity-placements.json` and run **PNG → Prefab**. Every delivered
   split PNG becomes a stable prefab; `NoColition_` files omit colliders.
2. Run **Prefab → 当前场景**. Exact PSD matches are instantiated below
   `KenneySampleMap/ArtPlacements` with document position, rotation, scale and
   layer order. They remain ordinary prefab instances and can be edited.
3. Use the existing map export command. Format v3 serializes the edited
   transforms and the runtime loader restores them without deleting gameplay
   Tilemap cells.

The current first-stage safety rule is intentionally strict: layers that do not
match pixels exactly, and identical PNG files that both claim the same layer,
stay in the audit report for manual work instead of being guessed.
