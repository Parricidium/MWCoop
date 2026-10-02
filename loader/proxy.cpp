// MWCoop - chargeur.
// Le jeu importe version.dll : cette copie, posee a cote de mywintercar.exe, est chargee a sa
// place. Elle renvoie les vraies fonctions (exports.asm) et :
//  - attrape mono_runtime_invoke quand Unity le demande a GetProcAddress, puis, au premier appel
//    du code du jeu sur le fil principal, charge MWCoop\MWCoop.dll et appelle MWCoop.Entry.Init() ;
//  - Profil=<nom> (ou -mwcoop-profil <nom>) : sauvegardes (LocalLow), registre et verrou
//    d'instance unique propres a ce profil -> plusieurs jeux sur un PC, sauvegarde de JD intacte ;
//  - ArrierePlan=1 : fenetre posee hors ecran, jamais activee, ne touche ni au curseur ni au
//    clavier/souris (instances de test sur le PC de JD).
#include <windows.h>
#include <shlobj.h>
#include <knownfolders.h>
#include <shlwapi.h>
#include <stdio.h>
#include <stdarg.h>
#include <string.h>
#include <wchar.h>

extern "C" FARPROC g_real[17];
FARPROC g_real[17];
static const char* kExports[17] = {
    "GetFileVersionInfoA", "GetFileVersionInfoByHandle", "GetFileVersionInfoExA",
    "GetFileVersionInfoExW", "GetFileVersionInfoSizeA", "GetFileVersionInfoSizeExA",
    "GetFileVersionInfoSizeExW", "GetFileVersionInfoSizeW", "GetFileVersionInfoW",
    "VerFindFileA", "VerFindFileW", "VerInstallFileA", "VerInstallFileW",
    "VerLanguageNameA", "VerLanguageNameW", "VerQueryValueA", "VerQueryValueW"};

static wchar_t g_gameDir[MAX_PATH];   // dossier de mywintercar.exe (sans \ final)
static wchar_t g_modDir[MAX_PATH];    // <jeu>\MWCoop
static wchar_t g_profil[64];          // vide = profil normal du joueur
static wchar_t g_profilDir[MAX_PATH]; // <jeu>\MWCoop\profils\<profil>
static int g_arrierePlan, g_fenX = -3000, g_fenY = 100;
static DWORD g_mainTid;
static HWND g_mainWnd;
static FILE* g_log;
static CRITICAL_SECTION g_logLock;

static void Log(const char* fmt, ...) {
    if (!g_log) return;
    EnterCriticalSection(&g_logLock);
    SYSTEMTIME t; GetLocalTime(&t);
    fprintf(g_log, "%02d:%02d:%02d.%03d ", t.wHour, t.wMinute, t.wSecond, t.wMilliseconds);
    va_list a; va_start(a, fmt); vfprintf(g_log, fmt, a); va_end(a);
    fputc('\n', g_log); fflush(g_log);
    LeaveCriticalSection(&g_logLock);
}

static void ToUtf8(const wchar_t* w, char* out, int n) {
    WideCharToMultiByte(CP_UTF8, 0, w, -1, out, n, NULL, NULL);
}

