// ---------------------------------------------------------------- page MEDIA, onglet Bobines (JD, 07/11 : « il faudrait prevoir
// un onglet pour gerer les bobines de cinema dans le launcher » puis « pouvoir tout gerer de la » ; specification de [Addons]).
// Les films d'une partie du lanceur : <donnees>\videot (principale), profils\partie-N\videot, profils\invite\videot (le mod
// ecrit dans MWCOOP_DATA : loader/proxy.cpp). Un film = <film>.mwv (MWV1) ; <film>.mwv.osa = reception en cours ou coupee ;
// export-*.mwv = temporaire d'un export ; recus.txt = films recus d'un autre joueur ; titres.txt = « film<TAB>titre » ecrit
// ici (import, renommage) et lu par le mod. Titre et usage lus dans les fichiers du monde de la partie (MWCoop-esineet.txt,
// MWCoop-kino.txt) : LECTURE SEULE, le lanceur n'y ecrit jamais. Jeu ouvert : rien n'est importe, renomme ni supprime.
// Duree et premiere image lues dans un fil a part (positions sur 64 bits : films de plusieurs Go), gardees par chemin,
// taille et date. Import et export par MWCoopRT.dll (LoadLibrary : jamais de .lib), comme l'onglet VHS.

// (identifiants UI_BOB_* : media.inc, avec l'onglet de l'en-tete)
static const int kBobRowsMax = (UI_BOB_END - UI_BOB_ROW) / 2;

enum { BU_NONE, BU_SALLE, BU_KELA, BU_VHS, BU_VCR, BU_CAM };   // ou sert un film
struct BobFilm {
    std::wstring id, path;            // path vide : seulement le .osa (reception)
    unsigned long long bytes = 0;     // .mwv et .osa
    ULONGLONG mt = 0;
    bool partial = false, recu = false;
    int use = BU_NONE; std::wstring room, title; bool titled = false;   // titled : titre de titres.txt (pas de la sauvegarde)
};
struct BobSlot { std::wstring prof, name; bool guest = false; };
struct BobInfo { bool ok = false; float secs = 0; int w = 0, h = 0, fps = 0; std::vector<unsigned char> jpg; };

static std::vector<BobSlot> g_bobSlots;
static std::wstring g_bobProf; static bool g_bobProfSet;
static std::vector<BobFilm> g_bob;
static DWORD g_bobScanT;
static bool g_bobGameOn;
static int g_bobTestGame = -1;        // (essais : 1 jeu ouvert, 0 ferme ; -1 : le vrai processus)
static unsigned long long g_bobTotal, g_bobFree; static bool g_bobFreeOk;
static std::wstring g_bobSelId; static std::set<std::wstring> g_bobChk;
static int g_bobKind;                 // import : 0 bobine de cinema, 1 cassette VHS
static bool g_bobEdit; static std::wstring g_bobEditText, g_bobEditId;
static DWORD g_bobDelT, g_bobLowT;
static std::wstring g_bobImpId, g_bobImpName, g_bobImpTitle, g_bobImpDir, g_bobExpDst, g_bobExpName;
static float g_bobImpP = -1, g_bobExpP = -1;
static bool g_bobImpCancel;
typedef void (__cdecl *VidCancelFn)();
static VidCancelFn g_bobCancel;

// ---- chemins
static std::wstring BobDataRoot() { std::wstring r = SaveProfRoot(); return r.size() > 8 ? r.substr(0, r.size() - 8) : r; }   // (sans « profils\ »)
static std::wstring BobFilmDirOf(const std::wstring &prof) { return prof.empty() ? BobDataRoot() + L"videot\\" : SaveProfRoot() + prof + L"\\videot\\"; }
static bool BobNameOk(const std::wstring &n)
{
    if (n.empty() || n.size() > 120 || n.find(L"..") != std::wstring::npos) return false;
    for (wchar_t c : n) if (c < 32 || wcschr(L"\\/:*?\"<>|", c)) return false;
    return true;
}
static bool BobEnds(const std::wstring &s, const wchar_t *e) { size_t n = wcslen(e); return s.size() > n && !_wcsicmp(s.c_str() + s.size() - n, e); }
static std::wstring BobSize(unsigned long long b)
{
    if (b < 1073741824ull) return VhsSize(b);
    wchar_t s[32]; swprintf_s(s, T(L"%.1f Go", L"%.1f GB"), b / 1073741824.0);
    if (g_fr) for (wchar_t *p = s; *p; p++) if (*p == L'.') *p = L',';
    return s;
}
static std::vector<std::wstring> BobLines(const std::wstring &file)
{
    std::vector<std::wstring> out; std::vector<unsigned char> d;
    if (!ReadAll(file, d) || d.empty()) return out;
    size_t skip = d.size() >= 3 && d[0] == 0xEF && d[1] == 0xBB && d[2] == 0xBF ? 3 : 0;
    std::wstring all = Widen(std::string(d.begin() + skip, d.end()), CP_UTF8);
    for (size_t s = 0; s < all.size();) {
        size_t e = all.find(L'\n', s); if (e == std::wstring::npos) e = all.size();
        std::wstring l = all.substr(s, e - s);
        if (!l.empty() && l.back() == L'\r') l.pop_back();
        if (!l.empty()) out.push_back(l);
        s = e + 1;
    }
    return out;
}
static std::wstring BobCleanTitle(std::wstring s) { for (auto &c : s) if (c == L'\t') c = L' '; return VhsClean(s); }

