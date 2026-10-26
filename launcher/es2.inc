// ---------------------------------------------------------------- EASY SAVE 2 (inclus par launcher.cpp, apres saves.inc)
// Lecture et reecriture des sauvegardes du jeu (lot B « Verifier la sauvegarde », JD 26/10 ; format releve avec
// [Project & Fesability] sur des sauvegardes d'essai, MWC et MSC) : une suite d'entrees
//   7E, etiquette (longueur 7 bits + UTF-8), int32 longueur, donnees
// et les donnees d'une valeur simple : FF, cle de type (4 octets), valeur, 7B. Tableaux : 53 (S) ou 52 (R), FF, cle(s) de type,
// un octet, int32 nombre, les elements, 7B. Une entree n'est reecrite que si on la change : le reste part octet pour octet.
enum : uint32_t {
    ES2_BOOL = 0xAD4D7C9C, ES2_FLOAT = 0x6E3ED76B, ES2_INT = 0xE2A80856, ES2_STRING = 0xFDE9F1EE,
    ES2_TRANSFORM = 0x097AFA76, ES2_VECTOR3 = 0xEC66DC46, ES2_COLOR = 0x32CF4B31,
};
struct Es2Entry { std::string tag; size_t start = 0, data = 0, len = 0; };   // start : le 7E ; data, len : les donnees
struct Es2File { std::string bytes; std::vector<Es2Entry> e; std::string err; };

static bool Es2Read7(const std::string &b, size_t &p, size_t &n)
{
    n = 0;
    for (int sh = 0; sh < 35; sh += 7) {
        if (p >= b.size()) return false;
        unsigned char c = (unsigned char)b[p++];
        n |= (size_t)(c & 0x7F) << sh;
        if (!(c & 0x80)) return true;
    }
    return false;
}
static uint32_t Es2U32(const std::string &b, size_t p) { return p + 4 <= b.size() ? (uint32_t)(unsigned char)b[p] | (uint32_t)(unsigned char)b[p + 1] << 8 | (uint32_t)(unsigned char)b[p + 2] << 16 | (uint32_t)(unsigned char)b[p + 3] << 24 : 0; }
static float Es2F32(const std::string &b, size_t p) { uint32_t u = Es2U32(b, p); float f; memcpy(&f, &u, 4); return f; }
// Decoupe le fichier en entrees ; faux (f.err : ou et pourquoi) s'il est illisible ou coupe.
static bool Es2Parse(const std::string &bytes, Es2File &f)
{
    f = Es2File(); f.bytes = bytes;
    const std::string &b = f.bytes;
    char why[160];
    for (size_t p = 0; p < b.size();) {
        Es2Entry e; e.start = p;
        if ((unsigned char)b[p] != 0x7E) { sprintf_s(why, "octet %zu : %02X au lieu du debut d'entree (7E)", p, (unsigned char)b[p]); f.err = why; return false; }
        size_t q = p + 1, n;
        if (!Es2Read7(b, q, n) || q + n + 4 > b.size()) { sprintf_s(why, "entree %zu (octet %zu) : etiquette coupee", f.e.size(), p); f.err = why; return false; }
        e.tag.assign(b, q, n); q += n;
        size_t len = Es2U32(b, q); q += 4;
        if (q + len > b.size()) { sprintf_s(why, "entree %zu « %.60s » : %zu octets annonces, %zu restent (fichier coupe)", f.e.size(), e.tag.c_str(), len, b.size() - q); f.err = why; return false; }
        e.data = q; e.len = len;
        f.e.push_back(e);
        p = q + len;
    }
    return true;
}
static uint32_t Es2Kind(const Es2File &f, const Es2Entry &e) { return e.len >= 6 && (unsigned char)f.bytes[e.data] == 0xFF ? Es2U32(f.bytes, e.data + 1) : 0; }
// Le fichier, avec les donnees remplacees des entrees donnees (indice -> nouvelles donnees) ; les autres octet pour octet.
static std::string Es2Write(const Es2File &f, const std::map<size_t, std::string> &repl)
{
    std::string out;
    out.reserve(f.bytes.size() + 64);
    size_t last = 0;
    for (const auto &r : repl) {
        if (r.first >= f.e.size()) continue;
        const Es2Entry &e = f.e[r.first];
        out.append(f.bytes, last, e.data - 4 - last);   // (jusqu'a la longueur des donnees)
        uint32_t n = (uint32_t)r.second.size();
        for (int k = 0; k < 4; k++) out += (char)((n >> (8 * k)) & 0xFF);
        out += r.second;
        last = e.data + e.len;
    }
    out.append(f.bytes, last, std::string::npos);
    return out;
}

