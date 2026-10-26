// ---------------------------------------------------------------- OUTIL DES PLACES RESERVE (inclus par launcher.cpp, apres
// es2.inc) : ID MWCoop de ce PC, cle d'acces signee par JD (cle.inc), liste des cles retirees. Spec : D:\1 - AI\ClaudeAI\
// MWCoop-spec-cle-places.md (validee par JD le 26/10, cles sans limite de duree). Bouton « Placement des sieges » : page
// Outils ([Addons et Tools]), seulement si AccesPlaces().
#include "cle.inc"

// Cles publiques de JD (X || Y en hexadecimal, 128 caracteres), en liste : une nouvelle paire n'invalide pas les cles faites
// avec l'ancienne. Ecrites par outils-prives/cles (creer-paire) ; la cle privee et sa sauvegarde restent chez JD, hors du depot.
static const char *const kJdPubKeys[] = {
    "be292a5edfff54146b959f97796fe2cafbe8e3f1cceae78d366bd6a09d2d81f8ddd022110d6835b8110a30d73390e17eb310d8405962ab2c9c9e27d8ab592857",   // (paire de JD, 26/10)
    NULL };
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
    std::set<std::string> r = CleSeries(s);
    EnterCriticalSection(&g_cs); g_cleRetirees = r; LeaveCriticalSection(&g_cs);
    if (!LocalDir().empty()) {
        HANDLE h = CreateFileW((LocalDir() + L"cles-retirees.txt").c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL);
        if (h != INVALID_HANDLE_VALUE) { DWORD w; WriteFile(h, s.data(), (DWORD)s.size(), &w, NULL); CloseHandle(h); }
    }
}
static CleInfo CleCurrent()
{
    CleRetireesLoad();
    // (dessinee a chaque image dans Reglages : verification refaite seulement quand la cle, la liste ou le SteamID change)
    static std::string lastLine; static uint64_t lastSteam; static size_t lastRet = (size_t)-1; static CleInfo last;
    std::string line = CleSaved();
    EnterCriticalSection(&g_cs);
    std::set<std::string> ret = g_cleRetirees;
    LeaveCriticalSection(&g_cs);
    if (line != lastLine || g_steamMe != lastSteam || ret.size() != lastRet) {
        lastLine = line; lastSteam = g_steamMe; lastRet = ret.size();
        last = CleCheck(line, ClePubs(), ret, MwcoopId(), g_steamMe);
    }
    return last;
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

// ---- l'outil : manifeste de la derniere release outils- qui l'a (ToolsSeen, outils.inc), paquet .mwcp a cote. Telecharge
// (miroirs), verifie (signature du manifeste, sha256), dechiffre avec k, extrait dans %LOCALAPPDATA%\MWCoop\OutilPlaces\ ;
// refait quand la version du manifeste change. Jamais l'outil en clair ailleurs que la.
static std::wstring g_placesManUrl;     // outil-places.json de la derniere release outils- (vide : jamais publie)
static volatile bool g_placesBusy;      // telechargement / lancement en cours
static float g_placesProg = -1;         // 0..1 pendant le telechargement
static volatile bool g_placesExporting; // « Exporter mes voitures » en cours
static void PlacesSeen(const std::vector<Note> &rel)
{
    const Note *b = NULL;
    for (const Note &n : rel) {
        if (n.pre && !NightlyOn()) continue;
        if (!n.places.empty() && (!b || CmpVer(n.ver, b->ver) > 0)) b = &n;
    }
    EnterCriticalSection(&g_cs); g_placesManUrl = b ? b->places : L""; LeaveCriticalSection(&g_cs);
}
static std::wstring PlacesDir() { std::wstring l = LocalDir(); return l.empty() ? L"" : l + L"OutilPlaces\\"; }
static std::wstring PlacesExe() { std::wstring d = PlacesDir(); return d.empty() ? L"" : d + L"OutilPlaces.exe"; }
static std::string PlacesLocalVer()
{
    std::vector<unsigned char> d;
    if (PlacesExe().empty() || !FileExists(PlacesExe()) || !ReadAll(PlacesDir() + L"version.txt", d)) return "";
    std::string v(d.begin(), d.end());
    while (!v.empty() && (v.back() == '\r' || v.back() == '\n' || v.back() == ' ')) v.pop_back();
    return v;
}
static bool PlacesRunning()
{
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return false;
    PROCESSENTRY32W pe = { sizeof(pe) };
    bool found = false;
    for (BOOL ok = Process32FirstW(snap, &pe); ok && !found; ok = Process32NextW(snap, &pe)) found = !_wcsicmp(pe.szExeFile, L"OutilPlaces.exe");
    CloseHandle(snap);
    return found;
}
// WebView2 (l'interface de l'outil) : Windows 10/11 l'ont avec Edge ; Windows 7, ou retire a la main : non.
static bool WebView2Ok()
{
    static const wchar_t *k = L"\\Microsoft\\EdgeUpdate\\Clients\\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
    const struct { HKEY root; std::wstring path; } where[] = {
        { HKEY_LOCAL_MACHINE, std::wstring(L"SOFTWARE\\WOW6432Node") + k }, { HKEY_LOCAL_MACHINE, std::wstring(L"SOFTWARE") + k },
        { HKEY_CURRENT_USER, std::wstring(L"Software") + k } };
    for (auto &w : where) {
        wchar_t v[64] = L""; DWORD sz = sizeof(v);
        if (RegGetValueW(w.root, w.path.c_str(), L"pv", RRF_RT_REG_SZ, NULL, v, &sz) == ERROR_SUCCESS && v[0] && wcscmp(v, L"0.0.0.0")) return true;
    }
    return false;
}
// Racine de l'export du jeu et des envois de l'outil : Documents\My Seasons Car\Places (sans \ final).
static std::wstring PlacesRoot()
{
    wchar_t doc[MAX_PATH] = L"";
    if (FAILED(SHGetFolderPathW(NULL, CSIDL_PERSONAL, NULL, SHGFP_TYPE_CURRENT, doc)) || !doc[0]) return L"";
    return WithSlash(doc) + L"My Seasons Car\\Places";
}

// Met l'outil a la version du manifeste (rien si c'est deja elle). why : raison d'un echec, pour le journal et le message.
static bool PlacesEnsure(const std::wstring &manUrl, const std::vector<std::string> &pubs, const std::string &k, std::string &why, float *prog)
{
    std::wstring dir = PlacesDir();
    if (dir.empty()) { why = "dossier local introuvable"; return false; }
    std::string json;
    DWORD code = 0;
    if (!HttpGet(manUrl, &json, L"", false, &code) || code != 200) { why = code ? "manifeste : HTTP " + std::to_string(code) : "manifeste : reseau (erreur " + std::to_string(g_httpErr) + ")"; return false; }
    PlacesMan m;
    if (!PlacesManifest(json, pubs, m)) { why = "manifeste refuse (signature)"; return false; }
    if (PlacesLocalVer() == m.version) return true;
    if (PlacesRunning()) { why = "outil-ouvert"; return false; }
    std::wstring url = manUrl.substr(0, manUrl.rfind(L'/') + 1) + Widen(m.asset);
    std::wstring tmp = LocalDir() + L"OutilPlaces-telechargement.mwcp", zf = LocalDir() + L"OutilPlaces-telechargement.zip", stage = LocalDir() + L"OutilPlaces-nouveau";
    bool ok = HttpGet(url, NULL, tmp, prog != NULL, &code, prog) && code == 200;
    std::vector<unsigned char> d;
    if (!ok) why = code ? "paquet : HTTP " + std::to_string(code) : "paquet : reseau (erreur " + std::to_string(g_httpErr) + ")";
    else if (!ReadAll(tmp, d)) { ok = false; why = "paquet : lecture"; }
    DeleteFileW(tmp.c_str());
    if (!ok) return false;
    std::string pkg(d.begin(), d.end()), zip;
    if (cle::Hex(cle::Sha256(pkg)) != m.sha256) { why = "paquet : sha256 different du manifeste"; return false; }
    if (!PlacesDecrypt(pkg, k, zip)) { why = "paquet : dechiffrement refuse (cle d'une autre version ?)"; return false; }
    HANDLE h = CreateFileW(zf.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_TEMPORARY, NULL);
    if (h == INVALID_HANDLE_VALUE) { why = "zip : ecriture"; return false; }
    DWORD w = 0;
    ok = WriteFile(h, zip.data(), (DWORD)zip.size(), &w, NULL) && w == zip.size();
    CloseHandle(h);
    DeleteTree(stage);   // (notre dossier, jamais une jonction)
    if (ok) ok = Unzip(zf, stage + L"\\", &why);
    else why = "zip : ecriture";
    DeleteFileW(zf.c_str());
    if (ok && !FileExists(stage + L"\\OutilPlaces.exe")) { ok = false; why = "paquet sans OutilPlaces.exe"; }
    if (ok) {
        std::wstring old = dir.substr(0, dir.size() - 1);
        DeleteTree(old);
        if (!MoveFileExW(stage.c_str(), old.c_str(), 0)) { ok = false; why = "remplacement (erreur " + std::to_string(GetLastError()) + ")"; }
    }
    if (!ok) { DeleteTree(stage); return false; }
    LaunchLog("outil des places %s installe", m.version.c_str());
    return true;
}

static bool PlacesLaunch()
{
    std::wstring exe = PlacesExe();
    if (exe.empty() || !FileExists(exe)) return false;
    std::wstring cmd = L"\"" + exe + L"\" --langue " + (g_fr ? L"fr" : L"en");
    std::wstring root = PlacesRoot();
    if (!root.empty()) cmd += L" --racine \"" + root + L"\"";
    if (!g_modRoot.empty()) cmd += L" --mod \"" + g_modRoot + L"MWCoop\"";   // (le jeu relit places\<jeu>\ tout seul)
    cmd += std::wstring(L" --jeu ") + GM().id + L" --lanceur \"" + g_self + L"\"";   // (export depuis l'outil : /exportplaces)
    STARTUPINFOW si = { sizeof(si) }; PROCESS_INFORMATION pi = {};
    std::vector<wchar_t> cb(cmd.begin(), cmd.end()); cb.push_back(0);
    if (!CreateProcessW(exe.c_str(), cb.data(), NULL, NULL, FALSE, 0, NULL, PlacesDir().c_str(), &si, &pi)) {
        SetStatus(K_ERR, T(L"Impossible de lancer l'outil des places (erreur %lu)", L"Could not start the seat tool (error %lu)"), GetLastError());
        return false;
    }
    CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
    LaunchLog("outil des places lance : %s", Narrow(cmd, CP_UTF8).c_str());
    return true;
}

static std::string g_placesK;   // (k de la cle, pris par PlacesOpen avant le fil)
static DWORD WINAPI PlacesOpenThread(void *)
{
    CleRetireesFetch();   // (une cle retiree entre-temps : l'outil n'est plus mis a jour ; le bouton disparait au prochain dessin)
    std::wstring man;
    EnterCriticalSection(&g_cs); man = g_placesManUrl; LeaveCriticalSection(&g_cs);
    std::string why;
    bool have = FileExists(PlacesExe());
    g_placesProg = 0;
    bool ok = !man.empty() && PlacesEnsure(man, ClePubs(), g_placesK, why, &g_placesProg);
    g_placesProg = -1;
    if (man.empty()) why = "pas encore publie";
    if (!ok) LaunchLog("outil des places : %s", why.c_str());
    if (!ok && why == "outil-ouvert") SetStatus(K_WARN, T(L"Ferme l'outil des places : une nouvelle version l'attend", L"Close the seat tool: a new version is waiting"));
    else if (!ok && !have) SetStatus(K_ERR, T(L"Outil des places indisponible (%s)", L"Seat tool unavailable (%s)"), Widen(why).c_str());
    else if (PlacesRunning()) SetStatus(K_NORMAL, T(L"L'outil des places est déjà ouvert", L"The seat tool is already open"));
    else if (PlacesLaunch()) {
        if (!WebView2Ok()) SetStatus(K_WARN, T(L"L'outil des places a besoin de Microsoft Edge WebView2 (Windows 10/11) : installe-le s'il ne s'affiche pas", L"The seat tool needs Microsoft Edge WebView2 (Windows 10/11): install it if nothing shows"));
        else SetStatus(K_OK, T(L"Outil des places ouvert", L"Seat tool opened"));
    }
    g_placesBusy = false;
    return 0;
}
// Pour [Addons et Tools] (bouton « Placement des sieges ») : met l'outil a jour s'il le faut, puis le lance (en tache de fond ;
// PlacesBusy() et g_placesProg pour le bouton). Rien sans cle valide.
static bool PlacesBusy() { return g_placesBusy; }
static void PlacesOpen()
{
    if (g_placesBusy) return;
    if (!AccesPlaces()) { SetStatus(K_WARN, L"%s", CleEtat(CleCurrent()).c_str()); return; }
    g_placesK = CleCurrent().k;
    g_placesBusy = true;
    HANDLE h = CreateThread(NULL, 0, PlacesOpenThread, NULL, 0, NULL);
    if (h) CloseHandle(h); else g_placesBusy = false;
}

// ---- « Exporter mes voitures » : le jeu de la personne, en arriere-plan (comme l'export de la carte), dans le profil a part
// places-export ou la partie en cours est copiee (ses voitures montees ; sa vraie sauvegarde n'est jamais ouverte) ; le mod
// (SeatExport.cs) ecrit Documents\My Seasons Car\Places\<jeu>\ et etat.txt, puis ferme le jeu. Par un 2e lanceur
// (/exportplaces <jeu> <dossier>, aussi pour l'outil : --lanceur) : ce lanceur-ci ne suit pas ce jeu-la comme une partie.
static bool PlacesExportPrepare(std::string &why)
{
    std::wstring src = SaveDirOf(SaveCurrentProf()), dst = SaveDirOf(L"places-export");
    if (src.empty() || dst.empty()) { why = "sauvegarde introuvable"; return false; }
    WIN32_FIND_DATAW fd;   // (dernier export : ses fichiers seulement, pas de sous-dossier)
    HANDLE h = FindFirstFileW((dst + L"*").c_str(), &fd);
    if (h != INVALID_HANDLE_VALUE) {
        do { if (!(fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)) { SetFileAttributesW((dst + fd.cFileName).c_str(), FILE_ATTRIBUTE_NORMAL); DeleteFileW((dst + fd.cFileName).c_str()); } } while (FindNextFileW(h, &fd));
        FindClose(h);
    }
    int n = SaveCopyFiles(src, dst);
    LaunchLog("export des places : partie %s copiee dans places-export (%d fichiers)", Narrow(SaveCurrentProf().empty() ? L"principale" : SaveCurrentProf(), CP_UTF8).c_str(), n);
    if (n == 0) { why = "pas de sauvegarde (joue une partie d'abord)"; return false; }
    return true;
}
static DWORD WINAPI PlacesExportThread(void *)
{
    std::wstring root = PlacesRoot(), dir = root.empty() ? L"" : root + L"\\" + GM().id;
    std::string why;
    if (dir.empty()) why = "Documents introuvable";
    else {
        SHCreateDirectoryExW(NULL, dir.c_str(), NULL);
        DeleteFileW((dir + L"\\etat.txt").c_str()); DeleteFileW((dir + L"\\lanceur.txt").c_str());
        std::wstring cmd = L"\"" + g_self + L"\" /exportplaces " + GM().id + L" \"" + dir + L"\"";
        STARTUPINFOW si = { sizeof(si) }; PROCESS_INFORMATION pi = {};
        std::vector<wchar_t> cb(cmd.begin(), cmd.end()); cb.push_back(0);
        if (!CreateProcessW(g_self.c_str(), cb.data(), NULL, NULL, FALSE, CREATE_NO_WINDOW, NULL, NULL, &si, &pi)) why = "lanceur (erreur " + std::to_string(GetLastError()) + ")";
        else {
            CloseHandle(pi.hThread);
            WaitForSingleObject(pi.hProcess, 120000);
            CloseHandle(pi.hProcess);
            std::vector<unsigned char> d;
            std::string t = ReadAll(dir + L"\\lanceur.txt", d) ? std::string(d.begin(), d.end()) : "";
            if (t.find("ok=1") == std::string::npos) why = t.find("erreur=") != std::string::npos ? t.substr(t.find("erreur=") + 7) : "lancement impossible";
        }
    }
    while (!why.empty() && (why.back() == '\r' || why.back() == '\n')) why.pop_back();
    if (!why.empty()) {
        LaunchLog("export des places : %s", why.c_str());
        if (why == "jeu-ouvert") SetStatus(K_ERR, T(L"Ferme le jeu avant d'exporter tes voitures", L"Close the game before exporting your cars"));
        else SetStatus(K_ERR, T(L"Export des voitures impossible (%s)", L"Could not export the cars (%s)"), Widen(why, CP_UTF8).c_str());
        g_placesExporting = false;
        return 0;
    }
    SetStatus(K_NORMAL, T(L"Export des voitures : le jeu démarre en arrière-plan (2 à 3 minutes)…", L"Exporting the cars: the game starts in the background (2 to 3 minutes)…"));
    DWORD t0 = GetTickCount(), goneT = 0;
    bool done = false, failed = false;
    std::wstring last;
    auto field = [](const std::string &s, const char *k) {
        size_t a = s.find(std::string(k) + "=");
        if (a == std::string::npos) return std::string();
        a += strlen(k) + 1;
        size_t b = s.find('\n', a);
        std::string v = s.substr(a, b == std::string::npos ? std::string::npos : b - a);
        while (!v.empty() && v.back() == '\r') v.pop_back();
        return v;
    };
    while (GetTickCount() - t0 < 15 * 60000) {
        Sleep(1000);
        std::vector<unsigned char> d;
        if (ReadAll(dir + L"\\etat.txt", d)) {
            std::string s(d.begin(), d.end());
            std::wstring txt = Widen(field(s, "texte"), CP_UTF8);
            if (field(s, "fini") == "1") { done = true; failed = field(s, "etape") == "erreur"; last = txt; break; }
            if (txt != last) { last = txt; SetStatus(K_NORMAL, T(L"Export des voitures : %s…", L"Exporting the cars: %s…"), txt.c_str()); }
        }
        if (GameProcessRunning(NULL)) goneT = 0;
        else if (!goneT) goneT = GetTickCount();
        else if (GetTickCount() - goneT > 20000) break;   // (jeu ferme sans fini=1 : arret brutal, ou ferme a la main)
    }
    LaunchLog("export des places : %s (%s)", done ? (failed ? "ERREUR" : "fini") : "jeu ferme avant la fin", Narrow(last, CP_UTF8).c_str());
    if (done && !failed) SetStatus(K_OK, T(L"Voitures exportées : ouvre l'outil des places", L"Cars exported: open the seat tool"));
    else if (done) SetStatus(K_ERR, T(L"Export des voitures : erreur (%s), voir les journaux", L"Car export: error (%s), see the logs"), last.c_str());
    else SetStatus(K_ERR, T(L"Le jeu s'est fermé avant la fin de l'export des voitures : voir les journaux", L"The game closed before the car export finished: see the logs"));
    g_placesExporting = false;
    return 0;
}
// Pour [Addons et Tools] (bouton « Exporter mes voitures ») : a faire avec une partie ou les voitures sont MONTEES.
static bool PlacesExporting() { return g_placesExporting; }
static void PlacesExport()
{
    if (g_placesExporting || g_gameDir.empty() || !g_modOk) return;
    if (GameProcessRunning(NULL)) { SetStatus(K_WARN, T(L"Ferme le jeu avant d'exporter tes voitures", L"Close the game before exporting your cars")); return; }
    g_placesExporting = true;
    HANDLE h = CreateThread(NULL, 0, PlacesExportThread, NULL, 0, NULL);
    if (h) CloseHandle(h); else g_placesExporting = false;
}

// ---- Reglages > Lanceur, carte « Acces reserve » (gfx.inc) : l'ID MWCoop (a envoyer a JD) et la cle recue en retour, collee,
// ouverte (.txt) ou deposee sur la fenetre. Gardee seulement si elle est valide pour ce PC.
static std::wstring CleIdText() { std::wstring id = MwcoopId(); return id.empty() ? T(L"indisponible", L"unavailable") : id; }
static std::wstring CleEtatText() { return CleEtat(CleCurrent()); }
static bool CleHas() { return !CleSaved().empty(); }
static void CleCopyId()
{
    std::wstring id = MwcoopId();
    if (!id.empty() && CopyText(id)) SetStatus(K_OK, T(L"ID MWCoop copié : envoie-le à JD", L"MWCoop ID copied: send it to JD"));
}
// Texte colle ou lu : la premiere « MWCP1.xxx.yyy » qu'il contient.
static void CleSet(const std::string &text)
{
    size_t a = text.find("MWCP1.");
    if (a == std::string::npos) { SetStatus(K_ERR, L"%s", CleEtat(CleInfo{ CLE_FORMAT }).c_str()); return; }
    size_t b = text.find_first_of(" \t\r\n\"'<>", a);
    std::string line = text.substr(a, b == std::string::npos ? std::string::npos : b - a);
    EnterCriticalSection(&g_cs);
    std::set<std::string> ret = g_cleRetirees;
    LeaveCriticalSection(&g_cs);
    CleInfo c = CleCheck(line, ClePubs(), ret, MwcoopId(), g_steamMe);
    LaunchLog("cle d'acces proposee : serie %s, etat %d", c.serie.c_str(), c.st);
    if (c.st != CLE_VALIDE) { SetStatus(K_ERR, L"%s", CleEtat(c).c_str()); return; }
    WritePrivateProfileStringW(L"Lanceur", L"CleAcces", Widen(line).c_str(), g_iniLauncher.c_str());
    SetStatus(K_OK, T(L"%s : bouton « Placement des sièges » sur la page Outils", L"%s: “Seat placement” button on the Tools page"), CleEtat(c).c_str());
}
static void ClePaste()
{
    std::string t;
    if (OpenClipboard(g_wnd)) {
        HANDLE h = GetClipboardData(CF_UNICODETEXT);
        const wchar_t *w = h ? (const wchar_t *)GlobalLock(h) : NULL;
        if (w) { t = Narrow(w, CP_UTF8); GlobalUnlock(h); }
        CloseClipboard();
    }
    if (t.empty()) { SetStatus(K_WARN, T(L"Rien à coller : copie d'abord la clé reçue de JD", L"Nothing to paste: copy the key you got from JD first")); return; }
    CleSet(t);
}
static void CleFile()
{
    wchar_t file[MAX_PATH] = L"";
    OPENFILENAMEW of = { sizeof(of) };
    of.hwndOwner = g_wnd;
    of.lpstrFilter = L"Texte\0*.txt\0*.*\0*.*\0";
    of.lpstrFile = file; of.nMaxFile = MAX_PATH;
    of.lpstrTitle = T(L"Choisir le fichier de la clé (mwcoop-cle-….txt)", L"Choose the key file (mwcoop-cle-….txt)");
    of.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_HIDEREADONLY;
    if (!GetOpenFileNameW(&of)) return;
    std::vector<unsigned char> d;
    if (ReadAll(file, d) && d.size() < 65536) CleSet(std::string(d.begin(), d.end()));
}
// Fichier depose sur la fenetre (WM_DROPFILES, launcher.cpp) : pris si c'est un .txt avec une cle, sinon laisse a ContentDrop.
static bool CleDrop(HDROP h)
{
    if (DragQueryFileW(h, 0xFFFFFFFF, NULL, 0) != 1) return false;
    wchar_t p[MAX_PATH] = L"";
    if (!DragQueryFileW(h, 0, p, MAX_PATH)) return false;
    size_t n = wcslen(p);
    std::vector<unsigned char> d;
    if (n < 4 || _wcsicmp(p + n - 4, L".txt") || !ReadAll(p, d) || d.size() >= 65536) return false;
    std::string t(d.begin(), d.end());
    if (t.find("MWCP1.") == std::string::npos) return false;
    DragFinish(h);
    CleSet(t);
    return true;
}
static void CleRemove()
{
    if (MessageBoxW(g_wnd, T(L"Retirer la clé d'accès de ce PC ? Tu pourras la remettre ensuite.", L"Remove the access key from this PC? You can put it back later."),
                    T(L"Clé d'accès", L"Access key"), MB_YESNO | MB_ICONQUESTION) != IDYES) return;
    WritePrivateProfileStringW(L"Lanceur", L"CleAcces", NULL, g_iniLauncher.c_str());
    SetStatus(K_NORMAL, T(L"Clé d'accès retirée", L"Access key removed"));
}