#!/usr/bin/env python3
"""Build a TokenNotch-ready pixel action pack from PMD animation exports."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import xml.etree.ElementTree as ET

from PIL import Image, ImageDraw


TICK_MS = 1000 / 30

CHARACTERS = {
    "0001": ("bulbasaur", "이상해씨"),
    "0002": ("ivysaur", "이상해풀"),
    "0003": ("venusaur", "이상해꽃"),
    "0004": ("charmander", "파이리"),
    "0005": ("charmeleon", "리자드"),
    "0006": ("charizard", "리자몽"),
    "0007": ("squirtle", "꼬부기"),
    "0008": ("wartortle", "어니부기"),
    "0009": ("blastoise", "거북왕"),
    "0025": ("pikachu", "피카츄"),
    "0132": ("ditto", "메타몽"),
    "0143": ("snorlax", "잠만보"),
}

# Rows match the existing TokenNotch action contract. PMD direction rows are
# S, SE, E, NE, N, NW, W, SW.
ACTIONS = [
    {"row": 0, "id": "idle", "sources": ["Idle"], "direction": 0, "frames": 6},
    {"row": 1, "id": "walk-right", "sources": ["Walk"], "direction": 2, "frames": 8},
    {"row": 2, "id": "walk-left", "sources": ["Walk"], "direction": 6, "frames": 8},
    {"row": 3, "id": "wave", "sources": ["Pose", "Swing", "Attack"], "direction": 0, "frames": 4},
    {"row": 4, "id": "jump", "sources": ["Hop"], "direction": 0, "frames": 5},
    {"row": 5, "id": "failed", "sources": ["Faint", "Sleep", "Hurt"], "direction": 0, "frames": 8},
    {"row": 6, "id": "waiting", "sources": ["Sit", "Idle"], "direction": 0, "frames": 6},
    {"row": 7, "id": "working", "sources": ["Attack"], "direction": 0, "frames": 6},
    {"row": 8, "id": "review", "sources": ["Nod", "LookUp", "Rotate", "Idle"], "direction": 0, "frames": 6},
    {"row": 9, "id": "look-right", "sources": ["Idle"], "direction": 2, "frames": 8},
    {"row": 10, "id": "look-left", "sources": ["Idle"], "direction": 6, "frames": 8},
]


def parse_animations(xml_path: Path) -> dict[str, ET.Element]:
    root = ET.parse(xml_path).getroot()
    return {
        anim.findtext("Name"): anim
        for anim in root.iter("Anim")
        if anim.findtext("Name")
    }


def resolve_animation(
    requested: list[str], animations: dict[str, ET.Element], animation_dir: Path
) -> tuple[str, int, int, list[int], Path]:
    for candidate in requested:
        name = candidate
        seen: set[str] = set()
        while name and name not in seen:
            seen.add(name)
            anim = animations.get(name)
            if anim is None:
                break
            copied = anim.findtext("CopyOf")
            if copied:
                name = copied
                continue
            width = anim.findtext("FrameWidth")
            height = anim.findtext("FrameHeight")
            sheet = animation_dir / f"{name}-Anim.png"
            if width and height and sheet.exists():
                durations = [
                    int(value.text)
                    for value in anim.findall("Durations/Duration")
                    if value.text
                ]
                return candidate, int(width), int(height), durations, sheet
            break
    raise ValueError(f"No usable animation found for: {', '.join(requested)}")


def resample_indices(source_count: int, target_count: int) -> list[int]:
    if source_count <= 0:
        raise ValueError("Animation contains no frames")
    return [min(source_count - 1, i * source_count // target_count) for i in range(target_count)]


def union_bbox(frames: list[Image.Image]) -> tuple[int, int, int, int]:
    boxes = [frame.getbbox() for frame in frames]
    boxes = [box for box in boxes if box is not None]
    if not boxes:
        raise ValueError("Animation frames are empty")
    return (
        min(box[0] for box in boxes),
        min(box[1] for box in boxes),
        max(box[2] for box in boxes),
        max(box[3] for box in boxes),
    )


def load_action(
    spec: dict, animations: dict[str, ET.Element], animation_dir: Path
) -> dict:
    source, frame_width, frame_height, durations, sheet_path = resolve_animation(
        spec["sources"], animations, animation_dir
    )
    with Image.open(sheet_path) as source_sheet:
        sheet = source_sheet.convert("RGBA")
    columns = sheet.width // frame_width
    rows = sheet.height // frame_height
    direction = min(spec["direction"], rows - 1)
    source_frames = [
        sheet.crop(
            (
                column * frame_width,
                direction * frame_height,
                (column + 1) * frame_width,
                (direction + 1) * frame_height,
            )
        )
        for column in range(columns)
    ]
    indices = resample_indices(columns, spec["frames"])
    selected = [source_frames[index] for index in indices]
    bbox = union_bbox(selected)
    cropped = [frame.crop(bbox) for frame in selected]
    frame_ms = [
        max(50, round((durations[index] if index < len(durations) else 4) * TICK_MS))
        for index in indices
    ]
    return {
        **spec,
        "source": source,
        "source_file": sheet_path.name,
        "source_indices": indices,
        "durations_ms": frame_ms,
        "frames_rgba": cropped,
        "native_box": list(bbox),
    }


def render_frames(action: dict, cell_size: tuple[int, int], scale: int) -> list[Image.Image]:
    cell_width, cell_height = cell_size
    rendered = []
    for source in action["frames_rgba"]:
        frame = source.resize((source.width * scale, source.height * scale), Image.Resampling.NEAREST)
        canvas = Image.new("RGBA", cell_size, (0, 0, 0, 0))
        x = (cell_width - frame.width) // 2
        y = cell_height - frame.height
        canvas.alpha_composite(frame, (x, y))
        rendered.append(canvas)
    return rendered


def save_strip(frames: list[Image.Image], path: Path) -> None:
    strip = Image.new("RGBA", (frames[0].width * len(frames), frames[0].height), (0, 0, 0, 0))
    for index, frame in enumerate(frames):
        strip.alpha_composite(frame, (index * frame.width, 0))
    strip.save(path)


def save_gif(frames: list[Image.Image], durations: list[int], path: Path) -> None:
    frames[0].save(
        path,
        save_all=True,
        append_images=frames[1:],
        duration=durations,
        disposal=2,
        loop=0,
        optimize=False,
        transparency=0,
    )


def make_contact_sheet(
    display_name: str, actions: list[dict], rendered: dict[str, list[Image.Image]], path: Path
) -> None:
    scale = 2
    thumb_width = max(frames[0].width for frames in rendered.values()) * scale
    thumb_height = max(frames[0].height for frames in rendered.values()) * scale
    label_width = 124
    row_height = thumb_height + 8
    sheet = Image.new("RGBA", (label_width + thumb_width, 28 + row_height * len(actions)), (24, 25, 28, 255))
    draw = ImageDraw.Draw(sheet)
    draw.text((8, 8), display_name, fill=(255, 255, 255, 255))
    for index, action in enumerate(actions):
        y = 28 + index * row_height
        draw.text((8, y + 8), f"{action['row']:02d} {action['id']}", fill=(220, 220, 220, 255))
        preview = rendered[action["id"]][0].resize((thumb_width, thumb_height), Image.Resampling.NEAREST)
        sheet.alpha_composite(preview, (label_width, y))
    sheet.save(path)


def build_stage(project_dir: Path, output_root: Path, stage_id: str, scale: int) -> dict:
    source_dir = next(project_dir.glob(f"{stage_id} *"))
    animation_dir = source_dir / "Animations"
    animations = parse_animations(animation_dir / "AnimData.xml")
    actions = [load_action(spec, animations, animation_dir) for spec in ACTIONS]

    max_width = max(frame.width for action in actions for frame in action["frames_rgba"])
    max_height = max(frame.height for action in actions for frame in action["frames_rgba"])
    cell_size = ((max_width + 4) * scale, (max_height + 4) * scale)
    slug, display_name = CHARACTERS[stage_id]
    stage_dir = output_root / slug
    strips_dir = stage_dir / "strips"
    previews_dir = stage_dir / "previews"
    strips_dir.mkdir(parents=True, exist_ok=True)
    previews_dir.mkdir(parents=True, exist_ok=True)

    rendered = {action["id"]: render_frames(action, cell_size, scale) for action in actions}
    atlas = Image.new("RGBA", (cell_size[0] * 8, cell_size[1] * len(actions)), (0, 0, 0, 0))
    manifest_actions = []
    for action in actions:
        frames = rendered[action["id"]]
        basename = f"{action['row']:02d}-{action['id']}"
        save_strip(frames, strips_dir / f"{basename}.png")
        save_gif(frames, action["durations_ms"], previews_dir / f"{basename}.gif")
        for column, frame in enumerate(frames):
            atlas.alpha_composite(frame, (column * cell_size[0], action["row"] * cell_size[1]))
        manifest_actions.append(
            {
                "row": action["row"],
                "id": action["id"],
                "frame_count": len(frames),
                "durations_ms": action["durations_ms"],
                "source_animation": action["source"],
                "source_file": action["source_file"],
                "source_indices": action["source_indices"],
                "native_box": action["native_box"],
            }
        )

    atlas.save(stage_dir / "atlas.png")
    make_contact_sheet(display_name, actions, rendered, stage_dir / "contact-sheet.png")
    manifest = {
        "id": slug,
        "display_name": display_name,
        "format": "TokenNotch pixel action atlas v1",
        "pixel_scale": scale,
        "columns": 8,
        "rows": len(actions),
        "cell_width": cell_size[0],
        "cell_height": cell_size[1],
        "transparent_background": True,
        "bitmap_scaling": "nearest-neighbor",
        "actions": manifest_actions,
    }
    (stage_dir / "manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--downloads", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--scale", type=int, default=2)
    parser.add_argument(
        "--ids",
        nargs="+",
        default=["0004", "0005", "0006"],
        help="Pokedex IDs to include, for example: 0001 0002 0003",
    )
    args = parser.parse_args()
    if args.scale < 1:
        parser.error("--scale must be at least 1")

    args.output.mkdir(parents=True, exist_ok=True)
    ids = [str(value).zfill(4) for value in args.ids]
    unknown = [value for value in ids if value not in CHARACTERS]
    if unknown:
        parser.error(f"unsupported character ids: {', '.join(unknown)}")
    manifests = [build_stage(args.downloads, args.output, stage_id, args.scale) for stage_id in ids]
    root_manifest = {
        "format": "TokenNotch pixel action pack v1",
        "characters": [manifest["id"] for manifest in manifests],
        "source": "PMD Collab animation assets",
        "source_url": "https://sprites.pmdcollab.org/",
        "notes": "Keep nearest-neighbor scaling enabled. Artwork rights remain with their respective owners and contributors.",
    }
    (args.output / "manifest.json").write_text(
        json.dumps(root_manifest, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
