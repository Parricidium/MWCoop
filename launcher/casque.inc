// ---- casque perso (JD 07/11, format de [Addons]) : onglet Casque de la page Personnage (g_tab == TAB_SKIN, g_persoTab == 3).
// Le joueur peint la coque sur un gabarit ; le mod vend ce casque au magasin CARDINAL et le montre aux autres joueurs. Il ne
// remplace pas le casque du jeu.
//   Fichier du joueur : %LOCALAPPDATA%\MWCoop\casque.png (ou casque.jpg), un seul des deux. Le mod le ramene a 1024 x 1024,
//   refuse plus de 2 Mo ou plus de 2048 px, prend l'alpha comme opaque, et le relit si sa date change. Le lanceur l'ecrit
//   deja a 1024 x 1024, alpha ignore (les couleurs des pixels transparents gardees, comme le mod les lit), PNG, ou JPEG
//   s'il depasse 2 Mo.
//   Interrupteur : mwcoop.ini [Coop] Casque=perso (autre valeur ou absent : pas de casque perso). Importer met perso,
//   Retirer supprime le fichier et met aucun.
//   Fichiers du mod (paquet du jeu) : MWCoop\casque\ gabarit-casque.png (1024 x 1024, la coque depliee en disque vu du
//   dessus : centre = haut, bord = bas), defaut-casque.png (le casque sans image du joueur), et pour l'apercu, vues k = 1
//   (3/4 avant) et k = 2 (3/4 arriere), 400 x 400 : apercu-<k>-uv.png (R = u x 255, G = v x 255, B = eclairage, A = 255 sur
//   la coque) et apercu-<k>-dessus.png (visiere, joint, interieur, a poser par-dessus). Par pixel : base = A > 0 ?
//   texture(u x (L-1), (1-v) x (H-1)) x B / 255 : transparent ; resultat = dessus « over » base.
static const unsigned kCasqueMaxOctets = 2000000;   // (« plus de 2 Mo » refuse par le mod : marge sous 2 x 10^6)
static std::wstring CasqueModDir() { return g_modRoot + L"MWCoop\\casque\\"; }
static std::wstring CasquePng() { return LocalDir() + L"casque.png"; }
static std::wstring CasqueJpg() { return LocalDir() + L"casque.jpg"; }
static std::wstring CasqueFile() { return FileExists(CasquePng()) ? CasquePng() : FileExists(CasqueJpg()) ? CasqueJpg() : std::wstring(); }
static bool CasqueHas() { return !CasqueFile().empty(); }
static bool CasqueIniPerso()
{
    if (g_gameDir.empty()) return false;
    char v[32] = "";
    GetPrivateProfileStringA("Coop", "Casque", "", v, sizeof(v), ModIniA().c_str());
    return !_stricmp(v, "perso");
}
static bool CasqueOn() { return CasqueIniPerso() && CasqueHas(); }
static void CasqueSetIni(bool on)
{
    if (g_gameDir.empty()) return;
    EnsureModDir();
    WritePrivateProfileStringA("Coop", "Casque", on ? "perso" : "aucun", ModIniA().c_str());
}
static ULONGLONG CasqueFileSize(const std::wstring &f)
{
    WIN32_FILE_ATTRIBUTE_DATA a;
    if (!GetFileAttributesExW(f.c_str(), GetFileExInfoStandard, &a)) return 0;
    return ((ULONGLONG)a.nFileSizeHigh << 32) | a.nFileSizeLow;
}
static std::wstring CasqueStamp(const std::wstring &f)
{
    WIN32_FILE_ATTRIBUTE_DATA a;
    if (!GetFileAttributesExW(f.c_str(), GetFileExInfoStandard, &a)) return f + L"|-;";
    wchar_t s[80]; swprintf_s(s, L"|%08lx%08lx|%lu;", a.ftLastWriteTime.dwHighDateTime, a.ftLastWriteTime.dwLowDateTime, a.nFileSizeLow);
    return f + s;
}
// Pixels ARGB non premultiplies (les couleurs sous un alpha 0 restent : le mod les lit telles quelles).
static bool CasqueRaw(const std::wstring &file, std::vector<uint32_t> &px, int &w, int &h)
{
    std::vector<unsigned char> data;
    if (!ReadAll(file, data) || data.empty()) return false;
    bool ok = false;
    auto grab = [&](Bitmap *b) {
        int W = (int)b->GetWidth(), H = (int)b->GetHeight();
        if (W <= 0 || H <= 0 || W > 16384 || H > 16384) return;
        Rect r(0, 0, W, H); BitmapData bd;
        if (b->LockBits(&r, ImageLockModeRead, PixelFormat32bppARGB, &bd) != Ok) return;
        px.resize((size_t)W * H);
        for (int y = 0; y < H; y++) memcpy(&px[(size_t)y * W], (BYTE *)bd.Scan0 + (size_t)y * bd.Stride, (size_t)W * 4);
        b->UnlockBits(&bd);
        w = W; h = H; ok = true;
    };
    if (IStream *st = SHCreateMemStream(data.data(), (UINT)data.size())) {
        Bitmap *b = Bitmap::FromStream(st);
        if (b && b->GetLastStatus() == Ok) grab(b);
        delete b;
        st->Release();
    }
    if (!ok) if (Bitmap *b = LoadAnyImage(file, NULL)) { grab(b); delete b; }   // (WebP, HEIC... par WIC : alpha deja applique)
    return ok;
}
static Bitmap *CasqueBitmap(const std::vector<uint32_t> &px, int w, int h, bool opaque)
{
    Bitmap *b = new Bitmap(w, h, PixelFormat32bppARGB);
    Rect r(0, 0, w, h); BitmapData bd;
    if (b->GetLastStatus() != Ok || b->LockBits(&r, ImageLockModeWrite, PixelFormat32bppARGB, &bd) != Ok) { delete b; return NULL; }
    for (int y = 0; y < h; y++) {
        uint32_t *d = (uint32_t *)((BYTE *)bd.Scan0 + (size_t)y * bd.Stride);
        const uint32_t *s = &px[(size_t)y * w];
        for (int x = 0; x < w; x++) d[x] = opaque ? s[x] | 0xFF000000u : s[x];
    }
    b->UnlockBits(&bd);
    return b;
}

