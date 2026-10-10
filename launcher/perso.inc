// ---------------------------------------------------------------- apparence complete (volet TENUE) - inclus par launcher.cpp
// Demande de JD (07/10, et d'un joueur sur Nexus) : corpulence, pantalon, visage, chapeau, lunettes, cheveux, en plus du
// haut. Le mod releve tout chez les PNJ du jeu et l'exporte (Sync\Looks.cs) : <jeu>\MWCoop\cache\perso.mesh (corps poses
// debout, accessoires, listes des matieres) et cache\perso\<matiere>.png (textures, 256 px au plus) + couleurs.txt.
// Ici : apercu 3D texture (meme rendu logiciel que la CORRIS : perspective, profondeur, Gouraud, rendu 2x reduit), et
// une ligne par partie avec ses fleches, rangees comme le corps (tete en haut, jambes en bas).
// Reglages : [Coop] du mwcoop.ini (Apparence = le haut, deja la ; Pantalon, Visage, Corps, Chapeau, Lunettes,
// Cheveux ; vide = celui du modele / aucun). Le mod les assemble en "haut|pantalon|visage|corps|chapeau|lunettes|cheveux".
struct PersoMesh { std::vector<float> v; std::vector<std::vector<uint32_t>> subs; };   // v : 8 par sommet (pos, normale, uv)
struct PersoBody { std::string key, def[3]; float head[16]; PersoMesh m; };
struct PersoAcc { std::string key; int kind; std::vector<std::string> mats; float mat[16]; PersoMesh m; };
struct PersoTex { bool tried = false; int w = 0, h = 0; std::vector<uint32_t> px; float col[3] = { 1, 1, 1 }; };
static struct {
    int state = 0;                     // 0 pas lu, 1 pret, -1 absent, -2 illisible
    std::wstring path;
    FILETIME mt = {};
    DWORD checkT = 0;
    std::vector<std::string> lists[3]; // hauts, pantalons, visages
    std::vector<PersoBody> bodies;
    std::vector<PersoAcc> accs;
    std::map<std::string, PersoTex> tex;
    std::map<std::string, std::array<float, 3>> colors;
    std::string ver;                   // version du mod qui a fait l'export
} g_perso;
static std::vector<uint32_t> g_persoBig, g_persoPix;
static std::vector<float> g_persoZ;
static std::string g_persoDrawn;       // ce qui est dans g_persoPix (apparence + angle + taille)
static Bitmap *g_persoBmp;
static int g_skinRowHot = -1;          // ligne survolee * 2 + (0 fleche gauche, 1 fleche droite)

enum { PF_HAT, PF_GLASSES, PF_HAIR, PF_FACE, PF_SHIRT, PF_PANTS, PF_BODY, PF_COUNT };
static const char *kPersoKey[PF_COUNT] = { "Chapeau", "Lunettes", "Cheveux", "Visage", "Apparence", "Pantalon", "Corps" };

// Images importees (demande de JD, 10/10) : %LOCALAPPDATA%\MWCoop\visage.jpg, haut.jpg, pantalon.jpg ; choisies dans la
// liste de leur partie ("perso" : « Visage perso », « Haut perso », « Pantalon perso »), montrees en direct par l'apercu
// (matiere "#perso-<n>"), et par le mod aux autres joueurs (Sync\CustomFace.cs).
enum { PI_FACE, PI_SHIRT, PI_PANTS };
static const wchar_t *kPersoImg[3] = { L"visage", L"haut", L"pantalon" };
static std::wstring PersoImgFile(int k) { return LocalDir() + kPersoImg[k] + L".jpg"; }
static bool PersoImgHas(int k) { return FileExists(PersoImgFile(k)); }
static int PersoImgOf(int f) { return f == PF_FACE ? PI_FACE : f == PF_SHIRT ? PI_SHIRT : f == PF_PANTS ? PI_PANTS : -1; }
static int PersoPartOf(int k) { return k == PI_FACE ? PF_FACE : k == PI_SHIRT ? PF_SHIRT : PF_PANTS; }

