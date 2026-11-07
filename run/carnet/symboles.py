# Planche de JD (2048x1536, 8x6 cases de 256) -> symboles.png RGBA (8x6 cases de 128) : fond blanc et reperes bleu tres
# clair retires (cadres, etiquettes, lignes guides), couleurs de JD gardees, traits un peu epaissis avant la reduction.
import sys
import numpy as np
from PIL import Image
from scipy import ndimage

src, dst, prev = sys.argv[1], sys.argv[2], sys.argv[3]
im = np.array(Image.open(src).convert('RGB')).astype(np.float32)
r, g, b = im[..., 0], im[..., 1], im[..., 2]
mx, mn = im.max(-1), im.min(-1)
sat = mx - mn
lum = 0.299 * r + 0.587 * g + 0.114 * b
# reperes : (200,216,240) et (225,232,245) et leurs melanges avec le blanc -> sat <= ~42, lum >= ~212, et teinte bleu pale
# (b le plus fort). Encre : nettement plus saturee ou plus foncee.
a_sat = np.clip((sat - 48) / 70.0, 0, 1)
a_lum = np.clip((200 - lum) / 70.0, 0, 1)
alpha = np.maximum(a_sat, a_lum)
# les pixels de repere eux-memes : jamais d'encre
guide = (sat <= 46) & (lum >= 196) & (b >= g) & (g >= r - 2)
alpha[guide] = 0
# couleur d'encre : demelange du blanc (c = a*ink + (1-a)*255)
a3 = np.maximum(alpha, 1e-3)[..., None]
ink = np.clip(255 - (255 - im) / a3, 0, 255)
# epaissir : dilatation de l'alpha (rayon 2 px), couleur du voisin le plus opaque
k = np.zeros((5, 5), bool)
yy, xx = np.mgrid[-2:3, -2:3]
k[(yy * yy + xx * xx) <= 5] = True
alpha_d = ndimage.grey_dilation(alpha, footprint=k)
# index du voisin le plus opaque : maximum_filter sur (alpha*1e6 + index) n'est pas exact en float ; on fait par decalages
best_a = alpha.copy(); best_c = ink.copy()
H, W = alpha.shape
for dy in range(-2, 3):
    for dx in range(-2, 3):
        if not k[dy + 2, dx + 2] or (dy == 0 and dx == 0):
            continue
        sa = np.zeros_like(alpha); sc = np.zeros_like(ink)
        ys0, ys1 = max(0, dy), H + min(0, dy); xs0, xs1 = max(0, dx), W + min(0, dx)
        sa[ys0 - dy:ys1 - dy, xs0 - dx:xs1 - dx] = alpha[ys0:ys1, xs0:xs1]
        sc[ys0 - dy:ys1 - dy, xs0 - dx:xs1 - dx] = ink[ys0:ys1, xs0:xs1]
        m = sa > best_a
        best_a[m] = sa[m]; best_c[m] = sc[m]
alpha_d = np.clip(alpha_d * 1.0, 0, 1)
# un peu de douceur sur le bord dilate (moyenne avec l'original) pour ne pas faire de traits en escalier
alpha_f = np.maximum(alpha, ndimage.uniform_filter(alpha_d, 3) * 0.95)
rgba = np.dstack([best_c, alpha_f[..., None] * 255]).astype(np.float32)
# reduction 2x en premultiplie
pm = rgba.copy(); pm[..., :3] *= alpha_f[..., None]
pm = pm.reshape(H // 2, 2, W // 2, 2, 4).mean((1, 3))
A = pm[..., 3:4] / 255.0
out = np.zeros_like(pm)
out[..., :3] = np.where(A > 1e-4, pm[..., :3] / np.maximum(A, 1e-4), 0)
out[..., 3] = pm[..., 3]
out = np.clip(out + 0.5, 0, 255).astype(np.uint8)
out[out[..., 3] < 6] = 0
Image.fromarray(out, 'RGBA').save(dst, optimize=True)
# controle : sur fond gris moyen et sur papier creme
for name, bg in (('gris', (128, 128, 128)), ('papier', (250, 244, 225))):
    base = Image.new('RGBA', (W // 2, H // 2), bg + (255,))
    base.alpha_composite(Image.fromarray(out, 'RGBA'))
    base.convert('RGB').save(prev.replace('.png', '-' + name + '.png'))
print('ok', out.shape, int((out[..., 3] > 0).sum()))
