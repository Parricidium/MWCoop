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
static const RectF kSkinCard(264, 88, 440, 656), kSkinPanel(720, 88, 544, 656);
static const RectF kSkinView(284, 112, 400, 520);
static const float kSkinCell = 54, kSkinGap = 8;
static const int kSkinCols = 8;
static int g_lookZone;                                       // zone mise en avant (0 tete, 1 torse, 2 jambes, 3 silhouette)
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
static RectF SkinCellRect(int i) { return RectF(kSkinPanel.X + 24 + (i % kSkinCols) * (kSkinCell + kSkinGap), kSkinPanel.Y + 62 + (i / kSkinCols) * (kSkinCell + kSkinGap), kSkinCell, kSkinCell); }
static RectF SkinArrowRect(int side) { return RectF(side < 0 ? kSkinCard.X + 20 : kSkinCard.X + kSkinCard.Width - 64, kSkinCard.Y + kSkinCard.Height - 106, 44, 44); }
static int SkinCellAt(float x, float y)
{
    for (int i = 0; i < SkinCount(); i++) { RectF r = SkinCellRect(i); r.Inflate(2, 2); if (r.Contains(x, y)) return i; }
    return -1;
}
static int SkinArrowAt(float x, float y)
{
    for (int s = -1; s <= 1; s += 2) { RectF r = SkinArrowRect(s); r.Inflate(4, 4); if (r.Contains(x, y)) return s; }
    return 0;
}

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

// Repere d'une zone du corps sur l'apercu : point, trait, pastille (cliquable : met la zone en avant a droite).
static RectF LookPinRect(int z)
{
    static const float fy[4] = { 0.15f, 0.38f, 0.68f, 0 };
    if (z == 3) return RectF(kSkinCard.X + 20, kSkinCard.Y + 20, 120, 36);
    return RectF(kSkinCard.X + kSkinCard.Width - 128, kSkinView.Y + kSkinView.Height * fy[z] - 18, 108, 36);
}

// Page TENUE : apercu a gauche (3D, ou la vue de la tenue), parties a droite (ou la galerie des hauts).
// ---- images importees (onglet TENUE) : %LOCALAPPDATA%\MWCoop\visage.jpg, haut.jpg, pantalon.jpg, a la taille de la texture du
// jeu (gabarit) ; choisies dans la liste de leur partie ("perso", perso.inc), montrees aux autres par le mod (CustomFace).
// Gabarits : ecrits par le mod au 1er passage en jeu de chaque version (gabarit-visage.png, gabarit-haut.png, gabarit-pantalon.png).
// La rangee sous l'apercu suit la zone choisie (tete : visage, torse : haut, jambes : pantalon).
static bool EncoderClsid(const wchar_t *mime, CLSID *out);
static Bitmap *g_persoThumb[3];
static bool g_persoThumbLooked[3];
static RectF g_faceRect;   // bloc dessine (bulle du guide)
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

// Rangee de l'image importee de la zone choisie (entre les fleches de rotation de l'apercu) : Mon visage / haut /
// pantalon (importer), Gabarit, Retirer ; vignette de l'image en bas a gauche de l'apercu.
static void DrawFaceRow(Graphics &g, RectF r, RectF view)
{
    g_faceRect = r;
    int k = PersoImgZone();
    Bitmap *th = PersoThumb(k);
    static const wchar_t *impFr[3] = { L"Mon visage…", L"Mon haut…", L"Mon pantalon…" }, *impEn[3] = { L"My face…", L"My top…", L"My pants…" };
    float gap = 6, bw = (r.Width - 2 * gap) / 3;
    UiButton(g, RectF(r.X, r.Y + 4, bw + 16, 36), T(impFr[k], impEn[k]), UI_FACE_IMPORT, th == NULL);
    UiButton(g, RectF(r.X + bw + 16 + gap, r.Y + 4, bw - 8, 36), T(L"Gabarit", L"Template"), UI_FACE_TPL, false);
    UiButton(g, RectF(r.X + 2 * bw + 8 + 2 * gap, r.Y + 4, bw - 8, 36), T(L"Retirer", L"Remove"), UI_FACE_DEL, false, -1, th != NULL);
    if (th) {
        static const wchar_t *capFr[3] = { L"visage importé", L"haut importé", L"pantalon importé" }, *capEn[3] = { L"imported face", L"imported top", L"imported pants" };
        float tw = 96, thh = k == PI_FACE ? 48 : 96 * th->GetHeight() / (float)max(1u, th->GetWidth());
        thh = min(thh, 96.0f);
        RectF ti(view.X + 14, view.Y + view.Height - 14 - thh, tw, thh);
        GraphicsPath p; RoundRect(p, RectF(ti.X - 4, ti.Y - 4, ti.Width + 8, ti.Height + 22), 8);
        SolidBrush b(TH(card)); g.FillPath(&b, &p);
        DrawPixels(g, th, ti);
        Text(g, T(capFr[k], capEn[k]), RectF(ti.X - 4, ti.Y + ti.Height, ti.Width + 8, 18), 10.5f, FontStyleRegular, kGrey);
    }
}

