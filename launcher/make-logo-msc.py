# Logo du lanceur pour My Summer Car (switch MSC / MWC, 19/10) : launcher\logo-msc.png, meme style que logo-titre.png
# (lettres rondes, degrade jaune -> orange, liseré blanc, contour bleu nuit ; "coop" en bleu), police Nunito ExtraBold
# du lanceur. Provisoire tant que JD n'a pas fourni le sien. Lancer : python launcher\make-logo-msc.py
import os
from PIL import Image, ImageDraw, ImageFont, ImageFilter

here = os.path.dirname(os.path.abspath(__file__))
font_path = os.path.join(here, 'Nunito-ExtraBold.ttf')
S = 4                     # dessin en grand, reduit a la fin (bords lisses)
W, H = 364 * S, 156 * S   # taille de logo-titre.png
NAVY = (19, 30, 72, 255)

def word(text, size, top, bottom, outline, white):
    f = ImageFont.truetype(font_path, size)
    box = f.getbbox(text)
    w, h = box[2] - box[0] + 2 * (outline + white) + 8, box[3] - box[1] + 2 * (outline + white) + 8
    ox, oy = outline + white + 4 - box[0], outline + white + 4 - box[1]
    mask = Image.new('L', (w, h), 0)
    ImageDraw.Draw(mask).text((ox, oy), text, font=f, fill=255)
    out = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    # contour bleu nuit, puis liseré blanc, puis lettres en degrade
    navy = mask.filter(ImageFilter.MaxFilter(2 * (outline + white) + 1))
    out.paste(Image.new('RGBA', (w, h), NAVY), (0, 0), navy)
    wm = mask.filter(ImageFilter.MaxFilter(2 * white + 1))
    out.paste(Image.new('RGBA', (w, h), (255, 255, 255, 255)), (0, 0), wm)
    grad = Image.new('RGBA', (w, h))
    gd = ImageDraw.Draw(grad)
    for y in range(h):
        t = min(1, max(0, (y - (oy + box[1])) / max(1, (box[3] - box[1]))))
        c = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,)
        gd.line([(0, y), (w, y)], fill=c)
    out.paste(grad, (0, 0), mask)
    # reflet clair en haut des lettres
    hi = Image.new('L', (w, h), 0)
    ImageDraw.Draw(hi).rectangle([0, 0, w, oy + box[1] + (box[3] - box[1]) * 0.28], fill=70)
    hl = Image.composite(hi, Image.new('L', (w, h), 0), mask)
    out.paste(Image.new('RGBA', (w, h), (255, 255, 255, 255)), (0, 0), hl)
    return out

logo = Image.new('RGBA', (W, H), (0, 0, 0, 0))
yellow, orange = (255, 236, 92), (255, 128, 36)
big = word('SummerCar', 76 * S, yellow, orange, 6 * S, 3 * S)
big = big.resize((min(big.width, W), big.height), Image.LANCZOS)
my = word('my', 40 * S, yellow, orange, 5 * S, 2 * S)
coop = word('coop', 34 * S, (190, 230, 255), (40, 120, 230), 5 * S, 2 * S)
by = 34 * S
logo.alpha_composite(big, ((W - big.width) // 2, by))
logo.alpha_composite(my, (int(W * 0.20), 0))
logo.alpha_composite(coop, (W - coop.width - 16 * S, by + big.height - 30 * S))
logo = logo.crop(logo.getbbox())
logo = logo.resize((logo.width // S, logo.height // S), Image.LANCZOS)
logo.save(os.path.join(here, 'logo-msc.png'))
print('logo-msc.png', logo.size)
