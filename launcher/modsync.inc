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
//  - Contenu personnalisable du jeu (0.37, demande de JD) : CD1-3 (musiques, jaquette), Radio, Images (peinture de la
//    CORRIS car.png, mobylette, drapeau du garage, posters, autocollant, tissu, photo d'identite) et Extra (hockey).
//    L'hote coche ce qu'il envoie (volet "Contenu envoye aux invites" : COOP, ou CHOISIR dans le salon ; [Contenu]
//    de mwcoop-lanceur.ini). Meme liste que les mods, chemins "<dossier>\..." ("Mods\..." pour les mods). L'invite
//    les recoit dans %LOCALAPPDATA%\MWCoop\contenu-invite\ ; sa copie de lancement relie CD1-3, Radio et Extra a ceux
//    de l'hote, et Images a un dossier ou les images envoyees remplacent les siennes (le reste : les siennes).
#include <mutex>

// (M_MODS, M_MODGET, M_MODDATA, M_MODSREQ : avec les autres messages du salon)
static const uint32_t kSyncChunk = 48 * 1024;
static const int kSyncWindow = 8, kSyncChannel = 5;
struct SyncFile { std::string path; uint32_t size; uint64_t hash; };
struct SyncOffer { bool msc = false; std::string mscVer; std::vector<std::string> mods, cats; std::vector<SyncFile> files; uint64_t key = 0; };

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
// Meme chose, sans relire un fichier inchange (taille et date) : les CD font vite des centaines de Mo.
static std::mutex g_fnvMx;
static std::map<std::wstring, std::pair<uint64_t, uint64_t>> g_fnvCache;   // chemin -> (taille ^ date, empreinte)
static bool FileFnvCached(const std::wstring &f, uint64_t *h, uint32_t *size)
{
    WIN32_FILE_ATTRIBUTE_DATA a;
    if (!GetFileAttributesExW(f.c_str(), GetFileExInfoStandard, &a) || (a.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)) return false;
    uint64_t stamp = ((uint64_t)a.nFileSizeHigh << 32 | a.nFileSizeLow) * 1000003ULL ^ ((uint64_t)a.ftLastWriteTime.dwHighDateTime << 32 | a.ftLastWriteTime.dwLowDateTime);
    {
        std::lock_guard<std::mutex> lk(g_fnvMx);
        auto it = g_fnvCache.find(f);
        if (it != g_fnvCache.end() && it->second.first == stamp && !a.nFileSizeHigh) { *h = it->second.second; *size = a.nFileSizeLow; return true; }
    }
    if (!FileFnv(f, h, size)) return false;
    std::lock_guard<std::mutex> lk(g_fnvMx);
    g_fnvCache[f] = std::make_pair(stamp, *h);
    return true;
}