static Bitmap *g_casqueView[2], *g_casqueThumb;
static std::wstring g_casqueViewKey[2], g_casqueThumbKey;
static void CasqueForget() { g_casqueViewKey[0].clear(); g_casqueViewKey[1].clear(); g_casqueThumbKey.clear(); }
// Image montree : celle du joueur si elle est en service, sinon le casque par defaut du mod.
static std::wstring CasqueTexture() { return CasqueOn() ? CasqueFile() : CasqueModDir() + L"defaut-casque.png"; }

// Ecrit casque.png (ou casque.jpg au-dela de 2 Mo) a 1024 x 1024 depuis une image du joueur ; met Casque=perso.
static bool CasqueImportFrom(const std::wstring &file, std::wstring &why)
{
    std::vector<uint32_t> px; int w = 0, h = 0;
    if (!CasqueRaw(file, px, w, h)) { why = T(L"fichier illisible", L"file can't be read"); return false; }
    Bitmap *src = CasqueBitmap(px, w, h, true);
    if (!src) { why = T(L"image trop grande", L"image too large"); return false; }
    const int N = 1024;
    Bitmap dst(N, N, PixelFormat24bppRGB);
    {
        Graphics dg(&dst);
        dg.SetInterpolationMode(InterpolationModeHighQualityBicubic);
        dg.SetPixelOffsetMode(PixelOffsetModeHalf);
        ImageAttributes ia; ia.SetWrapMode(WrapModeTileFlipXY);   // (bords nets, sans halo)
        dg.DrawImage(src, RectF(0, 0, (REAL)N, (REAL)N), 0, 0, (REAL)w, (REAL)h, UnitPixel, &ia);
    }
    delete src;
    CreateDirectoryW(LocalDir().c_str(), NULL);
    std::wstring tmp = LocalDir() + L"casque.tmp", out = CasquePng();
    CLSID png, jpg;
    bool ok = EncoderClsid(L"image/png", &png) && dst.Save(tmp.c_str(), &png, NULL) == Ok;
    if (ok && CasqueFileSize(tmp) > kCasqueMaxOctets) {   // (photo ou bruit : PNG trop lourd, JPEG comme la livree des addons)
        EncoderParameters ep; ULONG q = 90;
        ep.Count = 1; ep.Parameter[0].Guid = EncoderQuality; ep.Parameter[0].Type = EncoderParameterValueTypeLong; ep.Parameter[0].NumberOfValues = 1; ep.Parameter[0].Value = &q;
        ok = EncoderClsid(L"image/jpeg", &jpg) && dst.Save(tmp.c_str(), &jpg, &ep) == Ok && CasqueFileSize(tmp) <= kCasqueMaxOctets;
        out = CasqueJpg();
    }
    if (ok) {
        DeleteFileW(CasquePng().c_str()); DeleteFileW(CasqueJpg().c_str());   // (un seul fichier : jamais un ancien PNG a cote d'un JPEG)
        ok = MoveFileExW(tmp.c_str(), out.c_str(), MOVEFILE_REPLACE_EXISTING) != 0;
    }
    DeleteFileW(tmp.c_str());
    if (!ok) { why = T(L"enregistrement impossible", L"can't save it"); return false; }
    CasqueSetIni(true);
    CasqueForget();
    LaunchLog("casque perso importe : %dx%d -> %ls (%llu octets)", w, h, out.c_str() + out.find_last_of(L'\\') + 1, CasqueFileSize(out));
    return true;
}
static void CasqueImport()
{
    wchar_t file[MAX_PATH] = L"";
    OPENFILENAMEW of = { sizeof(of) };
    of.hwndOwner = g_wnd;
    of.lpstrFilter = L"Images\0*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic;*.heif;*.avif;*.jfif\0*.*\0*.*\0";
    of.lpstrFile = file;
    of.nMaxFile = MAX_PATH;
    of.lpstrTitle = T(L"Choisir le casque (peint sur le gabarit du casque)", L"Choose the helmet (painted on the helmet template)");
    of.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_HIDEREADONLY;
    if (!GetOpenFileNameW(&of)) return;
    std::wstring why;
    if (CasqueImportFrom(file, why)) { SetStatus(K_OK, T(L"Casque importé : achète-le en jeu au magasin CARDINAL", L"Helmet imported: buy it in game at the CARDINAL shop")); return; }
    LaunchLog("casque perso : echec (%ls)", why.c_str());
    std::wstring msg = std::wstring(T(L"Cette image n'a pas pu être lue : ", L"This image could not be read: ")) + why + L".";
    MessageBoxW(g_wnd, msg.c_str(), L"MWCoop", MB_ICONWARNING | MB_OK);
}
// Gabarit : copie de MWCoop\casque\gabarit-casque.png la ou le joueur veut, montree dans l'Explorateur.
static bool CasqueTemplateTo(const std::wstring &dest) { return CopyFileW((CasqueModDir() + L"gabarit-casque.png").c_str(), dest.c_str(), FALSE) != 0; }
static void CasqueTemplate()
{
    if (!FileExists(CasqueModDir() + L"gabarit-casque.png")) {
        MessageBoxW(g_wnd, T(L"Le gabarit du casque est introuvable (MWCoop\\casque\\gabarit-casque.png) : mets le mod à jour.",
                             L"The helmet template was not found (MWCoop\\casque\\gabarit-casque.png): update the mod."), L"MWCoop", MB_ICONWARNING | MB_OK);
        return;
    }
    wchar_t file[MAX_PATH] = L"gabarit-casque.png";
    OPENFILENAMEW of = { sizeof(of) };
    of.hwndOwner = g_wnd;
    of.lpstrFilter = L"PNG\0*.png\0";
    of.lpstrFile = file;
    of.nMaxFile = MAX_PATH;
    of.lpstrDefExt = L"png";
    of.lpstrTitle = T(L"Enregistrer le gabarit du casque", L"Save the helmet template");
    of.Flags = OFN_OVERWRITEPROMPT | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_HIDEREADONLY;
    if (!GetSaveFileNameW(&of)) return;
    if (!CasqueTemplateTo(file)) { SetStatus(K_ERR, T(L"Gabarit du casque : copie impossible (erreur %lu)", L"Helmet template: can't copy it (error %lu)"), GetLastError()); return; }
    std::wstring a = L"/select,\"" + std::wstring(file) + L"\"";
    ShellExecuteW(g_wnd, L"open", L"explorer.exe", a.c_str(), NULL, SW_SHOWNORMAL);
}
static void CasqueRemove()
{
    DeleteFileW(CasquePng().c_str());
    DeleteFileW(CasqueJpg().c_str());
    CasqueSetIni(false);
    CasqueForget();
    LaunchLog("casque perso retire");
}

