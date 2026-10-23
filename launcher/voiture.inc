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
// My Summer Car (19/10, demande de JD : « tout doit parler de la SATSUMA ») : la voiture est la SATSUMA. Apercu dans
// MWCoop\cache\satsuma.mesh (un fichier par jeu : jamais la CORRIS sur MSC ; absent : invitation a lancer une partie de
// My Summer Car). Pas de finition ni de plaque VIN sur la SATSUMA (rien de tel dans la scene de MSC) : caches. Le jeu ne
// tire pas sa couleur au hasard : la pastille 0 est sa couleur d'origine. Peinture perso : le gabarit est
// Images\template.png, l'image du joueur peinture-msc.png (autre gabarit que celui de la CORRIS).
static bool CarMsc() { return g_game == GAME_MSC; }
static const int kMscOrigColor = 0x726817;   // couleur d'origine des toles de la SATSUMA (0.447, 0.408, 0.090)
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
// Page PERSONNAGE (maquette du 17/10) : onglets Tenue / Visage / Voiture en haut de la page (tenue.inc), cartes dessous.
static const RectF kCarCard(264, 88, 640, 656), kCarPanel(920, 88, 344, 656);   // (cartes a y 88, onglets dans l'en-tete : maquette du 17/10)
static const RectF kCarView(284, 118, 600, 472);
static const float kPitchMin = 0.04f, kPitchMax = 0.55f;
static int g_carHot = -1;                // pastille survolee (1000 + k : onglet k de la page, tenue.inc)
static void PersoTabsDraw(Graphics &g);              // (tenue.inc : onglets de la page PERSONNAGE)
static int PersoPageHit(float x, float y);
static bool PersoPageMouseDown(float x, float y);

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

