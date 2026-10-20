#!/usr/bin/env python3
"""Generate the PaperKit app icon (PNG + multi-size ICO) with Pillow.

The mark is a document with a folded corner and two text lines - the same
glyph the app uses in its top bar - sitting on an indigo -> cyan plate.
"""
import os

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.abspath(os.path.join(HERE, "..", "build-assets"))
os.makedirs(OUT, exist_ok=True)

BG_A = (99, 102, 241)      # indigo
BG_B = (6, 182, 212)       # cyan
INK = (255, 255, 255)
MARK_INK = (255, 255, 255, 255)


def rounded_mask(size, radius):
    m = Image.new("L", (size, size), 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=255)
    return m


def draw_mark(size):
    """The PaperKit glyph: a page with a folded corner and two rules."""
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    s = size
    w = s * 0.50                       # page width
    h = s * 0.66                       # page height
    x0 = (s - w) / 2
    y0 = (s - h) / 2
    fold = w * 0.42                    # the folded corner triangle

    lw = max(2, int(s * 0.055))        # stroke width

    # page outline with the top-right corner cut off
    page = [
        (x0, y0),
        (x0 + w - fold, y0),
        (x0 + w, y0 + fold),
        (x0 + w, y0 + h),
        (x0, y0 + h),
    ]
    d.line(page + [page[0]], fill=MARK_INK, width=lw, joint="curve")

    # the fold itself
    d.line([(x0 + w - fold, y0), (x0 + w - fold, y0 + fold), (x0 + w, y0 + fold)],
           fill=MARK_INK, width=lw, joint="curve")

    # two rules of "text"
    rule = max(2, int(s * 0.045))
    d.line([(x0 + w * 0.22, y0 + h * 0.56), (x0 + w * 0.78, y0 + h * 0.56)],
           fill=MARK_INK, width=rule)
    d.line([(x0 + w * 0.22, y0 + h * 0.76), (x0 + w * 0.60, y0 + h * 0.76)],
           fill=MARK_INK, width=rule)
    return img


def draw_icon(size):
    """Gradient rounded plate with the mark on top."""
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))

    grad = Image.new("RGBA", (size, size))
    px = grad.load()
    for y in range(size):
        t = y / max(1, size - 1)
        col = (
            int(BG_A[0] + (BG_B[0] - BG_A[0]) * t),
            int(BG_A[1] + (BG_B[1] - BG_A[1]) * t),
            int(BG_A[2] + (BG_B[2] - BG_A[2]) * t),
            255,
        )
        for x in range(size):
            px[x, y] = col
    img.paste(grad, (0, 0), rounded_mask(size, size * 0.22))

    mark = draw_mark(size)
    img.alpha_composite(mark)
    return img


def main():
    big = draw_icon(1024)
    big.save(os.path.join(OUT, "icon.png"))

    sizes = [16, 24, 32, 48, 64, 128, 256]
    draw_icon(256).save(
        os.path.join(OUT, "icon.ico"),
        format="ICO",
        sizes=[(s, s) for s in sizes],
    )

    # a preview on the dark app background
    prev = Image.new("RGBA", big.size, (11, 14, 26, 255))
    prev.alpha_composite(big)
    prev.convert("RGB").save(os.path.join(OUT, "icon-preview.png"))

    print("wrote", OUT)
    for f in sorted(os.listdir(OUT)):
        print("  ", f)


if __name__ == "__main__":
    main()