// Vue k (1 : 3/4 avant, 2 : 3/4 arriere) : texture posee sur la coque par apercu-<k>-uv.png, puis apercu-<k>-dessus.png.
static Bitmap *CasqueCompose(const std::wstring &tex, const std::wstring &uv, const std::wstring &top)
{
    std::vector<uint32_t> tp, up; int L = 0, H = 0, W = 0, Hh = 0;
    if (!CasqueRaw(tex, tp, L, H) || !CasqueRaw(uv, up, W, Hh)) return NULL;
    std::vector<uint32_t> out((size_t)W * Hh, 0);
    for (size_t i = 0; i < out.size(); i++) {
        uint32_t p = up[i];
        if (!(p >> 24)) continue;
        float u = ((p >> 16) & 255) / 255.0f, v = ((p >> 8) & 255) / 255.0f;
        unsigned b = p & 255;
        int x = min(L - 1, max(0, (int)(u * (L - 1) + 0.5f))), y = min(H - 1, max(0, (int)((1 - v) * (H - 1) + 0.5f)));
        uint32_t t = tp[(size_t)y * L + x];   // (alpha de la texture ignore, comme le mod)
        unsigned r = ((t >> 16) & 255) * b / 255, gg = ((t >> 8) & 255) * b / 255, bb = (t & 255) * b / 255;
        out[i] = 0xFF000000u | (r << 16) | (gg << 8) | bb;
    }
    Bitmap *res = CasqueBitmap(out, W, Hh, false);
    if (res && FileExists(top)) {
        std::vector<uint32_t> dp; int dw = 0, dh = 0;
        if (CasqueRaw(top, dp, dw, dh)) if (Bitmap *d = CasqueBitmap(dp, dw, dh, false)) {
            Graphics g(res);
            g.SetCompositingMode(CompositingModeSourceOver);
            g.DrawImage(d, RectF(0, 0, (REAL)W, (REAL)Hh));
            delete d;
        }
    }
    return res;
}
static Bitmap *CasqueView(int k)
{
    int i = k - 1;
    std::wstring tex = CasqueTexture(), base = CasqueModDir() + L"apercu-" + std::to_wstring(k);
    std::wstring uv = base + L"-uv.png", top = base + L"-dessus.png";
    std::wstring key = CasqueStamp(tex) + CasqueStamp(uv) + CasqueStamp(top);
    if (key == g_casqueViewKey[i]) return g_casqueView[i];
    g_casqueViewKey[i] = key;
    delete g_casqueView[i];
    g_casqueView[i] = CasqueCompose(tex, uv, top);
    return g_casqueView[i];
}
static Bitmap *CasqueThumb()
{
    std::wstring tex = CasqueTexture(), key = CasqueStamp(tex);
    if (key == g_casqueThumbKey) return g_casqueThumb;
    g_casqueThumbKey = key;
    delete g_casqueThumb; g_casqueThumb = NULL;
    std::vector<uint32_t> px; int w = 0, h = 0;
    if (CasqueRaw(tex, px, w, h)) if (Bitmap *b = CasqueBitmap(px, w, h, true)) {
        g_casqueThumb = new Bitmap(256, 256, PixelFormat32bppPARGB);
        Graphics tg(g_casqueThumb);
        tg.SetInterpolationMode(InterpolationModeHighQualityBicubic);
        tg.SetPixelOffsetMode(PixelOffsetModeHalf);
        tg.DrawImage(b, RectF(0, 0, 256, 256));
        delete b;
    }
    return g_casqueThumb;
}

