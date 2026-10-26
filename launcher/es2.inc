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

// ---- controles (sans tables de jeu) : nombres invalides, positions hors du monde
struct Es2Issue { size_t entry; int sev; std::string what; };   // sev : 0 ok, 1 attention, 2 reparable
static bool Es2Bad(float v) { return !_finite(v); }
// Floats d'une entree (valeur, vecteur, couleur, transform : position, rotation, echelle) ; pos : indice de la position (-1 : aucune).
static std::vector<float> Es2Floats(const Es2File &f, const Es2Entry &e, uint32_t kind, int *pos)
{
    std::vector<float> v;
    if (pos) *pos = -1;
    size_t d = e.data + 5;
    int n = kind == ES2_FLOAT ? 1 : kind == ES2_VECTOR3 ? 3 : kind == ES2_COLOR ? 4 : kind == ES2_TRANSFORM ? 10 : 0;
    if (kind == ES2_TRANSFORM) d += 1;   // (un octet avant la position)
    if (!n || d + 4 * n > e.data + e.len) return v;
    for (int k = 0; k < n; k++) v.push_back(Es2F32(f.bytes, d + 4 * k));
    if (pos && (kind == ES2_TRANSFORM || kind == ES2_VECTOR3)) *pos = 0;
    return v;
}
static std::vector<Es2Issue> Es2Check(const Es2File &f)
{
    std::vector<Es2Issue> out;
    for (size_t i = 0; i < f.e.size(); i++) {
        const Es2Entry &e = f.e[i];
        uint32_t kind = Es2Kind(f, e);
        int pos;
        std::vector<float> v = Es2Floats(f, e, kind, &pos);
        if (v.empty()) continue;
        bool bad = false, outside = false;
        for (size_t k = 0; k < v.size(); k++) { if (Es2Bad(v[k])) bad = true; else if (pos >= 0 && k < 3 && fabsf(v[k]) > 1e5f) outside = true; }
        if (bad) out.push_back({ i, 2, "nombre invalide (NaN ou infini)" });
        else if (outside) out.push_back({ i, 2, "position hors du monde (plus de 100 km)" });
    }
    return out;
}
