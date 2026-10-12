// MWCoopRT.dll : ray tracing materiel (DXR) pour MWCoop.
//
// Le mod tire le device Direct3D 11 du jeu d'une de ses textures (RtInit) et, le jeu tournant avec -force-gfx-direct
// (Unity dessine sur son fil principal), appelle RtPostRender depuis OnPostRender de la camera principale : apres la
// scene (G-buffer, eclairage, objets transparents), avant les effets d'image (brouillard, tonemapping) et l'interface.
// Par image :
//   1. Direct3D 11 : position du pixel dans le monde (profondeur d'Unity + matrice inverse de la camera) et normale (G-buffer)
//      ecrites dans deux textures partagees avec Direct3D 12 ; signal de la barriere partagee.
//   2. Direct3D 12 (meme carte) : structures de rayons des maillages recus du mod (BLAS, une fois), scene de l'image
//      (TLAS, instances et matrices du mod), puis rt\shadow.hlsl : rayons vers le disque du soleil -> visibilite.
//   3. Direct3D 11 attend la barriere, puis multiplie l'image par le facteur d'ombre : (ambiance + soleil * N.L * (1 -
//      force * (1 - visibilite))) / (ambiance + soleil * N.L) -- les ombres du soleil d'Unity sont coupees par le mod.
// L'etat de Direct3D 11 d'Unity est mis de cote (SwapDeviceContextState) le temps de notre travail, puis rendu tel quel.
#include <windows.h>
#include <d3d11_4.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <d3dcompiler.h>
#include <stdio.h>
#include <stdarg.h>
#include <string.h>
#include <vector>
#include <unordered_map>
#include <array>
#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "d3d12.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "d3dcompiler.lib")
#include "rt_shadow.h"   // (build\rt_shadow.h, dxc : g_rtShadow)
#include "rt_lamps.h"    // (build\rt_lamps.h, dxc : g_rtLamps)

// ---------------------------------------------------------------- journal
static FILE *g_log;
static CRITICAL_SECTION g_logLock;
static bool g_logInit;
static wchar_t g_logPath[MAX_PATH];
static char g_early[8192];   // lignes ecrites avant que le mod donne le chemin du journal

static void Log(const char *fmt, ...)
{
    if (!g_logInit) { InitializeCriticalSection(&g_logLock); g_logInit = true; }
    EnterCriticalSection(&g_logLock);
    if (!g_log && g_logPath[0]) { g_log = _wfopen(g_logPath, L"ab"); if (g_log && g_early[0]) { fputs(g_early, g_log); g_early[0] = 0; } }
    SYSTEMTIME t; GetLocalTime(&t);
    char line[1024];
    int n = sprintf_s(line, "%02d:%02d:%02d.%03d ", t.wHour, t.wMinute, t.wSecond, t.wMilliseconds);
    va_list a; va_start(a, fmt); _vsnprintf_s(line + n, sizeof line - n - 3, _TRUNCATE, fmt, a); va_end(a);
    strcat_s(line, "\r\n");
    if (g_log) { fputs(line, g_log); fflush(g_log); }
    else if (strlen(g_early) + strlen(line) < sizeof g_early) strcat_s(g_early, line);
    LeaveCriticalSection(&g_logLock);
}

template <class T> static void Rel(T *&p) { if (p) { p->Release(); p = NULL; } }

// ---------------------------------------------------------------- Direct3D 11 (le jeu)
static ID3D11Device *g_dev11;
static ID3D11Device5 *g_dev11_5;
static ID3D11DeviceContext1 *g_ctx1;
static ID3D11DeviceContext4 *g_ctx4;
static ID3DDeviceContextState *g_ourState;
static ID3D11VertexShader *g_vsFull;
static ID3D11PixelShader *g_psTest, *g_psShowN, *g_psShowD, *g_psPrep, *g_psComp, *g_psShowV, *g_psShowP;
static ID3D11BlendState *g_blendMul;
static ID3D11SamplerState *g_sampPt, *g_sampLin;
static ID3D11Buffer *g_cb11;
static ID3D11DeviceContext *g_ctx0;
static UINT g_inW, g_inH;           // taille des entrees (ecran)
static ID3D11Resource *g_gb0, *g_gb1, *g_hdrRes;   // (image en cours : couleur des surfaces du G-buffer, cible de l'eclairage)
static ID3D11RenderTargetView *g_hdrRtv;
static int g_lightPhase;
static bool g_lampsArmed;
static void LampPass(ID3D11RenderTargetView *target, bool flip);
static void EarlyPrep();
static ID3D11PixelShader *g_psHalf, *g_psMask, *g_psAccum, *g_psLamps;
static ID3D11Buffer *g_cbLamps, *g_cbRefl;
static ID3D11BlendState *g_blendSub;
static ID3D11ShaderResourceView *g_srvDepth, *g_srvNormal;   // textures internes d'Unity (profondeur, normales du G-buffer)
static int g_devType = -1;
static int g_dxr = -1;   // -1 pas encore teste, 0 non, 1 oui
static char g_adapter[128];
static LUID g_luid;

// ---------------------------------------------------------------- Direct3D 12 (les rayons)
static ID3D12Device5 *g_d12;
static ID3D12CommandQueue *g_queue;
static ID3D12CommandAllocator *g_alloc[2], *g_allocL[2];   // (L : passe des lampes, sans attendre celle du soleil)
static ID3D12GraphicsCommandList4 *g_cmd;
static ID3D12Fence *g_fence12;          // barriere partagee (Direct3D 11 l'ouvre aussi)
static ID3D11Fence *g_fence11;
static UINT64 g_fenceVal, g_allocFence[2], g_allocFenceL[2];
static UINT g_tlasFrame = ~0u;   // (scene deja construite pour cette image : la passe des lampes la reprend)
static HANDLE g_fenceEvent;
static ID3D12RootSignature *g_rootSig;
static ID3D12PipelineState *g_pso, *g_psoLamps;
// Lampes (phares, lampadaires) : au plus 16 par image, envoyees par le mod (RtLamps).
struct LampIn { float pos[3], range, col[3], type, dir[3], cosHalf; };
static LampIn g_lamps[17];   // (16 : soleil et ambiance, pour les reflets)
static int g_nLamps;
static float g_lampStrength = 1;
static float g_aoStrength, g_aoRadius = 1.5f;   // (occlusion ambiante : force, longueur des rayons)
static float g_reflStrength;   // (reflets : force, 0 non)
static int g_aoRays = 4;
static ID3D12Resource *g_lampBuf[2];
static ID3D12DescriptorHeap *g_heap;
static UINT g_heapInc;
static bool g_d12Ok, g_d12Tried;

// Textures partagees a la taille de l'image
struct Shared { ID3D12Resource *r12; ID3D11Texture2D *t11; ID3D11RenderTargetView *rtv; ID3D11ShaderResourceView *srv; };
static Shared g_pos, g_nrm, g_vis, g_lampVis, g_ao, g_refl;
static UINT g_w, g_h;

// Maillages (BLAS) et scene (TLAS)
struct MeshAs { ID3D12Resource *vb, *ib, *uv, *blas; UINT nv, ni; bool built, alpha; int tex; float cutoff; UINT ibSlot, uvSlot, vbSlot; float col[3]; };
// Feuillages : alpha de la texture (un octet par texel), lu par les rayons (tableau de tampons gBufs).
struct AlphaTex { ID3D12Resource *buf; UINT w, h, slot; };
static std::unordered_map<int, AlphaTex> g_alphaTex;
static const UINT kHeapSize = 8192, kFirstDyn = 6;   // (0 positions, 1 normales, 2 visibilite, 3 lampes, 4 occlusion ambiante, 5 reflets)
static UINT g_nextSlot = kFirstDyn;
static ID3D12Resource *g_instInfo[2]; static UINT64 g_instInfoCap[2];
static std::unordered_map<int, MeshAs> g_meshes;
static std::vector<int> g_pendingBuild;
static std::vector<ID3D12Resource *> g_trash[2];       // a liberer quand l'allocateur revient (televersements, essais)
struct Inst { int mesh; float m[12]; };
static std::vector<Inst> g_inst;
static ID3D12Resource *g_tlas, *g_tlasScratch, *g_instBuf;
static UINT64 g_tlasSize, g_tlasScratchSize, g_instCap;
static ID3D12Resource *g_blasScratch; static UINT64 g_blasScratchSize;

// Parametres de l'image (envoyes par le mod)
struct FrameIn { float invVP[16]; float sunDir[4]; float sunCol[4]; float amb[4]; float misc[4]; float cam[4]; float prevVP[16]; float prevCam[4]; };   // (prev* : image precedente, rempli ici ; prevCam.w = historique valable)   // misc : force de l'ombre, portee, rayons, demi-angle ; cam : position de la camera
static FrameIn g_frame;
static UINT g_frameNo;
static int g_posts;

// ---------------------------------------------------------------- interface d'Unity 5.0 (plugin natif, si enregistre)
extern "C" __declspec(dllexport) void UnitySetGraphicsDevice(void *device, int deviceType, int eventType)
{
    if (eventType == 0) { g_dev11 = (ID3D11Device *)device; g_devType = deviceType; }
    Log("UnitySetGraphicsDevice : device %p, type %d, evenement %d", device, deviceType, eventType);
}
extern "C" __declspec(dllexport) void UnityRenderEvent(int) {}

// ---------------------------------------------------------------- DXR sur la carte du jeu
static IDXGIAdapter1 *GameAdapter()
{
    IDXGIAdapter *ad = NULL; IDXGIAdapter1 *a1 = NULL;
    if (g_dev11) {
        IDXGIDevice *dd = NULL;
        if (SUCCEEDED(g_dev11->QueryInterface(__uuidof(IDXGIDevice), (void **)&dd))) { dd->GetAdapter(&ad); dd->Release(); }
    }
    if (ad) { ad->QueryInterface(__uuidof(IDXGIAdapter1), (void **)&a1); ad->Release(); }
    return a1;
}

static void TestDxr()
{
    if (g_dxr >= 0) return;
    g_dxr = 0;
    IDXGIAdapter1 *ad = GameAdapter();
    if (ad) {
        DXGI_ADAPTER_DESC1 desc; ad->GetDesc1(&desc);
        WideCharToMultiByte(CP_UTF8, 0, desc.Description, -1, g_adapter, sizeof g_adapter, NULL, NULL);
        g_luid = desc.AdapterLuid;
    }
    ID3D12Device5 *d12 = NULL;
    HRESULT hr = D3D12CreateDevice(ad, D3D_FEATURE_LEVEL_12_0, __uuidof(ID3D12Device5), (void **)&d12);
    if (SUCCEEDED(hr)) {
        D3D12_FEATURE_DATA_D3D12_OPTIONS5 o5 = {};
        if (SUCCEEDED(d12->CheckFeatureSupport(D3D12_FEATURE_D3D12_OPTIONS5, &o5, sizeof o5)) && o5.RaytracingTier >= D3D12_RAYTRACING_TIER_1_1) g_dxr = 1;
        Log("DXR : %s, tier %d (1.1 requis : requetes de rayons)", g_adapter, (int)o5.RaytracingTier);
        d12->Release();
    } else Log("DXR : device Direct3D 12 impossible sur %s (0x%08lx)", g_adapter, hr);
    if (ad) ad->Release();
}

extern "C" __declspec(dllexport) int RtProbe(const wchar_t *logPath)
{
    if (logPath && !g_logPath[0]) wcsncpy_s(g_logPath, logPath, _TRUNCATE);
    TestDxr();
    Log("sonde : device %p (type %d), DXR %d", g_dev11, g_devType, g_dxr);
    return (g_dev11 ? 1 : 0) | (g_dxr == 1 ? 2 : 0);
}

// ---------------------------------------------------------------- shaders Direct3D 11
static const char *kShaders = R"(
struct V { float4 p : SV_Position; float2 uv : TEXCOORD0; };
V VsFull(uint id : SV_VertexID) { V o; o.uv = float2((id << 1) & 2, id & 2); o.p = float4(o.uv * float2(2, -2) + float2(-1, 1), 0, 1); return o; }
cbuffer C : register(b0) { row_major float4x4 invVP; float4 sunDir; float4 sunCol; float4 amb; float4 misc; float4 cam; row_major float4x4 prevVP; float4 prevCam; };
Texture2D t0 : register(t0);
Texture2D t1 : register(t1);
Texture2D t2 : register(t2);
Texture2D t3 : register(t3);
Texture2D<uint> tLampVis : register(t4);
Texture2D tAlbedo : register(t5);
Texture2D tScene : register(t6);
Texture2D tAO : register(t7);   // (occlusion ambiante : part degagee)
Texture2D tRefl : register(t8);   // (reflets : distance du point touche, 0 : rien)
Texture2D tSpec : register(t9);   // (2e cible du G-buffer : couleur speculaire, lisse)
cbuffer Rfl : register(b2) { row_major float4x4 curVP; float4 reflP; };   // (reflP : x force, y retourne)
  // (copie de la cible, lampes : on ne retire jamais plus que ce qui est la)