// La page : les deux vues a gauche (carte de l'apercu), l'image, les boutons et les etapes a droite.
static void DrawCasque(Graphics &g, RectF c, RectF pn)
{
    Title(g, T(L"Aperçu", L"Preview"), RectF(c.X + 16, c.Y + 16, 200, 30), 16, kInk);
    float s = min(c.Width - 32, (c.Height - 56 - 16 - 12) / 2);
    const wchar_t *lab[2] = { T(L"3/4 avant", L"3/4 front"), T(L"3/4 arrière", L"3/4 back") };
    bool any = false;
    for (int k = 1; k <= 2; k++) {
        RectF v(c.X + (c.Width - s) / 2, c.Y + 56 + (k - 1) * (s + 12), s, s);
        GraphicsPath vp; RoundRect(vp, v, 16);
        SolidBrush vb(g_dark ? Color(64, 0, 0, 0) : Color(26, 16, 30, 48)); g.FillPath(&vb, &vp);
        Pen vpen(g_dark ? Color(26, 255, 255, 255) : TH(choiceBorder), 1.0f); g.DrawPath(&vpen, &vp);
        if (Bitmap *b = CasqueView(k)) {
            any = true;
            InterpolationMode im = g.GetInterpolationMode();
            g.SetInterpolationMode(InterpolationModeHighQualityBicubic);
            g.DrawImage(b, RectF(v.X + 4, v.Y + 4, v.Width - 8, v.Height - 8));
            g.SetInterpolationMode(im);
        }
        Pill(g, RectF(v.X + 10, v.Y + 10, PillW(lab[k - 1], 11.5f) + 4, 26), g_dark ? Color(153, 10, 20, 45) : Color(200, 255, 255, 255), lab[k - 1], g_dark ? Color(255, 220, 231, 250) : kInk, 11.5f);
    }
    if (!any) Para(g, T(L"Pas encore d'aperçu : il vient avec la mise à jour du mod (MWCoop\\casque).", L"No preview yet: it comes with the mod update (MWCoop\\casque)."),
                   RectF(c.X + 40, c.Y + 56 + s / 2 - 20, c.Width - 80, 60), 13, kGrey);

    Title(g, T(L"Casque", L"Helmet"), RectF(pn.X + 18, pn.Y + 16, 200, 30), 16, kInk);
    Para(g, T(L"Un casque à ta peinture, en plus du casque du jeu (il ne le remplace pas). Il se vend au magasin CARDINAL, et les autres joueurs le voient.",
              L"A helmet with your own paint, on top of the game's helmet (it does not replace it). It is sold at the CARDINAL shop, and the other players see it."),
         RectF(pn.X + 18, pn.Y + 52, pn.Width - 36, 40), 12.5f, kGrey);
    bool has = CasqueHas(), on = CasqueOn();
    RectF th(pn.X + 18, pn.Y + 104, 256, 256);
    GraphicsPath tp; RoundRect(tp, th, 14);
    SolidBrush tb(TH(field)); g.FillPath(&tb, &tp);
    if (Bitmap *b = CasqueThumb()) { g.SetClip(&tp); g.DrawImage(b, th); g.ResetClip(); }
    else Icon(g, IC_IMAGE, th.X + th.Width / 2, th.Y + th.Height / 2, 48, WithA(kGrey, 0.6f), 1.4f);
    Pen tpen(on ? kAcc : TH(fieldBorder), on ? 2.0f : 1.0f); g.DrawPath(&tpen, &tp);
    Text(g, on ? T(L"Ton casque", L"Your helmet") : T(L"Casque par défaut", L"Default helmet"),
         RectF(th.X, th.Y + th.Height + 6, th.Width, 22), 12.5f, FontStyleBold, on ? kInk : kGrey, StringAlignmentCenter);
    float bx = th.X + th.Width + 24, bw = pn.X + pn.Width - 18 - bx, by = th.Y;
    UiButton(g, RectF(bx, by, bw, 44), T(L"Gabarit", L"Template"), UI_CASQUE_TPL, false, IC_FOLDER); by += 56;
    UiButton(g, RectF(bx, by, bw, 44), T(L"Mon image…", L"My image…"), UI_CASQUE_IMP, true, IC_IMAGE); by += 56;
    if (has) UiButton(g, RectF(bx, by, bw, 44), T(L"Retirer", L"Remove"), UI_CASQUE_DEL, false, IC_TRASH);
    float sy = th.Y + th.Height + 44;
    const wchar_t *steps[] = {
        T(L"1. Gabarit : la coque dépliée en un disque (1024 x 1024). Le centre est le haut du casque, l'avant est en bas de l'image ; gauche et droite sont celles du pilote. Écris le haut des lettres vers le centre.",
          L"1. Template: the shell unfolded into a disc (1024 x 1024). The centre is the top of the helmet, the front is at the bottom of the image; left and right are the driver's. Write with the top of the letters towards the centre."),
        T(L"2. Peins par-dessus, puis « Mon image… ». Une image carrée de préférence : elle est ramenée à 1024 x 1024.",
          L"2. Paint over it, then \"My image…\". A square image is best: it is brought to 1024 x 1024."),
        T(L"3. En jeu, achète-le au magasin CARDINAL : les autres joueurs voient ta peinture.",
          L"3. In game, buy it at the CARDINAL shop: the other players see your paint."),
    };
    for (const wchar_t *st : steps) { Para(g, st, RectF(pn.X + 18, sy, pn.Width - 36, 58), 13, kInk); sy += 62; }   // (3 lignes : le russe de l'etape 1 en prend 3)
    Para(g, T(L"Retirer efface ton image : le casque redevient celui par défaut.", L"Remove deletes your image: the helmet goes back to the default one."),
         RectF(pn.X + 18, sy + 4, pn.Width - 36, 22), 12, kGrey);
}