// ---- regles par jeu (My Summer Car : [MSC], run/msc-sauvegarde/LISEZMOI.txt, releve et essaye en jeu le 26/10)
struct Es2Part { const char *piece, *bolts, *tight; };
#include "es2-pieces.inc"
struct Es2Spot { const char *tag; float x, y, z; };   // point sur d'un objet nomme (positions d'une partie neuve, sol verifie)
struct Es2Fuel { const char *tag; float v; };
struct Es2Rules {
    const wchar_t *files[4];                      // fichiers controles (meshsave.txt : jamais)
    float ymin, xmin, xmax, zmin, zmax;           // sous la carte (y < ymin), hors du monde
    const Es2Spot *spots; int nspots;
    float bx0, bx1, bz0, bz1, by;                 // le reste (pieces, objets) : au sol du garage, 0,4 m d'ecart
    const Es2Fuel *fuel; int nfuel;
    const Es2Part *parts; int nparts;
    const char *skip;                             // transform qui n'est pas un objet (nuage de meteo)
    const char *const *badVer;                    // versions du mod a signaler (« <1.15.0 », « 1.15.0-1.15.5 »)
};
static const Es2Spot kEs2SpotsMsc[] = {
    { "CarTransform", -16.23f, -0.28f, 11.37f }, { "VanTransform", 608.0f, -3.38f, -798.0f }, { "MuscleTransform", 1554.0f, 4.77f, 723.58f },
    { "GifuTransform1", -1527.37f, 5.29f, 1324.64f }, { "RusckoTransform", -169.93f, -3.0f, 1040.63f }, { "TractorTransform", 55.41f, -0.98f, -86.23f },
    { "FlatbedTransform", 64.24f, -1.31f, -80.0f }, { "BoatTransform", 9.59f, -4.52f, 149.79f }, { "PlayerTransform", -11.19f, -0.29f, 13.09f },
    { "JunkCar1Transform", 907.61f, 1.62f, -936.36f }, { "JunkCar2Transform", 807.36f, -1.0f, -1115.79f },
    { "JunkCar3Transform", -1416.79f, 4.93f, -524.32f }, { "JunkCar4Transform", -1472.89f, 9.07f, -610.92f },
};
static const Es2Fuel kEs2FuelMsc[] = { { "VanFuelLevel", 24 }, { "MuscleFuelLevel", 45 }, { "GifuFuelLevel", 130 }, { "TractorFuelLevel", 42 },
    { "BoatFuelLevel", 3 }, { "MopedFuelLevel", 2.3f }, { "RusckoFuelLevel", 2.3f }, { "fuel tank(Clone)FuelLevel", 0 } };
static const char *const kEs2BadVerMsc[] = { "<1.15.0", "1.15.0-1.15.5", NULL };
#define ES2N(a) (int)(sizeof(a) / sizeof((a)[0]))
static const Es2Rules kEs2Msc = { { L"defaultES2File.txt", L"items.txt", NULL }, -40, -3100, 3300, -2450, 3850,
    kEs2SpotsMsc, ES2N(kEs2SpotsMsc), -15.5f, -13.5f, 5, 9, -0.27f, kEs2FuelMsc, ES2N(kEs2FuelMsc), kEs2PartsMsc, ES2N(kEs2PartsMsc), "WeatherPos", kEs2BadVerMsc };