cbuffer Lmp : register(b1) { float4 lampA[16]; float4 lampB[16]; float4 lampC[16]; float4 lampN; };   // A : position, portee ; B : couleur, type ; C : direction, cos du demi-angle
SamplerState sPt : register(s0);
SamplerState sLin : register(s1);
float4 PsHalf(V i) : SV_Target { return i.uv.y < 0.5 ? float4(0.3, 0.3, 0.3, 1) : float4(1, 1, 1, 1); }   // (controle : moitie haute assombrie)
float4 PsTest(V i) : SV_Target { return i.uv.x < 0.5 ? float4(0.45, 0.45, 0.45, 1) : float4(1, 1, 1, 1); }
float4 PsShowNormal(V i) : SV_Target { return float4(t0.Sample(sPt, i.uv).rgb, 1); }
float4 PsShowDepth(V i) : SV_Target { float d = t0.Sample(sPt, i.uv).r; return float4(frac(d * 40), d, d, 1); }
float4 PsShowVis(V i) : SV_Target { float v = t0.Sample(sLin, i.uv).r; return float4(v, v, v, 1); }
float4 PsShowPos(V i) : SV_Target { float4 p = t0.Sample(sPt, i.uv); return p.w > 0.5 ? float4(frac(p.xyz), 1) : float4(0, 0, 0.3, 1); }
// Preparation : position du pixel dans le monde (w = 1 : surface) et normale, vers les textures partagees.
struct P2 { float4 pos : SV_Target0; float4 nrm : SV_Target1; };
P2 PsPrep(V i) {
    P2 o;
    float d = t0.Sample(sPt, i.uv).r;
    if (d >= 0.999999) o.pos = float4(0, 0, 0, 0);
    else { float4 w = mul(invVP, float4(i.uv.x * 2 - 1, 1 - i.uv.y * 2, d, 1)); o.pos = float4(w.xyz / w.w, 1 + t2.Sample(sPt, i.uv).a); }   // (w : 1 + lisse du G-buffer)
    o.nrm = float4(t1.Sample(sPt, i.uv).rgb, 1);
    return o;
}
// Visibilite debruitee (filtre 5x5 qui suit les surfaces : normales proches, points proches). t0 visibilite, t1 normales
// d'Unity, t2 positions. 1 hors surface (ciel).
float FilteredVis(float2 uv0, float step) {
    float4 p = t2.SampleLevel(sPt, uv0, 0);
    if (p.w < 0.5) return 1;
    float3 n = normalize(t1.SampleLevel(sPt, uv0, 0).rgb * 2 - 1);
    float w0, h0; t0.GetDimensions(w0, h0);
    float2 px = step / float2(w0, h0);
    float tol = 0.02 * length(p.xyz - cam.xyz) + 0.05;
    float sum = 0, ws = 0;
    [unroll] for (int y = -2; y <= 2; y++)
        [unroll] for (int x = -2; x <= 2; x++) {
            float2 uv = uv0 + float2(x, y) * px;
            float4 q = t2.SampleLevel(sPt, uv, 0);
            float3 m = normalize(t1.SampleLevel(sPt, uv, 0).rgb * 2 - 1);
            float w = saturate(q.w) * pow(saturate(dot(n, m)), 8) * exp(-length(q.xyz - p.xyz) / tol) * (x == 0 && y == 0 ? 1.5 : 1);
            sum += t0.SampleLevel(sPt, uv, 0).r * w; ws += w;
        }
    return ws > 1e-4 ? sum / ws : t0.SampleLevel(sPt, uv0, 0).r;
}
// Accumulation d'une image a l'autre : le point du pixel est reprojete dans l'image precedente (prevVP) ; si
// l'historique y voit la meme surface (meme distance a son ancienne camera), 15 % de nouveau. Sortie : r visibilite
// accumulee, g distance a la camera. t0 visibilite brute, t2 positions, t3 historique.
float4 PsAccum(V i) : SV_Target {
    float4 p = t2.SampleLevel(sPt, i.uv, 0);
    if (p.w < 0.5) return float4(1, 0, 0, 1);
    float v = t0.SampleLevel(sPt, i.uv, 0).r;
    if (prevCam.w > 0.5) {
        float4 c = mul(prevVP, float4(p.xyz, 1));
        if (c.w > 1e-4) {
            float2 uv = float2(c.x / c.w * 0.5 + 0.5, 0.5 - c.y / c.w * 0.5);
            if (all(uv > 0) && all(uv < 1)) {
                float2 h = t3.SampleLevel(sPt, uv, 0).rg;
                float pd = length(p.xyz - prevCam.xyz);
                if (abs(h.y - pd) < 0.02 * pd + 0.1) v = lerp(h.x, v, 0.15);
            }
        }
    }
    return float4(v, length(p.xyz - cam.xyz), 0, 1);
}
// Ombre ecrite dans la carte d'ombres a l'echelle de l'ecran d'Unity (« Screenspace ShadowMap », lue par son eclairage
// differe) : attenuation = (1 - force) + force * visibilite. sunCol.w : lecture retournee (essais).
float4 PsMask(V i) : SV_Target {
    if (sunCol.w > 0.5) i.uv.y = 1 - i.uv.y;
    float a = 1 - misc.x * (1 - FilteredVis(i.uv, 0.75));
    return float4(a, a, a, a);
}
float FilteredAO(float2 uv0, float3 n, float4 p) {
    float w0, h0; tAO.GetDimensions(w0, h0);
    float2 px = 1.0 / float2(w0, h0);
    float tol = 0.02 * length(p.xyz - cam.xyz) + 0.05;
    float sum = 0, ws = 0;
    [unroll] for (int y = -2; y <= 2; y++)
        [unroll] for (int x = -2; x <= 2; x++) {
            float2 uv = uv0 + float2(x, y) * px;
            float4 q = t2.SampleLevel(sPt, uv, 0);
            float3 m = normalize(t1.SampleLevel(sPt, uv, 0).rgb * 2 - 1);
            float w = saturate(q.w) * pow(saturate(dot(n, m)), 8) * exp(-length(q.xyz - p.xyz) / tol);
            sum += tAO.SampleLevel(sPt, uv, 0).r * w; ws += w;
        }
    return ws > 1e-4 ? sum / ws : tAO.SampleLevel(sPt, uv0, 0).r;
}
// Lampes : part de chaque lampe bloquee (bit dans tLampVis), estimee comme l'eclairage differe d'Unity (couleur de la
// surface x couleur x intensite x attenuation x n.l, cone des projecteurs) ; retiree de l'image (melange soustractif).
float4 PsLamps(V i) : SV_Target {
    if (lampN.z > 0.5) i.uv.y = 1 - i.uv.y;   // (cible de fin de rendu : retournee par le brouillard du jeu face au G-buffer)
    float w0, h0; tLampVis.GetDimensions(w0, h0);
    uint occ = tLampVis.Load(int3(min(i.uv * float2(w0, h0), float2(w0, h0) - 1), 0));
    if (lampN.y < -1.5) return occ ? float4(2, 0, 0, 1) : tScene.Load(int3(i.p.xy, 0)) + float4(0, 0, 0.15, 0);   // (controle : rouge = lampe bloquee)
    if (occ == 0 && lampN.w <= 0 && reflP.x <= 0) discard;
    float4 p = t2.SampleLevel(sPt, i.uv, 0);
    if (p.w < 0.5) discard;
    float3 n = normalize(t1.SampleLevel(sPt, i.uv, 0).rgb * 2 - 1);
    float3 alb = tAlbedo.SampleLevel(sPt, i.uv, 0).rgb;
    float3 rem = 0;
    uint cnt = occ ? min((uint)lampN.x, 16u) : 0;
    for (uint k = 0; k < cnt; k++) {
        if (((occ >> k) & 1u) == 0) continue;
        float3 d = lampA[k].xyz - p.xyz;
        float r2 = dot(d, d) / max(lampA[k].w * lampA[k].w, 1e-4);
        if (r2 >= 1) continue;
        float att = 1 / (1 + 25 * r2) * saturate((1 - r2) * 5);
        float3 l = normalize(d);
        float ndl = saturate(dot(n, l));
        float spot = 1;
        if (lampB[k].w > 0.5) {   // projecteur : disque doux comme le cookie par defaut d'Unity
            float c = dot(-l, lampC[k].xyz), ch = lampC[k].w;
            float tn = sqrt(saturate(1 - c * c)) / max(c, 1e-3), th = sqrt(saturate(1 - ch * ch)) / max(ch, 1e-3);
            float rr = tn / max(th, 1e-3);
            spot = saturate(1 - rr * rr);
        }
        rem += alb * lampB[k].rgb * att * ndl * spot;
    }
    if (lampN.y < -0.5) return float4(rem * 10, 1);   // (controle : part estimee x10)
    float3 total = rem * max(lampN.y, 0);
    if (lampN.w > 0) {   // occlusion ambiante : part de l'ambiance (couleur du jeu) que la surface ne recoit pas
        float ao = FilteredAO(i.uv, n, p);
        if (lampN.w > 9) return float4(ao, ao, ao, 1);   // (controle : occlusion seule)
        total += alb * amb.rgb * (1 - ao) * lampN.w;
    }
    float4 sc = tScene.Load(int3(i.p.xy, 0));
    float3 col = max(sc.rgb - total, sc.rgb * 0.05);
    if (reflP.x > 19) { float sm = saturate(p.w - 1); float th = tRefl.SampleLevel(sPt, i.uv, 0).r; return float4(sm, th > 0 ? 1 : 0, 0, 1); }   // (controle : rouge = lisse, vert = rayon reflechi qui touche)
    if (reflP.x > 0 && p.w > 1.3) {   // reflets : point touche visible a l'ecran -> sa couleur dans l'image
        float4 rf = tRefl.SampleLevel(sPt, i.uv, 0);
        float tHit = rf.a;
        if (tHit > 0) {
            float3 hc = rf.rgb;   // (estimation : couleur de l'objet eclaire ; remplacee par l'image si le point y est visible)
            float3 v = normalize(p.xyz - cam.xyz), rd = reflect(v, n), hit = p.xyz + rd * tHit;
            float4 c = mul(curVP, float4(hit, 1));
            if (c.w > 1e-3) {
                float2 huv = float2(c.x / c.w * 0.5 + 0.5, 0.5 - c.y / c.w * 0.5);
                if (all(huv > 0.002) && all(huv < 0.998)) {
                    float4 q = t2.SampleLevel(sPt, huv, 0);
                    if (q.w > 0.5 && length(q.xyz - hit) < 0.04 * tHit + 0.25) {
                        float w0, h0; tScene.GetDimensions(w0, h0);
                        float2 iuv = reflP.y > 0.5 ? float2(huv.x, 1 - huv.y) : huv;
                        hc = tScene.Load(int3(min(iuv * float2(w0, h0), float2(w0, h0) - 1), 0)).rgb;
                    }
                }
            }
            float4 sp = tSpec.SampleLevel(sPt, i.uv, 0);
            float f0 = max(sp.r, max(sp.g, sp.b));
            float fres = f0 + (1 - f0) * pow(1 - saturate(dot(-v, n)), 5);
            float wgt = saturate((p.w - 1.3) / 0.5) * saturate(fres) * min(reflP.x, 1);
            if (reflP.x > 9) return float4(hc, 1);   // (controle : couleur reflechie seule)
            col = lerp(col, hc * max(sp.rgb, 0.04) / max(f0, 0.04), wgt);
        }
    }
    return float4(col, sc.a);
}
// (ancien melange, mode 14 : facteur multiplie a l'image apres coup)
float4 PsComp(V i) : SV_Target {
    if (amb.w > 0.5) i.uv.y = 1 - i.uv.y;
    float4 p = t2.SampleLevel(sPt, i.uv, 0);
    if (p.w < 0.5) return float4(1, 1, 1, 1);
    float3 n = normalize(t1.SampleLevel(sPt, i.uv, 0).rgb * 2 - 1);
    float vis = FilteredVis(i.uv, 1.5);
    float ndl = saturate(dot(n, sunDir.xyz));
    float3 s = sunCol.rgb * ndl * max(cam.w, 0.1);
    float3 lit = amb.rgb + s;
    float3 sh = amb.rgb + s * (1 - misc.x * (1 - vis));
    float3 f = sh / max(lit, 1e-4);
    if (misc.w < 0) return float4(ndl, vis, s.r / max(lit.r, 1e-4), 1);   // (controle, modes 8/9)
    return float4(f, 1);
}
)";

static bool Compile(const char *entry, const char *target, ID3DBlob **out)
{
    ID3DBlob *err = NULL;
    HRESULT hr = D3DCompile(kShaders, strlen(kShaders), "mwcrt", NULL, NULL, entry, target, D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, out, &err);
    if (FAILED(hr)) { Log("shader %s : %s", entry, err ? (const char *)err->GetBufferPointer() : "?"); if (err) err->Release(); return false; }
    if (err) err->Release();
    return true;
}
static ID3D11PixelShader *Ps(const char *entry)
{
    ID3DBlob *b = NULL; ID3D11PixelShader *p = NULL;
    if (Compile(entry, "ps_5_0", &b)) { g_dev11->CreatePixelShader(b->GetBufferPointer(), b->GetBufferSize(), NULL, &p); b->Release(); }
    return p;
}

extern "C" __declspec(dllexport) int RtInit(void *d3d11Resource)
{
    if (!g_dev11 && d3d11Resource) ((ID3D11Resource *)d3d11Resource)->GetDevice(&g_dev11);
    if (!g_dev11) { Log("RtInit : pas de device"); return 0; }
    if (g_ctx1) return 1 | (g_dxr == 1 ? 2 : 0);
    ID3D11DeviceContext *ctx = NULL; g_dev11->GetImmediateContext(&ctx);
    if (ctx) { ctx->AddRef(); g_ctx0 = ctx; }   // (pointeur qu'utilise Unity : sa table change apres un echange d'etat)
    if (!ctx || FAILED(ctx->QueryInterface(__uuidof(ID3D11DeviceContext1), (void **)&g_ctx1))) { Log("RtInit : pas de contexte 11.1"); if (ctx) ctx->Release(); return 0; }
    ctx->QueryInterface(__uuidof(ID3D11DeviceContext4), (void **)&g_ctx4);
    ctx->Release();
    g_dev11->QueryInterface(__uuidof(ID3D11Device5), (void **)&g_dev11_5);
    ID3D11Device1 *d1 = NULL;
    if (FAILED(g_dev11->QueryInterface(__uuidof(ID3D11Device1), (void **)&d1))) { Log("RtInit : pas de device 11.1"); return 0; }
    D3D_FEATURE_LEVEL fl = g_dev11->GetFeatureLevel(), got;
    UINT cf = g_dev11->GetCreationFlags();   // (Unity : device cree en SINGLETHREADED -> l'etat doit l'etre aussi)
    HRESULT hr = d1->CreateDeviceContextState((cf & D3D11_CREATE_DEVICE_SINGLETHREADED) ? D3D11_1_CREATE_DEVICE_CONTEXT_STATE_SINGLETHREADED : 0, &fl, 1, D3D11_SDK_VERSION, __uuidof(ID3D11Device1), &got, &g_ourState);
    d1->Release();
    if (FAILED(hr)) { Log("RtInit : etat de contexte impossible (0x%08lx)", hr); return 0; }
    ID3DBlob *b = NULL;
    if (!Compile("VsFull", "vs_5_0", &b)) return 0;
    g_dev11->CreateVertexShader(b->GetBufferPointer(), b->GetBufferSize(), NULL, &g_vsFull); b->Release();
    g_psTest = Ps("PsTest"); g_psShowN = Ps("PsShowNormal"); g_psShowD = Ps("PsShowDepth"); g_psShowV = Ps("PsShowVis"); g_psShowP = Ps("PsShowPos");
    g_psPrep = Ps("PsPrep"); g_psComp = Ps("PsComp"); g_psHalf = Ps("PsHalf"); g_psMask = Ps("PsMask"); g_psAccum = Ps("PsAccum"); g_psLamps = Ps("PsLamps");
    {   // (lampes : constantes, melange soustractif dest - src)
        D3D11_BUFFER_DESC lb = {}; lb.ByteWidth = 49 * 16; lb.Usage = D3D11_USAGE_DEFAULT; lb.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        g_dev11->CreateBuffer(&lb, NULL, &g_cbLamps);
        lb.ByteWidth = 5 * 16; g_dev11->CreateBuffer(&lb, NULL, &g_cbRefl);
        D3D11_BLEND_DESC sb = {}; sb.RenderTarget[0].BlendEnable = TRUE;
        sb.RenderTarget[0].SrcBlend = D3D11_BLEND_ONE; sb.RenderTarget[0].DestBlend = D3D11_BLEND_ONE; sb.RenderTarget[0].BlendOp = D3D11_BLEND_OP_REV_SUBTRACT;
        sb.RenderTarget[0].SrcBlendAlpha = D3D11_BLEND_ZERO; sb.RenderTarget[0].DestBlendAlpha = D3D11_BLEND_ONE; sb.RenderTarget[0].BlendOpAlpha = D3D11_BLEND_OP_ADD;
        sb.RenderTarget[0].RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_ALL;
        g_dev11->CreateBlendState(&sb, &g_blendSub);
    }
    D3D11_BLEND_DESC bd = {};
    bd.RenderTarget[0].BlendEnable = TRUE;
    bd.RenderTarget[0].SrcBlend = D3D11_BLEND_ZERO; bd.RenderTarget[0].DestBlend = D3D11_BLEND_SRC_COLOR; bd.RenderTarget[0].BlendOp = D3D11_BLEND_OP_ADD;
    bd.RenderTarget[0].SrcBlendAlpha = D3D11_BLEND_ZERO; bd.RenderTarget[0].DestBlendAlpha = D3D11_BLEND_ONE; bd.RenderTarget[0].BlendOpAlpha = D3D11_BLEND_OP_ADD;
    bd.RenderTarget[0].RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_ALL;
    g_dev11->CreateBlendState(&bd, &g_blendMul);
    D3D11_SAMPLER_DESC sd = {}; sd.Filter = D3D11_FILTER_MIN_MAG_MIP_POINT; sd.AddressU = sd.AddressV = sd.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP; sd.MaxLOD = D3D11_FLOAT32_MAX;
    g_dev11->CreateSamplerState(&sd, &g_sampPt);
    sd.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
    g_dev11->CreateSamplerState(&sd, &g_sampLin);
    D3D11_BUFFER_DESC cbd = {}; cbd.ByteWidth = (sizeof(FrameIn) + 15) & ~15; cbd.Usage = D3D11_USAGE_DEFAULT; cbd.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    g_dev11->CreateBuffer(&cbd, NULL, &g_cb11);
    Log("RtInit : device %p, niveau 0x%x, drapeaux 0x%x, 11.4 %s, 11.5 %s", g_dev11, (int)fl, cf, g_ctx4 ? "oui" : "non", g_dev11_5 ? "oui" : "non");
    TestDxr();
    return 1 | (g_dxr == 1 ? 2 : 0);
}

static ID3D11ShaderResourceView *MakeSrv(void *tex)
{
    if (!tex) return NULL;
    ID3D11ShaderResourceView *v = NULL;
    ID3D11Texture2D *t = NULL;
    D3D11_TEXTURE2D_DESC td = {};
    if (SUCCEEDED(((ID3D11Resource *)tex)->QueryInterface(__uuidof(ID3D11Texture2D), (void **)&t))) { t->GetDesc(&td); t->Release(); }
    DXGI_FORMAT f = td.Format;   // (textures de rendu d'Unity : format sans type -> sa version lisible)
    switch (f) {
    case DXGI_FORMAT_R32_TYPELESS: f = DXGI_FORMAT_R32_FLOAT; break;
    case DXGI_FORMAT_R10G10B10A2_TYPELESS: f = DXGI_FORMAT_R10G10B10A2_UNORM; break;
    case DXGI_FORMAT_R16G16B16A16_TYPELESS: f = DXGI_FORMAT_R16G16B16A16_FLOAT; break;
    case DXGI_FORMAT_R8G8B8A8_TYPELESS: f = DXGI_FORMAT_R8G8B8A8_UNORM; break;
    case DXGI_FORMAT_B8G8R8A8_TYPELESS: f = DXGI_FORMAT_B8G8R8A8_UNORM; break;
    case DXGI_FORMAT_R16_TYPELESS: f = DXGI_FORMAT_R16_UNORM; break;
    case DXGI_FORMAT_R24G8_TYPELESS: f = DXGI_FORMAT_R24_UNORM_X8_TYPELESS; break;
    case DXGI_FORMAT_R32G8X24_TYPELESS: f = DXGI_FORMAT_R32_FLOAT_X8X24_TYPELESS; break;
    default: break;
    }
    D3D11_SHADER_RESOURCE_VIEW_DESC vd = {};
    vd.Format = f; vd.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D; vd.Texture2D.MipLevels = 1;
    HRESULT hr = g_dev11->CreateShaderResourceView((ID3D11Resource *)tex, &vd, &v);
    if (FAILED(hr)) Log("SRV impossible (format %d -> %d, 0x%08lx)", (int)td.Format, (int)f, hr);
    return v;
}

