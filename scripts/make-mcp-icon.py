"""Rasterize the existing assets/brand/logo.svg mark to a 64px PNG for MCP."""
from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "src" / "AiProject.Console.Core" / "Stack" / "mcp-icon.png"
SIZE = 64
SCALE = 8
S = SIZE * SCALE


def lerp(a: int, b: int, t: float) -> int:
    return int(a + (b - a) * t)


def paint() -> Image.Image:
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    # Approximate the SVG gradient on the squircle.
    for y in range(S):
        t = y / (S - 1)
        if t < 0.42:
            u = t / 0.42
            color = (lerp(26, 11, u), lerp(163, 110, u), lerp(124, 86, u), 255)
        else:
            u = (t - 0.42) / 0.58
            color = (lerp(11, 5, u), lerp(110, 46, u), lerp(86, 39, u), 255)
        d.line([(0, y), (S - 1, y)], fill=color)

    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [0, 0, S - 1, S - 1], radius=int(S * 0.227), fill=255
    )
    bg = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    bg.paste(im, mask=mask)

    d = ImageDraw.Draw(bg)
    w = max(SCALE + 2, S // 26)
    white = (255, 255, 255, 255)
    gold = (212, 160, 23, 255)

    def xy(x: float, y: float) -> tuple[int, int]:
        return int(S * x / 256), int(S * y / 256)

    d.rounded_rectangle([*xy(50, 60), *xy(206, 196)], radius=int(S * 18 / 256), outline=white, width=w)
    d.line([xy(50, 90), xy(206, 90)], fill=white, width=w)
    d.line([xy(72, 122), xy(98, 144), xy(72, 166)], fill=white, width=w, joint="curve")
    d.line([xy(112, 166), xy(140, 166)], fill=white, width=w)

    r = max(SCALE, int(S * 5 / 256))
    for cx in (72, 90, 108):
        x, y = xy(cx, 75)
        d.ellipse((x - r, y - r, x + r, y + r), fill=white)

    bar_h = max(SCALE, int(S * 10 / 256))
    for y, width in ((118, 38), (138, 30), (158, 34)):
        x0, y0 = xy(152, y)
        x1 = x0 + int(S * width / 256)
        d.rounded_rectangle([x0, y0, x1, y0 + bar_h], radius=bar_h // 2, fill=white)

    gr = max(SCALE, int(S * 7 / 256))
    gx, gy = xy(200, 123)
    d.ellipse((gx - gr, gy - gr, gx + gr, gy + gr), fill=gold)
    return bg.resize((SIZE, SIZE), Image.Resampling.LANCZOS).filter(ImageFilter.UnsharpMask(radius=0.6, percent=80, threshold=2))


def main() -> None:
    OUT.parent.mkdir(parents=True, exist_ok=True)
    paint().save(OUT, "PNG", optimize=True)
    print(f"Wrote {OUT} ({OUT.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
