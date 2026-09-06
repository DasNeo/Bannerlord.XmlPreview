from __future__ import annotations

import argparse
import csv
import os
from pathlib import Path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Normalize Bannerlord sprite manifest paths to the local res folder and make them relative."
    )
    parser.add_argument("--manifest", type=Path, required=True, help="Path to the manifest CSV to normalize.")
    parser.add_argument("--res-root", type=Path, required=True, help="Path to the local res folder.")
    parser.add_argument(
        "--bannerlord-root",
        type=Path,
        required=True,
        help="Bannerlord installation root used for relative SpriteData paths.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    manifest_path = args.manifest.resolve()
    res_root = args.res_root.resolve()
    bannerlord_root = args.bannerlord_root.resolve()
    png_by_relative_name = index_png_files(res_root)

    rows = list(csv.DictReader(manifest_path.open("r", encoding="utf-8", newline="")))
    normalized_rows: list[dict[str, str]] = []
    corrected_png_paths = 0
    missing_png_paths = 0

    for row in rows:
        expected_png = expected_png_path(res_root, row["name"])
        normalized_name = expected_png.relative_to(res_root).with_suffix("").as_posix()
        local_png = png_by_relative_name.get(normalized_name, expected_png)
        original_png = resolve_optional_path(row.get("png_path", ""), manifest_path.parent)
        if original_png is not None and original_png.exists() and local_png != original_png:
            corrected_png_paths += 1
        if not local_png.exists():
            missing_png_paths += 1

        dds_path = resolve_path(row["dds_path"], manifest_path.parent)
        source_xml = resolve_source_xml_path(row["source_xml"], bannerlord_root, manifest_path.parent)

        row["dds_path"] = to_relative(dds_path, manifest_path.parent)
        row["png_path"] = to_relative(local_png, manifest_path.parent)
        row["source_xml"] = to_relative(source_xml, bannerlord_root)
        normalized_rows.append(row)

    with manifest_path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(
            handle,
            fieldnames=["name", "kind", "category", "sheet_id", "dds_path", "png_path", "source_xml"],
        )
        writer.writeheader()
        writer.writerows(normalized_rows)

    print(f"Normalized {len(normalized_rows)} rows in {manifest_path}")
    print(f"Corrected PNG path mismatches: {corrected_png_paths}")
    print(f"Rows whose expected PNG is still missing: {missing_png_paths}")
    return 0


def index_png_files(res_root: Path) -> dict[str, Path]:
    png_by_relative_name: dict[str, Path] = {}

    for png_path in res_root.rglob("*.png"):
        relative_name = png_path.relative_to(res_root).with_suffix("").as_posix()
        png_by_relative_name[relative_name] = png_path

    return png_by_relative_name


def expected_png_path(res_root: Path, sprite_name: str) -> Path:
    normalized_name = sprite_name.replace("\\", "/").strip("/")
    segments = [segment for segment in normalized_name.split("/") if segment]
    if not segments:
        return res_root / "_.png"

    directory_segments = segments[:-1]
    file_name = f"{segments[-1]}.png"
    return res_root.joinpath(*directory_segments, file_name)


def resolve_optional_path(raw_path: str, base_path: Path) -> Path | None:
    if not raw_path:
        return None

    return resolve_path(raw_path, base_path)


def resolve_path(raw_path: str, base_path: Path) -> Path:
    candidate = Path(raw_path)
    if candidate.is_absolute():
        return candidate.resolve()

    return (base_path / candidate).resolve()


def resolve_source_xml_path(raw_path: str, bannerlord_root: Path, manifest_root: Path) -> Path:
    candidate = Path(raw_path)
    if candidate.is_absolute():
        return candidate.resolve()

    bannerlord_candidate = (bannerlord_root / candidate).resolve()
    if bannerlord_candidate.exists():
        return bannerlord_candidate

    manifest_candidate = (manifest_root / candidate).resolve()
    if manifest_candidate.exists():
        return manifest_candidate

    normalized = raw_path.replace("\\", "/")
    modules_marker = "Modules/"
    marker_index = normalized.lower().find(modules_marker.lower())
    if marker_index >= 0:
        modules_relative = normalized[marker_index:]
        return (bannerlord_root / Path(*modules_relative.split("/"))).resolve()

    return bannerlord_candidate


def to_relative(path: Path, base_path: Path) -> str:
    return os.path.relpath(path.resolve(), base_path).replace("\\", "/")


if __name__ == "__main__":
    raise SystemExit(main())
