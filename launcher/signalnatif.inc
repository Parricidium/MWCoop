// ---------------------------------------------------------------- SIGNALER UN BUG : version native (refonte, lot L5)
// Etape 2 de la spec D:\1 - AI\ClaudeAI\MWCoop-spec-signalement-integre.md : dans la nouvelle interface, le lanceur dessine
// lui-meme « Mes dossiers », le formulaire de depot et les reponses (Completer, Toujours la, Ca marche, Retirer), joint le
// zip des journaux sans Explorateur, et parle a l'API JSON du site (MWCoop-Tableau docs/api-lanceur.md, v1) : Windows 7 et
// Linux compris, sans navigateur integre. Connexion Discord « comme un appareil TV » : POST /code -> le joueur ouvre la page
// /lanceur.html, s'y connecte et TAPE le code (jamais dans l'adresse) -> POST /jeton toutes les 5 s jusqu'au jeton.
// Jeton (43 caracteres) chiffre DPAPI dans %LOCALAPPDATA%\MWCoop\signalement.bin, jamais dans un journal ; le secret
// « appareil » reste en memoire le temps de la connexion. En-tete X-MWCoop-Lanceur = version du lanceur (chiffres.chiffres.chiffres).
// Tout texte venu du site est dessine en texte brut. Une seule source de traduction : GET /textes?lang= (cache 1 h dans
// signalement-textes-<lang>.json) ; les quelques textes propres au lanceur passent par T() comme ailleurs.
// Repli (tant que L5 n'est pas valide) : [Lanceur] SignalerNatif=0 rend le panneau WebView2 de l'etape 1 (ou le navigateur) ;
// si le site est injoignable, la page propose le navigateur et, quand il est possible, le panneau.
// Essais, jamais de fenetre : /testsignalnatif <journal> <base http du relais local de [Site Tableau]> (parcours complet contre
// le faux Discord : code -> jeton -> moi -> brouillon -> morceaux -> envoyer -> mes -> retirer -> deconnexion) ;
// /capture <png> signaler-natif-<connexion|code|liste|dossier|formulaire|envoi|merci> (donnees factices, MWCOOP_SIGNAL_TEXTES=<json>).
// Variables d'essai : MWCOOP_SIGNAL_BASE (base de l'API, http accepte), MWCOOP_SIGNAL_TEXTES (textes sans reseau).

static const wchar_t *kSigApiBase = L"https://live.myseasonscar.workers.dev/api/lanceur/v1";
static const wchar_t *kSigPage = L"https://live.myseasonscar.workers.dev/lanceur.html";
enum { UI_SIG = 14000, UI_SIG_LOGIN = 14000, UI_SIG_OPENPAGE, UI_SIG_CANCEL, UI_SIG_NEWCODE, UI_SIG_NEW, UI_SIG_LOGOUT, UI_SIG_BROWSER, UI_SIG_BACK,
       UI_SIG_CAMARCHE, UI_SIG_TOUJOURS, UI_SIG_COMPLETER, UI_SIG_RETIRER, UI_SIG_ADDSEND, UI_SIG_ADDCANCEL, UI_SIG_ADDZIP, UI_SIG_ADDTEXT,
       UI_SIG_SEND, UI_SIG_FORMCANCEL, UI_SIG_TOLIST, UI_SIG_RETRY, UI_SIG_PANEL, UI_SIG_REFRESH,
       UI_SIG_FIELD = 14100,   // + 10 * champ : +0 le texte (focus), +1..+7 les options d'un choix, +8 le detail, +9 l'interrupteur
       UI_SIG_ROW = 14300 };   // + i : une ligne de « Mes dossiers » (50 au plus)
enum { SV_LOADING, SV_LOGIN, SV_CODE, SV_LIST, SV_DETAIL, SV_FORM, SV_SEND, SV_DONE, SV_ERROR };
static float SigMaxScroll();
enum { SOP_NONE, SOP_TEXTES, SOP_CODE, SOP_JETON, SOP_MOI, SOP_MES, SOP_RENOUVELER, SOP_DECONNEXION, SOP_DEPOSER, SOP_CAMARCHE, SOP_RETIRER, SOP_AVATAR };

// ---- JSON : un petit arbre (le site renvoie des objets et des tableaux imbriques)
struct Jv {
    int t = 0;   // 0 null, 1 booleen, 2 nombre, 3 chaine, 4 tableau, 5 objet
    bool b = false; double n = 0; std::string s; std::vector<Jv> a; std::vector<std::pair<std::string, Jv>> o;
    const Jv *get(const char *k) const { if (t != 5) return NULL; for (auto &p : o) if (p.first == k) return &p.second; return NULL; }
    std::string str(const char *k, const char *d = "") const { const Jv *v = get(k); return v && v->t == 3 ? v->s : v && v->t == 2 ? std::to_string((long long)v->n) : d; }
    double num(const char *k, double d = 0) const { const Jv *v = get(k); return v && v->t == 2 ? v->n : v && v->t == 3 ? atof(v->s.c_str()) : d; }
    bool flag(const char *k, bool d = false) const { const Jv *v = get(k); return v && v->t == 1 ? v->b : d; }
    const Jv *arr(const char *k) const { const Jv *v = get(k); return v && v->t == 4 ? v : NULL; }
};
static size_t JSkip(const std::string &s, size_t p) { while (p < s.size() && (s[p] == ' ' || s[p] == '\t' || s[p] == '\r' || s[p] == '\n')) p++; return p; }
static size_t JParseV(const std::string &s, size_t p, Jv &v, int depth)
{
    const size_t npos = std::string::npos;
    p = JSkip(s, p);
    if (p >= s.size() || depth > 40) return npos;
    char c = s[p];
    if (c == '{') {
        v.t = 5; p = JSkip(s, p + 1);
        if (p < s.size() && s[p] == '}') return p + 1;
        for (;;) {
            p = JSkip(s, p);
            if (p >= s.size() || s[p] != '"') return npos;
            size_t e = JsonValueEnd(s, p);
            std::string k = JsonDecode(s, p);
            p = JSkip(s, e);
            if (p >= s.size() || s[p] != ':') return npos;
            v.o.push_back(std::make_pair(k, Jv()));
            p = JParseV(s, p + 1, v.o.back().second, depth + 1);
            if (p == npos) return npos;
            p = JSkip(s, p);
            if (p < s.size() && s[p] == ',') { p++; continue; }
            return p < s.size() && s[p] == '}' ? p + 1 : npos;
        }
    }
    if (c == '[') {
        v.t = 4; p = JSkip(s, p + 1);
        if (p < s.size() && s[p] == ']') return p + 1;
        for (;;) {
            v.a.push_back(Jv());
            p = JParseV(s, p, v.a.back(), depth + 1);
            if (p == npos) return npos;
            p = JSkip(s, p);
            if (p < s.size() && s[p] == ',') { p++; continue; }
            return p < s.size() && s[p] == ']' ? p + 1 : npos;
        }
    }
    if (c == '"') { v.t = 3; v.s = JsonDecode(s, p); return JsonValueEnd(s, p); }
    if (!s.compare(p, 4, "true")) { v.t = 1; v.b = true; return p + 4; }
    if (!s.compare(p, 5, "false")) { v.t = 1; v.b = false; return p + 5; }
    if (!s.compare(p, 4, "null")) { v.t = 0; return p + 4; }
    size_t e = p;
    while (e < s.size() && strchr("+-0123456789.eE", s[e])) e++;
    if (e == p) return npos;
    v.t = 2; v.n = atof(s.substr(p, e - p).c_str());
    return e;
}
static bool JParse(const std::string &s, Jv &v) { v = Jv(); size_t e = JParseV(s, 0, v, 0); return e != std::string::npos; }
static std::string SigJStr(const std::wstring &w)   // chaine JSON (echappements complets, UTF-8)
{
    std::string u = Narrow(w, CP_UTF8), o = "\"";
    for (unsigned char c : u) {
        if (c == '"' || c == '\\') { o += '\\'; o += (char)c; }
        else if (c == '\n') o += "\\n";
        else if (c == '\r') {}
        else if (c == '\t') o += "\\t";
        else if (c < 0x20) { char b[8]; sprintf_s(b, "\\u%04x", c); o += b; }
        else o += (char)c;
    }
    return o + "\"";
}

// ---- etat
static bool g_sigTest, g_sigCapture;                     // essais : pas de navigateur ; capture : donnees factices
static std::string g_sigJeton;                           // le jeton (jamais journalise)
static std::wstring g_sigPseudo;
static std::string g_sigExpire, g_sigRenouv;             // dates ISO du site
static bool g_sigLogged, g_sigDepotOuvert = true, g_sigDmBloque;
static bool g_sigAvatarFlag; static Bitmap *g_sigAvatar;   // avatar Discord 64x64 (GET /avatar), garde dans signalement-avatar.png
static Bitmap *LoadPngMem(const void *p, size_t n, UINT maxW, UINT maxH);   // (launcher.cpp)
static int g_sigQuotaH = -1, g_sigQuotaJ = -1;
static long long g_sigZipMax = 15LL * 1024 * 1024, g_sigEnvoiMax = 40LL * 1024 * 1024, g_sigMorceau = 1024 * 1024;
static Jv g_sigTx; static std::string g_sigTxLang; static bool g_sigTxOk;
static int g_sigView = SV_LOADING;
static std::wstring g_sigNote; static int g_sigNoteKind;  // message sous l'en-tete de la vue (erreur du site, etc.)
static std::wstring g_sigFatal;                          // SV_ERROR : site injoignable
// connexion par code
static std::string g_sigAppareil;                        // secret de /code, en memoire seulement
static std::wstring g_sigCode, g_sigCodeUrl; static DWORD g_sigCodeT0; static int g_sigCodeExpire = 600, g_sigCodeState;   // 0 en attente, 1 expire, 2 refuse
// dossiers
struct SigEvt { long long ts; std::string evt; std::wstring detail; };
struct SigDossier { int n = 0; std::wstring titre; std::string jeu, etat, confie, versionCorrigee, motif; long long cree = 0, maj = 0; int reouvertures = 0; bool peutRouvrir = false, peutRetirer = false, dmBloque = false; std::vector<SigEvt> hist; };
static std::vector<SigDossier> g_sigMes; static int g_sigSel = -1; static bool g_sigMesOk;
static int g_sigAddMode;                                 // 0 rien, 1 Completer, 2 Toujours la
static std::wstring g_sigAddText; static bool g_sigAddZip = true;
static DWORD g_sigConfirmT;                              // « Retirer » : second clic dans les 5 s
// formulaire
struct SigField {
    std::string champ, type, libelle, aide, erreur, exemple;
    int mn = 0, mx = 0;
    std::vector<std::pair<std::string, std::wstring>> opts;   // valeur, libelle resolu
    std::string dChamp, dSi, dIndice, dExemple; int dMax = 0;  // champ « detail », actif quand la valeur choisie vaut dSi
    std::wstring val, dVal; int sel = -1; bool on = false; std::wstring err;
    RectF r, dr;
};
static std::vector<SigField> g_sigForm;
static int g_sigFocus = -1;                              // champ i (texte), 100 + i (detail), 500 (texte d'ajout)
static int g_sigZipFiles;                                // journaux a joindre (ZipFiles)
static int g_sigDoneN; static bool g_sigDoneAdd;
static float g_sigContentH, g_sigViewH = 600;
static int g_sigRowHot = -1;
// travail en arriere-plan (une operation a la fois)
struct SigHttp { DWORD status = 0, err = 0; int retryAfter = 0; std::string body; Jv j; std::string erreur; };
struct SigJob { int kind = SOP_NONE; std::string body, path, lang; int n = 0; std::wstring zip; std::vector<std::wstring> files; bool zipWanted = false; };
static SigJob g_sigJob; static SigHttp g_sigRes;
static volatile LONG g_sigBusy, g_sigDone, g_sigCancel;
static std::wstring g_sigProg; static float g_sigFrac = -1; static CRITICAL_SECTION g_sigCs; static bool g_sigCsInit;
static int g_sigAfter;                                   // operation a enchainer (SOP_MES apres SOP_MOI...)
static void SigLock() { if (!g_sigCsInit) { InitializeCriticalSection(&g_sigCs); g_sigCsInit = true; } EnterCriticalSection(&g_sigCs); }
static void SigUnlock() { LeaveCriticalSection(&g_sigCs); }
static void SigProg(const std::wstring &s, float f) { SigLock(); g_sigProg = s; g_sigFrac = f; SigUnlock(); InvalidateAll(); }

