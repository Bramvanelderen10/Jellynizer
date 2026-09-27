#!/usr/bin/env python3
"""Generate the Jellynizer app icon source artwork.

The artwork is code, not a binary file, so colours and proportions stay editable and the
result is reproducible. The mark is the shape Jellyfin itself uses - a rounded triangle
ring with a solid triangle in the middle - filled with Jellyfin's purple -> blue gradient
on Jellyfin's dark navy, plus a white puzzle-piece badge in the lower-right corner that
marks this as a companion / plugin app rather than the Jellyfin app.

The two path definitions are the ones Jellyfin ships in its own icon artwork
(jellyfin/jellyfin-ux, branding/SVG/icon-transparent.svg). That artwork is licensed
CC BY-SA 4.0 (https://github.com/jellyfin/jellyfin-ux); only the geometry is reused, and
it is re-rasterised here at build time so there is no binary source to lose.

Usage:
    python3 -m pip install pillow          # once
    python3 tool/generate_icon.py

Then, from src/Jellynizer.App:
    dart run flutter_launcher_icons

Outputs (all 1024x1024) in assets/icon/:
    app_icon.png             full icon on navy (RGB, no alpha)
    app_icon_foreground.png  Android adaptive foreground layer (RGBA, transparent)
    app_icon_monochrome.png  Android 13+ themed icon (RGBA, white silhouette)
"""

import os
import re

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ICON_SIZE = 1024

# Jellyfin's palette: the navy behind its own launcher icon, plus the gradient and the
# white of its wordmark.
BG = (0, 11, 37)                 # #000B25
GRADIENT_FROM = (170, 92, 195)   # #AA5CC3
GRADIENT_TO = (0, 164, 220)      # #00A4DC
BADGE_COLOR = (255, 255, 255)

# The mark is drawn in Jellyfin's own 512x512 coordinate system, with its own gradient
# vector, and rasterised at SS times that before being scaled down for smooth edges.
MARK_VIEW = 512
GRADIENT_START = (110.25, 213.3)
GRADIENT_END = (496.14, 436.09)
SS = 2
MARK_RES = MARK_VIEW * SS

MARK_OUTER = (
    "M256,23.3C194.44,23.3-3.82,382.73,26.41,443.43s429.34,60,459.24,0S317.62,23.3,256,23.3Z"
    "M406.51,390.76c-19.59,39.33-281.08,39.77-300.89,0"
    "S215.71,115.48,256.06,115.48,426.1,351.42,406.51,390.76Z"
)
MARK_INNER = (
    "M256,201.62c-20.44,0-86.23,119.29-76.2,139.43s142.48,19.92,152.4,0S276.47,201.63,256,201.62Z"
)

# The full icon can use more of the canvas than the adaptive foreground, because
# flutter_launcher_icons insets the foreground by 16% (-> 68% of the layer).
FULL_MARK_FRACTION = 0.56
ADAPTIVE_MARK_FRACTION = 0.64

BADGE_BODY_FRACTION = 0.26    # badge width/height, as a fraction of the mark's height
BADGE_GAP_FRACTION = 0.07     # background-coloured gap between the badge and the mark
BADGE_CENTRE_X_FRACTION = 0.74  # badge centre, as a fraction of the mark's bounding box
BADGE_CENTRE_Y_FRACTION = 0.14  # measured up from the bottom of that box

OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "icon")


_PATH_TOKEN_RE = re.compile(r"[A-Za-z]|-?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?")
_ARITY = {"M": 2, "L": 2, "H": 1, "V": 1, "C": 6, "S": 4, "Z": 0}
CURVE_STEPS = 64   # segments per bezier; at MARK_RES each one is well under a pixel


def _cubic_points(start, ctrl1, ctrl2, end, steps=CURVE_STEPS):
    """Sample a cubic bezier into a polyline."""
    points = []
    for step in range(1, steps + 1):
        t = step / steps
        u = 1.0 - t
        w0, w1, w2, w3 = u * u * u, 3 * u * u * t, 3 * u * t * t, t * t * t
        points.append((
            w0 * start[0] + w1 * ctrl1[0] + w2 * ctrl2[0] + w3 * end[0],
            w0 * start[1] + w1 * ctrl1[1] + w2 * ctrl2[1] + w3 * end[1],
        ))
    return points


