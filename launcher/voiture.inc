// (inclus par launcher.cpp, a cette place : VOITURE (couleur, finition, plaque VIN, peinture perso, bosses) -- decoupe de la refonte du 14/10)
// ---------------------------------------------------------------- onglet VOITURE (couleur de la CORRIS, apercu 3D)
// [Coop] CouleurVoiture=RRGGBB (vide : au hasard, comme le jeu). Le mod la pose sur la carrosserie a une NOUVELLE
// partie ; l'hote la donne aux invites.
// Apercu : <jeu>\MWCoop\cache\corris.mesh, exporte par le mod depuis le jeu du joueur (jamais livre avec le mod).
// Format petit-boutiste : "MWCM", u32 version (1), u32 n, puis n morceaux : u16 longueur + nom UTF-8, u8 peignable
// (1 = prend la couleur choisie), 3 x f32 couleur de base (0..1), u32 nb sommets + nb x (3 x f32) (repere de la
// voiture : x droite, y haut, z avant, metres, repere gauche d'Unity), u32 nb indices + nb x u32 (triangles).
// Rendu logiciel : perspective, tampon de profondeur, normales lissees par sommet (Gouraud), lumiere directionnelle
// + lumiere d'appoint + ambiante + reflet ; rendu en 2x puis reduit (antialiasing), vitres en transparence. Au plus
// ~30 images/s, et seulement quand l'onglet est ouvert.
struct CarSwatch { int rgb; const wchar_t *fr, *en; };
static const CarSwatch kSwatches[] = {   // teintes des annees 70-80
    { 0xD9C49C, L"Beige Sahara", L"Sahara beige" },
    { 0x8DB33A, L"Vert pomme", L"Apple green" },
    { 0xE0702A, L"Orange", L"Orange" },
    { 0x86B8E2, L"Bleu ciel", L"Sky blue" },
    { 0x1E2C52, L"Bleu marine", L"Navy blue" },
    { 0xB3221E, L"Rouge", L"Red" },
    { 0xEEECE4, L"Blanc", L"White" },
    { 0x161616, L"Noir", L"Black" },
    { 0x8E949B, L"Gris m\u00E9tal", L"Metallic grey" },
    { 0x5B3B22, L"Brun", L"Brown" },
    { 0xD3A21C, L"Jaune moutarde", L"Mustard yellow" },
    { 0x23452D, L"Vert fonc\u00E9", L"Dark green" },
    { 0x6C1B28, L"Bordeaux", L"Burgundy" },
    { 0x2A9C9A, L"Turquoise", L"Turquoise" },
    { 0x707A2C, L"Vert avocat", L"Avocado green" },
};
static const int kSwatchN = 1 + _countof(kSwatches);   // pastille 0 : au hasard
static const RectF kCarCard(264, 88, 640, 656), kCarPanel(920, 88, 344, 656);
static const RectF kCarView(284, 120, 600, 470);
static const float kPitchMin = 0.04f, kPitchMax = 0.55f;
static int g_carHot = -1;                // pastille survolee

struct CarPart { bool paint, glass; float r, g, b; };
struct CarModel {
    int state = 0;                       // 0 pas lu, 1 pret, -1 absent, -2 illisible
    std::wstring path;
    FILETIME mt = {};
    DWORD checkT = 0;
    std::vector<float> pos, nrm;         // 3 par sommet (nrm : normale lissee)
    std::vector<uint32_t> tri;           // 3 par triangle
    std::vector<uint16_t> triPart;
    std::vector<CarPart> parts;
    float mn[3], mx[3], c[3], dist;      // boite de cadrage (carrosserie), son centre, distance de la camera
    float fx;                            // cadrage : max |x/z| sur tous les angles
};
static CarModel g_car;
static float g_carYaw = 3.75f, g_carPitch = 0.28f;
static bool g_carDrag, g_carDirty = true;
static float g_carDragX, g_carDragY;
static DWORD g_carIdleT, g_carDrawT;     // fin du dernier glisser (la rotation reprend 2,5 s apres), dernier rendu
static std::vector<uint32_t> g_carBig, g_carPix;   // rendu 2x et image reduite (PARGB)
static std::vector<float> g_carZ;
static int g_carW, g_carH, g_carDrawnColor = -2;

struct CarRot { float cy, sy, cp, sp; };
static inline void CarXf(const CarRot &R, float x, float y, float z, float &X, float &Y, float &Z)
{
    float x1 = x * R.cy + z * R.sy, z1 = -x * R.sy + z * R.cy;   // lacet (autour de y)
    X = x1;
    Y = y * R.cp + z1 * R.sp;                                     // tangage : le dessus vient vers la camera
    Z = -y * R.sp + z1 * R.cp;
}

