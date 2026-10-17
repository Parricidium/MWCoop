// (inclus par launcher.cpp, a cette place : TENUE (apparence, apercu, tenues offertes) -- decoupe de la refonte du 14/10)
// ---------------------------------------------------------------- onglet TENUE (Apparence, apercu facon GTA)
// Images pre-rendues par le mod dans <jeu>\MWCoop\cache\skins\ (apres ~30 s en jeu) :
//   <tenue>.png          : bande de 16 vues de 160 x 320 (RGBA, fond transparent) ; vue i = personnage tourne de
//                          i x 22,5 degres, vue 0 face a la camera ;
//   <tenue>-portrait.png : 128 x 128, tete et epaules (aussi dans le salon) ;
//   index.txt            : "MWSK 1 <version du mod>" puis "<tenue>\t<libelle>" par ligne ; ecrit en dernier : sans lui
//                          (ou avec un autre format), le dossier est ignore.
// Cache : PNG lus en memoire (le mod peut les reecrire, rien n'est garde ouvert) et copies en PARGB ; tout est oublie
// quand index.txt change (date verifiee au plus toutes les 3 s). Portraits charges au besoin (quelques-uns par image
// pour ne pas figer la fenetre), bandes seulement pour la tenue montree (6 au plus en memoire, ~3 Mo chacune).
// Choisir une tenue ecrit [Coop] Apparence comme l'onglet COOP ; dans un salon, LobbyTick l'annonce aussitot.
struct SkinPic {
    Bitmap *strip = NULL, *portrait = NULL;
    bool stripTried = false, portraitTried = false;
    DWORD used = 0;
    Bitmap *thumb[2] = {};                                  // portrait deja reduit et masque (0 galerie, 1 salon)
    int thumbW[2] = {}, thumbH[2] = {};
};
static std::map<std::string, SkinPic> g_skinPics;           // par tenue (nom en minuscules)
static std::map<std::string, std::wstring> g_skinLabels;    // libelles de index.txt (tenue inconnue du lanceur)
static std::wstring g_skinsArg;                             // /skins <dossier> : images de test
static std::wstring g_skinDirSeen;
static int g_skinState;                                     // 0 pas vu, 1 index.txt valide, -1 absent ou illisible
static FILETIME g_skinIdxMt;
static DWORD g_skinCheckT;
static int g_skinBudget = 1000;                             // portraits a charger pendant cette image
static bool g_skinAnnounce;                                 // salon : annoncer l'apparence sans attendre
// Page PERSONNAGE (maquette du 17/10) : onglets en haut (y 92), carte Apercu a gauche, carte des parties a droite.
static const RectF kSkinCard(264, 88, 390, 656), kSkinPanel(666, 88, 598, 656);   // (cartes a y 88 comme les autres pages, maquette du 17/10)
static const RectF kSkinView(280, 142, 358, 502);
static const float kPersoTabY = 26, kPersoTabH = 36;   // onglets Tenue / Visage / Voiture dans l'en-tete, a gauche des boutons ronds
static float g_persoTabRight = 1264;                   // bord droit des onglets (ui.inc DrawHeader)
static int g_lookZone;                                       // image perso montree dans la bande « Mes images » (0 visage, 1 haut, 2 pantalon)
static float g_skinYaw;                                     // vue montree (0..16, la plus proche est dessinee)
static bool g_skinDrag;
static float g_skinDragX;
static DWORD g_skinIdleT, g_skinChangeT;                    // fin du dernier glisser / changement de tenue
static int g_skinChangeDir = 1;
static int g_skinHot = -1, g_skinArrowHot;                  // portrait survole ; fleche survolee (-1, +1)
static Bitmap *g_skinFrame;                                 // vue montree, deja a la taille de l'ecran
static const Bitmap *g_skinFrameSrc;
static int g_skinFrameN = -1, g_skinFrameW, g_skinFrameH;

// PNG en memoire -> copie PARGB (dessin rapide) ; NULL si illisible ou trop grand.
static Bitmap *LoadPngMem(const void *p, size_t n, UINT maxW, UINT maxH)
{
    IStream *s = SHCreateMemStream((const BYTE *)p, (UINT)n);
    if (!s) return NULL;
    Bitmap *copy = NULL;
    Bitmap *b = Bitmap::FromStream(s);
    if (b && b->GetLastStatus() == Ok && b->GetWidth() > 0 && b->GetHeight() > 0 && b->GetWidth() <= maxW && b->GetHeight() <= maxH) {
        copy = new Bitmap(b->GetWidth(), b->GetHeight(), PixelFormat32bppPARGB);
        if (copy->GetLastStatus() != Ok) { delete copy; copy = NULL; }
        else {
            Graphics g(copy);
            g.SetCompositingMode(CompositingModeSourceCopy);
            g.DrawImage(b, 0, 0, b->GetWidth(), b->GetHeight());
        }
    }
    delete b;
    s->Release();
    return copy;
}

static std::wstring SkinsDir() { return !g_skinsArg.empty() ? g_skinsArg : g_gameDir.empty() ? L"" : g_gameDir + L"MWCoop\\cache\\skins\\"; }

// Nom de tenue sur (il vient aussi du reseau et finit dans un chemin) : lettres, chiffres, _ et -, en minuscules.
static std::string SkinKey(const std::string &skin)
{
    if (skin.empty()) return "";
    std::string k, top = skin.substr(0, skin.find('|'));   // (apparence complete : "haut|pantalon|...")
    if (top.size() > 64) return "";
    for (char c : top) {
        if (!isalnum((unsigned char)c) && c != '_' && c != '-') return "";
        k += (char)tolower((unsigned char)c);
    }
    return k;
}

static void SkinsFree()
{
    for (auto &kv : g_skinPics) { delete kv.second.strip; delete kv.second.portrait; delete kv.second.thumb[0]; delete kv.second.thumb[1]; }
    g_skinPics.clear();
    delete g_skinFrame;
    g_skinFrame = NULL;
    g_skinFrameSrc = NULL;
}

// index.txt apparu, disparu ou reecrit : cache oublie. Au plus toutes les 3 s.
static void SkinsCheck()
{
    DWORD now = GetTickCount();
    std::wstring dir = SkinsDir();
    if (g_skinState != 0 && dir == g_skinDirSeen && now - g_skinCheckT < 3000) return;
    g_skinCheckT = now;
    WIN32_FILE_ATTRIBUTE_DATA a;
    bool exists = !dir.empty() && GetFileAttributesExW((dir + L"index.txt").c_str(), GetFileExInfoStandard, &a) && !(a.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY);
    FILETIME mt = {};
    if (exists) mt = a.ftLastWriteTime;
    if (g_skinState != 0 && dir == g_skinDirSeen && exists == (g_skinState == 1) && CompareFileTime(&mt, &g_skinIdxMt) == 0) return;
    SkinsFree();
    g_skinLabels.clear();
    g_skinDirSeen = dir;
    g_skinIdxMt = mt;
    g_skinState = -1;
    std::vector<unsigned char> d;
    if (!exists || !ReadAll(dir + L"index.txt", d)) return;
    std::string t(d.begin(), d.end());
    if (t.size() >= 3 && (unsigned char)t[0] == 0xEF && (unsigned char)t[1] == 0xBB && (unsigned char)t[2] == 0xBF) t.erase(0, 3);
    size_t pos = 0;
    for (int line = 0; pos <= t.size(); line++) {
        size_t e = t.find('\n', pos);
        std::string l = t.substr(pos, e == std::string::npos ? std::string::npos : e - pos);
        pos = e == std::string::npos ? t.size() + 1 : e + 1;
        while (!l.empty() && (l.back() == '\r' || l.back() == ' ')) l.pop_back();
        if (line == 0) { if (l != "MWSK 1" && l.compare(0, 7, "MWSK 1 ")) return; g_skinState = 1; continue; }
        size_t tab = l.find('\t');
        std::string k = SkinKey(l.substr(0, tab));
        if (!k.empty() && tab != std::string::npos) g_skinLabels[k] = Trim(Widen(l.substr(tab + 1)));
    }
}

static SkinPic *SkinPicFor(const std::string &skin, std::string *key)
{
    if (g_skinState != 1) return NULL;
    *key = SkinKey(skin);
    return key->empty() ? NULL : &g_skinPics[*key];
}
static Bitmap *SkinLoad(const std::string &key, const wchar_t *suffix, UINT maxW, UINT maxH)
{
    std::vector<unsigned char> d;
    if (!ReadAll(g_skinDirSeen + Widen(key) + suffix, d) || d.empty()) return NULL;
    return LoadPngMem(d.data(), d.size(), maxW, maxH);
}
static Bitmap *SkinPortrait(const std::string &skin)
{
    std::string k;
    SkinPic *p = SkinPicFor(skin, &k);
    if (!p) return NULL;
    if (!p->portraitTried && g_skinBudget > 0) {
        g_skinBudget--;
        p->portraitTried = true;
        p->portrait = SkinLoad(k, L"-portrait.png", 1024, 1024);
    }
    return p->portrait;
}
static Bitmap *SkinStrip(const std::string &skin)
{
    std::string k;
    SkinPic *p = SkinPicFor(skin, &k);
    if (!p) return NULL;
    p->used = GetTickCount();
    if (!p->stripTried) {
        p->stripTried = true;
        p->strip = SkinLoad(k, L".png", 16 * 1024, 2048);
        if (p->strip && p->strip->GetWidth() < 16) { delete p->strip; p->strip = NULL; }
        for (;;) {   // au plus 6 bandes en memoire : la plus ancienne repart
            int n = 0;
            SkinPic *old = NULL;
            for (auto &kv : g_skinPics)
                if (kv.second.strip) { n++; if (&kv.second != p && (!old || kv.second.used < old->used)) old = &kv.second; }
            if (n <= 6 || !old) break;
            if (g_skinFrameSrc == old->strip) g_skinFrameSrc = NULL;
            delete old->strip; old->strip = NULL; old->stripTried = false;
        }
    }
    return p->strip;
}

