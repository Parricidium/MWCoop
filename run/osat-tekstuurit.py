# Textures du magasin de pieces CARDINAL VARAOSAT (assets\osat\*.png, integrees a la DLL par build.cmd) : enseigne du toit,
# panneaux des deux ailes (pieces des mods de Cardinal ; pieces d'occasion de la Rivett), panneau de retrait des achats.
# Tout est dessine ici (aucune image d'ailleurs).
#   python -X utf8 run\osat-tekstuurit.py
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageChops

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'assets', 'osat')
os.makedirs(OUT, exist_ok=True)
F = r'C:\Windows\Fonts'
def font(name, size): return ImageFont.truetype(os.path.join(F, name), size)
IMPACT, BAHN, ARIAL = 'impact.ttf', 'bahnschrift.ttf', 'arialbd.ttf'

def text_center(d, xy, s, f, fill, **kw):
    b = d.textbbox((0, 0), s, font=f, **kw)
    d.text((xy[0] - (b[2] + b[0]) / 2, xy[1] - (b[3] + b[1]) / 2), s, font=f, fill=fill, **kw)

def glow(layer, radius, strength=2):
    g = layer.filter(ImageFilter.GaussianBlur(radius))
    out = g
    for _ in range(strength - 1): out = ImageChops.add(out, g)
    return out

def save(im, name):
    im.convert('RGB').save(os.path.join(OUT, name))

