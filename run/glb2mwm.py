"""Drone (glTF .glb, Sketchfab, Z en haut, Y vers l'avant) -> maillages MWCoop (.mwm, voir fbx2mesh.py).
Simplification par regroupement de sommets sur une grille (le modele fait ~255 000 triangles ; vu de la taille d'une
tete, quelques milliers suffisent). Sortie : drone-corps.mwm (4 sous-maillages : noir, chrome, verre, gris) et
drone-heliceN.mwm (N = 0..3, une helice chacune, en coordonnees du drone : le mod la recentre sur son moyeu).
Repere Unity : (x, z, y) du glTF -- echange d'axes, donc ordre des triangles inverse. Usage :
  python -I glb2mwm.py drone.glb <dossier de sortie>"""
import struct, json, sys, os
import numpy as np

def load(path):
    b = open(path, 'rb').read()
    n = struct.unpack('<I', b[12:16])[0]
    j = json.loads(b[20:20 + n])
    off = 20 + n
    bl = struct.unpack('<I', b[off:off + 4])[0]
    bin_ = b[off + 8: off + 8 + bl]
    def acc(i):
        a = j['accessors'][i]; bv = j['bufferViews'][a['bufferView']]
        comp = {5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8}[a['componentType']]
        k = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}[a['type']]
        start = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
        arr = np.frombuffer(bin_, dtype=comp, count=a['count'] * k, offset=start)
        return arr.reshape(a['count'], k) if k > 1 else arr
    out = []
    for i, m in enumerate(j['meshes']):
        for p in m['primitives']:
            if p.get('mode', 4) != 4: continue   # (lignes : rien a afficher)
            pos = acc(p['attributes']['POSITION']).astype(np.float64)
            idx = acc(p['indices']).astype(np.int64).reshape(-1, 3)
            out.append((i, p.get('material', 0), pos, idx))
    return out

def simplify(pos, tri, cell):
    """Regroupement sur une grille : chaque cellule devient un sommet (moyenne), triangles degeneres et doublons retires."""
    key = np.floor(pos / cell).astype(np.int64)
    _, inv = np.unique(key, axis=0, return_inverse=True)
    inv = inv.reshape(-1)
    n = inv.max() + 1
    acc = np.zeros((n, 3)); cnt = np.zeros(n)
    np.add.at(acc, inv, pos); np.add.at(cnt, inv, 1)
    newpos = acc / cnt[:, None]
    t = inv[tri]
    ok = (t[:, 0] != t[:, 1]) & (t[:, 1] != t[:, 2]) & (t[:, 0] != t[:, 2])
    t = t[ok]
    s = np.sort(t, axis=1)
    _, first = np.unique(s, axis=0, return_index=True)
    t = t[np.sort(first)]
    used = np.unique(t)
    remap = -np.ones(n, dtype=np.int64); remap[used] = np.arange(len(used))
    return newpos[used], remap[t]

def normals(pos, tri):
    fn = np.cross(pos[tri[:, 1]] - pos[tri[:, 0]], pos[tri[:, 2]] - pos[tri[:, 0]])
    vn = np.zeros_like(pos)
    for k in range(3): np.add.at(vn, tri[:, k], fn)
    l = np.linalg.norm(vn, axis=1); l[l == 0] = 1
    return vn / l[:, None]

def to_unity(p):
    return np.stack([p[:, 0], p[:, 2], p[:, 1]], axis=1)

COLORS = [(0.03, 0.03, 0.03), (0.75, 0.75, 0.78), (0.15, 0.25, 0.35), (0.21, 0.21, 0.22)]

def write(path, subs):
    """subs : liste de (pos Unity, tri (ordre deja inverse), couleur)."""
    allp, alln, alli, base = [], [], [], 0
    for p, t, c in subs:
        allp.append(p); alln.append(normals(p, t)); alli.append((t + base, c)); base += len(p)
    P = np.concatenate(allp); N = np.concatenate(alln)
    with open(path, 'wb') as f:
        f.write(b'MWM1'); f.write(struct.pack('<ii', len(subs), len(P)))
        f.write(P.astype('<f4').tobytes()); f.write(N.astype('<f4').tobytes())
        f.write(np.zeros((len(P), 2), '<f4').tobytes())
        for t, c in alli:
            f.write(struct.pack('<i', t.size)); f.write(t.astype('<i4').tobytes()); f.write(struct.pack('<3f', *c))
    return len(P), sum(t.shape[0] for t, _ in alli)

def main(src, dst):
    parts = load(src)
    os.makedirs(dst, exist_ok=True)
    body = {m: [] for m in range(4)}
    props = None
    for i, mat, pos, idx in parts:
        if i == 0: props = (pos, idx); continue
        body[mat].append((pos, idx))
    subs = []
    for m in range(4):
        if not body[m]: subs.append((np.zeros((3, 3)), np.array([[0, 1, 2]]), COLORS[m])); continue
        P, T, base = [], [], 0
        for pos, idx in body[m]: P.append(pos); T.append(idx + base); base += len(pos)
        p, t = simplify(np.concatenate(P), np.concatenate(T), 0.018)
        subs.append((to_unity(p), t[:, ::-1], COLORS[m]))
    print('corps', write(os.path.join(dst, 'drone-corps.mwm'), subs))
    # Helices : une par quart (signe de x, signe de y autour du centre des moyeux).
    pos, idx = props
    cy = (pos[:, 1].min() + pos[:, 1].max()) / 2
    tc = pos[idx].mean(axis=1)
    q = (tc[:, 0] > 0).astype(int) + 2 * (tc[:, 1] > cy).astype(int)
    for k in range(4):
        t = idx[q == k]
        used = np.unique(t); remap = -np.ones(len(pos), dtype=np.int64); remap[used] = np.arange(len(used))
        p, tt = simplify(pos[used], remap[t], 0.022)
        print('helice', k, write(os.path.join(dst, 'drone-helice%d.mwm' % k), [(to_unity(p), tt[:, ::-1], COLORS[0])]),
              'moyeu', to_unity(((pos[used].min(0) + pos[used].max(0)) / 2)[None])[0].round(3))

if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