// Portrait reduit une fois a la taille de l'ecran et masque (0 : carre arrondi de la galerie, 1 : cercle du salon),
// bords lisses ; ensuite copie pixel pour pixel a chaque image. Refait si la taille change.
static Bitmap *SkinThumb(const std::string &skin, int shape, float w, float h)
{
    Bitmap *pt = SkinPortrait(skin);
    if (!pt) return NULL;
    SkinPic &p = g_skinPics[SkinKey(skin)];
    int W = max(4, (int)(w * g_scale + 0.5f)), H = max(4, (int)(h * g_scale + 0.5f));
    if (p.thumb[shape] && p.thumbW[shape] == W && p.thumbH[shape] == H) return p.thumb[shape];
    delete p.thumb[shape];
    p.thumb[shape] = NULL;
    Bitmap tmp(W, H, PixelFormat32bppPARGB), *th = new Bitmap(W, H, PixelFormat32bppPARGB);
    float kx = W / w, ky = H / h;
    {
        Graphics tg(&tmp);
        tg.Clear(Color(0, 0, 0, 0));
        tg.SetInterpolationMode(InterpolationModeHighQualityBicubic);
        tg.SetPixelOffsetMode(PixelOffsetModeHalf);
        tg.DrawImage(pt, RectF(1 * kx, 2.5f * ky, (w - 2) * kx, (h - 2) * ky));
    }
    {
        Graphics g(th);
        g.Clear(Color(0, 0, 0, 0));
        g.SetSmoothingMode(SmoothingModeAntiAlias);
        GraphicsPath mask;
        if (shape == 0) RoundRect(mask, RectF(0, 0, (float)W, (float)H), 10 * kx);
        else mask.AddEllipse(RectF(0, 0, (float)W, (float)H));
        TextureBrush tb(&tmp, WrapModeClamp);
        g.FillPath(&tb, &mask);
    }
    p.thumb[shape] = th;
    p.thumbW[shape] = W;
    p.thumbH[shape] = H;
    return th;
}
// Copie d'une image deja a la taille de l'ecran (pas de reechantillonnage).
static void DrawPixels(Graphics &g, Bitmap *b, RectF r)
{
    InterpolationMode im = g.GetInterpolationMode();
    g.SetInterpolationMode(InterpolationModeNearestNeighbor);
    g.DrawImage(b, r);
    g.SetInterpolationMode(im);
}

// Pastille d'un joueur (salon) : son portrait dans un cercle borde de sa couleur, sinon l'initiale sur sa couleur.
static Bitmap *PersoPortrait(const std::string &skin, int W, int H);   // (perso.inc : apparence complete en 3D)
static void DrawSkinAvatar(Graphics &g, RectF c, const std::string &skin, Color col, const std::wstring &name)
{
    Bitmap *th = PersoPortrait(skin, max(8, (int)(c.Width * g_scale + 0.5f)), max(8, (int)(c.Height * g_scale + 0.5f)));
    if (!th) th = SkinThumb(skin, 1, c.Width, c.Height);
    if (!th) {
        SolidBrush ab(col);
        g.FillEllipse(&ab, c);
        Text(g, name.empty() ? L"?" : name.substr(0, 1), RectF(c.X, c.Y + 0.5f, c.Width, c.Height), c.Height * 0.45f, FontStyleBold, Color(255, 255, 255, 255));
        return;
    }
    LinearGradientBrush bg(RectF(c.X, c.Y - 1, c.Width, c.Height + 2), Mix(TH(card), col, g_dark ? 0.55f : 0.40f), Mix(TH(card), col, g_dark ? 0.25f : 0.15f), LinearGradientModeVertical);
    g.FillEllipse(&bg, c);
    DrawPixels(g, th, c);
    Pen ring(col, 2.0f);
    g.DrawEllipse(&ring, c);
}

static const Opt *SkinOpt() { return OptByKey("Apparence"); }
static int SkinCount() { const Opt *ap = SkinOpt(); return ap ? (int)ap->svals.size() : 0; }
// (ancienne galerie des hauts et fleches de rotation : plus rien a survoler, les tuiles passent par PersoPageHit ; launcher.cpp les appelle encore)
static int SkinCellAt(float, float) { return -1; }
static int SkinArrowAt(float, float) { return 0; }

static void SkinSelect(int i, int dir)
{
    const Opt *ap = SkinOpt();
    if (!ap || i < 0 || i >= (int)ap->svals.size() || i == OptGet(*ap)) return;
    OptSet(*ap, i);
    g_skinAnnounce = true;
    g_skinYaw = 0;   // nouvelle tenue : de face, puis la rotation reprend
    g_skinChangeT = g_skinIdleT = GetTickCount();
    g_skinChangeDir = dir;
}
static void SkinStep(int dir)
{
    int n = SkinCount();
    if (n <= 0) return;
    SkinSelect((OptGet(*SkinOpt()) + dir + n) % n, dir);
}