static bool CarParse(const std::vector<unsigned char> &d, CarModel &m)
{
    struct Raw { std::string name; bool paint; float col[3]; std::vector<float> v; std::vector<uint32_t> idx; float mn[3], mx[3]; };
    std::vector<Raw> raw;
    size_t p = 0;
    auto get = [&](void *out, size_t n) { if (n > d.size() - p) return false; memcpy(out, d.data() + p, n); p += n; return true; };
    char magic[4];
    uint32_t ver = 0, n = 0;
    if (!get(magic, 4) || memcmp(magic, "MWCM", 4) || !get(&ver, 4) || ver != 1 || !get(&n, 4) || n > 4096) return false;
    for (uint32_t i = 0; i < n; i++) {
        Raw r;
        uint16_t len = 0;
        uint8_t pe = 0;
        uint32_t nv = 0, ni = 0;
        if (!get(&len, 2) || len > d.size() - p) return false;
        r.name.assign((const char *)d.data() + p, len);
        p += len;
        if (!get(&pe, 1) || !get(r.col, 12) || !get(&nv, 4) || nv > (d.size() - p) / 12) return false;
        r.v.resize((size_t)nv * 3);
        if (nv && !get(r.v.data(), (size_t)nv * 12)) return false;
        if (!get(&ni, 4) || ni > (d.size() - p) / 4 || ni % 3) return false;
        r.idx.resize(ni);
        if (ni && !get(r.idx.data(), (size_t)ni * 4)) return false;
        bool ok = nv && ni;
        for (uint32_t k : r.idx) if (k >= nv) ok = false;
        for (int a = 0; a < 3; a++) { r.mn[a] = 1e9f; r.mx[a] = -1e9f; }
        for (size_t k = 0; ok && k < r.v.size(); k++) {
            float x = r.v[k];
            if (!(fabsf(x) < 100)) ok = false;   // NaN ou valeur absurde : morceau ignore
            r.mn[k % 3] = min(r.mn[k % 3], x);
            r.mx[k % 3] = max(r.mx[k % 3], x);
        }
        r.paint = pe != 0;
        if (ok) raw.push_back(std::move(r));
    }
    // Boite de cadrage : les morceaux peignables (la carrosserie), sinon tout.
    bool anyPaint = false;
    for (const Raw &r : raw) anyPaint |= r.paint;
    float mn[3] = { 1e9f, 1e9f, 1e9f }, mx[3] = { -1e9f, -1e9f, -1e9f };
    for (const Raw &r : raw)
        if (r.paint || !anyPaint) for (int a = 0; a < 3; a++) { mn[a] = min(mn[a], r.mn[a]); mx[a] = max(mx[a], r.mx[a]); }
    if (raw.empty() || mn[0] > mx[0]) return false;

    m.pos.clear(); m.nrm.clear(); m.tri.clear(); m.triPart.clear(); m.parts.clear();
    for (const Raw &r : raw) {
        // Morceau qui depasse nettement de la carrosserie (ceinture pendante, cable du frein a main sous la voiture) :
        // laisse de cote, il deformerait l'apercu.
        bool out = false;
        for (int a = 0; a < 3; a++) if (r.mn[a] < mn[a] - 0.15f || r.mx[a] > mx[a] + 0.15f) out = true;
        if (out || m.parts.size() >= 65535) continue;
        std::string low = r.name;
        for (char &ch : low) ch = (char)tolower((unsigned char)ch);
        CarPart cp;
        cp.paint = r.paint;
        cp.glass = !r.paint && (low.find("window") != std::string::npos || low.find("windshield") != std::string::npos || low.find("glass") != std::string::npos);
        cp.r = min(max(r.col[0], 0.0f), 1.0f); cp.g = min(max(r.col[1], 0.0f), 1.0f); cp.b = min(max(r.col[2], 0.0f), 1.0f);
        // Blanc pur hors carrosserie : matiere texturee dans le jeu (habitacle, pieces) ; gris fonce dans l'apercu.
        if (!cp.paint && !cp.glass && cp.r > 0.95f && cp.g > 0.95f && cp.b > 0.95f) { cp.r = 0.24f; cp.g = 0.24f; cp.b = 0.25f; }
        if (cp.glass) { cp.r = 0.45f; cp.g = 0.56f; cp.b = 0.66f; }   // vitres (givre du jeu en blanc) : verre bleute
        uint16_t pi = (uint16_t)m.parts.size();
        m.parts.push_back(cp);
        uint32_t base = (uint32_t)(m.pos.size() / 3);
        m.pos.insert(m.pos.end(), r.v.begin(), r.v.end());
        m.nrm.resize(m.pos.size(), 0.0f);
        for (size_t t = 0; t + 2 < r.idx.size(); t += 3) {
            uint32_t ia = base + r.idx[t], ib = base + r.idx[t + 1], ic = base + r.idx[t + 2];
            const float *a = &m.pos[ia * 3], *b = &m.pos[ib * 3], *c = &m.pos[ic * 3];
            float e1[3] = { b[0] - a[0], b[1] - a[1], b[2] - a[2] }, e2[3] = { c[0] - a[0], c[1] - a[1], c[2] - a[2] };
            float fn[3] = { e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0] };
            for (uint32_t v : { ia, ib, ic }) for (int k = 0; k < 3; k++) m.nrm[v * 3 + k] += fn[k];   // ponderee par l'aire
            m.tri.push_back(ia); m.tri.push_back(ib); m.tri.push_back(ic);
            m.triPart.push_back(pi);
        }
    }
    if (m.tri.empty()) return false;
    for (size_t v = 0; v + 2 < m.nrm.size(); v += 3) {
        float *nv = &m.nrm[v], l = sqrtf(nv[0] * nv[0] + nv[1] * nv[1] + nv[2] * nv[2]);
        if (l > 1e-12f) { nv[0] /= l; nv[1] /= l; nv[2] /= l; } else { nv[0] = 0; nv[1] = 1; nv[2] = 0; }
    }
    float rad = 0;
    for (int a = 0; a < 3; a++) {
        m.mn[a] = mn[a]; m.mx[a] = mx[a]; m.c[a] = (mn[a] + mx[a]) / 2;
        rad += (mx[a] - mn[a]) * (mx[a] - mn[a]) / 4;
    }
    m.dist = 3.2f * sqrtf(rad);
    // Largeur du cadrage : la boite tient dans l'image sous tous les angles (lacet complet, tangage permis) ; la
    // hauteur se cale au tangage actuel (CarCamera).
    m.fx = 1e-3f;
    for (int ia = 0; ia < 72; ia++)
        for (int ip = 0; ip <= 4; ip++) {
            float yaw = ia * 6.2831853f / 72, pitch = kPitchMin + (kPitchMax - kPitchMin) * ip / 4;
            CarRot R = { cosf(yaw), sinf(yaw), cosf(pitch), sinf(pitch) };
            for (int k = 0; k < 8; k++) {
                float X, Y, Z;
                CarXf(R, ((k & 1) ? mx[0] : mn[0]) - m.c[0], ((k & 2) ? mx[1] : mn[1]) - m.c[1], ((k & 4) ? mx[2] : mn[2]) - m.c[2], X, Y, Z);
                Z += m.dist;
                m.fx = max(m.fx, fabsf(X / Z));
            }
        }
    return true;
}

// Relit corris.mesh s'il a change (le mod le reecrit a chaque partie) ; au plus toutes les 2 s.
static void CarCheckFile()
{
    DWORD now = GetTickCount();
    std::wstring path = g_gameDir + L"MWCoop\\cache\\corris.mesh";
    if (g_car.state != 0 && path == g_car.path && now - g_car.checkT < 2000) return;
    g_car.checkT = now;
    WIN32_FILE_ATTRIBUTE_DATA a;
    bool exists = !g_gameDir.empty() && GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &a) && !(a.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY);
    if (!exists) {
        if (g_car.state != -1) { g_car = CarModel(); g_car.state = -1; g_car.checkT = now; g_carDirty = true; }
        g_car.path = path;
        return;
    }
    if (g_car.state != 0 && g_car.state != -1 && path == g_car.path && CompareFileTime(&a.ftLastWriteTime, &g_car.mt) == 0) return;
    CarModel m;
    std::vector<unsigned char> d;
    m.state = ReadAll(path, d) && CarParse(d, m) ? 1 : -2;
    m.path = path;
    m.mt = a.ftLastWriteTime;
    m.checkT = now;
    g_car = std::move(m);
    g_carDirty = true;
}

struct CarSV { float x, y, iz, r, g, b; };

// Triangle en pixels (rendu 2x), profondeur en 1/z ; alpha < 0 : opaque (ecrit la profondeur), sinon vitre melangee.
static void CarTri(const CarSV &v0, const CarSV &v1, const CarSV &v2, int W, int H, uint32_t *pix, float *zb, float alpha)
{
    float area = (v1.x - v0.x) * (v2.y - v0.y) - (v1.y - v0.y) * (v2.x - v0.x);
    if (fabsf(area) < 1e-4f) return;
    int x0 = max(0, (int)floorf(min(v0.x, min(v1.x, v2.x)))), x1 = min(W - 1, (int)ceilf(max(v0.x, max(v1.x, v2.x))));
    int y0 = max(0, (int)floorf(min(v0.y, min(v1.y, v2.y)))), y1 = min(H - 1, (int)ceilf(max(v0.y, max(v1.y, v2.y))));
    if (x0 > x1 || y0 > y1) return;
    float inv = 1.0f / area;
    // poids barycentriques au centre des pixels : w0 (arete v1-v2), w1 (arete v2-v0), w2 = 1 - w0 - w1 ; pas en x (a)
    // et en y (b), valeur au pixel (x0, y0) (r)
    float a0 = -(v2.y - v1.y) * inv, b0 = (v2.x - v1.x) * inv;
    float a1 = -(v0.y - v2.y) * inv, b1 = (v0.x - v2.x) * inv;
    float a2 = -(a0 + a1), b2 = -(b0 + b1);
    float px = x0 + 0.5f, py = y0 + 0.5f;
    float r0 = ((v2.x - v1.x) * (py - v1.y) - (v2.y - v1.y) * (px - v1.x)) * inv;
    float r1 = ((v0.x - v2.x) * (py - v2.y) - (v0.y - v2.y) * (px - v2.x)) * inv;
    float r2 = 1.0f - r0 - r1;
    // attributs lineaires a l'ecran (1/z, couleur) : meme forme que les poids
    struct Pl { float at, dx, dy; };
    auto plane = [&](float q0, float q1, float q2) { return Pl{ q0 * r0 + q1 * r1 + q2 * r2, q0 * a0 + q1 * a1 + q2 * a2, q0 * b0 + q1 * b1 + q2 * b2 }; };
    Pl pz = plane(v0.iz, v1.iz, v2.iz), pr = plane(v0.r, v1.r, v2.r), pg = plane(v0.g, v1.g, v2.g), pb = plane(v0.b, v1.b, v2.b);
    const float eps = -1e-5f;
    for (int y = y0; y <= y1; y++) {
        int dy = y - y0;
        // portion de la ligne dans le triangle : w + a * k >= eps pour les trois aretes
        float lo = 0, hi = (float)(x1 - x0);
        bool empty = false;
        auto clip = [&](float w, float a) {
            if (a > 1e-12f) lo = max(lo, (eps - w) / a);
            else if (a < -1e-12f) hi = min(hi, (eps - w) / a);
            else if (w < eps) empty = true;
        };
        clip(r0 + b0 * dy, a0);
        clip(r1 + b1 * dy, a1);
        clip(r2 + b2 * dy, a2);
        if (empty || lo > hi) continue;
        int ks = (int)ceilf(lo), ke = (int)floorf(hi);
        float iz = pz.at + pz.dy * dy + pz.dx * ks, r = pr.at + pr.dy * dy + pr.dx * ks;
        float g = pg.at + pg.dy * dy + pg.dx * ks, b = pb.at + pb.dy * dy + pb.dx * ks;
        uint32_t *prow = pix + (size_t)y * W;
        float *zrow = zb + (size_t)y * W;
        for (int x = x0 + ks; x <= x0 + ke; x++, iz += pz.dx, r += pr.dx, g += pg.dx, b += pb.dx) {
            if (iz <= zrow[x]) continue;
            // (bords : l'extrapolation peut sortir de 0..255 d'un rien)
            float cr = min(max(r, 0.0f), 255.0f), cg = min(max(g, 0.0f), 255.0f), cb = min(max(b, 0.0f), 255.0f);
            if (alpha < 0) {
                zrow[x] = iz;
                prow[x] = 0xFF000000u | ((uint32_t)cr << 16) | ((uint32_t)cg << 8) | (uint32_t)cb;
            } else {   // premultiplie : dst = src * a + dst * (1 - a)
                uint32_t d = prow[x];
                float k = 1.0f - alpha;
                uint32_t A = (uint32_t)(alpha * 255 + (d >> 24) * k);
                uint32_t R = (uint32_t)min(255.0f, cr * alpha + ((d >> 16) & 255) * k);
                uint32_t G = (uint32_t)min(255.0f, cg * alpha + ((d >> 8) & 255) * k);
                uint32_t B = (uint32_t)min(255.0f, cb * alpha + (d & 255) * k);
                prow[x] = (A << 24) | (R << 16) | (G << 8) | B;
            }
        }
    }
}