static bool SigNativeOn() { return GetPrivateProfileIntW(L"Lanceur", L"SignalerNatif", 1, g_iniLauncher.c_str()) != 0; }
static bool SigActive() { return g_newUi && SigNativeOn(); }   // (la page Signaler un bug dessine la version native)
static std::wstring SigBase()
{
    wchar_t b[512] = L"";
    GetEnvironmentVariableW(L"MWCOOP_SIGNAL_BASE", b, 512);
    std::wstring s = b[0] ? b : kSigApiBase;
    while (!s.empty() && s.back() == L'/') s.pop_back();
    return s;
}
static std::wstring SigVer()   // chiffres.chiffres.chiffres, sinon le site repond 400 « version »
{
    std::wstring v = LauncherVer();
    std::vector<int> parts; int cur = -1;
    for (wchar_t c : v) { if (c >= L'0' && c <= L'9') cur = (cur < 0 ? 0 : cur * 10) + (c - L'0'); else if (cur >= 0) { parts.push_back(cur); cur = -1; } }
    if (cur >= 0) parts.push_back(cur);
    while (parts.size() < 3) parts.push_back(0);
    wchar_t b[48]; swprintf_s(b, L"%d.%d.%d", min(parts[0], 999), min(parts[1], 999), min(parts[2], 9999));
    return b;
}
static const char *SigSysteme()
{
    if (IsWine()) return "linux";
    OSVERSIONINFOW ov = { sizeof(ov) };
    typedef LONG(WINAPI * RtlGetVersionF)(OSVERSIONINFOW *);
    if (RtlGetVersionF rgv = (RtlGetVersionF)GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "RtlGetVersion")) rgv(&ov);
    if (ov.dwMajorVersion == 6 && ov.dwMinorVersion <= 1) return "win7";
    if (ov.dwMajorVersion == 6) return "win8";
    if (ov.dwMajorVersion == 10) return ov.dwBuildNumber >= 22000 ? "win11" : "win10";
    return "autre";
}