static std::string MyShirt();
static std::wstring SkinLabel(const std::string &skin);
// ---------------------------------------------------------------- tenues offertes (credits : onglet CREDITS)
// Pack de Dom (08/10/2026, demande de JD) : 155 tenues (84 hauts, 27 pantalons, 44 visages) sur le patron des vetements
// des PNJ. Telecharge une fois (8,7 Mo) depuis le depot (assets/tenues), a part des mises a jour du mod, dans
// MWCoop\tenues\<auteur>\ : lu par le mod (Sync\Tenues.cs) et par l'apercu 3D d'ici. Noms a part (dom_haut_NN...) :
// jamais a la place des textures du jeu. Pack absent chez un joueur : la tenue d'origine.
struct TenuePack { const char *id; const char *ver; const wchar_t *zip; };
// (dom 2, 08/10 : sans dom_haut_77 ni dom_haut_83, retires a la demande de JD ; pas de renumerotation)
static const TenuePack kTenuePacks[] = { { "dom", "2", L"tenues-dom-2.zip" } };
static std::atomic<int> g_tenuesState(0);   // 0 rien, 1 en cours, 2 recu (listes a completer), 3 echec
static std::wstring TenuesDir() { return g_gameDir.empty() ? L"" : g_gameDir + L"MWCoop\\tenues\\"; }
static bool IsTenue(const std::string &n) { return n.find("_haut_") != std::string::npos || n.find("_pantalon_") != std::string::npos || n.find("_visage_") != std::string::npos; }
// "dom_haut_05" -> "Dom 5"
static std::wstring TenueLabel(const std::string &n)
{
    size_t a = n.find('_'), b = n.rfind('_');
    if (a == std::string::npos || a == 0 || b == a) return Widen(n);
    std::wstring who = Widen(n.substr(0, a));
    who[0] = towupper(who[0]);
    return who + L" " + std::to_wstring(atoi(n.c_str() + b + 1));
}
static std::wstring TenueFile(const std::string &n) { size_t a = n.find('_'); return a == std::string::npos || TenuesDir().empty() ? L"" : TenuesDir() + Widen(n.substr(0, a)) + L"\\" + Widen(n) + L".jpg"; }
static bool TenuePackHere(const TenuePack &p)
{
    std::vector<unsigned char> d;
    if (!ReadAll(TenuesDir() + Widen(p.id) + L"\\version.txt", d)) return false;
    return std::string(d.begin(), d.end()).find(p.ver) == 0;
}
static DWORD WINAPI TenuesThread(void *)
{
    bool any = false, fail = false;
    for (const TenuePack &p : kTenuePacks) {
        if (TenuePackHere(p)) continue;
        std::wstring url = L"https://raw.githubusercontent.com/" + g_repo + L"/main/assets/tenues/" + p.zip;
        wchar_t tmp[MAX_PATH] = L"";
        GetTempPathW(MAX_PATH, tmp);
        std::wstring zf = std::wstring(tmp) + L"mwcoop-" + p.zip;
        DWORD st = 0;
        std::string why;
        if (!HttpGet(url, NULL, zf, false, &st) || st != 200) { LaunchLog("tenues offertes : %s : telechargement impossible (HTTP %lu)", p.id, st); fail = true; DeleteFileW(zf.c_str()); continue; }
        SHCreateDirectoryExW(NULL, TenuesDir().c_str(), NULL);
        {   // version precedente : ses fichiers retires d'abord (une tenue enlevee du pack ne doit pas rester)
            std::wstring pd = TenuesDir() + Widen(p.id) + L"\\";
            WIN32_FIND_DATAW fd;
            HANDLE h = FindFirstFileW((pd + L"*").c_str(), &fd);
            int n = 0;
            if (h != INVALID_HANDLE_VALUE) {
                do { if (!(fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) && DeleteFileW((pd + fd.cFileName).c_str())) n++; } while (FindNextFileW(h, &fd));
                FindClose(h);
            }
            if (n) LaunchLog("tenues offertes : %s : %d fichier(s) de la version precedente retires", p.id, n);
        }
        if (!Unzip(zf, TenuesDir(), &why)) { LaunchLog("tenues offertes : %s : %s", p.id, why.c_str()); fail = true; }
        else { LaunchLog("tenues offertes : pack %s (version %s) recu", p.id, p.ver); any = true; }
        DeleteFileW(zf.c_str());
    }
    g_tenuesState = fail ? 3 : any ? 2 : 0;
    return 0;
}
static void TenuesStart()
{
    if (g_gameDir.empty() || g_tenuesState == 1 || !g_testSalon.empty()) return;
    bool need = false;
    for (const TenuePack &p : kTenuePacks) need |= !TenuePackHere(p);
    if (!need) return;
    g_tenuesState = 1;
    HANDLE t = CreateThread(NULL, 0, TenuesThread, NULL, 0, NULL);
    if (t) CloseHandle(t); else g_tenuesState = 0;
}
// Tenues offertes presentes (noms, tries) : hauts, pantalons, visages.
static std::vector<std::string> g_tenueNames[3];
static std::vector<std::string> TenuesFind(const wchar_t *pattern)
{
    std::vector<std::string> names;
    if (TenuesDir().empty()) return names;
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((TenuesDir() + L"*").c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return names;
    do {
        if (!(fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) || fd.cFileName[0] == L'.') continue;
        WIN32_FIND_DATAW f2;
        HANDLE h2 = FindFirstFileW((TenuesDir() + fd.cFileName + L"\\" + pattern).c_str(), &f2);
        if (h2 == INVALID_HANDLE_VALUE) continue;
        do { std::wstring n = f2.cFileName; names.push_back(Narrow(n.substr(0, n.size() - 4), CP_UTF8)); } while (FindNextFileW(h2, &f2));
        FindClose(h2);
    } while (FindNextFileW(h, &fd));
    FindClose(h);
    std::sort(names.begin(), names.end());
    return names;
}
// Hauts offerts dans le choix de tenue, apres ceux du jeu ("Tenue Dom 5") ; pantalons et visages gardes pour PersoChoices
// (sans attendre que le jeu refasse son export : retour de JD, 08/10, 14 visages seulement juste apres le telechargement).
static void TenuesAddShirts()
{
    g_tenueNames[0] = TenuesFind(L"*_haut_*.jpg");
    g_tenueNames[1] = TenuesFind(L"*_pantalon_*.jpg");
    g_tenueNames[2] = TenuesFind(L"*_visage_*.jpg");
    Opt *ap = NULL;
    for (auto &o : g_opts) if (!strcmp(o.key, "Apparence")) ap = &o;
    if (!ap) return;
    for (auto &n : g_tenueNames[0]) {
        if (std::find(ap->svals.begin(), ap->svals.end(), n) != ap->svals.end()) continue;
        std::wstring lab = TenueLabel(n);
        ap->vals.push_back((int)ap->svals.size());
        ap->svals.push_back(n);
        ap->labFr.push_back(L"Tenue " + lab);
        ap->labEn.push_back(L"Outfit " + lab);
    }
}

#include "perso.inc"

// Silhouette neutre (pas d'image) : tete et buste.
static void DrawSkinGhost(Graphics &g, float cx, float top, float k)
{
    SolidBrush b(WithA(kGrey, 0.22f));
    g.FillEllipse(&b, cx - 22 * k, top, 44 * k, 48 * k);
    GraphicsPath bp;
    RoundRect(bp, RectF(cx - 42 * k, top + 56 * k, 84 * k, 96 * k), 24 * k);
    g.FillPath(&b, &bp);
}

// ---- images importees (page PERSONNAGE) : %LOCALAPPDATA%\MWCoop\visage.jpg, haut.jpg, pantalon.jpg, a la taille de la texture du
// jeu (gabarit) ; choisies dans la liste de leur partie ("perso", perso.inc), montrees aux autres par le mod (CustomFace).
// Gabarits : ecrits par le mod au 1er passage en jeu de chaque version (gabarit-visage.png, gabarit-haut.png, gabarit-pantalon.png).
// La bande « Mes images » (onglet Visage) suit g_lookZone (0 visage, 1 haut, 2 pantalon) ; les tuiles « Mon visage / haut /
// pantalon » des grilles lancent l'import quand l'image n'est pas la.
static bool EncoderClsid(const wchar_t *mime, CLSID *out);
static Bitmap *g_persoThumb[3];
static bool g_persoThumbLooked[3];
static RectF g_faceRect;   // bande « Mes images » (bulle du guide)
static int PersoImgZone() { return g_lookZone == 1 ? PI_SHIRT : g_lookZone == 2 ? PI_PANTS : PI_FACE; }
static void PersoImgChanged(int k)
{
    g_persoThumbLooked[k] = false;
    g_perso.tex.erase(std::string("#perso-") + (char)('0' + k));
    g_persoDrawn.clear();
    for (auto &kv : g_persoPortraits) delete kv.second;
    g_persoPortraits.clear();
}
// Image d'un fichier : GDI+ (PNG, JPG, BMP, GIF, TIFF), sinon le decodeur d'images de Windows (WIC : WebP, HEIC... --
// retour d'un joueur, 10/10 : « la deuxieme image ne se charge pas »). Lue en memoire : le fichier n'est pas bloque.
static Bitmap *LoadAnyImage(const std::wstring &file, std::wstring *why)
{
    std::vector<unsigned char> data;
    if (!ReadAll(file, data) || data.empty()) { if (why) *why = T(L"fichier illisible", L"file can't be read"); return NULL; }
    Bitmap *out = NULL;
    if (IStream *st = SHCreateMemStream(data.data(), (UINT)data.size())) {
        Bitmap *tmp = Bitmap::FromStream(st);
        if (tmp && tmp->GetLastStatus() == Ok && tmp->GetWidth() > 0) out = tmp->Clone(0, 0, (INT)tmp->GetWidth(), (INT)tmp->GetHeight(), PixelFormat32bppPARGB);
        delete tmp;
        st->Release();
        if (out && out->GetLastStatus() != Ok) { delete out; out = NULL; }
    }
    if (out) return out;
    static const GUID kClsidWic = { 0xcacaf262, 0x9370, 0x4615, { 0xa1, 0x3b, 0x9f, 0x55, 0x39, 0xda, 0x4c, 0x0a } };
    static const GUID kPbgra = { 0x6fddc324, 0x4e03, 0x4bfe, { 0xb1, 0x85, 0x3d, 0x77, 0x76, 0x8d, 0xc9, 0x10 } };
    HRESULT co = CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    IWICImagingFactory *f = NULL; IWICStream *ws = NULL; IWICBitmapDecoder *d = NULL; IWICBitmapFrameDecode *fr = NULL; IWICFormatConverter *cv = NULL;
    if (SUCCEEDED(CoCreateInstance(kClsidWic, NULL, CLSCTX_INPROC_SERVER, __uuidof(IWICImagingFactory), (void **)&f))
        && SUCCEEDED(f->CreateStream(&ws)) && SUCCEEDED(ws->InitializeFromMemory(data.data(), (DWORD)data.size()))
        && SUCCEEDED(f->CreateDecoderFromStream(ws, NULL, WICDecodeMetadataCacheOnDemand, &d))
        && SUCCEEDED(d->GetFrame(0, &fr)) && SUCCEEDED(f->CreateFormatConverter(&cv))
        && SUCCEEDED(cv->Initialize(fr, kPbgra, WICBitmapDitherTypeNone, NULL, 0, WICBitmapPaletteTypeCustom))) {
        UINT w = 0, h = 0;
        cv->GetSize(&w, &h);
        if (w > 0 && h > 0 && w <= 16384 && h <= 16384) {
            out = new Bitmap((INT)w, (INT)h, PixelFormat32bppPARGB);
            BitmapData bd;
            Rect r(0, 0, (INT)w, (INT)h);
            if (out->LockBits(&r, ImageLockModeWrite, PixelFormat32bppPARGB, &bd) == Ok) {
                HRESULT hr = cv->CopyPixels(NULL, (UINT)bd.Stride, (UINT)bd.Stride * h, (BYTE *)bd.Scan0);
                out->UnlockBits(&bd);
                if (FAILED(hr)) { delete out; out = NULL; }
            } else { delete out; out = NULL; }
        }
    }
    if (cv) cv->Release();
    if (fr) fr->Release();
    if (d) d->Release();
    if (ws) ws->Release();
    if (f) f->Release();
    if (SUCCEEDED(co)) CoUninitialize();
    if (!out && why) *why = T(L"format d'image non reconnu par Windows (essaie PNG ou JPG)", L"image format not recognized by Windows (try PNG or JPG)");
    return out;
}
static Bitmap *PersoThumb(int k)
{
    if (!g_persoThumbLooked[k]) {
        g_persoThumbLooked[k] = true;
        delete g_persoThumb[k]; g_persoThumb[k] = NULL;
        if (PersoImgHas(k)) g_persoThumb[k] = LoadAnyImage(PersoImgFile(k), NULL);
    }
    return g_persoThumb[k];
}
static void FaceImport()
{
    int k = PersoImgZone();
    wchar_t file[MAX_PATH] = L"";
    OPENFILENAMEW of = { sizeof(of) };
    of.hwndOwner = g_wnd;
    of.lpstrFilter = L"Images\0*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic;*.heif;*.avif;*.jfif\0*.*\0*.*\0";
    of.lpstrFile = file;
    of.nMaxFile = MAX_PATH;
    std::wstring init = LocalDir();
    of.lpstrInitialDir = init.c_str();
    static const wchar_t *titFr[3] = { L"Choisir l'image du visage (peinte sur le gabarit)", L"Choisir l'image du haut (peinte sur le gabarit)", L"Choisir l'image du pantalon (peinte sur le gabarit)" };
    static const wchar_t *titEn[3] = { L"Choose the face image (painted on the template)", L"Choose the top image (painted on the template)", L"Choose the pants image (painted on the template)" };
    of.lpstrTitle = T(titFr[k], titEn[k]);
    of.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_HIDEREADONLY;
    if (!GetOpenFileNameW(&of)) return;
    std::wstring why;
    Bitmap *src = LoadAnyImage(file, &why);
    bool ok = false;
    if (src) {
        // taille de la texture du jeu (gabarit ecrit par le mod), sinon celle des PNJ (visage 512 x 256)
        int W = 512, H = k == PI_FACE ? 256 : 512;
        Bitmap *game = LoadAnyImage(LocalDir() + kPersoImg[k] + L"-jeu.png", NULL);
        if (Bitmap *tpl = game ? game : LoadAnyImage(LocalDir() + L"gabarit-" + kPersoImg[k] + L".png", NULL)) {
            W = (int)tpl->GetWidth(); H = (int)tpl->GetHeight();
            if (tpl != game) delete tpl;
        }
        Bitmap dst(W, H, PixelFormat24bppRGB);
        {
            Graphics dg(&dst);
            dg.SetInterpolationMode(InterpolationModeHighQualityBicubic);
            dg.SetPixelOffsetMode(PixelOffsetModeHalf);
            if (game) dg.DrawImage(game, RectF(0, 0, (REAL)W, (REAL)H));   // (parties transparentes : la texture du jeu)
            else dg.Clear(Color(255, 128, 128, 128));
            dg.DrawImage(src, RectF(0, 0, (REAL)W, (REAL)H));
        }
        delete game;
        CLSID jpg;
        EncoderParameters ep; ULONG q = 90;
        ep.Count = 1; ep.Parameter[0].Guid = EncoderQuality; ep.Parameter[0].Type = EncoderParameterValueTypeLong; ep.Parameter[0].NumberOfValues = 1; ep.Parameter[0].Value = &q;
        CreateDirectoryW(LocalDir().c_str(), NULL);
        ok = EncoderClsid(L"image/jpeg", &jpg) && dst.Save(PersoImgFile(k).c_str(), &jpg, &ep) == Ok;
        if (!ok) why = T(L"enregistrement impossible", L"can't save it");
        LaunchLog("image perso importee (%ls) : %dx%d", kPersoImg[k], W, H);
    }
    delete src;
    PersoImgChanged(k);
    if (ok) { PersoSet(PersoPartOf(k), "perso"); g_skinAnnounce = true; g_skinChangeT = GetTickCount(); }   // (choisie : montree en direct)
    else {
        LaunchLog("image perso : echec (%ls)", why.c_str());
        std::wstring msg = std::wstring(T(L"Cette image n'a pas pu être lue : ", L"This image could not be read: ")) + why + L".";
        MessageBoxW(g_wnd, msg.c_str(), L"MWCoop", MB_ICONWARNING | MB_OK);
    }
}
static void FaceTemplate()
{
    int k = PersoImgZone();
    std::wstring t = LocalDir() + L"gabarit-" + kPersoImg[k] + L".png";
    if (FileExists(t)) { std::wstring a = L"/select,\"" + t + L"\""; ShellExecuteW(g_wnd, L"open", L"explorer.exe", a.c_str(), NULL, SW_SHOWNORMAL); return; }
    MessageBoxW(g_wnd, T(L"Les gabarits sont créés par le mod : lance une partie (solo suffit) et reste ~20 s en jeu avec cette version, puis reviens ici.\n\n"
                         L"Chacun montre la texture du jeu (visage, haut ou pantalon) avec ses coutures : peins par-dessus en gardant la taille, puis importe.",
                         L"The templates are made by the mod: start a game (solo is fine) and stay ~20 s in game with this version, then come back here.\n\n"
                         L"Each shows the game's texture (face, top or pants) with its seams: paint over it keeping the size, then import."),
                L"MWCoop", MB_ICONINFORMATION | MB_OK);
}
static void FaceRemove()
{
    int k = PersoImgZone(), f = PersoPartOf(k);
    DeleteFileW(PersoImgFile(k).c_str());
    if (PersoGet(f) == "perso") PersoSet(f, f == PF_SHIRT ? "char_shirt21" : "");
    PersoImgChanged(k);
    g_skinAnnounce = true;
    LaunchLog("image perso retiree (%ls)", kPersoImg[k]);
}
// Visage importe en 0.69-0.70 (montre d'office, [Coop] VisagePerso) : maintenant choisi dans la liste -- repris une fois.
static void PersoMigrate()
{
    if (GetPrivateProfileIntW(L"Lanceur", L"PersoListe", 0, g_iniLauncher.c_str())) return;
    WritePrivateProfileStringW(L"Lanceur", L"PersoListe", L"1", g_iniLauncher.c_str());
    if (!PersoImgHas(PI_FACE) || GetPrivateProfileIntA("Coop", "VisagePerso", 1, ModIniA().c_str()) == 0 || !PersoGet(PF_FACE).empty()) return;
    PersoSet(PF_FACE, "perso");
    LaunchLog("visage importe : choisi dans la liste (Visage perso)");
}

// ---------------------------------------------------------------- page PERSONNAGE (maquette validee le 17/10)
// Onglets Tenue / Visage / Voiture en haut de la page (Voiture : la page VOITURE de voiture.inc, g_tab = TAB_CAR, memes
// onglets). A gauche la carte Apercu : vue Pied / Portrait, le personnage (glisser pour tourner), le portrait tel que le
// salon le montre en bas a droite, puis Aleatoire / Comme avant / Enregistrer. A droite les parties en grilles de tuiles
// (Tenue : haut, bas, silhouette, puis la couleur de la voiture ; Visage : visage, chapeau, lunettes, cheveux, puis « Mes
// images »). Une tuile « + N » tourne les pages quand il y a plus de choix que de places.
// Les tuiles, onglets, pastilles et la vue ne passent pas par HotZone (UiClick, ui.inc, ne les connait pas) : PersoPageHit
// donne un code, SkinMouseDown / CarMouseDown l'appliquent (PersoPageMouseDown), g_skinRowHot (TENUE) ou g_carHot - 1000
// (VOITURE) le gardent pour le survol.
enum { HT_TAB = 0, HT_VIEW = 10, HT_SWATCH = 20, HT_PAINT = 40, HT_TILE = 100 };   // HT_TILE + partie * 16 + i (i = 15 : « + N »)
static int g_persoTab;                                     // 0 Tenue, 1 Visage (Voiture : g_tab == TAB_CAR)
static int g_persoView;                                    // 0 Pied (en entier), 1 Portrait
static int g_tilePage[PF_COUNT] = { -1, -1, -1, -1, -1, -1, -1 };   // page montree par partie (-1 : celle du choix actuel)
static const int kTileCols = 7;
static const float kTileH = 92, kTileGap = 8;
static const int kPersoPal[16] = { 0x3E86D0, 0xE0702A, 0x2A9C9A, 0x8E5BD6, 0xD64545, 0xC9971A, 0xD45D9A, 0x6C7A89, 0x55B04C, 0x3FBFD9, 0xE8A33C, 0xB24FA5, 0x8C6239, 0x5C6BC0, 0xA9C43A, 0xE88A7A };   // (couleur du joueur : meme palette que le salon et Sync/Markers.cs)
struct TileSec { int f; const wchar_t *fr, *en; int rows; };
static const TileSec kTenueSecs[] = { { PF_SHIRT, L"HAUT", L"TOP", 2 }, { PF_PANTS, L"BAS", L"BOTTOM", 1 }, { PF_BODY, L"SILHOUETTE", L"BUILD", 1 } };
static const TileSec kVisageSecs[] = { { PF_FACE, L"VISAGE", L"FACE", 1 }, { PF_HAT, L"CHAPEAU", L"HAT", 1 }, { PF_GLASSES, L"LUNETTES", L"GLASSES", 1 }, { PF_HAIR, L"CHEVEUX", L"HAIR", 1 } };
static const wchar_t *kPersoTabFr[3] = { L"Tenue", L"Visage", L"Voiture" }, *kPersoTabEn[3] = { L"Outfit", L"Face", L"Car" };

// Choix d'une partie ; en tete, la tuile « Mon visage / haut / pantalon » (import) quand l'image n'est pas encore la.
static std::vector<std::string> PersoTiles(int f)
{
    std::vector<std::string> v;
    int k = PersoImgOf(f);
    if (k >= 0 && !PersoImgHas(k)) v.push_back("#import");
    for (auto &s : PersoChoices(f)) v.push_back(s);
    return v;
}
static int PersoIndexOf(const std::vector<std::string> &v, const std::string &cur) { for (int i = 0; i < (int)v.size(); i++) if (v[i] == cur) return i; return 0; }

// Mise en page des sections de l'onglet montre : y, choix, pagination. Sans export du mod : seulement les hauts.
struct SecLay { const TileSec *sec; float y; int n, per, pages, page; std::vector<std::string> tiles; };
static float TileW() { return (kSkinPanel.Width - 36 - (kTileCols - 1) * kTileGap) / kTileCols; }
static RectF TileRect(const SecLay &L, int i) { return RectF(kSkinPanel.X + 18 + (i % kTileCols) * (TileW() + kTileGap), L.y + 24 + (i / kTileCols) * (kTileH + kTileGap), TileW(), kTileH); }
static float PersoLayout(std::vector<SecLay> &out)
{
    bool perso = g_perso.state == 1;
    const TileSec *secs = g_persoTab ? kVisageSecs : kTenueSecs;
    int ns = g_persoTab ? (int)_countof(kVisageSecs) : (int)_countof(kTenueSecs);
    float y = kSkinPanel.Y + 50;
    for (int s = 0; s < ns; s++) {
        if (!perso && secs[s].f != PF_SHIRT) continue;
        SecLay L;
        L.sec = &secs[s]; L.y = y; L.tiles = PersoTiles(secs[s].f); L.n = (int)L.tiles.size();
        int cap = secs[s].rows * kTileCols;
        L.per = L.n <= cap ? cap : cap - 1;   // (une place pour « + N »)
        L.pages = max(1, (L.n + L.per - 1) / L.per);
        int sel = PersoIndexOf(L.tiles, PersoGet(secs[s].f));
        L.page = g_tilePage[secs[s].f] < 0 ? sel / L.per : g_tilePage[secs[s].f] % L.pages;
        out.push_back(L);
        y += 24 + secs[s].rows * (kTileH + kTileGap) - kTileGap + 17;
    }
    return y;
}
static RectF PersoStripRect() { float b = kSkinPanel.Y + kSkinPanel.Height - 16; return g_persoTab ? RectF(kSkinPanel.X + 18, b - 62, kSkinPanel.Width - 36, 62) : RectF(kSkinPanel.X + 18, b - 86, kSkinPanel.Width - 36, 86); }   // (en bas de la carte)
static RectF PersoSwatchRect(int i) { RectF s = PersoStripRect(); return RectF(s.X + 12 + i * 32.0f, s.Y + 50, 26, 26); }
static RectF PersoPaintRect() { RectF s = PersoStripRect(); return RectF(s.X + s.Width - 12 - 136, s.Y + 8, 136, 34); }
static RectF PersoViewSegRect() { return RectF(kSkinCard.X + kSkinCard.Width - 16 - 150, kSkinCard.Y + 14, 150, 30); }
static float PersoTabW(int k) { return PillW(T(kPersoTabFr[k], kPersoTabEn[k]), 13) + 16; }
static float PersoTabsW() { float w = 0; for (int k = 0; k < 3; k++) w += PersoTabW(k) + (k ? 6 : 0); return w; }
static RectF PersoTabRect(int k) { float x = g_persoTabRight - PersoTabsW(); for (int j = 0; j < k; j++) x += PersoTabW(j) + 6; return RectF(x, kPersoTabY, PersoTabW(k), kPersoTabH); }

// Onglets de la page (TENUE et VOITURE), dessines par l'en-tete (ui.inc DrawHeader) : pilules comme la maquette.
static void PersoTabsDraw(Graphics &g)
{
    int sel = g_tab == TAB_CAR ? 2 : g_persoTab;
    for (int k = 0; k < 3; k++) {
        RectF r = PersoTabRect(k);
        bool on = k == sel, hot = g_tab == TAB_CAR ? g_carHot == 1000 + HT_TAB + k : g_skinRowHot == HT_TAB + k;
        GraphicsPath p; RoundRect(p, r, r.Height / 2);
        SolidBrush b(on ? kAcc : hot ? TH(cardSel) : TH(btn2)); g.FillPath(&b, &p);
        if (!on) { Pen pen(TH(fieldBorder), 1); g.DrawPath(&pen, &p); }
        Text(g, T(kPersoTabFr[k], kPersoTabEn[k]), r, 13, FontStyleBold, on ? kOnAcc : kInk);
    }
}

// Element sous la souris : code HT_*, -1 sinon (onglets sur les deux pages ; le reste sur TENUE).
static int PersoPageHit(float x, float y)
{
    for (int k = 0; k < 3; k++) if (PersoTabRect(k).Contains(x, y)) return HT_TAB + k;
    if (g_tab != TAB_SKIN) return -1;
    if (g_perso.state == 1) { RectF s = PersoViewSegRect(); if (s.Contains(x, y)) return HT_VIEW + (x < s.X + s.Width / 2 ? 0 : 1); }
    std::vector<SecLay> lay;
    PersoLayout(lay);
    for (auto &L : lay) {
        int shown = min(L.per, L.n - L.page * L.per);
        for (int i = 0; i < shown; i++) if (TileRect(L, i).Contains(x, y)) return HT_TILE + L.sec->f * 16 + i;
        if (L.n > L.per && TileRect(L, shown).Contains(x, y)) return HT_TILE + L.sec->f * 16 + 15;
    }
    if (!g_persoTab) {
        for (int i = 0; i < kSwatchN; i++) { RectF r = PersoSwatchRect(i); r.Inflate(3, 3); if (r.Contains(x, y)) return HT_SWATCH + i; }
        if (PersoPaintRect().Contains(x, y)) return HT_PAINT;
    }
    return -1;
}
static bool PersoPageMouseDown(float x, float y)
{
    int t = PersoPageHit(x, y);
    if (t < 0) return false;
    if (t < HT_TAB + 3) {   // (meme volet : les valeurs a rendre restent, comme l'onglet de l'en-tete)
        int k = t - HT_TAB;
        if (k == 2) { g_tab = TAB_CAR; g_carIdleT = GetTickCount(); }
        else { g_tab = TAB_SKIN; g_persoTab = k; g_skinIdleT = GetTickCount(); for (auto &p : g_tilePage) p = -1; }
        g_skinRowHot = -1; g_carHot = -1;
        return true;
    }
    if (t >= HT_VIEW && t < HT_VIEW + 2) { g_persoView = t - HT_VIEW; return true; }
    if (t >= HT_SWATCH && t < HT_SWATCH + kSwatchN) { CarSetColor(CarSwatchColor(t - HT_SWATCH)); return true; }
    if (t == HT_PAINT) { g_tab = TAB_CAR; g_paintView = true; g_vinView = false; g_skinRowHot = -1; return true; }
    int f = (t - HT_TILE) / 16, i = (t - HT_TILE) % 16;
    if (f < 0 || f >= PF_COUNT) return false;
    std::vector<SecLay> lay;
    PersoLayout(lay);
    for (auto &L : lay) {
        if (L.sec->f != f) continue;
        if (i == 15) { g_tilePage[f] = (L.page + 1) % L.pages; return true; }
        int at = L.page * L.per + i;
        if (at >= L.n) return true;
        const std::string &v = L.tiles[at];
        if (v == "#import") { g_lookZone = f == PF_SHIRT ? 1 : f == PF_PANTS ? 2 : 0; FaceImport(); return true; }
        if (g_tilePage[f] < 0) g_tilePage[f] = L.page;   // (la page reste : on compare les voisins)
        if (f == PF_SHIRT) { const Opt *ap = SkinOpt(); int cur = ap ? OptGet(*ap) : 0; SkinSelect(ap ? (int)(std::find(ap->svals.begin(), ap->svals.end(), v) - ap->svals.begin()) : 0, at < cur ? -1 : 1); }
        else { PersoSet(f, v); g_skinChangeT = GetTickCount(); g_skinAnnounce = true; }
        return true;
    }
    return true;
}

// Vignette d'une tuile : la texture de la matiere (export du mod, pack offert, ou image importee), reduite a 48 px par
// moyenne de blocs (la couleur d'ensemble, pas le bruit de l'atlas). NULL : pas d'image (bloc gris).
static Bitmap *TileBmp(int f, const std::string &v)
{
    std::string mat = v;
    if (f == PF_SHIRT || f == PF_PANTS || f == PF_FACE) {
        if (v == "perso") return PersoThumb(PersoImgOf(f));
        if (g_perso.state != 1) return NULL;
        if (v.empty()) { const PersoBody *b = PersoBodyFor(PersoGet(PF_BODY)); if (!b) return NULL; mat = b->def[f == PF_SHIRT ? 0 : f == PF_PANTS ? 1 : 2]; }
    } else if (f == PF_HAT || f == PF_GLASSES || f == PF_HAIR) {
        mat.clear();
        for (auto &a : g_perso.accs) if (a.key == v && !a.mats.empty()) mat = a.mats[0];
        if (mat.empty()) return NULL;
    } else return NULL;
    auto it = g_tileBmp.find(mat);
    if (it != g_tileBmp.end()) return it->second;
    const PersoTex &tx = PersoTexFor(mat);
    Bitmap *out = NULL;
    if (tx.w > 0 && tx.h > 0) {
        const int S = 48;
        out = new Bitmap(S, S, PixelFormat32bppARGB);
        BitmapData bd;
        Rect r(0, 0, S, S);
        if (out->LockBits(&r, ImageLockModeWrite, PixelFormat32bppARGB, &bd) == Ok) {
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++) {
                    int x0 = x * tx.w / S, x1 = max(x0 + 1, (x + 1) * tx.w / S), y0 = y * tx.h / S, y1 = max(y0 + 1, (y + 1) * tx.h / S);
                    unsigned sr = 0, sg = 0, sb = 0, n = 0;
                    for (int yy = y0; yy < y1 && yy < tx.h; yy++)
                        for (int xx = x0; xx < x1 && xx < tx.w; xx++) {
                            uint32_t c = tx.px[(size_t)yy * tx.w + xx];
                            if ((c >> 24) < 64) continue;
                            sr += (c >> 16) & 255; sg += (c >> 8) & 255; sb += c & 255; n++;
                        }
                    uint32_t c = 0;
                    if (n) c = 0xFF000000u | (min(255u, (unsigned)(sr / n * tx.col[0])) << 16) | (min(255u, (unsigned)(sg / n * tx.col[1])) << 8) | min(255u, (unsigned)(sb / n * tx.col[2]));
                    ((uint32_t *)((BYTE *)bd.Scan0 + (size_t)y * bd.Stride))[x] = c;
                }
            out->UnlockBits(&bd);
        } else { delete out; out = NULL; }
    }
    g_tileBmp[mat] = out;
    return out;
}

