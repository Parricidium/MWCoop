# Textures du cinema KINO TAHTI (assets\kino\*.png, integrees a la DLL par build.cmd) : enseigne lumineuse, affiches de films
# inventes (style annees 80), tableau des prix, panneaux des salles. Tout est dessine ici (aucune image d'ailleurs).
#   python -X utf8 run\kino-tekstuurit.py
import math, os, random
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageChops

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'assets', 'kino')
os.makedirs(OUT, exist_ok=True)
F = r'C:\Windows\Fonts'
def font(name, size): return ImageFont.truetype(os.path.join(F, name), size)
BAUHAUS, IMPACT, BAHN, GEORGIA, ARIAL, SEGOE = 'BAUHS93.TTF', 'impact.ttf', 'bahnschrift.ttf', 'georgiab.ttf', 'arialbd.ttf', 'segoeuib.ttf'

def text_center(d, xy, s, f, fill, **kw):
    b = d.textbbox((0, 0), s, font=f, **kw)
    d.text((xy[0] - (b[2] + b[0]) / 2, xy[1] - (b[3] + b[1]) / 2), s, font=f, fill=fill, **kw)

def glow(layer, radius, strength=2):
    g = layer.filter(ImageFilter.GaussianBlur(radius))
    out = g
    for _ in range(strength - 1): out = ImageChops.add(out, g)
    return out

def star(cx, cy, r1, r2, n=5, rot=-90):
    pts = []
    for i in range(n * 2):
        r = r1 if i % 2 == 0 else r2
        a = math.radians(rot + i * 180 / n)
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts

def gradient(w, h, top, bottom):
    im = Image.new('RGB', (w, h))
    d = ImageDraw.Draw(im)
    for y in range(h):
        t = y / (h - 1)
        d.line([(0, y), (w, y)], fill=tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)))
    return im

def save(im, name):
    im.convert('RGB').save(os.path.join(OUT, name))
    print(name, im.size)

# ---------------------------------------------------------------- enseigne
def kyltti():
    W, H = 2048, 512
    base = gradient(W, H, (14, 12, 30), (6, 6, 16))
    d = ImageDraw.Draw(base)
    d.rectangle([10, 10, W - 11, H - 11], outline=(60, 60, 80), width=6)
    neon = Image.new('RGB', (W, H), (0, 0, 0))
    n = ImageDraw.Draw(neon)
    # etoile a gauche
    n.polygon(star(230, 240, 165, 68), outline=(255, 214, 80), width=12)
    n.polygon(star(230, 240, 112, 46), fill=(255, 190, 40))
    # KINO en rouge (Bauhaus), TAHTI en blanc chaud (Impact) ; tubes nets, halo doux
    text_center(n, (760, 236), 'KINO', font(BAUHAUS, 250), (255, 50, 60))
    text_center(n, (1500, 236), 'TÄHTI', font(IMPACT, 270), (255, 236, 200))
    # filet et petite marque s21
    n.line([(470, 440), (1880, 440)], fill=(255, 50, 60), width=8)
    text_center(n, (1930, 446), 's21', font(GEORGIA, 40), (255, 214, 80))
    halo = Image.eval(glow(neon, 26, 1), lambda v: int(v * 0.9))
    out = ImageChops.add(base, halo)
    out = ImageChops.add(out, Image.eval(glow(neon, 7, 1), lambda v: int(v * 0.6)))
    out = ImageChops.lighter(out, neon)
    save(out, 'kino-kyltti.png')

