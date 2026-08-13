"""Regenerate app.ico / favicon / PNG from the master logo raster."""
from __future__ import annotations

import io
import shutil
import struct
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
CURSOR_MASTER = Path(
    r"C:\Users\user\.cursor\projects\e-sjvann-AI-Project-Console\assets\app-icon.png"
)
BRAND = ROOT / "assets" / "brand"
SOURCE = BRAND / "logo-source.png"
APP_ASSETS = ROOT / "src" / "AiProject.Console.App" / "Assets"
WWW_IMG = ROOT / "src" / "AiProject.Console.App" / "wwwroot" / "img"
WWWROOT = ROOT / "src" / "AiProject.Console.App" / "wwwroot"
ICO_SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)


def flood_transparent(im: Image.Image, thresh: int = 36) -> Image.Image:
    im = im.convert("RGBA")
    for xy in (
        (0, 0),
        (im.width - 1, 0),
        (0, im.height - 1),
        (im.width - 1, im.height - 1),
    ):
        ImageDraw.floodfill(im, xy, (0, 0, 0, 0), thresh=thresh)
    return im


def tight_square(im: Image.Image, pad_ratio: float = 0.05) -> Image.Image:
    bbox = im.getbbox()
    if not bbox:
        return im
    left, top, right, bottom = bbox
    side = max(right - left, bottom - top)
    pad = int(side * pad_ratio)
    cx = (left + right) / 2
    cy = (top + bottom) / 2
    half = side / 2 + pad
    box = (
        int(cx - half),
        int(cy - half),
        int(cx + half),
        int(cy + half),
    )
    canvas = Image.new("RGBA", (box[2] - box[0], box[3] - box[1]), (0, 0, 0, 0))
    src_box = (
        max(0, box[0]),
        max(0, box[1]),
        min(im.width, box[2]),
        min(im.height, box[3]),
    )
    dest = (src_box[0] - box[0], src_box[1] - box[1])
    canvas.paste(im.crop(src_box), dest)
    return canvas.resize((1024, 1024), Image.Resampling.LANCZOS)


def paint_simple(size: int) -> Image.Image:
    """Crisp taskbar-sized mark: teal squircle, prompt, gold status."""
    scale = 8
    s = size * scale
    im = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=int(s * 0.22), fill=(11, 110, 86, 255))
    w = max(scale + 2, s // 10)
    d.line(
        [(int(s * 0.26), int(s * 0.34)), (int(s * 0.50), int(s * 0.50)), (int(s * 0.26), int(s * 0.66))],
        fill=(255, 255, 255, 255),
        width=w,
        joint="curve",
    )
    d.line(
        [(int(s * 0.54), int(s * 0.66)), (int(s * 0.76), int(s * 0.66))],
        fill=(255, 255, 255, 255),
        width=w,
    )
    r = max(scale, int(s * 0.07))
    cx, cy = int(s * 0.76), int(s * 0.30)
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=(212, 160, 23, 255))
    return im.resize((size, size), Image.Resampling.LANCZOS)


def png_bytes(im: Image.Image) -> bytes:
    buf = io.BytesIO()
    im.save(buf, format="PNG", optimize=True)
    return buf.getvalue()


