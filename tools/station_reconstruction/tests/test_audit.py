import importlib.util
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path
from PIL import Image


MODULE_PATH = Path(__file__).parents[1] / "audit.py"
SPEC = importlib.util.spec_from_file_location("station_audit", MODULE_PATH)
audit = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = audit
SPEC.loader.exec_module(audit)


class AuditTests(unittest.TestCase):
    def test_semantic_and_rotated_footprint_evidence(self):
        self.assertEqual(1.0, audit.semantic_evidence("ticket_gate", "家具/ticket gate 复制"))
        hint = {"cols": 1, "rows": 2}
        self.assertEqual(1.0, audit.footprint_evidence(hint, "identity", (20, 40)))
        self.assertEqual(1.0, audit.footprint_evidence(hint, "rotate90", (40, 20)))
        self.assertEqual(0.0, audit.footprint_evidence(hint, "rotate90", (20, 40)))

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

    def test_confirmed_match_supports_rotation_scale_and_small_resampling_error(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = Image.new("RGBA", (12, 20), (0, 0, 0, 0))
            for x in range(2, 10):
                for y in range(3, 18):
                    source.putpixel((x, y), (220, 40 + x, 80 + y, 255))
            (root / "asset.png").parent.mkdir(exist_ok=True)
            source.save(root / "asset.png")
            target = audit.normalized_rgba(source).transpose(Image.Transpose.ROTATE_90)
            target = target.resize((30, 16), Image.Resampling.LANCZOS)
            # Simulate a slight Photoshop color/resampling delta.
            red, green, blue, alpha = target.split()
            red = red.point(lambda value: min(255, value + 2))
            target = Image.merge("RGBA", (red, green, blue, alpha))
            png = {"path": "asset.png", "assetKey": "asset", "noCollision": False,
                   "footprintHint": None}
            layer = {"layerPath": "group/instance", "bounds": [10, 20, 40, 36],
                     "zIndex": 4, "isGroup": False}
            result = audit.confirmed_matches([png], root, [(layer, target)])
            self.assertEqual("confirmed", result[0]["status"])
            self.assertEqual("rotate90", result[0]["candidates"][0]["transform"])
            self.assertAlmostEqual(2.0, result[0]["candidates"][0]["scaleX"])

    def test_identical_assets_are_review_not_auto_accepted(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            image = Image.new("RGBA", (8, 8), (20, 80, 160, 255))
            pngs = []
            for name in ("a", "b"):
                image.save(root / f"{name}.png")
                pngs.append({"path": f"{name}.png", "assetKey": name,
                             "noCollision": False, "footprintHint": None})
            layer = {"layerPath": "duplicate", "bounds": [0, 0, 8, 8],
                     "zIndex": 0, "isGroup": False}
            result = audit.confirmed_matches(pngs, root, [(layer, image)])
            self.assertEqual("review", result[0]["status"])
            self.assertEqual(0, len(result[0]["candidates"]))

    def test_named_instance_becomes_prototype_for_anonymous_repeat(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            image = Image.new("RGBA", (12, 20), (30, 90, 170, 255))
            pngs = []
            for name in ("bench_red", "bench_blue"):
                image.save(root / f"{name}.png")
                pngs.append({"path": f"{name}.png", "assetKey": name,
                             "noCollision": False, "footprintHint": None})
            layers = [
                ({"layerPath": "repeat 17", "bounds": [20, 0, 32, 20],
                  "zIndex": 0, "isGroup": False}, image),
                ({"layerPath": "furniture/bench red", "bounds": [0, 0, 12, 20],
                  "zIndex": 1, "isGroup": False}, image),
            ]
            result = audit.confirmed_matches(pngs, root, layers)
            self.assertEqual("confirmed", result[0]["status"])
            self.assertEqual(2, len(result[0]["candidates"]))
            self.assertTrue(any(candidate["prototypeEvidence"] > 0
                                for candidate in result[0]["candidates"]))
            self.assertEqual("unmatched", result[1]["status"])

    def test_free_rotation_is_matched_and_exported_to_unity(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = Image.new("RGBA", (20, 12), (0, 0, 0, 0))
            for x in range(3, 17):
                for y in range(2, 9):
                    source.putpixel((x, y), (20 + x * 5, 80 + y, 160, 255))
            source.save(root / "canopy.png")
            target = dict(audit.transformed_variants(source))["rotate30"]
            png = {"path": "canopy.png", "assetKey": "canopy", "noCollision": False,
                   "footprintHint": None}
            layer = {"layerPath": "canopy", "bounds": [10, 20, 10 + target.width, 20 + target.height],
                     "zIndex": 2, "isGroup": False}
            match = audit.confirmed_matches([png], root, [(layer, target)])[0]
            self.assertEqual("rotate30", match["candidates"][0]["transform"])
            manifest = {"tmx": [{"tileWidth": 10}], "psd": [{"width": 100, "height": 80}],
                        "png": [png], "matches": [match]}
            placement = audit.build_unity_placements(manifest)["placements"][0]
            self.assertEqual(30, placement["rotationDeg"])

    def test_semantics_cannot_promote_low_visual_confidence(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = Image.new("RGBA", (10, 10), (255, 0, 0, 255))
            source.save(root / "named.png")
            target = Image.new("RGBA", (10, 10), (100, 0, 0, 255))
            png = {"path": "named.png", "assetKey": "named", "noCollision": False,
                   "footprintHint": None}
            layer = {"layerPath": "named", "bounds": [0, 0, 10, 10],
                     "zIndex": 0, "isGroup": False}
            result = audit.confirmed_matches([png], root, [(layer, target)])[0]
            self.assertNotEqual("confirmed", result["status"])


if __name__ == "__main__":
    unittest.main()