// Projection (pixels du rendu 2x de taille BW x BH) d'un point de la voiture.
struct CarCam { CarRot R; float f, cx, cy, mid; };
static CarCam CarCamera(int BW, int BH)
{
    CarCam c;
    c.R = { cosf(g_carYaw), sinf(g_carYaw), cosf(g_carPitch), sinf(g_carPitch) };
    // hauteur : boite sous tous les lacets, au tangage actuel (largeur : g_car.fx, tous angles) ; les coins de la
    // boite debordent de la voiture (formes arrondies), d'ou des marges un peu plus serrees que 1
    float fy0 = 1e9f, fy1 = -1e9f;
    for (int ia = 0; ia < 72; ia++) {
        float yaw = ia * 6.2831853f / 72;
        CarRot R = { cosf(yaw), sinf(yaw), c.R.cp, c.R.sp };
        for (int k = 0; k < 8; k++) {
            float X, Y, Z;
            CarXf(R, ((k & 1) ? g_car.mx[0] : g_car.mn[0]) - g_car.c[0], ((k & 2) ? g_car.mx[1] : g_car.mn[1]) - g_car.c[1],
                  ((k & 4) ? g_car.mx[2] : g_car.mn[2]) - g_car.c[2], X, Y, Z);
            Z += g_car.dist;
            fy0 = min(fy0, Y / Z);
            fy1 = max(fy1, Y / Z);
        }
    }
    c.f = min(BW * 0.53f / g_car.fx, BH * 0.98f / (fy1 - fy0));
    c.mid = (fy0 + fy1) / 2;
    c.cx = BW * 0.5f;
    c.cy = BH * 0.5f;
    return c;
}
static void CarProject(const CarCam &c, float x, float y, float z, float &sx, float &sy, float &iz)
{
    float X, Y, Z;
    CarXf(c.R, x - g_car.c[0], y - g_car.c[1], z - g_car.c[2], X, Y, Z);
    Z += g_car.dist;
    iz = 1.0f / max(Z, 0.05f);
    sx = c.cx + c.f * X * iz;
    sy = c.cy - c.f * (Y * iz - c.mid);
}

static void CarRender(int W, int H)
{
    const int S = 2, BW = W * S, BH = H * S;
    g_carBig.assign((size_t)BW * BH, 0);
    g_carZ.assign((size_t)BW * BH, 0.0f);
    g_carPix.assign((size_t)W * H, 0);
    if (g_car.state != 1) return;
    CarCam cam = CarCamera(BW, BH);
    size_t nv = g_car.pos.size() / 3;
    static std::vector<float> vp, sv, lit;   // position vue, ecran (x, y, 1/z), eclairage (diffus, reflet ; face, dos)
    vp.resize(nv * 3); sv.resize(nv * 3); lit.resize(nv * 4);
    // Lumieres (repere de la camera, vers la lumiere) : principale en haut a gauche devant, appoint a droite.
    auto norm = [](float x, float y, float z, float *o) { float l = sqrtf(x * x + y * y + z * z); o[0] = x / l; o[1] = y / l; o[2] = z / l; };
    float L1[3], L2[3], Hh[3];
    norm(-0.5f, 0.8f, -0.45f, L1);
    norm(0.75f, 0.2f, -0.3f, L2);
    norm(L1[0], L1[1], L1[2] - 1.0f, Hh);   // demi-vecteur avec la direction de la camera (0, 0, -1)
    for (size_t v = 0; v < nv; v++) {
        const float *p = &g_car.pos[v * 3], *n = &g_car.nrm[v * 3];
        float X, Y, Z;
        CarXf(cam.R, p[0] - g_car.c[0], p[1] - g_car.c[1], p[2] - g_car.c[2], X, Y, Z);
        Z += g_car.dist;
        vp[v * 3] = X; vp[v * 3 + 1] = Y; vp[v * 3 + 2] = Z;
        float iz = 1.0f / max(Z, 0.05f);
        sv[v * 3] = cam.cx + cam.f * X * iz;
        sv[v * 3 + 1] = cam.cy - cam.f * (Y * iz - cam.mid);
        sv[v * 3 + 2] = iz;
        float nx, ny, nz;
        CarXf(cam.R, n[0], n[1], n[2], nx, ny, nz);
        for (int side = 0; side < 2; side++) {
            float s = side ? -1.0f : 1.0f;
            float d1 = s * (nx * L1[0] + ny * L1[1] + nz * L1[2]), d2 = s * (nx * L2[0] + ny * L2[1] + nz * L2[2]);
            float h = s * (nx * Hh[0] + ny * Hh[1] + nz * Hh[2]), sky = s * ny;
            lit[v * 4 + side * 2] = 0.30f + 0.10f * sky + 0.78f * max(d1, 0.0f) + 0.22f * max(d2, 0.0f);
            float hs = max(h, 0.0f), h2 = hs * hs, h4 = h2 * h2, h8 = h4 * h4, h16 = h8 * h8;
            lit[v * 4 + side * 2 + 1] = h16 * h8 * h4;   // h^28
        }
    }
    float paint[3];
    if (g_carColor >= 0) { paint[0] = ((g_carColor >> 16) & 255) / 255.0f; paint[1] = ((g_carColor >> 8) & 255) / 255.0f; paint[2] = (g_carColor & 255) / 255.0f; }
    else paint[0] = paint[1] = paint[2] = 0.80f;   // au hasard : gris clair
    uint32_t *pix = g_carBig.data();
    float *zb = g_carZ.data();
    size_t nt = g_car.tri.size() / 3;
    for (int pass = 0; pass < 2; pass++) {   // 0 : opaque ; 1 : vitres (apres, sans ecrire la profondeur)
        for (size_t t = 0; t < nt; t++) {
            const CarPart &part = g_car.parts[g_car.triPart[t]];
            if (part.glass != (pass == 1)) continue;
            uint32_t i[3] = { g_car.tri[t * 3], g_car.tri[t * 3 + 1], g_car.tri[t * 3 + 2] };
            const float *a = &vp[i[0] * 3], *b = &vp[i[1] * 3], *c = &vp[i[2] * 3];
            if (a[2] < 0.1f || b[2] < 0.1f || c[2] < 0.1f) continue;
            // face tournee vers la camera ? (normale d'Unity : cross(b - a, c - a)) ; sinon eclairee comme le dos
            float e1[3] = { b[0] - a[0], b[1] - a[1], b[2] - a[2] }, e2[3] = { c[0] - a[0], c[1] - a[1], c[2] - a[2] };
            float fn[3] = { e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0] };
            int side = fn[0] * a[0] + fn[1] * a[1] + fn[2] * a[2] < 0 ? 0 : 1;
            const float *base = part.paint ? paint : &part.r;
            float ks = part.glass ? 0.9f : part.paint ? 0.55f : 0.12f;
            CarSV s[3];
            for (int k = 0; k < 3; k++) {
                float df = lit[i[k] * 4 + side * 2], sp = lit[i[k] * 4 + side * 2 + 1] * ks;
                s[k].x = sv[i[k] * 3]; s[k].y = sv[i[k] * 3 + 1]; s[k].iz = sv[i[k] * 3 + 2];
                s[k].r = min(255.0f, (base[0] * df + sp) * 255);
                s[k].g = min(255.0f, (base[1] * df + sp) * 255);
                s[k].b = min(255.0f, (base[2] * df + sp) * 255);
            }
            CarTri(s[0], s[1], s[2], BW, BH, pix, zb, pass ? 0.4f : -1.0f);
        }
    }
    // Reduction 2x2 (antialiasing) ; moyenne des quatre canaux premultiplies
    for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++) {
            const uint32_t *q = pix + (size_t)(y * 2) * BW + x * 2;
            uint32_t c4[4] = { q[0], q[1], q[BW], q[BW + 1] }, out = 0;
            for (int sh = 0; sh < 32; sh += 8) {
                uint32_t sum = 0;
                for (uint32_t c : c4) sum += (c >> sh) & 255;
                out |= ((sum + 2) / 4) << sh;
            }
            g_carPix[(size_t)y * W + x] = out;
        }
}

