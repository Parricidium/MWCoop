"""Ceintures arriere de la SORBET (modele de Menos, v2 du 26/10 : assets/ceintures/rear_seatbelts_sorbett-menos-v2.obj) -> maillages
du mod (assets/ceintures/*.mwm, embarques par build.cmd). Repere Unity (x oppose, obj2mwm.py) ; g = x < 0, d = x > 0.
v2 (JD 28/10 : « mettre a jour la ceinture de Menor ») : un seul cote (g, x < 0), le cote d en miroir ; meme modele que la v1 dans
un repere 25 cm plus bas et 6 cm plus en avant (ecart mesure piece par piece, a 1 cm pres sauf l'ancrage haut, 3 cm) : remis dans
celui de la v1, ou le mod fait ses recalages (BuildRearModel). Deux matieres : sous-maillage 0 la sangle (Seatbelt, Material.003),
1 le metal et le plastique (Atlas, Material.002 -- texture ATLAS_MOTOR_MWC du jeu, Seats.RearAtlas).
Le mod (Sync/Seats.cs, BuildRearModel) recale boucles et ancrages bas sur la pliure des sieges du jeu et fait lui-meme la
sangle rangee le long du dossier (JD, 25/10 : « la sangle doit suivre le fauteuil, la boucle dans la pliure »).
  arr-ancres-haut.mwm  : ancrages de la plage arriere (les deux)               -- toujours
  arr-boucles.mwm      : boucles du milieu (les deux)                          -- toujours, recalees dans la pliure
  arr-ancre-bas-g/d    : ancrage bas cote portiere                              -- toujours, recale au bas du dossier
  arr-languette-g/d    : languette rangee                                       -- place libre (posee en haut du dossier)
  arr-sangle-haut-g/d  : sangle de l'ancrage haut a l'epaule                    -- place bouclee
  arr-bouclee-g/d      : ceinture bouclee                                       -- place bouclee (son bas suit la pliure)
Usage : python -I ceintures-arr.py   (depuis la racine du depot)"""
import os, sys, glob, struct
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import obj2mwm

SRC = 'assets/ceintures/rear_seatbelts_sorbett-menos-v2.obj'
SHOULDER = 0.84   # (y du haut de la languette rangee : au-dessus, la sangle va a l'epaule -- repere de la v1)
SHIFT = (0.0, 0.251, -0.063)   # (v2 -> repere de la v1)
STRAP = {'Seatbelt', 'Seatbeltt', 'Material.003'}   # (le reste : Atlas, ATLAS, Material.002)

# Faces avec leur matiere (obj2mwm.load ne la garde pas)
v, vt, vn, groups = obj2mwm.load(SRC)
mats = {}
cur, mat, k = None, None, {}
for l in open(SRC, encoding='utf-8', errors='replace'):
    p = l.split()
    if not p: continue
    if p[0] == 'o': cur = l[2:].strip(); k[cur] = 0
    elif p[0] == 'usemtl': mat = p[1]
    elif p[0] == 'f': mats[(cur, k[cur])] = mat; k[cur] += 1
def faces(names, keep=None):
    out = []
    for n in names:
        for i, f in enumerate(groups[n]):
            cx = sum(-v[a][0] for (a, b, c) in f) / len(f) + SHIFT[0]; cy = sum(v[a][1] for (a, b, c) in f) / len(f) + SHIFT[1]
            if keep is not None and not keep(cx, cy): continue
            out.append((f, mats.get((n, i)) in STRAP))
    return out