static bool PersoParse(const std::vector<unsigned char> &d)
{
    size_t p = 0;
    auto get = [&](void *out, size_t n) { if (n > d.size() - p) return false; memcpy(out, d.data() + p, n); p += n; return true; };
    auto str = [&](std::string &s) { uint16_t n = 0; if (!get(&n, 2) || n > d.size() - p) return false; s.assign((const char *)d.data() + p, n); p += n; return true; };
    auto mesh = [&](PersoMesh &m) {
        uint32_t nv = 0, ns = 0;
        if (!get(&nv, 4) || nv > (d.size() - p) / 32) return false;
        m.v.resize((size_t)nv * 8);
        if (nv && !get(m.v.data(), (size_t)nv * 32)) return false;
        if (!get(&ns, 4) || ns > 16) return false;
        m.subs.resize(ns);
        for (auto &s : m.subs) {
            uint32_t ni = 0;
            if (!get(&ni, 4) || ni > (d.size() - p) / 4 || ni % 3) return false;
            s.resize(ni);
            if (ni && !get(s.data(), (size_t)ni * 4)) return false;
            for (uint32_t i : s) if (i >= nv) return false;
        }
        for (float f : m.v) if (!(fabsf(f) < 1000)) return false;
        return true;
    };
    char magic[4];
    uint32_t ver = 0;
    std::string modVer;
    if (!get(magic, 4) || memcmp(magic, "MWCP", 4) || !get(&ver, 4) || ver != 1 || !str(modVer)) return false;
    g_perso.ver = modVer;
    for (auto &l : g_perso.lists) {
        uint32_t n = 0;
        if (!get(&n, 4) || n > 4096) return false;
        l.resize(n);
        for (auto &s : l) if (!str(s)) return false;
    }
    uint32_t nb = 0, na = 0;
    if (!get(&nb, 4) || nb > 64) return false;
    g_perso.bodies.resize(nb);
    for (auto &b : g_perso.bodies)
        if (!str(b.key) || !str(b.def[0]) || !str(b.def[1]) || !str(b.def[2]) || !get(b.head, 64) || !mesh(b.m)) return false;
    if (!get(&na, 4) || na > 1024) return false;
    g_perso.accs.resize(na);
    for (auto &a : g_perso.accs) {
        uint8_t k = 0;
        uint32_t nm = 0;
        if (!str(a.key) || !get(&k, 1) || !get(&nm, 4) || nm > 16) return false;
        a.kind = k;
        a.mats.resize(nm);
        for (auto &s : a.mats) if (!str(s)) return false;
        if (!get(a.mat, 64) || !mesh(a.m)) return false;
    }
    return !g_perso.bodies.empty();
}

static std::wstring PersoDir() { return g_gameDir.empty() ? L"" : g_gameDir + L"MWCoop\\cache\\"; }
// Export fait par une autre version du mod que celle installee (avant 0.41 : les corps des PNJ gardaient leur pose,
// la grand-mere restait assise) : il sera refait a la prochaine partie (~30 s en jeu).
static bool PersoStale() { return g_perso.state == 1 && !g_localVer.empty() && g_perso.ver.substr(0, g_perso.ver.find('+')) != Narrow(g_localVer, CP_UTF8); }   // ("+tenuesN" : tenues offertes de l'export)

// Relit perso.mesh s'il a change ; au plus toutes les 2 s.
static void PersoCheck()
{
    DWORD now = GetTickCount();
    std::wstring path = PersoDir() + L"perso.mesh";
    if (g_perso.state != 0 && path == g_perso.path && now - g_perso.checkT < 2000) return;
    g_perso.checkT = now;
    WIN32_FILE_ATTRIBUTE_DATA a;
    bool exists = !g_gameDir.empty() && GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &a) && !(a.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY);
    if (exists && g_perso.state == 1 && path == g_perso.path && CompareFileTime(&a.ftLastWriteTime, &g_perso.mt) == 0) return;
    if (!exists && g_perso.state == -1 && path == g_perso.path) return;
    g_perso.bodies.clear(); g_perso.accs.clear(); g_perso.tex.clear(); g_perso.colors.clear();
    for (auto &l : g_perso.lists) l.clear();
    g_perso.path = path;
    g_persoDrawn.clear();
    if (!exists) { g_perso.state = -1; return; }
    g_perso.mt = a.ftLastWriteTime;
    std::vector<unsigned char> d;
    g_perso.state = ReadAll(path, d) && PersoParse(d) ? 1 : -2;
    if (g_perso.state != 1) { g_perso.bodies.clear(); g_perso.accs.clear(); return; }
    std::vector<unsigned char> c;   // couleurs des matieres
    if (ReadAll(PersoDir() + L"perso\\couleurs.txt", c)) {
        std::string t(c.begin(), c.end());
        size_t at = 0;
        while (at < t.size()) {
            size_t e = t.find('\n', at), tab = t.find('\t', at);
            std::string l = t.substr(at, e == std::string::npos ? std::string::npos : e - at);
            at = e == std::string::npos ? t.size() : e + 1;
            size_t tb = l.find('\t');
            if (tb == std::string::npos) continue;
            std::array<float, 3> col = { 1, 1, 1 };
            sscanf_s(l.c_str() + tb + 1, "%f,%f,%f", &col[0], &col[1], &col[2]);
            g_perso.colors[l.substr(0, tb)] = col;
            (void)tab;
        }
    }
}

static PersoTex &PersoTexFor(const std::string &mat)
{
    PersoTex &t = g_perso.tex[mat];
    if (t.tried) return t;
    t.tried = true;
    auto c = g_perso.colors.find(mat);
    if (c != g_perso.colors.end()) for (int k = 0; k < 3; k++) t.col[k] = min(max(c->second[k], 0.0f), 1.5f);
    std::vector<unsigned char> d;
    if (!mat.compare(0, 7, "#perso-") && mat.size() == 8) {   // image importee (la sienne)
        int k = mat[7] - '0';
        if (k < 0 || k > 2 || !ReadAll(PersoImgFile(k), d)) return t;
        for (int c2 = 0; c2 < 3; c2++) t.col[c2] = 1;
    } else {
    if (mat.empty() || mat.find_first_of("\\/:") != std::string::npos) return t;
    // (tenue offerte : son image du pack, pas d'export du mod)
    if (!ReadAll(PersoDir() + L"perso\\" + Widen(mat, CP_UTF8) + L".png", d) && !(IsTenue(mat) && ReadAll(TenueFile(mat), d))) return t;
    }
    Bitmap *b = LoadPngMem(d.data(), d.size(), 2048, 2048);
    if (!b) return t;
    BitmapData bd;
    Rect r(0, 0, (INT)b->GetWidth(), (INT)b->GetHeight());
    if (b->LockBits(&r, ImageLockModeRead, PixelFormat32bppARGB, &bd) == Ok) {
        t.w = r.Width; t.h = r.Height;
        t.px.resize((size_t)t.w * t.h);
        for (int y = 0; y < t.h; y++) memcpy(&t.px[(size_t)y * t.w], (BYTE *)bd.Scan0 + (size_t)y * bd.Stride, (size_t)t.w * 4);
        b->UnlockBits(&bd);
    }
    delete b;
    return t;
}