# enseigne au-dessus de la porte (2048 x 512), JD 02/11 « dans le style du jeu comme Teimo » : tole peinte en blanc passe (joints
# verticaux, rivets, bords roules, coulures et salissures), lettres peintes a plat comme PUB NAPPO / FLEETARIN (store_banners) --
# CARDINAL rouge (Arial Black), VARAOSAT bleu a empattements (Rockwell Extra Bold), ligne du bas gris fonce ; peinture usee
import random
def noise(w, h, scale, seed):
    r = random.Random(seed)
    small = Image.new('L', (max(2, w // scale), max(2, h // scale)))
    small.putdata([r.randint(0, 255) for _ in range(small.size[0] * small.size[1])])
    return small.resize((w, h), Image.BICUBIC)

def worn(w, h, seed, keep=0.86):
    # masque d'usure de la peinture : bruit fin + taches, blanc = peinture tenue
    n = ImageChops.add(noise(w, h, 3, seed).point(lambda v: v // 2), noise(w, h, 40, seed + 1).point(lambda v: v // 2))
    cut = int(255 * (1 - keep))
    return n.point(lambda v: 0 if v < cut else 255).filter(ImageFilter.GaussianBlur(0.8))

def paint(im, xy, s, f, col, seed, **kw):
    w, h = im.size
    m = Image.new('L', (w, h), 0)
    text_center(ImageDraw.Draw(m), xy, s, f, 255, **kw)
    m = ImageChops.multiply(m, worn(w, h, seed))
    im.paste(Image.new('RGB', (w, h), col), (0, 0), m)

def kyltti():
    w, h = 2048, 512
    r = random.Random(21)
    base = Image.new('RGB', (w, h), (232, 230, 223))
    # fond : blanc passe, grandes taches et grain
    tone = ImageChops.add(noise(w, h, 90, 3).point(lambda v: v // 10), noise(w, h, 3, 4).point(lambda v: v // 22))
    im = ImageChops.subtract(base, Image.merge('RGB', (tone, tone, tone)))
    d = ImageDraw.Draw(im)
    # coulures de rouille et de crasse sous les joints et le bord haut
    streak = Image.new('L', (w, h), 0); ds = ImageDraw.Draw(streak)
    for _ in range(70):
        x = r.randint(20, w - 20); y0 = r.choice([24, 24, r.randint(30, h - 60)]); ln = r.randint(30, 200)
        ds.line([x, y0, x + r.randint(-3, 3), y0 + ln], fill=r.randint(20, 60), width=r.randint(2, 7))
    streak = streak.filter(ImageFilter.GaussianBlur(3))
    im.paste(Image.new('RGB', (w, h), (110, 86, 60)), (0, 0), streak)
    # joints verticaux des toles et rivets
    d = ImageDraw.Draw(im)
    for x in (683, 1365):
        d.line([x, 18, x, h - 18], fill=(150, 148, 140), width=5)
        d.line([x + 4, 18, x + 4, h - 18], fill=(236, 234, 228), width=2)
    for x in [40] + [v + dx for v in (683, 1365) for dx in (-22, 26)] + [w - 40]:
        for y in (44, h // 2, h - 44):
            d.ellipse([x - 7, y - 7, x + 7, y + 7], fill=(92, 90, 86)); d.ellipse([x - 4, y - 5, x + 2, y + 1], fill=(170, 168, 160))
    # bords roules sombres
    d.rectangle([0, 0, w - 1, h - 1], outline=(70, 68, 64), width=14)
    d.rectangle([14, 14, w - 15, h - 15], outline=(160, 158, 150), width=3)
    # lettres peintes
    paint(im, (w * 0.5, 178), 'CARDINAL', font('ariblk.ttf', 250), (176, 32, 28), 11)
    paint(im, (w * 0.5, 352), 'VARAOSAT', font('ROCKEB.TTF', 128), (34, 88, 160), 12, spacing=0)
    paint(im, (w * 0.5, 450), 'AUTO · MOTO · KÄYTETYT OSAT', font('FRAMDCN.TTF', 62), (58, 58, 60), 13)
    paint(im, (w - 104, h - 52), 's21', font(ARIAL, 30), (110, 108, 104), 14)
    # salissure generale par-dessus la peinture (bas plus sale)
    dirt = Image.linear_gradient('L').resize((w, h)).point(lambda v: v // 12)
    dirt = ImageChops.multiply(dirt, noise(w, h, 60, 5).point(lambda v: min(255, v * 2)))
    im = ImageChops.subtract(im, Image.merge('RGB', (dirt, dirt, dirt)))
    save(im, 'osat-kyltti.png')

# panneau suspendu d'une aile (1024 x 256)
def siipi(name, top, bottom, col):
    w, h = 1024, 256
    im = Image.new('RGB', (w, h), col)
    d = ImageDraw.Draw(im)
    d.rectangle([6, 6, w - 7, h - 7], outline=(255, 255, 255), width=6)
    text_center(d, (w / 2, 100), top, font(IMPACT, 110), (255, 255, 255))
    text_center(d, (w / 2, 200), bottom, font(BAHN, 50), (235, 235, 235))
    save(im, name)

# retrait des achats (512 x 256)
def nouto():
    w, h = 512, 256
    im = Image.new('RGB', (w, h), (250, 205, 40))
    d = ImageDraw.Draw(im)
    d.rectangle([6, 6, w - 7, h - 7], outline=(20, 20, 20), width=6)
    text_center(d, (w / 2, 95), 'NOUTO', font(IMPACT, 110), (20, 20, 20))
    text_center(d, (w / 2, 195), 'RETRAIT · PICK-UP', font(BAHN, 40), (20, 20, 20))
    save(im, name := 'osat-nouto.png')

# haut du vendeur (calque 512 x 512 sur la texture de torse du jeu, meme gabarit que assets\kauppa\myyja-paita.png : les
# zones transparentes -- les mains -- sont reprises de ce calque) : polo rouge CARDINAL, bandes noires, dos CARDINAL VARAOSAT
def paita():
    base = Image.open(os.path.join(OUT, '..', 'kauppa', 'myyja-paita.png')).convert('RGBA')
    w, h = base.size
    im = Image.new('RGBA', (w, h), (168, 24, 22, 255))
    d = ImageDraw.Draw(im)
    for y in (318, 468): d.rectangle([0, y, w, y + 14], fill=(18, 18, 20, 255))   # (bandes du col et de la taille)
    # dos
    text_center(d, (398, 205), 'CARDINAL', font(IMPACT, 38), (255, 255, 255, 255))
    text_center(d, (398, 245), 'VARAOSAT', font(BAHN, 24), (20, 20, 22, 255))
    # poitrine : etoile blanche + CARDINAL, badge MYYJA
    text_center(d, (165, 354), 'CARDINAL', font(IMPACT, 24), (255, 255, 255, 255))
    d.rounded_rectangle([222, 318 + 22, 302, 318 + 44], radius=4, fill=(245, 240, 230, 255), outline=(20, 20, 20, 255), width=2)
    text_center(d, (262, 351), 'MYYJÄ', font(ARIAL, 14), (150, 20, 20, 255))
    im.putalpha(base.split()[3])
    im.save(os.path.join(OUT, 'osat-paita.png'))

kyltti()
paita()
siipi('osat-cardinal.png', 'CARDINAL', 'MODIOSAT · MOD PARTS', (150, 22, 22))
siipi('osat-kaytetyt.png', 'KÄYTETYT OSAT', 'RIVETT · USED PARTS', (24, 60, 120))
siipi('osat-satsuma.png', 'SATSUMA OSAT', 'SATSUMA · PARTS', (24, 60, 120))   # (My Summer Car)
nouto()
print('ok', sorted(os.listdir(OUT)))
