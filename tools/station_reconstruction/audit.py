#!/usr/bin/env python3
"""Read-only production sample audit and exact PSD/PNG matching proof of concept."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import sys
import tempfile
import zipfile
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any, Iterable
from xml.etree import ElementTree

try:
    from PIL import Image, ImageChops
except ImportError:  # pragma: no cover - exercised by dependency check in main
    Image = None
    ImageChops = None


TOOL_VERSION = 2
FOOTPRINT_RE = re.compile(r"(?<!\d)(\d+)x(\d+)(?!\d)", re.IGNORECASE)
FINAL_HINTS = ("final", "flatten", "composite", "concept", "preview", "概念", "效果", "车站")
SEMANTIC_STOP_WORDS = {"png", "file", "group", "layer", "copy", "副本", "图层"}


@dataclass(frozen=True)
class Issue:
    severity: str
    code: str
    message: str


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def rel(path: Path, root: Path) -> str:
    return path.relative_to(root).as_posix()


def discover(root: Path, suffix: str) -> list[Path]:
    return sorted(p for p in root.rglob("*") if p.is_file() and p.suffix.lower() == suffix)


def safe_extract(source: Path, destination: Path) -> None:
    root = destination.resolve()
    with zipfile.ZipFile(source) as archive:
        for member in archive.infolist():
            target = (destination / member.filename).resolve()
            if target != root and root not in target.parents:
                raise ValueError(f"unsafe ZIP member: {member.filename}")
        archive.extractall(destination)


def parse_csv_data(data: ElementTree.Element, width: int, height: int) -> list[int]:
    if data.get("encoding", "").lower() != "csv":
        raise ValueError("only CSV-encoded TMX tile layers are supported by this PoC")
    values = [int(value) for value in re.split(r"[,\s]+", data.text or "") if value]
    if len(values) != width * height:
        raise ValueError(f"expected {width * height} cells, found {len(values)}")
    return values


def audit_tmx(path: Path, root: Path) -> dict[str, Any]:
    document = ElementTree.parse(path)
    node = document.getroot()
    width, height = int(node.get("width", 0)), int(node.get("height", 0))
    result: dict[str, Any] = {
        "path": rel(path, root),
        "sha256": sha256(path),
        "orientation": node.get("orientation"),
        "infinite": node.get("infinite") == "1",
        "width": width,
        "height": height,
        "tileWidth": int(node.get("tilewidth", 0)),
        "tileHeight": int(node.get("tileheight", 0)),
        "layers": [],
    }
    for layer in node.findall(".//layer"):
        layer_width = int(layer.get("width", width))
        layer_height = int(layer.get("height", height))
        entry: dict[str, Any] = {
            "name": layer.get("name", ""),
            "width": layer_width,
            "height": layer_height,
            "offsetX": float(layer.get("offsetx", 0)),
            "offsetY": float(layer.get("offsety", 0)),
            "visible": layer.get("visible", "1") != "0",
            "opacity": float(layer.get("opacity", 1)),
        }
        data = layer.find("data")
        if data is not None and not list(data.findall("chunk")):
            try:
                gids = parse_csv_data(data, layer_width, layer_height)
                occupied = [index for index, gid in enumerate(gids) if gid & 0x1FFFFFFF]
                occupied_set = set(occupied)
                entry["occupiedCount"] = len(occupied)
                entry["occupancySha256"] = hashlib.sha256(
                    bytes(1 if index in occupied_set else 0 for index in range(len(gids)))
                ).hexdigest()
                entry["flipFlagCount"] = sum(1 for gid in gids if gid & 0xE0000000)
            except ValueError as error:
                entry["parseError"] = str(error)
        result["layers"].append(entry)
    result["objectGroups"] = [group.get("name", "") for group in node.findall(".//objectgroup")]
    return result


def alpha_bounds(image: Any) -> tuple[int, int, int, int] | None:
    return image.getchannel("A").getbbox()


def normalized_rgba(image: Any) -> Any:
    rgba = image.convert("RGBA")
    bounds = alpha_bounds(rgba)
    return rgba.crop(bounds) if bounds else rgba.crop((0, 0, 0, 0))


def pixel_digest(image: Any) -> str:
    normalized = normalized_rgba(image)
    digest = hashlib.sha256()
    digest.update(f"{normalized.width}x{normalized.height}:RGBA:".encode())
    digest.update(normalized.tobytes())
    return digest.hexdigest()


def footprint_hint(name: str) -> dict[str, int] | None:
    match = FOOTPRINT_RE.search(name)
    return {"cols": int(match.group(1)), "rows": int(match.group(2))} if match else None


def audit_png(path: Path, root: Path) -> dict[str, Any]:
    with Image.open(path) as source:
        image = source.convert("RGBA")
        bounds = alpha_bounds(image)
        return {
            "path": rel(path, root),
            "assetKey": path.stem,
            "sha256": sha256(path),
            "width": image.width,
            "height": image.height,
            "alphaBounds": list(bounds) if bounds else None,
            "opaquePixelCount": sum(1 for value in image.getchannel("A").getdata() if value),
            "pixelDigest": pixel_digest(image),
            "footprintHint": footprint_hint(path.stem),
            "noCollision": path.stem.lower().startswith("nocolition_"),
        }


def layer_record(layer: Any, index: int, parent: str = "") -> Iterable[tuple[dict[str, Any], Any]]:
    layer_path = f"{parent}/{layer.name}" if parent else str(layer.name)
    bounds = [int(layer.left), int(layer.top), int(layer.right), int(layer.bottom)]
    record = {
        "index": index,
        "name": str(layer.name),
        "layerPath": layer_path,
        "kind": getattr(layer, "kind", type(layer).__name__),
        "visible": bool(layer.is_visible()),
        "opacity": int(getattr(layer, "opacity", 255)),
        "blendMode": str(getattr(layer, "blend_mode", "normal")),
        "bounds": bounds,
        "isGroup": bool(layer.is_group()),
    }
    image = None
    try:
        image = layer.composite()
    except Exception as error:  # individual unsupported layers must not abort inventory
        record["compositeError"] = str(error)
    if image is not None:
        rgba = image.convert("RGBA")
        record["compositeSize"] = [rgba.width, rgba.height]
        record["alphaBounds"] = list(alpha_bounds(rgba) or ()) or None
        record["pixelDigest"] = pixel_digest(rgba)
    yield record, image
    if layer.is_group():
        for child_index, child in enumerate(layer):
            yield from layer_record(child, child_index, layer_path)


def audit_psd(path: Path, root: Path, previews: Path) -> tuple[dict[str, Any], list[tuple[dict[str, Any], Any]]]:
    try:
        from psd_tools import PSDImage
    except ImportError as error:
        raise RuntimeError("psd-tools is required to inspect PSD files") from error
    psd = PSDImage.open(path)
    # Explicit viewport is important: layers may have negative/out-of-canvas
    # bounds, but Photoshop's document composite is always the document canvas.
    composite = psd.composite(viewport=(0, 0, psd.width, psd.height))
    preview_path = previews / f"{path.stem}-composite.png"
    if composite is not None:
        composite.save(preview_path)
    layers: list[tuple[dict[str, Any], Any]] = []
    for index, layer in enumerate(psd):
        layers.extend(layer_record(layer, index))
    return ({
        "path": rel(path, root),
        "sha256": sha256(path),
        "width": psd.width,
        "height": psd.height,
        "colorMode": str(psd.color_mode),
        "depth": psd.depth,
        "layerCount": len(layers),
        "compositePath": f"previews/{preview_path.name}" if composite is not None else None,
        "layers": [record for record, _ in layers],
    }, layers)


def transformed_variants(image: Any) -> Iterable[tuple[str, Any]]:
    normalized = normalized_rgba(image)
    variants = [("identity", normalized)]
    transpose = Image.Transpose
    variants.extend([
        ("rotate90", normalized.transpose(transpose.ROTATE_90)),
        ("rotate180", normalized.transpose(transpose.ROTATE_180)),
        ("rotate270", normalized.transpose(transpose.ROTATE_270)),
        ("flipX", normalized.transpose(transpose.FLIP_LEFT_RIGHT)),
        ("flipY", normalized.transpose(transpose.FLIP_TOP_BOTTOM)),
        ("rotate90FlipX", normalized.transpose(transpose.ROTATE_90).transpose(transpose.FLIP_LEFT_RIGHT)),
        ("rotate90FlipY", normalized.transpose(transpose.ROTATE_90).transpose(transpose.FLIP_TOP_BOTTOM)),
    ])
    yield from variants


def additional_rotation_variants(image: Any) -> Iterable[tuple[str, Any]]:
    """Small deterministic fallback set for Photoshop free-rotation transforms."""
    normalized = normalized_rgba(image)
    for angle in (-45, -30, -15, 15, 30, 45):
        yield f"rotate{angle}", normalized.rotate(
            angle, resample=Image.Resampling.BICUBIC, expand=True)


def semantic_tokens(value: str) -> set[str]:
    tokens = {token for token in re.findall(r"[a-z]+|\d+x\d+|[\u4e00-\u9fff]+", value.lower())
              if token not in SEMANTIC_STOP_WORDS and len(token) > 1}
    aliases = {"dec": "decor", "decoration": "decor", "track": "rail"}
    return {aliases.get(token, token) for token in tokens}


def semantic_evidence(asset_key: str, layer_path: str) -> tuple[float, list[str]]:
    shared = sorted(semantic_tokens(asset_key) & semantic_tokens(layer_path))
    return min(0.05, 0.03 * len(shared)), shared


def footprint_evidence(png: dict[str, Any], transform: str, target: Any) -> tuple[float, float | None]:
    hint = png.get("footprintHint")
    if not hint or target.width < 1 or target.height < 1:
        return 0.0, None
    cols, rows = hint["cols"], hint["rows"]
    if transform.startswith("rotate90") or transform in ("rotate270",):
        cols, rows = rows, cols
    pixels_x, pixels_y = target.width / cols, target.height / rows
    uniformity_error = abs(pixels_x / pixels_y - 1.0)
    # Footprint is tie-breaking evidence only; transparent art need not fill it.
    return (0.015 if uniformity_error <= 0.12 else 0.0), uniformity_error


def channel_mean(image: Any) -> float:
    histogram = image.histogram()
    pixels = image.width * image.height
    return sum(value * count for value, count in enumerate(histogram)) / (255 * pixels) if pixels else 0.0


def similarity(source: Any, target: Any) -> dict[str, float] | None:
    """Alpha-aware score after fitting source to target's trimmed bounds."""
    if source.width < 1 or source.height < 1 or target.width < 1 or target.height < 1:
        return None
    source_aspect = source.width / source.height
    target_aspect = target.width / target.height
    aspect_error = abs(source_aspect / target_aspect - 1.0)
    if aspect_error > 0.06:
        return None
    scale_x, scale_y = target.width / source.width, target.height / source.height
    if not (0.15 <= scale_x <= 6.0 and 0.15 <= scale_y <= 6.0):
        return None
    # Bound comparison cost for large PSD layers while retaining aspect/alpha.
    comparison_size = target.size
    if max(comparison_size) > 256:
        ratio = 256 / max(comparison_size)
        comparison_size = (max(1, round(target.width * ratio)), max(1, round(target.height * ratio)))
        target = target.resize(comparison_size, Image.Resampling.LANCZOS)
    fitted = source if source.size == comparison_size else source.resize(comparison_size, Image.Resampling.LANCZOS)
    source_alpha, target_alpha = fitted.getchannel("A"), target.getchannel("A")
    alpha_mae = channel_mean(ImageChops.difference(source_alpha, target_alpha))
    source_mask = source_alpha.point(lambda value: 255 if value >= 16 else 0)
    target_mask = target_alpha.point(lambda value: 255 if value >= 16 else 0)
    intersection = channel_mean(ImageChops.multiply(source_mask, target_mask))
    union = channel_mean(ImageChops.lighter(source_mask, target_mask))
    alpha_iou = intersection / union if union else 0.0

    # Premultiplication ignores arbitrary RGB values in fully transparent pixels.
    rgb_errors = []
    for channel in range(3):
        left = ImageChops.multiply(fitted.getchannel(channel), source_alpha)
        right = ImageChops.multiply(target.getchannel(channel), target_alpha)
        rgb_errors.append(channel_mean(ImageChops.difference(left, right)))
    color_mae = sum(rgb_errors) / 3
    score = max(0.0, 1.0 - (0.45 * alpha_mae + 0.35 * color_mae
                            + 0.20 * (1.0 - alpha_iou) + aspect_error))
    return {"confidence": score, "alphaIoU": alpha_iou, "alphaMae": alpha_mae,
            "colorMae": color_mae, "scaleX": scale_x, "scaleY": scale_y}


