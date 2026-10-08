# Pack de tenues de Dom -> tenues MWCoop : dom_haut_NN / dom_pantalon_NN / dom_visage_NN (jamais les noms du jeu).
# Usage : python -I packdom.py <dossier "SKIN FOR MP"> <dossier de sortie>
import sys, os, hashlib, io, zipfile
from PIL import Image

src, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
cats = {'haut': [], 'pantalon': [], 'visage': []}
seen = {}
for root, _, files in os.walk(src):
    for f in sorted(files):
        if not f.lower().endswith('.png'):
            continue
        p = os.path.join(root, f)
        low = f.lower()
        folder = os.path.basename(root).lower()
        if low.startswith('hat_'):
            continue                                   # (casquette : un autre maillage, plus tard)
        if folder == 'face':
            cat = 'visage'
        elif folder == 'pants' or 'pants' in low:
            cat = 'pantalon'
        else:
            cat = 'haut'
        h = hashlib.sha1(open(p, 'rb').read()).hexdigest()
        if h in seen:
            print('doublon', p, '=', seen[h])
            continue
        seen[h] = p
        cats[cat].append(p)

zbuf = io.BytesIO()
mapping = []
total = 0
with zipfile.ZipFile(zbuf, 'w', zipfile.ZIP_STORED) as z:
    for cat, lst in cats.items():
        lst.sort(key=lambda x: x.lower())
        for i, p in enumerate(lst, 1):
            im = Image.open(p)
            size = (512, 256) if cat == 'visage' else (512, 512)
            alpha = False   # (matiere Legacy Shaders/Diffuse, opaque : l'alpha ne sert pas)
            im = im.convert('RGBA' if alpha else 'RGB')
            if im.size != size:
                im = im.resize(size, Image.LANCZOS)
            name = 'dom_%s_%02d' % (cat, i)
            b = io.BytesIO()
            if alpha:
                im.save(b, 'PNG', optimize=True); ext = '.png'
            else:
                im.save(b, 'JPEG', quality=90, optimize=True); ext = '.jpg'
            data = b.getvalue()
            total += len(data)
            z.writestr('dom/' + name + ext, data)
            mapping.append('%s%s\t%s' % (name, ext, os.path.relpath(p, src)))
    lic = 'Pack de tenues par Dom (Discord 820705012450852864), donne a MWCoop le 08/10/2026.\n' \
          'Outfit pack by Dom, given to MWCoop on 2026-10-08.\n\n' + '\n'.join(mapping) + '\n'
    z.writestr('dom/origine.txt', lic)
    z.writestr('dom/version.txt', '1')
open(os.path.join(out, 'tenues-dom-1.zip'), 'wb').write(zbuf.getvalue())
for c, l in cats.items():
    print(c, len(l))
print('images', total // 1024, 'Ko ; zip', len(zbuf.getvalue()) // 1024, 'Ko')