// ---- reglages
static std::string PersoGet(int f)
{
    if (f == PF_SHIRT) return MyShirt();
    char v[160] = "";
    GetPrivateProfileStringA("Coop", kPersoKey[f], "", v, sizeof(v), ModIniA().c_str());
    return v;
}
static void PersoSet(int f, const std::string &v)
{
    if (f == PF_SHIRT) { const Opt *ap = OptByKey("Apparence"); if (ap) for (size_t i = 0; i < ap->svals.size(); i++) if (ap->svals[i] == v) OptSet(*ap, (int)i); g_skinAnnounce = true; return; }
    EnsureModDir();
    WritePrivateProfileStringA("Coop", kPersoKey[f], v.c_str(), ModIniA().c_str());
}
static std::vector<std::string> g_drPerso;   // volet ouvert : valeurs a rendre (ANNULER)
static void PersoSnapshot(bool restore)
{
    if (!restore) { g_drPerso.clear(); for (int f = 0; f < PF_COUNT; f++) g_drPerso.push_back(f == PF_SHIRT ? "" : PersoGet(f)); return; }
    for (int f = 0; f < PF_COUNT && f < (int)g_drPerso.size(); f++) if (f != PF_SHIRT && PersoGet(f) != g_drPerso[f]) PersoSet(f, g_drPerso[f]);
}

// Valeurs possibles d'une partie ("" : celle du modele, ou aucun accessoire), dans l'ordre des fleches.
static std::vector<std::string> PersoChoices(int f)
{
    std::vector<std::string> v;
    if (f == PF_SHIRT) { if (const Opt *ap = OptByKey("Apparence")) for (auto &s : ap->svals) if (s != "perso" || PersoImgHas(PI_SHIRT)) v.push_back(s); return v; }
    v.push_back("");
    if (f == PF_PANTS || f == PF_FACE) {
        if (PersoImgHas(PersoImgOf(f))) v.push_back("perso");
        for (auto &s : g_perso.lists[f == PF_PANTS ? 1 : 2]) v.push_back(s);
        for (auto &s : g_tenueNames[f == PF_PANTS ? 1 : 2]) if (std::find(v.begin(), v.end(), s) == v.end()) v.push_back(s);   // (tenues offertes)
        return v;
    }
    if (f == PF_BODY) {
        std::vector<std::string> ks;
        for (auto &b : g_perso.bodies) if (b.key != "1712-37") ks.push_back(b.key);   // (le modele de base : "")
        std::sort(ks.begin(), ks.end(), [](const std::string &a, const std::string &b) {
            auto w = [](const std::string &k) { size_t d = k.rfind('-'); return (k.compare(0, 4, "pig-") ? 0 : 1000) + atoi(k.c_str() + (d == std::string::npos ? 0 : d + 1)); };
            return w(a) < w(b);
        });
        for (auto &k : ks) v.push_back(k);
        return v;
    }
    int kind = f == PF_HAT ? 0 : f == PF_GLASSES ? 1 : 2;
    std::vector<std::string> ks;
    for (auto &a : g_perso.accs) if (a.kind == kind) ks.push_back(a.key);
    std::sort(ks.begin(), ks.end());
    for (auto &k : ks) v.push_back(k);
    return v;
}

