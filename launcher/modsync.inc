// ---------------------------------------------------------------- mods de l'hote chez les invites - inclus par launcher.cpp (apres mods.inc)
// Demande de JD (07/10) : les invites recoivent les mods MSCLoader de l'hote, d'un seul accord ("tout accepter" : on
// joue entre amis).
//  - Hote : les mods de son dossier Mods coches "Envoyer" (onglet MODS ; [Lanceur] ModsPrives : ceux qu'il garde pour
//    lui). Pour chacun la DLL, Assets\<ID>\ et ses reglages (Config\Mod Settings\<ID>\) ; en plus References\ et les
//    fichiers de MSCLoader lui-meme (Assets\MSCLoader_*). Liste : chemin (relatif au dossier Mods), taille, empreinte
//    FNV-1a 64 ; refaite quand un mod, un reglage ou une coche change.
//  - Invite : une copie a part, %LOCALAPPDATA%\MWCoop\mods-invite\ (ses propres mods et reglages ne sont jamais
//    touches). TOUT ACCEPTER, une fois (ensuite automatique : COOP > ModsAuto) : ce qui manque ou differe est
//    telecharge, les DLL que l'hote n'a plus sont retirees. MSCLoader absent chez l'invite : installe au passage.
//  - Lancement en invite : copie de lancement du jeu (%LOCALAPPDATA%\MWCoop\invite\My Winter Car\, comme celle de
//    Steam : exe, DLL et .ini copies, dossiers en jonctions) dont Mods est une jonction vers mods-invite et dont
//    doorstop_config.ini dit mods=GF : MSCLoader y charge exactement les mods de l'hote. Hote sans MSCLoader (ou
//    coupe), mods refuses ou pas fini : le jeu de l'invite part sans MSCLoader ([General] enabled=false).
//  - Transport : salon IP, des messages du salon (M_MODS la liste, M_MODGET un morceau demande, M_MODDATA le
//    morceau) ; salon Steam, les memes en pair-a-pair Steam (canal 5, fiable), la liste demandee (M_MODSREQ) quand
//    la donnee "mods" du salon (empreinte de la liste) change.
#include <mutex>

// (M_MODS, M_MODGET, M_MODDATA, M_MODSREQ : avec les autres messages du salon)
static const uint32_t kSyncChunk = 48 * 1024;
static const int kSyncWindow = 8, kSyncChannel = 5;
struct SyncFile { std::string path; uint32_t size; uint64_t hash; };
struct SyncOffer { bool msc = false; std::string mscVer; std::vector<std::string> mods; std::vector<SyncFile> files; uint64_t key = 0; };

static uint64_t Fnv(const void *p, size_t n, uint64_t h = 1469598103934665603ULL)
{
    const uint8_t *b = (const uint8_t *)p;
    for (size_t i = 0; i < n; i++) { h ^= b[i]; h *= 1099511628211ULL; }
    return h;
}
static bool FileFnv(const std::wstring &f, uint64_t *h, uint32_t *size)
{
    HANDLE fh = CreateFileW(f.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, NULL, OPEN_EXISTING, 0, NULL);
    if (fh == INVALID_HANDLE_VALUE) return false;
    LARGE_INTEGER sz = {};
    GetFileSizeEx(fh, &sz);
    uint64_t v = Fnv(NULL, 0);
    std::vector<char> buf(256 * 1024);
    DWORD n = 0;
    uint64_t total = 0;
    while (ReadFile(fh, buf.data(), (DWORD)buf.size(), &n, NULL) && n) { v = Fnv(buf.data(), n, v); total += n; }
    CloseHandle(fh);
    *h = v;
    *size = (uint32_t)total;
    return sz.QuadPart < 0xFFFFFFF0LL && total == (uint64_t)sz.QuadPart;
}

static std::string OfferBlob(const SyncOffer &o)
{
    Wr w;
    w.u8(M_MODS);
    w.u8(o.msc ? 1 : 0);
    w.str(o.mscVer);
    w.u16((int)o.mods.size());
    for (auto &m : o.mods) w.str(m);
    w.u16((int)o.files.size());
    for (auto &f : o.files) { w.str(f.path); w.u32(f.size); w.u32((uint32_t)f.hash); w.u32((uint32_t)(f.hash >> 32)); }
    return w.d;
}
// Chemin recu sur ? (relatif, sans "..", sans lecteur) ; '/' -> '\'.
static bool SyncPathOk(std::string &p)
{
    if (p.empty() || p.size() > 200 || p[0] == '\\' || p[0] == '/' || p.find(':') != std::string::npos || p.find("..") != std::string::npos) return false;
    for (char &c : p) if (c == '/') c = '\\';
    return true;
}
static bool OfferParse(const std::string &m, SyncOffer &o)
{
    Rd r(m);
    if (r.u8() != M_MODS) return false;
    o = SyncOffer();
    o.msc = r.u8() != 0;
    o.mscVer = r.str();
    int nm = r.u16();
    for (int i = 0; i < nm && r.ok; i++) o.mods.push_back(r.str());
    int nf = r.u16();
    for (int i = 0; i < nf && r.ok; i++) {
        SyncFile f;
        f.path = r.str();
        f.size = r.u32();
        uint32_t lo = r.u32(), hi = r.u32();
        f.hash = lo | ((uint64_t)hi << 32);
        if (!r.ok) break;
        if (!SyncPathOk(f.path)) { TestLog("mods : chemin refuse dans la liste de l'hote (%s)", f.path.c_str()); return false; }
        o.files.push_back(f);
    }
    o.key = Fnv(m.data(), m.size());
    return r.ok;
}