static int CarSwatchColor(int i) { return i <= 0 ? -1 : kSwatches[i - 1].rgb; }
// Pastille du choix actuel : 0 au hasard, 1.. une teinte, -1 couleur personnalisee
static int CarSwatchSel()
{
    if (g_carColor < 0) return 0;
    for (int i = 1; i < kSwatchN; i++) if (kSwatches[i - 1].rgb == g_carColor) return i;
    return -1;
}
static RectF CarSwatchRect(int i) { return RectF(kCarPanel.X + 26 + (i % 4) * 76.0f, kCarPanel.Y + 64 + (i / 4) * 72.0f, 56, 56); }
static int CarSwatchAt(float x, float y)
{
    for (int i = 0; i < kSwatchN; i++) {
        RectF r = CarSwatchRect(i);
        r.Inflate(3, 3);
        if (r.Contains(x, y)) return i;
    }
    return -1;
}
static Color CarRgb(int rgb, BYTE a = 255) { return Color(a, (BYTE)(rgb >> 16), (BYTE)(rgb >> 8), (BYTE)rgb); }

// Finition de la CORRIS pour une nouvelle partie ([Coop] FinitionVoiture) : 0 au hasard, 1 L, 2 LX, 3 SLX, 4 GT.
static const char *kTrims[] = { "", "L", "LX", "SLX", "GT" };
static RectF g_trimRect;   // (bulle du guide)
static int CarTrimGet()
{
    if (g_gameDir.empty()) return 0;
    char v[16] = "";
    GetPrivateProfileStringA("Coop", "FinitionVoiture", "", v, sizeof(v), ModIniA().c_str());
    for (int i = 1; i < 5; i++) if (!_stricmp(v, kTrims[i])) return i;
    return 0;
}
static void CarTrimSet(int k)
{
    if (g_gameDir.empty() || k < 0 || k > 4) return;
    EnsureModDir();
    WritePrivateProfileStringA("Coop", "FinitionVoiture", k ? kTrims[k] : NULL, ModIniA().c_str());
}