static void DrawSkins(Graphics &g)
{
    SkinsCheck();
    const Opt *ap = SkinOpt();
    if (!ap) return;
    Glass(kSkinCard);
    Glass(kSkinPanel);
    int n = (int)ap->svals.size(), sel = OptGet(*ap);
    const std::string &skin = ap->svals[sel];
    std::vector<std::wstring> lab;
    for (size_t i = 0; i < ap->labEn.size(); i++) lab.push_back(TLab(ap->labFr, ap->labEn, i));
    RectF view = kSkinView;
    PersoCheck();
    bool perso = g_perso.state == 1;
    {   // halo au sol
        GraphicsPath hp; hp.AddEllipse(view.X + 40, view.Y + view.Height - 46, view.Width - 80, 44.0f);
        PathGradientBrush hb(&hp);
        hb.SetCenterColor(WithA(kAcc, g_dark ? 0.32f : 0.26f));
        Color edge(0, 0, 0, 0); int one = 1; hb.SetSurroundColors(&edge, &one);
        g.FillPath(&hb, &hp);
    }
    float cx = view.X + view.Width / 2;
    float k = min((GetTickCount() - g_skinChangeT) / 180.0f, 1.0f);
    k = 1 - (1 - k) * (1 - k);
    Bitmap *strip = perso ? NULL : SkinStrip(skin), *portrait = perso || strip ? NULL : SkinPortrait(skin);
    if (perso) {
        RectF dst(view.X, view.Y, view.Width, view.Height - 16);
        if (Bitmap *pb = PersoImage(dst, g_skinYaw * 6.2831853f / 16)) DrawPixels(g, pb, dst);
    } else if (strip) {
        float fw = strip->GetWidth() / 16.0f, fh = (float)strip->GetHeight();
        int fr = (int)floorf(g_skinYaw + 0.5f) & 15;
        float h = view.Height - 16, w = h * fw / fh;
        RectF dst(cx - w / 2 + (1 - k) * 22 * g_skinChangeDir, view.Y, w, h);
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
    } else if (portrait) g.DrawImage(portrait, RectF(cx - 80, view.Y + 120, 160, 160));
    else {
        DrawSkinGhost(g, cx, view.Y + 90, 1.4f);
        const wchar_t *msg = g_skinState == 1 ? T(L"Pas d'aper\u00E7u pour cette tenue.", L"No preview for this outfit.")
                                              : T(L"Les aper\u00E7us appara\u00EEssent apr\u00E8s une premi\u00E8re partie (~30 s en jeu) avec cette version.",
                                                  L"Previews appear after a first game (~30 s in game) with this version.");
        Para(g, msg, RectF(view.X + 30, view.Y + 330, view.Width - 60, 60), 13, kGrey, StringAlignmentNear);
    }
    // reperes des zones (apparence complete)
    if (perso) {
        static const wchar_t *zf[4] = { L"T\u00EAte", L"Torse", L"Jambes", L"Silhouette" }, *ze[4] = { L"Head", L"Torso", L"Legs", L"Build" };
        static const float dx[3] = { 18, 34, 22 };
        for (int z = 0; z < 4; z++) {
            RectF r = LookPinRect(z);
            bool on = g_lookZone == z, hot = g_uiHot == UI_LOOKPIN + z;
            if (z < 3) {
                float y = r.Y + r.Height / 2, x0 = cx + dx[z];
                Pen ln(Color(110, 255, 255, 255), 1.5f); g.DrawLine(&ln, x0, y, r.X, y);
                SolidBrush halo(WithA(kAcc, 0.3f)); g.FillEllipse(&halo, x0 - 9, y - 9, 18.0f, 18.0f);
                SolidBrush dot(kAcc); g.FillEllipse(&dot, x0 - 5, y - 5, 10.0f, 10.0f);
            }
            GraphicsPath p; RoundRect(p, r, r.Height / 2);
            SolidBrush b(on ? kAcc : hot ? (g_dark ? Color(220, 24, 38, 72) : Color(240, 255, 255, 255)) : (g_dark ? Color(180, 6, 12, 30) : Color(215, 255, 255, 255)));
            g.FillPath(&b, &p);
            if (!on) { Pen pen(TH(fieldBorder), 1); g.DrawPath(&pen, &p); }
            Text(g, T(zf[z], ze[z]), r, 13, FontStyleBold, on ? kOnAcc : kInk);
            HotZone(r, UI_LOOKPIN + z);
        }
    }
    // dessous : tourner, aleatoire, comme avant
    {
        float by = kSkinCard.Y + kSkinCard.Height - 106;
        if (perso || strip) {
            for (int s = 0; s < 2; s++) {
                RectF r(s ? kSkinCard.X + kSkinCard.Width - 64 : kSkinCard.X + 20, by, 44, 44);
                int id = UI_LOOKROT + s;
                GraphicsPath p; RoundRect(p, r, 14);
                SolidBrush b(g_uiHot == id ? TH(btn2Hot) : TH(btn2)); g.FillPath(&b, &p);
                Pen pen(TH(fieldBorder), 1); g.DrawPath(&pen, &p);
                Icon(g, s ? IC_CHEVR : IC_CHEVL, r.X + 22, r.Y + 22, 18, kInk, 2.2f);
                HotZone(r, id);
            }
        }
        // (avant : « Glisse pour tourner » ; le glisser se devine, la rangee sert au visage importe)
        DrawFaceRow(g, RectF(kSkinCard.X + 74, by, kSkinCard.Width - 148, 44), view);
        float bw = (kSkinCard.Width - 50) / 2;
        UiButton(g, RectF(kSkinCard.X + 20, by + 56, bw, 44), T(L"Al\u00E9atoire", L"Random"), UI_LOOKRAND, false, IC_SHUFFLE);
        UiButton(g, RectF(kSkinCard.X + 30 + bw, by + 56, bw, 44), T(L"Comme avant", L"As before"), UI_LOOKRESET, false, IC_UNDO);
    }

    // --- a droite
    RectF pn = kSkinPanel;
    if (perso) DrawPersoRows(g);
    if (perso && PersoStale()) {   // export d'une ancienne version (corps des PNJ dans leur pose : la grand-mere assise...)
        RectF r(pn.X + 24, pn.Y + 532, pn.Width - 48, 0);
        r.Y = PersoRowRect(kPersoRowN - 1).Y + 58; r.Height = pn.Y + pn.Height - 80 - r.Y;
        GraphicsPath p; RoundRect(p, r, 14);
        SolidBrush b(WithA(kWarn, 0.14f)); g.FillPath(&b, &p);
        Pen pen(WithA(kWarn, 0.5f), 1); g.DrawPath(&pen, &p);
        Icon(g, IC_ALERT, r.X + 22, r.Y + r.Height / 2, 18, kWarn, 2.0f);
        Para(g, T(L"Aperçu fait par une ancienne version : lance une partie (solo suffit) et reste ~30 s en jeu, il sera refait (corps debout).",
                  L"Preview made by an older version: start a game (solo is fine) and stay ~30 s in game, it will be redone (standing bodies)."),
             RectF(r.X + 42, r.Y + 6, r.Width - 54, r.Height - 8), 12, kInk, StringAlignmentCenter);
    }
    if (!perso) {   // (pas d'apparence complete : la galerie des hauts)
        Title(g, T(L"Hauts", L"Tops"), RectF(pn.X + 24, pn.Y + 20, 200, 26), 17, kInk);
        std::wstring shown = g_skinHot >= 0 && g_skinHot < n ? lab[g_skinHot] : lab[sel];
        Text(g, shown, RectF(pn.X + 200, pn.Y + 20, pn.Width - 224, 26), 14, FontStyleBold, g_skinHot >= 0 ? kAcc : kInk, StringAlignmentFar);
        for (int i = 0; i < n; i++) {
            RectF r = SkinCellRect(i);
            GraphicsPath cp; RoundRect(cp, r, 12);
            bool on = i == sel, hot = i == g_skinHot;
            SolidBrush cb(on ? WithA(kAcc, 0.16f) : hot ? TH(cardSel) : TH(card)); g.FillPath(&cb, &cp);
            if (Bitmap *th = SkinThumb(ap->svals[i], 0, r.Width, r.Height)) DrawPixels(g, th, r);
            else {
                const std::string &sv = ap->svals[i];
                std::wstring t = !sv.compare(0, 10, "char_shirt") ? Widen(sv.substr(10)) : lab[i].substr(0, 2);
                Text(g, t, r, 14, FontStyleBold, on ? kAcc : kGrey);
            }
            Pen edge(on ? kAcc : hot ? WithA(kAcc, 0.5f) : TH(choiceBorder), on ? 2.0f : 1.0f); g.DrawPath(&edge, &cp);
        }
        RectF nr(pn.X + 24, SkinCellRect(n - 1).Y + kSkinCell + 30, pn.Width - 48, 96);
        GraphicsPath np; RoundRect(np, nr, 16);
        SolidBrush nb(WithA(kAcc, 0.12f)); g.FillPath(&nb, &np);
        Pen npen(WithA(kAcc, 0.45f), 1); g.DrawPath(&npen, &np);
        Icon(g, IC_SPARK, nr.X + 28, nr.Y + 30, 20, kAcc, 1.9f);
        Text(g, T(L"Apparence compl\u00E8te", L"Full look"), RectF(nr.X + 52, nr.Y + 14, nr.Width - 70, 24), 14.5f, FontStyleBold, kInk, StringAlignmentNear);
        Para(g, T(L"Corpulence, pantalon, visage, chapeau, lunettes, cheveux : lance une partie (solo suffit) et reste ~30 s en jeu avec cette version, ils appara\u00EEtront ici.",
                  L"Build, pants, face, hat, glasses, hair: start a game (solo is fine) and stay ~30 s in game with this version, they will show up here."),
             RectF(nr.X + 52, nr.Y + 40, nr.Width - 70, 52), 12.5f, kGrey);
    }
    float bw = (pn.Width - 60) / 2;
    UiButton(g, RectF(pn.X + 24, pn.Y + pn.Height - 68, bw, 48), T(L"ANNULER", L"CANCEL"), UI_PAGE_CANCEL, false);
    UiButton(g, RectF(pn.X + 36 + bw, pn.Y + pn.Height - 68, bw, 48), T(L"VALIDER", L"OK"), UI_PAGE_OK, true);
}

// Aleatoire : une valeur au hasard pour chaque partie (apparence complete), sinon un haut au hasard.
static void LookRandom()
{
    srand(GetTickCount());
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
    g_skinAnnounce = true;
    g_skinChangeT = GetTickCount();
}

// Clic dans la page : une ligne (fleches), un portrait, ou l'apercu (debut du glisser).
static bool SkinMouseDown(float x, float y)
{
    int pr = PersoRowAt(x, y);   // apercu 3D : fleches des lignes
    if (pr >= 0) { PersoStep(pr / 2, pr % 2 ? 1 : -1); g_skinAnnounce = true; return true; }
    if (g_perso.state != 1) {
        int c = SkinCellAt(x, y);
        if (c >= 0) { const Opt *ap = SkinOpt(); SkinSelect(c, ap && c < OptGet(*ap) ? -1 : 1); return true; }
    }
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