static std::wstring PersoLabel(int f, const std::string &v)
{
    if (f == PF_SHIRT) return SkinLabel(v);
    if (v.empty()) return f == PF_BODY ? T(L"Normal", L"Regular") : f == PF_PANTS || f == PF_FACE ? T(L"D'origine", L"Default") : f == PF_GLASSES ? T(L"Aucunes", L"None") : T(L"Aucun", L"None");
    if (f == PF_BODY) {
        static const struct { const char *k; const wchar_t *fr, *en; } kb[] = {
            { "1714-32", L"Mince", L"Thin" }, { "1709-34", L"Svelte", L"Slim" }, { "1712-48", L"Costaud", L"Stout" },
            { "1697-42", L"Petit et rond", L"Short and round" }, { "1789-29", L"Petit et fin", L"Short and slim" } };
        for (auto &b : kb) if (v == b.k) return g_fr ? b.fr : b.en;
        if (!v.compare(0, 4, "pig-")) return T(L"Cochon", L"Pig");
        return Widen(v);
    }
    if (f == PF_PANTS || f == PF_FACE) {
        if (v == "perso") return f == PF_PANTS ? T(L"Pantalon perso", L"Custom pants") : T(L"Visage perso", L"Custom face");
        static const struct { const char *k; const wchar_t *fr, *en; } kp[] = {
            { "cop_pants", L"Police", L"Police" }, { "inspector_pants", L"Inspecteur", L"Inspector" }, { "psk_pants", L"PSK", L"PSK" }, { "rally_pants", L"Rallye", L"Rally" } };
        for (auto &p : kp) if (v == p.k) return g_fr ? p.fr : p.en;
        if (IsTenue(v)) return std::wstring(f == PF_PANTS ? T(L"Pantalon ", L"Pants ") : T(L"Visage ", L"Face ")) + TenueLabel(v);
        const char *pre = f == PF_PANTS ? "char_pants" : "char_face";
        if (!v.compare(0, strlen(pre), pre)) {
            int n = atoi(v.c_str() + strlen(pre));
            wchar_t b[64];
            swprintf_s(b, L"%s %d%s", f == PF_PANTS ? T(L"Pantalon", L"Pants") : T(L"Visage", L"Face"), n, v.find("woman") != std::string::npos ? T(L" (femme)", L" (woman)") : L"");
            return b;
        }
        return Widen(v);
    }
    size_t at = v.find('@');
    std::string mesh = v.substr(0, at), mat = at == std::string::npos ? "" : v.substr(at + 1);
    static const struct { const char *k; const wchar_t *fr, *en; } ka[] = {
        { "hat_beanie1", L"Bonnet", L"Beanie" }, { "hat_cap", L"Casquette", L"Cap" }, { "hat_cap2", L"Casquette à filet", L"Trucker cap" },
        { "cop_hat", L"Casquette de police", L"Police cap" }, { "hat_karvalakki", L"Chapka", L"Fur hat" }, { "fish_hat", L"Bob de pêche", L"Fishing hat" },
        { "busdriver_hat", L"Casquette de chauffeur", L"Bus driver cap" }, { "teimo_hat", L"Chapeau de Teimo", L"Teimo's hat" }, { "gifu_hat", L"Casquette Gifu", L"Gifu cap" },
        { "latsa", L"Béret", L"Flat cap" }, { "npc_helmet", L"Casque", L"Helmet" }, { "eye_glasses", L"Lunettes", L"Glasses" }, { "eye_glasses2", L"Lunettes rondes", L"Round glasses" },
        { "eye_glasses_dsl", L"Lunettes de soleil", L"Sunglasses" }, { "bodymesh_ponytail", L"Queue de cheval", L"Ponytail" }, { "hyppyritukka", L"Coupe mulet", L"Mullet" } };
    std::wstring name = Widen(mesh);
    for (auto &a : ka) if (mesh == a.k) name = g_fr ? a.fr : a.en;
    // meme objet en plusieurs matieres : la matiere en plus (couleur, verre)
    int same = 0, rank = 0;
    for (auto &a : g_perso.accs) if (!a.key.compare(0, mesh.size() + 1, mesh + "@")) { same++; if (a.key <= v) rank++; }
    if (same > 1) {
        static const struct { const char *k; const wchar_t *fr, *en; } km[] = {
            { "glass", L"claires", L"clear" }, { "windows_out_black", L"noires", L"black" }, { "CHROME", L"chromées", L"chrome" }, { "paint_black_shiny", L"noir brillant", L"glossy black" },
            { "dashboard_glass", L"fumées", L"smoked" }, { "window_glass", L"verre", L"glass" }, { "eye_glasses_dsl", L"d'origine", L"original" } };
        std::wstring m;
        for (auto &k : km) if (mat == k.k) m = g_fr ? k.fr : k.en;
        if (m.empty()) { wchar_t b[16]; swprintf_s(b, L"%d", rank); m = b; }
        name += L" (" + m + L")";
    }
    return name;
}