// Libelle court d'une tuile (73 px) : « Dom 38 » plutot que « Visage Dom 38 », la rubrique le dit deja.
static std::wstring TileLabel(int f, const std::string &v)
{
    if (v == "#import") return f == PF_SHIRT ? T(L"Mon haut", L"My top") : f == PF_PANTS ? T(L"Mon pantalon", L"My pants") : T(L"Mon visage", L"My face");
    std::wstring l = PersoLabel(f, v);
    if (IsTenue(v)) { size_t sp = l.find(L' '); if (sp != std::wstring::npos) l = l.substr(sp + 1); }
    if (f == PF_GLASSES && !v.empty()) {   // « Lunettes (chromées) » -> « Chromées », « Lunettes de soleil » -> « De soleil »
        const wchar_t *pre = T(L"Lunettes", L"Glasses");
        if (!l.compare(0, wcslen(pre), pre)) {
            std::wstring r = Trim(l.substr(wcslen(pre)));
            if (r.size() > 2 && r.front() == L'(' && r.back() == L')') r = r.substr(1, r.size() - 2);
            if (!r.empty()) { r[0] = towupper(r[0]); l = r; }
        }
    }
    return l;
}

// Pastille « Photo » (image importee), en haut a droite d'une tuile.
static void TilePhotoChip(Graphics &g, RectF r)
{
    RectF c(r.X + r.Width - 40, r.Y + 5, 36, 15);
    Pill(g, c, Color(255, 255, 107, 139), T(L"Photo", L"Photo"), Color(255, 255, 255, 255), 8.5f);
}

