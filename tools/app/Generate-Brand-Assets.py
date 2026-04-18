from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw


REPO_ROOT = Path(__file__).resolve().parents[2]
APP_ASSETS = REPO_ROOT / "src" / "DopeCompanion.App" / "Assets"
PACKAGE_IMAGES = REPO_ROOT / "src" / "DopeCompanion.App.Package" / "Images"


VARIANTS = {
    "Preview": {
        "shell": (5, 11, 17, 255),
        "panel": (9, 20, 28, 255),
        "border": (21, 220, 246, 255),
        "inner": (20, 74, 96, 255),
        "beam": (21, 220, 246, 255),
        "beam_soft": (10, 45, 58, 255),
        "primary": (21, 220, 246, 255),
        "secondary": (234, 252, 255, 255),
        "tag": (255, 151, 72, 255),
        "tag_soft": (66, 33, 11, 255),
    },
    "Published": {
        "shell": (7, 14, 11, 255),
        "panel": (10, 24, 18, 255),
        "border": (56, 211, 153, 255),
        "inner": (25, 73, 51, 255),
        "beam": (112, 243, 194, 255),
        "beam_soft": (12, 42, 28, 255),
        "primary": (56, 211, 153, 255),
        "secondary": (242, 255, 250, 255),
        "tag": (170, 255, 211, 255),
        "tag_soft": (22, 54, 40, 255),
    },
}


def rounded_box(draw: ImageDraw.ImageDraw, box: tuple[int, int, int, int], radius: int, fill, outline=None, width: int = 1) -> None:
    draw.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)


def draw_variant(size: int, variant_name: str) -> Image.Image:
    palette = VARIANTS[variant_name]
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    margin = max(4, round(size * 0.08))
    outer_radius = max(8, round(size * 0.2))
    outline_width = max(2, round(size * 0.018))
    shell_box = (margin, margin, size - margin, size - margin)
    rounded_box(draw, shell_box, outer_radius, fill=palette["shell"], outline=palette["border"], width=outline_width)

    inner_margin = max(6, round(size * 0.14))
    inner_radius = max(6, round(size * 0.14))
    inner_box = (inner_margin, inner_margin, size - inner_margin, size - inner_margin)
    rounded_box(draw, inner_box, inner_radius, fill=palette["panel"], outline=palette["inner"], width=max(1, outline_width // 2))

    beam_height = max(3, round(size * 0.03))
    beam_margin = max(8, round(size * 0.2))
    rounded_box(
        draw,
        (
            beam_margin,
            inner_margin + max(3, round(size * 0.025)),
            size - beam_margin,
            inner_margin + max(3, round(size * 0.025)) + beam_height,
        ),
        max(2, beam_height // 2),
        fill=palette["beam"],
    )

    base_y = round(size * 0.72)
    base_height = max(5, round(size * 0.06))
    rounded_box(
        draw,
        (round(size * 0.24), base_y, round(size * 0.76), base_y + base_height),
        max(3, base_height // 2),
        fill=palette["beam_soft"],
        outline=palette["inner"],
        width=max(1, outline_width // 3),
    )

    pillar_width = max(6, round(size * 0.11))
    gap = max(4, round(size * 0.035))
    center_x = size // 2
    pillar_bottom = round(size * 0.68)
    side_top = round(size * 0.28)
    middle_top = round(size * 0.22)
    pillar_radius = max(4, round(size * 0.045))
    left_x = center_x - pillar_width - gap // 2
    middle_x = center_x - pillar_width // 2
    right_x = center_x + gap // 2

    rounded_box(
        draw,
        (left_x - pillar_width, side_top, left_x, pillar_bottom),
        pillar_radius,
        fill=palette["primary"],
    )
    rounded_box(
        draw,
        (middle_x, middle_top, middle_x + pillar_width, pillar_bottom + max(2, round(size * 0.02))),
        pillar_radius,
        fill=palette["secondary"],
    )
    rounded_box(
        draw,
        (right_x, side_top, right_x + pillar_width, pillar_bottom),
        pillar_radius,
        fill=palette["primary"],
    )

    slot_height = max(3, round(size * 0.022))
    slot_y = round(size * 0.47)
    rounded_box(
        draw,
        (round(size * 0.29), slot_y, round(size * 0.71), slot_y + slot_height),
        max(2, slot_height // 2),
        fill=palette["beam_soft"],
    )

    tag_size = max(8, round(size * 0.13))
    tag_box = (
        size - inner_margin - tag_size,
        inner_margin + max(4, round(size * 0.045)),
        size - inner_margin,
        inner_margin + max(4, round(size * 0.045)) + tag_size,
    )
    if variant_name == "Preview":
        draw.ellipse(tag_box, fill=palette["tag"])
        draw.ellipse(
            (
                tag_box[0] + max(2, round(size * 0.018)),
                tag_box[1] + max(2, round(size * 0.018)),
                tag_box[2] - max(2, round(size * 0.018)),
                tag_box[3] - max(2, round(size * 0.018)),
            ),
            fill=palette["tag_soft"],
        )
    else:
        draw.rounded_rectangle(tag_box, radius=max(2, tag_size // 4), fill=palette["tag"])
        inset = max(2, round(size * 0.018))
        draw.polygon(
            [
                ((tag_box[0] + tag_box[2]) // 2, tag_box[1] + inset),
                (tag_box[2] - inset, (tag_box[1] + tag_box[3]) // 2),
                ((tag_box[0] + tag_box[2]) // 2, tag_box[3] - inset),
                (tag_box[0] + inset, (tag_box[1] + tag_box[3]) // 2),
            ],
            fill=palette["tag_soft"],
        )

    return image


def ensure_directory(path: Path) -> None:
    path.mkdir(parents=True, exist_ok=True)


def save_icon_family(variant_name: str) -> None:
    brand_root = APP_ASSETS / "Branding" / variant_name
    ensure_directory(brand_root)

    app_image = draw_variant(512, variant_name)
    app_png_path = brand_root / "dope-companion.png"
    app_ico_path = brand_root / "dope-companion.ico"
    app_image.save(app_png_path)
    app_image.save(
        app_ico_path,
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )

    package_brand_root = PACKAGE_IMAGES / "Branding" / variant_name
    ensure_directory(package_brand_root)
    for size_name, size in {"StoreLogo.png": 50, "Square44x44Logo.png": 44, "Square150x150Logo.png": 150}.items():
        draw_variant(size, variant_name).save(package_brand_root / size_name)


def copy_default_assets() -> None:
    published_root = APP_ASSETS / "Branding" / "Published"
    preview_root = APP_ASSETS / "Branding" / "Preview"
    published_package_root = PACKAGE_IMAGES / "Branding" / "Published"

    (APP_ASSETS / "dope-companion.png").write_bytes((published_root / "dope-companion.png").read_bytes())
    (APP_ASSETS / "dope-companion.ico").write_bytes((published_root / "dope-companion.ico").read_bytes())
    (APP_ASSETS / "dope-companion-dev.ico").write_bytes((preview_root / "dope-companion.ico").read_bytes())

    for name in ("StoreLogo.png", "Square44x44Logo.png", "Square150x150Logo.png"):
        (PACKAGE_IMAGES / name).write_bytes((published_package_root / name).read_bytes())


def main() -> None:
    ensure_directory(APP_ASSETS)
    ensure_directory(PACKAGE_IMAGES)

    for variant_name in VARIANTS:
        save_icon_family(variant_name)

    copy_default_assets()


if __name__ == "__main__":
    main()