def transformed_asset_variants(image: Any) -> Iterable[tuple[str, Any]]:
    # Transform the trimmed asset, not the PSD layer.  The transform therefore
    # directly describes the Unity instance rather than requiring inversion.
    yield from transformed_variants(image)


def confirmed_matches(pngs: list[dict[str, Any]], root: Path,
                      layers: list[tuple[dict[str, Any], Any]],
                      threshold: float = 0.90, margin: float = 0.025) -> list[dict[str, Any]]:
    """Assign each PSD leaf to a PNG only when score and runner-up margin are safe."""
    asset_variants: dict[str, list[tuple[str, Any]]] = {}
    for png in pngs:
        with Image.open(root / png["path"]) as source:
            rgba = source.convert("RGBA")
            asset_variants[png["path"]] = (list(transformed_asset_variants(rgba))
                                                   + list(additional_rotation_variants(rgba)))
    by_asset: dict[str, list[dict[str, Any]]] = {png["path"]: [] for png in pngs}
    review_by_asset: dict[str, list[dict[str, Any]]] = {png["path"]: [] for png in pngs}

    def layer_semantic_strength(item: tuple[dict[str, Any], Any]) -> float:
        layer = item[0]
        return max((semantic_evidence(png["assetKey"], layer["layerPath"])[0] for png in pngs), default=0.0)

    # Named/grouped instances establish prototypes before anonymous copies.
    ordered_layers = sorted(layers, key=layer_semantic_strength, reverse=True)
    prototype_owners: dict[str, str] = {}
    for layer, image in ordered_layers:
        # A delivered PNG may have been exported from either a raster leaf or a
        # small PSD group (for example sprite + baked shadow), so both are valid.
        if image is None:
            continue
        target = normalized_rgba(image)
        target_digest = pixel_digest(target)
        ranked = []
        for png in pngs:
            for transform, variant in asset_variants[png["path"]]:
                metrics = similarity(variant, target)
                if metrics is not None:
                    semantic_bonus, shared_tokens = semantic_evidence(png["assetKey"], layer["layerPath"])
                    footprint_bonus, footprint_error = footprint_evidence(png, transform, target)
                    repeat_bonus = 0.03 if prototype_owners.get(target_digest) == png["path"] else 0.0
                    evidence_score = metrics["confidence"] + semantic_bonus + footprint_bonus + repeat_bonus
                    ranked.append((evidence_score, png["path"], transform, metrics,
                                   semantic_bonus, shared_tokens, footprint_bonus, footprint_error,
                                   repeat_bonus))
        ranked.sort(key=lambda item: item[0], reverse=True)
        if not ranked:
            continue
        best = ranked[0]
        runner_up = next((item for item in ranked[1:] if item[1] != best[1]), None)
        score_margin = best[0] - (runner_up[0] if runner_up else 0.0)
        candidate = {
            "layerPath": layer["layerPath"], "layerBounds": layer["bounds"],
            "zIndex": layer["zIndex"], "transform": best[2],
            "confidence": round(best[3]["confidence"], 6),
            "evidenceScore": round(best[0], 6), "scoreMargin": round(score_margin, 6),
            "semanticBonus": round(best[4], 6), "sharedTokens": best[5],
            "footprintBonus": round(best[6], 6),
            "footprintUniformityError": (round(best[7], 6) if best[7] is not None else None),
            "repeatPrototypeBonus": round(best[8], 6),
            "method": "alpha-aware-resampled", **{key: round(value, 6) for key, value in best[3].items()
                                                   if key != "confidence"},
        }
        if asset_variants[best[1]] and pixel_digest(asset_variants[best[1]][
                next(i for i, item in enumerate(asset_variants[best[1]]) if item[0] == best[2])][1]) == pixel_digest(target):
            candidate["method"] = "trimmed-rgba-sha256"
            candidate["confidence"] = 1.0
        geometrically_safe = best[3]["alphaIoU"] >= 0.85 and best[3]["alphaMae"] <= 0.15
        visually_safe = best[3]["colorMae"] <= 0.15
        if best[3]["confidence"] >= threshold and score_margin >= margin and geometrically_safe and visually_safe:
            by_asset[best[1]].append(candidate)
            prototype_owners[target_digest] = best[1]
        elif best[3]["confidence"] >= threshold - 0.08:
            candidate["reviewReason"] = (
                "low-confidence" if best[3]["confidence"] < threshold or not geometrically_safe or not visually_safe
                else "ambiguous")
            review_by_asset[best[1]].append(candidate)

    matches = []
    for png in pngs:
        candidates = by_asset[png["path"]]
        reviews = review_by_asset[png["path"]]
        status = "confirmed" if candidates else ("review" if reviews else "unmatched")
        matches.append({"assetPath": png["path"], "status": status,
                        "candidates": candidates, "reviewCandidates": reviews})
    return matches


