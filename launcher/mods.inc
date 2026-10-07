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
    if (!Unzip(pkg + L"core64.pack", root, &why)) return fail("winhttp.dll / doorstop_config.ini : " + why);
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
                                                      L"MSCLoader est un projet indépendant de MWCoop. Son support dans MWCoop est EXPÉRIMENTAL : les mods ne sont pas encore synchronisés entre joueurs.",
                                                      L"Download the latest official MSCLoader (piotrulos's GitHub, about 4 MB) and install it in the game folder?\n\n"
                                                      L"MSCLoader is a project independent from MWCoop. Its support in MWCoop is EXPERIMENTAL: mods are not synced between players yet."),
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
    if (g_tab == TAB_MODS && GetTickCount() - g_mscScanT > 3000) MscScan();
}

// ---- dessin
static const RectF kMscToggle(856, 176, 52, 24), kMscInstall(712, 172, 220, 32), kMscList(452, 214, 488, 296), kMscBack(756, 124, 180, 26), kMscOpenDir(452, 552, 160, 22);
static const float kMscRowH = 48, kMscSetH = 40;

static void DrawToggle(Graphics &g, RectF r, bool on, bool hot)
{
    GraphicsPath p;
    RoundRect(p, r, r.Height / 2);
    if (on) { LinearGradientBrush lg(r, kAcc, kAcc2, LinearGradientModeHorizontal); g.FillPath(&lg, &p); }
    else { SolidBrush b(TH(field)); g.FillPath(&b, &p); }
    Pen pen(hot ? kAcc : on ? WithA(kAcc, 0.0f) : TH(fieldBorder), 1.3f);
    g.DrawPath(&pen, &p);
    float d = r.Height - 6, x = on ? r.X + r.Width - d - 3 : r.X + 3;
    SolidBrush k(on ? Color(255, 255, 255, 255) : WithA(kGrey, 0.9f));
    g.FillEllipse(&k, x, r.Y + 3, d, d);
}
static void DrawGear(Graphics &g, float cx, float cy, Color c)
{
    Pen p(c, 2.2f);
    p.SetStartCap(LineCapRound); p.SetEndCap(LineCapRound);
    for (int i = 0; i < 8; i++) { float a = i * 3.14159265f / 4; g.DrawLine(&p, cx + cosf(a) * 5.5f, cy + sinf(a) * 5.5f, cx + cosf(a) * 8.5f, cy + sinf(a) * 8.5f); }
    Pen r(c, 2.4f);
    g.DrawEllipse(&r, cx - 5.2f, cy - 5.2f, 10.4f, 10.4f);
}
static void DrawExperimental(Graphics &g, float x, float y)
{
    RectF pr(x, y, 96, 18);
    GraphicsPath pp; RoundRect(pp, pr, 9);
    SolidBrush pb(Color(235, 214, 128, 32));
    g.FillPath(&pb, &pp);
    Text(g, T(L"EXPÉRIMENTAL", L"EXPERIMENTAL"), pr, 9.5f, FontStyleBold, Color(255, 255, 255, 255));
}

static float MscMaxScroll()
{
    int rows = g_mscPage >= 0 && g_mscPage < (int)g_msc.size() ? (int)g_msc[g_mscPage].sets.size() + 1 : (int)g_msc.size();
    float h = g_mscPage >= 0 ? kMscSetH : kMscRowH;
    return max(0.0f, rows * h - kMscList.Height);
}

