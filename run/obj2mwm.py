"""Wavefront .obj (Blender) -> maillages MWCoop (.mwm, voir fbx2mesh.py), un fichier par liste d'objets 'o'.
Repere : celui de l'import Unity d'un .obj -- x oppose, donc ordre des triangles inverse ; faces a plus de 3 cotes en
eventail ; un sommet par triple (position, uv, normale) ; normales recalculees si absentes. Un seul sous-maillage.
Usage : python -I obj2mwm.py <modele.obj> <sortie.mwm> <objet1> [<objet2> ...]
  (ceintures arriere de la SORBET, modele de Menos, 25/10 : voir Sync/Seats.cs, RearModel)"""
import struct, sys

def load(path):
    v, vt, vn, groups, cur = [], [], [], {}, None
    for l in open(path, encoding='utf-8', errors='replace'):
        p = l.split()
        if not p: continue
        if p[0] == 'o': cur = l[2:].strip(); groups[cur] = []
        elif p[0] == 'v': v.append(tuple(map(float, p[1:4])))
        elif p[0] == 'vt': vt.append(tuple(map(float, p[1:3])))
        elif p[0] == 'vn': vn.append(tuple(map(float, p[1:4])))
        elif p[0] == 'f':
            idx = []
            for c in p[1:]:
                s = c.split('/')
                idx.append((int(s[0]) - 1, int(s[1]) - 1 if len(s) > 1 and s[1] else -1, int(s[2]) - 1 if len(s) > 2 and s[2] else -1))
            groups.setdefault(cur, []).append(idx)
    return v, vt, vn, groups

def build(v, vt, vn, faces):
    key, pos, nrm, uv, tri = {}, [], [], [], []
    for f in faces:
        ids = []
        for (a, b, c) in f:
            k = (a, b, c)
            if k not in key:
                key[k] = len(pos)
                x, y, z = v[a]; pos.append((-x, y, z))
                if c >= 0: nx, ny, nz = vn[c]; nrm.append((-nx, ny, nz))
                else: nrm.append(None)
                uv.append(vt[b] if b >= 0 else (0.0, 0.0))
            ids.append(key[k])
        for i in range(1, len(ids) - 1):
            tri += [ids[0], ids[i + 1], ids[i]]   # (x oppose : sens inverse)
    if any(n is None for n in nrm):
        acc = [[0.0, 0.0, 0.0] for _ in pos]
        for i in range(0, len(tri), 3):
            a, b, c = (pos[tri[i]], pos[tri[i + 1]], pos[tri[i + 2]])
            u = [b[k] - a[k] for k in range(3)]; w = [c[k] - a[k] for k in range(3)]
            n = (u[1] * w[2] - u[2] * w[1], u[2] * w[0] - u[0] * w[2], u[0] * w[1] - u[1] * w[0])
            for j in tri[i:i + 3]:
                for k in range(3): acc[j][k] += n[k]
        nrm = []
        for a in acc:
            l = sum(x * x for x in a) ** 0.5 or 1.0
            nrm.append(tuple(x / l for x in a))
    return pos, nrm, uv, tri

def write(path, pos, nrm, uv, tri):
    with open(path, 'wb') as o:
        o.write(b'MWM1'); o.write(struct.pack('<ii', 1, len(pos)))
        for p in pos: o.write(struct.pack('<fff', *p))
        for n in nrm: o.write(struct.pack('<fff', *n))
        for t in uv: o.write(struct.pack('<ff', *t))
        o.write(struct.pack('<i', len(tri))); o.write(struct.pack('<%di' % len(tri), *tri))
        o.write(struct.pack('<fff', 1.0, 1.0, 1.0))

if __name__ == '__main__':
    src, dst, names = sys.argv[1], sys.argv[2], sys.argv[3:]
    v, vt, vn, groups = load(src)
    faces = []
    for n in names:
        if n not in groups: sys.exit('objet absent : ' + n + ' (presents : ' + ', '.join(groups) + ')')
        faces += groups[n]
    pos, nrm, uv, tri = build(v, vt, vn, faces)
    write(dst, pos, nrm, uv, tri)
    xs = [p[0] for p in pos]
    print('%s : %d sommets, %d triangles, x %.2f .. %.2f' % (dst, len(pos), len(tri) // 3, min(xs), max(xs)))