// ---- hote : la liste proposee
static SyncOffer g_offer;                 // (sous g_lcs)
static std::string g_offerBlob;           // (sous g_lcs) message M_MODS pret a envoyer
static std::wstring g_offerDir;           // (sous g_lcs) dossier Mods de l'hote
static std::atomic<int> g_offerBusy(0);
static std::atomic<bool> g_offerNew(false);
static std::string g_offerSig;            // ce sur quoi la liste a ete faite (fil de la fenetre)

static bool ModShared(const std::wstring &file)
{
    wchar_t b[4096] = L"";
    GetPrivateProfileStringW(L"Lanceur", L"ModsPrives", L"", b, 4096, g_iniLauncher.c_str());
    std::wstring s = std::wstring(L"|") + b + L"|", k = L"|" + file + L"|";
    for (auto &c : s) c = towlower(c);
    for (auto &c : k) c = towlower(c);
    return s.find(k) == std::wstring::npos;
}
static void ModSetShared(const std::wstring &file, bool on)
{
    wchar_t b[4096] = L"";
    GetPrivateProfileStringW(L"Lanceur", L"ModsPrives", L"", b, 4096, g_iniLauncher.c_str());
    std::wstring out, all = b;
    size_t a = 0;
    while (a <= all.size()) {
        size_t e = all.find(L'|', a);
        std::wstring n = all.substr(a, e == std::wstring::npos ? std::wstring::npos : e - a);
        if (!n.empty() && _wcsicmp(n.c_str(), file.c_str())) out += (out.empty() ? L"" : L"|") + n;
        if (e == std::wstring::npos) break;
        a = e + 1;
    }
    if (!on) out += (out.empty() ? L"" : L"|") + file;
    WritePrivateProfileStringW(L"Lanceur", L"ModsPrives", out.c_str(), g_iniLauncher.c_str());
}

struct OfferJob { std::wstring dir; bool msc; std::string ver; std::vector<std::pair<std::wstring, std::wstring>> mods; };   // (fichier, ID)
static void OfferAddTree(const std::wstring &dir, const std::wstring &rel, std::vector<SyncFile> &out, uint64_t &total)
{
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((dir + rel + L"\\*").c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return;
    do {
        std::wstring n = fd.cFileName;
        if (n == L"." || n == L"..") continue;
        std::wstring r = rel + L"\\" + n;
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) { OfferAddTree(dir, r, out, total); continue; }
        SyncFile f;
        f.path = Narrow(r.substr(1), CP_UTF8);   // (sans le \ de tete)
        if (out.size() >= 3000 || total > (1ull << 30) || !SyncPathOk(f.path) || !FileFnv(dir + r, &f.hash, &f.size)) continue;
        total += f.size;
        out.push_back(f);
    } while (FindNextFileW(h, &fd));
    FindClose(h);
}
static DWORD WINAPI OfferThread(void *p)
{
    OfferJob *job = (OfferJob *)p;
    SyncOffer o;
    o.msc = job->msc;
    o.mscVer = job->ver;
    uint64_t total = 0;
    if (o.msc) {
        std::wstring d = job->dir.substr(0, job->dir.size() - 1);   // (sans le \ final : chemins relatifs "X\Y")
        for (auto &m : job->mods) {
            o.mods.push_back(Narrow(m.first, CP_UTF8));
            SyncFile f;
            f.path = Narrow(m.first, CP_UTF8);
            if (SyncPathOk(f.path) && FileFnv(job->dir + m.first, &f.hash, &f.size)) { o.files.push_back(f); total += f.size; }
            OfferAddTree(d, L"\\Assets\\" + m.second, o.files, total);
            OfferAddTree(d, L"\\Config\\Mod Settings\\" + m.second, o.files, total);
        }
        if (!job->mods.empty()) OfferAddTree(d, L"\\References", o.files, total);
        WIN32_FIND_DATAW fd;   // MSCLoader lui-meme (console, page des reglages)
        HANDLE h = FindFirstFileW((job->dir + L"Assets\\MSCLoader_*").c_str(), &fd);
        if (h != INVALID_HANDLE_VALUE) {
            do if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) OfferAddTree(d, std::wstring(L"\\Assets\\") + fd.cFileName, o.files, total);
            while (FindNextFileW(h, &fd));
            FindClose(h);
        }
    }
    std::string blob = OfferBlob(o);
    while (blob.size() > 60000 && !o.files.empty()) { o.files.pop_back(); blob = OfferBlob(o); }   // (un message du salon : 64 Ko au plus)
    o.key = Fnv(blob.data(), blob.size());
    EnterCriticalSection(&g_lcs);
    g_offer = o;
    g_offerBlob = blob;
    g_offerDir = job->dir;
    LeaveCriticalSection(&g_lcs);
    TestLog("mods : liste de l'hote prete (MSCLoader %d, %d mod(s), %d fichier(s), %llu octets, empreinte %016llx)", (int)o.msc, (int)o.mods.size(), (int)o.files.size(),
            (unsigned long long)total, (unsigned long long)o.key);
    delete job;
    g_offerNew = true;
    g_offerBusy = 0;
    return 0;
}

