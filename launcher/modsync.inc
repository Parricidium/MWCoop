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
//  - My Summer Car (BETA, demande de JD, 19/10) : ce que son jeu lit vraiment (dossier du jeu et vidage ref\msc-v.250908-04 :
//    BuildString "Images/" + fichier). Communs : CD1-3 et Radio (trackN.ogg), car.png (peinture de la SATSUMA, ImportCarTexture),
//    jonnez.png (mobylette), flag_garage.png (drapeau dans le garage), poster1-3.png (chambre), window_sticker.png (vitre
//    arriere de la SATSUMA), mugshot.png (menu). Absents de MSC : fabric.png (tissu) et Extra (hockey) -- caches sur MSC.
//    Rien de propre a MSC. Coches a part ([ContenuMSC]) et contenu recu a part (contenu-invite-msc).
#include <mutex>

// (M_MODS, M_MODGET, M_MODDATA, M_MODSREQ : avec les autres messages du salon)
static const uint32_t kSyncChunk = 48 * 1024;
static const int kSyncWindow = 8, kSyncChannel = 5;
struct SyncFile { std::string path; uint32_t size; uint64_t hash; };
// dropped : dossiers que l'hote n'envoie pas, trop gros pour les plafonds (3000 fichiers / 2 Go) -- un circuit converti de
// Voyage le plus souvent (JD, 25/10 : « un dossier de destination part en entier ou pas du tout », et le salon le dit).
struct SyncOffer { bool msc = false; std::string mscVer; std::vector<std::string> mods, cats, dropped; std::vector<SyncFile> files; uint64_t key = 0; int expect = 0; };

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
        if (!SyncPathOk(f.path)) { LaunchLog("mods : chemin refuse dans la liste de l'hote (%s)", f.path.c_str()); return false; }
        o.files.push_back(f);
    }
    // Marques en fin de message, dans cet ordre : 0xC5 + nombre total de fichiers (liste trop longue pour un message : la suite
    // arrive en M_MODSX) ; 0xC6 + noms des dossiers pas envoyes (1.15.x). Les lanceurs d'avant ne lisent que la premiere.
    if (r.ok) {
        Rd t = r;
        while (t.ok && t.p < m.size()) {
            int k = t.u8();
            if (k == 0xC5) { int total = t.u16(); if (t.ok && total > (int)o.files.size()) o.expect = total; }
            else if (k == 0xC6) { int nd = t.u16(); for (int i = 0; i < nd && t.ok; i++) { std::string s = t.str(); if (t.ok) o.dropped.push_back(s); } }
            else break;
        }
    }
    o.key = Fnv(m.data(), m.size());
    return r.ok;
}

// Suite de la liste (M_MODSX) : empreinte du premier message, indice du premier fichier, fichiers.
static bool OfferMoreParse(const std::string &m, uint64_t &key, int &start, std::vector<SyncFile> &files)
{
    Rd r(m);
    if (r.u8() != M_MODSX) return false;
    uint32_t lo = r.u32(), hi = r.u32();
    key = lo | ((uint64_t)hi << 32);
    start = r.u16();
    int n = r.u16();
    for (int i = 0; i < n && r.ok; i++) {
        SyncFile f;
        f.path = r.str();
        f.size = r.u32();
        uint32_t a = r.u32(), b = r.u32();
        f.hash = a | ((uint64_t)b << 32);
        if (!r.ok) break;
        if (!SyncPathOk(f.path)) { LaunchLog("mods : chemin refuse dans la suite de la liste (%s)", f.path.c_str()); return false; }
        files.push_back(f);
    }
    return r.ok;
}

// ---- hote : la liste proposee
static SyncOffer g_offer;                 // (sous g_lcs)
static std::string g_offerBlob;           // (sous g_lcs) message M_MODS pret a envoyer
static std::vector<std::string> g_offerExtra;   // (sous g_lcs) suite de la liste (M_MODSX), envoyee juste apres
static std::wstring g_offerDir;           // (sous g_lcs) dossier Mods de l'hote
static std::wstring g_offerGame;          // (sous g_lcs) dossier du jeu de l'hote (contenu)
static std::wstring g_offerPaint;         // (sous g_lcs) peinture perso de la CORRIS envoyee a la place de Images\car.png (vide : celle du jeu)
static std::wstring g_offerModDir;       // (sous g_lcs) dossier du mod de l'hote (cartes de l'editeur)
static bool IsCarPng(const std::wstring &rel) { return !_wcsicmp(rel.c_str(), L"Images\\car.png"); }
// (sous g_lcs) fichier de l'hote d'un chemin de la liste : Mods\ (dossier des mods), cartes\ (dossier du mod), car.png
// (peinture perso), le reste dans le dossier du jeu.
static std::wstring OfferSource(const std::string &p)
{
    if (!p.compare(0, 5, "Mods\\")) return g_offerDir + Widen(p.substr(5), CP_UTF8);
    if (!_strnicmp(p.c_str(), "cartes\\", 7)) return g_offerModDir + Widen(p, CP_UTF8);
    std::wstring w = Widen(p, CP_UTF8);
    return !g_offerPaint.empty() && IsCarPng(w) ? g_offerPaint : g_offerGame + w;
}
static std::atomic<int> g_offerBusy(0);
static std::atomic<bool> g_offerNew(false);
static std::string g_offerSig;            // ce sur quoi la liste a ete faite (fil de la fenetre)