def out(name, fl, sides):
    """sides : 'g' (le modele), 'd' (son miroir) ou 'gd' (les deux dans un maillage)"""
    pos, nrm, uv, subs = [], [], [], [[], []]
    for s in sides:
        xs = 1.0 if s == 'd' else -1.0   # (x oppose : repere Unity ; d : miroir de g, donc x de l'obj tel quel)
        for strap in (True, False):
            sel = [f for (f, st) in fl if st == strap]
            if not sel: continue
            p, n, u, t = obj2mwm.build(v, vt, vn, sel, lambda x, y, z: (xs * x, y, z))
            if s == 'd': t = [t[i + j] for i in range(0, len(t), 3) for j in (0, 2, 1)]   # (pas un miroir : ordre a reinverser)
            base = len(pos)
            pos += [(q[0] + (-SHIFT[0] if s == 'd' else SHIFT[0]), q[1] + SHIFT[1], q[2] + SHIFT[2]) for q in p]; nrm += n; uv += u
            subs[0 if strap else 1] += [base + i for i in t]
    with open('assets/ceintures/' + name, 'wb') as o:
        o.write(b'MWM1'); o.write(struct.pack('<ii', 2, len(pos)))
        for q in pos: o.write(struct.pack('<fff', *q))
        for q in nrm: o.write(struct.pack('<fff', *q))
        for q in uv: o.write(struct.pack('<ff', *q))
        for tri in subs:
            o.write(struct.pack('<i', len(tri))); o.write(struct.pack('<%di' % len(tri), *tri))
            o.write(struct.pack('<fff', 1.0, 1.0, 1.0))
    print(name, len(pos), 'sommets', len(subs[0]) // 3, '+', len(subs[1]) // 3, 'triangles (sangle + metal)')
for f in glob.glob('assets/ceintures/*.mwm'): os.remove(f)
P = ['seatbelts_parts']
out('arr-ancres-haut.mwm', [q for q in faces(P, lambda x, y: y > 0.9) if not q[1]], 'gd')
out('arr-boucles.mwm', [q for q in faces(P, lambda x, y: abs(x) < 0.35 and y <= 0.9) if not q[1]], 'gd')
for s in 'gd':
    out('arr-ancre-bas-%s.mwm' % s, [q for q in faces(P, lambda x, y: abs(x) >= 0.35 and y <= 0.9) if not q[1]], s)
    out('arr-languette-%s.mwm' % s, faces(['seatbelts_handle']), s)
    out('arr-sangle-haut-%s.mwm' % s, [q for q in faces(P, lambda x, y: y >= SHOULDER) if q[1]], s)
    out('arr-bouclee-%s.mwm' % s, faces(['seatbelts_fastened']), s)

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
# (Base en pieces, les memes que la SORBET -- l'outil des places les deplace une a une : ancrages hauts y > 0,8 ; ancrages bas
# cote portiere |x| > 0,6 ; boucles au milieu)
def tpart(keep):
    out = []
    for f in tg['taxi_seatbelts_parts']:
        cx = sum(tv[a][0] for (a, b, c) in f) / len(f); cy = sum(tv[a][1] for (a, b, c) in f) / len(f)
        if keep(cx, cy): out.append(f)
    return out
tout('taxi-ancres-haut.mwm', tpart(lambda x, y: y > 0.8))
tout('taxi-boucles.mwm', tpart(lambda x, y: y <= 0.8 and abs(x) <= 0.6))
tout('taxi-ancre-bas-g.mwm', tpart(lambda x, y: y <= 0.8 and x < -0.6))
tout('taxi-ancre-bas-d.mwm', tpart(lambda x, y: y <= 0.8 and x > 0.6))
for s in 'gd':
    tout('taxi-sangle-haut-%s.mwm' % s, tfaces(['seatbelt', 'seatbelt.001'], s, ymin=TSHOULDER))
    tout('taxi-sangle-bas-%s.mwm' % s, tfaces(['seatbelt', 'seatbelt.001'], s, ymax=TSHOULDER))
    tout('taxi-bouclee-%s.mwm' % s, tfaces(['taxi_seatbelts_fastened', 'taxi_seatbelts_fastened.001'], s))
tout('taxi-languette-g.mwm', tfaces(['seatbelts_handle'], 'g'))
tout('taxi-languette-d.mwm', tfaces(['seatbelts_handle'], 'g'), XFM, True)
