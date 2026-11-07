"""Carnet de notes du copilote (JD 07/11, spec MWCoop-carnet\\SPEC-carnet-copilote.md) : modele et page.

    python -I run/carnet/carnet.py            -> assets/carnet/carnet.pak et assets/carnet/page.png
    python -I run/carnet/carnet.py --apercu   plus run/carnet/apercu-carnet.png (vues a plat, pas commitee)

Modele : « Single spiral notepad » par Sousinho, CC BY 4.0 (credit obligatoire), sketchfab.com/3d-models/single-spiral-notepad-
80299283fefc44649ae00bf4992b7ba2 -- fichier MWCoop-carnet\\single_spiral_notepad.glb. Meme format que assets/objets/objets.pak
(MWOB 2, voir run/objets/objets.py ; lu par Sync/KauppaModeles.cs), un objet « carnet ».
Repere du mod : debout, en portrait (21 x 30 cm), page vers -z (vers qui le lit), spirale a gauche (-x), centre sur sa boite.
Pieces :
  « page »     la feuille du dessus, UV 0..1 sur toute la feuille (u vers la droite, v vers le haut) : le mod y pose sa page dessinee ;
  « feuilles » les 34 autres feuilles (texture du modele, reduite) ;
  « spirale »  la spirale (soudee puis reduite : 30 000 triangles dans le fichier), couleur unie.
Points : page_min / page_max (coins de la feuille du dessus, repere du mod).
Page : 1024 x 1448 (rapport des feuilles), papier creme, lignes bleues et marge rouge comme la page du modele (sa texture n'a que
368 x 520 pixels pour la page) ; espacement des lignes et marge d'apres le croquis de JD (MWCoop-carnet\\croquis-jd.png).
"""
import io, json, os, struct, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ICI = os.path.dirname(os.path.abspath(__file__))
RACINE = os.path.dirname(os.path.dirname(ICI))
OUT = os.path.join(RACINE, "assets", "carnet")
SRC = r"D:\1 - AI\ClaudeAI\MWCoop-carnet\single_spiral_notepad.glb"
sys.path.insert(0, r"D:\1 - AI\ClaudeAI\MWCoop-voiture\outils\glb")
import glb as glbmod          # noqa: E402
from reduire import reduire   # noqa: E402

PAGE_W, PAGE_H = 1024, 1448


def ecrit_chaine(f, s):
    b = s.encode("utf-8"); n = len(b)
    while True:
        x = n & 0x7F; n >>= 7
        f.write(bytes([x | (0x80 if n else 0)]))
        if not n: break
    f.write(b)


def souder(P, N, U, I, tol=1e-5):
    key = np.round(P / tol).astype(np.int64)
    _, first, inv = np.unique(key, axis=0, return_index=True, return_inverse=True)
    inv = inv.ravel()
    N2 = np.zeros((len(first), 3)); np.add.at(N2, inv, N); N2 /= np.maximum(np.linalg.norm(N2, axis=1, keepdims=True), 1e-9)
    I2 = inv[I]
    ok = (I2[:, 0] != I2[:, 1]) & (I2[:, 1] != I2[:, 2]) & (I2[:, 0] != I2[:, 2])
    return P[first], N2, U[first], I2[ok]


