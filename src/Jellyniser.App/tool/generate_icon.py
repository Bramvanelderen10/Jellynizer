#!/usr/bin/env python3
"""Generate the Jellyniser app icon source artwork.

The artwork is code, not a binary file, so colours and proportions stay editable and the
result is reproducible. The mark is a Jellyfin-style rounded "J" with a puzzle-piece badge
added to the lower-right corner, marking this as a companion / plugin app.

Usage:
    python3 -m pip install pillow          # once
    python3 tool/generate_icon.py

Then, from src/Jellyniser.App:
    dart run flutter_launcher_icons

Outputs (all 1024x1024) in assets/icon/:
    app_icon.png             full icon on a teal gradient (RGB, no alpha)
    app_icon_foreground.png  Android adaptive foreground layer (RGBA, transparent)
    app_icon_monochrome.png  Android 13+ themed icon (RGBA, white silhouette)
"""

import math
import os

from PIL import Image, ImageDraw, ImageFilter

CANVAS = 1600

BG_TOP = (0, 121, 107)        # #00796B
BG_BOTTOM = (0, 77, 64)       # #004D40
MARK_COLOR = (255, 255, 255)
BADGE_COLOR = (123, 31, 162)  # #7B1FA2, jellyfin-ish purple
BADGE_OUTLINE = 14            # px of white outline around the badge, at CANVAS scale

# The full icon can use more of the canvas than the adaptive foreground, because
# flutter_launcher_icons insets the foreground by 16% (-> 68% of the layer).
FULL_MARK_FRACTION = 0.54
ADAPTIVE_MARK_FRACTION = 0.70

OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "icon")


def draw_j(mask, canvas=CANVAS):
    """Draw a Jellyfin-style rounded 'J': stem + half-ring hook + two round caps.

    The hook is an explicit polygon rather than draw.arc(), because a stroke this thick
    (0.16 * canvas) leaves visible spikes with arc()/joint="curve".
    """
    draw = ImageDraw.Draw(mask)
    half = int(0.08 * canvas)          # half the 0.16 * canvas stroke
    stem_x0, stem_x1 = 864, 1120
    stem_y0, stem_y1 = 320, 992

    draw.rectangle([stem_x0, stem_y0, stem_x1, stem_y1], fill=255)

    cx, cy, outer_r, inner_r = 736, stem_y1, 384, 128
    outer, inner = [], []
    for step in range(181):
        angle = math.radians(step)      # 0 -> 180 sweeps the lower half
        cos_a, sin_a = math.cos(angle), math.sin(angle)
        outer.append((cx + outer_r * cos_a, cy + outer_r * sin_a))
        inner.append((cx + inner_r * cos_a, cy + inner_r * sin_a))
    draw.polygon(outer + inner[::-1], fill=255)

    # Round the two free ends of the stroke: the stem's top centre and the hook's left end.
    stem_cx = (stem_x0 + stem_x1) // 2
    draw.ellipse([stem_cx - half, stem_y0 - half, stem_cx + half, stem_y0 + half], fill=255)
    draw.ellipse([480 - half, stem_y1 - half, 480 + half, stem_y1 + half], fill=255)


def draw_puzzle_badge(cx, cy, body, canvas=CANVAS):
    """Draw a classic puzzle piece (tab on top, notch on the right) as an L mask."""
    mask = Image.new("L", (canvas, canvas), 0)
    draw = ImageDraw.Draw(mask)
    half = body // 2
    tab_r = int(body * 0.30)
    notch_r = int(body * 0.28)

    draw.rounded_rectangle(
        [cx - half, cy - half, cx + half, cy + half],
        radius=int(body * 0.16),
        fill=255,
    )
    draw.ellipse(
        [cx - tab_r, cy - half - int(tab_r * 1.4), cx + tab_r, cy - half + int(tab_r * 0.6)],
        fill=255,
    )
    draw.ellipse(
        [cx + half - int(notch_r * 1.4), cy - notch_r, cx + half + int(notch_r * 0.6), cy + notch_r],
        fill=0,
    )
    return mask


def build_mark(canvas=CANVAS):
    """Compose the 'J' plus the plugin badge into one transparent RGBA mark."""
    j = Image.new("L", (canvas, canvas), 0)
    draw_j(j, canvas)

    x0, y0, x1, y1 = j.getbbox()
    mark_h = y1 - y0
    body = int(0.30 * mark_h)
    badge = draw_puzzle_badge(
        cx=x1 - int(body * 0.10),
        cy=y1 - int(body * 0.05),
        body=body,
        canvas=canvas,
    )
    outline = badge.filter(ImageFilter.MaxFilter(BADGE_OUTLINE * 2 + 1))

    mark = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
    mark.paste(MARK_COLOR + (255,), (0, 0), j)
    mark.paste(MARK_COLOR + (255,), (0, 0), outline)     # white ring around the badge
    mark.paste(BADGE_COLOR + (255,), (0, 0), badge)
    return mark.crop(mark.getbbox())


def vertical_gradient(size, top, bottom):
    strip = Image.new("RGB", (1, size), top)
    for y in range(size):
        t = y / max(1, size - 1)
        strip.putpixel((0, y), tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(3)))
    return strip.resize((size, size))


def scaled(mark, fraction, size=1024):
    scale = (fraction * size) / max(mark.size)
    return mark.resize(
        (max(1, round(mark.width * scale)), max(1, round(mark.height * scale))),
        Image.LANCZOS,
    )


def paste_centered(base, mark):
    base.alpha_composite(
        mark, ((base.width - mark.width) // 2, (base.height - mark.height) // 2)
    )


def solid(mark, color=(255, 255, 255, 255)):
    out = Image.new("RGBA", mark.size, color)
    out.putalpha(mark.getchannel("A"))
    return out


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    mark = build_mark()

    icon = vertical_gradient(1024, BG_TOP, BG_BOTTOM).convert("RGBA")
    paste_centered(icon, scaled(mark, FULL_MARK_FRACTION))
    # Flattened to RGB: no alpha, square, no baked corners.
    icon.convert("RGB").save(os.path.join(OUT_DIR, "app_icon.png"))

    foreground = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
    paste_centered(foreground, scaled(mark, ADAPTIVE_MARK_FRACTION))
    foreground.save(os.path.join(OUT_DIR, "app_icon_foreground.png"))

    monochrome = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
    paste_centered(monochrome, scaled(solid(mark), ADAPTIVE_MARK_FRACTION))
    monochrome.save(os.path.join(OUT_DIR, "app_icon_monochrome.png"))

    for name in ("app_icon.png", "app_icon_foreground.png", "app_icon_monochrome.png"):
        path = os.path.join(OUT_DIR, name)
        with Image.open(path) as image:
            print(f"{name}: {image.size[0]}x{image.size[1]} {image.mode}")


if __name__ == "__main__":
    main()