static void DrawTile(Graphics &g, RectF r, int f, const std::string &v, bool on, bool hot)
{
    GraphicsPath p; RoundRect(p, r, 12);
    SolidBrush bg(g_dark ? Color(64, 0, 0, 0) : Color(28, 16, 30, 48)); g.FillPath(&bg, &p);
    if (on) { GraphicsPath gp; RectF gr = r; gr.Inflate(2, 2); RoundRect(gp, gr, 14); Pen glow(WithA(kAcc, 0.22f), 4.0f); g.DrawPath(&glow, &gp); }
    bool import = v == "#import", none = v.empty() && (f == PF_HAT || f == PF_GLASSES || f == PF_HAIR);
    Pen edge(on ? kAcc : hot ? WithA(kAcc, 0.6f) : import ? WithA(Color(255, 255, 107, 139), 0.6f) : TH(choiceBorder), on ? 2.0f : 1.5f);
    g.DrawPath(&edge, &p);
    float cx = r.X + r.Width / 2;
    GraphicsPath sp;
    RectF sr;
    switch (f) {   // la forme : haut / pantalon bloc, visage et cheveux rond, chapeau calotte, lunettes barre
    case PF_SHIRT: sr = RectF(cx - 17, r.Y + 10, 34, 40); RoundRect(sp, sr, 8); break;
    case PF_PANTS: sr = RectF(cx - 14, r.Y + 8, 28, 44); RoundRect(sp, sr, 7); break;
    case PF_FACE: case PF_HAIR: sr = RectF(cx - 18, r.Y + 12, 36, 36); sp.AddEllipse(sr); break;
    case PF_HAT: sr = RectF(cx - 20, r.Y + 18, 40, 24); RoundRect(sp, sr, 10); break;
    case PF_GLASSES: sr = RectF(cx - 20, r.Y + 24, 40, 14); RoundRect(sp, sr, 7); break;
    default: break;
    }
    if (f == PF_BODY) {   // silhouette : bonhomme plus ou moins large / haut (cochon : rose)
        float w = 1, h = 1; bool pig = !v.compare(0, 4, "pig-");
        if (v == "1714-32") w = 0.72f; else if (v == "1709-34") w = 0.85f; else if (v == "1712-48") w = 1.3f;
        else if (v == "1697-42") { w = 1.3f; h = 0.8f; } else if (v == "1789-29") { w = 0.8f; h = 0.8f; } else if (pig) { w = 1.25f; h = 0.75f; }
        SolidBrush b(pig ? Color(255, 232, 150, 170) : WithA(kInk, on ? 0.9f : 0.7f));
        float top = r.Y + 12 + (1 - h) * 40;
        g.FillEllipse(&b, cx - 6 * h, top, 12 * h, 12 * h);
        GraphicsPath bp; RoundRect(bp, RectF(cx - 10 * w, top + 14 * h, 20 * w, 30 * h), 8 * min(w, h)); g.FillPath(&b, &bp);
    } else if (import) {
        Pen dash(WithA(Color(255, 255, 107, 139), 0.9f), 1.2f); dash.SetDashStyle(DashStyleDash);
        g.DrawPath(&dash, &sp);
        Icon(g, IC_IMAGE, cx, sr.Y + sr.Height / 2, 16, WithA(kInk, 0.8f), 1.8f);
        TilePhotoChip(g, r);
    } else if (none) {
        Pen dash(WithA(kGrey, 0.7f), 1.2f); dash.SetDashStyle(DashStyleDash);
        g.DrawPath(&dash, &sp);
        Pen bar(WithA(kGrey, 0.7f), 1.6f); g.DrawLine(&bar, sr.X + 6, sr.Y + sr.Height - 4, sr.X + sr.Width - 6, sr.Y + 4);
    } else {
        Bitmap *b = TileBmp(f, v);
        if (b) {
            float iw = (float)b->GetWidth(), ih = (float)b->GetHeight(), k = max(sr.Width / iw, sr.Height / ih);   // (recadre : remplit la forme)
            GraphicsState st = g.Save();
            g.SetClip(&sp, CombineModeIntersect);
            g.SetInterpolationMode(InterpolationModeHighQualityBilinear);
            g.DrawImage(b, RectF(sr.X + (sr.Width - iw * k) / 2, sr.Y + (sr.Height - ih * k) / 2, iw * k, ih * k));
            g.Restore(st);
        } else { SolidBrush fb(WithA(kGrey, 0.35f)); g.FillPath(&fb, &sp); }
        Pen rim(Color(40, 255, 255, 255), 1.0f); g.DrawPath(&rim, &sp);
        if (v == "perso") TilePhotoChip(g, r);
    }
    Text(g, TileLabel(f, v), RectF(r.X + 3, r.Y + r.Height - 26, r.Width - 6, 20), 11, FontStyleBold, import ? kGrey : on ? kInk : WithA(kInk, 0.85f));
}