// ---- DPAPI (crypt32 charge depuis System32 : jamais d'import statique)
struct SigBlob { DWORD cb; BYTE *pb; };
typedef BOOL(WINAPI *SigProtectF)(SigBlob *, LPCWSTR, SigBlob *, PVOID, PVOID, DWORD, SigBlob *);
typedef BOOL(WINAPI *SigUnprotectF)(SigBlob *, LPWSTR *, SigBlob *, PVOID, PVOID, DWORD, SigBlob *);
static bool SigDpapi(bool protect, const std::string &in, std::string &out)
{
    static HMODULE h;
    if (!h) { wchar_t sys[MAX_PATH] = L""; GetSystemDirectoryW(sys, MAX_PATH); h = LoadLibraryW((std::wstring(sys) + L"\\crypt32.dll").c_str()); }
    if (!h) return false;
    SigBlob di = { (DWORD)in.size(), (BYTE *)in.data() }, dout = { 0, NULL };
    BOOL ok;
    if (protect) { SigProtectF f = (SigProtectF)GetProcAddress(h, "CryptProtectData"); if (!f) return false; ok = f(&di, L"MWCoop signalement", NULL, NULL, NULL, 1 /*CRYPTPROTECT_UI_FORBIDDEN*/, &dout); }
    else { SigUnprotectF f = (SigUnprotectF)GetProcAddress(h, "CryptUnprotectData"); if (!f) return false; ok = f(&di, NULL, NULL, NULL, NULL, 1, &dout); }
    if (!ok || !dout.pb) return false;
    out.assign((const char *)dout.pb, dout.cb);
    LocalFree(dout.pb);
    return true;
}
static std::wstring SigTokenPath() { return LocalDir().empty() ? L"" : LocalDir() + L"signalement.bin"; }
static void SigTokenSave()
{
    std::wstring p = SigTokenPath(); if (p.empty()) return;
    std::string j = "{\"jeton\":" + JsonEsc(g_sigJeton) + ",\"pseudo\":" + SigJStr(g_sigPseudo) + ",\"expire\":" + JsonEsc(g_sigExpire) + ",\"renouvelableJusqua\":" + JsonEsc(g_sigRenouv) + "}", enc;
    if (!SigDpapi(true, j, enc)) { LaunchLog("signalement : jeton non enregistre (DPAPI)"); return; }
    CreateDirectoryW(LocalDir().c_str(), NULL);
    HANDLE f = CreateFileW(p.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (f == INVALID_HANDLE_VALUE) return;
    DWORD w = 0; WriteFile(f, enc.data(), (DWORD)enc.size(), &w, NULL); CloseHandle(f);
}
static void SigTokenClear()
{
    g_sigJeton.clear(); g_sigPseudo.clear(); g_sigExpire.clear(); g_sigRenouv.clear(); g_sigLogged = false; g_sigMes.clear(); g_sigMesOk = false; g_sigSel = -1;
    std::wstring p = SigTokenPath(); if (!p.empty()) DeleteFileW(p.c_str());
    if (g_sigAvatar) { delete g_sigAvatar; g_sigAvatar = NULL; }
    if (!LocalDir().empty()) DeleteFileW((LocalDir() + L"signalement-avatar.png").c_str());
}
static void SigTokenLoad()
{
    static bool done; if (done) return; done = true;
    std::wstring p = SigTokenPath(); std::vector<unsigned char> d;
    if (p.empty() || !ReadAll(p, d) || d.empty()) return;
    std::string dec; Jv j;
    if (!SigDpapi(false, std::string(d.begin(), d.end()), dec) || !JParse(dec, j)) { LaunchLog("signalement : jeton enregistre illisible, efface"); DeleteFileW(p.c_str()); return; }
    g_sigJeton = j.str("jeton"); g_sigPseudo = Widen(j.str("pseudo")); g_sigExpire = j.str("expire"); g_sigRenouv = j.str("renouvelableJusqua");
    g_sigLogged = g_sigJeton.size() >= 20;
    LaunchLog("signalement : jeton charge (%s)", g_sigLogged ? "connecte" : "vide");
    std::vector<unsigned char> av;
    if (g_sigLogged && !g_sigAvatar && ReadAll(LocalDir() + L"signalement-avatar.png", av) && av.size() > 8) g_sigAvatar = LoadPngMem(av.data(), av.size(), 64, 64);
}
static int SigDaysLeft(const std::string &iso)   // jours avant une date ISO (…Z) ; 999 si illisible
{
    int y = 0, mo = 0, d = 0, h = 0, mi = 0;
    if (sscanf_s(iso.c_str(), "%d-%d-%dT%d:%d", &y, &mo, &d, &h, &mi) < 3) return 999;
    SYSTEMTIME st = { (WORD)y, (WORD)mo, 0, (WORD)d, (WORD)h, (WORD)mi, 0, 0 }, now; FILETIME a, b;
    GetSystemTime(&now);
    if (!SystemTimeToFileTime(&st, &a) || !SystemTimeToFileTime(&now, &b)) return 999;
    long long da = ((long long)a.dwHighDateTime << 32 | a.dwLowDateTime) - ((long long)b.dwHighDateTime << 32 | b.dwLowDateTime);
    return (int)(da / (10000000LL * 86400));
}

// ---- textes du site (une seule source de traduction)
static std::wstring SigLang() { std::wstring l = LangCode(); return l == L"fr" || l == L"en" || l == L"ru" || l == L"ar" ? l : L"en"; }
static std::wstring ST(const char *key)   // texte du site dans la langue du lanceur, sinon la cle (jamais vide)
{
    if (const Jv *t = g_sigTx.get("textes")) if (const Jv *l = t->get(g_sigTxLang.c_str())) { std::string s = l->str(key); if (!s.empty()) return Widen(s); }
    return Widen(key);
}
static std::wstring SigFmt(std::wstring s, const wchar_t *tag, const std::wstring &val) { for (size_t p; (p = s.find(tag)) != std::wstring::npos;) s.replace(p, wcslen(tag), val); return s; }
static std::wstring SigTxPath(const std::wstring &lang) { return LocalDir().empty() ? L"" : LocalDir() + L"signalement-textes-" + lang + L".json"; }
static bool SigTxApply(const std::string &json, const std::wstring &lang)
{
    Jv j;
    if (!JParse(json, j) || !j.get("formulaire") || !j.get("textes")) return false;
    g_sigTx = j; g_sigTxLang = Narrow(lang, CP_UTF8); g_sigTxOk = true;
    return true;
}
static bool SigTxLoadCache(const std::wstring &lang, bool *fresh)   // cache d'une heure ; *fresh : encore valable
{
    wchar_t forced[MAX_PATH] = L""; GetEnvironmentVariableW(L"MWCOOP_SIGNAL_TEXTES", forced, MAX_PATH);
    std::wstring p = forced[0] ? std::wstring(forced) : SigTxPath(lang);
    std::vector<unsigned char> d;
    if (p.empty() || !ReadAll(p, d) || !SigTxApply(std::string(d.begin(), d.end()), lang)) return false;
    if (fresh) {
        *fresh = forced[0] != 0;
        WIN32_FILE_ATTRIBUTE_DATA fa;
        if (!*fresh && GetFileAttributesExW(p.c_str(), GetFileExInfoStandard, &fa)) {
            FILETIME now; GetSystemTimeAsFileTime(&now);
            long long dt = ((long long)now.dwHighDateTime << 32 | now.dwLowDateTime) - ((long long)fa.ftLastWriteTime.dwHighDateTime << 32 | fa.ftLastWriteTime.dwLowDateTime);
            *fresh = dt >= 0 && dt < 3600LL * 10000000LL;
        }
    }
    return true;
}
static void SigTxSaveCache(const std::string &json, const std::wstring &lang)
{
    std::wstring p = SigTxPath(lang); if (p.empty()) return;
    CreateDirectoryW(LocalDir().c_str(), NULL);
    HANDLE f = CreateFileW(p.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (f == INVALID_HANDLE_VALUE) return;
    DWORD w = 0; WriteFile(f, json.data(), (DWORD)json.size(), &w, NULL); CloseHandle(f);
}

// ---- HTTP (WinHTTP) : une requete de l'API, corps lu quel que soit le statut, JSON decode. Jamais le corps dans un journal
// (le jeton y passe), seulement la methode, le chemin, le statut et le code d'erreur du site.
static bool SigCall(const wchar_t *method, const std::wstring &path, const std::string &body, bool json, bool auth, SigHttp &r, const std::string &jeton = g_sigJeton)
{
    r = SigHttp();
    std::wstring url = SigBase() + path;
    URL_COMPONENTS uc = { sizeof(uc) };
    wchar_t host[256] = {}, upath[2048] = {}, extra[1024] = {};
    uc.lpszHostName = host; uc.dwHostNameLength = _countof(host);
    uc.lpszUrlPath = upath; uc.dwUrlPathLength = _countof(upath);
    uc.lpszExtraInfo = extra; uc.dwExtraInfoLength = _countof(extra);
    if (!WinHttpCrackUrl(url.c_str(), 0, 0, &uc)) { r.err = 1; return false; }
    std::wstring full = std::wstring(upath) + extra;
    HINTERNET s = WinHttpOpen(L"MWCoop-Launcher", WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY, NULL, NULL, 0);
    if (!s) s = WinHttpOpen(L"MWCoop-Launcher", WINHTTP_ACCESS_TYPE_DEFAULT_PROXY, NULL, NULL, 0);
    if (!s) { r.err = GetLastError(); return false; }
    WinHttpSetTimeouts(s, 8000, 8000, 30000, 60000);
    HINTERNET c = WinHttpConnect(s, host, uc.nPort, 0);
    HINTERNET q = c ? WinHttpOpenRequest(c, method, full.c_str(), NULL, WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, uc.nScheme == INTERNET_SCHEME_HTTPS ? WINHTTP_FLAG_SECURE : 0) : NULL;
    std::wstring hdr = L"X-MWCoop-Lanceur: " + SigVer() + L"\r\nAccept: application/json\r\n";
    if (auth && !jeton.empty()) hdr += L"Authorization: Bearer " + Widen(jeton) + L"\r\n";
    if (!body.empty() || json) hdr += json ? L"Content-Type: application/json\r\n" : L"Content-Type: application/octet-stream\r\n";
    bool ok = false;
    if (q && WinHttpSendRequest(q, hdr.c_str(), (DWORD)-1, body.empty() ? WINHTTP_NO_REQUEST_DATA : (LPVOID)body.data(), (DWORD)body.size(), (DWORD)body.size(), 0) && WinHttpReceiveResponse(q, NULL)) {
        DWORD code = 0, len = sizeof(code);
        WinHttpQueryHeaders(q, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER, WINHTTP_HEADER_NAME_BY_INDEX, &code, &len, WINHTTP_NO_HEADER_INDEX);
        r.status = code;
        DWORD ra = 0; len = sizeof(ra);
        if (WinHttpQueryHeaders(q, WINHTTP_QUERY_RETRY_AFTER | WINHTTP_QUERY_FLAG_NUMBER, WINHTTP_HEADER_NAME_BY_INDEX, &ra, &len, WINHTTP_NO_HEADER_INDEX)) r.retryAfter = (int)ra;
        std::vector<char> buf(64 * 1024);
        for (;;) {
            DWORD n = 0;
            if (!WinHttpReadData(q, buf.data(), (DWORD)buf.size(), &n) || !n) break;
            r.body.append(buf.data(), n);
            if (r.body.size() > 4u << 20) break;
        }
        ok = code != 0;
    }
    if (!ok) r.err = GetLastError();
    if (q) WinHttpCloseHandle(q);
    if (c) WinHttpCloseHandle(c);
    WinHttpCloseHandle(s);
    if (ok && !r.body.empty() && r.body[0] == '{' && JParse(r.body, r.j)) r.erreur = r.j.str("erreur");
    DebugLog("signalement api %s %s : statut %lu, erreur winhttp %lu, erreur site %s", Narrow(method, CP_UTF8).c_str(), Narrow(path, CP_UTF8).c_str(), r.status, r.err, r.erreur.empty() ? "-" : r.erreur.c_str());
    return ok;
}
static std::wstring SigErrText(const SigHttp &r)   // texte d'une reponse ratee, pour le joueur
{
    if (!r.status) { wchar_t b[200]; swprintf_s(b, T(L"Site de signalement injoignable (erreur %lu)", L"Bug report site unreachable (error %lu)"), r.err); return b; }
    if (r.status == 409 && r.erreur == "etat") return ST("erEtatDossier");
    if (!r.erreur.empty()) return ST(("er_" + r.erreur).c_str());
    wchar_t b[120]; swprintf_s(b, T(L"Réponse inattendue du site (%lu)", L"Unexpected answer from the site (%lu)"), r.status); return b;
}
static bool SigSleep(int ms)   // attente par pas, annulable ; false : annule
{
    for (int t = 0; t < ms; t += 100) { if (g_sigCancel) return false; Sleep(100); }
    return !g_sigCancel;
}

// ---- le depot : brouillon -> morceaux -> envoyer (dans le fil de travail)
static bool WriteZip(const std::wstring &zip, const std::vector<std::wstring> &files, int *count, LONGLONG budget, LONGLONG parFichier, bool site);   // (launcher.cpp)
static void SigDeposer(SigJob &job, SigHttp &r)
{
    long long size = 0; int nf = 0;
    if (job.zipWanted && !job.files.empty()) {
        SigProg(T(L"Préparation du zip des journaux…", L"Preparing the logs zip…"), -1);
        CreateDirectoryW(LocalDir().c_str(), NULL);
        if (!WriteZip(job.zip, job.files, &nf, g_sigZipMax - 256 * 1024, 4LL * 1024 * 1024, true)) { r.status = 0; r.err = GetLastError(); r.erreur = "zip-local"; return; }
        WIN32_FILE_ATTRIBUTE_DATA fa;
        if (GetFileAttributesExW(job.zip.c_str(), GetFileExInfoStandard, &fa)) size = (long long)fa.nFileSizeHigh << 32 | fa.nFileSizeLow;
        if (size <= 0) { r.erreur = "zip-local"; return; }
    }
    std::string body = job.body;
    size_t ph = body.find("__ZIPS__");
    if (ph != std::string::npos) body.replace(ph, 8, size > 0 ? "[" + std::to_string(size) + "]" : "[]");
    SigProg(T(L"Envoi des champs…", L"Sending the fields…"), -1);
    if (!SigCall(L"POST", L"/brouillon", body, true, true, r) || r.status != 200) return;
    std::string id = r.j.str("id");
    long long morceau = (long long)r.j.num("morceau", (double)g_sigMorceau);
    int nMorceaux = 0;
    if (const Jv *z = r.j.arr("zips")) if (!z->a.empty()) nMorceaux = (int)z->a[0].num("morceaux");
    if (size > 0 && nMorceaux > 0) {
        HANDLE f = CreateFileW(job.zip.c_str(), GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, 0, NULL);
        if (f == INVALID_HANDLE_VALUE) { r.status = 0; r.err = GetLastError(); r.erreur = "zip-local"; return; }
        std::string chunk;
        for (int i = 0; i < nMorceaux; i++) {
            wchar_t p[64]; swprintf_s(p, T(L"Envoi des journaux (%d/%d)…", L"Sending the logs (%d/%d)…"), i + 1, nMorceaux);
            SigProg(p, (float)i / nMorceaux);
            chunk.assign((size_t)morceau, 0);
            DWORD got = 0;
            LARGE_INTEGER pos; pos.QuadPart = (long long)i * morceau; SetFilePointerEx(f, pos, NULL, FILE_BEGIN);
            if (!ReadFile(f, &chunk[0], (DWORD)morceau, &got, NULL) || !got) { CloseHandle(f); r.status = 0; r.erreur = "zip-local"; return; }
            chunk.resize(got);
            std::wstring path = L"/brouillon/" + Widen(id) + L"/zip/1/" + std::to_wstring(i);
            SigHttp pr;
            bool ok = SigCall(L"PUT", path, chunk, false, true, pr) && pr.status == 200;
            if (!ok && !g_sigCancel) { SigSleep(1500); ok = SigCall(L"PUT", path, chunk, false, true, pr) && pr.status == 200; }   // (une coupure : on renvoie, sans doublon)
            if (!ok) { CloseHandle(f); r = pr; return; }
            if (g_sigCancel) { CloseHandle(f); r.status = 0; r.erreur = "annule"; return; }
        }
        CloseHandle(f);
    }
    SigProg(T(L"Finalisation…", L"Finishing…"), 1);
    std::wstring env = L"/brouillon/" + Widen(id) + L"/envoyer";
    if (!SigCall(L"POST", env, "{}", true, true, r)) return;
    if (r.status == 409 && r.erreur == "morceaux-manquants" && size > 0) {   // (reprise : les morceaux manquants, puis envoyer)
        SigHttp m;
        if (SigCall(L"GET", L"/brouillon/" + Widen(id), "", false, true, m) && m.status == 200) {
            HANDLE f = CreateFileW(job.zip.c_str(), GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, 0, NULL);
            if (const Jv *z = m.j.arr("zips")) if (!z->a.empty() && f != INVALID_HANDLE_VALUE) if (const Jv *mq = z->a[0].arr("manquants"))
                for (const Jv &k : mq->a) {
                    int i = (int)k.n; std::string chunk((size_t)morceau, 0); DWORD got = 0;
                    LARGE_INTEGER pos; pos.QuadPart = (long long)i * morceau; SetFilePointerEx(f, pos, NULL, FILE_BEGIN);
                    if (ReadFile(f, &chunk[0], (DWORD)morceau, &got, NULL) && got) { chunk.resize(got); SigHttp pr; SigCall(L"PUT", L"/brouillon/" + Widen(id) + L"/zip/1/" + std::to_wstring(i), chunk, false, true, pr); }
                }
            if (f != INVALID_HANDLE_VALUE) CloseHandle(f);
        }
        SigCall(L"POST", env, "{}", true, true, r);
    }
    if (!job.zip.empty()) DeleteFileW(job.zip.c_str());
}
static DWORD WINAPI SigThread(void *)
{
    SigJob &job = g_sigJob; SigHttp r;
    switch (job.kind) {
    case SOP_TEXTES: SigCall(L"GET", L"/textes?lang=" + Widen(job.lang), "", false, false, r); break;
    case SOP_CODE: SigCall(L"POST", L"/code", "{\"systeme\":\"" + std::string(SigSysteme()) + "\"}", true, false, r); break;
    case SOP_JETON: {
        DWORD t0 = GetTickCount(); int wait = 5000;
        for (;;) {
            if (!SigSleep(wait)) { r.status = 0; r.erreur = "annule"; break; }
            SigCall(L"POST", L"/jeton", "{\"appareil\":" + JsonEsc(job.body) + "}", true, false, r);
            if (r.status == 428 || (r.status == 429 && r.erreur == "trop-vite")) { wait = 5000; if (GetTickCount() - t0 > (DWORD)(g_sigCodeExpire + 30) * 1000) { r.status = 410; r.erreur = "expire"; break; } continue; }
            if (r.status == 0 && !g_sigCancel) { wait = 8000; if (GetTickCount() - t0 > 600000) break; continue; }   // (reseau : on reessaie jusqu'a la fin du code)
            break;
        }
        break;
    }
    case SOP_MOI: SigCall(L"GET", L"/moi", "", false, true, r); break;
    case SOP_MES: SigCall(L"GET", L"/mes", "", false, true, r); break;
    case SOP_RENOUVELER: SigCall(L"POST", L"/renouveler", "{}", true, true, r); break;
    case SOP_DECONNEXION: SigCall(L"POST", L"/deconnexion", "{}", true, true, r); break;
    case SOP_CAMARCHE: SigCall(L"POST", L"/dossiers/" + std::to_wstring(job.n) + L"/ca-marche", "{}", true, true, r); break;
    case SOP_RETIRER: SigCall(L"POST", L"/dossiers/" + std::to_wstring(job.n) + L"/retirer", "{}", true, true, r); break;
    case SOP_AVATAR: SigCall(L"GET", L"/avatar", "", false, true, r); break;
    case SOP_DEPOSER: SigDeposer(job, r); break;
    }
    SigLock(); g_sigRes = r; SigUnlock();
    InterlockedExchange(&g_sigDone, 1);
    InvalidateAll();
    return 0;
}
static bool SigStart(const SigJob &job)
{
    if (g_sigBusy) return false;
    g_sigJob = job; g_sigFrac = -1; g_sigProg.clear();
    InterlockedExchange(&g_sigCancel, 0); InterlockedExchange(&g_sigDone, 0); InterlockedExchange(&g_sigBusy, 1);
    HANDLE t = CreateThread(NULL, 0, SigThread, NULL, 0, NULL);
    if (!t) { InterlockedExchange(&g_sigBusy, 0); return false; }
    CloseHandle(t);
    InvalidateAll();
    return true;
}
static bool SigGo(int kind, int n = 0) { SigJob j; j.kind = kind; j.n = n; j.lang = Narrow(SigLang(), CP_UTF8); return SigStart(j); }
static void SigNote(const std::wstring &s, int kind) { g_sigNote = s; g_sigNoteKind = kind; InvalidateAll(); }

// ---- donnees recues
static void SigParseMes(const Jv &j)
{
    g_sigMes.clear();
    if (const Jv *ds = j.arr("dossiers")) for (const Jv &d : ds->a) {
        SigDossier x; x.n = (int)d.num("n"); x.titre = Widen(d.str("titre")); x.jeu = d.str("jeu"); x.etat = d.str("etat"); x.confie = d.str("confie"); x.versionCorrigee = d.str("version_corrigee"); x.motif = d.str("motif");
        x.cree = (long long)d.num("cree"); x.maj = (long long)d.num("maj"); x.reouvertures = (int)d.num("reouvertures");
        x.peutRouvrir = d.flag("peutRouvrir"); x.peutRetirer = d.flag("peutRetirer"); x.dmBloque = d.flag("dmBloque");
        if (const Jv *h = d.arr("historique")) for (const Jv &e : h->a) { SigEvt ev; ev.ts = (long long)e.num("ts"); ev.evt = e.str("evt"); ev.detail = Widen(e.str("detail")); x.hist.push_back(ev); }
        g_sigMes.push_back(x);
    }
    g_sigMesOk = true;
}
static std::wstring SigDate(long long ms)   // jj/mm/aaaa hh:mm, heure locale
{
    if (ms <= 0) return L"";
    ULONGLONG ft = ((ULONGLONG)(ms / 1000) + 11644473600ULL) * 10000000ULL;
    FILETIME f = { (DWORD)(ft & 0xFFFFFFFF), (DWORD)(ft >> 32) }, lf; SYSTEMTIME st;
    FileTimeToLocalFileTime(&f, &lf); FileTimeToSystemTime(&lf, &st);
    wchar_t b[40]; swprintf_s(b, L"%02d/%02d/%04d %02d:%02d", st.wDay, st.wMonth, st.wYear, st.wHour, st.wMinute);
    return b;
}
static std::wstring SigEtat(const SigDossier &d)
{
    std::wstring s = ST(("et_" + d.etat).c_str());
    s = SigFmt(s, L"{s}", Widen(d.confie)); s = SigFmt(s, L"{v}", Widen(d.versionCorrigee)); s = SigFmt(s, L"{m}", d.motif.empty() ? L"" : ST(("motif_" + d.motif).c_str()));
    return s;
}
static int SigEtatKind(const std::string &e) { return e == "corrige" || e == "clos_ok" ? SC_OK : e == "infos" ? SC_WARN : e == "refuse" || e == "retire" || e == "clos_motif" || e == "clos_auto" ? SC_BAD : SC_ACC; }
static std::wstring SigAide(const SigDossier &d)
{
    std::wstring s = d.etat == "a_transmettre" ? ST("aide_transmis") : d.etat == "clos_motif" ? ST(("aide_motif_" + d.motif).c_str()) : ST(("aide_" + d.etat).c_str());
    return SigFmt(s, L"{v}", Widen(d.versionCorrigee));
}
static std::wstring SigEvtText(const SigEvt &e)
{
    std::wstring d = e.evt == "clos" ? ST(("motif_" + Narrow(e.detail, CP_UTF8)).c_str()) : e.detail;
    return SigFmt(ST(("ev_" + e.evt).c_str()), L"{d}", d);
}

// ---- formulaire (depuis /textes)
static std::wstring SigOptLabel(const Jv &o) { std::string t = o.str("texte"); return t.empty() ? ST(o.str("libelle").c_str()) : Widen(t); }
static void SigFormBuild()
{
    g_sigForm.clear();
    const Jv *f = g_sigTx.arr("formulaire"); if (!f) return;
    for (const Jv &c : f->a) {
        SigField x; x.champ = c.str("champ"); x.type = c.str("type"); x.libelle = c.str("libelle"); x.aide = c.str("aide"); x.erreur = c.str("erreur"); x.exemple = c.str("exemple");
        x.mn = (int)c.num("min"); x.mx = (int)c.num("max");
        if (const Jv *o = c.arr("options")) for (const Jv &p : o->a) x.opts.push_back(std::make_pair(p.str("valeur"), SigOptLabel(p)));
        if (const Jv *d = c.get("detail")) { x.dChamp = d->str("champ"); x.dSi = d->str("si"); x.dIndice = d->str("indice"); x.dExemple = d->str("exemple"); x.dMax = (int)d->num("max"); }
        // valeurs de depart : le jeu en cours, la version du mod, 1 joueur = solo par defaut non choisi (le joueur dit son role)
        if (x.champ == "jeu") { const char *v = g_game == GAME_MSC ? "msc" : "mwc"; for (int i = 0; i < (int)x.opts.size(); i++) if (x.opts[i].first == v) x.sel = i; }
        if (x.champ == "version" && !g_localVer.empty()) x.val = std::wstring(g_game == GAME_MSC ? L"msc-" : L"mwc-") + g_localVer;
        if (x.champ == "zips") x.on = g_sigZipFiles > 0;
        g_sigForm.push_back(x);
    }
}
static std::string SigFieldValue(const SigField &f) { return f.type == "choix" ? (f.sel >= 0 && f.sel < (int)f.opts.size() ? f.opts[f.sel].first : "") : Narrow(f.val, CP_UTF8); }
static bool SigDetailOn(const SigField &f) { return !f.dChamp.empty() && f.sel >= 0 && f.sel < (int)f.opts.size() && f.opts[f.sel].first == f.dSi; }
static int SigValidate()   // premier champ en faute (-1 : tout va bien) ; les erreurs sont posees sur les champs
{
    int first = -1; std::string role, connexion; int joueurs = -1;
    for (auto &f : g_sigForm) { f.err.clear(); if (f.champ == "role") role = SigFieldValue(f); if (f.champ == "connexion") connexion = SigFieldValue(f); if (f.champ == "joueurs") joueurs = _wtoi(f.val.c_str()); }
    for (int i = 0; i < (int)g_sigForm.size(); i++) {
        SigField &f = g_sigForm[i];
        int len = (int)Trim(f.val).size();
        if (f.type == "choix" && f.sel < 0) f.err = ST("erVide");
        else if ((f.type == "texte" || f.type == "texte_long") && len == 0) f.err = ST("erVide");
        else if ((f.type == "texte" || f.type == "texte_long") && f.mn && len < f.mn) f.err = SigFmt(ST("erTropCourt"), L"{n}", std::to_wstring(f.mn));
        else if ((f.type == "texte" || f.type == "texte_long") && f.mx && len > f.mx) f.err = SigFmt(ST("erTropLong"), L"{n}", std::to_wstring(f.mx));
        else if (f.type == "nombre" && (joueurs < 1 || joueurs > 16 || (role == "solo") != (joueurs == 1))) f.err = ST("erJoueurs");
        else if (f.champ == "connexion" && f.sel >= 0 && ((role == "solo") != (connexion == "pas_concerne"))) f.err = ST("erConnexion");
        else if (f.type == "zips" && (!f.on || g_sigZipFiles <= 0)) f.err = ST("er_zip-manquant");
        else if (SigDetailOn(f) && Trim(f.dVal).empty()) f.err = ST("erVide");
        else if (SigDetailOn(f) && f.dMax && (int)f.dVal.size() > f.dMax) f.err = SigFmt(ST("erTropLong"), L"{n}", std::to_wstring(f.dMax));
        if (!f.err.empty() && first < 0) first = i;
    }
    return first;
}
static std::string SigFormJson()
{
    std::string j = "{";
    for (auto &f : g_sigForm) {
        if (f.type == "zips") { j += "\"zips\":__ZIPS__,"; continue; }
        if (f.type == "case") { j += "\"" + f.champ + "\":" + (f.on ? "true" : "false") + ","; continue; }
        if (f.type == "nombre") { j += "\"" + f.champ + "\":" + std::to_string(_wtoi(f.val.c_str())) + ","; continue; }
        j += "\"" + f.champ + "\":" + SigJStr(f.type == "choix" ? Widen(SigFieldValue(f)) : Trim(f.val)) + ",";
        if (SigDetailOn(f)) j += "\"" + f.dChamp + "\":" + SigJStr(Trim(f.dVal)) + ",";
    }
    if (j.size() > 1) j.pop_back();
    return j + "}";
}
static std::vector<std::wstring> ZipFiles();   // (launcher.cpp)
static void SigZipCount() { g_sigZipFiles = g_sigCapture ? 7 : (int)ZipFiles().size(); }
static std::wstring SigZipPath() { return LocalDir() + L"signalement-envoi.zip"; }

// ---- enchainement des operations
static void SigOpenList();
static void SigOnDone(int kind, SigHttp &r)
{
    bool authed = kind == SOP_MOI || kind == SOP_MES || kind == SOP_RENOUVELER || kind == SOP_DEPOSER || kind == SOP_CAMARCHE || kind == SOP_RETIRER || kind == SOP_AVATAR;
    if (r.status == 426) { g_sigFatal = ST("er_mise-a-jour"); g_sigView = SV_ERROR; LaunchLog("signalement : lanceur trop ancien pour le site (426)"); return; }
    if (authed && r.status == 401) {   // jeton, membre, reconnexion : on repart de la connexion
        std::wstring why = SigErrText(r);
        LaunchLog("signalement : 401 %s, jeton efface", r.erreur.c_str());
        SigTokenClear(); g_sigView = SV_LOGIN; SigNote(why, SC_WARN);
        return;
    }
    switch (kind) {
    case SOP_TEXTES:
        if (r.status == 200 && SigTxApply(r.body, SigLang())) { SigTxSaveCache(r.body, SigLang()); LaunchLog("signalement : textes du site recus (%s)", g_sigTxLang.c_str()); }
        else if (!g_sigTxOk) { g_sigFatal = SigErrText(r); g_sigView = SV_ERROR; LaunchLog("signalement : textes du site indisponibles (statut %lu, erreur %lu)", r.status, r.err); return; }
        if (g_sigView == SV_LOADING || g_sigView == SV_ERROR) { if (g_sigLogged) { g_sigView = SV_LOADING; SigGo(SOP_MOI); } else g_sigView = SV_LOGIN; }
        break;
    case SOP_CODE:
        if (r.status == 200) {
            g_sigAppareil = r.j.str("appareil"); g_sigCode = Widen(r.j.str("code")); g_sigCodeUrl = Widen(r.j.str("url")); g_sigCodeExpire = (int)r.j.num("expire", 600); g_sigCodeT0 = GetTickCount(); g_sigCodeState = 0;
            if (g_sigCodeUrl.empty() || g_sigCodeUrl.compare(0, 8, L"https://") != 0) g_sigCodeUrl = kSigPage;
            g_sigView = SV_CODE; g_sigNote.clear();
            LaunchLog("signalement : code de connexion recu (%d s)", g_sigCodeExpire);
            SigJob j; j.kind = SOP_JETON; j.body = g_sigAppareil; SigStart(j);
        } else { g_sigView = SV_LOGIN; SigNote(r.status == 429 && r.retryAfter ? SigFmt(SigErrText(r), L"{n}", std::to_wstring(r.retryAfter)) : SigErrText(r), SC_WARN); }
        break;
    case SOP_JETON:
        g_sigAppareil.clear();
        if (r.status == 200) {
            g_sigJeton = r.j.str("jeton"); g_sigPseudo = Widen(r.j.str("pseudo")); g_sigExpire = r.j.str("expire"); g_sigRenouv = r.j.str("renouvelableJusqua");
            g_sigLogged = g_sigJeton.size() >= 20; SigTokenSave();
            LaunchLog("signalement : lanceur connecte (jeton enregistre, DPAPI)");
            SoftNotify(SigFmt(ST("dpConnecteEn"), L"{p}", g_sigPseudo), SC_OK);
            g_sigView = SV_LOADING; SigGo(SOP_MOI);
        } else if (r.status == 410 && r.erreur == "expire") { g_sigCodeState = 1; g_sigView = SV_CODE; }
        else if (r.status == 410) { g_sigView = SV_LOGIN; SigNote(ST("er_refuse"), SC_WARN); }
        else if (r.erreur == "annule") { g_sigView = SV_LOGIN; g_sigNote.clear(); }
        else { g_sigView = SV_LOGIN; SigNote(SigErrText(r), SC_WARN); }
        break;
    case SOP_MOI:
        if (r.status == 200) {
            g_sigDepotOuvert = r.j.flag("depotOuvert", true); g_sigAvatarFlag = r.j.flag("avatar");
            std::string p = r.j.str("pseudo"); if (!p.empty()) g_sigPseudo = Widen(p);
            if (const Jv *q = r.j.get("quotas")) { g_sigQuotaH = (int)q->num("heure", -1); g_sigQuotaJ = (int)q->num("jour", -1); }
            if (const Jv *l = r.j.get("limites")) { g_sigZipMax = (long long)l->num("zipMax", (double)g_sigZipMax); g_sigEnvoiMax = (long long)l->num("envoiMax", (double)g_sigEnvoiMax); g_sigMorceau = (long long)l->num("morceau", (double)g_sigMorceau); }
            if (const Jv *j = r.j.get("jeton")) { std::string e = j->str("expire"); if (!e.empty()) g_sigExpire = e; }
        }
        SigGo(SOP_MES);
        break;
    case SOP_MES:
        if (r.status == 200) { SigParseMes(r.j); if (g_sigView == SV_LOADING || g_sigView == SV_LIST || g_sigView == SV_DONE) g_sigView = SV_LIST; if (g_sigView == SV_DETAIL && g_sigSel >= (int)g_sigMes.size()) g_sigView = SV_LIST; }
        else { if (g_sigView == SV_LOADING) g_sigView = g_sigMesOk ? SV_LIST : SV_ERROR; if (!g_sigMesOk) g_sigFatal = SigErrText(r); else SigNote(SigErrText(r), SC_WARN); }
        if (g_sigView == SV_LIST && g_sigLogged && SigDaysLeft(g_sigExpire) < 2 && SigDaysLeft(g_sigRenouv) > 0) SigGo(SOP_RENOUVELER);   // (moins de 2 jours : on renouvelle, sans navigateur)
        else if (g_sigLogged && g_sigAvatarFlag && !g_sigAvatar && !g_sigCapture) SigGo(SOP_AVATAR);
        break;
    case SOP_RENOUVELER:
        if (r.status == 200) { g_sigExpire = r.j.str("expire"); g_sigRenouv = r.j.str("renouvelableJusqua"); SigTokenSave(); LaunchLog("signalement : jeton renouvele"); }
        if (g_sigLogged && g_sigAvatarFlag && !g_sigAvatar) SigGo(SOP_AVATAR);
        break;
    case SOP_AVATAR:
        if (r.status == 200 && r.body.size() > 8 && !r.body.compare(0, 4, "\x89PNG")) {
            if (g_sigAvatar) delete g_sigAvatar;
            g_sigAvatar = LoadPngMem(r.body.data(), r.body.size(), 64, 64);
            if (!LocalDir().empty()) { HANDLE f = CreateFileW((LocalDir() + L"signalement-avatar.png").c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL); if (f != INVALID_HANDLE_VALUE) { DWORD w = 0; WriteFile(f, r.body.data(), (DWORD)r.body.size(), &w, NULL); CloseHandle(f); } }
        } else g_sigAvatarFlag = false;   // (404 : pas d'avatar ; on n'insiste pas)
        break;
    case SOP_DECONNEXION:
        SigTokenClear(); g_sigView = SV_LOGIN; g_sigNote.clear();
        LaunchLog("signalement : lanceur deconnecte");
        break;
    case SOP_DEPOSER:
        if (r.status == 200) {
            g_sigDoneN = (int)r.j.num("n"); g_sigDoneAdd = g_sigJob.n > 0; g_sigView = SV_DONE; g_sigAddMode = 0; g_sigAddText.clear(); g_sigFocus = -1;
            if (g_sigQuotaH > 0) g_sigQuotaH--; if (g_sigQuotaJ > 0) g_sigQuotaJ--;
            LaunchLog("signalement : dossier #%d envoye (%s)", g_sigDoneN, g_sigDoneAdd ? "ajout" : "depot");
            g_sigMesOk = false;
        } else {
            std::wstring why = r.erreur == "zip-local" ? T(L"Zip des journaux impossible (erreur %lu)", L"Could not build the logs zip (error %lu)") : SigErrText(r);
            if (r.erreur == "zip-local") { wchar_t b[200]; swprintf_s(b, why.c_str(), r.err); why = b; }
            if (r.erreur == "annule") why = T(L"Envoi annulé", L"Sending cancelled");
            g_sigView = g_sigJob.n > 0 ? SV_DETAIL : SV_FORM;
            if (r.status == 400 && r.erreur == "champs") {   // le premier champ fautif, souligne
                std::string champ = r.j.str("champ");
                for (auto &f : g_sigForm) if (f.champ == champ || f.dChamp == champ) { f.err = f.erreur.empty() ? ST("erCorriger") : ST(f.erreur.c_str()); }
                why = ST("erCorriger");
            }
            SigNote(why, SC_BAD);
            LaunchLog("signalement : envoi refuse (statut %lu, %s)", r.status, r.erreur.c_str());
        }
        break;
    case SOP_CAMARCHE: case SOP_RETIRER:
        if (r.status == 200) {
            std::string etat = r.j.str("etat");
            for (auto &d : g_sigMes) if (d.n == g_sigJob.n) { d.etat = etat; d.peutRetirer = false; d.peutRouvrir = false; }
            SoftNotify(kind == SOP_CAMARCHE ? ST("dpCaMarche") : ST("dpRetirerDossier"), SC_OK);
            SigGo(SOP_MES);   // (l'historique a jour)
        } else SigNote(SigErrText(r), SC_BAD);
        break;
    }
}
static void SigTick()
{
    if (g_sigBusy && g_sigDone) {
        SigLock(); SigHttp r = g_sigRes; SigUnlock();
        InterlockedExchange(&g_sigBusy, 0); InterlockedExchange(&g_sigDone, 0);
        SigOnDone(g_sigJob.kind, r);
        InvalidateAll();
    }
    if (g_tab == TAB_BUG && SigActive() && (g_sigView == SV_CODE || g_sigBusy)) InvalidateAll();   // (compte a rebours, barre d'envoi)
}
static void SigOpen()   // bouton Signaler un bug (nouvelle interface, natif)
{
    if (g_tab != TAB_BUG) GoPage(TAB_BUG);
    g_scroll[TAB_BUG] = 0; g_sigFocus = -1;
    SigTokenLoad();
    SigZipCount();
    bool fresh = false;
    if (!g_sigTxOk || g_sigTxLang != Narrow(SigLang(), CP_UTF8)) { g_sigTxOk = false; SigTxLoadCache(SigLang(), &fresh); }
    if (g_sigView == SV_LOADING || g_sigView == SV_ERROR || !g_sigTxOk) {
        g_sigView = SV_LOADING; g_sigFatal.clear();
        if (g_sigTxOk && fresh) { if (g_sigLogged) SigGo(SOP_MOI); else g_sigView = SV_LOGIN; }
        else if (!g_sigCapture) SigGo(SOP_TEXTES);
    } else if (g_sigLogged && !g_sigMesOk && !g_sigBusy) SigGo(SOP_MES);
    if (g_sigTxOk && !fresh && !g_sigBusy && !g_sigCapture && g_sigView != SV_LOADING) SigGo(SOP_TEXTES);   // (cache de plus d'une heure : rafraichi derriere)
    SetStatus(K_NORMAL, T(L"Signaler un bug : tes dossiers et le formulaire, ici dans le lanceur · le zip des journaux est joint tout seul",
                          L"Report a bug: your reports and the form, right here in the launcher · the logs zip is attached for you"));
}
static void SigOpenList() { g_sigView = SV_LIST; g_sigSel = -1; g_sigAddMode = 0; g_sigFocus = -1; g_scroll[TAB_BUG] = 0; if (!g_sigMesOk && !g_sigBusy) SigGo(SOP_MES); }
static void SigNewForm()
{
    if (!g_sigDepotOuvert) { SigNote(ST("dpFerme"), SC_WARN); return; }
    SigZipCount(); SigFormBuild(); g_sigView = SV_FORM; g_sigFocus = -1; g_sigNote.clear(); g_scroll[TAB_BUG] = 0;
}
static void SigSendForm()
{
    int bad = SigValidate();
    if (bad >= 0) { SigNote(ST("erCorriger"), SC_BAD); g_scroll[TAB_BUG] = max(0.0f, g_sigForm[bad].r.Y - 180 + g_scroll[TAB_BUG]); return; }
    SigJob j; j.kind = SOP_DEPOSER; j.body = SigFormJson(); j.zip = SigZipPath(); j.files = g_sigCapture ? std::vector<std::wstring>() : ZipFiles(); j.zipWanted = true;
    g_sigView = SV_SEND; g_sigFocus = -1; g_sigNote.clear();
    SigStart(j);
}
static void SigSendAdd()
{
    if (g_sigSel < 0 || g_sigSel >= (int)g_sigMes.size()) return;
    std::wstring t = Trim(g_sigAddText);
    if (t.size() < 10) { SigNote(SigFmt(ST("erTropCourt"), L"{n}", L"10"), SC_BAD); return; }
    if (t.size() > 2000) { SigNote(SigFmt(ST("erTropLong"), L"{n}", L"2000"), SC_BAD); return; }
    SigJob j; j.kind = SOP_DEPOSER; j.n = g_sigMes[g_sigSel].n; j.body = "{\"n\":" + std::to_string(j.n) + ",\"texte\":" + SigJStr(t) + ",\"zips\":__ZIPS__}";
    j.zip = SigZipPath(); j.zipWanted = g_sigAddZip && g_sigZipFiles > 0; j.files = j.zipWanted && !g_sigCapture ? ZipFiles() : std::vector<std::wstring>();
    g_sigView = SV_SEND; g_sigFocus = -1; g_sigNote.clear();
    SigStart(j);
}
static void SigOpenPage()
{
    std::wstring u = g_sigCodeUrl.empty() ? kSigPage : g_sigCodeUrl;
    LaunchLog("signalement : page de connexion ouverte dans le navigateur");
    if (!g_sigTest && !g_sigCapture) ShellExecuteW(g_wnd, L"open", u.c_str(), NULL, NULL, SW_SHOWNORMAL);
}

// ---- saisie au clavier (champs du formulaire, texte d'ajout)
static SigField *SigFocusField() { int i = g_sigFocus >= 100 && g_sigFocus < 500 ? g_sigFocus - 100 : g_sigFocus; return i >= 0 && i < (int)g_sigForm.size() ? &g_sigForm[i] : NULL; }
static std::wstring *SigFocusText(int *maxLen, bool *multi)
{
    if (g_sigFocus == 500) { *maxLen = 2000; *multi = true; return &g_sigAddText; }
    SigField *f = SigFocusField(); if (!f) return NULL;
    if (g_sigFocus >= 100) { *maxLen = f->dMax ? f->dMax : 600; *multi = false; return &f->dVal; }
    *maxLen = f->type == "nombre" ? 2 : f->mx ? f->mx : 2000; *multi = f->type == "texte_long"; return &f->val;
}
static void SigFocusNext()
{
    int n = (int)g_sigForm.size();
    for (int k = 1; k <= n; k++) {
        int i = ((g_sigFocus >= 100 && g_sigFocus < 500 ? g_sigFocus - 100 : g_sigFocus < 0 ? -1 : g_sigFocus) + k + n) % n;
        const SigField &f = g_sigForm[i];
        if (f.type == "texte" || f.type == "texte_long" || f.type == "nombre") { g_sigFocus = i; return; }
    }
}
static bool SigChar(wchar_t ch)
{
    if (g_tab != TAB_BUG || !SigActive() || g_sigBusy) return false;
    if (g_sigFocus < 0) return false;
    int maxLen = 0; bool multi = false;
    std::wstring *t = SigFocusText(&maxLen, &multi);
    if (!t) { g_sigFocus = -1; return false; }
    if (ch == 27) g_sigFocus = -1;
    else if (ch == 8) { if (!t->empty()) t->pop_back(); }
    else if (ch == 127) t->clear();
    else if (ch == 9) { if (g_sigFocus < 500) SigFocusNext(); }
    else if (ch == 13) { if (multi) { if ((int)t->size() < maxLen) *t += L'\n'; } else if (g_sigFocus < 500) SigFocusNext(); }
    else if (ch == 22 && OpenClipboard(g_wnd)) {   // Ctrl+V
        HANDLE d = GetClipboardData(CF_UNICODETEXT);
        const wchar_t *s = d ? (const wchar_t *)GlobalLock(d) : NULL;
        if (s) { for (; *s && (int)t->size() < maxLen; s++) { if (*s == L'\r') continue; if (*s == L'\n' && !multi) continue; if (*s >= 32 || *s == L'\n') *t += *s; } GlobalUnlock(d); }
        CloseClipboard();
    }
    else if (ch >= 32) { SigField *f = SigFocusField(); if (f && g_sigFocus < 100 && f->type == "nombre" && !iswdigit(ch)) return true; if ((int)t->size() < maxLen) *t += ch; }
    if (SigField *f = SigFocusField()) f->err.clear();
    InvalidateAll();
    return true;
}

// ---- dessin
static float SigTextH(const std::wstring &t, float w, float px, int style = FontStyleRegular)
{
    if (t.empty()) return 0;
    UiFont f(L"Segoe UI", px, style); StringFormat sf; RectF box;
    MeasureStr(MeasureG(), t.c_str(), -1, &f, RectF(0, 0, w, 100000), &sf, &box);
    return box.Height;
}
static void SigText(Graphics &g, const std::wstring &t, RectF r, float px, int style, Color c)
{
    if (t.empty()) return;
    UiFont f(L"Segoe UI", px, style); StringFormat sf; SolidBrush b(c);
    DrawStr(g, t.c_str(), -1, &f, r, &sf, &b);
}
static float SigPara(Graphics &g, const std::wstring &t, float x, float y, float w, float px, Color c, int style = FontStyleRegular)   // rend la hauteur
{
    float h = SigTextH(t, w, px, style);
    SigText(g, t, RectF(x, y, w, h + 4), px, style, c);
    return h;
}
static float SigBtnW(const std::wstring &s, bool icon) { return MeasureW(MeasureG(), s, 13, FontStyleBold) + (icon ? 54 : 32); }
static void SigNoteDraw(Graphics &g, float x, float &y, float w)
{
    if (g_sigNote.empty()) return;
    Color c = g_sigNoteKind == SC_BAD ? g_soft.bad : g_sigNoteKind == SC_WARN ? g_soft.warn : g_soft.ok;
    float h = SigTextH(g_sigNote, w - 44, 13, FontStyleBold) + 20;
    SoftShape(g, RectF(x, y, w, h), 12, g_sigNoteKind == SC_BAD ? g_soft.badBg : g_sigNoteKind == SC_WARN ? g_soft.warnBg : g_soft.okBg);
    Icon(g, g_sigNoteKind == SC_OK ? IC_CHECK : IC_ALERT, x + 20, y + h / 2, 18, c, 2.0f);
    SigText(g, g_sigNote, RectF(x + 36, y + 10, w - 44, h - 16), 13, FontStyleBold, c);
    y += h + 14;
}
static std::wstring SigCodeSpaced() { std::wstring s; for (wchar_t c : g_sigCode) { if (!s.empty()) s += L' '; s += c; } return s; }
// zone chaude seulement si visible dans la carte (le contenu defile)
static void SigHot(RectF r, int id, const RectF &clip) { if (r.Y + r.Height > clip.Y && r.Y < clip.Y + clip.Height) HotZone(r, id); }

static float SigDrawLogin(Graphics &g, float x, float y, float w, const RectF &clip)
{
    float y0 = y;
    SigNoteDraw(g, x, y, w);
    Icon(g, IC_DISCORD, x + 28, y + 26, 40, g_soft.acc, 2.0f);
    y += SigPara(g, ST("dpTitre"), x + 64, y, w - 64, 20, g_soft.ink, FontStyleBold) + 6;
    y += SigPara(g, ST("dpIntro"), x + 64, y, w - 64, 14, g_soft.muted) + 18;
    std::wstring b = ST("dpConnexion"); float bw = SigBtnW(b, true);
    SoftButton(g, RectF(x, y, bw, 46), b, UI_SIG_LOGIN, true, IC_DISCORD); SigHot(RectF(x, y, bw, 46), UI_SIG_LOGIN, clip);
    std::wstring nb = T(L"Ouvrir le site dans le navigateur", L"Open the site in the browser"); float nw = SigBtnW(nb, true);
    SoftButton(g, RectF(x + bw + 12, y, nw, 46), nb, UI_SIG_BROWSER, false, IC_GLOBE); SigHot(RectF(x + bw + 12, y, nw, 46), UI_SIG_BROWSER, clip);
    y += 46 + 22;
    y += SigPara(g, ST("dpConnexionNote"), x, y, w, 12.5f, g_soft.muted) + 10;
    y += SigPara(g, ST("dpConfidentialite"), x, y, w, 12.5f, g_soft.muted) + 10;
    y += SigPara(g, ST("lnDroits"), x, y, w, 12.5f, g_soft.muted) + 10;
    return y - y0;
}
static float SigDrawCode(Graphics &g, float x, float y, float w, const RectF &clip)
{
    float y0 = y;
    y += SigPara(g, ST("lnTitre"), x, y, w, 20, g_soft.ink, FontStyleBold) + 8;
    y += SigPara(g, T(L"1. Ouvre la page du site. 2. Connecte-toi avec Discord. 3. Tape ce code, puis « Autoriser ce lanceur ».", L"1. Open the site's page. 2. Sign in with Discord. 3. Type this code, then \"Allow this launcher\"."), x, y, w, 14, g_soft.muted) + 18;
    RectF box(x, y, min(w, 420.0f), 96);
    SoftShape(g, box, 16, g_soft.bgIn);
    { GraphicsState st = g.Save(); GraphicsPath p; RoundRect(p, box, 16); g.SetClip(&p); SoftInset(g, box, 16); g.Restore(st); }
    Text(g, SigCodeSpaced(), RectF(box.X, box.Y + 8, box.Width, 60), 38, FontStyleBold, g_sigCodeState ? g_soft.faint : g_soft.ink);
    int left = max(0, g_sigCodeExpire - (int)((GetTickCount() - g_sigCodeT0) / 1000));
    wchar_t st[160];
    if (g_sigCodeState == 1) swprintf_s(st, L"%s", ST("er_expire").c_str());
    else swprintf_s(st, T(L"En attente du site… code valable encore %d:%02d", L"Waiting for the site… code still valid for %d:%02d"), left / 60, left % 60);
    Text(g, st, RectF(box.X, box.Y + 66, box.Width, 22), 12, FontStyleRegular, g_sigCodeState ? g_soft.warn : g_soft.muted);
    y += box.Height + 18;
    float bx = x;
    if (g_sigCodeState == 1) {
        std::wstring nb = T(L"Nouveau code", L"New code"); float nw = SigBtnW(nb, true);
        SoftButton(g, RectF(bx, y, nw, 46), nb, UI_SIG_NEWCODE, true, IC_UNDO); SigHot(RectF(bx, y, nw, 46), UI_SIG_NEWCODE, clip); bx += nw + 12;
    } else {
        std::wstring ob = T(L"Ouvrir la page", L"Open the page"); float ow = SigBtnW(ob, true);
        SoftButton(g, RectF(bx, y, ow, 46), ob, UI_SIG_OPENPAGE, true, IC_GLOBE); SigHot(RectF(bx, y, ow, 46), UI_SIG_OPENPAGE, clip); bx += ow + 12;
    }
    std::wstring cb = T(L"Annuler", L"Cancel"); float cw = SigBtnW(cb, false);
    SoftButton(g, RectF(bx, y, cw, 46), cb, UI_SIG_CANCEL, false); SigHot(RectF(bx, y, cw, 46), UI_SIG_CANCEL, clip);
    y += 46 + 20;
    y += SigPara(g, ST("lnAvertir"), x, y, w, 12.5f, g_soft.warn) + 8;
    y += SigPara(g, T(L"Le code n'est jamais mis dans l'adresse : c'est toi qui le tapes sur la page.", L"The code is never put in the address: you type it on the page yourself."), x, y, w, 12.5f, g_soft.muted) + 8;
    return y - y0;
}
static float SigDrawList(Graphics &g, float x, float y, float w, const RectF &clip)
{
    float y0 = y;
    SigNoteDraw(g, x, y, w);
    std::wstring who = SigFmt(ST("dpConnecteEn"), L"{p}", g_sigPseudo);
    Text(g, ST("dpMes"), RectF(x, y, w / 2, 28), 18, FontStyleBold, g_soft.ink, StringAlignmentNear);
    float aw = g_sigAvatar ? 36.0f : 0.0f;
    Text(g, who, RectF(x + w / 2, y + 4, w / 2 - aw, 22), 12.5f, FontStyleRegular, g_soft.muted, StringAlignmentFar);
    if (g_sigAvatar) {   // (rond de 28, a droite du nom)
        RectF ar(x + w - 28, y - 1, 28, 28);
        GraphicsState st = g.Save(); GraphicsPath p; p.AddEllipse(ar); g.SetClip(&p, CombineModeIntersect);
        g.DrawImage(g_sigAvatar, ar.X, ar.Y, ar.Width, ar.Height); g.Restore(st);
        Pen rim(g_soft.line, 1.0f); g.DrawEllipse(&rim, ar);
    }
    y += 32;
    if (g_sigQuotaH >= 0) { wchar_t q[160]; swprintf_s(q, T(L"Envois restants : %d cette heure · %d aujourd'hui", L"Sends left: %d this hour · %d today"), g_sigQuotaH, g_sigQuotaJ); y += SigPara(g, q, x, y, w, 12, g_soft.faint) + 8; }
    if (!g_sigDepotOuvert) y += SigPara(g, ST("dpFerme") + L" " + ST("dpFermeSous"), x, y, w, 13, g_soft.warn, FontStyleBold) + 10;
    if (!g_sigMesOk) { SoftSkeleton(g, RectF(x, y, w, 56)); SoftSkeleton(g, RectF(x, y + 66, w, 56)); return y + 130 - y0; }
    if (g_sigMes.empty()) { y += SigPara(g, ST("dpAucun"), x, y + 10, w, 14, g_soft.muted) + 20; return y - y0; }
    for (int i = 0; i < (int)g_sigMes.size() && i < 50; i++) {
        const SigDossier &d = g_sigMes[i];
        RectF r(x, y, w, 64);
        bool hot = g_uiHot == UI_SIG_ROW + i;
        if (hot) SoftShape(g, r, 12, g_soft.accSoft);
        wchar_t num[24]; swprintf_s(num, L"#%d", d.n);
        Text(g, num, RectF(r.X + 12, r.Y + 10, 64, 22), 13, FontStyleBold, g_soft.acc, StringAlignmentNear);
        Text(g, d.titre.empty() ? ST("fcTitreCache") : d.titre, RectF(r.X + 70, r.Y + 10, w - 300, 22), 14, FontStyleBold, g_soft.ink, StringAlignmentNear);
        std::wstring sub = (d.jeu == "msc" ? L"My Summer Car" : L"My Winter Car") + std::wstring(L" · ") + SigDate(d.maj ? d.maj : d.cree);
        Text(g, sub, RectF(r.X + 70, r.Y + 36, w - 300, 18), 12, FontStyleRegular, g_soft.muted, StringAlignmentNear);
        std::wstring et = SigEtat(d); float cw = SoftChipW(et);
        SoftChip(g, r.X + w - cw - 36, r.Y + 20, et, SigEtatKind(d.etat));
        Icon(g, IC_CHEVR, r.X + w - 16, r.Y + 32, 16, g_soft.faint, 2.0f);
        Pen ln(g_soft.line, 1.0f); g.DrawLine(&ln, r.X + 8, r.Y + r.Height, r.X + w - 8, r.Y + r.Height);
        SigHot(r, UI_SIG_ROW + i, clip);
        y += 66;
    }
    return y - y0;
}
static float SigDrawTextArea(Graphics &g, RectF &r, float x, float y, float w, std::wstring &text, const std::wstring &placeholder, bool focus, int id, const RectF &clip, float minH = 96)
{
    std::wstring shown = text.empty() && !focus ? placeholder : text + (focus && (GetTickCount() / 500) % 2 == 0 ? L"|" : L"");
    float th = SigTextH(shown.empty() ? L"X" : shown, w - 32, 14), h = max(minH, th + 28);
    r = RectF(x, y, w, h);
    SoftFieldBox(g, r, focus);
    SigText(g, shown, RectF(x + 16, y + 12, w - 32, h - 20), 14, text.empty() && !focus ? FontStyleRegular : FontStyleBold, text.empty() && !focus ? g_soft.faint : g_soft.ink);
    SigHot(r, id, clip);
    if (focus) InvalidateAll();
    return h;
}
static float SigDrawDetail(Graphics &g, float x, float y, float w, const RectF &clip)
{
    if (g_sigSel < 0 || g_sigSel >= (int)g_sigMes.size()) return 0;
    const SigDossier &d = g_sigMes[g_sigSel];
    float y0 = y;
    SigNoteDraw(g, x, y, w);
    wchar_t num[24]; swprintf_s(num, L"#%d", d.n);
    Text(g, num, RectF(x, y, 80, 30), 18, FontStyleBold, g_soft.acc, StringAlignmentNear);
    y += SigPara(g, d.titre.empty() ? ST("fcTitreCache") : d.titre, x + 64, y, w - 64, 18, g_soft.ink, FontStyleBold) + 10;
    std::wstring et = SigEtat(d); float cw = SoftChip(g, x, y, et, SigEtatKind(d.etat));
    std::wstring sub = (d.jeu == "msc" ? L"My Summer Car" : L"My Winter Car") + std::wstring(L" · ") + SigDate(d.cree);
    Text(g, sub, RectF(x + cw + 12, y + 3, w - cw - 12, 20), 12.5f, FontStyleRegular, g_soft.muted, StringAlignmentNear);
    y += 34;
    y += SigPara(g, SigAide(d), x, y, w, 13.5f, g_soft.ink) + 10;
    if (d.dmBloque) y += SigPara(g, ST("dpDmBloque"), x, y, w, 12.5f, g_soft.warn, FontStyleBold) + 10;
    if (d.reouvertures > 0) y += SigPara(g, SigFmt(ST("dpRouvertures"), L"{n}", std::to_wstring(d.reouvertures)), x, y, w, 12.5f, g_soft.muted) + 6;
    // boutons selon l'etat
    float bx = x; y += 6;
    auto btn = [&](const std::wstring &s, int id, bool primary, int icon) { float bw = SigBtnW(s, icon >= 0); if (bx + bw > x + w) { bx = x; y += 54; } SoftButton(g, RectF(bx, y, bw, 44), s, id, primary, icon); SigHot(RectF(bx, y, bw, 44), id, clip); bx += bw + 10; };
    bool any = false;
    if (!g_sigAddMode) {
        if (d.etat == "corrige") { btn(ST("dpCaMarche"), UI_SIG_CAMARCHE, true, IC_CHECK); any = true; }
        if (d.peutRouvrir) { btn(ST("dpToujoursLa"), UI_SIG_TOUJOURS, d.etat != "corrige", IC_UNDO); any = true; }
        if (d.etat == "infos") { btn(ST("dpCompleter"), UI_SIG_COMPLETER, true, IC_PLUS); any = true; }
        if (d.peutRetirer) { btn(g_sigConfirmT && GetTickCount() - g_sigConfirmT < 5000 ? T(L"Confirmer ?", L"Confirm?") : ST("dpRetirerDossier"), UI_SIG_RETIRER, false, IC_TRASH); any = true; }
    }
    if (any) y += 44 + 18;
    if (g_sigAddMode) {
        y += SigPara(g, g_sigAddMode == 1 ? ST("dpCompleter") : ST("dpToujoursLa"), x, y, w, 15, g_soft.ink, FontStyleBold) + 8;
        RectF ar;
        y += SigDrawTextArea(g, ar, x, y, w, g_sigAddText, ST("dpTexteAjout"), g_sigFocus == 500, UI_SIG_ADDTEXT, clip, 110) + 12;
        RectF zr(x, y, w, 40);
        SoftToggle(g, RectF(x, y + 4, 52, 30), g_sigAddZip && g_sigZipFiles > 0, UI_SIG_ADDZIP, g_sigZipFiles > 0); SigHot(RectF(x, y, 300, 40), UI_SIG_ADDZIP, clip);
        wchar_t zt[200];
        if (g_sigZipFiles > 0) swprintf_s(zt, T(L"%s · le zip des journaux (%d fichiers)", L"%s · the logs zip (%d files)"), ST("dpAjouterZip").c_str(), g_sigZipFiles);
        else swprintf_s(zt, L"%s", T(L"Aucun journal pour l'instant : lance le jeu avec MWCoop d'abord", L"No logs yet: start the game with MWCoop first"));
        Text(g, zt, RectF(x + 64, y + 8, w - 64, 22), 13, FontStyleBold, g_sigZipFiles > 0 ? g_soft.ink : g_soft.muted, StringAlignmentNear);
        y += 52;
        bx = x;
        btn(ST("dpAjouter"), UI_SIG_ADDSEND, true, IC_CHECK);
        btn(T(L"Annuler", L"Cancel"), UI_SIG_ADDCANCEL, false, -1);
        y += 44 + 18;
    }
    // historique
    if (!d.hist.empty()) {
        y += SigPara(g, ST("fcHistorique"), x, y, w, 13, g_soft.muted, FontStyleBold) + 8;
        for (const SigEvt &e : d.hist) {
            Text(g, SigDate(e.ts), RectF(x, y, 150, 20), 12, FontStyleRegular, g_soft.faint, StringAlignmentNear);
            float h = SigPara(g, SigEvtText(e), x + 150, y, w - 150, 13, g_soft.ink);
            y += max(h, 20.0f) + 8;
        }
    }
    return y - y0;
}
static float SigDrawForm(Graphics &g, float x, float y, float w, const RectF &clip)
{
    float y0 = y;
    SigNoteDraw(g, x, y, w);
    y += SigPara(g, ST("dpNouveau"), x, y, w, 18, g_soft.ink, FontStyleBold) + 4;
    y += SigPara(g, ST("dpIntro"), x, y, w, 12.5f, g_soft.muted) + 16;
    for (int i = 0; i < (int)g_sigForm.size(); i++) {
        SigField &f = g_sigForm[i];
        int id0 = UI_SIG_FIELD + i * 10;
        bool bad = !f.err.empty();
        std::wstring lab = ST(f.libelle.c_str());
        if (f.type == "case" || f.type == "zips") {
            RectF r(x, y, w, 44);
            bool on = f.type == "zips" ? f.on && g_sigZipFiles > 0 : f.on;
            SoftToggle(g, RectF(x, y + 7, 52, 30), on, id0 + 9, f.type != "zips" || g_sigZipFiles > 0); SigHot(r, id0 + 9, clip);
            std::wstring t = lab;
            if (f.type == "zips") { wchar_t b[200]; if (g_sigZipFiles > 0) swprintf_s(b, T(L"%s · joint tout seul (%d fichiers)", L"%s · attached for you (%d files)"), lab.c_str(), g_sigZipFiles); else swprintf_s(b, L"%s", T(L"Aucun journal pour l'instant : lance le jeu avec MWCoop d'abord", L"No logs yet: start the game with MWCoop first")); t = b; }
            float th = SigTextH(t, w - 70, 13.5f, FontStyleBold);
            SigText(g, t, RectF(x + 64, y + (th > 24 ? 4 : 12), w - 70, th + 4), 13.5f, FontStyleBold, bad ? g_soft.bad : g_soft.ink);
            y += max(44.0f, th + 12);
            if (!f.aide.empty()) y += SigPara(g, ST(f.aide.c_str()), x + 64, y, w - 70, 12, g_soft.muted) + 4;
            f.r = r;
        } else {
            y += SigPara(g, lab, x, y, w, 13.5f, bad ? g_soft.bad : g_soft.ink, FontStyleBold) + 4;
            if (!f.aide.empty()) y += SigPara(g, ST(f.aide.c_str()), x, y, w, 12, g_soft.muted) + 6;
            if (f.type == "choix") {
                float bx = x; float rowY = y;
                for (int k = 0; k < (int)f.opts.size() && k < 7; k++) {
                    std::wstring o = f.opts[k].second; float bw = SigBtnW(o, false);
                    if (bx + bw > x + w) { bx = x; y += 46; }
                    SoftButton(g, RectF(bx, y, bw, 38), o, id0 + 1 + k, f.sel == k, -1, true, 19); SigHot(RectF(bx, y, bw, 38), id0 + 1 + k, clip);
                    bx += bw + 8;
                }
                f.r = RectF(x, rowY, w, y + 38 - rowY);
                y += 38 + 8;
                if (SigDetailOn(f)) {
                    bool fo = g_sigFocus == 100 + i;
                    RectF dr(x, y, min(w, 620.0f), 44);
                    std::wstring ph = !f.dIndice.empty() ? ST(f.dIndice.c_str()) : Widen(f.dExemple);
                    SoftField(g, dr, f.dVal + (fo ? L"" : L""), ph, fo); SigHot(dr, id0 + 8, clip);
                    f.dr = dr; y += 44 + 8;
                }
            } else if (f.type == "texte_long") {
                y += SigDrawTextArea(g, f.r, x, y, w, f.val, Widen(f.exemple), g_sigFocus == i, id0, clip) + 8;
            } else {
                RectF r(x, y, f.type == "nombre" ? 140.0f : min(w, 620.0f), 44);
                SoftField(g, r, f.val, Widen(f.exemple), g_sigFocus == i); SigHot(r, id0, clip);
                f.r = r; y += 44 + 8;
            }
        }
        if (bad) y += SigPara(g, f.err, x, y, w, 12.5f, g_soft.bad, FontStyleBold) + 4;
        y += 12;
    }
    y += 6;
    std::wstring sb = ST("dpEnvoyer"); float sw = SigBtnW(sb, true);
    SoftButton(g, RectF(x, y, sw, 46), sb, UI_SIG_SEND, true, IC_CHECK); SigHot(RectF(x, y, sw, 46), UI_SIG_SEND, clip);
    std::wstring cb = T(L"Annuler", L"Cancel"); float cw = SigBtnW(cb, false);
    SoftButton(g, RectF(x + sw + 12, y, cw, 46), cb, UI_SIG_FORMCANCEL, false); SigHot(RectF(x + sw + 12, y, cw, 46), UI_SIG_FORMCANCEL, clip);
    y += 46 + 10;
    return y - y0;
}
static float SigDrawSend(Graphics &g, float x, float y, float w, const RectF &)
{
    float y0 = y;
    SigLock(); std::wstring p = g_sigProg; float fr = g_sigFrac; SigUnlock();
    y += SigPara(g, ST("dpEnvoi"), x, y, w, 18, g_soft.ink, FontStyleBold) + 12;
    SoftProgress(g, RectF(x, y, min(w, 520.0f), 14), fr); y += 26;
    y += SigPara(g, p.empty() ? ST("dpEnvoi") : p, x, y, w, 13.5f, g_soft.muted) + 8;
    InvalidateAll();
    return y - y0;
}
static float SigDrawDone(Graphics &g, float x, float y, float w, const RectF &clip)
{
    float y0 = y;
    Icon(g, IC_CHECK, x + 22, y + 22, 36, g_soft.ok, 2.4f);
    y += SigPara(g, SigFmt(ST("dpMerci"), L"{n}", std::to_wstring(g_sigDoneN)), x + 56, y + 6, w - 56, 16, g_soft.ink, FontStyleBold) + 24;
    std::wstring lb = ST("dpMes"); float lw = SigBtnW(lb, true);
    SoftButton(g, RectF(x, y, lw, 46), lb, UI_SIG_TOLIST, true, IC_FILE); SigHot(RectF(x, y, lw, 46), UI_SIG_TOLIST, clip);
    y += 46 + 10;
    return y - y0;
}
static float SigDrawError(Graphics &g, float x, float y, float w, const RectF &clip)
{
    float y0 = y;
    Icon(g, IC_ALERT, x + 20, y + 20, 32, g_soft.warn, 2.2f);
    y += SigPara(g, g_sigFatal, x + 52, y + 4, w - 52, 14, g_soft.ink, FontStyleBold) + 20;
    float bx = x;
    auto btn = [&](const std::wstring &s, int id, bool primary, int icon) { float bw = SigBtnW(s, icon >= 0); SoftButton(g, RectF(bx, y, bw, 46), s, id, primary, icon); SigHot(RectF(bx, y, bw, 46), id, clip); bx += bw + 10; };
    btn(T(L"Réessayer", L"Retry"), UI_SIG_RETRY, true, IC_UNDO);
    btn(T(L"Ouvrir le site dans le navigateur", L"Open the site in the browser"), UI_SIG_BROWSER, false, IC_GLOBE);
    if (BugPanelPossible()) btn(T(L"Le formulaire du site (panneau)", L"The site's form (panel)"), UI_SIG_PANEL, false, IC_BUG);
    y += 46 + 10;
    return y - y0;
}
static void DrawSigPage(Graphics &g)
{
    // boutons en haut a droite (zone des pages : y 30..74), selon la vue
    float right = g_pageR.X + g_pageR.Width, by = 30;
    auto top = [&](const std::wstring &s, int id, bool primary, int icon, bool enabled = true) { float bw = SigBtnW(s, icon >= 0); right -= bw; SoftButton(g, RectF(right, by, bw, 44), s, id, primary, icon, enabled); HotZone(RectF(right, by, bw, 44), id); right -= 10; };
    if (g_sigView == SV_LIST) { top(ST("dpNouveau"), UI_SIG_NEW, true, IC_PLUS, g_sigDepotOuvert && !g_sigBusy); top(ST("dpDeconnexion"), UI_SIG_LOGOUT, false, -1, !g_sigBusy); top(L"", UI_SIG_REFRESH, false, IC_UNDO, !g_sigBusy); }
    else if (g_sigView == SV_DETAIL || g_sigView == SV_FORM) top(ST("dpMes"), UI_SIG_BACK, false, IC_CHEVL, !g_sigBusy);
    else if (g_sigView == SV_DONE) top(ST("dpMes"), UI_SIG_TOLIST, true, IC_FILE);
    if (g_sigView != SV_ERROR && g_sigView != SV_LOGIN) top(L"", UI_SIG_BROWSER, false, IC_GLOBE);
    // la carte, et son contenu qui defile
    RectF card(g_pageR.X, 88, g_pageR.Width, 656);
    SoftCard(g, card, kSoftRCard);
    RectF clip(card.X + 2, card.Y + 2, card.Width - 4, card.Height - 4);
    float pad = 28, x = card.X + pad, w = card.Width - 2 * pad;
    if (g_sigContentH > 0 && g_scroll[TAB_BUG] > SigMaxScroll()) g_scroll[TAB_BUG] = SigMaxScroll();   // (contenu plus court qu'avant : on remonte)
    float sc = g_scroll[TAB_BUG];
    g_sigViewH = card.Height - 2 * pad;
    GraphicsState st = g.Save();
    g.SetClip(clip, CombineModeIntersect);
    float y = card.Y + pad - sc, h = 0;
    switch (g_sigView) {
    case SV_LOADING: SoftSkeleton(g, RectF(x, y, w * 0.6f, 28)); SoftSkeleton(g, RectF(x, y + 40, w, 120)); h = 180; break;
    case SV_LOGIN: h = SigDrawLogin(g, x, y, w, clip); break;
    case SV_CODE: h = SigDrawCode(g, x, y, w, clip); break;
    case SV_LIST: h = SigDrawList(g, x, y, w, clip); break;
    case SV_DETAIL: h = SigDrawDetail(g, x, y, w, clip); break;
    case SV_FORM: h = SigDrawForm(g, x, y, w, clip); break;
    case SV_SEND: h = SigDrawSend(g, x, y, w, clip); break;
    case SV_DONE: h = SigDrawDone(g, x, y, w, clip); break;
    case SV_ERROR: h = SigDrawError(g, x, y, w, clip); break;
    }
    g.Restore(st);
    g_sigContentH = h + 2 * pad;
    if (g_sigBusy && g_sigView != SV_SEND && g_sigView != SV_LOADING) {   // (une operation en cours : un fil discret en bas de la carte)
        SigLock(); float fr = g_sigFrac; SigUnlock();
        SoftProgress(g, RectF(card.X + 24, card.Y + card.Height - 12, card.Width - 48, 6), fr); InvalidateAll();
    }
}
static float SigMaxScroll() { return max(0.0f, g_sigContentH - 656); }
static bool SigClick(int id)
{
    if (id < UI_SIG || id >= UI_SIG + 1000) return false;
    if (id == UI_SIG_BROWSER) {
        std::wstring url = std::wstring(kBugSiteUrl) + L"?lang=" + LangCode();
        LaunchLog("signalement : site ouvert dans le navigateur (page native)");
        if (!g_sigTest && !g_sigCapture) ShellExecuteW(g_wnd, L"open", url.c_str(), NULL, NULL, SW_SHOWNORMAL);
        return true;
    }
    if (g_sigBusy && id != UI_SIG_CANCEL) return true;
    g_sigNote.clear();
    switch (id) {
    case UI_SIG_LOGIN: case UI_SIG_NEWCODE: g_sigCodeState = 0; if (!g_sigCapture) SigGo(SOP_CODE); return true;
    case UI_SIG_OPENPAGE: SigOpenPage(); return true;
    case UI_SIG_CANCEL: InterlockedExchange(&g_sigCancel, 1); g_sigAppareil.clear(); g_sigView = SV_LOGIN; LaunchLog("signalement : connexion annulee"); return true;
    case UI_SIG_NEW: SigNewForm(); return true;
    case UI_SIG_LOGOUT: g_sigView = SV_LOADING; if (!g_sigCapture) SigGo(SOP_DECONNEXION); return true;
    case UI_SIG_REFRESH: g_sigMesOk = false; if (!g_sigCapture) SigGo(SOP_MES); return true;
    case UI_SIG_BACK: case UI_SIG_TOLIST: SigOpenList(); return true;
    case UI_SIG_RETRY: g_sigView = SV_LOADING; g_sigFatal.clear(); SigGo(g_sigTxOk ? (g_sigLogged ? SOP_MOI : SOP_TEXTES) : SOP_TEXTES); if (g_sigTxOk && !g_sigLogged) g_sigView = SV_LOGIN; return true;
    case UI_SIG_PANEL: WritePrivateProfileStringW(L"Lanceur", L"SignalerNatif", L"0", g_iniLauncher.c_str()); LaunchLog("signalement : repli sur le panneau du site (SignalerNatif=0)"); BugOpen(); return true;
    case UI_SIG_CAMARCHE: if (g_sigSel >= 0) SigGo(SOP_CAMARCHE, g_sigMes[g_sigSel].n); return true;
    case UI_SIG_TOUJOURS: g_sigAddMode = 2; g_sigAddText.clear(); g_sigFocus = 500; SigZipCount(); return true;
    case UI_SIG_COMPLETER: g_sigAddMode = 1; g_sigAddText.clear(); g_sigFocus = 500; SigZipCount(); return true;
    case UI_SIG_RETIRER:
        if (g_sigConfirmT && GetTickCount() - g_sigConfirmT < 5000) { g_sigConfirmT = 0; if (g_sigSel >= 0) SigGo(SOP_RETIRER, g_sigMes[g_sigSel].n); }
        else { g_sigConfirmT = GetTickCount(); if (g_sigSel >= 0) SigNote(SigFmt(ST("dpConfRetirer"), L"{n}", std::to_wstring(g_sigMes[g_sigSel].n)), SC_WARN); }
        return true;
    case UI_SIG_ADDSEND: SigSendAdd(); return true;
    case UI_SIG_ADDCANCEL: g_sigAddMode = 0; g_sigAddText.clear(); g_sigFocus = -1; return true;
    case UI_SIG_ADDZIP: g_sigAddZip = !g_sigAddZip; return true;
    case UI_SIG_ADDTEXT: g_sigFocus = 500; return true;
    case UI_SIG_SEND: SigSendForm(); return true;
    case UI_SIG_FORMCANCEL: g_sigFocus = -1; SigOpenList(); return true;
    }
    if (id >= UI_SIG_ROW && id < UI_SIG_ROW + 50) { int i = id - UI_SIG_ROW; if (i < (int)g_sigMes.size()) { g_sigSel = i; g_sigView = SV_DETAIL; g_sigAddMode = 0; g_sigFocus = -1; g_sigConfirmT = 0; g_scroll[TAB_BUG] = 0; } return true; }
    if (id >= UI_SIG_FIELD && id < UI_SIG_FIELD + 10 * (int)g_sigForm.size()) {
        int i = (id - UI_SIG_FIELD) / 10, k = (id - UI_SIG_FIELD) % 10;
        SigField &f = g_sigForm[i]; f.err.clear();
        if (k == 0) g_sigFocus = i;
        else if (k >= 1 && k <= 7) { f.sel = k - 1; g_sigFocus = SigDetailOn(f) ? 100 + i : -1; }
        else if (k == 8) g_sigFocus = 100 + i;
        else if (k == 9) { if (f.type != "zips" || g_sigZipFiles > 0) f.on = !f.on; }
        return true;
    }
    return true;
}

// ---- captures : donnees factices, sans reseau (MWCOOP_SIGNAL_TEXTES=<json des textes>)
static void SigCaptureState(const std::wstring &st)
{
    g_sigCapture = true; g_tab = TAB_BUG;
    SigTxLoadCache(SigLang(), NULL);
    g_sigLogged = true; g_sigPseudo = L"Teppo"; g_sigQuotaH = 3; g_sigQuotaJ = 9; g_sigZipFiles = 7;
    if (!g_sigAvatar) {   // (faux avatar : un rond Discord avec l'initiale)
        g_sigAvatar = new Bitmap(64, 64, PixelFormat32bppARGB); Graphics gb(g_sigAvatar); gb.SetSmoothingMode(SmoothingModeAntiAlias); gb.SetTextRenderingHint(TextRenderingHintAntiAlias);
        SolidBrush br(Color(255, 88, 101, 242)); gb.FillEllipse(&br, 0, 0, 64, 64);
        UiFont f(L"Segoe UI", 30, FontStyleBold); SolidBrush wb(Color(255, 255, 255, 255)); StringFormat sf; sf.SetAlignment(StringAlignmentCenter); sf.SetLineAlignment(StringAlignmentCenter);
        gb.DrawString(L"T", 1, &f, RectF(0, 0, 64, 64), &sf, &wb);
    }
    auto mk = [](int n, const wchar_t *titre, const char *jeu, const char *etat, const char *confie, const char *ver, bool rouvrir, bool retirer) {
        SigDossier d; d.n = n; d.titre = titre; d.jeu = jeu; d.etat = etat; d.confie = confie; d.versionCorrigee = ver; d.cree = 1761580000000LL; d.maj = 1761590000000LL; d.peutRouvrir = rouvrir; d.peutRetirer = retirer;
        d.hist.push_back({ 1761580000000LL, "depose", L"" }); d.hist.push_back({ 1761583000000LL, "traite", L"" });
        if (*confie) d.hist.push_back({ 1761586000000LL, "confie", Widen(confie) });
        if (*ver) d.hist.push_back({ 1761590000000LL, "corrige", Widen(ver) });
        return d;
    };
    g_sigMes = { mk(42, L"Roue qui tombe chez l'invité", "msc", "corrige", "MSC", "msc-1.15.13", true, false), mk(41, L"Téléphone muet après le respawn", "mwc", "confie", "MWC", "", false, false),
                 mk(40, L"Plantage à l'ouverture du magasin", "mwc", "infos", "", "", false, false), mk(39, L"", "msc", "a_trier", "", "", false, true), mk(37, L"Pas un bug : le moteur cale au froid", "msc", "clos_motif", "", "", false, false) };
    g_sigMes[4].motif = "pas-un-bug";
    g_sigMesOk = true;
    if (st == L"signaler-natif-connexion") { g_sigLogged = false; g_sigView = SV_LOGIN; }
    else if (st == L"signaler-natif-code") { g_sigView = SV_CODE; g_sigCode = L"K7QF-M2XP"; g_sigCodeT0 = GetTickCount() - 73000; g_sigCodeExpire = 600; }
    else if (st == L"signaler-natif-liste") g_sigView = SV_LIST;
    else if (st == L"signaler-natif-dossier") { g_sigView = SV_DETAIL; g_sigSel = 0; }
    else if (st == L"signaler-natif-dossier-ajout") { g_sigView = SV_DETAIL; g_sigSel = 2; g_sigAddMode = 1; g_sigAddText = L"Ça arrive aussi avec la Ferndale, dès que l'invité ouvre le coffre.\nVersion mwc-1.15.10, salon Steam à 3."; }
    else if (st == L"signaler-natif-formulaire") { SigFormBuild(); g_sigView = SV_FORM; for (auto &f : g_sigForm) { if (f.champ == "titre") f.val = L"Roue qui tombe chez l'invité"; if (f.champ == "type") f.sel = 2; if (f.champ == "probleme") f.val = L"Après un saut, la roue avant gauche tombe chez l'invité mais pas chez l'hôte."; if (f.champ == "role") f.sel = 2; if (f.champ == "joueurs") f.val = L"2"; } g_sigForm[1].err.clear(); }
    else if (st == L"signaler-natif-formulaire-bas") { SigFormBuild(); g_sigView = SV_FORM; for (auto &f : g_sigForm) if (f.champ == "mods_choix") f.sel = 1; DrawSigPage(MeasureG()); g_scroll[TAB_BUG] = SigMaxScroll(); }   // (le bas du formulaire : detail des mods, zip, anonyme, Envoyer)
    else if (st == L"signaler-natif-formulaire-erreurs") { SigFormBuild(); g_sigView = SV_FORM; SigValidate(); g_sigNote = ST("erCorriger"); g_sigNoteKind = SC_BAD; }
    else if (st == L"signaler-natif-envoi") { g_sigView = SV_SEND; g_sigProg = T(L"Envoi des journaux (2/3)…", L"Sending the logs (2/3)…"); g_sigFrac = 0.4f; }
    else if (st == L"signaler-natif-merci") { g_sigView = SV_DONE; g_sigDoneN = 43; }
    else if (st == L"signaler-natif-erreur") { g_sigView = SV_ERROR; g_sigFatal = T(L"Site de signalement injoignable (erreur 12029)", L"Bug report site unreachable (error 12029)"); g_bugPossible = 1; }
}

// ---- essai cache : parcours complet contre le relais local de [Site Tableau] (faux Discord), jamais de fenetre.
// /testsignalnatif <journal> <base> : le code recu est ecrit dans <journal>.code pour que le banc d'essai l'autorise.
static int SigSelfTest(const std::wstring &log, const std::wstring &base)
{
    g_launcherLog = log; g_sigTest = true;
    SetEnvironmentVariableW(L"MWCOOP_SIGNAL_BASE", base.c_str());
    int fails = 0;
    auto check = [&](bool ok, const char *what, const SigHttp &r) { LaunchLog("testsignalnatif : %s : %s (statut %lu, erreur %lu, site %s)", what, ok ? "OK" : "ECHEC", r.status, r.err, r.erreur.empty() ? "-" : r.erreur.c_str()); if (!ok) fails++; return ok; };
    SigHttp r;
    LaunchLog("testsignalnatif : base %s, version envoyee %s, systeme %s", Narrow(base, CP_UTF8).c_str(), Narrow(SigVer(), CP_UTF8).c_str(), SigSysteme());
    // 1. textes
    SigCall(L"GET", L"/textes?lang=fr", "", false, false, r);
    check(r.status == 200 && SigTxApply(r.body, L"fr"), "textes", r);
    SigFormBuild();
    LaunchLog("testsignalnatif : formulaire %d champs, texte dpJeu = %s", (int)g_sigForm.size(), Narrow(ST("dpJeu"), CP_UTF8).c_str());
    // 2. sans en-tete de version : 400 (on force une version vide par une base absurde ? non : on verifie le refus d'un jeton bidon)
    SigCall(L"GET", L"/mes", "", false, true, r, "bidon-bidon-bidon-bidon-bidon-bidon-bidon-");
    check(r.status == 401, "jeton bidon refuse", r);
    // 3. code, puis jeton (le banc d'essai autorise le code lu dans <journal>.code)
    SigCall(L"POST", L"/code", "{\"systeme\":\"win10\"}", true, false, r);
    if (!check(r.status == 200 && r.j.str("code").size() == 9, "code", r)) return 1;
    std::string appareil = r.j.str("appareil"), code = r.j.str("code");
    { HANDLE f = CreateFileW((log + L".code").c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL); if (f != INVALID_HANDLE_VALUE) { DWORD w; WriteFile(f, code.data(), (DWORD)code.size(), &w, NULL); CloseHandle(f); } }
    SigCall(L"POST", L"/jeton", "{\"appareil\":" + JsonEsc(appareil) + "}", true, false, r);
    check(r.status == 428, "jeton en attente (428)", r);
    DWORD t0 = GetTickCount();
    while (r.status == 428 || (r.status == 429 && r.erreur == "trop-vite")) { Sleep(5000); SigCall(L"POST", L"/jeton", "{\"appareil\":" + JsonEsc(appareil) + "}", true, false, r); if (GetTickCount() - t0 > 120000) break; }
    if (!check(r.status == 200 && r.j.str("jeton").size() == 43, "jeton recu", r)) return 1;
    g_sigJeton = r.j.str("jeton"); g_sigPseudo = Widen(r.j.str("pseudo")); g_sigExpire = r.j.str("expire"); g_sigRenouv = r.j.str("renouvelableJusqua"); g_sigLogged = true;
    SigTokenSave();
    { g_sigJeton.clear(); std::wstring p = SigTokenPath(); std::vector<unsigned char> d; std::string dec; Jv j; bool ok = ReadAll(p, d) && SigDpapi(false, std::string(d.begin(), d.end()), dec) && JParse(dec, j) && j.str("jeton").size() == 43 && std::string(d.begin(), d.end()).find(j.str("jeton")) == std::string::npos; g_sigJeton = j.str("jeton"); check(ok, "jeton chiffre DPAPI, relu, absent en clair du fichier", r); }
    // 4. moi, mes
    SigCall(L"GET", L"/moi", "", false, true, r);
    check(r.status == 200 && r.j.get("limites") != NULL, "moi", r);
    g_sigZipMax = (long long)r.j.get("limites")->num("zipMax", 15728640);
    SigCall(L"GET", L"/mes", "", false, true, r);
    check(r.status == 200 && r.j.arr("dossiers") != NULL, "mes", r);
    int before = r.status == 200 ? (int)r.j.arr("dossiers")->a.size() : -1;
    // 5. depot : formulaire rempli, zip = le journal de cet essai + l'ini du lanceur
    for (auto &f : g_sigForm) {
        if (f.champ == "jeu") f.sel = 1; if (f.champ == "titre") f.val = L"Essai du lanceur (natif)"; if (f.champ == "type") f.sel = 2; if (f.champ == "version") f.val = L"msc-1.15.13";
        if (f.champ == "probleme") f.val = L"La roue tombe chez l'invité depuis le lanceur natif (essai)."; if (f.champ == "etapes") f.val = L"1. rouler\n2. freiner"; if (f.champ == "frequence") f.sel = 1;
        if (f.champ == "role") f.sel = 2; if (f.champ == "joueurs") f.val = L"2"; if (f.champ == "connexion") f.sel = 0; if (f.champ == "mods_choix") f.sel = 0; if (f.champ == "zips") f.on = true;
        if (f.champ == "solo_sans_mods") f.sel = 2; if (f.champ == "version_precedente_choix") f.sel = 2; if (f.champ == "media_choix") f.sel = 1; if (f.champ == "systeme_choix") f.sel = 1; if (f.champ == "sauvegarde") f.sel = 0;
    }
    g_sigZipFiles = 2;
    check(SigValidate() < 0, "validation locale du formulaire", r);
    for (auto &f : g_sigForm) if (f.champ == "joueurs") f.val = L"1";
    check(SigValidate() >= 0, "regle croisee role/joueurs vue en local", r);
    for (auto &f : g_sigForm) if (f.champ == "joueurs") f.val = L"2";
    SigJob job; job.kind = SOP_DEPOSER; job.body = SigFormJson(); job.zip = log + L".zip"; job.files = { log, g_iniLauncher }; job.zipWanted = true;
    SigDeposer(job, r);
    int n = r.status == 200 ? (int)r.j.num("n") : 0;
    check(r.status == 200 && n > 0, "depot (brouillon, morceaux, envoyer)", r);
    LaunchLog("testsignalnatif : dossier #%d", n);
    SigCall(L"GET", L"/mes", "", false, true, r);
    bool seen = false; if (r.status == 200) for (const Jv &d : r.j.arr("dossiers")->a) if ((int)d.num("n") == n) seen = d.str("etat") == "a_trier";
    check(seen && (int)r.j.arr("dossiers")->a.size() == before + 1, "mes : le dossier est la, a trier", r);
    // 6. ajout « Toujours la » refuse dans cet etat (409 etat) ; retrait ; deconnexion ; jeton mort
    SigJob add; add.kind = SOP_DEPOSER; add.n = n; add.body = "{\"n\":" + std::to_string(n) + ",\"texte\":\"Dix caractères au moins, pour l'essai.\",\"zips\":__ZIPS__}"; add.zipWanted = false;
    SigDeposer(add, r);
    check(r.status == 409 && r.erreur == "etat", "ajout refuse (409 etat) et texte erEtatDossier", r);
    SigCall(L"POST", L"/dossiers/" + std::to_wstring(n) + L"/retirer", "{}", true, true, r);
    check(r.status == 200 && r.j.str("etat") == "retire", "retirer", r);
    SigCall(L"POST", L"/deconnexion", "{}", true, true, r);
    check(r.status == 200, "deconnexion", r);
    SigCall(L"GET", L"/mes", "", false, true, r);
    check(r.status == 401, "jeton retire : 401", r);
    SigTokenClear();
    check(GetFileAttributesW(SigTokenPath().c_str()) == INVALID_FILE_ATTRIBUTES, "fichier du jeton efface", r);
    DeleteFileW((log + L".zip").c_str());
    LaunchLog("testsignalnatif : fin, %d echec(s)", fails);
    return fails ? 1 : 0;
}
