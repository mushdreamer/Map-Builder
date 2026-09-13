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
- `audit-report.md`: human-readable health report and Go/No-Go issues;
- `previews/psd-composite.png`: PSD composite used by the audit (when the PSD
  decoder supports the document).

The output directory is replaced atomically. ZIP extraction rejects absolute
paths and `..` traversal. Source files are only read. A candidate is marked
`exact` only when trimmed RGBA pixels are identical; similarly sized or named
assets are never silently accepted.
