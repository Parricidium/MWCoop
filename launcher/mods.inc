// ---------------------------------------------------------------- onglet MODS : MSCLoader (EXPERIMENTAL) - inclus par launcher.cpp
// MSCLoader (piotrulos/MSCModLoader, GPL v3 : rien de son code ici, on lit et ecrit seulement ses fichiers) charge les
// mods de la communaute par UnityDoorstop (winhttp.dll + doorstop_config.ini a cote du jeu, MSCLoader.Preloader.dll et
// MSCLoader.dll dans mywintercar_Data\Managed). Notre chargeur s'accroche derriere lui (loader/proxy.cpp).
//  - Installe ? Les trois fichiers de l'installateur officiel. Dossier des mods : [MSCLoader] mods= de
//    doorstop_config.ini (GF : <jeu>\Mods, MD : Documents\My Winter Car\Mods, AD : LocalLow\Amistech\My Winter Car\Mods).
//  - Interrupteur ([Lanceur] MSCLoader=1|0) : coupe, le jeu part avec -mscloader-disable (son prechargeur s'arrete
//    tout de suite) ; rien n'est desinstalle.
//  - Installer : derniere version du GitHub officiel (MSCLoader.zip), comme son installateur (qui refuse un dossier
//    ou est deja notre version.dll) : core64.pack a la racine, main_ref.pack et Managed.zip (main_mwc.pack) dans
//    Managed, Mods.zip dans le dossier des mods ; target_assembly de doorstop_config.ini vers mywintercar_Data.
//  - Liste : les DLL du dossier des mods, avec nom, version, auteur et options releves en jeu par le mod (MscMods.cs :
//    MWCoop\cache\mscloader.txt) ; un mod jamais lance ne montre que son fichier.
//  - Engrenage : page des options du mod. Valeurs dans <mods>\Config\Mod Settings\<ID>\settings.json de MSCLoader
//    ({"isDisabled": .., "settings": [{"ID": .., "Value": ..}]}), relu et reecrit ici ; pris au prochain lancement.
//    Cases, curseurs (fleches), listes (fleches), mod active ou non ; textes et couleurs : en jeu seulement.
struct MscSet { std::string type, id; std::wstring name; std::string val, def, mn, mx, dec; std::vector<std::wstring> items; };
struct MscMod { std::wstring file, id, name, ver, author, desc; bool disabled = false, known = false; std::vector<MscSet> sets; };
static std::vector<MscMod> g_msc;
static bool g_mscInstalled;   // (g_mscOn : launcher.cpp)
static std::wstring g_mscVer, g_mscDir, g_mscDirKind;
static int g_mscPage = -1, g_mscHot = -1;
static DWORD g_mscScanT;
static std::vector<std::pair<std::string, std::string>> g_mscVals;   // page ouverte : (ID, valeur JSON brute), ordre du fichier
static bool g_mscPageDisabled;
static std::atomic<int> g_mscInstall(0);   // 0 rien, 1 en cours, 2 fini, 3 echec
static std::string g_mscInstallWhy;
static bool g_mscFake;                      // captures : liste factice, pas de releve
static bool ModShared(const std::wstring &file);   // (modsync.inc : envoye aux invites)
static void ModSetShared(const std::wstring &file, bool on);

static std::wstring MscManaged() { return g_gameDir + L"mywintercar_Data\\Managed\\"; }
static std::wstring MscIni() { return g_gameDir + L"doorstop_config.ini"; }

static std::wstring MscModsDir()
{
    wchar_t k[16] = L"GF";
    GetPrivateProfileStringW(L"MSCLoader", L"mods", L"GF", k, 16, MscIni().c_str());
    g_mscDirKind = k;
    if (!_wcsicmp(k, L"MD")) {
        wchar_t doc[MAX_PATH] = L"";
        if (SUCCEEDED(SHGetFolderPathW(NULL, CSIDL_PERSONAL, NULL, SHGFP_TYPE_CURRENT, doc))) return WithSlash(doc) + L"My Winter Car\\Mods\\";
    }
    if (!_wcsicmp(k, L"AD")) {
        wchar_t up[MAX_PATH] = L"";
        GetEnvironmentVariableW(L"USERPROFILE", up, MAX_PATH);
        if (up[0]) return WithSlash(up) + L"AppData\\LocalLow\\Amistech\\My Winter Car\\Mods\\";
    }
    g_mscDirKind = L"GF";
    return g_gameDir + L"Mods\\";
}

static std::vector<std::wstring> SplitTab(const std::wstring &l)
{
    std::vector<std::wstring> v;
    size_t a = 0;
    for (;;) {
        size_t b = l.find(L'\t', a);
        v.push_back(l.substr(a, b == std::wstring::npos ? std::wstring::npos : b - a));
        if (b == std::wstring::npos) break;
        a = b + 1;
    }
    return v;
}