// Element sous la souris : 1000 interrupteur, 1001 installer, 1002 retour, 1003 mod active (page), 1004 dossier,
// 2000+i ouvrir le mod i, 3000+i*4+k option i (k : 0 case, 1 moins, 2 plus).
static int MscAt(float x, float y)
{
    if (g_state != ST_IDLE) return -1;
    bool page = g_mscPage >= 0 && g_mscPage < (int)g_msc.size();
    if (page) {
        if (kMscBack.Contains(x, y)) return 1002;
        if (!kMscList.Contains(x, y)) return -1;
        int i = (int)((y - kMscList.Y + g_scroll[TAB_MODS]) / kMscSetH);
        if (i == 0) return x > kMscList.X + kMscList.Width - 80 ? 1003 : -1;
        const auto &sets = g_msc[g_mscPage].sets;
        if (--i < 0 || i >= (int)sets.size()) return -1;
        const MscSet &s = sets[i];
        float right = kMscList.X + kMscList.Width - 12;
        if (s.type == "CheckBox" || s.type == "CheckBoxGroup") return x > right - 64 ? 3000 + i * 4 : -1;
        if (s.type == "Slider" || s.type == "SliderInt" || s.type == "DropDown") {
            if (x > right - 30) return 3000 + i * 4 + 2;
            if (x > right - 190 && x < right - 160) return 3000 + i * 4 + 1;
        }
        return -1;
    }
    if (g_mscInstalled && kMscToggle.Contains(x, y)) return 1000;
    if (!g_mscInstalled && kMscInstall.Contains(x, y)) return 1001;
    if (g_mscInstalled && kMscOpenDir.Contains(x, y)) return 1004;
    if (!kMscList.Contains(x, y)) return -1;
    int i = (int)((y - kMscList.Y + g_scroll[TAB_MODS]) / kMscRowH);
    return i >= 0 && i < (int)g_msc.size() && g_msc[i].known ? 2000 + i : -1;
}

static bool MscClick(float x, float y)
{
    int h = MscAt(x, y);
    if (h == 1000) {
        g_mscOn = !g_mscOn;
        WritePrivateProfileStringW(L"Lanceur", L"MSCLoader", g_mscOn ? L"1" : L"0", g_iniLauncher.c_str());
        SetStatus(K_NORMAL, g_mscOn ? T(L"MSCLoader sera chargé au lancement", L"MSCLoader will load at launch") : T(L"MSCLoader ne sera pas chargé (rien n'est désinstallé)", L"MSCLoader will not load (nothing is uninstalled)"));
    } else if (h == 1001) MscInstall();
    else if (h == 1004) ShellExecuteW(g_wnd, L"open", g_mscDir.c_str(), NULL, NULL, SW_SHOWNORMAL);
    else if (h == 1002) { g_mscPage = -1; g_scroll[TAB_MODS] = 0; }
    else if (h >= 2000 && h < 3000) { g_mscPage = h - 2000; g_scroll[TAB_MODS] = 0; MscLoadPage(); }
    else if (h == 1003) { g_mscPageDisabled = !g_mscPageDisabled; if (!g_mscFake) MscSavePage(); }
    else if (h >= 3000) {
        int i = (h - 3000) / 4, k = (h - 3000) % 4;
        MscStep(g_msc[g_mscPage].sets[i], k == 1 ? -1 : 1);
        if (!g_mscFake) MscSavePage();
    } else return kOptPanel.Contains(x, y);
    return true;
}