// Mods qui sautent le menu principal (Autoload de Roman266 : « Continue » des le menu). Chez un invite, le jeu chargeait
// alors sa copie d'avant de la sauvegarde de l'hote, avant meme la connexion : il jouait l'ancien monde par-dessus le
// nouveau (objets que l'un voit et pas l'autre ; forum, cbetoykaktyc, 13/10). Jamais envoyes aux invites (et retires de
// leur copie des mods comme une DLL que l'hote n'a plus) : le salon fait lui-meme « Continuer » au bon moment.
static bool MenuSkipper(const MscMod &m)
{
    return !_wcsicmp(m.file.c_str(), L"Autoload.dll") || (m.known && !_wcsicmp(m.id.c_str(), L"Autoload"));
}
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
// games : 1 My Winter Car, 2 My Summer Car (3 : les deux) ; frMsc / enMsc : nom sur MSC (NULL : le meme).
// mobylette : jonnez.png, l'image que le jeu lit (MWC et MSC) ; jonnez_template.png n'est que le modele a peindre.
// mod : dossier du mod (g_modRoot + MWCoop\), pas celui du jeu -- cartes : les .mwcmap de l'editeur de carte (MapPacks.cs),
// un invite ne charge que celles de l'hote (meme carte, collisions comprises, pour tout le monde).
struct ContentCat { const char *key; const wchar_t *fr, *en, *root, *files; bool def; int games; const wchar_t *frMsc, *enMsc; bool mod; };   // files : "a.png|b.png" (NULL : tout le dossier)
static const ContentCat kContent[] = {
    { "cd1", L"CD 1", L"CD 1", L"CD1", NULL, true, 3, NULL, NULL },
    { "cd2", L"CD 2", L"CD 2", L"CD2", NULL, true, 3, NULL, NULL },
    { "cd3", L"CD 3", L"CD 3", L"CD3", NULL, true, 3, NULL, NULL },
    { "radio", L"Radio", L"Radio", L"Radio", NULL, true, 3, NULL, NULL },
    { "peinture", L"Peinture de la CORRIS", L"CORRIS paint", L"Images", L"car.png", true, 3, L"Peinture de la SATSUMA", L"SATSUMA paint" },
    { "mobylette", L"Peinture de la mobylette", L"Moped paint", L"Images", L"jonnez.png", true, 3, NULL, NULL },
    { "drapeau", L"Drapeau du garage", L"Garage flag", L"Images", L"flag_garage.png", true, 3, NULL, NULL },
    { "posters", L"Posters", L"Posters", L"Images", L"poster1.png|poster2.png|poster3.png", true, 3, NULL, NULL },
    { "autocollant", L"Autocollant de vitre", L"Window sticker", L"Images", L"window_sticker.png", true, 3, NULL, NULL },
    { "tissu", L"Tissu", L"Fabric", L"Images", L"fabric.png", true, 1, NULL, NULL },   // (MSC ne lit pas fabric.png)
    { "photo", L"Photo d'identit\u00E9", L"ID photo", L"Images", L"mugshot.png", false, 3, NULL, NULL },
    { "hockey", L"\u00C9quipes de hockey", L"Hockey teams", L"Extra", NULL, true, 1, NULL, NULL },   // (pas de dossier Extra dans MSC)
    { "cartes", L"Cartes de l'\u00E9diteur", L"Editor maps", L"cartes", NULL, true, 3, NULL, NULL, true },
};
static const int kContentN = (int)(sizeof(kContent) / sizeof(kContent[0]));
// Categorie lue par le jeu choisi ; coches par jeu ([Contenu] pour MWC, [ContenuMSC] pour MSC, dans mwcoop-lanceur.ini).
static bool ContentInGame(int i) { return (kContent[i].games & (g_game == GAME_MSC ? 2 : 1)) != 0; }
static const wchar_t *ContentSection() { return g_game == GAME_MSC ? L"ContenuMSC" : L"Contenu"; }
static std::wstring ContentName(int i) { const ContentCat &c = kContent[i]; return g_game == GAME_MSC && c.frMsc ? T(c.frMsc, c.enMsc) : T(c.fr, c.en); }
static bool ContentOn(int i) { return ContentInGame(i) && GetPrivateProfileIntW(ContentSection(), Widen(kContent[i].key).c_str(), kContent[i].def ? 1 : 0, g_iniLauncher.c_str()) != 0; }
static void ContentSet(int i, bool on) { if (ContentInGame(i)) WritePrivateProfileStringW(ContentSection(), Widen(kContent[i].key).c_str(), on ? L"1" : L"0", g_iniLauncher.c_str()); }
static int ContentCount() { int n = 0; for (int i = 0; i < kContentN; i++) n += ContentInGame(i); return n; }
// Dossier de base d'une categorie (avec \ final) : celui du jeu (game), ou celui du mod (modDir : g_modRoot + MWCoop\).
static std::wstring ModContentDir() { return g_modRoot.empty() ? L"" : g_modRoot + L"MWCoop\\"; }
static const std::wstring &ContentBase(int i, const std::wstring &game, const std::wstring &modDir) { return kContent[i].mod ? modDir : game; }
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
static std::vector<std::wstring> ContentFiles(int i, const std::wstring &game, const std::wstring &modDir)
{
    std::vector<std::wstring> out;
    const ContentCat &c = kContent[i];
    if (c.mod) {   // (cartes : les paquets seulement, pas photo.png des essais ni un .tmp de l'editeur)
        if (modDir.empty()) return out;
        ContentFilesTree(modDir, c.root, out);
        out.erase(std::remove_if(out.begin(), out.end(), [](const std::wstring &f) { const wchar_t *e = wcsrchr(f.c_str(), L'.'); return !e || _wcsicmp(e, L".mwcmap"); }), out.end());
        return out;
    }
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

struct OfferJob { std::wstring dir, game, modDir, paint; bool msc; std::string ver; std::vector<std::pair<std::wstring, std::wstring>> mods; std::vector<int> cats; std::vector<std::wstring> priv; };   // mods : (fichier, ID) ; priv : ID et noms des mods gardes pour soi
// Une unite de l'offre : un dossier (ou une DLL) qui part en entier ou pas du tout (JD, 25/10). prio : ordre de passage
// sous les plafonds -- 0 les DLL, 1 References et MSCLoader_*, 2 les dossiers des mods (Assets hors destinations\, reglages),
// 3 le contenu du jeu, 4 les circuits convertis (Assets\<X>\destinations\<circuit>) ; a prio egale, les plus petits d'abord.
struct OfferUnit { std::string name, cat; int prio = 0; std::vector<SyncFile> files; uint64_t bytes = 0; };
// Fichiers de base + rel (rel vide ou "\X\Y") dans u, chemins prefix + "X\Y\<fichier>" ; skip : un sous-dossier de rel lui-meme
// laisse de cote (destinations\ : chaque circuit est sa propre unite).
static void OfferCollect(const std::wstring &base, const std::wstring &rel, const std::string &prefix, OfferUnit &u, const wchar_t *skip = NULL)
{
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((base + rel + L"\\*").c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return;
    do {
        std::wstring n = fd.cFileName;
        if (n == L"." || n == L"..") continue;
        std::wstring r = rel + L"\\" + n;
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) { if (!skip || _wcsicmp(n.c_str(), skip)) OfferCollect(base, r, prefix, u); continue; }
        SyncFile f;
        f.path = prefix + Narrow(r.substr(1), CP_UTF8);   // (sans le \ de tete)
        if (!SyncPathOk(f.path) || !FileFnvCached(base + r, &f.hash, &f.size)) continue;
        u.bytes += f.size;
        u.files.push_back(f);
    } while (FindNextFileW(h, &fd));
    FindClose(h);
}
// Messages de la liste : le premier (M_MODS, ce qui tient dans 60 Ko, plus la marque 0xC5 si la suite existe), puis la
// suite (M_MODSX). o.key : empreinte du premier ; o.files : la liste entiere, dans l'ordre.
static void OfferSplit(SyncOffer &o, std::string &blob, std::vector<std::string> &extra)
{
    blob = OfferBlob(o);
    size_t full = o.files.size();
    std::vector<SyncFile> cut;
    // Liste trop longue pour un message du salon (64 Ko) : le premier message en garde ce qui y tient (les lanceurs d'avant
    // le lisent tel quel), suivi d'une marque (0xC5, nombre total) ; le reste part en messages M_MODSX (memes indices :
    // les demandes de fichiers restent valables). Avant : le reste etait coupe, les invites n'avaient pas ces fichiers.
    Wr dt;   // (dossiers pas envoyes : marque 0xC6, apres 0xC5 -- 40 noms au plus)
    if (!o.dropped.empty()) {
        int nd = min(40, (int)o.dropped.size());
        dt.u8(0xC6); dt.u16(nd);
        for (int i = 0; i < nd; i++) dt.str(o.dropped[i].substr(0, 80));
    }
    while (blob.size() + dt.d.size() + 3 > 59990 && !o.files.empty()) { cut.push_back(o.files.back()); o.files.pop_back(); blob = OfferBlob(o); }
    std::reverse(cut.begin(), cut.end());
    if (!cut.empty()) { Wr t; t.u8(0xC5); t.u16((int)full); blob += t.d; }
    blob += dt.d;
    o.key = Fnv(blob.data(), blob.size());
    for (size_t i = 0; i < cut.size();) {
        Wr w; w.u8(M_MODSX); w.u32((uint32_t)o.key); w.u32((uint32_t)(o.key >> 32)); w.u16((int)(o.files.size() + i));
        size_t n0 = w.d.size(); w.u16(0);
        int n = 0;
        while (i < cut.size() && w.d.size() < 59000) { const SyncFile &f = cut[i]; w.str(f.path); w.u32(f.size); w.u32((uint32_t)f.hash); w.u32((uint32_t)(f.hash >> 32)); i++; n++; }
        w.d[n0] = (char)(n & 0xFF); w.d[n0 + 1] = (char)((n >> 8) & 0xFF);
        extra.push_back(w.d);
    }
    for (auto &f : cut) o.files.push_back(f);
    if (!cut.empty()) LaunchLog("mods : liste longue (%d fichiers) : %d dans le premier message, %d en %d message(s) de plus", (int)full, (int)(full - cut.size()), (int)cut.size(), (int)extra.size());
}

static DWORD WINAPI OfferThread(void *p)
{
    OfferJob *job = (OfferJob *)p;
    SyncOffer o;
    o.msc = job->msc;
    o.mscVer = job->ver;
    uint64_t total = 0;
    // D'abord toutes les unites, sans plafond ; puis les plafonds (3000 fichiers, 2 Go), unite par unite dans l'ordre de
    // passage : une unite qui ne tient plus est laissee entiere, nommee dans o.dropped et au journal (JD, 25/10 : avant, des
    // fichiers sautes un par un sans rien dire -- un circuit converti a moitie envoye).
    std::vector<OfferUnit> units;
    auto unit = [&](const std::string &name, int prio) -> OfferUnit & { units.push_back(OfferUnit()); units.back().name = name; units.back().prio = prio; return units.back(); };
    if (o.msc) {
        std::wstring d = job->dir.substr(0, job->dir.size() - 1);   // (sans le \ final : chemins relatifs "X\Y")
        // Assets\<n> : le dossier sans destinations\, puis chaque circuit de destinations\ a part (Voyage)
        auto assets = [&](const std::wstring &n) {
            OfferCollect(d, L"\\Assets\\" + n, "Mods\\", unit(Narrow(n, CP_UTF8), 2), L"destinations");
            WIN32_FIND_DATAW dd;
            HANDLE dh = FindFirstFileW((d + L"\\Assets\\" + n + L"\\destinations\\*").c_str(), &dd);
            if (dh == INVALID_HANDLE_VALUE) return;
            do {
                std::wstring c = dd.cFileName;
                if (!(dd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) || c == L"." || c == L"..") continue;
                if (c.size() > 8 && !_wcsicmp(c.c_str() + c.size() - 8, L".nouveau")) continue;   // (conversion en cours ou annulee : ignoree par le mod -- conversion.log, a la racine, ne part pas non plus)
                OfferCollect(d, L"\\Assets\\" + n + L"\\destinations\\" + c, "Mods\\", unit(Narrow(c, CP_UTF8), 4));
            } while (FindNextFileW(dh, &dd));
            FindClose(dh);
        };
        for (auto &m : job->mods) {
            o.mods.push_back(Narrow(m.first, CP_UTF8));
            OfferUnit &u = unit(Narrow(m.first, CP_UTF8), 0);
            SyncFile f;
            f.path = "Mods\\" + Narrow(m.first, CP_UTF8);
            if (SyncPathOk(f.path) && FileFnvCached(job->dir + m.first, &f.hash, &f.size)) { u.files.push_back(f); u.bytes += f.size; }
            assets(m.second);
            OfferCollect(d, L"\\Config\\Mod Settings\\" + m.second, "Mods\\", unit(Narrow(m.second, CP_UTF8) + " (settings)", 2));
        }
        if (!job->mods.empty()) OfferCollect(d, L"\\References", "Mods\\", unit("References", 1));
        // Autres dossiers de Assets : un mod peut ranger les siens sous un autre nom que son ID (Peugeot205Rallye :
        // Assets\Peugeot205, 29 Mo -- l'invite n'avait pas la voiture, 11/10). Tous partent, sauf MSCLoader_* (plus bas)
        // et ceux d'un mod garde pour soi (son ID ou le nom de sa DLL, ou un nom qui commence par l'un d'eux).
        if (!job->mods.empty()) {
            WIN32_FIND_DATAW ad;
            HANDLE ah = FindFirstFileW((job->dir + L"Assets\\*").c_str(), &ad);
            if (ah != INVALID_HANDLE_VALUE) {
                do {
                    std::wstring n = ad.cFileName;
                    if (!(ad.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) || n == L"." || n == L".." || !_wcsnicmp(n.c_str(), L"MSCLoader_", 10)) continue;
                    bool mine = false, sent = false;
                    for (auto &p : job->priv) mine |= !_wcsnicmp(n.c_str(), p.c_str(), p.size()) || !_wcsnicmp(n.c_str(), p.c_str(), n.size());
                    for (auto &m : job->mods) sent |= !_wcsicmp(n.c_str(), m.second.c_str());
                    if (mine || sent) continue;
                    assets(n);
                    LaunchLog("mods : dossier Assets\\%s envoye aussi (pas au nom d'un mod)", Narrow(n, CP_UTF8).c_str());
                } while (FindNextFileW(ah, &ad));
                FindClose(ah);
            }
        }
        WIN32_FIND_DATAW fd;   // MSCLoader lui-meme (console, page des reglages)
        HANDLE h = FindFirstFileW((job->dir + L"Assets\\MSCLoader_*").c_str(), &fd);
        if (h != INVALID_HANDLE_VALUE) {
            do if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) OfferCollect(d, std::wstring(L"\\Assets\\") + fd.cFileName, "Mods\\", unit(Narrow(fd.cFileName, CP_UTF8), 1));
            while (FindNextFileW(h, &fd));
            FindClose(h);
        }
    }
    for (int i : job->cats) {   // contenu du jeu coche : une unite par categorie
        OfferUnit &u = unit(kContent[i].key, 3);
        u.cat = kContent[i].key;
        for (const std::wstring &rel : ContentFiles(i, job->game, job->modDir)) {
            SyncFile f;
            f.path = Narrow(rel, CP_UTF8);
            std::wstring src = !job->paint.empty() && IsCarPng(rel) ? job->paint : ContentBase(i, job->game, job->modDir) + rel;
            if (!SyncPathOk(f.path) || !FileFnvCached(src, &f.hash, &f.size)) continue;
            u.bytes += f.size;
            u.files.push_back(f);
        }
    }
    // Ordre de la liste (et des plafonds) : DLL des mods d'abord -- le premier message du salon en garde le debut (retour de JD,
    // 11/10 : une coupe a la fin enlevait la DLL de Peugeot205Rallye sans rien dire) --, puis References et MSCLoader, les
    // dossiers des mods du plus petit au plus gros, le contenu du jeu, les circuits convertis en dernier.
    std::stable_sort(units.begin(), units.end(), [](const OfferUnit &a, const OfferUnit &b) { return a.prio != b.prio ? a.prio < b.prio : a.bytes < b.bytes; });
    for (OfferUnit &u : units) {
        if (u.files.empty()) continue;
        if (o.files.size() + u.files.size() > 3000 || total + u.bytes > (2ull << 30)) {
            o.dropped.push_back(u.name);
            LaunchLog("mods : %s PAS envoye (%d fichier(s), %llu octets : au-dela de 3000 fichiers ou 2 Go au total)", u.name.c_str(), (int)u.files.size(), (unsigned long long)u.bytes);
            continue;
        }
        for (auto &f : u.files) o.files.push_back(f);
        total += u.bytes;
        if (!u.cat.empty()) o.cats.push_back(u.cat);
    }    std::string blob;
    std::vector<std::string> extra;
    OfferSplit(o, blob, extra);
    EnterCriticalSection(&g_lcs);
    g_offer = o;
    g_offerBlob = blob;
    g_offerExtra = extra;
    g_offerDir = job->dir;
    g_offerGame = job->game;
    g_offerModDir = job->modDir;
    g_offerPaint = job->paint;
    LeaveCriticalSection(&g_lcs);
    std::string names;
    for (auto &m : o.mods) names += " " + m;
    LaunchLog("mods : liste de l'hote prete (MSCLoader %d, %d mod(s) :%s, %d contenu(s), %d fichier(s), %llu octets, empreinte %016llx)", (int)o.msc, (int)o.mods.size(), names.c_str(), (int)o.cats.size(),
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
    g_offerExtra.clear();
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
        if (!g_lobbySteam) for (auto &c : g_conns) { SendMsg(c.s, Wr{ blob }); for (auto &x : g_offerExtra) SendMsg(c.s, Wr{ x }); }
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
    job->modDir = ModContentDir();
    job->paint = PaintOn() ? PaintFile() : L"";
    job->msc = msc;
    for (int i = 0; i < kContentN; i++) {   // contenu coche : ses fichiers (taille, date)
        if (!ContentOn(i)) continue;
        job->cats.push_back(i);
        sig += std::string("#") + kContent[i].key;
        for (const std::wstring &rel : ContentFiles(i, g_gameDir, job->modDir)) {
            WIN32_FILE_ATTRIBUTE_DATA a;
            if (!GetFileAttributesExW((!job->paint.empty() && IsCarPng(rel) ? job->paint : ContentBase(i, g_gameDir, job->modDir) + rel).c_str(), GetFileExInfoStandard, &a)) continue;
            char b[64];
            sprintf_s(b, "|%lu.%lu.%lu", a.nFileSizeLow, a.ftLastWriteTime.dwLowDateTime, a.ftLastWriteTime.dwHighDateTime);
            sig += Narrow(rel, CP_UTF8) + b;
        }
    }
    job->ver = Narrow(g_mscVer, CP_UTF8);
    if (msc) {
        for (auto &m : g_msc) {
            // (mod coupe ici : ni charge ni envoye -- et ses dossiers (Assets, reglages) gardes aussi : retour de JD, 12/10, NDSerra
            // coupe partait quand meme, 1 Go d'Assets « pas au nom d'un mod »)
            if (m.off || !ModShared(m.file) || MenuSkipper(m)) { job->priv.push_back(m.file.substr(0, m.file.size() - 4)); if (m.known && !m.id.empty()) job->priv.push_back(m.id); continue; }
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
    std::wstring path = OfferSource(f.path);
    LeaveCriticalSection(&g_lcs);
    static int served;
    if (!ok || off > f.size) { LaunchLog("mods : demande refusee (fichier %d, position %u, liste %s)", idx, off, key == g_offer.key ? "la meme" : "autre"); return false; }
    if (!served++) LaunchLog("mods : premiere demande servie (%s)", f.path.c_str());
    uint32_t n = min(kSyncChunk, f.size - off);
    out.u8(M_MODDATA);
    out.u16(idx);
    out.u32(off);
    if (!n) return true;
    HANDLE fh = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, NULL, OPEN_EXISTING, 0, NULL);
    if (fh == INVALID_HANDLE_VALUE) { LaunchLog("mods : %s illisible (erreur %lu)", Narrow(path, CP_UTF8).c_str(), GetLastError()); return false; }
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
// Gros envoi (JD, 25/10 : les circuits convertis de Voyage, 270 a 350 Mo) : au-dela, l'invite accepte d'abord, en voyant la
// taille et le temps estime (sauf « Tout accepter de cet hote »). Debit : celui du dernier telechargement ([Lanceur]
// DebitMods, Ko/s), sinon 0,34 Mo/s par Steam (vu chez un joueur) ou 1 Mo/s par IP.
static const uint64_t kSyncAskBytes = 100ull << 20;
static ULONGLONG g_syncStartT;            // debut du telechargement (mesure du debit)
static double SyncRate()   // octets par seconde
{
    int kb = GetPrivateProfileIntW(L"Lanceur", L"DebitMods", 0, g_iniLauncher.c_str());
    return kb > 0 ? kb * 1024.0 : g_steamNet ? 0.34 * 1048576 : 1048576.0;
}
static std::wstring SyncEta(uint64_t bytes)
{
    double s = bytes / SyncRate();
    wchar_t b[64];
    if (s < 60) return T(L"moins d'une minute", L"under a minute");
    swprintf_s(b, T(L"environ %d min", L"about %d min"), (int)(s / 60 + 0.5));
    return b;
}
static std::string g_syncWhy;
static uint64_t g_syncAskedKey;           // Steam : liste demandee pour cette empreinte

static std::wstring CoopModsDir() { std::wstring l = LocalDir(); return l.empty() ? L"" : l + L"mods-invite\\"; }
// (My Summer Car : un dossier a part -- les images d'un hote de MWC, la CORRIS de car.png, n'y restent pas pour la SATSUMA)
static std::wstring ContentDir() { std::wstring l = LocalDir(); return l.empty() ? L"" : l + (g_game == GAME_MSC ? L"contenu-invite-msc\\" : L"contenu-invite\\"); }
// Ou un fichier de la liste va chez l'invite : "Mods\..." dans mods-invite, le reste dans contenu-invite.
static std::wstring SyncLocal(const std::string &p) { return !p.compare(0, 5, "Mods\\") ? CoopModsDir() + Widen(p.substr(5), CP_UTF8) : ContentDir() + Widen(p, CP_UTF8); }
static bool OfferHasRoot(const SyncOffer &o, const std::wstring &root)
{
    std::string r = Narrow(root, CP_UTF8) + "\\";
    for (auto &f : o.files) if (!_strnicmp(f.path.c_str(), r.c_str(), r.size())) return true;
    return false;
}
static bool ModsAuto()
{
    if (g_lobby == LB_GUEST || g_lobby == LB_CONNECTING) {   // (choix fait dans le salon, pour ce salon seulement : salon.inc)
        auto it = g_lobbyOpt.find("ModsAuto");
        if (it != g_lobbyOpt.end()) return it->second != 0;
    }
    const Opt *o = OptByKey("ModsAuto");
    return o && OptGet(*o);
}

static void SyncSendHost(const Wr &w)
{
    if (g_lateSteam || (g_lobbySteam && !g_lateSync)) SteamSendP2P(g_sHost, w.d);
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
    if (g_testSlowMs) {   // (essai /lent : un morceau a la fois, espaces -- la reception dure)
        static DWORD lastReq;
        if (g_syncInflight > 0 || GetTickCount() - lastReq < g_testSlowMs || g_syncNext >= g_syncNeed.size()) return;
        lastReq = GetTickCount();
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
        return;
    }
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

// Fin : ce que l'hote n'a plus est retire de la copie (DLL des mods ; fichiers des CD, de la radio, du hockey, cartes de l'editeur).
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
        if (!keep) { DeleteFileW((dir + fd.cFileName).c_str()); LaunchLog("mods : %s retire de la copie (plus chez l'hote)", p.c_str()); }
    } while (FindNextFileW(h, &fd));
    FindClose(h);
}
static void SyncFinishLocked()
{
    SyncPrune(CoopModsDir(), "Mods\\", L"*.dll");
    for (const wchar_t *r : { L"CD1", L"CD2", L"CD3", L"Radio", L"Extra", L"cartes" })   // (cartes : une carte retiree par l'hote ne reste pas chez l'invite)
        if (OfferHasRoot(g_hostOffer, r)) SyncPrune(ContentDir() + r + L"\\", Narrow(r) + "\\", L"*");
    if (g_sync == SY_GET && g_syncStartT && g_syncGot > (20ull << 20)) {   // (debit de ce telechargement, pour la prochaine estimation)
        double secs = (GetTickCount64() - g_syncStartT) / 1000.0;
        if (secs > 1) {
            int kb = (int)(g_syncGot / secs / 1024);
            WritePrivateProfileStringW(L"Lanceur", L"DebitMods", std::to_wstring(kb).c_str(), g_iniLauncher.c_str());
            LaunchLog("mods : debit mesure %d Ko/s (%llu octets en %.0f s)", kb, (unsigned long long)g_syncGot.load(), secs);
        }
    }
    g_sync = SY_DONE;
    LaunchLog("mods : copie de l'hote a jour (%d mod(s), %d contenu(s))", (int)g_hostOffer.mods.size(), (int)g_hostOffer.cats.size());
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
    g_syncStartT = GetTickCount64();
    LaunchLog("mods : telechargement de %d fichier(s), %llu octets", (int)g_syncNeed.size(), (unsigned long long)total);
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
    uint64_t bytes = 0, reused = 0;
    int reusedN = 0;
    // Deja chez l'invite (demande de JD, 11/10 : « s'il a deja un mod que j'ai aussi, ca lui telecharge quand meme ») : un
    // fichier identique a celui de l'hote (meme taille, meme empreinte : la meme version exacte) dans SES mods (son dossier
    // Mods de MSCLoader) ou son jeu (CD, radio...) est recopie dans la copie de l'hote au lieu d'etre telecharge. Ses
    // propres dossiers ne sont que lus ; un fichier d'une autre version vient de l'hote.
    std::wstring ownMods = MscModsDir();
    for (int i = 0; i < (int)o.files.size(); i++) {
        uint64_t h = 0;
        uint32_t sz = 0;
        std::wstring dst = SyncLocal(o.files[i].path);
        if (FileFnvCached(dst, &h, &sz) && sz == o.files[i].size && h == o.files[i].hash) continue;
        std::wstring rel = Widen(o.files[i].path, CP_UTF8);
        std::wstring own = !o.files[i].path.compare(0, 5, "Mods\\") ? ownMods + rel.substr(5) : g_gameDir + rel;
        if (_wcsicmp(own.c_str(), dst.c_str()) && FileFnvCached(own, &h, &sz) && sz == o.files[i].size && h == o.files[i].hash) {
            size_t sl = dst.find_last_of(L'\\');
            if (sl != std::wstring::npos) SHCreateDirectoryExW(NULL, dst.substr(0, sl).c_str(), NULL);
            SetFileAttributesW(dst.c_str(), FILE_ATTRIBUTE_NORMAL);
            if (CopyFileW(own.c_str(), dst.c_str(), FALSE)) { reusedN++; reused += sz; continue; }
        }
        need.push_back(i);
        bytes += o.files[i].size;
    }
    if (reusedN) LaunchLog("mods : %d fichier(s) deja chez l'invite, a la meme version (%llu octets) : recopies de ses mods, pas telecharges", reusedN, (unsigned long long)reused);
    std::lock_guard<std::mutex> lk(g_syncMx);
    if (gen != g_syncGen) return 0;
    g_syncNeed = need;
    g_syncTotal = bytes;
    std::string names;
    for (auto &m : o.mods) names += " " + m;
    LaunchLog("mods : %d mod(s) chez l'hote :%s, %d fichier(s) a recevoir (%llu octets)", (int)o.mods.size(), names.c_str(), (int)need.size(), (unsigned long long)bytes);
    // (les 10 premiers, nommes : Mr_Hokly, 24/10, « les mods sont retelecharges » -- des reglages reecrits a chaque partie ?)
    for (size_t k = 0; k < need.size() && k < 10; k++)
        LaunchLog("mods : a recevoir %s (%u octets)", o.files[need[k]].path.c_str(), o.files[need[k]].size);
    // Mods de l'hote : acceptes d'office (demande de JD, 10/10 : « si quelqu'un rejoint une partie avec des mods, que ca lui
    // installe MSCLoader et lui prenne les mods » ; retour de Menos : un ami sans les mods entrait quand meme). Seul ce qui
    // n'est que du contenu (CD, images) demande encore TOUT ACCEPTER, sauf ModsAuto.
    // (gros envoi : a accepter, avec la taille et le temps estime -- sauf « Tout accepter de cet hote »)
    if (!o.mods.empty() && bytes <= kSyncAskBytes) g_syncAccepted = true;
    if (bytes > kSyncAskBytes && !g_syncAccepted && !ModsAuto()) LaunchLog("mods : gros envoi (%llu octets, %s) : a accepter", (unsigned long long)bytes, Narrow(SyncEta(bytes), CP_UTF8).c_str());
    if (need.empty()) SyncFinishLocked();
    else if (g_syncAccepted || ModsAuto()) { g_syncAccepted = true; SyncStartLocked(); }
    else g_sync = SY_ASK;
    return 0;
}

// Invite : liste de l'hote recue (fil du salon IP, ou fil de la fenetre pour Steam).
static void SyncStartOffer(SyncOffer o);
static SyncOffer g_offerWait;   // (sous g_syncMx) liste longue en attente de sa suite
static std::map<int, std::vector<SyncFile>> g_offerWaitParts;
static SyncOffer *g_offerTestWant;   // (auto-test /testmodsx : liste attendue, rien n'est telecharge)
static int g_offerTestResult = -1;
// Auto-test (/testmodsx N) : N fichiers factices, decoupes comme l'hote, relus comme l'invite (suite avant le debut et a
// l'envers), liste comparee.
static int OfferSelfTest(int n)
{
    SyncOffer o;
    o.msc = true; o.mscVer = "1.4"; o.mods.push_back("GrosMod"); o.mods.push_back("PetitMod");
    for (int i = 0; i < n; i++) { SyncFile f; char b[200]; sprintf_s(b, "Mods\\Assets\\GrosMod\\Textures\\dossier%02d\\fichier_numero_%05d.png", i / 100, i); f.path = b; f.size = 1000 + i; f.hash = 0x123456789ULL * (i + 1); o.files.push_back(f); }
    std::string blob; std::vector<std::string> extra;
    OfferSplit(o, blob, extra);
    TestLog("testmodsx : %d fichiers -> premier message %d octets, %d message(s) de plus (le plus gros %d octets)", n, (int)blob.size(), (int)extra.size(), extra.empty() ? 0 : (int)std::max_element(extra.begin(), extra.end(), [](const std::string &a, const std::string &b) { return a.size() < b.size(); })->size());
    SyncOffer want = o;
    g_offerTestWant = &want;
    for (int i = (int)extra.size() - 1; i >= 1; i--) SyncOnOfferMore(extra[i]);
    SyncOnOffer(blob);
    if (!extra.empty()) SyncOnOfferMore(extra[0]);
    g_offerTestWant = nullptr;
    SyncOffer old;
    bool oldOk = OfferParse(blob, old);
    TestLog("testmodsx : lanceur d'avant (premier message seul) : %s, %d fichiers", oldOk ? "lisible" : "ILLISIBLE", (int)old.files.size());
    return g_offerTestResult == 1 && oldOk ? 0 : 1;
}

static void SyncOnOffer(const std::string &m)
{
    SyncOffer o;
    if (!OfferParse(m, o)) { LaunchLog("mods : liste de l'hote illisible"); return; }
    if (o.expect > 0)
    {
        std::lock_guard<std::mutex> lk(g_syncMx);
        if (o.key == g_hostOffer.key && g_sync != SY_NONE && g_sync != SY_FAIL) return;   // (la meme, deja complete)
        if (o.key != g_offerWait.key) g_offerWaitParts.clear();
        g_offerWait = o;
        LaunchLog("mods : liste longue de l'hote (%d fichiers), %d recus, la suite arrive", o.expect, (int)o.files.size());
    }
    else { SyncStartOffer(o); return; }
    SyncOnOfferMore(std::string());   // (morceaux arrives avant le debut)
}
static void SyncOnOfferMore(const std::string &m)
{
    SyncOffer done;
    {
        std::lock_guard<std::mutex> lk(g_syncMx);
        if (!m.empty())
        {
            uint64_t key; int start; std::vector<SyncFile> files;
            if (!OfferMoreParse(m, key, start, files)) { LaunchLog("mods : suite de la liste illisible"); return; }
            if (g_offerWait.expect > 0 && key != g_offerWait.key) return;   // (autre liste)
            g_offerWaitParts[start] = files;
            if (g_offerWait.expect == 0) { g_offerWait.key = key; return; }   // (avant le premier message : gardee)
        }
        if (g_offerWait.expect == 0) return;
        for (auto it = g_offerWaitParts.find((int)g_offerWait.files.size()); it != g_offerWaitParts.end(); it = g_offerWaitParts.find((int)g_offerWait.files.size()))
        {
            for (auto &f : it->second) g_offerWait.files.push_back(f);
            g_offerWaitParts.erase(it);
        }
        if ((int)g_offerWait.files.size() < g_offerWait.expect) return;
        done = g_offerWait;
        done.expect = 0;
        g_offerWait = SyncOffer();
        g_offerWaitParts.clear();
        LaunchLog("mods : liste longue de l'hote complete (%d fichiers)", (int)done.files.size());
    }
    SyncStartOffer(done);
}
static void SyncStartOffer(SyncOffer o)
{
    if (g_offerTestWant)
    {
        bool same = o.files.size() == g_offerTestWant->files.size() && o.key == g_offerTestWant->key;
        for (size_t i = 0; same && i < o.files.size(); i++) same = o.files[i].path == g_offerTestWant->files[i].path && o.files[i].size == g_offerTestWant->files[i].size && o.files[i].hash == g_offerTestWant->files[i].hash;
        g_offerTestResult = same ? 1 : 0;
        TestLog("testmodsx : liste recue %d fichiers, attendue %d : %s", (int)o.files.size(), (int)g_offerTestWant->files.size(), same ? "IDENTIQUE" : "DIFFERENTE");
        return;
    }
    std::lock_guard<std::mutex> lk(g_syncMx);
    if (o.key == g_hostOffer.key && g_sync != SY_NONE && g_sync != SY_FAIL) return;   // (la meme)
    SyncCloseParts();
    g_hostOffer = o;
    g_syncGen++;
    g_syncInflight = 0;
    if (!o.msc) LaunchLog("mods : l'hote joue sans MSCLoader");
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
        LaunchLog("mods : %s", g_syncWhy.c_str());
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
            LaunchLog("mods : %s", g_syncWhy.c_str());
            return;
        }
        if (!MoveFileExW(part.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING)) {
            g_syncWhy = "remplacement impossible : " + f.path + " (jeu ouvert ?)";
            g_sync = SY_FAIL;
            LaunchLog("mods : %s", g_syncWhy.c_str());
            return;
        }
        bool all = true;
        for (int i : g_syncNeed) all &= g_syncGotFile[i] >= g_hostOffer.files[i].size;
        if (all) { SyncFinishLocked(); return; }
    }
    SyncPumpLocked();
}

