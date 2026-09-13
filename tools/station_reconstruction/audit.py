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


TOOL_VERSION = 1
FOOTPRINT_RE = re.compile(r"(?<!\d)(\d+)x(\d+)(?!\d)", re.IGNORECASE)
FINAL_HINTS = ("final", "flatten", "composite", "concept", "preview")


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
    composite = psd.composite()
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
    ])
    yield from variants


def exact_matches(pngs: list[dict[str, Any]], root: Path, layers: list[tuple[dict[str, Any], Any]]) -> list[dict[str, Any]]:
    by_digest: dict[str, list[tuple[dict[str, Any], str]]] = {}
    for record, image in layers:
        if image is None or record["isGroup"]:
            continue
        for transform, variant in transformed_variants(image):
            by_digest.setdefault(pixel_digest(variant), []).append((record, transform))
    matches = []
    for png in pngs:
        candidates = []
        with Image.open(root / png["path"]) as image:
            for transform, variant in transformed_variants(image):
                # Comparing transformed PNG to transformed layer duplicates solutions;
                # layer variants are sufficient, so only identity PNG is queried.
                if transform != "identity":
                    continue
                for layer, layer_transform in by_digest.get(pixel_digest(variant), []):
                    candidates.append({
                        "layerPath": layer["layerPath"],
                        "layerBounds": layer["bounds"],
                        "transform": layer_transform,
                        "confidence": 1.0,
                        "method": "trimmed-rgba-sha256",
                    })
        matches.append({"assetPath": png["path"], "status": "exact" if candidates else "unmatched", "candidates": candidates})
    return matches


def choose_final(png_paths: list[Path], psd_paths: list[Path]) -> Path | None:
    excluded = {path.stem.lower() for path in psd_paths}
    ranked = []
    for path in png_paths:
        lower = path.stem.lower()
        score = sum(1 for hint in FINAL_HINTS if hint in lower)
        if lower in excluded:
            score -= 1
        if score > 0:
            ranked.append((score, path.stat().st_size, path))
    return max(ranked, default=(0, 0, None))[2]


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
    exact = sum(1 for match in matches if match["status"] == "exact")
    lines = [
        "# Station reconstruction data audit", "",
        f"- TMX files: **{len(manifest['tmx'])}**",
        f"- PSD files: **{len(manifest['psd'])}**",
        f"- PNG files: **{len(manifest['png'])}**",
        f"- Exact PNG → PSD leaf matches: **{exact}/{len(matches)}**", "",
        "## Issues", "",
    ]
    lines.extend(f"- **{issue.severity} / {issue.code}** — {issue.message}" for issue in issues)
    if not issues:
        lines.append("- None")
    lines.extend(["", "## Exact candidates", ""])
    for match in matches:
        if match["status"] != "exact":
            continue
        candidate_names = ", ".join(candidate["layerPath"] for candidate in match["candidates"])
        lines.append(f"- `{match['assetPath']}` → {candidate_names}")
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
        matches = exact_matches(pngs, sample_root, all_layers) if all_layers else []
        composite_comparison = None
        if final is not None and psds and psds[0].get("compositePath"):
            with Image.open(final) as final_image, Image.open(staging / psds[0]["compositePath"]) as psd_image:
                composite_comparison = compare_images(final_image, psd_image)
            if not composite_comparison["sameSize"]:
                issues.append(Issue("error", "composite-size", "PSD composite and flattened composite have different dimensions."))
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
        (staging / "reconstruction.manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
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