// Options de la plaque VIN de la CORRIS pour une nouvelle partie (demande de HowardCS, 12/10 : « choisir la version GT,
// l'annee... »). Valeurs possibles par finition exportees par le mod (Vin.ExportOptions : <jeu>\MWCoop\cache\vin-options.txt,
// « finition;cle;v1,v2,... », listes du jeu) ; choix dans [Coop] Vin<cle> (vide : au hasard). Le mod ne garde que cette
// valeur dans la liste ou le jeu tire l'option (si la finition l'a).
static bool g_vinView;
static RectF g_vinBtnRect;   // (bulle du guide)
struct VinKey { const char *key; const wchar_t *fr, *en; };
static const VinKey kVinKeys[] = {
    { "Year", L"Ann\u00E9e (code)", L"Year (code)" }, { "Engine", L"Moteur", L"Engine" }, { "Gearbox", L"Bo\u00EEte", L"Gearbox" },
    { "Suspension", L"Suspension", L"Suspension" }, { "Windshield", L"Pare-brise", L"Windshield" }, { "VinylRoof", L"Toit vinyle", L"Vinyl roof" },
    { "InteriorTrim", L"Int\u00E9rieur", L"Interior" }, { "Wheels", L"Roues", L"Wheels" }, { "Radio", L"Radio", L"Radio" },
    { "InstrumentPanel", L"Tableau de bord", L"Instrument panel" }, { "PowerBrakes", L"Servofrein", L"Power brakes" }, { "WindowHeater", L"Lunette d\u00E9givrante", L"Heated rear window" },
};
static const int kVinKeyN = (int)(sizeof(kVinKeys) / sizeof(kVinKeys[0]));
static std::map<std::string, std::vector<std::string>> g_vinOpts;   // "finition|cle" -> valeurs
static ULONGLONG g_vinStamp, g_vinNext;
static void VinLoad()
{
    if (GetTickCount64() < g_vinNext) return;
    g_vinNext = GetTickCount64() + 2000;
    std::wstring path = g_gameDir + L"MWCoop\\cache\\vin-options.txt";
    WIN32_FILE_ATTRIBUTE_DATA a;
    if (!GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &a)) { g_vinOpts.clear(); g_vinStamp = 0; return; }
    ULONGLONG st = ((ULONGLONG)a.ftLastWriteTime.dwHighDateTime << 32) | a.ftLastWriteTime.dwLowDateTime;
    if (st == g_vinStamp) return;
    g_vinStamp = st;
    g_vinOpts.clear();
    FILE *f = _wfopen(path.c_str(), L"rb");
    if (!f) return;
    char line[1024];
    while (fgets(line, sizeof line, f)) {
        std::string l = line;
        while (!l.empty() && (l.back() == '\n' || l.back() == '\r')) l.pop_back();
        size_t a1 = l.find(';'), a2 = a1 == std::string::npos ? a1 : l.find(';', a1 + 1);
        if (a2 == std::string::npos) continue;
        std::vector<std::string> vals;
        std::string rest = l.substr(a2 + 1), cur;
        for (char c : rest) { if (c == ',') { if (!cur.empty()) vals.push_back(cur); cur.clear(); } else cur += c; }
        if (!cur.empty()) vals.push_back(cur);
        g_vinOpts[l.substr(0, a1) + "|" + l.substr(a1 + 1, a2 - a1 - 1)] = vals;
    }
    fclose(f);
}
// Valeurs possibles de l'option k pour la finition choisie (au hasard : toutes les finitions).
static std::vector<std::string> VinValues(int k)
{
    std::vector<std::string> out;
    int t = CarTrimGet();
    for (int i = 1; i < 5; i++) {
        if (t && t != i) continue;
        auto it = g_vinOpts.find(std::string(kTrims[i]) + "|" + kVinKeys[k].key);
        if (it == g_vinOpts.end()) continue;
        for (const std::string &v : it->second) if (std::find(out.begin(), out.end(), v) == out.end()) out.push_back(v);
    }
    return out;
}
static std::string VinGet(int k)
{
    char v[32] = "";
    std::string key = std::string("Vin") + kVinKeys[k].key;
    GetPrivateProfileStringA("Coop", key.c_str(), "", v, sizeof(v), ModIniA().c_str());
    return v;
}
static void VinCycle(int k)
{
    if (k < 0 || k >= kVinKeyN || g_gameDir.empty()) return;
    std::vector<std::string> vals = VinValues(k);
    std::string cur = VinGet(k), next;
    if (!vals.empty()) {
        auto it = std::find(vals.begin(), vals.end(), cur);
        if (cur.empty()) next = vals[0];
        else if (it != vals.end() && it + 1 != vals.end()) next = *(it + 1);
        else next = "";
    }
    EnsureModDir();
    std::string key = std::string("Vin") + kVinKeys[k].key;
    WritePrivateProfileStringA("Coop", key.c_str(), next.empty() ? NULL : next.c_str(), ModIniA().c_str());
}
// Sens connu de quelques codes (le reste : le code de la plaque, tel quel).
static std::wstring VinMeaning(int k, const std::string &v)
{
    std::string key = kVinKeys[k].key;
    std::wstring w = Widen(v);
    if (v == "-") return T(L"aucun(e)", L"none");
    if (key == "Gearbox") return v == "B" ? T(L"B \u00B7 manuelle", L"B \u00B7 manual") : v == "7" ? T(L"7 \u00B7 automatique", L"7 \u00B7 automatic") : w;
    if (key == "Year") return (v == "P" || v == "R" || v == "S") ? w + T(L" \u00B7 restyl\u00E9e", L" \u00B7 facelift") : w;
    if (key == "Radio" && v == "J") return T(L"J \u00B7 radio", L"J \u00B7 radio");
    if (key == "PowerBrakes" && v == "B") return T(L"B \u00B7 servofrein", L"B \u00B7 power brakes");
    if (key == "WindowHeater") return w + T(L" \u00B7 d\u00E9givrante", L" \u00B7 heated");
    return w;
}
static void DrawVinOptions(Graphics &g, RectF card)
{
    VinLoad();
    GraphicsPath bp; RoundRect(bp, card, 20);
    SolidBrush bb(TH(card)); g.FillPath(&bb, &bp);
    Title(g, T(L"Plaque VIN", L"VIN plate"), RectF(card.X + 24, card.Y + 20, 300, 30), 22, kInk);
    Para(g, T(L"Options de la CORRIS pour une nouvelle partie. Clique pour passer \u00E0 la valeur suivante ; Hasard : comme le jeu.",
              L"CORRIS options for a new game. Click for the next value; Random: like the game."),
         RectF(card.X + 24, card.Y + 52, card.Width - 48, 40), 12.5f, kGrey);
    if (g_vinOpts.empty()) {
        Text(g, T(L"Lance une partie une fois : le mod relève alors les options possibles de chaque finition.", L"Start a game once: the mod then reads the possible options of each trim."),
             RectF(card.X + 24, card.Y + 120, card.Width - 48, 60), 14, FontStyleBold, kInk, StringAlignmentNear);
        return;
    }
    float cw = (card.Width - 48 - 16) / 2, ch = 64, y0 = card.Y + 100;
    for (int k = 0; k < kVinKeyN; k++) {
        RectF r(card.X + 24 + (k % 2) * (cw + 16), y0 + (k / 2) * (ch + 12), cw, ch);
        std::vector<std::string> vals = VinValues(k);
        std::string cur = VinGet(k);
        bool fixed = vals.size() <= 1, hot = g_uiHot == UI_CAR_VINOPT + k;
        bool absent = !cur.empty() && std::find(vals.begin(), vals.end(), cur) == vals.end();
        GraphicsPath p; RoundRect(p, r, 14);
        SolidBrush fb(hot && !fixed ? TH(cardSel) : TH(field)); g.FillPath(&fb, &p);
        Pen pen(!cur.empty() ? kAcc : TH(fieldBorder), !cur.empty() ? 2.0f : 1.0f); g.DrawPath(&pen, &p);
        Text(g, T(kVinKeys[k].fr, kVinKeys[k].en), RectF(r.X + 14, r.Y + 8, r.Width - 28, 20), 12, FontStyleRegular, kGrey, StringAlignmentNear);
        std::wstring val = cur.empty() ? (fixed ? (vals.empty() ? T(L"\u2014", L"\u2014") : VinMeaning(k, vals[0]) + T(L" (seule possible)", L" (only one)")) : T(L"Hasard", L"Random"))
                                       : VinMeaning(k, cur) + (absent ? T(L" (pas pour cette finition)", L" (not for this trim)") : L"");
        Text(g, val, RectF(r.X + 14, r.Y + 28, r.Width - 28, 26), 14.5f, FontStyleBold, absent ? kGrey : kInk, StringAlignmentNear);
        if (!fixed || !cur.empty()) HotZone(r, UI_CAR_VINOPT + k);
    }
}

// ---- peinture perso de la CORRIS (suggestion de .nolly, 12/10 : « comme les visages, mais pour la Rivett, dans le
// catalogue du garagiste comme une peinture perso »). Le jeu le fait deja : CORRIS/AssembyDatabase/PaintMaterials,
// automate 'ImportPNG', lit <jeu>\Images\car.png (gabarit 2048 x 2048 livre avec le jeu, et son PSD en calques) dans
// CAR_PAINT_DECAL, posee quand on achete « Custom paint made by artist » chez Fleetari. Le lanceur importe l'image du
// joueur dans %LOCALAPPDATA%\MWCoop\peinture.png (le dossier du jeu n'est pas touche) ; [Coop] PeinturePerso=1 : le mod
// la met a la place de car.png (hote ou solo, CustomPaint) et l'hote l'envoie a ses invites a la place de
// Images\car.png (Contenu envoye > Peinture de la CORRIS, modsync.inc).
static Bitmap *LoadAnyImage(const std::wstring &file, std::wstring *why);
static bool EncoderClsid(const wchar_t *mime, CLSID *out);
static bool g_paintView;
static RectF g_paintBtnRect;   // (bulle du guide)
static RectF g_dentBtnRect;    // (bulle du guide)

