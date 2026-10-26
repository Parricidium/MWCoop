// ---------------------------------------------------------------- OUTIL DES PLACES RESERVE (inclus par launcher.cpp, apres
// es2.inc) : ID MWCoop de ce PC, cle d'acces signee par JD (cle.inc), liste des cles retirees. Spec : D:\1 - AI\ClaudeAI\
// MWCoop-spec-cle-places.md (validee par JD le 26/10, cles sans limite de duree). Bouton « Placement des sieges » : page
// Outils ([Addons et Tools]), seulement si AccesPlaces().
#include "cle.inc"

// Cles publiques de JD (X || Y en hexadecimal, 128 caracteres), en liste : une nouvelle paire n'invalide pas les cles faites
// avec l'ancienne. Ecrites par outils-prives/cles (creer-paire) ; vide tant que JD n'a pas cree sa paire.
static const char *const kJdPubKeys[] = { NULL };
static std::vector<std::string> g_clePubs;   // (decodees ; essais : /cle accepte une cle publique en plus)
static std::vector<std::string> &ClePubs()
{
    static bool done = false;
    if (!done) { done = true; for (const char *const *p = kJdPubKeys; *p; p++) { std::string b; if (cle::UnHex(*p, b) && b.size() == 64) g_clePubs.push_back(b); } }
    return g_clePubs;
}

// ---- ID MWCoop : 128 bits aleatoires, gardes chiffres par DPAPI (compte Windows) dans %LOCALAPPDATA%\MWCoop\id.dat ; montre
// « MWC-XXXXX-XXXXX-XXXXX-XXXXX-XXXXX-X » (base32, 26 caracteres). Copie sur un autre PC : illisible, donc un autre ID.
static std::wstring MwcoopIdText(const std::string &raw)
{
    static const char *a = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    std::string b;
    uint32_t v = 0; int bits = 0;
    for (unsigned char c : raw) { v = v << 8 | c; bits += 8; while (bits >= 5) { bits -= 5; b += a[(v >> bits) & 31]; } }
    if (bits) b += a[(v << (5 - bits)) & 31];
    std::wstring o = L"MWC";
    for (size_t i = 0; i < b.size(); i++) { if (i % 5 == 0) o += L'-'; o += (wchar_t)b[i]; }
    return o;
}
static std::wstring MwcoopId()
{
    static std::wstring id;
    if (!id.empty()) return id;
    std::wstring dir = LocalDir();
    if (dir.empty()) return L"";
    std::wstring f = dir + L"id.dat";
    std::vector<unsigned char> d;
    std::string raw;
    if (ReadAll(f, d) && !d.empty() && cle::Dpapi(false, std::string(d.begin(), d.end()), raw) && raw.size() == 16) return id = MwcoopIdText(raw);
    raw.assign(16, '\0');
    std::string enc;
    if (!cle::Random(&raw[0], 16) || !cle::Dpapi(true, raw, enc)) { LaunchLog("cle : ID MWCoop impossible a creer"); return L""; }
    SHCreateDirectoryExW(NULL, dir.c_str(), NULL);
    HANDLE h = CreateFileW(f.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_HIDDEN, NULL);
    if (h != INVALID_HANDLE_VALUE) { DWORD w; WriteFile(h, enc.data(), (DWORD)enc.size(), &w, NULL); CloseHandle(h); }
    id = MwcoopIdText(raw);
    LaunchLog("cle : ID MWCoop cree");
    return id;
}

