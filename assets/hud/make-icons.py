# Icones du HUD MWCoop (UI/NewHud.cs) : traits blancs sur fond transparent, 64x64, dessines en 256 puis reduits
# (lissage). Le mod les teinte (GUI.color). Relancer : python -X utf8 -I assets/hud/make-icons.py assets/hud
import sys, math
from PIL import Image, ImageDraw

out = sys.argv[1]
S = 256          # toile de travail
W = 20           # epaisseur du trait (toile 256)
WHITE = (255, 255, 255, 255)

def canvas():
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    return im, ImageDraw.Draw(im)

def line(d, pts, w=W):
    d.line(pts, fill=WHITE, width=w, joint="curve")
    for p in (pts[0], pts[-1]):
        d.ellipse((p[0] - w / 2, p[1] - w / 2, p[0] + w / 2, p[1] + w / 2), fill=WHITE)

def arc_pts(cx, cy, r, a0, a1, n=48):
    return [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * i / n)), cy + r * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)]

def drop(d, cx, top, r, w=W):
    # goutte : pointe en haut, ventre rond en bas
    body = arc_pts(cx, top + 2.2 * r, r, -30, 210)
    line(d, [(cx, top)] + body + [(cx, top)], w)

def save(im, name):
    im.resize((64, 64), Image.LANCZOS).save(f"{out}/{name}.png")
    print("ok", name)

# soif : verre
im, d = canvas()
line(d, [(72, 52), (90, 214), (166, 214), (184, 52), (72, 52)])
line(d, [(82, 118), (174, 118)])
save(im, "soif")

# probleme (alcool) : bouteille
im, d = canvas()
line(d, [(108, 30), (148, 30), (148, 84), (176, 120), (176, 222), (80, 222), (80, 120), (108, 84), (108, 30)])
line(d, [(80, 150), (176, 150)])
save(im, "probleme")

# faim : fourchette et couteau
im, d = canvas()
line(d, [(76, 36), (76, 110)]); line(d, [(108, 36), (108, 110)]); line(d, [(140, 36), (140, 110)])
line(d, [(76, 110), (140, 110)]); line(d, [(108, 110), (108, 222)])
line(d, [(196, 36), (172, 70), (172, 140), (196, 140), (196, 36)]); line(d, [(196, 140), (196, 222)])
save(im, "faim")

# stress : eclair
im, d = canvas()
line(d, [(146, 24), (66, 140), (126, 140), (110, 232), (190, 112), (130, 112), (146, 24)])
save(im, "stress")

# urine : goutte
im, d = canvas()
drop(d, 128, 30, 70)
save(im, "urine")

# fatigue : lune
im, d = canvas()
d.ellipse((32, 32, 224, 224), fill=WHITE)
d.ellipse((92, 8, 252, 168), fill=(0, 0, 0, 0))   # (croissant : disque moins un disque decale en haut a droite)
save(im, "fatigue")

# salete : taches
im, d = canvas()
for (cx, cy, r) in ((92, 150, 52), (170, 112, 40), (172, 190, 26)):
    d.ellipse((cx - r, cy - r, cx + r, cy + r), outline=WHITE, width=W)
save(im, "salete")

# temperature : thermometre
im, d = canvas()
line(d, [(108, 160), (108, 50)] + arc_pts(128, 50, 20, 180, 360) + [(148, 160)])
pts = arc_pts(128, 186, 40, -60, 240)
line(d, pts)
d.ellipse((108, 166, 148, 206), fill=WHITE)
line(d, [(128, 186), (128, 92)], 16)
save(im, "temperature")

# sueur : deux gouttes
im, d = canvas()
drop(d, 92, 40, 46)
drop(d, 176, 104, 34)
save(im, "sueur")

# argent : billet
im, d = canvas()
d.rounded_rectangle((28, 70, 228, 186), radius=22, outline=WHITE, width=W)
d.ellipse((100, 100, 156, 156), outline=WHITE, width=W)
save(im, "argent")

# jour : calendrier
im, d = canvas()
d.rounded_rectangle((36, 56, 220, 224), radius=24, outline=WHITE, width=W)
line(d, [(36, 106), (220, 106)]); line(d, [(84, 30), (84, 76)]); line(d, [(172, 30), (172, 76)])
save(im, "jour")

# prison : barreaux
im, d = canvas()
for x in (70, 128, 186):
    line(d, [(x, 36), (x, 220)])
line(d, [(40, 96), (216, 96)])
save(im, "prison")

# fleches de tendance (temperature)
im, d = canvas()
line(d, [(128, 210), (128, 50)]); line(d, [(72, 106), (128, 50), (184, 106)])
save(im, "monte")
im, d = canvas()
line(d, [(128, 46), (128, 206)]); line(d, [(72, 150), (128, 206), (184, 150)])
save(im, "descend")