def choose_final(png_paths: list[Path], psd_paths: list[Path]) -> Path | None:
    excluded = {path.stem.lower() for path in psd_paths}
    ranked = []
    for path in png_paths:
        lower = path.stem.lower()
        score = sum(1 for hint in FINAL_HINTS if hint in lower)
        # Production deliveries commonly separate the flattened image from split
        # assets by directory even when filenames are not English.
        if "concept" in path.parent.name.lower():
            score += 10
        if lower in excluded:
            score -= 1
        if score > 0:
            ranked.append((score, path.stat().st_size, path))
    return max(ranked, default=(0, 0, None))[2]


def build_unity_placements(manifest: dict[str, Any]) -> dict[str, Any]:
    """Convert unambiguous exact layer matches to a Unity-consumable manifest.

    Positions remain deterministic document-pixel calculations.  Unity imports
    sprites at tileWidth PPU, so one TMX tile is one world unit.
    """
    psd = manifest["psd"][0] if manifest.get("psd") else None
    tmx = manifest["tmx"][0] if manifest.get("tmx") else None
    ppu = (tmx or {}).get("tileWidth", 0) or 1
    document_height = (psd or {}).get("height", 0)
    png_by_path = {item["path"]: item for item in manifest.get("png", [])}
    placements = []
    transform_rotation = {"identity": 0, "rotate90": 90, "rotate180": 180,
                          "rotate270": -90, "flipX": 0, "flipY": 0,
                          "rotate90FlipX": 90, "rotate90FlipY": 90}
    matches = manifest.get("matches", [])
    for match in matches:
      for candidate in match.get("candidates", []):
        left, top, right, bottom = candidate["layerBounds"]
        asset = png_by_path[match["assetPath"]]
        transform = candidate["transform"]
        rotation = (float(transform[len("rotate"):]) if transform.startswith("rotate")
                    and transform[len("rotate"):].lstrip("-").isdigit()
                    else transform_rotation[transform])
        placements.append({
            "instanceId": hashlib.sha256(
                f'{match["assetPath"]}|{candidate["layerPath"]}|{candidate["zIndex"]}'.encode()).hexdigest()[:24],
            "assetKey": asset["assetKey"],
            "assetPath": match["assetPath"],
            "layerPath": candidate["layerPath"],
            "position": {"x": (left + right) / (2 * ppu),
                         "y": (document_height - (top + bottom) / 2) / ppu,
                         "z": 0},
            "rotationDeg": rotation,
            "scale": {"x": candidate.get("scaleX", 1) *
                             (-1 if transform in ("flipX", "rotate90FlipX") else 1),
                      "y": candidate.get("scaleY", 1) *
                             (-1 if transform in ("flipY", "rotate90FlipY") else 1)},
            "sortingOrder": 30000 - candidate["zIndex"],
            "confidence": 1.0,
            "reviewState": "autoAccepted",
            "noCollision": asset["noCollision"],
            "footprint": asset["footprintHint"],
        })
    assets = [{"assetKey": item["assetKey"], "assetPath": item["path"],
               "noCollision": item["noCollision"], "footprint": item["footprintHint"]}
              for item in manifest.get("png", [])]
    return {"formatVersion": 1, "pixelsPerUnit": ppu,
            "documentWidth": (psd or {}).get("width", 0),
            "documentHeight": document_height, "assets": assets, "placements": placements}