// Invite : MSCLoader absent ici (celui choisi dans l'onglet MODS) : installe pour les mods de l'hote. ask : d'abord la question
// (demande de JD, 12/10 : « Ce serveur utilise des mods, voulez-vous installer MSCLoader ? », Oui / Non ; oui : celui de MWCoop,
// sans rien changer au jeu ; non : l'invite joue sans les mods, une seule question par liste de l'hote).
static void SyncEnsureMsc(bool ask = false)
{
    MscScan();   // (MSCLoader installe ? releve a jour)
    if (!g_mscInstalled && g_mscInstall != 1 && !g_gameDir.empty() && !GameProcessRunning()) {
        bool own = MscOwn();
        if (ask) {
            int r = MessageBoxW(g_wnd, T(L"Ce salon utilise des mods (MSCLoader).\n\nInstaller MSCLoader (version Coop de MWCoop, install\u00E9e \u00E0 part) et recevoir les mods de l'h\u00F4te ?\n\nNon : tu rejoins sans les mods (des objets ou des voitures de l'h\u00F4te peuvent manquer).",
                                         L"This lobby uses mods (MSCLoader).\n\nInstall MSCLoader (MWCoop's Coop version, installed apart) and get the host's mods?\n\nNo: you join without the mods (some of the host's items or cars may be missing)."),
                                 L"MWCoop", MB_YESNO | MB_ICONQUESTION);
            if (r != IDYES) { LaunchLog("mods : MSCLoader absent, installation refusee (l'invite joue sans les mods)"); SetStatus(K_WARN, T(L"Sans MSCLoader : tu joues sans les mods de l'h\u00F4te", L"Without MSCLoader: you play without the host's mods")); return; }
            own = true;
            if (g_mscOwnPref < 0) { g_mscOwnPref = 1; WritePrivateProfileStringW(L"Lanceur", L"MSCLoaderMWCoop", L"1", g_iniLauncher.c_str()); }
        }
        g_mscInstall = 1;
        LaunchLog("mods : MSCLoader absent, installation pour les mods de l'hote%s", own ? " (version Coop)" : "");
        SetStatus(K_NORMAL, T(L"Installation de MSCLoader (version Coop)\u2026", L"Installing MSCLoader (Coop version)\u2026"));
        HANDLE t = CreateThread(NULL, 0, MscInstallThread, own ? (void *)1 : NULL, 0, NULL);   // (MSCLoader de MWCoop : rien dans le jeu)
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
    LaunchLog("mods : TOUT ACCEPTER");
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

// Envoi de l'hote en cours de verification ou de telechargement (accepte) : l'invite n'est pas encore pret.
static bool SyncBusy() { int s = g_sync; return s == SY_CHECK || s == SY_GET; }

// Lancement en invite a retenir : l'hote envoie des mods et ils ne sont pas encore la (verification, reception, MSCLoader
// en cours d'installation). Le jeu partirait sans eux ; il part a la fin de la reception (SyncTick, g_launchAfterSync).
static bool SyncLaunchPending()
{
    int s = g_sync;
    if (s == SY_NONE || s == SY_OFF || s == SY_FAIL) return false;
    bool mods, msc;
    { std::lock_guard<std::mutex> lk(g_syncMx); mods = !g_hostOffer.mods.empty(); msc = g_hostOffer.msc; }
    if (!mods) return false;
    return s != SY_DONE || (msc && g_mscInstall == 1);
}

// lancement.ini de l'invite : le contenu recu de l'hote (dossier, categories, images), lu directement par le jeu
// (Contenu.cs : les lecteurs du jeu pointes sur ce dossier), quelle que soit la facon dont le jeu demarre.
static std::string ContentLaunchLines()
{
    if (g_sync != SY_DONE || ContentDir().empty()) return "";
    std::string roots, images;
    {
        std::lock_guard<std::mutex> lk(g_syncMx);
        for (const wchar_t *r : { L"CD1", L"CD2", L"CD3", L"Radio", L"Extra", L"Images", L"cartes" })
            if (OfferHasRoot(g_hostOffer, r)) roots += (roots.empty() ? "" : ",") + Narrow(r);
        for (auto &f : g_hostOffer.files) if (!_strnicmp(f.path.c_str(), "Images\\", 7)) images += (images.empty() ? "" : ",") + f.path.substr(7);
    }
    if (roots.empty()) return "";
    LaunchLog("contenu de l'hote recu : %s%s%s", roots.c_str(), images.empty() ? "" : " ; images ", images.c_str());
    return "ContenuDossier=" + Narrow(ContentDir(), CP_UTF8) + "\r\nContenuRecu=" + roots + "\r\nContenuImages=" + images + "\r\n";
}

// Fil de la fenetre : plus rien de l'hote depuis 20 s pendant un telechargement -> echec (REESSAYER).
static void SyncTick()
{
    // Invite : PRET demande pendant l'envoi -> pret une fois recu ; envoi qui reprend (l'hote a change ses fichiers) alors
    // qu'il etait pret -> plus pret, jusqu'a la fin.
    if (g_lobby != LB_GUEST) g_wantReady = false;   // (salon quitte : rien a garder pour le suivant)
    else {
        if (g_meReady && SyncBusy()) { GuestToggleReady(); g_wantReady = true; SetStatus(K_NORMAL, T(L"L'h\u00F4te envoie de nouveaux fichiers : pr\u00EAt d\u00E8s qu'ils sont re\u00E7us", L"The host is sending new files: ready as soon as they're received")); }
        else if (g_wantReady && !g_meReady && !SyncBusy()) {
            int s = g_sync;
            if (s == SY_FAIL) { g_wantReady = false; SetStatus(K_WARN, T(L"Envoi de l'h\u00F4te rat\u00E9 : R\u00C9ESSAYER, puis PR\u00CAT", L"Host's files failed: RETRY, then READY")); }
            else if (s != SY_ASK) { g_wantReady = false; GuestToggleReady(); SetStatus(K_OK, T(L"Envoi de l'h\u00F4te re\u00E7u : tu es pr\u00EAt", L"Host's files received: you're ready")); }
        }
    }
    // Invite, mods de l'hote acceptes d'office (case "Accepter l'envoi de l'hote", ModsAuto) : MSCLoader installe ici aussi,
    // une fois par liste de l'hote. Avant : seulement par TOUT ACCEPTER, que la case cochee ne montre jamais (retour d'un
    // joueur, 08/10 : « les mods ne se synchronisent pas, j'ai coche l'envoi de l'hote »).
    if ((g_sync == SY_GET || g_sync == SY_DONE) && (g_syncAccepted || ModsAuto())) {
        static uint64_t mscFor;
        uint64_t key; bool msc;
        { std::lock_guard<std::mutex> lk(g_syncMx); key = g_hostOffer.key; msc = g_hostOffer.msc; }
        if (msc && key != mscFor) { mscFor = key; SyncEnsureMsc(true); }
    }
    if (g_testSlowMs && g_sync == SY_GET) { std::lock_guard<std::mutex> lk(g_syncMx); SyncPumpLocked(); }   // (essai /lent)
    // Invite : son avancement a l'hote (LANCER attend la fin ; demande de JD, 13/10).
    {
        static int sentDl = -2;
        static DWORD sentT;
        if (g_lobby != LB_GUEST) sentDl = -2;
        else if (g_sync != SY_NONE) {   // (liste de l'hote traitee : avant, l'hote attend -- PeerSyncPending)
            int dl = -1;
            if (SyncBusy() || SyncLaunchPending()) { uint64_t tot = g_syncTotal, got = g_syncGot; dl = tot ? (int)min<uint64_t>(99, got * 100 / tot) : 0; }
            if (dl != sentDl && (dl < 0 || sentDl < 0 || GetTickCount() - sentT > 1500)) {
                sentDl = dl; sentT = GetTickCount();
                if (g_lobbySteam) SSetMember("dl", dl < 0 ? "ok" : std::to_string(dl));
                else { Wr w; w.u8(M_SYNCST); w.u8(dl < 0 ? 255 : dl); GuestSend(w); }
            }
        }
    }
    // Invite en retard : la liste de l'hote doit d'abord arriver (rien en 12 s : rien a recevoir, ou hote d'avant).
    if (g_lateSync && g_sync == SY_NONE && GetTickCount() - g_lateT > 12000)
        LateFail(T(L"L'hôte n'envoie pas de mods : « Rejoindre en jeu »", L"The host isn't sending mods: \"Join in game\""));
    if (g_launchAfterSync && (g_lobby == LB_GUEST || (g_lateSync && g_sync != SY_NONE)) && !SyncLaunchPending()) {
        g_launchAfterSync = false;
        bool late = g_lateSync;
        LateEnd();
        if (g_sync == SY_FAIL) {
            SetStatus(K_WARN, T(L"Mods de l'hôte pas reçus : RÉESSAYER, puis rejoindre", L"Host's mods not received: RETRY, then join"));
            if (late) g_joinFallback = true;
        }
        else { LaunchLog("mods : mods de l'hote recus, lancement de l'invite%s", late ? " (partie deja lancee)" : ""); Launch(MODE_GUEST); }
    }
    if (g_sync == SY_GET && GetTickCount() - g_syncLastT > 20000) {
        std::lock_guard<std::mutex> lk(g_syncMx);
        if (g_sync == SY_GET) { SyncCloseParts(); g_syncWhy = "l'hote ne repond plus"; g_sync = SY_FAIL; LaunchLog("mods : %s", g_syncWhy.c_str()); }
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
static std::wstring GuestCopyDir() { std::wstring l = LocalDir(); return l.empty() ? L"" : l + L"invite\\" + GM().steamDir + L"\\"; }
// Fichiers de base de MSCLoader (Mods\Assets\MSCLoader_* : console, menu des mods...) dans la copie des mods de l'hote :
// ceux de l'invite, faits pour SON MSCLoader. Avant, seuls ceux de l'hote y etaient -- absents chez lui, ou d'une autre
// version (MSCLoader 1.4 cherche MSCLoader_Settings\settingsui.unity3d), l'invite avait « LoadBundle() Error: File not
// found ... settingsui.unity3d » puis « Asset files for [INTERNAL] Mod Menu not found » (retour d'un joueur, 11/10).
static int CopyTreeSimple(const std::wstring &src, const std::wstring &dst)
{
    SHCreateDirectoryExW(NULL, dst.c_str(), NULL);
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((src + L"\\*").c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return 0;
    int n = 0;
    do {
        std::wstring f = fd.cFileName;
        if (f == L"." || f == L"..") continue;
        std::wstring s = src + L"\\" + f, d = dst + L"\\" + f;
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) continue;   // (jamais a travers une jonction)
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) { n += CopyTreeSimple(s, d); continue; }
        if (SameFile(s, d)) continue;
        SetFileAttributesW(d.c_str(), FILE_ATTRIBUTE_NORMAL);
        if (CopyFileW(s.c_str(), d.c_str(), FALSE)) n++;
    } while (FindNextFileW(h, &fd));
    FindClose(h);
    return n;
}
static void GuestMscAssets(const std::wstring &coop)
{
    std::wstring src = MscModsDir() + L"Assets\\";
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((src + L"MSCLoader_*").c_str(), &fd);
    int dirs = 0, files = 0;
    if (h != INVALID_HANDLE_VALUE) {
        do if ((fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) && !(fd.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT)) {
            dirs++;
            files += CopyTreeSimple(src + fd.cFileName, coop + L"Assets\\" + fd.cFileName);
        } while (FindNextFileW(h, &fd));
        FindClose(h);
    }
    bool menu = FileExists(coop + L"Assets\\MSCLoader_Settings\\settingsui.unity3d") || FileExists(coop + L"Assets\\MSCLoader_Settings\\settings.unity3d");
    LaunchLog("copie de l'invite : fichiers de base de MSCLoader de l'invite (%s) : %d dossier(s), %d fichier(s) copies ; menu des mods %s",
              Narrow(src, CP_UTF8).c_str(), dirs, files, menu ? "present" : "ABSENT (reinstaller MSCLoader dans l'onglet Mods)");
    if (!menu) SetStatus(K_WARN, T(L"Fichiers de MSCLoader absents : réinstalle-le dans l'onglet Mods", L"MSCLoader files missing: reinstall it in the Mods tab"));
}
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
        LaunchLog("copie de l'invite : jonction %ls impossible (erreur %lu)", n.c_str(), GetLastError());
        return false;
    };
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((g_gameDir + L"*").c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return false;
    bool ok = true, hadMods = false;
    do {
        std::wstring n = fd.cFileName;
        if (n == L"." || n == L"..") continue;
        if (!ModInGame() && (!_wcsicmp(n.c_str(), L"MWCoop") || !_wcsicmp(n.c_str(), L"version.dll"))) continue;   // (mod a part : ModLink)
        std::wstring src = g_gameDir + n, dst = m + n;
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) {
            bool isMods = !_wcsicmp(n.c_str(), L"Mods");
            hadMods |= isMods;
            std::wstring target = isMods && mods ? coop.substr(0, coop.size() - 1) : hostRoot(n) ? cont + n : src;
            ok = link(n, target) && ok;
        } else if (CopyKind(n) && !SameFile(src, dst)) {
            if (!CopyFileW(src.c_str(), dst.c_str(), FALSE)) { LaunchLog("copie de l'invite : %ls impossible (erreur %lu)", n.c_str(), GetLastError()); ok = false; }
        }
    } while (FindNextFileW(h, &fd));
    FindClose(h);
    if (mods && !hadMods) ok = link(L"Mods", coop.substr(0, coop.size() - 1)) && ok;
    if (mods) GuestMscAssets(coop);
    ok = ModLink(m) && ok;
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
    if (a != INVALID_HANDLE_VALUE) { DWORD n; WriteFile(a, Narrow(GM().appid).c_str(), (DWORD)wcslen(GM().appid), &n, NULL); CloseHandle(a); }
    std::wstring ini = m + L"doorstop_config.ini";
    if (FileExists(ini)) {
        WritePrivateProfileStringW(L"General", L"enabled", mods ? L"true" : L"false", ini.c_str());
        WritePrivateProfileStringW(L"MSCLoader", L"mods", L"GF", ini.c_str());
    }
    std::string rl;
    for (auto &r : roots) rl += " " + Narrow(r);
    LaunchLog("copie de l'invite prete : %s (%s ; contenu de l'hote :%s)", Narrow(m, CP_UTF8).c_str(), mods ? "mods de l'hote" : "sans MSCLoader", rl.empty() ? " aucun" : rl.c_str());
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
    if (!g_offerBlob.empty()) { SendMsg(s, Wr{ g_offerBlob }); for (auto &x : g_offerExtra) SendMsg(s, Wr{ x }); }
    LeaveCriticalSection(&g_lcs);
}

// ---- invites en retard (demande de JD, 13/10 : « ceux qui rejoignent pendant la partie : les mods, puis ils entrent »)
// Hote, salon IP : apres LANCER l'ecoute reste ouverte (g_lateServe) ; un invite qui arrive recoit M_LATE puis la liste, et ses
// demandes de morceaux sont servies jusqu'a ce qu'il raccroche. Rien a envoyer (pas de mods) : false, il est refuse
// (« started ») et rejoint en jeu comme avant.
static bool SyncHostHasMods()
{
    EnterCriticalSection(&g_lcs);
    bool b = !g_offer.mods.empty();
    LeaveCriticalSection(&g_lcs);
    return b;
}

static bool LateSession(SOCKET s, const std::string &name)
{
    EnterCriticalSection(&g_lcs);
    bool any = !g_offerBlob.empty() && !g_offer.mods.empty();
    LeaveCriticalSection(&g_lcs);
    if (!any) return false;
    { Wr w; w.u8(M_LATE); w.u8(1); if (!SendMsg(s, w)) { closesocket(s); return true; } }
    SyncWelcome(s);
    TestLog("salon : %s arrive pendant la partie : envoi des mods", name.c_str());
    LaunchLog("mods : %s arrive pendant la partie, envoi des mods", name.c_str());
    DWORD to = 60000;
    setsockopt(s, SOL_SOCKET, SO_RCVTIMEO, (const char *)&to, sizeof(to));
    std::string m;
    while (RecvMsg(s, m)) {
        Rd q(m);
        if (q.u8() != M_MODGET) continue;
        Wr w;
        if (SyncServe(m, w)) { EnterCriticalSection(&g_lcs); SendMsg(s, w); LeaveCriticalSection(&g_lcs); }
    }
    closesocket(s);
    return true;
}

// Hote, au lancement de son jeu : la liste et les fichiers pour son jeu (MWCoop\offre-mods.bin, lu par Net/ModServe.cs), qui
// les sert aux invites Steam en retard. "MWOF", u32 nombre de messages (u32 longueur, octets : M_MODS, M_MODSX...), u32 + u32
// empreinte, u16 nombre de fichiers (u16 longueur, chemin complet UTF-8 ; u32 taille). Sans mods : fichier efface.
static void SyncWriteLateOffer()
{
    if (g_gameDir.empty()) return;
    std::wstring f = g_modRoot + L"MWCoop\\offre-mods.bin";
    std::string out;
    EnterCriticalSection(&g_lcs);
    bool any = !g_offerBlob.empty() && !g_offer.mods.empty();
    if (any) {
        Wr w;
        w.d = "MWOF";
        w.u32(1 + (uint32_t)g_offerExtra.size());
        w.u32((uint32_t)g_offerBlob.size()); w.d += g_offerBlob;
        for (auto &x : g_offerExtra) { w.u32((uint32_t)x.size()); w.d += x; }
        w.u32((uint32_t)g_offer.key); w.u32((uint32_t)(g_offer.key >> 32));
        w.u16((int)g_offer.files.size());
        for (auto &sf : g_offer.files) {
            std::wstring path = OfferSource(sf.path);
            std::string p8 = Narrow(path, CP_UTF8);
            w.u16((int)p8.size()); w.d += p8;
            w.u32(sf.size);
        }
        out = w.d;
    }
    LeaveCriticalSection(&g_lcs);
    if (!any) { DeleteFileW(f.c_str()); return; }
    EnsureModDir();
    HANDLE h = CreateFileW(f.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL);
    if (h == INVALID_HANDLE_VALUE) return;
    DWORD wr = 0;
    WriteFile(h, out.data(), (DWORD)out.size(), &wr, NULL);
    CloseHandle(h);
    LaunchLog("mods : liste pour les invites en retard ecrite (%u octets)", (unsigned)out.size());
}

// Invite en retard : debut (Steam : la liste est demandee au jeu de l'hote), fin, echec.
static bool LateBegin(bool steam)
{
    if (steam && (g_steamUp != 1 || !sapi::nw || !g_sHost)) return false;
    if (steam) SyncReset();   // (IP : deja fait par LobbyJoin -- la liste de l'hote a pu arriver avant ce message)
    g_syncAccepted = true;   // (il a demande a rejoindre : ses mods et son contenu, sans question)
    g_lateSync = true;
    g_lateSteam = steam;
    g_lateT = GetTickCount();
    g_launchAfterSync = true;
    g_joinFallback = false;
    if (steam) {
        sapi::AcceptP2PSessionWithUser(sapi::nw, g_sHost);
        Wr w; w.u8(M_MODSREQ);
        SteamSendP2P(g_sHost, w.d);
    }
    TestLog("salon : invite en retard (%s), demande des mods de l'hote", steam ? "steam" : "ip");
    return true;
}
static void LateEnd()
{
    if (!g_lateSync) return;
    g_lateSync = false;
    g_lateSteam = false;
    EnterCriticalSection(&g_lcs);
    if (g_guestSock != INVALID_SOCKET) shutdown(g_guestSock, SD_BOTH);   // (GuestThread la ferme)
    LeaveCriticalSection(&g_lcs);
}
static void LateFail(const wchar_t *why)
{
    if (!g_lateSync) return;
    TestLog("salon : invite en retard, sans les mods (%s)", Narrow(why, CP_UTF8).c_str());
    LateEnd();
    g_launchAfterSync = false;
    SyncReset();
    g_joinFallback = true;
    SetStatus(K_WARN, L"%s", why);
}

// Invite en retard (Steam) : paquets du jeu de l'hote sur le canal des mods ; la liste redemandee toutes les 3 s.
static void LateSteamTick()
{
    if (g_steamUp != 1 || !sapi::nw) return;
    uint32_t size = 0;
    int ch = P2PRecvChannel();
    for (int k = 0; k < 64 && sapi::IsP2PPacketAvailable(sapi::nw, &size, ch); k++) {
        std::string m(size, '\0');
        uint32_t got = 0;
        SteamId from = 0;
        if (!sapi::ReadP2PPacket(sapi::nw, size ? &m[0] : NULL, size, &got, &from, ch)) break;
        m.resize(got);
        if (m.empty() || from != g_sHost) continue;
        int t = (uint8_t)m[0];
        if (t == M_MODS) SyncOnOffer(m);
        else if (t == M_MODSX) SyncOnOfferMore(m);
        else if (t == M_MODDATA) SyncOnData(m);
        else if (t == M_LATE && m.size() >= 2 && m[1] == 0) { LateFail(T(L"L'hôte n'envoie pas de mods : « Rejoindre en jeu »", L"The host isn't sending mods: \"Join in game\"")); return; }
    }
    static DWORD lastAsk;
    if (g_lateSteam && g_sync == SY_NONE && GetTickCount() - lastAsk > 3000) {
        lastAsk = GetTickCount();
        Wr w; w.u8(M_MODSREQ);
        SteamSendP2P(g_sHost, w.d);
    }
}

// Salon Steam (fil de la fenetre) : paquets du canal des mods ; invite, liste a demander quand l'empreinte change.
static void LateSteamTick();
static void SyncSteamTick()
{
    if (g_lateSteam) { LateSteamTick(); return; }
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
                std::vector<std::string> extra = g_offerExtra;
                LeaveCriticalSection(&g_lcs);
                if (!blob.empty()) { SteamSendP2P(from, blob); for (auto &x : extra) SteamSendP2P(from, x); }
            } else if (t == M_MODGET) { Wr w; if (SyncServe(m, w)) SteamSendP2P(from, w.d); }
        } else if (from == g_sHost) {
            if (t == M_MODS) SyncOnOffer(m);
            else if (t == M_MODSX) SyncOnOfferMore(m);
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
// ---- page CONTENU ENVOYE (maquette du 17/10) : a gauche « Ce qui part », une ligne par groupe (CD, Radio, peinture, drapeau,
// posters, autres, visage) avec icone, detail et interrupteur, puis la jauge de la taille totale (100 Mo) ; a droite le detail
// du groupe choisi (pistes du CD avec Ajouter des pistes et glisser-deposer, ou les images, ou les categories « Autres »), les
// cartes Posters et Peinture & drapeau (Choisir), l'aide, ANNULER / VALIDER (les valeurs d'avant sont rendues : ui.inc PageCancel).
// songs / ignored : CD et radio -- le jeu ne lit que track1.ogg, track2.ogg... a la suite (OGG Vorbis), les autres
// fichiers audio sont ignores (retour d'un joueur, 10/10 : « ma musique ne s'importe pas chez les invites »).
struct ContentInfo { int n; uint64_t bytes; int songs, ignored; };
static ContentInfo g_contentInfo[16];
static DWORD g_contentScanT;
// (g_contentHot : uikit.inc)
static std::vector<int> g_drContent;                 // a l'ouverture de la page (ANNULER)
static std::map<std::wstring, std::pair<std::wstring, Bitmap *>> g_contentThumb;   // fichier -> (cle taille|date, image)
static const RectF kContentL(264, 88, 388, 656), kContentR(664, 88, 600, 656);
static const float kContentRowH = 60, kContentRowStep = 68;
// Groupes de la carte de gauche : categories de kContent (indices), icone, sous-titre ; *Msc : sur My Summer Car (NULL : le
// meme). Une categorie absente du jeu choisi (ContentInGame) n'est ni montree, ni comptee, ni envoyee.
struct ContentGroup { const wchar_t *fr, *en, *subFr, *subEn; int icon; const int *cats; int ncats; const wchar_t *frMsc, *enMsc, *subFrMsc, *subEnMsc; };
static const int kCgCd[] = { 0, 1, 2 }, kCgRadio[] = { 3 }, kCgPaint[] = { 4 }, kCgFlag[] = { 6 }, kCgPosters[] = { 7 }, kCgOther[] = { 5, 8, 9, 10, 11, 12 };
static const ContentGroup kCg[] = {
    { L"CD", L"CD", L"Lecteur CD des voitures", L"Car CD player", IC_MUSIC, kCgCd, 3, NULL, NULL, NULL, NULL },
    { L"Radio", L"Radio", L"Station perso", L"Custom station", IC_RADIO, kCgRadio, 1, NULL, NULL, NULL, NULL },
    { L"Peinture de ma CORRIS", L"My CORRIS paint", L"", L"", IC_CAR, kCgPaint, 1, L"Peinture de ma SATSUMA", L"My SATSUMA paint", NULL, NULL },
    { L"Drapeau", L"Flag", L"Devant le garage", L"In front of the garage", IC_FLAG, kCgFlag, 1, NULL, NULL, L"Dans le garage", L"In the garage" },   // (MSC : YARD/Building/LOD/flag)
    { L"Posters", L"Posters", L"Murs du garage et de la maison", L"Garage and house walls", IC_IMAGE, kCgPosters, 1, NULL, NULL, L"Murs de la chambre", L"Bedroom walls" },   // (ligne : « Garage et maison » ; MSC : BEDROOM1)
    { L"Autres", L"Others", L"Mobylette, autocollant, tissu, photo, cartes…", L"Moped, sticker, fabric, photo, maps…", IC_LAYERS, kCgOther, 6, NULL, NULL, L"Mobylette, autocollant, photo, cartes…", L"Moped, sticker, photo, maps…" },
};
static std::wstring CgName(int gi) { const ContentGroup &c = kCg[gi]; return g_game == GAME_MSC && c.frMsc ? T(c.frMsc, c.enMsc) : T(c.fr, c.en); }
static std::wstring CgSub(int gi) { const ContentGroup &c = kCg[gi]; return g_game == GAME_MSC && c.subFrMsc ? T(c.subFrMsc, c.subEnMsc) : T(c.subFr, c.subEn); }
// Categories du groupe lues par le jeu choisi (indices de kContent).
static std::vector<int> CgCats(int gi) { std::vector<int> v; for (int k = 0; k < kCg[gi].ncats; k++) if (ContentInGame(kCg[gi].cats[k])) v.push_back(kCg[gi].cats[k]); return v; }
static const int kCgN = 6, kCgFace = 6;   // (ligne 6 : « Mon visage (photo) », envoye avec la tenue : puce Auto, pas d'interrupteur)
static const Color kCgHue[7] = { Color(255, 166, 216, 255), Color(255, 255, 210, 143), Color(255, 240, 166, 255), Color(255, 154, 240, 192), Color(255, 201, 216, 242), Color(255, 201, 216, 242), Color(255, 201, 216, 242) };
static int g_contentSel = 0;   // groupe detaille a droite (0 CD par defaut, comme la maquette)
// Pistes d'un dossier de musique (CD1-3 ou Radio) : trackN.ogg a la suite, titre et duree lus dans l'OGG (en-tetes Vorbis).
struct ContentTrack { std::wstring file, title; uint64_t bytes = 0; float secs = -1; int cd = 0; };
static std::vector<ContentTrack> g_contentTracks[2];   // 0 : les trois CD, 1 : la radio
static std::map<std::wstring, std::pair<uint64_t, std::pair<std::wstring, float>>> g_oggCache;   // chemin -> (taille ^ date, (titre, secondes))

static void ContentScan();
static void GoPage(int p);   // (ui.inc)

// Titre (TITLE=) et duree (position du dernier granule / frequence) d'un OGG Vorbis : premiers et derniers 64 Ko seulement.
static bool OggInfo(const std::wstring &f, std::wstring *title, float *secs)
{
    HANDLE h = CreateFileW(f.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, NULL, OPEN_EXISTING, 0, NULL);
    if (h == INVALID_HANDLE_VALUE) return false;
    LARGE_INTEGER sz = {};
    GetFileSizeEx(h, &sz);
    std::vector<unsigned char> head((size_t)min(sz.QuadPart, (LONGLONG)65536)), tail;
    DWORD n = 0;
    ReadFile(h, head.data(), (DWORD)head.size(), &n, NULL); head.resize(n);
    if (sz.QuadPart > 65536) { LARGE_INTEGER at; at.QuadPart = sz.QuadPart - 65536; SetFilePointerEx(h, at, NULL, FILE_BEGIN); tail.resize(65536); ReadFile(h, tail.data(), 65536, &n, NULL); tail.resize(n); }
    else tail = head;
    CloseHandle(h);
    auto u32 = [](const std::vector<unsigned char> &b, size_t o) { return o + 4 <= b.size() ? (uint32_t)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | (uint32_t)b[o + 3] << 24) : 0u; };
    auto find = [](const std::vector<unsigned char> &b, const char *s, size_t from) { size_t l = strlen(s); for (size_t i = from; i + l <= b.size(); i++) if (!memcmp(&b[i], s, l)) return i; return std::string::npos; };
    if (head.size() < 4 || memcmp(head.data(), "OggS", 4)) return false;
    size_t id = find(head, "\x01vorbis", 0);
    uint32_t rate = id == std::string::npos ? 0 : u32(head, id + 12);
    size_t cm = find(head, "\x03vorbis", 0);
    if (cm != std::string::npos) {
        size_t p = cm + 7, vl = u32(head, p); p += 4 + vl;
        uint32_t cnt = u32(head, p); p += 4;
        for (uint32_t i = 0; i < cnt && i < 64 && p + 4 <= head.size(); i++) {
            uint32_t l = u32(head, p); p += 4;
            if (p + l > head.size()) break;
            std::string c((const char *)&head[p], l); p += l;
            if (!_strnicmp(c.c_str(), "TITLE=", 6)) *title = Trim(Widen(c.substr(6), CP_UTF8));
        }
    }
    *secs = -1;
    if (rate) {   // dernier en-tete de page : "OggS", version, type, granule (8 octets)
        size_t last = std::string::npos;
        for (size_t p = find(tail, "OggS", 0); p != std::string::npos; p = find(tail, "OggS", p + 4)) if (p + 14 <= tail.size()) last = p;
        if (last != std::string::npos) {
            uint64_t gr = u32(tail, last + 6) | ((uint64_t)u32(tail, last + 10) << 32);
            if (gr != ~0ull) *secs = (float)(gr / (double)rate);
        }
    }
    return true;
}
static void ContentTrackInfo(ContentTrack &t, const std::wstring &path)
{
    WIN32_FILE_ATTRIBUTE_DATA a;
    if (!GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &a)) return;
    t.bytes = ((uint64_t)a.nFileSizeHigh << 32) | a.nFileSizeLow;
    uint64_t stamp = t.bytes * 1000003ULL ^ ((uint64_t)a.ftLastWriteTime.dwHighDateTime << 32 | a.ftLastWriteTime.dwLowDateTime);
    auto it = g_oggCache.find(path);
    if (it == g_oggCache.end() || it->second.first != stamp) {
        std::wstring title; float secs = -1;
        OggInfo(path, &title, &secs);
        g_oggCache[path] = std::make_pair(stamp, std::make_pair(title, secs));
        it = g_oggCache.find(path);
    }
    t.title = it->second.second.first.empty() ? t.file.substr(0, t.file.rfind(L'.')) : it->second.second.first;
    t.secs = it->second.second.second;
}
static void ContentTracksScan()
{
    for (int k = 0; k < 2; k++) {
        g_contentTracks[k].clear();
        if (g_gameDir.empty()) continue;
        for (int cd = 1; cd <= (k ? 1 : 3); cd++) {
            std::wstring root = k ? L"Radio" : L"CD" + std::to_wstring(cd);
            for (int n = 1; n < 999; n++) {
                std::wstring f = L"track" + std::to_wstring(n) + L".ogg", p = g_gameDir + root + L"\\" + f;
                if (!FileExists(p)) break;
                ContentTrack t; t.file = f; t.cd = cd;
                ContentTrackInfo(t, p);
                g_contentTracks[k].push_back(t);
            }
        }
    }
}