static std::string OfferBlob(const SyncOffer &o)
{
    Wr w;
    w.u8(M_MODS);
    w.u8(o.msc ? 1 : 0);
    w.str(o.mscVer);
    w.u16((int)o.mods.size());
    for (auto &m : o.mods) w.str(m);
    w.u16((int)o.cats.size());
    for (auto &c : o.cats) w.str(c);
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
    int nc = r.u16();
    for (int i = 0; i < nc && r.ok; i++) o.cats.push_back(r.str());
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
static std::wstring g_offerGame;          // (sous g_lcs) dossier du jeu de l'hote (contenu)
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

// ---- contenu personnalisable du jeu
struct ContentCat { const char *key; const wchar_t *fr, *en, *root, *files; bool def; };   // files : "a.png|b.png" (NULL : tout le dossier)
static const ContentCat kContent[] = {
    { "cd1", L"CD 1", L"CD 1", L"CD1", NULL, true },
    { "cd2", L"CD 2", L"CD 2", L"CD2", NULL, true },
    { "cd3", L"CD 3", L"CD 3", L"CD3", NULL, true },
    { "radio", L"Radio", L"Radio", L"Radio", NULL, true },
    { "peinture", L"Peinture de la CORRIS", L"CORRIS paint", L"Images", L"car.png", true },
    { "mobylette", L"Peinture de la mobylette", L"Moped paint", L"Images", L"jonnez_template.png", true },
    { "drapeau", L"Drapeau du garage", L"Garage flag", L"Images", L"flag_garage.png", true },
    { "posters", L"Posters", L"Posters", L"Images", L"poster1.png|poster2.png|poster3.png", true },
    { "autocollant", L"Autocollant de vitre", L"Window sticker", L"Images", L"window_sticker.png", true },
    { "tissu", L"Tissu", L"Fabric", L"Images", L"fabric.png", true },
    { "photo", L"Photo d'identit\u00E9", L"ID photo", L"Images", L"mugshot.png", false },
    { "hockey", L"\u00C9quipes de hockey", L"Hockey teams", L"Extra", NULL, true },
};
static const int kContentN = (int)(sizeof(kContent) / sizeof(kContent[0]));
static bool ContentOn(int i) { return GetPrivateProfileIntW(L"Contenu", Widen(kContent[i].key).c_str(), kContent[i].def ? 1 : 0, g_iniLauncher.c_str()) != 0; }
static void ContentSet(int i, bool on) { WritePrivateProfileStringW(L"Contenu", Widen(kContent[i].key).c_str(), on ? L"1" : L"0", g_iniLauncher.c_str()); }
static int ContentCatOf(const std::string &key) { for (int i = 0; i < kContentN; i++) if (key == kContent[i].key) return i; return -1; }
// Fichiers presents d'une categorie, chemins "<dossier>\<fichier>" (dossier du jeu : game, avec \ final).
static void ContentFilesTree(const std::wstring &base, const std::wstring &rel, std::vector<std::wstring> &out)
{
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((base + rel + L"\\*").c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return;
    do {
        std::wstring n = fd.cFileName;
        if (n == L"." || n == L"..") continue;
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) ContentFilesTree(base, rel + L"\\" + n, out);
        else if (out.size() < 2000) out.push_back(rel + L"\\" + n);
    } while (FindNextFileW(h, &fd));
    FindClose(h);
}
static std::vector<std::wstring> ContentFiles(int i, const std::wstring &game)
{
    std::vector<std::wstring> out;
    const ContentCat &c = kContent[i];
    if (!c.files) ContentFilesTree(game, c.root, out);
    else {
        std::wstring all = c.files;
        for (size_t a = 0; a <= all.size();) {
            size_t e = all.find(L'|', a);
            std::wstring f = std::wstring(c.root) + L"\\" + all.substr(a, e == std::wstring::npos ? std::wstring::npos : e - a);
            if (FileExists(game + f)) out.push_back(f);
            if (e == std::wstring::npos) break;
            a = e + 1;
        }
    }
    return out;
}

struct OfferJob { std::wstring dir, game; bool msc; std::string ver; std::vector<std::pair<std::wstring, std::wstring>> mods; std::vector<int> cats; };   // mods : (fichier, ID)
// Fichiers de base + rel (rel vide ou "\X\Y"), chemins dans la liste : prefix + "X\Y\<fichier>".
static void OfferAddTree(const std::wstring &base, const std::wstring &rel, const std::string &prefix, std::vector<SyncFile> &out, uint64_t &total)
{
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((base + rel + L"\\*").c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return;
    do {
        std::wstring n = fd.cFileName;
        if (n == L"." || n == L"..") continue;
        std::wstring r = rel + L"\\" + n;
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) { OfferAddTree(base, r, prefix, out, total); continue; }
        SyncFile f;
        f.path = prefix + Narrow(r.substr(1), CP_UTF8);   // (sans le \ de tete)
        if (out.size() >= 3000 || total > (2ull << 30) || !SyncPathOk(f.path) || !FileFnvCached(base + r, &f.hash, &f.size)) continue;
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
            f.path = "Mods\\" + Narrow(m.first, CP_UTF8);
            if (SyncPathOk(f.path) && FileFnvCached(job->dir + m.first, &f.hash, &f.size)) { o.files.push_back(f); total += f.size; }
            OfferAddTree(d, L"\\Assets\\" + m.second, "Mods\\", o.files, total);
            OfferAddTree(d, L"\\Config\\Mod Settings\\" + m.second, "Mods\\", o.files, total);
        }
        if (!job->mods.empty()) OfferAddTree(d, L"\\References", "Mods\\", o.files, total);
        WIN32_FIND_DATAW fd;   // MSCLoader lui-meme (console, page des reglages)
        HANDLE h = FindFirstFileW((job->dir + L"Assets\\MSCLoader_*").c_str(), &fd);
        if (h != INVALID_HANDLE_VALUE) {
            do if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) OfferAddTree(d, std::wstring(L"\\Assets\\") + fd.cFileName, "Mods\\", o.files, total);
            while (FindNextFileW(h, &fd));
            FindClose(h);
        }
    }
    for (int i : job->cats) {   // contenu du jeu coche
        size_t before = o.files.size();
        for (const std::wstring &rel : ContentFiles(i, job->game)) {
            SyncFile f;
            f.path = Narrow(rel, CP_UTF8);
            if (o.files.size() >= 3000 || !SyncPathOk(f.path) || !FileFnvCached(job->game + rel, &f.hash, &f.size)) continue;
            total += f.size;
            o.files.push_back(f);
        }
        if (o.files.size() > before) o.cats.push_back(kContent[i].key);
    }
    std::string blob = OfferBlob(o);
    while (blob.size() > 60000 && !o.files.empty()) { o.files.pop_back(); blob = OfferBlob(o); }   // (un message du salon : 64 Ko au plus)
    o.key = Fnv(blob.data(), blob.size());
    EnterCriticalSection(&g_lcs);
    g_offer = o;
    g_offerBlob = blob;
    g_offerDir = job->dir;
    g_offerGame = job->game;
    LeaveCriticalSection(&g_lcs);
    TestLog("mods : liste de l'hote prete (MSCLoader %d, %d mod(s), %d contenu(s), %d fichier(s), %llu octets, empreinte %016llx)", (int)o.msc, (int)o.mods.size(), (int)o.cats.size(),
            (int)o.files.size(), (unsigned long long)total, (unsigned long long)o.key);
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
    job->game = g_gameDir;
    job->msc = msc;
    for (int i = 0; i < kContentN; i++) {   // contenu coche : ses fichiers (taille, date)
        if (!ContentOn(i)) continue;
        job->cats.push_back(i);
        sig += std::string("#") + kContent[i].key;
        for (const std::wstring &rel : ContentFiles(i, g_gameDir)) {
            WIN32_FILE_ATTRIBUTE_DATA a;
            if (!GetFileAttributesExW((g_gameDir + rel).c_str(), GetFileExInfoStandard, &a)) continue;
            char b[64];
            sprintf_s(b, "|%lu.%lu.%lu", a.nFileSizeLow, a.ftLastWriteTime.dwLowDateTime, a.ftLastWriteTime.dwHighDateTime);
            sig += Narrow(rel, CP_UTF8) + b;
        }
    }
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
    std::wstring path = !f.path.compare(0, 5, "Mods\\") ? g_offerDir + Widen(f.path.substr(5), CP_UTF8) : g_offerGame + Widen(f.path, CP_UTF8);
    LeaveCriticalSection(&g_lcs);
    static int served;
    if (!ok || off > f.size) { TestLog("mods : demande refusee (fichier %d, position %u, liste %s)", idx, off, key == g_offer.key ? "la meme" : "autre"); return false; }
    if (!served++) TestLog("mods : premiere demande servie (%s)", f.path.c_str());
    uint32_t n = min(kSyncChunk, f.size - off);
    out.u8(M_MODDATA);
    out.u16(idx);
    out.u32(off);
    if (!n) return true;
    HANDLE fh = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, NULL, OPEN_EXISTING, 0, NULL);
    if (fh == INVALID_HANDLE_VALUE) { TestLog("mods : %s illisible (erreur %lu)", Narrow(path, CP_UTF8).c_str(), GetLastError()); return false; }
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
static std::wstring ContentDir() { std::wstring l = LocalDir(); return l.empty() ? L"" : l + L"contenu-invite\\"; }
// Ou un fichier de la liste va chez l'invite : "Mods\..." dans mods-invite, le reste dans contenu-invite.
static std::wstring SyncLocal(const std::string &p) { return !p.compare(0, 5, "Mods\\") ? CoopModsDir() + Widen(p.substr(5), CP_UTF8) : ContentDir() + Widen(p, CP_UTF8); }
static bool OfferHasRoot(const SyncOffer &o, const std::wstring &root)
{
    std::string r = Narrow(root, CP_UTF8) + "\\";
    for (auto &f : o.files) if (!_strnicmp(f.path.c_str(), r.c_str(), r.size())) return true;
    return false;
}
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