extern "C" __declspec(dllexport) int RtSetInputs(void *depthTex, void *normalTex, int w, int h)
{
    if (!g_dev11) return 0;
    Rel(g_srvDepth); Rel(g_srvNormal);
    g_srvDepth = MakeSrv(depthTex); g_srvNormal = MakeSrv(normalTex);
    g_inW = w; g_inH = h;
    Log("RtSetInputs : %dx%d, profondeur %s, normales %s", w, h, g_srvDepth ? "ok" : "ABSENTE", g_srvNormal ? "ok" : "ABSENTES");
    return g_srvDepth && g_srvNormal ? 1 : 0;
}

// ---------------------------------------------------------------- Direct3D 12 : mise en place
static ID3D12Resource *Buffer(UINT64 size, D3D12_HEAP_TYPE heap, D3D12_RESOURCE_FLAGS flags, D3D12_RESOURCE_STATES state)
{
    D3D12_HEAP_PROPERTIES hp = {}; hp.Type = heap;
    D3D12_RESOURCE_DESC rd = {};
    rd.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER; rd.Width = size < 256 ? 256 : size; rd.Height = 1; rd.DepthOrArraySize = 1; rd.MipLevels = 1;
    rd.SampleDesc.Count = 1; rd.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR; rd.Flags = flags;
    ID3D12Resource *r = NULL;
    HRESULT hr = g_d12->CreateCommittedResource(&hp, D3D12_HEAP_FLAG_NONE, &rd, state, NULL, __uuidof(ID3D12Resource), (void **)&r);
    if (FAILED(hr)) Log("tampon de %llu octets impossible (0x%08lx)", (unsigned long long)size, hr);
    return r;
}
static ID3D12Resource *Upload(const void *data, UINT64 size)
{
    ID3D12Resource *r = Buffer(size, D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_FLAG_NONE, D3D12_RESOURCE_STATE_GENERIC_READ);
    if (!r) return NULL;
    void *p = NULL; D3D12_RANGE none = { 0, 0 };
    if (SUCCEEDED(r->Map(0, &none, &p))) { memcpy(p, data, (size_t)size); r->Unmap(0, NULL); }
    return r;
}

static bool InitD12()
{
    if (g_d12Tried) return g_d12Ok;
    g_d12Tried = true;
    if (!g_dev11_5 || !g_ctx4) { Log("DXR : Direct3D 11.4 / 11.5 absents (barrieres partagees)"); return false; }
    IDXGIAdapter1 *ad = GameAdapter();
    HRESULT hr = D3D12CreateDevice(ad, D3D_FEATURE_LEVEL_12_0, __uuidof(ID3D12Device5), (void **)&g_d12);
    if (ad) ad->Release();
    if (FAILED(hr)) { Log("DXR : device Direct3D 12 impossible (0x%08lx)", hr); return false; }
    D3D12_COMMAND_QUEUE_DESC qd = {}; qd.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
    g_d12->CreateCommandQueue(&qd, __uuidof(ID3D12CommandQueue), (void **)&g_queue);
    for (int i = 0; i < 2; i++) {
        g_d12->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, __uuidof(ID3D12CommandAllocator), (void **)&g_alloc[i]);
        g_d12->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, __uuidof(ID3D12CommandAllocator), (void **)&g_allocL[i]);
    }
    g_d12->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, g_alloc[0], NULL, __uuidof(ID3D12GraphicsCommandList4), (void **)&g_cmd);
    g_cmd->Close();
    // barriere partagee
    hr = g_d12->CreateFence(0, D3D12_FENCE_FLAG_SHARED, __uuidof(ID3D12Fence), (void **)&g_fence12);
    HANDLE fh = NULL;
    if (SUCCEEDED(hr)) hr = g_d12->CreateSharedHandle(g_fence12, NULL, GENERIC_ALL, NULL, &fh);
    if (SUCCEEDED(hr)) hr = g_dev11_5->OpenSharedFence(fh, __uuidof(ID3D11Fence), (void **)&g_fence11);
    if (fh) CloseHandle(fh);
    if (FAILED(hr)) { Log("DXR : barriere partagee impossible (0x%08lx)", hr); return false; }
    g_fenceEvent = CreateEventW(NULL, FALSE, FALSE, NULL);
    // signature : b0 (constantes en racine), t0 (scene), table t1 t2 u0
    D3D12_DESCRIPTOR_RANGE ranges[3] = {};
    ranges[0].RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_SRV; ranges[0].NumDescriptors = 2; ranges[0].BaseShaderRegister = 1; ranges[0].OffsetInDescriptorsFromTableStart = 0;
    ranges[1].RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_UAV; ranges[1].NumDescriptors = 4; ranges[1].BaseShaderRegister = 0; ranges[1].OffsetInDescriptorsFromTableStart = 2;
    ranges[2].RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_SRV; ranges[2].NumDescriptors = kHeapSize - kFirstDyn; ranges[2].BaseShaderRegister = 0; ranges[2].RegisterSpace = 1; ranges[2].OffsetInDescriptorsFromTableStart = kFirstDyn;
    D3D12_ROOT_PARAMETER rp[5] = {};
    rp[0].ParameterType = D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS; rp[0].Constants.ShaderRegister = 0; rp[0].Constants.Num32BitValues = 16;
    rp[1].ParameterType = D3D12_ROOT_PARAMETER_TYPE_SRV; rp[1].Descriptor.ShaderRegister = 0;
    rp[2].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE; rp[2].DescriptorTable.NumDescriptorRanges = 3; rp[2].DescriptorTable.pDescriptorRanges = ranges;
    rp[3].ParameterType = D3D12_ROOT_PARAMETER_TYPE_SRV; rp[3].Descriptor.ShaderRegister = 3;
    rp[4].ParameterType = D3D12_ROOT_PARAMETER_TYPE_SRV; rp[4].Descriptor.ShaderRegister = 4;   // (lampes)
    D3D12_ROOT_SIGNATURE_DESC rsd = {}; rsd.NumParameters = 5; rsd.pParameters = rp;
    ID3DBlob *sig = NULL, *err = NULL;
    hr = D3D12SerializeRootSignature(&rsd, D3D_ROOT_SIGNATURE_VERSION_1, &sig, &err);
    if (FAILED(hr)) { Log("DXR : signature : %s", err ? (const char *)err->GetBufferPointer() : "?"); return false; }
    g_d12->CreateRootSignature(0, sig->GetBufferPointer(), sig->GetBufferSize(), __uuidof(ID3D12RootSignature), (void **)&g_rootSig);
    sig->Release(); if (err) err->Release();
    D3D12_COMPUTE_PIPELINE_STATE_DESC pd = {}; pd.pRootSignature = g_rootSig; pd.CS.pShaderBytecode = g_rtShadow; pd.CS.BytecodeLength = sizeof g_rtShadow;
    hr = g_d12->CreateComputePipelineState(&pd, __uuidof(ID3D12PipelineState), (void **)&g_pso);
    if (FAILED(hr)) { Log("DXR : pipeline des ombres impossible (0x%08lx)", hr); return false; }
    pd.CS.pShaderBytecode = g_rtLamps; pd.CS.BytecodeLength = sizeof g_rtLamps;
    hr = g_d12->CreateComputePipelineState(&pd, __uuidof(ID3D12PipelineState), (void **)&g_psoLamps);
    if (FAILED(hr)) Log("DXR : pipeline des lampes impossible (0x%08lx)", hr);
    for (int i = 0; i < 2; i++) g_lampBuf[i] = Buffer(sizeof g_lamps, D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_FLAG_NONE, D3D12_RESOURCE_STATE_GENERIC_READ);
    D3D12_DESCRIPTOR_HEAP_DESC hd = {}; hd.Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV; hd.NumDescriptors = kHeapSize; hd.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
    g_d12->CreateDescriptorHeap(&hd, __uuidof(ID3D12DescriptorHeap), (void **)&g_heap);
    g_heapInc = g_d12->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);
    g_d12Ok = true;
    Log("DXR : Direct3D 12 pret (file, barriere partagee, pipeline des ombres)");
    return true;
}

static void FreeShared(Shared &s) { Rel(s.rtv); Rel(s.srv); Rel(s.t11); Rel(s.r12); }
// Texture creee par Direct3D 11 (partage par handle NT), ouverte par Direct3D 12 (l'inverse est refuse : 0x80070057).
static bool MakeShared(Shared &s, DXGI_FORMAT f, bool rt11, bool uav12)
{
    FreeShared(s);
    D3D11_TEXTURE2D_DESC td = {};
    td.Width = g_w; td.Height = g_h; td.MipLevels = 1; td.ArraySize = 1; td.Format = f; td.SampleDesc.Count = 1; td.Usage = D3D11_USAGE_DEFAULT;
    td.BindFlags = D3D11_BIND_SHADER_RESOURCE | (rt11 ? D3D11_BIND_RENDER_TARGET : 0) | (uav12 ? D3D11_BIND_UNORDERED_ACCESS : 0);
    td.MiscFlags = D3D11_RESOURCE_MISC_SHARED | D3D11_RESOURCE_MISC_SHARED_NTHANDLE;
    HRESULT hr = g_dev11->CreateTexture2D(&td, NULL, &s.t11);
    if (FAILED(hr)) { Log("texture partagee impossible (format %d, 0x%08lx)", (int)f, hr); return false; }
    IDXGIResource1 *dr = NULL; HANDLE h = NULL;
    hr = s.t11->QueryInterface(__uuidof(IDXGIResource1), (void **)&dr);
    if (SUCCEEDED(hr)) hr = dr->CreateSharedHandle(NULL, DXGI_SHARED_RESOURCE_READ | DXGI_SHARED_RESOURCE_WRITE, NULL, &h);
    if (dr) dr->Release();
    if (SUCCEEDED(hr)) hr = g_d12->OpenSharedHandle(h, __uuidof(ID3D12Resource), (void **)&s.r12);
    if (h) CloseHandle(h);
    if (FAILED(hr)) { Log("texture partagee : ouverture par Direct3D 12 impossible (format %d, 0x%08lx)", (int)f, hr); return false; }
    if (rt11) g_dev11->CreateRenderTargetView(s.t11, NULL, &s.rtv);
    g_dev11->CreateShaderResourceView(s.t11, NULL, &s.srv);
    return true;
}

static bool g_resizeFailed;
struct Hist { ID3D11Texture2D *t; ID3D11RenderTargetView *rtv; ID3D11ShaderResourceView *srv; };
static Hist g_hist[2];
static int g_histCur;
static bool g_histValid;
static float g_lastVP[16], g_lastCam[4];
static bool MakeHist(UINT w, UINT h)
{
    for (Hist &x : g_hist) {
        Rel(x.rtv); Rel(x.srv); Rel(x.t);
        D3D11_TEXTURE2D_DESC td = {}; td.Width = w; td.Height = h; td.MipLevels = 1; td.ArraySize = 1; td.Format = DXGI_FORMAT_R16G16_FLOAT;
        td.SampleDesc.Count = 1; td.Usage = D3D11_USAGE_DEFAULT; td.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
        if (FAILED(g_dev11->CreateTexture2D(&td, NULL, &x.t)) || FAILED(g_dev11->CreateRenderTargetView(x.t, NULL, &x.rtv)) || FAILED(g_dev11->CreateShaderResourceView(x.t, NULL, &x.srv))) return false;
    }
    g_histValid = false;
    return true;
}
// Inverse d'une matrice 4x4 (lignes).
static bool Inverse4(const float *m, float *o)
{
    double a[4][8];
    for (int r = 0; r < 4; r++) for (int c = 0; c < 8; c++) a[r][c] = c < 4 ? m[r * 4 + c] : (c - 4 == r ? 1 : 0);
    for (int c = 0; c < 4; c++) {
        int pv = c; for (int r = c + 1; r < 4; r++) if (fabs(a[r][c]) > fabs(a[pv][c])) pv = r;
        if (fabs(a[pv][c]) < 1e-12) return false;
        if (pv != c) for (int k = 0; k < 8; k++) { double t = a[c][k]; a[c][k] = a[pv][k]; a[pv][k] = t; }
        double d = a[c][c]; for (int k = 0; k < 8; k++) a[c][k] /= d;
        for (int r = 0; r < 4; r++) if (r != c) { double f = a[r][c]; for (int k = 0; k < 8; k++) a[r][k] -= f * a[c][k]; }
    }
    for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) o[r * 4 + c] = (float)a[r][c + 4];
    return true;
}

static bool Resize(UINT w, UINT h)
{
    if (w == g_w && h == g_h && (g_vis.srv || g_resizeFailed)) return g_vis.srv != NULL;
    g_resizeFailed = true;
    g_w = w; g_h = h;
    bool ok = MakeShared(g_pos, DXGI_FORMAT_R32G32B32A32_FLOAT, true, false) && MakeShared(g_nrm, DXGI_FORMAT_R10G10B10A2_UNORM, true, false)
              && MakeShared(g_vis, DXGI_FORMAT_R16_FLOAT, false, true) && MakeShared(g_lampVis, DXGI_FORMAT_R32_UINT, false, true)
              && MakeShared(g_ao, DXGI_FORMAT_R16_FLOAT, false, true) && MakeShared(g_refl, DXGI_FORMAT_R16G16B16A16_FLOAT, false, true);
    if (!ok) return false;
    D3D12_CPU_DESCRIPTOR_HANDLE c = g_heap->GetCPUDescriptorHandleForHeapStart();
    D3D12_SHADER_RESOURCE_VIEW_DESC sv = {}; sv.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D; sv.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING; sv.Texture2D.MipLevels = 1;
    sv.Format = DXGI_FORMAT_R32G32B32A32_FLOAT; g_d12->CreateShaderResourceView(g_pos.r12, &sv, c); c.ptr += g_heapInc;
    sv.Format = DXGI_FORMAT_R10G10B10A2_UNORM; g_d12->CreateShaderResourceView(g_nrm.r12, &sv, c); c.ptr += g_heapInc;
    D3D12_UNORDERED_ACCESS_VIEW_DESC uv = {}; uv.ViewDimension = D3D12_UAV_DIMENSION_TEXTURE2D; uv.Format = DXGI_FORMAT_R16_FLOAT;
    g_d12->CreateUnorderedAccessView(g_vis.r12, NULL, &uv, c); c.ptr += g_heapInc;
    uv.Format = DXGI_FORMAT_R32_UINT;
    g_d12->CreateUnorderedAccessView(g_lampVis.r12, NULL, &uv, c); c.ptr += g_heapInc;
    uv.Format = DXGI_FORMAT_R16_FLOAT;
    g_d12->CreateUnorderedAccessView(g_ao.r12, NULL, &uv, c); c.ptr += g_heapInc;
    uv.Format = DXGI_FORMAT_R16G16B16A16_FLOAT;
    g_d12->CreateUnorderedAccessView(g_refl.r12, NULL, &uv, c);
    if (!MakeHist(w, h)) { Log("historique %ux%u impossible", w, h); return false; }
    g_resizeFailed = false;
    Log("textures partagees %ux%u pretes", w, h);
    return true;
}

// ---------------------------------------------------------------- lots statiques (capture sur la carte)
// Le decor du jeu (terrain, batiments...) est regroupe par Unity en lots statiques a la compilation du jeu : maillages
// combines (sommets deja dans le monde) illisibles par le processeur. Pendant une image de capture (demandee par le mod,
// ombres d'Unity rallumees pour elle), on releve les dessins de la carte d'ombres d'Unity (cible : profondeur seule) dont
// le tampon de sommets a le nombre de sommets d'un lot (RtStaticCounts) : tampons de sommets et d'indices et plages
// dessinees. Ensuite : tampons recopies (lecture par le processeur, une fois), positions (decalage 0, float3) et
// triangles des plages -> maillages de la scene, matrice identite. Ce sont exactement les objets qui projettent une ombre.
typedef void (STDMETHODCALLTYPE *DrawIndexed_t)(ID3D11DeviceContext *, UINT, UINT, INT);
typedef void (STDMETHODCALLTYPE *SetVB_t)(ID3D11DeviceContext *, UINT, UINT, ID3D11Buffer *const *, const UINT *, const UINT *);
typedef void (STDMETHODCALLTYPE *SetIB_t)(ID3D11DeviceContext *, ID3D11Buffer *, DXGI_FORMAT, UINT);
// Le runtime change la table de fonctions du contexte quand on echange son etat (SwapDeviceContextState) : chaque table
// vue est accrochee, avec ses propres fonctions d'origine (retrouvees par la table de l'objet appelant).
struct VtHooks { void **vt; void *drawIdx, *setVB, *setIB, *setRT, *setRTU, *psSRV; };
static VtHooks g_vts[8];
static int g_nvt;
static VtHooks *VtOf(void *c)
{
    void **vt = *(void ***)c;
    for (int i = 0; i < g_nvt; i++) if (g_vts[i].vt == vt) return &g_vts[i];
    return &g_vts[0];
}
#define o_DrawIndexed ((DrawIndexed_t)VtOf(c)->drawIdx)
#define o_SetVB ((SetVB_t)VtOf(c)->setVB)
#define o_SetIB ((SetIB_t)VtOf(c)->setIB)
static ID3D11Buffer *g_curVB, *g_curIB;
static UINT g_curStride, g_curVBOff, g_curIBOff;
static DXGI_FORMAT g_curIBFmt;
static bool g_capturing;
static std::vector<UINT> g_staticCounts;   // nombres de sommets des lots statiques (mod)
struct Range { UINT start, count; INT base; };
struct Batch { ID3D11Buffer *vb, *ib; UINT stride, vbOff, ibOff; DXGI_FORMAT fmt; std::vector<Range> ranges; bool done; int meshId; };
static std::vector<Batch> g_batches;
static std::vector<int> g_staticMeshIds;  // maillages des lots, toujours dans la scene (identite)
static int g_nextStaticId = -1;