// ---- titres.txt (« film<TAB>titre<TAB>duree en ms », UTF-8 ; la duree est facultative) : seul fichier ecrit a cote des
// films. Le mod y prend le titre et la duree (liste FILMS PRETS sans relire un film de plusieurs Go).
struct BobTitle { std::wstring title; long long ms = -1; };
static std::map<std::wstring, BobTitle> BobTitles(const std::wstring &dir)
{
    std::map<std::wstring, BobTitle> m;
    for (auto &l : BobLines(dir + L"titres.txt")) {
        size_t a = l.find(L'\t');
        if (a == std::wstring::npos || a == 0) continue;
        size_t b = l.find(L'\t', a + 1);
        BobTitle e; e.title = l.substr(a + 1, b == std::wstring::npos ? std::wstring::npos : b - a - 1);
        if (b != std::wstring::npos) { std::wstring d = Trim(l.substr(b + 1)); if (!d.empty() && iswdigit(d[0])) e.ms = _wtoi64(d.c_str()); }
        m[l.substr(0, a)] = e;
    }
    return m;
}
static std::wstring BobTitleLine(const std::wstring &id, const BobTitle &e) { return id + L"\t" + e.title + (e.ms >= 0 ? L"\t" + std::to_wstring(e.ms) : std::wstring()) + L"\r\n"; }
// ecrit les changements (titre et/ou duree) ; title NULL : titre garde ; ms < 0 : duree gardee. Les autres lignes restent.
static bool BobTitleWrite(const std::wstring &dir, const std::map<std::wstring, std::pair<const std::wstring *, long long>> &chg)
{
    for (auto &c : chg) if (!BobNameOk(c.first)) return false;
    std::vector<std::wstring> lines = BobLines(dir + L"titres.txt");
    std::wstring out; std::set<std::wstring> done;
    auto merged = [&](const std::wstring &id, BobTitle e) { auto c = chg.find(id); if (c->second.first) e.title = *c->second.first; if (c->second.second >= 0) e.ms = c->second.second; return e; };
    for (auto &l : lines) {
        size_t a = l.find(L'\t');
        std::wstring id = a == std::wstring::npos ? L"" : l.substr(0, a);
        if (a == std::wstring::npos || !chg.count(id)) { out += l + L"\r\n"; continue; }
        if (done.count(id)) continue;
        done.insert(id);
        size_t b = l.find(L'\t', a + 1);
        BobTitle e; e.title = l.substr(a + 1, b == std::wstring::npos ? std::wstring::npos : b - a - 1);
        if (b != std::wstring::npos) { std::wstring d = Trim(l.substr(b + 1)); if (!d.empty() && iswdigit(d[0])) e.ms = _wtoi64(d.c_str()); }
        e = merged(id, e);
        if (!e.title.empty() || e.ms >= 0) out += BobTitleLine(id, e);
    }
    for (auto &c : chg) if (!done.count(c.first)) { BobTitle e = merged(c.first, BobTitle()); if (!e.title.empty() || e.ms >= 0) out += BobTitleLine(c.first, e); }
    std::string u = Narrow(out, CP_UTF8);
    std::wstring f = dir + L"titres.txt", tmp = f + L".lanceur";
    SHCreateDirectoryExW(NULL, dir.c_str(), NULL);
    HANDLE h = CreateFileW(tmp.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (h == INVALID_HANDLE_VALUE) return false;
    DWORD w = 0; BOOL ok = u.empty() || WriteFile(h, u.data(), (DWORD)u.size(), &w, NULL); CloseHandle(h);
    if (!ok || w != u.size() || !MoveFileExW(tmp.c_str(), f.c_str(), MOVEFILE_REPLACE_EXISTING)) { DeleteFileW(tmp.c_str()); return false; }
    return true;
}
static bool BobTitleSet(const std::wstring &dir, const std::wstring &id, const std::wstring &title)
{
    std::map<std::wstring, std::pair<const std::wstring *, long long>> c; c[id] = std::make_pair(&title, -1LL);
    return BobTitleWrite(dir, c);
}

// ---- usage : bobines et cassettes de la partie (lecture seule des fichiers du monde)
struct BobUse { int use = BU_NONE; std::wstring room, title; };
static void BobAddContent(std::map<std::wstring, BobUse> &u, const std::wstring &content, int use, const std::wstring &room)
{
    if (!content.compare(0, 4, L"url:")) return;   // (bobine « lien » : pas de film)
    size_t tl = content.find(L'~');
    std::wstring films = tl == std::wstring::npos ? content : content.substr(0, tl), title = tl == std::wstring::npos ? L"" : content.substr(tl + 1);
    if (films == L"tyhja") return;
    for (size_t s = 0; s <= films.size();) {
        size_t e = films.find(L'+', s); if (e == std::wstring::npos) e = films.size();
        std::wstring f = Trim(films.substr(s, e - s));
        if (!f.empty() && !u.count(f)) { BobUse b; b.use = use; b.room = room; b.title = title; u[f] = b; }
        s = e + 1;
    }
}
static void BobUses(const std::wstring &prof, std::map<std::wstring, BobUse> &u)
{
    std::wstring d = SaveDirOf(prof);
    if (d.empty()) return;
    for (auto &l : BobLines(d + L"MWCoop-kino.txt")) {   // salle;contenu (bobine sur le projecteur)
        size_t s = l.find(L';');
        if (s != std::wstring::npos) BobAddContent(u, l.substr(s + 1), BU_SALLE, Trim(l.substr(0, s)));
    }
    for (auto &l : BobLines(d + L"MWCoop-esineet.txt")) {   // id;sorte;donnees;position;rotation
        size_t a = l.find(L';'), b = a == std::wstring::npos ? a : l.find(L';', a + 1), c = b == std::wstring::npos ? b : l.find(L';', b + 1);
        if (b == std::wstring::npos) continue;
        std::wstring kind = l.substr(a + 1, b - a - 1), data = l.substr(b + 1, c == std::wstring::npos ? std::wstring::npos : c - b - 1);
        int use = kind == L"kela" ? BU_KELA : kind == L"vhs" ? BU_VHS : kind == L"nauhuri" ? BU_VCR : kind == L"kamera" ? BU_CAM : BU_NONE;
        if (use == BU_NONE) continue;
        if (use == BU_VCR || use == BU_CAM) { size_t bar = data.find(L'|'); if (bar != std::wstring::npos) data.resize(bar); }
        BobAddContent(u, data, use, L"");
    }
}
static std::wstring BobWhere(const BobFilm &f)
{
    switch (f.use) {
    case BU_SALLE: return T(L"bobine, salle ", L"reel, room ") + f.room;
    case BU_KELA: return T(L"bobine (objet)", L"reel (item)");
    case BU_VHS: return T(L"cassette", L"tape");
    case BU_VCR: return T(L"dans le magnétoscope", L"in the VCR");
    case BU_CAM: return T(L"dans le caméscope", L"in the camcorder");
    }
    return T(L"sur aucune bobine", L"on no reel");
}
static std::wstring BobOrigin(const BobFilm &f)
{
    if (f.partial) return T(L"réception inachevée (effacée au prochain chargement de la partie)", L"unfinished download (deleted at the next game load)");
    if (f.recu) return f.use != BU_NONE ? T(L"reçu, gardé (bobine de la partie)", L"received, kept (reel of the game)") : T(L"reçu, effacé au prochain chargement de la partie", L"received, deleted at the next game load");
    return T(L"fait sur ce PC (filmé, monté ou importé)", L"made on this PC (recorded, edited or imported)");
}
static std::wstring BobTitleOf(const BobFilm &f) { return f.title.empty() ? T(L"sans titre", L"no title") : f.title; }

// ---- duree, taille d'image et premiere image : fil a part (positions sur 64 bits), gardees par chemin + taille + date
static CRITICAL_SECTION g_bobCs; static bool g_bobCsOn;
static std::map<std::wstring, BobInfo> g_bobInfo;
static std::vector<std::pair<std::wstring, std::wstring>> g_bobQueue;   // (cle, chemin)
static bool g_bobRun;
static std::map<std::wstring, Bitmap *> g_bobThumb;
static void BobLock() { if (!g_bobCsOn) { InitializeCriticalSection(&g_bobCs); g_bobCsOn = true; } EnterCriticalSection(&g_bobCs); }
static void BobUnlock() { LeaveCriticalSection(&g_bobCs); }
static std::wstring BobKey(const BobFilm &f) { return f.path + L"|" + std::to_wstring(f.bytes) + L"|" + std::to_wstring(f.mt); }
static bool BobReadInfo(const std::wstring &path, BobInfo &o)
{
    HANDLE f = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, NULL, OPEN_EXISTING, FILE_FLAG_SEQUENTIAL_SCAN, NULL);
    if (f == INVALID_HANDLE_VALUE) return false;
    unsigned char hd[11]; DWORD got = 0;
    bool ok = ReadFile(f, hd, 11, &got, NULL) && got == 11 && !memcmp(hd, "MWV1", 4);
    LARGE_INTEGER sz = {}; GetFileSizeEx(f, &sz);
    if (ok) {
        o.w = hd[4] | hd[5] << 8; o.h = hd[6] | hd[7] << 8; o.fps = hd[8];
        int rate = hd[9] | hd[10] << 8; if (rate <= 0) rate = 11025;
        unsigned long long pos = 11, size = (unsigned long long)sz.QuadPart, snd = 0; uint32_t lastMs = 0;
        while (pos + 9 <= size) {
            unsigned char b[9];
            if (!ReadFile(f, b, 9, &got, NULL) || got != 9) break;
            uint32_t ms; int32_t n; memcpy(&ms, b + 1, 4); memcpy(&n, b + 5, 4);
            pos += 9;
            if (n < 0 || pos + (unsigned long long)n > size) break;
            if (ms > lastMs) lastMs = ms;
            if (b[0] == 2) snd += (unsigned long long)n;
            if (b[0] == 1 && o.jpg.empty() && n > 0 && n <= 8 * 1048576) {
                o.jpg.resize(n);
                if (!ReadFile(f, o.jpg.data(), (DWORD)n, &got, NULL) || got != (DWORD)n) { o.jpg.clear(); break; }
            } else if (n > 0) {
                LARGE_INTEGER li; li.QuadPart = n;
                if (!SetFilePointerEx(f, li, NULL, FILE_CURRENT)) break;
            }
            pos += (unsigned long long)n;
        }
        o.secs = (float)max(lastMs / 1000.0, (double)snd / rate);
    }
    CloseHandle(f);
    o.ok = ok;
    return ok;
}
static DWORD WINAPI BobInfoThread(LPVOID)
{
    for (;;) {
        BobLock();
        if (g_bobQueue.empty()) { g_bobRun = false; BobUnlock(); return 0; }
        std::pair<std::wstring, std::wstring> job = g_bobQueue.front(); g_bobQueue.erase(g_bobQueue.begin());
        BobUnlock();
        BobInfo i; BobReadInfo(job.second, i);
        BobLock(); g_bobInfo[job.first] = i; BobUnlock();
    }
}
static void BobQueueInfo(const BobFilm &f)
{
    if (f.path.empty()) return;
    std::wstring k = BobKey(f);
    BobLock();
    bool known = g_bobInfo.count(k) != 0;
    for (auto &q : g_bobQueue) if (q.first == k) known = true;
    if (!known) {
        g_bobQueue.push_back({ k, f.path });
        if (!g_bobRun) { HANDLE h = CreateThread(NULL, 0, BobInfoThread, NULL, 0, NULL); if (h) { g_bobRun = true; CloseHandle(h); } else g_bobQueue.pop_back(); }
    }
    BobUnlock();
}
static bool BobGetInfo(const BobFilm &f, BobInfo &out, bool withJpg = false)
{
    BobLock();
    auto it = g_bobInfo.find(BobKey(f));
    bool has = it != g_bobInfo.end();
    if (has) { out.ok = it->second.ok; out.secs = it->second.secs; out.w = it->second.w; out.h = it->second.h; out.fps = it->second.fps; if (withJpg) out.jpg = it->second.jpg; }
    BobUnlock();
    return has;
}
static Bitmap *BobThumbOf(const BobFilm &f)
{
    std::wstring k = BobKey(f);
    auto it = g_bobThumb.find(k);
    if (it != g_bobThumb.end()) return it->second;
    BobInfo i;
    if (!BobGetInfo(f, i, true)) return NULL;   // (pas encore lu)
    Bitmap *copy = NULL;
    if (!i.jpg.empty()) {
        IStream *s = SHCreateMemStream(i.jpg.data(), (UINT)i.jpg.size());
        if (s) {
            Bitmap *src = Bitmap::FromStream(s);
            if (src && src->GetLastStatus() == Ok && src->GetWidth() > 0) {
                float sc = min(1.0f, 400.0f / src->GetWidth());
                int w = max(1, (int)(src->GetWidth() * sc)), h = max(1, (int)(src->GetHeight() * sc));
                copy = new Bitmap(w, h, PixelFormat32bppPARGB);
                Graphics cg(copy); cg.SetInterpolationMode(InterpolationModeHighQualityBicubic); cg.DrawImage(src, 0, 0, w, h);
            }
            delete src; s->Release();
        }
    }
    g_bobThumb[k] = copy;
    return copy;
}