// ---- cle d'acces : « MWCP1.<donnees>.<signature> », donnees « v=1;serie=;nom=;ids=;emis=;[exp=;]droits=;k=; »
enum { CLE_AUCUNE, CLE_VALIDE, CLE_FORMAT, CLE_SIGNATURE, CLE_AUTRE_ID, CLE_RETIREE, CLE_EXPIREE };
struct CleInfo { int st = CLE_AUCUNE; std::wstring nom; std::string serie, droits, exp, k; };
static std::set<std::string> g_cleRetirees;   // series retirees (dist/cles-retirees.txt, dernier recu ; cache cles-retirees.txt)
static bool CleDroit(const CleInfo &c, const char *d)
{
    std::string l = "," + c.droits + ",";
    return l.find(std::string(",") + d + ",") != std::string::npos;
}
static CleInfo CleCheck(const std::string &line, const std::vector<std::string> &pubs, const std::set<std::string> &retired, const std::wstring &myId, uint64_t steamId)
{
    CleInfo c;
    if (line.find_first_not_of(" \r\n\t") == std::string::npos) return c;
    std::string data;
    if (line.find("MWCP1.") == std::string::npos) { c.st = CLE_FORMAT; return c; }
    if (!cle::Signed(line.substr(line.find("MWCP1.")), "MWCP1", pubs, data)) { c.st = CLE_SIGNATURE; return c; }
    auto f = cle::Fields(data);
    c.nom = Widen(f["nom"], CP_UTF8); c.serie = f["serie"]; c.droits = f["droits"]; c.exp = f["exp"];
    std::string k;
    if (f["v"] != "1" || c.serie.size() != 8 || !cle::UnB64u(f["k"], k) || k.size() != 32) { c.st = CLE_FORMAT; return c; }
    c.k = k;
    if (retired.count(c.serie)) { c.st = CLE_RETIREE; return c; }
    if (!c.exp.empty()) {
        SYSTEMTIME t; GetLocalTime(&t);
        char today[16]; sprintf_s(today, "%04d-%02d-%02d", t.wYear, t.wMonth, t.wDay);
        if (c.exp < today) { c.st = CLE_EXPIREE; return c; }
    }
    bool mine = false;
    std::string ids = "," + f["ids"] + ",", me = Narrow(myId, CP_UTF8);
    for (auto &ch : ids) ch = (char)toupper((unsigned char)ch);
    for (auto &ch : me) ch = (char)toupper((unsigned char)ch);
    if (!me.empty() && ids.find("," + me + ",") != std::string::npos) mine = true;
    if (steamId && ids.find("," + std::to_string(steamId) + ",") != std::string::npos) mine = true;
    c.st = mine ? CLE_VALIDE : CLE_AUTRE_ID;
    return c;
}
static std::string CleSaved()
{
    wchar_t b[1024] = L"";
    GetPrivateProfileStringW(L"Lanceur", L"CleAcces", L"", b, 1024, g_iniLauncher.c_str());
    return Narrow(b, CP_UTF8);
}
// Series d'un fichier cles-retirees.txt : une par ligne (8 hex), # = commentaire.
static std::set<std::string> CleSeries(const std::string &s)
{
    std::set<std::string> r;
    for (size_t a = 0; a < s.size();) {
        size_t b = s.find('\n', a);
        std::string l = s.substr(a, b == std::string::npos ? std::string::npos : b - a);
        a = b == std::string::npos ? s.size() : b + 1;
        while (!l.empty() && (l.back() == '\r' || l.back() == ' ' || l.back() == '\t')) l.pop_back();
        while (!l.empty() && (l[0] == ' ' || l[0] == '\t')) l.erase(0, 1);
        if (l.size() == 8 && l[0] != '#') r.insert(l);
    }
    return r;
}
static void CleRetireesLoad()
{
    static bool loaded = false;
    if (loaded) return;
    loaded = true;
    std::vector<unsigned char> d;
    if (!LocalDir().empty() && ReadAll(LocalDir() + L"cles-retirees.txt", d)) g_cleRetirees = CleSeries(std::string(d.begin(), d.end()));
}
// Liste des cles retirees : celle du depot (miroirs compris, comme versions.json), gardee en cache ; hors ligne : le cache.
static void CleRetireesFetch()
{
    std::string s;
    DWORD code = 0;
    if (!HttpGet(L"https://raw.githubusercontent.com/" + g_repo + L"/main/dist/cles-retirees.txt", &s, L"", false, &code) || code != 200) return;
    g_cleRetirees = CleSeries(s);
    if (!LocalDir().empty()) {
        HANDLE h = CreateFileW((LocalDir() + L"cles-retirees.txt").c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL);
        if (h != INVALID_HANDLE_VALUE) { DWORD w; WriteFile(h, s.data(), (DWORD)s.size(), &w, NULL); CloseHandle(h); }
    }
}
static CleInfo CleCurrent()
{
    CleRetireesLoad();
    return CleCheck(CleSaved(), ClePubs(), g_cleRetirees, MwcoopId(), g_steamMe);
}
// Pour [Addons et Tools] (page Outils) : une cle valide pour ce PC qui donne l'outil des places.
static bool AccesPlaces() { CleInfo c = CleCurrent(); return c.st == CLE_VALIDE && CleDroit(c, "places"); }
// Texte de l'etat (champ « Cle d'acces »).
static std::wstring CleEtat(const CleInfo &c)
{
    switch (c.st) {
    case CLE_VALIDE: { std::wstring s = T(L"Clé de ", L"Key of ") + c.nom + T(L" — outil des places", L" — seat tool"); if (!c.exp.empty()) s += T(L" (valable jusqu'au ", L" (valid until ") + Widen(c.exp) + L")"; return s; }
    case CLE_FORMAT: return T(L"Ce n'est pas une clé d'accès MWCoop", L"This is not an MWCoop access key");
    case CLE_SIGNATURE: return T(L"Clé refusée : signature invalide", L"Key refused: invalid signature");
    case CLE_AUTRE_ID: return T(L"Clé de ", L"Key of ") + c.nom + T(L" : pas pour cet ID MWCoop", L": not for this MWCoop ID");
    case CLE_RETIREE: return T(L"Clé de ", L"Key of ") + c.nom + T(L" : retirée", L": withdrawn");
    case CLE_EXPIREE: return T(L"Clé de ", L"Key of ") + c.nom + T(L" : expirée", L": expired");
    }
    return T(L"Aucune clé", L"No key");
}

// ---- outil chiffre : manifeste signe (outil-places.json : {"manifeste":"MWCM1.<donnees>.<signature>"}, donnees
// « v=1;version=;sha256=;asset=; ») et paquet .mwcp (« MWCP », 1, nonce 12, tag 16, zip chiffre AES-256-GCM avec k).
struct PlacesMan { std::string version, sha256, asset; };
static bool PlacesManifest(const std::string &json, const std::vector<std::string> &pubs, PlacesMan &m)
{
    size_t a = json.find("MWCM1.");
    if (a == std::string::npos) return false;
    size_t b = json.find('"', a);
    std::string data;
    if (!cle::Signed(json.substr(a, b == std::string::npos ? std::string::npos : b - a), "MWCM1", pubs, data)) return false;
    auto f = cle::Fields(data);
    m.version = f["version"]; m.sha256 = f["sha256"]; m.asset = f["asset"];
    return f["v"] == "1" && !m.version.empty() && m.sha256.size() == 64 && !m.asset.empty() && m.asset.find_first_of("/\\:") == std::string::npos;
}
static bool PlacesDecrypt(const std::string &pkg, const std::string &k, std::string &zip)
{
    if (pkg.size() < 33 || pkg.compare(0, 4, "MWCP") || pkg[4] != 1) return false;
    std::string tag = pkg.substr(17, 16);
    return cle::AesGcm(false, k, pkg.substr(5, 12), pkg.substr(33), zip, tag);
}