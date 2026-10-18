"""Plaques de numero de la VAZ-2108 (une par couleur de joueur, 1 a 16) : carre blanc arrondi a bord noir, chiffres noirs,
marge transparente (le decalque des portieres sort du carre : texture en Clamp, decoupe a l'alpha).

    python -I run/vaz-numeros.py <dossier de sortie>      -> numero-01.png ... numero-16.png
"""
import os, sys
from PIL import Image, ImageDraw, ImageFont

out = sys.argv[1]
os.makedirs(out, exist_ok=True)
S, M, B = 256, 12, 9   # taille, marge transparente, epaisseur du bord
font = None
for f in ("C:/Windows/Fonts/bahnschrift.ttf", "C:/Windows/Fonts/arialbd.ttf"):
    if os.path.exists(f):
        font = ImageFont.truetype(f, 150)
        try: font.set_variation_by_name("Bold")
        except Exception: pass
        break
for n in range(1, 17):
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle((M, M, S - 1 - M, S - 1 - M), radius=26, fill=(0, 0, 0, 255))
    d.rounded_rectangle((M + B, M + B, S - 1 - M - B, S - 1 - M - B), radius=18, fill=(245, 245, 242, 255))
    t = str(n)
    x0, y0, x1, y1 = d.textbbox((0, 0), t, font=font)
    d.text(((S - (x1 - x0)) / 2 - x0, (S - (y1 - y0)) / 2 - y0), t, font=font, fill=(12, 12, 12, 255))
    im.save(os.path.join(out, "numero-%02d.png" % n))
print("16 plaques dans", out)
