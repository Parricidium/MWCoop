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

# MACHTWAGEN (taxi, modele de Menos du 25/10 : assets/ceintures/machtwagen_rear_seatbelts-menos.obj) : modele tourne d'un
# demi-tour (l'arriere en z positif) et a la hauteur de la racine de la voiture -- repere Unity : (x, y, -z), un miroir comme
# l'import d'un .obj. Une seule languette (cote g) : celle du cote d en miroir. Pieces telles quelles (pas de siege separe a
# mesurer dans la MACHTWAGEN : sa banquette est dans LOD/interior).
TAXI = 'assets/ceintures/machtwagen_rear_seatbelts-menos.obj'
tv, tvt, tvn, tg = obj2mwm.load(TAXI)
XF = lambda x, y, z: (x, y, -z)
XFM = lambda x, y, z: (-x, y, -z)   # (miroir de la languette : cote oppose -- deux miroirs, donc pas un miroir : ordre a reinverser)
def tfaces(names, side, ymin=None, ymax=None):
    out = []
    for n in names:
        for f in tg[n]:
            cx = sum(tv[a][0] for (a, b, c) in f) / len(f); cy = sum(tv[a][1] for (a, b, c) in f) / len(f)
            if side == 'g' and cx >= 0: continue
            if side == 'd' and cx <= 0: continue
            if ymin is not None and cy < ymin: continue
            if ymax is not None and cy >= ymax: continue
            out.append(f)
    return out
TSHOULDER = 0.62   # (haut de la ceinture bouclee : au-dessus, la sangle va a l'ancrage haut dans les deux etats)
def tout(name, fl, xf=XF, flip=False):
    pos, nrm, uv, tri = obj2mwm.build(tv, tvt, tvn, fl, xf)
    if flip: tri = [tri[i + k] for i in range(0, len(tri), 3) for k in (0, 2, 1)]
    obj2mwm.write('assets/ceintures/' + name, pos, nrm, uv, tri)
    print(name, len(pos), 'sommets', len(tri) // 3, 'triangles')
tout('taxi-base.mwm', tfaces(['taxi_seatbelts_parts'], None))
for s in 'gd':
    tout('taxi-sangle-haut-%s.mwm' % s, tfaces(['seatbelt', 'seatbelt.001'], s, ymin=TSHOULDER))
    tout('taxi-sangle-bas-%s.mwm' % s, tfaces(['seatbelt', 'seatbelt.001'], s, ymax=TSHOULDER))
    tout('taxi-bouclee-%s.mwm' % s, tfaces(['taxi_seatbelts_fastened', 'taxi_seatbelts_fastened.001'], s))
tout('taxi-languette-g.mwm', tfaces(['seatbelts_handle'], 'g'))
tout('taxi-languette-d.mwm', tfaces(['seatbelts_handle'], 'g'), XFM, True)