// ---- releve
static void BobSlotsScan()
{
    SavesScan();
    std::set<std::wstring> guests;
    for (auto &l : BobLines(SaveProfRoot() + L"invite\\parties.ini")) { size_t e = l.find(L'='); if (e != std::wstring::npos) guests.insert(Trim(l.substr(e + 1))); }
    g_bobSlots.clear();
    for (auto &s : g_saves) { BobSlot b; b.prof = s.prof; b.name = s.name; b.guest = guests.count(s.prof) != 0; g_bobSlots.push_back(b); }
    DWORD a = g_gameDir.empty() ? INVALID_FILE_ATTRIBUTES : GetFileAttributesW((SaveProfRoot() + L"invite").c_str());
    if (a != INVALID_FILE_ATTRIBUTES && (a & FILE_ATTRIBUTE_DIRECTORY)) { BobSlot b; b.prof = L"invite"; b.name = T(L"Partie invité", L"Guest game"); g_bobSlots.push_back(b); }
    if (!g_bobProfSet) { g_bobProf = SaveCurrentProf(); g_bobProfSet = true; }
    bool found = false;
    for (auto &s : g_bobSlots) if (s.prof == g_bobProf) found = true;
    if (!found) g_bobProf = g_bobSlots.empty() ? L"" : g_bobSlots[0].prof;
}
static int BobSlotIndex() { for (int i = 0; i < (int)g_bobSlots.size(); i++) if (g_bobSlots[i].prof == g_bobProf) return i; return -1; }
static bool BobGameOn() { return g_bobTestGame >= 0 ? g_bobTestGame != 0 : GameProcessRunning(); }
static void BobScan()
{
    g_bobScanT = GetTickCount();
    g_bobGameOn = BobGameOn();
    if (g_bobEdit) return;   // (renommage en cours : la liste ne bouge pas)
    std::wstring dir = BobFilmDirOf(g_bobProf);
    std::map<std::wstring, BobUse> uses; BobUses(g_bobProf, uses);
    std::set<std::wstring> recus; for (auto &l : BobLines(dir + L"recus.txt")) recus.insert(Trim(l));
    std::map<std::wstring, BobTitle> titles = BobTitles(dir);
    std::vector<BobFilm> out; std::map<std::wstring, size_t> at;
    unsigned long long total = 0;
    WIN32_FIND_DATAW fd;
    HANDLE h = g_gameDir.empty() ? INVALID_HANDLE_VALUE : FindFirstFileW((dir + L"*").c_str(), &fd);
    if (h != INVALID_HANDLE_VALUE) {
        do {
            if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) continue;
            std::wstring name = fd.cFileName, id; bool part = false;
            if (BobEnds(name, L".mwv.osa")) { id = name.substr(0, name.size() - 8); part = true; }
            else if (BobEnds(name, L".mwv")) id = name.substr(0, name.size() - 4);
            else continue;
            if (!_wcsnicmp(id.c_str(), L"export-", 7) || !BobNameOk(id)) continue;   // (export en cours : temporaire)
            unsigned long long b = ((unsigned long long)fd.nFileSizeHigh << 32) | fd.nFileSizeLow;
            total += b;
            auto it = at.find(id);
            if (it == at.end()) { BobFilm f; f.id = id; out.push_back(f); at[id] = out.size() - 1; it = at.find(id); }
            BobFilm &f = out[it->second];
            f.bytes += b;
            ULONGLONG t = ((ULONGLONG)fd.ftLastWriteTime.dwHighDateTime << 32) | fd.ftLastWriteTime.dwLowDateTime;
            if (part) f.partial = true;
            else { f.path = dir + name; f.mt = t; }
            if (!f.mt) f.mt = t;
        } while (FindNextFileW(h, &fd));
        FindClose(h);
    }
    for (auto &f : out) {
        f.recu = recus.count(f.id) != 0;
        auto u = uses.find(f.id);
        if (u != uses.end()) { f.use = u->second.use; f.room = u->second.room; f.title = u->second.title; }
        if (f.title.empty()) { auto t = titles.find(f.id); if (t != titles.end() && !t->second.title.empty()) { f.title = t->second.title; f.titled = true; } }
        if (!f.partial) BobQueueInfo(f);
    }
    // duree lue (fil) et pas encore dans titres.txt : ecrite pour la liste FILMS PRETS du mod -- jeu ferme seulement
    if (!g_bobGameOn && !g_gameDir.empty()) {
        std::map<std::wstring, std::pair<const std::wstring *, long long>> chg;
        for (auto &f : out) {
            BobInfo i;
            if (f.partial || !BobGetInfo(f, i) || !i.ok) continue;
            long long ms = (long long)(i.secs * 1000.0 + 0.5);
            auto t = titles.find(f.id);
            if (t == titles.end() || t->second.ms < 0 || llabs(t->second.ms - ms) > 1000) chg[f.id] = std::make_pair((const std::wstring *)NULL, ms);
        }
        if (!chg.empty()) BobTitleWrite(dir, chg);
    }
    std::sort(out.begin(), out.end(), [](const BobFilm &a, const BobFilm &b) { return a.mt != b.mt ? a.mt > b.mt : a.id < b.id; });
    g_bob = out; g_bobTotal = total;
    std::set<std::wstring> keep;
    for (auto &f : g_bob) if (g_bobChk.count(f.id)) keep.insert(f.id);
    g_bobChk = keep;
    ULARGE_INTEGER fr;
    std::wstring probe = GetFileAttributesW(dir.c_str()) != INVALID_FILE_ATTRIBUTES ? dir : BobDataRoot();
    g_bobFreeOk = !g_gameDir.empty() && GetDiskFreeSpaceExW(probe.c_str(), &fr, NULL, NULL);
    g_bobFree = g_bobFreeOk ? fr.QuadPart : 0;
}
static int BobSelIndex() { for (int i = 0; i < (int)g_bob.size(); i++) if (g_bob[i].id == g_bobSelId) return i; return -1; }
static void BobEnter() { g_bobEdit = false; BobSlotsScan(); BobScan(); }
static bool BobGuard(const wchar_t *msg)
{
    if (g_bobTestGame >= 0) return g_bobTestGame == 0;
    if (GameOpenRefuse(K_WARN, msg)) { g_bobGameOn = true; return false; }
    return true;
}
static bool BobBusy() { return !g_bobImpId.empty() || !g_bobExpDst.empty() || !g_vhsImpFilm.empty() || !g_vhsExpDst.empty(); }

// ---- supprimer (les films coches, sinon celui choisi) : .mwv et .osa, a la Corbeille ; deux clics
static std::vector<int> BobTargets()
{
    std::vector<int> v;
    for (int i = 0; i < (int)g_bob.size(); i++) if (g_bobChk.count(g_bob[i].id)) v.push_back(i);
    if (v.empty() && BobSelIndex() >= 0) v.push_back(BobSelIndex());
    return v;
}
static std::wstring BobDeleteWarn(const std::vector<int> &v)
{
    for (int i : v) {
        const BobFilm &f = g_bob[i];
        wchar_t b[256];
        if (f.use == BU_SALLE) { swprintf_s(b, T(L"La bobine « %s » (salle %s) deviendra vide.", L"The reel \"%s\" (room %s) will become blank."), BobTitleOf(f).c_str(), f.room.c_str()); return b; }
        if (f.use == BU_KELA) { swprintf_s(b, T(L"La bobine « %s » deviendra vide.", L"The reel \"%s\" will become blank."), BobTitleOf(f).c_str()); return b; }
        if (f.use != BU_NONE) { swprintf_s(b, T(L"La cassette « %s » perdra ce film.", L"The tape \"%s\" will lose this film."), BobTitleOf(f).c_str()); return b; }
    }
    return L"";
}
static int BobDeleteIds(const std::vector<std::wstring> &ids, bool toBin)   // (essais : toBin faux, effacement direct dans le dossier jetable)
{
    std::wstring dir = BobFilmDirOf(g_bobProf);
    std::vector<std::wstring> paths; int n = 0;
    for (auto &id : ids) {
        if (!BobNameOk(id)) continue;
        bool any = false;
        for (const wchar_t *ext : { L".mwv", L".mwv.osa" }) {
            std::wstring p = dir + id + ext;
            if (!FileExists(p)) continue;
            any = true;
            if (toBin) paths.push_back(p); else DeleteFileW(p.c_str());
        }
        if (any) n++;
    }
    if (toBin && !paths.empty() && !ToRecycleBin(paths)) return -1;
    return n;
}
static void BobDelete()
{
    std::vector<int> v = BobTargets();
    if (v.empty()) return;
    if (!BobGuard(T(L"Ferme le jeu d'abord : il lit et efface ces films pendant la partie", L"Close the game first: it reads and deletes these films while playing"))) return;
    if (!g_bobDelT || GetTickCount() - g_bobDelT > 6000) {
        g_bobDelT = GetTickCount();
        unsigned long long b = 0; for (int i : v) b += g_bob[i].bytes;
        std::wstring w = BobDeleteWarn(v);
        if (v.size() == 1) SetStatus(K_WARN, T(L"Supprimer « %s » (%s) ? Clique encore sur Confirmer. %s", L"Delete \"%s\" (%s)? Click Confirm again. %s"), BobTitleOf(g_bob[v[0]]).c_str(), BobSize(b).c_str(), w.c_str());
        else SetStatus(K_WARN, T(L"Supprimer %d films (%s) ? Clique encore sur Confirmer. %s", L"Delete %d films (%s)? Click Confirm again. %s"), (int)v.size(), BobSize(b).c_str(), w.c_str());
        return;
    }
    g_bobDelT = 0;
    std::vector<std::wstring> ids; for (int i : v) ids.push_back(g_bob[i].id);
    int n = BobDeleteIds(ids, true);
    LaunchLog("bobines : %d film(s) a la corbeille (%s)", n, Narrow(g_bobProf.empty() ? L"principale" : g_bobProf, CP_UTF8).c_str());
    if (n < 0) SetStatus(K_ERR, T(L"Films pas supprimés", L"Films not deleted"));
    else SetStatus(K_OK, T(L"%d film(s) mis à la Corbeille", L"%d film(s) moved to the Recycle Bin"), n);
    g_bobChk.clear(); g_bobSelId.clear();
    BobScan();
}