def compare_images(left: Any, right: Any) -> dict[str, Any]:
    left_rgba, right_rgba = left.convert("RGBA"), right.convert("RGBA")
    result: dict[str, Any] = {
        "leftSize": [left_rgba.width, left_rgba.height],
        "rightSize": [right_rgba.width, right_rgba.height],
        "sameSize": left_rgba.size == right_rgba.size,
    }
    if left_rgba.size != right_rgba.size:
        return result
    difference = ImageChops.difference(left_rgba, right_rgba)
    histogram = difference.histogram()
    channel_pixels = left_rgba.width * left_rgba.height * 4
    absolute_sum = sum((index % 256) * count for index, count in enumerate(histogram))
    result["meanAbsoluteError"] = absolute_sum / (channel_pixels * 255) if channel_pixels else 0
    result["identical"] = difference.getbbox() is None
    return result


def make_report(manifest: dict[str, Any], issues: list[Issue]) -> str:
    matches = manifest.get("matches", [])
    confirmed_assets = sum(1 for match in matches if match["status"] == "confirmed")
    review_assets = sum(1 for match in matches if match["status"] == "review")
    instances = sum(len(match.get("candidates", [])) for match in matches)
    lines = [
        "# Station reconstruction data audit", "",
        f"- TMX files: **{len(manifest['tmx'])}**",
        f"- PSD files: **{len(manifest['psd'])}**",
        f"- PNG files: **{len(manifest['png'])}**",
        f"- Confirmed PNG → PSD matches: **{confirmed_assets}/{len(matches)}**",
        f"- Confirmed PSD instances / Unity placements: **{instances}**",
        f"- PNG requiring review: **{review_assets}**",
        f"- Unmatched PNG: **{len(matches) - confirmed_assets - review_assets}**", "",
        "## Issues", "",
    ]
    lines.extend(f"- **{issue.severity} / {issue.code}** — {issue.message}" for issue in issues)
    if not issues:
        lines.append("- None")
    lines.extend(["", "## Confirmed candidates", ""])
    for match in matches:
        if match["status"] != "confirmed":
            continue
        candidate_names = ", ".join(candidate["layerPath"] for candidate in match["candidates"])
        lines.append(f"- `{match['assetPath']}` → {candidate_names}")
    lines.extend(["", "## Review candidates", ""])
    for match in matches:
        for candidate in match.get("reviewCandidates", []):
            lines.append(f"- `{match['assetPath']}` → {candidate['layerPath']} "
                         f"(score={candidate['confidence']}, margin={candidate['scoreMargin']}, "
                         f"reason={candidate['reviewReason']})")
    lines.append("")
    return "\n".join(lines)


