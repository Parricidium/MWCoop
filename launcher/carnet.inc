// ---- Carnet de notes du copilote (JD 07/11 ; spec de [Project & Fesability], MWCoop-carnet\SPEC-carnet-copilote.md ;
// format arrete avec [MWC] le 07/11) : page Carnet de la barre, sous RallyWorld. Le trace des speciales (exporte par le
// mod), les notes posees dessus, la page du carnet en apercu ; poser, glisser, regler, retirer des notes ; carnets
// Nouveau / Importer / Exporter / Supprimer, carnet actif.
// Fichiers (%LOCALAPPDATA%\MWCoop\, comme casque.png ; essais : LOCALAPPDATA a part) :
//   speciales-<jeu>.json      ecrit par le mod (monde charge) : lu seulement ;
//   carnets\<fichier>.json    les carnets des deux jeux (champ « jeu ») ; ecrits ici jeu ferme seulement, .tmp puis remplacement ;
//   carnet-actif-<jeu>.txt    une ligne : le nom du fichier seul ; vide ou absent = notes automatiques.
// Un fichier carnet importe est une donnee : valide entierement (memes regles que le mod), jamais execute, reecrit au
// format du lanceur ; une valeur inconnue d'une cle connue refuse tout le fichier, une cle inconnue est ignoree.
enum { UI_CN_PREV = 17000, UI_CN_NEXT, UI_CN_BPREV, UI_CN_BNEXT, UI_CN_NEW, UI_CN_IMPORT, UI_CN_EXPORT, UI_CN_RESET, UI_CN_ACTIVE,
       UI_CN_DEL, UI_CN_RENAME, UI_CN_REN_OK, UI_CN_REN_CANCEL, UI_CN_DIR, UI_CN_NDEL, UI_CN_NOK, UI_CN_FIT, UI_CN_POP,
       UI_CN_TYPE = 17050 /* + type */, UI_CN_SIDE = 17070 /* + 0 gauche, 1 droite */, UI_CN_GRADE = 17080 /* + cote 0..6 */,
       UI_CN_MOD = 17090 /* + mod */, UI_CN_TEXT = 17100, UI_CN_END = 17200 };
enum { CT_VIRAGE, CT_CARRE, CT_BOSSE, CT_PONT, CT_DANGER, CT_ARRIVEE, CT_DROIT, CT_TEXTE, CT_N };
enum { CM_LONG, CM_RESSERRE, CM_OUVRE, CM_NC, CM_DANGER, CM_ENCHAINE, CM_N };
static const char *kCnType[CT_N] = { "virage", "carre", "bosse", "pont", "danger", "arrivee", "droit", "texte" };
static const char *kCnMod[CM_N] = { "long", "resserre", "ouvre", "nc", "danger", "enchaine" };
static const int kCnMaxNotes = 1000, kCnMaxBytes = 256 * 1024, kCnTextMax = 40, kCnNameMax = 40;

struct CnNote { int m = 0; int type = CT_VIRAGE; int side = 1; int grade = 4; unsigned mods = 0; std::wstring text; int uid = 0; };
struct CnStage { std::wstring id, name; int day = 0; float step = 2; int len = 0; std::vector<PointF> p; std::vector<CnNote> autoN; };
struct CnBook { std::wstring file, name, game; std::map<std::wstring, std::vector<CnNote>> st; };

static std::vector<CnStage> g_cnStages; static std::wstring g_cnStagesErr; static bool g_cnStagesMissing = true;
static std::vector<CnBook> g_cnBooks; static int g_cnBad;          // (carnets de ce jeu ; fichiers illisibles de la bibliotheque)
static std::wstring g_cnBookFile, g_cnActive;                      // carnet montre, carnet actif (noms de fichiers)
static int g_cnSt;                                                 // speciale montree
static int g_cnSelUid = -1; static bool g_cnPop;                   // note choisie, ses champs ouverts
static int g_cnUid = 1;
static DWORD g_cnScanT, g_cnDelT, g_cnResetT; static unsigned long long g_cnSig;
static bool g_cnGameOn; static int g_cnTestGame = -1;
static bool g_cnEdit; static std::wstring g_cnEditText;            // nom du carnet en cours d'ecriture
static float g_cnPos;                                              // position de l'apercu (m)
static int g_cnHover = -1;                                         // point du trace sous la souris
// vue du trace
static RectF g_cnMapR; static float g_cnZoom, g_cnCx, g_cnCz, g_cnFitZoom; static int g_cnViewSt = -1;
static int g_cnDrag; static float g_cnDx, g_cnDy, g_cnDcx, g_cnDcz; static bool g_cnMoved;   // 1 note, 2 vue
static bool g_cnCapNote;   // (/capture carnet-note : la premiere note virage choisie, ses champs ouverts)

