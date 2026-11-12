#!/usr/bin/env python3
"""
Draws the home-screen icons from the same orange upload arrow as web/public/favicon.svg.
    python3 dev/make-icons.py        # needs Pillow; writes web/public/icons/*.png
"""
import os
from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(__file__), "..", "web", "public", "icons")
ORANGE, INK = (255, 90, 31, 255), (10, 10, 10, 255)
SS = 4  # draw 4x bigger, then shrink, for smooth edges


def arrow(draw, size, scale=1.35):
    """The favicon's arrow (a 32x32 drawing), centred and scaled to `size` pixels."""
    k = size / 32 * scale
    cx, cy = 16, 16.5  # the arrow's own centre in the 32x32 drawing

    def p(x, y):
        return (size / 2 + (x - cx) * k, size / 2 + (y - cy) * k)

    w = 2.2 * k
    r = w / 2
    for a, b in [((16, 23), (16, 10)), ((10.5, 15.5), (16, 10)), ((16, 10), (21.5, 15.5))]:
        draw.line([p(*a), p(*b)], fill=INK, width=round(w))
        for x, y in (a, b):  # round caps
            px, py = p(x, y)
            draw.ellipse([px - r, py - r, px + r, py + r], fill=INK)


def icon(size, rounded):
    big = size * SS
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    if rounded:
        d.rounded_rectangle([0, 0, big - 1, big - 1], radius=round(big * 0.19), fill=ORANGE)
    else:
        d.rectangle([0, 0, big, big], fill=ORANGE)  # full bleed: the phone rounds it itself
    arrow(d, big)
    return img.resize((size, size), Image.LANCZOS)


os.makedirs(OUT, exist_ok=True)
# "any": shown as is (browser tabs, install dialogs). "maskable": full bleed, the arrow stays inside the
# central 80% safe zone so Android can crop it to a circle or squircle. The Apple icon must be opaque.
for name, size, rounded in [
    ("icon-192.png", 192, True),
    ("icon-512.png", 512, True),
    ("maskable-512.png", 512, False),
    ("apple-touch-icon.png", 180, False),
]:
    icon(size, rounded).save(os.path.join(OUT, name), optimize=True)
    print("wrote", name)