// ---- rendu
struct PSV { float x, y, iz, u, v, l; };
static void PersoTri(const PSV &v0, const PSV &v1, const PSV &v2, int W, int H, uint32_t *pix, float *zb, const PersoTex &tx, float alpha, float tint)
{
    float area = (v1.x - v0.x) * (v2.y - v0.y) - (v1.y - v0.y) * (v2.x - v0.x);
    if (fabsf(area) < 1e-4f) return;
    int x0 = max(0, (int)floorf(min(v0.x, min(v1.x, v2.x)))), x1 = min(W - 1, (int)ceilf(max(v0.x, max(v1.x, v2.x))));
    int y0 = max(0, (int)floorf(min(v0.y, min(v1.y, v2.y)))), y1 = min(H - 1, (int)ceilf(max(v0.y, max(v1.y, v2.y))));
    if (x0 > x1 || y0 > y1) return;
    float inv = 1.0f / area;
    float a0 = -(v2.y - v1.y) * inv, b0 = (v2.x - v1.x) * inv;
    float a1 = -(v0.y - v2.y) * inv, b1 = (v0.x - v2.x) * inv;
    float a2 = -(a0 + a1), b2 = -(b0 + b1);
    float px = x0 + 0.5f, py = y0 + 0.5f;
    float r0 = ((v2.x - v1.x) * (py - v1.y) - (v2.y - v1.y) * (px - v1.x)) * inv;
    float r1 = ((v0.x - v2.x) * (py - v2.y) - (v0.y - v2.y) * (px - v2.x)) * inv;
    float r2 = 1.0f - r0 - r1;
    struct Pl { float at, dx, dy; };
    auto plane = [&](float q0, float q1, float q2) { return Pl{ q0 * r0 + q1 * r1 + q2 * r2, q0 * a0 + q1 * a1 + q2 * a2, q0 * b0 + q1 * b1 + q2 * b2 }; };
    // perspective correcte : u/z, v/z et 1/z lineaires a l'ecran
    Pl pz = plane(v0.iz, v1.iz, v2.iz), pu = plane(v0.u * v0.iz, v1.u * v1.iz, v2.u * v2.iz), pv = plane(v0.v * v0.iz, v1.v * v1.iz, v2.v * v2.iz), pl = plane(v0.l, v1.l, v2.l);
    const float eps = -1e-5f;
    bool tex = tx.w > 0;
    for (int y = y0; y <= y1; y++) {
        int dy = y - y0;
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
        float iz = pz.at + pz.dy * dy + pz.dx * ks, uu = pu.at + pu.dy * dy + pu.dx * ks, vv = pv.at + pv.dy * dy + pv.dx * ks, ll = pl.at + pl.dy * dy + pl.dx * ks;
        uint32_t *prow = pix + (size_t)y * W;
        float *zrow = zb + (size_t)y * W;
        for (int x = x0 + ks; x <= x0 + ke; x++, iz += pz.dx, uu += pu.dx, vv += pv.dx, ll += pl.dx) {
            if (iz <= zrow[x]) continue;
            float cr = tx.col[0], cg = tx.col[1], cb = tx.col[2], ca = 1;
            if (tex) {
                float u = uu / iz, v = vv / iz;
                u -= floorf(u); v -= floorf(v);
                int tx_ = min(tx.w - 1, (int)(u * tx.w)), ty = min(tx.h - 1, (int)((1 - v) * tx.h));
                uint32_t c = tx.px[(size_t)ty * tx.w + tx_];
                ca = (c >> 24) / 255.0f;
                if (alpha < 0 && ca < 0.4f) continue;   // decoupe (cheveux, bords)
                cr *= ((c >> 16) & 255) / 255.0f; cg *= ((c >> 8) & 255) / 255.0f; cb *= (c & 255) / 255.0f;
            }
            float l = max(ll, 0.0f) * tint;
            float R = min(255.0f, cr * l * 255), G = min(255.0f, cg * l * 255), B = min(255.0f, cb * l * 255);
            if (alpha < 0) {
                zrow[x] = iz;
                prow[x] = 0xFF000000u | ((uint32_t)R << 16) | ((uint32_t)G << 8) | (uint32_t)B;
            } else {
                uint32_t d = prow[x];
                float k = 1.0f - alpha;
                uint32_t A = (uint32_t)min(255.0f, alpha * 255 + (d >> 24) * k);
                prow[x] = (A << 24) | ((uint32_t)min(255.0f, R * alpha + ((d >> 16) & 255) * k) << 16) | ((uint32_t)min(255.0f, G * alpha + ((d >> 8) & 255) * k) << 8)
                        | (uint32_t)min(255.0f, B * alpha + (d & 255) * k);
            }
        }
    }
}

static const PersoBody *PersoBodyFor(const std::string &key)
{
    for (auto &b : g_perso.bodies) if (b.key == key) return &b;
    for (auto &b : g_perso.bodies) if (b.key == "1712-37") return &b;
    return g_perso.bodies.empty() ? NULL : &g_perso.bodies[0];
}

// Apparence complete "haut|pantalon|visage|corps|chapeau|lunettes|cheveux" (comme le mod) <-> parties PF_*.
static void PersoParseLook(const std::string &s, std::string look[PF_COUNT])
{
    static const int order[] = { PF_SHIRT, PF_PANTS, PF_FACE, PF_BODY, PF_HAT, PF_GLASSES, PF_HAIR };
    for (int f = 0; f < PF_COUNT; f++) look[f].clear();
    size_t a = 0;
    for (int k = 0; k < 7 && a <= s.size(); k++) {
        size_t e = s.find('|', a);
        look[order[k]] = s.substr(a, e == std::string::npos ? std::string::npos : e - a);
        if (e == std::string::npos) break;
        a = e + 1;
    }
}
static std::string PersoLookString()
{
    std::string s = PersoGet(PF_SHIRT);
    for (int f : { PF_PANTS, PF_FACE, PF_BODY, PF_HAT, PF_GLASSES, PF_HAIR }) s += "|" + PersoGet(f);
    while (!s.empty() && s.back() == '|') s.pop_back();
    return s;
}

