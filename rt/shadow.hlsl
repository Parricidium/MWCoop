// Ombres du soleil par rayons (DXR 1.1, requetes de rayons « inline » : RayQuery, cs_6_5). Compile par build.cmd :
//   dxc -T cs_6_5 -E Main -Fh build\rt_shadow.h -Vn g_rtShadow rt\shadow.hlsl
// Entrees (preparees par Direct3D 11 depuis le G-buffer d'Unity, textures partagees) : position du pixel dans le monde
// (w = 1 si surface) et normale du monde (* 0.5 + 0.5). Sortie : visibilite du soleil (0 a 1, ombres douces : rayons
// tires dans le disque du soleil).
// Feuillages (arbres, haies : shaders a decoupe) : instances non opaques ; a chaque triangle candidat, l'alpha de leur
// texture (copie octet par octet, gAlpha) au point touche decide (seuil de la matiere).

struct Frame {
    float4 sunDir;   // xyz : vers le soleil ; w : demi-angle du disque (radians)
    uint4 size;      // largeur, hauteur, numero d'image, rayons par pixel
    float4 params;   // x : portee des rayons (m), y : decalage le long de la normale (m), z : decalage vers le soleil (m)
    float4 cam;      // position de la camera (controle : rayons depuis la camera)
};
struct InstInfo { uint ib, uv, tex, texW, texH; float cutoff; uint vb, sub; float cr, cg, cb, pad1; };   // cases des tampons (tableau gBufs), couleur de l'objet

ConstantBuffer<Frame> gF : register(b0);
RaytracingAccelerationStructure gScene : register(t0);
Texture2D<float4> gPos : register(t1);
Texture2D<float4> gNrm : register(t2);
StructuredBuffer<InstInfo> gInst : register(t3);
RWTexture2D<float> gVis : register(u0);
ByteAddressBuffer gBufs[] : register(t0, space1);   // indices (uint3 par triangle), uv (float2 par sommet), alpha (octets)

uint Hash(uint x) { x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16; return x; }
float Rand(inout uint s) { s = Hash(s); return (s & 0xFFFFFF) / 16777216.0; }

bool AlphaPass(uint inst, uint prim, float2 bary)
{
    InstInfo ii = gInst[inst];
    uint3 t = gBufs[NonUniformResourceIndex(ii.ib)].Load3(prim * 12);
    ByteAddressBuffer uvb = gBufs[NonUniformResourceIndex(ii.uv)];
    float2 a = asfloat(uvb.Load2(t.x * 8)), b = asfloat(uvb.Load2(t.y * 8)), c = asfloat(uvb.Load2(t.z * 8));
    float2 uv = a * (1 - bary.x - bary.y) + b * bary.x + c * bary.y;
    // lecture bilineaire (4 texels voisins, en repetition) : contours arrondis comme la carte d'ombres d'Unity
    float2 tc = frac(uv) * float2(ii.texW, ii.texH) - 0.5;
    float2 f = frac(tc);
    int2 b0 = (int2)floor(tc);
    ByteAddressBuffer ab = gBufs[NonUniformResourceIndex(ii.tex)];
    float a4[4];
    [unroll] for (int k = 0; k < 4; k++) {
        uint x = (uint)((b0.x + (k & 1) + (int)ii.texW) % (int)ii.texW), y = (uint)((b0.y + (k >> 1) + (int)ii.texH) % (int)ii.texH);
        uint i = y * ii.texW + x;
        a4[k] = ((ab.Load(i & ~3u) >> ((i & 3u) * 8)) & 255u) / 255.0;
    }
    float al = lerp(lerp(a4[0], a4[1], f.x), lerp(a4[2], a4[3], f.x), f.y);
    return al >= ii.cutoff;
}

// Rayon d'ombre : vrai si rien ne coupe (feuillages : test alpha).
bool Free(RayDesc ray)
{
    RayQuery<RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH | RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES> q;
    q.TraceRayInline(gScene, 0, 0x01, ray);   // (masque 1 : pas le sol, qui ne fait pas d'ombre chez Unity non plus)
    while (q.Proceed())
        if (q.CandidateType() == CANDIDATE_NON_OPAQUE_TRIANGLE && AlphaPass(q.CandidateInstanceID(), q.CandidatePrimitiveIndex(), q.CandidateTriangleBarycentrics()))
            q.CommitNonOpaqueTriangleHit();
    return q.CommittedStatus() == COMMITTED_NOTHING;
}

[numthreads(8, 8, 1)]
void Main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= gF.size.x || id.y >= gF.size.y) return;
    float4 p = gPos[id.xy];
    if (p.w < 0.5) { gVis[id.xy] = 1; return; }                 // ciel
    float3 n = normalize(gNrm[id.xy].xyz * 2 - 1);
    float3 L = normalize(gF.sunDir.xyz);
    if (dot(n, L) <= 0.0) { gVis[id.xy] = 1; return; }          // dos au soleil : Unity ne l'eclaire deja pas
    uint seed = Hash(id.x * 1973 + id.y * 9277 + gF.size.z * 26699);
    float3 t = normalize(abs(L.y) < 0.99 ? cross(L, float3(0, 1, 0)) : cross(L, float3(1, 0, 0)));
    float3 b = cross(L, t);
    uint rays = max(gF.size.w, 1);
    float dist = length(p.xyz - gF.cam.xyz);
    float vis = 0;
    for (uint i = 0; i < rays; i++)
    {
        float r = sqrt(Rand(seed)) * gF.sunDir.w, a = Rand(seed) * 6.2831853;
        RayDesc ray;
        ray.Origin = p.xyz + n * (gF.params.y + 0.002 * dist) + L * gF.params.z;
        ray.Direction = normalize(L + (t * cos(a) + b * sin(a)) * r);
        ray.TMin = 0.1 + 0.004 * dist;   // (soleil rasant : pas d'ombre de la surface sur elle-meme, bosses du terrain)
        ray.TMax = gF.params.x;
        if (Free(ray)) vis += 1;
    }
    gVis[id.xy] = vis / rays;
}
