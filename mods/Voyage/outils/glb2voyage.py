"""Modele glTF (.glb, Sketchfab) -> maillage du mod Voyage (.vmesh) + textures de couleur (.jpg / .png), a embarquer dans
Voyage.dll (ressources). Un sous-maillage par matiere ; les noeuds dont le nom est donne par --a-part (ex. l'helice) sont
sortis en sous-maillages a eux, avec leur pivot. Repere Unity : y et z du modele echanges (z en haut
dans 3ds Max), triangles retournes, v des UV inverse ; centimetres -> metres (--echelle). Le noeud racine de Sketchfab (vue de la scene, inclinee) est ignore.
Usage : python -I glb2voyage.py modele.glb dossier_de_sortie prefixe [--echelle 0.01] [--a-part propelting] [--taille 1024]

Format .vmesh (petit-boutiste) : 'VOYM', int32 version (1), int32 n ; par sous-maillage : chaine nom, chaine texture,
octet transparent, float metal, float lisse, 3 floats pivot, int32 nv, nv*3 floats positions, nv*3 normales, nv*2 UV,
int32 ni, ni int32 indices. Chaine = int32 longueur + UTF-8."""
import io, json, os, struct, sys
import numpy as np
from PIL import Image


def load(path):
    b = open(path, 'rb').read()
    n = struct.unpack('<I', b[12:16])[0]
    j = json.loads(b[20:20 + n])
    off = 20 + n
    bl = struct.unpack('<I', b[off:off + 4])[0]
    return j, b[off + 8: off + 8 + bl]


def accessor(j, bin_, i):
    a = j['accessors'][i]; bv = j['bufferViews'][a['bufferView']]
    comp = {5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8}[a['componentType']]
    k = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}[a['type']]
    start = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
    stride = bv.get('byteStride', 0)
    size = np.dtype(comp).itemsize * k
    if stride and stride != size:
        raw = np.frombuffer(bin_, dtype=np.uint8, count=stride * (a['count'] - 1) + size, offset=start)
        rows = [np.frombuffer(raw[r * stride: r * stride + size].tobytes(), dtype=comp) for r in range(a['count'])]
        arr = np.array(rows)
    else:
        arr = np.frombuffer(bin_, dtype=comp, count=a['count'] * k, offset=start)
    return arr.reshape(a['count'], k) if k > 1 else arr


