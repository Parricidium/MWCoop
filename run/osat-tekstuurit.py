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

# enseigne du toit (2048 x 512) : CARDINAL en rouge lumineux, VARAOSAT en blanc, bandeau AUTO · MOTO, petit s21
def kyltti():
    w, h = 2048, 512
    im = Image.new('RGB', (w, h), (14, 16, 20))
    d = ImageDraw.Draw(im)
    d.rectangle([10, 10, w - 11, h - 11], outline=(70, 74, 82), width=8)
    lay = Image.new('RGB', (w, h), (0, 0, 0))
    dl = ImageDraw.Draw(lay)
    text_center(dl, (w * 0.5, 175), 'CARDINAL', font(IMPACT, 250), (235, 40, 30))
    im = ImageChops.add(im, glow(lay, 16, 1))
    d = ImageDraw.Draw(im)
    text_center(d, (w * 0.5, 175), 'CARDINAL', font(IMPACT, 250), (255, 88, 70))   # (coeur net du neon, sur le halo)
    text_center(d, (w * 0.5, 345), 'V A R A O S A T', font(BAHN, 110), (245, 245, 240))
    d.rectangle([300, 420, w - 300, 470], fill=(235, 40, 30))
    text_center(d, (w * 0.5, 445), 'AUTO  ·  MOTO  ·  KÄYTETYT OSAT', font(ARIAL, 38), (255, 255, 255))
    text_center(d, (w - 80, h - 50), 's21', font(ARIAL, 34), (150, 150, 150))
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
nouto()
print('ok', sorted(os.listdir(OUT)))
