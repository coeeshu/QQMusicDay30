"""Draws the app icon (a green calendar page with "30") into src/QQDay30/Assets/app.ico.

    python tools/make_icon.py
"""
import os
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "src", "QQDay30", "Assets")
FONT = next(p for p in (r"C:\Windows\Fonts\seguibl.ttf", r"C:\Windows\Fonts\arialbd.ttf") if os.path.exists(p))

TOP, BOTTOM = (0x45, 0xDE, 0x97), (0x0F, 0x9A, 0x58)


def gradient(size):
    """Diagonal top-left -> bottom-right gradient."""
    g = Image.new("RGB", (size, size))
    px = g.load()
    for y in range(size):
        for x in range(size):
            t = (x + y) / (2 * (size - 1))
            px[x, y] = tuple(round(a + (b - a) * t) for a, b in zip(TOP, BOTTOM))
    return g


def draw(size, simple):
    s = 1024
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    m = 24 if simple else 48
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle((m, m, s - m, s - m), radius=230, fill=255)
    img.paste(gradient(256).resize((s, s), Image.BICUBIC), (0, 0), mask)
    d = ImageDraw.Draw(img)

    if not simple:
        # calendar header: a soft band and two binding rings
        band = Image.new("RGBA", (s, s), (0, 0, 0, 0))
        ImageDraw.Draw(band).rounded_rectangle((m, m, s - m, 300), radius=230, fill=(0, 0, 0, 38))
        ImageDraw.Draw(band).rectangle((m, 200, s - m, 300), fill=(0, 0, 0, 38))
        img.alpha_composite(Image.composite(band, Image.new("RGBA", (s, s)), mask))
        d = ImageDraw.Draw(img)
        for cx in (330, 694):
            d.rounded_rectangle((cx - 34, 120, cx + 34, 270), radius=34, fill="white")

    text = "30"
    font = ImageFont.truetype(FONT, 600 if simple else 500)
    box = d.textbbox((0, 0), text, font=font)
    w, h = box[2] - box[0], box[3] - box[1]
    cy = 512 if simple else 640
    d.text(((s - w) / 2 - box[0], cy - h / 2 - box[1]), text, font=font, fill="white")
    return img.resize((size, size), Image.LANCZOS)


def main():
    os.makedirs(OUT, exist_ok=True)
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    frames = [draw(n, simple=n <= 32) for n in sizes]
    frames[-1].save(os.path.join(OUT, "app.ico"), sizes=[(n, n) for n in sizes], append_images=frames[:-1])
    draw(512, simple=False).save(os.path.join(OUT, "app.png"))
    print("written", OUT)


if __name__ == "__main__":
    main()
