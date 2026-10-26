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
    float ymax;                                   // trop haut : hors du monde
    bool mwc;                                     // pieces de My Winter Car : <ID>AID / <ID>BLT / <ID>TGH, cablage <nom> (sinon la table)
    const char *carTag;                           // voiture des pieces montees (leur position NaN : celle de la voiture)
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
    kEs2SpotsMsc, ES2N(kEs2SpotsMsc), -15.5f, -13.5f, 5, 9, -0.27f, kEs2FuelMsc, ES2N(kEs2FuelMsc), kEs2PartsMsc, ES2N(kEs2PartsMsc), "WeatherPos", kEs2BadVerMsc,
    1e9f, false, "CarTransform" };
// My Winter Car ([MWC], run/mwc-sauvegarde/LISEZMOI.txt, releve et essaye en jeu sur Joueur1 le 26/10)
static const Es2Spot kEs2SpotsMwc[] = {
    { "SorbettPos", -1283.7f, 0.8f, 1100.2f }, { "CorrisPos", 1935.5f, 4.4f, -422.4f }, { "BachglotzTransform", 1552.9f, 4.8f, 723.7f },
    { "GifuTransform1", -1527.4f, 5.3f, 1324.6f }, { "TractorTransform", 55.4f, -1.0f, -86.2f }, { "FlatbedTransform", 64.2f, -1.3f, -80.0f },
    { "WoodCutter", 64.6f, -0.5f, -87.2f }, { "MopedTransform", -19.7f, -0.4f, 6.1f }, { "PlayerTransform", -1285.5f, 0.4f, 1076.4f },
};
static const Es2Fuel kEs2FuelMwc[] = { { "SorbettFuelLevel", 19.3f }, { "BachglotzFuelLevel", 37 }, { "GifuFuelLevel", 130 }, { "TractorFuelLevel", 42 },
    { "MopedFuelLevel", 2.3f }, { "FuelPriceGasoline", 4.33f }, { "FuelPriceDiesel", 3.37f }, { "FuelPriceFuelOil", 2.64f }, { "PlayerWeight", 83 } };
static const char *const kEs2BadVerMwc[] = { NULL };   // (aucune version connue pour ecrire une sauvegarde abimee)
static const Es2Rules kEs2Mwc = { { L"savefile.txt", L"carparts.txt", L"items2.txt", NULL }, -40, -3100, 3300, -2450, 3850,
    kEs2SpotsMwc, ES2N(kEs2SpotsMwc), -6.2f, -5.0f, 6.6f, 7.8f, 1.0f, kEs2FuelMwc, ES2N(kEs2FuelMwc), NULL, 0, NULL, kEs2BadVerMwc,
    500, true, "CorrisPos" };   // (objets : maison des parents, depart -6.2, 1.0, 6.6 ; jamais le milieu du garage : fosse)