// ---- import (MWCoopRT.dll : VidImportStart / Poll / Cancel) et export (VidExportStart)
static void BobImport(const wchar_t *given = NULL)   // (given : essais, sans la fenetre de choix)
{
    if (BobBusy()) { SetStatus(K_WARN, T(L"Une conversion ou un export est déjà en cours", L"A conversion or an export is already running")); return; }
    if (!BobGuard(T(L"Ferme le jeu pour importer une vidéo (il écrit dans le dossier des films)", L"Close the game to import a video (it writes in the films folder)"))) return;
    if (!given && g_bobFreeOk && g_bobFree < 1073741824ull && (!g_bobLowT || GetTickCount() - g_bobLowT > 6000)) {
        g_bobLowT = GetTickCount();
        SetStatus(K_WARN, T(L"Moins de 1 Go libre sur le disque (%s) : clique encore pour importer quand même", L"Less than 1 GB free on the disk (%s): click again to import anyway"), BobSize(g_bobFree).c_str());
        return;
    }
    g_bobLowT = 0;
    wchar_t file[MAX_PATH] = L"";
    if (given) wcsncpy_s(file, given, _TRUNCATE);
    OPENFILENAMEW of = { sizeof(of) };
    of.hwndOwner = g_wnd;
    of.lpstrFilter = L"Vidéos\0*.mp4;*.m4v;*.mov;*.wmv;*.avi;*.mkv;*.webm;*.mpg;*.mpeg;*.3gp\0*.*\0*.*\0";
    of.lpstrFile = file; of.nMaxFile = MAX_PATH;
    of.lpstrTitle = g_bobKind ? T(L"Choisir la vidéo pour une cassette VHS", L"Choose the video for a VHS tape") : T(L"Choisir la vidéo pour une bobine de cinéma", L"Choose the video for a cinema reel");
    of.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_HIDEREADONLY;
    if (!given && !GetOpenFileNameW(&of)) return;
    VhsImportFree();
    g_vidLib = LoadLibraryW((g_modRoot + L"MWCoop\\MWCoopRT.dll").c_str());
    VidStartFn start = g_vidLib ? (VidStartFn)GetProcAddress(g_vidLib, "VidImportStart") : NULL;
    g_vidPoll = g_vidLib ? (VidPollFn)GetProcAddress(g_vidLib, "VidImportPoll") : NULL;
    g_bobCancel = g_vidLib ? (VidCancelFn)GetProcAddress(g_vidLib, "VidImportCancel") : NULL;
    if (!start || !g_vidPoll) { VhsImportFree(); g_bobCancel = NULL; SetStatus(K_ERR, T(L"Conversion impossible : MWCoopRT.dll manque ou est trop ancienne (Windows 7 : pas de conversion)", L"Can't convert: MWCoopRT.dll missing or too old (no conversion on Windows 7)")); return; }
    std::wstring ini = ModIni();
    auto num = [&](const wchar_t *sec, const wchar_t *key, double def) { wchar_t b[32] = L""; GetPrivateProfileStringW(sec, key, L"", b, 32, ini.c_str()); return b[0] ? _wtof(b) : def; };
    int w, h, fps, q, secs;
    if (g_bobKind == 0) {   // bobine : [Kino], sans effet VHS (+1000), sans limite si ImportMinutes vaut 0
        w = max(64, min(960, (int)num(L"Kino", L"ImportLargeur", 480))); h = max(48, min(540, (int)num(L"Kino", L"ImportHauteur", 270)));
        fps = max(1, min(30, (int)num(L"Kino", L"ImportImages", 18))); q = max(10, min(95, (int)num(L"Kino", L"ImportQualite", 60))) + 1000;
        double mins = num(L"Kino", L"ImportMinutes", 0);
        secs = mins > 0 ? (int)min(2147483647.0, mins * 60) : 2147483647;
    } else {                // cassette : [Boutique], effet VHS
        w = max(64, min(640, (int)num(L"Boutique", L"ImportLargeur", 320))); h = max(48, min(480, (int)num(L"Boutique", L"ImportHauteur", 240)));
        fps = max(1, min(25, (int)num(L"Boutique", L"ImportImages", 12))); q = max(10, min(95, (int)num(L"Boutique", L"ImportQualite", 50)));
        secs = (int)(max(0.2, min(120.0, num(L"Boutique", L"ImportMinutes", 20))) * 60);
    }
    FILETIME ft; GetSystemTimeAsFileTime(&ft);
    wchar_t id[64]; swprintf_s(id, L"fl-%016llx", ((unsigned long long)ft.dwHighDateTime << 32) | ft.dwLowDateTime);
    std::wstring dir = BobFilmDirOf(g_bobProf);
    SHCreateDirectoryExW(NULL, dir.c_str(), NULL);
    std::wstring dst = dir + id + L".mwv";
    if (start(file, dst.c_str(), w, h, fps, secs, q) != 0) { VhsImportFree(); g_bobCancel = NULL; SetStatus(K_ERR, T(L"Conversion pas lancée", L"Conversion not started")); return; }
    const wchar_t *base = wcsrchr(file, L'\\'); g_bobImpName = base ? base + 1 : file;
    std::wstring t = g_bobImpName; size_t dot = t.rfind(L'.'); if (dot != std::wstring::npos) t.resize(dot);
    g_bobImpTitle = BobCleanTitle(t); g_bobImpId = id; g_bobImpDir = dir; g_bobImpP = 0; g_bobImpCancel = false; g_progress = 0;
    LaunchLog("bobines : import %s -> %s (%dx%d, %d i/s, %s)", Narrow(g_bobImpName, CP_UTF8).c_str(), Narrow(dst, CP_UTF8).c_str(), w, h, fps, g_bobKind ? "cassette" : "bobine");
    SetStatus(K_NORMAL, T(L"Conversion de %s…", L"Converting %s…"), g_bobImpName.c_str());
}
static void BobExport()
{
    int i = BobSelIndex();
    if (i < 0 || g_bob[i].path.empty() || g_bob[i].partial) return;
    if (BobBusy()) { SetStatus(K_WARN, T(L"Une conversion ou un export est déjà en cours", L"A conversion or an export is already running")); return; }
    const BobFilm &f = g_bob[i];
    std::wstring dir = VhsVideoDir();
    SHCreateDirectoryExW(NULL, dir.c_str(), NULL);
    SYSTEMTIME st; GetLocalTime(&st);
    wchar_t stamp[64]; swprintf_s(stamp, L"%04d-%02d-%02d_%02d-%02d-%02d", st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond);
    std::wstring name = f.title.empty() ? f.id : f.title;
    for (wchar_t &c : name) if (wcschr(L"\\/:*?\"<>|", c) || c < 32) c = L'_';
    std::wstring dst = dir + name + L"_" + stamp + L".mp4";
    VhsImportFree();
    g_vidLib = LoadLibraryW((g_modRoot + L"MWCoop\\MWCoopRT.dll").c_str());
    VidExportFn ex = g_vidLib ? (VidExportFn)GetProcAddress(g_vidLib, "VidExportStart") : NULL;
    g_vidPoll = g_vidLib ? (VidPollFn)GetProcAddress(g_vidLib, "VidImportPoll") : NULL;
    if (!ex || !g_vidPoll) { VhsImportFree(); SetStatus(K_ERR, T(L"Export impossible : MWCoopRT.dll manque ou est trop ancienne (Windows 7 : pas d'export)", L"Can't export: MWCoopRT.dll missing or too old (no export on Windows 7)")); return; }
    std::wstring ini = ModIni();
    int scale = max(1, min(6, (int)GetPrivateProfileIntW(L"Boutique", L"ExportEchelle", 3, ini.c_str())));
    std::wstring lg = BobFilmDirOf(g_bobProf) + L"logo-mwcoop.png";   // (le mod l'ecrit dans le videot de la partie ; sinon celui de la partie utilisee)
    if (!FileExists(lg)) lg = VhsFilmDir() + L"logo-mwcoop.png";
    std::wstring logo = GetPrivateProfileIntW(L"Boutique", L"ExportLogo", 1, ini.c_str()) != 0 && FileExists(lg) ? lg : L"";
    if (ex(f.path.c_str(), dst.c_str(), scale, logo.c_str()) != 0) { VhsImportFree(); SetStatus(K_ERR, T(L"Export pas lancé", L"Export not started")); return; }
    g_bobExpDst = dst; g_bobExpName = BobTitleOf(f); g_bobExpP = 0; g_progress = 0;
    SetStatus(K_NORMAL, T(L"Export de « %s » en .mp4…", L"Exporting \"%s\" as .mp4…"), g_bobExpName.c_str());
}
static bool BobTick()   // (VhsTick, chaque image) : vrai si une conversion de l'onglet est suivie
{
    if (g_bobImpId.empty() && g_bobExpDst.empty()) return false;
    if (!g_vidPoll) { g_bobImpId.clear(); g_bobExpDst.clear(); return false; }
    float p = 0; wchar_t msg[512] = L"";
    int st = g_vidPoll(&p, msg, 512);
    if (st == 1) { if (!g_bobImpId.empty()) g_bobImpP = p; else g_bobExpP = p; g_progress = p; return true; }
    g_progress = -1;
    if (!g_bobExpDst.empty()) {
        std::wstring dst = g_bobExpDst; g_bobExpDst.clear(); g_bobExpP = -1;
        VhsImportFree();
        if (st == 2) { SetStatus(K_OK, T(L"Vidéo exportée : %s", L"Video exported: %s"), dst.c_str()); std::wstring a = L"/select,\"" + dst + L"\""; ShellExecuteW(g_wnd, L"open", L"explorer.exe", a.c_str(), NULL, SW_SHOWNORMAL); }
        else SetStatus(K_ERR, T(L"Export raté : %s", L"Export failed: %s"), msg);
        return true;
    }
    std::wstring id = g_bobImpId, dir = g_bobImpDir, title = g_bobImpTitle;
    bool cancelled = g_bobImpCancel;
    g_bobImpId.clear(); g_bobImpP = -1; g_bobImpCancel = false; g_bobCancel = NULL;
    VhsImportFree();
    if (st != 2) {
        if (FileExists(dir + id + L".mwv")) DeleteFileW((dir + id + L".mwv").c_str());   // (film neuf, a nous : nom unique fl-)
        if (cancelled) SetStatus(K_NORMAL, T(L"Conversion annulée", L"Conversion cancelled"));
        else SetStatus(K_ERR, T(L"Conversion ratée : %s", L"Conversion failed: %s"), msg);
        return true;
    }
    BobTitleSet(dir, id, title);
    g_bobSelId = id; g_bobChk.clear();
    BobScan();
    SetStatus(K_OK, T(L"Vidéo importée : « %s » (%s). En jeu, bobine en main, R : FILMS PRÊTS.", L"Video imported: \"%s\" (%s). In game, reel in hand, R: READY FILMS."), title.c_str(), msg);
    return true;
}