// ---------------------------------------------------------------- IAT
static void* HookIat(HMODULE mod, const char* dll, const char* fn, void* hook) {
    BYTE* base = (BYTE*)mod;
    IMAGE_NT_HEADERS* nt = (IMAGE_NT_HEADERS*)(base + ((IMAGE_DOS_HEADER*)base)->e_lfanew);
    IMAGE_DATA_DIRECTORY& dir = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (!dir.VirtualAddress) return NULL;
    for (IMAGE_IMPORT_DESCRIPTOR* imp = (IMAGE_IMPORT_DESCRIPTOR*)(base + dir.VirtualAddress); imp->Name; imp++) {
        if (_stricmp((char*)(base + imp->Name), dll)) continue;
        IMAGE_THUNK_DATA* th = (IMAGE_THUNK_DATA*)(base + imp->FirstThunk);
        IMAGE_THUNK_DATA* nm = (IMAGE_THUNK_DATA*)(base + (imp->OriginalFirstThunk ? imp->OriginalFirstThunk : imp->FirstThunk));
        for (; nm->u1.AddressOfData; th++, nm++) {
            if (IMAGE_SNAP_BY_ORDINAL(nm->u1.Ordinal)) continue;
            IMAGE_IMPORT_BY_NAME* ibn = (IMAGE_IMPORT_BY_NAME*)(base + nm->u1.AddressOfData);
            if (strcmp((char*)ibn->Name, fn)) continue;
            DWORD old;
            VirtualProtect(&th->u1.Function, sizeof(void*), PAGE_READWRITE, &old);
            void* orig = (void*)th->u1.Function;
            th->u1.Function = (ULONG_PTR)hook;
            VirtualProtect(&th->u1.Function, sizeof(void*), old, &old);
            return orig;
        }
    }
    Log("IAT : %s!%s introuvable", dll, fn);
    return NULL;
}

// ---------------------------------------------------------------- Mono
typedef void* (*mono_runtime_invoke_t)(void*, void*, void**, void**);
static mono_runtime_invoke_t o_invoke;
static HMODULE g_mono;
static bool g_started;

template <class T> static T MonoFn(const char* n) {
    T p = (T)GetProcAddress(g_mono, n);
    if (!p) Log("mono : %s absent", n);
    return p;
}

static const char* ImageOf(void* method) {
    static void* (*get_class)(void*) = MonoFn<void* (*)(void*)>("mono_method_get_class");
    static void* (*get_image)(void*) = MonoFn<void* (*)(void*)>("mono_class_get_image");
    static const char* (*img_name)(void*) = MonoFn<const char* (*)(void*)>("mono_image_get_name");
    if (!get_class || !get_image || !img_name || !method) return NULL;
    void* c = get_class(method);
    void* i = c ? get_image(c) : NULL;
    return i ? img_name(i) : NULL;
}

static void LogException(void* exc) {
    void* (*to_string)(void*, void**) = MonoFn<void* (*)(void*, void**)>("mono_object_to_string");
    char* (*to_utf8)(void*) = MonoFn<char* (*)(void*)>("mono_string_to_utf8");
    void* s = (to_string && to_utf8) ? to_string(exc, NULL) : NULL;
    Log("exception dans MWCoop.Entry.Init :\n%s", s ? to_utf8(s) : "(illisible)");
}

static void StartManaged() {
    wchar_t wpath[MAX_PATH]; char path[MAX_PATH * 3];
    swprintf(wpath, MAX_PATH, L"%s\\MWCoop.dll", g_modDir);
    ToUtf8(wpath, path, sizeof path);
    auto domain_get = MonoFn<void* (*)()>("mono_domain_get");
    auto asm_open = MonoFn<void* (*)(void*, const char*)>("mono_domain_assembly_open");
    auto asm_image = MonoFn<void* (*)(void*)>("mono_assembly_get_image");
    auto class_from_name = MonoFn<void* (*)(void*, const char*, const char*)>("mono_class_from_name");
    auto get_method = MonoFn<void* (*)(void*, const char*, int)>("mono_class_get_method_from_name");
    if (!domain_get || !asm_open || !asm_image || !class_from_name || !get_method) return;
    void* a = asm_open(domain_get(), path);
    if (!a) { Log("impossible de charger %s", path); return; }
    void* cls = class_from_name(asm_image(a), "MWCoop", "Entry");
    void* m = cls ? get_method(cls, "Init", 0) : NULL;
    if (!m) { Log("MWCoop.Entry.Init introuvable"); return; }
    void* exc = NULL;
    o_invoke(m, NULL, NULL, &exc);
    if (exc) LogException(exc);
    else Log("MWCoop.Entry.Init appele");
}