def modele(apercu):
    g = glbmod.load(SRC)
    raw = open(SRC, "rb").read()
    jl = struct.unpack_from("<I", raw, 12)[0]
    # rotation (apres le passage au repere d'Unity, x inverse) : spirale (axe x du fichier) -> -y, normale de la page (+y) -> -z,
    # largeur de la page (+z, depuis la spirale) -> +x
    R = np.array([[0, 0, 1], [-1, 0, 0], [0, -1, 0]], float)
    parts = {"spirale": [], "feuilles": [], "page": []}
    top = max((m for m in g.meshes if "papers" in m.name), key=lambda m: float(((np.c_[m.pos, np.ones(len(m.pos))] @ m.matrix)[:, 1]).mean()))
    for m in g.meshes:
        P = (np.c_[m.pos, np.ones(len(m.pos))] @ m.matrix)[:, :3]
        Nn = m.normal @ m.matrix[:3, :3]
        Nn /= np.maximum(np.linalg.norm(Nn, axis=1, keepdims=True), 1e-9)
        P[:, 0] *= -1; Nn[:, 0] *= -1
        P = P @ R.T; Nn = Nn @ R.T
        I = np.asarray(m.idx, np.int64).reshape(-1, 3)
        if np.linalg.det(m.matrix[:3, :3]) > 0: I = I[:, ::-1]
        fn = np.cross(P[I[:, 1]] - P[I[:, 0]], P[I[:, 2]] - P[I[:, 0]])
        vn = Nn[I[:, 0]] + Nn[I[:, 1]] + Nn[I[:, 2]]
        bad = (fn * vn).sum(1) < 0
        I[bad] = I[bad][:, ::-1]
        U = np.asarray(m.uv, float).copy()
        key = "spirale" if "spiral" in m.name else "page" if m is top else "feuilles"
        parts[key].append((P, Nn, U, I))
    allP = np.concatenate([p[0] for v in parts.values() for p in v])
    lo, hi = allP.min(0), allP.max(0); ctr = (lo + hi) / 2
    pieces = []
    for key in ("page", "feuilles", "spirale"):
        P = []; Nn = []; U = []; I = []; base = 0
        for p in parts[key]:
            P.append(p[0] - ctr); Nn.append(p[1]); U.append(p[2]); I.append(p[3] + base); base += len(p[0])
        P = np.concatenate(P); Nn = np.concatenate(Nn); U = np.concatenate(U); I = np.concatenate(I)
        tri0 = len(I)
        if key == "spirale":
            P, Nn, U, I = souder(P, Nn, U, I)
            I = reduire(P, Nn, U, I, 0.00035)
            used = np.unique(I); remap = -np.ones(len(P), np.int64); remap[used] = np.arange(len(used))
            P, Nn, U, I = P[used], Nn[used], U[used], remap[I]
        if key == "page":
            # UV sur toute la feuille, dans le repere du mod : u vers +x (la droite), v vers +y (le haut)
            pl, ph = P.min(0), P.max(0)
            U = np.c_[(P[:, 0] - pl[0]) / (ph[0] - pl[0]), (P[:, 1] - pl[1]) / (ph[1] - pl[1])]
            page_min, page_max = pl, ph
        else:
            U = np.c_[U[:, 0], 1.0 - U[:, 1]]   # (glTF : v vers le bas)
        print("%-9s %6d -> %5d triangles, %5d sommets" % (key, tri0, len(I), len(P)))
        pieces.append([key, P, Nn, U, I])
    size = hi - lo
    print("carnet : taille %.3f x %.3f x %.3f m, page %s .. %s" % (*size, np.round(page_min, 4), np.round(page_max, 4)))
    # textures : celle du modele (feuilles) en 512 ; la spirale en couleur unie (moyenne de la texture sous ses UV)
    j = json.loads(raw[20:20 + jl]); off = 20 + jl + 8
    bv = j["bufferViews"][j["images"][j["textures"][j["materials"][0]["pbrMetallicRoughness"]["baseColorTexture"]["index"]]["source"]]["bufferView"]]
    tex = Image.open(io.BytesIO(raw[off + bv.get("byteOffset", 0):off + bv.get("byteOffset", 0) + bv["byteLength"]])).convert("RGB")
    a = np.asarray(tex, float) / 255.0
    sp = [p for p in pieces if p[0] == "spirale"][0]
    uv = np.clip(sp[3], 0, 1)
    xs = (uv[:, 0] * (tex.width - 1)).astype(int); ys = ((1.0 - uv[:, 1]) * (tex.height - 1)).astype(int)
    spc = a[ys, xs].mean(0)
    print("spirale : couleur moyenne de la texture", np.round(spc, 3))
    buf = io.BytesIO(); tex.resize((512, 512), Image.LANCZOS).save(buf, "JPEG", quality=88)
    texlist = [("carnet_feuilles.jpg", buf.getvalue())]
    out = []
    for key, P, Nn, U, I in pieces:
        ti = 0 if key == "feuilles" else -1
        col = [1.0, 1.0, 1.0, 1.0] if key != "spirale" else [float(spc[0]), float(spc[1]), float(spc[2]), 1.0]
        out.append((key, ti, col, P, Nn, U, I, 0))
    points = {"page_min": page_min, "page_max": page_max}
    if apercu: vues(out)
    return size, texlist, out, points


