"""Fichiers du casque personnalise pour le lanceur (section « Casque », JD 07/11), dans dist/files/MWCoop/casque/ :

    gabarit-casque.png      1024 x 1024 : la coque depliee (un disque vu du dessus), reperes, zones hors coque grisees
    defaut-casque.png       1024 x 1024 : le casque par defaut (masque de la deco, colore)
    apercu-<k>-uv.png       400 x 400, RGBA : R = u x 255, G = v x 255, B = eclairage, A = 255 ou la coque est visible
    apercu-<k>-dessus.png   400 x 400, RGBA (alpha normal) : le reste du casque (visiere fumee, joint, interieur), a poser par-dessus

    python -I run/objets/casque_lanceur.py         (OpenGL, moderngl, sur la machine de travail)

Composition (lanceur) : base = A > 0 ? texture(u x (L-1), (1-v) x (H-1)) x B / 255 : transparent ; resultat = dessus « over » base.
"""
import io, math, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont
import moderngl

ICI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, ICI)
import objets   # noqa: E402

OUT = os.path.join(os.path.dirname(os.path.dirname(ICI)), "dist", "files", "MWCoop", "casque")
FOND, BANDE = (0.82, 0.12, 0.10), (0.95, 0.95, 0.92)   # (casque par defaut : rouge, bande blanche)


def persp(fov, asp, n, f):
    t = 1 / math.tan(math.radians(fov) / 2)
    return np.array([[t / asp, 0, 0, 0], [0, t, 0, 0], [0, 0, (f + n) / (n - f), 2 * f * n / (n - f)], [0, 0, -1, 0]], "f4")


def look(eye, at):
    f = at - eye; f /= np.linalg.norm(f); up = np.array([0, 1, 0.0]); s = np.cross(f, up); s /= np.linalg.norm(s); u = np.cross(s, f)
    m = np.eye(4, dtype="f4"); m[0, :3] = s; m[1, :3] = u; m[2, :3] = -f; m[:3, 3] = -m[:3, :3] @ eye
    return m


VS = """#version 330
uniform mat4 mvp; in vec3 p; in vec3 n; in vec2 uv; out vec3 vn; out vec2 vuv;
void main(){ gl_Position = mvp * vec4(p, 1.0); vn = n; vuv = uv; }"""
# mode 0 : coque -> (u, v, eclairage, 1) ; 1 : autre piece opaque -> transparent (profondeur seule) ;
# mode 2 : coque -> transparent (profondeur seule) ; 3 : autre piece -> sa couleur eclairee ; 4 : vitre -> couleur, alpha
FS = """#version 330
uniform int mode; uniform vec4 col; uniform sampler2D tex; uniform int hastex; in vec3 vn; in vec2 vuv; out vec4 o;
void main(){
  vec3 L = normalize(vec3(-0.4, 0.8, -0.6)); float d = abs(dot(normalize(vn), L)); float sh = clamp(0.38 + 0.72 * d, 0.0, 1.0);
  if (mode == 0) { o = vec4(vuv.x, vuv.y, sh, 1.0); return; }
  if (mode == 1 || mode == 2) { o = vec4(0.0); return; }
  vec4 c = col; if (hastex == 1) c.rgb *= texture(tex, vuv).rgb;
  o = vec4(c.rgb * sh, mode == 4 ? c.a : 1.0);
}"""