# ---------------------------------------------------------------- affiches (films inventes)
def poster(i, title, tag, palette, art, rating, sub):
    W, H = 512, 720
    im = gradient(W, H, palette[0], palette[1])
    d = ImageDraw.Draw(im)
    rnd = random.Random(i)
    art(im, d, rnd)
    # bandeau du titre
    tf = font(IMPACT, 84 if len(title) <= 10 else 64 if len(title) <= 15 else 52)
    txt = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    t = ImageDraw.Draw(txt)
    text_center(t, (W / 2 + 4, 104), title, tf, (0, 0, 0, 200))
    text_center(t, (W / 2, 100), title, tf, palette[2] + (255,))
    text_center(t, (W / 2, 168), tag, font(GEORGIA, 24), (255, 255, 255, 230))
    im.paste(txt, (0, 0), txt)
    d = ImageDraw.Draw(im)
    # pied : distribution inventee, ensi-ilta, classement
    d.rectangle([0, H - 120, W, H], fill=(10, 10, 14))
    text_center(d, (W / 2, H - 92), sub, font(BAHN, 18), (200, 200, 205))
    text_center(d, (W / 2, H - 58), 'ENSI-ILTA KINO TÄHDESSÄ', font(IMPACT, 30), palette[2])
    text_center(d, (W / 2, H - 24), 'SUOMI-FILMI 1999  ·  ' + rating, font(BAHN, 16), (150, 150, 160))
    d.rectangle([0, 0, W - 1, H - 1], outline=(255, 255, 255), width=4)
    save(im, 'kino-juliste%d.png' % i)

def art_lumimyrsky(im, d, rnd):
    # montagnes enneigees, flocons, silhouette
    for k, (col, base) in enumerate([((70, 90, 140), 470), ((150, 170, 210), 540)]):
        pts = [(0, 720)]
        x = 0
        while x <= 512:
            pts.append((x, base - rnd.randint(0, 160) + k * 40)); x += rnd.randint(40, 90)
        pts.append((512, 720))
        d.polygon(pts, fill=col)
    d.ellipse([230, 420, 262, 452], fill=(15, 15, 25))
    d.polygon([(226, 452), (266, 452), (276, 580), (216, 580)], fill=(15, 15, 25))
    for _ in range(260):
        x, y, r = rnd.randint(0, 512), rnd.randint(0, 600), rnd.choice([1, 1, 2, 3])
        d.ellipse([x - r, y - r, x + r, y + r], fill=(240, 245, 255))

def art_ralli(im, d, rnd):
    d.ellipse([140, 230, 380, 470], fill=(255, 200, 80))
    for y in range(260, 470, 22): d.rectangle([100, y, 420, y + 8], fill=(255, 120, 40))
    d.polygon([(0, 560), (512, 500), (512, 600), (0, 600)], fill=(40, 25, 30))
    # voiture de rallye en silhouette, poussiere
    d.polygon([(90, 520), (150, 470), (300, 462), (380, 490), (430, 495), (440, 530), (90, 545)], fill=(20, 15, 18))
    for cx in (160, 370): d.ellipse([cx - 32, 512, cx + 32, 576], fill=(20, 15, 18))
    for _ in range(40):
        x, y, r = rnd.randint(0, 120), rnd.randint(470, 580), rnd.randint(10, 30)
        d.ellipse([x - r, y - r, x + r, y + r], fill=(200, 140, 90))

def art_susi(im, d, rnd):
    d.ellipse([150, 200, 370, 420], fill=(200, 30, 30))
    d.polygon([(0, 600), (80, 470), (140, 520), (230, 400), (300, 500), (380, 430), (512, 560), (512, 600)], fill=(10, 5, 8))
    # loup qui hurle
    d.polygon([(290, 430), (320, 360), (345, 330), (360, 300), (372, 330), (365, 360), (390, 420), (400, 470), (300, 470)], fill=(10, 5, 8))
    for _ in range(60):
        x, y = rnd.randint(0, 512), rnd.randint(0, 200)
        d.point((x, y), fill=(255, 220, 220))

def art_kesa(im, d, rnd):
    d.ellipse([330, 190, 450, 310], fill=(255, 240, 160))
    d.rectangle([0, 470, 512, 600], fill=(80, 150, 200))
    for y in range(480, 600, 14): d.line([(rnd.randint(0, 200), y), (rnd.randint(300, 512), y)], fill=(170, 210, 235), width=2)
    for x in range(0, 512, 36):
        h = rnd.randint(60, 120)
        d.polygon([(x, 470), (x + 18, 470 - h), (x + 36, 470)], fill=(40, 100, 60))
    # sauna rouge au bord du lac
    d.rectangle([60, 410, 160, 470], fill=(150, 30, 30)); d.polygon([(50, 410), (110, 370), (170, 410)], fill=(60, 40, 40))