// Bouton dessine comme UiButton, sans zone (le clic passe par PersoPageHit).
static void PersoBtn(Graphics &g, RectF r, const std::wstring &s, bool hot, int icon = -1)
{
    GraphicsPath p; RoundRect(p, r, 11);
    SolidBrush b(hot ? TH(btn2Hot) : TH(btn2)); g.FillPath(&b, &p);
    Pen pen(TH(fieldBorder), 1.0f); g.DrawPath(&pen, &p);
    Graphics &mg = MeasureG();
    float tw = MeasureW(mg, s, 12.5f, FontStyleBold), iw = icon >= 0 ? 24.0f : 0.0f, x0 = r.X + (r.Width - tw - iw) / 2;
    if (icon >= 0) Icon(g, icon, x0 + 9, r.Y + r.Height / 2, 17, kInk, 2.1f);
    Text(g, s, RectF(x0 + iw, r.Y, tw + 2, r.Height), 12.5f, FontStyleBold, kInk, StringAlignmentNear);
}
// Bande du bas (verre leger) : fond et liseré.
static void PersoStripBg(Graphics &g, RectF s)
{
    GraphicsPath sp; RoundRect(sp, s, 14);
    SolidBrush sb(g_dark ? Color(13, 255, 255, 255) : Color(60, 255, 255, 255)); g.FillPath(&sb, &sp);
    Pen pen(g_dark ? Color(31, 255, 255, 255) : TH(choiceBorder), 1.0f); g.DrawPath(&pen, &sp);
}

