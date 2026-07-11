# -*- coding: utf-8 -*-
"""Build a quick visual test sheet from latest card renders."""
from pathlib import Path
import json
from PIL import Image, ImageDraw, ImageFont

out = Path(__file__).resolve().parents[1] / "output" / "from_cards_textured"
names = ["Kawamoto_Nanako", "Aragaki_Yoko", "Kana"]
th, rh, pad = 200, 420, 12
cols = []

try:
    font = ImageFont.truetype("arial.ttf", 16)
    font_s = ImageFont.truetype("arial.ttf", 13)
    font_t = ImageFont.truetype("arial.ttf", 18)
except OSError:
    font = font_s = font_t = ImageFont.load_default()

for name in names:
    meta = json.loads((out / f"{name}_meta.json").read_text(encoding="utf-8"))
    imgs = []
    for p, h in ((out / f"{name}_card.png", th), (out / f"{name}_front.png", rh), (out / f"{name}_side.png", rh)):
        im = Image.open(p).convert("RGB")
        w = int(im.width * h / im.height)
        imgs.append(im.resize((w, h), Image.Resampling.LANCZOS))
    max_w = max(i.width for i in imgs)
    gap = 8
    title_h = 52
    total_h = title_h + sum(i.height for i in imgs) + gap * (len(imgs) + 1)
    col = Image.new("RGB", (max_w + pad * 2, total_h), (40, 42, 46))
    draw = ImageDraw.Draw(col)
    mk = meta.get("makeup") or {}
    title = (
        f"{name}\n"
        f"head={meta.get('headId')} skin={meta.get('skinId')} "
        f"lip={mk.get('lipId')} cheek={mk.get('cheekId')} shadow={mk.get('eyeshadowId')}"
    )
    draw.multiline_text((pad, 6), title, fill=(230, 230, 230), font=font_s)
    y = title_h
    for label, im in zip(("card", "front", "side"), imgs):
        x = pad + (max_w - im.width) // 2
        col.paste(im, (x, y))
        draw.text((pad, y + 2), label, fill=(255, 220, 80), font=font_s)
        y += im.height + gap
    cols.append(col)

H = max(c.height for c in cols)
W = sum(c.width for c in cols) + pad * (len(cols) + 1)
sheet = Image.new("RGB", (W, H + 36), (28, 30, 34))
draw = ImageDraw.Draw(sheet)
draw.text(
    (pad, 8),
    "face_min_render test — makeup AddTex + fo_head native eyes (no retarget)",
    fill=(200, 210, 220),
    font=font_t,
)
x = pad
for c in cols:
    sheet.paste(c, (x, 36))
    x += c.width + pad

dest = out / "TEST_sheet_three_cards.png"
sheet.save(dest)
print(dest)
print("size", sheet.size)