def art_avaruus(im, d, rnd):
    for _ in range(200):
        x, y = rnd.randint(0, 512), rnd.randint(0, 600)
        d.point((x, y), fill=(255, 255, 255))
    d.ellipse([260, 220, 520, 480], fill=(180, 60, 160))
    d.arc([180, 310, 600, 400], 160, 380, fill=(255, 200, 255), width=6)
    for k in range(9):
        y = 480 + k * k * 2.2
        d.line([(0, y), (512, y)], fill=(255, 0, 200), width=2)
    for x in range(-400, 912, 64): d.line([(256, 480), (x, 600)], fill=(255, 0, 200), width=2)

def art_sauna(im, d, rnd):
    d.rectangle([60, 300, 452, 600], fill=(120, 70, 40))
    for y in range(310, 600, 24): d.line([(60, y), (452, y)], fill=(90, 50, 28), width=3)
    d.rectangle([200, 420, 312, 600], fill=(60, 35, 20))
    for _ in range(30):
        x, y, r = rnd.randint(40, 470), rnd.randint(180, 330), rnd.randint(20, 50)
        d.ellipse([x - r, y - r, x + r, y + r], fill=(235, 235, 235))
    for cx in (140, 370):
        d.ellipse([cx - 30, 350, cx + 30, 410], fill=(240, 200, 170))
        d.rectangle([cx - 20, 410, cx + 20, 470], fill=(240, 240, 250))

def posters():
    poster(1, 'LUMIMYRSKY', 'Talvi ei armahda.', ((20, 30, 60), (120, 140, 190), (255, 255, 255)), art_lumimyrsky, 'K-12', 'Pekka Lahti · Raili Joki · Ohjaus: Arvo Nieminen')
    poster(2, 'RALLIKUNINGAS', 'Suomen nopein mies.', ((60, 20, 40), (255, 120, 40), (255, 220, 60)), art_ralli, 'S', 'Matti Kivelä · Liisa Ahola · Ohjaus: Seppo Talvi')
    poster(3, 'YÖN SUSI', 'Kun kuu nousee...', ((5, 5, 15), (60, 10, 20), (230, 30, 30)), art_susi, 'K-16', 'Juha Mäki · Anu Salo · Ohjaus: Kari Vuori')
    poster(4, 'PERÄJÄRVEN KESÄ', 'Rakkautta ja mökkielämää.', ((120, 190, 240), (250, 230, 200), (230, 40, 80)), art_kesa, 'S', 'Ville Rinne · Sari Hakala · Ohjaus: Tuula Saari')
    poster(5, 'AVARUUDEN VARTIJAT', 'Taistelu tähdistä alkaa.', ((10, 0, 30), (80, 0, 90), (0, 255, 240)), art_avaruus, 'K-12', 'Jari Koski · Mia Lind · Ohjaus: Timo Aro')
    poster(6, 'SAUNA-ÄIJÄT', 'Löylyä ja naurua!', ((60, 40, 30), (200, 150, 110), (255, 230, 120)), art_sauna, 'S', 'Erkki Pulli · Heikki Mattila · Ohjaus: Olli Hiltunen')

