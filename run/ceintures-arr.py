"""Ceintures arriere de la SORBET (modele de Menos, v2 du 26/10 : assets/ceintures/rear_seatbelts_sorbett-menos-v2.obj) -> maillages
du mod (assets/ceintures/*.mwm, embarques par build.cmd). Repere Unity (x oppose, obj2mwm.py) ; g = x < 0, d = x > 0.
Systeme de la ceinture du conducteur (Menor et JD, 28/10 : « supprime ce systeme, refais-le sur celui du conducteur ») : les trois
objets de Menos tels quels, deja dans le repere de la voiture -- aucun recalage ni sangle faite par le mod. Un seul cote (g), le
cote d en miroir. Deux matieres : sous-maillage 0 la sangle (Seatbelt, Material.003 : matiere de la ceinture avant du jeu), 1 le
metal et le plastique (Atlas, Material.002 -- texture ATLAS_MOTOR_MWC du jeu, Seats.RearAtlas).
  arr-pieces-g/d     : seatbelts_parts -- ancrages, boucle, sangle rangee de la plage arriere a l'ancrage bas -- toujours
  arr-languette-g/d  : seatbelts_handle -- languette pendue sur la sangle rangee                              -- place libre
  arr-bouclee-g/d    : seatbelts_fastened -- ceinture bouclee                                                 -- place bouclee
Usage : python -I ceintures-arr.py   (depuis la racine du depot)"""
import os, sys, glob, struct
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import obj2mwm

SRC = 'assets/ceintures/rear_seatbelts_sorbett-menos-v2.obj'
SHIFT = (0.0, 0.0, 0.0)   # (v2 telle quelle : repere de la voiture)
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
# Haut de la languette (la ou passe la sangle, pendue ou bouclee) : quelques faces du metal de Menos tombent sur des zones rouges et
# orange de l'atlas du jeu (JD 28/10 : « bug de texture » sur le haut noir) -> sur le plastique noir de l'atlas (texel (0,586 ; 0,87),
# le noir de ses autres faces). Seulement dans cette bande (y 0,585..0,62, |x| 0,52..0,61 : boucles rouges et ancrages restent).
vt.append((0.586, 0.87)); NOIR = len(vt) - 1
def noircir(n, i, f):
    if mats.get((n, i)) in STRAP: return f
    cx = sum(abs(v[a][0]) for (a, b, c) in f) / len(f); cy = sum(v[a][1] for (a, b, c) in f) / len(f)
    if 0.585 < cy < 0.62 and 0.52 <= cx <= 0.61: return [(a, NOIR, c) for (a, b, c) in f]   # (pas les ancrages hauts, y 0,75)
    return f
def faces(names, keep=None):
    out = []
    for n in names:
        for i, f in enumerate(groups[n]):
            f = noircir(n, i, f)
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
for f in glob.glob('assets/ceintures/arr-*.mwm'): os.remove(f)
for s in 'gd':
    out('arr-pieces-%s.mwm' % s, faces(['seatbelts_parts']), s)
    out('arr-languette-%s.mwm' % s, faces(['seatbelts_handle']), s)
    out('arr-bouclee-%s.mwm' % s, faces(['seatbelts_fastened']), s)
# (MACHTWAGEN et CORRIS : modeles de Menor du 31/10, run/ceintures-menor.py)
