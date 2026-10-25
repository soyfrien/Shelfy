#!/usr/bin/env python3
"""Generates synthetic bookshelf photos for end-to-end recognition tests. Needs Pillow.

  python3 tools/make-test-shelf.py                 # test-shelf.jpg: 4 rotated spines (as before)
  python3 tools/make-test-shelf.py --wide          # wide-shelf.jpg: 16 spines, 6000x1500, plus wide-shelf.json (expected books)
  python3 tools/make-test-shelf.py --wide --tiles  # also writes wide-shelf-tile-1.jpg ... using the same plan as wwwroot/tiles.js

Spines are laid out so that a few of them straddle the tile boundaries of the wide image.
"""
import json, math, sys
from PIL import Image, ImageDraw, ImageFont

FB = "/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf"
FR = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"

SMALL = [("The Hobbit", "J.R.R. Tolkien", (30, 90, 60), (240, 220, 140)),
         ("Dune", "Frank Herbert", (200, 170, 90), (40, 30, 20)),
         ("Foundation", "Isaac Asimov", (25, 40, 100), (240, 240, 240)),
         ("Neuromancer", "William Gibson", (110, 20, 30), (240, 240, 240))]

WIDE = [("The Hobbit", "J.R.R. Tolkien", (30, 90, 60), (240, 220, 140)),
        ("Dune", "Frank Herbert", (200, 170, 90), (40, 30, 20)),
        ("Foundation", "Isaac Asimov", (25, 40, 100), (240, 240, 240)),
        ("Neuromancer", "William Gibson", (110, 20, 30), (240, 240, 240)),
        ("Fahrenheit 451", "Ray Bradbury", (190, 70, 30), (250, 240, 220)),
        ("Brave New World", "Aldous Huxley", (40, 110, 130), (250, 250, 250)),
        ("The Left Hand of Darkness", "Ursula K. Le Guin", (70, 50, 110), (240, 230, 250)),
        ("Snow Crash", "Neal Stephenson", (20, 20, 20), (250, 200, 40)),
        ("Ender's Game", "Orson Scott Card", (150, 30, 30), (250, 250, 250)),
        ("Hyperion", "Dan Simmons", (60, 60, 90), (230, 230, 200)),
        ("The Martian", "Andy Weir", (180, 90, 40), (255, 255, 255)),
        ("Frankenstein", "Mary Shelley", (35, 70, 40), (230, 240, 210)),
        ("Slaughterhouse-Five", "Kurt Vonnegut", (200, 190, 150), (30, 30, 30)),
        ("Do Androids Dream of Electric Sheep?", "Philip K. Dick", (90, 30, 90), (250, 240, 250)),
        ("The Handmaid's Tale", "Margaret Atwood", (150, 20, 40), (250, 250, 250)),
        ("Kindred", "Octavia E. Butler", (30, 80, 80), (240, 250, 250))]


def fit(draw, text, path, size, max_len):
    while size > 20:
        f = ImageFont.truetype(path, size)
        if draw.textlength(text, font=f) <= max_len:
            return f
        size -= 4
    return ImageFont.truetype(path, 20)


def draw_spine(img, x, top, bot, sw, title, author, bg, fg):
    d = ImageDraw.Draw(img)
    d.rectangle([x, top, x + sw, bot], fill=bg, outline=(20, 20, 20), width=4)
    length = bot - top
    sp = Image.new("RGB", (length, sw), bg)
    sd = ImageDraw.Draw(sp)
    ft = fit(sd, title, FB, 88, length - 80)
    fa = fit(sd, author, FR, 52, length - 80)
    tw = sd.textlength(title, font=ft)
    aw = sd.textlength(author, font=fa)
    sd.text(((length - tw) / 2, 40), title, font=ft, fill=fg)
    # author right under the title, so a long title in a small font still groups with it (like a real spine)
    sd.text(((length - aw) / 2, min(sw - 120, 40 + ft.size + 25)), author, font=fa, fill=fg)
    sp = sp.rotate(270, expand=True)  # text reads top-to-bottom
    img.paste(sp, (x, top))


def small():
    W, H = 1300, 1000
    img = Image.new("RGB", (W, H), (120, 85, 55))
    x, sw, gap, top, bot = 70, 270, 30, 60, H - 60
    for t, a, bg, fg in SMALL:
        draw_spine(img, x, top, bot, sw, t, a, bg, fg)
        x += sw + gap
    img.save("test-shelf.jpg", quality=90)


def plan_tiles(w, h):
    """Python port of planTiles() in src/Shelfy.Api/wwwroot/tiles.js (keep in sync)."""
    long_, short = max(w, h), min(w, h)
    horizontal = w >= h
    if long_ <= 2000 or (long_ / short <= 1.6 and long_ <= 4096):
        return [(0, 0, w, h)]
    target = min(short * 1.5, 2400)
    step = 1 - 0.22
    n = min(12, max(2, math.ceil((long_ - target) / (target * step)) + 1))
    ln = long_ / (1 + (n - 1) * step)
    tl = min(long_, round(ln))
    out = []
    for i in range(n):
        pos = long_ - tl if i == n - 1 else round(i * ln * step)
        out.append((pos, 0, tl, h) if horizontal else (0, pos, w, tl))
    return out


def wide(tiles):
    W, H = 6000, 1500
    img = Image.new("RGB", (W, H), (120, 85, 55))
    sw, gap, top, bot = 270, 30, 60, H - 60
    x = 50
    xs = []
    for t, a, bg, fg in WIDE:
        draw_spine(img, x, top, bot, sw, t, a, bg, fg)
        xs.append((t, x, x + sw))
        x += sw + gap
    img.save("wide-shelf.jpg", quality=90)
    plan = plan_tiles(W, H)
    edges = [p[0] for p in plan[1:]] + [p[0] + p[2] for p in plan[:-1]]
    straddling = [t for t, a, b in xs if any(a < e < b for e in edges)]
    json.dump({"expected": [t for t, _, _ in xs], "tiles": plan, "straddlingTileEdges": straddling},
              open("wide-shelf.json", "w"), indent=1)
    print(f"wide-shelf.jpg {W}x{H}: {len(WIDE)} spines, {len(plan)} tiles, straddling a tile edge: {straddling}")
    if tiles:
        for i, (x0, y0, w, h) in enumerate(plan, 1):
            img.crop((x0, y0, x0 + w, y0 + h)).save(f"wide-shelf-tile-{i}.jpg", quality=90)


if __name__ == "__main__":
    if "--wide" in sys.argv:
        wide("--tiles" in sys.argv)
    else:
        small()