static void ContentScan()
{
    g_contentScanT = GetTickCount();
    for (int i = 0; i < kContentN && i < 16; i++) {
        ContentInfo ci = { 0, 0, 0, 0 };
        bool music = !wcsncmp(kContent[i].root, L"CD", 2) || !wcscmp(kContent[i].root, L"Radio");
        if (!g_gameDir.empty()) {
            int audio = 0;
            std::wstring modDir = ModContentDir();
            for (const std::wstring &rel : ContentFiles(i, g_gameDir, modDir)) {
                WIN32_FILE_ATTRIBUTE_DATA a;
                if (GetFileAttributesExW((ContentBase(i, g_gameDir, modDir) + rel).c_str(), GetFileExInfoStandard, &a)) { ci.n++; ci.bytes += a.nFileSizeLow; }
                const wchar_t *ext = wcsrchr(rel.c_str(), L'.');
                if (music && ext) for (const wchar_t *e : { L".ogg", L".mp3", L".wav", L".flac", L".m4a", L".wma", L".aac", L".opus" }) if (!_wcsicmp(ext, e)) audio++;
            }
            if (music) {
                while (ci.songs < 999 && FileExists(g_gameDir + kContent[i].root + L"\\track" + std::to_wstring(ci.songs + 1) + L".ogg")) ci.songs++;
                ci.ignored = audio - ci.songs;
            }
        }
        g_contentInfo[i] = ci;
    }
    ContentTracksScan();
}
static std::wstring ContentSummary()
{
    int on = 0;
    for (int i = 0; i < kContentN; i++) on += ContentOn(i);
    wchar_t b[32];
    swprintf_s(b, L"%d / %d", on, ContentCount());
    return b;
}
static void ContentSnapshot(bool restore)
{
    if (!restore) { g_drContent.clear(); for (int i = 0; i < kContentN; i++) g_drContent.push_back(ContentOn(i)); return; }
    for (int i = 0; i < (int)g_drContent.size() && i < kContentN; i++) if (ContentInGame(i) && ContentOn(i) != (g_drContent[i] != 0)) ContentSet(i, g_drContent[i] != 0);
}
// Groupe : allume si une de ses categories l'est ; l'interrupteur les regle toutes.
static bool ContentGroupOn(int gi) { for (int i : CgCats(gi)) if (ContentOn(i)) return true; return false; }
static int ContentGroupOnCount(int gi) { int n = 0; for (int i : CgCats(gi)) n += ContentOn(i); return n; }
static void ContentGroupSet(int gi, bool on) { for (int i : CgCats(gi)) ContentSet(i, on); }
static ContentInfo ContentGroupInfo(int gi)
{
    ContentInfo s = { 0, 0, 0, 0 };
    for (int i : CgCats(gi)) { const ContentInfo &c = g_contentInfo[i]; s.n += c.n; s.bytes += c.bytes; s.songs += c.songs; s.ignored += c.ignored; }
    return s;
}
// Miniature d'une image du jeu (Images\...), relue quand le fichier change.
static Bitmap *ContentThumbFile(const std::wstring &rel)
{
    std::wstring f = g_gameDir + rel;
    WIN32_FILE_ATTRIBUTE_DATA a;
    if (g_gameDir.empty() || !GetFileAttributesExW(f.c_str(), GetFileExInfoStandard, &a)) return NULL;
    std::wstring key = std::to_wstring(a.nFileSizeLow) + L"|" + std::to_wstring(a.ftLastWriteTime.dwLowDateTime);
    auto it = g_contentThumb.find(f);
    if (it != g_contentThumb.end() && it->second.first == key) return it->second.second;
    if (it != g_contentThumb.end()) delete it->second.second;
    Bitmap *b = LoadAnyImage(f, NULL);
    g_contentThumb[f] = std::make_pair(key, b);
    return b;
}
static Bitmap *ContentThumb(int i)   // (premiere image de la categorie)
{
    if (kContent[i].files == NULL || wcscmp(kContent[i].root, L"Images")) return NULL;
    std::vector<std::wstring> f = ContentFiles(i, g_gameDir, ModContentDir());
    return f.empty() ? NULL : ContentThumbFile(f[0]);
}
// Image dessinee dans un rectangle arrondi, recadree (couverture).
static void ContentDrawImage(Graphics &g, Bitmap *b, RectF r, float rad)
{
    GraphicsPath p; RoundRect(p, r, rad);
    if (!b) { SolidBrush f(WithA(kInk, 0.08f)); g.FillPath(&f, &p); Pen pen(TH(choiceBorder), 1); g.DrawPath(&pen, &p); return; }
    GraphicsState st = g.Save();
    g.SetClip(&p, CombineModeIntersect);
    float bw = (float)b->GetWidth(), bh = (float)b->GetHeight(), s = max(r.Width / bw, r.Height / bh);
    g.DrawImage(b, RectF(r.X + (r.Width - bw * s) / 2, r.Y + (r.Height - bh * s) / 2, bw * s, bh * s));
    g.Restore(st);
    Pen pen(TH(choiceBorder), 1); g.DrawPath(&pen, &p);
}
static std::wstring ContentClock(float s) { if (s < 0) return L""; int t = (int)(s + 0.5f); wchar_t b[32]; if (t >= 3600) swprintf_s(b, L"%d:%02d:%02d", t / 3600, t / 60 % 60, t % 60); else swprintf_s(b, L"%d:%02d", t / 60, t % 60); return b; }