// Relit corris.mesh (satsuma.mesh sur MSC) s'il a change (le mod le reecrit a chaque partie) ; au plus toutes les 2 s.
static const wchar_t *CarMeshName() { return CarMsc() ? L"satsuma.mesh" : L"corris.mesh"; }
static void CarCheckFile()
{
    DWORD now = GetTickCount();
    std::wstring path = g_modRoot + L"MWCoop\\cache\\" + CarMeshName();
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
    else if (CarMsc()) { paint[0] = ((kMscOrigColor >> 16) & 255) / 255.0f; paint[1] = ((kMscOrigColor >> 8) & 255) / 255.0f; paint[2] = (kMscOrigColor & 255) / 255.0f; }   // (MSC : la couleur d'origine)
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
static RectF CarSwatchRect(int i) { return RectF(kCarPanel.X + 26 + (i % 4) * 76.0f, kCarPanel.Y + 60 + (i / 4) * 66.0f, 52, 52); }
static int CarSwatchAt(float x, float y)
{
    int t = PersoPageHit(x, y);   // (onglets de la page : survol en 1000 + k)
    if (t >= 0) return 1000 + t;
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
    std::wstring path = g_modRoot + L"MWCoop\\cache\\vin-options.txt";
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
// Un fichier par jeu (gabarits differents : une peinture de CORRIS ne doit jamais partir comme car.png de la SATSUMA).
static std::wstring PaintFile() { return LocalDir() + (CarMsc() ? L"peinture-msc.png" : L"peinture.png"); }
static std::wstring PaintGame() { return g_gameDir + L"Images\\car.png"; }
// Gabarit du jeu : Images\car.png sur MWC ; Images\template.png sur MSC (le jeu ne livre pas de car.png : « How to use
// car template.txt » : peindre template.png, l'enregistrer en car.png).
static std::wstring PaintTplGame() { return CarMsc() ? g_gameDir + L"Images\\template.png" : PaintGame(); }
static bool PaintHas() { return FileExists(PaintFile()); }
// Nom du fichier de peinture dans un texte (« peinture.png ») : peinture-msc.png sur My Summer Car (PaintFile).
static std::wstring PaintNameFix(std::wstring s)
{
    size_t p = CarMsc() ? s.find(L"peinture.png") : std::wstring::npos;
    if (p != std::wstring::npos) s.replace(p, 12, L"peinture-msc.png");
    return s;
}
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
    if (!PaintOn() && CarMsc() && !FileExists(f)) f = PaintTplGame();   // (MSC sans car.png : le gabarit)
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
    of.lpstrTitle = CarMsc() ? T(L"Choisir la peinture de la SATSUMA (peinte sur le gabarit template.png)", L"Choose the SATSUMA paint (painted on the template.png template)")
                             : T(L"Choisir la peinture de la CORRIS (peinte sur le gabarit car.png)", L"Choose the CORRIS paint (painted on the car.png template)");
    of.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_HIDEREADONLY;
    if (!GetOpenFileNameW(&of)) return;
    std::wstring why;
    Bitmap *src = LoadAnyImage(file, &why);
    bool ok = false;
    if (src) {
        Bitmap *game = LoadAnyImage(PaintTplGame(), NULL);   // taille du gabarit du jeu (2048 x 2048)
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
// Gabarit : une copie de Images\car.png du jeu (MSC : Images\template.png ; on peint sur la copie), montree dans l'Explorateur.
static void PaintTemplate()
{
    std::wstring t = LocalDir() + (CarMsc() ? L"gabarit-satsuma.png" : L"gabarit-voiture.png");
    CreateDirectoryW(LocalDir().c_str(), NULL);
    if (!FileExists(t) && !CopyFileW(PaintTplGame().c_str(), t.c_str(), TRUE)) {
        MessageBoxW(g_wnd, CarMsc() ? T(L"Images\\template.png est introuvable dans le dossier du jeu.", L"Images\\template.png was not found in the game folder.")
                                    : T(L"Images\\car.png est introuvable dans le dossier du jeu.", L"Images\\car.png was not found in the game folder."), L"MWCoop", MB_ICONWARNING | MB_OK);
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
    Para(g, CarMsc() ? T(L"Ta propre peinture sur la SATSUMA, peinte sur le gabarit du jeu. L'hôte l'envoie à ses invités.",
                         L"Your own paint on the SATSUMA, painted on the game's template. The host sends it to their guests.")
                     : T(L"Ta propre peinture sur la CORRIS, peinte sur le gabarit du jeu. L'hôte l'envoie à ses invités.",
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
    Text(g, on ? T(L"Ta peinture", L"Your paint") : CarMsc() && !FileExists(PaintGame()) ? T(L"Gabarit du jeu (Images\\template.png)", L"Game template (Images\\template.png)")
                                                 : T(L"Peinture du jeu (Images\\car.png)", L"Game paint (Images\\car.png)"),
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
        CarMsc() ? T(L"1. Gabarit : la carrosserie de la SATSUMA à plat (Images\\template.png du jeu, 2048 x 2048).",
                     L"1. Template: the SATSUMA's body laid flat (the game's Images\\template.png, 2048 x 2048).")
                 : T(L"1. Gabarit : la carrosserie du jeu à plat (2048 x 2048). Le dossier Images du jeu a aussi corris_template_layered.psd, en calques.",
                     L"1. Template: the game's body laid flat (2048 x 2048). The game's Images folder also has corris_template_layered.psd, in layers."),
        T(L"2. Peins par-dessus en gardant la taille, puis « Mon image… ».", L"2. Paint over it keeping the size, then \"My image…\"."),
        CarMsc() ? T(L"3. En jeu, achète « Custom paint made by artist » à l'atelier de Fleetari : la SATSUMA prend ta peinture.",
                     L"3. In game, buy \"Custom paint made by artist\" at Fleetari's repair shop: the SATSUMA takes your paint.")
                 : T(L"3. En jeu, achète « Custom paint made by artist » chez Fleetari (réparation) : la CORRIS prend ta peinture.",
                     L"3. In game, buy \"Custom paint made by artist\" at Fleetari's (repair shop): the CORRIS takes your paint."),
    };
    for (const wchar_t *s : steps) { Para(g, s, RectF(card.X + 24, sy, card.Width - 48, 40), 13, kInk); sy += 42; }
}

// ---- onglet ADDONS de la page Personnage (JD, 20/10 : « il y aura peut-etre d'autres voitures ») : voitures en
// EXTENSION, chacune un dossier <jeu>\MWCoop\<cle> avec addon.ini (Nom, NomCourt, Description, DescriptionEn, Livree=1),
// apercu.png et le modele (carrosserie.p205...). Rien n'est ecrit en dur ici : le lanceur public ne montre que ce qui est
// installe (la premiere, Lanzer Ebo III -- l'Evo III Gr. A, extension privee de JD, jamais publiee). Liste de cartes ; une
// carte ouvre son volet : livree a gauche (gabarit, import, utilisee, retirer), sa fiche a droite (pas les couleurs de
// la CORRIS). Sans livree, la voiture est comme la VAZ (couleur du joueur + numero) ; avec, l'image du joueur
// (%LOCALAPPDATA%\MWCoop\<cle>-livree.jpg, JPEG 2048 x 2048 de 2,1 Mo au plus : le mod l'envoie aux autres joueurs,
// EvoLivery) ; [Coop] EvoLivree (cle evo3) ou Livree_<cle> = perso.
// Depuis 1.8.0 (JD, 21/10 : « mettre les voitures a part comme l'editeur de carte ») : les addons se telechargent ici
// (catalogue plus bas) ; une carte pas encore installee porte « Telecharger », une installee plus ancienne que le
// catalogue « Mettre a jour ».
struct Addon { std::wstring key, dir, name, nameShort, desc, descEn, ver, remoteVer, group; bool livery = false; bool builtin = false; bool installed = true; };   // (builtin : la VAZ, livree avec le mod ; ver : Version= de addon.ini ; remoteVer : celle du catalogue)
static std::vector<Addon> g_addonList;
static ULONGLONG g_addonScanT;
static int g_addonSel = -1;          // carte ouverte (-1 : la liste)
static bool g_addonsTab;             // onglet Addons (g_tab == TAB_CAR)
static std::map<std::wstring, Bitmap *> g_addonPics;
static Bitmap *g_addonThumb;
static std::wstring g_addonThumbOf;
// Dossier des addons : <jeu>\MWCoop\addons (1.5.1, JD : « un raccourci pour ouvrir le dossier addons ») ; ceux poses avant
// directement dans MWCoop\<cle> (1.5.0) restent lus.
static std::wstring AddonsRoot() { return g_modRoot + L"MWCoop\\addons\\"; }
// Raccourci de la barre (ui.inc, TAB_ADDONS) et etat de l'onglet.
static void GoPage(int p);   // (ui.inc, inclus apres)
static bool AddonsOn() { return g_addonsTab; }
static void AddonsLeave() { g_addonsTab = false; }

// ---- catalogue des addons a telecharger (1.8.0, JD 21/10 : « que les joueurs puissent les telecharger du lanceur et les
// mettre a jour ainsi », bouton Telecharger / Mettre a jour, etiquettes NEW / UPDATE). Depot public a part,
// Parricidium/MWCoop-Addons (un retrait = une section enlevee du catalogue) : catalogue.txt (UTF-8, sections [cle] :
// Version, Nom, NomCourt, Description, DescriptionEn, Livree, Taille, Paquet, Apercu, ModMin), lu sur
// raw.githubusercontent.com (pas l'API : sa limite de 60 demandes par heure sert deja aux versions du mod) au premier
// affichage, a l'ouverture de l'onglet (5 min au moins entre deux) et toutes les 30 min ; copie hors ligne
// %LOCALAPPDATA%\MWCoop\addons-catalogue.txt, apercus dans %LOCALAPPDATA%\MWCoop\addons-apercus\<cle>-<version>.png.
// Paquet : un zip <cle>\..., avec Version= dans son addon.ini, installe dans MWCoop\addons\<cle> (extrait a cote dans
// .<cle>-nouveau, puis l'ancien dossier remplace : le scan saute les noms a point).
// Vus : [Lanceur] AddonsVus (cles du catalogue deja montrees) ; NEW sur la barre tant qu'une voiture du catalogue n'a pas
// ete vue, sur la carte pendant la session ou elle est apparue.
struct AddonCat { std::wstring key, ver, name, nameShort, desc, descEn, url, pic, modMin, group; long long size = 0; bool livery = false; };
static std::wstring g_addonsRepo = L"Parricidium/MWCoop-Addons";   // (/depotaddons : autre depot, pour les essais)
static std::vector<AddonCat> g_addonCat;                           // (sous g_cs)
static volatile bool g_addonCatBusy;
static ULONGLONG g_addonCatT, g_addonCatGoT;
static std::wstring g_addonDlKey;                                  // telechargement en cours (sous g_cs)
static volatile bool g_addonDlBusy;
static float g_addonDlProg = -1;
static std::set<std::wstring> g_addonSeen, g_addonSeenStart;
static bool g_addonSeenRead;
static bool GameProcessRunning(DWORD *pidOut);   // (launcher.cpp)
static void DeleteTree(const std::wstring &dir);
static std::wstring AddonPicCache(const std::wstring &key, const std::wstring &ver) { return LocalDir() + L"addons-apercus\\" + key + L"-" + ver + L".png"; }
static int AddonCmpVer(const std::wstring &a, const std::wstring &b) { return a.empty() || b.empty() ? (a.empty() ? (b.empty() ? 0 : -1) : 1) : CmpVer(a, b); }   // (sans Version= : la plus ancienne)

static std::vector<AddonCat> AddonCatParse(const std::string &src)
{
    std::vector<AddonCat> v;
    std::string t = src;
    if (t.size() >= 3 && (unsigned char)t[0] == 0xEF) t = t.substr(3);
    for (size_t p = 0; p < t.size();) {
        size_t e = t.find('\n', p); if (e == std::string::npos) e = t.size();
        std::string l = t.substr(p, e - p); p = e + 1;
        if (!l.empty() && l.back() == '\r') l.pop_back();
        if (l.empty() || l[0] == ';' || l[0] == '#') continue;
        if (l[0] == '[') {
            size_t q = l.find(']');
            std::wstring k = Widen(l.substr(1, q == std::string::npos ? std::string::npos : q - 1));
            bool ok = !k.empty();
            for (wchar_t c : k) if (!iswalnum(c) && c != L'_' && c != L'-') ok = false;   // (un nom de dossier simple : pas de .., \, :)
            if (ok) { AddonCat c; c.key = k; c.name = k; v.push_back(c); }
            else v.push_back(AddonCat());   // (section ignoree)
            continue;
        }
        size_t q = l.find('=');
        if (q == std::string::npos || v.empty() || v.back().key.empty()) continue;
        AddonCat &c = v.back();
        std::string k = l.substr(0, q); std::wstring val = Widen(l.substr(q + 1));
        if (k == "Version") c.ver = val; else if (k == "Nom") c.name = val; else if (k == "NomCourt") c.nameShort = val;
        else if (k == "Description") c.desc = val; else if (k == "DescriptionEn") c.descEn = val;
        else if (k == "Livree") c.livery = val == L"1"; else if (k == "Taille") c.size = _wtoi64(val.c_str());
        else if (k == "Paquet") c.url = val; else if (k == "Apercu") c.pic = val; else if (k == "ModMin") c.modMin = val;
        else if (k == "Categorie") c.group = val;
    }
    std::vector<AddonCat> o;
    for (const AddonCat &c : v)
        if (!c.key.empty() && !c.ver.empty() && !_wcsnicmp(c.url.c_str(), L"https://", 8)) { o.push_back(c); if (o.back().nameShort.empty()) o.back().nameShort = c.name; }
    return o;
}

static DWORD WINAPI AddonCatThread(void *)
{
    std::string t;
    DWORD code = 0;
    std::wstring local = LocalDir(), cache = local.empty() ? L"" : local + L"addons-catalogue.txt";
    bool got = HttpGet(L"https://raw.githubusercontent.com/" + g_addonsRepo + L"/main/catalogue.txt", &t, L"", false, &code) && code == 200 && t.find('[') != std::string::npos;
    if (got && !cache.empty()) {
        HANDLE f = CreateFileW(cache.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL);
        if (f != INVALID_HANDLE_VALUE) { DWORD w; WriteFile(f, t.data(), (DWORD)t.size(), &w, NULL); CloseHandle(f); }
    } else if (!cache.empty()) {
        std::vector<unsigned char> b;
        t = ReadAll(cache, b) ? std::string(b.begin(), b.end()) : "";
    }
    std::vector<AddonCat> cat = AddonCatParse(t);
    LaunchLog("addons : catalogue %s (code HTTP %lu), %d voiture(s)", got ? "recu" : "pas recu, copie locale", code, (int)cat.size());
    if (!local.empty())
        for (const AddonCat &c : cat) {   // apercus (une fois par version)
            std::wstring f = AddonPicCache(c.key, c.ver);
            if (FileExists(f) || _wcsnicmp(c.pic.c_str(), L"https://", 8)) continue;
            SHCreateDirectoryExW(NULL, (local + L"addons-apercus").c_str(), NULL);
            DWORD pc = 0;
            if (!HttpGet(c.pic, NULL, f, false, &pc) || pc != 200) DeleteFileW(f.c_str());
        }
    EnterCriticalSection(&g_cs);
    if (got || !cat.empty() || g_addonCat.empty()) g_addonCat = cat;
    LeaveCriticalSection(&g_cs);
    g_addonScanT = 0;
    g_addonCatBusy = false;
    return 0;
}
static void AddonCatRefresh()
{
    if (g_addonCatBusy) return;
    g_addonCatBusy = true;
    g_addonCatT = GetTickCount64() + 30 * 60 * 1000;
    HANDLE h = CreateThread(NULL, 0, AddonCatThread, NULL, 0, NULL);
    if (h) CloseHandle(h); else g_addonCatBusy = false;
}
// (barre : appele a chaque image pour la pastille ; la premiere fois, puis toutes les 30 min)
static void AddonCatTick()
{
    if (GetTickCount64() < g_addonCatT) return;
    static bool cached;
    if (!cached && !LocalDir().empty()) {   // (premiere fois : la copie locale tout de suite, pour les pastilles des le demarrage)
        cached = true;
        std::vector<unsigned char> b;
        if (ReadAll(LocalDir() + L"addons-catalogue.txt", b)) {
            std::vector<AddonCat> cat = AddonCatParse(std::string(b.begin(), b.end()));
            EnterCriticalSection(&g_cs);
            if (g_addonCat.empty()) g_addonCat = cat;
            LeaveCriticalSection(&g_cs);
            g_addonScanT = 0;
        }
    }
    AddonCatRefresh();
}

static void AddonSeenLoad()
{
    if (g_addonSeenRead) return;
    g_addonSeenRead = true;
    wchar_t b[2048] = {};
    GetPrivateProfileStringW(L"Lanceur", L"AddonsVus", L"", b, 2048, g_iniLauncher.c_str());
    for (wchar_t *ctx = NULL, *k = wcstok_s(b, L",", &ctx); k; k = wcstok_s(NULL, L",", &ctx)) g_addonSeen.insert(k);
    g_addonSeenStart = g_addonSeen;
}
// Onglet ouvert : tout le catalogue est vu (la pastille NEW de la barre s'eteint ; celles des cartes restent la session).
static void AddonSeenMark()
{
    AddonSeenLoad();
    bool ch = false;
    EnterCriticalSection(&g_cs);
    for (const AddonCat &c : g_addonCat) if (g_addonSeen.insert(c.key).second) ch = true;
    LeaveCriticalSection(&g_cs);
    if (!ch) return;
    std::wstring s;
    for (const std::wstring &k : g_addonSeen) { if (!s.empty()) s += L','; s += k; }
    WritePrivateProfileStringW(L"Lanceur", L"AddonsVus", s.c_str(), g_iniLauncher.c_str());
}
static void AddonsGo()
{
    if (g_tab != TAB_CAR) GoPage(TAB_CAR);
    g_addonsTab = true; g_addonSel = -1; g_addonScanT = 0;
    ULONGLONG now = GetTickCount64();
    if (now >= g_addonCatGoT) { g_addonCatGoT = now + 5 * 60 * 1000; AddonCatRefresh(); }
}

static DWORD WINAPI AddonDlThread(void *)
{
    std::wstring key;
    AddonCat c;
    EnterCriticalSection(&g_cs);
    key = g_addonDlKey;
    for (const AddonCat &x : g_addonCat) if (x.key == key) c = x;
    LeaveCriticalSection(&g_cs);
    std::wstring root = AddonsRoot(), stage = root + L"." + key + L"-nouveau", tmp = LocalDir() + L"addon-telechargement.zip", dst = root + key;
    SHCreateDirectoryExW(NULL, root.substr(0, root.size() - 1).c_str(), NULL);
    DeleteTree(stage);
    g_addonDlProg = 0;
    DWORD code = 0;
    std::string why;
    bool ok = !c.key.empty() && HttpGet(c.url, NULL, tmp, true, &code, &g_addonDlProg) && (code == 0 || code == 200);
    if (!ok) why = c.key.empty() ? "plus au catalogue" : code ? "HTTP " + std::to_string(code) : "erreur " + std::to_string(g_httpErr);
    else if (!Unzip(tmp, stage, &why)) ok = false;
    else if (!FileExists(stage + L"\\" + key + L"\\carrosserie.p205") || !FileExists(stage + L"\\" + key + L"\\addon.ini")
             || !FileExists(stage + L"\\" + key + L"\\infos.txt")) { ok = false; why = "paquet incomplet"; }   // (infos.txt : roues, volant -- paquets du 21/10 sans lui)
    DeleteFileW(tmp.c_str());
    if (ok) {
        DeleteTree(dst);
        if (GetFileAttributesW(dst.c_str()) != INVALID_FILE_ATTRIBUTES) { ok = false; why = "ancien dossier en cours d'utilisation"; }
        else if (!MoveFileW((stage + L"\\" + key).c_str(), dst.c_str())) { ok = false; why = "deplacement (erreur " + std::to_string(GetLastError()) + ")"; }
    }
    DeleteTree(stage);
    LaunchLog("addon %ls %ls : %s (%s)", key.c_str(), c.ver.c_str(), ok ? "installe" : "ECHEC", why.c_str());
    if (ok) SetStatus(K_OK, T(L"%s installée : en jeu, prends-la au F10 (onglet Voitures)", L"%s installed: in game, take it with F10 (Cars tab)"), c.name.c_str());
    else SetStatus(K_ERR, T(L"Téléchargement de %s impossible (%s)", L"Could not download %s (%s)"), c.name.c_str(), Widen(why).c_str());
    g_addonPics.erase(key);
    g_addonScanT = 0;
    g_addonDlProg = -1;
    EnterCriticalSection(&g_cs); g_addonDlKey.clear(); LeaveCriticalSection(&g_cs);
    g_addonDlBusy = false;
    return 0;
}
static void AddonDownload(const std::wstring &key)
{
    if (g_addonDlBusy || g_gameDir.empty() || LocalDir().empty()) return;
    DWORD pid = 0;
    if (GameProcessRunning(&pid)) { SetStatus(K_WARN, T(L"Ferme le jeu avant d'installer une voiture", L"Close the game before installing a car")); return; }
    g_addonDlBusy = true;
    EnterCriticalSection(&g_cs); g_addonDlKey = key; LeaveCriticalSection(&g_cs);
    HANDLE h = CreateThread(NULL, 0, AddonDlThread, NULL, 0, NULL);
    if (h) CloseHandle(h); else g_addonDlBusy = false;
}
static void WikiOpenAddons();   // (launcher.cpp : article Les addons du GUIDE)
static void AddonsOpenDir()
{
    if (g_gameDir.empty()) return;
    CreateDirectoryW((g_modRoot + L"MWCoop").c_str(), NULL);
    CreateDirectoryW(AddonsRoot().c_str(), NULL);
    ShellExecuteW(g_wnd, L"open", L"explorer.exe", (L"\"" + AddonsRoot() + L"\"").c_str(), NULL, SW_SHOWNORMAL);
    LaunchLog("addons : dossier ouvert (%ls)", AddonsRoot().c_str());
}
// Filtre de la liste par categorie (Categorie= de addon.ini et du catalogue ; la VAZ : « MWCoop ») : [Lanceur] AddonsFiltre.
static std::vector<Addon> g_addonAll;
static std::wstring AddonFilter()
{
    wchar_t b[64] = L"";
    GetPrivateProfileStringW(L"Lanceur", L"AddonsFiltre", L"", b, 64, g_iniLauncher.c_str());
    return b;
}
// categories presentes : MWCoop (la VAZ) d'abord, puis par nom ; au plus 7 (boutons 517-524 avec « Toutes »)
static std::vector<std::wstring> AddonGroups()
{
    std::vector<std::wstring> g;
    for (const Addon &a : g_addonAll) if (!a.group.empty() && std::find(g.begin(), g.end(), a.group) == g.end()) g.push_back(a.group);
    std::sort(g.begin(), g.end(), [](const std::wstring &x, const std::wstring &y) { return x == L"MWCoop" ? y != L"MWCoop" : y == L"MWCoop" ? false : x < y; });
    if (g.size() > 7) g.resize(7);
    return g;
}
static void AddonsSetFilter(int k)
{
    std::vector<std::wstring> g = AddonGroups();
    std::wstring f = k > 0 && k <= (int)g.size() ? g[k - 1] : L"";
    WritePrivateProfileStringW(L"Lanceur", L"AddonsFiltre", f.empty() ? NULL : f.c_str(), g_iniLauncher.c_str());
    g_scroll[TAB_CAR] = 0; g_addonSel = -1; g_addonScanT = 0;
    LaunchLog("addons : filtre %ls", f.empty() ? L"(toutes)" : f.c_str());
}
static void AddonsScan()
{
    ULONGLONG now = GetTickCount64();
    if (now < g_addonScanT) return;
    g_addonScanT = now + 3000;
    std::vector<Addon> v;
    if (!g_gameDir.empty())
      for (const std::wstring &root : { AddonsRoot(), g_modRoot + L"MWCoop\\" }) {
        WIN32_FIND_DATAW fd;
        HANDLE h = FindFirstFileW((root + L"*").c_str(), &fd);
        if (h != INVALID_HANDLE_VALUE) {
            do {
                if (!(fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) || wcschr(fd.cFileName, L'.')) continue;   // (« evo3.off » : addon coupe)
                std::wstring dir = root + fd.cFileName + L"\\";
                std::vector<unsigned char> b;
                if (!FileExists(dir + L"carrosserie.p205") || !ReadAll(dir + L"addon.ini", b)) continue;
                bool dup = false;
                for (const Addon &x : v) if (!_wcsicmp(x.key.c_str(), fd.cFileName)) dup = true;   // (deja dans addons\)
                if (dup) continue;
                Addon a; a.key = fd.cFileName; a.dir = dir; a.name = a.key;
                std::string t(b.begin(), b.end());
                if (t.size() >= 3 && (unsigned char)t[0] == 0xEF) t = t.substr(3);
                size_t p = 0;
                while (p < t.size()) {
                    size_t e = t.find('\n', p); if (e == std::string::npos) e = t.size();
                    std::string l = t.substr(p, e - p); p = e + 1;
                    if (!l.empty() && l.back() == '\r') l.pop_back();
                    size_t q = l.find('=');
                    if (q == std::string::npos || l[0] == ';' || l[0] == '[') continue;
                    std::string k = l.substr(0, q); std::wstring val = Widen(l.substr(q + 1));
                    if (k == "Nom") a.name = val; else if (k == "NomCourt") a.nameShort = val;
                    else if (k == "Description") a.desc = val; else if (k == "DescriptionEn") a.descEn = val;
                    else if (k == "Livree") a.livery = val == L"1"; else if (k == "Version") a.ver = val;
                    else if (k == "Categorie") a.group = val;
                }
                if (a.nameShort.empty()) a.nameShort = a.name;
                v.push_back(a);
            } while (FindNextFileW(h, &fd));
            FindClose(h);
        }
    }
    // catalogue : version publiee des installees, cartes « Telecharger » pour les autres
    if (!g_gameDir.empty() && !CarMsc()) {
        EnterCriticalSection(&g_cs);
        for (const AddonCat &c : g_addonCat) {
            Addon *in = NULL;
            for (Addon &x : v) if (!_wcsicmp(x.key.c_str(), c.key.c_str())) in = &x;
            if (in) { in->remoteVer = c.ver; if (in->group.empty()) in->group = c.group; continue; }   // (installee avant les categories : celle du catalogue)
            Addon a; a.key = c.key; a.name = c.name; a.nameShort = c.nameShort; a.desc = c.desc; a.descEn = c.descEn; a.livery = c.livery; a.group = c.group;
            a.remoteVer = c.ver; a.installed = false;
            v.push_back(a);
        }
        LeaveCriticalSection(&g_cs);
    }
    std::sort(v.begin(), v.end(), [](const Addon &x, const Addon &y) { return x.name < y.name; });
    // La VAZ-2108, livree avec le mod (MWCoop\vaz, jamais dans addons\) : une carte comme les autres, avec sa livree (JD, 21/10 :
    // « la VAZ reste compilee avec le mod, mais donne la possibilite de faire une livree »). Toujours en tete.
    if (!g_gameDir.empty() && !CarMsc()) {
        std::wstring vd = g_modRoot + L"MWCoop\\vaz\\";
        if (FileExists(vd + L"carrosserie.p205")) {
            Addon a; a.key = L"vaz"; a.dir = vd; a.name = L"VAZ-2108"; a.nameShort = L"VAZ"; a.livery = true; a.builtin = true; a.group = L"MWCoop";
            a.desc = L"Livrée avec le mod · la voiture de tout le monde · seize couleurs"; a.descEn = L"Ships with the mod · everyone's car · sixteen colours";
            v.insert(v.begin(), a);
        }
    }
    std::wstring sel = g_addonSel >= 0 && g_addonSel < (int)g_addonList.size() ? g_addonList[g_addonSel].key : L"";   // (le catalogue arrive : les rangs changent)
    // filtre par categorie (Gr. A, Gr. B... -- JD, 23/10) : la liste montree ; g_addonAll garde tout (les boutons du filtre)
    for (Addon &a : v)   // (sans Categorie= : le « Gr. X » de la description, sinon Autres)
        if (a.group.empty()) {
            size_t i = a.desc.find(L"Gr. ");
            a.group = i != std::wstring::npos && i + 4 < a.desc.size() && a.desc[i + 4] != L' ' ? a.desc.substr(i, 5) : T(L"Autres", L"Others");
        }
    g_addonAll = v;
    std::wstring f = AddonFilter();
    if (!f.empty()) {
        bool any = false;
        for (const Addon &a : v) if (a.group == f) any = true;
        if (any) { std::vector<Addon> w; for (const Addon &a : v) if (a.group == f) w.push_back(a); v = w; }
    }
    g_addonList = v;
    g_addonSel = -1;
    for (int i = 0; i < (int)v.size() && !sel.empty(); i++) if (v[i].key == sel && v[i].installed) g_addonSel = i;
}
static const Addon *AddonCur() { return g_addonSel >= 0 && g_addonSel < (int)g_addonList.size() ? &g_addonList[g_addonSel] : NULL; }
static std::wstring AddonDir(const Addon &a) { return a.dir; }
static std::wstring AddonLiveryFile(const Addon &a) { return LocalDir() + a.key + L"-livree.jpg"; }
static std::string AddonCfgKey(const Addon &a) { return a.key == L"evo3" ? "EvoLivree" : "Livree_" + Narrow(a.key); }
static Bitmap *AddonPic(const Addon &a)
{
    auto it = g_addonPics.find(a.key);
    if (it != g_addonPics.end()) return it->second;
    if (!a.installed) {   // (pas encore installee : l'apercu du catalogue, quand il est arrive)
        std::wstring f = AddonPicCache(a.key, a.remoteVer);
        Bitmap *b = FileExists(f) ? LoadAnyImage(f, NULL) : NULL;
        if (b) g_addonPics[a.key] = b;
        return b;
    }
    Bitmap *b = LoadAnyImage(AddonDir(a) + L"apercu.png", NULL);
    g_addonPics[a.key] = b;
    return b;
}
// Etiquettes : NEW (pas installee, apparue au catalogue depuis la derniere visite d'avant cette session), UPDATE (installee,
// plus ancienne que le catalogue).
static bool AddonIsNew(const Addon &a) { AddonSeenLoad(); return !a.installed && !g_addonSeenStart.count(a.key); }
static bool AddonHasUpdate(const Addon &a) { return a.installed && !a.builtin && !a.remoteVer.empty() && AddonCmpVer(a.remoteVer, a.ver) > 0; }
// Barre (ui.inc) : NEW tant qu'une voiture du catalogue n'a pas ete vue, UPDATE si une installee a une mise a jour.
static void AddonsScan();
static void AddonsBadges(bool &isNew, bool &upd)
{
    AddonCatTick();
    AddonsScan();
    AddonSeenLoad();
    isNew = upd = false;
    for (const Addon &a : g_addonList) {
        if (!a.installed && !g_addonSeen.count(a.key)) isNew = true;
        if (AddonHasUpdate(a)) upd = true;
    }
}
static bool AddonLiveryOn(const Addon &a)
{
    if (g_gameDir.empty()) return false;
    char v[16] = "";
    GetPrivateProfileStringA("Coop", AddonCfgKey(a).c_str(), "", v, sizeof(v), ModIniA().c_str());
    return strcmp(v, "perso") == 0 && FileExists(AddonLiveryFile(a));
}
static void AddonLiveryUse(const Addon &a, bool on)
{
    if (g_gameDir.empty()) return;
    EnsureModDir();
    WritePrivateProfileStringA("Coop", AddonCfgKey(a).c_str(), on ? "perso" : "", ModIniA().c_str());
    g_addonThumbOf.clear();
}
// Miniature (512 x 512) : la livree du joueur, sinon le gabarit de l'addon.
static Bitmap *AddonThumb(const Addon &a)
{
    std::wstring f = AddonLiveryOn(a) ? AddonLiveryFile(a) : AddonDir(a) + L"gabarit-livree.png";
    WIN32_FILE_ATTRIBUTE_DATA at;
    std::wstring key = f;
    if (GetFileAttributesExW(f.c_str(), GetFileExInfoStandard, &at)) key += L"|" + std::to_wstring(at.ftLastWriteTime.dwLowDateTime);
    if (key == g_addonThumbOf) return g_addonThumb;
    g_addonThumbOf = key;
    delete g_addonThumb; g_addonThumb = NULL;
    if (Bitmap *b = LoadAnyImage(f, NULL)) {
        g_addonThumb = new Bitmap(512, 512, PixelFormat32bppPARGB);
        Graphics tg(g_addonThumb);
        tg.SetInterpolationMode(InterpolationModeHighQualityBicubic);
        tg.DrawImage(b, RectF(0, 0, 512, 512));
        delete b;
    }
    return g_addonThumb;
}
static void AddonImport()
{
    const Addon *pa = AddonCur();
    if (!pa) return;
    Addon a = *pa;
    wchar_t file[MAX_PATH] = L"";
    OPENFILENAMEW of = { sizeof(of) };
    of.hwndOwner = g_wnd;
    of.lpstrFilter = L"Images\0*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic;*.heif;*.avif;*.jfif\0*.*\0*.*\0";
    of.lpstrFile = file;
    of.nMaxFile = MAX_PATH;
    std::wstring init = LocalDir();
    of.lpstrInitialDir = init.c_str();
    std::wstring title = std::wstring(T(L"Choisir la livrée : ", L"Choose the livery: ")) + a.name + T(L" (peinte sur le gabarit)", L" (painted on the template)");
    of.lpstrTitle = title.c_str();
    of.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_HIDEREADONLY;
    if (!GetOpenFileNameW(&of)) return;
    std::wstring why;
    Bitmap *src = LoadAnyImage(file, &why);
    bool ok = false;
    if (src) {
        const int W = 2048, H = 2048;
        Bitmap *base = LoadAnyImage(AddonDir(a) + L"peinture.png", NULL);   // (parties transparentes : la voiture sans livree)
        Bitmap dst(W, H, PixelFormat24bppRGB);
        {
            Graphics dg(&dst);
            dg.SetInterpolationMode(InterpolationModeHighQualityBicubic);
            dg.SetPixelOffsetMode(PixelOffsetModeHalf);
            if (base) dg.DrawImage(base, RectF(0, 0, (REAL)W, (REAL)H));
            else dg.Clear(Color(255, 236, 236, 236));
            dg.DrawImage(src, RectF(0, 0, (REAL)W, (REAL)H));
        }
        delete base;
        CLSID jpg;
        CreateDirectoryW(LocalDir().c_str(), NULL);
        ok = EncoderClsid(L"image/jpeg", &jpg);
        // qualite 90, puis moins si le fichier depasse ce que le mod accepte d'envoyer (2,1 Mo)
        for (ULONG q : { 90UL, 80UL, 70UL, 55UL }) {
            if (!ok) break;
            EncoderParameters ep; ULONG qq = q;
            ep.Count = 1; ep.Parameter[0].Guid = EncoderQuality; ep.Parameter[0].Type = EncoderParameterValueTypeLong; ep.Parameter[0].NumberOfValues = 1; ep.Parameter[0].Value = &qq;
            ok = dst.Save(AddonLiveryFile(a).c_str(), &jpg, &ep) == Ok;
            WIN32_FILE_ATTRIBUTE_DATA at;
            if (ok && GetFileAttributesExW(AddonLiveryFile(a).c_str(), GetFileExInfoStandard, &at) && at.nFileSizeLow < 2100000 && at.nFileSizeHigh == 0) { LaunchLog("livree %ls importee : qualite %lu, %lu octets", a.key.c_str(), q, at.nFileSizeLow); break; }
        }
        if (!ok) why = T(L"enregistrement impossible", L"can't save it");
    }
    delete src;
    if (ok) AddonLiveryUse(a, true);
    else {
        LaunchLog("livree %ls : echec (%ls)", a.key.c_str(), why.c_str());
        std::wstring msg = std::wstring(T(L"Cette image n'a pas pu être lue : ", L"This image could not be read: ")) + why + L".";
        MessageBoxW(g_wnd, msg.c_str(), L"MWCoop", MB_ICONWARNING | MB_OK);
    }
}
// Gabarit : copie de <cle>\gabarit-livree.png dans %LOCALAPPDATA%\MWCoop (on peint sur la copie), montree dans l'Explorateur.
static void AddonTemplate()
{
    const Addon *a = AddonCur();
    if (!a) return;
    std::wstring t = LocalDir() + L"gabarit-" + a->key + L".png";
    CreateDirectoryW(LocalDir().c_str(), NULL);
    if (!CopyFileW((AddonDir(*a) + L"gabarit-livree.png").c_str(), t.c_str(), FALSE) && !FileExists(t)) {
        std::wstring m = std::wstring(T(L"Gabarit introuvable (MWCoop\\", L"Template not found (MWCoop\\")) + a->key + T(L"\\gabarit-livree.png) : réinstalle l'addon.", L"\\gabarit-livree.png): reinstall the add-on.");
        MessageBoxW(g_wnd, m.c_str(), L"MWCoop", MB_ICONWARNING | MB_OK);
        return;
    }
    std::wstring arg = L"/select,\"" + t + L"\"";
    ShellExecuteW(g_wnd, L"open", L"explorer.exe", arg.c_str(), NULL, SW_SHOWNORMAL);
}
// Desinstaller (JD, 21/10 : « une option pour desinstaller les voitures dans addons, a part la VAZ ») : son dossier supprime,
// sa livree gardee (%LOCALAPPDATA%, reprise si elle est retelechargee) ; une voiture du catalogue y reste, avec Telecharger.
static void AddonUninstall()
{
    const Addon *pa = AddonCur();
    if (!pa || pa->builtin || !pa->installed || pa->dir.empty() || g_addonDlBusy) return;
    Addon a = *pa;
    DWORD pid = 0;
    if (GameProcessRunning(&pid)) { SetStatus(K_WARN, T(L"Ferme le jeu avant de désinstaller une voiture", L"Close the game before uninstalling a car")); return; }
    std::wstring q = std::wstring(T(L"Désinstaller « ", L"Uninstall “")) + a.name + T(L" » ?\n\nTa livrée est gardée. Tu pourras la retélécharger depuis Addons (bouton Télécharger).",
                                                                                    L"”?\n\nYour livery is kept. You can download it again from Add-ons (Download button).");
    if (MessageBoxW(g_wnd, q.c_str(), L"MWCoop", MB_YESNO | MB_ICONQUESTION) != IDYES) return;
    std::wstring dir = a.dir;
    while (!dir.empty() && dir.back() == L'\\') dir.pop_back();
    DeleteTree(dir);
    bool ok = GetFileAttributesW(dir.c_str()) == INVALID_FILE_ATTRIBUTES;
    LaunchLog("addon %ls : desinstallation %s (%ls)", a.key.c_str(), ok ? "faite" : "INCOMPLETE", dir.c_str());
    if (ok) SetStatus(K_OK, T(L"%s désinstallée", L"%s uninstalled"), a.name.c_str());
    else SetStatus(K_ERR, T(L"%s : des fichiers n'ont pas pu être supprimés (ouverts ailleurs ?)", L"%s: some files could not be deleted (open elsewhere?)"), a.name.c_str());
    g_addonPics.erase(a.key);
    AddonSeenLoad();
    g_addonSeenStart.insert(a.key);   // (de retour avec « Telecharger », sans etiquette NEW)
    g_addonSel = -1;
    g_addonScanT = 0;
}
static void AddonRemove()
{
    const Addon *a = AddonCur();
    if (!a) return;
    DeleteFileW(AddonLiveryFile(*a).c_str());
    AddonLiveryUse(*a, false);
    LaunchLog("livree %ls retiree", a->key.c_str());
}
// Image contenue (proportions gardees) et coins arrondis.
static void AddonDrawPic(Graphics &g, Bitmap *b, RectF r, float rad)
{
    GraphicsPath p; RoundRect(p, r, rad);
    SolidBrush f(TH(field)); g.FillPath(&f, &p);
    if (!b) { Icon(g, IC_CAR, r.X + r.Width / 2, r.Y + r.Height / 2, 44, WithA(kGrey, 0.6f), 1.4f); return; }
    float k = max(r.Width / b->GetWidth(), r.Height / b->GetHeight()), w = b->GetWidth() * k, h = b->GetHeight() * k;
    GraphicsState st = g.Save(); g.SetClip(&p, CombineModeIntersect);   // (sans ResetClip : la liste des addons est elle-meme decoupee)
    g.DrawImage(b, RectF(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h));
    g.Restore(st);
}
// Liste : une carte par addon installe.
// Liste : galerie (cartes, 3 par rangee ; par defaut) ou liste (une ligne par addon) -- JD 21/10, quatre voitures : la
// deuxieme rangee de cartes depassait sous le cadre ; zone bornee au cadre, defilement a la molette et ascenseur.
// [Lanceur] AddonsVue=1 : liste.
static void DrawScrollBar(Graphics &g, RectF area, float sc, float ms);   // (mods.inc)
static int g_addonView = -1;
static int AddonsView() { if (g_addonView < 0) g_addonView = GetPrivateProfileIntW(L"Lanceur", L"AddonsVue", 0, g_iniLauncher.c_str()) == 1 ? 1 : 0; return g_addonView; }
static void AddonsSetView(int v) { g_addonView = v; g_scroll[TAB_CAR] = 0; WritePrivateProfileStringW(L"Lanceur", L"AddonsVue", v == 1 ? L"1" : NULL, g_iniLauncher.c_str()); }
static RectF AddonsArea() { return RectF(kCarCard.X + 16, kCarCard.Y + 104, kCarPanel.X + kCarPanel.Width - kCarCard.X - 32, kCarCard.Height - 104 - 14); }
static const float kAddonRowH = 92;
static RectF AddonCardRect(int i)   // (sans le defilement)
{
    RectF a = AddonsArea();
    if (AddonsView() == 1) return RectF(a.X + 8, a.Y + i * kAddonRowH, a.Width - 30, kAddonRowH - 10);
    return RectF(a.X + 8 + (i % 3) * 324.0f, a.Y + (i / 3) * 290.0f, 300, 270);
}
static float AddonsMaxScroll()
{
    if (!g_addonsTab || g_addonSel >= 0 || g_addonList.empty()) return 0.0f;
    int n = (int)min(g_addonList.size(), (size_t)20);
    float total = AddonsView() == 1 ? n * kAddonRowH : ((n + 2) / 3) * 290.0f;
    return max(0.0f, total - AddonsArea().Height);
}
static void DrawAddonList(Graphics &g)
{
    RectF area(kCarCard.X, kCarCard.Y, kCarPanel.X + kCarPanel.Width - kCarCard.X, kCarCard.Height);
    Glass(area);
    Title(g, T(L"Addons", L"Add-ons"), RectF(area.X + 24, area.Y + 20, 400, 30), 22, kInk);
    AddonSeenMark();
    Para(g, T(L"Voitures en extension : télécharge-les ici, elles se mettent à jour d'un clic. En partie, on les prend au F10 (onglet Voitures).",
              L"Extension cars: download them here, they update in one click. In game, take them with F10 (Cars tab)."),
         RectF(area.X + 24, area.Y + 56, area.Width - 48 - 400, 40), 12.5f, kGrey);
    UiButton(g, RectF(area.X + area.Width - 24 - 264, area.Y + 20, 264, 38), T(L"Ouvrir le dossier des addons", L"Open the add-ons folder"), UI_ADDON_DIR, false, IC_FOLDER, true, 10);
    UiButton(g, RectF(area.X + area.Width - 24 - 264 - 12 - 108, area.Y + 20, 108, 38), T(L"Aide", L"Help"), UI_ADDON_HELP, false, IC_BOOK, true, 10);
    if (!g_addonList.empty())
        UiSeg(g, RectF(area.X + area.Width - 24 - 264 - 12 - 108 - 12 - 200, area.Y + 22, 200, 34), { T(L"Galerie", L"Gallery"), T(L"Liste", L"List") }, AddonsView(), UI_ADDON_VIEW, 12);
    // filtre par categorie (sous les boutons, a droite) : Toutes, MWCoop, Gr. A, Gr. B... -- seulement s'il y en a plusieurs
    {
        std::vector<std::wstring> gs = AddonGroups();
        if (gs.size() > 1) {
            std::wstring f = AddonFilter();
            std::vector<std::wstring> labels; labels.push_back(T(L"Toutes", L"All"));
            for (const std::wstring &s : gs) labels.push_back(s);
            float x = area.X + area.Width - 24;
            for (int k = (int)labels.size() - 1; k >= 0; k--) {
                float w = PillW(labels[k].c_str(), 11) + 26;
                x -= w;
                bool on = k == 0 ? f.empty() || std::find(gs.begin(), gs.end(), f) == gs.end() : labels[k] == f;
                UiButton(g, RectF(x, area.Y + 64, w, 28), labels[k].c_str(), UI_ADDON_FILTER + k, on, -1, true, 14);
                x -= 6;
            }
        }
    }
    if (g_addonList.empty()) {
        Icon(g, IC_CAR, area.X + area.Width / 2, area.Y + area.Height / 2 - 50, 56, WithA(kGrey, 0.6f), 1.4f);
        Text(g, T(L"Aucun addon installé", L"No add-on installed"), RectF(area.X, area.Y + area.Height / 2 - 10, area.Width, 26), 15, FontStyleBold, kGrey);
        Text(g, g_addonCatBusy ? T(L"Recherche des voitures à télécharger…", L"Looking for cars to download…")
                               : T(L"Catalogue des voitures injoignable : réessaie plus tard, ou copie le dossier d'un addon dans le dossier des addons.", L"Car catalogue unreachable: try again later, or copy an add-on's folder into the add-ons folder."),
             RectF(area.X + 40, area.Y + area.Height / 2 + 20, area.Width - 80, 22), 12.5f, FontStyleRegular, kGrey);
        UiButton(g, RectF(area.X + area.Width / 2 - 140, area.Y + area.Height / 2 + 58, 280, 44), T(L"Ouvrir le dossier des addons", L"Open the add-ons folder"), UI_ADDON_DIR, true, IC_FOLDER);
        return;
    }
    RectF ar = AddonsArea();
    float ms = AddonsMaxScroll(), sc = g_scroll[TAB_CAR] = min(max(g_scroll[TAB_CAR], 0.0f), ms);
    bool list = AddonsView() == 1;
    GraphicsState st = g.Save(); g.SetClip(ar, CombineModeIntersect);
    for (int i = 0; i < (int)g_addonList.size() && i < 20; i++) {
        const Addon &a = g_addonList[i];
        RectF r = AddonCardRect(i); r.Y -= sc;
        if (r.Y + r.Height < ar.Y || r.Y > ar.Y + ar.Height) continue;
        bool hot = g_uiHot == UI_ADDON_CARD + i;
        GraphicsPath p; RoundRect(p, r, list ? 14.0f : 16.0f);
        SolidBrush cb(hot ? TH(cardSel) : TH(card)); g.FillPath(&cb, &p);
        Pen pen(hot ? kAcc : TH(choiceBorder), hot ? 1.6f : 1.0f); g.DrawPath(&pen, &p);
        std::wstring desc = !g_fr && !a.descEn.empty() ? a.descEn : a.desc;
        std::wstring liv = a.livery && a.installed ? (AddonLiveryOn(a) ? T(L"Ta livrée", L"Your livery") : T(L"Couleur + numéro", L"Colour + number")) : L"";
        // bouton du catalogue : Telecharger (pas installee) / Mettre a jour (plus ancienne) ; pendant le telechargement : le %
        bool upd = AddonHasUpdate(a), get = !a.installed || upd;
        std::wstring btn;
        bool btnOn = !g_addonDlBusy;
        if (get) {
            AddonCat c;
            EnterCriticalSection(&g_cs);
            for (const AddonCat &x : g_addonCat) if (x.key == a.key) c = x;
            bool mine = g_addonDlBusy && g_addonDlKey == a.key;
            LeaveCriticalSection(&g_cs);
            wchar_t b[96];
            if (mine) swprintf_s(b, T(L"Téléchargement… %d %%", L"Downloading… %d%%"), (int)(max(0.0f, g_addonDlProg) * 100));
            else if (!c.modMin.empty() && !g_localVer.empty() && CmpVer(c.modMin, g_localVer) > 0) { swprintf_s(b, T(L"MWCoop %s requis", L"MWCoop %s required"), c.modMin.c_str()); btnOn = false; }
            else if (upd) swprintf_s(b, T(L"Mettre à jour · v%s", L"Update · v%s"), a.remoteVer.c_str());
            else if (c.size > 0) swprintf_s(b, T(L"Télécharger · %.0f Mo", L"Download · %.0f MB"), c.size / 1048576.0);
            else swprintf_s(b, L"%s", T(L"Télécharger", L"Download"));
            btn = b;
        }
        RectF br;
        if (list) {
            // ligne : vignette a gauche (16/9), nom, fiche, livree ; bouton du catalogue a droite
            AddonDrawPic(g, AddonPic(a), RectF(r.X + 8, r.Y + 8, (r.Height - 16) * 16 / 9, r.Height - 16), 10);
            float tx = r.X + 8 + (r.Height - 16) * 16 / 9 + 18, tw = r.X + r.Width - 16 - tx - (get ? 236 : 0);
            Text(g, a.name, RectF(tx, r.Y + 10, tw, 24), 16, FontStyleBold, kInk, StringAlignmentNear);
            Text(g, desc, RectF(tx, r.Y + 34, tw, 20), 12.5f, FontStyleRegular, kGrey, StringAlignmentNear);
            if (!liv.empty()) Text(g, liv, RectF(tx, r.Y + 54, tw, 20), 12, FontStyleBold, AddonLiveryOn(a) ? kAcc : kGrey, StringAlignmentNear);
            br = RectF(r.X + r.Width - 16 - 220, r.Y + (r.Height - 40) / 2, 220, 40);
        } else {
            AddonDrawPic(g, AddonPic(a), RectF(r.X + 10, r.Y + 10, r.Width - 20, 160), 12);
            Text(g, a.name, RectF(r.X + 16, r.Y + 180, r.Width - 32, 26), 16, FontStyleBold, kInk, StringAlignmentNear);
            Text(g, desc, RectF(r.X + 16, r.Y + 206, r.Width - 32, 22), 12.5f, FontStyleRegular, kGrey, StringAlignmentNear);
            if (!liv.empty() && !get) Text(g, liv, RectF(r.X + 16, r.Y + 232, r.Width - 32, 22), 12, FontStyleBold, AddonLiveryOn(a) ? kAcc : kGrey, StringAlignmentNear);
            br = RectF(r.X + 16, r.Y + 228, r.Width - 32, 34);
        }
        // etiquettes NEW / UPDATE, en haut a gauche de l'image
        {
            float px = list ? r.X + 14 : r.X + 18, py = list ? r.Y + 14 : r.Y + 18;
            if (AddonIsNew(a)) { const wchar_t *s = L"NEW"; float w = PillW(s, 10); Pill(g, RectF(px, py, w, 20), kAcc, s, kOnAcc, 10); px += w + 6; }
            if (upd) { const wchar_t *s = L"UPDATE"; float w = PillW(s, 10); Pill(g, RectF(px, py, w, 20), kWarn, s, Color(255, 255, 255, 255), 10); }
        }
        // (zone cliquable : la partie visible de la carte seulement ; une carte pas installee n'ouvre rien)
        RectF hz = r; float top = max(hz.Y, ar.Y), bot = min(hz.Y + hz.Height, ar.Y + ar.Height);
        if (bot - top > 12 && a.installed) { hz.Y = top; hz.Height = bot - top; HotZone(hz, UI_ADDON_CARD + i); }
        if (get) {
            if (br.Y >= ar.Y && br.Y + br.Height <= ar.Y + ar.Height) UiButton(g, br, btn, UI_ADDON_GET + i, true, g_addonDlBusy ? -1 : IC_DOWNLOAD, btnOn, 10);
            else {   // (a moitie hors du cadre : dessine sans zone cliquable)
                GraphicsPath bp; RoundRect(bp, br, 10);
                SolidBrush bb(kAcc); g.FillPath(&bb, &bp);
            }
        }
    }
    g.Restore(st);
    if (ms > 0) DrawScrollBar(g, ar, sc, ms);
}
// Volet d'un addon : livree a gauche, sa fiche a droite.
static void DrawAddonView(Graphics &g, const Addon &a)
{
    Glass(kCarCard);
    Glass(kCarPanel);
    RectF card = kCarCard;
    Title(g, a.livery ? (std::wstring(T(L"Livrée : ", L"Livery: ")) + a.nameShort).c_str() : a.name.c_str(), RectF(card.X + 24, card.Y + 20, card.Width - 48 - 260, 30), 22, kInk);
    UiButton(g, RectF(card.X + card.Width - 24 - 140, card.Y + 18, 140, 34), T(L"‹ Addons", L"‹ Add-ons"), UI_ADDON_BACK, false, -1, true, 10);
    UiButton(g, RectF(card.X + card.Width - 24 - 140 - 10 - 96, card.Y + 18, 96, 34), T(L"Aide", L"Help"), UI_ADDON_HELP, false, IC_BOOK, true, 10);
    if (a.livery) {
        Para(g, T(L"Sans livrée, elle prend ta couleur et ton numéro, comme la VAZ. Avec, tous les joueurs voient la tienne.",
                  L"Without a livery, it takes your colour and number, like the VAZ. With one, every player sees yours."),
             RectF(card.X + 24, card.Y + 56, card.Width - 48, 40), 12.5f, kGrey);
        bool has = FileExists(AddonLiveryFile(a)), on = AddonLiveryOn(a);
        RectF th(card.X + 24, card.Y + 104, 300, 300);
        GraphicsPath tp; RoundRect(tp, th, 14);
        SolidBrush tb(TH(field)); g.FillPath(&tb, &tp);
        if (Bitmap *b = AddonThumb(a)) { g.SetClip(&tp); g.DrawImage(b, th); g.ResetClip(); }
        else Icon(g, IC_IMAGE, th.X + th.Width / 2, th.Y + th.Height / 2, 48, WithA(kGrey, 0.6f), 1.4f);
        Pen tpen(on ? kAcc : TH(fieldBorder), on ? 2.0f : 1.0f); g.DrawPath(&tpen, &tp);
        Text(g, on ? T(L"Ta livrée", L"Your livery") : T(L"Gabarit (couleur + numéro)", L"Template (colour + number)"),
             RectF(th.X, th.Y + th.Height + 6, th.Width, 22), 12.5f, FontStyleBold, on ? kInk : kGrey, StringAlignmentCenter);
        float bx = th.X + th.Width + 24, bw = card.X + card.Width - 24 - bx, by = th.Y;
        UiButton(g, RectF(bx, by, bw, 44), T(L"Gabarit", L"Template"), UI_CAR_EVOTPL, false, IC_FOLDER); by += 56;
        UiButton(g, RectF(bx, by, bw, 44), T(L"Mon image…", L"My image…"), UI_CAR_EVOIMP, true, IC_IMAGE); by += 56;
        if (has) {
            UiButton(g, RectF(bx, by, bw, 44), on ? T(L"Utilisée : oui", L"In use: yes") : T(L"Utilisée : non", L"In use: no"), UI_CAR_EVOUSE, false, on ? IC_CHECK : -1); by += 56;
            UiButton(g, RectF(bx, by, bw, 44), T(L"Retirer", L"Remove"), UI_CAR_EVODEL, false, IC_TRASH); by += 56;
        }
        float sy = th.Y + th.Height + 44;
        const wchar_t *steps[] = {
            T(L"1. Gabarit : la voiture dépliée à plat (2048 x 2048). La zone hachurée en rouge reçoit ton numéro : ne dessine rien d'important dessous.",
              L"1. Template: the car laid flat (2048 x 2048). The red hatched area gets your number: draw nothing important under it."),
            T(L"2. Peins par-dessus en gardant la taille, puis « Mon image… ».", L"2. Paint over it keeping the size, then \"My image…\"."),
            T(L"3. En jeu, prends-la au F10 (onglet Voitures) : tout le monde voit ta livrée.", L"3. In game, take it with F10 (Cars tab): everyone sees your livery."),
        };
        for (const wchar_t *st : steps) { Para(g, st, RectF(card.X + 24, sy, card.Width - 48, 40), 13, kInk); sy += 42; }
    } else Para(g, T(L"Pas de livrée pour cet addon : couleur et numéro du joueur.", L"No livery for this add-on: the player's colour and number."), RectF(card.X + 24, card.Y + 56, card.Width - 48, 40), 13, kGrey);
    // fiche (a la place des couleurs de la CORRIS)
    RectF pn = kCarPanel;
    Title(g, T(L"Fiche", L"Details"), RectF(pn.X + 24, pn.Y + 20, 200, 26), 16, kInk);
    AddonDrawPic(g, AddonPic(a), RectF(pn.X + 24, pn.Y + 60, pn.Width - 48, 180), 14);
    Text(g, a.name, RectF(pn.X + 24, pn.Y + 252, pn.Width - 48, 28), 17, FontStyleBold, kInk, StringAlignmentNear);
    Para(g, !g_fr && !a.descEn.empty() ? a.descEn : a.desc, RectF(pn.X + 24, pn.Y + 284, pn.Width - 48, 44), 13, kGrey);
    float y = pn.Y + 344;
    const wchar_t *rows[][2] = {
        { T(L"En jeu", L"In game"), T(L"F10 › Voitures", L"F10 › Cars") },
        { T(L"Couleur", L"Colour"), T(L"unique par joueur", L"unique per player") },
        { T(L"Numéro", L"Number"), T(L"le sien, sur les portières", L"theirs, on the doors") },
        { a.builtin ? T(L"Intégrée", L"Built in") : T(L"Sans l'addon", L"Without the add-on"),
          a.builtin ? T(L"au mod : tout le monde l'a", L"into the mod: everyone has it") : T(L"les autres voient une VAZ", L"others see a VAZ") },
    };
    for (auto &rw : rows) {
        Text(g, rw[0], RectF(pn.X + 24, y, 120, 22), 12.5f, FontStyleBold, kInk, StringAlignmentNear);
        Text(g, rw[1], RectF(pn.X + 144, y, pn.Width - 168, 22), 12.5f, FontStyleRegular, kGrey, StringAlignmentNear);
        y += 30;
    }
    if (a.livery) {
        bool on = AddonLiveryOn(a);
        Text(g, T(L"Livrée", L"Livery"), RectF(pn.X + 24, y, 120, 22), 12.5f, FontStyleBold, kInk, StringAlignmentNear);
        Text(g, on ? T(L"la tienne (partagée)", L"yours (shared)") : T(L"aucune", L"none"), RectF(pn.X + 144, y, pn.Width - 168, 22), 12.5f, FontStyleBold, on ? kAcc : kGrey, StringAlignmentNear);
    }
    if (!a.builtin) {   // (la VAZ : livree avec le mod, ni mise a jour ni desinstallation ici)
        bool upd = AddonHasUpdate(a);
        float by = pn.Y + pn.Height - 40 - 52;
        UiButton(g, RectF(pn.X + 24, by, pn.Width - 48, 42), T(L"Désinstaller", L"Uninstall"), UI_ADDON_UNINST, false, IC_TRASH, !g_addonDlBusy, 10);
        if (upd) {   // (au-dessus : Mettre a jour)
            bool mine = g_addonDlBusy && g_addonDlKey == a.key;
            wchar_t b[96];
            if (mine) swprintf_s(b, T(L"Téléchargement… %d %%", L"Downloading… %d%%"), (int)(max(0.0f, g_addonDlProg) * 100));
            else swprintf_s(b, T(L"Mettre à jour · v%s", L"Update · v%s"), a.remoteVer.c_str());
            UiButton(g, RectF(pn.X + 24, by - 52, pn.Width - 48, 42), b, UI_ADDON_UPD, true, mine ? -1 : IC_DOWNLOAD, !g_addonDlBusy, 10);
        }
    }
    Text(g, a.dir.substr(g_modRoot.size()).c_str(), RectF(pn.X + 24, pn.Y + pn.Height - 40, pn.Width - 48, 20), 11, FontStyleRegular, WithA(kGrey, 0.8f), StringAlignmentNear);
}
static void DrawAddons(Graphics &g)
{
    AddonsScan();
    if (const Addon *a = AddonCur()) DrawAddonView(g, *a);
    else DrawAddonList(g);
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
    if (g_addonsTab && !CarMsc()) { DrawAddons(g); return; }   // (onglet Addons : voitures en extension)
    CarCheckFile();
    Glass(kCarCard);
    Glass(kCarPanel);
    RectF view = kCarView;
    if (CarMsc()) g_vinView = false;   // (pas de plaque VIN sur la SATSUMA)
    if (g_vinView) DrawVinOptions(g, kCarCard);
    else if (g_paintView) DrawPaintView(g, kCarCard);
    else if (g_car.state != 1) {
        const wchar_t *msg = CarMsc() ? (g_car.state == -2 ? T(L"Aper\u00E7u illisible (MWCoop\\cache\\satsuma.mesh) : lance une partie de My Summer Car pour le refaire.", L"Preview unreadable (MWCoop\\cache\\satsuma.mesh): start a My Summer Car game to rebuild it.")
                                                           : T(L"Aper\u00E7u de la SATSUMA apr\u00E8s ta premi\u00E8re partie de My Summer Car avec MWCoop.", L"SATSUMA preview after your first My Summer Car game with MWCoop."))
                     : g_car.state == -2 ? T(L"Aper\u00E7u illisible (MWCoop\\cache\\corris.mesh) : lance une partie pour le refaire.", L"Preview unreadable (MWCoop\\cache\\corris.mesh): start a game to rebuild it.")
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
        Title(g, CarMsc() ? T(L"Ta SATSUMA", L"Your SATSUMA") : T(L"Ta CORRIS", L"Your CORRIS"), RectF(kCarCard.X + 24, kCarCard.Y + kCarCard.Height - 100, 400, 32), 22, kInk);
        Text(g, CarMsc() ? T(L"Couleur pour une nouvelle partie ; l'h\u00F4te la donne \u00E0 ses invit\u00E9s.", L"Color for a new game; the host gives it to their guests.")
                         : T(L"Couleur et finition pour une nouvelle partie ; l'h\u00F4te les donne \u00E0 ses invit\u00E9s.", L"Color and trim for a new game; the host gives them to their guests."),
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
    Title(g, T(L"Couleur", L"Color"), RectF(pn.X + 24, pn.Y + 20, 200, 26), 16, kInk);   // (titres de carte a 16 comme la maquette)
    int sel = CarSwatchSel(), show = g_carHot >= 0 && g_carHot < kSwatchN ? g_carHot : sel;
    for (int i = 0; i < kSwatchN; i++) {
        RectF r = CarSwatchRect(i);
        if (i == sel) { Pen ring(kAcc, 2.6f); g.DrawEllipse(&ring, r.X - 5, r.Y - 5, r.Width + 10, r.Height + 10); }
        else if (i == g_carHot) { Pen ring(WithA(kInk, 0.5f), 1.6f); g.DrawEllipse(&ring, r.X - 4, r.Y - 4, r.Width + 8, r.Height + 8); }
        if (i == 0 && CarMsc()) { SolidBrush b(CarRgb(kMscOrigColor)); g.FillEllipse(&b, r); }   // (MSC : la couleur d'origine, pas de tirage)
        else if (i == 0) {   // au hasard : camembert et point d'interrogation
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
    if (show == 0) name = CarMsc() ? T(L"Couleur d'origine (comme le jeu)", L"Original color (like the game)") : T(L"Au hasard (comme le jeu)", L"Random (like the game)");
    else {
        int c = show > 0 ? kSwatches[show - 1].rgb : g_carColor;
        swprintf_s(hex, L"#%06X", c & 0xFFFFFF);
        name = show > 0 ? T(kSwatches[show - 1].fr, kSwatches[show - 1].en) : T(L"Couleur personnalis\u00E9e", L"Custom color");
    }
    float ny = CarSwatchRect(kSwatchN - 1).Y + 68;
    {
        RectF nr(pn.X + 24, ny, pn.Width - 48, 52);
        GraphicsPath np; RoundRect(np, nr, 14);
        SolidBrush nb(TH(card)); g.FillPath(&nb, &np);
        Pen npen(TH(choiceBorder), 1); g.DrawPath(&npen, &np);
        RectF d(nr.X + 14, nr.Y + 13, 26, 26);
        if (show == 0 && CarMsc()) { SolidBrush db(CarRgb(kMscOrigColor)); GraphicsPath dp; RoundRect(dp, d, 8); g.FillPath(&db, &dp); }
        else if (show == 0) DrawCarWheel(g, d); else { SolidBrush db(CarRgb(show > 0 ? kSwatches[show - 1].rgb : g_carColor)); GraphicsPath dp; RoundRect(dp, d, 8); g.FillPath(&db, &dp); }
        Text(g, name, RectF(nr.X + 50, nr.Y + 4, nr.Width - 60, 24), 14, FontStyleBold, kInk, StringAlignmentNear);
        if (hex[0]) Text(g, hex, RectF(nr.X + 50, nr.Y + 26, nr.Width - 60, 20), 12, FontStyleRegular, kGrey, StringAlignmentNear);
    }
    {   // Autre couleur... (selecteur de Windows)
        Button &b = g_btn[B_COLOR];
        b.r = RectF(pn.X + 24, ny + 62, pn.Width - 48, 46);
        GraphicsPath p; RoundRect(p, b.r, 14);
        SolidBrush fill(Mix(TH(btn2), TH(btn2Hot), b.hover)); g.FillPath(&fill, &p);
        Pen pen(sel < 0 ? kAcc : TH(fieldBorder), sel < 0 ? 2.0f : 1.0f); g.DrawPath(&pen, &p);
        RectF dot(b.r.X + 14, b.r.Y + 11, 24, 24);
        if (sel < 0) { SolidBrush db(CarRgb(g_carColor)); g.FillEllipse(&db, dot); } else DrawCarWheel(g, dot);
        Text(g, T(L"Autre couleur\u2026", L"Other color\u2026"), RectF(dot.X + 34, b.r.Y, b.r.Width - 60, b.r.Height), 13.5f, FontStyleBold, kInk, StringAlignmentNear);
    }
    if (CarMsc()) { g_trimRect = g_vinBtnRect = RectF(0, 0, 0, 0); }   // (MSC : ni finition ni plaque VIN sur la SATSUMA)
    else {   // Finition ([Coop] FinitionVoiture : L, LX, SLX, GT ; vide : au hasard) : imposee au tirage d'une nouvelle partie (Vin.cs)
        float ty = ny + 118;
        Title(g, T(L"Finition", L"Trim"), RectF(pn.X + 24, ty, 200, 24), 16, kInk);
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
    if (PersoPageMouseDown(x, y)) return true;   // (onglets de la page)
    if (g_addonsTab) return false;   // (Addons : cartes et boutons par HotZone)
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