static void DrawModsPage(Graphics &g)
{
    const MscMod &m = g_msc[g_mscPage];
    Text(g, m.name, RectF(460, 124, 290, 26), 17, FontStyleBold, kInk, StringAlignmentNear);
    bool backHot = g_mscHot == 1002;
    Text(g, T(L"← Retour aux mods", L"← Back to mods"), kMscBack, 12.5f, backHot ? FontStyleBold | FontStyleUnderline : FontStyleBold, backHot ? kAcc : kGrey, StringAlignmentFar);
    std::wstring sub = L"v" + m.ver + (m.author.empty() ? L"" : L" · " + m.author) + L" · " + m.file;
    Text(g, sub, RectF(460, 148, 476, 20), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
    float sc = g_scroll[TAB_MODS], ms = MscMaxScroll(), right = kMscList.X + kMscList.Width - 12;
    g.SetClip(kMscList);
    for (int i = -1; i < (int)m.sets.size(); i++) {
        RectF r(kMscList.X + 4, kMscList.Y + (i + 1) * kMscSetH - sc, kMscList.Width - (ms > 0 ? 16 : 8), kMscSetH - 4);
        if (r.Y + r.Height < kMscList.Y || r.Y > kMscList.Y + kMscList.Height) continue;
        if (i < 0) {   // le mod lui-meme : active ou non (isDisabled de MSCLoader)
            GraphicsPath rp; RoundRect(rp, r, 9);
            SolidBrush rb(TH(card)); g.FillPath(&rb, &rp);
            Text(g, T(L"Mod activé", L"Mod enabled"), RectF(r.X + 12, r.Y, 300, r.Height), 13, FontStyleBold, kInk, StringAlignmentNear);
            DrawToggle(g, RectF(right - 56, r.Y + (r.Height - 24) / 2, 52, 24), !g_mscPageDisabled, g_mscHot == 1003);
            continue;
        }
        const MscSet &s = m.sets[i];
        bool hdr = s.type == "HeaderGroup" || s.type == "Header";
        if (hdr) { Text(g, s.name, RectF(r.X + 4, r.Y + 6, r.Width - 8, r.Height - 6), 12.5f, FontStyleBold, kAcc, StringAlignmentNear); continue; }
        if (s.type == "Text") { Para(g, s.name, RectF(r.X + 8, r.Y + 4, r.Width - 16, r.Height - 4), 11.5f, kGrey); continue; }
        if (s.type == "Button" || s.type == "RButton" || s.type == "LayoutGroup" || s.type == "LayoutGroupEnd") {
            Text(g, s.name + T(L"  (bouton : en jeu)", L"  (button: in game)"), RectF(r.X + 12, r.Y, r.Width - 24, r.Height), 11.5f, FontStyleRegular, WithA(kGrey, 0.8f), StringAlignmentNear);
            continue;
        }
        GraphicsPath rp; RoundRect(rp, r, 9);
        SolidBrush rb(TH(card)); g.FillPath(&rb, &rp);
        Text(g, s.name, RectF(r.X + 12, r.Y, r.Width - 220, r.Height), 12.5f, FontStyleRegular, kInk, StringAlignmentNear);
        std::string *v = MscVal(s.id);
        std::string val = v ? *v : s.val;
        int base = 3000 + i * 4;
        if (s.type == "CheckBox" || s.type == "CheckBoxGroup") { DrawToggle(g, RectF(right - 56, r.Y + (r.Height - 24) / 2, 52, 24), val == "true" || val == "1", g_mscHot == base); continue; }
        std::wstring shown;
        bool arrows = s.type == "Slider" || s.type == "SliderInt" || s.type == "DropDown";
        if (s.type == "DropDown") { int k = atoi(val.c_str()); shown = k >= 0 && k < (int)s.items.size() ? s.items[k] : Widen(val); }
        else if (s.type == "SliderInt") { int k = atoi(val.c_str()) - atoi(s.mn.c_str()); shown = k >= 0 && k < (int)s.items.size() ? s.items[k] : Widen(val); }
        else if (s.type == "Slider") shown = Widen(Num(atof(val.c_str()), max(atoi(s.dec.c_str()), 0)));
        else if (s.type == "ColorPicker") {
            int c[4] = { 0, 0, 0, 255 };
            sscanf_s(val.c_str(), "\"%d,%d,%d,%d", &c[0], &c[1], &c[2], &c[3]);
            RectF sw(right - 70, r.Y + 8, 26, r.Height - 16);
            GraphicsPath sp; RoundRect(sp, sw, 5);
            SolidBrush sb(Color(255, (BYTE)c[0], (BYTE)c[1], (BYTE)c[2])); g.FillPath(&sb, &sp);
            Text(g, T(L"en jeu", L"in game"), RectF(right - 40, r.Y, 40, r.Height), 10.5f, FontStyleRegular, kGrey, StringAlignmentFar);
            continue;
        } else {   // textes
            std::string t = val.size() >= 2 && val[0] == '"' ? JsonDecode(val, 0) : val;
            shown = Widen(t, CP_UTF8) + T(L"  (en jeu)", L"  (in game)");
            Text(g, shown, RectF(right - 200, r.Y, 200, r.Height), 11.5f, FontStyleRegular, kGrey, StringAlignmentFar);
            continue;
        }
        if (arrows) {
            for (int k = 1; k <= 2; k++) {
                RectF ar = k == 1 ? RectF(right - 190, r.Y + 7, 26, r.Height - 14) : RectF(right - 26, r.Y + 7, 26, r.Height - 14);
                GraphicsPath ap; RoundRect(ap, ar, 7);
                bool hot = g_mscHot == base + k;
                SolidBrush ab(hot ? WithA(kAcc, 0.25f) : TH(field)); g.FillPath(&ab, &ap);
                Text(g, k == 1 ? L"‹" : L"›", ar, 15, FontStyleBold, hot ? kAcc : kInk);
            }
            Text(g, shown, RectF(right - 160, r.Y, 130, r.Height), 12.5f, FontStyleBold, kInk);
        }
    }
    g.ResetClip();
    if (ms > 0) {
        float h = kMscList.Height * kMscList.Height / (kMscList.Height + ms), y = kMscList.Y + (kMscList.Height - h) * sc / ms;
        GraphicsPath sp; RoundRect(sp, RectF(kMscList.X + kMscList.Width - 6, y, 4, h), 2);
        SolidBrush sb(WithA(kAcc, 0.5f)); g.FillPath(&sb, &sp);
    }
}

static void DrawMods(Graphics &g)
{
    DrawPanel(g);
    if (g_mscPage >= 0 && g_mscPage < (int)g_msc.size()) {
        DrawModsPage(g);
    } else {
        Text(g, T(L"MODS · MSCLOADER", L"MODS · MSCLOADER"), RectF(460, 124, 300, 26), 17, FontStyleBold, kInk, StringAlignmentNear);
        Bitmap mb(1, 1);
        Graphics mg(&mb);
        DrawExperimental(g, 460 + MeasureW(mg, T(L"MODS · MSCLOADER", L"MODS · MSCLOADER"), 17, FontStyleBold) + 14, 128);
        std::wstring sub;
        if (g_mscInstall == 1) sub = T(L"Installation de MSCLoader…", L"Installing MSCLoader…");
        else if (!g_mscInstalled) sub = T(L"MSCLoader n'est pas installé.", L"MSCLoader is not installed.");
        else {
            wchar_t b[200];
            swprintf_s(b, T(L"MSCLoader %s · %d mod(s) · dossier %s", L"MSCLoader %s · %d mod(s) · %s folder"), g_mscVer.empty() ? L"" : g_mscVer.c_str(), (int)g_msc.size(),
                       g_mscDirKind == L"MD" ? T(L"Documents", L"Documents") : g_mscDirKind == L"AD" ? L"LocalLow" : T(L"du jeu", L"game"));
            sub = b;
        }
        Text(g, sub, RectF(460, 150, 476, 20), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
        if (g_mscInstalled) {
            Text(g, T(L"Charger MSCLoader au lancement", L"Load MSCLoader at launch"), RectF(460, 176, 380, 24), 13, FontStyleBold, kInk, StringAlignmentNear);
            DrawToggle(g, kMscToggle, g_mscOn, g_mscHot == 1000);
        } else {
            GraphicsPath bp; RoundRect(bp, kMscInstall, kMscInstall.Height / 2);
            float a = g_mscInstall == 1 ? 0.5f : 1.0f;
            LinearGradientBrush lg(kMscInstall, WithA(Mix(kAcc, Color(255, 255, 255, 255), g_mscHot == 1001 ? 0.15f : 0.0f), a), WithA(kAcc2, a), LinearGradientModeHorizontal);
            g.FillPath(&lg, &bp);
            Text(g, g_mscInstall == 1 ? T(L"INSTALLATION…", L"INSTALLING…") : T(L"INSTALLER MSCLOADER", L"INSTALL MSCLOADER"), kMscInstall, 13, FontStyleBold, kOnAcc);
            Para(g, T(L"Le chargeur de mods de la communauté (projet indépendant).", L"The community mod loader (independent project)."), RectF(460, 176, 240, 34), 11, kGrey);
        }
        float sc = g_scroll[TAB_MODS], ms = MscMaxScroll();
        if (g_mscInstalled && g_msc.empty())
            Para(g, T(L"Aucun mod dans le dossier Mods. Ajoute les DLL des mods MSCLoader (version My Winter Car) puis lance le jeu une fois : leurs noms et leurs options apparaîtront ici.",
                      L"No mod in the Mods folder. Add MSCLoader mods (My Winter Car version), then start the game once: their names and options will show up here."),
                 RectF(480, 250, 436, 80), 12, kGrey, StringAlignmentCenter);
        g.SetClip(kMscList);
        for (int i = 0; i < (int)g_msc.size(); i++) {
            const MscMod &m = g_msc[i];
            RectF r(kMscList.X + 4, kMscList.Y + i * kMscRowH - sc, kMscList.Width - (ms > 0 ? 16 : 8), kMscRowH - 6);
            if (r.Y + r.Height < kMscList.Y || r.Y > kMscList.Y + kMscList.Height) continue;
            bool hot = g_mscHot == 2000 + i;
            GraphicsPath rp; RoundRect(rp, r, 10);
            SolidBrush rb(hot ? TH(cardSel) : TH(card)); g.FillPath(&rb, &rp);
            Pen rpen(hot ? kAcc : TH(choiceBorder), 1.1f); g.DrawPath(&rpen, &rp);
            float a = m.disabled ? 0.5f : 1.0f;
            Text(g, m.name, RectF(r.X + 14, r.Y + 3, r.Width - 140, 22), 13.5f, FontStyleBold, WithA(kInk, a), StringAlignmentNear);
            std::wstring line = m.known ? L"v" + m.ver + (m.author.empty() ? L"" : L" · " + m.author) + (m.disabled ? T(L" · désactivé", L" · disabled") : L"")
                                        : m.file + T(L" · lance le jeu une fois pour les détails", L" · start the game once for details");
            Text(g, line, RectF(r.X + 14, r.Y + 23, r.Width - 80, 18), 11, FontStyleRegular, kGrey, StringAlignmentNear);
            if (m.known) DrawGear(g, r.X + r.Width - 26, r.Y + r.Height / 2, hot ? kAcc : kGrey);
        }
        g.ResetClip();
        if (ms > 0) {
            float h = kMscList.Height * kMscList.Height / (kMscList.Height + ms), y = kMscList.Y + (kMscList.Height - h) * sc / ms;
            GraphicsPath sp; RoundRect(sp, RectF(kMscList.X + kMscList.Width - 6, y, 4, h), 2);
            SolidBrush sb(WithA(kAcc, 0.5f)); g.FillPath(&sb, &sp);
        }
    }
    Pen sep(TH(sep), 1);
    g.DrawLine(&sep, kOptPanel.X + 18, 518.0f, kOptPanel.X + kOptPanel.Width - 18, 518.0f);
    Para(g, T(L"Réglages pris au prochain lancement du jeu. Envoi des mods aux autres joueurs : bientôt.",
              L"Settings apply at the next game launch. Sending mods to the other players: coming soon."),
         RectF(kOptPanel.X + 20, 524, kOptPanel.Width - 40, 26), 11.5f, kGrey, StringAlignmentCenter);
    if (g_mscInstalled && g_mscPage < 0) {
        bool hot = g_mscHot == 1004;
        Text(g, T(L"Ouvrir le dossier Mods", L"Open the Mods folder"), kMscOpenDir, 11.5f, hot ? FontStyleBold | FontStyleUnderline : FontStyleUnderline, hot ? kAcc : kGrey, StringAlignmentNear);
    }
}