// ---- ajouter des pistes (bouton, ou fichiers deposes sur la fenetre : launcher.cpp WM_DROPFILES -> ContentDrop)
// Le lanceur n'a pas de convertisseur audio (MWCoopRT.dll ne fait que la video des cassettes) : .ogg seulement, numerote
// trackN.ogg a la suite dans le premier CD qui a de la place (CD1, puis CD2, CD3 : 99 pistes chacun), ou dans Radio.
static const int kContentCdMax = 99;
static std::wstring ContentMusicRoot(bool radio, int *next)
{
    if (radio) { *next = g_contentInfo[3].songs + 1; return L"Radio"; }
    for (int cd = 1; cd <= 3; cd++) if (g_contentInfo[cd - 1].songs < kContentCdMax) { *next = g_contentInfo[cd - 1].songs + 1; return L"CD" + std::to_wstring(cd); }
    *next = 0;
    return L"";
}
// Image choisie -> PNG du jeu (Images\<rel>) : n'importe quel format lisible (LoadAnyImage), taille d'origine.
static bool ContentPutImage(const std::wstring &src, const std::wstring &rel)
{
    std::wstring why;
    Bitmap *b = LoadAnyImage(src, &why);
    if (!b) { SetStatus(K_ERR, T(L"Image illisible : %s", L"Unreadable image: %s"), why.c_str()); return false; }
    std::wstring dst = g_gameDir + rel;
    SHCreateDirectoryExW(NULL, dst.substr(0, dst.find_last_of(L'\\')).c_str(), NULL);
    CLSID png;
    SetFileAttributesW(dst.c_str(), FILE_ATTRIBUTE_NORMAL);
    bool ok = EncoderClsid(L"image/png", &png) && b->Save(dst.c_str(), &png, NULL) == Ok;
    delete b;
    if (!ok) SetStatus(K_ERR, T(L"%s : écriture impossible dans le dossier du jeu", L"%s: can't write into the game folder"), rel.c_str());
    else TestLog("contenu : %s <- %s", Narrow(rel, CP_UTF8).c_str(), Narrow(src, CP_UTF8).c_str());
    return ok;
}
static void ContentAddFiles(const std::vector<std::wstring> &files)
{
    if (g_gameDir.empty() || files.empty()) return;
    ContentScan();
    // images deposees sur Posters / Drapeau : les images du jeu
    if (g_contentSel == 3 || g_contentSel == 4) {
        int put = 0, k = 0;
        for (const std::wstring &f : files) {
            const wchar_t *ext = wcsrchr(f.c_str(), L'.');
            bool img = ext && (!_wcsicmp(ext, L".png") || !_wcsicmp(ext, L".jpg") || !_wcsicmp(ext, L".jpeg") || !_wcsicmp(ext, L".bmp") || !_wcsicmp(ext, L".gif") || !_wcsicmp(ext, L".webp") || !_wcsicmp(ext, L".tif") || !_wcsicmp(ext, L".tiff"));
            if (!img) continue;
            if (g_contentSel == 3) { if (ContentPutImage(f, L"Images\\flag_garage.png")) put++; break; }
            if (k < 3 && ContentPutImage(f, L"Images\\poster" + std::to_wstring(k + 1) + L".png")) { put++; k++; }
        }
        ContentScan();
        if (put) SetStatus(K_OK, g_contentSel == 3 ? T(L"Drapeau du garage remplacé (Images\\flag_garage.png)", L"Garage flag replaced (Images\\flag_garage.png)") : T(L"%d poster(s) mis dans Images (poster1…poster3)", L"%d poster(s) put into Images (poster1…poster3)"), put);
        else SetStatus(K_WARN, T(L"Aucune image reconnue (png, jpg, bmp, gif, webp, tif)", L"No image recognized (png, jpg, bmp, gif, webp, tif)"));
        return;
    }
    bool radio = g_contentSel == 1;
    int added = 0, refused = 0, first = 0, last = 0;
    std::wstring root;
    for (const std::wstring &f : files) {
        const wchar_t *ext = wcsrchr(f.c_str(), L'.');
        if (!ext || _wcsicmp(ext, L".ogg")) { refused++; continue; }
        int n = 0;
        std::wstring r = ContentMusicRoot(radio, &n);
        if (r.empty() || !n) { SetStatus(K_WARN, T(L"Plus de place : 99 pistes par CD", L"No room left: 99 tracks per CD")); break; }
        std::wstring dir = g_gameDir + r;
        SHCreateDirectoryExW(NULL, dir.c_str(), NULL);
        std::wstring dst = dir + L"\\track" + std::to_wstring(n) + L".ogg";
        if (!CopyFileW(f.c_str(), dst.c_str(), TRUE)) { SetStatus(K_ERR, T(L"%s : copie impossible (erreur %lu)", L"%s: can't copy (error %lu)"), f.substr(f.find_last_of(L'\\') + 1).c_str(), GetLastError()); ContentScan(); return; }
        TestLog("contenu : %s <- %s", Narrow(r + L"\\track" + std::to_wstring(n) + L".ogg", CP_UTF8).c_str(), Narrow(f, CP_UTF8).c_str());
        if (!added) first = n;
        last = n; root = r; added++;
        ContentScan();   // (le prochain numero)
    }
    wchar_t b[200];
    if (added) {
        if (added == 1) swprintf_s(b, T(L"1 piste ajoutée dans %s (track%d.ogg)", L"1 track added to %s (track%d.ogg)"), root.c_str(), first);
        else swprintf_s(b, T(L"%d pistes ajoutées dans %s (track%d…track%d)", L"%d tracks added to %s (track%d…track%d)"), added, root.c_str(), first, last);
        if (refused) { wchar_t r2[80]; swprintf_s(r2, T(L" · %d refusé(s) : format .ogg seulement", L" · %d refused: .ogg format only"), refused); wcscat_s(b, r2); }
        SetStatus(refused ? K_WARN : K_OK, L"%s", b);
    } else if (refused) SetStatus(K_WARN, T(L"Format .ogg seulement : le jeu ne lit que des OGG Vorbis (convertis d'abord tes fichiers)", L"Format .ogg only: the game reads only OGG Vorbis (convert your files first)"));
    ContentScan();
}
// Fenetre de choix (plusieurs fichiers) : filtre et titre selon ce qu'on ajoute.
static std::vector<std::wstring> ContentPickFiles(const wchar_t *filter, const wchar_t *title, bool multi)
{
    std::vector<wchar_t> buf(32768, 0);
    OPENFILENAMEW of = { sizeof(of) };
    of.hwndOwner = g_wnd;
    of.lpstrFilter = filter;
    of.lpstrFile = buf.data(); of.nMaxFile = (DWORD)buf.size();
    of.lpstrTitle = title;
    of.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_HIDEREADONLY | OFN_EXPLORER | (multi ? OFN_ALLOWMULTISELECT : 0);
    std::vector<std::wstring> out;
    if (!GetOpenFileNameW(&of)) return out;
    std::wstring dir = buf.data();
    const wchar_t *p = buf.data() + dir.size() + 1;
    if (!*p) { out.push_back(dir); return out; }   // (un seul : le chemin entier)
    for (; *p; p += wcslen(p) + 1) out.push_back(WithSlash(dir) + p);
    return out;
}
static void ContentAddTracks()
{
    std::vector<std::wstring> f = ContentPickFiles(L"OGG Vorbis (*.ogg)\0*.ogg\0*.*\0*.*\0", g_contentSel == 1 ? T(L"Ajouter des morceaux à la radio (.ogg)", L"Add songs to the radio (.ogg)") : T(L"Ajouter des pistes au CD (.ogg)", L"Add tracks to the CD (.ogg)"), true);
    if (!f.empty()) ContentAddFiles(f);
}
static void ContentChooseImages(bool flag)
{
    int sel = g_contentSel;
    g_contentSel = flag ? 3 : 4;
    std::vector<std::wstring> f = ContentPickFiles(L"Images\0*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp\0*.*\0*.*\0",
                                                   flag ? T(L"Choisir le drapeau du garage (Images\\flag_garage.png)", L"Choose the garage flag (Images\\flag_garage.png)") : T(L"Choisir jusqu'à 3 posters (Images\\poster1…3.png)", L"Choose up to 3 posters (Images\\poster1…3.png)"), !flag);
    if (!f.empty()) ContentAddFiles(f);
    g_contentSel = f.empty() ? sel : g_contentSel;
}
// Fichiers deposes sur la fenetre (WM_DROPFILES, launcher.cpp) : pris sur la page Contenu envoye seulement.
static void ContentDrop(HDROP h)
{
    UINT n = DragQueryFileW(h, 0xFFFFFFFF, NULL, 0);
    std::vector<std::wstring> files;
    for (UINT i = 0; i < n && i < 200; i++) { wchar_t p[MAX_PATH] = L""; if (DragQueryFileW(h, i, p, MAX_PATH)) files.push_back(p); }
    DragFinish(h);
    if (g_tab != TAB_CONTENT || g_state != ST_IDLE || g_gameDir.empty()) { SetStatus(K_WARN, T(L"Dépose tes fichiers sur la page Contenu envoyé (pistes .ogg, images)", L"Drop your files on the Sent content page (.ogg tracks, images)")); return; }
    if (g_contentSel == 2 || g_contentSel == 5) g_contentSel = 0;   // (peinture : page Voiture ; autres : rien a deposer -> le CD)
    ContentAddFiles(files);
}
static void ContentOpenMusicDir()
{
    std::wstring root = g_contentSel == 1 ? L"Radio" : L"CD1";
    if (g_contentSel != 1) for (int cd = 1; cd <= 3; cd++) if (g_contentInfo[cd - 1].songs) { root = L"CD" + std::to_wstring(cd); break; }   // (le premier CD garni)
    std::wstring d = g_gameDir + root;
    SHCreateDirectoryExW(NULL, d.c_str(), NULL);
    ShellExecuteW(g_wnd, L"open", d.c_str(), NULL, NULL, SW_SHOWNORMAL);
}

