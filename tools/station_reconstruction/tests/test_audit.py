import importlib.util
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path


MODULE_PATH = Path(__file__).parents[1] / "audit.py"
SPEC = importlib.util.spec_from_file_location("station_audit", MODULE_PATH)
audit = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = audit
SPEC.loader.exec_module(audit)


class AuditTests(unittest.TestCase):
    def test_footprint_and_no_collision_conventions(self):
        self.assertEqual({"cols": 1, "rows": 3}, audit.footprint_hint("NoColition_1x3_7"))
        self.assertIsNone(audit.footprint_hint("background"))

    def test_tmx_csv_inventory_preserves_flip_count(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            path = root / "map.tmx"
            path.write_text(
                '<map orientation="orthogonal" width="2" height="2" tilewidth="64" tileheight="64">'
                '<layer name="Wall" width="2" height="2"><data encoding="csv">1,0,2147483649,0</data></layer>'
                '</map>', encoding="utf-8")
            result = audit.audit_tmx(path, root)
            self.assertEqual(2, result["layers"][0]["occupiedCount"])
            self.assertEqual(1, result["layers"][0]["flipFlagCount"])

    def test_zip_slip_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            archive = root / "bad.zip"
            with zipfile.ZipFile(archive, "w") as zipped:
                zipped.writestr("../escape.txt", "bad")
            destination = root / "out"
            destination.mkdir()
            with self.assertRaises(ValueError):
                audit.safe_extract(archive, destination)

    def test_unity_manifest_keeps_all_assets_and_repeated_instances(self):
        manifest = {
            "tmx": [{"tileWidth": 10}], "psd": [{"width": 100, "height": 80}],
            "png": [{"path": "PNG/a.png", "assetKey": "a", "noCollision": True,
                     "footprintHint": {"cols": 1, "rows": 2}},
                    {"path": "PNG/unmatched.png", "assetKey": "unmatched", "noCollision": False,
                     "footprintHint": None}],
            "matches": [{"assetPath": "PNG/a.png", "candidates": [
                {"layerPath": "one", "layerBounds": [0, 10, 10, 30], "transform": "identity", "zIndex": 2},
                {"layerPath": "two", "layerBounds": [20, 20, 30, 40], "transform": "identity", "zIndex": 3}
            ]}]
        }
        result = audit.build_unity_placements(manifest)
        self.assertEqual(2, len(result["assets"]))
        self.assertEqual(2, len(result["placements"]))
        self.assertEqual({"x": 0.5, "y": 6.0, "z": 0}, result["placements"][0]["position"])


if __name__ == "__main__":
    unittest.main()
