# -*- coding: utf-8 -*-
"""Regenerate README art (hero, steps, CTA buttons) with brand colors.

Run from repo: python assets/brand/render_readme.py
"""
from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent
OUT = ROOT

W, H = 2400, 620
GREEN = (11, 110, 86)
GREEN_DEEP = (5, 46, 39)
GREEN_MID = (14, 61, 52)
GREEN_BRIGHT = (26, 163, 124)
GOLD = (212, 160, 23)
WHITE = (255, 255, 255)
INK = (28, 36, 48)
MUTED = (90, 106, 122)
PANEL = (247, 249, 251)
OK = (26, 127, 75)
WAIT = (184, 110, 0)
DOWN = (138, 149, 163)
BORDER = (197, 206, 216)
ROW = (238, 242, 246)


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    name = "msjhbd.ttc" if bold else "msjh.ttc"
    path = Path(r"C:\Windows\Fonts") / name
    return ImageFont.truetype(str(path), size=size, index=0)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def gradient_bg(img: Image.Image) -> None:
    px = img.load()
    for y in range(H):
        ty = y / (H - 1)
        for x in range(W):
            tx = x / (W - 1)
            c1 = lerp(GREEN_DEEP, GREEN, 0.35 + 0.45 * tx)
            c2 = lerp(GREEN, GREEN_MID, ty)
            c = lerp(c1, c2, ty * 0.7)
            # soft highlight top-left
            glow = max(0.0, 1.0 - ((x - 220) ** 2 + (y - 40) ** 2) ** 0.5 / 620)
            c = lerp(c, GREEN_BRIGHT, glow * 0.22)
            px[x, y] = c + (255,)


def rr(draw: ImageDraw.ImageDraw, box, r, fill, outline=None, width=1):
    draw.rounded_rectangle(box, radius=r, fill=fill, outline=outline, width=width)


def load_logo(size: int) -> Image.Image:
    im = Image.open(ROOT / "logo.png").convert("RGBA")
    pixels = [(0, 0, 0, 0) if r < 18 and g < 18 and b < 18 else (r, g, b, a) for r, g, b, a in im.getdata()]
    im.putdata(pixels)
    return im.resize((size, size), Image.Resampling.LANCZOS)