// Bosses des carrosseries (suggestion de « ...., », 13/10 : « pouvoir retirer les bosses de la Corris Rivett en supprimant
// le fichier meshsave ») : le jeu garde la forme cabossee de chaque tole dans LocalLow\...\meshsave.txt ; sans lui, toutes
// les voitures repartent lisses. Le fichier est renomme (meshsave.txt.bak-<date>), pas supprime : on peut le remettre.
// Jeu ferme seulement (il le reecrit a la sauvegarde). En coop, la sauvegarde est celle de l'hote : c'est lui qui le fait.
static void CarRemoveDents()
{
    if (GameProcessRunning()) { SetStatus(K_WARN, T(L"Ferme le jeu pour retirer les bosses", L"Close the game to remove the dents")); return; }
    std::wstring d = SaveDirOf(SaveCurrentProf());   // (la sauvegarde utilisee : saves.inc)
    if (d.empty()) return;
    std::wstring f = d + L"meshsave.txt";
    if (GetFileAttributesW(f.c_str()) == INVALID_FILE_ATTRIBUTES) { SetStatus(K_NORMAL, T(L"Aucune bosse enregistrée : les carrosseries sont déjà lisses", L"No dents saved: the bodies are already smooth")); return; }
    if (MessageBoxW(g_wnd, T(L"Retirer toutes les bosses des voitures de ta sauvegarde ?\n\nLe jeu garde la forme cabossée des tôles dans meshsave.txt. Il sera mis de côté (une copie reste à côté), et toutes les voitures repartiront lisses au prochain chargement.\n\nEn coop, c'est l'hôte qui le fait : sa sauvegarde est envoyée aux invités.",
                             L"Remove all dents from the cars in your save?\n\nThe game keeps the dented shape of the body panels in meshsave.txt. It will be set aside (a copy is kept next to it), and every car will be smooth again at the next load.\n\nIn co-op, the host does it: their save is sent to the guests."),
                    L"MWCoop", MB_ICONQUESTION | MB_YESNO) != IDYES) return;
    SYSTEMTIME t; GetLocalTime(&t);
    wchar_t bak[64]; swprintf_s(bak, L".bak-%04d%02d%02d-%02d%02d%02d", t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute, t.wSecond);
    if (!MoveFileExW(f.c_str(), (f + bak).c_str(), 0)) { SetStatus(K_ERR, T(L"meshsave.txt : renommage impossible (erreur %lu)", L"meshsave.txt: can't rename (error %lu)"), GetLastError()); return; }
    TestLog("voiture : bosses retirees (meshsave.txt -> meshsave.txt%s)", Narrow(bak, CP_UTF8).c_str());
    SetStatus(K_OK, T(L"Bosses retirées : les voitures seront lisses au prochain chargement (copie : meshsave.txt%s)", L"Dents removed: the cars will be smooth at the next load (copy: meshsave.txt%s)"), bak);
}
static Bitmap *g_paintThumb;
static std::wstring g_paintThumbOf;
static std::wstring PaintFile() { return LocalDir() + L"peinture.png"; }
static std::wstring PaintGame() { return g_gameDir + L"Images\\car.png"; }
static bool PaintHas() { return FileExists(PaintFile()); }
static bool PaintOn() { return !g_gameDir.empty() && GetPrivateProfileIntA("Coop", "PeinturePerso", 0, ModIniA().c_str()) != 0 && PaintHas(); }
static void PaintUse(bool on)
{
    if (g_gameDir.empty()) return;
    EnsureModDir();
    WritePrivateProfileStringA("Coop", "PeinturePerso", on ? "1" : "0", ModIniA().c_str());
    g_paintThumbOf.clear();
}
// Miniature (512 x 512) de la peinture en service : la sienne, ou celle du jeu.
static Bitmap *PaintThumb()
{
    std::wstring f = PaintOn() ? PaintFile() : PaintGame();
    WIN32_FILE_ATTRIBUTE_DATA a;
    std::wstring key = f;
    if (GetFileAttributesExW(f.c_str(), GetFileExInfoStandard, &a)) key += L"|" + std::to_wstring(a.ftLastWriteTime.dwLowDateTime);
    if (key == g_paintThumbOf) return g_paintThumb;
    g_paintThumbOf = key;
    delete g_paintThumb; g_paintThumb = NULL;
    if (Bitmap *b = LoadAnyImage(f, NULL)) {
        g_paintThumb = new Bitmap(512, 512, PixelFormat32bppPARGB);
        Graphics tg(g_paintThumb);
        tg.SetInterpolationMode(InterpolationModeHighQualityBicubic);
        tg.DrawImage(b, RectF(0, 0, 512, 512));
        delete b;
    }
    return g_paintThumb;
}
static void PaintImport()
{
    wchar_t file[MAX_PATH] = L"";
    OPENFILENAMEW of = { sizeof(of) };
    of.hwndOwner = g_wnd;
    of.lpstrFilter = L"Images\0*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic;*.heif;*.avif;*.jfif\0*.*\0*.*\0";
    of.lpstrFile = file;
    of.nMaxFile = MAX_PATH;
    std::wstring init = LocalDir();
    of.lpstrInitialDir = init.c_str();
    of.lpstrTitle = T(L"Choisir la peinture de la CORRIS (peinte sur le gabarit car.png)", L"Choose the CORRIS paint (painted on the car.png template)");
    of.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_HIDEREADONLY;
    if (!GetOpenFileNameW(&of)) return;
    std::wstring why;
    Bitmap *src = LoadAnyImage(file, &why);
    bool ok = false;
    if (src) {
        Bitmap *game = LoadAnyImage(PaintGame(), NULL);   // taille du gabarit du jeu (2048 x 2048)
        int W = game ? (int)game->GetWidth() : 2048, H = game ? (int)game->GetHeight() : 2048;
        Bitmap dst(W, H, PixelFormat24bppRGB);
        {
            Graphics dg(&dst);
            dg.SetInterpolationMode(InterpolationModeHighQualityBicubic);
            dg.SetPixelOffsetMode(PixelOffsetModeHalf);
            if (game) dg.DrawImage(game, RectF(0, 0, (REAL)W, (REAL)H));   // (parties transparentes : la peinture du jeu)
            else dg.Clear(Color(255, 200, 200, 200));
            dg.DrawImage(src, RectF(0, 0, (REAL)W, (REAL)H));
        }
        delete game;
        CLSID png;
        CreateDirectoryW(LocalDir().c_str(), NULL);
        ok = EncoderClsid(L"image/png", &png) && dst.Save(PaintFile().c_str(), &png, NULL) == Ok;
        if (!ok) why = T(L"enregistrement impossible", L"can't save it");
        LaunchLog("peinture perso importee : %dx%d", W, H);
    }
    delete src;
    if (ok) PaintUse(true);
    else {
        LaunchLog("peinture perso : echec (%ls)", why.c_str());
        std::wstring msg = std::wstring(T(L"Cette image n'a pas pu être lue : ", L"This image could not be read: ")) + why + L".";
        MessageBoxW(g_wnd, msg.c_str(), L"MWCoop", MB_ICONWARNING | MB_OK);
    }
}
// Gabarit : une copie de Images\car.png du jeu (on peint sur la copie), montree dans l'Explorateur.
static void PaintTemplate()
{
    std::wstring t = LocalDir() + L"gabarit-voiture.png";
    CreateDirectoryW(LocalDir().c_str(), NULL);
    if (!FileExists(t) && !CopyFileW(PaintGame().c_str(), t.c_str(), TRUE)) {
        MessageBoxW(g_wnd, T(L"Images\\car.png est introuvable dans le dossier du jeu.", L"Images\\car.png was not found in the game folder."), L"MWCoop", MB_ICONWARNING | MB_OK);
        return;
    }
    std::wstring a = L"/select,\"" + t + L"\"";
    ShellExecuteW(g_wnd, L"open", L"explorer.exe", a.c_str(), NULL, SW_SHOWNORMAL);
}
static void PaintRemove()
{
    DeleteFileW(PaintFile().c_str());
    PaintUse(false);
    LaunchLog("peinture perso retiree");
}
static void DrawPaintView(Graphics &g, RectF card)
{
    GraphicsPath bp; RoundRect(bp, card, 20);
    SolidBrush bb(TH(card)); g.FillPath(&bb, &bp);
    Title(g, T(L"Peinture perso", L"Custom paint"), RectF(card.X + 24, card.Y + 20, 300, 30), 22, kInk);
    Para(g, T(L"Ta propre peinture sur la CORRIS, peinte sur le gabarit du jeu. L'hôte l'envoie à ses invités.",
              L"Your own paint on the CORRIS, painted on the game's template. The host sends it to their guests."),
         RectF(card.X + 24, card.Y + 56, card.Width - 48, 40), 12.5f, kGrey);
    bool has = PaintHas(), on = PaintOn();
    RectF th(card.X + 24, card.Y + 104, 300, 300);
    GraphicsPath tp; RoundRect(tp, th, 14);
    SolidBrush tb(TH(field)); g.FillPath(&tb, &tp);
    if (Bitmap *b = PaintThumb()) {
        g.SetClip(&tp);
        g.DrawImage(b, th);
        g.ResetClip();
    } else Icon(g, IC_IMAGE, th.X + th.Width / 2, th.Y + th.Height / 2, 48, WithA(kGrey, 0.6f), 1.4f);
    Pen tpen(on ? kAcc : TH(fieldBorder), on ? 2.0f : 1.0f); g.DrawPath(&tpen, &tp);
    Text(g, on ? T(L"Ta peinture", L"Your paint") : T(L"Peinture du jeu (Images\\car.png)", L"Game paint (Images\\car.png)"),
         RectF(th.X, th.Y + th.Height + 6, th.Width, 22), 12.5f, FontStyleBold, on ? kInk : kGrey, StringAlignmentCenter);
    float bx = th.X + th.Width + 24, bw = card.X + card.Width - 24 - bx, by = th.Y;
    UiButton(g, RectF(bx, by, bw, 44), T(L"Gabarit", L"Template"), UI_CAR_PAINTTPL, false, IC_FOLDER); by += 56;
    UiButton(g, RectF(bx, by, bw, 44), T(L"Mon image…", L"My image…"), UI_CAR_PAINTIMP, true, IC_IMAGE); by += 56;
    if (has) {
        UiButton(g, RectF(bx, by, bw, 44), on ? T(L"Utilisée : oui", L"In use: yes") : T(L"Utilisée : non", L"In use: no"), UI_CAR_PAINTUSE, false, on ? IC_CHECK : -1); by += 56;
        UiButton(g, RectF(bx, by, bw, 44), T(L"Retirer", L"Remove"), UI_CAR_PAINTDEL, false, IC_TRASH); by += 56;
    }
    float sy = th.Y + th.Height + 44;
    const wchar_t *steps[] = {
        T(L"1. Gabarit : la carrosserie du jeu à plat (2048 x 2048). Le dossier Images du jeu a aussi corris_template_layered.psd, en calques.",
          L"1. Template: the game's body laid flat (2048 x 2048). The game's Images folder also has corris_template_layered.psd, in layers."),
        T(L"2. Peins par-dessus en gardant la taille, puis « Mon image… ».", L"2. Paint over it keeping the size, then \"My image…\"."),
        T(L"3. En jeu, achète « Custom paint made by artist » chez Fleetari (réparation) : la CORRIS prend ta peinture.",
          L"3. In game, buy \"Custom paint made by artist\" at Fleetari's (repair shop): the CORRIS takes your paint."),
    };
    for (const wchar_t *s : steps) { Para(g, s, RectF(card.X + 24, sy, card.Width - 48, 40), 13, kInk); sy += 42; }
}