// Rendu W x H (pixels de l'ecran) de l'apparence 'look', tournee de 'yaw' (radians, 0 : de face) ; portrait : tete et
// epaules, sinon le personnage entier.
static void PersoRender(int W, int H, float yaw, const std::string look[PF_COUNT], bool portrait = false, bool own = false)
{
    const int S = 2, BW = W * S, BH = H * S;
    g_persoBig.assign((size_t)BW * BH, 0);
    g_persoZ.assign((size_t)BW * BH, 0.0f);
    g_persoPix.assign((size_t)W * H, 0);
    const PersoBody *body = PersoBodyFor(look[PF_BODY]);
    if (!body) return;
    // camera : personnage debout de face (il regarde +z ; la camera est devant lui, regard vers -z), un peu en
    // plongee ; 2,05 m de haut dans l'image (portrait : 0,62 m autour de la tete, plus pres)
    const float dist = portrait ? 2.6f : 5.0f, cy0 = portrait ? body->head[13] - 0.02f : 0.98f, pitch = portrait ? 0.04f : 0.08f;
    float f = BH * 0.95f * dist / (portrait ? 0.62f : 2.05f);
    float cyaw = cosf(yaw + 3.14159265f), syaw = sinf(yaw + 3.14159265f), cp = cosf(pitch), sp = sinf(pitch);
    auto xf = [&](float x, float y, float z, float &X, float &Y, float &Z) {
        float x1 = x * cyaw + z * syaw, z1 = -x * syaw + z * cyaw;
        y -= cy0;
        X = x1; Y = y * cp + z1 * sp; Z = -y * sp + z1 * cp + dist;
    };
    float L1[3] = { -0.45f, 0.75f, -0.5f }, L2[3] = { 0.7f, 0.25f, -0.35f };
    for (float *L : { L1, L2 }) { float n = sqrtf(L[0] * L[0] + L[1] * L[1] + L[2] * L[2]); for (int k = 0; k < 3; k++) L[k] /= n; }
    uint32_t *pix = g_persoBig.data();
    float *zb = g_persoZ.data();
    // un maillage : sommets (repere de l'avatar, apres m) ; matieres par sous-maillage ; passe 0 opaque, 1 verre
    auto draw = [&](const PersoMesh &mesh, const float *m, const std::vector<std::string> &mats, int pass) {
        size_t nv = mesh.v.size() / 8;
        static std::vector<float> sv;
        sv.resize(nv * 6);
        for (size_t i = 0; i < nv; i++) {
            const float *q = &mesh.v[i * 8];
            float p[3] = { q[0], q[1], q[2] }, n[3] = { q[3], q[4], q[5] };
            if (m) {   // matrice colonne par colonne (comme Unity)
                float r[3], rn[3];
                for (int k = 0; k < 3; k++) {
                    r[k] = m[0 * 4 + k] * p[0] + m[1 * 4 + k] * p[1] + m[2 * 4 + k] * p[2] + m[3 * 4 + k];
                    rn[k] = m[0 * 4 + k] * n[0] + m[1 * 4 + k] * n[1] + m[2 * 4 + k] * n[2];
                }
                float ln = sqrtf(rn[0] * rn[0] + rn[1] * rn[1] + rn[2] * rn[2]);
                for (int k = 0; k < 3; k++) { p[k] = r[k]; n[k] = ln > 1e-9f ? rn[k] / ln : 0; }
            }
            float X, Y, Z;
            xf(p[0], p[1], p[2], X, Y, Z);
            float nx = n[0] * cyaw + n[2] * syaw, nz1 = -n[0] * syaw + n[2] * cyaw;   // normale : rotation seule
            float ny = n[1] * cp + nz1 * sp, nz = -n[1] * sp + nz1 * cp;
            float iz = 1.0f / max(Z, 0.05f);
            sv[i * 6] = BW * 0.5f + f * X * iz;
            sv[i * 6 + 1] = BH * 0.5f - f * Y * iz;
            sv[i * 6 + 2] = iz;
            sv[i * 6 + 3] = q[6];
            sv[i * 6 + 4] = q[7];
            float d1 = nx * L1[0] + ny * L1[1] + nz * L1[2], d2 = nx * L2[0] + ny * L2[1] + nz * L2[2];
            sv[i * 6 + 5] = 0.42f + 0.08f * ny + 0.62f * fabsf(d1) * (d1 > 0 ? 1.0f : 0.35f) + 0.22f * max(d2, 0.0f);
        }
        for (size_t s = 0; s < mesh.subs.size(); s++) {
            std::string mat = s < mats.size() ? mats[s] : mats.empty() ? "" : mats.back();
            bool glass = mat.find("glass") != std::string::npos && mat != "eye_glasses_dsl";
            if (glass != (pass == 1)) continue;
            const PersoTex &tx = PersoTexFor(mat);
            const std::vector<uint32_t> &idx = mesh.subs[s];
            for (size_t t = 0; t + 2 < idx.size(); t += 3) {
                PSV v[3];
                for (int k = 0; k < 3; k++) {
                    const float *a = &sv[idx[t + k] * 6];
                    v[k] = { a[0], a[1], a[2], a[3], a[4], a[5] };
                }
                PersoTri(v[0], v[1], v[2], BW, BH, pix, zb, tx, glass ? 0.45f : -1.0f, glass ? 0.35f : 1.0f);
            }
        }
    };
    // "perso" : l'image importee (la sienne ; celle d'un autre joueur n'est pas ici : celle d'origine)
    auto part = [&](int f2, const std::string &def) { const std::string &v = look[f2]; return v.empty() || (v == "perso" && !(own && PersoImgHas(PersoImgOf(f2)))) ? def : v == "perso" ? std::string("#perso-") + (char)('0' + PersoImgOf(f2)) : v; };
    std::vector<std::string> bm = { part(PF_SHIRT, body->def[0]), part(PF_PANTS, body->def[1]), part(PF_FACE, body->def[2]) };
    std::vector<const PersoAcc *> acc;
    for (int f2 : { PF_HAT, PF_GLASSES, PF_HAIR })
        for (auto &a : g_perso.accs) if (!look[f2].empty() && a.key == look[f2]) acc.push_back(&a);
    for (int pass = 0; pass < 2; pass++) {
        draw(body->m, NULL, bm, pass);
        for (const PersoAcc *a : acc) {
            float m[16];   // tete (repere de l'avatar) x accessoire (repere de la tete)
            for (int c = 0; c < 4; c++) for (int r = 0; r < 4; r++) {
                float s = 0;
                for (int k = 0; k < 4; k++) s += body->head[k * 4 + r] * a->mat[c * 4 + k];
                m[c * 4 + r] = s;
            }
            draw(a->m, m, a->mats, pass);
        }
    }
    for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++) {
            const uint32_t *q = pix + (size_t)(y * 2) * BW + x * 2;
            uint32_t c4[4] = { q[0], q[1], q[BW], q[BW + 1] }, out = 0;
            for (int sh = 0; sh < 32; sh += 8) {
                uint32_t sum = 0;
                for (uint32_t c : c4) sum += (c >> sh) & 255;
                out |= ((sum + 2) / 4) << sh;
            }
            g_persoPix[(size_t)y * W + x] = out;
        }
}

