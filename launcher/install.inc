// (inclus par launcher.cpp, apres tuto.inc : INSTALLATION dans %LOCALAPPDATA%\MWCoop, migration et raccourci -- 19/10)
//
// Demande de JD (19/10) : le lanceur gere deux jeux, il ne doit plus vivre dans l'un d'eux. « Qu'il s'installe dans
// AppData comme la plupart des mods, et qu'il cree un raccourci sur le bureau. » Au demarrage d'un lanceur qui ne tourne
// pas depuis %LOCALAPPDATA%\MWCoop (paquet decompresse n'importe ou, ou ancien lanceur pose dans le dossier du jeu) :
//  - question : installer (et, s'il y en a, deplacer les fichiers de MWCoop des dossiers des jeux) ou plus tard ;
//  - oui : MWCoop.exe et ses reglages (mwcoop-lanceur.ini) copies dans %LOCALAPPDATA%\MWCoop ; pour chaque jeu
//    (My Winter Car, My Summer Car) ou MWCoop etait installe : son dossier MWCoop (mod, reglages, profils, sauvegardes
//    coop, journaux, tenues, cache) copie dans %LOCALAPPDATA%\MWCoop\MWC (ou MSC), puis ce que MWCoop avait pose dans le
//    jeu part a la Corbeille (rien n'est efface pour de bon) : dossier MWCoop, version.dll (si c'est le notre), lanceur,
//    mwcoop-lanceur.ini, textes du paquet, steam_appid.txt pose par MWCoop. Jamais : un MSCLoader officiel, Mods, la
//    sauvegarde du jeu. Raccourci « MWCoop » sur le bureau (ceux qui visaient l'ancien lanceur sont redriges), puis le
//    lanceur installe demarre (+apres-installation <ancien exe> : il met l'ancien a la Corbeille une fois ferme) ;
//  - plus tard : rien ne change, le lanceur marche comme avant depuis le dossier du jeu ; la question revient au
//    prochain demarrage. Wine / Proton : pas propose (MWCoop y reste dans le dossier du jeu : DansLeJeu).

static int RemoveJunctionsIn(const std::wstring &dir);   // (desinstallation, plus bas)
static bool ToRecycleBin(const std::vector<std::wstring> &paths);

static std::wstring FindGameOf(int game)
{
    int was = g_game;
    g_game = game;
    std::wstring d = FindGame();
    g_game = was;
    return d;
}

// MWCoop installe dans ce dossier de jeu (avant 1.1) ?
static bool LegacyIn(const std::wstring &gameDir)
{
    return !gameDir.empty() && (FileExists(gameDir + L"MWCoop\\MWCoop.dll") || FileExists(gameDir + L"MWCoop\\mwcoop.ini"));
}

// Copie recursive sans suivre les jonctions (jamais : un lien vers le jeu recopie en entier) ; un fichier deja la n'est pas
// remplace (rien n'ecrase une installation deja faite). Compte des fichiers copies, -1 en cas d'echec.
static int CopyDirSafe(const std::wstring &src, const std::wstring &dst)
{
    SHCreateDirectoryExW(NULL, dst.c_str(), NULL);
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((src + L"\\*").c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return 0;
    int n = 0;
    do {
        std::wstring f = fd.cFileName;
        if (f == L"." || f == L"..") continue;
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) { LaunchLog("installation : lien %s pas recopie", Narrow(src + L"\\" + f, CP_UTF8).c_str()); continue; }
        std::wstring s = src + L"\\" + f, d = dst + L"\\" + f;
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) { int k = CopyDirSafe(s, d); if (k < 0) { FindClose(h); return -1; } n += k; continue; }
        if (FileExists(d)) continue;
        if (!CopyFileW(s.c_str(), d.c_str(), TRUE)) { LaunchLog("installation : %s impossible a copier (erreur %lu)", Narrow(s, CP_UTF8).c_str(), GetLastError()); FindClose(h); return -1; }
        n++;
    } while (FindNextFileW(h, &fd));
    FindClose(h);
    return n;
}

