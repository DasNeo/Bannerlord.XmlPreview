from __future__ import annotations

import argparse
import csv
from dataclasses import dataclass
import os
from pathlib import Path
from typing import Iterable
import xml.etree.ElementTree as ET

from PIL import Image


@dataclass(frozen=True)
class SpritePart:
    name: str
    category: str
    sheet_id: int
    sheet_x: int
    sheet_y: int
    width: int
    height: int
    source_xml: Path


@dataclass(frozen=True)
class SpriteEntry:
    name: str
    kind: str
    part_name: str
    category: str
    sheet_id: int
    sheet_x: int
    sheet_y: int
    width: int
    height: int
    source_xml: Path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Extract Bannerlord UI sprites from DDS atlases into individual PNG files."
    )
    parser.add_argument(
        "--bannerlord-root",
        type=Path,
        required=True,
        help="Bannerlord installation root that contains the Modules folder.",
    )
    parser.add_argument(
        "--dds-root",
        type=Path,
        required=True,
        help="Folder that contains extracted DDS files such as ui_textures_1.dds.",
    )
    parser.add_argument(
        "--png-root",
        type=Path,
        required=True,
        help="Output folder for extracted PNG files and the manifest CSV.",
    )
    parser.add_argument(
        "--overwrite",
        action="store_true",
        help="Overwrite existing PNG files instead of skipping them.",
    )
    parser.add_argument(
        "--category",
        action="append",
        default=[],
        help="Only extract sprites from the given category. Can be provided multiple times.",
    )
    parser.add_argument(
        "--sheet-id",
        type=int,
        action="append",
        default=[],
        help="Only extract sprites from the given sheet id. Can be provided multiple times.",
    )
    parser.add_argument(
        "--skip-manifest",
        action="store_true",
        help="Do not rewrite _manifest.csv in the output folder.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    manifest_base = args.png_root.resolve()
    bannerlord_root = args.bannerlord_root.resolve()
    dds_root = args.dds_root.resolve()
    modules_root = args.bannerlord_root / "Modules"
    if not modules_root.is_dir():
        raise SystemExit(f"Modules folder not found: {modules_root}")

    args.png_root.mkdir(parents=True, exist_ok=True)

    sprite_parts: dict[str, SpritePart] = {}
    entries: list[SpriteEntry] = []
    sprite_data_files = sorted(modules_root.rglob("*SpriteData.xml"))

    for sprite_data_path in sprite_data_files:
        collect_sprite_entries(sprite_data_path, sprite_parts, entries)

    selected_categories = {category.casefold() for category in args.category}
    selected_sheet_ids = set(args.sheet_id)
    if selected_categories or selected_sheet_ids:
        entries = [
            entry
            for entry in entries
            if (not selected_categories or entry.category.casefold() in selected_categories)
            and (not selected_sheet_ids or entry.sheet_id in selected_sheet_ids)
        ]

    dds_cache: dict[Path, Image.Image] = {}
    manifest_rows: list[list[str]] = []

    extracted = 0
    skipped_existing = 0
    missing_dds = 0
    invalid_bounds = 0

    for entry in entries:
        dds_path = dds_root / f"{entry.category}_{entry.sheet_id}.dds"
        png_path = args.png_root / (entry.name.replace("\\", "/") + ".png")

        manifest_rows.append(
            [
                entry.name,
                entry.kind,
                entry.category,
                str(entry.sheet_id),
                make_relative_path(dds_path, manifest_base),
                make_relative_path(png_path, manifest_base),
                make_relative_path(entry.source_xml.resolve(), bannerlord_root),
            ]
        )

        if png_path.exists() and not args.overwrite:
            skipped_existing += 1
            continue

        if not dds_path.is_file():
            missing_dds += 1
            continue

        atlas = dds_cache.get(dds_path)
        if atlas is None:
            atlas = Image.open(dds_path).convert("RGBA")
            dds_cache[dds_path] = atlas

        crop_box = (
            entry.sheet_x,
            entry.sheet_y,
            entry.sheet_x + entry.width,
            entry.sheet_y + entry.height,
        )
        if crop_box[2] > atlas.width or crop_box[3] > atlas.height or entry.width <= 0 or entry.height <= 0:
            invalid_bounds += 1
            continue

        png_path.parent.mkdir(parents=True, exist_ok=True)
        atlas.crop(crop_box).save(png_path)
        extracted += 1

    if not args.skip_manifest:
        write_manifest(args.png_root / "_manifest.csv", manifest_rows)

    print(f"Sprite XML files: {len(sprite_data_files)}")
    print(f"Sprite entries: {len(entries)}")
    print(f"Extracted PNGs: {extracted}")
    print(f"Skipped existing PNGs: {skipped_existing}")
    print(f"Missing DDS atlases: {missing_dds}")
    print(f"Invalid sprite bounds: {invalid_bounds}")
    if args.skip_manifest:
        print("Manifest: skipped")
    else:
        print(f"Manifest: {args.png_root / '_manifest.csv'}")
    return 0


def collect_sprite_entries(
    sprite_data_path: Path,
    sprite_parts: dict[str, SpritePart],
    entries: list[SpriteEntry],
) -> None:
    try:
        root = ET.parse(sprite_data_path).getroot()
    except ET.ParseError:
        return

    for sprite_part_element in root.iter("SpritePart"):
        part = parse_sprite_part(sprite_part_element, sprite_data_path)
        if part is None:
            continue

        sprite_parts[part.name] = part
        entries.append(
            SpriteEntry(
                name=part.name,
                kind="SpritePart",
                part_name=part.name,
                category=part.category,
                sheet_id=part.sheet_id,
                sheet_x=part.sheet_x,
                sheet_y=part.sheet_y,
                width=part.width,
                height=part.height,
                source_xml=sprite_data_path,
            )
        )

    sprites_element = root.find("Sprites")
    if sprites_element is None:
        return

    for sprite_element in sprites_element:
        name = get_child_text(sprite_element, "Name")
        part_name = get_child_text(sprite_element, "SpritePartName")
        if not name or not part_name:
            continue

        part = sprite_parts.get(part_name)
        if part is None:
            continue

        entries.append(
            SpriteEntry(
                name=name,
                kind=sprite_element.tag,
                part_name=part_name,
                category=part.category,
                sheet_id=part.sheet_id,
                sheet_x=part.sheet_x,
                sheet_y=part.sheet_y,
                width=part.width,
                height=part.height,
                source_xml=sprite_data_path,
            )
        )


def parse_sprite_part(sprite_part_element: ET.Element, source_xml: Path) -> SpritePart | None:
    name = get_child_text(sprite_part_element, "Name")
    category = get_child_text(sprite_part_element, "CategoryName")
    if not name or not category:
        return None

    return SpritePart(
        name=name,
        category=category,
        sheet_id=parse_int(get_child_text(sprite_part_element, "SheetID")),
        sheet_x=parse_int(get_child_text(sprite_part_element, "SheetX")),
        sheet_y=parse_int(get_child_text(sprite_part_element, "SheetY")),
        width=parse_int(get_child_text(sprite_part_element, "Width")),
        height=parse_int(get_child_text(sprite_part_element, "Height")),
        source_xml=source_xml,
    )


def get_child_text(element: ET.Element, child_name: str) -> str | None:
    child = element.find(child_name)
    if child is None or child.text is None:
        return None

    value = child.text.strip()
    return value or None


def parse_int(value: str | None) -> int:
    if value is None:
        return 0
    try:
        return int(value)
    except ValueError:
        return 0


def make_relative_path(path: Path, base_path: Path) -> str:
    return os.path.relpath(Path(path).resolve(), base_path).replace("\\", "/")


def write_manifest(manifest_path: Path, rows: Iterable[list[str]]) -> None:
    with manifest_path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle)
        writer.writerow(["name", "kind", "category", "sheet_id", "dds_path", "png_path", "source_xml"])
        writer.writerows(rows)


if __name__ == "__main__":
    raise SystemExit(main())