// ---- zones (comme mods.inc MscZone : la souris passe par ContentAt, le clic par ContentClick)
// 5000+g interrupteur du groupe g ; 5050+g choisir le groupe ; 5200 ouvrir le dossier CD / Radio ; 5201 ajouter des pistes ;
// 5202 page Voiture (peinture) ; 5203 importer une peinture ; 5210 choisir les posters ; 5211 choisir le drapeau ;
// 5300+k interrupteur de la categorie k de « Autres ».
static std::vector<HotRect> g_contentZones;
static void ContentZone(RectF r, int id) { g_contentZones.push_back({ r, id }); }
static int ContentAt(float x, float y)
{
    for (int i = (int)g_contentZones.size() - 1; i >= 0; i--) if (g_contentZones[i].r.Contains(x, y)) return g_contentZones[i].id;
    return -1;
}
static void ContentClick(float x, float y)
{
    int h = ContentAt(x, y);
    if (h >= 5000 && h < 5000 + kCgN) { int gi = h - 5000; ContentGroupSet(gi, !ContentGroupOn(gi)); }
    else if (h >= 5050 && h < 5050 + kCgN) g_contentSel = h - 5050;
    else if (h == 5200) ContentOpenMusicDir();
    else if (h == 5201) ContentAddTracks();
    else if (h == 5202) GoPage(TAB_CAR);
    else if (h == 5203) { PaintImport(); ContentScan(); }
    else if (h == 5210) ContentChooseImages(false);
    else if (h == 5211) ContentChooseImages(true);
    else if (h >= 5300 && h < 5300 + (int)CgCats(5).size()) { int i = CgCats(5)[h - 5300]; ContentSet(i, !ContentOn(i)); }
}