def flatten_path(d):
    """Flatten an SVG path (Move/Line/H/V/Cubic/Smooth-cubic/Close) into polygons.

    Pillow has no path support, so the curves are sampled by hand. Repeating parameter
    groups (for example a single "S" followed by two coordinate pairs) are handled the
    same way SVG does, as an implicit repeat of the command.
    """
    tokens = _PATH_TOKEN_RE.findall(d)
    commands = []
    index = 0
    while index < len(tokens):
        name = tokens[index]
        index += 1
        numbers = []
        while index < len(tokens) and not tokens[index].isalpha():
            numbers.append(float(tokens[index]))
            index += 1
        commands.append((name, numbers))

    polygons = []
    polygon = []
    x = y = start_x = start_y = 0.0
    ctrl2 = None
    for name, numbers in commands:
        upper = name.upper()
        if upper not in _ARITY:
            raise ValueError(f"unsupported path command: {name!r}")
        relative = name.islower()
        arity = _ARITY[upper]
        if arity == 0:
            if polygon:
                polygons.append(polygon)
                polygon = []
            x, y = start_x, start_y
            ctrl2 = None
            continue
        if len(numbers) % arity:
            raise ValueError(f"{name!r} has {len(numbers)} parameters")
        move = upper == "M"
        for offset in range(0, len(numbers), arity):
            values = numbers[offset:offset + arity]
            if move:
                move = False
                nx = values[0] + x if relative else values[0]
                ny = values[1] + y if relative else values[1]
                if polygon:
                    polygons.append(polygon)
                polygon = [(nx, ny)]
                x = start_x = nx
                y = start_y = ny
                ctrl2 = None
            elif upper in ("M", "L"):
                nx = values[0] + x if relative else values[0]
                ny = values[1] + y if relative else values[1]
                polygon.append((nx, ny))
                x, y = nx, ny
                ctrl2 = None
            elif upper == "H":
                x = values[0] + x if relative else values[0]
                polygon.append((x, y))
                ctrl2 = None
            elif upper == "V":
                y = values[0] + y if relative else values[0]
                polygon.append((x, y))
                ctrl2 = None
            elif upper == "C":
                ctrl1, ctrl2, end = values[0:2], values[2:4], values[4:6]
                if relative:
                    ctrl1 = [ctrl1[0] + x, ctrl1[1] + y]
                    ctrl2 = [ctrl2[0] + x, ctrl2[1] + y]
                    end = [end[0] + x, end[1] + y]
                polygon.extend(_cubic_points((x, y), ctrl1, ctrl2, end))
                x, y = end
            else:   # "S": first control point mirrors the previous curve's second one
                previous, ctrl2, end = ctrl2, values[0:2], values[2:4]
                if relative:
                    ctrl2 = [ctrl2[0] + x, ctrl2[1] + y]
                    end = [end[0] + x, end[1] + y]
                ctrl1 = (2 * x - previous[0], 2 * y - previous[1]) if previous else (x, y)
                polygon.extend(_cubic_points((x, y), ctrl1, ctrl2, end))
                x, y = end
    if polygon:
        polygons.append(polygon)
    return polygons


def render_mark_mask(res=MARK_RES):
    """Rasterise the mark: outer triangle ring (even-odd hole) plus inner triangle."""
    scale = res / MARK_VIEW
    mask = Image.new("L", (res, res), 0)
    draw = ImageDraw.Draw(mask)
    outer, hole = flatten_path(MARK_OUTER)
    draw.polygon([(px * scale, py * scale) for px, py in outer], fill=255)
    draw.polygon([(px * scale, py * scale) for px, py in hole], fill=0)
    inner, = flatten_path(MARK_INNER)
    draw.polygon([(px * scale, py * scale) for px, py in inner], fill=255)
    return mask