static void SteamSendP2P(SteamId to, const std::string &d);
static void SyncOfferReset()
{
    EnterCriticalSection(&g_lcs);
    g_offer = SyncOffer();
    g_offerBlob.clear();
    LeaveCriticalSection(&g_lcs);
    g_offerSig.clear();
}
// Hote (fil de la fenetre, chaque seconde) : liste a refaire ? Nouvelle liste : envoyee aux invites (IP), ou son
// empreinte dans le salon (Steam : les invites la demandent).
static void SyncHostTick()
{
    if (g_offerNew.exchange(false)) {
        EnterCriticalSection(&g_lcs);
        std::string blob = g_offerBlob;
        uint64_t key = g_offer.key;
        if (!g_lobbySteam) for (auto &c : g_conns) SendMsg(c.s, Wr{ blob });
        LeaveCriticalSection(&g_lcs);
        if (g_lobbySteam) { char k[24]; sprintf_s(k, "%016llx", (unsigned long long)key); SSetLobby("mods", k); }
    }
    if (g_offerBusy) return;
    if (GetTickCount() - g_mscScanT > 3000) MscScan();
    bool msc = g_mscInstalled && g_mscOn && !g_mscDir.empty();
    std::string sig = msc ? "1" + Narrow(g_mscDir, CP_UTF8) : "0";
    OfferJob *job = new OfferJob;
    job->dir = g_mscDir;
    job->msc = msc;
    job->ver = Narrow(g_mscVer, CP_UTF8);
    if (msc) {
        for (auto &m : g_msc) {
            if (!ModShared(m.file)) continue;
            std::wstring id = m.known && !m.id.empty() ? m.id : m.file.substr(0, m.file.size() - 4);
            job->mods.push_back({ m.file, id });
            WIN32_FILE_ATTRIBUTE_DATA a;   // (la DLL et ses reglages : date et taille)
            for (const std::wstring &f : { g_mscDir + m.file, g_mscDir + L"Config\\Mod Settings\\" + id + L"\\settings.json" })
                if (GetFileAttributesExW(f.c_str(), GetFileExInfoStandard, &a)) {
                    char b[64];
                    sprintf_s(b, "|%lu.%lu.%lu", a.nFileSizeLow, a.ftLastWriteTime.dwLowDateTime, a.ftLastWriteTime.dwHighDateTime);
                    sig += b;
                }
            sig += "|" + Narrow(m.file, CP_UTF8);
        }
    }
    if (sig == g_offerSig) { delete job; return; }
    g_offerSig = sig;
    g_offerBusy = 1;
    HANDLE t = CreateThread(NULL, 0, OfferThread, job, 0, NULL);
    if (t) CloseHandle(t); else { delete job; g_offerBusy = 0; }
}

// Hote : un morceau demande (M_MODGET : empreinte de la liste, numero du fichier, position) -> M_MODDATA, ou rien
// (liste changee entre-temps : la nouvelle part de toute facon).
static bool SyncServe(const std::string &m, Wr &out)
{
    Rd r(m);
    r.u8();
    uint32_t lo = r.u32(), hi = r.u32();
    int idx = r.u16();
    uint32_t off = r.u32();
    if (!r.ok) return false;
    uint64_t key = lo | ((uint64_t)hi << 32);
    EnterCriticalSection(&g_lcs);
    bool ok = key == g_offer.key && idx < (int)g_offer.files.size();
    SyncFile f = ok ? g_offer.files[idx] : SyncFile();
    std::wstring path = g_offerDir + Widen(f.path, CP_UTF8);
    LeaveCriticalSection(&g_lcs);
    if (!ok || off > f.size) return false;
    uint32_t n = min(kSyncChunk, f.size - off);
    out.u8(M_MODDATA);
    out.u16(idx);
    out.u32(off);
    if (!n) return true;
    HANDLE fh = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, NULL, OPEN_EXISTING, 0, NULL);
    if (fh == INVALID_HANDLE_VALUE) return false;
    LARGE_INTEGER li; li.QuadPart = off;
    SetFilePointerEx(fh, li, NULL, FILE_BEGIN);
    size_t at = out.d.size();
    out.d.resize(at + n);
    DWORD got = 0;
    bool rd = ReadFile(fh, &out.d[at], n, &got, NULL) && got == n;
    CloseHandle(fh);
    return rd;
}

