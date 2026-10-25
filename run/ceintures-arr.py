"""Ceintures arriere de la SORBET (modele de Menos, 25/10 : assets/ceintures/rear_seatbelts_sorbett-menos.obj) -> maillages
du mod (assets/ceintures/*.mwm, embarques par build.cmd). Repere Unity (x oppose, obj2mwm.py) ; g = x < 0, d = x > 0.
  arr-base.mwm      : ancrages et boucles (seatbelts_parts) + haut des sangles (seatbelt, au-dessus de l'epaule) -- toujours
  arr-rangee-g/d    : languette rangee (seatbelts_handle) + bas de la sangle (jusqu'au plancher) -- place vide ou detachee
  arr-bouclee-g/d   : ceinture bouclee (seatbelts_fastened) -- joueur attache a cette place
Usage : python -I ceintures-arr.py   (depuis la racine du depot)"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import obj2mwm

SRC = 'assets/ceintures/rear_seatbelts_sorbett-menos.obj'
SHOULDER = 0.84   # (y du haut de la languette rangee : au-dessus, la sangle reste quand on s'attache)

v, vt, vn, groups = obj2mwm.load(SRC)
def faces(names, side=None, ymin=None, ymax=None):
    out = []
    for n in names:
        for f in groups[n]:
            xs = [-v[a][0] for (a, b, c) in f]; ys = [v[a][1] for (a, b, c) in f]
            cx, cy = sum(xs) / len(xs), sum(ys) / len(ys)
            if side == 'g' and cx >= 0: continue
            if side == 'd' and cx <= 0: continue
            if ymin is not None and cy < ymin: continue
            if ymax is not None and cy >= ymax: continue
            out.append(f)
    return out
def out(name, fl):
    pos, nrm, uv, tri = obj2mwm.build(v, vt, vn, fl)
    obj2mwm.write('assets/ceintures/' + name, pos, nrm, uv, tri)
    print(name, len(pos), 'sommets', len(tri) // 3, 'triangles')
straps = ['seatbelt', 'seatbelt.001']
out('arr-base.mwm', faces(['seatbelts_parts']) + faces(straps, ymin=SHOULDER))
for s in 'gd':
    out('arr-rangee-%s.mwm' % s, faces(['seatbelts_handle', 'seatbelts_handle.001'], s) + faces(straps, s, ymax=SHOULDER))
    out('arr-bouclee-%s.mwm' % s, faces(['seatbelts_fastened', 'seatbelts_fastened.001'], s))