def render_gradient(res=MARK_RES):
    """Jellyfin's purple -> blue ramp, projected on the vector its own artwork uses.

    Drawn at MARK_VIEW resolution and upscaled: the ramp is linear, so bilinear upscaling
    reproduces it exactly, and a per-pixel loop at MARK_RES would be 16x slower.
    """
    dx = GRADIENT_END[0] - GRADIENT_START[0]
    dy = GRADIENT_END[1] - GRADIENT_START[1]
    length_sq = dx * dx + dy * dy
    ramp = Image.new("RGB", (MARK_VIEW, MARK_VIEW))
    pixels = ramp.load()
    for y in range(MARK_VIEW):
        offset = (y - GRADIENT_START[1]) * dy - GRADIENT_START[0] * dx
        for x in range(MARK_VIEW):
            t = (offset + x * dx) / length_sq
            t = 0.0 if t < 0.0 else (1.0 if t > 1.0 else t)
            pixels[x, y] = (
                round(GRADIENT_FROM[0] + (GRADIENT_TO[0] - GRADIENT_FROM[0]) * t),
                round(GRADIENT_FROM[1] + (GRADIENT_TO[1] - GRADIENT_FROM[1]) * t),
                round(GRADIENT_FROM[2] + (GRADIENT_TO[2] - GRADIENT_FROM[2]) * t),
            )
    if res != MARK_VIEW:
        ramp = ramp.resize((res, res), Image.BILINEAR)
    return ramp


def render_badge(cx, cy, body, res=MARK_RES):
    """Draw a classic puzzle piece (tab on top, notch on the right) as an L mask."""
    mask = Image.new("L", (res, res), 0)
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


def build_mark():
    """Compose the mark, the gap around the badge and the badge itself.

    Returns a coloured RGBA mark and, cropped to the same box, a plain stencil for the
    monochrome layer (where the gap is transparency rather than a background colour).
    """
    mask = render_mark_mask()
    x0, y0, x1, y1 = mask.getbbox()
    width, height = x1 - x0, y1 - y0
    body = int(BADGE_BODY_FRACTION * height)
    gap = max(3, int(BADGE_GAP_FRACTION * body))
    badge = render_badge(
        cx=x0 + int(BADGE_CENTRE_X_FRACTION * width),
        cy=y1 - int(BADGE_CENTRE_Y_FRACTION * height),
        body=body,
    )
    ring = badge.filter(ImageFilter.MaxFilter(gap * 2 + 1))

    mark = render_gradient().convert("RGBA")
    mark.putalpha(mask)
    mark.paste(BG + (255,), (0, 0), ring)
    mark.paste(BADGE_COLOR + (255,), (0, 0), badge)

    stencil = Image.new("L", mask.size, 0)
    stencil.paste(255, (0, 0), mask)
    stencil.paste(255, (0, 0), ring)
    stencil.paste(0, (0, 0), ImageChops.subtract(ring, badge))
    stencil.paste(255, (0, 0), badge)

    box = mark.getbbox()
    return mark.crop(box), stencil.crop(box)


def scaled(image, fraction, size=ICON_SIZE):
    scale = (fraction * size) / max(image.size)
    return image.resize(
        (max(1, round(image.width * scale)), max(1, round(image.height * scale))),
        Image.LANCZOS,
    )


def paste_centered(base, overlay):
    base.alpha_composite(
        overlay, ((base.width - overlay.width) // 2, (base.height - overlay.height) // 2)
    )


def solid(image, color=(255, 255, 255, 255)):
    """Tint an L mask or an RGBA mark white, keeping its alpha."""
    out = Image.new("RGBA", image.size, color)
    out.putalpha(image if image.mode == "L" else image.getchannel("A"))
    return out


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    mark, stencil = build_mark()

    icon = Image.new("RGBA", (ICON_SIZE, ICON_SIZE), BG + (255,))
    paste_centered(icon, scaled(mark, FULL_MARK_FRACTION))
    # Flattened to RGB: no alpha, square, no baked corners.
    icon.convert("RGB").save(os.path.join(OUT_DIR, "app_icon.png"))

    foreground = Image.new("RGBA", (ICON_SIZE, ICON_SIZE), (0, 0, 0, 0))
    paste_centered(foreground, scaled(mark, ADAPTIVE_MARK_FRACTION))
    foreground.save(os.path.join(OUT_DIR, "app_icon_foreground.png"))

    monochrome = Image.new("RGBA", (ICON_SIZE, ICON_SIZE), (0, 0, 0, 0))
    paste_centered(monochrome, scaled(solid(stencil), ADAPTIVE_MARK_FRACTION))
    monochrome.save(os.path.join(OUT_DIR, "app_icon_monochrome.png"))

    for name in ("app_icon.png", "app_icon_foreground.png", "app_icon_monochrome.png"):
        path = os.path.join(OUT_DIR, name)
        with Image.open(path) as image:
            print(f"{name}: {image.size[0]}x{image.size[1]} {image.mode}")


if __name__ == "__main__":
    main()