// ---- invite : la copie des mods de l'hote
enum { SY_NONE, SY_OFF, SY_CHECK, SY_ASK, SY_GET, SY_DONE, SY_FAIL };
static std::atomic<int> g_sync(SY_NONE);
static std::mutex g_syncMx;
static SyncOffer g_hostOffer;             // (sous g_syncMx)
static std::vector<int> g_syncNeed;       // fichiers a recevoir
static size_t g_syncNext;                 // prochain fichier a demander
static uint32_t g_syncNextOff;
static int g_syncInflight;
static std::map<int, HANDLE> g_syncParts;
static std::map<int, uint32_t> g_syncGotFile;
static std::atomic<uint64_t> g_syncGot(0), g_syncTotal(0);
static std::atomic<int> g_syncGen(0);
static std::atomic<DWORD> g_syncLastT(0);
static bool g_syncAccepted;               // TOUT ACCEPTER dans ce salon
static std::string g_syncWhy;
static uint64_t g_syncAskedKey;           // Steam : liste demandee pour cette empreinte

static std::wstring CoopModsDir() { std::wstring l = LocalDir(); return l.empty() ? L"" : l + L"mods-invite\\"; }
static bool ModsAuto() { const Opt *o = OptByKey("ModsAuto"); return o && OptGet(*o); }

static void SyncSendHost(const Wr &w)
{
    if (g_lobbySteam) SteamSendP2P(g_sHost, w.d);
    else GuestSend(w);
}
static void SyncCloseParts()   // (sous g_syncMx)
{
    for (auto &p : g_syncParts) CloseHandle(p.second);
    g_syncParts.clear();
}
static void SyncReset()
{
    std::lock_guard<std::mutex> lk(g_syncMx);
    SyncCloseParts();
    g_hostOffer = SyncOffer();
    g_syncNeed.clear();
    g_syncGotFile.clear();
    g_syncInflight = 0;
    g_syncAccepted = false;
    g_syncAskedKey = 0;
    g_syncGen++;
    g_sync = SY_NONE;
}

// Demandes en vol : jusqu'a kSyncWindow morceaux (sous g_syncMx).
static void SyncPumpLocked()
{
    if (g_sync != SY_GET) return;
    while (g_syncInflight < kSyncWindow && g_syncNext < g_syncNeed.size()) {
        int idx = g_syncNeed[g_syncNext];
        const SyncFile &f = g_hostOffer.files[idx];
        Wr w;
        w.u8(M_MODGET);
        w.u32((uint32_t)g_hostOffer.key); w.u32((uint32_t)(g_hostOffer.key >> 32));
        w.u16(idx);
        w.u32(g_syncNextOff);
        SyncSendHost(w);
        g_syncInflight++;
        g_syncNextOff += kSyncChunk;
        if (g_syncNextOff >= f.size) { g_syncNext++; g_syncNextOff = 0; }
    }
}

// Fin : DLL que l'hote n'a plus retirees de la copie (le reste ne gene pas MSCLoader).
static void SyncFinishLocked()
{
    std::wstring dir = CoopModsDir();
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((dir + L"*.dll").c_str(), &fd);
    if (h != INVALID_HANDLE_VALUE) {
        do {
            bool keep = false;
            for (auto &f : g_hostOffer.files) keep |= !_wcsicmp(Widen(f.path, CP_UTF8).c_str(), fd.cFileName);
            if (!keep) { DeleteFileW((dir + fd.cFileName).c_str()); TestLog("mods : %s retire de la copie (plus chez l'hote)", Narrow(fd.cFileName, CP_UTF8).c_str()); }
        } while (FindNextFileW(h, &fd));
        FindClose(h);
    }
    g_sync = SY_DONE;
    TestLog("mods : copie des mods de l'hote a jour (%d mod(s))", (int)g_hostOffer.mods.size());
}

static void SyncStartLocked()
{
    g_syncNext = 0;
    g_syncNextOff = 0;
    g_syncInflight = 0;
    g_syncGotFile.clear();
    uint64_t total = 0;
    for (int i : g_syncNeed) total += g_hostOffer.files[i].size;
    g_syncTotal = total;
    g_syncGot = 0;
    g_syncLastT = GetTickCount();
    if (g_syncNeed.empty()) { SyncFinishLocked(); return; }
    g_sync = SY_GET;
    TestLog("mods : telechargement de %d fichier(s), %llu octets", (int)g_syncNeed.size(), (unsigned long long)total);
    // fichiers vides : rien a demander
    std::wstring dir = CoopModsDir();
    for (int i : g_syncNeed) if (!g_hostOffer.files[i].size) {
        std::wstring p = dir + Widen(g_hostOffer.files[i].path, CP_UTF8);
        SHCreateDirectoryExW(NULL, DirOf(p).c_str(), NULL);
        HANDLE fh = CreateFileW(p.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL);
        if (fh != INVALID_HANDLE_VALUE) CloseHandle(fh);
    }
    std::vector<int> need;
    for (int i : g_syncNeed) if (g_hostOffer.files[i].size) need.push_back(i);
    g_syncNeed = need;
    if (g_syncNeed.empty()) { SyncFinishLocked(); return; }
    SyncPumpLocked();
}