// ---- renommer (titres.txt) : seulement un film qui n'est sur aucune bobine ni cassette (sinon : /titre en jeu)
static bool BobCanRename(const BobFilm &f) { return f.use == BU_NONE && !f.partial; }
static void BobRenameOk()
{
    int i = -1; for (int k = 0; k < (int)g_bob.size(); k++) if (g_bob[k].id == g_bobEditId) i = k;
    g_bobEdit = false;
    if (i < 0 || !BobGuard(T(L"Ferme le jeu pour renommer un film", L"Close the game to rename a film"))) return;
    std::wstring t = BobCleanTitle(g_bobEditText);
    if (BobTitleSet(BobFilmDirOf(g_bobProf), g_bob[i].id, t)) SetStatus(K_OK, T(L"Film renommé", L"Film renamed"));
    else SetStatus(K_ERR, T(L"Titre pas écrit", L"Title not written"));
    BobScan();
}
static bool BobChar(wchar_t c)
{
    if (!g_bobEdit) return false;
    if (c == 8) { if (!g_bobEditText.empty()) g_bobEditText.pop_back(); }
    else if (c == 127) g_bobEditText.clear();
    else if (c == 13) BobRenameOk();
    else if (c == 27) g_bobEdit = false;
    else if (c == 22 && OpenClipboard(g_wnd)) {
        HANDLE d = GetClipboardData(CF_UNICODETEXT);
        const wchar_t *s = d ? (const wchar_t *)GlobalLock(d) : NULL;
        if (s) { for (; *s && *s != L'\r' && *s != L'\n' && g_bobEditText.size() < 24; s++) if (*s >= 32 && !wcschr(L"|~;+\t", *s)) g_bobEditText += *s; GlobalUnlock(d); }
        CloseClipboard();
    }
    else if (c >= 32 && !wcschr(L"|~;+\t", c) && g_bobEditText.size() < 24) g_bobEditText += c;
    g_time = 0.2f;
    return true;
}