// Texte du paquet de MWCoop (LISEZMOI.txt, README.txt : premiere ligne « MWCoop - ... ») ?
static bool OurReadme(const std::wstring &p)
{
    std::vector<unsigned char> d;
    if (!ReadAll(p, d)) return false;
    std::string t(d.begin(), d.begin() + min<size_t>(d.size(), 64));
    return t.find("MWCoop") != std::string::npos;
}

// Le jeu : ses fichiers MWCoop copies dans home\MWC (ou MSC), puis ce que MWCoop avait pose dans le jeu a la Corbeille.
static bool MigrateGame(int game, const std::wstring &gameDir, const std::wstring &home, std::wstring *report)
{
    std::wstring sub = home + (game == GAME_MSC ? L"MSC\\" : L"MWC\\");
    SHCreateDirectoryExW(NULL, sub.c_str(), NULL);
    RemoveJunctionsIn(gameDir + L"MWCoop\\");   // (jamais de lien dedans d'ordinaire ; jamais suivi en tout cas)
    int n = CopyDirSafe(gameDir + L"MWCoop", sub + L"MWCoop");
    if (n < 0) { *report += std::wstring(L"\n• ") + kGames[game].name + T(L" : copie impossible, rien n'a été retiré", L": copy failed, nothing was removed"); return false; }
    if (FileExists(gameDir + L"version.dll") && !FileExists(sub + L"version.dll")) CopyFileW((gameDir + L"version.dll").c_str(), (sub + L"version.dll").c_str(), TRUE);
    std::vector<std::wstring> bin;
    auto add = [&](const std::wstring &p) { if (GetFileAttributesW(p.c_str()) != INVALID_FILE_ATTRIBUTES) bin.push_back(p); };
    add(gameDir + L"MWCoop");
    std::wstring vd = gameDir + L"version.dll";
    if (FileExists(vd) && VerString(vd, L"InternalName") == L"MWCoopLoader") add(vd);   // (notre chargeur seulement)
    for (const wchar_t *f : { L"version.dll.old", L"version.dll.mwcoop-tmp", L"MWCoop.exe.old", L"mwcoop-lanceur.ini" }) add(gameDir + f);
    for (const wchar_t *f : { L"LISEZMOI.txt", L"README.txt" }) if (OurReadme(gameDir + f)) add(gameDir + f);
    {   // steam_appid.txt : pose par MWCoop (le jeu n'en livre pas) -- seulement s'il porte l'appid de ce jeu
        std::vector<unsigned char> d;
        if (ReadAll(gameDir + L"steam_appid.txt", d) && Trim(Widen(std::string(d.begin(), d.end()))) == kGames[game].appid) add(gameDir + L"steam_appid.txt");
    }
    std::wstring exe = gameDir + L"MWCoop.exe";
    if (FileExists(exe) && _wcsicmp(exe.c_str(), g_self.c_str())) add(exe);   // (le lanceur en cours : par le lanceur installe, une fois ferme)
    bool ok = ToRecycleBin(bin);
    LaunchLog("installation : %s, %d fichier(s) copies dans %s, %d element(s) du jeu a la Corbeille%s", Narrow(kGames[game].name).c_str(), n,
              Narrow(sub, CP_UTF8).c_str(), (int)bin.size(), ok ? "" : " (PAS TOUS)");
    *report += std::wstring(L"\n• ") + kGames[game].name + L" : " + std::to_wstring(n) + T(L" fichier(s) déplacé(s), dossier du jeu nettoyé", L" file(s) moved, game folder cleaned")
             + (ok ? L"" : T(L" (sauf des fichiers ouverts : ferme le jeu et relance)", L" (except files in use: close the game and restart)"));
    return ok;
}