static void* h_invoke(void* method, void* obj, void** params, void** exc) {
    void* r = o_invoke(method, obj, params, exc);
    if (!g_started && GetCurrentThreadId() == g_mainTid) {
        const char* img = ImageOf(method);
        if (img && (!strncmp(img, "Assembly-CSharp", 15) || !strcmp(img, "PlayMaker"))) {
            g_started = true;
            Log("premier appel du jeu (%s) : chargement de MWCoop.dll", img);
            StartManaged();
        }
    }
    return r;
}

// ---------------------------------------------------------------- profil isole
static HRESULT WINAPI h_SHGetKnownFolderPath(REFKNOWNFOLDERID id, DWORD flags, HANDLE tok, PWSTR* out) {
    if (g_profil[0] && IsEqualGUID(id, FOLDERID_LocalAppDataLow)) {
        wchar_t p[MAX_PATH]; swprintf(p, MAX_PATH, L"%s\\LocalLow", g_profilDir);
        SHCreateDirectoryExW(NULL, p, NULL);
        size_t n = (wcslen(p) + 1) * sizeof(wchar_t);
        *out = (PWSTR)CoTaskMemAlloc(n);
        memcpy(*out, p, n);
        return S_OK;
    }
    return SHGetKnownFolderPath(id, flags, tok, out);
}

static HRESULT WINAPI h_SHGetFolderPathW(HWND w, int csidl, HANDLE tok, DWORD flags, LPWSTR out) {
    HRESULT r = SHGetFolderPathW(w, csidl, tok, flags, out);
    static int logged;
    if (logged++ < 20) Log("SHGetFolderPathW(0x%x) -> %ls", csidl, SUCCEEDED(r) ? out : L"?");
    if (g_profil[0] && (csidl & 0xff) == CSIDL_LOCAL_APPDATA) {
        swprintf(out, MAX_PATH, L"%s\\Local", g_profilDir);
        SHCreateDirectoryExW(NULL, out, NULL);
        return S_OK;
    }
    return r;
}

static FARPROC WINAPI h_GetProcAddress(HMODULE m, LPCSTR name) {
    FARPROC p = GetProcAddress(m, name);
    if (!p || IS_INTRESOURCE(name)) return p;
    if (!strcmp(name, "mono_runtime_invoke") && !o_invoke) {
        g_mono = m; o_invoke = (mono_runtime_invoke_t)p;
        Log("mono_runtime_invoke attrape");
        return (FARPROC)h_invoke;
    }
    if (g_profil[0] && !strcmp(name, "SHGetKnownFolderPath")) return (FARPROC)h_SHGetKnownFolderPath;
    return p;
}

// HKCU\Software\Amistech\... -> HKCU\Software\MWCoop-Profils\<profil>\Amistech\...
static bool RedirKeyW(HKEY k, LPCWSTR sub, wchar_t* out) {
    if (!g_profil[0] || k != HKEY_CURRENT_USER || !sub || _wcsnicmp(sub, L"Software\\Amistech", 17)) return false;
    swprintf(out, 512, L"Software\\MWCoop-Profils\\%s\\%s", g_profil, sub + 9);
    return true;
}
static bool RedirKeyA(HKEY k, LPCSTR sub, char* out) {
    if (!g_profil[0] || k != HKEY_CURRENT_USER || !sub || _strnicmp(sub, "Software\\Amistech", 17)) return false;
    char p[64]; ToUtf8(g_profil, p, sizeof p);
    sprintf(out, "Software\\MWCoop-Profils\\%s\\%s", p, sub + 9);
    return true;
}
static LSTATUS WINAPI h_RegCreateKeyExW(HKEY k, LPCWSTR sub, DWORD r, LPWSTR cls, DWORD o, REGSAM s,
                                        const LPSECURITY_ATTRIBUTES sa, PHKEY res, LPDWORD d) {
    wchar_t b[512];
    return RegCreateKeyExW(k, RedirKeyW(k, sub, b) ? b : sub, r, cls, o, s, sa, res, d);
}
static LSTATUS WINAPI h_RegOpenKeyExW(HKEY k, LPCWSTR sub, DWORD o, REGSAM s, PHKEY res) {
    wchar_t b[512];
    return RegOpenKeyExW(k, RedirKeyW(k, sub, b) ? b : sub, o, s, res);
}
static LSTATUS WINAPI h_RegOpenKeyExA(HKEY k, LPCSTR sub, DWORD o, REGSAM s, PHKEY res) {
    char b[512];
    return RegOpenKeyExA(k, RedirKeyA(k, sub, b) ? b : sub, o, s, res);
}
static LSTATUS WINAPI h_RegCreateKeyA(HKEY k, LPCSTR sub, PHKEY res) {
    char b[512];
    return RegCreateKeyA(k, RedirKeyA(k, sub, b) ? b : sub, res);
}
static BOOL WINAPI h_SHDeleteKeyA(HKEY k, LPCSTR sub) {
    char b[512];
    return SHDeleteKeyA(k, RedirKeyA(k, sub, b) ? b : sub);
}