static void STDMETHODCALLTYPE H_SetVB(ID3D11DeviceContext *c, UINT slot, UINT n, ID3D11Buffer *const *b, const UINT *st, const UINT *off)
{
    if (slot == 0 && n > 0) { g_curVB = b ? b[0] : NULL; g_curStride = st ? st[0] : 0; g_curVBOff = off ? off[0] : 0; }
    o_SetVB(c, slot, n, b, st, off);
}
static void STDMETHODCALLTYPE H_SetIB(ID3D11DeviceContext *c, ID3D11Buffer *b, DXGI_FORMAT f, UINT off)
{
    g_curIB = b; g_curIBFmt = f; g_curIBOff = off;
    o_SetIB(c, b, f, off);
}
static unsigned g_cntDrawIdx;
static void STDMETHODCALLTYPE H_DrawIndexed(ID3D11DeviceContext *c, UINT count, UINT start, INT base)
{
    g_cntDrawIdx++;
    if (g_capturing && g_curVB && g_curIB && g_curStride >= 12) {
        ID3D11RenderTargetView *rtv = NULL; ID3D11DepthStencilView *dsv = NULL;
        c->OMGetRenderTargets(1, &rtv, &dsv);
        bool shadowPass = !rtv && dsv;
        if (rtv) rtv->Release();
        if (dsv) dsv->Release();
        if (shadowPass) {
            D3D11_BUFFER_DESC bd; g_curVB->GetDesc(&bd);
            UINT nv = (bd.ByteWidth - g_curVBOff) / g_curStride;
            bool isStatic = false;
            for (UINT k : g_staticCounts) if (k == nv) { isStatic = true; break; }
            if (isStatic) {
                Batch *bt = NULL;
                for (Batch &x : g_batches) if (x.vb == g_curVB && x.ib == g_curIB) { bt = &x; break; }
                if (!bt) {
                    Batch nb = {}; nb.vb = g_curVB; nb.ib = g_curIB; nb.stride = g_curStride; nb.vbOff = g_curVBOff; nb.ibOff = g_curIBOff; nb.fmt = g_curIBFmt;
                    g_curVB->AddRef(); g_curIB->AddRef();
                    g_batches.push_back(nb); bt = &g_batches.back();
                }
                bool have = false;
                for (Range &r : bt->ranges) if (r.start == start && r.count == count && r.base == base) { have = true; break; }
                if (!have) { bt->ranges.push_back({ start, count, base }); bt->done = false; }
            }
        }
    }
    o_DrawIndexed(c, count, start, base);
}

static void Patch(void **vt, int i, void *hook, void **orig)
{
    DWORD old;
    VirtualProtect(&vt[i], sizeof(void *), PAGE_EXECUTE_READWRITE, &old);
    if (vt[i] != hook) *orig = vt[i];   // (deja accrochee : l'original connu reste)
    vt[i] = hook;
    VirtualProtect(&vt[i], sizeof(void *), old, &old);
}
// Carte d'ombres a l'echelle de l'ecran d'Unity : le jeu y rassemble les ombres du soleil (passe « collect ») juste
// avant l'eclairage differe. Quand il quitte cette cible (changement de cible suivant), les rayons sont lances et leur
// resultat la remplace : Unity eclaire avec (ambiance, reflets, force de l'ombre : les siens).
typedef void (STDMETHODCALLTYPE *SetRT_t)(ID3D11DeviceContext *, UINT, ID3D11RenderTargetView *const *, ID3D11DepthStencilView *);
#define o_SetRT ((SetRT_t)VtOf(c)->setRT)
typedef void (STDMETHODCALLTYPE *SetRTU_t)(ID3D11DeviceContext *, UINT, ID3D11RenderTargetView *const *, ID3D11DepthStencilView *, UINT, UINT, ID3D11UnorderedAccessView *const *, const UINT *);
static void STDMETHODCALLTYPE H_SetRT(ID3D11DeviceContext *c, UINT n, ID3D11RenderTargetView *const *v, ID3D11DepthStencilView *dsv);
static bool g_viaU;
// (variante avec vues d'acces non ordonne : meme suivi des cibles, sauf « garder les cibles »)
static void STDMETHODCALLTYPE H_SetRTU(ID3D11DeviceContext *c, UINT n, ID3D11RenderTargetView *const *v, ID3D11DepthStencilView *dsv, UINT us, UINT un, ID3D11UnorderedAccessView *const *uv, const UINT *uc);

struct SsView { ID3D11Resource *r; ID3D11RenderTargetView *rtv; };
static SsView g_ssViews[4];
static int g_ssNext;
static bool g_inSS, g_armed, g_inOwn;
static int g_armFlags;
static void InjectMask();
static int g_seqLog;
static unsigned g_cntSetRT, g_cntDraw;
static bool g_prevDepthOnly;
static ID3D11Resource *g_ssCur;
static void STDMETHODCALLTYPE H_SetRT(ID3D11DeviceContext *c, UINT n, ID3D11RenderTargetView *const *v, ID3D11DepthStencilView *dsv)
{
    g_cntSetRT++;
    if (!g_inOwn) {
        ID3D11Resource *r = NULL; D3D11_TEXTURE2D_DESC d = {};
        if (n > 0 && v && v[0]) { v[0]->GetResource(&r); ID3D11Texture2D *t = NULL; if (r && SUCCEEDED(r->QueryInterface(__uuidof(ID3D11Texture2D), (void **)&t))) { t->GetDesc(&d); t->Release(); } }
        // carte d'ombres a l'echelle de l'ecran : juste apres la carte d'ombres du soleil (profondeur seule), cible
        // unique RGBA 8 bits a la taille de l'ecran, sans profondeur
        bool isSS = r && n == 1 && !dsv && g_prevDepthOnly && d.Width == g_inW && d.Height == g_inH
                    && (d.Format == DXGI_FORMAT_R8G8B8A8_TYPELESS || d.Format == DXGI_FORMAT_R8G8B8A8_UNORM);
        if (g_seqLog > 0) {
            g_seqLog--;
            Log("cible : %u vue(s), %p %ux%u format %d, profondeur %s%s", n, (void *)r, d.Width, d.Height, (int)d.Format, dsv ? "oui" : "non", isSS ? " <- carte d'ombres" : "");
        }
        if (g_inSS && !isSS) {
            g_inSS = false;
            if (g_armed) { g_armed = false; g_inOwn = true; InjectMask(); g_inOwn = false; }
        }
        // Lampes : G-buffer (3 ou 4 cibles a la taille de l'ecran : la 1re = couleur des surfaces), puis la cible HDR de
        // l'eclairage (avec profondeur) ; au premier changement vers une autre cible (hors cartes d'ombres), l'eclairage est fini.
        if (n >= 3 && r && d.Width == g_inW && d.Height == g_inH) {
            g_gb0 = r; g_lightPhase = 1;
            if (v[1]) { ID3D11Resource *r1 = NULL; v[1]->GetResource(&r1); g_gb1 = r1; if (r1) r1->Release(); }   // (speculaire, lisse)
        }
        else if (g_lightPhase == 1 && r && n == 1 && dsv && d.Width == g_inW && d.Height == g_inH
                 && (d.Format == DXGI_FORMAT_R16G16B16A16_FLOAT || d.Format == DXGI_FORMAT_R16G16B16A16_TYPELESS)) {
            g_hdrRes = r; Rel(g_hdrRtv); g_hdrRtv = v[0]; g_hdrRtv->AddRef(); g_lightPhase = 2;
            if (g_lampsArmed) { g_inOwn = true; EarlyPrep(); g_inOwn = false; }   // (positions des pixels tant que la profondeur est celle de l'image)
        }
        // (fin de l'eclairage : la passe des lampes est faite a la fin du rendu de la camera, RtPostRender ; les effets d'image
        // viennent apres)
        if (isSS) { g_inSS = true; g_ssCur = r; }
        g_prevDepthOnly = (n == 0 || !v || !v[0]) && dsv;
        if (r) r->Release();
    }
    if (c) o_SetRT(c, n, v, dsv);   // (c nul : appele par H_SetRTU, qui transmet lui-meme)
}
static void STDMETHODCALLTYPE H_SetRTU(ID3D11DeviceContext *c, UINT n, ID3D11RenderTargetView *const *v, ID3D11DepthStencilView *dsv, UINT us, UINT un, ID3D11UnorderedAccessView *const *uv, const UINT *uc)
{
    if (n != D3D11_KEEP_RENDER_TARGETS_AND_DEPTH_STENCIL && !g_inOwn) { g_viaU = true; H_SetRT(NULL, n, v, dsv); g_viaU = false; }
    ((SetRTU_t)VtOf(c)->setRTU)(c, n, v, dsv, us, un, uv, uc);
}


static bool g_hookWanted;
static int g_rehooks;
static void HookTable(void **vt)
{
    if (vt[12] == (void *)H_DrawIndexed && vt[33] == (void *)H_SetRT && vt[34] == (void *)H_SetRTU) return;   // (deja accrochee)
    VtHooks *f = NULL;
    for (int i = 0; i < g_nvt; i++) if (g_vts[i].vt == vt) f = &g_vts[i];
    if (f) {   // entrees remises par le runtime (echange d'etat) : reposees
        if (g_rehooks++ < 3) Log("crochets : table %p remise par le runtime, reposes", (void *)vt);
    } else {
        if (g_nvt >= 8) return;
        f = &g_vts[g_nvt++];
    }
    VtHooks &h = *f;
    h.vt = vt;
    Patch(vt, 12, (void *)H_DrawIndexed, &h.drawIdx);
    Patch(vt, 18, (void *)H_SetVB, &h.setVB);
    Patch(vt, 19, (void *)H_SetIB, &h.setIB);
    Patch(vt, 33, (void *)H_SetRT, &h.setRT);
    Patch(vt, 34, (void *)H_SetRTU, &h.setRTU);
    if (g_rehooks == 0) Log("crochets poses sur la table %d du contexte (%p)", g_nvt, (void *)vt);
}
static void HookContext()
{
    if (!g_ctx1) return;
    g_hookWanted = true;
    HookTable(*(void ***)g_ctx1);
    if (g_ctx0) HookTable(*(void ***)g_ctx0);
}
static void Rehook() { if (g_hookWanted) HookContext(); }

extern "C" __declspec(dllexport) void RtStaticCounts(const int *counts, int n)
{
    g_staticCounts.assign(counts, counts + (n > 0 ? n : 0));
}
extern "C" __declspec(dllexport) void RtCapture(int on)
{
    HookContext();
    g_capturing = on != 0;
}

static bool ReadBuffer(ID3D11Buffer *b, std::vector<unsigned char> &out)
{
    D3D11_BUFFER_DESC d; b->GetDesc(&d);
    D3D11_BUFFER_DESC sd = {}; sd.ByteWidth = d.ByteWidth; sd.Usage = D3D11_USAGE_STAGING; sd.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    ID3D11Buffer *st = NULL;
    if (FAILED(g_dev11->CreateBuffer(&sd, NULL, &st))) return false;
    g_ctx1->CopyResource(st, b);
    D3D11_MAPPED_SUBRESOURCE m;
    bool ok = SUCCEEDED(g_ctx1->Map(st, 0, D3D11_MAP_READ, 0, &m));
    if (ok) { out.assign((unsigned char *)m.pData, (unsigned char *)m.pData + d.ByteWidth); g_ctx1->Unmap(st, 0); }
    st->Release();
    return ok;
}

static int StaticMesh(const float *verts, int nv, const int *idx, int ni);
static void StaticColorOf(int nv, int id);
// Lots releves pendant la capture : lus et ajoutes a la scene (hors de la capture : apres l'image).
static void ProcessBatches()
{
    for (Batch &b : g_batches) {
        if (b.done || b.ranges.empty()) continue;
        b.done = true;
        std::vector<unsigned char> vb, ib;
        if (!ReadBuffer(b.vb, vb) || !ReadBuffer(b.ib, ib)) { Log("capture : lecture d'un lot impossible"); continue; }
        UINT nv = (UINT)((vb.size() - b.vbOff) / b.stride);
        std::vector<float> pos(nv * 3);
        for (UINT i = 0; i < nv; i++) memcpy(&pos[i * 3], vb.data() + b.vbOff + (size_t)i * b.stride, 12);
        bool i16 = b.fmt == DXGI_FORMAT_R16_UINT;
        std::vector<int> tri;
        for (Range &r : b.ranges)
            for (UINT k = 0; k < r.count; k++) {
                size_t o = b.ibOff + (size_t)(r.start + k) * (i16 ? 2 : 4);
                if (o + (i16 ? 2 : 4) > ib.size()) break;
                UINT v = i16 ? *(unsigned short *)(ib.data() + o) : *(unsigned int *)(ib.data() + o);
                tri.push_back((int)v + r.base);
            }
        tri.resize(tri.size() / 3 * 3);
        if (b.meshId) {   // (plages en plus pour un lot deja vu : maillage refait)
            for (size_t i = 0; i < g_staticMeshIds.size(); i++) if (g_staticMeshIds[i] == b.meshId) { g_staticMeshIds.erase(g_staticMeshIds.begin() + i); break; }
        }
        b.meshId = StaticMesh(pos.data(), (int)nv, tri.data(), (int)tri.size());
        if (b.meshId) StaticColorOf((int)nv, b.meshId);
        if (b.meshId) g_staticMeshIds.push_back(b.meshId);
        Log("capture : lot de %u sommets, %u plages, %u triangles -> maillage %d", nv, (unsigned)b.ranges.size(), (unsigned)(tri.size() / 3), b.meshId);
    }
}

static UINT RawView(ID3D12Resource *r, UINT64 bytes)
{
    if (g_nextSlot >= kHeapSize) return 0xFFFFFFFFu;   // (tas plein : pas de vue, le shader s'en passe)
    UINT slot = g_nextSlot++;
    D3D12_SHADER_RESOURCE_VIEW_DESC sv = {}; sv.ViewDimension = D3D12_SRV_DIMENSION_BUFFER; sv.Format = DXGI_FORMAT_R32_TYPELESS;
    sv.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING; sv.Buffer.Flags = D3D12_BUFFER_SRV_FLAG_RAW; sv.Buffer.NumElements = (UINT)((bytes + 3) / 4);
    D3D12_CPU_DESCRIPTOR_HANDLE c = g_heap->GetCPUDescriptorHandleForHeapStart(); c.ptr += (SIZE_T)slot * g_heapInc;
    g_d12->CreateShaderResourceView(r, &sv, c);
    return slot - kFirstDyn;   // (indice dans gBufs)
}

