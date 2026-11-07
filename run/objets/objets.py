"""Modeles 3D des objets du mod (JD 07/11 : « changer les models temporaires ») : .glb de Sketchfab -> assets/objets/objets.pak,
embarque dans MWCoop.dll (ressource MWCoop.objets.pak, lue par Sync/ObjetsModeles.cs).

    python -I run/objets/objets.py              tout refaire (sources : MWCoop-voiture/objets/*.glb)
    python -I run/objets/objets.py --apercu     plus des vues a plat de chaque objet (run/objets/apercu-*.png, pas commitees)

Repere de sortie (celui du mod) : y en haut, facade vers -z, objet centre sur sa boite, en metres ; x de glTF inverse (repere
direct -> Unity), triangles retournes. Par objet : rotation, taille visee (le plus grand cote de la boite), seuil de reduction
(metres, outils/glb/reduire.py), texture de base seule (couleur), reduite. Une texture presque unie devient une couleur.

Format (BinaryWriter de C#, petit-boutiste, chaines prefixees 7 bits) :
  "MWOB" int32 1, int32 n ; par objet : chaine nom, 3 float taille, int32 nTex, (chaine nom, int32 n, octets jpg/png)*,
  int32 nPieces, par piece : chaine nom, int32 tex (-1 : aucune), 4 float couleur, int32 nv, 3nv float pos, 3nv float normales,
  2nv float uv, int32 ni, ni int32 ; int32 nPoints, (chaine nom, 3 float)* -- points remarquables (objectif, ecran...).
"""
import io, json, os, struct, sys
import numpy as np
from PIL import Image

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))), "assets", "objets", "objets.pak")
SRC = r"D:\1 - AI\ClaudeAI\MWCoop-voiture\objets"
GLB = r"D:\1 - AI\ClaudeAI\MWCoop-voiture\outils\glb"
sys.path.insert(0, GLB)
import glb as glbmod          # noqa: E402
from reduire import reduire   # noqa: E402

# nom : (fichier, rotation (degres, x y z, appliquee apres le passage au repere du mod), taille visee (un nombre : le plus grand
#        cote, echelle uniforme ; trois : la boite exacte, celle de l'ancien modele), seuil de reduction, cote des textures,
#        pieces ignorees (nom contient), triangles vises au plus[, options : "ecran" (matiere dont la piece devient l'ecran
#        « TVScreen » : sa texture recoupee a ses UV, UV ramenees a 0..1 pour la video), "couleur" (pieces sans texture)])
OBJETS = {
    "popcorn":      ("popcorn.glb",      (0, 0, 0),     0.24,                   0.0,    256, (), 0),
    "camescope":    ("camescope.glb",    (0, 90, 0),    0.30,                   0.0,    512, (), 0),      # (objectif vers +z)
    "tele":         ("tele.glb",         (0, 180, 0),   0.55,                   0.0,    512, (), 0, {"ecran": "monitor_screen"}),   # (ecran vers -z)
    "cassette":     ("cassette.glb",     (0, 180, 0),   (0.188, 0.025, 0.103),  0.0,    0,   (), 0),      # (etiquette de tranche vers -z)
    "bobine":       ("bobine.glb",       (0, 0, 0),     0.36,                   0.0006, 256, (), 9000),
    "magnetoscope": ("magnetoscope.glb", (0, 180, 0),   0.36,                   0.0006, 512, (), 9000),   # (facade vers -z)
    "gps":          ("gps.glb",          (-90, 180, 0), 0.165,                  0.0,    0,   (), 0, {"couleur": (0.24, 0.25, 0.27)}),   # (debout, ecran vers -z ; gris fonce, le modele n'a qu'un blanc)
}


def rot(deg):
    x, y, z = np.radians(deg)
    Rx = np.array([[1, 0, 0], [0, np.cos(x), -np.sin(x)], [0, np.sin(x), np.cos(x)]])
    Ry = np.array([[np.cos(y), 0, np.sin(y)], [0, 1, 0], [-np.sin(y), 0, np.cos(y)]])
    Rz = np.array([[np.cos(z), -np.sin(z), 0], [np.sin(z), np.cos(z), 0], [0, 0, 1]])
    return Ry @ Rx @ Rz   # (ordre d'Unity : z, x, y)


def srgb(c):
    return [float(min(1.0, max(0.0, v)) ** (1 / 2.2)) for v in c[:3]] + [float(c[3]) if len(c) > 3 else 1.0]