// My Winter Car : regles de [MWC] a venir ; sans elles, les nombres invalides et le fichier illisible seulement (signales).
static const Es2Rules *Es2RulesFor(int game) { return game == GAME_MSC ? &kEs2Msc : NULL; }

// ---- controles et correctifs
// Un probleme : sev 1 attention (signale), 2 reparable (fix : les donnees de remplacement, meme longueur d'octets).
struct Es2Issue { size_t entry; int sev; std::wstring what; std::vector<std::pair<size_t, std::string>> fix; };
static bool Es2Bad(float v) { return !_finite(v); }
static void Es2PutF32(std::string &d, size_t at, float v) { uint32_t u; memcpy(&u, &v, 4); for (int k = 0; k < 4; k++) d[at + k] = (char)((u >> (8 * k)) & 0xFF); }
static std::wstring Es2W(const std::string &s) { return Widen(s, CP_UTF8); }
// Liste « S » de textes « int(n) » (vis) : les n ; faux si ce n'en est pas une.
static bool Es2Bolts(const Es2File &f, const Es2Entry &e, std::vector<int> &n, std::vector<size_t> *at = NULL)
{
    n.clear();
    const std::string &b = f.bytes;
    if (e.len < 12 || (unsigned char)b[e.data] != 0x53 || (unsigned char)b[e.data + 1] != 0xFF || Es2U32(b, e.data + 2) != ES2_STRING) return false;
    uint32_t cnt = Es2U32(b, e.data + 7);
    size_t p = e.data + 11, end = e.data + e.len;
    for (uint32_t k = 0; k < cnt; k++) {
        size_t len;
        if (!Es2Read7(b, p, len) || p + len > end) return false;
        std::string s = b.substr(p, len);
        if (s.size() != 6 || s.compare(0, 4, "int(") || s[5] != ')' || s[4] < '0' || s[4] > '9') return false;
        if (at) at->push_back(p + 4 - e.data);   // (le chiffre, relatif aux donnees)
        n.push_back(s[4] - '0');
        p += len;
    }
    return true;
}
struct Es2Ctx { const Es2Rules *r; float money; bool hasMoney; int slot; };
// Valeur sure d'un float invalide (regles de [MSC]) ; tightTag : serrage = somme des crans de la liste des vis.
static float Es2SafeFloat(const Es2File &f, const std::map<std::string, size_t> &idx, const std::string &tag, const Es2Ctx &c)
{
    if (tag == "PlayerMoney") return c.hasMoney ? c.money : 0;
    if (!tag.compare(0, 6, "Player")) return 0;
    if (c.r) for (int k = 0; k < c.r->nfuel; k++) if (tag == c.r->fuel[k].tag) return c.r->fuel[k].v;
    if (!tag.compare(0, 4, "Wear") || (tag.size() > 8 && !tag.compare(tag.size() - 8, 8, "TireWear"))) return 50;
    if (tag.size() > 9 && !tag.compare(tag.size() - 9, 9, "Tightness")) {
        auto it = idx.find(tag.substr(0, tag.size() - 9) + "Bolts");
        std::vector<int> n;
        if (it != idx.end() && Es2Bolts(f, f.e[it->second], n)) { int s = 0; for (int v : n) s += v; return (float)s; }
    }
    return 0;
}
static std::vector<Es2Issue> Es2Check(const Es2File &f, Es2Ctx &c)
{
    std::vector<Es2Issue> out;
    std::map<std::string, size_t> idx;
    for (size_t i = 0; i < f.e.size(); i++) idx[f.e[i].tag] = i;
    const std::string &b = f.bytes;
    for (size_t i = 0; i < f.e.size(); i++) {
        const Es2Entry &e = f.e[i];
        uint32_t kind = Es2Kind(f, e);
        std::string d = b.substr(e.data, e.len);
        if (kind == ES2_FLOAT && e.len >= 10 && Es2Bad(Es2F32(b, e.data + 5))) {
            float v = Es2SafeFloat(f, idx, e.tag, c);
            Es2PutF32(d, 5, v);
            wchar_t w[120]; swprintf_s(w, T(L"nombre invalide, remis à %g", L"invalid number, reset to %g"), v);
            out.push_back({ i, c.r ? 2 : 1, w, { { i, d } } });
        } else if ((kind == ES2_VECTOR3 || kind == ES2_COLOR) && e.len >= (kind == ES2_COLOR ? 22u : 18u)) {
            int n = kind == ES2_COLOR ? 4 : 3;
            bool bad = false;
            for (int k = 0; k < n; k++) if (Es2Bad(Es2F32(b, e.data + 5 + 4 * k))) { bad = true; Es2PutF32(d, 5 + 4 * k, kind == ES2_COLOR && k == 3 ? 1.0f : 0.0f); }
            if (bad) out.push_back({ i, c.r ? 2 : 1, T(L"nombre invalide, remis à zéro", L"invalid number, reset to zero"), { { i, d } } });
        } else if (kind == ES2_TRANSFORM && e.len >= 47) {
            if (c.r && c.r->skip && e.tag == c.r->skip) continue;
            float p[3], q[4], s[3];
            for (int k = 0; k < 3; k++) p[k] = Es2F32(b, e.data + 6 + 4 * k);
            for (int k = 0; k < 4; k++) q[k] = Es2F32(b, e.data + 18 + 4 * k);
            for (int k = 0; k < 3; k++) s[k] = Es2F32(b, e.data + 34 + 4 * k);
            bool badP = Es2Bad(p[0]) || Es2Bad(p[1]) || Es2Bad(p[2]);
            bool under = !badP && c.r && p[1] < c.r->ymin;
            bool outside = !badP && (c.r ? (p[0] < c.r->xmin || p[0] > c.r->xmax || p[2] < c.r->zmin || p[2] > c.r->zmax) : (fabsf(p[0]) > 1e5f || fabsf(p[2]) > 1e5f));
            bool badQ = Es2Bad(q[0]) || Es2Bad(q[1]) || Es2Bad(q[2]) || Es2Bad(q[3]) || (q[0] == 0 && q[1] == 0 && q[2] == 0 && q[3] == 0);
            bool badS = Es2Bad(s[0]) || Es2Bad(s[1]) || Es2Bad(s[2]);
            if (!badP && !under && !outside && !badQ && !badS) continue;
            std::wstring what;
            if (badP || under || outside) {
                what = badP ? T(L"position invalide (NaN)", L"invalid position (NaN)") : under ? T(L"sous la carte", L"under the map") : T(L"hors du monde", L"out of the world");
                if (c.r) {   // point sur : celui de l'objet, sinon le sol du garage (une case de 0,4 m par objet replace)
                    float x, y, z;
                    const Es2Spot *sp = NULL;
                    for (int k = 0; k < c.r->nspots; k++) if (e.tag == c.r->spots[k].tag) sp = &c.r->spots[k];
                    if (sp) { x = sp->x; y = sp->y; z = sp->z; }
                    else {
                        int cols = max(1, (int)((c.r->bx1 - c.r->bx0) / 0.4f) + 1), rows = max(1, (int)((c.r->bz1 - c.r->bz0) / 0.4f) + 1), k = c.slot++ % (cols * rows);
                        x = c.r->bx0 + 0.4f * (k % cols); z = c.r->bz0 + 0.4f * (k / cols); y = c.r->by;
                    }
                    Es2PutF32(d, 6, x); Es2PutF32(d, 10, y); Es2PutF32(d, 14, z);
                    wchar_t w[80]; swprintf_s(w, T(L" : remis en %.1f, %.1f, %.1f", L": moved to %.1f, %.1f, %.1f"), x, y, z); what += w;
                }
            }
            if (badQ) { Es2PutF32(d, 18, 0); Es2PutF32(d, 22, 0); Es2PutF32(d, 26, 0); Es2PutF32(d, 30, 1); if (what.empty()) what = T(L"rotation invalide, remise droite", L"invalid rotation, reset upright"); }
            if (badS) { for (int k = 0; k < 3; k++) Es2PutF32(d, 34 + 4 * k, 1); if (what.empty()) what = T(L"échelle invalide, remise à 1", L"invalid scale, reset to 1"); }
            out.push_back({ i, c.r ? 2 : 1, what, { { i, d } } });
        }
    }
    if (!c.r) return out;
    // pieces « pas montees mais vissees » : vis a zero, serrage 0, Bolted faux (l'inverse, montee mais desserree : normal en montage)
    auto fixUnbolt = [&](size_t bi, const std::string &tightTag, const std::string &boltedTag, const std::wstring &name) {
        std::vector<int> n; std::vector<size_t> at;
        if (!Es2Bolts(f, f.e[bi], n, &at)) return;
        int sum = 0; for (int v : n) sum += v;
        if (!sum) return;
        Es2Issue is{ bi, 2, name + T(L" : pas montée mais vissée, vis remises à zéro", L": not fitted but bolted, bolts reset to zero"), {} };
        std::string bd = b.substr(f.e[bi].data, f.e[bi].len);
        for (size_t a : at) bd[a] = '0';
        is.fix.push_back({ bi, bd });
        auto ti = idx.find(tightTag);
        if (ti != idx.end() && Es2Kind(f, f.e[ti->second]) == ES2_FLOAT) { std::string td = b.substr(f.e[ti->second].data, f.e[ti->second].len); Es2PutF32(td, 5, 0); is.fix.push_back({ ti->second, td }); }
        auto bo = idx.find(boltedTag);
        if (bo != idx.end() && Es2Kind(f, f.e[bo->second]) == ES2_BOOL) { std::string od = b.substr(f.e[bo->second].data, f.e[bo->second].len); od[5] = 0; is.fix.push_back({ bo->second, od }); }
        out.push_back(is);
    };
    int loose = 0;
    for (int k = 0; k < c.r->nparts; k++) {
        const Es2Part &p = c.r->parts[k];
        auto in = idx.find(std::string(p.piece) + "Installed"), bi = idx.find(p.bolts);
        if (in == idx.end() || bi == idx.end() || Es2Kind(f, f.e[in->second]) != ES2_BOOL) continue;
        bool installed = b[f.e[in->second].data + 5] != 0;
        if (!installed) fixUnbolt(bi->second, p.tight, std::string(p.piece) + "Bolted", Es2W(p.piece));
        else { std::vector<int> n; if (Es2Bolts(f, f.e[bi->second], n)) for (int v : n) if (v < 8) { loose++; break; } }
    }
    for (size_t i = 0; i < f.e.size(); i++) {   // roues : wheel_<type><n>Corner vide = pas montee
        const std::string &tg = f.e[i].tag;
        if (tg.compare(0, 6, "wheel_") || tg.size() < 12 || tg.compare(tg.size() - 6, 6, "Corner") || Es2Kind(f, f.e[i]) != ES2_STRING) continue;
        size_t p = f.e[i].data + 5, len;
        if (!Es2Read7(b, p, len) || len) continue;   // (monte : FL, FR, RL, RR)
        std::string base = tg.substr(0, tg.size() - 6);
        auto bi = idx.find(base + "Bolts");
        if (bi != idx.end()) fixUnbolt(bi->second, base + "Tightness", base + "Bolted", Es2W(base));
    }
    if (loose) {
        wchar_t w[160]; swprintf_s(w, T(L"%d pièce(s) montée(s) pas entièrement vissée(s) (normal pendant un montage)", L"%d fitted part(s) not fully bolted (normal while assembling)"), loose);
        out.push_back({ (size_t)-1, 1, w, {} });
    }
    return out;
}
// Les correctifs des problemes reparables, par entree (le dernier gagne : pieces apres les nombres).
static std::map<size_t, std::string> Es2Fixes(const std::vector<Es2Issue> &is)
{
    std::map<size_t, std::string> m;
    for (auto &i : is) if (i.sev == 2) for (auto &x : i.fix) m[x.first] = x.second;
    return m;
}