def vues(pieces):
    W = 420
    img = Image.new("RGB", (W * 2, W), (40, 44, 52)); d = ImageDraw.Draw(img)
    allP = np.concatenate([p[3] for p in pieces]); lo, hi = allP.min(0), allP.max(0); span = float((hi - lo).max()) * 1.15
    cols = {"page": (235, 232, 220), "feuilles": (180, 180, 170), "spirale": (120, 140, 200)}
    for k, (ax, ay, flipx) in enumerate(((0, 1, False), (2, 1, False))):   # face (-z : x a droite, y en haut), cote
        for name, ti, col, P, Nn, U, I, fl in pieces:
            for t in I[:: max(1, len(I) // 4000)]:
                pts = [((P[v, ax] - (lo[ax] + hi[ax]) / 2) / span * W + W / 2 + k * W, W / 2 - (P[v, ay] - (lo[ay] + hi[ay]) / 2) / span * W) for v in t]
                d.polygon(pts, outline=cols[name])
    img.save(os.path.join(ICI, "apercu-carnet.png"))


def page():
    """papier creme, lignes bleues, marge rouge pres de la spirale (a gauche), trous ; d'apres le modele (couleurs) et le
    croquis de JD (espacement : 4,6 % de la hauteur, marge a 13 % de la largeur)"""
    S = 2   # (dessine en double, reduit : traits nets ; papier uni -- un grain ne se voit pas et pesait 450 Ko dans la DLL)
    W, H = PAGE_W * S, PAGE_H * S
    im = Image.new("RGB", (W, H), (250, 246, 233))
    d = ImageDraw.Draw(im)
    blue, red = (170, 206, 236), (226, 150, 142)
    step = 0.046 * H
    y = 0.075 * H
    while y < H - 0.03 * H:
        d.line([(0, y), (W, y)], fill=blue, width=2 * S)
        y += step
    xm = 0.13 * W
    d.line([(xm, 0), (xm, H)], fill=red, width=2 * S)
    # trous de la spirale (sous les spires, pres du bord gauche)
    for k in range(44):
        cy = (k + 0.5) / 44 * H; cx = 0.035 * W; r = 0.0085 * W
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(214, 210, 200), outline=(190, 186, 176), width=S)
    im = im.resize((PAGE_W, PAGE_H), Image.LANCZOS)
    p = os.path.join(OUT, "page.png")
    im.save(p, optimize=True)
    print("page :", p, os.path.getsize(p) // 1024, "Ko")


def hintalappu():
    """etiquette de prix de l'Osuuskauppa (meme style que hintalappu-vhs.png / kino-hintalappu-kela.png)"""
    from PIL import ImageFont
    F = r"C:\Windows\Fonts"
    def text_center(d, xy, s, f, fill):
        b = d.textbbox((0, 0), s, font=f)
        d.text((xy[0] - (b[2] + b[0]) / 2, xy[1] - (b[3] + b[1]) / 2), s, font=f, fill=fill)
    W, H = 256, 128
    im = Image.new("RGB", (W, H), (240, 200, 50))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W - 1, H - 1], outline=(200, 150, 30), width=3)
    text_center(d, (W / 2, 24), "MUISTIVIHKO", ImageFont.truetype(os.path.join(F, "arialbd.ttf"), 26), (30, 30, 30))
    text_center(d, (W / 2, 80), "59 mk", ImageFont.truetype(os.path.join(F, "impact.ttf"), 62), (180, 30, 30))
    p = os.path.join(OUT, "hintalappu-carnet.png")
    im.save(p)
    print("etiquette :", p)


