#!/usr/bin/env python3
"""
Draws the favicon and the home-screen icons from the logo's single source file, web/src/assets/logo.json
(a 24 x 24 grid, one letter per pixel). Run it after changing the logo:

    python3 dev/make-icons.py        # needs Pillow; writes web/public/favicon.svg and web/public/icons/*.png

Pixels are always scaled by whole numbers so every art pixel stays a crisp square. Then bump VERSION in
web/public/sw.js, or phones will keep showing the old icon from their saved copy.
"""
import json
import os

from PIL import Image

ROOT = os.path.join(os.path.dirname(__file__), "..")
SRC = os.path.join(ROOT, "web", "src", "assets", "logo.json")
PUBLIC = os.path.join(ROOT, "web", "public")
OUT = os.path.join(PUBLIC, "icons")

logo = json.load(open(SRC))
ROWS = logo["rows"]
N = len(ROWS)
PALETTE = logo["palette"]
PLATE = PALETTE["P"]
assert all(len(r) == N for r in ROWS), "the logo must be a square grid"


def rgba(hex_color):
    h = hex_color.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (255,)


def draw(rows, scale, background=None):
    """The grid as an image, `scale` real pixels per art pixel. Transparent where there's nothing."""
    img = Image.new("RGBA", (len(rows[0]) * scale, len(rows) * scale), rgba(background) if background else (0, 0, 0, 0))
    px = img.load()
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if ch == ".":
                continue
            c = rgba(PALETTE[ch])
            for dy in range(scale):
                for dx in range(scale):
                    px[x * scale + dx, y * scale + dy] = c
    return img


def bottle_only():
    """The bottle without the plate or the sparkle, cropped to its edges, for icons that bring their own background."""
    keep = ["".join("." if ch in "Pv" else ch for ch in row) for row in ROWS]
    ys = [y for y, r in enumerate(keep) if r.strip(".")]
    xs = [x for r in keep for x, ch in enumerate(r) if ch != "."]
    return [r[min(xs):max(xs) + 1] for r in keep[min(ys):max(ys) + 1]]


def centred(inner, size, background):
    canvas = Image.new("RGBA", (size, size), background)
    canvas.alpha_composite(inner, ((size - inner.width) // 2, (size - inner.height) // 2))
    return canvas


def save(img, name):
    os.makedirs(OUT, exist_ok=True)
    img.save(os.path.join(OUT, name), optimize=True)
    print("wrote", name)


# "any": the rounded plate, shown as is (browser tabs, install dialogs).
save(draw(ROWS, 8), "icon-192.png")                                          # 24 x 8 = 192, exact
save(centred(draw(ROWS, 21), 512, (0, 0, 0, 0)), "icon-512.png")             # 24 x 21 = 504, in a 512 frame

# "maskable": full bleed, and the bottle stays inside the central 80% so Android can crop it to a circle or squircle.
save(centred(draw(bottle_only(), 16), 512, rgba(PLATE)), "maskable-512.png")

# Apple's icon must be opaque (iOS rounds the corners itself).
save(centred(draw(bottle_only(), 6), 180, rgba(PLATE)), "apple-touch-icon.png")


def favicon_svg():
    """The same grid as a vector favicon: runs of equal pixels merged into single rectangles."""
    rects = []
    for y, row in enumerate(ROWS):
        x = 0
        while x < N:
            ch = row[x]
            if ch == ".":
                x += 1
                continue
            start = x
            while x < N and row[x] == ch:
                x += 1
            rects.append(f'<rect x="{start}" y="{y}" width="{x - start}" height="1" fill="{PALETTE[ch]}"/>')
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {N} {N}" width="{N}" height="{N}" shape-rendering="crispEdges">'
        + "".join(rects)
        + "</svg>\n"
    )


with open(os.path.join(PUBLIC, "favicon.svg"), "w") as f:
    f.write(favicon_svg())
print("wrote favicon.svg")
