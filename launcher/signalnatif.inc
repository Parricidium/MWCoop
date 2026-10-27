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
       UI_SIG_SEND, UI_SIG_FORMCANCEL, UI_SIG_TOLIST, UI_SIG_RETRY, UI_SIG_PANEL, UI_SIG_REFRESH, UI_SIG_COPYURL, UI_SIG_ADDEXT,
       UI_SIG_FIELD = 14100,   // + 10 * champ : +0 le texte (focus), +1..+7 les options d'un choix, +8 le detail, +9 l'interrupteur
       UI_SIG_ROW = 14300,     // + i : une ligne de « Mes dossiers » (50 au plus)
       UI_SIG_SESS = 14400,    // + k : la partie k du selecteur de journaux du formulaire (30 au plus)
       UI_SIG_EXT = 14450 };   // + k : retirer le zip k d'un autre joueur (16 au plus)
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
static bool g_sigOpenFailed;                             // « Ouvrir la page » : le navigateur ne s'est pas ouvert (adresse a copier)
static bool CopyText(const std::wstring &t);   // (launcher.cpp)
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
// selecteur de journaux du formulaire (voir SigSessScan) : parties du jeu choisi, fichiers communs, zips d'un autre joueur
struct SigSess { LogGroup g; std::vector<std::wstring> paths; bool checked = false; };
static std::vector<SigSess> g_sigSess; static int g_sigSessGame = -1;
static std::vector<std::wstring> g_sigCommon, g_sigExt;
struct SigZipSpec { std::wstring path; std::vector<std::wstring> files; };   // files vide : un zip existant (autre joueur), envoye tel quel
struct SigJob { int kind = SOP_NONE; std::string body, path, lang; int n = 0; std::wstring zip; std::vector<std::wstring> files; bool zipWanted = false; std::vector<SigZipSpec> zips; };
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
// Plusieurs zips (JD 27/10, selecteur de journaux) : zip k = /zip/<k+1>/<morceau> ; les zips fabriques ici sont effaces a la fin,
// ceux d'un autre joueur (choisis a la main) restent.
static void SigDeposer(SigJob &job, SigHttp &r)
{
    if (job.zipWanted && !job.files.empty()) { SigZipSpec z; z.path = job.zip; z.files = job.files; job.zips.insert(job.zips.begin(), z); }   // (ancienne forme : un seul zip)
    std::vector<std::wstring> paths; std::string sizesJson = "[";
    for (int k = 0; k < (int)job.zips.size(); k++) {
        SigZipSpec &z = job.zips[k];
        if (!z.files.empty()) {
            int nf = 0; wchar_t pb[96]; swprintf_s(pb, T(L"Préparation du zip des journaux (%d/%d)…", L"Preparing the logs zip (%d/%d)…"), k + 1, (int)job.zips.size()); SigProg(pb, -1);
            CreateDirectoryW(LocalDir().c_str(), NULL);
            if (z.path.empty()) z.path = LocalDir() + L"signalement-envoi-" + std::to_wstring(k + 1) + L".zip";
            if (!WriteZip(z.path, z.files, &nf, g_sigZipMax - 256 * 1024, 4LL * 1024 * 1024, true)) { r.status = 0; r.err = GetLastError(); r.erreur = "zip-local"; return; }
        }
        WIN32_FILE_ATTRIBUTE_DATA fa; long long size = 0;
        if (GetFileAttributesExW(z.path.c_str(), GetFileExInfoStandard, &fa)) size = (long long)fa.nFileSizeHigh << 32 | fa.nFileSizeLow;
        if (size <= 0) { r.erreur = "zip-local"; return; }
        paths.push_back(z.path); sizesJson += (k ? "," : "") + std::to_string(size);
    }
    sizesJson += "]";
    std::string body = job.body;
    size_t ph = body.find("__ZIPS__");
    if (ph != std::string::npos) body.replace(ph, 8, sizesJson);
    SigProg(T(L"Envoi des champs…", L"Sending the fields…"), -1);
    if (!SigCall(L"POST", L"/brouillon", body, true, true, r) || r.status != 200) return;
    std::string id = r.j.str("id");
    long long morceau = (long long)r.j.num("morceau", (double)g_sigMorceau);
    auto sendChunk = [&](HANDLE f, int k, int i, SigHttp &pr) {   // morceau i du zip k (0..) : false si illisible ou refuse
        std::string chunk((size_t)morceau, 0); DWORD got = 0;
        LARGE_INTEGER pos; pos.QuadPart = (long long)i * morceau; SetFilePointerEx(f, pos, NULL, FILE_BEGIN);
        if (!ReadFile(f, &chunk[0], (DWORD)morceau, &got, NULL) || !got) { pr.status = 0; pr.erreur = "zip-local"; return false; }
        chunk.resize(got);
        std::wstring path = L"/brouillon/" + Widen(id) + L"/zip/" + std::to_wstring(k + 1) + L"/" + std::to_wstring(i);
        return SigCall(L"PUT", path, chunk, false, true, pr) && pr.status == 200;
    };
    std::vector<int> nM(paths.size(), 0); int totalM = 0, doneM = 0;
    if (const Jv *z = r.j.arr("zips")) for (int k = 0; k < (int)z->a.size() && k < (int)paths.size(); k++) { nM[k] = (int)z->a[k].num("morceaux"); totalM += nM[k]; }
    for (int k = 0; k < (int)paths.size(); k++) {
        if (nM[k] <= 0) continue;
        HANDLE f = CreateFileW(paths[k].c_str(), GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, 0, NULL);
        if (f == INVALID_HANDLE_VALUE) { r.status = 0; r.err = GetLastError(); r.erreur = "zip-local"; return; }
        for (int i = 0; i < nM[k]; i++) {
            wchar_t p[96]; swprintf_s(p, T(L"Envoi des journaux (%d/%d)…", L"Sending the logs (%d/%d)…"), doneM + 1, totalM);
            SigProg(p, (float)doneM / totalM);
            SigHttp pr;
            bool ok = sendChunk(f, k, i, pr);
            if (!ok && pr.erreur != "zip-local" && !g_sigCancel) { SigSleep(1500); ok = sendChunk(f, k, i, pr); }   // (une coupure : on renvoie, sans doublon)
            if (!ok) { CloseHandle(f); r = pr; return; }
            if (g_sigCancel) { CloseHandle(f); r.status = 0; r.erreur = "annule"; return; }
            doneM++;
        }
        CloseHandle(f);
    }
    SigProg(T(L"Finalisation…", L"Finishing…"), 1);
    std::wstring env = L"/brouillon/" + Widen(id) + L"/envoyer";
    if (!SigCall(L"POST", env, "{}", true, true, r)) return;
    if (r.status == 409 && r.erreur == "morceaux-manquants" && !paths.empty()) {   // (reprise : les morceaux manquants de chaque zip, puis envoyer)
        SigHttp m;
        if (SigCall(L"GET", L"/brouillon/" + Widen(id), "", false, true, m) && m.status == 200)
            if (const Jv *z = m.j.arr("zips")) for (const Jv &zz : z->a) {
                int k = (int)zz.num("zip") - 1; if (k < 0 || k >= (int)paths.size()) continue;
                HANDLE f = CreateFileW(paths[k].c_str(), GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, 0, NULL);
                if (f == INVALID_HANDLE_VALUE) continue;
                if (const Jv *mq = zz.arr("manquants")) for (const Jv &i : mq->a) { SigHttp pr; sendChunk(f, k, (int)i.n, pr); }
                CloseHandle(f);
            }
        SigCall(L"POST", env, "{}", true, true, r);
    }
    for (auto &z : job.zips) if (!z.files.empty() && !z.path.empty()) DeleteFileW(z.path.c_str());
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
// ---- selecteur de journaux (JD 27/10 : « selon la selection MSC ou MWC, la liste des logs s'ouvre et on en selectionne 1 ou
// plusieurs ») : les parties enregistrees du jeu choisi en haut du formulaire, la derniere cochee d'office ; les fichiers communs
// (reglages, journal du lanceur, MSCLoader, cle masquee) vont avec la premiere partie cochee ; chaque autre partie cochee fait son
// propre zip ; les zips d'un autre joueur choisis a la main suivent. Limites du site (15 Mo par zip, 16 zips, 40 Mo en tout)
// verifiees ici avant l'envoi, taille estimee affichee en direct. On ne quitte jamais le formulaire.
static std::vector<std::wstring> ZipFiles();   // (launcher.cpp)
static void SteamLibraries(std::vector<std::wstring> &out);   // (launcher.cpp)
static bool GameProcessRunning(DWORD *pidOut);
static std::wstring SigDateOf(const FILETIME &ft) { return ft.dwLowDateTime == 0 && ft.dwHighDateTime == 0 ? T(L"date inconnue", L"unknown date") : LogDate(ft); }   // (jamais 01/01/1601)
// Accord d'un nom avec un nombre : formes « a|b|c|d » du texte traduit -- FR / EN : 1, autres ; russe : 1, 2-4, 5+ (11-14 : 5+) ;
// arabe : 1, 2, 3-10, 11+. S'il manque des formes, la derniere sert.
static std::wstring SigPlural(int n, const wchar_t *forms)
{
    std::vector<std::wstring> f; std::wstring cur;
    for (const wchar_t *p = forms; ; p++) { if (*p == L'|' || !*p) { f.push_back(cur); cur.clear(); if (!*p) break; } else cur += *p; }
    int i;
    if (g_ru) { int m10 = n % 10, m100 = n % 100; i = m10 == 1 && m100 != 11 ? 0 : m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14) ? 1 : 2; }
    else if (g_ar) i = n == 1 ? 0 : n == 2 ? 1 : n >= 3 && n <= 10 ? 2 : 3;
    else i = n == 1 ? 0 : 1;
    if (i >= (int)f.size()) i = (int)f.size() - 1;
    return f[i];
}
static std::wstring SigFilesText(int n) { wchar_t b[48]; swprintf_s(b, L"%d %s", n, SigPlural(n, T(L"fichier|fichiers", L"file|files")).c_str()); return b; }
// Date d'une partie : son mwcoop.log le plus recent, sinon le fichier le plus recent (le journal de Unity compris : JD 27/10,
// « 01/01/1601 » sur une partie qui n'avait que lui), sinon 0 -> « date inconnue ».
static void SigSessEnd(SigSess &s)
{
    FILETIME best = {}; bool mod = false;
    for (auto &p : s.paths) {
        WIN32_FILE_ATTRIBUTE_DATA a; if (!GetFileAttributesExW(p.c_str(), GetFileExInfoStandard, &a)) continue;
        bool isMod = p.find(L"mwcoop.log") != std::wstring::npos;
        if ((isMod && !mod) || (isMod == mod && CompareFileTime(&a.ftLastWriteTime, &best) > 0)) { best = a.ftLastWriteTime; mod = mod || isMod; }
    }
    s.g.end = best;
}
static std::wstring SigSessGameDir(int game)   // dossier du jeu demande : celui en cours, ou l'autre (dossier du lanceur, dossier retenu, bibliotheques Steam)
{
    if (game == g_game) return g_gameDir;
    const GameDef &gd = kGames[game];
    if (IsGameDirOf(g_dir, game)) return g_dir;
    wchar_t saved[MAX_PATH] = L""; GetPrivateProfileStringW(L"Lanceur", gd.iniDir, L"", saved, MAX_PATH, g_iniLauncher.c_str());
    if (saved[0] && IsGameDirOf(WithSlash(saved), game)) return WithSlash(saved);
    std::vector<std::wstring> libs; SteamLibraries(libs);
    for (const std::wstring &l : libs) { std::wstring d = l + L"steamapps\\common\\" + gd.steamDir + L"\\"; if (IsGameDirOf(d, game)) return d; }
    return L"";
}
static std::wstring SigSessModRoot(int game, const std::wstring &dir) { return game == g_game ? g_modRoot : dir.empty() ? L"" : ModInGame() ? dir : g_dir + (game == GAME_MSC ? L"MSC\\" : L"MWC\\"); }
static int SigGameOfForm() { for (auto &f : g_sigForm) if (f.champ == "jeu" && f.sel >= 0 && f.sel < (int)f.opts.size()) return f.opts[f.sel].first == "msc" ? GAME_MSC : GAME_MWC; return g_game; }
static void LogHeadText(const LogGroup &gr, std::wstring &title, std::wstring &sub);   // (ui.inc : l'en-tete d'une partie, le meme que la page Journaux)
static std::wstring SigModVersion(int game)   // version du mod installe pour ce jeu (l'autre jeu : son MWCoop\version.txt)
{
    if (game == g_game) return g_localVer;
    std::vector<unsigned char> d; std::wstring root = SigSessModRoot(game, SigSessGameDir(game));
    return !root.empty() && ReadAll(root + L"MWCoop\\version.txt", d) ? Trim(Widen(std::string(d.begin(), d.end()))) : L"";
}
static std::wstring SigVersionAuto(int game) { std::wstring v = SigModVersion(game); return v.empty() ? L"" : std::wstring(game == GAME_MSC ? L"msc-" : L"mwc-") + v; }
static std::wstring g_sigVerAuto;   // (derniere version preremplie : remplacee au changement de jeu si le joueur n'y a pas touche)
// Le selecteur = les parties de la page Journaux, par LogsScan (meme regroupement, meme tri : la plus recente en haut). L'autre
// jeu : les globales du jeu basculent le temps du scan, puis la page Journaux est rescannee pour le jeu en cours.
static void SigSessScan(int game, bool keepChecks)
{
    std::vector<std::wstring> was; if (keepChecks) for (auto &s : g_sigSess) if (s.checked) was.push_back(s.g.key);
    g_sigSess.clear(); g_sigCommon.clear(); g_sigSessGame = game;
    if (g_sigCapture) {   // (captures : trois parties factices, comme sur la page Journaux)
        auto mk = [](const wchar_t *key, const wchar_t *mode, const wchar_t *ver, const wchar_t *startHM, int files, uint64_t bytes, int daysAgo, int hour, int minute) {
            SigSess s; s.g.key = key; s.g.mode = mode; s.g.version = ver; s.g.startHM = startHM; s.g.files = files; s.g.bytes = bytes;
            SYSTEMTIME st; GetLocalTime(&st); st.wHour = (WORD)hour; st.wMinute = (WORD)minute; st.wSecond = 0; st.wMilliseconds = 0;
            FILETIME lt; SystemTimeToFileTime(&st, &lt); ULARGE_INTEGER u; u.LowPart = lt.dwLowDateTime; u.HighPart = lt.dwHighDateTime; u.QuadPart -= (ULONGLONG)daysAgo * 864000000000ULL; lt.dwLowDateTime = u.LowPart; lt.dwHighDateTime = u.HighPart;
            LocalFileTimeToFileTime(&lt, &s.g.end);
            for (int i = 0; i < files; i++) s.paths.push_back(L"factice");
            return s; };
        g_sigSess.push_back(mk(L"*", T(L"Hôte", L"Host"), L"1.15.14", L"18h11", 14, 600000, 0, 18, 36));
        g_sigSess.push_back(mk(L"a", T(L"Invité", L"Guest"), L"1.15.13", L"21h02", 3, 1100000, 1, 21, 40));
        g_sigSess.push_back(mk(L"b", T(L"Solo", L"Solo"), L"1.15.12", L"17h20", 2, 400000, 2, 17, 39));
        g_sigSess[0].checked = true; g_sigCommon = { L"factice", L"factice", L"factice", L"factice" }; g_sigZipFiles = 7;
        return;
    }
    int gameWas = g_game; std::wstring dirWas = g_gameDir, rootWas = g_modRoot;
    if (game != g_game) { std::wstring d = SigSessGameDir(game), root = SigSessModRoot(game, d); g_game = game; g_gameDir = d; g_modRoot = root; }
    std::vector<std::wstring> all = ZipFiles();   // (LogsScan dedans : g_logGroups / g_logList = les parties de la page Journaux)
    for (const LogGroup &gr : g_logGroups) {
        SigSess s; s.g = gr;
        for (int i : gr.items) if (i >= 0 && i < (int)g_logList.size()) s.paths.push_back(g_logList[i].path);
        if (!s.g.end.dwLowDateTime && !s.g.end.dwHighDateTime) SigSessEnd(s);   // (partie sans journal date : le fichier le plus recent, sinon « date inconnue »)
        if (s.g.files) g_sigSess.push_back(s);
    }
    if (g_sigSess.size() > 30) g_sigSess.resize(30);
    for (auto &p : all) {   // fichiers communs : ceux du zip de la page Journaux moins les journaux des parties (reglages, lanceur, MSCLoader, copies)
        bool sess = false;
        for (auto &s : g_sigSess) for (auto &q : s.paths) if (!_wcsicmp(q.c_str(), p.c_str())) sess = true;
        if (!sess) g_sigCommon.push_back(p);
    }
    if (game != gameWas) { g_game = gameWas; g_gameDir = dirWas; g_modRoot = rootWas; LogsScan(); }   // (la page Journaux retrouve le jeu en cours)
    bool any = false;   // cochee d'office : la derniere partie (ou celles cochees avant, si elles sont encore la)
    for (auto &s : g_sigSess) { s.checked = std::find(was.begin(), was.end(), s.g.key) != was.end(); any = any || s.checked; }
    if (!any && !g_sigSess.empty()) g_sigSess[0].checked = true;
    g_sigZipFiles = (int)g_sigCommon.size() + (g_sigSess.empty() ? 0 : g_sigSess[0].g.files);
}
static std::vector<SigZipSpec> SigZipSpecs(bool latestOnly)   // latestOnly : ajout a un dossier -- la derniere partie et les communs
{
    std::vector<SigZipSpec> out; bool first = true;
    for (int k = 0; k < (int)g_sigSess.size(); k++) {
        const SigSess &s = g_sigSess[k];
        if (latestOnly ? k != 0 : !s.checked) continue;
        SigZipSpec z; z.files = s.paths;
        if (first) { z.files.insert(z.files.end(), g_sigCommon.begin(), g_sigCommon.end()); first = false; }
        out.push_back(z);
    }
    if (out.empty() && !g_sigCommon.empty() && (latestOnly || g_sigSess.empty())) { SigZipSpec z; z.files = g_sigCommon; out.push_back(z); }   // (aucune partie : les communs quand meme)
    if (!latestOnly) for (auto &e : g_sigExt) { SigZipSpec z; z.path = e; out.push_back(z); }
    return out;
}
static long long SigSpecSize(const SigZipSpec &z)   // taille estimee d'un zip (stocke sans compression : la somme des fichiers, 4 Mo par fichier au plus)
{
    WIN32_FILE_ATTRIBUTE_DATA a;
    if (z.files.empty()) { if (g_sigCapture) return 1900000; return GetFileAttributesExW(z.path.c_str(), GetFileExInfoStandard, &a) ? (long long)(((uint64_t)a.nFileSizeHigh << 32) | a.nFileSizeLow) : 0; }
    long long t = 0;
    for (auto &p : z.files) {
        if (p == L"factice") { t += 700000; continue; }
        if (!GetFileAttributesExW(p.c_str(), GetFileExInfoStandard, &a)) continue;
        long long b = (long long)(((uint64_t)a.nFileSizeHigh << 32) | a.nFileSizeLow);
        t += (b < 4LL * 1024 * 1024 ? b : 4LL * 1024 * 1024) + 120;
    }
    return t;
}
static int SigZipCheck(long long *total, int *nz, long long *biggest)   // 0 ok ; 1 rien a joindre ; 2 trop de zips ; 3 trop gros en tout
{
    std::vector<SigZipSpec> zs = SigZipSpecs(false);
    *total = 0; *nz = (int)zs.size(); *biggest = 0;
    for (auto &z : zs) { long long s = SigSpecSize(z); *total += s; if (s > *biggest) *biggest = s; }
    if (zs.empty()) return 1;
    if (*nz > 16) return 2;
    if (*total > g_sigEnvoiMax) return 3;
    return 0;
}
static void SigAddExt()   // « Ajouter un zip d'un autre joueur… » : un .zip choisi a la main (jamais de boite en essai ni en capture)
{
    if (g_sigExt.size() >= 16) return;
    if (g_sigTest || g_sigCapture) { g_sigExt.push_back(g_sigCapture ? L"C:\\Users\\Teppo\\Downloads\\MWCoop-journaux-2026-10-26.zip" : g_launcherLog + L".ext.zip"); return; }
    wchar_t file[MAX_PATH] = L"";
    OPENFILENAMEW of = { sizeof(of) };
    of.hwndOwner = g_wnd; of.lpstrFilter = L"zip\0*.zip\0"; of.lpstrFile = file; of.nMaxFile = MAX_PATH;
    of.lpstrTitle = T(L"Choisir le zip des journaux d'un autre joueur", L"Choose another player's logs zip");
    of.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_HIDEREADONLY;
    if (!GetOpenFileNameW(&of)) return;
    for (auto &e : g_sigExt) if (!_wcsicmp(e.c_str(), file)) return;
    g_sigExt.push_back(file);
    for (auto &f : g_sigForm) if (f.type == "zips") f.err.clear();
    InvalidateAll();
}
// ---- fenetre integree de choix des zips d'un autre joueur (JD 27/10 : « un popup integre au launcher avec son design ») :
// Recents (les .zip du Bureau, des Telechargements, des Documents et de MWCoop dans LOCALAPPDATA, les plus recents en haut), raccourcis
// de dossiers et fil d'Ariane pour aller ailleurs, cases a cocher (plusieurs), « Parcourir… » (boite Windows, selection multiple) en
// repli. Chaque zip est verifie AVANT d'etre ajoute (SigZipInspect : un vrai zip, 15 Mo au plus, entrees .txt / .log / .json, pas de
// « .. » ni de chemin absolu, 64 Mo decompresses au plus, 500 entrees au plus) : sinon un message clair sur sa ligne. Essais et
// captures : dossier factice (g_zpFake), bouchon de la boite Windows, jamais de fenetre.
enum { UI_SIG_ZP_CLOSE = 14480, UI_SIG_ZP_ADD, UI_SIG_ZP_BROWSE, UI_SIG_ZP_UP, UI_SIG_ZP_SHORT = 14485, UI_SIG_ZP_ROW = 14500, UI_SIG_ZP_CRUMB = 14590 };   // raccourcis +0..4, lignes +0..79, fil +0..7
struct ZpItem { std::wstring path, name, where; bool dir = false; FILETIME mt = {}; long long bytes = 0; bool checked = false; std::wstring bad; };
static bool g_zpOpen; static int g_zpView;                // 0 : Recents ; 1 : un dossier (g_zpDir)
static std::wstring g_zpDir, g_zpFake, g_zpNote; static int g_zpNoteKind;
static std::vector<ZpItem> g_zpItems; static float g_zpScroll; static RectF g_zpListR;
static std::wstring SigZpKnown(int which)   // 0 Bureau, 1 Telechargements, 2 Documents, 3 MWCoop (LOCALAPPDATA)
{
    if (!g_zpFake.empty()) return which == 1 ? g_zpFake + L"telechargements\\" : g_zpFake;
    wchar_t p[MAX_PATH] = L"";
    if (which == 3) return LocalDir();
    if (which == 1) {
        PWSTR kf = NULL;
        if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_Downloads, 0, NULL, &kf)) && kf) { std::wstring d = WithSlash(kf); CoTaskMemFree(kf); return d; }
        if (SUCCEEDED(SHGetFolderPathW(NULL, CSIDL_PROFILE, NULL, SHGFP_TYPE_CURRENT, p)) && p[0]) return WithSlash(p) + L"Downloads\\";   // (Wine, Windows 7 sans le dossier connu)
        return L"";
    }
    if (SUCCEEDED(SHGetFolderPathW(NULL, which == 0 ? CSIDL_DESKTOPDIRECTORY : CSIDL_PERSONAL, NULL, SHGFP_TYPE_CURRENT, p)) && p[0]) return WithSlash(p);
    return L"";
}
static const wchar_t *SigZpKnownName(int which) { return which == 0 ? T(L"Bureau", L"Desktop") : which == 1 ? T(L"Téléchargements", L"Downloads") : which == 2 ? T(L"Documents", L"Documents") : L"MWCoop"; }
static void SigZpScanDir(const std::wstring &dir, const std::wstring &where, bool dirsToo, std::vector<ZpItem> &out)
{
    if (dir.empty()) return;
    WIN32_FIND_DATAW fd; HANDLE h = FindFirstFileW((dir + L"*").c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return;
    do {
        if (fd.cFileName[0] == L'.' || (fd.dwFileAttributes & (FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_SYSTEM))) continue;
        bool isDir = (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
        size_t n = wcslen(fd.cFileName);
        if (!isDir && (n < 4 || _wcsicmp(fd.cFileName + n - 4, L".zip"))) continue;
        if (isDir && !dirsToo) continue;
        ZpItem it; it.name = fd.cFileName; it.path = dir + fd.cFileName + (isDir ? L"\\" : L""); it.where = where; it.dir = isDir; it.mt = fd.ftLastWriteTime;
        it.bytes = ((long long)fd.nFileSizeHigh << 32) | fd.nFileSizeLow;
        for (auto &e : g_sigExt) if (!_wcsicmp(e.c_str(), it.path.c_str())) it.checked = true;   // (deja ajoute : coche, grise)
        bool dup = false; for (auto &o : out) if (!_wcsicmp(o.path.c_str(), it.path.c_str())) dup = true;   // (Recents : deux dossiers connus confondus)
        if (!dup) out.push_back(it);
    } while (FindNextFileW(h, &fd) && out.size() < 400);
    FindClose(h);
}
static void SigZpSort(std::vector<ZpItem> &v)   // dossiers d'abord (par nom), puis les zips les plus recents en haut
{
    std::stable_sort(v.begin(), v.end(), [](const ZpItem &a, const ZpItem &b) { if (a.dir != b.dir) return a.dir; if (a.dir) return _wcsicmp(a.name.c_str(), b.name.c_str()) < 0; return CompareFileTime(&a.mt, &b.mt) > 0; });
    if (v.size() > 80) v.resize(80);
}
static void SigZpScan()
{
    g_zpItems.clear(); g_zpScroll = 0;
    if (g_sigCapture) {   // (captures : cinq zips factices, un deja coche, un refuse)
        auto mk = [](const wchar_t *name, const wchar_t *where, long long bytes, int hoursAgo, bool checked, const wchar_t *bad) {
            ZpItem it; it.name = name; it.path = std::wstring(L"C:\\factice\\") + name; it.where = where; it.bytes = bytes; it.checked = checked; it.bad = bad;
            FILETIME now; GetSystemTimeAsFileTime(&now); ULARGE_INTEGER u; u.LowPart = now.dwLowDateTime; u.HighPart = now.dwHighDateTime; u.QuadPart -= (ULONGLONG)hoursAgo * 36000000000ULL; it.mt.dwLowDateTime = u.LowPart; it.mt.dwHighDateTime = u.HighPart; return it; };
        g_zpItems.push_back(mk(L"MWCoop-journaux-2026-10-27_19h02.zip", T(L"Téléchargements", L"Downloads"), 1900000, 2, true, L""));
        g_zpItems.push_back(mk(L"MWCoop-journaux-2026-10-26_21h40.zip", T(L"Bureau", L"Desktop"), 2400000, 23, false, L""));
        g_zpItems.push_back(mk(L"MWCoop-signalement-2026-10-26.zip", L"MWCoop", 650000, 26, false, L""));
        g_zpItems.push_back(mk(L"photos-vacances.zip", T(L"Téléchargements", L"Downloads"), 48000000, 40, false, T(L"Pas un zip de journaux : « IMG_2041.jpg » n'est ni .txt, ni .log, ni .json", L"Not a logs zip: \"IMG_2041.jpg\" is not .txt, .log or .json")));
        g_zpItems.push_back(mk(L"logs-kristof.zip", T(L"Documents", L"Documents"), 880000, 70, false, L""));
        return;
    }
    if (g_zpView == 0) { for (int k = 0; k < 4; k++) SigZpScanDir(SigZpKnown(k), SigZpKnownName(k), false, g_zpItems); }
    else SigZpScanDir(g_zpDir, L"", true, g_zpItems);
    SigZpSort(g_zpItems);
}
static void SigZpOpen() { g_zpOpen = true; g_zpView = 0; g_zpNote.clear(); SigZpScan(); InvalidateAll(); }
static void SigZpGo(const std::wstring &dir) { g_zpView = 1; g_zpDir = WithSlash(dir); g_zpNote.clear(); SigZpScan(); }
// Verification d'un zip avant l'envoi (les memes regles que le site, pour un message clair ici) : vrai zip, 15 Mo au plus, entrees
// .txt / .log / .json, pas de « .. » ni de chemin absolu, 64 Mo decompresses au plus, 500 entrees au plus, au moins une entree.
static bool SigZipInspect(const std::wstring &path, std::wstring &why)
{
    WIN32_FILE_ATTRIBUTE_DATA a; wchar_t b[320];
    if (!GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &a)) { why = T(L"Fichier introuvable", L"File not found"); return false; }
    long long size = ((long long)a.nFileSizeHigh << 32) | a.nFileSizeLow;
    if (size > g_sigZipMax) { swprintf_s(b, T(L"Trop gros : %s (%s au plus par zip)", L"Too big: %s (%s at most per zip)"), SaveSizeText(size).c_str(), SaveSizeText(g_sigZipMax).c_str()); why = b; return false; }
    std::vector<unsigned char> d;
    if (size < 22 || !ReadAll(path, d)) { why = T(L"Pas un zip (fichier illisible ou vide)", L"Not a zip (unreadable or empty file)"); return false; }
    auto u16 = [&](size_t o) { return (uint32_t)d[o] | ((uint32_t)d[o + 1] << 8); };
    auto u32 = [&](size_t o) { return u16(o) | (u16(o + 2) << 16); };
    size_t eocd = std::wstring::npos;
    for (size_t o = d.size() - 22; ; o--) { if (u32(o) == 0x06054b50) { eocd = o; break; } if (o == 0 || d.size() - o > 65557) break; }
    if (eocd == std::wstring::npos || u32(0) != 0x04034b50) { why = T(L"Pas un zip", L"Not a zip"); return false; }
    uint32_t n = u16(eocd + 10), cdSize = u32(eocd + 12), cdOff = u32(eocd + 16);
    if (n == 0) { why = T(L"Zip vide", L"Empty zip"); return false; }
    if (n > 500) { swprintf_s(b, T(L"Trop d'entrées : %u (500 au plus)", L"Too many entries: %u (500 at most)"), n); why = b; return false; }
    if ((uint64_t)cdOff + cdSize > d.size()) { why = T(L"Zip abîmé (répertoire hors du fichier)", L"Damaged zip (directory outside the file)"); return false; }
    size_t o = cdOff; unsigned long long total = 0;
    for (uint32_t i = 0; i < n; i++) {
        if (o + 46 > d.size() || u32(o) != 0x02014b50) { why = T(L"Zip abîmé (entrée illisible)", L"Damaged zip (unreadable entry)"); return false; }
        uint32_t usize = u32(o + 24), nameLen = u16(o + 28), extraLen = u16(o + 30), comLen = u16(o + 32);
        if (o + 46 + nameLen > d.size()) { why = T(L"Zip abîmé (nom tronqué)", L"Damaged zip (truncated name)"); return false; }
        std::string name((const char *)&d[o + 46], nameLen);
        std::wstring wn = Widen(name);
        total += usize;
        bool dirEntry = !name.empty() && (name.back() == '/' || name.back() == '\\');
        std::string low = name; for (auto &c : low) c = (char)tolower((unsigned char)c);
        auto ends = [&](const char *x) { size_t m = strlen(x); return low.size() >= m && !low.compare(low.size() - m, m, x); };
        if (name.find("..") != std::string::npos || name.empty() || name[0] == '/' || name[0] == '\\' || (name.size() > 1 && name[1] == ':')) { swprintf_s(b, T(L"Nom dangereux dans le zip : « %s »", L"Dangerous name in the zip: \"%s\""), wn.c_str()); why = b; return false; }
        if (!dirEntry && !ends(".txt") && !ends(".log") && !ends(".json")) { swprintf_s(b, T(L"Pas un zip de journaux : « %s » n'est ni .txt, ni .log, ni .json", L"Not a logs zip: \"%s\" is not .txt, .log or .json"), wn.c_str()); why = b; return false; }
        o += 46 + nameLen + extraLen + comLen;
    }
    if (total > 64ull * 1024 * 1024) { swprintf_s(b, T(L"Trop gros une fois décompressé : %s (64 Mo au plus)", L"Too big once unpacked: %s (64 MB at most)"), SaveSizeText(total).c_str()); why = b; return false; }
    return true;
}
static int SigZpAddPath(const std::wstring &path, std::wstring &why)   // 1 ajoute, 0 deja la, -1 refuse (why)
{
    for (auto &e : g_sigExt) if (!_wcsicmp(e.c_str(), path.c_str())) return 0;
    if (g_sigExt.size() >= 16) { why = ST("er_trop-de-zips"); return -1; }
    if (!g_sigTest && !g_sigCapture && !SigZipInspect(path, why)) return -1;
    if (g_sigTest && !SigZipInspect(path, why)) return -1;   // (essais : les zips factices existent vraiment)
    g_sigExt.push_back(path);
    for (auto &f : g_sigForm) if (f.type == "zips") f.err.clear();
    return 1;
}
static void SigZpAddChecked()   // « Ajouter » : chaque zip coche est verifie ; les refuses restent dans la liste avec leur raison
{
    int added = 0, bad = 0; std::wstring firstWhy;
    for (auto &it : g_zpItems) {
        if (it.dir || !it.checked) continue;
        bool already = false; for (auto &e : g_sigExt) if (!_wcsicmp(e.c_str(), it.path.c_str())) already = true;
        if (already) continue;
        std::wstring why; int r = SigZpAddPath(it.path, why);
        if (r == 1) added++; else if (r < 0) { bad++; it.bad = why; it.checked = false; if (firstWhy.empty()) firstWhy = why; }
    }
    if (bad) { wchar_t b[400]; swprintf_s(b, T(L"%d zip(s) ajouté(s), %d refusé(s) : %s", L"%d zip(s) added, %d refused: %s"), added, bad, firstWhy.c_str()); g_zpNote = b; g_zpNoteKind = SC_BAD; }
    else { g_zpOpen = false; if (added) { wchar_t b[120]; swprintf_s(b, T(L"%d zip(s) ajouté(s) au dossier", L"%d zip(s) added to the report"), added); SoftNotify(b, SC_OK); } }
    InvalidateAll();
}
static void SigZpBrowse()   // « Parcourir… » : la boite Windows (selection multiple), bouchon en essai et en capture
{
    std::vector<std::wstring> picked;
    if (g_sigTest || g_sigCapture) picked.push_back(g_sigCapture ? L"C:\\factice\\ailleurs.zip" : g_zpFake + L"ailleurs.zip");
    else {
        std::vector<wchar_t> buf(32768, 0);
        OPENFILENAMEW of = { sizeof(of) };
        of.hwndOwner = g_wnd; of.lpstrFilter = L"zip\0*.zip\0"; of.lpstrFile = buf.data(); of.nMaxFile = (DWORD)buf.size();
        of.lpstrTitle = T(L"Choisir le zip des journaux d'un autre joueur", L"Choose another player's logs zip");
        of.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_HIDEREADONLY | OFN_ALLOWMULTISELECT | OFN_EXPLORER;
        if (!GetOpenFileNameW(&of)) return;
        const wchar_t *p = buf.data(); std::wstring first = p; p += first.size() + 1;
        if (!*p) picked.push_back(first);   // (un seul : le chemin complet)
        else for (; *p; p += wcslen(p) + 1) picked.push_back(WithSlash(first) + p);
    }
    int added = 0; std::wstring why, firstWhy;
    for (auto &f : picked) { int r = SigZpAddPath(f, why); if (r == 1) added++; else if (r < 0 && firstWhy.empty()) firstWhy = why; }
    if (!firstWhy.empty()) { g_zpNote = firstWhy; g_zpNoteKind = SC_BAD; SigZpScan(); }
    else { g_zpOpen = false; if (added) { wchar_t b[120]; swprintf_s(b, T(L"%d zip(s) ajouté(s) au dossier", L"%d zip(s) added to the report"), added); SoftNotify(b, SC_OK); } }
    InvalidateAll();
}
static bool SigZipPickWheel(float step) { if (!g_zpOpen || g_tab != TAB_BUG) return false; g_zpScroll = max(0.0f, g_zpScroll + step); InvalidateAll(); return true; }
static bool SigZpClick(int id)
{
    if (!g_zpOpen) return false;
    if (id == UI_SIG_ZP_CLOSE) { g_zpOpen = false; return true; }
    if (id == UI_SIG_ZP_ADD) { SigZpAddChecked(); return true; }
    if (id == UI_SIG_ZP_BROWSE) { SigZpBrowse(); return true; }
    if (id == UI_SIG_ZP_UP) { if (g_zpView == 1) { std::wstring d = g_zpDir; if (d.size() > 3) { d.pop_back(); size_t k = d.find_last_of(L'\\'); if (k != std::wstring::npos && k >= 2) SigZpGo(d.substr(0, k + 1)); } } return true; }
    if (id >= UI_SIG_ZP_SHORT && id < UI_SIG_ZP_SHORT + 5) { int k = id - UI_SIG_ZP_SHORT; if (k == 0) { g_zpView = 0; g_zpNote.clear(); SigZpScan(); } else SigZpGo(SigZpKnown(k - 1)); return true; }
    if (id >= UI_SIG_ZP_CRUMB && id < UI_SIG_ZP_CRUMB + 8) {   // fil d'Ariane : le segment k du dossier
        int k = id - UI_SIG_ZP_CRUMB; std::wstring d = g_zpDir; size_t pos = 0;
        for (int i = 0; i <= k; i++) { pos = d.find(L'\\', pos); if (pos == std::wstring::npos) return true; pos++; }
        SigZpGo(d.substr(0, pos)); return true;
    }
    if (id >= UI_SIG_ZP_ROW && id < UI_SIG_ZP_ROW + 80) {
        int k = id - UI_SIG_ZP_ROW; if (k >= (int)g_zpItems.size()) return true;
        ZpItem &it = g_zpItems[k];
        if (it.dir) { SigZpGo(it.path); return true; }
        bool already = false; for (auto &e : g_sigExt) if (!_wcsicmp(e.c_str(), it.path.c_str())) already = true;
        if (already) return true;
        it.checked = !it.checked; it.bad.clear(); g_zpNote.clear();
        return true;
    }
    return id == UI_NONE;
}
static float SigPara(Graphics &g, const std::wstring &t, float x, float y, float w, float px, Color c, int style = FontStyleRegular);   // (plus bas)
static float SigBtnW(const std::wstring &s, bool icon);
static void SigDrawZipPick(Graphics &g, const RectF &card)   // par-dessus la carte de la page ; rien en dessous ne repond
{
    if (!g_zpOpen) return;
    HotZone(RectF(g_pageR.X, 0, g_pageR.Width, 800), UI_NONE);
    SolidBrush veil(Color(g_dark ? 150 : 110, 0, 0, 0)); g.FillRectangle(&veil, card);
    float w = min(800.0f, card.Width - 40), h = min(620.0f, card.Height - 24);
    RectF c(card.X + (card.Width - w) / 2, card.Y + (card.Height - h) / 2, w, h);
    SoftCard(g, c, kSoftRCard);
    float x = c.X + 24, y = c.Y + 20, cw = c.Width - 48;
    Title(g, T(L"Ajouter des zips de journaux", L"Add logs zips"), RectF(x, y, cw - 50, 30), 18, g_soft.ink);
    SoftButton(g, RectF(c.X + c.Width - 24 - 40, y - 2, 40, 36), L"", UI_SIG_ZP_CLOSE, false, IC_CLOSE, true, 18); HotZone(RectF(c.X + c.Width - 24 - 40, y - 2, 40, 36), UI_SIG_ZP_CLOSE);
    y += 34;
    y += SigPara(g, T(L"Les zips des journaux d'un autre joueur (son bouton Journaux → Créer un zip, ou le tien envoyé par Discord). Coche ceux à joindre : chacun est vérifié avant l'envoi.", L"Another player's logs zips (their Logs → Create a zip button, or yours sent over Discord). Tick the ones to attach: each one is checked before sending."), x, y, cw, 12, g_soft.muted) + 10;
    {   // raccourcis : Recents, Bureau, Telechargements, Documents, MWCoop
        float bx = x;
        for (int k = 0; k < 5; k++) {
            std::wstring s = k == 0 ? T(L"Récents", L"Recent") : SigZpKnownName(k - 1); float bw = SigBtnW(s, k == 0) - 6;
            bool cur = k == 0 ? g_zpView == 0 : g_zpView == 1 && !_wcsicmp(g_zpDir.c_str(), SigZpKnown(k - 1).c_str());
            SoftButton(g, RectF(bx, y, bw, 34), s, UI_SIG_ZP_SHORT + k, cur, k == 0 ? IC_SPARK : -1, true, 17); HotZone(RectF(bx, y, bw, 34), UI_SIG_ZP_SHORT + k);
            bx += bw + 8;
        }
        y += 34 + 10;
    }
    if (g_zpView == 1) {   // fil d'Ariane : C:\ › Users › JD › Downloads, chaque segment cliquable, et « Dossier parent »
        float bx = x; std::wstring d = g_zpDir; size_t pos = 0; int k = 0;
        while (pos < d.size() && k < 8) {
            size_t e = d.find(L'\\', pos); if (e == std::wstring::npos) break;
            std::wstring seg = d.substr(pos, e - pos); if (seg.empty()) { pos = e + 1; continue; }
            bool last = e + 1 >= d.size();
            float sw = MeasureW(MeasureG(), seg, 12.5f, last ? FontStyleBold : FontStyleRegular) + 10;
            if (bx + sw > x + cw - 120) { Text(g, L"…", RectF(bx, y, 16, 24), 12.5f, FontStyleRegular, g_soft.muted, StringAlignmentNear); break; }
            Text(g, seg, RectF(bx, y, sw, 24), 12.5f, last ? FontStyleBold : FontStyleRegular, last ? g_soft.ink : g_soft.muted, StringAlignmentNear);
            if (!last) HotZone(RectF(bx, y, sw, 24), UI_SIG_ZP_CRUMB + k);
            bx += sw; if (!last) { Text(g, L"›", RectF(bx, y, 14, 24), 12.5f, FontStyleRegular, g_soft.faint, StringAlignmentCenter); bx += 14; }
            pos = e + 1; k++;
        }
        std::wstring up = T(L"Dossier parent", L"Parent folder"); float uw = SigBtnW(up, true) - 6;
        SoftButton(g, RectF(x + cw - uw, y - 5, uw, 32), up, UI_SIG_ZP_UP, false, IC_CHEVL, g_zpDir.size() > 3, 16); HotZone(RectF(x + cw - uw, y - 5, uw, 32), UI_SIG_ZP_UP);
        y += 30;
    }
    // la liste (defile a la molette)
    float bottomH = 58 + (g_zpNote.empty() ? 0 : 26);
    RectF list(x, y, cw, c.Y + c.Height - bottomH - y);
    g_zpListR = list;
    float rowH = 44, totalH = (float)g_zpItems.size() * rowH;
    g_zpScroll = max(0.0f, min(g_zpScroll, max(0.0f, totalH - list.Height)));
    GraphicsState st = g.Save(); g.SetClip(list, CombineModeIntersect);
    if (g_zpItems.empty()) SigPara(g, g_zpView == 0 ? T(L"Aucun zip récent sur le Bureau, dans Téléchargements, Documents ou MWCoop. Ouvre un dossier au-dessus, ou Parcourir…", L"No recent zip on the Desktop, in Downloads, Documents or MWCoop. Open a folder above, or Browse…") : T(L"Aucun zip ni sous-dossier ici.", L"No zip or subfolder here."), x, y + 8, cw, 13, g_soft.muted);
    for (int k = 0; k < (int)g_zpItems.size(); k++) {
        ZpItem &it = g_zpItems[k];
        RectF r(x, y + k * rowH - g_zpScroll, cw, rowH);
        if (r.Y + rowH < list.Y || r.Y > list.Y + list.Height) continue;
        bool already = false; if (!it.dir) for (auto &e : g_sigExt) if (!_wcsicmp(e.c_str(), it.path.c_str())) already = true;
        if (it.dir) { Icon(g, IC_FOLDER, r.X + 26, r.Y + rowH / 2, 20, g_soft.ink, 1.8f); Text(g, it.name, RectF(r.X + 60, r.Y + 11, cw - 70, 22), 13.5f, FontStyleBold, g_soft.ink, StringAlignmentNear); }
        else {
            SoftToggle(g, RectF(r.X, r.Y + 7, 52, 30), it.checked || already, UI_SIG_ZP_ROW + k, !already);
            std::wstring t = it.name + L" · " + SigDateOf(it.mt) + L" · " + SaveSizeText(it.bytes);
            if (!it.where.empty()) t += L" · " + it.where;
            if (already) t += T(L" · déjà ajouté", L" · already added");
            bool bad = !it.bad.empty();
            Text(g, t, RectF(r.X + 64, r.Y + (bad ? 3 : 11), cw - 70, 22), 13, it.checked || already ? FontStyleBold : FontStyleRegular, bad ? g_soft.bad : already ? g_soft.muted : g_soft.ink, StringAlignmentNear);
            if (bad) Text(g, it.bad, RectF(r.X + 64, r.Y + 22, cw - 70, 20), 11.5f, FontStyleRegular, g_soft.bad, StringAlignmentNear);
        }
        if (r.Y >= list.Y - rowH && r.Y + rowH <= list.Y + list.Height + rowH) { RectF hz = r; if (hz.Y < list.Y) { hz.Height -= list.Y - hz.Y; hz.Y = list.Y; } if (hz.Y + hz.Height > list.Y + list.Height) hz.Height = list.Y + list.Height - hz.Y; if (hz.Height > 4) HotZone(hz, UI_SIG_ZP_ROW + k); }
    }
    g.Restore(st);
    // en bas : note, total, Ajouter (n), Parcourir…, Annuler
    y = c.Y + c.Height - bottomH + 4;
    if (!g_zpNote.empty()) { y += SigPara(g, g_zpNote, x, y, cw, 12.5f, g_zpNoteKind == SC_BAD ? g_soft.bad : g_soft.ink, FontStyleBold) + 4; }
    int n = 0; long long sz = 0; for (auto &it : g_zpItems) if (!it.dir && it.checked) { bool already = false; for (auto &e : g_sigExt) if (!_wcsicmp(e.c_str(), it.path.c_str())) already = true; if (!already) { n++; sz += it.bytes; } }
    long long total, big; int nz; SigZipCheck(&total, &nz, &big);
    wchar_t tb[200]; swprintf_s(tb, T(L"%d coché(s), %s · avec le dossier : %s en %d zip(s) · 15 Mo par zip, 16 zips, 40 Mo en tout", L"%d ticked, %s · with the report: %s in %d zip(s) · 15 MB per zip, 16 zips, 40 MB in all"), n, SaveSizeText(sz).c_str(), SaveSizeText(total + sz).c_str(), nz + n);
    std::wstring ab = T(L"Ajouter", L"Add"); if (n) { wchar_t nb[16]; swprintf_s(nb, L" (%d)", n); ab += nb; } float aw = SigBtnW(ab, true);
    std::wstring pb = T(L"Parcourir…", L"Browse…"); float pw = SigBtnW(pb, true);
    std::wstring cb = T(L"Annuler", L"Cancel"); float cww = SigBtnW(cb, false);
    bool over = total + sz > g_sigEnvoiMax || nz + n > 16;
    SigPara(g, tb, x, y + 2, cw - aw - pw - cww - 30, 11.5f, over ? g_soft.bad : g_soft.muted);   // (deux lignes si la place manque)
    float bx = x + cw - aw - pw - cww - 20;
    SoftButton(g, RectF(bx, y, aw, 42), ab, UI_SIG_ZP_ADD, true, IC_CHECK, n > 0 && !over); HotZone(RectF(bx, y, aw, 42), UI_SIG_ZP_ADD); bx += aw + 10;
    SoftButton(g, RectF(bx, y, pw, 42), pb, UI_SIG_ZP_BROWSE, false, IC_FOLDER); HotZone(RectF(bx, y, pw, 42), UI_SIG_ZP_BROWSE); bx += pw + 10;
    SoftButton(g, RectF(bx, y, cww, 42), cb, UI_SIG_ZP_CLOSE, false); HotZone(RectF(bx, y, cww, 42), UI_SIG_ZP_CLOSE);
}
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
        if (x.champ == "version") { int gm = g_game; for (auto &f : g_sigForm) if (f.champ == "jeu" && f.sel >= 0 && f.sel < (int)f.opts.size()) gm = f.opts[f.sel].first == "msc" ? GAME_MSC : GAME_MWC; g_sigVerAuto = SigVersionAuto(gm); x.val = g_sigVerAuto; }   // (msc- / mwc- + version du mod de CE jeu)
        if (x.champ == "zips") x.on = true;
        g_sigForm.push_back(x);
    }
    if (!g_sigTest) { g_sigExt.clear(); SigSessScan(SigGameOfForm(), false); }   // (le selecteur de journaux suit le jeu du formulaire)
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
        else if (f.type == "zips") { long long t, b; int n; int c = SigZipCheck(&t, &n, &b); if (c == 1) f.err = T(L"Coche au moins une partie, ou ajoute un zip.", L"Tick at least one game session, or add a zip."); else if (c == 2) f.err = ST("er_trop-de-zips") + L" " + T(L"Décoche une partie.", L"Untick a session."); else if (c == 3) f.err = ST("er_envoi-trop-gros") + L" " + T(L"Décoche une partie.", L"Untick a session."); }
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
    if (g_tab == TAB_BUG && SigActive() && g_sigView == SV_CODE) SoftBlink(1000, 3);   // (compte a rebours : une image par seconde)
    else if (g_tab == TAB_BUG && SigActive() && g_sigBusy) SoftBlink(250, 4);   // (barre d'envoi)
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
    SetStatus(K_NORMAL, T(L"Signaler un bug : tes dossiers et le formulaire, ici dans le lanceur · les journaux des parties se cochent sur place",
                          L"Report a bug: your reports and the form, right here in the launcher · the game sessions' logs are ticked right there"));
}
static void SigOpenList() { g_sigView = SV_LIST; g_sigSel = -1; g_sigAddMode = 0; g_sigFocus = -1; g_scroll[TAB_BUG] = 0; if (!g_sigMesOk && !g_sigBusy) SigGo(SOP_MES); }
static void SigNewForm()
{
    if (!g_sigDepotOuvert) { SigNote(ST("dpFerme"), SC_WARN); return; }
    SigFormBuild(); g_sigView = SV_FORM; g_sigFocus = -1; g_sigNote.clear(); g_scroll[TAB_BUG] = 0;
}
static void SigSendForm()
{
    int bad = SigValidate();
    if (bad >= 0) { SigNote(ST("erCorriger"), SC_BAD); g_scroll[TAB_BUG] = max(0.0f, g_sigForm[bad].r.Y - 180 + g_scroll[TAB_BUG]); return; }
    SigJob j; j.kind = SOP_DEPOSER; j.body = SigFormJson(); if (!g_sigCapture) j.zips = SigZipSpecs(false);   // (les parties cochees, les communs avec la premiere, les zips d'un autre joueur)
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
    if (g_sigAddZip && g_sigZipFiles > 0 && !g_sigCapture) { SigSessScan(g_sigMes[g_sigSel].jeu == "msc" ? GAME_MSC : GAME_MWC, false); j.zips = SigZipSpecs(true); }   // (ajout : la derniere partie du jeu du dossier, et les communs)
    g_sigView = SV_SEND; g_sigFocus = -1; g_sigNote.clear();
    SigStart(j);
}
static void SigOpenPage()
{
    std::wstring u = g_sigCodeUrl.empty() ? kSigPage : g_sigCodeUrl;
    LaunchLog("signalement : ouverture de la page de connexion dans le navigateur");
    g_sigOpenFailed = !g_sigTest && !g_sigCapture && !ShellOpen(u);   // (echec : l'adresse reste a copier, message sous le bouton)
    if (g_sigOpenFailed) SoftNotify(T(L"La page ne s'est pas ouverte : copie l'adresse dans ton navigateur", L"The page didn't open: copy the address into your browser"), SC_WARN);
    InvalidateAll();
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
    if (g_zpOpen) { if (ch == 27) { g_zpOpen = false; InvalidateAll(); } return true; }   // (fenetre de choix des zips : Echap la ferme)
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
static float SigPara(Graphics &g, const std::wstring &t, float x, float y, float w, float px, Color c, int style)   // rend la hauteur
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
    y += 46 + 16;
    {   // l'adresse en clair, a copier (JD 27/10, Zen Browser : le navigateur ne s'ouvrait pas)
        std::wstring u = g_sigCodeUrl.empty() ? kSigPage : g_sigCodeUrl, shown = u.compare(0, 8, L"https://") == 0 ? u.substr(8) : u;
        std::wstring lab = T(L"Adresse de la page : ", L"Page address: ");
        float lw = MeasureW(MeasureG(), lab, 13, FontStyleRegular), sw = MeasureW(MeasureG(), shown, 13, FontStyleBold);
        Text(g, lab, RectF(x, y + 4, lw + 4, 22), 13, FontStyleRegular, g_soft.muted, StringAlignmentNear);
        Text(g, shown, RectF(x + lw + 2, y + 4, sw + 6, 22), 13, FontStyleBold, g_soft.ink, StringAlignmentNear);
        std::wstring cpb = T(L"Copier l'adresse", L"Copy the address"); float cpw = SigBtnW(cpb, false);
        float cx2 = min(x + lw + sw + 20, x + w - cpw);
        SoftButton(g, RectF(cx2, y - 4, cpw, 38), cpb, UI_SIG_COPYURL, false, -1, true, 19); SigHot(RectF(cx2, y - 4, cpw, 38), UI_SIG_COPYURL, clip);
        y += 40;
        if (g_sigOpenFailed) y += SigPara(g, T(L"La page ne s'est pas ouverte ? Copie l'adresse dans ton navigateur, connecte-toi avec Discord et tape le code.", L"The page didn't open? Copy the address into your browser, sign in with Discord and type the code."), x, y, w, 13, g_soft.warn, FontStyleBold) + 8;
    }
    y += 4;
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
    if (focus) SoftBlinkAt(r, 500, 0);
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
        if (f.type == "zips") {   // selecteur de journaux (JD 27/10) : les parties du jeu choisi, cochables ; zips d'un autre joueur ; taille estimee
            float top = y;
            y += SigPara(g, T(L"Journaux à joindre", L"Logs to attach"), x, y, w, 13.5f, bad ? g_soft.bad : g_soft.ink, FontStyleBold) + 4;
            y += SigPara(g, T(L"Les parties enregistrées du jeu choisi en haut ; la dernière est cochée d'office. Coche celles où le problème est arrivé : les réglages et le journal du lanceur sont joints avec (clé masquée), rien d'autre à faire.",
                               L"The recorded game sessions of the game chosen above; the latest is ticked for you. Tick the ones where the problem happened: the settings and the launcher log are attached with them (key masked), nothing else to do."), x, y, w, 12, g_soft.muted) + 8;
            if (g_sigSess.empty()) y += SigPara(g, T(L"Aucune partie enregistrée pour ce jeu : lance-le une fois avec MWCoop. Les réglages et le journal du lanceur sont joints quand même.", L"No recorded game session for this game: start it once with MWCoop. The settings and the launcher log are attached anyway."), x, y, w, 12.5f, g_soft.muted, FontStyleBold) + 8;
            for (int k = 0; k < (int)g_sigSess.size(); k++) {
                const SigSess &s = g_sigSess[k];
                RectF r(x, y, w, 52);
                SoftToggle(g, RectF(x, y + 11, 52, 30), s.checked, UI_SIG_SESS + k, true); SigHot(r, UI_SIG_SESS + k, clip);
                std::wstring title, sub; LogHeadText(s.g, title, sub);   // (le meme en-tete que la page Journaux)
                Text(g, title, RectF(x + 64, y + 6, w - 64, 22), 13.5f, FontStyleBold, s.checked ? g_soft.ink : g_soft.muted, StringAlignmentNear);
                Text(g, sub, RectF(x + 64, y + 27, w - 64, 20), 12, FontStyleRegular, g_soft.muted, StringAlignmentNear);
                y += 52;
            }
            for (int k = 0; k < (int)g_sigExt.size(); k++) {
                std::wstring n = g_sigExt[k].substr(g_sigExt[k].find_last_of(L"\\/") + 1);
                Icon(g, IC_ZIP, x + 26, y + 20, 20, g_soft.ink, 1.8f);
                std::wstring rb = T(L"Retirer", L"Remove"); float rw = SigBtnW(rb, false);
                SoftButton(g, RectF(x + w - rw, y + 3, rw, 34), rb, UI_SIG_EXT + k, false, -1, true, 17); SigHot(RectF(x + w - rw, y + 3, rw, 34), UI_SIG_EXT + k, clip);
                SigZipSpec z; z.path = g_sigExt[k];
                Text(g, n + T(L" · zip d'un autre joueur, ", L" · another player's zip, ") + SaveSizeText(SigSpecSize(z)), RectF(x + 64, y + 10, w - 64 - rw - 12, 22), 13, FontStyleBold, g_soft.ink, StringAlignmentNear);
                y += 40;
            }
            y += 6;
            std::wstring ab = T(L"Ajouter un zip d'un autre joueur…", L"Add another player's zip…"); float aw = SigBtnW(ab, true);
            SoftButton(g, RectF(x, y, aw, 38), ab, UI_SIG_ADDEXT, false, IC_PLUS, (int)g_sigExt.size() < 16, 19); SigHot(RectF(x, y, aw, 38), UI_SIG_ADDEXT, clip);
            y += 38 + 10;
            long long total, big; int nz; int c = SigZipCheck(&total, &nz, &big);
            wchar_t eb[320];
            if (c == 1) swprintf_s(eb, L"%s", T(L"Rien à joindre pour l'instant : coche au moins une partie, ou ajoute un zip.", L"Nothing to attach yet: tick at least one game session, or add a zip."));
            else swprintf_s(eb, T(L"Taille estimée : %s en %d zip(s) · limites du site : %s par zip, 16 zips, %s en tout%s", L"Estimated size: %s in %d zip(s) · site limits: %s per zip, 16 zips, %s in all%s"), SaveSizeText(total).c_str(), nz, SaveSizeText(g_sigZipMax).c_str(), SaveSizeText(g_sigEnvoiMax).c_str(),
                            big > g_sigZipMax - 256 * 1024 ? T(L" · un zip dépasse : ses plus gros journaux seront coupés", L" · one zip is over: its biggest logs will be cut") : L"");
            y += SigPara(g, eb, x, y, w, 12.5f, c ? g_soft.bad : g_soft.muted, c ? FontStyleBold : FontStyleRegular) + 4;
            f.r = RectF(x, top, w, y - top);
        } else if (f.type == "case") {
            RectF r(x, y, w, 44);
            bool on = f.on;
            SoftToggle(g, RectF(x, y + 7, 52, 30), on, id0 + 9, true); SigHot(r, id0 + 9, clip);
            std::wstring t = lab;
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
    SoftBlink(250, 4);
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
    SigDrawZipPick(g, card);   // (fenetre integree de choix des zips, par-dessus la carte)
    if (g_sigBusy && g_sigView != SV_SEND && g_sigView != SV_LOADING) {   // (une operation en cours : un fil discret en bas de la carte)
        SigLock(); float fr = g_sigFrac; SigUnlock();
        SoftProgress(g, RectF(card.X + 24, card.Y + card.Height - 12, card.Width - 48, 6), fr); SoftBlinkAt(RectF(card.X, card.Y + card.Height - 24, card.Width, 24), 250, 4);
    }
}
static float SigMaxScroll() { return max(0.0f, g_sigContentH - 656); }
static bool SigClick(int id)
{
    if (g_zpOpen && SigZpClick(id)) { InvalidateAll(); return true; }   // (fenetre de choix des zips, par-dessus la page)
    if (id < UI_SIG || id >= UI_SIG + 1000) return false;
    if (id == UI_SIG_BROWSER) {
        std::wstring url = std::wstring(kBugSiteUrl) + L"?lang=" + LangCode();
        LaunchLog("signalement : site ouvert dans le navigateur (page native)");
        if (!g_sigTest && !g_sigCapture) ShellOpen(url.c_str());
        return true;
    }
    if (id == UI_SIG_OPENPAGE) { SigOpenPage(); return true; }   // (pendant l'interrogation du jeton -- g_sigBusy -- le bouton reste actif : JD 27/10, « rien ne se passe »)
    if (id == UI_SIG_COPYURL) { std::wstring u = g_sigCodeUrl.empty() ? kSigPage : g_sigCodeUrl; SoftNotify(CopyText(u) ? T(L"Adresse copiée", L"Address copied") : T(L"Copie impossible", L"Could not copy"), SC_OK); return true; }
    if (g_sigBusy && id != UI_SIG_CANCEL) return true;
    g_sigNote.clear();
    switch (id) {
    case UI_SIG_LOGIN: case UI_SIG_NEWCODE: g_sigCodeState = 0; g_sigOpenFailed = false; if (!g_sigCapture) SigGo(SOP_CODE); return true;
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
        else if (k == 9) f.on = !f.on;
        if (f.champ == "jeu" && k >= 1 && k <= 7) {   // le selecteur de journaux et la version suivent le jeu (JD 27/10 : « mwc-9.9.9 » avec My Summer Car)
            if (SigGameOfForm() != g_sigSessGame) SigSessScan(SigGameOfForm(), true);
            std::wstring v = SigVersionAuto(SigGameOfForm());
            for (auto &x : g_sigForm) if (x.champ == "version" && (Trim(x.val).empty() || x.val == g_sigVerAuto)) { x.val = v; x.err.clear(); }
            g_sigVerAuto = v;
        }
        return true;
    }
    if (id >= UI_SIG_SESS && id < UI_SIG_SESS + 30) { int k = id - UI_SIG_SESS; if (k < (int)g_sigSess.size()) { g_sigSess[k].checked = !g_sigSess[k].checked; for (auto &f : g_sigForm) if (f.type == "zips") f.err.clear(); } return true; }
    if (id >= UI_SIG_EXT && id < UI_SIG_EXT + 16) { int k = id - UI_SIG_EXT; if (k < (int)g_sigExt.size()) g_sigExt.erase(g_sigExt.begin() + k); return true; }
    if (id == UI_SIG_ADDEXT) { SigZpOpen(); return true; }
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
    else if (st == L"signaler-natif-code-echec") { g_sigView = SV_CODE; g_sigCode = L"K7QF-M2XP"; g_sigCodeT0 = GetTickCount() - 73000; g_sigCodeExpire = 600; g_sigOpenFailed = true; }
    else if (st == L"signaler-natif-liste") g_sigView = SV_LIST;
    else if (st == L"signaler-natif-dossier") { g_sigView = SV_DETAIL; g_sigSel = 0; }
    else if (st == L"signaler-natif-dossier-ajout") { g_sigView = SV_DETAIL; g_sigSel = 2; g_sigAddMode = 1; g_sigAddText = L"Ça arrive aussi avec la Ferndale, dès que l'invité ouvre le coffre.\nVersion mwc-1.15.10, salon Steam à 3."; }
    else if (st == L"signaler-natif-formulaire") { SigFormBuild(); g_sigView = SV_FORM; for (auto &f : g_sigForm) { if (f.champ == "titre") f.val = L"Roue qui tombe chez l'invité"; if (f.champ == "type") f.sel = 2; if (f.champ == "probleme") f.val = L"Après un saut, la roue avant gauche tombe chez l'invité mais pas chez l'hôte."; if (f.champ == "role") f.sel = 2; if (f.champ == "joueurs") f.val = L"2"; } g_sigForm[1].err.clear(); }
    else if (st == L"signaler-natif-formulaire-bas") { SigFormBuild(); g_sigView = SV_FORM; for (auto &f : g_sigForm) if (f.champ == "mods_choix") f.sel = 1; if (g_sigSess.size() > 1) g_sigSess[1].checked = true; SigAddExt(); DrawSigPage(MeasureG()); g_scroll[TAB_BUG] = SigMaxScroll(); }   // (le bas du formulaire : detail des mods, selecteur de journaux, anonyme, Envoyer)
    else if (st == L"signaler-natif-zips") { SigFormBuild(); g_sigView = SV_FORM; SigAddExt(); DrawSigPage(MeasureG()); for (auto &f : g_sigForm) if (f.type == "zips") g_scroll[TAB_BUG] = max(0.0f, min(SigMaxScroll(), f.r.Y - 150 + g_scroll[TAB_BUG])); SigZpOpen(); }   // (la fenetre de choix des zips)
    else if (st == L"signaler-natif-formulaire-journaux") { SigFormBuild(); g_sigView = SV_FORM; if (g_sigSess.size() > 1) g_sigSess[1].checked = true; SigAddExt(); DrawSigPage(MeasureG()); for (auto &f : g_sigForm) if (f.type == "zips") g_scroll[TAB_BUG] = max(0.0f, min(SigMaxScroll(), f.r.Y - 150 + g_scroll[TAB_BUG])); }   // (le selecteur de journaux)
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
    g_sigZipFiles = 2; g_sigSess.clear(); g_sigExt.clear(); g_sigCommon = { log, g_iniLauncher };
    check(SigValidate() < 0, "validation locale du formulaire", r);
    {   // selecteur de journaux : parties factices, cochage, zips multiples, limites du site vues en local
        auto mk = [&](const wchar_t *key, bool checked) { SigSess s; s.g.key = key; s.paths = { log }; s.g.files = 1; s.checked = checked; return s; };
        g_sigSess = { mk(L"*", true), mk(L"a", false), mk(L"b", false) };
        std::vector<SigZipSpec> z1 = SigZipSpecs(false);
        check(z1.size() == 1 && z1[0].files.size() == 3, "selecteur : derniere partie cochee d'office -> 1 zip (partie + 2 communs)", r);
        g_sigSess[1].checked = g_sigSess[2].checked = true;
        std::vector<SigZipSpec> z3 = SigZipSpecs(false);
        check(z3.size() == 3 && z3[0].files.size() == 3 && z3[1].files.size() == 1 && z3[2].files.size() == 1, "selecteur : 3 parties cochees -> 3 zips, communs avec la premiere seulement", r);
        for (int i = 0; i < 14; i++) SigAddExt();
        long long t, b; int n;
        check(SigZipCheck(&t, &n, &b) == 2 && n == 17 && SigValidate() >= 0, "limites : 17 zips refuses en local (16 au plus)", r);
        g_sigExt.clear(); long long keepMax = g_sigEnvoiMax; g_sigEnvoiMax = 100;
        check(SigZipCheck(&t, &n, &b) == 3 && SigValidate() >= 0, "limites : total trop gros refuse en local", r);
        g_sigEnvoiMax = keepMax;
        for (auto &s : g_sigSess) s.checked = false;
        check(SigZipCheck(&t, &n, &b) == 1 && SigValidate() >= 0, "rien de coche : refuse en local", r);
        g_sigSess.clear(); g_sigExt.clear();
        check(SigZipCheck(&t, &n, &b) == 0 && n == 1 && SigValidate() < 0, "aucune partie enregistree : les communs seuls, accepte", r);
        // jeu du formulaire : le selecteur suit le choix (scan reel sur cette instance, sans partie : au moins les communs)
        for (auto &f : g_sigForm) if (f.champ == "jeu") { f.sel = 0; SigSessScan(SigGameOfForm(), false); int g0 = g_sigSessGame; f.sel = 1; SigSessScan(SigGameOfForm(), false); check(g0 == GAME_MWC && g_sigSessGame == GAME_MSC, "selecteur : suit le jeu choisi (mwc puis msc)", r); }
        LaunchLog("testsignalnatif : selecteur reel sur cette instance : %d partie(s), %d fichier(s) commun(s)", (int)g_sigSess.size(), (int)g_sigCommon.size());
        {   // JD 27/10 : « ça c'est ma liste réelle » -- le selecteur = les parties de la page Journaux (meme regroupement, meme en-tete, meme tri) :
            // un faux dossier de jeu (profils partie-1 / 2 / 3 avec leurs journaux en place, trois parties rangees), LogsScan des deux cotes
            std::wstring root = log + L".jeu\\", dirWas = g_gameDir, rootWas = g_modRoot; std::vector<SigField> formWas = g_sigForm;   // (le formulaire rempli est rendu a la fin)
            auto mkdir = [](const std::wstring &d) { CreateDirectoryW(d.c_str(), NULL); };
            auto put = [](const std::wstring &p, const char *txt) { HANDLE f = CreateFileW(p.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL); if (f != INVALID_HANDLE_VALUE) { DWORD w; WriteFile(f, txt, (DWORD)strlen(txt), &w, NULL); CloseHandle(f); } };
            mkdir(root); mkdir(root + L"MWCoop"); mkdir(root + L"MWCoop\\logs"); mkdir(root + L"MWCoop\\logs\\sessions"); mkdir(root + L"MWCoop\\profils");
            put(root + L"MWCoop\\logs\\mwcoop.log", "18:11:05 MWCoop 1.15.14, hote sur le port 7777\nx\njeu ferme normalement\n");
            put(root + L"MWCoop\\logs\\chargeur.log", "18:11:00 chargeur\n");
            for (int k = 1; k <= 3; k++) { std::wstring pr = root + L"MWCoop\\profils\\partie-" + std::to_wstring(k) + L"\\"; mkdir(pr); mkdir(pr + L"logs"); put(pr + L"logs\\mwcoop.log", "18:12:00 MWCoop 1.15.14, hote sur le port 7777\njeu ferme normalement\n"); }
            const wchar_t *dates[3] = { L"2026-10-25_18h11m00", L"2026-10-26_21h02m10", L"2026-10-27_09h30m00" };
            for (int k = 0; k < 3; k++) { std::wstring sd = root + L"MWCoop\\logs\\sessions\\" + dates[k] + L"\\"; mkdir(sd); put(sd + L"mwcoop.log", k == 1 ? "21:02:10 MWCoop 1.15.13, connexion a 1.2.3.4\njeu ferme normalement\n" : "09:30:00 MWCoop 1.15.12\njeu ferme normalement\n"); put(sd + L"chargeur.log", "x\n"); }
            g_gameDir = root; g_modRoot = root;
            LogsScan();
            std::vector<std::wstring> pageTitles, pageSubs; std::vector<int> pageFiles; std::vector<uint64_t> pageBytes;
            for (auto &gr : g_logGroups) { std::wstring t, su; LogHeadText(gr, t, su); pageTitles.push_back(t); pageSubs.push_back(su); pageFiles.push_back(gr.files); pageBytes.push_back(gr.bytes); }
            g_sigForm.clear(); SigSessScan(g_game, false);
            bool same = g_sigSess.size() == g_logGroups.size() && g_logGroups.size() == 4;
            for (size_t k = 0; same && k < g_sigSess.size(); k++) { std::wstring t, su; LogHeadText(g_sigSess[k].g, t, su); same = t == pageTitles[k] && su == pageSubs[k] && g_sigSess[k].g.files == pageFiles[k] && g_sigSess[k].g.bytes == pageBytes[k] && (int)g_sigSess[k].paths.size() == pageFiles[k]; }
            check(same, "selecteur = page Journaux : 4 parties (derniere + 3 rangees), memes en-tetes, memes journaux, memes tailles, meme ordre", r);
            bool order = g_sigSess.size() == 4 && g_sigSess[0].g.key == L"*" && g_sigSess[0].g.files == 5 && g_sigSess[1].g.dir.find(dates[2]) != std::wstring::npos && g_sigSess[2].g.dir.find(dates[1]) != std::wstring::npos && g_sigSess[3].g.dir.find(dates[0]) != std::wstring::npos && g_sigSess[0].checked && !g_sigSess[1].checked;
            check(order, "ordre : derniere partie (5 journaux : partie-1/2/3 + logs) puis 27/10, 26/10, 25/10 ; la derniere cochee d'office", r);
            bool head = g_sigSess.size() == 4 && pageTitles[0].find(L"18h1") != std::wstring::npos && pageSubs[0].find(L"MWCoop 1.15.14") != std::wstring::npos && pageSubs[0].find(T(L"Hôte", L"Host")) != std::wstring::npos && pageSubs[2].find(T(L"Invité", L"Guest")) != std::wstring::npos && pageTitles[1].find(L"1601") == std::wstring::npos;
            check(head, "en-tete : « 18h11 », « MWCoop 1.15.14 », Hote, Invite pour la partie du 26/10, jamais 1601", r);
            for (size_t k = 0; k < pageTitles.size(); k++) LaunchLog("testsignalnatif : partie %d : %s -- %s", (int)k, Narrow(pageTitles[k], CP_UTF8).c_str(), Narrow(pageSubs[k], CP_UTF8).c_str());
            std::vector<SigZipSpec> zs = SigZipSpecs(false);
            check(zs.size() == 1 && zs[0].files.size() == 5 + g_sigCommon.size(), "zip de la derniere partie = ses 5 journaux + les communs (le pack entier)", r);
            g_gameDir = dirWas; g_modRoot = rootWas; LogsScan();
            for (int k = 0; k < 3; k++) { std::wstring sd = root + L"MWCoop\\logs\\sessions\\" + dates[k] + L"\\"; DeleteFileW((sd + L"mwcoop.log").c_str()); DeleteFileW((sd + L"chargeur.log").c_str()); RemoveDirectoryW(sd.c_str()); }
            for (int k = 1; k <= 3; k++) { std::wstring pr = root + L"MWCoop\\profils\\partie-" + std::to_wstring(k) + L"\\"; DeleteFileW((pr + L"logs\\mwcoop.log").c_str()); RemoveDirectoryW((pr + L"logs").c_str()); RemoveDirectoryW(pr.c_str()); }
            DeleteFileW((root + L"MWCoop\\logs\\mwcoop.log").c_str()); DeleteFileW((root + L"MWCoop\\logs\\chargeur.log").c_str());
            RemoveDirectoryW((root + L"MWCoop\\logs\\sessions").c_str()); RemoveDirectoryW((root + L"MWCoop\\logs").c_str()); RemoveDirectoryW((root + L"MWCoop\\profils").c_str()); RemoveDirectoryW((root + L"MWCoop").c_str()); RemoveDirectoryW(root.c_str());
            // version : suit le jeu ; anonyme : decoche d'office, valeur JSON
            SigFormBuild();
            std::wstring v0; bool anonOff = true, anonSeen = false;
            for (auto &f : g_sigForm) { if (f.champ == "version") v0 = f.val; if (f.champ == "anonyme") { anonSeen = true; anonOff = !f.on; } }
            check(anonSeen && anonOff && SigFormJson().find("\"anonyme\":false") != std::string::npos, "anonyme : decoche d'office, JSON anonyme:false", r);
            for (auto &f : g_sigForm) if (f.champ == "anonyme") f.on = true;
            check(SigFormJson().find("\"anonyme\":true") != std::string::npos, "anonyme coche : JSON anonyme:true", r);
            for (auto &f : g_sigForm) if (f.champ == "anonyme") f.on = false;
            int jeuIdx = -1; for (int k = 0; k < (int)g_sigForm.size(); k++) if (g_sigForm[k].champ == "jeu") jeuIdx = k;
            std::wstring vMwc, vMsc;
            if (jeuIdx >= 0) { SigClick(UI_SIG_FIELD + jeuIdx * 10 + 1); for (auto &f : g_sigForm) if (f.champ == "version") vMwc = f.val; SigClick(UI_SIG_FIELD + jeuIdx * 10 + 2); for (auto &f : g_sigForm) if (f.champ == "version") vMsc = f.val; }
            check(jeuIdx >= 0 && (vMwc.empty() || vMwc.rfind(L"mwc-", 0) == 0) && (vMsc.empty() || vMsc.rfind(L"msc-", 0) == 0) && vMsc.find(L"mwc-") == std::wstring::npos, "version : prefixe du jeu choisi (mwc- puis msc-), jamais mwc- avec My Summer Car", r);
            LaunchLog("testsignalnatif : version preremplie : %s ; apres MWC : %s ; apres MSC : %s", Narrow(v0, CP_UTF8).c_str(), Narrow(vMwc, CP_UTF8).c_str(), Narrow(vMsc, CP_UTF8).c_str());
            g_sigForm = formWas;
        }
        {   // JD 27/10 : « 01/01/1601 01h00 · derniere partie · 1 fichiers » -- partie sans mwcoop.log (le journal de Unity seul), accord du nombre
            bool noneZero = true; for (auto &s : g_sigSess) if (!s.g.end.dwLowDateTime && !s.g.end.dwHighDateTime) noneZero = false;
            check(noneZero, "scan reel : aucune partie datee 1601", r);
            std::wstring ulog = log + L".output_log.txt"; CopyFileW(g_iniLauncher.c_str(), ulog.c_str(), FALSE);
            SigSess s; s.paths = { ulog }; s.g.files = 1; SigSessEnd(s);
            check((s.g.end.dwLowDateTime || s.g.end.dwHighDateTime) && SigDateOf(s.g.end) != T(L"date inconnue", L"unknown date") && SigDateOf(s.g.end).find(L"1601") == std::wstring::npos, "partie avec le seul journal de Unity : datee du fichier, pas 1601", r);
            FILETIME zero = {}; SigSess z; z.paths = { log + L".absent.log" }; SigSessEnd(z);
            check(SigDateOf(zero) == T(L"date inconnue", L"unknown date") && SigDateOf(z.g.end) == T(L"date inconnue", L"unknown date"), "aucune date valide : « date inconnue »", r);
            DeleteFileW(ulog.c_str());
            bool fr = g_fr, ru = g_ru, ar = g_ar;
            g_fr = true; g_ru = g_ar = false; bool okFr = SigFilesText(1) == L"1 fichier" && SigFilesText(3) == L"3 fichiers";
            g_fr = false; bool okEn = SigFilesText(1) == L"1 file" && SigFilesText(2) == L"2 files";
            g_ru = true; std::wstring r1 = SigFilesText(1), r3 = SigFilesText(3), r5 = SigFilesText(5), r21 = SigFilesText(21), r12 = SigFilesText(12);
            bool okRu = r1 == L"1 \u0444\u0430\u0439\u043b" && r3 == L"3 \u0444\u0430\u0439\u043b\u0430" && r5 == L"5 \u0444\u0430\u0439\u043b\u043e\u0432" && r21 == L"21 \u0444\u0430\u0439\u043b" && r12 == L"12 \u0444\u0430\u0439\u043b\u043e\u0432";
            g_ru = false; g_ar = true; std::wstring a1 = SigFilesText(1), a2 = SigFilesText(2), a3 = SigFilesText(3), a11 = SigFilesText(11);
            bool okAr = a1 != a2 && a2 != a3 && a3 != a11 && a1.find(L"1 ") == 0;
            g_fr = fr; g_ru = ru; g_ar = ar;
            check(okFr && okEn && okRu && okAr, "accord « fichier » : FR 1/3, EN 1/2, RU 1/3/5/21/12, AR 1/2/3/11", r);
            LaunchLog("testsignalnatif : russe : %s, %s, %s, %s, %s ; arabe : %s, %s, %s, %s", Narrow(r1, CP_UTF8).c_str(), Narrow(r3, CP_UTF8).c_str(), Narrow(r5, CP_UTF8).c_str(), Narrow(r21, CP_UTF8).c_str(), Narrow(r12, CP_UTF8).c_str(), Narrow(a1, CP_UTF8).c_str(), Narrow(a2, CP_UTF8).c_str(), Narrow(a3, CP_UTF8).c_str(), Narrow(a11, CP_UTF8).c_str());
        }
        {   // fenetre de choix des zips : dossier factice (Bureau = Documents = MWCoop = <journal>.zp\, Telechargements = son sous-dossier), jamais de fenetre
            g_zpFake = log + L".zp\\"; CreateDirectoryW(g_zpFake.c_str(), NULL); CreateDirectoryW((g_zpFake + L"telechargements\\").c_str(), NULL);
            int nf = 0; std::vector<std::wstring> one = { g_iniLauncher };
            WriteZip(g_zpFake + L"bon.zip", one, &nf, 0, 16 << 20, true);
            WriteZip(g_zpFake + L"telechargements\\sous-dossier.zip", one, &nf, 0, 16 << 20, true);
            WriteZip(g_zpFake + L"ailleurs.zip", one, &nf, 0, 16 << 20, true);
            { HANDLE f = CreateFileW((g_zpFake + L"pas-un-zip.zip").c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL); if (f != INVALID_HANDLE_VALUE) { DWORD w; WriteFile(f, "bonjour, ceci n'est pas un zip du tout, juste du texte", 54, &w, NULL); CloseHandle(f); } }
            { std::wstring exe = g_zpFake + L"journal.exe"; CopyFileW(g_iniLauncher.c_str(), exe.c_str(), FALSE); std::vector<std::wstring> e = { exe }; WriteZip(g_zpFake + L"mauvaise-extension.zip", e, &nf, 0, 16 << 20, false); DeleteFileW(exe.c_str()); }
            g_sigExt.clear(); SigZpOpen();
            int zips = 0, dirs = 0; for (auto &it : g_zpItems) { if (it.dir) dirs++; else zips++; }
            check(g_zpOpen && g_zpView == 0 && zips == 5 && dirs == 0, "fenetre des zips : Recents = 5 zips des dossiers connus (bon x3 dont le sous-dossier Telechargements, pas-un-zip, mauvaise extension)", r);
            for (auto &it : g_zpItems) it.checked = it.name == L"bon.zip" || it.name == L"pas-un-zip.zip" || it.name == L"mauvaise-extension.zip";
            SigZpAddChecked();
            std::wstring badNot, badExt; for (auto &it : g_zpItems) { if (it.name == L"pas-un-zip.zip") badNot = it.bad; if (it.name == L"mauvaise-extension.zip") badExt = it.bad; }
            check(g_zpOpen && g_sigExt.size() == 1 && !badNot.empty() && !badExt.empty() && badExt.find(L".exe") != std::wstring::npos, "Ajouter : le bon zip ajoute, les deux mauvais refuses avec leur raison (pas un zip ; journal.exe)", r);
            LaunchLog("testsignalnatif : raisons : « %s » ; « %s »", Narrow(badNot, CP_UTF8).c_str(), Narrow(badExt, CP_UTF8).c_str());
            SigZpBrowse();
            check(!g_zpOpen && g_sigExt.size() == 2, "Parcourir… (bouchon) : ailleurs.zip ajoute, fenetre fermee", r);
            SigZpOpen(); SigZpGo(g_zpFake);
            int sub = -1; for (int k = 0; k < (int)g_zpItems.size(); k++) if (g_zpItems[k].dir && g_zpItems[k].name == L"telechargements") sub = k;
            check(g_zpView == 1 && sub >= 0, "dossier : le sous-dossier Telechargements est liste", r);
            if (sub >= 0) SigZpClick(UI_SIG_ZP_ROW + sub);
            int szip = -1; for (int k = 0; k < (int)g_zpItems.size(); k++) if (!g_zpItems[k].dir && g_zpItems[k].name == L"sous-dossier.zip") szip = k;
            check(szip >= 0 && g_zpDir == g_zpFake + L"telechargements\\", "entrer dans le sous-dossier : son zip est la", r);
            SigZpClick(UI_SIG_ZP_UP);
            check(g_zpDir == g_zpFake, "Dossier parent", r);
            SigZpGo(g_zpFake + L"telechargements\\"); if (szip >= 0) { SigZpClick(UI_SIG_ZP_ROW + szip); SigZpClick(UI_SIG_ZP_ADD); }
            check(!g_zpOpen && g_sigExt.size() == 3, "zip du sous-dossier coche puis Ajouter : 3 zips d'un autre joueur", r);
            long long t, b; int n; check(SigZipCheck(&t, &n, &b) == 0 && n == 4, "limites avec les 3 zips ajoutes + les communs : 4 zips, accepte", r);
            std::wstring why; check(!SigZipInspect(g_zpFake + L"pas-un-zip.zip", why) && !why.empty(), "SigZipInspect : texte brut refuse", r);
            check(SigZipInspect(g_zpFake + L"bon.zip", why), "SigZipInspect : zip du lanceur accepte", r);
            for (const wchar_t *f : { L"bon.zip", L"ailleurs.zip", L"pas-un-zip.zip", L"mauvaise-extension.zip", L"telechargements\\sous-dossier.zip" }) DeleteFileW((g_zpFake + f).c_str());
            RemoveDirectoryW((g_zpFake + L"telechargements").c_str()); RemoveDirectoryW(g_zpFake.c_str()); g_zpFake.clear(); g_sigExt.clear();
        }
        g_sigSess.clear(); g_sigExt.clear(); g_sigCommon = { log, g_iniLauncher };
    }
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
    // 5b. depot a trois zips (deux fabriques ici, un « d'un autre joueur » existant), comme avec deux parties cochees + un zip ajoute
    {
        int nf = 0; std::wstring ext = log + L".ext.zip"; std::vector<std::wstring> one = { g_iniLauncher };
        check(WriteZip(ext, one, &nf, 0, 16 << 20, true) && nf == 1, "zip d'un autre joueur fabrique pour l'essai", r);
        SigJob j3; j3.kind = SOP_DEPOSER; j3.body = SigFormJson();
        SigZipSpec a; a.files = { log, g_iniLauncher }; SigZipSpec b; b.files = { log }; SigZipSpec c; c.path = ext;
        j3.zips = { a, b, c };
        SigDeposer(j3, r);
        int n3 = r.status == 200 ? (int)r.j.num("n") : 0;
        check(r.status == 200 && n3 > 0, "depot a trois zips (2 fabriques + 1 existant)", r);
        check(FileExists(ext) && !FileExists(LocalDir() + L"signalement-envoi-1.zip") && !FileExists(LocalDir() + L"signalement-envoi-2.zip"), "zips fabriques effaces, celui de l'autre joueur garde", r);
        DeleteFileW(ext.c_str());
        if (n3 > 0) { SigCall(L"POST", L"/dossiers/" + std::to_wstring(n3) + L"/retirer", "{}", true, true, r); check(r.status == 200, "retirer le dossier a trois zips", r); }
    }
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