// Bande « Voiture » (onglet Tenue) : les teintes de la CORRIS (CarSetColor) et le bouton vers la peinture perso.
static void DrawCarStrip(Graphics &g)
{
    RectF s = PersoStripRect();
    PersoStripBg(g, s);
    Text(g, T(L"VOITURE", L"CAR"), RectF(s.X + 12, s.Y + 10, 70, 18), 10.5f, FontStyleBold, kAcc, StringAlignmentNear);
    int sel = CarSwatchSel();
    std::wstring name;
    if (sel == 0) name = T(L"au hasard", L"random");
    else if (sel > 0) name = T(kSwatches[sel - 1].fr, kSwatches[sel - 1].en);
    else { wchar_t hx[16]; swprintf_s(hx, L"#%06X", g_carColor & 0xFFFFFF); name = hx; }
    RectF pb = PersoPaintRect();
    Text(g, T(L"Ma CORRIS (nouvelle partie) : ", L"My CORRIS (new game): ") + name,
         RectF(s.X + 80, s.Y + 10, pb.X - 12 - (s.X + 80), 18), 12, FontStyleRegular, kGrey, StringAlignmentNear);
    PersoBtn(g, pb, T(L"Peinture perso…", L"Custom paint…"), g_skinRowHot == HT_PAINT, IC_IMAGE);
    for (int i = 0; i < kSwatchN; i++) {
        RectF r = PersoSwatchRect(i);
        bool hot = g_skinRowHot == HT_SWATCH + i;
        if (i == sel) { Pen ring(Color(255, 255, 255, 255), 2.0f); g.DrawEllipse(&ring, r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6); Pen halo(Color(60, 255, 255, 255), 3.0f); g.DrawEllipse(&halo, r.X - 6, r.Y - 6, r.Width + 12, r.Height + 12); }
        else if (hot) { Pen ring(WithA(kInk, 0.6f), 1.6f); g.DrawEllipse(&ring, r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6); }
        if (i == 0) {
            DrawCarWheel(g, r);
            SolidBrush mid(g_dark ? Color(255, 22, 32, 58) : Color(255, 245, 248, 252));
            g.FillEllipse(&mid, r.X + 7, r.Y + 7, r.Width - 14, r.Height - 14);
            Text(g, L"?", RectF(r.X, r.Y + 0.5f, r.Width, r.Height), 10.5f, FontStyleBold, kInk);
        } else { SolidBrush b(CarRgb(kSwatches[i - 1].rgb)); g.FillEllipse(&b, r); }
        Pen edge(Color(70, 255, 255, 255), 1.0f); g.DrawEllipse(&edge, r);
    }
}

// Bande « Mes images » (onglet Visage) : l'image perso choisie (visage / haut / pantalon), Importer, Gabarit, Retirer.
static void DrawImagesStrip(Graphics &g)
{
    RectF s = PersoStripRect();
    g_faceRect = s;
    PersoStripBg(g, s);
    Text(g, T(L"MES IMAGES", L"MY IMAGES"), RectF(s.X + 12, s.Y + 5, 90, 16), 10.5f, FontStyleBold, kAcc, StringAlignmentNear);
    Text(g, T(L"peintes sur le gabarit du jeu, montrées aux autres joueurs", L"painted on the game's template, shown to the other players"), RectF(s.X + 100, s.Y + 5, s.Width - 112, 16), 10.5f, FontStyleRegular, kGrey, StringAlignmentNear);
    int k = PersoImgZone();
    Bitmap *th = PersoThumb(k);
    float y = s.Y + 24;
    RectF ti(s.X + 12, y, 32, 32);
    GraphicsPath tp; RoundRect(tp, ti, 8);
    if (th) {
        float iw = (float)th->GetWidth(), ih = (float)th->GetHeight(), kk = max(ti.Width / iw, ti.Height / ih);
        GraphicsState st = g.Save();
        g.SetClip(&tp, CombineModeIntersect);
        g.SetInterpolationMode(InterpolationModeHighQualityBilinear);
        g.DrawImage(th, RectF(ti.X + (ti.Width - iw * kk) / 2, ti.Y + (ti.Height - ih * kk) / 2, iw * kk, ih * kk));
        g.Restore(st);
        Pen rim(kAcc, 1.2f); g.DrawPath(&rim, &tp);
    } else {
        Pen dash(WithA(kGrey, 0.7f), 1.2f); dash.SetDashStyle(DashStyleDash); g.DrawPath(&dash, &tp);
        Icon(g, IC_IMAGE, ti.X + 16, ti.Y + 16, 14, WithA(kGrey, 0.8f), 1.8f);
    }
    UiSeg(g, RectF(s.X + 54, y, 198, 32), { T(L"Visage", L"Face"), T(L"Haut", L"Top"), T(L"Pantalon", L"Pants") }, k, UI_LOOKPIN, 11.5f);
    UiButton(g, RectF(s.X + 262, y, 108, 32), T(L"Importer…", L"Import…"), UI_FACE_IMPORT, th == NULL, -1, true, 11);
    UiButton(g, RectF(s.X + 378, y, 84, 32), T(L"Gabarit", L"Template"), UI_FACE_TPL, false, -1, true, 11);
    UiButton(g, RectF(s.X + 470, y, 84, 32), T(L"Retirer", L"Remove"), UI_FACE_DEL, false, -1, th != NULL, 11);
}

// Encart « Apparence complète » (pas d'export du mod : seulement les hauts).
static void DrawPersoCallout(Graphics &g, RectF nr)
{
    GraphicsPath np; RoundRect(np, nr, 16);
    SolidBrush nb(WithA(kAcc, 0.12f)); g.FillPath(&nb, &np);
    Pen npen(WithA(kAcc, 0.45f), 1); g.DrawPath(&npen, &np);
    Icon(g, IC_SPARK, nr.X + 28, nr.Y + 30, 20, kAcc, 1.9f);
    Text(g, T(L"Apparence complète", L"Full look"), RectF(nr.X + 52, nr.Y + 14, nr.Width - 70, 24), 14.5f, FontStyleBold, kInk, StringAlignmentNear);
    Para(g, T(L"Corpulence, pantalon, visage, chapeau, lunettes, cheveux : lance une partie (solo suffit) et reste ~30 s en jeu avec cette version, ils apparaîtront ici.",
              L"Build, pants, face, hat, glasses, hair: start a game (solo is fine) and stay ~30 s in game with this version, they will show up here."),
         RectF(nr.X + 52, nr.Y + 40, nr.Width - 70, nr.Height - 48), 12.5f, kGrey);
}