def render_hero() -> None:
    img = Image.new("RGBA", (W, H), GREEN_DEEP + (255,))
    gradient_bg(img)
    d = ImageDraw.Draw(img)

    f_ui = font(22, True)
    f_small = font(18, False)
    f_mono = font(18, False)

    # Window mock — title lives in README markdown so this stays a product shot.
    wx, wy, ww, wh = 72, 48, 2256, 524
    shadow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    sd = ImageDraw.Draw(shadow)
    rr(sd, (wx, wy + 18, wx + ww, wy + wh + 18), 22, (2, 26, 22, 70))
    img.alpha_composite(shadow)
    rr(d, (wx, wy, wx + ww, wy + wh), 22, PANEL)
    rr(d, (wx, wy, wx + ww, wy + 64), 22, WHITE)
    d.rectangle((wx, wy + 40, wx + ww, wy + 64), fill=WHITE)
    d.line((wx, wy + 64, wx + ww, wy + 64), fill=BORDER, width=2)

    for i, col in enumerate(((197, 206, 216), (197, 206, 216), (197, 206, 216))):
        cx = wx + 28 + i * 28
        d.ellipse((cx, wy + 24, cx + 14, wy + 42), fill=col)
    d.text((wx + 120, wy + 20), "AI_Project 控制台", font=f_ui, fill=INK)
    d.text((wx + 360, wy + 24), "0.6.13", font=f_small, fill=MUTED)

    rr(d, (wx + ww - 360, wy + 16, wx + ww - 232, wy + 48), 8, GREEN)
    d.text((wx + ww - 338, wy + 20), "GitHub", font=f_small, fill=WHITE)
    rr(d, (wx + ww - 220, wy + 16, wx + ww - 132, wy + 48), 8, ROW, BORDER, 2)
    d.text((wx + ww - 202, wy + 20), "設定", font=f_small, fill=INK)
    rr(d, (wx + ww - 120, wy + 16, wx + ww - 32, wy + 48), 8, ROW, BORDER, 2)
    d.text((wx + ww - 102, wy + 20), "離開", font=f_small, fill=INK)

    # project row
    py = wy + 110
    d.text((wx + 32, py), "專案", font=f_small, fill=MUTED)
    d.text((wx + 90, py - 4), "demo-stack", font=f_ui, fill=INK)
    rr(d, (wx + 280, py - 8, wx + 400, py + 28), 16, ROW, BORDER, 2)
    d.text((wx + 304, py - 2), "main", font=f_small, fill=OK)
    rr(d, (wx + 416, py - 8, wx + 620, py + 28), 8, ROW, BORDER, 2)
    d.text((wx + 436, py - 2), "選擇專案目錄…", font=f_small, fill=INK)
    rr(d, (wx + 636, py - 8, wx + 850, py + 28), 8, ROW, BORDER, 2)
    d.text((wx + 656, py - 2), "從 GitHub 開啟…", font=f_small, fill=INK)

    d.text((wx + 32, py + 52), "Pulse", font=f_small, fill=MUTED)
    d.text((wx + 110, py + 52), "main  ·  乾淨  ·  下一步：發行 Release", font=f_small, fill=INK)

    # toolbar
    ty = wy + 200
    rr(d, (wx + 32, ty, wx + 140, ty + 44), 8, GREEN)
    d.text((wx + 56, ty + 8), "啟動", font=f_ui, fill=WHITE)
    rr(d, (wx + 156, ty, wx + 292, ty + 44), 8, ROW, BORDER, 2)
    d.text((wx + 176, ty + 10), "停止全部", font=f_small, fill=INK)
    rr(d, (wx + 308, ty, wx + 444, ty + 44), 8, ROW, BORDER, 2)
    d.text((wx + 328, ty + 10), "開啟前端", font=f_small, fill=INK)
    rr(d, (wx + 460, ty, wx + 548, ty + 44), 8, ROW, BORDER, 2)
    d.text((wx + 480, ty + 10), "建置", font=f_small, fill=INK)
    rr(d, (wx + 564, ty, wx + 700, ty + 44), 8, ROW, BORDER, 2)
    d.text((wx + 580, ty + 10), "專案問答", font=f_small, fill=INK)

    d.text((wx + 32, ty + 72), "就緒 3 / 4", font=f_ui, fill=INK)
    rr(d, (wx + 200, ty + 66, wx + 360, ty + 106), 8, (255, 246, 229), WAIT, 2)
    d.text((wx + 218, ty + 74), "需重編 1", font=f_small, fill=WAIT)
    d.text((wx + 380, ty + 74), "堆疊正常  ·  先編譯過期項目再啟動", font=f_small, fill=MUTED)

    # services
    sy = wy + 370
    rr(d, (wx + 32, sy, wx + ww - 32, sy + 128), 12, WHITE, BORDER, 2)

    def service(x, name, status, color):
        d.ellipse((x, sy + 50, x + 18, sy + 68), fill=color)
        d.text((x + 32, sy + 34), name, font=f_ui, fill=INK)
        d.text((x + 32, sy + 70), status, font=f_small, fill=color)

    service(wx + 56, "api", "線上  ·  :5080", OK)
    service(wx + 340, "web", "線上  ·  :5173", OK)
    service(wx + 640, "worker", "待命", DOWN)
    service(wx + 940, "admin", "需重編", WAIT)
    d.text((wx + 1260, sy + 34), "Log", font=f_small, fill=MUTED)
    d.text((wx + 1260, sy + 70), "[12:01] listening on http://localhost:5080", font=f_mono, fill=INK)

    img.convert("RGB").save(OUT / "readme-hero.png", "PNG", optimize=True)
    print("wrote", OUT / "readme-hero.png")