// Raccourci « MWCoop » sur le bureau vers target ; ceux du bureau qui visaient l'ancien lanceur sont redriges.
static void DesktopShortcut(const std::wstring &target, const std::wstring &old)
{
    wchar_t desk[MAX_PATH] = L"";
    if (!GetEnvironmentVariableW(L"MWCOOP_BUREAU", desk, MAX_PATH)   // (essais : un faux bureau)
        && FAILED(SHGetFolderPathW(NULL, CSIDL_DESKTOPDIRECTORY, NULL, SHGFP_TYPE_CURRENT, desk))) return;
    HRESULT co = CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    bool retargeted = false;
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((WithSlash(desk) + L"*.lnk").c_str(), &fd);
    if (h != INVALID_HANDLE_VALUE) {
        do {
            std::wstring lnk = WithSlash(desk) + fd.cFileName;
            IShellLinkW *sl = NULL;
            if (FAILED(CoCreateInstance(CLSID_ShellLink, NULL, CLSCTX_INPROC_SERVER, IID_IShellLinkW, (void **)&sl))) continue;
            IPersistFile *pf = NULL;
            if (SUCCEEDED(sl->QueryInterface(IID_IPersistFile, (void **)&pf)) && SUCCEEDED(pf->Load(lnk.c_str(), STGM_READWRITE))) {
                wchar_t path[MAX_PATH] = L"";
                sl->GetPath(path, MAX_PATH, NULL, SLGP_RAWPATH);
                if (!_wcsicmp(path, target.c_str())) retargeted = true;
                else if (!old.empty() && !_wcsicmp(path, old.c_str())) {
                    sl->SetPath(target.c_str());
                    sl->SetWorkingDirectory(DirOf(target).c_str());
                    sl->SetIconLocation(target.c_str(), 0);
                    if (SUCCEEDED(pf->Save(lnk.c_str(), TRUE))) { retargeted = true; LaunchLog("installation : raccourci %s redirige", Narrow(lnk, CP_UTF8).c_str()); }
                }
            }
            if (pf) pf->Release();
            sl->Release();
        } while (FindNextFileW(h, &fd));
        FindClose(h);
    }
    if (!retargeted) {
        IShellLinkW *sl = NULL;
        if (SUCCEEDED(CoCreateInstance(CLSID_ShellLink, NULL, CLSCTX_INPROC_SERVER, IID_IShellLinkW, (void **)&sl))) {
            sl->SetPath(target.c_str());
            sl->SetWorkingDirectory(DirOf(target).c_str());
            sl->SetIconLocation(target.c_str(), 0);
            sl->SetDescription(L"MWCoop - My Winter Car / My Summer Car co-op");
            IPersistFile *pf = NULL;
            if (SUCCEEDED(sl->QueryInterface(IID_IPersistFile, (void **)&pf))) {
                std::wstring lnk = WithSlash(desk) + L"MWCoop.lnk";
                LaunchLog("installation : raccourci %s %s", Narrow(lnk, CP_UTF8).c_str(), SUCCEEDED(pf->Save(lnk.c_str(), TRUE)) ? "cree" : "IMPOSSIBLE");
                pf->Release();
            }
            sl->Release();
        }
    }
    if (SUCCEEDED(co)) CoUninitialize();
}