def run(source: Path, output: Path) -> dict[str, Any]:
    if Image is None:
        raise RuntimeError("Pillow is required; install requirements.txt")
    with tempfile.TemporaryDirectory(prefix="station-reconstruction-") as temporary:
        if source.is_file() and source.suffix.lower() == ".zip":
            sample_root = Path(temporary) / "sample"
            sample_root.mkdir()
            safe_extract(source, sample_root)
        elif source.is_dir():
            sample_root = source.resolve()
        else:
            raise ValueError("source must be a ZIP file or extracted directory")

        staging = Path(temporary) / "report"
        previews = staging / "previews"
        previews.mkdir(parents=True)
        issues: list[Issue] = []
        tmx_paths, psd_paths, png_paths = (discover(sample_root, suffix) for suffix in (".tmx", ".psd", ".png"))
        if not tmx_paths:
            issues.append(Issue("error", "missing-tmx", "No TMX file was found."))
        if not psd_paths:
            issues.append(Issue("error", "missing-psd", "No PSD file was found."))
        if not png_paths:
            issues.append(Issue("error", "missing-png", "No PNG file was found."))

        tmx = []
        for path in tmx_paths:
            try:
                tmx.append(audit_tmx(path, sample_root))
            except Exception as error:
                issues.append(Issue("error", "tmx-parse", f"{rel(path, sample_root)}: {error}"))
        final = choose_final(png_paths, psd_paths)
        asset_png_paths = [path for path in png_paths if path != final]
        pngs = [audit_png(path, sample_root) for path in asset_png_paths]
        duplicate_keys = sorted(key for key in {p["assetKey"] for p in pngs} if sum(x["assetKey"] == key for x in pngs) > 1)
        if duplicate_keys:
            issues.append(Issue("error", "duplicate-asset-key", ", ".join(duplicate_keys)))

        psds, all_layers = [], []
        for path in psd_paths:
            try:
                psd, layers = audit_psd(path, sample_root, previews)
                psds.append(psd)
                all_layers.extend(layers)
            except Exception as error:
                issues.append(Issue("error", "psd-parse", f"{rel(path, sample_root)}: {error}"))
        for z_index, (record, _) in enumerate(all_layers):
            record["zIndex"] = z_index
        matches = confirmed_matches(pngs, sample_root, all_layers)
        composite_comparison = None
        if final is not None and psds and psds[0].get("compositePath"):
            with Image.open(final) as final_image, Image.open(staging / psds[0]["compositePath"]) as psd_image:
                composite_comparison = compare_images(final_image, psd_image)
            if not composite_comparison["sameSize"]:
                issues.append(Issue(
                    "error", "composite-size",
                    "Flattened concept export size "
                    f"{composite_comparison['leftSize']} differs from PSD document canvas "
                    f"{composite_comparison['rightSize']}; PSD rendering is constrained to the document viewport."))
            elif composite_comparison["meanAbsoluteError"] > 0.02:
                issues.append(Issue("warning", "composite-difference", "PSD and flattened composites differ by more than 2% normalized MAE."))
        manifest = {
            "toolVersion": TOOL_VERSION,
            "source": source.name,
            "tmx": tmx,
            "psd": psds,
            "png": pngs,
            "flattenedCompositeCandidate": rel(final, sample_root) if final else None,
            "compositeComparison": composite_comparison,
            "readmeFiles": [rel(path, sample_root) for path in sorted(sample_root.rglob("*"))
                            if path.is_file() and path.name.lower().startswith("readme")],
            "matches": matches,
            "issues": [asdict(issue) for issue in issues],
        }
        unity_manifest = build_unity_placements(manifest)
        (staging / "reconstruction.manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        (staging / "unity-placements.json").write_text(json.dumps(unity_manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        (staging / "audit-report.md").write_text(make_report(manifest, issues), encoding="utf-8")
        output.parent.mkdir(parents=True, exist_ok=True)
        replacement = output.with_name(output.name + ".new")
        if replacement.exists():
            shutil.rmtree(replacement)
        shutil.copytree(staging, replacement)
        if output.exists():
            shutil.rmtree(output)
        replacement.rename(output)
        return manifest


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path, help="production sample ZIP or extracted directory")
    parser.add_argument("--output", type=Path, default=Path("ReconstructionReports/station"))
    args = parser.parse_args(argv)
    try:
        manifest = run(args.source.resolve(), args.output.resolve())
    except Exception as error:
        print(f"audit failed: {error}", file=sys.stderr)
        return 2
    errors = sum(1 for issue in manifest["issues"] if issue["severity"] == "error")
    print(f"wrote {args.output} ({len(manifest['png'])} PNG, {errors} error(s))")
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
