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
- `unity-placements.json`: every split PNG plus tiered editable placements for
  PSD layers/groups; Tier A is strict and Tier B–F are tentative previews;
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
roles. Roles do not change visual scores; they constrain decal/floor eligibility
and inform reporting. The audit does not attempt base-plus-overlay composites.

Unresolved `base` assets receive an asset-centric diagnostic containing their
best PSD layer, visual score, alpha IoU/MAE, premultiplied color MAE, transform
and Unity rotation, scale, and margin against the best competing PNG. This is
reported even when the candidate is below the confirmation gates, so Review and
Unmatched failure modes can be inspected without changing those gates.

The matcher treats exact or extremely close PNG pixels as a visual equivalence
class. Equivalent filenames no longer reduce one another's candidate margin;
the selected representative and all equivalent paths are recorded in the
manifest. Every representative must still clear the unchanged visual gates. A
clear visual winner is also selected before semantic tie-breaking, so a naming
hint cannot overturn a candidate that already has the required visual margin.

`decal` assets are constrained by PSD path before running the same
transform-aware, alpha-aware scoring and unchanged confirmation gates. General
`dec_*` assets match only `地表贴花`/decal paths, `stain_*` only stain/污渍/血液
paths, and `snow_*` only snow/雪 paths. Safely confirmed decal candidates flow
through the existing `matches` and Unity placement output. Those paths are not
exclusive ownership boundaries: base and overlay assets remain eligible because
real PSD organization can place ordinary props below decal-named parent groups.
Within one visual equivalence class a scoped decal is the deterministic
representative, while a clearly better base visual match still wins. Edge scope
remains diagnostic-only because it has not produced useful candidates, while floor PNGs
still receive no ordinary object-candidate diagnostics or automatic placement;
no composite-asset behavior is introduced.

Strict auto-acceptance is now in a clear diminishing-returns region: the
remaining cases are dominated by small margins, equivalent exports, composites,
and PSD organization rather than one safely relaxable cutoff. The Unity manifest
therefore also contains one best-effort candidate per otherwise-unrepresented
PSD instance. Tier A remains `autoAccepted`; Tier B requires a very strong visual
candidate and some separation, Tier C is visually strong but ambiguous, and
Tier D/E/F progressively expose weaker candidates. All B–F placements are
`tentative`, retain confidence/margin/rank metadata, and have colliders disabled.

Then open the map scene in Unity and use
`Tools → Kenney → Art Reconstruction → PNG to Prefabs and Place`:

1. Select `unity-placements.json` and run **PNG → Prefab**. Every delivered
   split PNG becomes a stable prefab; `NoColition_` files omit colliders.
2. Run **Prefab → 当前场景**. Placements are instantiated below
   `KenneySampleMap/ArtPlacements/TierA` through `TierF`. Use the tier checkboxes
   and **应用 Tier 可见性** to inspect progressively weaker candidates.
3. Collision-bearing Tier A instances clear only the `Walls` cells covered by
   their position and footprint. Tentative and `NoColition_` instances never
   clear gameplay cells. Removed wall tiles are recorded in
   `KenneyArtReplacementState`, restored before every rerun, and recalculated.
4. **PNG → Prefab** also creates a collider-free Tile from
   `NoColition_Tile_basic`. `ArtFloorReplacement` paints it over every existing
   `Floor` occupancy cell and hides the placeholder Floor renderer without
   deleting its tiles. Ground, remaining Walls, and collision stay intact.
5. Use the existing map export command. Format v3 serializes the edited
   transforms. Tentative records are deliberately excluded from map JSON; only
   `autoAccepted` or inspector-reviewed `manuallyConfirmed` instances reach the
   unchanged runtime loader.

The strict matcher remains intentionally conservative. Lower tiers fill visual
gaps for inspection, but never silently become gameplay-authoritative content.