// ---- dessin
static const float kBobRowH = 66;
static RectF BobListR() { RectF c = kMediaCard; return RectF(c.X + 18, c.Y + 128, c.Width - 36, c.Height - 128 - 14 - 62 - 10); }
static float BobMaxScroll() { return max(0.0f, (float)g_bob.size() * kBobRowH - BobListR().Height); }
static void BobCheckBox(Graphics &g, RectF r, bool on, bool hot)
{
    GraphicsPath p; RoundRect(p, r, 5);
    SolidBrush b(on ? kAcc : hot ? TH(cardSel) : TH(field)); g.FillPath(&b, &p);
    Pen pen(on ? kAcc : TH(fieldBorder), 1.2f); g.DrawPath(&pen, &p);
    if (on) Icon(g, IC_CHECK, r.X + r.Width / 2, r.Y + r.Height / 2, r.Width - 6, kOnAcc, 2.2f);
}
static void BobFilmGlyph(Graphics &g, RectF r)   // (pas encore d'image : une bobine stylisee)
{
    GraphicsPath p; RoundRect(p, r, 6);
    SolidBrush b(Color(255, 20, 20, 24)); g.FillPath(&b, &p);
    float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, rad = min(r.Width, r.Height) * 0.36f;
    Pen ring(Color(255, 110, 110, 116), 2.0f); g.DrawEllipse(&ring, cx - rad, cy - rad, rad * 2, rad * 2);
    SolidBrush hub(Color(255, 110, 110, 116));
    for (int k = 0; k < 6; k++) { float a = k * 3.14159f / 3, hx = cx + cosf(a) * rad * 0.55f, hy = cy + sinf(a) * rad * 0.55f; g.FillEllipse(&hub, hx - rad * 0.16f, hy - rad * 0.16f, rad * 0.32f, rad * 0.32f); }
}
static void BobDrawImage(Graphics &g, Bitmap *bmp, RectF r, float rad)
{
    GraphicsPath p; RoundRect(p, r, rad);
    SolidBrush blk(Color(255, 8, 8, 10)); g.FillPath(&blk, &p);
    if (!bmp) return;
    float iw = (float)bmp->GetWidth(), ih = (float)bmp->GetHeight(), s = min(r.Width / iw, r.Height / ih);
    RectF dr(r.X + (r.Width - iw * s) / 2, r.Y + (r.Height - ih * s) / 2, iw * s, ih * s);
    GraphicsState st = g.Save(); g.SetClip(&p, CombineModeIntersect); g.SetInterpolationMode(InterpolationModeBilinear); g.DrawImage(bmp, dr); g.Restore(st);
}
static std::wstring BobInfoLine(const BobFilm &f, bool dims)
{
    BobInfo i; bool has = BobGetInfo(f, i);
    std::wstring s;
    if (f.partial) s = T(L"Réception inachevée", L"Unfinished download");
    else if (!has) s = T(L"Lecture…", L"Reading…");
    else if (!i.ok) s = T(L"Fichier illisible", L"Unreadable file");
    else s = VhsClock(i.secs);
    s += L" · " + BobSize(f.bytes);
    if (dims && has && i.ok) { wchar_t b[64]; swprintf_s(b, T(L"%d × %d, %d images/s", L"%d × %d, %d frames/s"), i.w, i.h, i.fps); s += L" · " + std::wstring(b); }
    return s;
}
static void DrawBob(Graphics &g)
{
    MediaLayout();
    if (GetTickCount() - g_bobScanT > 2000) BobScan();
    RectF c = kMediaCard, v = kMediaView, lr = BobListR();
    Glass(c); Glass(v);
    int sel = BobSelIndex();
    bool busy = BobBusy(), can = !g_bobGameOn && !busy;
    float lx = c.X + 18, lw = c.Width - 36;
    // en-tete : titre, Importer, relire
    Title(g, T(L"Films des bobines", L"Reel films") + (g_bob.empty() ? std::wstring() : L" · " + std::to_wstring(g_bob.size())), RectF(lx, c.Y + 14, lw - 210, 36), 16, kInk);
    std::wstring impS = T(L"Importer une vidéo…", L"Import a video…");
    float impW = max(160.0f, MeasureW(g, impS, 13, FontStyleBold) + 56);   // (russe : plus long)
    UiButton(g, RectF(lx + lw - impW, c.Y + 14, impW, 36), impS, UI_BOB_IMPORT, true, IC_PLUS, can, 11);
    UiButton(g, RectF(lx + lw - impW - 8 - 36, c.Y + 14, 36, 36), L"", UI_BOB_REFRESH, false, IC_UNDO, true, 11);
    // partie choisie (‹ nom ›) et sorte de l'import (bobine / cassette)
    {
        float y = c.Y + 58;
        int si = BobSlotIndex();
        UiButton(g, RectF(lx, y, 32, 30), L"", UI_BOB_PREV, false, IC_CHEVL, g_bobSlots.size() > 1, 10);
        std::wstring nm = si >= 0 ? g_bobSlots[si].name : T(L"Partie principale", L"Main game");
        float kindW = PillW(T(L"Bobine de cinéma", L"Cinema reel"), 11.5f) + PillW(T(L"Cassette VHS", L"VHS tape"), 11.5f) + 6;
        float nw = min(MeasureW(g, nm, 13.5f, FontStyleBold) + 8, max(60.0f, lw - 32 * 2 - 16 - kindW - 16));
        Text(g, nm, RectF(lx + 40, y, nw, 30), 13.5f, FontStyleBold, kInk, StringAlignmentNear);
        UiButton(g, RectF(lx + 40 + nw + 6, y, 32, 30), L"", UI_BOB_NEXT, false, IC_CHEVR, g_bobSlots.size() > 1, 10);
        float kx = lx + lw - kindW;
        for (int k = 0; k < 2; k++) {
            std::wstring s = k ? T(L"Cassette VHS", L"VHS tape") : T(L"Bobine de cinéma", L"Cinema reel");
            float w = PillW(s, 11.5f);
            RectF r(kx, y + 2, w, 26);
            MediaChip(g, r, s, g_bobKind == k ? 3 : 0);
            HotZone(r, UI_BOB_KIND + k);
            kx += w + 6;
        }
        std::wstring sub;
        if (si >= 0 && g_bobSlots[si].guest) sub = T(L"Partie « chez l'hôte » : ses bobines désignent des films de la partie invité.", L"\"At the host's\" game: its reels point to films of the guest game.");
        else sub = g_bobKind ? T(L"« Importer » : pour une cassette VHS (image 4:3, effet VHS).", L"\"Import\": for a VHS tape (4:3 picture, VHS effect).") : T(L"« Importer » : pour une bobine de cinéma, sans limite de durée.", L"\"Import\": for a cinema reel, no length limit.");
        float subW = lw;
        if (g_bobGameOn) {
            std::wstring w = T(L"Jeu ouvert : lecture seule", L"Game open: read only");
            float pw = PillW(w, 11.5f);
            MediaChip(g, RectF(lx + lw - pw, c.Y + 96, pw, 22), w, 2);
            subW -= pw + 12;
        }
        Text(g, sub, RectF(lx, c.Y + 98, subW, 18), 12, FontStyleRegular, kGrey, StringAlignmentNear);
    }
    // la liste
    if (g_bob.empty()) {
        Para(g, T(L"Aucun film dans cette partie. Filme au caméscope, importe une vidéo ici ou en jeu (bobine en main, R), ou reçois-en d'un autre joueur.", L"No film in this game. Record with the camcorder, import a video here or in game (reel in hand, R), or receive some from another player."),
             RectF(lr.X, lr.Y + 12, lr.Width - 30, 60), 13.5f, kGrey);
    } else {
        float ms = BobMaxScroll(), sc = g_scroll[TAB_VHS] = min(g_scroll[TAB_VHS], ms);
        GraphicsState st = g.Save(); g.SetClip(lr, CombineModeIntersect);
        for (int i = 0; i < (int)g_bob.size(); i++) {
            BobFilm &f = g_bob[i];
            RectF r(lr.X, lr.Y + i * kBobRowH - sc, lr.Width - (ms > 0 ? 14 : 0), kBobRowH - 6);
            if (r.Y + r.Height < lr.Y || r.Y > lr.Y + lr.Height) continue;
            bool vis = r.Y >= lr.Y - 1 && r.Y + r.Height <= lr.Y + lr.Height + 1 && i < kBobRowsMax;
            bool isSel = i == sel, hot = g_uiHot == UI_BOB_ROW + i * 2, chk = g_bobChk.count(f.id) != 0;
            GraphicsPath p; RoundRect(p, r, 14);
            SolidBrush rb(isSel ? WithA(kAcc, 0.10f) : hot ? TH(cardSel) : TH(card)); g.FillPath(&rb, &p);
            Pen rp(isSel ? kAcc : hot ? WithA(kAcc, 0.6f) : TH(choiceBorder), 1); g.DrawPath(&rp, &p);
            RectF cb(r.X + 12, r.Y + (r.Height - 20) / 2, 20, 20);
            BobCheckBox(g, cb, chk, g_uiHot == UI_BOB_ROW + i * 2 + 1);
            RectF th(r.X + 42, r.Y + 6, 85, 48);
            Bitmap *bmp = f.partial ? NULL : BobThumbOf(f);
            if (bmp) BobDrawImage(g, bmp, th, 6); else BobFilmGlyph(g, th);
            float tx = th.X + th.Width + 12, bx = r.X + r.Width - 12;
            std::wstring pill = f.partial ? T(L"Inachevé", L"Unfinished") : f.recu ? (f.use != BU_NONE ? T(L"Reçu, gardé", L"Received, kept") : T(L"Reçu, sera effacé", L"Received, will go")) : T(L"À toi", L"Yours");
            int kind = f.partial ? 2 : f.recu ? (f.use != BU_NONE ? 1 : 2) : 0;
            float pw = PillW(pill, 11.5f) + 4;
            MediaChip(g, RectF(bx - pw, r.Y + (r.Height - 26) / 2, pw, 26), pill, kind);
            float tw = bx - pw - 12 - tx;
            Text(g, BobTitleOf(f), RectF(tx, r.Y + 9, tw, 22), 14.5f, FontStyleBold, f.title.empty() ? kGrey : kInk, StringAlignmentNear);
            Text(g, BobInfoLine(f, false) + L" · " + BobWhere(f), RectF(tx, r.Y + 31, tw, 18), 12, FontStyleRegular, kGrey, StringAlignmentNear);
            if (vis) { HotZone(cb, UI_BOB_ROW + i * 2 + 1); HotZone(r, UI_BOB_ROW + i * 2); }
        }
        g.Restore(st);
        if (ms > 0) DrawScrollBar(g, lr, sc, ms);
    }
    // bande du bas : conversion en cours, sinon la suppression (cochés ou film choisi), sinon la place
    {
        RectF s(lx, c.Y + c.Height - 14 - 62, lw, 62);
        PersoStripBg(g, s);
        float bx = s.X + s.Width - 12;
        if (!g_bobImpId.empty()) {
            UiButton(g, RectF(bx - 104, s.Y + 13, 104, 36), T(L"Annuler", L"Cancel"), UI_BOB_CANCEL, false, IC_CLOSE, g_bobCancel != NULL, 11);
            float tw = bx - 104 - 12 - (s.X + 12);
            Text(g, T(L"Conversion de ", L"Converting ") + g_bobImpName + L"… " + std::to_wstring((int)(max(0.0f, g_bobImpP) * 100)) + L" %", RectF(s.X + 12, s.Y + 9, tw, 20), 13, FontStyleBold, kInk, StringAlignmentNear);
            DrawBar(g, RectF(s.X + 12, s.Y + 38, tw, 6), max(0.0f, g_bobImpP));
        } else {
            std::vector<int> tg = BobTargets();
            bool arm = g_bobDelT && GetTickCount() - g_bobDelT < 6000;
            UiButton(g, RectF(bx - 120, s.Y + 13, 120, 36), arm ? T(L"Confirmer ?", L"Confirm?") : T(L"Supprimer", L"Delete"), UI_BOB_DEL, false, IC_TRASH, can && !tg.empty(), 11);
            float tw = bx - 120 - 12 - (s.X + 12);
            std::wstring l1, l2;
            if (tg.empty()) {
                l1 = T(L"Films de la partie : ", L"Films of the game: ") + BobSize(g_bobTotal) + (g_bobFreeOk ? T(L" · libre sur le disque : ", L" · free on the disk: ") + BobSize(g_bobFree) : std::wstring());
                l2 = T(L"Coche des films (ou choisis-en un) pour les supprimer. Les tiens ne reviennent pas.", L"Tick films (or pick one) to delete them. Yours don't come back.");
            } else {
                unsigned long long b = 0; for (int i : tg) b += g_bob[i].bytes;
                l1 = g_bobChk.empty() ? T(L"Film choisi : ", L"Selected film: ") + BobTitleOf(g_bob[tg[0]]) + L" (" + BobSize(b) + L")" : std::to_wstring(tg.size()) + T(L" film(s) coché(s) (", L" film(s) ticked (") + BobSize(b) + L")";
                std::wstring w = BobDeleteWarn(tg);
                l2 = g_bobGameOn ? T(L"Ferme le jeu pour supprimer.", L"Close the game to delete.") : arm ? T(L"Clique encore sur Confirmer : à la Corbeille. ", L"Click Confirm again: to the Recycle Bin. ") + w : !w.empty() ? T(L"Si tu le supprimes : ", L"If you delete it: ") + w : T(L"Supprimer : sur aucune bobine ni cassette de la partie.", L"Delete: on no reel or tape of the game.");
            }
            Text(g, l1, RectF(s.X + 12, s.Y + 11, tw, 20), 13, FontStyleBold, kInk, StringAlignmentNear);
            Text(g, l2, RectF(s.X + 12, s.Y + 31, tw, 18), 12, FontStyleRegular, !tg.empty() && !BobDeleteWarn(tg).empty() ? kWarn : kGrey, StringAlignmentNear);
        }
    }
    // a droite : le film choisi
    float fy = v.Y + v.Height - 16 - 40, hw = (v.Width - 48 - 10) / 2;
    std::wstring dirS = T(L"Dossier des films", L"Films folder"), vidS = T(L"Vidéos exportées", L"Exported videos");
    if (max(MeasureW(g, dirS, 13, FontStyleBold), MeasureW(g, vidS, 13, FontStyleBold)) + 52 > hw) {   // (trop long cote a cote : l'un sur l'autre)
        UiButton(g, RectF(v.X + 24, fy - 48, v.Width - 48, 40), dirS, UI_BOB_DIR, false, IC_FOLDER, true, 12);
        UiButton(g, RectF(v.X + 24, fy, v.Width - 48, 40), vidS, UI_BOB_VIDDIR, false, IC_FOLDER, true, 12);
    } else {
        UiButton(g, RectF(v.X + 24, fy, hw, 40), dirS, UI_BOB_DIR, false, IC_FOLDER, true, 12);
        UiButton(g, RectF(v.X + 24 + hw + 10, fy, hw, 40), vidS, UI_BOB_VIDDIR, false, IC_FOLDER, true, 12);
    }
    if (sel < 0) {
        Icon(g, IC_FILM, v.X + v.Width / 2, v.Y + 220, 56, WithA(kGrey, 0.45f), 1.4f);
        Para(g, T(L"Clique sur un film pour le voir ici : titre, durée, où il sert, d'où il vient. Puis le renommer ou l'exporter en .mp4.", L"Click a film to see it here: title, length, where it's used, where it comes from. Then rename it or export it as .mp4."), RectF(v.X + 24, v.Y + 270, v.Width - 48, 70), 13, kGrey, StringAlignmentNear);
        return;
    }
    BobFilm &f = g_bob[sel];
    RectF pr(v.X + 16, v.Y + 16, v.Width - 32, (v.Width - 32) * 9 / 16);
    Bitmap *bmp = f.partial ? NULL : BobThumbOf(f);
    if (bmp) BobDrawImage(g, bmp, pr, 10); else { BobFilmGlyph(g, pr); }
    float y = pr.Y + pr.Height + 12, w = v.Width - 48;
    if (g_bobEdit && g_bobEditId == f.id) {
        RectF fr(v.X + 24, y, w, 34);
        GraphicsPath fp; RoundRect(fp, fr, 9); SolidBrush fb(TH(field)); g.FillPath(&fb, &fp); Pen fpn(kAcc, 1.6f); g.DrawPath(&fpn, &fp);
        RectF trr(fr.X + 12, fr.Y, fr.Width - 24, fr.Height);
        Text(g, g_bobEditText.empty() ? T(L"Titre du film", L"Film title") : g_bobEditText, trr, 14, FontStyleBold, g_bobEditText.empty() ? TH(placeholder) : kInk, StringAlignmentNear);
        if (fmodf(g_time, 1.0f) < 0.55f) { float cw = g_bobEditText.empty() ? 0 : MeasureW(g, g_bobEditText, 14, FontStyleBold), cx = min(trr.X + cw + 1, trr.X + trr.Width); Pen cp(kAcc, 1.6f); g.DrawLine(&cp, cx, fr.Y + 8, cx, fr.Y + fr.Height - 8); }
        y += 42;
        UiButton(g, RectF(v.X + 24, y, (w - 8) / 2, 34), T(L"Annuler", L"Cancel"), UI_BOB_REN_CANCEL, false, -1, true, 10);
        UiButton(g, RectF(v.X + 24 + (w - 8) / 2 + 8, y, (w - 8) / 2, 34), T(L"Écrire", L"Write"), UI_BOB_REN_OK, true, -1, true, 10);
        y += 42;
        Text(g, T(L"Entrée : écrire · Échap : annuler · 24 caractères au plus", L"Enter: write · Esc: cancel · 24 characters at most"), RectF(v.X + 24, y, w, 18), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
        return;
    }
    Text(g, BobTitleOf(f), RectF(v.X + 24, y, w, 24), 15, FontStyleBold, f.title.empty() ? kGrey : kInk, StringAlignmentNear);
    y += 28;
    Text(g, BobInfoLine(f, true), RectF(v.X + 24, y, w, 18), 12, FontStyleRegular, kGrey, StringAlignmentNear); y += 20;
    Text(g, T(L"Où : ", L"Where: ") + BobWhere(f), RectF(v.X + 24, y, w, 18), 12, FontStyleRegular, kGrey, StringAlignmentNear); y += 20;
    Para(g, T(L"Origine : ", L"Origin: ") + BobOrigin(f), RectF(v.X + 24, y, w, 36), 12, f.recu && f.use == BU_NONE ? kWarn : kGrey); y += 40;
    bool exporting = !g_bobExpDst.empty();
    UiButton(g, RectF(v.X + 24, y, w, 40), exporting ? T(L"Export… ", L"Exporting… ") + std::to_wstring((int)(max(0.0f, g_bobExpP) * 100)) + L" %" : T(L"Exporter en .mp4", L"Export as .mp4"), UI_BOB_EXPORT, true, IC_DOWNLOAD, !busy && !f.partial && !f.path.empty());
    y += 48;
    UiButton(g, RectF(v.X + 24, y, w, 40), T(L"Renommer", L"Rename"), UI_BOB_RENAME, false, IC_PENCIL, can && BobCanRename(f));
    y += 44;
    if (!BobCanRename(f) && !f.partial) Para(g, T(L"Sur une bobine ou une cassette : son titre est dans la sauvegarde, renomme-le en jeu (/titre).", L"On a reel or tape: its title is in the save, rename it in game (/titre)."), RectF(v.X + 24, y, w, 34), 11.5f, kGrey);
}

static void BobClick(int id)
{
    if (id == UI_BOB_TAB) { if (g_tab != TAB_VHS) GoPage(TAB_VHS); if (!g_mediaBob) { g_mediaBob = true; g_scroll[TAB_VHS] = 0; } g_vhsEdit = -1; BobEnter(); return; }
    if (id == UI_BOB_PREV || id == UI_BOB_NEXT) {
        if (g_bobSlots.empty()) return;
        int i = BobSlotIndex(), n = (int)g_bobSlots.size();
        i = ((i < 0 ? 0 : i) + (id == UI_BOB_NEXT ? 1 : n - 1)) % n;
        g_bobProf = g_bobSlots[i].prof; g_bobSelId.clear(); g_bobChk.clear(); g_bobEdit = false; g_bobDelT = 0; g_scroll[TAB_VHS] = 0;
        BobScan(); return;
    }
    if (id == UI_BOB_REFRESH) { g_bobEdit = false; BobEnter(); return; }
    if (id == UI_BOB_KIND || id == UI_BOB_KIND + 1) { g_bobKind = id - UI_BOB_KIND; return; }
    if (id == UI_BOB_DIR) { std::wstring d = BobFilmDirOf(g_bobProf); SHCreateDirectoryExW(NULL, d.c_str(), NULL); ShellOpen(d); return; }
    if (id == UI_BOB_VIDDIR) { std::wstring d = VhsVideoDir(); SHCreateDirectoryExW(NULL, d.c_str(), NULL); ShellOpen(d); return; }
    if (id == UI_BOB_IMPORT) { g_bobEdit = false; BobImport(); return; }
    if (id == UI_BOB_CANCEL) { if (g_bobCancel && !g_bobImpId.empty()) { g_bobImpCancel = true; g_bobCancel(); } return; }
    if (id == UI_BOB_EXPORT) { BobExport(); return; }
    if (id == UI_BOB_DEL) { g_bobEdit = false; BobDelete(); return; }
    if (id == UI_BOB_RENAME) {
        int i = BobSelIndex();
        if (i < 0 || !BobCanRename(g_bob[i]) || !BobGuard(T(L"Ferme le jeu pour renommer un film", L"Close the game to rename a film"))) return;
        g_bobEdit = true; g_bobEditId = g_bob[i].id; g_bobEditText = g_bob[i].title; g_focus = -1; g_time = 0; return;
    }
    if (id == UI_BOB_REN_OK) { BobRenameOk(); return; }
    if (id == UI_BOB_REN_CANCEL) { g_bobEdit = false; return; }
    if (id >= UI_BOB_ROW && id < UI_BOB_END) {
        int i = (id - UI_BOB_ROW) / 2, part = (id - UI_BOB_ROW) % 2;
        if (i < 0 || i >= (int)g_bob.size()) return;
        g_bobDelT = 0;
        if (part == 1) { if (!g_bobChk.erase(g_bob[i].id)) g_bobChk.insert(g_bob[i].id); return; }
        if (g_bobSelId != g_bob[i].id) { g_bobSelId = g_bob[i].id; g_bobEdit = false; }
    }
}

// ---- /testbobines <journal> : releve, titres, usage, origine, durees (dont un film de plus de 4 Go), noms refuses,
// suppression (et refus jeu ouvert), fichiers du monde jamais touches. Dossier de jeu jetable seulement ; laisse ses
// films d'essai dans profils\partie-1 pour les captures « bobines ».
static bool BobWriteFile(const std::wstring &p, const std::string &s)
{
    HANDLE h = CreateFileW(p.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL);
    if (h == INVALID_HANDLE_VALUE) return false;
    DWORD w = 0; BOOL ok = s.empty() || WriteFile(h, s.data(), (DWORD)s.size(), &w, NULL); CloseHandle(h);
    return ok && w == s.size();
}
static void BobPutBlock(std::string &o, int kind, uint32_t ms, const std::string &data)
{
    o += (char)kind; o.append((const char *)&ms, 4); int32_t n = (int32_t)data.size(); o.append((const char *)&n, 4); o += data;
}
static std::string BobHeader(int w, int h, int fps, int rate)
{
    std::string o = "MWV1";
    o += (char)(w & 255); o += (char)(w >> 8); o += (char)(h & 255); o += (char)(h >> 8); o += (char)fps; o += (char)(rate & 255); o += (char)(rate >> 8);
    return o;
}
static std::string BobTestJpeg(Color col)
{
    CLSID jpg; std::string out;
    if (!EncoderClsid(L"image/jpeg", &jpg)) return out;
    Bitmap b(160, 90, PixelFormat24bppRGB);
    { Graphics g(&b); SolidBrush br(col); g.FillRectangle(&br, 0, 0, 160, 90); SolidBrush w(Color(255, 255, 255, 255)); g.FillEllipse(&w, 60, 25, 40, 40); }
    IStream *s = NULL;
    if (CreateStreamOnHGlobal(NULL, TRUE, &s) != S_OK) return out;
    if (b.Save(s, &jpg, NULL) == Ok) {
        STATSTG stt; s->Stat(&stt, STATFLAG_NONAME);
        out.resize((size_t)stt.cbSize.QuadPart);
        LARGE_INTEGER z = {}; s->Seek(z, STREAM_SEEK_SET, NULL);
        ULONG got = 0; s->Read(&out[0], (ULONG)out.size(), &got);
    }
    s->Release();
    return out;
}
static int BobSelfTest(const wchar_t *log)
{
    DeleteFileW(log);
    int bad = 0;
    auto say = [&](const std::string &l, bool good) {
        FILE *o = _wfopen(log, L"ab");
        if (o) { fprintf(o, "%s %s\r\n", l.c_str(), good ? "ok" : "ECHEC"); fclose(o); }
        if (!good) bad++;
    };
    std::wstring parent = g_dir.substr(0, g_dir.find_last_of(L'\\', g_dir.size() - 2) + 1);
    if (g_gameDir.empty() || _wcsicmp(g_gameDir.c_str(), g_dir.c_str()) || _wcsnicmp(LocalDir().c_str(), parent.c_str(), parent.size())) { say("refuse : dossier de jeu jetable et LOCALAPPDATA a cote obligatoires", false); return 3; }
    if (_wcsnicmp(SaveProfRoot().c_str(), g_dir.c_str(), g_dir.size())) { say("refuse : profils hors du dossier jetable", false); return 3; }
    // partie d'essai : partie-1, ses fichiers du monde et ses films
    std::wstring prof = L"partie-1", save = SaveDirOf(prof), dir = BobFilmDirOf(prof);
    SHCreateDirectoryExW(NULL, save.c_str(), NULL); SHCreateDirectoryExW(NULL, dir.c_str(), NULL);
    BobWriteFile(SaveProfRoot() + prof + L"\\nom.txt", "Essai bobines");
    std::string esineet = "1;kela;f1-aaaa~Bobine A;1;2;3;0;0;0;1\n2;vhs;f5-eeee+f6-ffff~Ma cassette;0;0;0;0;0;0;1\n3;nauhuri;f7-gggg~Dans le VCR|0;0;0;0;0;0;0;1\n4;kela;url:http://exemple/video.mp4~Lien;0;0;0;0;0;0;1\n5;kela;tyhja~Vierge;0;0;0;0;0;0;1\n";
    std::string kino = "1;f2-bbbb~Film salle\n2;tyhja\n";
    say("fichiers du monde ecrits", BobWriteFile(save + L"MWCoop-esineet.txt", esineet) && BobWriteFile(save + L"MWCoop-kino.txt", kino));
    std::string jA = BobTestJpeg(Color(255, 200, 60, 40)), jB = BobTestJpeg(Color(255, 40, 90, 200)), jC = BobTestJpeg(Color(255, 40, 160, 80));
    say("images d'essai (jpeg)", !jA.empty() && !jB.empty());
    std::string f1 = BobHeader(480, 270, 18, 11025); BobPutBlock(f1, 1, 0, jA); BobPutBlock(f1, 2, 0, std::string(22050, '\x7f')); BobPutBlock(f1, 1, 1500, jA);
    std::string f2 = BobHeader(480, 270, 18, 11025); BobPutBlock(f2, 1, 0, jB); BobPutBlock(f2, 1, 5000, jB);
    std::string f3 = BobHeader(320, 240, 12, 11025); BobPutBlock(f3, 1, 0, jC); BobPutBlock(f3, 1, 3000, jC);
    std::string fl = BobHeader(480, 270, 18, 11025); BobPutBlock(fl, 1, 0, jC); BobPutBlock(fl, 2, 0, std::string(11025 * 70, '\x7f'));
    std::string f5 = BobHeader(320, 240, 12, 11025); BobPutBlock(f5, 1, 2000, jA);
    bool w = BobWriteFile(dir + L"f1-aaaa.mwv", f1) && BobWriteFile(dir + L"f2-bbbb.mwv", f2) && BobWriteFile(dir + L"f3-cccc.mwv", f3) && BobWriteFile(dir + L"fl-0000000000000001.mwv", fl)
          && BobWriteFile(dir + L"f5-eeee.mwv", f5) && BobWriteFile(dir + L"f4-dddd.mwv.osa", std::string(4096, '\0')) && BobWriteFile(dir + L"export-2026-11-07.mwv", f2)
          && BobWriteFile(dir + L"recus.txt", "f2-bbbb\r\nf3-cccc\r\n") && BobWriteFile(dir + L"titres.txt", "fl-0000000000000001\tMon film\r\nf1-aaaa\tPas celui-ci\r\n")
          && BobWriteFile(dir + L"bad..name.mwv", f1) && BobWriteFile(dir + L"note.txt", "x");
    say("films d'essai ecrits", w);
    // film de plus de 4 Go (fichier creux) : deux blocs sautes de 2 Go puis une image a 9 s au-dela de 4 Go
    std::wstring big = dir + L"f8-gros.mwv";
    bool bigOk = false;
    {
        HANDLE h = CreateFileW(big.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL);
        DWORD r = 0;
        if (h != INVALID_HANDLE_VALUE && DeviceIoControl(h, FSCTL_SET_SPARSE, NULL, 0, NULL, 0, &r, NULL)) {
            std::string hd = BobHeader(480, 270, 18, 11025);
            auto at = [&](unsigned long long pos, const std::string &s) { LARGE_INTEGER li; li.QuadPart = (LONGLONG)pos; DWORD wr = 0; return SetFilePointerEx(h, li, NULL, FILE_BEGIN) && WriteFile(h, s.data(), (DWORD)s.size(), &wr, NULL) && wr == s.size(); };
            const int32_t n = 0x7FFFFF00;
            std::string b1; b1 += (char)3; uint32_t m0 = 100; b1.append((const char *)&m0, 4); b1.append((const char *)&n, 4);
            unsigned long long p = 11;
            bigOk = at(0, hd) && at(p, b1); p += 9 + (unsigned long long)n;
            bigOk = bigOk && at(p, b1); p += 9 + (unsigned long long)n;
            bigOk = bigOk && at(p, b1); p += 9 + (unsigned long long)n;
            std::string img; BobPutBlock(img, 1, 9000, jA);
            bigOk = bigOk && p > 4294967296ull && at(p, img);
        }
        if (h != INVALID_HANDLE_VALUE) CloseHandle(h);
    }
    say("film de plus de 4 Go (creux)", bigOk);
    std::vector<unsigned char> esA, kiA; ReadAll(save + L"MWCoop-esineet.txt", esA); ReadAll(save + L"MWCoop-kino.txt", kiA);
    // releve
    g_bobTestGame = 0; g_bobProfSet = true; g_bobProf = prof;
    BobSlotsScan();
    say("parties : principale et partie-1", g_bobSlots.size() >= 2 && BobSlotIndex() >= 0);
    BobScan();
    for (int k = 0; k < 200; k++) { BobLock(); bool run = g_bobRun; BobUnlock(); if (!run) break; Sleep(50); }
    auto find = [&](const wchar_t *id) -> BobFilm * { for (auto &f : g_bob) if (f.id == id) return &f; return NULL; };
    say("7 films (export- , nom avec .. et .txt ignores)", g_bob.size() == 7);
    BobFilm *a = find(L"f1-aaaa"), *b = find(L"f2-bbbb"), *c3 = find(L"f3-cccc"), *l = find(L"fl-0000000000000001"), *d4 = find(L"f4-dddd"), *e5 = find(L"f5-eeee"), *g8 = find(L"f8-gros");
    say("tous trouves", a && b && c3 && l && d4 && e5 && g8);
    if (!(a && b && c3 && l && d4 && e5 && g8)) { g_bobTestGame = -1; return 1; }
    say("f1 : titre de la bobine, pas de titres.txt", a->title == L"Bobine A" && !a->titled);
    say("f1 : bobine (objet)", a->use == BU_KELA);
    say("f2 : bobine, salle 1", b->use == BU_SALLE && b->room == L"1" && b->title == L"Film salle");
    say("f2 : recu, garde", b->recu && BobOrigin(*b) == T(L"reçu, gardé (bobine de la partie)", L"received, kept (reel of the game)"));
    say("f3 : recu, sur aucune bobine, efface au chargement", c3->recu && c3->use == BU_NONE && c3->title.empty());
    say("fl : titre de titres.txt, renommable", l->title == L"Mon film" && l->titled && BobCanRename(*l) && !l->recu);
    say("f5 : cassette", e5->use == BU_VHS && e5->title == L"Ma cassette" && !BobCanRename(*e5));
    say("f4 : reception inachevee (.osa seul)", d4->partial && d4->path.empty() && d4->bytes == 4096);
    BobInfo ia, ib, il, ig;
    say("f1 : 2,0 s (son), 480 x 270, 18 i/s", BobGetInfo(*a, ia) && ia.ok && fabsf(ia.secs - 2.0f) < 0.01f && ia.w == 480 && ia.h == 270 && ia.fps == 18);
    say("f2 : 5,0 s (images)", BobGetInfo(*b, ib) && ib.ok && fabsf(ib.secs - 5.0f) < 0.01f);
    say("fl : 70 s (son)", BobGetInfo(*l, il) && il.ok && fabsf(il.secs - 70.0f) < 0.01f);
    say("f8 : 9 s lue au-dela de 4 Go", BobGetInfo(*g8, ig) && ig.ok && fabsf(ig.secs - 9.0f) < 0.01f && g8->bytes > 4294967296ull);
    say("vignette de f1 decodee", BobThumbOf(*a) != NULL);
    say("taille en Go", BobSize(5ull * 1073741824ull).find(L"5") != std::wstring::npos);
    // noms
    say("noms refuses : .., \\, /, :, vide", !BobNameOk(L"..\\x") && !BobNameOk(L"a/b") && !BobNameOk(L"a\\b") && !BobNameOk(L"c:x") && !BobNameOk(L"") && !BobNameOk(L"a..b") && BobNameOk(L"f1-aaaa"));
    // titres.txt
    say("titre ecrit (nettoye)", BobTitleSet(dir, L"f3-cccc", BobCleanTitle(L"Nouveau;titre|\tlong long long long long")));
    std::map<std::wstring, BobTitle> tt = BobTitles(dir);
    say("titre relu, 24 caracteres au plus, sans ; | tab", tt.count(L"f3-cccc") && tt[L"f3-cccc"].title.size() <= 24 && tt[L"f3-cccc"].title.find_first_of(L";|\t") == std::wstring::npos);
    say("titres.txt : les autres lignes gardees", tt[L"fl-0000000000000001"].title == L"Mon film" && tt[L"f1-aaaa"].title == L"Pas celui-ci");
    say("titre refuse pour un nom interdit", !BobTitleSet(dir, L"..\\x", L"t"));
    BobScan();
    c3 = find(L"f3-cccc");
    say("f3 : titre de titres.txt apres relecture", c3 && c3->titled && !c3->title.empty());
    tt = BobTitles(dir);
    say("titres.txt : durees ecrites (fl 70000, f2 5000, f8 9000 ms)", tt[L"fl-0000000000000001"].ms == 70000 && tt[L"f2-bbbb"].ms == 5000 && tt[L"f8-gros"].ms == 9000);
    say("titres.txt : duree seule pour un film sans titre", tt.count(L"f5-eeee") && tt[L"f5-eeee"].title.empty() && tt[L"f5-eeee"].ms == 2000);
    say("titres.txt : pas de ligne pour une reception inachevee", !tt.count(L"f4-dddd"));
    std::vector<std::wstring> tl = BobLines(dir + L"titres.txt"); bool fmt = false;
    for (auto &x : tl) if (x == L"fl-0000000000000001\tMon film\t70000") fmt = true;
    say("titres.txt : ligne « film<TAB>titre<TAB>ms »", fmt);
    BobTitleSet(dir, L"fl-0000000000000001", L"Autre titre");
    tt = BobTitles(dir);
    say("renommer garde la duree", tt[L"fl-0000000000000001"].title == L"Autre titre" && tt[L"fl-0000000000000001"].ms == 70000);
    BobTitleSet(dir, L"fl-0000000000000001", L"Mon film");
    // avertissement de suppression
    std::vector<int> vb; for (int i = 0; i < (int)g_bob.size(); i++) if (g_bob[i].id == L"f2-bbbb") vb.push_back(i);
    std::wstring warn = BobDeleteWarn(vb);
    say("avertissement : bobine de la salle 1", warn.find(L"Film salle") != std::wstring::npos && warn.find(L"1") != std::wstring::npos);
    // suppression : refusee jeu ouvert, puis faite (effacement direct : dossier jetable)
    g_bobTestGame = 1;
    std::vector<unsigned char> ti1, ti2; ReadAll(dir + L"titres.txt", ti1);
    BobWriteFile(dir + L"f9-neuf.mwv", f2); BobScan();
    for (int k = 0; k < 200; k++) { BobLock(); bool run = g_bobRun; BobUnlock(); if (!run) break; Sleep(50); }
    BobScan(); ReadAll(dir + L"titres.txt", ti2);
    say("jeu ouvert : titres.txt pas reecrit", ti1 == ti2);
    DeleteFileW((dir + L"f9-neuf.mwv").c_str());
    g_bobChk.clear(); g_bobChk.insert(L"f3-cccc"); g_bobChk.insert(L"f4-dddd"); g_bobDelT = GetTickCount();
    BobDelete();
    say("jeu ouvert : rien supprime", FileExists(dir + L"f3-cccc.mwv") && FileExists(dir + L"f4-dddd.mwv.osa"));
    g_bobTestGame = 0;
    say("suppression de 2 films (.mwv et .osa)", BobDeleteIds({ L"f3-cccc", L"f4-dddd" }, false) == 2 && !FileExists(dir + L"f3-cccc.mwv") && !FileExists(dir + L"f4-dddd.mwv.osa"));
    say("nom interdit : rien supprime", BobDeleteIds({ L"..\\f1-aaaa", L"bad..name" }, false) == 0 && FileExists(dir + L"bad..name.mwv"));
    say("les autres films restent", FileExists(dir + L"f1-aaaa.mwv") && FileExists(dir + L"f2-bbbb.mwv"));
    BobScan();
    say("relecture : 5 films", g_bob.size() == 5);
    // fichiers du monde inchanges
    std::vector<unsigned char> esB, kiB; ReadAll(save + L"MWCoop-esineet.txt", esB); ReadAll(save + L"MWCoop-kino.txt", kiB);
    say("fichiers du monde jamais modifies", esA == esB && kiA == kiB);
    DeleteFileW(big.c_str()); DeleteFileW((dir + L"bad..name.mwv").c_str()); DeleteFileW((dir + L"note.txt").c_str());
    g_bobTestGame = -1;
    FILE *o = _wfopen(log, L"ab"); if (o) { fprintf(o, bad ? "ECHEC\r\n" : "TOUT OK\r\n"); fclose(o); }
    return bad ? 1 : 0;
}