static void MscScan()
{
    g_mscScanT = GetTickCount();
    if (g_mscFake || g_gameDir.empty()) return;
    g_mscInstalled = FileExists(MscManaged() + L"MSCLoader.Preloader.dll") && FileExists(g_gameDir + L"winhttp.dll") && FileExists(MscIni());
    g_mscDir = g_mscInstalled ? MscModsDir() : L"";
    std::vector<MscMod> cache;
    std::wstring loaderVer;
    std::vector<unsigned char> d;
    if (ReadAll(g_gameDir + L"MWCoop\\cache\\mscloader.txt", d)) {
        std::wstring all = Widen(std::string(d.begin(), d.end()), CP_UTF8);
        size_t a = 0;
        while (a < all.size()) {
            size_t b = all.find(L'\n', a);
            std::wstring l = all.substr(a, b == std::wstring::npos ? std::wstring::npos : b - a);
            a = b == std::wstring::npos ? all.size() : b + 1;
            std::vector<std::wstring> f = SplitTab(l);
            if (f[0] == L"loader" && f.size() > 1) loaderVer = f[1];
            else if (f[0] == L"mod" && f.size() >= 8) {
                MscMod m;
                m.file = f[1]; m.id = f[2]; m.name = f[3]; m.ver = f[4]; m.author = f[5]; m.disabled = f[6] == L"1"; m.desc = f[7]; m.known = true;
                cache.push_back(m);
            } else if (f[0] == L"set" && f.size() >= 11 && !cache.empty()) {
                MscSet s;
                s.type = Narrow(f[1]); s.id = Narrow(f[2], CP_UTF8); s.name = f[3];
                s.val = Narrow(f[4], CP_UTF8); s.def = Narrow(f[5], CP_UTF8); s.mn = Narrow(f[6]); s.mx = Narrow(f[7]); s.dec = Narrow(f[8]);
                if (!f[9].empty()) { size_t p = 0; for (;;) { size_t q = f[9].find(L'|', p); s.items.push_back(f[9].substr(p, q == std::wstring::npos ? std::wstring::npos : q - p)); if (q == std::wstring::npos) break; p = q + 1; } }
                cache.back().sets.push_back(s);
            }
        }
    }
    // Version : celle relevee en jeu (1.4.2.410 -> 1.4.2), sinon inconnue.
    g_mscVer.clear();
    if (!loaderVer.empty()) { g_mscVer = loaderVer; size_t p = g_mscVer.rfind(L'.'); if (std::count(g_mscVer.begin(), g_mscVer.end(), L'.') >= 3 && p != std::wstring::npos) g_mscVer.resize(p); }
    std::vector<MscMod> list;
    if (!g_mscDir.empty()) {
        WIN32_FIND_DATAW fd;
        HANDLE h = FindFirstFileW((g_mscDir + L"*.dll").c_str(), &fd);
        if (h != INVALID_HANDLE_VALUE) {
            do {
                if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) continue;
                MscMod m;
                for (auto &c : cache) if (!_wcsicmp(c.file.c_str(), fd.cFileName)) { m = c; break; }
                if (!m.known) { m.file = fd.cFileName; m.name = m.file.substr(0, m.file.size() - 4); }
                list.push_back(m);
            } while (FindNextFileW(h, &fd));
            FindClose(h);
        }
    }
    std::sort(list.begin(), list.end(), [](const MscMod &a, const MscMod &b) { return _wcsicmp(a.name.c_str(), b.name.c_str()) < 0; });
    // (page ouverte : le meme mod, retrouve par son fichier)
    std::wstring open = g_mscPage >= 0 && g_mscPage < (int)g_msc.size() ? g_msc[g_mscPage].file : L"";
    g_msc = list;
    if (!open.empty()) { g_mscPage = -1; for (int i = 0; i < (int)g_msc.size(); i++) if (g_msc[i].file == open) g_mscPage = i; }
}

// ---- settings.json de MSCLoader (format fixe : isDisabled, puis la liste des valeurs)
static std::wstring MscSettingsPath(const MscMod &m) { return g_mscDir + L"Config\\Mod Settings\\" + m.id + L"\\settings.json"; }

static size_t JsonValueEnd(const std::string &j, size_t p)   // fin d'une valeur simple (nombre, booleen, chaine)
{
    if (p < j.size() && j[p] == '"') { for (size_t i = p + 1; i < j.size(); i++) { if (j[i] == '\\') { i++; continue; } if (j[i] == '"') return i + 1; } return j.size(); }
    size_t e = j.find_first_of(",}\r\n]", p);
    return e == std::string::npos ? j.size() : e;
}

static void MscLoadPage()
{
    g_mscVals.clear();
    g_mscPageDisabled = false;
    if (g_mscPage < 0 || g_mscPage >= (int)g_msc.size()) return;
    const MscMod &m = g_msc[g_mscPage];
    g_mscPageDisabled = m.disabled;
    std::vector<unsigned char> d;
    if (ReadAll(MscSettingsPath(m), d)) {
        std::string j(d.begin(), d.end());
        size_t p = j.find("\"isDisabled\"");
        if (p != std::string::npos) { size_t v = j.find_first_not_of(" \t:", p + 12); g_mscPageDisabled = v != std::string::npos && !j.compare(v, 4, "true"); }
        for (size_t q = j.find("\"ID\""); q != std::string::npos; q = j.find("\"ID\"", q + 4)) {
            size_t s = j.find('"', j.find(':', q) + 1);
            if (s == std::string::npos) break;
            std::string id = JsonDecode(j, s);
            size_t vk = j.find("\"Value\"", s);
            if (vk == std::string::npos) break;
            size_t v = j.find_first_not_of(" \t\r\n:", vk + 7);
            g_mscVals.push_back({ id, j.substr(v, JsonValueEnd(j, v) - v) });
        }
    }
    // Options sans valeur enregistree : leur valeur relevee en jeu.
    for (const MscSet &s : m.sets) {
        bool saved = s.type == "CheckBox" || s.type == "CheckBoxGroup" || s.type == "Slider" || s.type == "SliderInt" || s.type == "TextBox" || s.type == "TextArea" || s.type == "DropDown" || s.type == "ColorPicker";
        if (!saved || s.id.empty()) continue;
        bool have = false;
        for (auto &v : g_mscVals) have |= v.first == s.id;
        if (have) continue;
        std::string raw = s.val;
        if (s.type == "CheckBox" || s.type == "CheckBoxGroup") raw = s.val == "1" ? "true" : "false";
        else if (s.type == "TextBox" || s.type == "TextArea" || s.type == "ColorPicker") raw = "\"" + s.val + "\"";
        g_mscVals.push_back({ s.id, raw.empty() ? "0" : raw });
    }
}