// Image du personnage pour la vue 'dst' (coordonnees logiques), refaite si l'apparence, l'angle ou la taille change.
static Bitmap *PersoImage(RectF dst, float yaw)
{
    if (g_perso.state != 1) return NULL;
    int W = max(8, (int)(dst.Width * g_scale + 0.5f)), H = max(8, (int)(dst.Height * g_scale + 0.5f));
    std::string key;
    for (int f = 0; f < PF_COUNT; f++) key += PersoGet(f) + "|";
    char b[64];
    sprintf_s(b, "%d|%d|%d", (int)(yaw * 100), W, H);
    key += b;
    if (key == g_persoDrawn && g_persoBmp) return g_persoBmp;
    std::string look[PF_COUNT];
    for (int f = 0; f < PF_COUNT; f++) look[f] = PersoGet(f);
    PersoRender(W, H, yaw, look, false, true);
    delete g_persoBmp;
    g_persoBmp = new Bitmap(W, H, PixelFormat32bppPARGB);
    BitmapData bd;
    Rect r(0, 0, W, H);
    if (g_persoBmp->LockBits(&r, ImageLockModeWrite, PixelFormat32bppPARGB, &bd) == Ok) {
        for (int y = 0; y < H; y++) memcpy((BYTE *)bd.Scan0 + (size_t)y * bd.Stride, &g_persoPix[(size_t)y * W], (size_t)W * 4);
        g_persoBmp->UnlockBits(&bd);
    }
    g_persoDrawn = key;
    return g_persoBmp;
}

// Pastille d'un joueur (salon, etat du serveur) : portrait 3D de SON apparence complete, dans un cercle ; NULL sans
// export (ou pour une apparence "haut seul" : les portraits d'images suffisent). Gardees par apparence et taille.
static std::map<std::string, Bitmap *> g_persoPortraits;
static Bitmap *PersoPortrait(const std::string &skin, int W, int H)
{
    if (g_perso.state != 1 || skin.find('|') == std::string::npos || W < 8 || H < 8) return NULL;
    char b[32];
    sprintf_s(b, "#%dx%d", W, H);
    std::string key = skin + b;
    auto it = g_persoPortraits.find(key);
    if (it != g_persoPortraits.end()) return it->second;
    if (g_persoPortraits.size() > 40) { for (auto &kv : g_persoPortraits) delete kv.second; g_persoPortraits.clear(); }
    std::string look[PF_COUNT];
    PersoParseLook(skin, look);
    PersoRender(W, H, 0.3f, look, true, skin == PersoLookString());
    Bitmap tmp(W, H, PixelFormat32bppPARGB), *out = new Bitmap(W, H, PixelFormat32bppPARGB);
    BitmapData bd;
    Rect r(0, 0, W, H);
    if (tmp.LockBits(&r, ImageLockModeWrite, PixelFormat32bppPARGB, &bd) == Ok) {
        for (int y = 0; y < H; y++) memcpy((BYTE *)bd.Scan0 + (size_t)y * bd.Stride, &g_persoPix[(size_t)y * W], (size_t)W * 4);
        tmp.UnlockBits(&bd);
    }
    {
        Graphics g(out);
        g.Clear(Color(0, 0, 0, 0));
        g.SetSmoothingMode(SmoothingModeAntiAlias);
        GraphicsPath mask;
        mask.AddEllipse(RectF(0, 0, (float)W, (float)H));
        TextureBrush tb(&tmp, WrapModeClamp);
        g.FillPath(&tb, &mask);
    }
    g_persoDrawn.clear();   // (g_persoPix reutilise : l'apercu du volet sera refait)
    g_persoPortraits[key] = out;
    return out;
}

