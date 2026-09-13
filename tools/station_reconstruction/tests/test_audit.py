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
    def test_asset_roles_follow_no_collision_filename_conventions(self):
        expected = {
            "Wall_1x1": "base",
            "NoColition_1X1_S_15": "overlay",
            "NoColition_dec_2": "decal",
            "NoColition_stain_4": "decal",
            "NoColition_snow_1": "decal",
            "NoColition_EdgeTile_pink_3": "edge",
            "NoColition_Tile_6": "floor",
            "NoColition_misc_1": "base",
        }
        for name, role in expected.items():
            with self.subTest(name=name):
                self.assertEqual(role, audit.asset_role(name))

    def test_role_layer_constraints_are_diagnostic_and_specific(self):
        decal = {"assetRole": "decal", "assetKey": "NoColition_dec_2"}
        stain = {"assetRole": "decal", "assetKey": "NoColition_stain_2"}
        edge = {"assetRole": "edge", "assetKey": "NoColition_EdgeTile_pink_3"}
        self.assertTrue(audit.role_layer_allowed(decal, "场景/地表贴花/碎片"))
        self.assertFalse(audit.role_layer_allowed(decal, "场景/墙体"))
        self.assertTrue(audit.role_layer_allowed(stain, "地表贴花/stain 02"))
        self.assertFalse(audit.role_layer_allowed(stain, "地表贴花/snow 02"))
        self.assertTrue(audit.role_layer_allowed(edge, "装饰/边缘地砖"))
        self.assertTrue(audit.matching_layer_allowed(decal, "场景/地表贴花/碎片"))
        self.assertFalse(audit.matching_layer_allowed(decal, "场景/墙体"))
        self.assertTrue(audit.matching_layer_allowed(
            {"assetRole": "base", "assetKey": "1X1_S_10"}, "场景/地表贴花/道具"))
        self.assertFalse(audit.matching_layer_allowed(
            {"assetRole": "floor", "assetKey": "NoColition_Tile_6"}, "map/地面"))

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

    def test_best_effort_candidates_enter_unity_as_tentative_tiers(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = Image.new("RGBA", (10, 10), (100, 50, 20, 255))
            target = Image.new("RGBA", (10, 10), (125, 50, 20, 255))
            source.save(root / "asset.png")
            png = {"path": "asset.png", "assetKey": "asset", "assetRole": "base",
                   "noCollision": False, "footprintHint": {"cols": 1, "rows": 1}}
            layer = {"layerPath": "preview", "bounds": [0, 0, 10, 10],
                     "zIndex": 0, "isGroup": False}
            candidates = audit.best_effort_candidates(
                [png], root, [(layer, target)],
                [{"assetPath": "asset.png", "status": "unmatched", "candidates": []}])
            self.assertEqual(1, len(candidates))
            self.assertEqual("tentative", candidates[0]["reviewState"])
            self.assertIn(candidates[0]["tier"], ("B", "C", "D", "E", "F"))
            manifest = {"tmx": [{"tileWidth": 10}], "psd": [{"width": 20, "height": 20}],
                        "png": [png], "matches": [], "bestEffortCandidates": candidates}
            placement = audit.build_unity_placements(manifest)["placements"][0]
            self.assertEqual("tentative", placement["reviewState"])
            self.assertEqual(candidates[0]["tier"], placement["tier"])
            self.assertEqual(1, placement["candidateRank"])

    def test_confidence_tiers_do_not_reclassify_strict_acceptance(self):
        metrics = {"alphaIoU": 0.95, "colorMae": 0.02}
        self.assertEqual("B", audit.confidence_tier(0.97, metrics, 0.015))
        self.assertEqual("C", audit.confidence_tier(0.93, metrics, 0.0))

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

    def test_identical_assets_form_visual_equivalence_class(self):
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
            self.assertEqual("confirmed", result[0]["status"])
            self.assertEqual(["a.png", "b.png"],
                             result[0]["candidates"][0]["equivalentAssetPaths"])
            self.assertEqual("unmatched", result[1]["status"])

    def test_named_instance_becomes_prototype_for_anonymous_repeat(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            image = Image.new("RGBA", (12, 20), (30, 90, 170, 255))
            alternate = Image.new("RGBA", (12, 20), (45, 90, 170, 255))
            pngs = []
            for name, asset_image in (("bench_red", image), ("bench_blue", alternate)):
                asset_image.save(root / f"{name}.png")
                pngs.append({"path": f"{name}.png", "assetKey": name,
                             "assetRole": "base", "noCollision": False,
                             "footprintHint": None})
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

    def test_clear_visual_winner_is_not_overturned_by_semantics(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            winner = Image.new("RGBA", (10, 10), (100, 50, 20, 255))
            decoy = Image.new("RGBA", (10, 10), (160, 50, 20, 255))
            winner.save(root / "correct_asset.png")
            decoy.save(root / "wrong.png")
            pngs = [
                {"path": "correct_asset.png", "assetKey": "correct_asset",
                 "assetRole": "base", "noCollision": False, "footprintHint": None},
                {"path": "wrong.png", "assetKey": "wrong", "assetRole": "base",
                 "noCollision": False, "footprintHint": None},
            ]
            layer = {"layerPath": "wrong", "bounds": [0, 0, 10, 10],
                     "zIndex": 0, "isGroup": False}
            result = audit.confirmed_matches(pngs, root, [(layer, winner)])
            self.assertEqual("confirmed", result[0]["status"])
            self.assertEqual("unmatched", result[1]["status"])

    def test_decal_scope_is_enforced_by_matcher_and_reaches_placements(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            image = Image.new("RGBA", (8, 8), (50, 100, 150, 255))
            image.save(root / "base.png")
            image.save(root / "NoColition_dec_2.png")
            pngs = [
                {"path": "base.png", "assetKey": "base", "assetRole": "base",
                 "noCollision": False, "footprintHint": None},
                {"path": "NoColition_dec_2.png", "assetKey": "NoColition_dec_2",
                 "assetRole": "decal", "noCollision": True, "footprintHint": None},
            ]
            layer = {"layerPath": "场景/地表贴花/纸片", "bounds": [10, 20, 18, 28],
                     "zIndex": 1, "isGroup": False}
            matches = audit.confirmed_matches(pngs, root, [(layer, image)])
            self.assertEqual("unmatched", matches[0]["status"])
            self.assertEqual("confirmed", matches[1]["status"])
            manifest = {"tmx": [{"tileWidth": 8}], "psd": [{"width": 40, "height": 40}],
                        "png": pngs, "matches": matches}
            placements = audit.build_unity_placements(manifest)["placements"]
            self.assertEqual("NoColition_dec_2", placements[0]["assetKey"])

    def test_decal_group_does_not_hide_a_clear_base_visual_match(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            base_image = Image.new("RGBA", (10, 10), (80, 120, 160, 255))
            decal_image = Image.new("RGBA", (10, 10), (170, 120, 160, 255))
            base_image.save(root / "1X1_S_10.png")
            decal_image.save(root / "NoColition_dec_1.png")
            pngs = [
                {"path": "1X1_S_10.png", "assetKey": "1X1_S_10", "assetRole": "base",
                 "noCollision": False, "footprintHint": {"cols": 1, "rows": 1}},
                {"path": "NoColition_dec_1.png", "assetKey": "NoColition_dec_1",
                 "assetRole": "decal", "noCollision": True, "footprintHint": None},
            ]
            layer = {"layerPath": "场景/地表贴花/道具", "bounds": [0, 0, 10, 10],
                     "zIndex": 0, "isGroup": False}
            matches = audit.confirmed_matches(pngs, root, [(layer, base_image)])
            self.assertEqual("confirmed", matches[0]["status"])
            self.assertEqual("unmatched", matches[1]["status"])

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

    def test_report_summarizes_match_status_by_asset_role(self):
        manifest = {
            "tmx": [], "psd": [],
            "png": [
                {"path": "base.png", "assetRole": "base"},
                {"path": "overlay-a.png", "assetRole": "overlay"},
                {"path": "overlay-b.png", "assetRole": "overlay"},
                {"path": "floor.png", "assetRole": "floor"},
            ],
            "matches": [
                {"assetPath": "base.png", "status": "confirmed", "candidates": []},
                {"assetPath": "overlay-a.png", "status": "review", "candidates": []},
                {"assetPath": "overlay-b.png", "status": "unmatched", "candidates": []},
                {"assetPath": "floor.png", "status": "unmatched", "candidates": []},
            ],
            "roleDiagnostics": {
                "baseAssets": [{
                    "assetPath": "base.png", "status": "review",
                    "bestCandidate": {"layerPath": "objects/base", "score": 0.89,
                                      "alphaIoU": 0.9, "colorMae": 0.1,
                                      "rotationDeg": 15, "scaleX": 1.0, "scaleY": 1.0,
                                      "margin": 0.01},
                }],
                "decalAssets": [], "edgeAssets": [],
                "floor": {"possibleLayers": [{"layerPath": "map/地面"}]},
            },
        }
        report = audit.make_report(manifest, [])
        self.assertIn("| base | 1 | 1 | 0 | 0 |", report)
        self.assertIn("| overlay | 2 | 0 | 1 | 1 |", report)
        self.assertIn("| floor | 1 | 0 | 0 | 1 |", report)
        self.assertIn("`objects/base` | 0.89 | 0.9 | 0.1 | 15°", report)
        self.assertIn("1 possible PSD ground layer(s)", report)

    def test_role_diagnostics_reports_base_metrics_and_scopes_other_roles(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            image = Image.new("RGBA", (8, 12), (30, 60, 90, 255))
            pngs = []
            for filename, role in (("base.png", "base"), ("decal.png", "decal"),
                                   ("floor.png", "floor")):
                image.save(root / filename)
                pngs.append({"path": filename, "assetKey": Path(filename).stem,
                             "assetRole": role, "footprintHint": None})
            layers = [
                ({"layerPath": "objects/chair", "bounds": [0, 0, 8, 12],
                  "isGroup": False, "visible": True}, image),
                ({"layerPath": "地表贴花/mark", "bounds": [10, 0, 18, 12],
                  "isGroup": False, "visible": True}, image),
                ({"layerPath": "map/地面", "bounds": [0, 20, 8, 32],
                  "isGroup": True, "visible": True}, image),
            ]
            matches = [{"assetPath": png["path"], "status": "unmatched"} for png in pngs]
            result = audit.role_diagnostics(pngs, root, layers, matches)
            base = result["baseAssets"][0]["bestCandidate"]
            self.assertEqual("objects/chair", base["layerPath"])
            self.assertEqual(1.0, base["score"])
            self.assertIn("alphaIoU", base)
            self.assertIn("colorMae", base)
            self.assertIn("rotationDeg", base)
            self.assertIn("scaleX", base)
            self.assertIn("margin", base)
            self.assertEqual("地表贴花/mark",
                             result["decalAssets"][0]["bestCandidate"]["layerPath"])
            self.assertEqual("map/地面", result["floor"]["possibleLayers"][0]["layerPath"])


if __name__ == "__main__":
    unittest.main()