// Instance unique (forceSingleInstance) : le nom du verrou recoit le profil.
static HANDLE WINAPI h_CreateMutexW(LPSECURITY_ATTRIBUTES sa, BOOL own, LPCWSTR name) {
    Log("CreateMutexW(%ls)", name ? name : L"null");
    if (g_profil[0] && name) {
        wchar_t b[512]; swprintf(b, 512, L"%s-MWCoop-%s", name, g_profil);
        return CreateMutexW(sa, own, b);
    }
    return CreateMutexW(sa, own, name);
}
static HANDLE WINAPI h_CreateMutexA(LPSECURITY_ATTRIBUTES sa, BOOL own, LPCSTR name) {
    Log("CreateMutexA(%s)", name ? name : "null");
    if (g_profil[0] && name) {
        char p[64], b[512]; ToUtf8(g_profil, p, sizeof p);
        sprintf(b, "%s-MWCoop-%s", name, p);
        return CreateMutexA(sa, own, b);
    }
    return CreateMutexA(sa, own, name);
}

// ---------------------------------------------------------------- arriere-plan
static bool IsUnityClass(LPCWSTR cls) {
    return cls && !IS_INTRESOURCE(cls) && !wcscmp(cls, L"UnityWndClass");
}
static HWND WINAPI h_CreateWindowExW(DWORD ex, LPCWSTR cls, LPCWSTR title, DWORD style, int x, int y,
                                     int w, int h, HWND parent, HMENU menu, HINSTANCE inst, LPVOID param) {
    bool main = IsUnityClass(cls) && !parent;
    if (main) { x = g_fenX; y = g_fenY; ex |= WS_EX_NOACTIVATE; }
    HWND r = CreateWindowExW(ex, cls, title, style, x, y, w, h, parent, menu, inst, param);
    if (main) { g_mainWnd = r; Log("fenetre du jeu %p posee en %d,%d (%dx%d)", r, x, y, w, h); }
    return r;
}
static BOOL WINAPI h_SetWindowPos(HWND wnd, HWND after, int x, int y, int cx, int cy, UINT f) {
    if (wnd == g_mainWnd) { x = g_fenX; y = g_fenY; f = (f & ~SWP_NOMOVE) | SWP_NOACTIVATE | SWP_NOZORDER; }
    return SetWindowPos(wnd, after, x, y, cx, cy, f);
}
static BOOL WINAPI h_ShowWindow(HWND wnd, int cmd) {
    if (wnd == g_mainWnd && cmd != SW_HIDE && cmd != SW_MINIMIZE) cmd = SW_SHOWNOACTIVATE;
    return ShowWindow(wnd, cmd);
}
static BOOL WINAPI h_SetForegroundWindow(HWND) { return TRUE; }
static HWND WINAPI h_SetFocus(HWND) { return NULL; }
static HWND WINAPI h_SetCapture(HWND) { return NULL; }
static BOOL WINAPI h_ClipCursor(const RECT*) { return TRUE; }
static BOOL WINAPI h_SetCursorPos(int, int) { return TRUE; }
static int WINAPI h_ShowCursor(BOOL show) { return show ? 0 : -1; }
static SHORT WINAPI h_GetAsyncKeyState(int) { return 0; }
static SHORT WINAPI h_GetKeyState(int) { return 0; }
static BOOL WINAPI h_GetCursorPos(LPPOINT p) { p->x = g_fenX - 50; p->y = g_fenY - 50; return TRUE; }
static UINT WINAPI h_GetRawInputData(HRAWINPUT, UINT, LPVOID, PUINT, UINT) { return (UINT)-1; }
static HCURSOR WINAPI h_SetCursor(HCURSOR c) { return c; }
// Fenetre de choix de la resolution (displayResolutionDialog) : sautee, reglages du registre.
static INT_PTR WINAPI h_DialogBoxParamW(HINSTANCE i, LPCWSTR t, HWND p, DLGPROC f, LPARAM l) {
    Log("DialogBoxParamW(%p) saute", t);
    return IDOK;
}
static INT_PTR WINAPI h_DialogBoxParamA(HINSTANCE i, LPCSTR t, HWND p, DLGPROC f, LPARAM l) {
    Log("DialogBoxParamA(%p) saute", t);
    return IDOK;
}

