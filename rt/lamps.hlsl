// Ombres des lampes (phares, lampadaires) par rayons (DXR 1.1, RayQuery, cs_6_5). Compile par build.cmd :
//   dxc -T cs_6_5 -E Main -Fh build\rt_lamps.h -Vn g_rtLamps rt\lamps.hlsl
// Pour chaque pixel (positions et normales preparees comme pour le soleil) et chaque lampe proche (au plus 16) : un
// rayon vers la lampe ; sortie : un bit par lampe dont la lumiere est bloquee. Le mod retire ensuite, apres l'eclairage
// d'Unity, la part de ces lampes (Direct3D 11, PsLamps dans mwcrt.cpp). Feuillages : test alpha, comme shadow.hlsl.

struct Frame {
    float4 sunDir;   // w : nombre de lampes
    uint4 size;      // largeur, hauteur, numero d'image
    float4 params;
    float4 cam;      // position de la camera
};
struct InstInfo { uint ib, uv, tex, texW, texH; float cutoff; uint pad0, pad1; };
struct Lamp { float3 pos; float range; float3 col; float type; float3 dir; float cosHalf; };   // type : 0 point, 1 projecteur

ConstantBuffer<Frame> gF : register(b0);
RaytracingAccelerationStructure gScene : register(t0);
Texture2D<float4> gPos : register(t1);
Texture2D<float4> gNrm : register(t2);
StructuredBuffer<InstInfo> gInst : register(t3);
StructuredBuffer<Lamp> gLamps : register(t4);
RWTexture2D<uint> gLampVis : register(u1);
ByteAddressBuffer gBufs[] : register(t0, space1);

bool AlphaPass(uint inst, uint prim, float2 bary)
{
    InstInfo ii = gInst[inst];
    uint3 t = gBufs[NonUniformResourceIndex(ii.ib)].Load3(prim * 12);
    ByteAddressBuffer uvb = gBufs[NonUniformResourceIndex(ii.uv)];
    float2 a = asfloat(uvb.Load2(t.x * 8)), b = asfloat(uvb.Load2(t.y * 8)), c = asfloat(uvb.Load2(t.z * 8));
    float2 uv = a * (1 - bary.x - bary.y) + b * bary.x + c * bary.y;
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

bool Free(RayDesc ray)
{
    RayQuery<RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH | RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES> q;
    q.TraceRayInline(gScene, 0, 0xFF, ray);
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
    uint occ = 0;
    if (p.w > 0.5)
    {
        float3 n = normalize(gNrm[id.xy].xyz * 2 - 1);
        float camDist = length(p.xyz - gF.cam.xyz);
        uint count = min((uint)gF.sunDir.w, 16u);
        for (uint i = 0; i < count; i++)
        {
            Lamp L = gLamps[i];
            float3 d = L.pos - p.xyz;
            float dist = length(d);
            if (dist > L.range || dist < 0.7) continue;
            float3 l = d / dist;
            if (dot(n, l) <= 0.0) continue;                                     // (dos a la lampe : deja sombre)
            if (L.type > 0.5 && dot(-l, L.dir) < L.cosHalf) continue;          // (hors du cone du projecteur)
            // part de la lampe ici (comme PsLamps) : pas de rayon si elle ne se verrait pas
            float r2 = dist * dist / max(L.range * L.range, 1e-4);
            float att = 1 / (1 + 25 * r2) * saturate((1 - r2) * 5);
            float spot = 1;
            if (L.type > 0.5) {
                float c = dot(-l, L.dir), ch = L.cosHalf;
                float rr = (sqrt(saturate(1 - c * c)) / max(c, 1e-3)) / max(sqrt(saturate(1 - ch * ch)) / max(ch, 1e-3), 1e-3);
                spot = saturate(1 - rr * rr);
            }
            if (att * dot(n, l) * spot * max(L.col.r, max(L.col.g, L.col.b)) < 0.015) continue;
            RayDesc r;
            r.Origin = p.xyz + n * (0.03 + 0.002 * camDist);
            r.Direction = l;
            r.TMin = 0.05;
            r.TMax = dist - (L.type > 0.5 ? 1.0 : 0.6);                         // (pas le boitier de la lampe, son verre ; phares : ni le pare-chocs)
            if (!Free(r)) occ |= 1u << i;
        }
    }
    gLampVis[id.xy] = occ;
}