// Comparaison de la copie avec la liste de l'hote (fil a part : empreintes des fichiers).
static DWORD WINAPI SyncCheckThread(void *p)
{
    int gen = (int)(intptr_t)p;
    SyncOffer o;
    {
        std::lock_guard<std::mutex> lk(g_syncMx);
        if (gen != g_syncGen) return 0;
        o = g_hostOffer;
    }
    std::wstring dir = CoopModsDir();
    std::vector<int> need;
    uint64_t bytes = 0;
    for (int i = 0; i < (int)o.files.size(); i++) {
        uint64_t h = 0;
        uint32_t sz = 0;
        if (FileFnv(dir + Widen(o.files[i].path, CP_UTF8), &h, &sz) && sz == o.files[i].size && h == o.files[i].hash) continue;
        need.push_back(i);
        bytes += o.files[i].size;
    }
    std::lock_guard<std::mutex> lk(g_syncMx);
    if (gen != g_syncGen) return 0;
    g_syncNeed = need;
    g_syncTotal = bytes;
    TestLog("mods : %d mod(s) chez l'hote, %d fichier(s) a recevoir (%llu octets)", (int)o.mods.size(), (int)need.size(), (unsigned long long)bytes);
    if (need.empty()) SyncFinishLocked();
    else if (g_syncAccepted || ModsAuto()) { g_syncAccepted = true; SyncStartLocked(); }
    else g_sync = SY_ASK;
    return 0;
}

// Invite : liste de l'hote recue (fil du salon IP, ou fil de la fenetre pour Steam).
static void SyncOnOffer(const std::string &m)
{
    SyncOffer o;
    if (!OfferParse(m, o)) { TestLog("mods : liste de l'hote illisible"); return; }
    std::lock_guard<std::mutex> lk(g_syncMx);
    if (o.key == g_hostOffer.key && g_sync != SY_NONE && g_sync != SY_FAIL) return;   // (la meme)
    SyncCloseParts();
    g_hostOffer = o;
    g_syncGen++;
    g_syncInflight = 0;
    if (!o.msc) { g_sync = SY_OFF; TestLog("mods : l'hote joue sans MSCLoader"); return; }
    g_sync = SY_CHECK;
    HANDLE t = CreateThread(NULL, 0, SyncCheckThread, (void *)(intptr_t)(int)g_syncGen, 0, NULL);
    if (t) CloseHandle(t);
}

// Invite : un morceau recu (M_MODDATA).
static void SyncOnData(const std::string &m)
{
    Rd r(m);
    r.u8();
    int idx = r.u16();
    uint32_t off = r.u32();
    if (!r.ok) return;
    uint32_t n = (uint32_t)(m.size() - r.p);
    std::lock_guard<std::mutex> lk(g_syncMx);
    if (g_sync != SY_GET || idx >= (int)g_hostOffer.files.size() || std::find(g_syncNeed.begin(), g_syncNeed.end(), idx) == g_syncNeed.end()) return;
    const SyncFile &f = g_hostOffer.files[idx];
    if ((uint64_t)off + n > f.size) return;
    g_syncLastT = GetTickCount();
    if (g_syncInflight > 0) g_syncInflight--;
    std::wstring dir = CoopModsDir(), path = dir + Widen(f.path, CP_UTF8), part = path + L".part";
    HANDLE &fh = g_syncParts[idx];
    if (!fh) {
        SHCreateDirectoryExW(NULL, DirOf(path).c_str(), NULL);
        fh = CreateFileW(part.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL);
    }
    if (fh == INVALID_HANDLE_VALUE) {
        g_syncParts.erase(idx);
        g_syncWhy = "ecriture impossible : " + Narrow(part, CP_UTF8);
        g_sync = SY_FAIL;
        TestLog("mods : %s", g_syncWhy.c_str());
        return;
    }
    LARGE_INTEGER li; li.QuadPart = off;
    SetFilePointerEx(fh, li, NULL, FILE_BEGIN);
    DWORD w = 0;
    if (n) WriteFile(fh, m.data() + r.p, n, &w, NULL);
    g_syncGot += n;
    uint32_t &got = g_syncGotFile[idx];
    got += n;
    if (got >= f.size) {   // fichier complet : verifie, puis a sa place
        CloseHandle(fh);
        g_syncParts.erase(idx);
        uint64_t h = 0;
        uint32_t sz = 0;
        if (!FileFnv(part, &h, &sz) || sz != f.size || h != f.hash) {
            DeleteFileW(part.c_str());
            g_syncWhy = "fichier abime : " + f.path;
            g_sync = SY_FAIL;
            TestLog("mods : %s", g_syncWhy.c_str());
            return;
        }
        if (!MoveFileExW(part.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING)) {
            g_syncWhy = "remplacement impossible : " + f.path + " (jeu ouvert ?)";
            g_sync = SY_FAIL;
            TestLog("mods : %s", g_syncWhy.c_str());
            return;
        }
        bool all = true;
        for (int i : g_syncNeed) all &= g_syncGotFile[i] >= g_hostOffer.files[i].size;
        if (all) { SyncFinishLocked(); return; }
    }
    SyncPumpLocked();
}