// ---------------------------------------------------------------- configuration
static void ReadConfig() {
    wchar_t ini[MAX_PATH]; swprintf(ini, MAX_PATH, L"%s\\mwcoop.ini", g_modDir);
    GetPrivateProfileStringW(L"Test", L"Profil", L"", g_profil, 64, ini);
    g_arrierePlan = GetPrivateProfileIntW(L"Test", L"ArrierePlan", 0, ini);
    g_fenX = GetPrivateProfileIntW(L"Test", L"FenetreX", g_fenX, ini);
    g_fenY = GetPrivateProfileIntW(L"Test", L"FenetreY", g_fenY, ini);
    int argc; LPWSTR* argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    for (int i = 1; argv && i < argc; i++) {
        if (!_wcsicmp(argv[i], L"-mwcoop-profil") && i + 1 < argc) wcsncpy(g_profil, argv[++i], 63);
        else if (!_wcsicmp(argv[i], L"-mwcoop-arriereplan")) g_arrierePlan = 1;
    }
    if (argv) LocalFree(argv);
    for (wchar_t* c = g_profil; *c; c++)   // nom de dossier sur
        if (wcschr(L"\\/:*?\"<>|", *c)) *c = L'_';
    if (g_profil[0]) {
        swprintf(g_profilDir, MAX_PATH, L"%s\\profils\\%s", g_modDir, g_profil);
        SHCreateDirectoryExW(NULL, g_profilDir, NULL);
    }
}