static void DrawSkins(Graphics &g)
{
    SkinsCheck();
    const Opt *ap = SkinOpt();
    if (!ap) return;
    Glass(kSkinCard);
    Glass(kSkinPanel);
    int sel = OptGet(*ap);
    const std::string &skin = ap->svals[sel];
    PersoCheck();
    bool perso = g_perso.state == 1;
    // --- a gauche : l'apercu
    RectF c = kSkinCard, view = kSkinView;
    Title(g, T(L"Aperçu", L"Preview"), RectF(c.X + 16, c.Y + 14, 200, 30), 16, kInk);
    if (perso) {   // vue : en entier / portrait (sans export : la bande d'images n'a qu'une vue)
        RectF sr = PersoViewSegRect();
        GraphicsPath tp; RoundRect(tp, sr, 12);
        SolidBrush tb(TH(field)); g.FillPath(&tb, &tp);
        const wchar_t *vf[2] = { L"Pied", L"Portrait" }, *ve[2] = { L"Full", L"Portrait" };
        for (int i = 0; i < 2; i++) {
            RectF s(sr.X + 3 + i * (sr.Width - 6) / 2, sr.Y + 3, (sr.Width - 6) / 2, sr.Height - 6);
            bool on = g_persoView == i, hot = g_skinRowHot == HT_VIEW + i;
            GraphicsPath p; RoundRect(p, s, 9);
            if (on) { SolidBrush b(kAcc); g.FillPath(&b, &p); } else if (hot) { SolidBrush b(TH(cardSel)); g.FillPath(&b, &p); }
            Text(g, T(vf[i], ve[i]), s, 12, FontStyleBold, on ? kOnAcc : kInk);
        }
    }
    {   // la scene : fond sombre, halo au sol
        GraphicsPath vp; RoundRect(vp, view, 16);
        SolidBrush vb(g_dark ? Color(64, 0, 0, 0) : Color(26, 16, 30, 48)); g.FillPath(&vb, &vp);
        Pen vpen(g_dark ? Color(26, 255, 255, 255) : TH(choiceBorder), 1.0f); g.DrawPath(&vpen, &vp);
        if (!(perso && g_persoView == 1)) {
            GraphicsPath hp; hp.AddEllipse(view.X + 40, view.Y + view.Height - 52, view.Width - 80, 44.0f);
            PathGradientBrush hb(&hp);
            hb.SetCenterColor(WithA(kAcc, g_dark ? 0.32f : 0.26f));
            Color edge(0, 0, 0, 0); int one = 1; hb.SetSurroundColors(&edge, &one);
            g.FillPath(&hb, &hp);
        }
    }
    float cx = view.X + view.Width / 2;
    float k = min((GetTickCount() - g_skinChangeT) / 180.0f, 1.0f);
    k = 1 - (1 - k) * (1 - k);
    Bitmap *strip = perso ? NULL : SkinStrip(skin), *portrait = perso || strip ? NULL : SkinPortrait(skin);
    if (perso) {
        RectF dst(view.X + 4, view.Y + 8, view.Width - 8, view.Height - 24);
        if (Bitmap *pb = PersoImage(dst, g_skinYaw * 6.2831853f / 16, g_persoView == 1)) DrawPixels(g, pb, dst);
    } else if (strip) {
        float fw = strip->GetWidth() / 16.0f, fh = (float)strip->GetHeight();
        int fr = (int)floorf(g_skinYaw + 0.5f) & 15;
        float h = view.Height - 28, w = h * fw / fh;
        RectF dst(cx - w / 2 + (1 - k) * 22 * g_skinChangeDir, view.Y + 8, w, h);
        if (k < 1) {
            ColorMatrix cm = { { { 1, 0, 0, 0, 0 }, { 0, 1, 0, 0, 0 }, { 0, 0, 1, 0, 0 }, { 0, 0, 0, k, 0 }, { 0, 0, 0, 0, 1 } } };
            ImageAttributes ia;
            ia.SetColorMatrix(&cm);
            g.DrawImage(strip, dst, fr * fw, 0, fw, fh, UnitPixel, &ia);
        } else {
            int W = max(4, (int)(dst.Width * g_scale + 0.5f)), H = max(4, (int)(dst.Height * g_scale + 0.5f));
            if (!g_skinFrame || g_skinFrameSrc != strip || g_skinFrameN != fr || g_skinFrameW != W || g_skinFrameH != H) {
                delete g_skinFrame;
                g_skinFrame = new Bitmap(W, H, PixelFormat32bppPARGB);
                Graphics fg(g_skinFrame);
                fg.Clear(Color(0, 0, 0, 0));
                fg.SetInterpolationMode(InterpolationModeHighQualityBicubic);
                fg.SetPixelOffsetMode(PixelOffsetModeHalf);
                fg.DrawImage(strip, RectF(0, 0, (float)W, (float)H), fr * fw, 0, fw, fh, UnitPixel);
                g_skinFrameSrc = strip; g_skinFrameN = fr; g_skinFrameW = W; g_skinFrameH = H;
            }
            DrawPixels(g, g_skinFrame, dst);
        }
    } else if (portrait) g.DrawImage(portrait, RectF(cx - 80, view.Y + 110, 160, 160));
    else {
        DrawSkinGhost(g, cx, view.Y + 80, 1.3f);
        const wchar_t *msg = g_skinState == 1 ? T(L"Pas d'aperçu pour cette tenue.", L"No preview for this outfit.")
                                              : T(L"Les aperçus apparaîssent après une première partie (~30 s en jeu) avec cette version.",
                                                  L"Previews appear after a first game (~30 s in game) with this version.");
        Para(g, msg, RectF(view.X + 24, view.Y + 300, view.Width - 48, 60), 13, kGrey, StringAlignmentNear);
    }
    if (perso || strip) {   // comment tourner
        std::wstring tip = T(L"Glisser : tourner", L"Drag: rotate");
        Pill(g, RectF(view.X + 10, view.Y + 10, PillW(tip, 11) + 4, 26), g_dark ? Color(153, 10, 20, 45) : Color(200, 255, 255, 255), tip, g_dark ? Color(255, 220, 231, 250) : kInk, 11);
    }
    {   // le portrait tel que le salon et la carte le montrent (sa couleur de joueur)
        RectF av(view.X + view.Width - 10 - 72, view.Y + view.Height - 10 - 72, 72, 72);
        SolidBrush sh(Color(90, 0, 0, 0)); g.FillEllipse(&sh, av.X - 2, av.Y, av.Width + 4, av.Height + 4);
        DrawSkinAvatar(g, av, PersoLookString(), CarRgb(kPersoPal[max(0, g_myId) % 16]), PlayerName());
    }
    if (PersoStale())   // export d'une ancienne version (corps des PNJ dans leur pose : la grand-mere assise...)
        Para(g, T(L"Aperçu fait par une ancienne version : lance une partie (solo suffit) et reste ~30 s en jeu, il sera refait.",
                  L"Preview made by an older version: start a game (solo is fine) and stay ~30 s in game, it will be redone."),
             RectF(c.X + 16, c.Y + c.Height - 96, c.Width - 32, 34), 11.5f, kWarn);
    else Para(g, T(L"Le portrait en bas à droite est celui affiché dans les salons et sur la carte.", L"The portrait at the bottom right is the one shown in lobbies and on the map."),
              RectF(c.X + 16, c.Y + c.Height - 96, c.Width - 32, 34), 11.5f, kGrey);
    float by = c.Y + c.Height - 16 - 44;
    UiButton(g, RectF(c.X + 16, by, 110, 44), T(L"Aléatoire", L"Random"), UI_LOOKRAND, false, IC_SHUFFLE);
    UiButton(g, RectF(c.X + 134, by, 110, 44), T(L"Comme avant", L"As before"), UI_LOOKRESET, false);
    UiButton(g, RectF(c.X + 252, by, 122, 44), T(L"Enregistrer", L"Save"), UI_PAGE_OK, true, IC_CHECK);

    // --- a droite : les parties de l'onglet
    RectF pn = kSkinPanel;
    Title(g, g_persoTab ? T(L"Visage", L"Face") : T(L"Tenue", L"Outfit"), RectF(pn.X + 18, pn.Y + 14, 160, 30), 16, kInk);
    Text(g, g_persoTab ? T(L"Visage, chapeau, lunettes, cheveux · tes images", L"Face, hat, glasses, hair · your images") : T(L"Vêtements du jeu + packs offerts par la communauté", L"Game clothes + packs offered by the community"),
         RectF(pn.X + 120, pn.Y + 16, pn.Width - 138, 26), 12, FontStyleRegular, kGrey, StringAlignmentFar);
    std::vector<SecLay> lay;
    float yEnd = PersoLayout(lay);
    for (auto &L : lay) {
        int f = L.sec->f;
        std::string cur = PersoGet(f);
        int at = PersoIndexOf(L.tiles, cur);
        Text(g, T(L.sec->fr, L.sec->en), RectF(pn.X + 18, L.y, 200, 20), 10.5f, FontStyleBold, kAcc, StringAlignmentNear);
        int imp = L.tiles[0] == "#import" ? 1 : 0;   // (la tuile d'import ne compte pas)
        wchar_t cnt[32]; swprintf_s(cnt, L" · %d / %d", at + 1 - imp, L.n - imp);
        Text(g, PersoLabel(f, cur) + cnt, RectF(pn.X + 150, L.y, pn.Width - 168, 20), 11.5f, FontStyleRegular, kGrey, StringAlignmentFar);
        int shown = min(L.per, L.n - L.page * L.per);
        for (int i = 0; i < shown; i++) {
            int idx = L.page * L.per + i;
            DrawTile(g, TileRect(L, i), f, L.tiles[idx], idx == at, g_skinRowHot == HT_TILE + f * 16 + i);
        }
        if (L.n > L.per) {   // « + N » : la page suivante (la derniere ramene au debut)
            RectF r = TileRect(L, shown);
            bool hot = g_skinRowHot == HT_TILE + f * 16 + 15;
            int rest = L.n - (L.page + 1) * L.per;
            GraphicsPath p; RoundRect(p, r, 12);
            if (hot) { SolidBrush hb(TH(cardSel)); g.FillPath(&hb, &p); }
            Pen dash(hot ? WithA(kAcc, 0.7f) : TH(fieldBorder), 1.5f); dash.SetDashStyle(DashStyleDash); g.DrawPath(&dash, &p);
            if (rest > 0) Text(g, L"+ " + std::to_wstring(rest), RectF(r.X, r.Y + 14, r.Width, 30), 15, FontStyleBold, hot ? kAcc : kGrey);
            else Icon(g, IC_UNDO, r.X + r.Width / 2, r.Y + 30, 20, hot ? kAcc : kGrey, 2.0f);
            wchar_t pg[32]; swprintf_s(pg, L"%d / %d", L.page + 1, L.pages);
            Text(g, pg, RectF(r.X, r.Y + r.Height - 26, r.Width, 20), 11, FontStyleBold, kGrey);
        }
    }
    if (!perso) DrawPersoCallout(g, RectF(pn.X + 18, yEnd + 4, pn.Width - 36, 104));
    if (g_persoTab) DrawImagesStrip(g); else DrawCarStrip(g);
}

// Aleatoire : une valeur au hasard pour chaque partie (apparence complete), sinon un haut au hasard.
static void LookRandom()
{
    srand(GetTickCount());
    for (auto &p : g_tilePage) p = -1;   // (les pages suivent les nouveaux choix)
    if (g_perso.state == 1) {
        for (int f = 0; f < PF_COUNT; f++) {
            std::vector<std::string> v = PersoChoices(f);
            if (v.empty()) continue;
            if (f == PF_SHIRT) { const Opt *ap = SkinOpt(); if (ap) OptSet(*ap, rand() % (int)v.size()); }
            else PersoSet(f, v[rand() % v.size()]);
        }
        g_skinChangeT = GetTickCount();
        g_skinAnnounce = true;
    } else SkinSelect(rand() % max(1, SkinCount()), 1);
}
// Comme avant : les valeurs a l'arrivee sur la page (sans la quitter).
static void LookReset()
{
    const Opt *ap = SkinOpt();
    if (ap && g_snapSkin >= 0) OptSet(*ap, g_snapSkin);
    PersoSnapshot(true);
    for (auto &p : g_tilePage) p = -1;
    g_skinAnnounce = true;
    g_skinChangeT = GetTickCount();
}

// Clic dans la page : un onglet, une tuile, une pastille, la vue, ou l'apercu (debut du glisser).
static bool SkinMouseDown(float x, float y)
{
    if (PersoPageMouseDown(x, y)) return true;
    if (kSkinView.Contains(x, y)) {
        g_skinDrag = true;
        g_skinDragX = x;
        SetCapture(g_wnd);
        return true;
    }
    return false;
}
static void SkinDragTo(float x)
{
    g_skinYaw = fmodf(g_skinYaw - (x - g_skinDragX) / 14.0f, 16.0f);   // une vue tous les 14 px
    if (g_skinYaw < 0) g_skinYaw += 16;
    g_skinDragX = x;
}