// Fin : ce que l'hote n'a plus est retire de la copie (DLL des mods ; fichiers des CD, de la radio, du hockey).
static void SyncPrune(const std::wstring &dir, const std::string &prefix, const wchar_t *pattern)
{
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((dir + pattern).c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return;
    do {
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) continue;
        std::string p = prefix + Narrow(fd.cFileName, CP_UTF8);
        bool keep = false;
        for (auto &f : g_hostOffer.files) keep |= !_stricmp(f.path.c_str(), p.c_str());
        if (!keep) { DeleteFileW((dir + fd.cFileName).c_str()); TestLog("mods : %s retire de la copie (plus chez l'hote)", p.c_str()); }
    } while (FindNextFileW(h, &fd));
    FindClose(h);
}
static void SyncFinishLocked()
{
    SyncPrune(CoopModsDir(), "Mods\\", L"*.dll");
    for (const wchar_t *r : { L"CD1", L"CD2", L"CD3", L"Radio", L"Extra" })
        if (OfferHasRoot(g_hostOffer, r)) SyncPrune(ContentDir() + r + L"\\", Narrow(r) + "\\", L"*");
    g_sync = SY_DONE;
    TestLog("mods : copie de l'hote a jour (%d mod(s), %d contenu(s))", (int)g_hostOffer.mods.size(), (int)g_hostOffer.cats.size());
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
    for (int i : g_syncNeed) if (!g_hostOffer.files[i].size) {
        std::wstring p = SyncLocal(g_hostOffer.files[i].path);
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
    std::vector<int> need;
    uint64_t bytes = 0;
    for (int i = 0; i < (int)o.files.size(); i++) {
        uint64_t h = 0;
        uint32_t sz = 0;
        if (FileFnvCached(SyncLocal(o.files[i].path), &h, &sz) && sz == o.files[i].size && h == o.files[i].hash) continue;
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
    if (!o.msc) TestLog("mods : l'hote joue sans MSCLoader");
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
    std::wstring path = SyncLocal(f.path), part = path + L".part";
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

// Invite : MSCLoader absent ici (celui choisi dans l'onglet MODS) : installe pour les mods de l'hote.
static void SyncEnsureMsc()
{
    MscScan();   // (MSCLoader installe ? releve a jour)
    if (!g_mscInstalled && g_mscInstall != 1 && !g_gameDir.empty() && !GameProcessRunning()) {
        g_mscInstall = 1;
        TestLog("mods : MSCLoader absent, installation pour les mods de l'hote");
        HANDLE t = CreateThread(NULL, 0, MscInstallThread, MscOwn() ? (void *)1 : NULL, 0, NULL);   // (MSCLoader de MWCoop : rien dans le jeu)
        if (t) CloseHandle(t);
    }
}

// TOUT ACCEPTER (et la prochaine fois sans demander). MSCLoader absent : installe aussi.
static void SyncAccept()
{
    if (const Opt *o = OptByKey("ModsAuto")) if (!OptGet(*o)) OptSet(*o, 1);
    SyncEnsureMsc();
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
    // Invite, mods de l'hote acceptes d'office (case "Accepter l'envoi de l'hote", ModsAuto) : MSCLoader installe ici aussi,
    // une fois par liste de l'hote. Avant : seulement par TOUT ACCEPTER, que la case cochee ne montre jamais (retour d'un
    // joueur, 08/10 : « les mods ne se synchronisent pas, j'ai coche l'envoi de l'hote »).
    if ((g_sync == SY_GET || g_sync == SY_DONE) && (g_syncAccepted || ModsAuto())) {
        static uint64_t mscFor;
        uint64_t key; bool msc;
        { std::lock_guard<std::mutex> lk(g_syncMx); key = g_hostOffer.key; msc = g_hostOffer.msc; }
        if (msc && key != mscFor) { mscFor = key; SyncEnsureMsc(); }
    }
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
    // (celui choisi dans l'onglet MODS : avant, seul l'officiel comptait -- un invite avec le MSCLoader de MWCoop partait sans
    // MSCLoader, les mods recus de l'hote jamais charges)
    bool installed = MscOwn() ? MscOwnInstalled() : MscOfficialInstalled();
    bool msc;
    { std::lock_guard<std::mutex> lk(g_syncMx); msc = g_hostOffer.msc; }
    return s == SY_DONE && msc && installed ? 2 : 1;
}

// Copie de lancement de l'invite : comme PrepareMirror, mais Mods -> la copie des mods de l'hote (avec), et
// doorstop_config.ini a soi (mods=GF ; enabled=false sans). Jonctions refaites a chaque fois (dossier du jeu change...).
static bool IsJunction(const std::wstring &p) { DWORD a = GetFileAttributesW(p.c_str()); return a != INVALID_FILE_ATTRIBUTES && (a & FILE_ATTRIBUTE_DIRECTORY) && (a & FILE_ATTRIBUTE_REPARSE_POINT); }
static std::wstring GuestCopyDir() { std::wstring l = LocalDir(); return l.empty() ? L"" : l + L"invite\\My Winter Car\\"; }
static bool PrepareGuestCopy(const std::wstring &m, bool mods)
{
    SHCreateDirectoryExW(NULL, m.c_str(), NULL);
    std::wstring coop = CoopModsDir(), cont = ContentDir();
    if (mods) SHCreateDirectoryExW(NULL, coop.c_str(), NULL);
    // contenu de l'hote recu : dossiers a relier a contenu-invite ; Images : les siennes la ou l'hote n'envoie rien
    std::vector<std::wstring> roots;
    std::vector<std::string> sentImages;
    if (g_sync == SY_DONE) {
        std::lock_guard<std::mutex> lk(g_syncMx);
        for (const wchar_t *r : { L"CD1", L"CD2", L"CD3", L"Radio", L"Extra", L"Images" }) if (OfferHasRoot(g_hostOffer, r)) roots.push_back(r);
        for (auto &f : g_hostOffer.files) if (!_strnicmp(f.path.c_str(), "Images\\", 7)) sentImages.push_back(f.path.substr(7));
    }
    auto hostRoot = [&](const std::wstring &n) { for (auto &r : roots) if (!_wcsicmp(r.c_str(), n.c_str())) return true; return false; };
    if (hostRoot(L"Images")) {
        std::wstring di = cont + L"Images\\";
        SHCreateDirectoryExW(NULL, di.c_str(), NULL);
        WIN32_FIND_DATAW fi;
        HANDLE hi = FindFirstFileW((g_gameDir + L"Images\\*.png").c_str(), &fi);
        if (hi != INVALID_HANDLE_VALUE) {
            do {
                bool sent = false;
                for (auto &s : sentImages) sent |= !_wcsicmp(Widen(s, CP_UTF8).c_str(), fi.cFileName);
                std::wstring src = g_gameDir + L"Images\\" + fi.cFileName, dst = di + fi.cFileName;
                if (!sent && !SameFile(src, dst)) CopyFileW(src.c_str(), dst.c_str(), FALSE);
            } while (FindNextFileW(hi, &fi));
            FindClose(hi);
        }
    }
    for (auto &r : roots) SHCreateDirectoryExW(NULL, (cont + r).c_str(), NULL);
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
            std::wstring target = isMods && mods ? coop.substr(0, coop.size() - 1) : hostRoot(n) ? cont + n : src;
            ok = link(n, target) && ok;
        } else if (CopyKind(n) && !SameFile(src, dst)) {
            if (!CopyFileW(src.c_str(), dst.c_str(), FALSE)) { TestLog("copie de l'invite : %ls impossible (erreur %lu)", n.c_str(), GetLastError()); ok = false; }
        }
    } while (FindNextFileW(h, &fd));
    FindClose(h);
    if (mods && !hadMods) ok = link(L"Mods", coop.substr(0, coop.size() - 1)) && ok;
    for (auto &r : roots) if (GetFileAttributesW((g_gameDir + r).c_str()) == INVALID_FILE_ATTRIBUTES) ok = link(r, cont + r) && ok;   // (absent du jeu)
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
    std::string rl;
    for (auto &r : roots) rl += " " + Narrow(r);
    TestLog("copie de l'invite prete : %ls (%s ; contenu de l'hote :%s)", m.c_str(), mods ? "mods de l'hote" : "sans MSCLoader", rl.empty() ? " aucun" : rl.c_str());
    return ok;
}

// ---- dessin et clics (salon)
static RectF kSyncBtn(0, 0, 0, 0);   // (place par DrawSyncCard)
static bool SyncBarShown()
{
    if (g_lobby == LB_HOST) return true;
    int s = g_sync;
    return g_lobby == LB_GUEST && s != SY_NONE;
}
static int SyncBtnAt(float x, float y)   // 1 : TOUT ACCEPTER / REESSAYER ; 2 : hote, ouvrir l'onglet MODS
{
    if (!SyncBarShown() || kSyncBtn.Width <= 0 || !kSyncBtn.Contains(x, y)) return 0;
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
// Carte "Envoye aux invites" (hote) ou "Envoi de l'hote" (invite), page du salon.
static void DrawSyncCard(Graphics &g, RectF c, int hot)
{
    kSyncBtn = RectF(0, 0, 0, 0);
    bool host = g_lobby == LB_HOST;
    Title(g, host ? T(L"Envoy\u00E9 aux invit\u00E9s", L"Sent to guests") : T(L"Envoi de l'h\u00F4te", L"Host's files"), RectF(c.X + 20, c.Y + 16, c.Width - 40, 24), 17, kInk);
    if (!SyncBarShown()) { Text(g, T(L"Rien pour l'instant", L"Nothing for now"), RectF(c.X + 20, c.Y + 52, c.Width - 40, 22), 13, FontStyleRegular, kGrey, StringAlignmentNear); return; }
    std::wstring line, btn;
    Color col = kGrey;
    float prog = -1;
    if (host) {
        EnterCriticalSection(&g_lcs);
        bool msc = g_offer.msc;
        int n = (int)g_offer.mods.size(), nc = (int)g_offer.cats.size();
        uint64_t bytes = 0;
        for (auto &f : g_offer.files) bytes += f.size;
        LeaveCriticalSection(&g_lcs);
        wchar_t b[200];
        if (!n && !nc) swprintf_s(b, L"%s", msc ? T(L"Rien n'est envoy\u00E9", L"Nothing is sent") : T(L"Rien (MSCLoader coup\u00E9 : invit\u00E9s sans mods)", L"Nothing (MSCLoader off: guests without mods)"));
        else swprintf_s(b, T(L"%d mod(s) \u00B7 %d contenu(s) \u00B7 %s", L"%d mod(s) \u00B7 %d item(s) \u00B7 %s"), n, nc, SizeText(bytes).c_str());
        line = b;
        col = n || nc ? kInk : kGrey;
        btn = T(L"Choisir", L"Choose");
    } else {
        std::lock_guard<std::mutex> lk(g_syncMx);
        int s = g_sync, n = (int)g_hostOffer.mods.size(), nc = (int)g_hostOffer.cats.size();
        wchar_t b[200];
        if (s == SY_CHECK) line = T(L"V\u00E9rification\u2026", L"Checking\u2026");
        else if (s == SY_ASK) {
            swprintf_s(b, T(L"%d mod(s) \u00B7 %d contenu(s) \u00B7 %s", L"%d mod(s) \u00B7 %d item(s) \u00B7 %s"), n, nc, SizeText(g_syncTotal).c_str());
            line = b; col = kInk; btn = T(L"TOUT ACCEPTER", L"ACCEPT ALL");
        } else if (s == SY_GET) {
            uint64_t t = g_syncTotal, got = g_syncGot;
            prog = t ? (float)((double)got / t) : 0;
            swprintf_s(b, T(L"T\u00E9l\u00E9chargement\u2026 %d %% (%s)", L"Downloading\u2026 %d%% (%s)"), (int)(prog * 100), SizeText(t).c_str());
            line = b; col = kInk;
        } else if (s == SY_DONE) {
            if (n || nc) swprintf_s(b, T(L"Pr\u00EAt \u2713  %d mod(s) \u00B7 %d contenu(s)", L"Ready \u2713  %d mod(s) \u00B7 %d item(s)"), n, nc);
            else swprintf_s(b, L"%s", T(L"L'h\u00F4te n'envoie rien \u2713", L"The host sends nothing \u2713"));
            line = b; col = kOk;
            if (!g_hostOffer.msc) line += T(L" \u00B7 sans MSCLoader", L" \u00B7 no MSCLoader");
            if (g_mscInstall == 1) line += T(L" \u00B7 installation de MSCLoader\u2026", L" \u00B7 installing MSCLoader\u2026");
        } else if (s == SY_FAIL) {
            line = std::wstring(T(L"\u00C9chec (", L"Failed (")) + Widen(g_syncWhy, CP_UTF8) + L")";
            col = kRed; btn = T(L"R\u00C9ESSAYER", L"RETRY");
        }
    }
    Para(g, line, RectF(c.X + 20, c.Y + 48, c.Width - 40, 40), 13, col);
    if (prog >= 0) DrawBar(g, RectF(c.X + 20, c.Y + 96, c.Width - 40, 5), prog);
    if (!btn.empty()) {
        kSyncBtn = RectF(c.X + 20, c.Y + c.Height - 54, c.Width - 40, 40);
        GraphicsPath p; RoundRect(p, kSyncBtn, 12);
        bool h = hot == 1 || hot == 2;
        if (host) { SolidBrush hb(h ? TH(btn2Hot) : TH(btn2)); g.FillPath(&hb, &p); Pen pp(TH(fieldBorder), 1); g.DrawPath(&pp, &p); }
        else { SolidBrush lg(kAcc); g.FillPath(&lg, &p); if (h) { SolidBrush hb(Color(55, 255, 255, 255)); g.FillPath(&hb, &p); } }
        Text(g, btn, kSyncBtn, 13, FontStyleBold, host ? kInk : kOnAcc);
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
    int ch = P2PRecvChannel();
    for (int k = 0; k < 64 && sapi::IsP2PPacketAvailable(sapi::nw, &size, ch); k++) {
        std::string m(size, '\0');
        uint32_t got = 0;
        SteamId from = 0;
        if (!sapi::ReadP2PPacket(sapi::nw, size ? &m[0] : NULL, size, &got, &from, ch)) break;
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
// ---- volet "Contenu envoye aux invites" (coordonnees du panneau des onglets, comme les autres volets)
struct ContentInfo { int n; uint64_t bytes; };
static ContentInfo g_contentInfo[16];
static DWORD g_contentScanT;
// (g_contentHot : drawer.inc)
static std::vector<int> g_drContent;                 // a l'ouverture du volet (ANNULER)
static std::map<std::wstring, Bitmap *> g_contentThumb;
static const float kContentRowH = 72;   // pas des tuiles (deux colonnes)
static RectF ContentTileRect(int i)
{
    float w = (kMain.Width - 48 - 12) / 2;
    return RectF(kMain.X + 24 + (i % 2) * (w + 12), kMain.Y + 84 + (i / 2) * kContentRowH, w, kContentRowH - 10);
}

static void ContentScan()
{
    g_contentScanT = GetTickCount();
    for (int i = 0; i < kContentN && i < 16; i++) {
        ContentInfo ci = { 0, 0 };
        if (!g_gameDir.empty())
            for (const std::wstring &rel : ContentFiles(i, g_gameDir)) {
                WIN32_FILE_ATTRIBUTE_DATA a;
                if (GetFileAttributesExW((g_gameDir + rel).c_str(), GetFileExInfoStandard, &a)) { ci.n++; ci.bytes += a.nFileSizeLow; }
            }
        g_contentInfo[i] = ci;
    }
}
static std::wstring ContentSummary()
{
    int on = 0;
    for (int i = 0; i < kContentN; i++) on += ContentOn(i);
    wchar_t b[32];
    swprintf_s(b, L"%d / %d", on, kContentN);
    return b;
}
static void ContentSnapshot(bool restore)
{
    if (!restore) { g_drContent.clear(); for (int i = 0; i < kContentN; i++) g_drContent.push_back(ContentOn(i)); return; }
    for (int i = 0; i < (int)g_drContent.size() && i < kContentN; i++) if (ContentOn(i) != (g_drContent[i] != 0)) ContentSet(i, g_drContent[i] != 0);
}
// Miniature d'une image du jeu (premiere image de la categorie), lue une fois.
static Bitmap *ContentThumb(int i)
{
    if (kContent[i].files == NULL || wcscmp(kContent[i].root, L"Images")) return NULL;
    std::vector<std::wstring> f = ContentFiles(i, g_gameDir);
    if (f.empty()) return NULL;
    auto it = g_contentThumb.find(f[0]);
    if (it != g_contentThumb.end()) return it->second;
    std::vector<unsigned char> d;
    Bitmap *b = ReadAll(g_gameDir + f[0], d) ? LoadPngMem(d.data(), d.size(), 8192, 8192) : NULL;
    g_contentThumb[f[0]] = b;
    return b;
}
static RectF ContentModsRow() { RectF last = ContentTileRect(kContentN - 1); return RectF(kMain.X + 24, last.Y + last.Height + 14, kMain.Width - 48, 52); }
// 5000+i : categorie i (la tuile entiere) ; 5100 : ligne des mods (page MODS).
static int ContentAt(float x, float y)
{
    for (int i = 0; i < kContentN; i++) if (ContentTileRect(i).Contains(x, y)) return 5000 + i;
    return ContentModsRow().Contains(x, y) ? 5100 : -1;
}
static void ContentClick(float x, float y)
{
    int h = ContentAt(x, y);
    if (h >= 5000 && h < 5100) { int i = h - 5000; ContentSet(i, !ContentOn(i)); }
    else if (h == 5100) { g_tab = TAB_MODS; g_mscPage = -1; g_scroll[TAB_MODS] = 0; MscScan(); }
}
// Page CONTENU : une tuile par categorie (miniature, taille, interrupteur), puis les mods, puis ANNULER / VALIDER.
static void DrawContent(Graphics &g)
{
    if (GetTickCount() - g_contentScanT > 2000) ContentScan();
    RectF c = kMain;
    Glass(c);
    Title(g, T(L"Contenu envoy\u00E9 aux invit\u00E9s", L"Content sent to guests"), RectF(c.X + 24, c.Y + 20, 600, 28), 18, kInk);
    Text(g, T(L"Tes CD, ta peinture, ton drapeau\u2026 tels que tu les as dans ton jeu, copi\u00E9s \u00E0 part chez les invit\u00E9s de ton salon.",
              L"Your CDs, paint, flag\u2026 as you have them in your game, copied apart to your lobby's guests."),
         RectF(c.X + 24, c.Y + 48, c.Width - 48, 22), 13, FontStyleRegular, kGrey, StringAlignmentNear);
    int on_ = 0;
    for (int i = 0; i < kContentN; i++) {
        RectF r = ContentTileRect(i);
        bool on = ContentOn(i), hot = g_contentHot == 5000 + i;
        on_ += on;
        const ContentInfo &ci = g_contentInfo[i];
        GraphicsPath rp; RoundRect(rp, r, 16);
        SolidBrush rb(hot ? TH(cardSel) : TH(card)); g.FillPath(&rb, &rp);
        Pen rpen(TH(choiceBorder), 1); g.DrawPath(&rpen, &rp);
        float a = on ? 1.0f : 0.55f;
        RectF tr(r.X + 10, r.Y + 9, 44, 44);
        GraphicsPath tp; RoundRect(tp, tr, 12);
        if (Bitmap *b = ContentThumb(i)) {   // miniature (images)
            GraphicsState st = g.Save();
            g.SetClip(&tp, CombineModeIntersect);
            g.DrawImage(b, tr);
            g.Restore(st);
        } else {   // CD, radio, hockey : une icone
            SolidBrush pb(WithA(kAcc, 0.22f)); g.FillPath(&pb, &tp);
            int ic = !wcsncmp(kContent[i].root, L"CD", 2) ? IC_MUSIC : !wcscmp(kContent[i].root, L"Radio") ? IC_RADIO : IC_HASH;
            Icon(g, ic, tr.X + 22, tr.Y + 22, 20, WithA(kInk, a), 1.9f);
        }
        Text(g, g_fr ? kContent[i].fr : kContent[i].en, RectF(r.X + 66, r.Y + 9, r.Width - 150, 24), 14.5f, FontStyleBold, WithA(kInk, a), StringAlignmentNear);
        std::wstring det;
        if (!ci.n) det = T(L"rien dans le jeu", L"nothing in the game");
        else if (!kContent[i].files) { wchar_t b[64]; swprintf_s(b, T(L"%d fichier(s) \u00B7 %s", L"%d file(s) \u00B7 %s"), ci.n, SizeText(ci.bytes).c_str()); det = b; }
        else det = SizeText(ci.bytes);
        Text(g, det, RectF(r.X + 66, r.Y + 32, r.Width - 150, 20), 12, FontStyleRegular, kGrey, StringAlignmentNear);
        UiToggle(g, RectF(r.X + r.Width - 66, r.Y + (r.Height - 28) / 2, 50, 28), on, hot);
    }
    RectF mr = ContentModsRow();
    bool mh = g_contentHot == 5100;
    GraphicsPath mp; RoundRect(mp, mr, 16);
    SolidBrush mb_(mh ? TH(cardSel) : TH(card)); g.FillPath(&mb_, &mp);
    Pen mpen(mh ? WithA(kAcc, 0.7f) : TH(choiceBorder), 1); g.DrawPath(&mpen, &mp);
    Icon(g, IC_PUZZLE, mr.X + 30, mr.Y + mr.Height / 2, 20, kAcc, 1.9f);
    int shared = 0;
    for (auto &m : g_msc) shared += ModShared(m.file);
    wchar_t mb[160];
    if (!g_mscInstalled) swprintf_s(mb, L"%s", T(L"Mods MSCLoader : non install\u00E9", L"MSCLoader mods: not installed"));
    else if (!g_mscOn) swprintf_s(mb, L"%s", T(L"Mods MSCLoader : coup\u00E9 (invit\u00E9s sans mods)", L"MSCLoader mods: off (guests without mods)"));
    else swprintf_s(mb, T(L"Mods MSCLoader : %d envoy\u00E9(s) sur %d", L"MSCLoader mods: %d sent of %d"), shared, (int)g_msc.size());
    Text(g, mb, RectF(mr.X + 54, mr.Y, mr.Width - 100, mr.Height), 14, FontStyleBold, kInk, StringAlignmentNear);
    Icon(g, IC_CHEVR, mr.X + mr.Width - 28, mr.Y + mr.Height / 2, 18, kInk, 2.2f);
    // bas : resume, annuler / valider
    float by = c.Y + c.Height - 66;
    Pen sep(TH(sep), 1); g.DrawLine(&sep, c.X + 24, by - 10, c.X + c.Width - 24, by - 10);
    Icon(g, IC_CHECK, c.X + 36, by + 24, 18, kOk, 2.4f);
    wchar_t sum[160];
    swprintf_s(sum, T(L"%d / %d \u00E9l\u00E9ments envoy\u00E9s \u00B7 copi\u00E9s \u00E0 part chez tes invit\u00E9s", L"%d / %d items sent \u00B7 copied apart to your guests"), on_, kContentN);
    Text(g, sum, RectF(c.X + 56, by, 520, 48), 13.5f, FontStyleBold, kInk, StringAlignmentNear);
    UiButton(g, RectF(c.X + c.Width - 24 - 300, by, 140, 48), T(L"ANNULER", L"CANCEL"), UI_PAGE_CANCEL, false);
    UiButton(g, RectF(c.X + c.Width - 24 - 148, by, 148, 48), T(L"VALIDER", L"OK"), UI_PAGE_OK, true);
}