// Jamais d'installation ni de migration : instance d'essai du harnais ([Test] ArrierePlan=1 dans le MWCoop\mwcoop.ini a cote
// du lanceur -- la question s'etait affichee chez JD pour Joueur4, 21/10, puis acceptee pour Joueur1, 26/10 : la garde lisait
// « MWCoop\mwcoop.ini » avec un seul \, jamais trouve), ou MWCOOP_SANS_INSTALL=1 (essais). Raison, ou NULL.
static const char *InstallBlocked(const std::wstring &dir)
{
    wchar_t e[4] = L"";
    if (GetEnvironmentVariableW(L"MWCOOP_SANS_INSTALL", e, 4) && e[0] == L'1') return "MWCOOP_SANS_INSTALL=1";
    if (GetPrivateProfileIntW(L"Test", L"ArrierePlan", 0, (dir + L"MWCoop\\mwcoop.ini").c_str()) != 0) return "instance d'essai ([Test] ArrierePlan=1)";
    return NULL;
}
// Au demarrage (fenetre normale) : vrai si ce lanceur s'est installe et passe la main (il doit s'arreter).
// Essais (MWCOOP_ESSAI_INSTALL=1, avec un LOCALAPPDATA et un MWCOOP_BUREAU jetables) : oui sans question, et le lanceur
// installe n'est pas demarre (verifie ensuite en ligne de commande : /jeu, /miroir).
static bool InstallAtStartup()
{
    if (RunningFromHome() || IsWine() || LocalDir().empty()) return false;
    if (const char *why = InstallBlocked(g_dir)) { LaunchLog("installation dans AppData : jamais ici (%s)", why); return false; }
    wchar_t ess[8] = L"";
    bool test = GetEnvironmentVariableW(L"MWCOOP_ESSAI_INSTALL", ess, 8) && ess[0] == L'1';
    std::wstring home = LocalDir();
    std::wstring dirs[2] = { FindGameOf(GAME_MWC), FindGameOf(GAME_MSC) };
    bool legacy[2] = { LegacyIn(dirs[0]), LegacyIn(dirs[1]) };
    std::wstring q = T(L"MWCoop s'installe maintenant dans AppData\\Local\\MWCoop, avec un raccourci « MWCoop » sur le bureau : un seul lanceur pour My Winter Car et My Summer Car.",
                       L"MWCoop now installs into AppData\\Local\\MWCoop, with an “MWCoop” shortcut on the desktop: one launcher for My Winter Car and My Summer Car.");
    if (legacy[0] || legacy[1]) {
        q += T(L"\n\nTes fichiers MWCoop (sauvegardes coop, réglages, tenues, journaux) y sont déplacés depuis :", L"\n\nYour MWCoop files (co-op saves, settings, outfits, logs) are moved there from:");
        for (int g = 0; g < 2; g++) if (legacy[g]) q += L"\n• " + dirs[g];
        q += T(L"\n\nPuis le dossier du jeu est nettoyé : ce que MWCoop y avait mis part à la Corbeille (ta sauvegarde du jeu, tes mods et un MSCLoader officiel ne sont pas touchés).",
               L"\n\nThen the game folder is cleaned: what MWCoop had put there goes to the Recycle Bin (your game save, your mods and an official MSCLoader are not touched).");
    }
    q += T(L"\n\nInstaller maintenant ? (Non : plus tard, le lanceur marche comme avant.)", L"\n\nInstall now? (No: later, the launcher works as before.)");
    if (!test && MessageBoxW(NULL, q.c_str(), L"MWCoop", MB_YESNO | MB_ICONQUESTION | MB_SETFOREGROUND) != IDYES) { LaunchLog("installation dans AppData : plus tard"); return false; }
    for (int g = 0; g < 2; g++) {
        int was = g_game; g_game = g;
        bool running = !dirs[g].empty() && GameProcessRunning(NULL);
        g_game = was;
        if (running && !test) { MessageBoxW(NULL, T(L"Ferme le jeu, puis relance MWCoop pour l'installer.", L"Close the game, then restart MWCoop to install it."), L"MWCoop", MB_OK | MB_ICONWARNING); return false; }
    }
    SHCreateDirectoryExW(NULL, home.c_str(), NULL);
    std::wstring exe = home + L"MWCoop.exe", ini = home + L"mwcoop-lanceur.ini";
    SetFileAttributesW(exe.c_str(), FILE_ATTRIBUTE_NORMAL);
    if (!CopyFileW(g_self.c_str(), exe.c_str(), FALSE)) {
        MessageBoxW(NULL, (std::wstring(T(L"Installation impossible : copie du lanceur refusée (erreur ", L"Install failed: could not copy the launcher (error ")) + std::to_wstring(GetLastError()) + L").").c_str(), L"MWCoop", MB_OK | MB_ICONERROR);
        return false;
    }
    if (!FileExists(ini) && FileExists(g_iniLauncher)) CopyFileW(g_iniLauncher.c_str(), ini.c_str(), TRUE);
    for (int g = 0; g < 2; g++) if (!dirs[g].empty()) WritePrivateProfileStringW(L"Lanceur", kGames[g].iniDir, dirs[g].c_str(), ini.c_str());
    WritePrivateProfileStringW(L"Lanceur", L"JeuChoisi", kGames[g_game].id, ini.c_str());
    std::wstring report;
    for (int g = 0; g < 2; g++) if (legacy[g]) MigrateGame(g, dirs[g], home, &report);
    DesktopShortcut(exe, g_self);
    LaunchLog("installation dans AppData : %s", Narrow(exe, CP_UTF8).c_str());
    // le lanceur installe prend la suite ; il met celui-ci a la Corbeille une fois ferme (s'il etait dans un dossier de jeu)
    std::wstring cmd = L"\"" + exe + L"\" +apres-installation \"" + g_self + L"\"";
    for (auto &c : report) if (c == L'\n') c = L'|';   // (une seule ligne dans l'ini)
    if (!report.empty()) WritePrivateProfileStringW(L"Lanceur", L"RapportInstallation", report.c_str(), ini.c_str());
    if (test) { LaunchLog("installation (essai) : lanceur installe pas demarre"); return true; }
    STARTUPINFOW si = { sizeof(si) }; PROCESS_INFORMATION pi = {};
    std::vector<wchar_t> cb(cmd.begin(), cmd.end()); cb.push_back(0);
    if (!CreateProcessW(exe.c_str(), cb.data(), NULL, NULL, FALSE, 0, NULL, home.c_str(), &si, &pi)) { LaunchLog("installation : lanceur installe pas demarre (erreur %lu)", GetLastError()); return false; }
    CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
    return true;
}