static std::string JsonEsc(const std::string &s)
{
    std::string o = "\"";
    for (char c : s) { if (c == '"' || c == '\\') o += '\\'; o += c; }
    return o + "\"";
}

static bool MscSavePage()
{
    if (g_mscPage < 0 || g_mscPage >= (int)g_msc.size()) return false;
    const MscMod &m = g_msc[g_mscPage];
    std::string t = "{\r\n  \"isDisabled\": " + std::string(g_mscPageDisabled ? "true" : "false") + ",\r\n  \"settings\": [";
    for (size_t i = 0; i < g_mscVals.size(); i++)
        t += std::string(i ? "," : "") + "\r\n    {\r\n      \"ID\": " + JsonEsc(g_mscVals[i].first) + ",\r\n      \"Value\": " + g_mscVals[i].second + "\r\n    }";
    t += g_mscVals.empty() ? "]\r\n}" : "\r\n  ]\r\n}";
    std::wstring path = MscSettingsPath(m);
    SHCreateDirectoryExW(NULL, path.substr(0, path.find_last_of(L'\\')).c_str(), NULL);
    HANDLE f = CreateFileW(path.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (f == INVALID_HANDLE_VALUE) { SetStatus(K_ERR, T(L"Réglages du mod non enregistrés (erreur %lu)", L"Mod settings not saved (error %lu)"), GetLastError()); return false; }
    DWORD w = 0;
    WriteFile(f, t.data(), (DWORD)t.size(), &w, NULL);
    CloseHandle(f);
    g_msc[g_mscPage].disabled = g_mscPageDisabled;
    TestLog("mods : reglages de %s enregistres", Narrow(m.id, CP_UTF8).c_str());
    return true;
}

static std::string *MscVal(const std::string &id) { for (auto &v : g_mscVals) if (v.first == id) return &v.second; return NULL; }
static std::string Num(double v, int dec)
{
    char b[48];
    if (dec <= 0) sprintf_s(b, "%.0f", v);
    else sprintf_s(b, "%.*f", dec, v);
    return b;
}

// Pas d'une fleche : 1 (entier), sinon un vingtieme de la plage arrondi aux decimales.
static void MscStep(const MscSet &s, int dir)
{
    std::string *v = MscVal(s.id);
    if (!v) return;
    if (s.type == "CheckBox" || s.type == "CheckBoxGroup") { *v = *v == "true" ? "false" : "true"; return; }
    double cur = atof(v->c_str()), mn = atof(s.mn.c_str()), mx = atof(s.mx.c_str());
    if (s.type == "DropDown") {
        int n = (int)s.items.size();
        if (n > 0) *v = std::to_string(((int)cur + dir + n) % n);
        return;
    }
    if (mx <= mn) return;
    if (s.type == "SliderInt") { cur = min(mx, max(mn, floor(cur + 0.5) + dir)); *v = Num(cur, 0); return; }
    int dec = atoi(s.dec.c_str());
    double step = (mx - mn) / 20.0, q = pow(10.0, -dec);
    step = max(q, floor(step / q + 0.5) * q);
    cur = min(mx, max(mn, cur + dir * step));
    *v = Num(cur, max(dec, 1));
}

// ---- installation (fil a part)
static std::wstring MscTmp() { return LocalDir() + L"mscloader\\"; }
static DWORD WINAPI MscInstallThread(void *)
{
    auto fail = [](const std::string &w) { g_mscInstallWhy = w; TestLog("mods : installation de MSCLoader impossible : %s", w.c_str()); g_mscInstall = 3; return (DWORD)0; };
    std::string json;
    DWORD code = 0;
    if (!HttpGet(L"https://api.github.com/repos/piotrulos/MSCModLoader/releases/latest", &json, L"", false, &code)) return fail("GitHub injoignable (HTTP " + std::to_string(code) + ")");
    size_t z = json.find("/MSCLoader.zip\"");
    size_t u = z == std::string::npos ? z : json.rfind("\"browser_download_url\"", z);
    if (u == std::string::npos) return fail("MSCLoader.zip absent de la derniere version");
    std::string url = JsonDecode(json, json.find('"', json.find(':', u) + 1));
    std::string tag = JsonField(json, "tag_name", 0, json.size());
    std::wstring tmp = MscTmp();
    SHCreateDirectoryExW(NULL, (tmp + L"pkg").c_str(), NULL);
    std::wstring zip = tmp + L"MSCLoader.zip";
    if (!HttpGet(Widen(url), NULL, zip, true, &code)) return fail("telechargement (HTTP " + std::to_string(code) + ")");
    std::string why;
    if (!Unzip(zip, tmp + L"pkg", &why)) return fail("zip : " + why);
    std::wstring pkg = tmp + L"pkg\\";
    if (!FileExists(pkg + L"core64.pack") || !FileExists(pkg + L"main_ref.pack") || !FileExists(pkg + L"main_mwc.pack")) return fail("paquet incomplet (core64 / main_ref / main_mwc)");
    // Reglages deja choisis (reinstallation) : dossier des mods, intro, ecran de configuration.
    wchar_t mods[16] = L"GF", intro[16] = L"false", cfg[16] = L"false";
    std::wstring ini = MscIni();
    bool had = FileExists(ini);
    if (had) {
        GetPrivateProfileStringW(L"MSCLoader", L"mods", L"GF", mods, 16, ini.c_str());
        GetPrivateProfileStringW(L"MSCLoader", L"skipIntro", L"false", intro, 16, ini.c_str());
        GetPrivateProfileStringW(L"MSCLoader", L"skipConfigScreen", L"false", cfg, 16, ini.c_str());
    }
    std::wstring root = g_gameDir.substr(0, g_gameDir.size() - 1), managed = MscManaged();
    managed.pop_back();
    // core64.pack (winhttp.dll, doorstop_config.ini) d'abord a part : une winhttp.dll deja la est chargee par ce
    // lanceur s'il est dans le dossier du jeu (verrouillee) ; la garder si c'est la meme, et ne jamais laisser un
    // doorstop_config.ini a moitie ecrit (celui du paquet vise mysummercar_Data).
    SHCreateDirectoryExW(NULL, (tmp + L"core").c_str(), NULL);
    if (!Unzip(pkg + L"core64.pack", tmp + L"core", &why)) return fail("core64.pack : " + why);
    {
        std::vector<unsigned char> a, b;
        std::wstring wh = g_gameDir + L"winhttp.dll";
        bool same = ReadAll(wh, a) && ReadAll(tmp + L"core\\winhttp.dll", b) && a == b;
        if (!same && !CopyFileW((tmp + L"core\\winhttp.dll").c_str(), wh.c_str(), FALSE)) return fail("winhttp.dll : en cours d'utilisation (erreur " + std::to_string(GetLastError()) + ")");
        if (!had && !CopyFileW((tmp + L"core\\doorstop_config.ini").c_str(), ini.c_str(), FALSE)) return fail("doorstop_config.ini : ecriture impossible");
    }
    if (!Unzip(pkg + L"main_ref.pack", managed, &why)) return fail("bibliotheques : " + why);
    if (!Unzip(pkg + L"main_mwc.pack", tmp + L"mwc", &why)) return fail("main_mwc : " + why);
    if (!Unzip(tmp + L"mwc\\Managed.zip", managed, &why)) return fail("MSCLoader.dll : " + why);
    WritePrivateProfileStringW(L"General", L"target_assembly", L"mywintercar_Data\\Managed\\MSCLoader.Preloader.dll", ini.c_str());
    WritePrivateProfileStringW(L"MSCLoader", L"mods", mods, ini.c_str());
    WritePrivateProfileStringW(L"MSCLoader", L"skipIntro", intro, ini.c_str());
    WritePrivateProfileStringW(L"MSCLoader", L"skipConfigScreen", cfg, ini.c_str());
    std::wstring md = MscModsDir();
    md.pop_back();
    if (!Unzip(tmp + L"mwc\\Mods.zip", md, &why)) return fail("dossier Mods : " + why);
    CreateDirectoryW((md + L"\\References").c_str(), NULL);
    CreateDirectoryW((md + L"\\Config").c_str(), NULL);
    TestLog("mods : MSCLoader %s installe (%s)", tag.c_str(), Narrow(md, CP_UTF8).c_str());
    g_mscInstallWhy = tag;
    g_mscInstall = 2;
    return 0;
}

static void MscInstall()
{
    if (g_mscInstall == 1 || g_gameDir.empty()) return;
    if (GameProcessRunning()) { SetStatus(K_ERR, T(L"Ferme My Winter Car avant d'installer MSCLoader", L"Close My Winter Car before installing MSCLoader")); return; }
    if (g_testSalon.empty() && MessageBoxW(g_wnd, T(L"Télécharger la dernière version officielle de MSCLoader (GitHub de piotrulos, environ 4 Mo) et l'installer dans le dossier du jeu ?\n\n"
                                                      L"MSCLoader est un projet indépendant de MWCoop. Son support dans MWCoop est EXPÉRIMENTAL.",
                                                      L"Download the latest official MSCLoader (piotrulos's GitHub, about 4 MB) and install it in the game folder?\n\n"
                                                      L"MSCLoader is a project independent from MWCoop. Its support in MWCoop is EXPERIMENTAL."),
                                         L"MWCoop", MB_YESNO | MB_ICONQUESTION) != IDYES) return;
    g_mscInstall = 1;
    g_progress = 0;
    SetStatus(K_NORMAL, T(L"Installation de MSCLoader…", L"Installing MSCLoader…"));
    HANDLE t = CreateThread(NULL, 0, MscInstallThread, NULL, 0, NULL);
    if (t) CloseHandle(t);
}

static void MscTick()
{
    int st = g_mscInstall;
    if (st == 2 || st == 3) {
        g_mscInstall = 0;
        g_progress = -1;
        if (st == 2) SetStatus(K_OK, T(L"MSCLoader %s installé : place tes mods dans son dossier Mods", L"MSCLoader %s installed: put your mods in its Mods folder"), Widen(g_mscInstallWhy).c_str());
        else SetStatus(K_ERR, T(L"MSCLoader non installé : %s", L"MSCLoader not installed: %s"), Widen(g_mscInstallWhy, CP_UTF8).c_str());
        MscScan();
    }
    if ((g_tab == TAB_MODS || g_tab == TAB_CONTENT) && GetTickCount() - g_mscScanT > 3000) MscScan();
}

// ---- dessin (page MODS, dans kMain)
static int MscModCount() { return g_mscInstalled ? (int)g_msc.size() : 0; }
static const RectF kMscToggle(690, 188, 50, 28), kMscAuto(1172, 188, 50, 28), kMscInstall(300, 179, 300, 46), kMscList(288, 282, 952, 390),
                   kMscBack(1000, 108, 240, 30), kMscOpenDir(288, 694, 260, 30);
static const float kMscRowH = 76, kMscSetH = 54;

static void DrawToggle(Graphics &g, RectF r, bool on, bool hot) { UiToggle(g, r, on, hot); }
static void DrawGear(Graphics &g, float cx, float cy, Color c) { Icon(g, IC_GEAR, cx, cy, 20, c, 1.9f); }
static void DrawExperimental(Graphics &g, float x, float y)
{
    const wchar_t *s = T(L"EXP\u00C9RIMENTAL", L"EXPERIMENTAL");
    float w = PillW(s, 10.5f);
    Pill(g, RectF(x, y, w, 22), WithA(kWarn, 0.22f), s, kWarn, 10.5f);
}

static float MscMaxScroll()
{
    int rows = g_mscPage >= 0 && g_mscPage < (int)g_msc.size() ? (int)g_msc[g_mscPage].sets.size() + 1 : (int)g_msc.size();
    float h = g_mscPage >= 0 ? kMscSetH : kMscRowH;
    return max(0.0f, rows * h - kMscList.Height);
}
static RectF MscRowRect(int i) { float ms = MscMaxScroll(); return RectF(kMscList.X, kMscList.Y + i * kMscRowH - g_scroll[TAB_MODS], kMscList.Width - (ms > 0 ? 12 : 0), kMscRowH - 10); }
static RectF MscGearRect(const RectF &r) { return RectF(r.X + r.Width - 60, r.Y + (r.Height - 44) / 2, 44, 44); }
static RectF MscSendRect(const RectF &r) { return RectF(r.X + r.Width - 60 - 16 - 50, r.Y + (r.Height - 28) / 2, 50, 28); }

// Element sous la souris : 1000 interrupteur, 1001 installer, 1002 retour, 1003 mod active (page), 1004 dossier,
// 1005 accepter les mods de l'hote, 2000+i ouvrir le mod i, 3000+i*4+k option i (k : 0 case, 1 moins, 2 plus), 4000+i envoyer.
static int MscAt(float x, float y)
{
    if (g_state != ST_IDLE) return -1;
    bool page = g_mscPage >= 0 && g_mscPage < (int)g_msc.size();
    if (page) {
        if (kMscBack.Contains(x, y)) return 1002;
        if (!kMscList.Contains(x, y)) return -1;
        int i = (int)((y - kMscList.Y + g_scroll[TAB_MODS]) / kMscSetH);
        float right = kMscList.X + kMscList.Width - 16;
        if (i == 0) return x > right - 80 ? 1003 : -1;
        const auto &sets = g_msc[g_mscPage].sets;
        if (--i < 0 || i >= (int)sets.size()) return -1;
        const MscSet &s = sets[i];
        if (s.type == "CheckBox" || s.type == "CheckBoxGroup") return x > right - 64 ? 3000 + i * 4 : -1;
        if (s.type == "Slider" || s.type == "SliderInt" || s.type == "DropDown") {
            if (x > right - 40) return 3000 + i * 4 + 2;
            if (x > right - 220 && x < right - 180) return 3000 + i * 4 + 1;
        }
        return -1;
    }
    if (g_mscInstalled && RectF(kMscToggle.X - 8, kMscToggle.Y - 8, kMscToggle.Width + 16, kMscToggle.Height + 16).Contains(x, y)) return 1000;
    if (RectF(kMscAuto.X - 8, kMscAuto.Y - 8, kMscAuto.Width + 16, kMscAuto.Height + 16).Contains(x, y)) return 1005;
    if (!g_mscInstalled && kMscInstall.Contains(x, y)) return 1001;
    if (g_mscInstalled && kMscOpenDir.Contains(x, y)) return 1004;
    if (!kMscList.Contains(x, y)) return -1;
    int i = (int)((y - kMscList.Y + g_scroll[TAB_MODS]) / kMscRowH);
    if (i < 0 || i >= (int)g_msc.size()) return -1;
    RectF r = MscRowRect(i), sr = MscSendRect(r);
    if (x > sr.X - 80 && x < sr.X + sr.Width + 8) return 4000 + i;   // Envoyer aux invites
    return g_msc[i].known ? 2000 + i : -1;
}

static bool MscClick(float x, float y)
{
    int h = MscAt(x, y);
    if (h == 1000) {
        g_mscOn = !g_mscOn;
        WritePrivateProfileStringW(L"Lanceur", L"MSCLoader", g_mscOn ? L"1" : L"0", g_iniLauncher.c_str());
        SetStatus(K_NORMAL, g_mscOn ? T(L"MSCLoader sera charg\u00E9 au lancement", L"MSCLoader will load at launch") : T(L"MSCLoader ne sera pas charg\u00E9 (rien n'est d\u00E9sinstall\u00E9)", L"MSCLoader will not load (nothing is uninstalled)"));
    } else if (h == 1005) { if (const Opt *o = OptByKey("ModsAuto")) OptSet(*o, OptGet(*o) ? 0 : 1); }
    else if (h == 1001) MscInstall();
    else if (h == 1004) ShellExecuteW(g_wnd, L"open", g_mscDir.c_str(), NULL, NULL, SW_SHOWNORMAL);
    else if (h == 1002) { g_mscPage = -1; g_scroll[TAB_MODS] = 0; }
    else if (h >= 4000) {
        const MscMod &m = g_msc[h - 4000];
        bool on = !ModShared(m.file);
        ModSetShared(m.file, on);
        SetStatus(K_NORMAL, on ? T(L"%s sera envoy\u00E9 aux invit\u00E9s du salon", L"%s will be sent to the lobby's guests") : T(L"%s reste chez toi (pas envoy\u00E9)", L"%s stays with you (not sent)"), m.name.c_str());
    }
    else if (h >= 2000 && h < 3000) { g_mscPage = h - 2000; g_scroll[TAB_MODS] = 0; MscLoadPage(); }
    else if (h == 1003) { g_mscPageDisabled = !g_mscPageDisabled; if (!g_mscFake) MscSavePage(); }
    else if (h >= 3000) {
        int i = (h - 3000) / 4, k = (h - 3000) % 4;
        MscStep(g_msc[g_mscPage].sets[i], k == 1 ? -1 : 1);
        if (!g_mscFake) MscSavePage();
    } else return false;
    return true;
}

static void DrawScrollBar(Graphics &g, RectF area, float sc, float ms)
{
    if (ms <= 0) return;
    float h = area.Height * area.Height / (area.Height + ms), y = area.Y + (area.Height - h) * sc / ms;
    GraphicsPath sp; RoundRect(sp, RectF(area.X + area.Width - 5, y, 4, h), 2);
    SolidBrush sb(WithA(kAcc, 0.5f)); g.FillPath(&sb, &sp);
}

static void DrawModsPage(Graphics &g)
{
    const MscMod &m = g_msc[g_mscPage];
    RectF c = kMain;
    Title(g, m.name, RectF(c.X + 24, c.Y + 20, 600, 28), 18, kInk);
    bool backHot = g_mscHot == 1002;
    Text(g, T(L"\u2190 Retour aux mods", L"\u2190 Back to mods"), kMscBack, 13, backHot ? FontStyleBold | FontStyleUnderline : FontStyleBold, backHot ? kAcc : kGrey, StringAlignmentFar);
    std::wstring sub = L"v" + m.ver + (m.author.empty() ? L"" : L" \u00B7 " + m.author) + L" \u00B7 " + m.file;
    Text(g, sub, RectF(c.X + 24, c.Y + 48, c.Width - 48, 22), 13, FontStyleRegular, kGrey, StringAlignmentNear);
    float sc = g_scroll[TAB_MODS], ms = MscMaxScroll(), right = kMscList.X + kMscList.Width - 16;
    GraphicsState st = g.Save();
    g.SetClip(kMscList, CombineModeIntersect);
    for (int i = -1; i < (int)m.sets.size(); i++) {
        RectF r(kMscList.X, kMscList.Y + (i + 1) * kMscSetH - sc, kMscList.Width - (ms > 0 ? 12 : 0), kMscSetH - 6);
        if (r.Y + r.Height < kMscList.Y || r.Y > kMscList.Y + kMscList.Height) continue;
        if (i < 0) {   // le mod lui-meme : active ou non (isDisabled de MSCLoader)
            GraphicsPath rp; RoundRect(rp, r, 14);
            SolidBrush rb(TH(card)); g.FillPath(&rb, &rp);
            Text(g, T(L"Mod activ\u00E9", L"Mod enabled"), RectF(r.X + 16, r.Y, 300, r.Height), 14, FontStyleBold, kInk, StringAlignmentNear);
            DrawToggle(g, RectF(right - 58, r.Y + (r.Height - 28) / 2, 50, 28), !g_mscPageDisabled, g_mscHot == 1003);
            continue;
        }
        const MscSet &s = m.sets[i];
        bool hdr = s.type == "HeaderGroup" || s.type == "Header";
        if (hdr) { Text(g, s.name, RectF(r.X + 6, r.Y + 8, r.Width - 12, r.Height - 8), 13.5f, FontStyleBold, kAcc, StringAlignmentNear); continue; }
        if (s.type == "Text") { Para(g, s.name, RectF(r.X + 10, r.Y + 6, r.Width - 20, r.Height - 6), 12.5f, kGrey); continue; }
        if (s.type == "Button" || s.type == "RButton" || s.type == "LayoutGroup" || s.type == "LayoutGroupEnd") {
            Text(g, s.name + T(L"  (bouton : en jeu)", L"  (button: in game)"), RectF(r.X + 16, r.Y, r.Width - 32, r.Height), 12.5f, FontStyleRegular, kGrey, StringAlignmentNear);
            continue;
        }
        GraphicsPath rp; RoundRect(rp, r, 14);
        SolidBrush rb(TH(card)); g.FillPath(&rb, &rp);
        Text(g, s.name, RectF(r.X + 16, r.Y, r.Width - 260, r.Height), 13.5f, FontStyleRegular, kInk, StringAlignmentNear);
        std::string *v = MscVal(s.id);
        std::string val = v ? *v : s.val;
        int base = 3000 + i * 4;
        if (s.type == "CheckBox" || s.type == "CheckBoxGroup") { DrawToggle(g, RectF(right - 58, r.Y + (r.Height - 28) / 2, 50, 28), val == "true" || val == "1", g_mscHot == base); continue; }
        std::wstring shown;
        bool arrows = s.type == "Slider" || s.type == "SliderInt" || s.type == "DropDown";
        if (s.type == "DropDown") { int k = atoi(val.c_str()); shown = k >= 0 && k < (int)s.items.size() ? s.items[k] : Widen(val); }
        else if (s.type == "SliderInt") { int k = atoi(val.c_str()) - atoi(s.mn.c_str()); shown = k >= 0 && k < (int)s.items.size() ? s.items[k] : Widen(val); }
        else if (s.type == "Slider") shown = Widen(Num(atof(val.c_str()), max(atoi(s.dec.c_str()), 0)));
        else if (s.type == "ColorPicker") {
            int col[4] = { 0, 0, 0, 255 };
            sscanf_s(val.c_str(), "\"%d,%d,%d,%d", &col[0], &col[1], &col[2], &col[3]);
            RectF sw(right - 90, r.Y + 10, 30, r.Height - 20);
            GraphicsPath sp; RoundRect(sp, sw, 6);
            SolidBrush sb(Color(255, (BYTE)col[0], (BYTE)col[1], (BYTE)col[2])); g.FillPath(&sb, &sp);
            Text(g, T(L"en jeu", L"in game"), RectF(right - 52, r.Y, 52, r.Height), 12, FontStyleRegular, kGrey, StringAlignmentFar);
            continue;
        } else {   // textes
            std::string t = val.size() >= 2 && val[0] == '"' ? JsonDecode(val, 0) : val;
            shown = Widen(t, CP_UTF8) + T(L"  (en jeu)", L"  (in game)");
            Text(g, shown, RectF(right - 240, r.Y, 240, r.Height), 12.5f, FontStyleRegular, kGrey, StringAlignmentFar);
            continue;
        }
        if (arrows) {
            for (int k = 1; k <= 2; k++) {
                RectF ar = k == 1 ? RectF(right - 220, r.Y + 7, 40, r.Height - 14) : RectF(right - 40, r.Y + 7, 40, r.Height - 14);
                GraphicsPath ap; RoundRect(ap, ar, 11);
                bool hot = g_mscHot == base + k;
                SolidBrush ab(hot ? WithA(kAcc, 0.25f) : TH(btn2)); g.FillPath(&ab, &ap);
                Icon(g, k == 1 ? IC_CHEVL : IC_CHEVR, ar.X + 20, ar.Y + ar.Height / 2, 16, hot ? kAcc : kInk, 2.3f);
            }
            Text(g, shown, RectF(right - 180, r.Y, 140, r.Height), 13.5f, FontStyleBold, kInk);
        }
    }
    g.Restore(st);
    DrawScrollBar(g, kMscList, sc, ms);
}

static void DrawMods(Graphics &g)
{
    RectF c = kMain;
    Glass(c);
    if (g_mscPage >= 0 && g_mscPage < (int)g_msc.size()) DrawModsPage(g);
    else {
        Graphics &mg = MeasureG();
        Title(g, L"Mods \u00B7 MSCLoader", RectF(c.X + 24, c.Y + 20, 400, 28), 18, kInk);
        DrawExperimental(g, c.X + 24 + MeasureW(mg, L"Mods \u00B7 MSCLoader", 18, FontStyleBold) + 18, c.Y + 24);
        std::wstring sub;
        if (g_mscInstall == 1) sub = T(L"Installation de MSCLoader\u2026", L"Installing MSCLoader\u2026");
        else if (!g_mscInstalled) sub = T(L"MSCLoader, le chargeur de mods de la communaut\u00E9 (projet ind\u00E9pendant), n'est pas install\u00E9.", L"MSCLoader, the community mod loader (independent project), is not installed.");
        else {
            wchar_t b[200];
            swprintf_s(b, T(L"MSCLoader %s \u00B7 %d mod(s) \u00B7 dossier %s", L"MSCLoader %s \u00B7 %d mod(s) \u00B7 %s folder"), g_mscVer.empty() ? L"" : g_mscVer.c_str(), (int)g_msc.size(),
                       g_mscDirKind == L"MD" ? T(L"Documents", L"Documents") : g_mscDirKind == L"AD" ? L"LocalLow" : T(L"du jeu", L"game"));
            sub = b;
        }
        Text(g, sub, RectF(c.X + 24, c.Y + 48, c.Width - 48, 22), 13, FontStyleRegular, kGrey, StringAlignmentNear);
        // deux tuiles : charger MSCLoader (ou l'installer), accepter les mods de l'hote
        float tw = (c.Width - 48 - 12) / 2;
        RectF t1(c.X + 24, c.Y + 80, tw, 68), t2(c.X + 36 + tw, c.Y + 80, tw, 68);
        for (int k = 0; k < 2; k++) {
            RectF t = k ? t2 : t1;
            GraphicsPath p; RoundRect(p, t, 16);
            SolidBrush b(TH(card)); g.FillPath(&b, &p);
            Pen pen(TH(choiceBorder), 1); g.DrawPath(&pen, &p);
        }
        if (g_mscInstalled) {
            SetRowText(g, RectF(t1.X + 18, t1.Y, t1.Width - 100, t1.Height), T(L"Charger MSCLoader", L"Load MSCLoader"), T(L"Tes mods dans tes parties MWCoop", L"Your mods in your MWCoop games"));
            DrawToggle(g, kMscToggle, g_mscOn, g_mscHot == 1000);
        } else {
            GraphicsPath bp; RoundRect(bp, kMscInstall, 14);
            float a = g_mscInstall == 1 ? 0.5f : 1.0f;
            SolidBrush lg(WithA(kAcc, a)); g.FillPath(&lg, &bp);
            if (g_mscHot == 1001) { SolidBrush hb(Color(55, 255, 255, 255)); g.FillPath(&hb, &bp); }
            Icon(g, IC_DOWNLOAD, kMscInstall.X + 28, kMscInstall.Y + 23, 18, kOnAcc, 2.2f);
            Text(g, g_mscInstall == 1 ? T(L"INSTALLATION\u2026", L"INSTALLING\u2026") : T(L"INSTALLER MSCLOADER", L"INSTALL MSCLOADER"), RectF(kMscInstall.X + 44, kMscInstall.Y, kMscInstall.Width - 52, kMscInstall.Height), 13.5f, FontStyleBold, kOnAcc, StringAlignmentNear);
        }
        if (const Opt *ma = OptByKey("ModsAuto")) {
            SetRowText(g, RectF(t2.X + 18, t2.Y, t2.Width - 100, t2.Height), T(L"Accepter l'envoi de l'h\u00F4te", L"Accept the host's files"), T(L"Plus de TOUT ACCEPTER dans le salon d'un ami", L"No more ACCEPT ALL in a friend's lobby"));
            DrawToggle(g, kMscAuto, OptGet(*ma) != 0, g_mscHot == 1005);
        }
        Title(g, T(L"Tes mods", L"Your mods"), RectF(c.X + 24, c.Y + 160, 300, 26), 16, kInk);
        float sc = g_scroll[TAB_MODS], ms = MscMaxScroll();
        if (g_mscInstalled && g_msc.empty())
            Para(g, T(L"Aucun mod dans le dossier Mods. Ajoute les DLL des mods MSCLoader (version My Winter Car) puis lance le jeu une fois : leurs noms et leurs options appara\u00EEtront ici.",
                      L"No mod in the Mods folder. Add MSCLoader mods (My Winter Car version), then start the game once: their names and options will show up here."),
                 RectF(kMscList.X + 140, kMscList.Y + 80, kMscList.Width - 280, 80), 13.5f, kGrey, StringAlignmentCenter);
        GraphicsState st = g.Save();
        g.SetClip(kMscList, CombineModeIntersect);
        for (int i = 0; i < (int)g_msc.size(); i++) {
            const MscMod &m = g_msc[i];
            RectF r = MscRowRect(i);
            if (r.Y + r.Height < kMscList.Y || r.Y > kMscList.Y + kMscList.Height) continue;
            bool hot = g_mscHot == 2000 + i;
            GraphicsPath rp; RoundRect(rp, r, 16);
            SolidBrush rb(hot ? TH(cardSel) : TH(card)); g.FillPath(&rb, &rp);
            Pen rpen(hot ? WithA(kAcc, 0.6f) : TH(choiceBorder), 1); g.DrawPath(&rpen, &rp);
            float a = m.disabled ? 0.5f : 1.0f;
            Text(g, m.name, RectF(r.X + 18, r.Y + 10, r.Width - 260, 24), 15, FontStyleBold, WithA(kInk, a), StringAlignmentNear);
            std::wstring line = m.known ? L"v" + m.ver + (m.author.empty() ? L"" : L" \u00B7 " + m.author) + (m.disabled ? T(L" \u00B7 d\u00E9sactiv\u00E9", L" \u00B7 disabled") : L"")
                                        : m.file + T(L" \u00B7 lance le jeu une fois pour les d\u00E9tails", L" \u00B7 start the game once for details");
            Text(g, line, RectF(r.X + 18, r.Y + 34, r.Width - 260, 20), 12.5f, FontStyleRegular, kGrey, StringAlignmentNear);
            RectF sr = MscSendRect(r), gr = MscGearRect(r);
            bool sh = ModShared(m.file), shHot = g_mscHot == 4000 + i;   // envoye aux invites du salon
            Text(g, T(L"Envoyer", L"Send"), RectF(sr.X - 84, r.Y, 76, r.Height), 12.5f, FontStyleBold, shHot ? kAcc : sh ? kInk : kGrey, StringAlignmentFar);
            DrawToggle(g, sr, sh, shHot);
            if (m.known) {
                GraphicsPath gp; RoundRect(gp, gr, 12);
                SolidBrush gb(hot ? TH(btn2Hot) : TH(btn2)); g.FillPath(&gb, &gp);
                DrawGear(g, gr.X + 22, gr.Y + 22, hot ? kAcc : kInk);
            }
        }
        g.Restore(st);
        DrawScrollBar(g, kMscList, sc, ms);
    }
    Pen sep(TH(sep), 1);
    g.DrawLine(&sep, c.X + 24, c.Y + c.Height - 62, c.X + c.Width - 24, c.Y + c.Height - 62);
    Para(g, T(L"R\u00E9glages pris au prochain lancement du jeu. \u00AB Envoyer \u00BB : le mod est copi\u00E9 chez les invit\u00E9s de ton salon, dans un dossier \u00E0 part.",
              L"Settings apply at the next game launch. \"Send\": the mod is copied to your lobby's guests, in a separate folder."),
         RectF(c.X + 300, c.Y + c.Height - 52, c.Width - 324, 40), 12.5f, kGrey, StringAlignmentCenter);
    if (g_mscInstalled && g_mscPage < 0) {
        bool hot = g_mscHot == 1004;
        Icon(g, IC_FOLDER, kMscOpenDir.X + 10, kMscOpenDir.Y + 15, 17, hot ? kAcc : kInk, 1.9f);
        Text(g, T(L"Ouvrir le dossier Mods", L"Open the Mods folder"), RectF(kMscOpenDir.X + 26, kMscOpenDir.Y, kMscOpenDir.Width - 26, kMscOpenDir.Height), 13, hot ? FontStyleBold | FontStyleUnderline : FontStyleUnderline, hot ? kAcc : kInk, StringAlignmentNear);
    }
}