static void CarSetColor(int rgb)
{
    g_carColor = rgb;
    g_carDirty = true;
    if (g_gameDir.empty()) return;
    EnsureModDir();
    char v[16] = "";
    if (rgb >= 0) sprintf_s(v, "%06X", rgb & 0xFFFFFF);
    WritePrivateProfileStringA("Coop", "CouleurVoiture", v, ModIniA().c_str());
}

// Selecteur de couleur de Windows (couleurs perso : les teintes de la palette).
static void CarPickColor()
{
    static COLORREF custom[16];
    static bool init;
    if (!init) {
        for (int i = 0; i < 16 && i < (int)_countof(kSwatches); i++) { int c = kSwatches[i].rgb; custom[i] = RGB(c >> 16, (c >> 8) & 255, c & 255); }
        init = true;
    }
    int cur = g_carColor >= 0 ? g_carColor : 0xC8C8C8;
    CHOOSECOLORW cc = { sizeof(cc) };
    cc.hwndOwner = g_wnd;
    cc.lpCustColors = custom;
    cc.rgbResult = RGB(cur >> 16, (cur >> 8) & 255, cur & 255);
    cc.Flags = CC_FULLOPEN | CC_RGBINIT | CC_ANYCOLOR;
    if (ChooseColorW(&cc)) CarSetColor((GetRValue(cc.rgbResult) << 16) | (GetGValue(cc.rgbResult) << 8) | GetBValue(cc.rgbResult));
}

// Camembert de six teintes (pastille "au hasard", bouton "Autre couleur").
static void DrawCarWheel(Graphics &g, RectF r)
{
    static const int pick[] = { 5, 2, 10, 1, 13, 3 };
    for (int k = 0; k < 6; k++) { SolidBrush b(CarRgb(kSwatches[pick[k]].rgb)); g.FillPie(&b, r, k * 60.0f - 90, 60.0f); }
}

