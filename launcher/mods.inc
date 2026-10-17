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
// off : mod coupe par le lanceur (demande de JD, 12/10 : « activer / desactiver un mod, pas seulement le partager ») -- sa DLL
// renommee <nom>.dll.off, que MSCLoader ne charge pas (il ne lit que les *.dll) ; file garde le nom en .dll.
struct MscMod { std::wstring file, id, name, ver, author, desc; bool disabled = false, known = false, off = false; unsigned long long bytes = 0; std::vector<MscSet> sets; };
static std::vector<MscMod> g_msc;
// Mods recus de l'hote du dernier salon : les DLL de %LOCALAPPDATA%\MWCoop\mods-invite (modsync.inc CoopModsDir, gardes pour la
// prochaine fois). Releves ici toutes les 3 s avec les mods (page MODS, carte « Recus de l'hote »).
struct MscRecv { std::wstring name; unsigned long long bytes = 0; };
static std::vector<MscRecv> g_mscRecv;
static FILETIME g_mscRecvT = {};   // (le plus recent)
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
static bool MenuSkipper(const MscMod &m);       // (modsync.inc : saute le menu -- jamais envoye)
static void ModSetShared(const std::wstring &file, bool on);

static std::wstring MscManaged() { return g_gameDir + L"mywintercar_Data\\Managed\\"; }
static std::wstring MscIni() { return g_gameDir + L"doorstop_config.ini"; }

