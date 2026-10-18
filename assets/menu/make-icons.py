# Icones du menu F10 et des petits panneaux (UI/Kit.cs) : traits blancs sur fond transparent, 64x64, dessines en 256 puis
# reduits (lissage). Le mod les teinte (GUI.color). Relancer : python -X utf8 -I assets/menu/make-icons.py assets/menu
import sys, math
from PIL import Image, ImageDraw

out = sys.argv[1]
S = 256
W = 20
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

def ring(d, cx, cy, r, w=W):
    d.ellipse((cx - r, cy - r, cx + r, cy + r), outline=WHITE, width=w)

def save(im, name):
    im.resize((64, 64), Image.LANCZOS).save(f"{out}/{name}.png")
    print("ok", name)

# joueurs : deux bustes
im, d = canvas()
ring(d, 100, 84, 40)
line(d, arc_pts(100, 214, 72, 200, 340))
ring(d, 178, 92, 30, 18)
line(d, arc_pts(186, 206, 54, 250, 340), 18)
save(im, "joueurs")

# tchat : bulle
im, d = canvas()
line(d, [(50, 52), (206, 52), (214, 60), (214, 160), (206, 168), (110, 168), (64, 210), (70, 168), (50, 168), (42, 160), (42, 60), (50, 52)])
save(im, "tchat")

# course : drapeau sur un mat
im, d = canvas()
line(d, [(62, 34), (62, 226)])
line(d, [(62, 44), (108, 30), (150, 52), (200, 38), (200, 138), (150, 152), (108, 130), (62, 144)])
save(im, "course")

# apparence : tee-shirt
im, d = canvas()
line(d, [(96, 40), (128, 62), (160, 40), (222, 70), (204, 118), (178, 108), (178, 220), (78, 220), (78, 108), (52, 118), (34, 70), (96, 40)])
save(im, "apparence")

# hud : cadran et aiguille
im, d = canvas()
line(d, arc_pts(128, 156, 96, 180, 360))
line(d, [(128, 156), (182, 98)])
d.ellipse((114, 142, 142, 170), fill=WHITE)
save(im, "hud")

# graphismes : ecran et pied
im, d = canvas()
line(d, [(34, 48), (222, 48), (222, 176), (34, 176), (34, 48)])
line(d, [(128, 178), (128, 214)])
line(d, [(84, 218), (172, 218)])
save(im, "graphismes")

# synchro : fleche circulaire
im, d = canvas()
line(d, arc_pts(128, 132, 84, -40, 250))
line(d, [(196, 30), (200, 82), (148, 84)])
save(im, "synchro")

# aller : fleche vers la droite
im, d = canvas()
line(d, [(44, 128), (206, 128)])
line(d, [(140, 62), (206, 128), (140, 194)])
save(im, "aller")

# venir : fleche vers la gauche
im, d = canvas()
line(d, [(212, 128), (50, 128)])
line(d, [(116, 62), (50, 128), (116, 194)])
save(im, "venir")

# exclure / fermer : croix
im, d = canvas()
line(d, [(62, 62), (194, 194)], 24)
line(d, [(194, 62), (62, 194)], 24)
save(im, "croix")

# inviter : buste et plus
im, d = canvas()
ring(d, 104, 86, 42)
line(d, arc_pts(104, 220, 76, 200, 340))
line(d, [(206, 92), (206, 168)])
line(d, [(168, 130), (244, 130)])
save(im, "inviter")

# suivant : chevron
im, d = canvas()
line(d, [(98, 52), (174, 128), (98, 204)], 24)
save(im, "suivant")