// TOUT ACCEPTER (et la prochaine fois sans demander). MSCLoader absent : installe aussi.
static void SyncAccept()
{
    if (const Opt *o = OptByKey("ModsAuto")) if (!OptGet(*o)) OptSet(*o, 1);
    MscScan();   // (MSCLoader installe ? releve a jour)
    if (!g_mscInstalled && g_mscInstall != 1 && !g_gameDir.empty() && !GameProcessRunning()) {
        g_mscInstall = 1;
        TestLog("mods : MSCLoader absent, installation pour les mods de l'hote");
        HANDLE t = CreateThread(NULL, 0, MscInstallThread, NULL, 0, NULL);
        if (t) CloseHandle(t);
    }
    std::lock_guard<std::mutex> lk(g_syncMx);
    g_syncAccepted = true;
    TestLog("mods : TOUT ACCEPTER");
    if (g_sync == SY_ASK) SyncStartLocked();
}
static void SyncRetry()
{
    std::lock_guard<std::mutex> lk(g_syncMx);
    if (g_sync != SY_FAIL) return;
    SyncCloseParts();
    g_syncGen++;
    g_sync = SY_CHECK;
    HANDLE t = CreateThread(NULL, 0, SyncCheckThread, (void *)(intptr_t)(int)g_syncGen, 0, NULL);
    if (t) CloseHandle(t);
}

// Fil de la fenetre : plus rien de l'hote depuis 20 s pendant un telechargement -> echec (REESSAYER).
static void SyncTick()
{
    if (g_sync == SY_GET && GetTickCount() - g_syncLastT > 20000) {
        std::lock_guard<std::mutex> lk(g_syncMx);
        if (g_sync == SY_GET) { SyncCloseParts(); g_syncWhy = "l'hote ne repond plus"; g_sync = SY_FAIL; TestLog("mods : %s", g_syncWhy.c_str()); }
    }
    if (g_lobby == LB_HOST) {
        static DWORD last;
        if (GetTickCount() - last >= 1000 || g_offerNew) { last = GetTickCount(); SyncHostTick(); }
    }
}

// Lancement en invite : quoi faire de MSCLoader. 0 : comme d'habitude (pas de salon : rejoindre en jeu...),
// 1 : sans MSCLoader, 2 : avec les mods de l'hote.
static int SyncLaunchKind()
{
    int s = g_sync;
    if (s == SY_NONE) return 0;
    bool installed = FileExists(g_gameDir + L"winhttp.dll") && FileExists(g_gameDir + L"doorstop_config.ini") && FileExists(MscManaged() + L"MSCLoader.Preloader.dll");
    return s == SY_DONE && installed ? 2 : 1;
}