def local_matrix(node):
    if 'matrix' in node:
        return np.array(node['matrix'], dtype=np.float64).reshape(4, 4).T   # (glTF : colonnes)
    t = np.array(node.get('translation', [0, 0, 0]), dtype=np.float64)
    x, y, z, w = node.get('rotation', [0, 0, 0, 1])
    s = np.array(node.get('scale', [1, 1, 1]), dtype=np.float64)
    r = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                  [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                  [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
    m = np.eye(4); m[:3, :3] = r * s; m[:3, 3] = t
    return m


def main():
    args = sys.argv[1:]
    src, out, prefix = args[0], args[1], args[2]
    scale = float(args[args.index('--echelle') + 1]) if '--echelle' in args else 0.01
    apart = args[args.index('--a-part') + 1].split(',') if '--a-part' in args else []
    size = int(args[args.index('--taille') + 1]) if '--taille' in args else 1024
    os.makedirs(out, exist_ok=True)
    j, bin_ = load(src)
    nodes = j['nodes']
    root = j['scenes'][j.get('scene', 0)]['nodes'][0]

    # matrices monde (racine de Sketchfab remise a l'identite) ; groupe = matiere, ou nom du noeud a part
    groups = {}
    def walk(i, parent, group):
        nd = nodes[i]
        m = parent @ (np.eye(4) if i == root else local_matrix(nd))
        if nd.get('name') in apart: group = nd['name']
        if 'mesh' in nd:
            for p in j['meshes'][nd['mesh']]['primitives']:
                if p.get('mode', 4) != 4: continue
                pos = accessor(j, bin_, p['attributes']['POSITION']).astype(np.float64)
                nor = accessor(j, bin_, p['attributes']['NORMAL']).astype(np.float64)
                uv = accessor(j, bin_, p['attributes']['TEXCOORD_0']).astype(np.float64)
                idx = accessor(j, bin_, p['indices']).astype(np.int64).reshape(-1, 3)
                wp = (np.c_[pos, np.ones(len(pos))] @ m.T)[:, :3]
                nm = np.linalg.inv(m[:3, :3]).T
                wn = nor @ nm.T
                wn /= np.maximum(np.linalg.norm(wn, axis=1, keepdims=True), 1e-9)
                key = (group, p.get('material', 0))
                g = groups.setdefault(key, {'p': [], 'n': [], 'u': [], 'i': [], 'nv': 0, 'pivot': None})
                g['i'].append(idx + g['nv']); g['p'].append(wp); g['n'].append(wn); g['u'].append(uv); g['nv'] += len(wp)
                if group and g['pivot'] is None: g['pivot'] = m[:3, 3].copy()
        for c in nd.get('children', []): walk(c, m, group)
    walk(root, np.eye(4), None)

    mats = j['materials']
    def texture_of(mi):
        t = mats[mi].get('pbrMetallicRoughness', {}).get('baseColorTexture')
        return j['images'][j['textures'][t['index']]['source']] if t else None

    written = {}
    subs = []
    allp = np.concatenate([np.concatenate(g['p']) for g in groups.values()]) * scale
    for (group, mi), g in sorted(groups.items(), key=lambda kv: (kv[0][0] or '', kv[0][1])):
        mat = mats[mi]
        p = np.concatenate(g['p']) * scale
        n = np.concatenate(g['n']); u = np.concatenate(g['u']); idx = np.concatenate(g['i'])
        # Unity : le modele est en z vers le haut (3ds Max, sans la racine de Sketchfab) -> y et z echanges (haut = y,
        # nez = +z) : une symetrie, donc triangles retournes ; v des UV inverse
        p = p[:, [0, 2, 1]]; n = n[:, [0, 2, 1]]
        idx = idx[:, [0, 2, 1]]
        u = np.c_[u[:, 0], 1.0 - u[:, 1]]
        pivot = p.mean(0) if group else np.zeros(3)   # (piece a part : tourne autour de son centre, l'helice)
        transparent = mat.get('alphaMode') == 'BLEND'
        img = texture_of(mi)
        tex = ''
        if img is not None:
            name = '%s_%s.%s' % (prefix, mat['name'].lower(), 'png' if transparent else 'jpg')
            if name not in written:
                bv = j['bufferViews'][img['bufferView']]
                im = Image.open(io.BytesIO(bin_[bv.get('byteOffset', 0): bv.get('byteOffset', 0) + bv['byteLength']]))
                im = im.convert('RGBA' if transparent else 'RGB')
                lim = size // 2 if transparent else size   # (vitres : PNG lourd, 512 suffit)
                if max(im.size) > lim: im = im.resize((lim, lim * im.size[1] // im.size[0]), Image.LANCZOS)
                if transparent: im.save(os.path.join(out, name), optimize=True)
                else: im.save(os.path.join(out, name), quality=88, optimize=True)
                written[name] = os.path.getsize(os.path.join(out, name))
            tex = name
        pbr = mat.get('pbrMetallicRoughness', {})
        subs.append(dict(name=(group or mat['name']), tex=tex, transparent=transparent, metal=float(pbr.get('metallicFactor', 0.3)),
                         smooth=1.0 - float(pbr.get('roughnessFactor', 0.6)), pivot=pivot, p=p, n=n, u=u, i=idx.reshape(-1)))

    def s(b, t): e = t.encode('utf-8'); b.write(struct.pack('<i', len(e))); b.write(e)
    path = os.path.join(out, prefix + '.vmesh')
    with open(path, 'wb') as b:
        b.write(b'VOYM'); b.write(struct.pack('<ii', 1, len(subs)))
        for x in subs:
            s(b, x['name']); s(b, x['tex'])
            b.write(struct.pack('<Bff', 1 if x['transparent'] else 0, x['metal'], x['smooth']))
            b.write(struct.pack('<3f', *x['pivot']))
            b.write(struct.pack('<i', len(x['p']))); b.write(x['p'].astype('<f4').tobytes()); b.write(x['n'].astype('<f4').tobytes()); b.write(x['u'].astype('<f4').tobytes())
            b.write(struct.pack('<i', len(x['i']))); b.write(x['i'].astype('<i4').tobytes())
    mn, mx = allp.min(0), allp.max(0)
    print('boite (glTF, m) : min %s max %s' % (np.round(mn, 2), np.round(mx, 2)))
    for x in subs:
        c = x['p'].mean(0)
        print('  %-22s %6d sommets %6d triangles  tex %-26s centre %s pivot %s' % (x['name'], len(x['p']), len(x['i']) // 3, x['tex'], np.round(c, 2), np.round(x['pivot'], 2)))
    for k, v in written.items(): print('  %s %d Ko' % (k, v // 1024))
    print('%s : %d Ko' % (path, os.path.getsize(path) // 1024))


main()
