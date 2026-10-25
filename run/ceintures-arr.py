"""Ceintures arriere de la SORBET (modele de Menos, 25/10 : assets/ceintures/rear_seatbelts_sorbett-menos.obj) -> maillages
du mod (assets/ceintures/*.mwm, embarques par build.cmd). Repere Unity (x oppose, obj2mwm.py) ; g = x < 0, d = x > 0.
Le mod (Sync/Seats.cs, BuildRearModel) recale boucles et ancrages bas sur la pliure des sieges du jeu et fait lui-meme la
sangle rangee le long du dossier (JD, 25/10 : « la sangle doit suivre le fauteuil, la boucle dans la pliure »).
  arr-ancres-haut.mwm  : ancrages de la plage arriere (les deux)               -- toujours
  arr-boucles.mwm      : boucles du milieu (les deux)                          -- toujours, recalees dans la pliure
  arr-ancre-bas-g/d    : ancrage bas cote portiere                              -- toujours, recale au bas du dossier
  arr-languette-g/d    : languette rangee                                       -- place libre (posee en haut du dossier)
  arr-sangle-haut-g/d  : sangle de l'ancrage haut a l'epaule                    -- place bouclee
  arr-bouclee-g/d      : ceinture bouclee                                       -- place bouclee (son bas suit la pliure)
Usage : python -I ceintures-arr.py   (depuis la racine du depot)"""
import os, sys, glob
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import obj2mwm

SRC = 'assets/ceintures/rear_seatbelts_sorbett-menos.obj'
SHOULDER = 0.84   # (y du haut de la languette rangee : au-dessus, la sangle va a l'epaule)

v, vt, vn, groups = obj2mwm.load(SRC)
def faces(names, side=None, keep=None):
    out = []
    for n in names:
        for f in groups[n]:
            xs = [-v[a][0] for (a, b, c) in f]; ys = [v[a][1] for (a, b, c) in f]
            cx, cy = sum(xs) / len(xs), sum(ys) / len(ys)
            if side == 'g' and cx >= 0: continue
            if side == 'd' and cx <= 0: continue
            if keep is not None and not keep(cx, cy): continue
            out.append(f)
    return out
def out(name, fl):
    pos, nrm, uv, tri = obj2mwm.build(v, vt, vn, fl)
    obj2mwm.write('assets/ceintures/' + name, pos, nrm, uv, tri)
    print(name, len(pos), 'sommets', len(tri) // 3, 'triangles')
for f in glob.glob('assets/ceintures/*.mwm'): os.remove(f)
P = ['seatbelts_parts']
out('arr-ancres-haut.mwm', faces(P, keep=lambda x, y: y > 0.9))
out('arr-boucles.mwm', faces(P, keep=lambda x, y: abs(x) < 0.35 and y <= 0.9))
for s in 'gd':
    out('arr-ancre-bas-%s.mwm' % s, faces(P, s, keep=lambda x, y: abs(x) >= 0.35 and y <= 0.9))
    out('arr-languette-%s.mwm' % s, faces(['seatbelts_handle', 'seatbelts_handle.001'], s))
    out('arr-sangle-haut-%s.mwm' % s, faces(['seatbelt', 'seatbelt.001'], s, keep=lambda x, y: y >= SHOULDER))
    out('arr-bouclee-%s.mwm' % s, faces(['seatbelts_fastened', 'seatbelts_fastened.001'], s))