def texture(data, side):
    """octets de l'image -> (octets jpg ou png, couleur moyenne, unie ?)"""
    im = Image.open(io.BytesIO(data))
    alpha = im.mode in ("RGBA", "LA") and im.getchannel("A").getextrema()[0] < 250
    im = im.convert("RGBA" if alpha else "RGB")
    a = np.asarray(im.convert("RGB"), float)
    mean = a.reshape(-1, 3).mean(0) / 255.0
    flat = a.reshape(-1, 3).std(0).max() < 6.0
    if side and max(im.size) > side: im = im.resize((side, side), Image.LANCZOS)
    buf = io.BytesIO()
    if alpha: im.save(buf, "PNG", optimize=True)
    else: im.save(buf, "JPEG", quality=88)
    return buf.getvalue(), list(mean) + [1.0], flat


def build(name, spec, apercu):
    fn, deg, longest, err, side, skip, maxtri = spec[:7]
    opt = spec[7] if len(spec) > 7 else {}
    path = os.path.join(SRC, fn)
    g = glbmod.load(path)
    raw = open(path, "rb").read()
    R = rot(deg)
    # pieces -> repere du mod (x inverse), regroupees par matiere
    groups = {}
    for m in g.meshes:
        if any(s in m.name for s in skip): continue
        P = (np.c_[m.pos, np.ones(len(m.pos))] @ m.matrix)[:, :3]
        Nn = m.normal @ m.matrix[:3, :3]
        Nn /= np.maximum(np.linalg.norm(Nn, axis=1, keepdims=True), 1e-9)
        P[:, 0] *= -1; Nn[:, 0] *= -1
        P = P @ R.T; Nn = Nn @ R.T
        I = np.asarray(m.idx, np.int64).reshape(-1, 3)
        if np.linalg.det(m.matrix[:3, :3]) > 0: I = I[:, ::-1]   # (miroir en x : sens des faces retourne -- sauf piece deja en miroir dans le fichier, le flanc gauche de la tele)
        # (sens de chaque triangle d'apres les normales du fichier : les matieres « doubleSided » de Sketchfab cachent des faces
        # retournees -- un flanc de la tele etait vu de l'interieur)
        fn = np.cross(P[I[:, 1]] - P[I[:, 0]], P[I[:, 2]] - P[I[:, 0]])
        vn = Nn[I[:, 0]] + Nn[I[:, 1]] + Nn[I[:, 2]]
        bad = (fn * vn).sum(1) < 0
        I[bad] = I[bad][:, ::-1]
        grp = groups.setdefault(m.material, [[], [], [], []])
        base = sum(len(x) for x in grp[0])
        grp[0].append(P); grp[1].append(Nn); grp[2].append(np.asarray(m.uv, float).copy()); grp[3].append(I + base)
    allP = np.concatenate([np.concatenate(v[0]) for v in groups.values()])
    lo, hi = allP.min(0), allP.max(0)
    s = np.array(longest, float) / (hi - lo) if isinstance(longest, tuple) else longest / float((hi - lo).max())
    c = (lo + hi) / 2
    # textures et couleurs
    texmap, texlist, pieces = {}, [], []
    tris0 = tris1 = 0
    for mi, (Ps, Ns, Us, Is) in sorted(groups.items()):
        mat = g.materials[mi]
        P = (np.concatenate(Ps) - c) * s; Nn = np.concatenate(Ns); U = np.concatenate(Us); I = np.concatenate(Is)
        col = srgb(mat.props.get("baseColorFactor", [1, 1, 1, 1]))
        ti = -1
        tname = mat.maps.get("txDiffuse", "")
        pname = mat.name
        if "ecran" in opt and opt["ecran"] in mat.name and tname:
            # ecran : la partie de l'atlas qu'il couvre devient sa texture, UV de 0 a 1 (la video du mod la remplace)
            t = next(t for t in g.textures if t.name == tname)
            im = Image.open(io.BytesIO(raw[t.offset:t.offset + t.size])).convert("RGB")
            u0, u1, v0, v1 = U[:, 0].min(), U[:, 0].max(), U[:, 1].min(), U[:, 1].max()
            im = im.crop((int(u0 * im.width), int(v0 * im.height), int(np.ceil(u1 * im.width)), int(np.ceil(v1 * im.height)))).resize((256, 256), Image.LANCZOS)
            buf = io.BytesIO(); im.save(buf, "JPEG", quality=88)
            ti = len(texlist); texlist.append((name + "_ecran.jpg", buf.getvalue()))
            # (UV a plat, face a -z : u avec x, v avec y -- celles du fichier tournaient l'image d'un quart de tour)
            lo2, hi2 = P.min(0), P.max(0)
            U = np.c_[(P[:, 0] - lo2[0]) / (hi2[0] - lo2[0]), (P[:, 1] - lo2[1]) / (hi2[1] - lo2[1])]
            pname = "TVScreen"
            tris0 += len(I)
            pieces.append([pname, ti, col, P, Nn, U, I])
            continue
        if tname and side:
            t = next(t for t in g.textures if t.name == tname)
            if tname not in texmap:
                data, mean, flat = texture(raw[t.offset:t.offset + t.size], side)
                texmap[tname] = (-1 if flat else len(texlist), mean)
                if not flat: texlist.append((name + "_" + str(len(texlist)) + (".png" if data[:4] == b"\x89PNG" else ".jpg"), data))
            ti, mean = texmap[tname]
            if ti < 0: col = [col[0] * mean[0], col[1] * mean[1], col[2] * mean[2], col[3]]
        elif tname:
            t = next(t for t in g.textures if t.name == tname)
            _, mean, _ = texture(raw[t.offset:t.offset + t.size], 0)
            col = [col[0] * mean[0], col[1] * mean[1], col[2] * mean[2], col[3]]
        if ti < 0 and "couleur" in opt: col = list(opt["couleur"]) + [1.0]
        tris0 += len(I)
        pieces.append([pname, ti, col, P, Nn, U, I])
    # reduction : seuil donne, double tant que l'objet depasse le nombre de triangles vise
    if err > 0:
        e = err
        for _ in range(3):   # (au plus 2,25 fois le seuil : au-dela, les bords ronds deviennent des polygones)
            red = [reduire(p[3], p[4], p[5], p[6], e) for p in pieces]
            n = sum(len(r) for r in red)
            if not maxtri or n <= maxtri: break
            e *= 1.5
        for p, r in zip(pieces, red): p[6] = r
    # sommets inutilises retires
    for p in pieces:
        used = np.unique(p[6]); remap = -np.ones(len(p[3]), np.int64); remap[used] = np.arange(len(used))
        p[3], p[4], p[5], p[6] = p[3][used], p[4][used], p[5][used], remap[p[6]]
        tris1 += len(p[6])
    pieces = [p for p in pieces if len(p[6])]
    flat = [p for p in pieces if p[1] < 0]
    if len(flat) > 1 or (flat and len(pieces) > 1):
        cols = [p[2] for p in flat]
        B = 4   # (4 x 4 pixels par couleur : pas de debord au filtrage)
        pal = Image.new("RGB", (B * len(cols), B))
        for k, cc in enumerate(cols): pal.paste(tuple(int(255 * min(1, v)) for v in cc[:3]), (k * B, 0, k * B + B, B))
        buf = io.BytesIO(); pal.save(buf, "PNG")
        ti = len(texlist); texlist.append((name + "_pal.png", buf.getvalue()))
        P = []; Nn = []; U = []; I = []; base = 0
        for k, p in enumerate(flat):
            P.append(p[3]); Nn.append(p[4]); U.append(np.tile([(k * B + B / 2) / (B * len(cols)), 0.5], (len(p[3]), 1))); I.append(p[6] + base); base += len(p[3])
        pieces = [p for p in pieces if p[1] >= 0] + [["palette", ti, [1, 1, 1, 1], np.concatenate(P), np.concatenate(Nn), np.concatenate(U), np.concatenate(I)]]
    for p in pieces:
        if p[0] not in ("palette", "TVScreen"): p[5] = np.c_[p[5][:, 0], 1.0 - p[5][:, 1]]
        assert len(p[3]) < 65000, (name, p[0], len(p[3]))   # (Unity 5 : 65 535 sommets par maillage)
    allP = np.concatenate([p[3] for p in pieces])
    size = allP.max(0) - allP.min(0)
    print("%-13s %6d -> %5d triangles, %d pieces, %d textures (%d Ko), taille %.3f x %.3f x %.3f" % (
        name, tris0, tris1, len(pieces), len(texlist), sum(len(d) for _, d in texlist) // 1024, *size))
    if apercu: vues(name, pieces)
    return name, size, texlist, pieces


def vues(name, pieces):
    """trois vues a plat (face -z, cote +x, dessus), ombrage par la normale : pour verifier l'orientation"""
    W = 360
    img = Image.new("RGB", (W * 3, W), (40, 44, 52))
    px = img.load()
    allP = np.concatenate([p[3] for p in pieces]); lo, hi = allP.min(0), allP.max(0); span = float((hi - lo).max()) * 1.1
    for k, (ax, ay, depth, sign) in enumerate(((0, 1, 2, -1), (2, 1, 0, 1), (0, 2, 1, 1))):
        zb = np.full((W, W), -1e9)
        for p in pieces:
            P, Nn, I, col = p[3], p[4], p[6], p[2]
            for t in I:
                a, b, c = P[t]
                n = np.cross(b - a, c - a); ln = np.linalg.norm(n)
                if ln < 1e-12: continue
                n /= ln
                lit = 0.35 + 0.65 * abs(n[depth])
                xs = ((np.array([a[ax], b[ax], c[ax]]) - (lo[ax] + hi[ax]) / 2) / span + 0.5) * (W - 1)
                ys = (0.5 - (np.array([a[ay], b[ay], c[ay]]) - (lo[ay] + hi[ay]) / 2) / span) * (W - 1)
                if k == 2: ys = W - 1 - ys
                zs = np.array([a[depth], b[depth], c[depth]]) * (-sign)
                x0, x1 = int(max(0, xs.min())), int(min(W - 1, xs.max())); y0, y1 = int(max(0, ys.min())), int(min(W - 1, ys.max()))
                if x1 < x0 or y1 < y0: continue
                d = (ys[1] - ys[2]) * (xs[0] - xs[2]) + (xs[2] - xs[1]) * (ys[0] - ys[2])
                if abs(d) < 1e-9: continue
                for yy in range(y0, y1 + 1):
                    for xx in range(x0, x1 + 1):
                        l1 = ((ys[1] - ys[2]) * (xx - xs[2]) + (xs[2] - xs[1]) * (yy - ys[2])) / d
                        l2 = ((ys[2] - ys[0]) * (xx - xs[2]) + (xs[0] - xs[2]) * (yy - ys[2])) / d
                        l3 = 1 - l1 - l2
                        if l1 < -0.01 or l2 < -0.01 or l3 < -0.01: continue
                        z = l1 * zs[0] + l2 * zs[1] + l3 * zs[2]
                        if z <= zb[yy, xx]: continue
                        zb[yy, xx] = z
                        cc = (0.75, 0.75, 0.75) if p[1] >= 0 else col
                        px[k * W + xx, yy] = tuple(int(255 * min(1, v * lit)) for v in cc[:3])
    img.save(os.path.join(os.path.dirname(os.path.abspath(__file__)), "apercu-" + name + ".png"))


def ecrit_chaine(f, s):
    b = s.encode("utf-8"); n = len(b)
    while True:
        x = n & 0x7F; n >>= 7
        f.write(bytes([x | (0x80 if n else 0)]))
        if not n: break
    f.write(b)


def main():
    apercu = "--apercu" in sys.argv
    only = [a for a in sys.argv[1:] if not a.startswith("--")]
    objs = [build(n, s, apercu) for n, s in OBJETS.items() if not only or n in only]
    if only: return
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "wb") as f:
        f.write(b"MWOB"); f.write(struct.pack("<ii", 1, len(objs)))
        for name, size, texlist, pieces in objs:
            ecrit_chaine(f, name); f.write(struct.pack("<3f", *size))
            f.write(struct.pack("<i", len(texlist)))
            for tn, data in texlist: ecrit_chaine(f, tn); f.write(struct.pack("<i", len(data))); f.write(data)
            f.write(struct.pack("<i", len(pieces)))
            for pn, ti, col, P, Nn, U, I in pieces:
                ecrit_chaine(f, pn); f.write(struct.pack("<i4f", ti, *col[:4]))
                f.write(struct.pack("<i", len(P))); f.write(P.astype("<f4").tobytes()); f.write(Nn.astype("<f4").tobytes())
                Uf = U.copy(); f.write(Uf.astype("<f4").tobytes())
                f.write(struct.pack("<i", I.size)); f.write(I.astype("<i4").ravel().tobytes())
            f.write(struct.pack("<i", 0))
    print("ecrit", OUT, os.path.getsize(OUT) // 1024, "Ko")


if __name__ == "__main__":
    main()