// ---------------------------------------------------------------- maillages et scene (appels du mod, fil principal)
// Maillage : positions (float3, repere de l'objet) et triangles (int). Construit en BLAS a l'image suivante.
extern "C" __declspec(dllexport) int RtMesh(int id, const float *verts, int nv, const int *idx, int ni)
{
    if (!InitD12() || nv <= 0 || ni < 3) return 0;
    auto it = g_meshes.find(id);
    if (it != g_meshes.end()) return 1;
    MeshAs m = {};
    m.nv = nv; m.ni = ni;
    m.vb = Buffer((UINT64)nv * 12, D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_FLAG_NONE, D3D12_RESOURCE_STATE_GENERIC_READ);
    m.ib = Buffer((UINT64)ni * 4, D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_FLAG_NONE, D3D12_RESOURCE_STATE_GENERIC_READ);
    if (!m.vb || !m.ib) { Rel(m.vb); Rel(m.ib); return 0; }
    void *p = NULL; D3D12_RANGE none = { 0, 0 };
    if (SUCCEEDED(m.vb->Map(0, &none, &p))) { memcpy(p, verts, (size_t)nv * 12); m.vb->Unmap(0, NULL); }
    if (SUCCEEDED(m.ib->Map(0, &none, &p))) { memcpy(p, idx, (size_t)ni * 4); m.ib->Unmap(0, NULL); }
    m.vbSlot = RawView(m.vb, (UINT64)nv * 12); m.ibSlot = RawView(m.ib, (UINT64)ni * 4);   // (reflets : normale au point touche)
    m.col[0] = m.col[1] = m.col[2] = 0.6f;
    g_meshes[id] = m;
    g_pendingBuild.push_back(id);
    return 1;
}
// Couleur d'un maillage (matiere x moyenne de sa texture) : reflets hors de l'ecran.
extern "C" __declspec(dllexport) void RtMeshColor(int id, float r, float g, float b)
{
    auto it = g_meshes.find(id);
    if (it != g_meshes.end()) { it->second.col[0] = r; it->second.col[1] = g; it->second.col[2] = b; }
}
// Lots statiques : couleur moyenne de leurs objets, par nombre de sommets du lot (le mod la connait, la DLL releve le lot).
static std::unordered_map<int, std::array<float, 3>> g_staticColor;
extern "C" __declspec(dllexport) void RtStaticColor(int vertexCount, float r, float g, float b) { g_staticColor[vertexCount] = { r, g, b }; }
static void StaticColorOf(int nv, int id) { auto sc = g_staticColor.find(nv); if (sc != g_staticColor.end()) RtMeshColor(id, sc->second[0], sc->second[1], sc->second[2]); }
// Feuillage : maillage a decoupe (uv par sommet, texture d'alpha deja envoyee, seuil).
extern "C" __declspec(dllexport) int RtMeshAlpha(int id, const float *verts, int nv, const int *idx, int ni, const float *uvs, int tex, float cutoff)
{
    if (!RtMesh(id, verts, nv, idx, ni)) return 0;
    MeshAs &m = g_meshes[id];
    if (m.alpha) return 1;
    auto t = g_alphaTex.find(tex);
    if (t == g_alphaTex.end()) return 1;   // (texture absente : reste opaque)
    m.uv = Buffer((UINT64)nv * 8, D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_FLAG_NONE, D3D12_RESOURCE_STATE_GENERIC_READ);
    if (!m.uv) return 1;
    void *p = NULL; D3D12_RANGE none = { 0, 0 };
    if (SUCCEEDED(m.uv->Map(0, &none, &p))) { memcpy(p, uvs, (size_t)nv * 8); m.uv->Unmap(0, NULL); }
    m.uvSlot = RawView(m.uv, (UINT64)nv * 8);   // (indices : vue deja faite par RtMesh)
    m.alpha = true; m.tex = tex; m.cutoff = cutoff;
    return 1;
}
// Texture d'alpha d'un feuillage : un octet par texel, lignes du bas vers le haut (comme les uv d'Unity).
extern "C" __declspec(dllexport) int RtTexture(int id, int w, int h, const unsigned char *alpha)
{
    if (!InitD12() || w <= 0 || h <= 0) return 0;
    if (g_alphaTex.count(id)) return 1;
    AlphaTex t = {}; t.w = w; t.h = h;
    std::vector<unsigned char> pad(((size_t)w * h + 3) & ~(size_t)3, 0);   // (vue brute : par mots de 4 octets)
    memcpy(pad.data(), alpha, (size_t)w * h);
    t.buf = Upload(pad.data(), pad.size());
    if (!t.buf) return 0;
    t.slot = RawView(t.buf, (UINT64)w * h);
    g_alphaTex[id] = t;
    return 1;
}

// Alpha d'une texture du jeu, lue directement (Direct3D 11) : niveau de mip d'au plus maxSize texels de cote, copie
// lisible, alpha decode (DXT1/DXT3/DXT5 = BC1/BC2/BC3, RGBA/BGRA 8 bits, A8). Lignes dans l'ordre de la memoire (celui
// des uv d'Unity). Retour : 1 envoyee, 0 format non pris (texture laissee opaque).
static void DecodeBc3Alpha(const unsigned char *b, unsigned char out[16])
{
    unsigned a[8]; a[0] = b[0]; a[1] = b[1];
    if (a[0] > a[1]) for (int i = 1; i < 7; i++) a[i + 1] = ((7 - i) * a[0] + i * a[1]) / 7;
    else { for (int i = 1; i < 5; i++) a[i + 1] = ((5 - i) * a[0] + i * a[1]) / 5; a[6] = 0; a[7] = 255; }
    unsigned long long bits = 0;
    for (int i = 0; i < 6; i++) bits |= (unsigned long long)b[2 + i] << (8 * i);
    for (int i = 0; i < 16; i++) out[i] = (unsigned char)a[(bits >> (3 * i)) & 7];
}
extern "C" __declspec(dllexport) int RtTextureFrom(int id, void *d3d11tex, int maxSize)
{
    if (!InitD12() || !d3d11tex || !g_ctx1) return 0;
    if (g_alphaTex.count(id)) return 1;
    ID3D11Texture2D *src = NULL;
    if (FAILED(((IUnknown *)d3d11tex)->QueryInterface(__uuidof(ID3D11Texture2D), (void **)&src))) return 0;
    D3D11_TEXTURE2D_DESC d; src->GetDesc(&d);
    UINT mip = 0;
    while (mip + 1 < d.MipLevels && ((d.Width >> mip) > (UINT)maxSize || (d.Height >> mip) > (UINT)maxSize)) mip++;
    UINT w = d.Width >> mip, h = d.Height >> mip; if (!w) w = 1; if (!h) h = 1;
    DXGI_FORMAT f = d.Format;
    int kind = 0;   // 1 BC1, 2 BC2, 3 BC3, 4 rgba/bgra, 5 a8
    switch (f) {
    case DXGI_FORMAT_BC1_UNORM: case DXGI_FORMAT_BC1_UNORM_SRGB: case DXGI_FORMAT_BC1_TYPELESS: kind = 1; break;
    case DXGI_FORMAT_BC2_UNORM: case DXGI_FORMAT_BC2_UNORM_SRGB: case DXGI_FORMAT_BC2_TYPELESS: kind = 2; break;
    case DXGI_FORMAT_BC3_UNORM: case DXGI_FORMAT_BC3_UNORM_SRGB: case DXGI_FORMAT_BC3_TYPELESS: kind = 3; break;
    case DXGI_FORMAT_R8G8B8A8_UNORM: case DXGI_FORMAT_R8G8B8A8_UNORM_SRGB: case DXGI_FORMAT_R8G8B8A8_TYPELESS:
    case DXGI_FORMAT_B8G8R8A8_UNORM: case DXGI_FORMAT_B8G8R8A8_UNORM_SRGB: case DXGI_FORMAT_B8G8R8A8_TYPELESS: kind = 4; break;
    case DXGI_FORMAT_A8_UNORM: kind = 5; break;
    default: break;
    }
    if (!kind) { Log("texture %d : format %d non pris (opaque)", id, (int)f); src->Release(); return 0; }
    D3D11_TEXTURE2D_DESC sd = d; sd.Width = w; sd.Height = h; sd.MipLevels = 1; sd.ArraySize = 1; sd.Usage = D3D11_USAGE_STAGING;
    sd.BindFlags = 0; sd.CPUAccessFlags = D3D11_CPU_ACCESS_READ; sd.MiscFlags = 0;
    ID3D11Texture2D *st = NULL;
    if (FAILED(g_dev11->CreateTexture2D(&sd, NULL, &st))) { Log("texture %d : copie lisible impossible (%ux%u format %d)", id, w, h, (int)f); src->Release(); return 0; }
    g_ctx1->CopySubresourceRegion(st, 0, 0, 0, 0, src, D3D11CalcSubresource(mip, 0, d.MipLevels), NULL);
    src->Release();
    D3D11_MAPPED_SUBRESOURCE m;
    if (FAILED(g_ctx1->Map(st, 0, D3D11_MAP_READ, 0, &m))) { st->Release(); return 0; }
    std::vector<unsigned char> al((size_t)w * h);
    const unsigned char *base = (const unsigned char *)m.pData;
    if (kind <= 3) {
        UINT bw = (w + 3) / 4, bh = (h + 3) / 4, bs = kind == 1 ? 8 : 16;
        for (UINT by = 0; by < bh; by++)
            for (UINT bx = 0; bx < bw; bx++) {
                const unsigned char *b = base + (size_t)by * m.RowPitch + (size_t)bx * bs;
                unsigned char a[16];
                if (kind == 1) {
                    unsigned c0 = b[0] | (b[1] << 8), c1 = b[2] | (b[3] << 8);
                    unsigned idx = b[4] | (b[5] << 8) | (b[6] << 16) | ((unsigned)b[7] << 24);
                    for (int i = 0; i < 16; i++) a[i] = (c0 <= c1 && ((idx >> (2 * i)) & 3) == 3) ? 0 : 255;
                } else if (kind == 2) {
                    for (int i = 0; i < 16; i++) a[i] = (unsigned char)(((b[i / 2] >> (4 * (i & 1))) & 15) * 17);
                } else DecodeBc3Alpha(b, a);
                for (int i = 0; i < 16; i++) {
                    UINT x = bx * 4 + (i & 3), y = by * 4 + (i >> 2);
                    if (x < w && y < h) al[(size_t)y * w + x] = a[i];
                }
            }
    } else {
        for (UINT y = 0; y < h; y++) {
            const unsigned char *row = base + (size_t)y * m.RowPitch;
            for (UINT x = 0; x < w; x++) al[(size_t)y * w + x] = kind == 4 ? row[x * 4 + 3] : row[x];
        }
    }
    g_ctx1->Unmap(st, 0);
    st->Release();
    // (sans mips : reduite au plus proche)
    UINT ow = w, oh = h;
    while (ow > (UINT)maxSize * 2 || oh > (UINT)maxSize * 2) { ow = (ow + 1) / 2; oh = (oh + 1) / 2; }
    if (ow != w || oh != h) {
        std::vector<unsigned char> r((size_t)ow * oh);
        for (UINT y = 0; y < oh; y++) for (UINT x = 0; x < ow; x++) r[(size_t)y * ow + x] = al[(size_t)(y * h / oh) * w + x * w / ow];
        al.swap(r); w = ow; h = oh;
    }
    size_t solid = 0; for (unsigned char v : al) if (v > 127) solid++;
    Log("texture %d : %ux%u (mip %u, format %d) %u %% plein", id, w, h, mip, (int)f, (unsigned)(solid * 100 / al.size()));
    if (g_logPath[0]) {   // (essai : alpha decode, a cote du journal)
        wchar_t p[MAX_PATH]; swprintf_s(p, L"%s.alpha%d.pgm", g_logPath, id);
        FILE *fo = _wfopen(p, L"wb");
        if (fo) { fprintf(fo, "P5 %u %u 255%c", w, h, 10); fwrite(al.data(), 1, al.size(), fo); fclose(fo); }
    }
    return RtTexture(id, (int)w, (int)h, al.data());
}

// Maillage anime (personnages) : nouvelles positions (memes sommets, memes triangles), BLAS refait a l'image suivante
// dans la meme memoire (l'image precedente est terminee : on l'attend a chaque image).
extern "C" __declspec(dllexport) int RtMeshUpdate(int id, const float *verts, int nv)
{
    auto it = g_meshes.find(id);
    if (it == g_meshes.end() || (UINT)nv != it->second.nv) return 0;
    MeshAs &m = it->second;
    void *p = NULL; D3D12_RANGE none = { 0, 0 };
    if (FAILED(m.vb->Map(0, &none, &p))) return 0;
    memcpy(p, verts, (size_t)nv * 12); m.vb->Unmap(0, NULL);
    if (m.built) { m.built = false; g_pendingBuild.push_back(id); }
    return 1;
}

// Couleur moyenne d'une texture (dernier niveau de mip : DXT1/3/5 -> moyenne des deux couleurs de bout ; RGBA 8 bits).
static float C565(unsigned v, int k) { return k == 0 ? ((v >> 11) & 31) / 31.f : k == 1 ? ((v >> 5) & 63) / 63.f : (v & 31) / 31.f; }
extern "C" __declspec(dllexport) int RtTexAverage(void *d3d11tex, float *rgb)
{
    if (!d3d11tex || !g_ctx1) return 0;
    ID3D11Texture2D *src = NULL;
    if (FAILED(((IUnknown *)d3d11tex)->QueryInterface(__uuidof(ID3D11Texture2D), (void **)&src))) return 0;
    D3D11_TEXTURE2D_DESC d; src->GetDesc(&d);
    UINT mip = d.MipLevels - 1, w = max(d.Width >> mip, 1u), h = max(d.Height >> mip, 1u);
    int kind = 0;
    switch (d.Format) {
    case DXGI_FORMAT_BC1_UNORM: case DXGI_FORMAT_BC1_UNORM_SRGB: case DXGI_FORMAT_BC1_TYPELESS: kind = 1; break;
    case DXGI_FORMAT_BC2_UNORM: case DXGI_FORMAT_BC2_UNORM_SRGB: case DXGI_FORMAT_BC2_TYPELESS:
    case DXGI_FORMAT_BC3_UNORM: case DXGI_FORMAT_BC3_UNORM_SRGB: case DXGI_FORMAT_BC3_TYPELESS: kind = 2; break;
    case DXGI_FORMAT_R8G8B8A8_UNORM: case DXGI_FORMAT_R8G8B8A8_UNORM_SRGB: case DXGI_FORMAT_R8G8B8A8_TYPELESS: kind = 3; break;
    case DXGI_FORMAT_B8G8R8A8_UNORM: case DXGI_FORMAT_B8G8R8A8_UNORM_SRGB: case DXGI_FORMAT_B8G8R8A8_TYPELESS: kind = 4; break;
    default: break;
    }
    if (!kind) { src->Release(); return 0; }
    D3D11_TEXTURE2D_DESC sd = d; sd.Width = w; sd.Height = h; sd.MipLevels = 1; sd.ArraySize = 1; sd.Usage = D3D11_USAGE_STAGING;
    sd.BindFlags = 0; sd.CPUAccessFlags = D3D11_CPU_ACCESS_READ; sd.MiscFlags = 0;
    ID3D11Texture2D *st = NULL;
    if (FAILED(g_dev11->CreateTexture2D(&sd, NULL, &st))) { src->Release(); return 0; }
    g_ctx1->CopySubresourceRegion(st, 0, 0, 0, 0, src, D3D11CalcSubresource(mip, 0, d.MipLevels), NULL);
    src->Release();
    D3D11_MAPPED_SUBRESOURCE m;
    int ok = 0;
    if (SUCCEEDED(g_ctx1->Map(st, 0, D3D11_MAP_READ, 0, &m))) {
        const unsigned char *b = (const unsigned char *)m.pData;
        if (kind <= 2) {
            const unsigned char *c = b + (kind == 2 ? 8 : 0);
            unsigned c0 = c[0] | (c[1] << 8), c1 = c[2] | (c[3] << 8);
            for (int k = 0; k < 3; k++) rgb[k] = (C565(c0, k) + C565(c1, k)) * 0.5f;
        } else {
            double s[3] = {}; UINT n = 0;
            for (UINT y = 0; y < h; y++) for (UINT x = 0; x < w; x++) {
                const unsigned char *p = b + (size_t)y * m.RowPitch + x * 4;
                for (int k = 0; k < 3; k++) s[k] += p[kind == 4 ? 2 - k : k] / 255.0;
                n++;
            }
            for (int k = 0; k < 3; k++) rgb[k] = n ? (float)(s[k] / n) : 0.6f;
        }
        g_ctx1->Unmap(st, 0);
        ok = 1;
    }
    st->Release();
    return ok;
}

static int StaticMesh(const float *verts, int nv, const int *idx, int ni)
{
    int id = g_nextStaticId--;
    return RtMesh(id, verts, nv, idx, ni) ? id : 0;
}
extern "C" __declspec(dllexport) void RtForgetMesh(int id)
{
    auto it = g_meshes.find(id);
    if (it == g_meshes.end()) return;
    int a = g_frameNo & 1;
    g_trash[a].push_back(it->second.vb); g_trash[a].push_back(it->second.ib); if (it->second.blas) g_trash[a].push_back(it->second.blas); if (it->second.uv) g_trash[a].push_back(it->second.uv);
    g_meshes.erase(it);
}
// Instances de l'image : par instance 13 nombres (maillage, puis matrice objet -> monde 3x4 par lignes).
extern "C" __declspec(dllexport) void RtInstances(const float *data, int n)
{
    g_inst.resize(n > 0 ? n : 0);
    for (int i = 0; i < n; i++) { g_inst[i].mesh = (int)data[i * 13]; memcpy(g_inst[i].m, data + i * 13 + 1, 48); }
}
extern "C" __declspec(dllexport) void RtFrame(const float *p)
{
    memcpy(&g_frame, p, 36 * sizeof(float));   // (le mod envoie 36 nombres ; la suite est remplie ici)
}