// MSCLoader de MWCoop (demande de JD, 08/10 : un joueur dont le MSCLoader officiel ne chargeait aucun mod, meme apres une
// reinstallation propre ; chez d'autres il marche). Le meme MSCLoader officiel, telecharge par le lanceur dans
// %LOCALAPPDATA%\MWCoop\mscloader-mwcoop\ (winhttp.dll de Doorstop, Managed\ : MSCLoader et ses bibliotheques) : rien
// n'est ecrit dans le jeu (ni winhttp.dll ni Managed). Le jeu part d'une copie de lancement (MscCopyDir, ou celle de
// l'invite) avec ce winhttp.dll et un doorstop_config.ini qui vise ce dossier (cible absolue ; [UnityMono]
// dll_search_path_override : Mono y cherche d'abord, devant un MSCLoader officiel range dans Managed). Les mods restent
// dans le dossier Mods du jeu (ou celui choisi pour l'officiel : [MSCLoader] mods= de son doorstop_config.ini). Essai a
// la main du 08/10 : MSCLoader charge depuis ce dossier, 11 mods trouves par la jonction Mods de la copie.
// Choix dans l'onglet MODS ([Lanceur] MSCLoaderMWCoop) : par defaut l'officiel s'il est installe, sinon celui de MWCoop.
static std::wstring MscPackDir() { std::wstring l = LocalDir(); return l.empty() ? L"" : l + L"mscloader-mwcoop\\"; }
static std::wstring MscCopyDir() { std::wstring l = LocalDir(); return l.empty() ? L"" : l + L"jeu-mscloader\\My Winter Car\\"; }
static bool MscOfficialInstalled() { return !g_gameDir.empty() && FileExists(MscManaged() + L"MSCLoader.Preloader.dll") && FileExists(g_gameDir + L"winhttp.dll") && FileExists(MscIni()); }
static bool MscOwnInstalled() { std::wstring p = MscPackDir(); return !p.empty() && FileExists(p + L"winhttp.dll") && FileExists(p + L"Managed\\MSCLoader.Preloader.dll"); }
static bool MscOwn() { return g_mscOwnPref >= 0 ? g_mscOwnPref == 1 : !MscOfficialInstalled(); }
static std::wstring MscModsDir();
// Doorstop d'une copie de lancement : notre winhttp.dll, et un doorstop_config.ini vers notre dossier. guest : la copie de
// l'invite (Mods de la copie : ceux de l'hote) ; sinon le dossier des mods de l'officiel (GF : la jonction Mods de la copie).
static bool MscOwnApply(const std::wstring &m, bool enabled, bool guest)
{
    std::wstring pack = MscPackDir(), dll = m + L"winhttp.dll";
    SetFileAttributesW(dll.c_str(), FILE_ATTRIBUTE_NORMAL);
    if (!CopyFileW((pack + L"winhttp.dll").c_str(), dll.c_str(), FALSE)) { TestLog("mscloader de MWCoop : winhttp.dll impossible dans la copie (erreur %lu)", GetLastError()); return false; }
    wchar_t kind[16] = L"GF", intro[16] = L"false", cfg[16] = L"false";
    if (FileExists(MscIni())) {
        if (!guest) GetPrivateProfileStringW(L"MSCLoader", L"mods", L"GF", kind, 16, MscIni().c_str());
        GetPrivateProfileStringW(L"MSCLoader", L"skipIntro", L"false", intro, 16, MscIni().c_str());
        GetPrivateProfileStringW(L"MSCLoader", L"skipConfigScreen", L"false", cfg, 16, MscIni().c_str());
    }
    if (!guest && !_wcsicmp(kind, L"GF")) {   // Mods du jeu, relie par la copie (cree ici, apres la copie : jonction a faire)
        SHCreateDirectoryExW(NULL, (g_gameDir + L"Mods").c_str(), NULL);
        if (GetFileAttributesW((m + L"Mods").c_str()) == INVALID_FILE_ATTRIBUTES && !MakeJunction(m + L"Mods", g_gameDir + L"Mods"))
            TestLog("mscloader de MWCoop : jonction Mods impossible (erreur %lu)", GetLastError());
    }
    // MSCLoader 1.4.2 (menu des mods, ModMenu.Mod_Settings) lit la version de <jeu>\mywintercar_Data\Managed\MSCLoader.Preloader.dll :
    // absente (MSCLoader jamais installe dans le jeu), son Init s'arrete sur FileNotFoundException avant de charger le moindre mod
    // (retour de JD, 12/10 : l'invite recoit la 205 de l'hote mais ne la voit pas). Ce seul fichier y est pose (Managed de la
    // copie : jonction vers celui du jeu) ; le jeu ne le charge pas tout seul (seul doorstop_config.ini le vise).
    std::wstring pre = m + L"mywintercar_Data\\Managed\\MSCLoader.Preloader.dll";
    if (!FileExists(pre)) {
        if (CopyFileW((pack + L"Managed\\MSCLoader.Preloader.dll").c_str(), pre.c_str(), TRUE)) TestLog("mscloader de MWCoop : MSCLoader.Preloader.dll pose dans Managed du jeu (lu par MSCLoader 1.4)");
        else TestLog("mscloader de MWCoop : MSCLoader.Preloader.dll impossible dans Managed (erreur %lu) : MSCLoader 1.4 risque de ne charger aucun mod", GetLastError());
    }
    std::wstring ini = m + L"doorstop_config.ini", managed = pack + L"Managed";
    std::string t = "[General]\r\nenabled=" + std::string(enabled ? "true" : "false") + "\r\ntarget_assembly=" + Narrow(managed + L"\\MSCLoader.Preloader.dll", CP_UTF8)
                  + "\r\nredirect_output_log=true\r\nignore_disable_switch=true\r\n\r\n[UnityMono]\r\ndll_search_path_override=" + Narrow(managed, CP_UTF8)
                  + "\r\n\r\n[MSCLoader]\r\nmods=" + Narrow(kind) + "\r\nskipIntro=" + Narrow(intro) + "\r\nskipConfigScreen=" + Narrow(cfg) + "\r\n";
    SetFileAttributesW(ini.c_str(), FILE_ATTRIBUTE_NORMAL);
    HANDLE f = CreateFileW(ini.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (f == INVALID_HANDLE_VALUE) { TestLog("mscloader de MWCoop : doorstop_config.ini impossible (erreur %lu)", GetLastError()); return false; }
    DWORD n; WriteFile(f, t.data(), (DWORD)t.size(), &n, NULL); CloseHandle(f);
    TestLog("mscloader de MWCoop : copie %ls, %s, mods %ls", m.c_str(), enabled ? "active" : "coupe", kind);
    return true;
}

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
    g_mscInstalled = MscOwn() ? MscOwnInstalled() : MscOfficialInstalled();
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
                m.bytes = ((unsigned long long)fd.nFileSizeHigh << 32) | fd.nFileSizeLow;
                list.push_back(m);
            } while (FindNextFileW(h, &fd));
            FindClose(h);
        }
        h = FindFirstFileW((g_mscDir + L"*.dll.off").c_str(), &fd);   // (mods coupes ici)
        if (h != INVALID_HANDLE_VALUE) {
            do {
                if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) continue;
                std::wstring f = fd.cFileName;
                f.resize(f.size() - 4);   // (sans .off)
                bool dup = false;
                for (auto &x : list) dup |= !_wcsicmp(x.file.c_str(), f.c_str());
                if (dup) continue;   // (les deux : la DLL l'emporte)
                MscMod m;
                for (auto &c : cache) if (!_wcsicmp(c.file.c_str(), f.c_str())) { m = c; break; }
                if (!m.known) { m.file = f; m.name = f.substr(0, f.size() - 4); }
                m.off = true;
                m.bytes = ((unsigned long long)fd.nFileSizeHigh << 32) | fd.nFileSizeLow;
                list.push_back(m);
            } while (FindNextFileW(h, &fd));
            FindClose(h);
        }
    }
    // recus de l'hote (mods-invite)
    g_mscRecv.clear(); g_mscRecvT = FILETIME();
    std::wstring ld = LocalDir();
    if (!ld.empty()) {
        WIN32_FIND_DATAW fd;
        HANDLE h = FindFirstFileW((ld + L"mods-invite\\*.dll").c_str(), &fd);
        if (h != INVALID_HANDLE_VALUE) {
            do {
                if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) continue;
                MscRecv r; r.name = fd.cFileName; r.name.resize(r.name.size() - 4);
                r.bytes = ((unsigned long long)fd.nFileSizeHigh << 32) | fd.nFileSizeLow;
                if (CompareFileTime(&fd.ftLastWriteTime, &g_mscRecvT) > 0) g_mscRecvT = fd.ftLastWriteTime;
                g_mscRecv.push_back(r);
            } while (FindNextFileW(h, &fd));
            FindClose(h);
        }
        std::sort(g_mscRecv.begin(), g_mscRecv.end(), [](const MscRecv &a, const MscRecv &b) { return _wcsicmp(a.name.c_str(), b.name.c_str()) < 0; });
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
static DWORD WINAPI MscInstallThread(void *param)
{
    bool own = param != NULL;   // MSCLoader de MWCoop (MscPackDir) ; sinon l'officiel, dans le jeu
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
    if (own) {   // tout dans notre dossier ; seul le dossier Mods (le meme que l'officiel) recoit ses dossiers de base
        std::wstring pack = MscPackDir();
        if (pack.empty()) return fail("dossier %LOCALAPPDATA%\\MWCoop introuvable");
        SHCreateDirectoryExW(NULL, (pack + L"Managed").c_str(), NULL);
        SHCreateDirectoryExW(NULL, (tmp + L"core").c_str(), NULL);
        if (!Unzip(pkg + L"core64.pack", tmp + L"core", &why)) return fail("core64.pack : " + why);
        SetFileAttributesW((pack + L"winhttp.dll").c_str(), FILE_ATTRIBUTE_NORMAL);
        if (!CopyFileW((tmp + L"core\\winhttp.dll").c_str(), (pack + L"winhttp.dll").c_str(), FALSE)) return fail("winhttp.dll : en cours d'utilisation (erreur " + std::to_string(GetLastError()) + ")");
        if (!Unzip(pkg + L"main_ref.pack", pack + L"Managed", &why)) return fail("bibliotheques : " + why);
        if (!Unzip(pkg + L"main_mwc.pack", tmp + L"mwc", &why)) return fail("main_mwc : " + why);
        if (!Unzip(tmp + L"mwc\\Managed.zip", pack + L"Managed", &why)) return fail("MSCLoader.dll : " + why);
        std::wstring md = MscModsDir();
        md.pop_back();
        SHCreateDirectoryExW(NULL, md.c_str(), NULL);
        if (!Unzip(tmp + L"mwc\\Mods.zip", md, &why)) return fail("dossier Mods : " + why);
        CreateDirectoryW((md + L"\\References").c_str(), NULL);
        CreateDirectoryW((md + L"\\Config").c_str(), NULL);
        HANDLE vf = CreateFileW((pack + L"version.txt").c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
        if (vf != INVALID_HANDLE_VALUE) { DWORD n; WriteFile(vf, tag.data(), (DWORD)tag.size(), &n, NULL); CloseHandle(vf); }
        TestLog("mods : MSCLoader %s de MWCoop installe (%s ; mods : %s)", tag.c_str(), Narrow(pack, CP_UTF8).c_str(), Narrow(md, CP_UTF8).c_str());
        g_mscInstallWhy = tag;
        g_mscInstall = 2;
        return 0;
    }
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

// Installer (ou reinstaller : demande de JD, 08/10) le MSCLoader choisi dans l'onglet : celui de MWCoop (a part) ou
// l'officiel (dans le jeu ; ses reglages gardes).
static void MscInstall(bool reinstall = false)
{
    if (g_mscInstall == 1 || g_gameDir.empty()) return;
    if (GameProcessRunning()) { SetStatus(K_ERR, T(L"Ferme My Winter Car avant d'installer MSCLoader", L"Close My Winter Car before installing MSCLoader")); return; }
    bool own = MscOwn();
    if (own || reinstall) {
        std::wstring q = own
            ? T(L"Télécharger la dernière version officielle de MSCLoader (GitHub de piotrulos, environ 4 Mo) et l'installer POUR MWCOOP, à part (%LOCALAPPDATA%\\MWCoop) ?\n\n"
                L"Le dossier du jeu n'est pas modifié (sauf le dossier Mods, s'il manque). Tes mods restent dans le dossier Mods du jeu. Un MSCLoader officiel déjà installé n'est pas touché.\n\n"
                L"MSCLoader est un projet indépendant de MWCoop. Son support dans MWCoop est EXPÉRIMENTAL.",
                L"Download the latest official MSCLoader (piotrulos's GitHub, about 4 MB) and install it FOR MWCOOP, apart (%LOCALAPPDATA%\\MWCoop)?\n\n"
                L"The game folder is not changed (except the Mods folder, if missing). Your mods stay in the game's Mods folder. An official MSCLoader already installed is not touched.\n\n"
                L"MSCLoader is a project independent from MWCoop. Its support in MWCoop is EXPERIMENTAL.")
            : T(L"Télécharger la dernière version officielle de MSCLoader (GitHub de piotrulos, environ 4 Mo) et la réinstaller dans le dossier du jeu ? Tes mods et leurs réglages sont gardés.",
                L"Download the latest official MSCLoader (piotrulos's GitHub, about 4 MB) and reinstall it in the game folder? Your mods and their settings are kept.");
        if (reinstall && own) q = std::wstring(T(L"RÉINSTALLER MSCLoader (tes mods et leurs réglages sont gardés).\n\n", L"REINSTALL MSCLoader (your mods and their settings are kept).\n\n")) + q;
        if (g_testSalon.empty() && MessageBoxW(g_wnd, q.c_str(), L"MWCoop", MB_YESNO | MB_ICONQUESTION) != IDYES) return;
        g_mscInstall = 1;
        g_progress = 0;
        SetStatus(K_NORMAL, T(L"Installation de MSCLoader…", L"Installing MSCLoader…"));
        TestLog("mods : %s de MSCLoader %s", reinstall ? "reinstallation" : "installation", own ? "de MWCoop" : "officiel");
        HANDLE t = CreateThread(NULL, 0, MscInstallThread, own ? (void *)1 : NULL, 0, NULL);
        if (t) CloseHandle(t);
        return;
    }
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

// ---- dessin (page MODS, dans kMain ; maquette du 17/10 : « Mes mods » a gauche, « Recus de l'hote » et « Bon a savoir » a droite)
static int MscModCount() { return g_mscInstalled ? (int)g_msc.size() : 0; }
static const RectF kMscL(264, 88, 658, 656), kMscRecvCard(934, 88, 330, 280), kMscInfoCard(934, 380, 330, 364);
static const RectF kMscList(282, 234, 622, 418),          // lignes des mods (liste)
                   kMscPageList(288, 170, 952, 540),      // options d'un mod (page)
                   kMscToggle(854, 104, 50, 28),          // Mods actives (g_mscOn)
                   kMscAuto(1196, 288, 50, 28),           // Accepter les fichiers de l'hote (ModsAuto)
                   kMscInstall(282, 250, 300, 46),        // INSTALLER MSCLOADER (absent)
                   kMscBack(1000, 108, 240, 30),          // page : retour
                   kMscOpenDir(282, 692, 196, 36), kMscSendAll(488, 692, 130, 36), kMscClearRecv(952, 316, 294, 36),
                   kMscSrcOff(582, 170, 96, 30), kMscSrcOwn(682, 170, 96, 30), kMscReinst(784, 170, 120, 30);   // officiel / MWCoop, reinstaller
static RectF kMscCompat(952, 704, 294, 24);   // lien « Guide : jouer avec des mods » (article du GUIDE ; tuto.inc le vise)
static const float kMscRowH = 62, kMscSetH = 54;
// colonnes des lignes : icone 40 | nom | version 90 | envoye 130 | actif 50 | engrenage 36
static const float kMscColVer = 562, kMscColSend = 664, kMscColOn = 806, kMscColGear = 868, kMscColName = 334;

static void DrawToggle(Graphics &g, RectF r, bool on, bool hot) { UiToggle(g, r, on, hot); }
static void DrawGear(Graphics &g, float cx, float cy, Color c) { Icon(g, IC_GEAR, cx, cy, 20, c, 1.9f); }
static std::wstring SizeText(uint64_t b);   // (modsync.inc)

static const RectF &MscListRect() { return g_mscPage >= 0 && g_mscPage < (int)g_msc.size() ? kMscPageList : kMscList; }
static float MscMaxScroll()
{
    bool page = g_mscPage >= 0 && g_mscPage < (int)g_msc.size();
    int rows = page ? (int)g_msc[g_mscPage].sets.size() + 1 : (int)g_msc.size();
    float h = page ? kMscSetH : kMscRowH;
    return max(0.0f, rows * h - 6 - MscListRect().Height);
}
static RectF MscRowRect(int i) { float ms = MscMaxScroll(); return RectF(kMscList.X, kMscList.Y + i * kMscRowH - g_scroll[TAB_MODS], kMscList.Width - (ms > 0 ? 12 : 0), kMscRowH - 6); }
static RectF MscGearRect(const RectF &r) { return RectF(kMscColGear, r.Y + (r.Height - 36) / 2, 36, 36); }
static RectF MscSendRect(const RectF &r) { return RectF(kMscColSend, r.Y + (r.Height - 26) / 2, 130, 26); }
static RectF MscActiveRect(const RectF &r) { return RectF(kMscColOn, r.Y + (r.Height - 26) / 2, 50, 26); }
// Mod coupe / remis : sa DLL renommee <nom>.dll.off / <nom>.dll (le jeu ferme : sinon la DLL est verrouillee).
static void MscSetActive(int i, bool on)
{
    if (i < 0 || i >= (int)g_msc.size() || g_mscDir.empty()) return;
    MscMod m = g_msc[i];
    std::wstring dll = g_mscDir + m.file, off = dll + L".off";
    if (on == !m.off) return;
    if (GameProcessRunning()) { SetStatus(K_WARN, T(L"Ferme le jeu pour activer ou désactiver un mod", L"Close the game to turn a mod on or off")); return; }
    BOOL ok = on ? MoveFileExW(off.c_str(), dll.c_str(), 0) : MoveFileExW(dll.c_str(), off.c_str(), MOVEFILE_REPLACE_EXISTING);
    if (!ok) { SetStatus(K_ERR, T(L"%s : renommage impossible (erreur %lu)", L"%s: can't rename (error %lu)"), m.name.c_str(), GetLastError()); return; }
    TestLog("mods : %s %s", Narrow(m.file, CP_UTF8).c_str(), on ? "active (.dll)" : "desactive (.dll.off)");
    SetStatus(K_NORMAL, on ? T(L"%s activé : chargé au prochain lancement", L"%s on: loaded at the next launch") : T(L"%s désactivé : ni chargé ni envoyé (fichier .dll.off)", L"%s off: neither loaded nor sent (.dll.off file)"), m.name.c_str());
    MscScan();
}
static bool MscSent(const MscMod &m) { return ModShared(m.file) && !m.off && !MenuSkipper(m); }   // envoye aux invites du salon

// Zones de la liste, declarees pendant le dessin (comme HotZone, mais pour g_mscHot : la souris et le clic passent par MscAt).
// Element : 1000 interrupteur Mods actives, 1001 installer, 1002 retour, 1003 mod active (page), 1004 dossier, 1005 accepter
// les fichiers de l'hote, 1006 / 1007 MSCLoader officiel / MWCoop, 1008 reinstaller, 1009 guide des mods, 1010 tout envoyer,
// 2000+i ouvrir le mod i, 3000+i*4+k option i (k : 0 case, 1 moins, 2 plus), 4000+i envoyer, 6000+i actif.
static std::vector<HotRect> g_mscZones;
static void MscZone(RectF r, int id) { g_mscZones.push_back({ r, id }); }
static int MscAt(float x, float y)
{
    if (g_state != ST_IDLE) return -1;
    bool page = g_mscPage >= 0 && g_mscPage < (int)g_msc.size();
    if (page) {
        if (kMscBack.Contains(x, y)) return 1002;
        if (!kMscPageList.Contains(x, y)) return -1;
        int i = (int)((y - kMscPageList.Y + g_scroll[TAB_MODS]) / kMscSetH);
        float right = kMscPageList.X + kMscPageList.Width - 16;
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
    for (int i = (int)g_mscZones.size() - 1; i >= 0; i--) if (g_mscZones[i].r.Contains(x, y)) return g_mscZones[i].id;
    return -1;
}

// Lien « Guide : jouer avec des mods » : l'article du GUIDE (wikidata.inc, kW_ModCompat), sa categorie ouverte.
static void MscOpenCompat();   // (wiki.inc)

static bool MscClick(float x, float y)
{
    int h = MscAt(x, y);
    if (h == 1000) {
        g_mscOn = !g_mscOn;
        WritePrivateProfileStringW(L"Lanceur", L"MSCLoader", g_mscOn ? L"1" : L"0", g_iniLauncher.c_str());
        SetStatus(K_NORMAL, g_mscOn ? T(L"MSCLoader sera chargé au lancement", L"MSCLoader will load at launch") : T(L"MSCLoader ne sera pas chargé (rien n'est désinstallé)", L"MSCLoader will not load (nothing is uninstalled)"));
    } else if (h == 1005) { if (const Opt *o = OptByKey("ModsAuto")) OptSet(*o, OptGet(*o) ? 0 : 1); }
    else if (h == 1001) MscInstall();
    else if (h == 1008) MscInstall(true);
    else if (h == 1006 || h == 1007) {
        g_mscOwnPref = h == 1007 ? 1 : 0;
        WritePrivateProfileStringW(L"Lanceur", L"MSCLoaderMWCoop", g_mscOwnPref ? L"1" : L"0", g_iniLauncher.c_str());
        MscScan();
        if (g_mscOwnPref) SetStatus(K_NORMAL, MscOwnInstalled() ? T(L"MSCLoader de MWCoop : installé à part, le jeu n'est pas modifié", L"MWCoop's MSCLoader: installed apart, the game is not changed")
                                                                : T(L"MSCLoader de MWCoop : clique INSTALLER MSCLOADER (installé à part, le jeu n'est pas modifié)", L"MWCoop's MSCLoader: click INSTALL MSCLOADER (installed apart, the game is not changed)"));
        else SetStatus(K_NORMAL, MscOfficialInstalled() ? T(L"MSCLoader officiel : celui installé dans le jeu", L"Official MSCLoader: the one installed in the game")
                                                         : T(L"MSCLoader officiel : pas installé dans le jeu (INSTALLER MSCLOADER)", L"Official MSCLoader: not installed in the game (INSTALL MSCLOADER)"));
    }
    else if (h == 1004) ShellExecuteW(g_wnd, L"open", g_mscDir.c_str(), NULL, NULL, SW_SHOWNORMAL);
    else if (h == 1009) MscOpenCompat();
    else if (h == 1010) {   // tout envoyer : chaque mod actif (les sauteurs de menu ne partent jamais)
        int n = 0;
        for (const MscMod &m : g_msc) if (!m.off && !MenuSkipper(m) && !ModShared(m.file)) { ModSetShared(m.file, true); n++; }
        SetStatus(K_NORMAL, n ? T(L"%d mod(s) de plus seront envoyés aux invités du salon", L"%d more mod(s) will be sent to the lobby's guests") : T(L"Tous tes mods actifs sont déjà envoyés", L"All your active mods are already sent"), n);
    }
    else if (h == 1002) { g_mscPage = -1; g_scroll[TAB_MODS] = 0; }
    else if (h >= 6000) MscSetActive(h - 6000, g_msc[h - 6000].off);
    else if (h >= 4000) {
        const MscMod &m = g_msc[h - 4000];
        if (m.off) { SetStatus(K_NORMAL, T(L"%s est désactivé : il n'est pas envoyé", L"%s is off: it is not sent"), m.name.c_str()); return true; }
        if (MenuSkipper(m)) { SetStatus(K_WARN, T(L"%s saute le menu du jeu : jamais envoyé aux invités", L"%s skips the game menu: never sent to guests"), m.name.c_str()); return true; }
        bool on = !ModShared(m.file);
        ModSetShared(m.file, on);
        SetStatus(K_NORMAL, on ? T(L"%s sera envoyé aux invités du salon", L"%s will be sent to the lobby's guests") : T(L"%s reste chez toi (pas envoyé)", L"%s stays with you (not sent)"), m.name.c_str());
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

// Petits elements : bouton en verre clair (hot : g_mscHot == id, zone MscZone), puce (fond leger, liseré), carte interieure.
static void MscButton(Graphics &g, RectF r, const std::wstring &s, int id, int icon = -1, bool enabled = true, bool primary = false, float padR = 0)
{
    bool hot = enabled && g_mscHot == id;
    float a = enabled ? 1.0f : 0.4f;
    GraphicsPath p; RoundRect(p, r, min(11.0f, r.Height / 2));
    if (primary) { SolidBrush b(WithA(kAcc, a)); g.FillPath(&b, &p); if (hot) { SolidBrush hb(Color(60, 255, 255, 255)); g.FillPath(&hb, &p); } }
    else { SolidBrush b(hot ? TH(btn2Hot) : TH(btn2)); g.FillPath(&b, &p); Pen pen(WithA(TH(fieldBorder), a), 1.0f); g.DrawPath(&pen, &p); }
    Color ink = WithA(primary ? kOnAcc : hot ? kAcc : kInk, a);
    Graphics &mg = MeasureG();
    float px = 12.5f, tw = MeasureW(mg, s, px, FontStyleBold), iw = icon >= 0 ? 24.0f : 0.0f;
    float x0 = r.X + (r.Width - padR - tw - iw) / 2;   // (padR : place gardee a droite, une puce par exemple)
    if (icon >= 0) Icon(g, icon, x0 + 9, r.Y + r.Height / 2, 16, ink, 2.0f);
    Text(g, s, RectF(x0 + iw, r.Y, tw + 2, r.Height), px, FontStyleBold, ink, StringAlignmentNear);
    if (enabled) MscZone(r, id);
}
static void MscChip(Graphics &g, RectF r, const std::wstring &s, Color fill, Color ink, Color border, float px = 11.5f, int icon = -1)
{
    GraphicsPath p; RoundRect(p, r, r.Height / 2);
    SolidBrush b(fill); g.FillPath(&b, &p);
    Pen pen(border, 1.0f); g.DrawPath(&pen, &p);
    if (icon >= 0) { Icon(g, icon, r.X + 14, r.Y + r.Height / 2, 12, ink, 2.6f); Text(g, s, RectF(r.X + 22, r.Y, r.Width - 28, r.Height), px, FontStyleBold, ink, StringAlignmentNear); }
    else Text(g, s, r, px, FontStyleBold, ink);
}
static float MscChipW(const std::wstring &s, float px = 11.5f, int icon = -1) { return PillW(s, px) + 8 + (icon >= 0 ? 16 : 0); }
static void MscInnerCard(Graphics &g, RectF r)
{
    GraphicsPath p; RoundRect(p, r, 14);
    SolidBrush b(TH(card)); g.FillPath(&b, &p);
    Pen pen(TH(choiceBorder), 1.0f); g.DrawPath(&pen, &p);
}

static void DrawModsPage(Graphics &g)
{
    const MscMod &m = g_msc[g_mscPage];
    RectF c = kMain;
    Title(g, m.name, RectF(c.X + 24, c.Y + 20, 600, 28), 18, kInk);
    bool backHot = g_mscHot == 1002;
    Text(g, T(L"← Retour aux mods", L"← Back to mods"), kMscBack, 13, backHot ? FontStyleBold | FontStyleUnderline : FontStyleBold, backHot ? kAcc : kGrey, StringAlignmentFar);
    std::wstring sub = L"v" + m.ver + (m.author.empty() ? L"" : L" · " + m.author) + L" · " + m.file;
    Text(g, sub, RectF(c.X + 24, c.Y + 48, c.Width - 48, 22), 13, FontStyleRegular, kGrey, StringAlignmentNear);
    const RectF &L = kMscPageList;
    float sc = g_scroll[TAB_MODS], ms = MscMaxScroll(), right = L.X + L.Width - 16;
    GraphicsState st = g.Save();
    g.SetClip(L, CombineModeIntersect);
    for (int i = -1; i < (int)m.sets.size(); i++) {
        RectF r(L.X, L.Y + (i + 1) * kMscSetH - sc, L.Width - (ms > 0 ? 12 : 0), kMscSetH - 6);
        if (r.Y + r.Height < L.Y || r.Y > L.Y + L.Height) continue;
        if (i < 0) {   // le mod lui-meme : active ou non (isDisabled de MSCLoader)
            GraphicsPath rp; RoundRect(rp, r, 14);
            SolidBrush rb(TH(card)); g.FillPath(&rb, &rp);
            Text(g, T(L"Mod activé", L"Mod enabled"), RectF(r.X + 16, r.Y, 300, r.Height), 14, FontStyleBold, kInk, StringAlignmentNear);
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
    DrawScrollBar(g, L, sc, ms);
}

static void DrawMods(Graphics &g)
{
    g_mscZones.clear();
    if (g_mscPage >= 0 && g_mscPage < (int)g_msc.size()) { Glass(kMain); DrawModsPage(g); return; }
    Glass(kMscL); Glass(kMscRecvCard); Glass(kMscInfoCard);
    Graphics &mg = MeasureG();
    // ---- a gauche : mes mods
    {
        RectF c = kMscL;
        std::wstring t = T(L"Mes mods", L"My mods");
        float tw = MeasureW(mg, t, 16, FontStyleBold);
        Title(g, t, RectF(c.X + 18, c.Y + 14, tw + 6, 26), 16, kInk);
        if (g_mscInstalled) {
            wchar_t b[160];
            swprintf_s(b, T(L"· %d dans %s", L"· %d in %s"), (int)g_msc.size(), g_mscDirKind == L"MD" ? T(L"Documents\\My Winter Car\\Mods", L"Documents\\My Winter Car\\Mods") : g_mscDirKind == L"AD" ? L"LocalLow\\...\\Mods" : T(L"le dossier Mods du jeu", L"the game's Mods folder"));
            Text(g, b, RectF(c.X + 18 + tw + 6, c.Y + 15, 330, 26), 13, FontStyleBold, kGrey, StringAlignmentNear);
            bool hot = g_mscHot == 1000;
            Text(g, T(L"Mods activés", L"Mods on"), RectF(kMscToggle.X - 130, kMscToggle.Y, 122, kMscToggle.Height), 13, FontStyleBold, hot ? kAcc : kInk, StringAlignmentFar);
            DrawToggle(g, kMscToggle, g_mscOn, hot);
            MscZone(RectF(kMscToggle.X - 130, kMscToggle.Y - 6, 180, kMscToggle.Height + 12), 1000);
        }
        Para(g, T(L"« Envoyé » = copié chez les invités de ton salon ; un invité reçoit les mods de l'hôte dans sa copie de lancement, jamais dans son dossier. « Actif » coupé : le mod n'est plus chargé. Pris au prochain lancement du jeu.",
                  L"\"Sent\" = copied to your lobby's guests; a guest receives the host's mods in their launch copy, never in their own folder. \"On\" off: the mod no longer loads. Applied at the next game launch."), RectF(c.X + 18, c.Y + 46, c.Width - 36, 36), 12, kGrey);
        // MSCLoader : son etat (puce), officiel / MWCoop, reinstaller
        {
            bool own = MscOwn();
            std::wstring st;
            if (g_mscInstall == 1) st = T(L"Installation de MSCLoader…", L"Installing MSCLoader…");
            else if (!g_mscInstalled) st = T(L"MSCLoader absent", L"MSCLoader missing");
            else st = L"MSCLoader" + (g_mscVer.empty() ? L"" : L" " + g_mscVer) + (own ? T(L" · fourni par MWCoop", L" · provided by MWCoop") : T(L" · officiel (dans le jeu)", L" · official (in the game)"));
            float w = min(MscChipW(st, 11.5f, IC_CHECK), 290.0f);
            Color k = g_mscInstalled ? kOk : kWarn;
            MscChip(g, RectF(c.X + 18, kMscSrcOff.Y + 2, w, 26), st, WithA(k, 0.18f), k, WithA(k, 0.35f), 11.5f, g_mscInstalled ? IC_CHECK : IC_ALERT);
            for (int k2 = 0; k2 < 2; k2++) {
                RectF r = k2 ? kMscSrcOwn : kMscSrcOff;
                bool sel = (k2 == 1) == own, hot = g_mscHot == 1006 + k2;
                GraphicsPath p; RoundRect(p, r, 15);
                SolidBrush b(sel ? kAcc : hot ? TH(cardSel) : TH(card)); g.FillPath(&b, &p);
                Pen pen(sel ? kAcc : TH(choiceBorder), 1); g.DrawPath(&pen, &p);
                Text(g, k2 ? L"MWCOOP" : T(L"OFFICIEL", L"OFFICIAL"), r, 11.5f, FontStyleBold, sel ? kOnAcc : hot ? kAcc : kInk, StringAlignmentCenter);
                MscZone(r, 1006 + k2);
            }
            if (g_mscInstalled) MscButton(g, kMscReinst, T(L"RÉINSTALLER", L"REINSTALL"), 1008, IC_DOWNLOAD, g_mscInstall != 1);
        }
        if (!g_mscInstalled) {   // pas de liste : installer
            Para(g, T(L"MSCLoader, le chargeur de mods de la communauté (projet indépendant), n'est pas installé. Installe-le ici, puis place tes mods (version My Winter Car) dans son dossier Mods.",
                      L"MSCLoader, the community mod loader (independent project), is not installed. Install it here, then put your mods (My Winter Car version) in its Mods folder."),
                 RectF(c.X + 18, c.Y + 124, c.Width - 36, 60), 12.5f, kGrey);
            GraphicsPath bp; RoundRect(bp, kMscInstall, 14);
            float a = g_mscInstall == 1 ? 0.5f : 1.0f;
            SolidBrush lg(WithA(kAcc, a)); g.FillPath(&lg, &bp);
            if (g_mscHot == 1001) { SolidBrush hb(Color(55, 255, 255, 255)); g.FillPath(&hb, &bp); }
            Icon(g, IC_DOWNLOAD, kMscInstall.X + 28, kMscInstall.Y + 23, 18, kOnAcc, 2.2f);
            Text(g, g_mscInstall == 1 ? T(L"INSTALLATION…", L"INSTALLING…") : T(L"INSTALLER MSCLOADER", L"INSTALL MSCLOADER"), RectF(kMscInstall.X + 44, kMscInstall.Y, kMscInstall.Width - 52, kMscInstall.Height), 13.5f, FontStyleBold, kOnAcc, StringAlignmentNear);
            if (g_mscInstall != 1) MscZone(kMscInstall, 1001);
            Para(g, T(L"MSCLoader est un projet indépendant de MWCoop ; son support ici est expérimental. Rien n'est écrit dans le jeu avec la version fournie par MWCoop.",
                      L"MSCLoader is a project independent from MWCoop; its support here is experimental. Nothing is written into the game with the version provided by MWCoop."),
                 RectF(c.X + 18, c.Y + 230, c.Width - 36, 60), 12, kGrey);
        } else {
            // en-tete du tableau
            float hy = c.Y + 126;
            Text(g, T(L"MOD", L"MOD"), RectF(kMscColName, hy, 200, 16), 10.5f, FontStyleBold, kGrey, StringAlignmentNear);
            Text(g, T(L"VERSION", L"VERSION"), RectF(kMscColVer, hy, 90, 16), 10.5f, FontStyleBold, kGrey, StringAlignmentNear);
            Text(g, T(L"ENVOYÉ", L"SENT"), RectF(kMscColSend, hy, 130, 16), 10.5f, FontStyleBold, kGrey, StringAlignmentNear);
            Text(g, T(L"ACTIF", L"ON"), RectF(kMscColOn - 10, hy, 70, 16), 10.5f, FontStyleBold, kGrey, StringAlignmentCenter);
            float sc = g_scroll[TAB_MODS] = min(g_scroll[TAB_MODS], MscMaxScroll()), ms = MscMaxScroll();
            if (g_msc.empty())
                Para(g, T(L"Aucun mod dans le dossier Mods. Ajoute les DLL des mods MSCLoader (version My Winter Car) puis lance le jeu une fois : leurs noms et leurs options apparaîtront ici.",
                          L"No mod in the Mods folder. Add MSCLoader mods (My Winter Car version), then start the game once: their names and options will show up here."),
                     RectF(kMscList.X + 80, kMscList.Y + 60, kMscList.Width - 160, 80), 13, kGrey, StringAlignmentCenter);
            static const Color kIconHue[4] = { Color(255, 166, 216, 255), Color(255, 255, 210, 143), Color(255, 154, 240, 192), Color(255, 240, 166, 255) };
            int hidden = 0;
            GraphicsState st = g.Save();
            g.SetClip(kMscList, CombineModeIntersect);
            for (int i = 0; i < (int)g_msc.size(); i++) {
                const MscMod &m = g_msc[i];
                RectF r = MscRowRect(i);
                if (r.Y + r.Height < kMscList.Y || r.Y > kMscList.Y + kMscList.Height) { hidden++; continue; }
                bool vis = r.Y >= kMscList.Y - 1 && r.Y + r.Height <= kMscList.Y + kMscList.Height + 1;
                if (!vis) hidden++;
                bool hot = g_mscHot == 2000 + i;
                GraphicsPath rp; RoundRect(rp, r, 14);
                SolidBrush rb(hot ? TH(cardSel) : TH(card)); g.FillPath(&rb, &rp);
                Pen rpen(hot ? WithA(kAcc, 0.6f) : TH(choiceBorder), 1); g.DrawPath(&rpen, &rp);
                float a = m.disabled || m.off ? 0.5f : 1.0f;
                RectF ic(r.X + 8, r.Y + 8, 40, 40);
                GraphicsPath ip; RoundRect(ip, ic, 11);
                SolidBrush ib(WithA(kInk, 0.08f)); g.FillPath(&ib, &ip);
                Icon(g, IC_PUZZLE, ic.X + 20, ic.Y + 20, 18, g_dark ? WithA(kIconHue[i % 4], a) : WithA(kAcc, a), 1.9f);
                float nw = kMscColVer - 12 - kMscColName;
                Text(g, m.name, RectF(kMscColName, r.Y + 8, nw, 20), 13.5f, FontStyleBold, WithA(kInk, a), StringAlignmentNear);
                std::wstring line = m.off ? m.file + T(L".off · désactivé : ni chargé ni envoyé", L".off · off: neither loaded nor sent")
                                  : m.known ? (m.author.empty() ? m.file : m.author + L" · " + m.file) + (m.disabled ? T(L" · désactivé dans MSCLoader", L" · disabled in MSCLoader") : L"")
                                            : m.file + T(L" · lance le jeu une fois pour les détails", L" · start the game once for details");
                Text(g, line, RectF(kMscColName, r.Y + 29, nw, 18), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
                Text(g, m.known && !m.ver.empty() ? m.ver : L"—", RectF(kMscColVer, r.Y, 90, r.Height), 12.5f, FontStyleRegular, kGrey, StringAlignmentNear);
                // Envoye : puce cliquable (Oui · taille / Non · perso)
                RectF sr = MscSendRect(r);
                bool sent = MscSent(m), shHot = g_mscHot == 4000 + i;
                std::wstring sl = m.off ? T(L"Non · coupé", L"No · off") : MenuSkipper(m) ? T(L"Jamais", L"Never")
                                : sent ? std::wstring(T(L"Oui", L"Yes")) + (m.bytes ? L" · " + SizeText(m.bytes) : L"") : T(L"Non · perso", L"No · private");
                float slw = min(MscChipW(sl), sr.Width);
                RectF cr(sr.X, sr.Y, slw, sr.Height);
                Color ck = sent ? kOk : kGrey;
                MscChip(g, cr, sl, sent ? WithA(kOk, 0.18f) : WithA(kInk, 0.06f), WithA(ck, m.off ? 0.6f : 1.0f), shHot ? kAcc : sent ? WithA(kOk, 0.35f) : TH(choiceBorder), 11.5f);
                RectF ar = MscActiveRect(r);
                bool acHot = g_mscHot == 6000 + i;
                DrawToggle(g, ar, !m.off, acHot);
                if (m.known) {
                    RectF gr = MscGearRect(r);
                    GraphicsPath gp; RoundRect(gp, gr, 10);
                    SolidBrush gb(hot ? TH(btn2Hot) : TH(btn2)); g.FillPath(&gb, &gp);
                    DrawGear(g, gr.X + 18, gr.Y + 18, hot ? kAcc : kInk);
                }
                if (!vis) continue;
                if (m.known) MscZone(r, 2000 + i);
                MscZone(RectF(sr.X - 6, r.Y + 4, sr.Width + 6, r.Height - 8), 4000 + i);
                MscZone(RectF(ar.X - 6, r.Y + 4, ar.Width + 12, r.Height - 8), 6000 + i);
            }
            g.Restore(st);
            DrawScrollBar(g, kMscList, sc, ms);
            if (hidden > 0) { wchar_t hb[64]; swprintf_s(hb, T(L"+ %d autre(s) · défilement", L"+ %d more · scroll"), hidden); Text(g, hb, RectF(c.X + 18, c.Y + 568, c.Width - 36, 18), 12, FontStyleRegular, kGrey, StringAlignmentNear); }
            // bas : dossier, tout envoyer, total envoye
            MscButton(g, kMscOpenDir, T(L"Ouvrir le dossier Mods", L"Open the Mods folder"), 1004, IC_FOLDER);
            MscButton(g, kMscSendAll, T(L"Tout envoyer", L"Send all"), 1010, -1, !g_msc.empty());
            unsigned long long tot = 0; int n = 0;
            for (const MscMod &m : g_msc) if (MscSent(m)) { tot += m.bytes; n++; }
            wchar_t tb[96];
            swprintf_s(tb, T(L"Envoyé aux invités : %d mod(s) · %s", L"Sent to guests: %d mod(s) · %s"), n, SizeText(tot).c_str());
            Text(g, tb, RectF(kMscSendAll.X + kMscSendAll.Width + 10, kMscOpenDir.Y, c.X + c.Width - 18 - (kMscSendAll.X + kMscSendAll.Width + 10), 36), 12, FontStyleRegular, kGrey, StringAlignmentFar);
        }
    }
    // ---- a droite : recus de l'hote
    {
        RectF c = kMscRecvCard;
        Title(g, T(L"Reçus de l'hôte", L"Received from the host"), RectF(c.X + 18, c.Y + 14, c.Width - 36, 26), 16, kInk);
        std::vector<MscRecv> recv = g_mscRecv;
        if (g_mscFake && recv.empty()) recv = { { L"Better Headlights", 1258291 }, { L"Trunk Light", 204800 } };   // (captures)
        std::wstring sub;
        if (recv.empty()) sub = T(L"Aucun pour l'instant : ils arrivent en rejoignant un salon", L"None yet: they arrive when you join a lobby");
        else {
            wchar_t b[160];
            FILETIME lt; SYSTEMTIME t = {};
            if (g_mscRecvT.dwHighDateTime) { FileTimeToLocalFileTime(&g_mscRecvT, &lt); FileTimeToSystemTime(&lt, &t); }
            wchar_t dt[16]; swprintf_s(dt, L"%02d/%02d", g_fr ? t.wDay : t.wMonth, g_fr ? t.wMonth : t.wDay);
            if (t.wYear) swprintf_s(b, T(L"Dernier salon le %s · %d mod(s) · gardés pour la prochaine fois", L"Last lobby on %s · %d mod(s) · kept for next time"), dt, (int)recv.size());
            else swprintf_s(b, T(L"%d mod(s) · gardés pour la prochaine fois", L"%d mod(s) · kept for next time"), (int)recv.size());
            sub = b;
        }
        Text(g, sub, RectF(c.X + 18, c.Y + 42, c.Width - 36, 18), 12, FontStyleRegular, kGrey, StringAlignmentNear);
        float y = c.Y + 66;
        for (int i = 0; i < (int)recv.size() && i < 4; i++, y += 28) {
            Text(g, recv[i].name, RectF(c.X + 18, y, c.Width - 36 - 70, 24), 13, FontStyleBold, kInk, StringAlignmentNear);
            std::wstring sz = recv[i].bytes ? SizeText(recv[i].bytes) : L"ok";
            float w = MscChipW(sz, 10.5f);
            MscChip(g, RectF(c.X + c.Width - 18 - w, y + 1, w, 22), sz, WithA(kOk, 0.18f), kOk, WithA(kOk, 0.35f), 10.5f);
        }
        if (recv.size() > 4) { wchar_t more[48]; swprintf_s(more, T(L"+ %d autre(s)", L"+ %d more"), (int)recv.size() - 4); Text(g, more, RectF(c.X + 18, y, c.Width - 36, 18), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear); }
        if (const Opt *ma = OptByKey("ModsAuto")) {   // accepter les fichiers de l'hote (ModsAuto)
            bool hot = g_mscHot == 1005;
            float ty = kMscAuto.Y - 6;
            Text(g, T(L"Accepter les fichiers de l'hôte", L"Accept the host's files"), RectF(c.X + 18, ty, c.Width - 36 - 60, 20), 13, FontStyleBold, hot ? kAcc : kInk, StringAlignmentNear);
            Text(g, T(L"Sans redemander à chaque salon", L"Without asking again in each lobby"), RectF(c.X + 18, ty + 20, c.Width - 36 - 60, 18), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
            DrawToggle(g, kMscAuto, OptGet(*ma) != 0, hot);
            MscZone(RectF(c.X + 18, ty - 4, c.Width - 36, 46), 1005);
        }
        // TODO : vider mods-invite (copie des mods de l'hote) -- pas d'action encore : bouton gris + « Bientot »
        const wchar_t *soon = T(L"Bientôt", L"Soon");
        float sw = MscChipW(soon, 10);
        MscButton(g, kMscClearRecv, T(L"Vider les mods reçus", L"Clear received mods"), 1011, -1, false, false, sw + 8);
        MscChip(g, RectF(kMscClearRecv.X + kMscClearRecv.Width - sw - 8, kMscClearRecv.Y + 8, sw, 20), soon, WithA(kWarn, 0.2f), kWarn, WithA(kWarn, 0.4f), 10);
    }
    // ---- a droite : bon a savoir
    {
        RectF c = kMscInfoCard;
        Title(g, T(L"Bon à savoir", L"Good to know"), RectF(c.X + 18, c.Y + 14, c.Width - 36, 26), 16, kInk);
        struct { const wchar_t *t, *s; float h; } notes[2] = {
            { T(L"Le salon attend les téléchargements", L"The lobby waits for downloads"),
              T(L"Tant qu'un invité reçoit tes mods, LANCER reste gris. Un joueur qui arrive en cours de partie les reçoit avant d'entrer.",
                L"While a guest is receiving your mods, START stays grey. A player who arrives mid-game gets them before joining."), 100 },
            { T(L"Mods non pris en charge", L"Unsupported mods"),
              T(L"MWCoop ne garantit pas les mods tiers : ils fonctionnent chez toi comme en solo, mais leurs objets ne sont pas forcément vus par les autres.",
                L"MWCoop does not guarantee third-party mods: they work for you as in solo, but their objects are not necessarily seen by the others."), 116 } };
        float y = c.Y + 50;
        for (auto &n : notes) {
            RectF r(c.X + 18, y, c.Width - 36, n.h);
            MscInnerCard(g, r);
            Text(g, n.t, RectF(r.X + 12, r.Y + 10, r.Width - 24, 20), 13, FontStyleBold, kInk, StringAlignmentNear);
            Para(g, n.s, RectF(r.X + 12, r.Y + 32, r.Width - 24, r.Height - 38), 12, kGrey);
            y += n.h + 8;
        }
        bool hot = g_mscHot == 1009;
        Text(g, T(L"Guide : jouer avec des mods →", L"Guide: playing with mods →"), kMscCompat, 12.5f, hot ? FontStyleBold | FontStyleUnderline : FontStyleBold, kAcc, StringAlignmentNear);
        MscZone(kMscCompat, 1009);
    }
}