def dib_bytes(im: Image.Image) -> bytes:
    im = im.convert("RGBA")
    w, h = im.size
    pix = im.load()
    xor = bytearray()
    for row in range(h - 1, -1, -1):
        for col in range(w):
            r, g, b, a = pix[col, row]
            xor.extend((b, g, r, a))
    and_row = ((w + 31) // 32) * 4
    and_mask = bytearray(and_row * h)
    for row in range(h - 1, -1, -1):
        dest_row = h - 1 - row
        for col in range(w):
            if pix[col, row][3] < 128:
                and_mask[dest_row * and_row + col // 8] |= 1 << (7 - col % 8)
    header = struct.pack("<IIIHHIIIIII", 40, w, h * 2, 1, 32, 0, len(xor), 0, 0, 0, 0)
    return header + xor + and_mask


def write_ico(images: list[Image.Image], path: Path) -> None:
    payloads = [png_bytes(im) if im.width >= 128 else dib_bytes(im) for im in images]
    count = len(images)
    offset = 6 + 16 * count
    out = bytearray(struct.pack("<HHH", 0, 1, count))
    for im, data in zip(images, payloads):
        w = 0 if im.width >= 256 else im.width
        h = 0 if im.height >= 256 else im.height
        out += struct.pack("<BBBBHHII", w, h, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    for data in payloads:
        out += data
    path.write_bytes(bytes(out))


def write_svg(path: Path) -> None:
    path.write_text(
        """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" role="img" aria-label="AI_Project 控制台">
  <defs>
    <linearGradient id="bg" x1="36" y1="12" x2="220" y2="244" gradientUnits="userSpaceOnUse">
      <stop offset="0" stop-color="#1aa37c"/>
      <stop offset="0.42" stop-color="#0b6e56"/>
      <stop offset="1" stop-color="#052e27"/>
    </linearGradient>
  </defs>
  <rect width="256" height="256" rx="58" fill="url(#bg)"/>
  <g fill="none" stroke="#fff" stroke-width="10" stroke-linecap="round" stroke-linejoin="round">
    <rect x="50" y="60" width="156" height="136" rx="18"/>
    <path d="M50 90h156"/>
    <path d="M72 122l26 22-26 22"/>
    <path d="M112 166h28"/>
  </g>
  <circle cx="72" cy="75" r="5" fill="#fff"/>
  <circle cx="90" cy="75" r="5" fill="#fff"/>
  <circle cx="108" cy="75" r="5" fill="#fff"/>
  <rect x="152" y="118" width="38" height="10" rx="5" fill="#fff"/>
  <rect x="152" y="138" width="30" height="10" rx="5" fill="#fff"/>
  <rect x="152" y="158" width="34" height="10" rx="5" fill="#fff"/>
  <circle cx="200" cy="123" r="7" fill="#d4a017"/>
</svg>
""",
        encoding="utf-8",
    )


def main() -> None:
    if not SOURCE.exists():
        BRAND.mkdir(parents=True, exist_ok=True)
        if CURSOR_MASTER.exists():
            shutil.copy2(CURSOR_MASTER, SOURCE)
        elif (BRAND / "logo.png").exists():
            shutil.copy2(BRAND / "logo.png", SOURCE)
        else:
            raise SystemExit(f"找不到主圖：{SOURCE}")

    for folder in (BRAND, APP_ASSETS, WWW_IMG):
        folder.mkdir(parents=True, exist_ok=True)

    processed = tight_square(flood_transparent(Image.open(SOURCE)))
    png_1024 = BRAND / "logo.png"
    png_256 = WWW_IMG / "logo.png"
    ico = APP_ASSETS / "app.ico"
    svg = WWW_IMG / "logo.svg"

    processed.save(png_1024, "PNG", optimize=True)
    processed.resize((256, 256), Image.Resampling.LANCZOS).save(png_256, "PNG", optimize=True)

    frames: list[Image.Image] = []
    for size in ICO_SIZES:
        if size <= 24:
            frames.append(paint_simple(size))
        else:
            frames.append(processed.resize((size, size), Image.Resampling.LANCZOS))
    write_ico(frames, ico)
    write_svg(svg)
    shutil.copy2(ico, WWWROOT / "favicon.ico")
    shutil.copy2(ico, BRAND / "app.ico")
    shutil.copy2(svg, BRAND / "logo.svg")
    for name in ("preview-16.png", "preview-32.png"):
        p = BRAND / name
        if p.exists():
            p.unlink()
    print(f"Wrote {ico} ({ico.stat().st_size} bytes)")
    print(f"Wrote {png_1024} ({png_1024.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