// Petits elements : bouton en verre clair ou plein (hot : g_contentHot == id), puce, carte interieure.
static void ContentButton(Graphics &g, RectF r, const std::wstring &s, int id, bool primary = false, bool enabled = true)
{
    bool hot = enabled && g_contentHot == id;
    float a = enabled ? 1.0f : 0.4f;
    GraphicsPath p; RoundRect(p, r, min(11.0f, r.Height / 2));
    if (primary) { SolidBrush b(WithA(kAcc, a)); g.FillPath(&b, &p); if (hot) { SolidBrush hb(Color(60, 255, 255, 255)); g.FillPath(&hb, &p); } }
    else { SolidBrush b(hot ? TH(btn2Hot) : TH(btn2)); g.FillPath(&b, &p); Pen pen(WithA(TH(fieldBorder), a), 1.0f); g.DrawPath(&pen, &p); }
    Text(g, s, r, 12.5f, FontStyleBold, WithA(primary ? kOnAcc : hot ? kAcc : kInk, a));
    if (enabled) ContentZone(r, id);
}
static void ContentChip(Graphics &g, RectF r, const std::wstring &s, Color fill, Color ink, Color border, float px = 11.5f)
{
    GraphicsPath p; RoundRect(p, r, r.Height / 2);
    SolidBrush b(fill); g.FillPath(&b, &p);
    Pen pen(border, 1.0f); g.DrawPath(&pen, &p);
    Text(g, s, r, px, FontStyleBold, ink);
}
static void ContentInner(Graphics &g, RectF r, float rad = 14)
{
    GraphicsPath p; RoundRect(p, r, rad);
    SolidBrush b(TH(card)); g.FillPath(&b, &p);
    Pen pen(TH(choiceBorder), 1.0f); g.DrawPath(&pen, &p);
}
// Ligne « titre + detail + interrupteur » (categories « Autres », a droite).
static void ContentSwitchRow(Graphics &g, RectF r, const std::wstring &t, const std::wstring &d, bool on, int id, Color dc)
{
    bool hot = g_contentHot == id;
    Text(g, t, RectF(r.X, r.Y + 4, r.Width - 70, 20), 13.5f, FontStyleBold, WithA(kInk, on ? 1.0f : 0.6f), StringAlignmentNear);
    Text(g, d, RectF(r.X, r.Y + 24, r.Width - 70, 18), 11.5f, FontStyleRegular, dc, StringAlignmentNear);
    UiToggle(g, RectF(r.X + r.Width - 48, r.Y + (r.Height - 26) / 2, 48, 26), on, hot);
    Pen sep(TH(sep), 1); g.DrawLine(&sep, r.X, r.Y + r.Height, r.X + r.Width, r.Y + r.Height);
    ContentZone(r, id);
}
// Detail d'une categorie : « 3 fichier(s) · 3 Ko », « 44 Ko », « rien dans le jeu », ou les pistes lues / ignorees.
static std::wstring ContentDetail(const ContentInfo &ci, bool music, bool folder)
{
    wchar_t b[96];
    if (!ci.n) return T(L"rien dans le jeu", L"nothing in the game");
    if (music && ci.ignored > 0) { swprintf_s(b, T(L"%d lue(s) par le jeu · %d ignorée(s) (track1.ogg…)", L"%d read by the game · %d ignored (track1.ogg…)"), ci.songs, ci.ignored); return b; }
    if (music) { swprintf_s(b, T(L"%d musique(s) · %s", L"%d song(s) · %s"), ci.songs, SizeText(ci.bytes).c_str()); return b; }
    if (folder) { swprintf_s(b, T(L"%d fichier(s) · %s", L"%d file(s) · %s"), ci.n, SizeText(ci.bytes).c_str()); return b; }
    return SizeText(ci.bytes);
}

// Carte de droite, haut : les pistes du CD (ou de la radio), avec Ouvrir le dossier / Ajouter des pistes et la zone de depot.
static void DrawContentTracks(Graphics &g, RectF c, float &y)
{
    bool radio = g_contentSel == 1;
    const std::vector<ContentTrack> &tr = g_contentTracks[radio ? 1 : 0];
    ContentInfo gi = ContentGroupInfo(g_contentSel);
    Graphics &mg = MeasureG();
    std::wstring t = radio ? T(L"Radio", L"Radio") : L"CD";
    float tw = MeasureW(mg, t, 16, FontStyleBold);
    Title(g, t, RectF(c.X + 18, c.Y + 14, tw + 6, 26), 16, kInk);
    wchar_t sb[96];
    if (radio) swprintf_s(sb, T(L"· %d morceau(x) · %s", L"· %d song(s) · %s"), (int)tr.size(), SizeText(gi.bytes).c_str());
    else swprintf_s(sb, T(L"· %d piste(s) · %s", L"· %d track(s) · %s"), (int)tr.size(), SizeText(gi.bytes).c_str());
    Text(g, sb, RectF(c.X + 18 + tw + 6, c.Y + 15, 220, 26), 13, FontStyleBold, kGrey, StringAlignmentNear);
    float bx = c.X + c.Width - 18;
    ContentButton(g, RectF(bx - 158, c.Y + 12, 158, 36), T(L"Ajouter des pistes", L"Add tracks"), 5201, true, !g_gameDir.empty());
    std::wstring od = radio ? T(L"Ouvrir le dossier Radio", L"Open the Radio folder") : T(L"Ouvrir le dossier CD", L"Open the CD folder");
    float ow = MeasureW(mg, od, 12.5f, FontStyleBold) + 28;
    ContentButton(g, RectF(bx - 158 - 8 - ow, c.Y + 12, ow, 36), od, 5200, false, !g_gameDir.empty());
    std::wstring hint = radio ? T(L"Glisse des fichiers .ogg ici : numérotés track1…trackN dans Radio. Le jeu les lit au menu principal ; MWCoop les importe pour toi.", L"Drop .ogg files here: numbered track1…trackN in Radio. The game reads them in the main menu; MWCoop imports them for you.")
                              : T(L"Glisse des fichiers .ogg ici : numérotés track1…trackN. Le jeu les lit au menu principal ; MWCoop les importe pour toi.", L"Drop .ogg files here: numbered track1…trackN. The game reads them in the main menu; MWCoop imports them for you.");
    if (gi.ignored > 0) { wchar_t ib[160]; swprintf_s(ib, T(L"%d fichier(s) audio ignoré(s) par le jeu : il ne lit que track1.ogg, track2.ogg… à la suite (OGG Vorbis).", L"%d audio file(s) ignored by the game: it reads only track1.ogg, track2.ogg… in a row (OGG Vorbis)."), gi.ignored); hint = ib; }
    Para(g, hint, RectF(c.X + 18, c.Y + 50, c.Width - 36, 36), 12, gi.ignored > 0 ? kWarn : kGrey);
    // grille : 2 colonnes, 7 lignes (14 cases) ; au-dela, une case « 13 à N · total » ; la derniere : la zone de depot
    const int rows = 7, cells = rows * 2;
    float gx = c.X + 18, gw = c.Width - 36, cw = (gw - 6) / 2, ch = 36, step = 42;
    y = c.Y + 92;
    int shown = (int)tr.size() <= cells - 1 ? (int)tr.size() : cells - 2;   // (toutes, ou 12 + la case « de plus »)
    int extra = shown < (int)tr.size() ? 2 : 1;                              // (+ la case de depot)
    static const Color kDisc[6] = { Color(255, 46, 139, 106), Color(255, 201, 74, 74), Color(255, 60, 90, 140), Color(255, 122, 63, 168), Color(255, 224, 192, 112), Color(255, 143, 166, 204) };
    bool multiCd = false;
    for (auto &x : tr) multiCd |= x.cd > 1;
    for (int k = 0; k < shown + extra && k < cells; k++) {
        RectF r(gx + (k % 2) * (cw + 6), y + (k / 2) * step, cw, ch);
        bool rest = k == shown && extra == 2, drop = k == shown + extra - 1;
        GraphicsPath p; RoundRect(p, r, 12);
        SolidBrush b(Color((BYTE)(g_dark ? 56 : 14), 0, 0, 0)); g.FillPath(&b, &p);
        Pen pen(TH(choiceBorder), 1); if (drop) pen.SetDashStyle(DashStyleDash); g.DrawPath(&pen, &p);
        if (drop) { Text(g, T(L"+ Glisser des fichiers ici", L"+ Drop files here"), r, 12, FontStyleBold, kGrey); continue; }
        RectF disc(r.X + 10, r.Y + 4, 28, 28);
        if (rest) {
            SolidBrush db(WithA(kInk, 0.15f)); g.FillEllipse(&db, disc);
            float tot = 0; uint64_t by = 0; bool allDur = true;
            for (int i = shown; i < (int)tr.size(); i++) { if (tr[i].secs < 0) allDur = false; else tot += tr[i].secs; by += tr[i].bytes; }
            wchar_t rb[64]; swprintf_s(rb, T(L"%02d à %02d · %d de plus", L"%02d to %02d · %d more"), shown + 1, (int)tr.size(), (int)tr.size() - shown);
            Text(g, rb, RectF(r.X + 46, r.Y, r.Width - 46 - 64, r.Height), 12.5f, FontStyleBold, WithA(kInk, 0.7f), StringAlignmentNear);
            Text(g, allDur ? ContentClock(tot) : SizeText(by), RectF(r.X + r.Width - 70, r.Y, 60, r.Height), 12, FontStyleRegular, kGrey, StringAlignmentFar);
            continue;
        }
        const ContentTrack &x = tr[k];
        uint32_t hsh = 2166136261u; for (wchar_t ch2 : x.title) hsh = (hsh ^ (uint32_t)ch2) * 16777619u;
        Pen ring(WithA(kInk, 0.14f), 4); g.DrawEllipse(&ring, RectF(disc.X + 2, disc.Y + 2, 24, 24));
        SolidBrush db(kDisc[hsh % 6]); g.FillEllipse(&db, RectF(disc.X + 4, disc.Y + 4, 20, 20));
        SolidBrush hole(Color((BYTE)(g_dark ? 120 : 60), 0, 0, 0)); g.FillEllipse(&hole, RectF(disc.X + 11, disc.Y + 11, 6, 6));
        wchar_t nb[16]; swprintf_s(nb, L"%02d · ", k + 1);
        std::wstring lab = (multiCd ? L"CD" + std::to_wstring(x.cd) + L" · " : L"") + nb + x.title;
        std::wstring right = x.secs >= 0 ? ContentClock(x.secs) : SizeText(x.bytes);
        float rw = MeasureW(mg, right, 12, FontStyleRegular) + 6;
        RectF lr(r.X + 46, r.Y, r.Width - 46 - rw - 14, r.Height);
        while (lab.size() > 4 && MeasureW(mg, lab, 12.5f, FontStyleBold) > lr.Width) { lab.resize(lab.size() - 2); lab += L"…"; }
        Text(g, lab, lr, 12.5f, FontStyleBold, kInk, StringAlignmentNear);
        Text(g, right, RectF(r.X + r.Width - rw - 10, r.Y, rw, r.Height), 12, FontStyleRegular, kGrey, StringAlignmentFar);
    }
    y += rows * step;
}
// Carte de droite, haut : les images (peinture, drapeau, posters) en grand, avec Choisir ; ou les categories « Autres ».
static void DrawContentDetail(Graphics &g, RectF c, float &y)
{
    int gi = g_contentSel;
    ContentInfo info = ContentGroupInfo(gi);
    Graphics &mg = MeasureG();
    std::vector<int> cats = CgCats(gi);
    std::wstring t = CgName(gi);
    float tw = MeasureW(mg, t, 16, FontStyleBold);
    Title(g, t, RectF(c.X + 18, c.Y + 14, tw + 6, 26), 16, kInk);
    wchar_t sb[96];
    if (gi == 5) swprintf_s(sb, T(L"· %d / %d envoyé(s) · %s", L"· %d / %d sent · %s"), ContentGroupOnCount(gi), (int)cats.size(), SizeText(info.bytes).c_str());
    else if (gi == 4) swprintf_s(sb, L"· %d · %s", info.n, SizeText(info.bytes).c_str());
    else swprintf_s(sb, L"· %s", info.n ? SizeText(info.bytes).c_str() : T(L"rien dans le jeu", L"nothing in the game"));
    Text(g, sb, RectF(c.X + 18 + tw + 6, c.Y + 15, 260, 26), 13, FontStyleBold, kGrey, StringAlignmentNear);
    float bx = c.X + c.Width - 18;
    y = c.Y + 74;
    if (gi == 2) {   // peinture : celle du jeu (Images\car.png) ou la perso (page Voiture, voiture.inc PaintImport)
        ContentButton(g, RectF(bx - 150, c.Y + 12, 150, 36), T(L"Importer une image", L"Import an image"), 5203, true, !g_gameDir.empty());
        ContentButton(g, RectF(bx - 150 - 8 - 130, c.Y + 12, 130, 36), T(L"Page Voiture", L"Car page"), 5202);
        Text(g, PaintOn() ? PaintNameFix(T(L"Ta peinture perso (peinture.png) part à la place de Images\\car.png. Réglages : Personnage › Voiture.", L"Your custom paint (peinture.png) goes instead of Images\\car.png. Settings: Character › Car."))
                          : T(L"La peinture du jeu (Images\\car.png) telle que tu l'as. Une peinture perso : Personnage › Voiture.", L"The game's paint (Images\\car.png) as you have it. A custom paint: Character › Car."),
             RectF(c.X + 18, c.Y + 50, c.Width - 36, 18), 12, FontStyleRegular, kGrey, StringAlignmentNear);
        ContentDrawImage(g, PaintOn() ? PaintThumb() : ContentThumbFile(L"Images\\car.png"), RectF(c.X + 18, y, c.Width - 36, 280), 14);
        y += 294;
        return;
    }
    if (gi == 3 || gi == 4) {   // drapeau, posters : Choisir (fenetre), ou deposer des images
        bool flag = gi == 3;
        ContentButton(g, RectF(bx - 110, c.Y + 12, 110, 36), T(L"Choisir", L"Choose"), flag ? 5211 : 5210, true, !g_gameDir.empty());
        bool msc = g_game == GAME_MSC;   // (MSC : drapeau dans le garage, posters dans la chambre)
        Text(g, flag ? (msc ? T(L"Glisse une image ici, ou Choisir : elle devient Images\\flag_garage.png (le drapeau dans le garage).", L"Drop an image here, or Choose: it becomes Images\\flag_garage.png (the flag in the garage).")
                            : T(L"Glisse une image ici, ou Choisir : elle devient Images\\flag_garage.png (le drapeau devant le garage).", L"Drop an image here, or Choose: it becomes Images\\flag_garage.png (the flag in front of the garage)."))
                     : msc ? T(L"Glisse jusqu'à 3 images ici, ou Choisir : elles deviennent Images\\poster1…3.png (murs de la chambre).", L"Drop up to 3 images here, or Choose: they become Images\\poster1…3.png (bedroom walls).")
                           : T(L"Glisse jusqu'à 3 images ici, ou Choisir : elles deviennent Images\\poster1…3.png (murs du garage et de la maison).", L"Drop up to 3 images here, or Choose: they become Images\\poster1…3.png (garage and house walls)."),
             RectF(c.X + 18, c.Y + 50, c.Width - 36, 18), 12, FontStyleRegular, kGrey, StringAlignmentNear);
        std::vector<std::wstring> files = ContentFiles(kCg[gi].cats[0], g_gameDir, ModContentDir());
        if (flag) { ContentDrawImage(g, files.empty() ? NULL : ContentThumbFile(files[0]), RectF(c.X + 18, y, c.Width - 36, 280), 14); if (files.empty()) Text(g, T(L"Aucun drapeau dans Images", L"No flag in Images"), RectF(c.X + 18, y, c.Width - 36, 280), 13, FontStyleBold, kGrey); }
        else {
            float pw = (c.Width - 36 - 2 * 10) / 3;
            for (int k = 0; k < 3; k++) {
                RectF r(c.X + 18 + k * (pw + 10), y, pw, 280);
                std::wstring rel = L"Images\\poster" + std::to_wstring(k + 1) + L".png";
                Bitmap *b = FileExists(g_gameDir + rel) ? ContentThumbFile(rel) : NULL;
                ContentDrawImage(g, b, r, 14);
                if (!b) Text(g, L"poster" + std::to_wstring(k + 1) + L".png", r, 12, FontStyleBold, kGrey);
            }
        }
        y += 294;
        return;
    }
    // autres : une ligne par categorie, avec son interrupteur
    Text(g, T(L"Le reste des fichiers perso du jeu, chacun à part. Tout est copié à part chez les invités.", L"The rest of the game's custom files, each on its own. Everything is copied apart to the guests."),
         RectF(c.X + 18, c.Y + 50, c.Width - 36, 18), 12, FontStyleRegular, kGrey, StringAlignmentNear);
    for (int k = 0; k < (int)cats.size(); k++) {
        int i = cats[k];
        const ContentInfo &ci = g_contentInfo[i];
        RectF r(c.X + 18, y + k * 52, c.Width - 36, 50);
        Bitmap *b = ContentThumb(i);
        RectF ir(r.X, r.Y + 5, 40, 40);
        if (b) ContentDrawImage(g, b, ir, 10);
        else { GraphicsPath ip; RoundRect(ip, ir, 10); SolidBrush ib(WithA(kAcc, 0.22f)); g.FillPath(&ib, &ip); Icon(g, IC_HASH, ir.X + 20, ir.Y + 20, 18, kInk, 1.9f); }
        ContentSwitchRow(g, RectF(r.X + 52, r.Y, r.Width - 52, r.Height), ContentName(i), ContentDetail(ci, false, !kContent[i].files), ContentOn(i), 5300 + k, ci.n ? kGrey : WithA(kGrey, 0.7f));
    }
    y += (int)cats.size() * 52 + 8;
}