# ---------------------------------------------------------------- tableau des prix
def hinnasto():
    W, H = 1024, 512
    im = Image.new('RGB', (W, H), (18, 18, 22))
    d = ImageDraw.Draw(im)
    d.rectangle([8, 8, W - 9, H - 9], outline=(200, 160, 60), width=6)
    text_center(d, (W / 2, 60), 'HINNASTO · PRICES', font(IMPACT, 60), (255, 214, 80))
    rows = [('LIPPU · TICKET', '35 mk'), ('POPCORN', '12 mk'), ('SIPSIT · CHIPS', '8 mk'), ('LIMSA · SODA', '9 mk'), ('KARKIT · CANDY', '6 mk')]
    for k, (a, b) in enumerate(rows):
        y = 140 + k * 72
        d.text((70, y), a, font=font(BAHN, 50), fill=(240, 240, 240))
        bb = d.textbbox((0, 0), b, font=font(IMPACT, 52))
        d.text((W - 70 - bb[2], y - 4), b, font=font(IMPACT, 52), fill=(255, 90, 80))
        d.line([(70, y + 62), (W - 70, y + 62)], fill=(60, 60, 70), width=2)
    save(im, 'kino-hinnasto.png')

def sali(n):
    W, H = 600, 200
    base = Image.new('RGB', (W, H), (12, 12, 16))
    neon = Image.new('RGB', (W, H), (0, 0, 0))
    nd = ImageDraw.Draw(neon)
    text_center(nd, (W / 2, H / 2), 'SALI %d' % n, font(IMPACT, 140), (255, 214, 80) if n == 1 else (120, 220, 255))
    out = ImageChops.add(base, Image.eval(glow(neon, 12, 1), lambda v: int(v * 0.8)))
    out = ImageChops.lighter(out, neon)
    ImageDraw.Draw(out).rectangle([3, 3, W - 4, H - 4], outline=(80, 80, 90), width=4)
    save(out, 'kino-sali%d.png' % n)

def konehuone():
    W, H = 512, 128
    im = Image.new('RGB', (W, H), (20, 60, 140))
    d = ImageDraw.Draw(im)
    d.rectangle([6, 6, W - 7, H - 7], outline=(240, 240, 240), width=4)
    text_center(d, (W / 2, 50), 'KONEHUONE', font(ARIAL, 52), (255, 255, 255))
    text_center(d, (W / 2, 100), 'PROJECTION ROOM', font(ARIAL, 22), (220, 230, 255))
    save(im, 'kino-konehuone.png')

def ulos():
    W, H = 320, 128
    im = Image.new('RGB', (W, H), (0, 150, 70))
    d = ImageDraw.Draw(im)
    text_center(d, (205, 64), 'ULOS', font(ARIAL, 60), (255, 255, 255))
    # petit bonhomme qui court + fleche
    d.ellipse([40, 22, 60, 42], fill=(255, 255, 255))
    d.line([(50, 42), (44, 75), (30, 100)], fill=(255, 255, 255), width=8)
    d.line([(44, 75), (66, 95)], fill=(255, 255, 255), width=8)
    d.line([(48, 52), (72, 62)], fill=(255, 255, 255), width=7)
    d.line([(48, 52), (28, 66)], fill=(255, 255, 255), width=7)
    d.polygon([(84, 54), (104, 64), (84, 74)], fill=(255, 255, 255))
    save(im, 'kino-ulos.png')

def valkokangas():
    W, H = 512, 256
    im = Image.new('RGB', (W, H), (232, 232, 228))
    v = Image.new('L', (W, H), 0)
    ImageDraw.Draw(v).ellipse([-W * 0.3, -H * 0.4, W * 1.3, H * 1.4], fill=255)
    v = v.filter(ImageFilter.GaussianBlur(60))
    dark = Image.new('RGB', (W, H), (175, 175, 172))
    im = Image.composite(im, dark, v)
    save(im, 'kino-valkokangas.png')

kyltti(); posters(); hinnasto(); sali(1); sali(2); konehuone(); ulos(); valkokangas()

# ---------------------------------------------------------------- emballages (objets achetes au comptoir, bobine)
def popcorn():
    W, H = 512, 256   # (tour du pot : rayures rouges et blanches, POPCORN au milieu)
    im = Image.new('RGB', (W, H), (250, 250, 245))
    d = ImageDraw.Draw(im)
    for x in range(0, W, 64): d.rectangle([x, 0, x + 31, H], fill=(210, 25, 30))
    for cx in (128, 384):
        d.rounded_rectangle([cx - 100, 80, cx + 100, 176], 18, fill=(255, 214, 80), outline=(120, 20, 20), width=5)
        text_center(d, (cx, 128), 'POPCORN', font(IMPACT, 44), (170, 20, 20))
    save(im, 'kino-popcorn.png')