static void DrawCar(Graphics &g)
{
    CarCheckFile();
    Glass(kCarCard);
    Glass(kCarPanel);
    RectF view = kCarView;
    if (g_vinView) DrawVinOptions(g, kCarCard);
    else if (g_paintView) DrawPaintView(g, kCarCard);
    else if (g_car.state != 1) {
        const wchar_t *msg = g_car.state == -2 ? T(L"Aper\u00E7u illisible (MWCoop\\cache\\corris.mesh) : lance une partie pour le refaire.", L"Preview unreadable (MWCoop\\cache\\corris.mesh): start a game to rebuild it.")
                                               : T(L"Lance une partie une fois pour voir l'aper\u00E7u de la voiture.", L"Start a game once to see the car preview.");
        Icon(g, IC_CAR, view.X + view.Width / 2, view.Y + view.Height / 2 - 40, 64, WithA(kGrey, 0.6f), 1.4f);
        Para(g, msg, RectF(view.X + 80, view.Y + view.Height / 2 + 10, view.Width - 160, 60), 14, kGrey, StringAlignmentNear);
    } else {
        int W = max(8, (int)(view.Width * g_scale + 0.5f)), H = max(8, (int)(view.Height * g_scale + 0.5f));
        DWORD now = GetTickCount();
        if (W != g_carW || H != g_carH || g_carDirty || g_carDrawnColor != g_carColor || now - g_carDrawT >= 33) {
            g_carW = W; g_carH = H;
            g_carDirty = false;
            g_carDrawnColor = g_carColor;
            g_carDrawT = now;
            CarRender(W, H);
        }
        {   // ombre douce sous la voiture : empreinte au sol projetee
            CarCam cam = CarCamera(W * 2, H * 2);
            float x0 = 1e9f, x1 = -1e9f, y0 = 1e9f, y1 = -1e9f;
            for (int k = 0; k < 4; k++) {
                float sx, sy, iz;
                CarProject(cam, (k & 1) ? g_car.mx[0] : g_car.mn[0], g_car.mn[1], (k & 2) ? g_car.mx[2] : g_car.mn[2], sx, sy, iz);
                x0 = min(x0, sx); x1 = max(x1, sx); y0 = min(y0, sy); y1 = max(y1, sy);
            }
            float k = 1.0f / (2 * g_scale);
            RectF sr(view.X + x0 * k, view.Y + y0 * k, (x1 - x0) * k, (y1 - y0) * k);
            sr.Inflate(sr.Width * 0.06f, max(6.0f, sr.Height * 0.12f));
            sr.Offset(0, 4);
            GraphicsPath sp;
            sp.AddEllipse(sr);
            PathGradientBrush pb(&sp);
            pb.SetCenterColor(Color(g_dark ? 150 : 90, 0, 0, 0));
            Color edge(0, 0, 0, 0);
            int one = 1;
            pb.SetSurroundColors(&edge, &one);
            g.FillPath(&pb, &sp);
        }
        Bitmap bm(g_carW, g_carH, g_carW * 4, PixelFormat32bppPARGB, (BYTE *)g_carPix.data());
        InterpolationMode im = g.GetInterpolationMode();
        g.SetInterpolationMode(InterpolationModeNearestNeighbor);   // 1 pixel pour 1 pixel (deja lisse)
        g.DrawImage(&bm, view);
        g.SetInterpolationMode(im);
    }
    if (!g_vinView && !g_paintView) {
        Title(g, T(L"Ta CORRIS", L"Your CORRIS"), RectF(kCarCard.X + 24, kCarCard.Y + kCarCard.Height - 100, 400, 32), 22, kInk);
        Text(g, T(L"Couleur et finition pour une nouvelle partie ; l'h\u00F4te les donne \u00E0 ses invit\u00E9s.", L"Color and trim for a new game; the host gives them to their guests."),
             RectF(kCarCard.X + 24, kCarCard.Y + kCarCard.Height - 66, 460, 22), 13, FontStyleRegular, kGrey, StringAlignmentNear);
        if (g_car.state == 1) Text(g, T(L"Glisse pour tourner", L"Drag to rotate"), RectF(kCarCard.X + kCarCard.Width - 224, kCarCard.Y + kCarCard.Height - 66, 200, 22), 12.5f, FontStyleRegular, kGrey, StringAlignmentFar);
    }
    g_paintBtnRect = RectF(kCarCard.X + kCarCard.Width - 24 - 184, kCarCard.Y + 18, 184, 34);
    UiButton(g, g_paintBtnRect, g_paintView ? T(L"Voir la voiture", L"Show the car") : T(L"Peinture perso…", L"Custom paint…"), UI_CAR_PAINT, g_paintView, g_paintView ? -1 : IC_IMAGE, true, 10);
    g_dentBtnRect = RectF(g_paintBtnRect.X - 12 - 196, g_paintBtnRect.Y, 196, 34);
    if (!g_vinView && !g_paintView) UiButton(g, g_dentBtnRect, T(L"Retirer les bosses…", L"Remove dents…"), UI_CAR_DENTS, false, IC_UNDO, true, 10);
    else g_dentBtnRect = RectF(0, 0, 0, 0);

    // Couleur : pastilles, nom, autre couleur
    RectF pn = kCarPanel;
    Title(g, T(L"Couleur", L"Color"), RectF(pn.X + 24, pn.Y + 20, 200, 26), 17, kInk);
    int sel = CarSwatchSel(), show = g_carHot >= 0 ? g_carHot : sel;
    for (int i = 0; i < kSwatchN; i++) {
        RectF r = CarSwatchRect(i);
        if (i == sel) { Pen ring(kAcc, 2.6f); g.DrawEllipse(&ring, r.X - 5, r.Y - 5, r.Width + 10, r.Height + 10); }
        else if (i == g_carHot) { Pen ring(WithA(kInk, 0.5f), 1.6f); g.DrawEllipse(&ring, r.X - 4, r.Y - 4, r.Width + 8, r.Height + 8); }
        if (i == 0) {   // au hasard : camembert et point d'interrogation
            DrawCarWheel(g, r);
            SolidBrush mid(g_dark ? Color(255, 22, 32, 58) : Color(255, 245, 248, 252));
            g.FillEllipse(&mid, r.X + 14, r.Y + 14, r.Width - 28, r.Height - 28);
            Text(g, L"?", RectF(r.X, r.Y + 0.5f, r.Width, r.Height), 15, FontStyleBold, kInk);
        } else { SolidBrush b(CarRgb(kSwatches[i - 1].rgb)); g.FillEllipse(&b, r); }
        Pen edge(Color(70, 255, 255, 255), 1.0f);
        g.DrawEllipse(&edge, r);
    }
    std::wstring name;
    wchar_t hex[16] = L"";
    if (show == 0) name = T(L"Au hasard (comme le jeu)", L"Random (like the game)");
    else {
        int c = show > 0 ? kSwatches[show - 1].rgb : g_carColor;
        swprintf_s(hex, L"#%06X", c & 0xFFFFFF);
        name = show > 0 ? T(kSwatches[show - 1].fr, kSwatches[show - 1].en) : T(L"Couleur personnalis\u00E9e", L"Custom color");
    }
    float ny = CarSwatchRect(kSwatchN - 1).Y + 76;
    {
        RectF nr(pn.X + 24, ny, pn.Width - 48, 52);
        GraphicsPath np; RoundRect(np, nr, 14);
        SolidBrush nb(TH(card)); g.FillPath(&nb, &np);
        Pen npen(TH(choiceBorder), 1); g.DrawPath(&npen, &np);
        RectF d(nr.X + 14, nr.Y + 13, 26, 26);
        if (show == 0) DrawCarWheel(g, d); else { SolidBrush db(CarRgb(show > 0 ? kSwatches[show - 1].rgb : g_carColor)); GraphicsPath dp; RoundRect(dp, d, 8); g.FillPath(&db, &dp); }
        Text(g, name, RectF(nr.X + 50, nr.Y + 4, nr.Width - 60, 24), 14, FontStyleBold, kInk, StringAlignmentNear);
        if (hex[0]) Text(g, hex, RectF(nr.X + 50, nr.Y + 26, nr.Width - 60, 20), 12, FontStyleRegular, kGrey, StringAlignmentNear);
    }
    {   // Autre couleur... (selecteur de Windows)
        Button &b = g_btn[B_COLOR];
        b.r = RectF(pn.X + 24, ny + 66, pn.Width - 48, 46);
        GraphicsPath p; RoundRect(p, b.r, 14);
        SolidBrush fill(Mix(TH(btn2), TH(btn2Hot), b.hover)); g.FillPath(&fill, &p);
        Pen pen(sel < 0 ? kAcc : TH(fieldBorder), sel < 0 ? 2.0f : 1.0f); g.DrawPath(&pen, &p);
        RectF dot(b.r.X + 14, b.r.Y + 11, 24, 24);
        if (sel < 0) { SolidBrush db(CarRgb(g_carColor)); g.FillEllipse(&db, dot); } else DrawCarWheel(g, dot);
        Text(g, T(L"Autre couleur\u2026", L"Other color\u2026"), RectF(dot.X + 34, b.r.Y, b.r.Width - 60, b.r.Height), 13.5f, FontStyleBold, kInk, StringAlignmentNear);
    }
    {   // Finition ([Coop] FinitionVoiture : L, LX, SLX, GT ; vide : au hasard) : imposee au tirage d'une nouvelle partie (Vin.cs)
        float ty = ny + 128;
        Title(g, T(L"Finition", L"Trim"), RectF(pn.X + 24, ty, 200, 24), 17, kInk);
        g_vinBtnRect = RectF(pn.X + pn.Width - 24 - 168, ty - 5, 168, 32);   // (options de la plaque VIN : a droite du titre)
        UiButton(g, g_vinBtnRect, g_vinView ? T(L"Voir la voiture", L"Show the car") : T(L"Plaque VIN\u2026", L"VIN plate\u2026"), UI_CAR_VIN, g_vinView, -1, true, 10);
        g_trimRect = RectF(pn.X + 24, ty, pn.Width - 48, 70);
        UiSeg(g, RectF(pn.X + 24, ty + 32, pn.Width - 48, 38), { T(L"Hasard", L"Random"), L"L", L"LX", L"SLX", L"GT" }, CarTrimGet(), UI_CAR_TRIM, 12);
    }
    float bw = (pn.Width - 60) / 2;
    UiButton(g, RectF(pn.X + 24, pn.Y + pn.Height - 68, bw, 48), T(L"ANNULER", L"CANCEL"), UI_PAGE_CANCEL, false);
    UiButton(g, RectF(pn.X + 36 + bw, pn.Y + pn.Height - 68, bw, 48), T(L"VALIDER", L"OK"), UI_PAGE_OK, true);
}

// Clic dans l'onglet : une pastille, ou l'apercu (debut du glisser).
static bool CarMouseDown(float x, float y)
{
    int s = CarSwatchAt(x, y);
    if (s >= 0) { CarSetColor(CarSwatchColor(s)); return true; }
    if (g_car.state == 1 && !g_vinView && !g_paintView && kCarView.Contains(x, y) && !g_paintBtnRect.Contains(x, y) && !g_dentBtnRect.Contains(x, y)) {
        g_carDrag = true;
        g_carDragX = x; g_carDragY = y;
        SetCapture(g_wnd);
        return true;
    }
    return false;
}
static void CarDragTo(float x, float y)
{
    g_carYaw = fmodf(g_carYaw - (x - g_carDragX) * 0.012f + 6.2831853f, 6.2831853f);
    g_carPitch = min(max(g_carPitch + (y - g_carDragY) * 0.008f, kPitchMin), kPitchMax);
    g_carDragX = x; g_carDragY = y;
    g_carDirty = true;
}

