"""ASCII FBX 7.x -> maillage MWCoop (.mwm) : sommets (pos, normale, uv) dedoublonnes, triangles par materiau.
Format (little endian) : 'MWM1', nSub(i32), nVert(i32), pos[n]*3f, nrm[n]*3f, uv[n]*2f, puis par sous-maillage :
nTri*3 (i32) indices, et couleur diffuse rgb (3f).
Repere : FBX Y en haut, droitier -> Unity gaucher (x inverse), unites en cm -> m (UnitScaleFactor)."""
import re, struct, sys, math

def arr(txt, name, start=0):
    m = re.search(name + r': \*(\d+) \{\s*a: ([^}]*)\}', txt[start:], re.S)
    if not m: return None, -1
    vals = [float(x) for x in m.group(2).replace('\n', '').split(',') if x.strip()]
    return vals, start + m.end()

def rot_matrix(rx, ry, rz):
    # FBX : ordre XYZ (rotation appliquee X puis Y puis Z), degres
    rx, ry, rz = [math.radians(a) for a in (rx, ry, rz)]
    cx, sx, cy, sy, cz, sz = math.cos(rx), math.sin(rx), math.cos(ry), math.sin(ry), math.cos(rz), math.sin(rz)
    X = [[1,0,0],[0,cx,-sx],[0,sx,cx]]; Y = [[cy,0,sy],[0,1,0],[-sy,0,cy]]; Z = [[cz,-sz,0],[sz,cz,0],[0,0,1]]
    mul = lambda A,B: [[sum(A[i][k]*B[k][j] for k in range(3)) for j in range(3)] for i in range(3)]
    return mul(Z, mul(Y, X))

def convert(src, dst, extra_rot=(0,0,0)):
    txt = open(src, encoding='utf-8', errors='replace').read()
    unit = float(re.search(r'"UnitScaleFactor", "double", "Number", "",([-\d.e]+)', txt).group(1))
    g = txt.index('Geometry:')
    V, _ = arr(txt, 'Vertices', g)
    P, _ = arr(txt, 'PolygonVertexIndex', g)
    P = [int(x) for x in P]
    ln = txt.index('LayerElementNormal', g)
    N, _ = arr(txt, 'Normals', ln)
    lu = txt.index('LayerElementUV', g)
    UVv, _ = arr(txt, 'UV', lu)
    UVi, _ = arr(txt, 'UVIndex', lu)
    UVi = [int(x) for x in UVi]
    lm = txt.find('LayerElementMaterial', g)
    M = None
    if lm >= 0:
        M, _ = arr(txt, 'Materials', lm)
        M = [int(x) for x in M]
    # rotation du modele parent (Null) : Lcl Rotation la plus proche apres la geometrie
    rots = re.findall(r'"Lcl Rotation", "Lcl Rotation", "", "A",([-\d.e]+),([-\d.e]+),([-\d.e]+)', txt[g:])
    R = rot_matrix(*[float(x) for x in rots[0]]) if rots else rot_matrix(0, 0, 0)
    R2 = rot_matrix(*extra_rot)
    def tr(v):
        x = [sum(R[i][k]*v[k] for k in range(3)) for i in range(3)]
        return [sum(R2[i][k]*x[k] for k in range(3)) for i in range(3)]
    mats = re.findall(r'Material: \d+, "Material::([^"]+)"[^{]*\{(.*?)\n\t\}', txt, re.S)
    colors = []
    for name, body in mats:
        c = re.search(r'"DiffuseColor", "Color", "", "A",([-\d.e]+),([-\d.e]+),([-\d.e]+)', body)
        colors.append((name, tuple(float(x) for x in c.groups()) if c else (1, 1, 1)))
    verts, key2i = [], {}
    subs = {}
    poly, pv0, polyIdx = [], 0, 0
    for k, raw in enumerate(P):
        last = raw < 0
        vi = ~raw if last else raw
        poly.append((vi, k))
        if last:
            mat = M[polyIdx] if M and len(M) > 1 else (M[0] if M else 0)
            ids = []
            for vi2, pk in poly:
                p = tr(V[vi2*3:vi2*3+3]); n = tr(N[pk*3:pk*3+3]); ui = UVi[pk]; u = UVv[ui*2:ui*2+2]
                key = (round(p[0],5), round(p[1],5), round(p[2],5), round(n[0],3), round(n[1],3), round(n[2],3), round(u[0],5), round(u[1],5))
                if key not in key2i:
                    key2i[key] = len(verts)
                    # gaucher : x inverse ; cm -> m
                    s = unit / 100.0
                    verts.append(((-p[0]*s, p[1]*s, p[2]*s), (-n[0], n[1], n[2]), (u[0], u[1])))
                ids.append(key2i[key])
            for t in range(1, len(ids) - 1):
                # (x inverse : ordre des sommets inverse)
                subs.setdefault(mat, []).extend([ids[0], ids[t+1], ids[t]])
            poly = []; polyIdx += 1
    xs = [v[0][0] for v in verts]; ys = [v[0][1] for v in verts]; zs = [v[0][2] for v in verts]
    # recentre : milieu en x/z, bas en y = 0
    cx, cz, by = (min(xs)+max(xs))/2, (min(zs)+max(zs))/2, min(ys)
    with open(dst, 'wb') as f:
        f.write(b'MWM1'); f.write(struct.pack('<ii', len(subs), len(verts)))
        for p, n, u in verts: f.write(struct.pack('<3f', p[0]-cx, p[1]-by, p[2]-cz))
        for p, n, u in verts: f.write(struct.pack('<3f', *n))
        for p, n, u in verts: f.write(struct.pack('<2f', *u))
        for m in sorted(subs):
            idx = subs[m]
            f.write(struct.pack('<i', len(idx))); f.write(struct.pack('<%di' % len(idx), *idx))
            col = colors[m][1] if m < len(colors) else (1, 1, 1)
            f.write(struct.pack('<3f', *col))
    print(dst, 'sommets', len(verts), 'triangles', sum(len(v) for v in subs.values())//3, 'sous-maillages', {m: len(v)//3 for m, v in subs.items()},
          'taille %.2f x %.2f x %.2f m' % (max(xs)-min(xs), max(ys)-min(ys), max(zs)-min(zs)), 'materiaux', colors, 'unite', unit, 'rotation', rots[:2])

if __name__ == '__main__':
    convert(sys.argv[1], sys.argv[2])