// /testcasque <journal> : import (taille, alpha ignore, JPEG au-dela de 2 Mo), Retirer, interrupteur, apercu compose ;
// dans un dossier de jeu jetable, LOCALAPPDATA a cote, seulement.
static int CasqueSelfTest(const wchar_t *log)
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
    CLSID png;
    if (!EncoderClsid(L"image/png", &png)) { say("encodeur PNG", false); return 1; }
    auto save = [&](const std::vector<uint32_t> &px, int w, int h, const std::wstring &f) {
        Bitmap *b = CasqueBitmap(px, w, h, false);
        bool ok = b && b->Save(f.c_str(), &png, NULL) == Ok;
        delete b;
        return ok;
    };
    auto ini = [&]() { char v[32] = ""; GetPrivateProfileStringA("Coop", "Casque", "", v, sizeof(v), ModIniA().c_str()); return std::string(v); };
    auto at = [&](const std::wstring &f, int x, int y, uint32_t &p, int &w, int &h) { std::vector<uint32_t> px; if (!CasqueRaw(f, px, w, h)) return false; p = px[(size_t)y * w + x]; return true; };
    auto near3 = [&](uint32_t p, uint32_t q) { for (int s = 0; s < 24; s += 8) if (abs((int)((p >> s) & 255) - (int)((q >> s) & 255)) > 3) return false; return true; };
    std::wstring tmpDir = parent + L"casque-essai\\";
    CreateDirectoryW(tmpDir.c_str(), NULL);
    CasqueRemove();
    say("depart : pas de casque, Casque=aucun", !CasqueHas() && ini() == "aucun" && !CasqueOn());

    // 1. 300 x 200 : moitie gauche transparente (vert sous alpha 0), moitie droite rouge opaque
    std::vector<uint32_t> a(300 * 200);
    for (int y = 0; y < 200; y++) for (int x = 0; x < 300; x++) a[y * 300 + x] = x < 150 ? 0x000AC81Eu : 0xFFD01010u;
    std::wstring fa = tmpDir + L"a.png";
    say("image d'essai ecrite", save(a, 300, 200, fa));
    std::wstring why;
    say("import : reussi", CasqueImportFrom(fa, why));
    uint32_t p = 0; int w = 0, h = 0;
    say("import : casque.png seul", FileExists(CasquePng()) && !FileExists(CasqueJpg()));
    say("import : 1024 x 1024", at(CasquePng(), 256, 512, p, w, h) && w == 1024 && h == 1024);
    say("import : couleur sous l'alpha gardee (vert)", near3(p, 0x0AC81E));
    say("import : moitie opaque (rouge)", at(CasquePng(), 768, 512, p, w, h) && near3(p, 0xD01010));
    say("import : Casque=perso, en service", ini() == "perso" && CasqueOn());
    say("import : au plus 2 Mo", CasqueFileSize(CasquePng()) <= kCasqueMaxOctets);

    // 2. bruit 1024 x 1024 : PNG trop lourd -> casque.jpg, plus de casque.png
    std::vector<uint32_t> n(1024 * 1024);
    uint32_t r = 12345;
    for (auto &q : n) { r = r * 1103515245u + 12345u; uint32_t s = r; r = r * 1103515245u + 12345u; q = 0xFF000000u | ((s >> 8) & 0xFFFF00u) | (r >> 24); }
    std::wstring fn = tmpDir + L"bruit.png";
    say("bruit ecrit", save(n, 1024, 1024, fn));
    say("bruit : import reussi", CasqueImportFrom(fn, why));
    say("bruit : casque.jpg seul", FileExists(CasqueJpg()) && !FileExists(CasquePng()));
    say("bruit : au plus 2 Mo", CasqueFileSize(CasqueJpg()) > 0 && CasqueFileSize(CasqueJpg()) <= kCasqueMaxOctets);
    say("bruit : en service", CasqueOn() && CasqueFile() == CasqueJpg());

    // 3. une image illisible ne touche a rien
    std::wstring fz = tmpDir + L"faux.png";
    if (FILE *o = _wfopen(fz.c_str(), L"wb")) { fputs("pas une image", o); fclose(o); }
    say("illisible : refuse", !CasqueImportFrom(fz, why));
    say("illisible : l'ancien reste", FileExists(CasqueJpg()) && CasqueOn());

    // 4. Retirer
    CasqueRemove();
    say("retirer : plus de fichier, Casque=aucun", !CasqueHas() && ini() == "aucun" && !CasqueOn());
    CasqueImportFrom(fa, why);
    WritePrivateProfileStringA("Coop", "Casque", "autre", ModIniA().c_str());
    say("autre valeur : pas en service", CasqueHas() && !CasqueOn());
    CasqueSetIni(true);

    // 5. apercu : uv 4 x 2 et dessus, sur une texture 4 x 4 dont chaque pixel a sa couleur
    std::wstring md = CasqueModDir();
    SHCreateDirectoryExW(NULL, md.c_str(), NULL);
    std::vector<uint32_t> t(16);
    for (int i = 0; i < 16; i++) t[i] = 0x00000000u | ((uint32_t)(i * 16) << 16) | ((uint32_t)(255 - i * 16) << 8) | 200;   // (alpha 0 : ignore)
    std::wstring ft = tmpDir + L"tex.png";
    save(t, 4, 4, ft);
    CasqueImportFrom(ft, why);   // (casque.png : 1024 x 1024, par blocs de 256)
    std::vector<uint32_t> uv = { 0xFF0000FFu, 0xFFFF00FFu, 0xFF00FF80u, 0xFFFFFF00u, 0x00FFFFFFu, 0xFF808080u, 0xFF0000FFu, 0xFF0000FFu };
    std::vector<uint32_t> top = { 0, 0, 0, 0, 0, 0, 0xFF2040F0u, 0x80FFFFFFu };
    say("apercu : fichiers d'essai", save(uv, 4, 2, md + L"apercu-1-uv.png") && save(top, 4, 2, md + L"apercu-1-dessus.png"));
    CasqueForget();
    std::vector<uint32_t> tex; int L = 0, H = 0;
    CasqueRaw(CasquePng(), tex, L, H);
    auto texAt = [&](float u, float v) { int x = (int)(u * (L - 1) + 0.5f), y = (int)((1 - v) * (H - 1) + 0.5f); return tex[(size_t)y * L + x]; };
    auto lit = [&](uint32_t c, unsigned b) { return 0xFF000000u | ((((c >> 16) & 255) * b / 255) << 16) | ((((c >> 8) & 255) * b / 255) << 8) | ((c & 255) * b / 255); };
    Bitmap *v1 = CasqueView(1);
    say("apercu : vue composee 4 x 2", v1 && v1->GetWidth() == 4 && v1->GetHeight() == 2);
    if (v1) {
        std::vector<uint32_t> o(8);
        Rect rr(0, 0, 4, 2); BitmapData bd;
        if (v1->LockBits(&rr, ImageLockModeRead, PixelFormat32bppARGB, &bd) == Ok) {
            for (int y = 0; y < 2; y++) memcpy(&o[y * 4], (BYTE *)bd.Scan0 + y * bd.Stride, 16);
            v1->UnlockBits(&bd);
        }
        say("apercu : u 0 v 0, plein eclairage = bas gauche de la texture", near3(o[0], lit(texAt(0, 0), 255)) && (o[0] >> 24) == 255);
        say("apercu : u 1 v 0 = bas droite", near3(o[1], lit(texAt(1, 0), 255)));
        say("apercu : v 1, eclairage 128 = haut, assombri", near3(o[2], lit(texAt(0, 1), 128)));
        say("apercu : eclairage 0 = noir", near3(o[3], 0xFF000000u) && (o[3] >> 24) == 255);
        say("apercu : A 0 = transparent", (o[4] >> 24) == 0);
        say("apercu : milieu", near3(o[5], lit(texAt(128 / 255.0f, 128 / 255.0f), 128)));
        say("apercu : dessus opaque par-dessus", near3(o[6], 0x2040F0));
        uint32_t base = lit(texAt(0, 0), 255);
        uint32_t mix = 0;
        for (int s = 0; s < 24; s += 8) mix |= (uint32_t)((255 * 128 + ((base >> s) & 255) * 127) / 255) << s;
        say("apercu : dessus a moitie transparent (over)", near3(o[7], mix));
    }
    say("apercu : meme image tant que rien ne change", CasqueView(1) == v1);
    CasqueRemove();
    say("apercu : sans casque, defaut-casque.png absent : pas de vue", CasqueView(1) == NULL);

    DeleteFileW(fa.c_str()); DeleteFileW(fn.c_str()); DeleteFileW(fz.c_str()); DeleteFileW(ft.c_str());
    DeleteFileW((md + L"apercu-1-uv.png").c_str()); DeleteFileW((md + L"apercu-1-dessus.png").c_str());
    RemoveDirectoryW(md.c_str()); RemoveDirectoryW(tmpDir.c_str());
    FILE *o = _wfopen(log, L"ab");
    if (o) { fprintf(o, "%s\r\n", bad ? "ECHEC" : "TOUT OK"); fclose(o); }
    return bad ? 1 : 0;
}