static std::wstring CnGame() { return g_game == GAME_MSC ? L"MSC" : L"MWC"; }
static std::wstring CnRoot() { return LocalDir(); }
static std::wstring CnLibDir() { return CnRoot() + L"carnets\\"; }
static std::wstring CnActivePath() { return CnRoot() + L"carnet-actif-" + CnGame() + L".txt"; }
static std::wstring CnStagesPath() { return CnRoot() + L"speciales-" + CnGame() + L".json"; }
static bool CnFileOk(const std::wstring &n)
{
    if (n.size() < 6 || n.size() > 64 || _wcsicmp(n.c_str() + n.size() - 5, L".json")) return false;
    for (wchar_t c : n) if (!(iswalnum(c) && c < 128) && c != L'-' && c != L'_' && c != L'.') return false;
    return n.find(L"..") == std::wstring::npos;
}
static bool CnIdOk(const std::wstring &s) { if (s.empty() || s.size() > 16) return false; for (wchar_t c : s) if (!(c < 128 && (iswalnum(c) || c == L'_' || c == L'-'))) return false; return true; }
static bool CnGameOn() { return g_cnTestGame >= 0 ? g_cnTestGame != 0 : GameProcessRunning(); }
static bool CnReadFile(const std::wstring &p, size_t maxB, std::string &out)
{
    out.clear();
    WIN32_FILE_ATTRIBUTE_DATA fa;
    if (!GetFileAttributesExW(p.c_str(), GetFileExInfoStandard, &fa) || (fa.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)) return false;
    if (fa.nFileSizeHigh || fa.nFileSizeLow > maxB) return false;
    std::vector<unsigned char> d;
    if (!ReadAll(p, d) || d.size() > maxB) return false;
    size_t o = d.size() >= 3 && d[0] == 0xEF && d[1] == 0xBB && d[2] == 0xBF ? 3 : 0;
    out.assign((const char *)d.data() + o, d.size() - o);
    return true;
}
static bool CnWriteAtomic(const std::wstring &p, const std::string &s)
{
    std::wstring tmp = p + L".tmp";
    HANDLE h = CreateFileW(tmp.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (h == INVALID_HANDLE_VALUE) return false;
    DWORD w = 0; BOOL ok = WriteFile(h, s.data(), (DWORD)s.size(), &w, NULL); CloseHandle(h);
    if (!ok || w != s.size() || !MoveFileExW(tmp.c_str(), p.c_str(), MOVEFILE_REPLACE_EXISTING)) { DeleteFileW(tmp.c_str()); return false; }
    return true;
}

// ---- lecture (memes regles que le mod)
static bool CnTextOk(const std::wstring &s, size_t mx) { if (s.empty() || s.size() > mx) return false; for (wchar_t c : s) if (c < 32 || (c >= 127 && c < 160) || c == 0x2028 || c == 0x2029) return false; return true; }
static bool CnParseNote(const Jv &v, CnNote &n, std::wstring &err)
{
    if (v.t != 5) { err = L"note : pas un objet"; return false; }
    const Jv *m = v.get("m"), *ty = v.get("type");
    if (!m || m->t != 2 || m->n != floor(m->n) || m->n < 0 || m->n > 100000) { err = L"note : m absent ou hors de 0..100000"; return false; }
    n.m = (int)m->n;
    if (!ty || ty->t != 3) { err = L"note : type absent"; return false; }
    n.type = -1;
    for (int k = 0; k < CT_N; k++) if (ty->s == kCnType[k]) n.type = k;
    if (n.type < 0) { err = L"note : type inconnu « " + Widen(ty->s) + L" »"; return false; }
    if (n.type == CT_VIRAGE || n.type == CT_CARRE) {
        const Jv *se = v.get("sens");
        if (!se || se->t != 3 || (se->s != "g" && se->s != "d")) { err = L"note : sens g ou d obligatoire"; return false; }
        n.side = se->s == "g" ? -1 : 1;
    }
    if (n.type == CT_VIRAGE) {
        const Jv *co = v.get("cote");
        if (!co || co->t != 2 || co->n != floor(co->n) || co->n < 0 || co->n > 6) { err = L"note : cote de 0 a 6 obligatoire"; return false; }
        n.grade = (int)co->n;
    }
    if (n.type == CT_TEXTE) {
        const Jv *tx = v.get("texte");
        if (!tx || tx->t != 3) { err = L"note : texte obligatoire"; return false; }
        n.text = Trim(Widen(tx->s));
        if (!CnTextOk(n.text, kCnTextMax)) { err = L"note : texte vide, trop long ou avec un caractere de controle"; return false; }
    }
    n.mods = 0;
    if (const Jv *mo = v.get("mods")) {
        if (mo->t != 4 || mo->a.size() > CM_N) { err = L"note : mods doit etre une liste"; return false; }
        for (auto &e : mo->a) {
            int k = -1;
            if (e.t == 3) for (int j = 0; j < CM_N; j++) if (e.s == kCnMod[j]) k = j;
            if (k < 0) { err = L"note : mod inconnu"; return false; }
            if (n.mods & (1u << k)) { err = L"note : mod en double"; return false; }
            n.mods |= 1u << k;
        }
        if ((n.mods & (1u << CM_RESSERRE)) && (n.mods & (1u << CM_OUVRE))) { err = L"note : resserre et ouvre ensemble"; return false; }
    }
    n.uid = g_cnUid++;
    return true;
}
static bool CnParseNotes(const Jv *a, std::vector<CnNote> &out, std::wstring &err)
{
    out.clear();
    if (!a) return true;
    if (a->t != 4) { err = L"notes : pas une liste"; return false; }
    if (a->a.size() > (size_t)kCnMaxNotes) { err = L"plus de 1000 notes sur une speciale"; return false; }
    for (auto &e : a->a) { CnNote n; if (!CnParseNote(e, n, err)) return false; out.push_back(n); }
    std::stable_sort(out.begin(), out.end(), [](const CnNote &x, const CnNote &y) { return x.m < y.m; });
    return true;
}
static bool CnJsonEnd(const std::string &s, size_t e) { for (; e < s.size(); e++) if (!strchr(" \t\r\n", s[e])) return false; return true; }
static bool CnParseBook(const std::string &s, CnBook &b, std::wstring &err)
{
    b = CnBook();
    Jv v; size_t e = JParseV(s, 0, v, 0);
    if (e == std::string::npos || !CnJsonEnd(s, e) || v.t != 5) { err = L"JSON illisible"; return false; }
    const Jv *ver = v.get("v"), *jeu = v.get("jeu"), *nom = v.get("nom"), *sp = v.get("speciales");
    if (!ver || ver->t != 2 || ver->n != 1) { err = L"version (v) differente de 1"; return false; }
    if (!jeu || jeu->t != 3 || (jeu->s != "MSC" && jeu->s != "MWC")) { err = L"jeu : MSC ou MWC"; return false; }
    b.game = Widen(jeu->s);
    if (!nom || nom->t != 3) { err = L"nom absent"; return false; }
    b.name = Trim(Widen(nom->s));
    if (!CnTextOk(b.name, kCnNameMax)) { err = L"nom : 1 a 40 caracteres"; return false; }
    if (sp) {
        if (sp->t != 5) { err = L"speciales : pas un objet"; return false; }
        for (auto &kv : sp->o) {
            std::wstring id = Widen(kv.first);
            if (!CnIdOk(id)) continue;   // (speciale inconnue : ignoree)
            if (kv.second.t != 5) { err = L"speciale " + id + L" : pas un objet"; return false; }
            std::vector<CnNote> ns;
            if (!CnParseNotes(kv.second.get("notes"), ns, err)) { err = id + L" : " + err; return false; }
            b.st[id] = ns;
        }
    }
    return true;
}
static bool CnParseStages(const std::string &s, std::vector<CnStage> &out, std::wstring &err)
{
    out.clear();
    Jv v; size_t e = JParseV(s, 0, v, 0);
    if (e == std::string::npos || !CnJsonEnd(s, e) || v.t != 5) { err = L"JSON illisible"; return false; }
    const Jv *ver = v.get("v"), *jeu = v.get("jeu"), *st = v.get("step"), *sp = v.get("speciales");
    if (!ver || ver->t != 2 || ver->n != 1) { err = L"version (v) differente de 1"; return false; }
    if (!jeu || jeu->t != 3 || Widen(jeu->s) != CnGame()) { err = L"fichier d'un autre jeu"; return false; }
    if (!st || st->t != 2 || !(st->n >= 0.5 && st->n <= 20)) { err = L"step hors de 0,5..20"; return false; }
    if (!sp || sp->t != 4 || sp->a.size() > 16) { err = L"speciales : liste de 16 au plus"; return false; }
    for (auto &x : sp->a) {
        CnStage c; c.step = (float)st->n;
        if (x.t != 5) { err = L"speciale : pas un objet"; return false; }
        c.id = Widen(x.str("id"));
        if (!CnIdOk(c.id)) { err = L"speciale : id invalide"; return false; }
        c.name = Trim(Widen(x.str("nom")));
        if (c.name.empty() || !CnTextOk(c.name, kCnNameMax)) c.name = c.id;
        c.day = (int)x.num("jour", 0);
        if (c.day != 6 && c.day != 7) c.day = 0;
        const Jv *xz = x.arr("xz");
        if (!xz || xz->a.size() < 4 || (xz->a.size() & 1) || xz->a.size() > 200000) { err = c.id + L" : xz invalide"; return false; }
        for (size_t i = 0; i < xz->a.size(); i += 2) {
            const Jv &a = xz->a[i], &b = xz->a[i + 1];
            if (a.t != 2 || b.t != 2 || !_finite(a.n) || !_finite(b.n) || fabs(a.n) > 1e5 || fabs(b.n) > 1e5) { err = c.id + L" : point invalide"; return false; }
            c.p.push_back(PointF((REAL)a.n, (REAL)b.n));
        }
        c.len = (int)((c.p.size() - 1) * c.step + 0.5f);
        if (!CnParseNotes(x.get("notes"), c.autoN, err)) { err = c.id + L" : " + err; return false; }
        bool dup = false; for (auto &o : out) if (o.id == c.id) dup = true;
        if (!dup) out.push_back(c);
    }
    return true;
}

// ---- ecriture (UTF-8 sans BOM)
static std::string CnNoteJson(const CnNote &n)
{
    std::string o = "{ \"m\": " + std::to_string(n.m) + ", \"type\": \"" + kCnType[n.type] + "\"";
    if (n.type == CT_VIRAGE || n.type == CT_CARRE) o += std::string(", \"sens\": \"") + (n.side < 0 ? "g" : "d") + "\"";
    if (n.type == CT_VIRAGE) o += ", \"cote\": " + std::to_string(n.grade);
    if (n.type == CT_TEXTE) o += ", \"texte\": " + SigJStr(n.text);
    if (n.mods) {
        o += ", \"mods\": [";
        bool first = true;
        for (int k = 0; k < CM_N; k++) if (n.mods & (1u << k)) { o += std::string(first ? "" : ", ") + "\"" + kCnMod[k] + "\""; first = false; }
        o += "]";
    }
    return o + " }";
}
static std::string CnBookJson(const CnBook &b)
{
    std::string o = "{ \"v\": 1, \"jeu\": " + SigJStr(b.game) + ", \"nom\": " + SigJStr(b.name) + ",\r\n  \"speciales\": {";
    bool first = true;
    for (auto &kv : b.st) {
        o += std::string(first ? "" : ",") + "\r\n    " + SigJStr(kv.first) + ": { \"notes\": [";
        for (size_t i = 0; i < kv.second.size(); i++) o += std::string(i ? "," : "") + "\r\n      " + CnNoteJson(kv.second[i]);
        o += kv.second.empty() ? "] }" : "\r\n    ] }";
        first = false;
    }
    return o + (first ? "} }\r\n" : "\r\n  } }\r\n");
}

// ---- etat
static CnBook *CnBookCur() { for (auto &b : g_cnBooks) if (b.file == g_cnBookFile) return &b; return NULL; }
static CnStage *CnStageCur() { return g_cnSt >= 0 && g_cnSt < (int)g_cnStages.size() ? &g_cnStages[g_cnSt] : NULL; }
static const std::vector<CnNote> &CnNotesShown()
{
    static const std::vector<CnNote> none;
    CnStage *s = CnStageCur(); if (!s) return none;
    CnBook *b = CnBookCur();
    if (b) { auto it = b->st.find(s->id); if (it != b->st.end()) return it->second; }
    return s->autoN;
}
static bool CnStageOwn() { CnStage *s = CnStageCur(); CnBook *b = CnBookCur(); return s && b && b->st.count(s->id); }
static const CnNote *CnSel() { for (auto &n : CnNotesShown()) if (n.uid == g_cnSelUid) return &n; return NULL; }
static std::wstring CnStageLabel(const CnStage &s)
{
    std::wstring d = s.day == 6 ? T(L"samedi", L"Saturday") : s.day == 7 ? T(L"dimanche", L"Sunday") : L"";
    return s.name + (d.empty() ? L"" : L" · " + d);
}
static unsigned long long CnFileSig(const std::wstring &p)
{
    WIN32_FILE_ATTRIBUTE_DATA fa;
    if (!GetFileAttributesExW(p.c_str(), GetFileExInfoStandard, &fa)) return 0;
    return ((unsigned long long)fa.ftLastWriteTime.dwHighDateTime << 32 | fa.ftLastWriteTime.dwLowDateTime) ^ ((unsigned long long)fa.nFileSizeLow * 0x9E3779B97F4A7C15ULL);
}
static unsigned long long CnDirSig()   // (dates des fichiers lus : on ne relit que s'ils changent -- le mod ecrit en jeu)
{
    unsigned long long h = CnFileSig(CnStagesPath()) * 31 + CnFileSig(CnActivePath());
    WIN32_FIND_DATAW fd; HANDLE f = FindFirstFileW((CnLibDir() + L"*.json").c_str(), &fd);
    if (f != INVALID_HANDLE_VALUE) {
        do { h = h * 1099511628211ULL ^ (((unsigned long long)fd.ftLastWriteTime.dwHighDateTime << 32 | fd.ftLastWriteTime.dwLowDateTime) + fd.nFileSizeLow); for (wchar_t c : std::wstring(fd.cFileName)) h = h * 131 + c; } while (FindNextFileW(f, &fd));
        FindClose(f);
    }
    return h;
}
static std::wstring CnReadActive()
{
    std::string s;
    if (!CnReadFile(CnActivePath(), 1024, s)) return L"";
    std::wstring w = Trim(Widen(s));
    size_t nl = w.find_first_of(L"\r\n"); if (nl != std::wstring::npos) w = Trim(w.substr(0, nl));
    return CnFileOk(w) ? w : L"";
}
static void CnLoad(bool force)
{
    unsigned long long sig = CnDirSig();
    g_cnScanT = GetTickCount();
    g_cnGameOn = CnGameOn();
    if (!force && sig == g_cnSig) return;
    g_cnSig = sig;
    std::wstring curStage = CnStageCur() ? CnStageCur()->id : L"";
    std::string s;
    g_cnStagesErr.clear();
    g_cnStagesMissing = !CnReadFile(CnStagesPath(), 16u << 20, s);
    if (g_cnStagesMissing) g_cnStages.clear();
    else if (!CnParseStages(s, g_cnStages, g_cnStagesErr)) { g_cnStages.clear(); LaunchLog("carnet : speciales-%s.json refuse (%s)", Narrow(CnGame()).c_str(), Narrow(g_cnStagesErr, CP_UTF8).c_str()); }
    g_cnSt = 0;
    for (int i = 0; i < (int)g_cnStages.size(); i++) if (g_cnStages[i].id == curStage) g_cnSt = i;
    g_cnBooks.clear(); g_cnBad = 0;
    WIN32_FIND_DATAW fd; HANDLE f = FindFirstFileW((CnLibDir() + L"*.json").c_str(), &fd);
    if (f != INVALID_HANDLE_VALUE) {
        do {
            std::wstring n = fd.cFileName;
            if ((fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) || !CnFileOk(n)) continue;
            CnBook b; std::wstring err;
            if (!CnReadFile(CnLibDir() + n, kCnMaxBytes, s) || !CnParseBook(s, b, err)) { g_cnBad++; continue; }
            if (b.game != CnGame()) continue;
            b.file = n;
            g_cnBooks.push_back(b);
        } while (FindNextFileW(f, &fd));
        FindClose(f);
    }
    std::sort(g_cnBooks.begin(), g_cnBooks.end(), [](const CnBook &a, const CnBook &b) { int c = _wcsicmp(a.name.c_str(), b.name.c_str()); return c ? c < 0 : a.file < b.file; });
    g_cnActive = CnReadActive();
    bool activeOk = false; for (auto &b : g_cnBooks) if (b.file == g_cnActive) activeOk = true;
    if (!activeOk) g_cnActive.clear();   // (fichier absent ou d'un autre jeu : notes automatiques, comme le mod)
    if (!CnBookCur()) g_cnBookFile = !g_cnActive.empty() ? g_cnActive : g_cnBooks.empty() ? L"" : g_cnBooks[0].file;
    if (!CnSel()) { g_cnSelUid = -1; g_cnPop = false; }
}
static void CarnetEnter() { g_cnEdit = false; CnLoad(true); }
static bool CnGuard()
{
    if (!g_cnGameOn) return true;
    SetStatus(K_WARN, T(L"Ferme le jeu pour modifier un carnet : le jeu s'en sert", L"Close the game to change a notebook: the game is using it"));
    return false;
}
static std::wstring CnUniqueFile(const std::wstring &name)
{
    std::wstring b;
    for (wchar_t c : name) {
        wchar_t l = towlower(c);
        if (l < 128 && iswalnum(l)) b += l;
        else if (!b.empty() && b.back() != L'-') b += L'-';
        if (b.size() >= 32) break;
    }
    while (!b.empty() && b.back() == L'-') b.pop_back();
    if (b.empty()) b = L"carnet";
    for (int k = 1; k < 1000; k++) {
        std::wstring f = b + (k > 1 ? L"-" + std::to_wstring(k) : L"") + L".json";
        if (GetFileAttributesW((CnLibDir() + f).c_str()) == INVALID_FILE_ATTRIBUTES) return f;
    }
    return L"";
}
static bool CnSaveBook(const CnBook &b)
{
    SHCreateDirectoryExW(NULL, CnLibDir().c_str(), NULL);
    std::string j = CnBookJson(b);
    if (j.size() > (size_t)kCnMaxBytes) { SetStatus(K_ERR, T(L"Carnet trop gros (256 Ko au plus) : retire des notes", L"Notebook too big (256 KB at most): remove some notes")); return false; }
    if (!CnWriteAtomic(CnLibDir() + b.file, j)) { SetStatus(K_ERR, T(L"Impossible d'écrire le carnet", L"Could not write the notebook")); return false; }
    g_cnSig = CnDirSig();   // (notre ecriture : pas besoin de tout relire)
    return true;
}
static bool CnSetActive(const std::wstring &file)
{
    SHCreateDirectoryExW(NULL, CnRoot().c_str(), NULL);
    if (!CnWriteAtomic(CnActivePath(), Narrow(file, CP_UTF8))) { SetStatus(K_ERR, T(L"Impossible d'écrire le carnet actif", L"Could not write the active notebook")); return false; }
    g_cnActive = file; g_cnSig = CnDirSig();
    return true;
}
static CnBook *CnNewBook(const std::wstring &name)
{
    CnBook b; b.game = CnGame(); b.name = name; b.file = CnUniqueFile(name);
    if (b.file.empty() || !CnSaveBook(b)) return NULL;
    g_cnBooks.push_back(b); g_cnBookFile = b.file;
    if (g_cnActive.empty()) CnSetActive(b.file);   // (premier carnet : actif tout de suite)
    return CnBookCur();
}
// Notes de la speciale montree, modifiables : a la premiere modification, le carnet (cree s'il n'y en a pas) recoit
// d'abord une copie des notes automatiques (memes uid : la note choisie le reste).
static std::vector<CnNote> *CnEditable()
{
    CnStage *s = CnStageCur();
    if (!s || !CnGuard()) return NULL;
    CnBook *b = CnBookCur();
    if (!b) { b = CnNewBook(T(L"Mes notes", L"My notes")); if (!b) return NULL; SetStatus(K_OK, T(L"Carnet « %s » créé", L"Notebook \"%s\" created"), b->name.c_str()); }
    if (!b->st.count(s->id)) b->st[s->id] = s->autoN;
    return &b->st[s->id];
}
static void CnCommit()
{
    CnBook *b = CnBookCur(); CnStage *s = CnStageCur();
    if (!b || !s) return;
    std::stable_sort(b->st[s->id].begin(), b->st[s->id].end(), [](const CnNote &x, const CnNote &y) { return x.m < y.m; });
    CnSaveBook(*b);
}
static CnNote *CnSelEdit() { std::vector<CnNote> *v = CnEditable(); if (!v) return NULL; for (auto &n : *v) if (n.uid == g_cnSelUid) return &n; return NULL; }

// ---- estimation au clic (sans note automatique proche), sur les 50 m qui suivent : virage (seuils du KARTTURI), carre (regle de la spec et de [MWC],
// 07/11 : angle de 65 a 115 deg ET (80 % de l'angle sur 25 m au plus OU rayon mini sous 20 m) -- [MSC] : le lissage etale
// un carre sur 32 a 34 m, « ~90 deg sur moins de 25 m » n'en attrapait aucun), H au-dela de 150 deg.
static float CnHeading(const CnStage &s, int j) { return atan2f(s.p[j + 1].X - s.p[j].X, s.p[j + 1].Y - s.p[j].Y); }   // (croit a droite : x a droite, z en haut vu du ciel)
static CnNote CnGuess(const CnStage &s, int i);
// Note proposee au clic ([MWC] 07/11) : la note automatique du mod (virage, carre ou bosse) la plus proche, a moins de 25 m
// -- calculee sur le trace brut du jeu, avant lissage, que le lanceur n'a pas : lanceur et jeu proposent la meme ; sinon
// l'estimation du lanceur sur le trace exporte (CnGuess). Le joueur corrige ensuite.
static CnNote CnSuggest(const CnStage &s, int i)
{
    int m = (int)(i * s.step + 0.5f), bd = 26;
    const CnNote *best = NULL;
    for (auto &a : s.autoN) {
        if (a.type != CT_VIRAGE && a.type != CT_CARRE && a.type != CT_BOSSE) continue;
        int d = abs(a.m - m);
        if (d < bd) { bd = d; best = &a; }
    }
    if (!best) return CnGuess(s, i);
    CnNote n = *best; n.m = m; n.uid = g_cnUid++; n.text.clear();
    return n;
}
static CnNote CnGuess(const CnStage &s, int i)
{
    CnNote n; n.m = (int)(i * s.step + 0.5f); n.uid = g_cnUid++;
    int np = (int)s.p.size(), w = max(2, (int)(50 / s.step + 0.5f));
    int a = max(1, i), b = min(np - 2, i + w);
    std::vector<float> dh;   // (virage de chaque pas, en radians, > 0 a droite)
    for (int j = a; j <= b; j++) { float d = CnHeading(s, j) - CnHeading(s, j - 1); while (d > 3.14159265f) d -= 6.2831853f; while (d < -3.14159265f) d += 6.2831853f; dh.push_back(d); }
    float tot = 0; for (float d : dh) tot += d;
    float ang = fabsf(tot) * 57.29578f;
    if (dh.empty() || ang < 10) { n.type = CT_DROIT; return n; }
    n.side = tot < 0 ? -1 : 1;
    float sg = tot < 0 ? -1.0f : 1.0f;
    // longueur la plus courte qui porte 80 % de l'angle ; rayon mini sur 3 pas
    size_t best = dh.size();
    for (size_t x = 0; x < dh.size(); x++) { float acc = 0; for (size_t y = x; y < dh.size(); y++) { acc += dh[y] * sg; if (acc >= 0.8f * fabsf(tot)) { best = min(best, y - x + 1); break; } } }
    float l80 = best * s.step, rmin = 1e9f;
    for (size_t x = 0; x + 2 < dh.size(); x++) { float k = (dh[x] + dh[x + 1] + dh[x + 2]) * sg / (3 * s.step); if (k > 1e-4f) rmin = min(rmin, 1 / k); }
    if (ang >= 65 && ang <= 115 && (l80 <= 25 || rmin < 20)) { n.type = CT_CARRE; return n; }
    float r = l80 / (0.8f * fabsf(tot));
    n.type = CT_VIRAGE;
    n.grade = r < 14 || ang > 150 ? 0 : r < 22 ? 1 : r < 35 ? 2 : r < 55 ? 3 : r < 85 ? 4 : r < 130 ? 5 : 6;
    return n;
}

// ---- symboles de JD (planche 8 x 6, cases de 128 : launcher\carnet\symboles.png, ressource 62)
static Bitmap *g_cnSym; static bool g_cnSymTried; static Rect g_cnBox[48];
static Bitmap *CnSym()
{
    if (g_cnSymTried) return g_cnSym;
    g_cnSymTried = true;
    g_cnSym = LoadPngRes(62);
    for (int c = 0; c < 48; c++) g_cnBox[c] = Rect((c % 8) * 128 + 32, (c / 8) * 128 + 32, 64, 64);
    if (!g_cnSym || g_cnSym->GetWidth() != 1024 || g_cnSym->GetHeight() != 768) return g_cnSym;
    BitmapData bd; Rect all(0, 0, 1024, 768);
    if (g_cnSym->LockBits(&all, ImageLockModeRead, PixelFormat32bppARGB, &bd) != Ok) return g_cnSym;
    for (int c = 0; c < 48; c++) {   // (boite de l'encre de chaque case : les symboles se suivent sans trou)
        int x0 = 128, y0 = 128, x1 = -1, y1 = -1, cx = (c % 8) * 128, cy = (c / 8) * 128;
        for (int y = 0; y < 128; y++) {
            const unsigned *row = (const unsigned *)((const BYTE *)bd.Scan0 + (cy + y) * bd.Stride) + cx;
            for (int x = 0; x < 128; x++) if ((row[x] >> 24) > 24) { x0 = min(x0, x); x1 = max(x1, x); y0 = min(y0, y); y1 = max(y1, y); }
        }
        if (x1 >= 0) g_cnBox[c] = Rect(cx + x0, cy + y0, x1 - x0 + 1, y1 - y0 + 1);
    }
    {   // (trait orange : une image sur les cases 24 a 27, une seule boite)
        int x0 = 4096, y0 = 4096, x1 = -1, y1 = -1;
        for (int c = 24; c <= 27; c++) { const Rect &r = g_cnBox[c]; if (r.Width == 64 && r.Height == 64 && r.X == (c % 8) * 128 + 32) continue; x0 = min(x0, r.X); y0 = min(y0, r.Y); x1 = max(x1, r.X + r.Width); y1 = max(y1, r.Y + r.Height); }
        if (x1 > x0) g_cnBox[24] = Rect(x0, y0, x1 - x0, y1 - y0);
    }
    g_cnSym->UnlockBits(&bd);
    return g_cnSym;
}
static bool CnV2(int m, int k) { unsigned h = (unsigned)m * 2654435761u; return ((h >> (31 - k)) & 1) != 0; }   // (k : 0 chiffre, 1 fleche ou carre, 2 bosse, 3 danger -- regle de [MWC])
// Cases d'une ligne de note, de gauche a droite : [cote] [fleche ou carre] [long] [resserre ou ouvre] [nc] [!] [enchaine].
static void CnCells(const CnNote &n, std::vector<int> &c)
{
    c.clear();
    bool turn = n.type == CT_VIRAGE || n.type == CT_CARRE;
    if (n.type == CT_VIRAGE) {
        c.push_back(n.grade == 0 ? 10 : CnV2(n.m, 0) ? 32 + n.grade : n.grade);
        c.push_back(n.side > 0 ? (CnV2(n.m, 1) ? 12 : 11) : (CnV2(n.m, 1) ? 14 : 13));
    } else if (n.type == CT_CARRE) c.push_back(n.side > 0 ? (CnV2(n.m, 1) ? 43 : 42) : (CnV2(n.m, 1) ? 45 : 44));
    else if (n.type == CT_BOSSE) c.push_back(CnV2(n.m, 2) ? 19 : 18);
    else if (n.type == CT_PONT) c.push_back(30);
    else if (n.type == CT_ARRIVEE) c.push_back(29);
    else if (n.type == CT_DROIT) c.push_back(31);
    else if (n.type == CT_DANGER) c.push_back(CnV2(n.m, 3) ? 21 : 20);
    if (turn && (n.mods & (1u << CM_LONG))) c.push_back(15);
    if (turn && (n.mods & (1u << CM_RESSERRE))) c.push_back(16);
    if (turn && (n.mods & (1u << CM_OUVRE))) c.push_back(17);
    if (turn && (n.mods & (1u << CM_NC))) c.push_back(22);
    if ((n.mods & (1u << CM_DANGER)) && n.type != CT_DANGER) c.push_back(CnV2(n.m, 3) ? 21 : 20);
    if (n.mods & (1u << CM_ENCHAINE)) c.push_back(28);
}
static const Color kCnPen(255, 63, 72, 204);
// Texte libre : Caveat (OFL, launcher\carnet\Caveat-Regular.ttf, ressource 63 ; la meme que le mod), en formes (GraphicsPath) :
// independant du mode de texte du lanceur. Sans arabe dans Caveat : Arial pour un texte en arabe (le mod prend l'Arial du jeu).
static FontFamily *CnHandFamily()
{
    static FontFamily *fam; static bool tried;
    if (tried) return fam;
    tried = true;
    HRSRC r = FindResourceW(NULL, MAKEINTRESOURCEW(63), RT_RCDATA);
    HGLOBAL h = r ? LoadResource(NULL, r) : NULL;
    const void *p = h ? LockResource(h) : NULL;
    if (!p) { LaunchLog("carnet : police Caveat absente de l'exe"); return NULL; }
    // (objets GDI+ jamais liberes, comme les polices du lanceur : un statique serait detruit apres GdiplusShutdown)
    PrivateFontCollection *col = new PrivateFontCollection();
    if (col->AddMemoryFont(p, SizeofResource(NULL, r)) != Ok || col->GetFamilyCount() < 1) { LaunchLog("carnet : police Caveat illisible"); return NULL; }
    FontFamily *f = new FontFamily(); int got = 0;
    col->GetFamilies(1, f, &got);
    if (got == 1) fam = f;
    return fam;
}
static float CnHandText(Graphics &g, const std::wstring &s, float x, float cy, float px, float maxW, Color c)
{
    bool arabic = false; for (wchar_t ch : s) if (ch >= 0x0600 && ch <= 0x06FF) arabic = true;
    static FontFamily *arial;
    if (arabic && !arial) arial = new FontFamily(L"Arial");
    FontFamily *f = arabic ? arial : CnHandFamily();
    if (!f || maxW < 4) { Text(g, s, RectF(x, cy - px, max(4.0f, maxW), px * 2), px * 0.8f, FontStyleBold, c, StringAlignmentNear); return min(maxW, MeasureW(MeasureG(), s, px * 0.8f, FontStyleBold)); }
    GraphicsPath p;
    StringFormat sf(StringFormat::GenericTypographic());
    if (arabic) sf.SetFormatFlags(sf.GetFormatFlags() | StringFormatFlagsDirectionRightToLeft);
    p.AddString(s.c_str(), -1, f, FontStyleRegular, px, PointF(0, 0), &sf);
    RectF b; p.GetBounds(&b);
    if (b.Width <= 0) return 0;
    float k = min(1.0f, maxW / b.Width);
    Matrix mt; mt.Translate(x, cy); mt.Scale(k, k); mt.Translate(-b.X, -(b.Y + b.Height / 2));
    p.Transform(&mt);
    SmoothingMode sm = g.GetSmoothingMode(); g.SetSmoothingMode(SmoothingModeAntiAlias);
    SolidBrush br(c); g.FillPath(&br, &p);
    g.SetSmoothingMode(sm);
    return b.Width * k;
}
// Une ligne de note (cotes de la page du mod, [MWC] 07/11) : case dessinee a cellH de haut, le bas des dessins (70 % de la
// case) sur la ligne baseY, chaque symbole a sa largeur d'encre, 8/190 de case entre deux ; texte libre en Caveat a 1,55 x 60
// pour une case de 190. Ligne trop longue pour maxX : reduite. Rend la largeur dessinee.
static float CnHandW(const std::wstring &s, float px)
{
    FontFamily *f = CnHandFamily(); if (!f || s.empty()) return 0;
    GraphicsPath p; StringFormat sf(StringFormat::GenericTypographic());
    p.AddString(s.c_str(), -1, f, FontStyleRegular, px, PointF(0, 0), &sf);
    RectF b; p.GetBounds(&b); return b.Width;
}
static float DrawCnLineB(Graphics &g, const CnNote &n, float x, float baseY, float cellH, float maxX)
{
    Bitmap *sym = CnSym();
    std::vector<int> cells; CnCells(n, cells);
    float k = cellH / 128.0f, gap = cellH * 8 / 190, px = cellH * 1.55f * 60 / 190;
    float tw = n.type == CT_TEXTE ? CnHandW(n.text, px) : 0, total = tw;
    for (size_t i = 0; i < cells.size(); i++) total += g_cnBox[cells[i]].Width * k + (i || tw > 0 ? gap : 0);
    if (total <= 0) return 0;
    float sc = min(1.0f, max(0.05f, (maxX - x) / total));
    k *= sc; gap *= sc; px *= sc;
    float x0 = x;
    if (n.type == CT_TEXTE) {
        FontFamily *f = CnHandFamily();
        bool arabic = false; for (wchar_t ch : n.text) if (ch >= 0x0600 && ch <= 0x06FF) arabic = true;
        if (f && !arabic) {   // (pied des lettres sur la ligne : ascendante de la police)
            float asc = px * f->GetCellAscent(FontStyleRegular) / max(1, (int)f->GetEmHeight(FontStyleRegular));
            GraphicsPath gp; StringFormat sf(StringFormat::GenericTypographic());
            gp.AddString(n.text.c_str(), -1, f, FontStyleRegular, px, PointF(0, 0), &sf);
            RectF bb; gp.GetBounds(&bb);
            Matrix mt; mt.Translate(x - bb.X, baseY - asc); gp.Transform(&mt);
            SmoothingMode sm = g.GetSmoothingMode(); g.SetSmoothingMode(SmoothingModeAntiAlias);
            SolidBrush br(kCnPen); g.FillPath(&br, &gp); g.SetSmoothingMode(sm);
            x += bb.Width;
        } else x += CnHandText(g, n.text, x, baseY - px * 0.35f, px, maxX - x, kCnPen);
    }
    if (!sym) { if (n.type != CT_TEXTE) Text(g, Widen(kCnType[n.type]), RectF(x, baseY - cellH * 0.6f, maxX - x, cellH * 0.6f), cellH * 0.25f, FontStyleBold, kCnPen, StringAlignmentNear); return maxX - x0; }
    InterpolationMode im = g.GetInterpolationMode(); g.SetInterpolationMode(InterpolationModeHighQualityBicubic);
    float top = baseY - 0.70f * 128 * k;
    for (size_t i = 0; i < cells.size(); i++) {
        const Rect &bx = g_cnBox[cells[i]];
        if (i || x > x0) x += gap;
        int cellY = (cells[i] / 8) * 128;
        g.DrawImage(sym, RectF(x, top + (bx.Y - cellY) * k, bx.Width * k, bx.Height * k), (REAL)bx.X, (REAL)bx.Y, (REAL)bx.Width, (REAL)bx.Height, UnitPixel);
        x += bx.Width * k;
    }
    g.SetInterpolationMode(im);
    return x - x0;
}
// Ligne de note centree sur cy, encre ~ h de haut (etiquettes du trace, champs d'une note) : case de 2 h, ligne a cy + 0,4 h.
static float DrawCnLine(Graphics &g, const CnNote &n, float x, float cy, float h, float maxX) { return DrawCnLineB(g, n, x, cy + 0.4f * h, 2 * h, maxX); }
static void DrawCnCell(Graphics &g, int c, RectF r)   // (une case entiere, centree dans r)
{
    Bitmap *sym = CnSym(); if (!sym) return;
    const Rect &b = g_cnBox[c];
    float k = min(r.Width / b.Width, r.Height / b.Height);
    InterpolationMode im = g.GetInterpolationMode(); g.SetInterpolationMode(InterpolationModeHighQualityBicubic);
    g.DrawImage(sym, RectF(r.X + (r.Width - b.Width * k) / 2, r.Y + (r.Height - b.Height * k) / 2, b.Width * k, b.Height * k), (REAL)b.X, (REAL)b.Y, (REAL)b.Width, (REAL)b.Height, UnitPixel);
    g.SetInterpolationMode(im);
}

// ---- la page du carnet (apercu), en pixels de la page du mod (1024 x 1448) multiplies par u
static void DrawCnPaper(Graphics &g, RectF p)
{
    float u = p.Width / 1024;
    { SolidBrush sh(Color(60, 0, 0, 0)); g.FillRectangle(&sh, p.X + 3, p.Y + 4, p.Width, p.Height); }
    // (page.png de [MWC] : creme, traits tous les 66,6 des 108,6, marge a 13 %)
    SolidBrush pb(Color(255, 250, 246, 233)); g.FillRectangle(&pb, p);
    Pen lp(Color(255, 170, 206, 236), max(1.0f, 2 * u));
    for (float y = 108.6f; y * u < p.Height - 2; y += 66.6f) g.DrawLine(&lp, p.X + 1, p.Y + y * u, p.X + p.Width - 1, p.Y + y * u);
    Pen mp(Color(255, 226, 150, 142), max(1.0f, 2 * u)); g.DrawLine(&mp, p.X + p.Width * 0.13f, p.Y, p.X + p.Width * 0.13f, p.Y + p.Height);
    Pen sp(Color(200, 70, 70, 76), 2.0f); SolidBrush hole(Color(255, 60, 60, 66));
    for (float y = p.Y + 8; y < p.Y + p.Height - 6; y += 13) { g.FillEllipse(&hole, p.X + 6, y + 2, 4.0f, 4.0f); g.DrawArc(&sp, p.X - 6, y, 16.0f, 8.0f, 90.0f, 250.0f); }
}
static void DrawCnPage(Graphics &g, RectF p)
{
    DrawCnPaper(g, p);
    CnStage *s = CnStageCur();
    if (!s) return;
    float u = p.Width / 1024;
    const std::vector<CnNote> &ns = CnNotesShown();
    float pos = min(max(g_cnPos, 0.0f), (float)s->len);
    // radar : colonne x 154 a 502, rond vert fixe en (317, 695), 0,5 m par pixel ; trace tourne pour monter, tremble lie a l'index
    {
        RectF rad(p.X + 154 * u, p.Y, 348 * u, p.Height);
        float cx = p.X + 317 * u, cy = p.Y + 695 * u, sc = 2 * u;
        GraphicsState st = g.Save(); g.SetClip(rad, CombineModeIntersect);
        int n = (int)s->p.size(), i0 = min(n - 1, max(0, (int)(pos / s->step + 0.5f)));
        int a = max(0, i0 - (int)(6 / s->step)), b = min(n - 1, i0 + max(1, (int)(14 / s->step)));
        float dx = s->p[b].X - s->p[a].X, dz = s->p[b].Y - s->p[a].Y, dl = sqrtf(dx * dx + dz * dz);
        if (dl < 1e-3f) { dx = 0; dz = 1; dl = 1; }
        float ux = dx / dl, uz = dz / dl;   // (le sens de la route : vers le haut de la page)
        std::vector<PointF> pts;
        int lo = max(0, i0 - (int)(390 / s->step)), hi = min(n - 1, i0 + (int)(360 / s->step));
        for (int i = lo; i <= hi; i++) {
            float rx = s->p[i].X - s->p[i0].X, rz = s->p[i].Y - s->p[i0].Y;
            float fw = rx * ux + rz * uz, sd = rx * uz - rz * ux;   // (en avant, a droite)
            float wob = (sinf(i * 0.83f) * 1.1f + sinf(i * 0.29f + 1.7f) * 1.7f) * u;   // (ne nage pas d'une image a l'autre)
            pts.push_back(PointF(cx + sd * sc + wob, cy - fw * sc));
        }
        if (pts.size() > 1) { Pen rp(kCnPen, max(1.4f, 5 * u)); rp.SetLineJoin(LineJoinRound); rp.SetStartCap(LineCapRound); rp.SetEndCap(LineCapRound); g.DrawLines(&rp, pts.data(), (INT)pts.size()); }
        g.Restore(st);
        const Rect &cb = g_cnBox[23]; float k = 190 * u / 128;
        DrawCnCell(g, 23, RectF(cx - cb.Width * k / 2, cy - cb.Height * k / 2, cb.Width * k, cb.Height * k));
    }
    // notes : la note actuelle sur le 13e trait (908), une note tous les 2 traits (133,2), interpolees selon les metres ;
    // trait orange 10 px sous la note, de x 497 a 998 ; colonne des notes x 517 a 998
    float base = p.Y + 908 * u, pitch = 133.2f * u, cellH = 190 * u;
    std::vector<const CnNote *> v;
    for (auto &n : ns) if (n.m <= s->len) v.push_back(&n);
    int k = -1; for (int i = 0; i < (int)v.size(); i++) if (v[i]->m <= pos) k = i;
    float m0 = k >= 0 ? (float)v[k]->m : 0.0f, m1 = k + 1 < (int)v.size() ? (float)v[k + 1]->m : -1.0f;
    float f = m1 > m0 ? min(1.0f, max(0.0f, (pos - m0) / (m1 - m0))) : 0.0f;
    RectF col(p.X + 497 * u, p.Y, p.Width - 497 * u, p.Height);
    GraphicsState st = g.Save(); g.SetClip(col, CombineModeIntersect);
    {
        const Rect &ob = g_cnBox[24]; float oh = max(2.0f, ob.Height * 190 * u / 128);
        if (Bitmap *sym = CnSym()) { InterpolationMode im = g.GetInterpolationMode(); g.SetInterpolationMode(InterpolationModeHighQualityBicubic); g.DrawImage(sym, RectF(p.X + 497 * u, base + 10 * u - oh / 2, 501 * u, oh), (REAL)ob.X, (REAL)ob.Y, (REAL)ob.Width, (REAL)ob.Height, UnitPixel); g.SetInterpolationMode(im); }
    }
    for (int i = 0; i < (int)v.size(); i++) {
        float y = base - (i - k - f) * pitch;
        if (y < p.Y - pitch || y > p.Y + p.Height + pitch) continue;
        float w = DrawCnLineB(g, *v[i], p.X + 517 * u, y, cellH, p.X + 998 * u);
        if (v[i]->uid == g_cnSelUid) { Pen sp(WithA(kAcc, 0.8f), 1.4f); sp.SetDashStyle(DashStyleDash); g.DrawRectangle(&sp, p.X + 509 * u, y - 0.62f * cellH, max(40 * u, w) + 16 * u, 0.74f * cellH); }
    }
    g.Restore(st);
}
// ---- le trace (vue du ciel, nord en haut) et les notes posees dessus
static PointF CnToScr(float x, float z) { return PointF(g_cnMapR.X + g_cnMapR.Width / 2 + (x - g_cnCx) * g_cnZoom, g_cnMapR.Y + g_cnMapR.Height / 2 - (z - g_cnCz) * g_cnZoom); }
static void CnFit()
{
    CnStage *s = CnStageCur(); if (!s || s->p.empty() || g_cnMapR.Width < 10) return;
    float x0 = 1e9f, x1 = -1e9f, z0 = 1e9f, z1 = -1e9f;
    for (auto &q : s->p) { x0 = min(x0, q.X); x1 = max(x1, q.X); z0 = min(z0, q.Y); z1 = max(z1, q.Y); }
    g_cnCx = (x0 + x1) / 2; g_cnCz = (z0 + z1) / 2;
    g_cnFitZoom = g_cnZoom = 0.9f * min(g_cnMapR.Width / max(20.0f, x1 - x0), g_cnMapR.Height / max(20.0f, z1 - z0));
    g_cnViewSt = g_cnSt;
}
static int CnNearestIdx(float sx, float sy, float maxPx, int around = -1, int span = 0)
{
    CnStage *s = CnStageCur(); if (!s) return -1;
    int best = -1; float bd = maxPx * maxPx;
    int lo = 0, hi = (int)s->p.size() - 1;
    if (around >= 0) { lo = max(0, around - span); hi = min(hi, around + span); }
    for (int i = lo; i <= hi; i++) { PointF q = CnToScr(s->p[i].X, s->p[i].Y); float d = (q.X - sx) * (q.X - sx) + (q.Y - sy) * (q.Y - sy); if (d < bd) { bd = d; best = i; } }
    return best;
}
static PointF CnNoteScr(const CnStage &s, int m) { int i = min((int)s.p.size() - 1, max(0, (int)(m / s.step + 0.5f))); return CnToScr(s.p[i].X, s.p[i].Y); }
static int CnNoteAt(float x, float y)
{
    CnStage *s = CnStageCur(); if (!s) return -1;
    int best = -1; float bd = 9.0f * 9.0f;
    for (auto &n : CnNotesShown()) {
        if (n.m > s->len) continue;
        PointF q = CnNoteScr(*s, n.m); float d = (q.X - x) * (q.X - x) + (q.Y - y) * (q.Y - y);
        if (d < bd || (d <= bd + 1 && n.uid == g_cnSelUid)) { bd = d; best = n.uid; }
    }
    return best;
}
static Color CnDotColor(const CnNote &n)
{
    switch (n.type) {
    case CT_CARRE: case CT_BOSSE: return Color(255, 255, 127, 39);
    case CT_DANGER: return Color(255, 170, 20, 40);
    case CT_PONT: return Color(255, 20, 160, 230);
    case CT_ARRIVEE: return g_dark ? Color(255, 235, 235, 235) : Color(255, 20, 20, 20);
    case CT_TEXTE: return Color(255, 130, 130, 140);
    default: return kCnPen;
    }
}
static void DrawCnMap(Graphics &g, RectF r)
{
    g_cnMapR = r;
    CnStage *s = CnStageCur();
    { GraphicsPath bp; RoundRect(bp, r, 14); SolidBrush bb(g_dark ? Color(255, 22, 26, 34) : Color(255, 244, 246, 250)); g.FillPath(&bb, &bp); }
    if (!s) return;
    if (g_cnViewSt != g_cnSt || g_cnZoom <= 0) CnFit();
    GraphicsState st = g.Save();
    { GraphicsPath cp; RoundRect(cp, r, 14); g.SetClip(&cp, CombineModeIntersect); }
    g.SetSmoothingMode(SmoothingModeAntiAlias);
    {   // quadrillage de 100 m quand on est assez pres
        float stepW = g_cnZoom * 100 >= 24 ? 100.0f : g_cnZoom * 500 >= 24 ? 500.0f : 0.0f;
        if (stepW > 0) {
            Pen gp(g_dark ? Color(18, 255, 255, 255) : Color(22, 16, 30, 48), 1.0f);
            float wx0 = g_cnCx - r.Width / 2 / g_cnZoom, wz0 = g_cnCz - r.Height / 2 / g_cnZoom;
            for (float x = floorf(wx0 / stepW) * stepW; x < wx0 + r.Width / g_cnZoom + stepW; x += stepW) { PointF a = CnToScr(x, 0); g.DrawLine(&gp, a.X, r.Y, a.X, r.Y + r.Height); }
            for (float z = floorf(wz0 / stepW) * stepW; z < wz0 + r.Height / g_cnZoom + stepW; z += stepW) { PointF a = CnToScr(0, z); g.DrawLine(&gp, r.X, a.Y, r.X + r.Width, a.Y); }
        }
    }
    std::vector<PointF> pts; pts.reserve(s->p.size());
    for (auto &q : s->p) pts.push_back(CnToScr(q.X, q.Y));
    { Pen halo(g_dark ? Color(70, 120, 140, 255) : Color(60, 63, 72, 204), 7.0f); halo.SetLineJoin(LineJoinRound); g.DrawLines(&halo, pts.data(), (INT)pts.size()); }
    { Pen rp(g_dark ? Color(255, 140, 150, 255) : kCnPen, 2.2f); rp.SetLineJoin(LineJoinRound); g.DrawLines(&rp, pts.data(), (INT)pts.size()); }
    { SolidBrush gb(Color(255, 40, 170, 80)); g.FillEllipse(&gb, pts[0].X - 6, pts[0].Y - 6, 12.0f, 12.0f); }
    DrawCnCell(g, 29, RectF(pts.back().X - 14, pts.back().Y - 26, 28, 24));
    if (g_cnHover >= 0 && g_cnHover < (int)pts.size() && g_cnDrag == 0) { Pen hp(WithA(kAcc, 0.9f), 2.0f); g.DrawEllipse(&hp, pts[g_cnHover].X - 7, pts[g_cnHover].Y - 7, 14.0f, 14.0f); }
    const std::vector<CnNote> &ns = CnNotesShown();
    bool labels = g_cnZoom >= 0.7f;
    for (int pass = 0; pass < 2; pass++)
        for (auto &n : ns) {
            if (n.m > s->len) continue;
            bool sel = n.uid == g_cnSelUid;
            if ((pass == 1) != sel) continue;   // (la note choisie par-dessus)
            PointF q = CnNoteScr(*s, n.m);
            SolidBrush db(CnDotColor(n)); g.FillEllipse(&db, q.X - 5, q.Y - 5, 10.0f, 10.0f);
            Pen ep(g_dark ? Color(200, 20, 22, 30) : Color(230, 255, 255, 255), 1.6f); g.DrawEllipse(&ep, q.X - 5, q.Y - 5, 10.0f, 10.0f);
            if (sel) { Pen sp(kAcc, 2.4f); g.DrawEllipse(&sp, q.X - 9, q.Y - 9, 18.0f, 18.0f); }
            if (labels || sel) {
                RectF lb(q.X + 10, q.Y - 13, 120, 26);
                SolidBrush lbg(Color(sel ? 235 : 200, 250, 246, 233));
                std::vector<int> cells; CnCells(n, cells);
                float wEst = n.type == CT_TEXTE ? min(130.0f, n.text.size() * 8.0f + 10) : cells.size() * 20.0f + 6;
                GraphicsPath lp; RoundRect(lp, RectF(lb.X, lb.Y, wEst, lb.Height), 6); g.FillPath(&lbg, &lp);
                DrawCnLine(g, n, lb.X + 3, q.Y, 19, lb.X + wEst);
            }
        }
    g.Restore(st);
}

// ---- les champs d'une note (ouverts a cote du point)
static const wchar_t *CnTypeLabel(int t)
{
    switch (t) {
    case CT_VIRAGE: return T(L"Virage", L"Corner"); case CT_CARRE: return T(L"Carré", L"Square");
    case CT_BOSSE: return T(L"Bosse", L"Crest"); case CT_PONT: return T(L"Pont", L"Bridge");
    case CT_DANGER: return T(L"Danger", L"Caution"); case CT_ARRIVEE: return T(L"Arrivée", L"Finish");
    case CT_DROIT: return T(L"Tout droit", L"Straight"); default: return T(L"Texte", L"Text");
    }
}
static const wchar_t *CnModLabel(int k)
{
    switch (k) {
    case CM_LONG: return T(L"Long", L"Long corner"); case CM_RESSERRE: return T(L"Se resserre", L"Tightens");
    case CM_OUVRE: return T(L"S'ouvre", L"Opens"); case CM_NC: return T(L"Ne coupe pas", L"Don't cut");
    case CM_DANGER: return L"!"; default: return T(L"Enchaîné", L"Into");
    }
}
static void CnChip(Graphics &g, RectF r, const std::wstring &s, bool on, int id, bool en)
{
    bool hot = en && g_uiHot == id;
    GraphicsPath p; RoundRect(p, r, r.Height / 2);
    SolidBrush b(on ? WithA(kAcc, en ? 0.9f : 0.4f) : hot ? TH(cardSel) : TH(field)); g.FillPath(&b, &p);
    Pen pen(on ? kAcc : TH(fieldBorder), 1.0f); g.DrawPath(&pen, &p);
    Text(g, s, r, 11.5f, FontStyleBold, on ? kOnAcc : WithA(kInk, en ? 1.0f : 0.45f));
    if (en) HotZone(r, id);
}
static float CnChipW(const std::wstring &s) { return MeasureW(MeasureG(), s, 11.5f, FontStyleBold) + 20; }
// Rangee de pastilles coupee a la largeur w ; rend la hauteur prise.
template <class F> static float CnChipRow(Graphics &g, float x, float y, float w, int n, F item)
{
    float cx = x, cy = y;
    for (int i = 0; i < n; i++) {
        std::wstring s; bool on; int id; bool en; item(i, s, on, id, en);
        float cw = CnChipW(s);
        if (cx > x && cx + cw > x + w) { cx = x; cy += 30; }
        CnChip(g, RectF(cx, cy, cw, 26), s, on, id, en);
        cx += cw + 6;
    }
    return cy + 26 - y;
}
static void DrawCnPop(Graphics &g, RectF card)
{
    CnStage *s = CnStageCur(); const CnNote *n = CnSel();
    if (!s || !n || !g_cnPop) return;
    bool en = !g_cnGameOn;
    const float W = 330;
    // hauteur : mesure a vide (meme code, Graphics de mesure, zones jetees)
    auto body = [&](Graphics &gg, float x, float y) -> float {
        float y0 = y, w = W - 32;
        Text(gg, T(L"Type", L"Type"), RectF(x, y, w, 18), 11.5f, FontStyleBold, kGrey, StringAlignmentNear); y += 20;
        y += CnChipRow(gg, x, y, w, CT_N, [&](int i, std::wstring &l, bool &on, int &id, bool &e) { l = CnTypeLabel(i); on = n->type == i; id = UI_CN_TYPE + i; e = en; }) + 10;
        if (n->type == CT_VIRAGE || n->type == CT_CARRE) {
            Text(gg, n->type == CT_VIRAGE ? T(L"Côté et cote (H = épingle)", L"Side and grade (H = hairpin)") : T(L"Côté", L"Side"), RectF(x, y, w, 18), 11.5f, FontStyleBold, kGrey, StringAlignmentNear); y += 20;
            float cx = x;
            for (int k = 0; k < 2; k++) { std::wstring l = k ? T(L"Droite", L"Right") : T(L"Gauche", L"Left"); float cw = CnChipW(l); CnChip(gg, RectF(cx, y, cw, 26), l, (n->side > 0) == (k == 1), UI_CN_SIDE + k, en); cx += cw + 6; }
            if (n->type == CT_VIRAGE) {
                cx += 8;
                for (int k = 0; k <= 6; k++) { std::wstring l = k ? std::to_wstring(k) : L"H"; float cw = 28; if (cx + cw > x + w) { cx = x; y += 30; } CnChip(gg, RectF(cx, y, cw, 26), l, n->grade == k, UI_CN_GRADE + k, en); cx += cw + 4; }
            }
            y += 36;
        }
        if (n->type == CT_TEXTE) {
            Text(gg, T(L"Texte (40 caractères au plus)", L"Text (40 characters at most)"), RectF(x, y, w, 18), 11.5f, FontStyleBold, kGrey, StringAlignmentNear); y += 20;
            RectF fr(x, y, w, 32);
            GraphicsPath fp; RoundRect(fp, fr, 9); SolidBrush fb(TH(field)); gg.FillPath(&fb, &fp); Pen fpn(en ? kAcc : TH(fieldBorder), 1.4f); gg.DrawPath(&fpn, &fp);
            RectF tr(fr.X + 10, fr.Y, fr.Width - 20, fr.Height);
            Text(gg, n->text.empty() ? T(L"Écris ta note", L"Write your note") : n->text, tr, 13, FontStyleBold, n->text.empty() ? TH(placeholder) : kInk, StringAlignmentNear);
            if (en && fmodf(g_time, 1.0f) < 0.55f) { float tw = n->text.empty() ? 0 : MeasureW(gg, n->text, 13, FontStyleBold); Pen cp(kAcc, 1.5f); gg.DrawLine(&cp, min(tr.X + tw + 1, tr.X + tr.Width), fr.Y + 7, min(tr.X + tw + 1, tr.X + tr.Width), fr.Y + fr.Height - 7); }
            y += 42;
        }
        if (n->type != CT_ARRIVEE) {
            bool turn = n->type == CT_VIRAGE || n->type == CT_CARRE;
            Text(gg, T(L"Modificateurs", L"Modifiers"), RectF(x, y, w, 18), 11.5f, FontStyleBold, kGrey, StringAlignmentNear); y += 20;
            std::vector<int> ks; for (int k = 0; k < CM_N; k++) if (turn || k == CM_DANGER || k == CM_ENCHAINE) ks.push_back(k);
            y += CnChipRow(gg, x, y, w, (int)ks.size(), [&](int i, std::wstring &l, bool &on, int &id, bool &e) { l = CnModLabel(ks[i]); on = (n->mods >> ks[i]) & 1; id = UI_CN_MOD + ks[i]; e = en; }) + 10;
        }
        return y - y0;
    };
    size_t z0 = g_zones.size();
    float bh = body(MeasureG(), 0, 0);
    g_zones.resize(z0);
    float H = 16 + 22 + 8 + 40 + 8 + bh + 44 + 14;
    PointF q = CnNoteScr(*s, n->m);
    float x = q.X + 22 + W <= card.X + card.Width - 8 ? q.X + 22 : q.X - 22 - W;
    x = min(max(x, card.X + 8), card.X + card.Width - 8 - W);
    float y = min(max(q.Y - 60, card.Y + 60), card.Y + card.Height - 8 - H);
    RectF r(x, y, W, H);
    { GraphicsPath sp; RoundRect(sp, RectF(r.X + 2, r.Y + 5, r.Width, r.Height), 16); SolidBrush sb(Color(70, 0, 0, 0)); g.FillPath(&sb, &sp); }
    { GraphicsPath pp; RoundRect(pp, r, 16); SolidBrush pb(g_dark ? Color(255, 34, 38, 48) : Color(255, 255, 255, 255)); g.FillPath(&pb, &pp); Pen pe(WithA(kAcc, 0.5f), 1.2f); g.DrawPath(&pe, &pp); }
    HotZone(r, UI_CN_POP);   // (clic dans les champs : pas une nouvelle note)
    float ix = r.X + 16, iw = W - 32, iy = r.Y + 16;
    wchar_t hd[96]; swprintf_s(hd, T(L"Note à %d m", L"Note at %d m"), n->m);
    Text(g, hd, RectF(ix, iy, iw - 30, 22), 13.5f, FontStyleBold, kInk, StringAlignmentNear);
    UiButton(g, RectF(r.X + r.Width - 16 - 26, iy - 2, 26, 26), L"", UI_CN_NOK, false, IC_CLOSE, true, 9);
    iy += 30;
    { RectF lr(ix, iy, iw, 40); SolidBrush lb(Color(255, 250, 246, 233)); GraphicsPath lp; RoundRect(lp, lr, 8); g.FillPath(&lb, &lp); DrawCnLine(g, *n, lr.X + 8, lr.Y + 20, 30, lr.X + lr.Width - 6); }
    iy += 48;
    iy += body(g, ix, iy);
    float bw = (iw - 8) / 2;
    UiButton(g, RectF(ix, iy, bw, 36), T(L"Retirer", L"Remove"), UI_CN_NDEL, false, IC_TRASH, en, 10);
    UiButton(g, RectF(ix + bw + 8, iy, bw, 36), T(L"OK", L"OK"), UI_CN_NOK, true, IC_CHECK, true, 10);
}

// ---- la page
static RectF kCnCard, kCnView;
static void CarnetLayout() { float x = g_pageR.X, w = g_pageR.Width, rw = 364, gap = 12; kCnCard = RectF(x, 88, w - rw - gap, 656); kCnView = RectF(x + w - rw, 88, rw, 656); }
static void DrawCarnetPage(Graphics &g)
{
    CarnetLayout();
    if (GetTickCount() - g_cnScanT > 2000) CnLoad(false);
    if (g_cnCapNote) {   // (/capture carnet-note)
        g_cnCapNote = false;
        for (auto &n : CnNotesShown()) if (n.type == CT_VIRAGE) { g_cnSelUid = n.uid; g_cnPop = true; g_cnPos = (float)n.m; break; }
    }
    RectF c = kCnCard, v = kCnView;
    Glass(c); Glass(v);
    float lx = c.X + 18, lw = c.Width - 36;
    CnStage *s = CnStageCur();
    bool en = !g_cnGameOn;
    // en-tete : speciale (‹ nom ›), notes auto ou du carnet, Reinitialiser
    {
        float y = c.Y + 14;
        UiButton(g, RectF(lx, y, 32, 32), L"", UI_CN_PREV, false, IC_CHEVL, g_cnStages.size() > 1, 10);
        std::wstring nm = s ? CnStageLabel(*s) : T(L"Spéciales", L"Stages");
        float nw = min(MeasureW(g, nm, 16, FontStyleBold) + 10, lw * 0.5f);
        Title(g, nm, RectF(lx + 40, y - 2, nw, 36), 16, kInk);
        UiButton(g, RectF(lx + 40 + nw + 4, y, 32, 32), L"", UI_CN_NEXT, false, IC_CHEVR, g_cnStages.size() > 1, 10);
        std::wstring rs = g_cnResetT && GetTickCount() - g_cnResetT < 6000 ? T(L"Confirmer ?", L"Confirm?") : T(L"Réinitialiser", L"Reset");
        float rw = MeasureW(g, rs, 12.5f, FontStyleBold) + 56;
        UiButton(g, RectF(lx + lw - rw, y, rw, 32), rs, UI_CN_RESET, false, IC_UNDO, en && CnStageOwn(), 10);
        UiButton(g, RectF(lx + lw - rw - 8 - 32, y, 32, 32), L"", UI_CN_FIT, false, IC_MAP, s != NULL, 10);
        if (s) {
            wchar_t inf[160];
            int cnt = 0; for (auto &n : CnNotesShown()) if (n.m <= s->len) cnt++;
            swprintf_s(inf, T(L"%d,%d km · %d notes · ", L"%d.%d km · %d notes · "), (s->len + 50) / 1000, (s->len + 50) % 1000 / 100, cnt);
            std::wstring w = std::wstring(inf) + (CnStageOwn() ? T(L"notes de ton carnet", L"your notebook's notes") : T(L"notes automatiques (modifie-les : elles passent dans ton carnet)", L"automatic notes (change them: they go into your notebook)"));
            float tw = lw;
            if (g_cnGameOn) { std::wstring gs = T(L"Jeu ouvert : lecture seule", L"Game open: read only"); float pw = PillW(gs, 11.5f); MediaChip(g, RectF(lx + lw - pw, c.Y + 54, pw, 22), gs, 2); tw -= pw + 12; }
            Text(g, w, RectF(lx, c.Y + 54, tw, 22), 12, FontStyleRegular, kGrey, StringAlignmentNear);
        }
    }
    RectF mr(lx, c.Y + 84, lw, c.Height - 84 - 14 - 26);
    if (!s) {
        DrawCnMap(g, mr);
        Icon(g, IC_MAP, mr.X + mr.Width / 2, mr.Y + mr.Height / 2 - 60, 56, WithA(kGrey, 0.45f), 1.4f);
        std::wstring m = !g_cnStagesErr.empty() ? T(L"Le fichier des spéciales est illisible : relance une partie, le mod le réécrit. (", L"The stages file can't be read: start a game again, the mod rewrites it. (") + g_cnStagesErr + L")"
                         : T(L"Lance une partie une fois avec le mod : il écrit ici le tracé des spéciales du rallye (speciales-", L"Start a game once with the mod: it writes the rally stages' layout here (speciales-") + CnGame() + L".json).";
        Para(g, m, RectF(mr.X + 40, mr.Y + mr.Height / 2, mr.Width - 80, 80), 13.5f, kGrey, StringAlignmentNear);
    } else {
        DrawCnMap(g, mr);
        DrawCnPop(g, c);
    }
    Text(g, T(L"Clic sur le tracé : poser une note · la glisser : la déplacer · Suppr : la retirer · molette : zoom", L"Click the layout: add a note · drag it: move it · Del: remove it · wheel: zoom"),
         RectF(lx, c.Y + c.Height - 14 - 22, lw, 22), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);

    // a droite : le carnet (‹ nom ›, actif), la page, les boutons
    float vx = v.X + 16, vw = v.Width - 32, y = v.Y + 14;
    CnBook *b = CnBookCur();
    int bi = -1; for (int i = 0; i < (int)g_cnBooks.size(); i++) if (g_cnBooks[i].file == g_cnBookFile) bi = i;
    if (g_cnEdit && b) {
        RectF fr(vx, y, vw - 84, 32);
        GraphicsPath fp; RoundRect(fp, fr, 9); SolidBrush fb(TH(field)); g.FillPath(&fb, &fp); Pen fpn(kAcc, 1.6f); g.DrawPath(&fpn, &fp);
        RectF tr(fr.X + 10, fr.Y, fr.Width - 20, fr.Height);
        Text(g, g_cnEditText.empty() ? T(L"Nom du carnet", L"Notebook name") : g_cnEditText, tr, 13.5f, FontStyleBold, g_cnEditText.empty() ? TH(placeholder) : kInk, StringAlignmentNear);
        if (fmodf(g_time, 1.0f) < 0.55f) { float tw = g_cnEditText.empty() ? 0 : MeasureW(g, g_cnEditText, 13.5f, FontStyleBold); Pen cp(kAcc, 1.6f); g.DrawLine(&cp, min(tr.X + tw + 1, tr.X + tr.Width), fr.Y + 7, min(tr.X + tw + 1, tr.X + tr.Width), fr.Y + fr.Height - 7); }
        UiButton(g, RectF(vx + vw - 78, y, 36, 32), L"", UI_CN_REN_CANCEL, false, IC_CLOSE, true, 10);
        UiButton(g, RectF(vx + vw - 36, y, 36, 32), L"", UI_CN_REN_OK, true, IC_CHECK, true, 10);
    } else {
        UiButton(g, RectF(vx, y, 30, 32), L"", UI_CN_BPREV, false, IC_CHEVL, g_cnBooks.size() > 1, 10);
        std::wstring nm = b ? b->name : T(L"Aucun carnet", L"No notebook");
        Text(g, nm, RectF(vx + 36, y, vw - 36 - 30 - 6 - 34, 32), 14, FontStyleBold, b ? kInk : kGrey, StringAlignmentNear);
        UiButton(g, RectF(vx + vw - 30 - 6 - 30, y, 30, 32), L"", UI_CN_RENAME, false, IC_PENCIL, b && en, 10);
        UiButton(g, RectF(vx + vw - 30, y, 30, 32), L"", UI_CN_BNEXT, false, IC_CHEVR, g_cnBooks.size() > 1, 10);
    }
    y += 38;
    {
        bool act = b && b->file == g_cnActive;
        std::wstring a = act ? T(L"Carnet actif : le jeu lit celui-ci", L"Active notebook: the game reads this one") : g_cnActive.empty() ? T(L"Aucun carnet actif : notes automatiques en jeu", L"No active notebook: automatic notes in game") : T(L"Pas le carnet actif", L"Not the active notebook");
        if (bi >= 0 && g_cnBooks.size() > 1) a = std::to_wstring(bi + 1) + L"/" + std::to_wstring(g_cnBooks.size()) + L" · " + a;
        Text(g, a, RectF(vx, y, vw, 18), 11.5f, act ? FontStyleBold : FontStyleRegular, act ? kOk : kGrey, StringAlignmentNear);
    }
    y += 24;
    float bh = 3 * 40 + 2 * 8, availH = v.Y + v.Height - 14 - bh - 12 - y;
    float pw = min(vw, availH / 1.414f), ph = pw * 1.414f;
    DrawCnPage(g, RectF(vx + (vw - pw) / 2, y, pw, ph));
    if (s) {   // (molette sur la page : avancer / reculer)
        wchar_t ps[64]; swprintf_s(ps, L"%d m", (int)min(max(g_cnPos, 0.0f), (float)s->len));
        Text(g, ps, RectF(vx + (vw - pw) / 2 + pw - 70, y + ph - 20, 64, 16), 10.5f, FontStyleBold, Color(150, 63, 72, 204), StringAlignmentFar);
    }
    float by = v.Y + v.Height - 14 - bh, hw = (vw - 8) / 2;
    bool act = b && b->file == g_cnActive;
    UiButton(g, RectF(vx, by, vw, 40), act ? T(L"Ne plus l'utiliser en jeu", L"Stop using it in game") : T(L"Utiliser en jeu (carnet actif)", L"Use in game (active notebook)"), UI_CN_ACTIVE, !act, act ? IC_CLOSE : IC_CHECK, en && b, 12);
    by += 48;
    UiButton(g, RectF(vx, by, hw, 40), T(L"Nouveau", L"New"), UI_CN_NEW, false, IC_PLUS, en, 12);
    UiButton(g, RectF(vx + hw + 8, by, hw, 40), T(L"Importer", L"Import"), UI_CN_IMPORT, false, IC_FOLDER, en, 12);
    by += 48;
    bool arm = g_cnDelT && GetTickCount() - g_cnDelT < 6000;
    UiButton(g, RectF(vx, by, hw, 40), T(L"Exporter", L"Export"), UI_CN_EXPORT, false, IC_DOWNLOAD, b != NULL, 12);
    UiButton(g, RectF(vx + hw + 8, by, hw, 40), arm ? T(L"Confirmer ?", L"Confirm?") : T(L"Supprimer", L"Delete"), UI_CN_DEL, false, IC_TRASH, en && b, 12);
}

// ---- souris, clavier
static void CnSelect(int uid) { g_cnSelUid = uid; g_cnPop = uid >= 0; const CnNote *n = CnSel(); if (n) g_cnPos = (float)n->m; }
static bool CarnetMouseDown(float x, float y)
{
    CnStage *s = CnStageCur();
    if (!s || !g_cnMapR.Contains(x, y)) return false;
    g_cnEdit = false;
    int uid = CnNoteAt(x, y);
    g_cnDx = x; g_cnDy = y; g_cnMoved = false;
    if (uid >= 0) { CnSelect(uid); g_cnDrag = 1; SetCapture(g_wnd); return true; }
    int i = CnNearestIdx(x, y, 10);
    if (i >= 0) {
        std::vector<CnNote> *v = CnEditable();
        if (!v) return true;
        if ((int)v->size() >= kCnMaxNotes) { SetStatus(K_WARN, T(L"1000 notes au plus sur une spéciale", L"1000 notes at most on a stage")); return true; }
        CnNote n = CnSuggest(*s, i);
        v->push_back(n);
        CnCommit();
        CnSelect(n.uid);
        return true;
    }
    g_cnDrag = 2; g_cnDcx = g_cnCx; g_cnDcz = g_cnCz; SetCapture(g_wnd);
    return true;
}
static void CarnetDragTo(float x, float y)
{
    CnStage *s = CnStageCur();
    if (!s) { g_cnDrag = 0; return; }
    if (!g_cnMoved && fabsf(x - g_cnDx) + fabsf(y - g_cnDy) < 4) return;
    g_cnMoved = true;
    if (g_cnDrag == 2) { g_cnCx = g_cnDcx - (x - g_cnDx) / g_cnZoom; g_cnCz = g_cnDcz + (y - g_cnDy) / g_cnZoom; InvalidateAll(); return; }
    const CnNote *n = CnSel(); if (!n || g_cnGameOn) return;
    int cur = (int)(n->m / s->step + 0.5f);
    int i = CnNearestIdx(x, y, 60, cur, (int)(250 / s->step));
    if (i < 0) return;
    int m = (int)(i * s->step + 0.5f);
    if (m == n->m) return;
    CnNote *e = CnSelEdit(); if (!e) return;
    e->m = m; g_cnPos = (float)m;
    InvalidateAll();
}
static void CarnetMouseUp()
{
    int d = g_cnDrag; g_cnDrag = 0;
    if (d == 1 && g_cnMoved) CnCommit();
    if (d == 2 && !g_cnMoved) { g_cnSelUid = -1; g_cnPop = false; }   // (clic sur le fond : ferme les champs)
}
static bool CarnetHover(float x, float y)   // (curseur main sur une note ou le trace ; l'apercu suit la souris sans note choisie)
{
    if (!CnStageCur() || !g_cnMapR.Contains(x, y)) { if (g_cnHover >= 0) { g_cnHover = -1; InvalidateAll(); } return false; }
    int on = CnNoteAt(x, y), i = on >= 0 ? -1 : CnNearestIdx(x, y, 10);
    if (i != g_cnHover) { g_cnHover = i; if (i >= 0 && g_cnSelUid < 0) g_cnPos = i * CnStageCur()->step; InvalidateAll(); }
    return on >= 0 || i >= 0;
}
static bool CarnetWheel(float x, float y, int delta)
{
    CnStage *s = CnStageCur(); if (!s) return false;
    if (kCnView.Contains(x, y)) { g_cnPos = min(max(g_cnPos - delta / 120.0f * 20.0f, 0.0f), (float)s->len); return true; }
    if (!g_cnMapR.Contains(x, y)) return false;
    float wx = g_cnCx + (x - (g_cnMapR.X + g_cnMapR.Width / 2)) / g_cnZoom, wz = g_cnCz - (y - (g_cnMapR.Y + g_cnMapR.Height / 2)) / g_cnZoom;
    float z = g_cnZoom * powf(1.25f, delta / 120.0f);
    z = min(max(z, g_cnFitZoom * 0.6f), 8.0f);
    g_cnCx = wx - (x - (g_cnMapR.X + g_cnMapR.Width / 2)) / z; g_cnCz = wz + (y - (g_cnMapR.Y + g_cnMapR.Height / 2)) / z;
    g_cnZoom = z;
    return true;
}
static void CnDeleteSel()
{
    std::vector<CnNote> *v = CnEditable(); if (!v) return;
    for (size_t i = 0; i < v->size(); i++) if ((*v)[i].uid == g_cnSelUid) { v->erase(v->begin() + i); break; }
    g_cnSelUid = -1; g_cnPop = false;
    CnCommit();
}
static bool CarnetKey(WPARAM k)
{
    if (g_tab != TAB_CARNET || g_cnEdit) return false;
    if (k == VK_DELETE && CnSel()) { CnDeleteSel(); return true; }
    return false;
}
static void CnRenameOk()
{
    CnBook *b = CnBookCur(); g_cnEdit = false;
    std::wstring n = Trim(g_cnEditText);
    if (!b || !CnTextOk(n, kCnNameMax) || !CnGuard()) return;
    b->name = n; CnSaveBook(*b);
}
static bool CarnetChar(wchar_t c)
{
    if (g_tab != TAB_CARNET) return false;
    if (g_cnEdit) {
        if (c == 8) { if (!g_cnEditText.empty()) g_cnEditText.pop_back(); }
        else if (c == 127) g_cnEditText.clear();
        else if (c == 13) CnRenameOk();
        else if (c == 27) g_cnEdit = false;
        else if (c >= 32 && g_cnEditText.size() < (size_t)kCnNameMax) g_cnEditText += c;
        g_time = 0.2f;
        return true;
    }
    const CnNote *n = CnSel();
    if (!g_cnPop || !n) return false;
    if (c == 27 || (c == 13 && n->type != CT_TEXTE)) { g_cnPop = false; g_cnSelUid = -1; return true; }
    if (n->type != CT_TEXTE) return false;
    if (c == 13) { g_cnPop = false; g_cnSelUid = -1; return true; }
    std::wstring t = n->text;
    if (c == 8) { if (!t.empty()) t.pop_back(); }
    else if (c == 127) t.clear();
    else if (c == 22 && OpenClipboard(g_wnd)) {
        HANDLE d = GetClipboardData(CF_UNICODETEXT);
        const wchar_t *p = d ? (const wchar_t *)GlobalLock(d) : NULL;
        if (p) { for (; *p && *p != L'\r' && *p != L'\n' && t.size() < (size_t)kCnTextMax; p++) if (CnTextOk(std::wstring(1, *p), 1)) t += *p; GlobalUnlock(d); }
        CloseClipboard();
    }
    else if (c >= 32 && CnTextOk(std::wstring(1, c), 1) && t.size() < (size_t)kCnTextMax) t += c;
    else return true;
    CnNote *e = CnSelEdit(); if (!e) return true;
    e->text = t;
    if (!Trim(t).empty()) CnCommit();   // (vide : pas ecrit -- le fichier refuserait un texte vide)
    g_time = 0.2f;
    return true;
}

// ---- boutons
static bool CnImportFile(const std::wstring &path, std::wstring &err)
{
    std::string s; CnBook b;
    if (!CnReadFile(path, kCnMaxBytes, s)) { err = T(L"fichier absent ou de plus de 256 Ko", L"file missing or over 256 KB"); return false; }
    if (!CnParseBook(s, b, err)) return false;
    if (b.game != CnGame()) { err = T(L"c'est un carnet de l'autre jeu", L"it's a notebook for the other game"); return false; }   // (pas de nom de jeu : T() echange My Winter Car et My Summer Car sur MSC)
    for (auto &o : g_cnBooks) if (!_wcsicmp(o.name.c_str(), b.name.c_str())) { std::wstring n2 = b.name.substr(0, kCnNameMax - 4) + L" (2)"; b.name = n2; break; }
    b.file = CnUniqueFile(b.name);
    if (b.file.empty() || !CnSaveBook(b)) { err = T(L"écriture impossible", L"could not write"); return false; }
    g_cnBooks.push_back(b); g_cnBookFile = b.file; g_cnSelUid = -1; g_cnPop = false;
    return true;
}
static void CnImport()
{
    if (!CnGuard()) return;
    wchar_t file[MAX_PATH] = L"";
    OPENFILENAMEW of = { sizeof(of) };
    of.hwndOwner = g_wnd; of.lpstrFilter = L"Carnet (*.json)\0*.json\0"; of.lpstrFile = file; of.nMaxFile = MAX_PATH;
    of.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR;
    if (!GetOpenFileNameW(&of)) return;
    std::wstring err;
    if (CnImportFile(file, err)) SetStatus(K_OK, T(L"Carnet « %s » importé", L"Notebook \"%s\" imported"), CnBookCur()->name.c_str());
    else SetStatus(K_ERR, T(L"Carnet refusé : %s", L"Notebook refused: %s"), err.c_str());
}
static void CnExport()
{
    CnBook *b = CnBookCur(); if (!b) return;
    wchar_t file[MAX_PATH]; wcsncpy_s(file, b->file.c_str(), _TRUNCATE);
    OPENFILENAMEW of = { sizeof(of) };
    of.hwndOwner = g_wnd; of.lpstrFilter = L"Carnet (*.json)\0*.json\0"; of.lpstrFile = file; of.nMaxFile = MAX_PATH; of.lpstrDefExt = L"json";
    of.Flags = OFN_OVERWRITEPROMPT | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR;
    if (!GetSaveFileNameW(&of)) return;
    if (CnWriteAtomic(file, CnBookJson(*b))) SetStatus(K_OK, T(L"Carnet exporté : %s", L"Notebook exported: %s"), file);
    else SetStatus(K_ERR, T(L"Export impossible", L"Export failed"));
}
static void CarnetClick(int id)
{
    CnStage *s = CnStageCur(); CnBook *b = CnBookCur();
    if (id == UI_CN_PREV || id == UI_CN_NEXT) {
        int n = (int)g_cnStages.size(); if (n < 2) return;
        g_cnSt = (g_cnSt + (id == UI_CN_NEXT ? 1 : n - 1)) % n; g_cnSelUid = -1; g_cnPop = false; g_cnPos = 0; g_cnResetT = 0; return;
    }
    if (id == UI_CN_BPREV || id == UI_CN_BNEXT) {
        int n = (int)g_cnBooks.size(); if (n < 2) return;
        int i = 0; for (int k = 0; k < n; k++) if (g_cnBooks[k].file == g_cnBookFile) i = k;
        g_cnBookFile = g_cnBooks[(i + (id == UI_CN_BNEXT ? 1 : n - 1)) % n].file; g_cnSelUid = -1; g_cnPop = false; g_cnDelT = 0; g_cnResetT = 0; g_cnEdit = false; return;
    }
    if (id == UI_CN_FIT) { CnFit(); return; }
    if (id == UI_CN_POP) return;
    if (id == UI_CN_NOK) { g_cnPop = false; g_cnSelUid = -1; return; }
    if (id == UI_CN_NDEL) { CnDeleteSel(); return; }
    if (id == UI_CN_RENAME) { if (b && CnGuard()) { g_cnEdit = true; g_cnEditText = b->name; g_time = 0; } return; }
    if (id == UI_CN_REN_OK) { CnRenameOk(); return; }
    if (id == UI_CN_REN_CANCEL) { g_cnEdit = false; return; }
    if (id == UI_CN_NEW) {
        if (!CnGuard()) return;
        wchar_t nm[64]; swprintf_s(nm, T(L"Carnet %d", L"Notebook %d"), (int)g_cnBooks.size() + 1);
        if (CnNewBook(nm)) { g_cnSelUid = -1; g_cnPop = false; SetStatus(K_OK, T(L"Carnet « %s » créé : les spéciales y gardent les notes automatiques tant que tu n'y touches pas", L"Notebook \"%s\" created: the stages keep the automatic notes until you change them"), nm); }
        return;
    }
    if (id == UI_CN_IMPORT) { CnImport(); return; }
    if (id == UI_CN_EXPORT) { CnExport(); return; }
    if (id == UI_CN_ACTIVE) {
        if (!b || !CnGuard()) return;
        bool act = b->file == g_cnActive;
        if (CnSetActive(act ? L"" : b->file)) SetStatus(K_OK, act ? T(L"Plus de carnet actif : le jeu montrera les notes automatiques", L"No active notebook: the game will show the automatic notes") : T(L"Carnet actif : le jeu lira « %s »", L"Active notebook: the game will read \"%s\""), b->name.c_str());
        return;
    }
    if (id == UI_CN_RESET) {
        if (!s || !b || !CnStageOwn() || !CnGuard()) return;
        if (!g_cnResetT || GetTickCount() - g_cnResetT > 6000) { g_cnResetT = GetTickCount(); SetStatus(K_WARN, T(L"Clique encore : les notes de %s reviennent aux notes automatiques", L"Click again: the notes of %s go back to the automatic notes"), s->name.c_str()); return; }
        g_cnResetT = 0; b->st.erase(s->id); g_cnSelUid = -1; g_cnPop = false; CnSaveBook(*b);
        return;
    }
    if (id == UI_CN_DEL) {
        if (!b || !CnGuard()) return;
        if (!g_cnDelT || GetTickCount() - g_cnDelT > 6000) { g_cnDelT = GetTickCount(); SetStatus(K_WARN, T(L"Clique encore sur Confirmer : « %s » part à la Corbeille", L"Click Confirm again: \"%s\" goes to the Recycle Bin"), b->name.c_str()); return; }
        g_cnDelT = 0;
        std::wstring f = b->file;
        if (!ToRecycleBin({ CnLibDir() + f })) { SetStatus(K_ERR, T(L"Suppression impossible", L"Could not delete")); return; }
        if (f == g_cnActive) CnSetActive(L"");
        g_cnBookFile.clear(); g_cnSelUid = -1; g_cnPop = false;
        CnLoad(true);
        return;
    }
    // champs de la note choisie
    CnNote *n = NULL;
    if ((id >= UI_CN_TYPE && id < UI_CN_TYPE + CT_N) || id == UI_CN_SIDE || id == UI_CN_SIDE + 1 || (id >= UI_CN_GRADE && id <= UI_CN_GRADE + 6) || (id >= UI_CN_MOD && id < UI_CN_MOD + CM_N)) n = CnSelEdit();
    if (!n) return;
    if (id >= UI_CN_TYPE && id < UI_CN_TYPE + CT_N) {
        n->type = id - UI_CN_TYPE;
        if (n->type == CT_TEXTE && Trim(n->text).empty()) { n->text = T(L"note", L"note"); }
    }
    else if (id == UI_CN_SIDE || id == UI_CN_SIDE + 1) n->side = id == UI_CN_SIDE ? -1 : 1;
    else if (id >= UI_CN_GRADE && id <= UI_CN_GRADE + 6) n->grade = id - UI_CN_GRADE;
    else {
        int k = id - UI_CN_MOD;
        n->mods ^= 1u << k;
        if (k == CM_RESSERRE && (n->mods >> k & 1)) n->mods &= ~(1u << CM_OUVRE);
        if (k == CM_OUVRE && (n->mods >> k & 1)) n->mods &= ~(1u << CM_RESSERRE);
    }
    CnCommit();
}

// ---- /testcarnet <journal> : lecture des speciales, regles du carnet (fichiers bons et mauvais), ecriture et relecture,
// premiere modification (copie des notes auto), carnet actif, import (validation, autre jeu), jeu ouvert. Dossier
// LOCALAPPDATA jetable seulement ; laisse une speciale et un carnet d'essai pour les captures « carnet ».
static std::string CnFixtureStages(const std::wstring &game)
{
    // une speciale de ~3,4 km : droites, virages de rayons varies, une epingle, un carre, un pont, l'arrivee
    struct Seg { float len, deg; } segs[] = { { 120, 0 }, { 60, 40 }, { 80, 0 }, { 45, -70 }, { 150, 0 }, { 30, 90 }, { 60, 0 }, { 50, -160 }, { 90, 0 },
                                              { 120, 55 }, { 70, -30 }, { 200, 0 }, { 40, -85 }, { 110, 0 }, { 90, 120 }, { 60, 0 }, { 140, -45 }, { 300, 0 },
                                              { 60, 75 }, { 40, -60 }, { 180, 0 }, { 25, 92 }, { 220, 0 }, { 80, -110 }, { 160, 0 }, { 70, 35 }, { 400, 0 } };
    std::string xz, notes;
    float x = 120, z = -300, hd = 0.2f, step = 2, m = 0, acc = 0;
    char b[64];
    sprintf_s(b, "%.1f, %.1f", x, z); xz = b;
    for (auto &sg : segs) {
        int n = max(1, (int)(sg.len / step));
        if (sg.deg != 0) {
            float r = sg.len / (fabsf(sg.deg) / 57.29578f);
            int gr = r < 14 || fabsf(sg.deg) > 150 ? 0 : r < 22 ? 1 : r < 35 ? 2 : r < 55 ? 3 : r < 85 ? 4 : r < 130 ? 5 : 6;
            bool sq = fabsf(sg.deg) >= 70 && fabsf(sg.deg) <= 110 && sg.len < 32;
            sprintf_s(b, "%s{ \"m\": %d, \"type\": \"%s\", \"sens\": \"%s\"", notes.empty() ? "" : ", ", (int)m, sq ? "carre" : "virage", sg.deg > 0 ? "d" : "g"); notes += b;
            if (!sq) { sprintf_s(b, ", \"cote\": %d", gr); notes += b; }
            if (sg.len > 100) notes += ", \"mods\": [\"long\"]";
            notes += " }";
        }
        for (int i = 0; i < n; i++) {
            hd += sg.deg / 57.29578f / n;
            x += sinf(hd) * step; z += cosf(hd) * step; m += step;
            sprintf_s(b, ", %.1f, %.1f", x, z); xz += b;
        }
        acc += sg.len;
    }
    notes += ", { \"m\": 1290, \"type\": \"pont\" }, { \"m\": 760, \"type\": \"bosse\" }";
    sprintf_s(b, ", { \"m\": %d, \"type\": \"arrivee\" }", (int)m); notes += b;
    return "{ \"v\": 1, \"jeu\": \"" + Narrow(game) + "\", \"mod\": \"1.15.71\", \"step\": 2, \"speciales\": [\r\n"
           "  { \"id\": \"SS1\", \"nom\": \"SS1\", \"jour\": 6, \"longueur\": " + std::to_string((int)m) + ", \"xz\": [" + xz + "], \"notes\": [" + notes + "] },\r\n"
           "  { \"id\": \"SS2\", \"nom\": \"SS2\", \"jour\": 7, \"longueur\": 40, \"xz\": [0, 0, 0, 20, 0, 40], \"notes\": [] } ] }\r\n";
}
static bool CarnetSelfTest(const std::wstring &logPath)
{
    FILE *lf = NULL; _wfopen_s(&lf, logPath.c_str(), L"wb");
    int bad = 0, total = 0;
    auto say = [&](const char *what, bool ok) { total++; if (!ok) bad++; if (lf) { fprintf(lf, "%s : %s\r\n", ok ? "OK" : "ECHEC", what); fflush(lf); } };
    std::wstring root = CnRoot();
    if (root.empty() || g_gameDir.empty() || g_gameDir != g_dir) { say("dossier d'essai (exe dans un faux dossier de jeu, LOCALAPPDATA a part)", false); if (lf) fclose(lf); return false; }
    g_cnTestGame = 0;
    SHCreateDirectoryExW(NULL, CnLibDir().c_str(), NULL);
    std::wstring game = CnGame(), other = game == L"MSC" ? L"MWC" : L"MSC";
    // speciales
    CnWriteAtomic(CnStagesPath(), CnFixtureStages(game));
    CnLoad(true);
    say("speciales lues : 2 speciales", g_cnStages.size() == 2);
    say("SS1 : points, longueur, jour, notes auto (pont, arrivee)", g_cnStages.size() == 2 && g_cnStages[0].p.size() > 1000 && g_cnStages[0].len > 3000 && g_cnStages[0].day == 6 && g_cnStages[0].autoN.size() > 10
        && std::any_of(g_cnStages[0].autoN.begin(), g_cnStages[0].autoN.end(), [](const CnNote &n) { return n.type == CT_PONT; }) && g_cnStages[0].autoN.back().type == CT_ARRIVEE);
    say("SS1 : notes auto triees par m", std::is_sorted(g_cnStages[0].autoN.begin(), g_cnStages[0].autoN.end(), [](const CnNote &a, const CnNote &b) { return a.m < b.m; }));
    {   std::vector<CnStage> v; std::wstring e;
        say("speciales d'un autre jeu refusees", !CnParseStages("{ \"v\": 1, \"jeu\": \"" + Narrow(other) + "\", \"step\": 2, \"speciales\": [] }", v, e));
        say("speciales : point non numerique refuse", !CnParseStages("{ \"v\": 1, \"jeu\": \"" + Narrow(game) + "\", \"step\": 2, \"speciales\": [ { \"id\": \"SS1\", \"xz\": [0, 0, \"x\", 1] } ] }", v, e));
        say("speciales : v 2 refuse", !CnParseStages("{ \"v\": 2, \"jeu\": \"" + Narrow(game) + "\", \"step\": 2, \"speciales\": [] }", v, e)); }
    // regles du carnet
    auto ok = [&](const std::string &notes) { CnBook b; std::wstring e; return CnParseBook("{ \"v\": 1, \"jeu\": \"MWC\", \"nom\": \"Essai\", \"speciales\": { \"SS1\": { \"notes\": [" + notes + "] } } }", b, e); };
    say("carnet : exemple de la spec accepte", ok("{ \"m\": 312, \"type\": \"virage\", \"sens\": \"d\", \"cote\": 4, \"mods\": [\"long\", \"nc\"] }, { \"m\": 540, \"type\": \"pont\" }, { \"m\": 610, \"type\": \"texte\", \"texte\": \"attention vache\" }"));
    say("carnet : cle inconnue ignoree", ok("{ \"m\": 1, \"type\": \"bosse\", \"futur\": [1, 2] }"));
    say("carnet : champ inutile pour le type ignore", ok("{ \"m\": 1, \"type\": \"pont\", \"cote\": 99, \"sens\": \"x\" }"));
    say("carnet : mods sur tous les types", ok("{ \"m\": 1, \"type\": \"arrivee\", \"mods\": [\"long\", \"enchaine\"] }"));
    say("carnet : type inconnu refuse", !ok("{ \"m\": 1, \"type\": \"saut\" }"));
    say("carnet : virage sans sens refuse", !ok("{ \"m\": 1, \"type\": \"virage\", \"cote\": 3 }"));
    say("carnet : virage sans cote refuse", !ok("{ \"m\": 1, \"type\": \"virage\", \"sens\": \"g\" }"));
    say("carnet : cote 7 refusee", !ok("{ \"m\": 1, \"type\": \"virage\", \"sens\": \"g\", \"cote\": 7 }"));
    say("carnet : cote 2.5 refusee", !ok("{ \"m\": 1, \"type\": \"virage\", \"sens\": \"g\", \"cote\": 2.5 }"));
    say("carnet : carre sans sens refuse", !ok("{ \"m\": 1, \"type\": \"carre\" }"));
    say("carnet : sens x refuse", !ok("{ \"m\": 1, \"type\": \"carre\", \"sens\": \"x\" }"));
    say("carnet : m negatif refuse", !ok("{ \"m\": -1, \"type\": \"bosse\" }"));
    say("carnet : m 100001 refuse", !ok("{ \"m\": 100001, \"type\": \"bosse\" }"));
    say("carnet : m decimal refuse", !ok("{ \"m\": 12.5, \"type\": \"bosse\" }"));
    say("carnet : mod inconnu refuse", !ok("{ \"m\": 1, \"type\": \"bosse\", \"mods\": [\"turbo\"] }"));
    say("carnet : mod en double refuse", !ok("{ \"m\": 1, \"type\": \"bosse\", \"mods\": [\"long\", \"long\"] }"));
    say("carnet : resserre et ouvre ensemble refuses", !ok("{ \"m\": 1, \"type\": \"virage\", \"sens\": \"g\", \"cote\": 2, \"mods\": [\"resserre\", \"ouvre\"] }"));
    say("carnet : texte vide (apres trim) refuse", !ok("{ \"m\": 1, \"type\": \"texte\", \"texte\": \"   \" }"));
    say("carnet : texte de 41 caracteres refuse", !ok("{ \"m\": 1, \"type\": \"texte\", \"texte\": \"" + std::string(41, 'a') + "\" }"));
    say("carnet : texte avec caractere de controle refuse", !ok("{ \"m\": 1, \"type\": \"texte\", \"texte\": \"a\\u0007b\" }"));
    {   std::string many; for (int i = 0; i < 1001; i++) many += std::string(i ? ", " : "") + "{ \"m\": " + std::to_string(i) + ", \"type\": \"bosse\" }";
        say("carnet : 1001 notes refusees", !ok(many)); }
    {   CnBook b; std::wstring e;
        say("carnet : v 2 refuse", !CnParseBook("{ \"v\": 2, \"jeu\": \"MWC\", \"nom\": \"x\" }", b, e));
        say("carnet : jeu inconnu refuse", !CnParseBook("{ \"v\": 1, \"jeu\": \"GTA\", \"nom\": \"x\" }", b, e));
        say("carnet : nom vide refuse", !CnParseBook("{ \"v\": 1, \"jeu\": \"MWC\", \"nom\": \"\" }", b, e));
        say("carnet : nom de 41 caracteres refuse", !CnParseBook("{ \"v\": 1, \"jeu\": \"MWC\", \"nom\": \"" + std::string(41, 'n') + "\" }", b, e));
        say("carnet : JSON coupe refuse", !CnParseBook("{ \"v\": 1, \"jeu\": \"MWC\", \"nom\": \"x\", \"speciales\": { \"SS1\": { \"notes\": [", b, e));
        say("carnet : texte apres le JSON refuse", !CnParseBook("{ \"v\": 1, \"jeu\": \"MWC\", \"nom\": \"x\" } rm -rf", b, e));
        say("carnet : speciale au nom invalide ignoree", CnParseBook("{ \"v\": 1, \"jeu\": \"MWC\", \"nom\": \"x\", \"speciales\": { \"..\\\\..\": { \"notes\": [] }, \"SS9\": {} } }", b, e) && b.st.size() == 1 && b.st.count(L"SS9")); }
    // ecriture et relecture
    {   CnBook b; b.game = game; b.name = L"Aller \"retour\" \\ é"; b.file = L"x.json";
        CnNote a; a.m = 10; a.type = CT_VIRAGE; a.side = -1; a.grade = 0; a.mods = (1u << CM_LONG) | (1u << CM_ENCHAINE);
        CnNote t; t.m = 5; t.type = CT_TEXTE; t.text = L"vache « là »";
        b.st[L"SS1"] = { t, a }; b.st[L"SS2"] = {};
        CnBook r; std::wstring e;
        bool rt = CnParseBook(CnBookJson(b), r, e);
        say("ecriture puis relecture identiques (nom, texte, H, mods, speciale vide)", rt && r.name == b.name && r.st.size() == 2 && r.st[L"SS2"].empty() && r.st[L"SS1"].size() == 2
            && r.st[L"SS1"][0].text == t.text && r.st[L"SS1"][1].grade == 0 && r.st[L"SS1"][1].side == -1 && r.st[L"SS1"][1].mods == a.mods);
        std::string j = CnBookJson(b);
        say("ecriture sans BOM, sans cote ni sens pour le texte", j.compare(0, 3, "\xEF\xBB\xBF") && j.find("\"m\": 5, \"type\": \"texte\", \"texte\"") != std::string::npos); }
    // v1 / v2 : regle de [MWC], stable
    say("v2 : meme choix pour la meme note, cases independantes", CnV2(312, 0) == CnV2(312, 0) && (CnV2(312, 0) != CnV2(312, 1) || CnV2(540, 0) != CnV2(540, 1) || CnV2(610, 0) != CnV2(610, 2)));
    // premiere modification : un carnet cree, la speciale y recoit d'abord les notes auto
    g_cnSt = 0; g_cnBookFile.clear();
    for (auto &f : { L"mes-notes.json", L"my-notes.json" }) DeleteFileW((CnLibDir() + f).c_str());
    DeleteFileW(CnActivePath().c_str());
    CnLoad(true);
    size_t nAuto = g_cnStages[0].autoN.size();
    std::vector<CnNote> *v = CnEditable();
    say("premiere modification : carnet cree et actif", v && CnBookCur() && g_cnActive == CnBookCur()->file && CnReadActive() == g_cnActive);
    say("premiere modification : notes auto copiees d'abord", v && v->size() == nAuto);
    if (v) { CnNote n = CnGuess(g_cnStages[0], 600); v->push_back(n); CnCommit(); }
    CnLoad(true);
    say("relu : la note ajoutee est dans le fichier", CnBookCur() && CnBookCur()->st[L"SS1"].size() == nAuto + 1);
    say("SS2 absente du carnet : notes auto", CnBookCur() && !CnBookCur()->st.count(L"SS2"));
    {   // note devinee : un virage a droite de 60 m de rayon (SS1 : 2e virage, 40 deg sur 60 m), un carre (30 m, 90 deg)
        CnNote g1 = CnGuess(g_cnStages[0], (int)(120 / 2) + 2), g2 = CnGuess(g_cnStages[0], (int)((120 + 60 + 80 + 45 + 150) / 2) + 1);
        say("note devinee : virage a droite au 1er virage", g1.type == CT_VIRAGE && g1.side > 0);
        say("note devinee : carre au virage de 90 deg sur 30 m", g2.type == CT_CARRE && g2.side > 0);
        // regle du carre ([MWC] / [MSC] 07/11) sur des traces faits ici : carre etale par le lissage, grand virage, epingle
        auto mk = [](std::initializer_list<std::pair<float, float>> sg) { CnStage c; c.step = 2; float x = 0, z = 0, hd = 0; c.p.push_back(PointF(0, 0));
            for (auto &q : sg) { int k = max(1, (int)(q.first / 2)); for (int j = 0; j < k; j++) { hd += q.second / 57.29578f / k; x += sinf(hd) * 2; z += cosf(hd) * 2; c.p.push_back(PointF(x, z)); } }
            c.len = (int)((c.p.size() - 1) * 2); return c; };
        CnStage sq = mk({ { 40.0f, 0.0f }, { 12.0f, -10.0f }, { 10.0f, -70.0f }, { 12.0f, -10.0f }, { 80.0f, 0.0f } }), big = mk({ { 40.0f, 0.0f }, { 60.0f, 90.0f }, { 80.0f, 0.0f } }), hp = mk({ { 40.0f, 0.0f }, { 40.0f, 170.0f }, { 80.0f, 0.0f } });
        CnNote q1 = CnGuess(sq, 20), q2 = CnGuess(big, 20), q3 = CnGuess(hp, 20);
        say("regle du carre : 90 deg etale sur 34 m (coeur serre) = carre a gauche", q1.type == CT_CARRE && q1.side < 0);
        say("regle du carre : 90 deg sur 60 m = virage, pas carre", q2.type == CT_VIRAGE && q2.side > 0);
        say("regle du carre : 170 deg = epingle (H)", q3.type == CT_VIRAGE && q3.grade == 0);
        // suggestion au clic : la note automatique du mod a moins de 25 m, sinon l'estimation
        CnStage sg = g_cnStages[0];
        CnNote au; au.type = CT_CARRE; au.side = -1; au.m = 400; au.mods = 1u << CM_NC;
        sg.autoN = { au };
        CnNote s1 = CnSuggest(sg, 210), s2 = CnSuggest(sg, 300);
        say("suggestion : note automatique a 20 m reprise (type, sens, mods), a l'endroit du clic", s1.type == CT_CARRE && s1.side < 0 && s1.mods == (1u << CM_NC) && s1.m == 420 && s1.uid != au.uid);
        say("suggestion : rien a moins de 25 m, estimation du lanceur", CnGuess(sg, 300).type == s2.type && s2.m == 600); }
    // carnet actif : un fichier hors format n'est jamais pris
    CnWriteAtomic(CnActivePath(), "..\\..\\x.json");
    say("carnet actif : chemin refuse", CnReadActive().empty());
    CnSetActive(CnBookCur() ? CnBookCur()->file : L"");
    // import
    {   std::wstring e, f = CnRoot() + L"import-essai.json";
        CnWriteAtomic(f, "\xEF\xBB\xBF{ \"v\": 1, \"jeu\": \"" + Narrow(game) + "\", \"nom\": \"Ami\", \"speciales\": { \"SS1\": { \"notes\": [ { \"m\": 30, \"type\": \"danger\" } ] } }, \"plus\": 1 }");
        size_t nb = g_cnBooks.size();
        say("import : carnet valide (BOM) accepte, reecrit dans la bibliotheque", CnImportFile(f, e) && g_cnBooks.size() == nb + 1 && GetFileAttributesW((CnLibDir() + L"ami.json").c_str()) != INVALID_FILE_ATTRIBUTES);
        std::string back; CnReadFile(CnLibDir() + L"ami.json", kCnMaxBytes, back);
        say("import : la cle inconnue n'est pas recopiee", back.find("plus") == std::string::npos);
        CnWriteAtomic(f, "{ \"v\": 1, \"jeu\": \"" + Narrow(other) + "\", \"nom\": \"Autre\" }");
        say("import : carnet de l'autre jeu refuse", !CnImportFile(f, e));
        CnWriteAtomic(f, "{ \"v\": 1, \"jeu\": \"" + Narrow(game) + "\", \"nom\": \"Faux\", \"speciales\": { \"SS1\": { \"notes\": [ { \"m\": 1, \"type\": \"virage\" } ] } } }");
        say("import : carnet hors format refuse, rien ecrit", !CnImportFile(f, e) && GetFileAttributesW((CnLibDir() + L"faux.json").c_str()) == INVALID_FILE_ATTRIBUTES);
        std::string big(300 * 1024, ' '); CnWriteAtomic(f, big);
        say("import : fichier de plus de 256 Ko refuse", !CnImportFile(f, e));
        DeleteFileW(f.c_str()); }
    // jeu ouvert : rien n'est ecrit
    {   g_cnTestGame = 1; CnLoad(true);
        unsigned long long s0 = CnDirSig();
        std::vector<CnNote> *w = CnEditable();
        CarnetClick(UI_CN_NEW);
        say("jeu ouvert : aucune modification, aucun fichier ecrit", !w && CnDirSig() == s0);
        g_cnTestGame = 0; CnLoad(true); }
    // fichiers illisibles de la bibliotheque : comptes, pas montres
    CnWriteAtomic(CnLibDir() + L"casse.json", "{ pas du json");
    CnLoad(true);
    say("bibliotheque : fichier illisible compte a part", g_cnBad == 1);
    DeleteFileW((CnLibDir() + L"casse.json").c_str());
    // pour les captures : le premier carnet choisi et actif, quelques notes de joueur sur SS1
    CnLoad(true);
    for (auto &b : g_cnBooks) if (b.file != L"ami.json") { g_cnBookFile = b.file; CnSetActive(b.file); }
    if (std::vector<CnNote> *e = CnEditable()) {
        CnNote t; t.m = 410; t.type = CT_TEXTE; t.text = T(L"attention vache", L"watch the cow"); t.uid = g_cnUid++;
        CnNote d; d.m = 905; d.type = CT_DANGER; d.uid = g_cnUid++;
        e->push_back(t); e->push_back(d);
        for (auto &n : *e) if (n.type == CT_VIRAGE && n.m > 1000 && n.m < 1600) { n.mods |= (1u << CM_NC) | (1u << CM_RESSERRE); break; }
        CnCommit();
    }
    g_cnTestGame = -1;
    if (lf) { fprintf(lf, "%s : %d/%d\r\n", bad ? "ECHECS" : "TOUT OK", total - bad, total); fclose(lf); }
    return bad == 0;
}