static void Init() {
    InitializeCriticalSection(&g_logLock);
    g_mainTid = GetCurrentThreadId();
    GetModuleFileNameW(NULL, g_gameDir, MAX_PATH);
    *wcsrchr(g_gameDir, L'\\') = 0;
    swprintf(g_modDir, MAX_PATH, L"%s\\MWCoop", g_gameDir);

    wchar_t sys[MAX_PATH]; GetSystemDirectoryW(sys, MAX_PATH);
    wcscat(sys, L"\\version.dll");
    HMODULE real = LoadLibraryW(sys);
    for (int i = 0; i < 17; i++) g_real[i] = real ? GetProcAddress(real, kExports[i]) : NULL;

    ReadConfig();
    wchar_t logs[MAX_PATH], lp[MAX_PATH];
    swprintf(logs, MAX_PATH, L"%s\\logs", g_profil[0] ? g_profilDir : g_modDir);
    SHCreateDirectoryExW(NULL, logs, NULL);
    swprintf(lp, MAX_PATH, L"%s\\chargeur.log", logs);
    g_log = _wfopen(lp, L"w");
    Log("MWCoop chargeur - jeu : %ls", g_gameDir);
    Log("profil : %ls, arriere-plan : %d", g_profil[0] ? g_profil : L"(normal)", g_arrierePlan);
    if (!real) Log("ERREUR : %ls introuvable", sys);
    // Pour le mod (Environment.GetEnvironmentVariable) : ou ecrire ses journaux et ses donnees.
    SetEnvironmentVariableW(L"MWCOOP_DIR", g_modDir);
    SetEnvironmentVariableW(L"MWCOOP_PROFIL", g_profil);
    SetEnvironmentVariableW(L"MWCOOP_DATA", g_profil[0] ? g_profilDir : g_modDir);
    SetEnvironmentVariableW(L"MWCOOP_ARRIEREPLAN", g_arrierePlan ? L"1" : L"0");

    HMODULE exe = GetModuleHandleW(NULL);
    HookIat(exe, "KERNEL32.dll", "GetProcAddress", (void*)h_GetProcAddress);
    if (g_profil[0]) {
        HookIat(exe, "SHELL32.dll", "SHGetFolderPathW", (void*)h_SHGetFolderPathW);
        HookIat(exe, "ADVAPI32.dll", "RegCreateKeyExW", (void*)h_RegCreateKeyExW);
        HookIat(exe, "ADVAPI32.dll", "RegOpenKeyExW", (void*)h_RegOpenKeyExW);
        HookIat(exe, "ADVAPI32.dll", "RegOpenKeyExA", (void*)h_RegOpenKeyExA);
        HookIat(exe, "ADVAPI32.dll", "RegCreateKeyA", (void*)h_RegCreateKeyA);
        HookIat(exe, "SHLWAPI.dll", "SHDeleteKeyA", (void*)h_SHDeleteKeyA);
        HookIat(exe, "KERNEL32.dll", "CreateMutexW", (void*)h_CreateMutexW);
        HookIat(exe, "KERNEL32.dll", "CreateMutexA", (void*)h_CreateMutexA);
    }
    if (g_arrierePlan) {
        HookIat(exe, "USER32.dll", "CreateWindowExW", (void*)h_CreateWindowExW);
        HookIat(exe, "USER32.dll", "SetWindowPos", (void*)h_SetWindowPos);
        HookIat(exe, "USER32.dll", "ShowWindow", (void*)h_ShowWindow);
        HookIat(exe, "USER32.dll", "SetForegroundWindow", (void*)h_SetForegroundWindow);
        HookIat(exe, "USER32.dll", "SetFocus", (void*)h_SetFocus);
        HookIat(exe, "USER32.dll", "SetCapture", (void*)h_SetCapture);
        HookIat(exe, "USER32.dll", "ClipCursor", (void*)h_ClipCursor);
        HookIat(exe, "USER32.dll", "SetCursorPos", (void*)h_SetCursorPos);
        HookIat(exe, "USER32.dll", "ShowCursor", (void*)h_ShowCursor);
        HookIat(exe, "USER32.dll", "SetCursor", (void*)h_SetCursor);
        HookIat(exe, "USER32.dll", "GetAsyncKeyState", (void*)h_GetAsyncKeyState);
        HookIat(exe, "USER32.dll", "GetKeyState", (void*)h_GetKeyState);
        HookIat(exe, "USER32.dll", "GetCursorPos", (void*)h_GetCursorPos);
        HookIat(exe, "USER32.dll", "GetRawInputData", (void*)h_GetRawInputData);
        HookIat(exe, "USER32.dll", "DialogBoxParamW", (void*)h_DialogBoxParamW);
        HookIat(exe, "USER32.dll", "DialogBoxParamA", (void*)h_DialogBoxParamA);
    }
}

BOOL WINAPI DllMain(HINSTANCE h, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(h);
        Init();
    }
    return TRUE;
}