def main():
    os.makedirs(OUT, exist_ok=True)
    _, size, texlist, pieces, points = objets.build("casque", objets.OBJETS["casque"], False)
    imgs = [Image.open(io.BytesIO(d)).convert("RGB") for _, d in texlist]
    coque = next(p for p in pieces if p[0] == "coque")
    allP = np.concatenate([p[3] for p in pieces]); c = (allP.min(0) + allP.max(0)) / 2; span = float((allP.max(0) - allP.min(0)).max())
    # --- casque par defaut : le masque colore
    m = np.asarray(imgs[coque[1]].resize((1024, 1024), Image.LANCZOS).convert("L"), float)[..., None] / 255.0
    defaut = (np.array(FOND) * (1 - m) + np.array(BANDE) * m) * 255
    Image.fromarray(defaut.astype("uint8")).save(os.path.join(OUT, "defaut-casque.png"), optimize=True)
    # --- gabarit : iles de la coque, bords, reperes
    W = 1024
    gab = Image.new("RGB", (W, W), (176, 178, 182)); dr = ImageDraw.Draw(gab)
    U, I = coque[5], coque[6]
    for t in I: dr.polygon([(U[k, 0] * (W - 1), (1 - U[k, 1]) * (W - 1)) for k in t], fill=(250, 250, 248))
    # (bords des iles : aretes d'un seul triangle)
    e = np.sort(np.r_[I[:, [0, 1]], I[:, [1, 2]], I[:, [2, 0]]], 1)
    ue, cnt = np.unique(e, axis=0, return_counts=True)
    key = lambda k: (round(U[k, 0] * 4096), round(U[k, 1] * 4096))
    for a, b2 in ue[cnt == 1]:
        dr.line([(U[a, 0] * (W - 1), (1 - U[a, 1]) * (W - 1)), (U[b2, 0] * (W - 1), (1 - U[b2, 1]) * (W - 1))], fill=(40, 40, 46), width=2)
    P = coque[3]
    try: font = ImageFont.truetype("arialbd.ttf", 26)
    except Exception: font = ImageFont.load_default()
    def mark(direction, label):
        d = np.array(direction, float); d /= np.linalg.norm(d)
        k = int(np.argmax(((P - c) / np.linalg.norm(P - c, axis=1, keepdims=True)) @ d))
        x, y = U[k, 0] * (W - 1), (1 - U[k, 1]) * (W - 1)
        dr.ellipse([x - 6, y - 6, x + 6, y + 6], fill=(220, 40, 40))
        dr.text((x + 10, y - 14), label, fill=(220, 40, 40), font=font)
    mark((0, 1, 0), "HAUT / TOP")
    mark((0, 0.25, -1), "AVANT / FRONT")
    mark((0, 0.25, 1), "ARRIERE / BACK")
    # (cotes du pilote : il regarde vers -z, sa droite est -x dans le repere d'Unity)
    mark((-1, 0.2, 0), "DROITE / RIGHT")
    mark((1, 0.2, 0), "GAUCHE / LEFT")
    f18 = ImageFont.truetype("arial.ttf", 18)
    dr.text((14, W - 62), "MWCoop - casque : peindre le blanc, le gris n'est pas vu ; textes : le haut des lettres vers le centre (HAUT)", fill=(40, 40, 46), font=f18)
    dr.text((14, W - 38), "MWCoop - helmet: paint the white area, grey is not visible; text: top of the letters toward the centre (TOP)", fill=(40, 40, 46), font=f18)
    gab.save(os.path.join(OUT, "gabarit-casque.png"), optimize=True)
    # --- apercus
    ctx = moderngl.create_standalone_context()
    prog = ctx.program(vertex_shader=VS, fragment_shader=FS)
    S = 400
    fbo = ctx.framebuffer(color_attachments=[ctx.texture((S, S), 4)], depth_attachment=ctx.depth_texture((S, S)))
    objs = []
    for p in pieces:
        Pp = (p[3] - c) * np.array([-1.0, 1, 1]); Nn = p[4] * np.array([-1.0, 1, 1])   # (repere d'Unity, main gauche : x retourne pour l'afficher a l'endroit)
        vbo = ctx.buffer(np.c_[Pp, Nn, p[5]].astype("f4").tobytes()); ibo = ctx.buffer(np.asarray(p[6], "i4").ravel().tobytes())
        vao = ctx.vertex_array(prog, [(vbo, "3f 3f 2f", "p", "n", "uv")], ibo)
        tex = None
        if p[1] >= 0 and p[0] != "coque":
            im = imgs[p[1]].transpose(Image.FLIP_TOP_BOTTOM); tex = ctx.texture(im.size, 3, im.tobytes())
        objs.append((p, vao, tex))
    d = span * 2.4
    views = [((-0.75 * d, 0.3 * d, -0.75 * d), (0, -0.01, 0)), ((0.65 * d, 0.32 * d, 0.8 * d), (0, -0.01, 0))]
    for k, (eye, at) in enumerate(views, 1):
        mvp = (persp(30, 1.0, 0.01, 50) @ look(np.array(eye, float), np.array(at, float))).T.copy()
        prog["mvp"].write(mvp.astype("f4").tobytes())
        layers = []
        for passe in (0, 1):
            fbo.use(); ctx.enable(moderngl.DEPTH_TEST); ctx.disable(moderngl.BLEND); ctx.depth_mask = True; fbo.clear(0, 0, 0, 0)
            for p, vao, tex in objs:
                if p[7] & 2: continue
                prog["mode"].value = (0 if p[0] == "coque" else 1) if passe == 0 else (2 if p[0] == "coque" else 3)
                prog["col"].value = tuple(p[2]); prog["hastex"].value = 1 if tex is not None else 0
                if tex is not None: tex.use(0); prog["tex"].value = 0
                vao.render()
            if passe == 1:
                ctx.enable(moderngl.BLEND); ctx.blend_func = (moderngl.SRC_ALPHA, moderngl.ONE_MINUS_SRC_ALPHA, moderngl.ONE, moderngl.ONE_MINUS_SRC_ALPHA); ctx.depth_mask = False
                for p, vao, tex in objs:
                    if not p[7] & 2: continue
                    prog["mode"].value = 4; prog["col"].value = tuple(p[2]); prog["hastex"].value = 0
                    vao.render()
            a = np.frombuffer(fbo.read(components=4), "uint8").reshape(S, S, 4)[::-1].copy()
            if passe == 1:
                al = a[..., 3:4].astype(float)
                rgb = np.where(al > 0, np.clip(a[..., :3] * 255.0 / np.maximum(al, 1), 0, 255), 0)   # (alpha normal, pas premultiplie)
                a = np.concatenate([rgb, al], 2).astype("uint8")
            layers.append(a)
        Image.fromarray(layers[0], "RGBA").save(os.path.join(OUT, "apercu-%d-uv.png" % k), optimize=True)
        Image.fromarray(layers[1], "RGBA").save(os.path.join(OUT, "apercu-%d-dessus.png" % k), optimize=True)
    print("ecrit", OUT, sorted(os.listdir(OUT)))


if __name__ == "__main__":
    main()