// ---- lignes de la page TENUE (a droite de l'apercu) : tete en haut, jambes en bas
static const int kPersoRows[] = { PF_HAT, PF_GLASSES, PF_HAIR, PF_FACE, PF_SHIRT, PF_PANTS, PF_BODY };
static const int kPersoRowN = 7;
static const int kPersoZone[] = { 0, 0, 0, 0, 1, 2, 3 };   // zone de chaque ligne (tete, torse, jambes, silhouette)
static RectF PersoRowRect(int i)
{
    static const float ys[] = { 134, 190, 246, 302, 388, 474, 560 };
    return RectF(744, ys[i], 496, 50);
}
static RectF PersoArrowRect(const RectF &r, int s) { return RectF(r.X + r.Width - (s ? 48 : 96), r.Y + 5, 40, 40); }
static int PersoRowAt(float x, float y)
{
    if (g_perso.state != 1) return -1;
    for (int i = 0; i < kPersoRowN; i++) {
        RectF r = PersoRowRect(i);
        if (!r.Contains(x, y)) continue;
        return i * 2 + (PersoArrowRect(r, 0).Contains(x, y) ? 0 : 1);
    }
    return -1;
}
static void PersoStep(int row, int dir)
{
    int f = kPersoRows[row];
    std::vector<std::string> v = PersoChoices(f);
    if (v.empty()) return;
    std::string cur = PersoGet(f);
    int i = 0;
    for (int k = 0; k < (int)v.size(); k++) if (v[k] == cur) i = k;
    i = (i + dir + (int)v.size()) % (int)v.size();
    PersoSet(f, v[i]);
    g_skinChangeT = GetTickCount();
    g_lookZone = kPersoZone[row];
}
static void DrawPersoRows(Graphics &g)
{
    static const wchar_t *grpFr[] = { L"T\u00CATE", L"TORSE", L"JAMBES", L"SILHOUETTE" }, *grpEn[] = { L"HEAD", L"TORSO", L"LEGS", L"BUILD" };
    static const float gy[] = { 104, 358, 444, 530 }, gh[] = { 254, 82, 82, 82 };
    for (int k = 0; k < 4; k++) {
        RectF zr(732, gy[k], 520, gh[k] + (k == 0 ? 0 : 0));
        if (g_lookZone == k) {   // zone mise en avant (repere de l'apercu, ou derniere changee)
            GraphicsPath zp; RoundRect(zp, zr, 18);
            SolidBrush zb(WithA(kAcc, 0.10f)); g.FillPath(&zb, &zp);
            Pen zpen(WithA(kAcc, 0.45f), 1); g.DrawPath(&zpen, &zp);
        }
        Text(g, g_fr ? grpFr[k] : grpEn[k], RectF(748, gy[k] + 6, 300, 22), 11, FontStyleBold, g_lookZone == k ? kAcc : kGrey, StringAlignmentNear);
    }
    static const wchar_t *labFr[] = { L"Chapeau", L"Lunettes", L"Cheveux", L"Visage", L"Haut", L"Pantalon", L"Corpulence" }, *labEn[] = { L"Hat", L"Glasses", L"Hair", L"Face", L"Top", L"Pants", L"Build" };
    for (int i = 0; i < kPersoRowN; i++) {
        int f = kPersoRows[i];
        RectF r = PersoRowRect(i);
        bool hot = g_skinRowHot / 2 == i && g_skinRowHot >= 0;
        GraphicsPath rp; RoundRect(rp, r, 14);
        SolidBrush rb(hot ? TH(cardSel) : TH(card)); g.FillPath(&rb, &rp);
        Pen rpen(TH(choiceBorder), 1.0f); g.DrawPath(&rpen, &rp);
        for (int s = 0; s < 2; s++) {
            RectF a = PersoArrowRect(r, s);
            bool ah = g_skinRowHot == i * 2 + s;
            GraphicsPath ap; RoundRect(ap, a, 11);
            SolidBrush ab(ah ? WithA(kAcc, 0.25f) : TH(btn2)); g.FillPath(&ab, &ap);
            Icon(g, s ? IC_CHEVR : IC_CHEVL, a.X + 20, a.Y + 20, 16, ah ? kAcc : kInk, 2.3f);
        }
        std::vector<std::string> ch = PersoChoices(f);
        std::string cur = PersoGet(f);
        int at = 0;
        for (int k2 = 0; k2 < (int)ch.size(); k2++) if (ch[k2] == cur) at = k2;
        wchar_t cnt[32]; swprintf_s(cnt, L"%d / %d", at + 1, (int)ch.size());
        Text(g, cnt, RectF(r.X + r.Width - 196, r.Y, 92, r.Height), 12, FontStyleBold, kGrey, StringAlignmentFar);
        Text(g, g_fr ? labFr[i] : labEn[i], RectF(r.X + 16, r.Y + 5, 200, 18), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
        std::wstring v = PersoLabel(f, cur);
        Text(g, v, RectF(r.X + 16, r.Y + 22, r.Width - 220, 24), 15, FontStyleBold, kInk, StringAlignmentNear);
    }
}