// Page CONTENU : « Ce qui part » a gauche, le detail a droite, ANNULER / VALIDER.
static void DrawContent(Graphics &g)
{
    g_contentZones.clear();
    if (GetTickCount() - g_contentScanT > 2000) ContentScan();
    Glass(kContentL); Glass(kContentR);
    Graphics &mg = MeasureG();
    int on_ = 0;
    for (int i = 0; i < kContentN; i++) on_ += ContentOn(i);
    // ---- a gauche : ce qui part
    {
        RectF c = kContentL;
        Title(g, T(L"Ce qui part", L"What goes out"), RectF(c.X + 18, c.Y + 14, c.Width - 36, 26), 16, kInk);
        for (int gi = 0; gi <= kCgN; gi++) {
            RectF r(c.X + 18, c.Y + 48 + gi * kContentRowStep, c.Width - 36, kContentRowH);
            bool face = gi == kCgFace, sel = !face && gi == g_contentSel, hot = g_contentHot == 5050 + gi;
            bool on = face ? PersoImgHas(PI_FACE) : ContentGroupOn(gi);
            GraphicsPath rp; RoundRect(rp, r, 16);
            SolidBrush rb(sel ? WithA(kAcc, 0.12f) : hot ? TH(cardSel) : TH(card)); g.FillPath(&rb, &rp);
            Pen rpen(sel ? kAcc : hot ? WithA(kAcc, 0.6f) : TH(choiceBorder), sel ? 1.6f : 1); g.DrawPath(&rpen, &rp);
            float a = face ? 0.75f : on ? 1.0f : 0.55f;
            RectF ir(r.X + 10, r.Y + 8, 44, 44);
            GraphicsPath ip; RoundRect(ip, ir, 12);
            SolidBrush ib(WithA(kInk, 0.08f)); g.FillPath(&ib, &ip);
            Icon(g, face ? IC_USER : kCg[gi].icon, ir.X + 22, ir.Y + 22, 20, g_dark ? WithA(kCgHue[gi], a) : WithA(kAcc, a), 2.0f);
            // titre + compte gris, puis le detail
            std::wstring t = face ? T(L"Mon visage (photo)", L"My face (photo)") : CgName(gi), cnt, det;
            ContentInfo info = face ? ContentInfo{ 0, 0, 0, 0 } : ContentGroupInfo(gi);
            Color dc = kGrey;
            if (face) {
                WIN32_FILE_ATTRIBUTE_DATA fa;
                bool has = PersoImgHas(PI_FACE) && GetFileAttributesExW(PersoImgFile(PI_FACE).c_str(), GetFileExInfoStandard, &fa);
                det = has ? T(L"Envoyé avec ta tenue · ", L"Sent with your outfit · ") + SizeText(fa.nFileSizeLow) : T(L"Aucune : Personnage › Visage", L"None: Character › Face");
            } else if (gi == 0 || gi == 1) {
                int n = (int)g_contentTracks[gi].size();
                wchar_t b[48]; swprintf_s(b, gi ? T(L"· %d morceau(x)", L"· %d song(s)") : T(L"· %d piste(s)", L"· %d track(s)"), n);
                cnt = b;
                if (!info.n) det = T(L"rien dans le jeu", L"nothing in the game");
                else if (info.ignored > 0) { det = ContentDetail(info, true, true); dc = kWarn; }
                else det = CgSub(gi) + L" · " + SizeText(info.bytes);
            } else if (gi == 2) {
                WIN32_FILE_ATTRIBUTE_DATA pa;
                bool perso = PaintOn() && GetFileAttributesExW(PaintFile().c_str(), GetFileExInfoStandard, &pa);
                det = PaintNameFix(std::wstring(perso ? T(L"peinture.png (perso)", L"peinture.png (custom)") : L"car.png")) + L" · " + (perso ? SizeText(pa.nFileSizeLow) : info.n ? SizeText(info.bytes) : std::wstring(T(L"rien dans le jeu", L"nothing in the game")));
            }
            else if (gi == 4) { cnt = L"· " + std::to_wstring(info.n); det = info.n ? std::wstring(g_game == GAME_MSC ? T(L"Chambre", L"Bedroom") : T(L"Garage et maison", L"Garage and house")) + L" · " + SizeText(info.bytes) : T(L"rien dans le jeu", L"nothing in the game"); }
            else if (gi == 5) { wchar_t b[24]; swprintf_s(b, L"· %d / %d", ContentGroupOnCount(gi), (int)CgCats(gi).size()); cnt = b; det = CgSub(gi); }
            else det = info.n ? CgSub(gi) + L" · " + SizeText(info.bytes) : T(L"rien dans le jeu", L"nothing in the game");
            float tx = r.X + 66, tw = MeasureW(mg, t, 13.5f, FontStyleBold), maxw = r.Width - 66 - 70;
            Text(g, t, RectF(tx, r.Y + 9, min(tw + 4, maxw), 20), 13.5f, FontStyleBold, WithA(kInk, a), StringAlignmentNear);
            if (!cnt.empty() && tw + 8 < maxw) Text(g, cnt, RectF(tx + tw + 5, r.Y + 10, maxw - tw - 5, 20), 12, FontStyleRegular, kGrey, StringAlignmentNear);
            Text(g, det, RectF(tx, r.Y + 31, maxw, 18), 12, FontStyleRegular, WithA(dc, face ? 0.85f : 1.0f), StringAlignmentNear);
            if (face) { const wchar_t *au = T(L"Auto", L"Auto"); float aw = PillW(au, 11.5f) + 6; ContentChip(g, RectF(r.X + r.Width - 12 - aw, r.Y + (r.Height - 26) / 2, aw, 26), au, WithA(kInk, 0.1f), kInk, TH(choiceBorder)); }
            else {
                bool th = g_contentHot == 5000 + gi;
                RectF tr(r.X + r.Width - 12 - 48, r.Y + (r.Height - 26) / 2, 48, 26);
                UiToggle(g, tr, on, th);
                ContentZone(RectF(tr.X - 8, r.Y, tr.Width + 20, r.Height), 5000 + gi);
                ContentZone(RectF(r.X, r.Y, r.Width - 76, r.Height), 5050 + gi);
            }
        }
        // jauge : total de ce qui part, sur 100 Mo (orange au-dela de 85 %)
        uint64_t total = 0;
        for (int i = 0; i < kContentN; i++) if (ContentOn(i)) total += g_contentInfo[i].bytes;
        RectF tc(c.X + 18, c.Y + c.Height - 14 - 84, c.Width - 36, 84);
        ContentInner(g, tc);
        Text(g, T(L"Taille totale", L"Total size"), RectF(tc.X + 14, tc.Y + 10, 160, 20), 12.5f, FontStyleBold, kInk, StringAlignmentNear);
        Text(g, SizeText(total), RectF(tc.X + tc.Width - 114, tc.Y + 10, 100, 20), 12.5f, FontStyleBold, kInk, StringAlignmentFar);
        float frac = min(1.0f, total / (100.0f * 1024 * 1024));
        RectF bar(tc.X + 14, tc.Y + 38, tc.Width - 28, 8);
        GraphicsPath bp; RoundRect(bp, bar, 4);
        SolidBrush bb(TH(toggleOff)); g.FillPath(&bb, &bp);
        if (frac > 0.005f) { GraphicsPath fp; RoundRect(fp, RectF(bar.X, bar.Y, max(8.0f, bar.Width * frac), 8), 4); SolidBrush fb(frac > 0.85f ? kWarn : kAcc); g.FillPath(&fb, &fp); }
        Text(g, T(L"Au-delà de 100 Mo, l'arrivée des invités ralentit.", L"Above 100 MB, guests take longer to arrive."), RectF(tc.X + 14, tc.Y + 54, tc.Width - 28, 18), 11.5f, FontStyleRegular, frac > 0.85f ? kWarn : kGrey, StringAlignmentNear);
    }
    // ---- a droite : le detail du groupe choisi, puis Posters et Peinture & drapeau, l'aide, ANNULER / VALIDER
    {
        RectF c = kContentR;
        if (g_contentSel < 0 || g_contentSel >= kCgN) g_contentSel = 0;
        float y = c.Y + 74;
        if (g_contentSel <= 1) DrawContentTracks(g, c, y);
        else DrawContentDetail(g, c, y);
        y += 6;
        float cw = (c.Width - 36 - 12) / 2, ch = 124;
        if (y + ch > c.Y + c.Height - 150) ch = max(96.0f, c.Y + c.Height - 150 - y);
        {   // posters
            RectF r(c.X + 18, y, cw, ch);
            ContentInner(g, r);
            ContentInfo pi = g_contentInfo[7];
            Text(g, T(L"Posters · ", L"Posters · ") + std::to_wstring(pi.n), RectF(r.X + 14, r.Y + 10, r.Width - 28 - 100, 36), 13.5f, FontStyleBold, kInk, StringAlignmentNear);
            ContentButton(g, RectF(r.X + r.Width - 14 - 92, r.Y + 10, 92, 34), T(L"Choisir", L"Choose"), 5210, false, !g_gameDir.empty());
            float th = ch - 52 - 12, tw2 = th * 44 / 60;
            for (int k = 0; k < 3; k++) {
                std::wstring rel = L"Images\\poster" + std::to_wstring(k + 1) + L".png";
                Bitmap *b = !g_gameDir.empty() && FileExists(g_gameDir + rel) ? ContentThumbFile(rel) : NULL;
                ContentDrawImage(g, b, RectF(r.X + 14 + k * (tw2 + 6), r.Y + 52, tw2, th), 6);
            }
            if (!pi.n) Text(g, T(L"Aucun poster dans Images", L"No poster in Images"), RectF(r.X + 14 + 3 * (tw2 + 6), r.Y + 52, r.Width - 28 - 3 * (tw2 + 6), th), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
        }
        {   // peinture & drapeau
            RectF r(c.X + 18 + cw + 12, y, cw, ch);
            ContentInner(g, r);
            Text(g, T(L"Peinture & drapeau", L"Paint & flag"), RectF(r.X + 14, r.Y + 10, r.Width - 28 - 100, 36), 13.5f, FontStyleBold, kInk, StringAlignmentNear);
            ContentButton(g, RectF(r.X + r.Width - 14 - 92, r.Y + 10, 92, 34), T(L"Choisir", L"Choose"), 5211, false, !g_gameDir.empty());
            float th = ch - 52 - 12;
            RectF pr(r.X + 14, r.Y + 52, th * 80 / 60, th);   // (clic : importer une peinture perso, voiture.inc)
            bool paintHot = g_contentHot == 5203;
            ContentDrawImage(g, PaintOn() ? PaintThumb() : ContentThumbFile(L"Images\\car.png"), pr, 8);
            if (paintHot) { GraphicsPath hp; RoundRect(hp, pr, 8); Pen hpn(kAcc, 1.6f); g.DrawPath(&hpn, &hp); }
            ContentZone(pr, 5203);
            RectF fr(pr.X + pr.Width + 8, r.Y + 52 + (th - th * 2 / 3) / 2, th * 0.9f, th * 2 / 3);
            bool hasFlag = !g_gameDir.empty() && FileExists(g_gameDir + L"Images\\flag_garage.png");
            ContentDrawImage(g, hasFlag ? ContentThumbFile(L"Images\\flag_garage.png") : NULL, fr, 4);
            std::wstring names = PaintNameFix(PaintOn() ? L"peinture.png" : L"car.png") + std::wstring(L"\n") + (hasFlag ? L"flag_garage.png" : T(L"pas de drapeau", L"no flag"));
            Para(g, names, RectF(fr.X + fr.Width + 10, r.Y + 52, r.Width - (fr.X + fr.Width + 10 - r.X) - 10, th), 11.5f, kGrey);
        }
        y += ch + 14;
        Para(g, T(L"Les invités reçoivent tout ça à l'arrivée dans ton salon, dans leur copie de lancement. Rien n'écrase leurs propres fichiers.",
                  L"Guests receive all this when they arrive in your lobby, in their launch copy. Nothing overwrites their own files."), RectF(c.X + 18, y, c.Width - 36, 40), 12, kGrey);
        // bas : resume, annuler / valider (valeurs d'avant rendues par ANNULER : ui.inc)
        float by = c.Y + c.Height - 14 - 44;
        Pen sep(TH(sep), 1); g.DrawLine(&sep, c.X + 18, by - 12, c.X + c.Width - 18, by - 12);
        Icon(g, IC_CHECK, c.X + 28, by + 22, 16, kOk, 2.4f);
        wchar_t sum[160];
        swprintf_s(sum, T(L"%d / %d éléments envoyés", L"%d / %d items sent"), on_, ContentCount());
        Text(g, sum, RectF(c.X + 46, by, c.Width - 46 - 290, 44), 13, FontStyleBold, kInk, StringAlignmentNear);
        UiButton(g, RectF(c.X + c.Width - 18 - 270, by, 124, 44), T(L"ANNULER", L"CANCEL"), UI_PAGE_CANCEL, false);
        UiButton(g, RectF(c.X + c.Width - 18 - 136, by, 136, 44), T(L"VALIDER", L"OK"), UI_PAGE_OK, true);
    }
}
