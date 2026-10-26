// ---------------------------------------------------------------- CLES D'ACCES (inclus par launcher.cpp ; aussi par l'outil
// prive de JD, outils-prives/cles/clesjd.cpp, avec CLE_OUTIL : creer la paire, signer, chiffrer)
// Outil des places reserve aux personnes choisies par JD (spec validee le 26/10, D:\1 - AI\ClaudeAI\MWCoop-spec-cle-places.md) :
// une cle « MWCP1.<donnees base64url>.<signature base64url> » signee par JD (ECDSA P-256 + SHA-256) nomme la personne et ses
// identifiants (ID MWCoop de son PC, SteamID64) et porte la cle AES-256 de l'outil chiffre. Verifiee sans serveur avec la cle
// publique de JD integree ; sans limite de duree (exp= facultatif) ; retiree par sa serie dans dist/cles-retirees.txt.
// Cryptographie de Windows seulement (bcrypt.dll, crypt32.dll), chargee par son chemin dans System32 : jamais d'import direct
// d'une DLL qui pourrait etre dans le dossier du jeu.
#include <bcrypt.h>
#include <wincrypt.h>
#ifndef NT_SUCCESS
#define NT_SUCCESS(s) (((NTSTATUS)(s)) >= 0)
#endif
namespace cle {
struct Api {
    bool ok = false;
    decltype(&BCryptOpenAlgorithmProvider) Open; decltype(&BCryptCloseAlgorithmProvider) Close;
    decltype(&BCryptGenRandom) Random; decltype(&BCryptCreateHash) CreateHash; decltype(&BCryptHashData) HashData;
    decltype(&BCryptFinishHash) FinishHash; decltype(&BCryptDestroyHash) DestroyHash; decltype(&BCryptImportKeyPair) ImportPair;
    decltype(&BCryptVerifySignature) Verify; decltype(&BCryptDestroyKey) DestroyKey; decltype(&BCryptSetProperty) SetProp;
    decltype(&BCryptGenerateSymmetricKey) SymKey; decltype(&BCryptDecrypt) Decrypt; decltype(&BCryptEncrypt) Encrypt;
    decltype(&BCryptGenerateKeyPair) GenPair; decltype(&BCryptFinalizeKeyPair) FinalizePair; decltype(&BCryptExportKey) Export;
    decltype(&BCryptSignHash) Sign;
    decltype(&CryptProtectData) Protect; decltype(&CryptUnprotectData) Unprotect;
};
static HMODULE SysDll(const wchar_t *name)
{
    wchar_t p[MAX_PATH];
    UINT n = GetSystemDirectoryW(p, MAX_PATH);
    if (!n || n > MAX_PATH - 32) return NULL;
    wcscat_s(p, L"\\"); wcscat_s(p, name);
    return LoadLibraryExW(p, NULL, LOAD_WITH_ALTERED_SEARCH_PATH);
}
static const Api &A()
{
    static Api a;
    static bool tried = false;
    if (tried) return a;
    tried = true;
    HMODULE b = SysDll(L"bcrypt.dll"), c = SysDll(L"crypt32.dll");
    if (!b || !c) return a;
#define CLE_GET(m, f, n) a.f = (decltype(a.f))GetProcAddress(m, n)
    CLE_GET(b, Open, "BCryptOpenAlgorithmProvider"); CLE_GET(b, Close, "BCryptCloseAlgorithmProvider"); CLE_GET(b, Random, "BCryptGenRandom");
    CLE_GET(b, CreateHash, "BCryptCreateHash"); CLE_GET(b, HashData, "BCryptHashData"); CLE_GET(b, FinishHash, "BCryptFinishHash");
    CLE_GET(b, DestroyHash, "BCryptDestroyHash"); CLE_GET(b, ImportPair, "BCryptImportKeyPair"); CLE_GET(b, Verify, "BCryptVerifySignature");
    CLE_GET(b, DestroyKey, "BCryptDestroyKey"); CLE_GET(b, SetProp, "BCryptSetProperty"); CLE_GET(b, SymKey, "BCryptGenerateSymmetricKey");
    CLE_GET(b, Decrypt, "BCryptDecrypt"); CLE_GET(b, Encrypt, "BCryptEncrypt"); CLE_GET(b, GenPair, "BCryptGenerateKeyPair");
    CLE_GET(b, FinalizePair, "BCryptFinalizeKeyPair"); CLE_GET(b, Export, "BCryptExportKey"); CLE_GET(b, Sign, "BCryptSignHash");
    CLE_GET(c, Protect, "CryptProtectData"); CLE_GET(c, Unprotect, "CryptUnprotectData");
#undef CLE_GET
    a.ok = a.Open && a.Close && a.Random && a.CreateHash && a.HashData && a.FinishHash && a.DestroyHash && a.ImportPair && a.Verify && a.DestroyKey
        && a.SetProp && a.SymKey && a.Decrypt && a.Encrypt && a.Protect && a.Unprotect;
    return a;
}
static bool Random(void *out, size_t n)
{
    const Api &a = A();
    BCRYPT_ALG_HANDLE h = NULL;
    if (!a.ok || !NT_SUCCESS(a.Open(&h, BCRYPT_RNG_ALGORITHM, NULL, 0))) return false;
    bool ok = NT_SUCCESS(a.Random(h, (PUCHAR)out, (ULONG)n, 0));
    a.Close(h, 0);
    return ok;
}
static std::string Sha256(const std::string &d)
{
    const Api &a = A();
    std::string out;
    BCRYPT_ALG_HANDLE h = NULL; BCRYPT_HASH_HANDLE hh = NULL;
    if (!a.ok || !NT_SUCCESS(a.Open(&h, BCRYPT_SHA256_ALGORITHM, NULL, 0))) return out;
    unsigned char r[32];
    if (NT_SUCCESS(a.CreateHash(h, &hh, NULL, 0, NULL, 0, 0)) && NT_SUCCESS(a.HashData(hh, (PUCHAR)d.data(), (ULONG)d.size(), 0)) && NT_SUCCESS(a.FinishHash(hh, r, 32, 0)))
        out.assign((char *)r, 32);
    if (hh) a.DestroyHash(hh);
    a.Close(h, 0);
    return out;
}
// pub : X || Y (64 octets) ; sig : r || s (64 octets) ; d : donnees signees (hachees ici en SHA-256)
static bool VerifyP256(const std::string &pub, const std::string &d, const std::string &sig)
{
    const Api &a = A();
    if (!a.ok || pub.size() != 64 || sig.size() != 64) return false;
    std::string blob(8, '\0');
    BCRYPT_ECCKEY_BLOB *hd = (BCRYPT_ECCKEY_BLOB *)&blob[0];
    hd->dwMagic = BCRYPT_ECDSA_PUBLIC_P256_MAGIC; hd->cbKey = 32;
    blob += pub;
    BCRYPT_ALG_HANDLE h = NULL; BCRYPT_KEY_HANDLE k = NULL;
    if (!NT_SUCCESS(a.Open(&h, BCRYPT_ECDSA_P256_ALGORITHM, NULL, 0))) return false;
    std::string hash = Sha256(d);
    bool ok = hash.size() == 32 && NT_SUCCESS(a.ImportPair(h, NULL, BCRYPT_ECCPUBLIC_BLOB, &k, (PUCHAR)blob.data(), (ULONG)blob.size(), 0))
           && NT_SUCCESS(a.Verify(k, NULL, (PUCHAR)hash.data(), 32, (PUCHAR)sig.data(), 64, 0));
    if (k) a.DestroyKey(k);
    a.Close(h, 0);
    return ok;
}
// AES-256-GCM : chiffre (enc) ou dechiffre in -> out ; nonce 12 octets, tag 16 octets (sortie en chiffrement, verifie sinon).
static bool AesGcm(bool enc, const std::string &key, const std::string &nonce, const std::string &in, std::string &out, std::string &tag)
{
    const Api &a = A();
    if (!a.ok || key.size() != 32 || nonce.size() != 12 || (!enc && tag.size() != 16)) return false;
    BCRYPT_ALG_HANDLE h = NULL; BCRYPT_KEY_HANDLE k = NULL;
    if (!NT_SUCCESS(a.Open(&h, BCRYPT_AES_ALGORITHM, NULL, 0))) return false;
    bool ok = NT_SUCCESS(a.SetProp(h, BCRYPT_CHAINING_MODE, (PUCHAR)BCRYPT_CHAIN_MODE_GCM, sizeof(BCRYPT_CHAIN_MODE_GCM), 0))
           && NT_SUCCESS(a.SymKey(h, &k, NULL, 0, (PUCHAR)key.data(), 32, 0));
    if (ok) {
        if (enc) tag.assign(16, '\0');
        BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO ai;
        BCRYPT_INIT_AUTH_MODE_INFO(ai);
        ai.pbNonce = (PUCHAR)nonce.data(); ai.cbNonce = 12; ai.pbTag = (PUCHAR)&tag[0]; ai.cbTag = 16;
        out.assign(in.size(), '\0');
        ULONG got = 0;
        NTSTATUS s = enc ? a.Encrypt(k, (PUCHAR)in.data(), (ULONG)in.size(), &ai, NULL, 0, (PUCHAR)(out.empty() ? NULL : &out[0]), (ULONG)out.size(), &got, 0)
                         : a.Decrypt(k, (PUCHAR)in.data(), (ULONG)in.size(), &ai, NULL, 0, (PUCHAR)(out.empty() ? NULL : &out[0]), (ULONG)out.size(), &got, 0);
        ok = NT_SUCCESS(s) && got == in.size();   // (dechiffrement : tag faux -> STATUS_AUTH_TAG_MISMATCH)
        if (!ok) out.clear();
    }
    if (k) a.DestroyKey(k);
    a.Close(h, 0);
    return ok;
}
// DPAPI (compte Windows) : protect vrai -> chiffre, faux -> dechiffre.
static bool Dpapi(bool protect, const std::string &in, std::string &out)
{
    const Api &a = A();
    if (!a.ok) return false;
    DATA_BLOB i = { (DWORD)in.size(), (BYTE *)in.data() }, o = {};
    BOOL r = protect ? a.Protect(&i, L"MWCoop", NULL, NULL, NULL, CRYPTPROTECT_UI_FORBIDDEN, &o) : a.Unprotect(&i, NULL, NULL, NULL, NULL, CRYPTPROTECT_UI_FORBIDDEN, &o);
    if (!r) return false;
    out.assign((char *)o.pbData, o.cbData);
    SecureZeroMemory(o.pbData, o.cbData);
    LocalFree(o.pbData);
    return true;
}
static std::string B64u(const std::string &d)
{
    static const char *t = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
    std::string o;
    size_t i = 0;
    for (; i + 2 < d.size(); i += 3) { uint32_t v = (uint8_t)d[i] << 16 | (uint8_t)d[i + 1] << 8 | (uint8_t)d[i + 2]; o += t[v >> 18]; o += t[(v >> 12) & 63]; o += t[(v >> 6) & 63]; o += t[v & 63]; }
    if (d.size() - i == 1) { uint32_t v = (uint8_t)d[i] << 16; o += t[v >> 18]; o += t[(v >> 12) & 63]; }
    else if (d.size() - i == 2) { uint32_t v = (uint8_t)d[i] << 16 | (uint8_t)d[i + 1] << 8; o += t[v >> 18]; o += t[(v >> 12) & 63]; o += t[(v >> 6) & 63]; }
    return o;
}
static bool UnB64u(const std::string &s, std::string &o)
{
    o.clear();
    uint32_t v = 0; int bits = 0;
    for (char ch : s) {
        int c = ch >= 'A' && ch <= 'Z' ? ch - 'A' : ch >= 'a' && ch <= 'z' ? ch - 'a' + 26 : ch >= '0' && ch <= '9' ? ch - '0' + 52 : ch == '-' || ch == '+' ? 62 : ch == '_' || ch == '/' ? 63 : ch == '=' ? -2 : -1;
        if (c == -2) break;
        if (c < 0) return false;
        v = v << 6 | (uint32_t)c; bits += 6;
        if (bits >= 8) { bits -= 8; o += (char)((v >> bits) & 0xFF); }
    }
    return true;
}
static std::string Hex(const std::string &d) { static const char *x = "0123456789abcdef"; std::string o; for (unsigned char c : d) { o += x[c >> 4]; o += x[c & 15]; } return o; }
static bool UnHex(const std::string &s, std::string &o)
{
    o.clear();
    if (s.size() % 2) return false;
    for (size_t i = 0; i < s.size(); i += 2) {
        auto h = [](char c) { return c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1; };
        int a = h(s[i]), b = h(s[i + 1]);
        if (a < 0 || b < 0) return false;
        o += (char)(a << 4 | b);
    }
    return true;
}
// « cle=valeur; » -> table
static std::map<std::string, std::string> Fields(const std::string &d)
{
    std::map<std::string, std::string> m;
    for (size_t a = 0; a < d.size();) {
        size_t b = d.find(';', a);
        std::string kv = d.substr(a, b == std::string::npos ? std::string::npos : b - a);
        a = b == std::string::npos ? d.size() : b + 1;
        size_t eq = kv.find('=');
        if (eq != std::string::npos) m[kv.substr(0, eq)] = kv.substr(eq + 1);
    }
    return m;
}
// Ligne signee « <prefixe>.<donnees>.<signature> » verifiee par une des cles publiques (liste) : donnees, ou faux.
static bool Signed(const std::string &line, const char *prefix, const std::vector<std::string> &pubs, std::string &data)
{
    std::string l = line;
    while (!l.empty() && (l.back() == '\r' || l.back() == '\n' || l.back() == ' ')) l.pop_back();
    while (!l.empty() && (l[0] == ' ' || (unsigned char)l[0] == 0xEF || (unsigned char)l[0] == 0xBB || (unsigned char)l[0] == 0xBF)) l.erase(0, 1);
    std::string p = std::string(prefix) + ".";
    if (l.compare(0, p.size(), p)) return false;
    size_t dot = l.find('.', p.size());
    if (dot == std::string::npos) return false;
    std::string sig;
    if (!UnB64u(l.substr(p.size(), dot - p.size()), data) || !UnB64u(l.substr(dot + 1), sig)) return false;
    for (const std::string &pk : pubs) if (VerifyP256(pk, data, sig)) return true;
    return false;
}
#ifdef CLE_OUTIL
// (outil prive de JD seulement) paire P-256 : prive = blob BCRYPT_ECCPRIVATE_BLOB, pub = X || Y
static bool GenP256(std::string &privBlob, std::string &pub)
{
    const Api &a = A();
    if (!a.ok || !a.GenPair || !a.FinalizePair || !a.Export) return false;
    BCRYPT_ALG_HANDLE h = NULL; BCRYPT_KEY_HANDLE k = NULL;
    if (!NT_SUCCESS(a.Open(&h, BCRYPT_ECDSA_P256_ALGORITHM, NULL, 0))) return false;
    bool ok = NT_SUCCESS(a.GenPair(h, &k, 256, 0)) && NT_SUCCESS(a.FinalizePair(k, 0));
    ULONG n = 0;
    if (ok) ok = NT_SUCCESS(a.Export(k, NULL, BCRYPT_ECCPRIVATE_BLOB, NULL, 0, &n, 0));
    if (ok) { privBlob.assign(n, '\0'); ok = NT_SUCCESS(a.Export(k, NULL, BCRYPT_ECCPRIVATE_BLOB, (PUCHAR)&privBlob[0], n, &n, 0)); }
    if (ok) pub = privBlob.substr(sizeof(BCRYPT_ECCKEY_BLOB), 64);
    if (k) a.DestroyKey(k);
    a.Close(h, 0);
    return ok && pub.size() == 64;
}
static bool SignP256(const std::string &privBlob, const std::string &d, std::string &sig)
{
    const Api &a = A();
    if (!a.ok || !a.Sign) return false;
    BCRYPT_ALG_HANDLE h = NULL; BCRYPT_KEY_HANDLE k = NULL;
    if (!NT_SUCCESS(a.Open(&h, BCRYPT_ECDSA_P256_ALGORITHM, NULL, 0))) return false;
    std::string hash = Sha256(d);
    ULONG n = 0;
    sig.assign(64, '\0');
    bool ok = NT_SUCCESS(a.ImportPair(h, NULL, BCRYPT_ECCPRIVATE_BLOB, &k, (PUCHAR)privBlob.data(), (ULONG)privBlob.size(), 0))
           && NT_SUCCESS(a.Sign(k, NULL, (PUCHAR)hash.data(), 32, (PUCHAR)&sig[0], 64, &n, 0)) && n == 64;
    if (k) a.DestroyKey(k);
    a.Close(h, 0);
    return ok;
}
#endif
}   // namespace cle
