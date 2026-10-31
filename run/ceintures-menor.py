"""Ceintures arriere de Menor (31/10, transmises par JD : « Menor a fait de nouvelles ceintures ») -> maillages du mod
(assets/ceintures/<cle>-*.mwm, embarques par build.cmd), sur le systeme de la ceinture du conducteur de la SORBET (Seats.RearModel).
  taxi2  : MACHTWAGEN (le modele du 25/10 refait comme la SORBET v2 : sangle + metal, une languette par cote)
  corris : CORRIS -- deux places laterales + la ceinture du milieu (ventrale, bouclee d'un clic comme celle de la BACHGLOTZ)
Les objets de Menor sont deja dans le repere de la voiture, tournes d'un demi-tour (l'arriere en z positif, comme le taxi du 25/10) :
repere Unity (x, y, -z), un miroir comme l'import d'un .obj. Par objet : 'parts' -> pieces (ancrages, boucles, sangle rangee),
'handle' -> languette pendue (place libre), 'fastened' -> ceinture bouclee, 'middle...fastened' -> ceinture du milieu.
Cote d'un objet ou d'une face : g = x < 0 (Unity), d = x > 0 ; pieces du milieu (|x| < 0,12) : <cle>-pieces-m, toujours montrees.
Deux sous-maillages : 0 la sangle, 1 le metal et le plastique (atlas du jeu ATLAS_MOTOR_MWC) -- une matiere est la sangle quand ses
coordonnees de texture sortent de [0 ; 1] (sangle repetee en u), quel que soit son nom (Material.00x, '' ou Seatbelt.00x).
Usage : python -I run/ceintures-menor.py   (depuis la racine du depot)"""
import os, sys, struct
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import obj2mwm

XF = lambda x, y, z: (x, y, -z)
CARS = [('taxi2', 'assets/ceintures/machtwagen_rear_seatbelts-menor-v2.obj', XF),
        ('corris', 'assets/ceintures/corris_rear_seatbelts-menor.obj', XF)]
MID = 0.12

def det(xf):
    a, b, c = xf(1, 0, 0), xf(0, 1, 0), xf(0, 0, 1)
    return a[0] * (b[1] * c[2] - b[2] * c[1]) - a[1] * (b[0] * c[2] - b[2] * c[0]) + a[2] * (b[0] * c[1] - b[1] * c[0])

def convert(key, src, xf):
    v, vt, vn, groups = obj2mwm.load(src)
    # matiere de chaque face (obj2mwm.load ne la garde pas) et plage u de chaque (objet, matiere)
    mats, urange = {}, {}
    cur, mat, k = None, None, {}
    for l in open(src, encoding='utf-8', errors='replace'):
        p = l.split()
        if not p: continue
        if p[0] == 'o': cur = l[2:].strip(); k.setdefault(cur, 0)
        elif p[0] == 'usemtl': mat = l[7:].strip()
        elif p[0] == 'f':
            mats[(cur, k[cur])] = mat; k[cur] += 1
            for c in p[1:]:
                s = c.split('/')
                if len(s) > 1 and s[1]:
                    u = vt[int(s[1]) - 1][0]; r = urange.setdefault((cur, mat), [u, u]); r[0] = min(r[0], u); r[1] = max(r[1], u)
    strap = lambda n, i: (lambda r: r is not None and (r[0] < -0.1 or r[1] > 1.1))(urange.get((n, mats.get((n, i)))))
    flip = det(xf) > 0   # (obj2mwm.build suppose un miroir)
    cx = lambda f: sum(xf(*v[a])[0] for (a, b, c) in f) / len(f)
    sets = {}
    def add(name, n, i, f): sets.setdefault(name, []).append((f, strap(n, i)))
    for n, fl in groups.items():
        if not fl: continue
        ln = n.lower()
        ox = sum(cx(f) for f in fl) / len(fl)
        side = 'g' if ox < 0 else 'd'
        for i, f in enumerate(fl):
            if 'parts' in ln:
                x = cx(f)
                add('pieces-' + ('m' if abs(x) < MID else 'g' if x < 0 else 'd'), n, i, f)
            elif 'middle' in ln: add('milieu', n, i, f)
            elif 'handle' in ln: add('languette-' + side, n, i, f)
            elif 'fastened' in ln: add('bouclee-' + side, n, i, f)
            else: print('  objet ignore :', n)
    for name, fl in sorted(sets.items()):
        pos, nrm, uv, subs = [], [], [], [[], []]
        for st in (True, False):
            sel = [f for (f, s) in fl if s == st]
            if not sel: continue
            p, nn, u, t = obj2mwm.build(v, vt, vn, sel, xf)
            if flip: t = [t[i + j] for i in range(0, len(t), 3) for j in (0, 2, 1)]
            base = len(pos)
            pos += p; nrm += nn; uv += u
            subs[0 if st else 1] += [base + i for i in t]
        out = 'assets/ceintures/%s-%s.mwm' % (key, name)
        with open(out, 'wb') as o:
            o.write(b'MWM1'); o.write(struct.pack('<ii', 2, len(pos)))
            for q in pos: o.write(struct.pack('<fff', *q))
            for q in nrm: o.write(struct.pack('<fff', *q))
            for q in uv: o.write(struct.pack('<ff', *q))
            for tri in subs:
                o.write(struct.pack('<i', len(tri)))
                if tri: o.write(struct.pack('<%di' % len(tri), *tri))
                o.write(struct.pack('<fff', 1.0, 1.0, 1.0))
        xs = [q[0] for q in pos]; ys = [q[1] for q in pos]; zs = [q[2] for q in pos]
        print('%s : %d sommets, %d + %d triangles (sangle + metal), x %.2f..%.2f y %.2f..%.2f z %.2f..%.2f'
              % (out, len(pos), len(subs[0]) // 3, len(subs[1]) // 3, min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))

for key, src, xf in CARS:
    for f in os.listdir('assets/ceintures'):
        if f.startswith(key + '-') and f.endswith('.mwm'): os.remove(os.path.join('assets/ceintures', f))
    convert(key, src, xf)