static const Es2Rules *Es2RulesFor(int game) { return game == GAME_MSC ? &kEs2Msc : &kEs2Mwc; }

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
struct Es2Ctx { const Es2Rules *r; float money; bool hasMoney; int slot; float bank = 0; bool hasBank = false; };
static int32_t Es2I32(const Es2File &f, const Es2Entry &e) { return e.len >= 10 ? (int32_t)Es2U32(f.bytes, e.data + 5) : 0; }
// Valeur sure d'un float invalide (regles de [MSC]) ; tightTag : serrage = somme des crans de la liste des vis.
static float Es2SafeFloat(const Es2File &f, const std::map<std::string, size_t> &idx, const std::string &tag, const Es2Ctx &c)
{
    if (tag == "PlayerMoney") return c.hasMoney ? c.money : 0;
    if (tag == "PlayerMoneyBank") return c.hasBank ? c.bank : 0;
    if (c.r) for (int k = 0; k < c.r->nfuel; k++) if (tag == c.r->fuel[k].tag) return c.r->fuel[k].v;   // (avant Player* : PlayerWeight 83)
    if (!tag.compare(0, 6, "Player")) return 0;
    auto ends = [&](const char *s) { size_t n = strlen(s); return tag.size() > n && !tag.compare(tag.size() - n, n, s); };
    if (!tag.compare(0, 4, "Wear") || ends("TireWear") || (c.r && c.r->mwc && ends("WEA"))) return 50;
    for (auto &pr : { std::make_pair("Tightness", "Bolts"), std::make_pair("TGH", "BLT") }) {   // serrage : somme des crans des vis
        if (!ends(pr.first)) continue;
        auto it = idx.find(tag.substr(0, tag.size() - strlen(pr.first)) + pr.second);
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
    bool carMoved = false;   // (voiture des pieces remise a sa place : les pieces devissees vont au garage, pas a son ancienne place)
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
            if (p[0] == 0 && p[1] == 0 && p[2] == 0 && q[0] == 0 && q[1] == 0 && q[2] == 0 && q[3] == 1) continue;   // (objet jamais apparu : normal)
            bool badP = Es2Bad(p[0]) || Es2Bad(p[1]) || Es2Bad(p[2]);
            bool under = !badP && c.r && p[1] < c.r->ymin;
            bool outside = !badP && (c.r ? (p[0] < c.r->xmin || p[0] > c.r->xmax || p[2] < c.r->zmin || p[2] > c.r->zmax || p[1] > c.r->ymax) : (fabsf(p[0]) > 1e5f || fabsf(p[2]) > 1e5f));
            // piece montee de My Winter Car (<ID>AID > 0) : le jeu la remet sur son point de montage -- pas deplacee ; NaN : la voiture
            bool mounted = false;
            if (c.r && c.r->mwc && e.tag.size() > 3 && !e.tag.compare(e.tag.size() - 3, 3, "POS")) {
                auto ai = idx.find(e.tag.substr(0, e.tag.size() - 3) + "AID");
                mounted = ai != idx.end() && Es2Kind(f, f.e[ai->second]) == ES2_INT && Es2I32(f, f.e[ai->second]) > 0;
            } else if (c.r && !c.r->mwc) {   // (My Summer Car : <piece>Installed vrai -- essai de [MSC] : le jeu la remet sur son point de montage)
                auto ii = idx.find(e.tag + "Installed");
                mounted = ii != idx.end() && Es2Kind(f, f.e[ii->second]) == ES2_BOOL && b[f.e[ii->second].data + 5] != 0;
            }
            if (mounted) under = outside = false;
            bool badQ = Es2Bad(q[0]) || Es2Bad(q[1]) || Es2Bad(q[2]) || Es2Bad(q[3]) || (q[0] == 0 && q[1] == 0 && q[2] == 0 && q[3] == 0);
            bool badS = Es2Bad(s[0]) || Es2Bad(s[1]) || Es2Bad(s[2]) || (s[0] == 0 && s[1] == 0 && s[2] == 0);
            if (!badP && !under && !outside && !badQ && !badS) continue;
            std::wstring what;
            if (badP || under || outside) {
                what = badP ? T(L"position invalide (NaN)", L"invalid position (NaN)") : under ? T(L"sous la carte", L"under the map") : T(L"hors du monde", L"out of the world");
                if (c.r) {   // point sur : celui de l'objet, sinon le sol du garage (une case de 0,4 m par objet replace)
                    float x, y, z;
                    const Es2Spot *sp = NULL;
                    for (int k = 0; k < c.r->nspots; k++) if (e.tag == c.r->spots[k].tag) sp = &c.r->spots[k];
                    auto car = mounted && c.r->carTag ? idx.find(c.r->carTag) : idx.end();
                    float cp[3] = { 0, 0, 0 };
                    if (car != idx.end() && f.e[car->second].len >= 47) for (int k = 0; k < 3; k++) cp[k] = Es2F32(b, f.e[car->second].data + 6 + 4 * k);
                    if (car != idx.end() && !Es2Bad(cp[0]) && !Es2Bad(cp[1]) && !Es2Bad(cp[2])) { x = cp[0]; y = cp[1]; z = cp[2]; }   // (montee : la voiture)
                    else if (mounted && c.r->carTag) { for (int k = 0; k < c.r->nspots; k++) if (!strcmp(c.r->spots[k].tag, c.r->carTag)) sp = &c.r->spots[k]; x = sp ? sp->x : 0; y = sp ? sp->y : 0; z = sp ? sp->z : 0; }
                    else if (sp) { x = sp->x; y = sp->y; z = sp->z; }
                    else {
                        int cols = max(1, (int)((c.r->bx1 - c.r->bx0) / 0.4f) + 1), rows = max(1, (int)((c.r->bz1 - c.r->bz0) / 0.4f) + 1), k = c.slot++ % (cols * rows);
                        x = c.r->bx0 + 0.4f * (k % cols); z = c.r->bz0 + 0.4f * (k / cols); y = c.r->by;
                    }
                    Es2PutF32(d, 6, x); Es2PutF32(d, 10, y); Es2PutF32(d, 14, z);
                    if (c.r->carTag && e.tag == c.r->carTag) carMoved = true;
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
    // bi : la liste des vis (npos : aucune) ; une piece sans vis mais serree (courroie) compte aussi
    auto fixUnbolt = [&](size_t bi, const std::string &tightTag, const std::string &boltedTag, const std::wstring &name, const std::string &posTag) {
        std::vector<int> n; std::vector<size_t> at;
        bool bolts = bi != (size_t)-1 && Es2Bolts(f, f.e[bi], n, &at);
        int sum = 0; for (int v : n) sum += v;
        auto ti = idx.find(tightTag);
        bool tf = ti != idx.end() && Es2Kind(f, f.e[ti->second]) == ES2_FLOAT;
        float tv = tf ? Es2F32(b, f.e[ti->second].data + 5) : 0;
        if (sum <= 0 && !(tv > 0)) return;
        Es2Issue is{ bolts ? bi : ti->second, 2, name + T(L" : pas montée mais vissée, vis remises à zéro", L": not fitted but bolted, bolts reset to zero"), {} };
        if (bolts) { std::string bd = b.substr(f.e[bi].data, f.e[bi].len); for (size_t a : at) bd[a] = '0'; is.fix.push_back({ bi, bd }); }
        if (tf) { std::string td = b.substr(f.e[ti->second].data, f.e[ti->second].len); Es2PutF32(td, 5, 0); is.fix.push_back({ ti->second, td }); }
        auto bo = idx.find(boltedTag);
        if (bo != idx.end() && Es2Kind(f, f.e[bo->second]) == ES2_BOOL) { std::string od = b.substr(f.e[bo->second].data, f.e[bo->second].len); od[5] = 0; is.fix.push_back({ bo->second, od }); }
        auto pt = idx.find(posTag);
        if (carMoved && pt != idx.end() && Es2Kind(f, f.e[pt->second]) == ES2_TRANSFORM && f.e[pt->second].len >= 47) {   // (au sol du garage, une case de 0,4 m)
            int cols = max(1, (int)((c.r->bx1 - c.r->bx0) / 0.4f) + 1), rows = max(1, (int)((c.r->bz1 - c.r->bz0) / 0.4f) + 1), k = c.slot++ % (cols * rows);
            std::string pd = b.substr(f.e[pt->second].data, f.e[pt->second].len);
            for (auto &o : out) for (auto &x : o.fix) if (x.first == pt->second) pd = x.second;   // (deja corrigee : on part de la correction)
            Es2PutF32(pd, 6, c.r->bx0 + 0.4f * (k % cols)); Es2PutF32(pd, 10, c.r->by); Es2PutF32(pd, 14, c.r->bz0 + 0.4f * (k / cols));
            is.fix.push_back({ pt->second, pd });
            is.what += T(L", posée au garage (la voiture a été déplacée)", L", put in the garage (the car was moved)");
        }
        out.push_back(is);
    };
    int loose = 0;
    if (c.r->mwc) {   // My Winter Car : <ID>AID (0 : piece libre) / <ID>BLT / <ID>TGH ; cablage <nom> (bool) avec <nom>BLT / <nom>TGH
        for (size_t i = 0; i < f.e.size(); i++) {
            const std::string &tg = f.e[i].tag;
            uint32_t kd = Es2Kind(f, f.e[i]);
            std::string id;
            bool fitted;
            if (kd == ES2_INT && tg.size() > 3 && !tg.compare(tg.size() - 3, 3, "AID")) { id = tg.substr(0, tg.size() - 3); fitted = Es2I32(f, f.e[i]) > 0; }
            else if (kd == ES2_BOOL && idx.count(tg + "BLT")) { id = tg; fitted = b[f.e[i].data + 5] != 0; }
            else continue;
            auto bi = idx.find(id + "BLT");
            if (!fitted) fixUnbolt(bi == idx.end() ? (size_t)-1 : bi->second, id + "TGH", "", Es2W(id), id + "POS");
            else if (bi != idx.end()) { std::vector<int> n; if (Es2Bolts(f, f.e[bi->second], n)) for (int v : n) if (v < 8) { loose++; break; } }
        }
    }
    for (int k = 0; k < c.r->nparts; k++) {
        const Es2Part &p = c.r->parts[k];
        auto in = idx.find(std::string(p.piece) + "Installed"), bi = idx.find(p.bolts);
        if (in == idx.end() || bi == idx.end() || Es2Kind(f, f.e[in->second]) != ES2_BOOL) continue;
        bool installed = b[f.e[in->second].data + 5] != 0;
        if (!installed) fixUnbolt(bi->second, p.tight, std::string(p.piece) + "Bolted", Es2W(p.piece), p.piece);
        else { std::vector<int> n; if (Es2Bolts(f, f.e[bi->second], n)) for (int v : n) if (v < 8) { loose++; break; } }
    }
    for (size_t i = 0; i < f.e.size() && !c.r->mwc; i++) {   // roues (My Summer Car) : wheel_<type><n>Corner vide = pas montee
        const std::string &tg = f.e[i].tag;
        if (tg.compare(0, 6, "wheel_") || tg.size() < 12 || tg.compare(tg.size() - 6, 6, "Corner") || Es2Kind(f, f.e[i]) != ES2_STRING) continue;
        size_t p = f.e[i].data + 5, len;
        if (!Es2Read7(b, p, len) || len) continue;   // (monte : FL, FR, RL, RR)
        std::string base = tg.substr(0, tg.size() - 6);
        auto bi = idx.find(base + "Bolts");
        if (bi != idx.end()) fixUnbolt(bi->second, base + "Tightness", base + "Bolted", Es2W(base), base);
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

// ---- « Verifier la sauvegarde » (fiche d'une partie, onglet Sauvegardes) : jeu ferme pour reparer, copie d'abord
struct SaveCheckFile { std::wstring name; bool present = false, ok = false; std::string err; Es2File f; std::vector<Es2Issue> is; };
struct SaveCheckState { std::wstring prof, name, verWarn; std::vector<SaveCheckFile> files; int warn = 0, fix = 0, broken = 0; };
static SaveCheckState g_chk;
static const wchar_t *const kEs2FilesMwc[] = { L"savefile.txt", L"carparts.txt", L"items2.txt", NULL };
static std::wstring SavePartieKey(const std::wstring &dir, const wchar_t *key)
{
    std::vector<unsigned char> d;
    if (!ReadAll(dir + L"partie.txt", d)) return L"";
    std::wstring all = Widen(std::string(d.begin(), d.end()), CP_UTF8), k = std::wstring(key) + L"=";
    for (size_t a = 0; a < all.size();) {
        size_t b = all.find(L'\n', a);
        std::wstring l = Trim(all.substr(a, b == std::wstring::npos ? std::wstring::npos : b - a));
        a = b == std::wstring::npos ? all.size() : b + 1;
        if (!l.compare(0, k.size(), k)) return l.substr(k.size());
    }
    return L"";
}
static void SaveCheckRun(std::wstring prof, std::wstring name, std::wstring dir = L"")   // (par valeur : on l'appelle avec g_chk.prof)   // (dir : captures, un dossier d'essai)
{
    g_chk = SaveCheckState(); g_chk.prof = prof; g_chk.name = name;
    if (dir.empty()) dir = SaveDirOf(prof);
    const Es2Rules *r = Es2RulesFor(g_game);
    Es2Ctx c = { r, 0, false, 0 };
    std::wstring money = SavePartieKey(dir, L"argent");
    if (!money.empty() && _wtof(money.c_str()) >= 0) { c.money = (float)_wtof(money.c_str()); c.hasMoney = true; }   // (-1 : absent, My Summer Car)
    std::wstring bank = SavePartieKey(dir, L"banque");
    if (!bank.empty() && _wtof(bank.c_str()) >= 0) { c.bank = (float)_wtof(bank.c_str()); c.hasBank = true; }
    for (const wchar_t *const *fn = r ? r->files : kEs2FilesMwc; *fn; fn++) {
        SaveCheckFile sf; sf.name = *fn;
        std::vector<unsigned char> d;
        sf.present = ReadAll(dir + *fn, d);
        if (sf.present) {
            sf.ok = Es2Parse(std::string(d.begin(), d.end()), sf.f);
            if (!sf.ok) { sf.err = sf.f.err; g_chk.broken++; }
            else { sf.is = Es2Check(sf.f, c); for (auto &i : sf.is) (i.sev == 2 ? g_chk.fix : g_chk.warn)++; }
        }
        g_chk.files.push_back(sf);
    }
    std::wstring ver = SavePartieKey(dir, L"version");   // (ecrite par le mod a chaque sauvegarde, depuis mwc-/msc- 1.15.8+)
    if (r && !ver.empty()) for (const char *const *bv = r->badVer; *bv; bv++) {
        std::wstring b = Widen(*bv);
        size_t dash = b.find(L'-');
        bool hit = b[0] == L'<' ? CmpVer(ver, b.substr(1)) < 0 : dash != std::wstring::npos && CmpVer(ver, b.substr(0, dash)) >= 0 && CmpVer(ver, b.substr(dash + 1)) <= 0;
        if (!hit) continue;
        wchar_t w[400];
        swprintf_s(w, T(L"Dernière sauvegarde faite avec %s%s, une version qui pouvait abîmer les sauvegardes. Si la partie a un souci : Copies de secours, la copie « Avant » une version plus récente.",
                        L"Last save made with %s%s, a version that could damage saves. If the game has a problem: Backup copies, the \"Before\" copy of a newer version."), g_game == GAME_MSC ? L"msc-" : L"mwc-", ver.c_str());
        g_chk.verWarn = w; g_chk.warn++;
        break;
    }
    LaunchLog("sauvegardes : verification de %s : %d reparable(s), %d attention, %d fichier(s) illisible(s)%s", prof.empty() ? "principale" : Narrow(prof, CP_UTF8).c_str(), g_chk.fix, g_chk.warn, g_chk.broken, ver.empty() ? "" : (" (version " + Narrow(ver, CP_UTF8) + ")").c_str());
}
static void SaveCheckOpen(int i)
{
    if (i < 0 || i >= (int)g_saves.size()) return;
    if (!g_saves[i].has) { SetStatus(K_NORMAL, T(L"Rien à vérifier : partie vide", L"Nothing to check: empty game")); return; }
    SaveCheckRun(g_saves[i].prof, g_saves[i].name);
    g_saveModal = SM_CHECK;
}
static void SaveRepairApply();
static void SaveRepair()
{
    if (!g_chk.fix || !SaveGuard()) return;
    wchar_t q[600];
    swprintf_s(q, T(L"Réparer « %s » ?\n\n%d correctif(s) sûr(s) : nombres invalides, objets perdus remis au garage ou à leur place, pièces démontées mais vissées. Une copie de la partie est faite avant (« Avant la réparation »).",
                    L"Repair \"%s\"?\n\n%d safe fix(es): invalid numbers, lost objects put back in the garage or their spot, parts removed but bolted. A copy of the game is made first (\"Before the repair\")."), g_chk.name.c_str(), g_chk.fix);
    if (MessageBoxW(g_wnd, q, L"MWCoop", MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON2) != IDYES) return;
    SaveRepairApply();
}
// La reparation elle-meme (jeu ferme, accord deja donne) : copie « avant la reparation », puis chaque fichier reecrit par un
// fichier temporaire remplace d'un coup ; chaque correctif note dans lanceur.log ; puis relue.
static void SaveRepairApply()
{
    std::wstring dir = SaveDirOf(g_chk.prof);
    if (SaveAutoCopy(g_chk.prof, L"_avant-reparation", true, false).empty()) { SetStatus(K_ERR, T(L"Réparation annulée : copie de la partie impossible", L"Repair cancelled: could not copy the game")); return; }
    int done = 0, failed = 0;
    for (auto &sf : g_chk.files) {
        std::map<size_t, std::string> fx = Es2Fixes(sf.is);
        if (fx.empty()) continue;
        std::string out = Es2Write(sf.f, fx);
        std::wstring path = dir + sf.name, tmp = path + L".mwcoop-tmp";
        HANDLE h = CreateFileW(tmp.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL);
        DWORD wr = 0;
        bool ok = h != INVALID_HANDLE_VALUE && WriteFile(h, out.data(), (DWORD)out.size(), &wr, NULL) && wr == out.size();
        if (h != INVALID_HANDLE_VALUE) CloseHandle(h);
        ok = ok && MoveFileExW(tmp.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH);
        if (!ok) { DeleteFileW(tmp.c_str()); failed++; LaunchLog("sauvegardes : reparation de %s IMPOSSIBLE (erreur %lu)", Narrow(sf.name, CP_UTF8).c_str(), GetLastError()); continue; }
        for (auto &i : sf.is) if (i.sev == 2) { done++; LaunchLog("sauvegardes : repare %s : %s : %s", Narrow(sf.name, CP_UTF8).c_str(), i.entry < sf.f.e.size() ? sf.f.e[i.entry].tag.c_str() : "-", Narrow(i.what, CP_UTF8).c_str()); }
    }
    SaveCopiesPrune(g_chk.prof);
    g_saveBakFor = L"?";
    if (failed) SetStatus(K_ERR, T(L"Réparation incomplète : %d fichier(s) pas écrit(s) (voir lanceur.log)", L"Repair incomplete: %d file(s) not written (see lanceur.log)"), failed);
    else SetStatus(K_OK, T(L"« %s » réparée : %d correctif(s), copie d'avant gardée", L"\"%s\" repaired: %d fix(es), copy from before kept"), g_chk.name.c_str(), done);
    std::wstring prof = g_chk.prof, name = g_chk.name;   // (copies : SaveCheckRun remet g_chk a zero)
    SaveCheckRun(prof, name);   // (relue : ce qui reste)
}
// Contenu de la fenetre (cadre c, titre deja dessine ; y : sous le titre).
static void DrawSaveCheck(Graphics &g, RectF c, float y)
{
    float x = c.X + 24, w = c.Width - 48;
    wchar_t sum[200];
    if (!g_chk.fix && !g_chk.warn && !g_chk.broken) swprintf_s(sum, T(L"« %s » : rien à signaler.", L"\"%s\": nothing to report."), g_chk.name.c_str());
    else swprintf_s(sum, T(L"« %s » : %d réparable(s), %d à surveiller, %d fichier(s) illisible(s).", L"\"%s\": %d repairable, %d to watch, %d unreadable file(s)."), g_chk.name.c_str(), g_chk.fix, g_chk.warn, g_chk.broken);
    Para(g, sum, RectF(x, y, w, 40), 13.5f, kInk);   // (deux lignes : le russe est plus long)
    y += sum[0] && MeasureW(MeasureG(), sum, 13.5f, FontStyleRegular) > w ? 46 : 28;
    int rows = 0;
    const int maxRows = 13;
    auto row = [&](Color col, const std::wstring &t) {
        if (rows++ >= maxRows) return;
        SolidBrush dot(col); g.FillEllipse(&dot, x, y + 7, 8.0f, 8.0f);
        Text(g, FitText(t, 12, FontStyleRegular, w - 18), RectF(x + 16, y, w - 16, 22), 12, FontStyleRegular, kInk, StringAlignmentNear);
        y += 24;
    };
    if (!g_chk.verWarn.empty()) { Para(g, g_chk.verWarn, RectF(x, y, w, 56), 12, kWarn); y += 62; }   // (trois lignes)
    int more = 0;
    for (auto &sf : g_chk.files) {
        if (!sf.present) continue;
        if (!sf.ok) { row(kRed, sf.name + T(L" : illisible (", L": unreadable (") + Widen(sf.err, CP_UTF8) + T(L") — remets une copie de secours", L") — restore a backup copy")); continue; }
        if (sf.is.empty()) { row(kOk, sf.name + T(L" : OK", L": OK")); continue; }
        for (auto &i : sf.is) { if (rows >= maxRows) { more++; continue; } row(i.sev == 2 ? kAcc : kWarn, sf.name + L" · " + (i.entry < sf.f.e.size() ? Widen(sf.f.e[i.entry].tag, CP_UTF8) + L" : " : L"") + i.what); }
    }
    if (more) { wchar_t m[64]; swprintf_s(m, T(L"+ %d autre(s)", L"+ %d more"), more); Text(g, m, RectF(x, y, w, 20), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear); }
    float by = c.Y + c.Height - 24 - 44;
    if (g_chk.fix) { wchar_t b[64]; swprintf_s(b, T(L"Réparer (%d)", L"Repair (%d)"), g_chk.fix); UiButton(g, RectF(c.X + c.Width - 24 - 170, by, 170, 44), b, UI_SAVE_REPAIR, true, IC_CHECK); }
    UiButton(g, RectF(x, by, 200, 44), T(L"Copies de secours", L"Backup copies"), UI_SAVE_CHECKCOPIES, false, -1);
    if (!g_chk.fix) Text(g, T(L"Rien à réparer ici.", L"Nothing to repair here."), RectF(x + 210, by, w - 210, 44), 12, FontStyleRegular, kGrey, StringAlignmentFar);
}