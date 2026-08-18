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
    png_by_relative_name, png_by_base_name = index_png_files(res_root)

    rows = list(csv.DictReader(manifest_path.open("r", encoding="utf-8", newline="")))
    normalized_rows: list[dict[str, str]] = []

    for row in rows:
        name = row["name"].replace("\\", "/")
        local_png = png_by_relative_name.get(name, res_root / f"{name}.png")
        if not local_png.exists():
            base_name = Path(name).name
            matches = png_by_base_name.get(base_name, [])
            if len(matches) == 1:
                local_png = matches[0]

        dds_path = Path(row["dds_path"])
        if not dds_path.is_absolute():
            dds_path = (manifest_path.parent / dds_path).resolve()

        source_xml = Path(row["source_xml"])
        if not source_xml.is_absolute():
            source_xml = (manifest_path.parent / source_xml).resolve()

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
    return 0


def index_png_files(res_root: Path) -> tuple[dict[str, Path], dict[str, list[Path]]]:
    png_by_relative_name: dict[str, Path] = {}
    png_by_base_name: dict[str, list[Path]] = {}

    for png_path in res_root.rglob("*.png"):
        relative_name = png_path.relative_to(res_root).with_suffix("").as_posix()
        png_by_relative_name[relative_name] = png_path
        png_by_base_name.setdefault(png_path.stem, []).append(png_path)

    return png_by_relative_name, png_by_base_name


def to_relative(path: Path, base_path: Path) -> str:
    return os.path.relpath(path.resolve(), base_path).replace("\\", "/")


if __name__ == "__main__":
    raise SystemExit(main())