def sipsit():
    W, H = 256, 360
    im = gradient(W, H, (255, 200, 40), (240, 120, 20))
    d = ImageDraw.Draw(im)
    d.ellipse([40, 150, 216, 300], fill=(250, 225, 150), outline=(200, 140, 40), width=4)
    for k in range(6): d.arc([60 + k * 8, 170 + k * 6, 196 - k * 8, 280 - k * 6], 200, 340, fill=(220, 170, 70), width=3)
    text_center(d, (W / 2, 70), 'SIPSIT', font(IMPACT, 72), (160, 20, 20))
    text_center(d, (W / 2, 120), 'suolattu · salted', font(BAHN, 22), (90, 30, 10))
    text_center(d, (W / 2, 330), 'KINO TÄHTI', font(IMPACT, 26), (255, 255, 255))
    save(im, 'kino-sipsit.png')

def limsa():
    W, H = 512, 256
    im = Image.new('RGB', (W, H), (245, 245, 250))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 60, W, 196], fill=(20, 80, 170))
    for cx in (128, 384):
        d.polygon(star(cx, 128, 50, 20), fill=(255, 214, 80))
        text_center(d, (cx, 220), 'LIMSA', font(IMPACT, 30), (200, 30, 40))
    save(im, 'kino-limsa.png')

def karkit():
    W, H = 256, 340
    im = Image.new('RGB', (W, H), (235, 225, 205))
    d = ImageDraw.Draw(im)
    rnd = random.Random(9)
    for _ in range(60):
        x, y, r = rnd.randint(20, 236), rnd.randint(150, 320), rnd.randint(8, 16)
        d.ellipse([x - r, y - r, x + r, y + r], fill=rnd.choice([(230, 30, 60), (40, 160, 70), (250, 200, 30), (120, 60, 200), (250, 120, 30)]))
    d.rectangle([0, 30, W, 130], fill=(255, 255, 255))
    text_center(d, (W / 2, 70), 'KARKIT', font(IMPACT, 60), (220, 30, 120))
    text_center(d, (W / 2, 112), 'irtokarkkia', font(BAHN, 22), (90, 90, 90))
    save(im, 'kino-karkit.png')

def kela():
    W, H = 512, 512   # (couvercle de la boite de bobine : metal, etiquette au milieu)
    im = Image.new('RGB', (W, H), (150, 152, 158))
    d = ImageDraw.Draw(im)
    for r in range(250, 0, -18): d.ellipse([256 - r, 256 - r, 256 + r, 256 + r], outline=(130, 132, 138), width=3)
    d.rounded_rectangle([96, 196, 416, 316], 12, fill=(245, 240, 225), outline=(60, 60, 70), width=4)
    text_center(d, (256, 236), 'FILMIKELA', font(IMPACT, 54), (30, 30, 40))
    text_center(d, (256, 290), '35 mm · KINO TÄHTI', font(BAHN, 24), (90, 30, 30))
    save(im.transpose(Image.FLIP_LEFT_RIGHT), 'kino-kela.png')   # (le couvercle est vu par le dos du quad : image retournee)

popcorn(); sipsit(); limsa(); karkit(); kela()

def hintalappu_kela():
    # etiquette de prix de l'Osuuskauppa (meme style que hintalappu-vhs.png)
    W, H = 256, 128
    im = Image.new('RGB', (W, H), (240, 200, 50))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W - 1, H - 1], outline=(200, 150, 30), width=3)
    text_center(d, (W / 2, 24), 'FILMIKELA', font(ARIAL, 26), (30, 30, 30))
    text_center(d, (W / 2, 80), '49 mk', font(IMPACT, 62), (180, 30, 30))
    save(im, 'kino-hintalappu-kela.png')

hintalappu_kela()
