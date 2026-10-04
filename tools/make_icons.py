"""Icône d'Airsoft Planner : réticule blanc et point orange sur fond vert olive avec courbes de niveau.

Produit l'icône Windows (.ico multi-tailles) et les icônes Android (classique, adaptative, notification).
Usage : python tools/make_icons.py  (depuis la racine du dépôt ; nécessite Pillow)
"""
import math
import os
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
S = 1024  # dessin en grand, réduit ensuite
GREEN_TOP, GREEN_BOTTOM = (70, 104, 62), (30, 52, 38)
ORANGE = (245, 124, 0)


def background(size=S, rounded=True):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    grad = Image.new("RGBA", (size, size))
    d = ImageDraw.Draw(grad)
    for y in range(size):
        t = y / (size - 1)
        c = tuple(int(GREEN_TOP[i] * (1 - t) + GREEN_BOTTOM[i] * t) for i in range(3))
        d.line([(0, y), (size, y)], fill=c + (255,))
    # Courbes de niveau (carte topographique), discrètes.
    contours = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    dc = ImageDraw.Draw(contours)
    cx, cy = size * 0.30, size * 0.72
    for k in range(1, 9):
        r = size * 0.11 * k
        pts = []
        for a in range(0, 361, 4):
            rad = math.radians(a)
            wobble = 1 + 0.08 * math.sin(3 * rad + k) + 0.05 * math.cos(5 * rad - k)
            pts.append((cx + r * wobble * math.cos(rad) * 1.25, cy + r * wobble * math.sin(rad)))
        dc.line(pts, fill=(200, 230, 190, 46), width=max(2, size // 180))
    grad = Image.alpha_composite(grad, contours)
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], radius=int(size * 0.22) if rounded else 0, fill=255)
    img.paste(grad, (0, 0), mask)
    return img


def reticle(size=S, scale=1.0, color=(255, 255, 255, 255), dot=True):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = size / 2
    r = size * 0.30 * scale
    w = max(2, int(size * 0.052 * scale))
    d.ellipse([c - r, c - r, c + r, c + r], outline=color, width=w)
    # Branches du réticule : de l'extérieur vers le centre, avec un vide au milieu.
    outer, inner = size * 0.42 * scale, size * 0.13 * scale
    for dx, dy in [(1, 0), (-1, 0), (0, 1), (0, -1)]:
        d.line([(c + dx * inner, c + dy * inner), (c + dx * outer, c + dy * outer)], fill=color, width=w)
    if dot:
        dr = size * 0.065 * scale
        d.ellipse([c - dr, c - dr, c + dr, c + dr], fill=ORANGE + (255,))
    return img


def full_icon(size=S):
    img = background(size)
    shadow = reticle(size, color=(0, 0, 0, 110), dot=False).filter(ImageFilter.GaussianBlur(size / 90))
    img = Image.alpha_composite(img, Image.new("RGBA", img.size, (0, 0, 0, 0)))
    img.alpha_composite(shadow, (int(size * 0.008), int(size * 0.014)))
    return Image.alpha_composite(img, reticle(size))


def save_resized(img, path, px):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.resize((px, px), Image.LANCZOS).save(path)


def main():
    icon = full_icon()
    # Windows : .ico multi-tailles + PNG pour la documentation.
    ico = os.path.join(ROOT, "src", "AirsoftPlanner.App", "Assets", "airsoftplanner.ico")
    icon.save(ico, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    save_resized(icon, os.path.join(ROOT, "src", "AirsoftPlanner.App", "Assets", "airsoftplanner-256.png"), 256)
    save_resized(icon, os.path.join(ROOT, "docs", "icone.png"), 512)

    # Android : icône classique, adaptative (fond + premier plan dans la zone sûre), notification monochrome.
    res = os.path.join(ROOT, "src", "AirsoftPlanner.Mobile", "Resources")
    densities = {"mdpi": 1, "hdpi": 1.5, "xhdpi": 2, "xxhdpi": 3, "xxxhdpi": 4}
    adaptive_bg = background(S, rounded=False)
    adaptive_fg = reticle(S, scale=0.62)
    notification = reticle(S, scale=1.1, color=(255, 255, 255, 255), dot=False)
    for name, factor in densities.items():
        save_resized(icon, os.path.join(res, f"mipmap-{name}", "appicon.png"), int(48 * factor))
        save_resized(adaptive_bg, os.path.join(res, f"mipmap-{name}", "appicon_background.png"), int(108 * factor))
        save_resized(adaptive_fg, os.path.join(res, f"mipmap-{name}", "appicon_foreground.png"), int(108 * factor))
        save_resized(notification, os.path.join(res, f"drawable-{name}", "ic_notification.png"), int(24 * factor))
    print("Icônes générées.")


if __name__ == "__main__":
    main()
