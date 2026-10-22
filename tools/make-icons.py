# Draws the IBDM Pro icon (black tile, gold download arrow into a tray) and every size the app, extension and installer need.
import sys, os
from PIL import Image, ImageDraw, ImageFont
root = sys.argv[1]
S = 1024
GOLD = (212, 175, 55); GOLD2 = (240, 208, 110); DARK1 = (30, 30, 34); DARK2 = (8, 8, 10)

def tile(size=S, circle=False):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    grad = Image.new("RGBA", (S, S))
    gd = ImageDraw.Draw(grad)
    for y in range(S):
        t = y / S
        gd.line([(0, y), (S, y)], fill=tuple(int(DARK1[i] * (1 - t) + DARK2[i] * t) for i in range(3)) + (255,))
    mask = Image.new("L", (S, S), 0)
    md = ImageDraw.Draw(mask)
    if circle: md.ellipse([16, 16, S - 16, S - 16], fill=255)
    else: md.rounded_rectangle([24, 24, S - 24, S - 24], radius=220, fill=255)
    img.paste(grad, (0, 0), mask)
    d = ImageDraw.Draw(img)
    if circle: d.ellipse([16, 16, S - 16, S - 16], outline=GOLD, width=22)
    else: d.rounded_rectangle([24, 24, S - 24, S - 24], radius=220, outline=GOLD + (255,), width=22)
    # arrow
    cx = S // 2
    d.rounded_rectangle([cx - 70, 200, cx + 70, 560], radius=30, fill=GOLD2)
    d.polygon([(cx - 230, 500), (cx + 230, 500), (cx, 740)], fill=GOLD2)
    # tray
    d.rounded_rectangle([cx - 300, 770, cx + 300, 840], radius=35, fill=GOLD)
    d.rounded_rectangle([cx - 300, 640, cx - 230, 840], radius=35, fill=GOLD)
    d.rounded_rectangle([cx + 230, 640, cx + 300, 840], radius=35, fill=GOLD)
    return img.resize((size, size), Image.LANCZOS)

big = tile()
os.makedirs(f"{root}/assets", exist_ok=True)
big.resize((256, 256), Image.LANCZOS).save(f"{root}/assets/ibdm-256.png")
big.resize((512, 512), Image.LANCZOS).save(f"{root}/assets/ibdm-512.png")
big.save(f"{root}/assets/ibdm.ico", sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])
for n in (16, 32, 48, 128):
    big.resize((n, n), Image.LANCZOS).save(f"{root}/extension/icons/icon{n}.png")

# Inno Setup wizard pictures (100/150/200% DPI)
def side(w, h):
    img = Image.new("RGB", (w, h), DARK2)
    d = ImageDraw.Draw(img)
    for y in range(h):
        t = y / h
        d.line([(0, y), (w, y)], fill=tuple(int(DARK1[i] * (1 - t) + DARK2[i] * t) for i in range(3)))
    logo = big.resize((int(w * 0.62), int(w * 0.62)), Image.LANCZOS)
    img.paste(logo, ((w - logo.width) // 2, int(h * 0.28)), logo)
    d.rectangle([0, h - int(h * 0.012), w, h], fill=GOLD)
    return img
for scale, (w, h) in {100: (164, 314), 150: (246, 459), 200: (328, 628)}.items():
    side(w, h).save(f"{root}/installer/wizard-side-{scale}.bmp")
for scale, n in {100: 55, 150: 83, 200: 110}.items():
    img = Image.new("RGB", (n, n), (255, 255, 255))
    logo = big.resize((n, n), Image.LANCZOS)
    img.paste(logo, (0, 0), logo)
    img.save(f"{root}/installer/wizard-corner-{scale}.bmp")
print("ok")