def police():
    """texte libre des notes : Caveat (SIL OFL 1.1, assets/carnet/Caveat-OFL.txt) rendue en atlas de glyphes blancs (alpha) :
    assets/carnet/caveat.png + caveat.txt (« taille hauteur_de_ligne ascendante », puis par glyphe « code x y l h dx dy avance »,
    pixels de l'atlas, y vers le bas). Latin, accents, cyrillique (pas d'arabe dans Caveat)."""
    from PIL import ImageFont
    size = 60
    f = ImageFont.truetype(os.path.join(OUT, "Caveat-Regular.ttf"), size)
    chars = [chr(c) for c in range(33, 127)] + [chr(c) for c in range(0xA1, 0x100) if c not in (0xAD,)] + list("ŒœŸ€’“”«»–—…") \
        + [chr(c) for c in range(0x410, 0x450)] + ["Ё", "ё"]
    asc, desc = f.getmetrics()
    W = 1024
    glyphs = []
    x = y = 2; rowh = 0
    img = Image.new("L", (W, 1024), 0)
    d = ImageDraw.Draw(img)
    for ch in chars:
        b = f.getbbox(ch)   # (x0, y0, x1, y1) depuis l'origine (haut de la ligne)
        if b[2] <= b[0] or b[3] <= b[1]: continue
        w, h = b[2] - b[0] + 2, b[3] - b[1] + 2
        if x + w > W - 2: x = 2; y += rowh + 2; rowh = 0
        d.text((x - b[0] + 1, y - b[1] + 1), ch, font=f, fill=255)
        adv = f.getlength(ch)
        glyphs.append((ord(ch), x, y, w, h, b[0] - 1, b[1] - 1, adv))
        x += w + 2; rowh = max(rowh, h)
    H = 1 << (y + rowh + 2 - 1).bit_length()
    img = img.crop((0, 0, W, H))
    rgba = Image.merge("RGBA", (Image.new("L", img.size, 255),) * 3 + (img,))
    rgba.save(os.path.join(OUT, "caveat.png"), optimize=True)
    space = f.getlength(" ")
    with open(os.path.join(OUT, "caveat.txt"), "w", encoding="utf-8", newline="\n") as o:
        o.write("%d %d %d %.2f\n" % (size, asc + desc, asc, space))
        for g in glyphs: o.write("%d %d %d %d %d %d %d %.2f\n" % g)
    print("police : %d glyphes, atlas %dx%d, %d Ko" % (len(glyphs), W, H, os.path.getsize(os.path.join(OUT, "caveat.png")) // 1024))


def main():
    apercu = "--apercu" in sys.argv
    os.makedirs(OUT, exist_ok=True)
    page()
    hintalappu()
    police()
    size, texlist, pieces, points = modele(apercu)
    p = os.path.join(OUT, "carnet.pak")
    with open(p, "wb") as f:
        f.write(b"MWOB"); f.write(struct.pack("<ii", 2, 1))
        ecrit_chaine(f, "carnet"); f.write(struct.pack("<3f", *[float(x) for x in size]))
        f.write(struct.pack("<i", len(texlist)))
        for tn, data in texlist: ecrit_chaine(f, tn); f.write(struct.pack("<i", len(data))); f.write(data)
        f.write(struct.pack("<i", len(pieces)))
        for pn, ti, col, P, Nn, U, I, flags in pieces:
            assert len(P) < 65000
            ecrit_chaine(f, pn); f.write(struct.pack("<i4fi", ti, *col[:4], flags))
            f.write(struct.pack("<i", len(P))); f.write(P.astype("<f4").tobytes()); f.write(Nn.astype("<f4").tobytes())
            f.write(U.astype("<f4").tobytes())
            f.write(struct.pack("<i", I.size)); f.write(I.astype("<i4").ravel().tobytes())
        f.write(struct.pack("<i", len(points)))
        for k, v in points.items(): ecrit_chaine(f, k); f.write(struct.pack("<3f", *[float(x) for x in v]))
    print("ecrit", p, os.path.getsize(p) // 1024, "Ko")


if __name__ == "__main__":
    main()