static void BuildPending()
{
    std::vector<D3D12_RAYTRACING_GEOMETRY_DESC> geos;
    int built = 0;
    for (int id : g_pendingBuild) {
        auto it = g_meshes.find(id);
        if (it == g_meshes.end() || it->second.built) continue;
        MeshAs &m = it->second;
        D3D12_RAYTRACING_GEOMETRY_DESC g = {};
        g.Type = D3D12_RAYTRACING_GEOMETRY_TYPE_TRIANGLES; g.Flags = m.alpha ? D3D12_RAYTRACING_GEOMETRY_FLAG_NONE : D3D12_RAYTRACING_GEOMETRY_FLAG_OPAQUE;
        g.Triangles.VertexBuffer.StartAddress = m.vb->GetGPUVirtualAddress(); g.Triangles.VertexBuffer.StrideInBytes = 12;
        g.Triangles.VertexFormat = DXGI_FORMAT_R32G32B32_FLOAT; g.Triangles.VertexCount = m.nv;
        g.Triangles.IndexBuffer = m.ib->GetGPUVirtualAddress(); g.Triangles.IndexFormat = DXGI_FORMAT_R32_UINT; g.Triangles.IndexCount = m.ni;
        D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_INPUTS in = {};
        in.Type = D3D12_RAYTRACING_ACCELERATION_STRUCTURE_TYPE_BOTTOM_LEVEL; in.Flags = D3D12_RAYTRACING_ACCELERATION_STRUCTURE_BUILD_FLAG_PREFER_FAST_TRACE;
        in.NumDescs = 1; in.DescsLayout = D3D12_ELEMENTS_LAYOUT_ARRAY; in.pGeometryDescs = &g;
        D3D12_RAYTRACING_ACCELERATION_STRUCTURE_PREBUILD_INFO pi = {};
        g_d12->GetRaytracingAccelerationStructurePrebuildInfo(&in, &pi);
        if (!m.blas) m.blas = Buffer(pi.ResultDataMaxSizeInBytes, D3D12_HEAP_TYPE_DEFAULT, D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS, D3D12_RESOURCE_STATE_RAYTRACING_ACCELERATION_STRUCTURE);   // (maillage anime : meme memoire)
        if (!m.blas) continue;
        if (pi.ScratchDataSizeInBytes > g_blasScratchSize) {
            if (g_blasScratch) g_trash[g_frameNo & 1].push_back(g_blasScratch);
            g_blasScratchSize = pi.ScratchDataSizeInBytes;
            g_blasScratch = Buffer(g_blasScratchSize, D3D12_HEAP_TYPE_DEFAULT, D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        }
        D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC bd = {};
        bd.Inputs = in; bd.DestAccelerationStructureData = m.blas->GetGPUVirtualAddress(); bd.ScratchAccelerationStructureData = g_blasScratch->GetGPUVirtualAddress();
        g_cmd->BuildRaytracingAccelerationStructure(&bd, 0, NULL);
        D3D12_RESOURCE_BARRIER ub = {}; ub.Type = D3D12_RESOURCE_BARRIER_TYPE_UAV; ub.UAV.pResource = g_blasScratch;
        g_cmd->ResourceBarrier(1, &ub);   // (meme brouillon pour le suivant)
        m.built = true;
        built++;
    }
    g_pendingBuild.clear();
    if (built) { D3D12_RESOURCE_BARRIER ub = {}; ub.Type = D3D12_RESOURCE_BARRIER_TYPE_UAV; ub.UAV.pResource = NULL; g_cmd->ResourceBarrier(1, &ub); }   // (BLAS finis avant la scene)
}

static bool BuildTlas()
{
    std::vector<D3D12_RAYTRACING_INSTANCE_DESC> descs;
    descs.reserve(g_inst.size());
    struct InstInfo { UINT ib, uv, tex, texW, texH; float cutoff; UINT vb, pad0; float cr, cg, cb, pad1; };
    std::vector<InstInfo> infos;
    infos.reserve(g_inst.size() + g_staticMeshIds.size());
    for (const Inst &i : g_inst) {
        auto it = g_meshes.find(i.mesh);
        if (it == g_meshes.end() || !it->second.built) continue;
        D3D12_RAYTRACING_INSTANCE_DESC d = {};
        memcpy(d.Transform, i.m, 48);
        d.InstanceMask = 0xFF; d.Flags = D3D12_RAYTRACING_INSTANCE_FLAG_TRIANGLE_CULL_DISABLE | D3D12_RAYTRACING_INSTANCE_FLAG_FORCE_OPAQUE;
        const MeshAs &mm = it->second;
        InstInfo inf = { mm.ibSlot, 0, 0, 1, 1, 2.f, mm.vbSlot, 0, mm.col[0], mm.col[1], mm.col[2], 0 };
        if (mm.alpha) {
            auto t = g_alphaTex.find(mm.tex);
            if (t != g_alphaTex.end()) {
                d.Flags = D3D12_RAYTRACING_INSTANCE_FLAG_TRIANGLE_CULL_DISABLE | D3D12_RAYTRACING_INSTANCE_FLAG_FORCE_NON_OPAQUE;
                inf.uv = mm.uvSlot; inf.tex = t->second.slot; inf.texW = t->second.w; inf.texH = t->second.h; inf.cutoff = mm.cutoff;
            }
        }
        d.InstanceID = (UINT)infos.size();
        infos.push_back(inf);
        d.AccelerationStructure = it->second.blas->GetGPUVirtualAddress();
        descs.push_back(d);
    }
    for (int sid : g_staticMeshIds) {
        auto it = g_meshes.find(sid);
        if (it == g_meshes.end() || !it->second.built) continue;
        D3D12_RAYTRACING_INSTANCE_DESC d = {};
        d.Transform[0][0] = d.Transform[1][1] = d.Transform[2][2] = 1.f;
        d.InstanceMask = 0xFF; d.Flags = D3D12_RAYTRACING_INSTANCE_FLAG_TRIANGLE_CULL_DISABLE | D3D12_RAYTRACING_INSTANCE_FLAG_FORCE_OPAQUE;
        const MeshAs &sm = it->second;
        d.InstanceID = (UINT)infos.size();
        infos.push_back({ sm.ibSlot, 0, 0, 1, 1, 2.f, sm.vbSlot, 0, sm.col[0], sm.col[1], sm.col[2], 0 });
        d.AccelerationStructure = it->second.blas->GetGPUVirtualAddress();
        descs.push_back(d);
    }
    if (descs.empty()) return false;
    if (infos.empty()) infos.push_back({});
    {   // informations par instance (feuillages), un tampon par moitie de l'anneau d'allocateurs
        int a = g_frameNo & 1;
        UINT64 ib = infos.size() * sizeof(InstInfo);
        if (ib > g_instInfoCap[a]) { if (g_instInfo[a]) g_trash[a].push_back(g_instInfo[a]); g_instInfoCap[a] = ib * 2; g_instInfo[a] = Buffer(g_instInfoCap[a], D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_FLAG_NONE, D3D12_RESOURCE_STATE_GENERIC_READ); }
        void *q = NULL; D3D12_RANGE nr = { 0, 0 };
        if (!g_instInfo[a] || FAILED(g_instInfo[a]->Map(0, &nr, &q))) return false;
        memcpy(q, infos.data(), (size_t)ib); g_instInfo[a]->Unmap(0, NULL);
    }
    UINT64 bytes = descs.size() * sizeof(D3D12_RAYTRACING_INSTANCE_DESC);
    if (bytes > g_instCap) { if (g_instBuf) g_trash[g_frameNo & 1].push_back(g_instBuf); g_instCap = bytes * 2; g_instBuf = Buffer(g_instCap, D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_FLAG_NONE, D3D12_RESOURCE_STATE_GENERIC_READ); }
    void *p = NULL; D3D12_RANGE none = { 0, 0 };
    if (!g_instBuf || FAILED(g_instBuf->Map(0, &none, &p))) return false;
    memcpy(p, descs.data(), (size_t)bytes); g_instBuf->Unmap(0, NULL);
    D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_INPUTS in = {};
    in.Type = D3D12_RAYTRACING_ACCELERATION_STRUCTURE_TYPE_TOP_LEVEL; in.Flags = D3D12_RAYTRACING_ACCELERATION_STRUCTURE_BUILD_FLAG_PREFER_FAST_BUILD;
    in.NumDescs = (UINT)descs.size(); in.DescsLayout = D3D12_ELEMENTS_LAYOUT_ARRAY; in.InstanceDescs = g_instBuf->GetGPUVirtualAddress();
    D3D12_RAYTRACING_ACCELERATION_STRUCTURE_PREBUILD_INFO pi = {};
    g_d12->GetRaytracingAccelerationStructurePrebuildInfo(&in, &pi);
    if (pi.ResultDataMaxSizeInBytes > g_tlasSize) {
        if (g_tlas) g_trash[g_frameNo & 1].push_back(g_tlas);
        g_tlasSize = pi.ResultDataMaxSizeInBytes * 2;
        g_tlas = Buffer(g_tlasSize, D3D12_HEAP_TYPE_DEFAULT, D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS, D3D12_RESOURCE_STATE_RAYTRACING_ACCELERATION_STRUCTURE);
    }
    if (pi.ScratchDataSizeInBytes > g_tlasScratchSize) {
        if (g_tlasScratch) g_trash[g_frameNo & 1].push_back(g_tlasScratch);
        g_tlasScratchSize = pi.ScratchDataSizeInBytes * 2;
        g_tlasScratch = Buffer(g_tlasScratchSize, D3D12_HEAP_TYPE_DEFAULT, D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
    }
    if (!g_tlas || !g_tlasScratch) return false;
    D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC bd = {};
    bd.Inputs = in; bd.DestAccelerationStructureData = g_tlas->GetGPUVirtualAddress(); bd.ScratchAccelerationStructureData = g_tlasScratch->GetGPUVirtualAddress();
    g_cmd->BuildRaytracingAccelerationStructure(&bd, 0, NULL);
    D3D12_RESOURCE_BARRIER ub = {}; ub.Type = D3D12_RESOURCE_BARRIER_TYPE_UAV; ub.UAV.pResource = g_tlas;
    g_cmd->ResourceBarrier(1, &ub);
    return true;
}

// ---------------------------------------------------------------- l'image
static int g_lastInst, g_lastMeshes;
static bool g_ctrl;   // controle : rayons depuis la camera (mode 6)
static DWORD g_nextStat;

// Rayons de l'image (Direct3D 12) entre deux barrieres : attend `wait`, signale le retour.
static UINT64 TraceShadows(UINT64 wait, bool lamps = false)
{
    int a = g_frameNo & 1;
    UINT64 *af = lamps ? g_allocFenceL : g_allocFence;
    ID3D12CommandAllocator *al = lamps ? g_allocL[a] : g_alloc[a];
    if (af[a] && g_fence12->GetCompletedValue() < af[a]) {
        g_fence12->SetEventOnCompletion(af[a], g_fenceEvent);
        WaitForSingleObject(g_fenceEvent, 2000);
    }
    bool reuse = lamps && g_tlasFrame == g_frameNo && g_tlas;   // (scene du soleil de cette image)
    if (!reuse) {
        // (ressources a liberer : quand les deux reserves de cette moitie de l'anneau sont revenues)
        if (!(g_allocFenceL[a] && g_fence12->GetCompletedValue() < g_allocFenceL[a]) && !(g_allocFence[a] && g_fence12->GetCompletedValue() < g_allocFence[a])) {
            for (ID3D12Resource *r : g_trash[a]) r->Release();
            g_trash[a].clear();
        }
    }
    al->Reset();
    g_cmd->Reset(al, NULL);
    bool scene = true;
    if (!reuse) { BuildPending(); scene = BuildTlas(); if (scene) g_tlasFrame = g_frameNo; }
    if (scene) {
        g_cmd->SetComputeRootSignature(g_rootSig);
        ID3D12DescriptorHeap *hs[] = { g_heap };
        g_cmd->SetDescriptorHeaps(1, hs);
        struct { float sun[4]; UINT size[4]; float params[4]; float cam[4]; } c;
        c.sun[0] = g_frame.sunDir[0]; c.sun[1] = g_frame.sunDir[1]; c.sun[2] = g_frame.sunDir[2]; c.sun[3] = g_frame.misc[3];
        c.size[0] = g_w; c.size[1] = g_h; c.size[2] = g_frameNo; c.size[3] = (UINT)(g_frame.misc[2] > 0 ? g_frame.misc[2] : 1);
        c.params[0] = g_frame.misc[1] > 0 ? g_frame.misc[1] : 300.f; c.params[1] = 0.03f; c.params[2] = 0.02f; c.params[3] = g_ctrl ? 1.f : 0.f;
        memcpy(c.cam, g_frame.cam, 16);
        if (lamps) { c.sun[3] = (float)g_nLamps; c.params[0] = g_aoRadius; c.params[1] = (float)g_aoRays; c.params[2] = g_aoStrength > 0 ? 1.f : 0.f; c.params[3] = g_lampStrength < -2.5f ? 2.f : 0.f; c.size[3] = g_reflStrength > 0 ? 1u : 0u; }
        g_cmd->SetComputeRoot32BitConstants(0, 16, &c, 0);
        g_cmd->SetComputeRootShaderResourceView(1, g_tlas->GetGPUVirtualAddress());
        g_cmd->SetComputeRootDescriptorTable(2, g_heap->GetGPUDescriptorHandleForHeapStart());
        g_cmd->SetComputeRootShaderResourceView(3, g_instInfo[g_frameNo & 1]->GetGPUVirtualAddress());
        g_cmd->SetComputeRootShaderResourceView(4, g_lampBuf[g_frameNo & 1]->GetGPUVirtualAddress());
        g_cmd->SetPipelineState(lamps ? g_psoLamps : g_pso);
        // (textures partagees : etat COMMON entre les deux devices ; sortie en UAV le temps des rayons)
        D3D12_RESOURCE_BARRIER tb[3] = {};
        for (int k = 0; k < 3; k++) {
            tb[k].Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION; tb[k].Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
            tb[k].Transition.StateBefore = D3D12_RESOURCE_STATE_COMMON; tb[k].Transition.StateAfter = D3D12_RESOURCE_STATE_UNORDERED_ACCESS;
        }
        tb[0].Transition.pResource = lamps ? g_lampVis.r12 : g_vis.r12; tb[1].Transition.pResource = g_ao.r12; tb[2].Transition.pResource = g_refl.r12;
        UINT nb = lamps ? 3 : 1;
        g_cmd->ResourceBarrier(nb, tb);
        g_cmd->Dispatch((g_w + 7) / 8, (g_h + 7) / 8, 1);
        for (int k = 0; k < 3; k++) { tb[k].Transition.StateBefore = D3D12_RESOURCE_STATE_UNORDERED_ACCESS; tb[k].Transition.StateAfter = D3D12_RESOURCE_STATE_COMMON; }
        g_cmd->ResourceBarrier(nb, tb);
    }
    g_cmd->Close();
    g_queue->Wait(g_fence12, wait);
    ID3D12CommandList *lists[] = { g_cmd };
    g_queue->ExecuteCommandLists(1, lists);
    UINT64 done = ++g_fenceVal;
    g_queue->Signal(g_fence12, done);
    af[a] = done;
    if (GetTickCount() >= g_nextStat) {
        g_nextStat = GetTickCount() + 10000;
        Log("image %u : %d maillages, %d instances (%s)", g_frameNo, (int)g_meshes.size(), (int)g_inst.size(), scene ? "scene construite" : "SCENE VIDE");
    }
    return scene ? done : 0;
}

// Apres la scene (OnPostRender de la camera principale, jeu en -force-gfx-direct). mode : 0 ombres, 1 normales,
// 2 profondeur, 3 positions, 4 visibilite, 5 essai (moitie gauche assombrie).
// Controle (mode 12) : orientation du contenu de la cible face au G-buffer. Moyenne de luminance du quart haut et du
// quart bas des lignes de la cible, et part de ciel (profondeur au loin) dans le quart haut / bas de la profondeur.
static float HalfToFloat(unsigned short h)
{
    unsigned s = (h >> 15) & 1, e = (h >> 10) & 31, m = h & 1023;
    float v = e == 0 ? m / 1024.f / 16384.f : (e == 31 ? 65504.f : (1 + m / 1024.f) * powf(2.f, (float)e - 15));
    return s ? -v : v;
}
static void DumpTex(ID3D11Texture2D *t, const wchar_t *suffix)
{
    D3D11_TEXTURE2D_DESC d; t->GetDesc(&d);
    d.Usage = D3D11_USAGE_STAGING; d.BindFlags = 0; d.CPUAccessFlags = D3D11_CPU_ACCESS_READ; d.MiscFlags = 0;
    ID3D11Texture2D *st = NULL;
    if (FAILED(g_dev11->CreateTexture2D(&d, NULL, &st))) return;
    g_ctx1->CopyResource(st, t);
    D3D11_MAPPED_SUBRESOURCE m;
    if (SUCCEEDED(g_ctx1->Map(st, 0, D3D11_MAP_READ, 0, &m))) {
        wchar_t p[MAX_PATH]; swprintf_s(p, L"%s.%s.pgm", g_logPath, suffix);
        FILE *f = _wfopen(p, L"wb");
        if (f) {
            fprintf(f, "P5 %u %u 255%c", d.Width, d.Height, 10);
            for (UINT y = 0; y < d.Height; y++) for (UINT x = 0; x < d.Width; x++) {
                const char *row = (const char *)m.pData + (size_t)y * m.RowPitch;
                float v = 0;
                if (d.Format == DXGI_FORMAT_R32G32B32A32_FLOAT) v = ((const float *)row)[x * 4 + 3];       // pos.w
                else if (d.Format == DXGI_FORMAT_R16_FLOAT) v = HalfToFloat(((const unsigned short *)row)[x]);
                else if (d.Format == DXGI_FORMAT_R8G8B8A8_TYPELESS || d.Format == DXGI_FORMAT_R8G8B8A8_UNORM)
                    v = ((const unsigned char *)row)[x * 4] / 255.f;
                fputc((int)(v > 1 ? 255 : v < 0 ? 0 : v * 255), f);
            }
            fclose(f);
        }
        g_ctx1->Unmap(st, 0);
    }
    st->Release();
}
static void ProbeOrientation(ID3D11RenderTargetView *rtv)
{
    ID3D11Resource *tr = NULL, *dr = NULL; rtv->GetResource(&tr); g_srvDepth->GetResource(&dr);
    ID3D11Texture2D *tt = NULL, *dt = NULL;
    tr->QueryInterface(__uuidof(ID3D11Texture2D), (void **)&tt); dr->QueryInterface(__uuidof(ID3D11Texture2D), (void **)&dt);
    tr->Release(); dr->Release();
    if (!tt || !dt) { Rel(tt); Rel(dt); return; }
    D3D11_TEXTURE2D_DESC a, b; tt->GetDesc(&a); dt->GetDesc(&b);
    a.Usage = D3D11_USAGE_STAGING; a.BindFlags = 0; a.CPUAccessFlags = D3D11_CPU_ACCESS_READ; a.MiscFlags = 0;
    b.Usage = D3D11_USAGE_STAGING; b.BindFlags = 0; b.CPUAccessFlags = D3D11_CPU_ACCESS_READ; b.MiscFlags = 0;
    ID3D11Texture2D *sa = NULL, *sb = NULL;
    g_dev11->CreateTexture2D(&a, NULL, &sa); g_dev11->CreateTexture2D(&b, NULL, &sb);
    if (sa && sb && a.Width == b.Width && a.Height == b.Height && (a.Format == DXGI_FORMAT_R16G16B16A16_FLOAT || a.Format == DXGI_FORMAT_R16G16B16A16_TYPELESS)) {
        g_ctx1->CopyResource(sa, tt); g_ctx1->CopyResource(sb, dt);
        D3D11_MAPPED_SUBRESOURCE ma, mb;
        if (SUCCEEDED(g_ctx1->Map(sa, 0, D3D11_MAP_READ, 0, &ma))) {
            if (SUCCEEDED(g_ctx1->Map(sb, 0, D3D11_MAP_READ, 0, &mb))) {
                // pixels de ciel de la profondeur : luminance de la cible a la meme ligne (s) et a la ligne retournee (r)
                double s1 = 0, s2 = 0, r1 = 0, r2 = 0; int n = 0;
                for (UINT y = 0; y < b.Height; y += 2) for (UINT x = 0; x < b.Width; x += 2) {
                    UINT dv = *((const UINT *)((const char *)mb.pData + (size_t)y * mb.RowPitch) + x) & 0xFFFFFF;
                    if (dv < 0xFFFFF0) continue;
                    const unsigned short *t = (const unsigned short *)((const char *)ma.pData + (size_t)y * ma.RowPitch) + x * 4;
                    const unsigned short *u = (const unsigned short *)((const char *)ma.pData + (size_t)(a.Height - 1 - y) * ma.RowPitch) + x * 4;
                    double ls = (HalfToFloat(t[0]) + HalfToFloat(t[1]) + HalfToFloat(t[2])) / 3, lr = (HalfToFloat(u[0]) + HalfToFloat(u[1]) + HalfToFloat(u[2])) / 3;
                    s1 += ls; s2 += ls * ls; r1 += lr; r2 += lr * lr; n++;
                }
                if (n) { s1 /= n; r1 /= n; }
                Log("orientation : %d pixels de ciel ; cible meme ligne moyenne %.3f ecart %.3f ; ligne retournee moyenne %.3f ecart %.3f",
                    n, s1, n ? sqrt(s2 / n - s1 * s1) : 0, r1, n ? sqrt(r2 / n - r1 * r1) : 0);
                {   // images brutes a cote du journal : cible (luminance) et profondeur (ciel blanc), lignes dans l'ordre de la memoire
                    wchar_t p1[MAX_PATH], p2[MAX_PATH];
                    swprintf_s(p1, L"%s.cible.pgm", g_logPath); swprintf_s(p2, L"%s.profondeur.pgm", g_logPath);
                    FILE *f1 = _wfopen(p1, L"wb"), *f2 = _wfopen(p2, L"wb");
                    if (f1 && f2) {
                        fprintf(f1, "P5 %u %u 255\n", a.Width, a.Height); fprintf(f2, "P5 %u %u 255\n", b.Width, b.Height);
                        for (UINT y = 0; y < a.Height; y++) for (UINT x = 0; x < a.Width; x++) {
                            const unsigned short *t = (const unsigned short *)((const char *)ma.pData + (size_t)y * ma.RowPitch) + x * 4;
                            float l = (HalfToFloat(t[0]) + HalfToFloat(t[1]) + HalfToFloat(t[2])) / 3;
                            fputc((int)(l > 1 ? 255 : l < 0 ? 0 : l * 255), f1);
                            UINT dv = *((const UINT *)((const char *)mb.pData + (size_t)y * mb.RowPitch) + x) & 0xFFFFFF;
                            fputc((int)(dv >> 16), f2);
                        }
                    }
                    if (f1) fclose(f1);
                    if (f2) fclose(f2);
                }
                g_ctx1->Unmap(sb, 0);
            }
            g_ctx1->Unmap(sa, 0);
        }
    } else Log("orientation : textures differentes (cible %ux%u format %d, profondeur %ux%u)", a.Width, a.Height, (int)a.Format, b.Width, b.Height);
    Rel(sa); Rel(sb); tt->Release(); dt->Release();
}

// Preparation (positions, normales -> textures partagees), rayons (Direct3D 12), attente sur la carte. Etat du
// contexte : le notre (vue, shaders plein ecran, constantes deja poses).
static UINT64 g_prepFrame = ~0ull, g_prepFence;
// 2e cible du G-buffer (couleur speculaire, lisse) : vue de lecture, refaite quand la texture change.
static ID3D11ShaderResourceView *Gb1Srv()
{
    static ID3D11Resource *res; static ID3D11ShaderResourceView *srv;
    if (res == g_gb1 && srv) return srv;
    Rel(srv); res = g_gb1;
    if (!g_gb1) return NULL;
    D3D11_SHADER_RESOURCE_VIEW_DESC sd = {}; sd.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D; sd.Format = DXGI_FORMAT_R8G8B8A8_UNORM; sd.Texture2D.MipLevels = 1;
    if (FAILED(g_dev11->CreateShaderResourceView(g_gb1, &sd, &srv))) srv = NULL;
    return srv;
}
static UINT64 Prep()
{
    if (g_prepFrame == g_frameNo) return g_prepFence;   // (deja faite pour cette image : soleil puis lampes)
    ID3D11RenderTargetView *two[2] = { g_pos.rtv, g_nrm.rtv };
    g_ctx1->OMSetRenderTargets(2, two, NULL);
    ID3D11ShaderResourceView *in[3] = { g_srvDepth, g_srvNormal, Gb1Srv() };
    g_ctx1->PSSetShaderResources(0, 3, in);
    g_ctx1->PSSetShader(g_psPrep, NULL, 0);
    g_ctx1->Draw(3, 0);
    ID3D11ShaderResourceView *none[3] = { NULL, NULL, NULL };
    g_ctx1->PSSetShaderResources(0, 3, none);
    g_ctx1->OMSetRenderTargets(0, NULL, NULL);
    UINT64 prepDone = ++g_fenceVal;
    g_ctx4->Signal(g_fence11, prepDone);
    g_ctx1->Flush();
    g_prepFrame = g_frameNo; g_prepFence = prepDone;
    return prepDone;
}
static UINT64 Rays()
{
    UINT64 ready = TraceShadows(Prep());
    if (ready) g_ctx4->Wait(g_fence11, ready);
    return ready;
}
static void BeginOwn(UINT w, UINT h, ID3DDeviceContextState **prev)
{
    g_ctx1->SwapDeviceContextState(g_ourState, prev);
    D3D11_VIEWPORT vp = { 0, 0, (float)w, (float)h, 0, 1 };
    g_ctx1->RSSetViewports(1, &vp);
    g_ctx1->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    g_ctx1->VSSetShader(g_vsFull, NULL, 0);
    ID3D11SamplerState *ss[2] = { g_sampPt, g_sampLin };
    g_ctx1->PSSetSamplers(0, 2, ss);
    g_ctx1->UpdateSubresource(g_cb11, 0, NULL, &g_frame, 0, 0);
    g_ctx1->PSSetConstantBuffers(0, 1, &g_cb11);
}
static void DumpTex(ID3D11Texture2D *t, const wchar_t *suffix);
static int g_masks;
static ID3D11RenderTargetView *SsRtv(ID3D11Resource *r)
{
    for (SsView &e : g_ssViews) if (e.r == r && e.rtv) return e.rtv;
    SsView &e = g_ssViews[g_ssNext++ & 3];
    Rel(e.rtv); e.r = NULL;
    D3D11_RENDER_TARGET_VIEW_DESC rd = {}; rd.ViewDimension = D3D11_RTV_DIMENSION_TEXTURE2D; rd.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    if (FAILED(g_dev11->CreateRenderTargetView(r, &rd, &e.rtv))) { Log("carte d'ombres : vue impossible"); return NULL; }
    e.r = r;
    Log("carte d'ombres d'Unity reconnue (%p)", (void *)r);
    return e.rtv;
}
static void InjectMask()
{
    ID3D11Resource *ss = g_ssCur;
    if (!ss || !g_srvDepth || !g_srvNormal || !InitD12() || !Resize(g_inW, g_inH)) return;
    ID3D11RenderTargetView *rtv = SsRtv(ss);
    if (!rtv) return;
    ID3D11Texture2D *ssT = NULL; ss->QueryInterface(__uuidof(ID3D11Texture2D), (void **)&ssT);
    if ((g_armFlags & 2) && ssT) DumpTex(ssT, L"unity");   // (essai : carte d'Unity avant remplacement)
    memcpy(g_frame.prevVP, g_lastVP, sizeof g_lastVP);
    memcpy(g_frame.prevCam, g_lastCam, sizeof g_lastCam);
    g_frame.prevCam[3] = g_histValid && !(g_armFlags & 8) ? 1.f : 0.f;   // (8 : sans accumulation, essais)
    ID3DDeviceContextState *prev = NULL;
    BeginOwn(g_inW, g_inH, &prev);
    UINT64 ready = Rays();
    if (ready) {
        // accumulation : historique precedent -> courant
        int cur = g_histCur ^ 1;
        g_ctx1->OMSetRenderTargets(1, &g_hist[cur].rtv, NULL);
        ID3D11ShaderResourceView *ac[4] = { g_vis.srv, g_srvNormal, g_pos.srv, g_hist[g_histCur].srv };
        g_ctx1->PSSetShaderResources(0, 4, ac);
        g_ctx1->PSSetShader(g_psAccum, NULL, 0);
        g_ctx1->Draw(3, 0);
        ID3D11ShaderResourceView *none4[4] = { NULL, NULL, NULL, NULL };
        g_ctx1->PSSetShaderResources(0, 4, none4);
        g_histCur = cur; g_histValid = true;
        // carte d'ombres d'Unity
        g_ctx1->OMSetRenderTargets(1, &rtv, NULL);
        ID3D11ShaderResourceView *sv[3] = { g_hist[cur].srv, g_srvNormal, g_pos.srv };
        g_ctx1->PSSetShaderResources(0, 3, sv);
        g_ctx1->PSSetShader(g_psMask, NULL, 0);
        g_ctx1->Draw(3, 0);
        ID3D11ShaderResourceView *none[3] = { NULL, NULL, NULL };
        g_ctx1->PSSetShaderResources(0, 3, none);
        Inverse4(g_frame.invVP, g_lastVP);
        memcpy(g_lastCam, g_frame.cam, sizeof g_lastCam);
    }
    g_ctx1->OMSetRenderTargets(0, NULL, NULL);
    g_ctx1->SwapDeviceContextState(prev, NULL);
    if (prev) prev->Release();
    Rehook();
    if ((g_armFlags & 2) && ssT) { DumpTex(ssT, L"rt"); DumpTex(g_vis.t11, L"vis"); }
    if (ssT) ssT->Release();
    if (g_masks++ % 600 == 0) Log("ombres RT dans la carte d'Unity : %d images, rayons %s", g_masks, ready ? "ok" : "ECHEC");
}
// Preparation (positions, normales) juste apres le G-buffer, pour les lampes (la nuit, sans passe du soleil ; en fin de
// rendu, la profondeur d'Unity n'est plus celle de l'image).
static void EarlyPrep()
{
    if (!g_srvDepth || !g_srvNormal || !InitD12() || !Resize(g_inW, g_inH)) return;
    ID3DDeviceContextState *prev = NULL;
    BeginOwn(g_inW, g_inH, &prev);
    Prep();
    g_ctx1->OMSetRenderTargets(0, NULL, NULL);
    g_ctx1->SwapDeviceContextState(prev, NULL);
    if (prev) prev->Release();
    Rehook();
}

// Une colonne de pixels d'une texture (luminance), lue par la carte.
static bool ReadColumn(ID3D11Resource *res, UINT x, std::vector<float> &out)
{
    ID3D11Texture2D *t = NULL;
    if (FAILED(res->QueryInterface(__uuidof(ID3D11Texture2D), (void **)&t))) return false;
    D3D11_TEXTURE2D_DESC d; t->GetDesc(&d); t->Release();
    bool half = d.Format == DXGI_FORMAT_R16G16B16A16_FLOAT || d.Format == DXGI_FORMAT_R16G16B16A16_TYPELESS;
    bool byte8 = d.Format == DXGI_FORMAT_R8G8B8A8_TYPELESS || d.Format == DXGI_FORMAT_R8G8B8A8_UNORM || d.Format == DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
    if (!half && !byte8) return false;
    D3D11_TEXTURE2D_DESC sd = {}; sd.Width = 1; sd.Height = d.Height; sd.MipLevels = 1; sd.ArraySize = 1; sd.Format = d.Format; sd.SampleDesc.Count = 1;
    sd.Usage = D3D11_USAGE_STAGING; sd.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    ID3D11Texture2D *st = NULL;
    if (FAILED(g_dev11->CreateTexture2D(&sd, NULL, &st))) return false;
    D3D11_BOX box = { x, 0, 0, x + 1, d.Height, 1 };
    g_ctx1->CopySubresourceRegion(st, 0, 0, 0, 0, res, 0, &box);
    D3D11_MAPPED_SUBRESOURCE m;
    bool ok = SUCCEEDED(g_ctx1->Map(st, 0, D3D11_MAP_READ, 0, &m));
    if (ok) {
        out.resize(d.Height);
        for (UINT y = 0; y < d.Height; y++) {
            const char *row = (const char *)m.pData + (size_t)y * m.RowPitch;
            if (half) { const unsigned short *p = (const unsigned short *)row; out[y] = HalfToFloat(p[0]) + HalfToFloat(p[1]) + HalfToFloat(p[2]); }
            else { const unsigned char *p = (const unsigned char *)row; out[y] = (p[0] + p[1] + p[2]) / 255.f; }
        }
        g_ctx1->Unmap(st, 0);
    }
    st->Release();
    return ok;
}

// Orientation de l'image finale face au G-buffer : contours (ecarts d'une ligne a l'autre) de la couleur des surfaces
// (G-buffer) et de l'image, sur 8 colonnes ; correlation a l'endroit et a l'envers. Mesuree toutes les 300 images.
static bool GbufFlip(ID3D11Resource *gb, ID3D11Resource *img)
{
    static bool flip = true; static unsigned checks;
    if (checks++ % 300 != 0) return flip;   // (l'image finale alterne entre deux textures : cadence au compteur seul)
    std::vector<double> ea, eb;
    for (int k = 1; k <= 8; k++) {
        std::vector<float> a, b;
        UINT x = g_inW * k / 9;
        if (!ReadColumn(gb, x, a) || !ReadColumn(img, x, b) || a.size() != b.size() || a.size() < 4) return flip;
        if (ea.empty()) { ea.assign(a.size() - 1, 0); eb.assign(a.size() - 1, 0); }
        for (size_t y = 0; y + 1 < a.size(); y++) { ea[y] += fabs(a[y + 1] - a[y]); eb[y] += fabs(b[y + 1] - b[y]) / (fabs(b[y]) + fabs(b[y + 1]) + 0.05); }
    }
    size_t h = ea.size();
    double ma = 0, mb = 0; for (size_t y = 0; y < h; y++) { ma += ea[y]; mb += eb[y]; } ma /= h; mb /= h;
    double same = 0, inv = 0;
    for (size_t y = 0; y < h; y++) { same += (ea[y] - ma) * (eb[y] - mb); inv += (ea[y] - ma) * (eb[h - 1 - y] - mb); }
    bool f = inv > same;
    if (f != flip || checks < 2) Log("lampes : image finale %s face au G-buffer (contours : a l'endroit %.2f, retournee %.2f)", f ? "retournee" : "a l'endroit", same, inv);
    flip = f;
    return flip;
}

// Lampes : rayons vers chacune, puis part des lampes bloquees retiree de la cible de l'eclairage (avant le brouillard).
static int g_lampPasses;
static void LampPass(ID3D11RenderTargetView *target, bool flip)
{
    if (!target || !g_gb0 || (g_nLamps <= 0 && g_aoStrength <= 0 && g_reflStrength <= 0) || !g_psoLamps || !g_srvDepth || !g_srvNormal || !InitD12() || !Resize(g_inW, g_inH)) {
        static int fails; if (fails++ < 3) Log("lampes : passe sautee (cible %p, G-buffer %p, %d lampes, pipeline %p)", (void *)g_hdrRtv, (void *)g_gb0, g_nLamps, (void *)g_psoLamps);
        return;
    }
    static ID3D11Resource *gbRes; static ID3D11ShaderResourceView *gbSrv;
    if (gbRes != g_gb0) {
        Rel(gbSrv); gbRes = g_gb0;
        D3D11_SHADER_RESOURCE_VIEW_DESC sd = {}; sd.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D; sd.Format = DXGI_FORMAT_R8G8B8A8_UNORM; sd.Texture2D.MipLevels = 1;
        if (FAILED(g_dev11->CreateShaderResourceView(g_gb0, &sd, &gbSrv))) { Log("lampes : couleur des surfaces illisible"); gbSrv = NULL; }
    }
    if (!gbSrv) return;
    // constantes des lampes (Direct3D 12 : tampon de l'image ; 11 : b1) -- apres la fin de la passe qui s'en servait
    {
        int pa = g_frameNo & 1;
        if (g_allocFenceL[pa] && g_fence12->GetCompletedValue() < g_allocFenceL[pa]) { g_fence12->SetEventOnCompletion(g_allocFenceL[pa], g_fenceEvent); WaitForSingleObject(g_fenceEvent, 2000); }
        void *q = NULL; D3D12_RANGE nr = { 0, 0 };
        LampIn &env = g_lamps[16];
        memset(&env, 0, sizeof env);
        for (int k = 0; k < 3; k++) { env.pos[k] = g_frame.sunCol[k]; env.col[k] = g_frame.amb[k]; }
        if (SUCCEEDED(g_lampBuf[g_frameNo & 1]->Map(0, &nr, &q))) { memcpy(q, g_lamps, sizeof g_lamps); g_lampBuf[g_frameNo & 1]->Unmap(0, NULL); }
    }
    float cb[49 * 4] = {};
    for (int k = 0; k < g_nLamps; k++) {
        const LampIn &L = g_lamps[k];
        float *a = cb + k * 4, *b = cb + (16 + k) * 4, *c = cb + (32 + k) * 4;
        a[0] = L.pos[0]; a[1] = L.pos[1]; a[2] = L.pos[2]; a[3] = L.range;
        b[0] = L.col[0]; b[1] = L.col[1]; b[2] = L.col[2]; b[3] = L.type;
        c[0] = L.dir[0]; c[1] = L.dir[1]; c[2] = L.dir[2]; c[3] = L.cosHalf;
    }
    cb[48 * 4] = (float)g_nLamps; cb[48 * 4 + 1] = g_lampStrength; cb[48 * 4 + 2] = flip ? 1.f : 0.f; cb[48 * 4 + 3] = g_aoStrength;
    g_ctx1->UpdateSubresource(g_cbLamps, 0, NULL, cb, 0, 0);
    ID3DDeviceContextState *prev = NULL;
    BeginOwn(g_inW, g_inH, &prev);
    UINT64 ready = TraceShadows(Prep(), true);
    if (ready) {
        g_ctx4->Wait(g_fence11, ready);
        // copie de la cible (le shader lit l'image actuelle : retire au plus 95 % de ce qui est la)
        static ID3D11Texture2D *copy; static ID3D11ShaderResourceView *copySrv; static D3D11_TEXTURE2D_DESC copyDesc;
        ID3D11Resource *tr = NULL; target->GetResource(&tr);
        ID3D11Texture2D *tt = NULL; if (tr) tr->QueryInterface(__uuidof(ID3D11Texture2D), (void **)&tt);
        if (tt) {
            D3D11_TEXTURE2D_DESC td; tt->GetDesc(&td);
            if (!copy || td.Width != copyDesc.Width || td.Height != copyDesc.Height || td.Format != copyDesc.Format) {
                Rel(copySrv); Rel(copy); copyDesc = td;
                D3D11_TEXTURE2D_DESC cd = td; cd.BindFlags = D3D11_BIND_SHADER_RESOURCE; cd.MiscFlags = 0; cd.CPUAccessFlags = 0; cd.Usage = D3D11_USAGE_DEFAULT; cd.MipLevels = 1;
                if (cd.Format == DXGI_FORMAT_R16G16B16A16_TYPELESS) cd.Format = DXGI_FORMAT_R16G16B16A16_FLOAT;
                if (SUCCEEDED(g_dev11->CreateTexture2D(&cd, NULL, &copy))) g_dev11->CreateShaderResourceView(copy, NULL, &copySrv);
            }
            if (copy) g_ctx1->CopyResource(copy, tt);
            tt->Release();
        }
        if (tr) tr->Release();
        g_ctx1->OMSetRenderTargets(1, &target, NULL);
        {   // (reflets : projection de l'image, force, retournement)
            float rb[20] = {};
            Inverse4(g_frame.invVP, rb);
            rb[16] = g_reflStrength; rb[17] = flip ? 1.f : 0.f;
            g_ctx1->UpdateSubresource(g_cbRefl, 0, NULL, rb, 0, 0);
        }
        ID3D11Buffer *cbs[3] = { g_cb11, g_cbLamps, g_cbRefl };
        g_ctx1->PSSetConstantBuffers(0, 3, cbs);
        ID3D11ShaderResourceView *sv[10] = { NULL, g_srvNormal, g_pos.srv, NULL, g_lampVis.srv, gbSrv, copySrv, g_ao.srv, g_refl.srv, Gb1Srv() };
        g_ctx1->PSSetShaderResources(0, 10, sv);
        g_ctx1->PSSetShader(g_psLamps, NULL, 0);
        g_ctx1->Draw(3, 0);
        ID3D11ShaderResourceView *none[10] = {};
        g_ctx1->PSSetShaderResources(0, 10, none);
    }
    g_ctx1->OMSetRenderTargets(0, NULL, NULL);
    g_ctx1->SwapDeviceContextState(prev, NULL);
    if (prev) prev->Release();
    Rehook();
    if (g_lampPasses++ % 1800 == 0) Log("lampes RT : %d images, %d lampes, rayons %s", g_lampPasses, g_nLamps, ready ? "ok" : "ECHEC");
}
// Lampes de l'image : par lampe 12 nombres (position, portee, couleur x intensite, type 0 point / 1 projecteur,
// direction, cos du demi-angle) ; force : part retiree (1 = celle estimee).
extern "C" __declspec(dllexport) void RtLamps(const float *data, int n, float strength)
{
    g_nLamps = n < 0 ? 0 : n > 16 ? 16 : n;
    memcpy(g_lamps, data, (size_t)g_nLamps * sizeof(LampIn));
    g_lampStrength = strength;
}
// Occlusion ambiante : force (0 : non ; 1 : part bloquee de l'ambiance retiree en entier), longueur des rayons, nombre.
extern "C" __declspec(dllexport) void RtReflections(float strength) { g_reflStrength = strength; }
extern "C" __declspec(dllexport) void RtAmbient(float strength, float radius, int rays)
{
    g_aoStrength = strength; g_aoRadius = radius > 0.1f ? radius : 1.5f; g_aoRays = rays < 1 ? 1 : rays > 16 ? 16 : rays;
}
// A chaque image (avant le rendu) : remplacer la carte d'ombres d'Unity par les ombres RT (1), + vidage d'essai (2),
// + journal des cibles (4).
extern "C" __declspec(dllexport) void RtArm(int flags) { HookContext(); g_armed = (flags & 1) != 0; g_lampsArmed = (flags & 16) != 0; g_armFlags = flags; if (flags & 4) g_seqLog = 40; }

extern "C" __declspec(dllexport) void RtPostRender(int mode)
{
    if (!g_ctx1 || !g_ourState) return;
    ID3D11RenderTargetView *rtv = NULL; ID3D11DepthStencilView *dsv = NULL;
    g_ctx1->OMGetRenderTargets(1, &rtv, &dsv);
    if (dsv) dsv->Release();
    if (!rtv) return;
    D3D11_TEXTURE2D_DESC d = {};
    { ID3D11Resource *r = NULL; rtv->GetResource(&r); ID3D11Texture2D *t = NULL; if (r && SUCCEEDED(r->QueryInterface(__uuidof(ID3D11Texture2D), (void **)&t))) { t->GetDesc(&d); t->Release(); } if (r) r->Release(); }
    {
        static int lastMode = -1;
        if (mode != lastMode) Log("RtPostRender(%d) : cible %ux%u format %d", mode, d.Width, d.Height, (int)d.Format);
        lastMode = mode;
    }
    if (!g_capturing) ProcessBatches();
    if (g_lampsArmed && mode == 7 && (d.Width != g_inW || d.Height != g_inH)) {   // (pas la taille de l'ecran : autre camera, rien)
        static int other; if (other++ < 3) Log("lampes : cible %ux%u, pas celle de l'ecran (%ux%u) : passe sautee", d.Width, d.Height, g_inW, g_inH);
        g_lampsArmed = false;
    }
    if (g_lampsArmed && mode == 7) {
        // Lampes, en fin de rendu de la camera : la cible courante est une copie de l'image (apres le brouillard du jeu), dont
        // l'orientation face au G-buffer varie : mesuree (GbufFlip).
        g_lampsArmed = false;
        ID3D11Resource *cur = NULL; rtv->GetResource(&cur);
        bool flip = g_gb0 && GbufFlip(g_gb0, cur);
        if (cur) cur->Release();
        g_inOwn = true; LampPass(rtv, flip); g_inOwn = false;
    }
    g_frameNo++;
    bool probe = false;
    if (mode == 14) mode = 0;   // (ancien melange apres coup)
    if (mode == 12 || mode == 13) { static int probes; if (g_frameNo > 1500 && probes < 1) { probes++; probe = true; } mode = mode == 12 ? 0 : 4; }
    g_ctrl = mode == 6;
    bool rays = (mode == 0 || mode == 3 || mode == 4 || mode == 6 || mode == 8 || mode == 9) && g_srvDepth && g_srvNormal && InitD12() && Resize(d.Width, d.Height);
    ID3DDeviceContextState *prev = NULL;
    g_ctx1->SwapDeviceContextState(g_ourState, &prev);
    D3D11_VIEWPORT vp = { 0, 0, (float)d.Width, (float)d.Height, 0, 1 };
    g_ctx1->RSSetViewports(1, &vp);
    g_ctx1->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    g_ctx1->VSSetShader(g_vsFull, NULL, 0);
    ID3D11SamplerState *ss[2] = { g_sampPt, g_sampLin };
    g_ctx1->PSSetSamplers(0, 2, ss);
    { FrameIn fr = g_frame; if (mode == 8 || mode == 9) fr.misc[3] = -1; g_ctx1->UpdateSubresource(g_cb11, 0, NULL, &fr, 0, 0); }
    g_ctx1->PSSetConstantBuffers(0, 1, &g_cb11);
    float f[4] = { 0, 0, 0, 0 };
    UINT64 ready = 0;
    if (rays) {
        // 1. preparation (positions, normales) vers les textures partagees
        ID3D11RenderTargetView *two[2] = { g_pos.rtv, g_nrm.rtv };
        g_ctx1->OMSetRenderTargets(2, two, NULL);
        ID3D11ShaderResourceView *in[2] = { g_srvDepth, g_srvNormal };
        g_ctx1->PSSetShaderResources(0, 2, in);
        g_ctx1->PSSetShader(g_psPrep, NULL, 0);
        g_ctx1->Draw(3, 0);
        ID3D11ShaderResourceView *none[2] = { NULL, NULL };
        g_ctx1->PSSetShaderResources(0, 2, none);
        g_ctx1->OMSetRenderTargets(0, NULL, NULL);
        UINT64 prepDone = ++g_fenceVal;
        g_ctx4->Signal(g_fence11, prepDone);
        g_ctx1->Flush();
        // 2. rayons (Direct3D 12), 3. attente sur la carte
        ready = TraceShadows(prepDone);
        if (ready) g_ctx4->Wait(g_fence11, ready);
    }
    if (g_frameNo % 600 == 1) Log("image %llu : mode %d, rayons %d, pret %llu ; depuis 600 images : %u changements de cible, %u DrawIndexed", (unsigned long long)g_frameNo, mode, (int)rays, (unsigned long long)ready, g_cntSetRT, g_cntDrawIdx);
    if (g_frameNo % 600 == 1) { g_cntSetRT = g_cntDrawIdx = 0; }
    g_ctx1->OMSetRenderTargets(1, &rtv, NULL);
    ID3D11ShaderResourceView *sv[3] = { NULL, NULL, NULL };
    if (mode == 1 && g_srvNormal) { sv[0] = g_srvNormal; g_ctx1->PSSetShader(g_psShowN, NULL, 0); }
    else if (mode == 2 && g_srvDepth) { sv[0] = g_srvDepth; g_ctx1->PSSetShader(g_psShowD, NULL, 0); }
    else if (mode == 3 && rays) { sv[0] = g_pos.srv; g_ctx1->PSSetShader(g_psShowP, NULL, 0); }
    else if ((mode == 4 || mode == 6) && ready) { sv[0] = g_vis.srv; g_ctx1->PSSetShader(g_psShowV, NULL, 0); }
    else if ((mode == 0 || mode == 9) && ready) { sv[0] = g_vis.srv; sv[1] = g_srvNormal; sv[2] = g_pos.srv; g_ctx1->PSSetShader(g_psComp, NULL, 0); g_ctx1->OMSetBlendState(g_blendMul, f, 0xffffffff); }
    else if (mode == 8 && ready) { sv[0] = g_vis.srv; sv[1] = g_srvNormal; sv[2] = g_pos.srv; g_ctx1->PSSetShader(g_psComp, NULL, 0); }   // (controle : facteur du melange)
    else if (mode == 10) { g_ctx1->PSSetShader(g_psHalf, NULL, 0); g_ctx1->OMSetBlendState(g_blendMul, f, 0xffffffff); }
    else if (mode == 11) { g_ctx1->PSSetShader(g_psHalf, NULL, 0); }
    else if (mode == 5) { g_ctx1->PSSetShader(g_psTest, NULL, 0); g_ctx1->OMSetBlendState(g_blendMul, f, 0xffffffff); }
    else sv[0] = (ID3D11ShaderResourceView *)1;   // (rien a dessiner)
    if (sv[0] != (ID3D11ShaderResourceView *)1) {
        g_ctx1->PSSetShaderResources(0, 3, sv);
        g_ctx1->Draw(3, 0);
        ID3D11ShaderResourceView *none[3] = { NULL, NULL, NULL };
        g_ctx1->PSSetShaderResources(0, 3, none);
    }
    g_ctx1->OMSetRenderTargets(0, NULL, NULL);
    g_ctx1->SwapDeviceContextState(prev, NULL);
    if (probe) { ProbeOrientation(rtv); DumpTex(g_pos.t11, L"pos"); DumpTex(g_vis.t11, L"vis"); }   // (apres le melange)
    if (prev) prev->Release();
    Rehook();
    rtv->Release();
}

BOOL WINAPI DllMain(HINSTANCE, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) { InitializeCriticalSection(&g_logLock); g_logInit = true; }
    return TRUE;
}