// Lanceur installe, juste apres l'installation : l'ancien lanceur (dossier du jeu) a la Corbeille une fois ferme, et le compte rendu.
static void AfterInstall(const std::wstring &old)
{
    if (!old.empty() && _wcsicmp(old.c_str(), g_self.c_str())) {
        std::wstring low = old; for (auto &c : low) c = towlower(c);
        bool inGame = IsGameDirOf(DirOf(old), GAME_MWC) || IsGameDirOf(DirOf(old), GAME_MSC);
        if (inGame) {
            for (int i = 0; i < 40 && FileExists(old); i++) {   // (l'ancien finit de se fermer)
                HANDLE f = CreateFileW(old.c_str(), DELETE, 0, NULL, OPEN_EXISTING, 0, NULL);
                if (f != INVALID_HANDLE_VALUE) { CloseHandle(f); break; }
                Sleep(250);
            }
            std::vector<std::wstring> bin;
            if (FileExists(old)) bin.push_back(old);
            if (FileExists(old + L".old")) bin.push_back(old + L".old");
            LaunchLog("installation : ancien lanceur %s a la Corbeille : %s", Narrow(old, CP_UTF8).c_str(), ToRecycleBin(bin) ? "ok" : "ECHEC");
        }
    }
    wchar_t rep[2048] = L"";
    GetPrivateProfileStringW(L"Lanceur", L"RapportInstallation", L"", rep, 2048, g_iniLauncher.c_str());
    WritePrivateProfileStringW(L"Lanceur", L"RapportInstallation", NULL, g_iniLauncher.c_str());
    std::wstring msg = T(L"MWCoop est installé dans AppData\\Local\\MWCoop, avec un raccourci « MWCoop » sur le bureau.", L"MWCoop is installed in AppData\\Local\\MWCoop, with an “MWCoop” shortcut on the desktop.");
    for (wchar_t *c = rep; *c; c++) if (*c == L'|') *c = L'\n';
    if (rep[0]) msg += std::wstring(L"\n") + rep;
    msg += T(L"\n\nSi tu avais mis MWCoop dans les options de lancement de Steam, remets-le depuis Réglages › Jeu (le chemin du lanceur a changé).",
             L"\n\nIf you had put MWCoop in Steam's launch options, set it again from Settings › Game (the launcher's path changed).");
    MessageBoxW(NULL, msg.c_str(), L"MWCoop", MB_OK | MB_ICONINFORMATION | MB_SETFOREGROUND);
}