// Copie de lancement de l'invite : comme PrepareMirror, mais Mods -> la copie des mods de l'hote (avec), et
// doorstop_config.ini a soi (mods=GF ; enabled=false sans). Jonctions refaites a chaque fois (dossier du jeu change...).
static bool IsJunction(const std::wstring &p) { DWORD a = GetFileAttributesW(p.c_str()); return a != INVALID_FILE_ATTRIBUTES && (a & FILE_ATTRIBUTE_DIRECTORY) && (a & FILE_ATTRIBUTE_REPARSE_POINT); }
static std::wstring GuestCopyDir() { std::wstring l = LocalDir(); return l.empty() ? L"" : l + L"invite\\My Winter Car\\"; }
static bool PrepareGuestCopy(const std::wstring &m, bool mods)
{
    SHCreateDirectoryExW(NULL, m.c_str(), NULL);
    std::wstring coop = CoopModsDir();
    if (mods) SHCreateDirectoryExW(NULL, coop.c_str(), NULL);
    auto link = [&](const std::wstring &n, const std::wstring &target) {
        std::wstring dst = m + n;
        if (IsJunction(dst)) RemoveDirectoryW(dst.c_str());   // (la jonction seule ; jamais ce qu'elle montre)
        else if (GetFileAttributesW(dst.c_str()) != INVALID_FILE_ATTRIBUTES) return true;   // vrai dossier : laisse
        if (MakeJunction(dst, target)) return true;
        TestLog("copie de l'invite : jonction %ls impossible (erreur %lu)", n.c_str(), GetLastError());
        return false;
    };
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((g_gameDir + L"*").c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return false;
    bool ok = true, hadMods = false;
    do {
        std::wstring n = fd.cFileName;
        if (n == L"." || n == L"..") continue;
        std::wstring src = g_gameDir + n, dst = m + n;
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) {
            bool isMods = !_wcsicmp(n.c_str(), L"Mods");
            hadMods |= isMods;
            std::wstring target = isMods && mods ? coop.substr(0, coop.size() - 1) : src;
            ok = link(n, target) && ok;
        } else if (CopyKind(n) && !SameFile(src, dst)) {
            if (!CopyFileW(src.c_str(), dst.c_str(), FALSE)) { TestLog("copie de l'invite : %ls impossible (erreur %lu)", n.c_str(), GetLastError()); ok = false; }
        }
    } while (FindNextFileW(h, &fd));
    FindClose(h);
    if (mods && !hadMods) ok = link(L"Mods", coop.substr(0, coop.size() - 1)) && ok;
    h = FindFirstFileW((m + L"*").c_str(), &fd);   // retires du jeu
    if (h != INVALID_HANDLE_VALUE) {
        do {
            std::wstring n = fd.cFileName;
            if ((fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) || !CopyKind(n)) continue;
            if (GetFileAttributesW((g_gameDir + n).c_str()) == INVALID_FILE_ATTRIBUTES) DeleteFileW((m + n).c_str());
        } while (FindNextFileW(h, &fd));
        FindClose(h);
    }
    HANDLE a = CreateFileW((m + L"steam_appid.txt").c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (a != INVALID_HANDLE_VALUE) { DWORD n; WriteFile(a, "4164420", 7, &n, NULL); CloseHandle(a); }
    std::wstring ini = m + L"doorstop_config.ini";
    if (FileExists(ini)) {
        WritePrivateProfileStringW(L"General", L"enabled", mods ? L"true" : L"false", ini.c_str());
        WritePrivateProfileStringW(L"MSCLoader", L"mods", L"GF", ini.c_str());
    }
    TestLog("copie de l'invite prete : %ls (%s)", m.c_str(), mods ? "mods de l'hote" : "sans MSCLoader");
    return ok;
}

// ---- dessin et clics (salon)
static const RectF kSyncBar(456, 392, 480, 30), kSyncBtn(782, 395, 150, 24);
static bool SyncBarShown()
{
    if (g_lobby == LB_HOST) return true;
    int s = g_sync;
    return g_lobby == LB_GUEST && s != SY_NONE;
}
static int SyncBtnAt(float x, float y)   // 1 : TOUT ACCEPTER / REESSAYER ; 2 : hote, ouvrir l'onglet MODS
{
    if (!SyncBarShown() || !kSyncBtn.Contains(x, y)) return 0;
    if (g_lobby == LB_HOST) return 2;
    int s = g_sync;
    return s == SY_ASK || s == SY_FAIL ? 1 : 0;
}
static std::wstring SizeText(uint64_t b)
{
    wchar_t t[32];
    if (b >= 1024 * 1024) swprintf_s(t, T(L"%.1f Mo", L"%.1f MB"), b / 1048576.0);
    else swprintf_s(t, T(L"%llu Ko", L"%llu KB"), (unsigned long long)((b + 1023) / 1024));
    if (g_fr) for (wchar_t *c = t; *c; c++) if (*c == L'.') *c = L',';   // (2,2 Mo)
    return t;
}
static void DrawSyncBar(Graphics &g, int hot)
{
    if (!SyncBarShown()) return;
    GraphicsPath bp; RoundRect(bp, kSyncBar, 10);
    SolidBrush bb(TH(card)); g.FillPath(&bb, &bp);
    Pen bpen(TH(choiceBorder), 1.0f); g.DrawPath(&bpen, &bp);
    std::wstring line, btn;
    Color c = kGrey;
    float prog = -1;
    if (g_lobby == LB_HOST) {
        EnterCriticalSection(&g_lcs);
        bool msc = g_offer.msc;
        int n = (int)g_offer.mods.size();
        uint64_t bytes = 0;
        for (auto &f : g_offer.files) bytes += f.size;
        LeaveCriticalSection(&g_lcs);
        wchar_t b[160];
        if (!msc) swprintf_s(b, L"%s", T(L"Mods : MSCLoader coupé, les invités jouent sans mods", L"Mods: MSCLoader off, guests play without mods"));
        else swprintf_s(b, T(L"Mods envoyés aux invités : %d (%s)", L"Mods sent to guests: %d (%s)"), n, SizeText(bytes).c_str());
        line = b;
        c = msc && n ? kInk : kGrey;
        btn = T(L"CHOISIR", L"CHOOSE");
    } else {
        std::lock_guard<std::mutex> lk(g_syncMx);
        int s = g_sync, n = (int)g_hostOffer.mods.size();
        wchar_t b[200];
        if (s == SY_OFF) { line = T(L"L'hôte joue sans mods : MSCLoader coupé pour cette partie", L"The host plays without mods: MSCLoader off for this game"); }
        else if (s == SY_CHECK) line = T(L"Mods de l'hôte : vérification…", L"Host's mods: checking…");
        else if (s == SY_ASK) {
            swprintf_s(b, T(L"Mods de l'hôte : %d · %s à télécharger", L"Host's mods: %d · %s to download"), n, SizeText(g_syncTotal).c_str());
            line = b; c = kInk; btn = T(L"TOUT ACCEPTER", L"ACCEPT ALL");
        } else if (s == SY_GET) {
            uint64_t t = g_syncTotal, got = g_syncGot;
            prog = t ? (float)((double)got / t) : 0;
            swprintf_s(b, T(L"Téléchargement des mods de l'hôte… %d %%", L"Downloading the host's mods… %d%%"), (int)(prog * 100));
            line = b; c = kInk;
        } else if (s == SY_DONE) {
            if (n) swprintf_s(b, T(L"Mods de l'hôte : %d prêt(s) ✓", L"Host's mods: %d ready ✓"), n);
            else swprintf_s(b, L"%s", T(L"L'hôte n'envoie aucun mod ✓", L"The host sends no mods ✓"));
            line = b; c = kAcc;
            if (g_mscInstall == 1) line += T(L" · installation de MSCLoader…", L" · installing MSCLoader…");
        } else if (s == SY_FAIL) {
            line = std::wstring(T(L"Mods de l'hôte : échec (", L"Host's mods: failed (")) + Widen(g_syncWhy, CP_UTF8) + L")";
            c = kRed; btn = T(L"RÉESSAYER", L"RETRY");
        }
    }
    float tw = btn.empty() ? kSyncBar.Width - 24 : kSyncBtn.X - kSyncBar.X - 18;
    Text(g, line, RectF(kSyncBar.X + 12, kSyncBar.Y, tw, prog >= 0 ? kSyncBar.Height - 6 : kSyncBar.Height), 11.5f, FontStyleBold, c, StringAlignmentNear);
    if (prog >= 0) DrawBar(g, RectF(kSyncBar.X + 12, kSyncBar.Y + kSyncBar.Height - 8, kSyncBar.Width - 24, 3), prog);
    if (!btn.empty()) {
        GraphicsPath p; RoundRect(p, kSyncBtn, kSyncBtn.Height / 2);
        bool h = hot == 1 || hot == 2;
        if (g_lobby == LB_HOST) { SolidBrush hb(h ? WithA(kAcc, 0.22f) : TH(field)); g.FillPath(&hb, &p); Pen pp(h ? kAcc : TH(choiceBorder), 1.1f); g.DrawPath(&pp, &p); }
        else { LinearGradientBrush lg(kSyncBtn, Mix(kAcc, Color(255, 255, 255, 255), h ? 0.15f : 0.0f), kAcc2, LinearGradientModeHorizontal); g.FillPath(&lg, &p); }
        Text(g, btn, kSyncBtn, 11, FontStyleBold, g_lobby == LB_HOST ? (h ? kAcc : kInk) : kOnAcc);
    }
}

// Hote (salon IP) : la liste a un invite qui arrive.
static void SyncWelcome(SOCKET s)
{
    EnterCriticalSection(&g_lcs);
    if (!g_offerBlob.empty()) SendMsg(s, Wr{ g_offerBlob });
    LeaveCriticalSection(&g_lcs);
}

// Salon Steam (fil de la fenetre) : paquets du canal des mods ; invite, liste a demander quand l'empreinte change.
static void SyncSteamTick()
{
    if (g_steamUp != 1 || !sapi::nw || !g_sLobby || (g_lobby != LB_HOST && g_lobby != LB_GUEST)) return;
    bool host = g_lobby == LB_HOST;
    static DWORD lastAccept;
    if (GetTickCount() - lastAccept > 1000) {   // (sessions pair-a-pair : membres du salon seulement)
        lastAccept = GetTickCount();
        int n = sapi::GetNumLobbyMembers(sapi::mm, g_sLobby);
        for (int i = 0; i < n && i < 32; i++) { SteamId u = sapi::GetLobbyMemberByIndex(sapi::mm, g_sLobby, i); if (u && u != g_steamMe) sapi::AcceptP2PSessionWithUser(sapi::nw, u); }
    }
    uint32_t size = 0;
    for (int k = 0; k < 64 && sapi::IsP2PPacketAvailable(sapi::nw, &size, kSyncChannel); k++) {
        std::string m(size, '\0');
        uint32_t got = 0;
        SteamId from = 0;
        if (!sapi::ReadP2PPacket(sapi::nw, size ? &m[0] : NULL, size, &got, &from, kSyncChannel)) break;
        m.resize(got);
        if (m.empty()) continue;
        int t = (uint8_t)m[0];
        if (host) {
            bool member = false;
            int n = sapi::GetNumLobbyMembers(sapi::mm, g_sLobby);
            for (int i = 0; i < n && i < 32; i++) member |= sapi::GetLobbyMemberByIndex(sapi::mm, g_sLobby, i) == from;
            if (!member) continue;
            if (t == M_MODSREQ) {
                EnterCriticalSection(&g_lcs);
                std::string blob = g_offerBlob;
                LeaveCriticalSection(&g_lcs);
                if (!blob.empty()) SteamSendP2P(from, blob);
            } else if (t == M_MODGET) { Wr w; if (SyncServe(m, w)) SteamSendP2P(from, w.d); }
        } else if (from == g_sHost) {
            if (t == M_MODS) SyncOnOffer(m);
            else if (t == M_MODDATA) SyncOnData(m);
        }
    }
    if (!host) {
        static DWORD lastAsk;
        std::string k = SLobbyData("mods");
        uint64_t key = k.empty() ? 0 : _strtoui64(k.c_str(), NULL, 16);
        bool have;
        { std::lock_guard<std::mutex> lk(g_syncMx); have = g_hostOffer.key == key; }
        if (key && !have && (key != g_syncAskedKey || GetTickCount() - lastAsk > 4000)) {
            g_syncAskedKey = key;
            lastAsk = GetTickCount();
            Wr w; w.u8(M_MODSREQ);
            SteamSendP2P(g_sHost, w.d);
        }
    }
}