def render_steps() -> None:
    w, h = 2400, 320
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    f_h = font(36, True)
    f_b = font(24, False)
    cards = [
        ("1", "下載安裝包", "從 Releases 下載 setup.exe 或 zip。\nWindows 10／11 通常已內建 WebView2。"),
        ("2", "選擇專案目錄", "選本機資料夾，或從 GitHub 開啟。\n控制台會掃描服務與需重編項目。"),
        ("3", "編譯後啟動", "有「需重編」先編譯過期項目。\n再按啟動，然後開啟前端。"),
    ]
    gap = 36
    card_w = (w - gap * 2) // 3
    for i, (num, title, body) in enumerate(cards):
        x = i * (card_w + gap)
        rr(d, (x, 16, x + card_w, h - 16), 28, GREEN)
        d.ellipse((x + 36, 48, x + 84, 96), fill=GOLD)
        d.text((x + 52, 54), num, font=f_h, fill=GREEN_DEEP)
        d.text((x + 108, 54), title, font=f_h, fill=WHITE)
        d.multiline_text((x + 108, 112), body, font=f_b, fill=(215, 238, 230), spacing=8)
        if i < 2:
            ax = x + card_w + 6
            d.polygon([(ax, 150), (ax + 24, 168), (ax, 186)], fill=GOLD)
    img.save(OUT / "readme-steps.png", "PNG", optimize=True)
    print("wrote", OUT / "readme-steps.png")


def render_cta() -> None:
    f = font(30, True)

    def button(text, path, filled: bool, width=420):
        h = 96
        img = Image.new("RGBA", (width, h), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        if filled:
            rr(d, (0, 0, width - 1, h - 1), 18, GREEN)
            d.text((0, 0), text, font=f, fill=WHITE)
            tw = d.textlength(text, font=f)
            img2 = Image.new("RGBA", (width, h), (0, 0, 0, 0))
            d2 = ImageDraw.Draw(img2)
            rr(d2, (0, 0, width - 1, h - 1), 18, GREEN)
            d2.text(((width - tw) / 2, 26), text, font=f, fill=WHITE)
            img2.save(path, "PNG", optimize=True)
        else:
            rr(d, (2, 2, width - 3, h - 3), 18, (244, 251, 248), GREEN_BRIGHT, 5)
            tw = d.textlength(text, font=f)
            d.text(((width - tw) / 2, 26), text, font=f, fill=GREEN)
            img.save(path, "PNG", optimize=True)

    button("下載安裝包", OUT / "readme-cta-download.png", True, 400)
    button("使用文件", OUT / "readme-cta-docs.png", False, 360)
    print("wrote CTAs")


def render_social() -> None:
    """GitHub social preview: 1280x640, under 1 MB."""
    w, h = 1280, 640
    img = Image.new("RGBA", (w, h), GREEN_DEEP + (255,))
    px = img.load()
    for y in range(h):
        ty = y / (h - 1)
        for x in range(w):
            tx = x / (w - 1)
            c1 = lerp(GREEN_DEEP, GREEN, 0.35 + 0.45 * tx)
            c2 = lerp(GREEN, GREEN_MID, ty)
            c = lerp(c1, c2, ty * 0.7)
            glow = max(0.0, 1.0 - ((x - 180) ** 2 + (y - 40) ** 2) ** 0.5 / 520)
            c = lerp(c, GREEN_BRIGHT, glow * 0.22)
            px[x, y] = c + (255,)
    d = ImageDraw.Draw(img)
    logo = load_logo(128)
    img.alpha_composite(logo, (80, 168))
    d.text((236, 176), "AI_Project 控制台", font=font(48, True), fill=WHITE)
    d.text((236, 244), "本機堆疊控制台 · 給軟體公司", font=font(24, True), fill=(215, 245, 234))
    d.text((80, 340), "選專案、編譯、一鍵啟動", font=font(44, True), fill=WHITE)
    d.text((80, 412), "掃描服務與需重編 · GitHub · MCP · 卡住交給本機 Agent", font=font(26, False), fill=(215, 238, 230))

    chips = [("Windows x64", WHITE, (255, 255, 255, 40)), ("公開可見", WHITE, (255, 255, 255, 40)), ("保留一切權利", (243, 226, 168), (212, 160, 23, 72))]
    x = 80
    for text, fg, bg in chips:
        overlay = Image.new("RGBA", img.size, (0, 0, 0, 0))
        od = ImageDraw.Draw(overlay)
        f = font(22, True)
        cw = int(od.textlength(text, font=f)) + 40
        od.rounded_rectangle((x, 488, x + cw, 540), 24, fill=bg)
        img.alpha_composite(overlay)
        ImageDraw.Draw(img).text((x + 20, 498), text, font=f, fill=fg)
        x += cw + 16

    img.convert("RGB").save(OUT / "github-social-preview.png", "PNG", optimize=True)
    print("wrote", OUT / "github-social-preview.png")


if __name__ == "__main__":
    render_hero()
    render_steps()
    render_cta()
    render_social()
