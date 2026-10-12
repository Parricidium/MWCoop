// Ombres des lampes (phares, lampadaires) et occlusion ambiante par rayons (DXR 1.1, RayQuery, cs_6_5). Compile par build.cmd :
//   dxc -T cs_6_5 -E Main -Fh build\rt_lamps.h -Vn g_rtLamps rt\lamps.hlsl
// Pour chaque pixel (positions et normales preparees comme pour le soleil) et chaque lampe proche (au plus 16) : un
// rayon vers la lampe ; sortie : un bit par lampe dont la lumiere est bloquee. Le mod retire ensuite, apres l'eclairage
// d'Unity, la part de ces lampes (Direct3D 11, PsLamps dans mwcrt.cpp). Feuillages : test alpha, comme shadow.hlsl.
// Occlusion ambiante (params.z = 1) : quelques rayons courts (params.y rayons, params.x m) autour de la normale ; sortie :
// part degagee (gAO, 0 a 1). Tirage fixe par pixel (pas de scintillement), adouci ensuite par le filtre du composite.

struct Frame {
    float4 sunDir;   // w : nombre de lampes
    uint4 size;      // largeur, hauteur, numero d'image
    float4 params;
    float4 cam;      // position de la camera
};
struct InstInfo { uint ib, uv, tex, texW, texH; float cutoff; uint vb, sub; float cr, cg, cb, pad1; };   // (sub : table des parties colorees par texture)
struct Lamp { float3 pos; float range; float3 col; float type; float3 dir; float cosHalf; };   // type : 0 point, 1 projecteur

ConstantBuffer<Frame> gF : register(b0);
RaytracingAccelerationStructure gScene : register(t0);
Texture2D<float4> gPos : register(t1);
Texture2D<float4> gNrm : register(t2);
StructuredBuffer<InstInfo> gInst : register(t3);
StructuredBuffer<Lamp> gLamps : register(t4);
RWTexture2D<uint> gLampVis : register(u1);
RWTexture2D<float> gAO : register(u2);
RWTexture2D<float4> gRefl : register(u3);
RWTexture2D<float4> gGI : register(u4);
RWTexture2D<float4> gGlass : register(u5);   // (vitres : rgb couleur reflechie, a poids de Fresnel ; 0 : pas de vitre)   // (rebond de lumiere : lumiere recue des surfaces voisines eclairees)   // (reflets : rgb couleur estimee du point touche, a distance ; 0 : rien)

uint Hash(uint x) { x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16; return x; }
float Rand(inout uint s) { s = Hash(s); return (s & 0xFFFFFF) / 16777216.0; }
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

// masque : 1 = ce qui fait de l'ombre (pas le sol), 0xFF = tout (occlusion ambiante, reflets)
bool FreeM(RayDesc ray, uint mask)
{
    RayQuery<RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH | RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES> q;
    q.TraceRayInline(gScene, 0, mask, ray);
    while (q.Proceed())
        if (q.CandidateType() == CANDIDATE_NON_OPAQUE_TRIANGLE && AlphaPass(q.CandidateInstanceID(), q.CandidatePrimitiveIndex(), q.CandidateTriangleBarycentrics()))
            q.CommitNonOpaqueTriangleHit();
    return q.CommittedStatus() == COMMITTED_NOTHING;
}
bool Free(RayDesc ray) { return FreeM(ray, 0x01); }
// (rebond de lumiere : feuillages pris comme opaques, plus rapide ; la lumiere diffuse ne le montre pas)
bool FreeOpaque(RayDesc ray)
{
    RayQuery<RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH | RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES | RAY_FLAG_FORCE_OPAQUE> q;
    q.TraceRayInline(gScene, 0, 0x01, ray);
    q.Proceed();
    return q.CommittedStatus() == COMMITTED_NOTHING;
}

// Eclairage d'un point touche (reflets, rebond de lumiere) : couleur de l'objet (personnages : texel touche) x
// (ambiance + soleil x n.l x ombre). Normale du triangle (sommets de l'instance).
float3 ShadeHit(uint instId, uint primId, float2 bary, float3x4 o2w, float3 hp, float3 rd, bool fast)
{
        // couleur du point touche (utile hors de l'ecran) : couleur de l'objet x (ambiance + soleil x ombre)
        InstInfo ii = gInst[instId];
        float3 hn = -rd;
        if (ii.vb != 0xFFFFFFFFu && ii.ib != 0xFFFFFFFFu)
        {
            uint3 t = gBufs[NonUniformResourceIndex(ii.ib)].Load3(primId * 12);
            ByteAddressBuffer vbuf = gBufs[NonUniformResourceIndex(ii.vb)];
            float3 a = asfloat(vbuf.Load3(t.x * 12)), b = asfloat(vbuf.Load3(t.y * 12)), c = asfloat(vbuf.Load3(t.z * 12));
            float3 on = cross(b - a, c - a);
                                float3 wn = float3(dot(o2w[0].xyz, on), dot(o2w[1].xyz, on), dot(o2w[2].xyz, on));
            if (dot(wn, wn) > 1e-12) hn = normalize(wn);
            if (dot(hn, rd) > 0) hn = -hn;
        }
        Lamp env = gLamps[16];   // (pos : couleur du soleil, col : ambiance)
        float3 L = normalize(gF.sunDir.xyz);
        float ndl = saturate(dot(hn, L)), sv = 0;
        if (ndl > 0)
        {
            RayDesc sr; sr.Origin = hp + hn * 0.05; sr.Direction = L; sr.TMin = 0.02; sr.TMax = fast ? 100 : 300;
            sv = (fast ? FreeOpaque(sr) : Free(sr)) ? 1 : 0;
        }
        float3 baseCol = float3(ii.cr, ii.cg, ii.cb);
        if (ii.sub != 0xFFFFFFFFu && ii.uv != 0xFFFFFFFFu && ii.ib != 0xFFFFFFFFu)
        {   // personnages : couleur du texel touche (partie du triangle, uv aux barycentres)
            ByteAddressBuffer tb = gBufs[NonUniformResourceIndex(ii.sub)];
            uint n = tb.Load(0), prim = primId, k = 0;
            for (uint j = 1; j < n; j++) if (tb.Load((1 + j * 5) * 4) <= prim) k = j;
            uint4 e = tb.Load4((1 + k * 5 + 1) * 4); uint ec = tb.Load((1 + k * 5 + 4) * 4);
            float3 mc = float3(ec & 255u, (ec >> 8) & 255u, (ec >> 16) & 255u) / 255.0;
            baseCol = mc;
            if (e.x != 0xFFFFFFFFu)
            {
                uint3 t3 = gBufs[NonUniformResourceIndex(ii.ib)].Load3(prim * 12);
                ByteAddressBuffer uvb = gBufs[NonUniformResourceIndex(ii.uv)];
                float2 ua = asfloat(uvb.Load2(t3.x * 8)), ub = asfloat(uvb.Load2(t3.y * 8)), uc = asfloat(uvb.Load2(t3.z * 8));
                float2 bc = bary;
                float2 uv = ua * (1 - bc.x - bc.y) + ub * bc.x + uc * bc.y;
                uint x = min((uint)(frac(uv.x) * e.y), e.y - 1), y = min((uint)(frac(uv.y) * e.z), e.z - 1);
                uint tx = gBufs[NonUniformResourceIndex(e.x)].Load((y * e.y + x) * 4);
                baseCol = mc * float3(tx & 255u, (tx >> 8) & 255u, (tx >> 16) & 255u) / 255.0;
            }
        }
        return baseCol * (env.col + env.pos * ndl * sv);
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
    float ao = 1;
    if (gF.params.z > 0.5 && p.w > 0.5)
    {
        float3 n = normalize(gNrm[id.xy].xyz * 2 - 1);
        float camDist = length(p.xyz - gF.cam.xyz);
        float3 t = normalize(abs(n.y) < 0.99 ? cross(n, float3(0, 1, 0)) : cross(n, float3(1, 0, 0)));
        float3 b = cross(n, t);
        uint seed = Hash(id.x * 1973 + id.y * 9277);
        uint rays = max((uint)gF.params.y, 1u);
        float freeN = 0;
        for (uint k = 0; k < rays; k++)
        {
            float u1 = Rand(seed), u2 = Rand(seed) * 6.2831853, rr = sqrt(u1);
            RayDesc r;
            r.Origin = p.xyz + n * (0.02 + 0.001 * camDist);
            r.Direction = normalize(t * (rr * cos(u2)) + b * (rr * sin(u2)) + n * sqrt(1 - u1));   // (hemisphere, cosinus)
            r.TMin = 0.02;
            r.TMax = gF.params.x;
            if (FreeM(r, 0x03)) freeN += 1;
        }
        ao = freeN / rays;
    }
    gAO[id.xy] = ao;
    // Reflets (size.w = 1) : surfaces lisses (lisse dans pos.w - 1, prepare depuis le G-buffer), rayon reflechi parfait.
    float hitT = 0;
    float3 hitCol = 0;
    if (gF.size.w == 1 && p.w > 1.3)
    {
        float3 n = normalize(gNrm[id.xy].xyz * 2 - 1);
        float3 v = normalize(p.xyz - gF.cam.xyz);
        float3 rd = reflect(v, n);
        if (dot(rd, n) > 0.02)
        {
            float camDist = length(p.xyz - gF.cam.xyz);
            RayDesc r;
            r.Origin = p.xyz + n * (0.03 + 0.002 * camDist);
            r.Direction = rd;
            r.TMin = 0.03;
            r.TMax = 80;
            RayQuery<RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES> q;   // (premier objet touche : le plus proche)
            q.TraceRayInline(gScene, 0, 0x03, r);
            while (q.Proceed())
                if (q.CandidateType() == CANDIDATE_NON_OPAQUE_TRIANGLE && AlphaPass(q.CandidateInstanceID(), q.CandidatePrimitiveIndex(), q.CandidateTriangleBarycentrics()))
                    q.CommitNonOpaqueTriangleHit();
            if (q.CommittedStatus() == COMMITTED_TRIANGLE_HIT)
            {
                hitT = q.CommittedRayT();
                hitCol = ShadeHit(q.CommittedInstanceID(), q.CommittedPrimitiveIndex(), q.CommittedTriangleBarycentrics(), q.CommittedObjectToWorld3x4(), r.Origin + rd * hitT, rd, false);
            }
        }
    }
    gRefl[id.xy] = float4(hitCol, hitT);
    // Rebond de lumiere (cam.w = 1) : un rayon dans l'hemisphere (cosinus), jusqu'a 15 m ; s'il touche une surface, sa
    // lumiere (couleur x eclairage direct) ; rien touche : le ciel, deja dans l'ambiance d'Unity. Tirage different a chaque image (accumule).
    // Vitres (env.range > 0 : force) : rayon de la camera vers le point du pixel ; s'il traverse une vitre (masque 4), rayon
    // reflechi depuis la vitre, lumiere du point touche ; poids de Fresnel (verre : 4 % de face, 100 % en rasant).
    float4 glass = 0;
    if (gLamps[16].range > 0 && p.w > 0.5)
    {
        float3 dv = p.xyz - gF.cam.xyz; float dd = length(dv);
        RayDesc cr; cr.Origin = gF.cam.xyz; cr.Direction = dv / dd; cr.TMin = 0.05; cr.TMax = dd + 0.05;   // (vitre devant le point, ou le point lui-meme : givre dessine)
        RayQuery<RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES | RAY_FLAG_FORCE_OPAQUE> gq;
        gq.TraceRayInline(gScene, 0, 0x04, cr);
        gq.Proceed();
        if (gq.CommittedStatus() == COMMITTED_TRIANGLE_HIT)
        {
            InstInfo gi0 = gInst[gq.CommittedInstanceID()];
            float3 hp = cr.Origin + cr.Direction * gq.CommittedRayT(), gn = -cr.Direction;
            if (gi0.vb != 0xFFFFFFFFu && gi0.ib != 0xFFFFFFFFu)
            {
                uint3 t3 = gBufs[NonUniformResourceIndex(gi0.ib)].Load3(gq.CommittedPrimitiveIndex() * 12);
                ByteAddressBuffer vbuf = gBufs[NonUniformResourceIndex(gi0.vb)];
                float3 a = asfloat(vbuf.Load3(t3.x * 12)), b = asfloat(vbuf.Load3(t3.y * 12)), c = asfloat(vbuf.Load3(t3.z * 12));
                float3 on = cross(b - a, c - a);
                float3x4 o2w = gq.CommittedObjectToWorld3x4();
                float3 wn = float3(dot(o2w[0].xyz, on), dot(o2w[1].xyz, on), dot(o2w[2].xyz, on));
                if (dot(wn, wn) > 1e-12) gn = normalize(wn);
                if (dot(gn, cr.Direction) > 0) gn = -gn;
            }
            float3 rd = reflect(cr.Direction, gn);
            float cosv = saturate(dot(-cr.Direction, gn));
            float fres = 0.04 + 0.96 * pow(1 - cosv, 5);
            RayDesc rr; rr.Origin = hp + gn * 0.02; rr.Direction = rd; rr.TMin = 0.02; rr.TMax = 80;
            RayQuery<RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES | RAY_FLAG_FORCE_OPAQUE> rq;
            rq.TraceRayInline(gScene, 0, 0x03, rr);
            rq.Proceed();
            if (rq.CommittedStatus() == COMMITTED_TRIANGLE_HIT)
                glass = float4(ShadeHit(rq.CommittedInstanceID(), rq.CommittedPrimitiveIndex(), rq.CommittedTriangleBarycentrics(), rq.CommittedObjectToWorld3x4(), rr.Origin + rd * rq.CommittedRayT(), rd, true), fres);
            else
                glass = float4(gLamps[16].col, fres);   // (rien touche : le ciel, couleur de l'ambiance)
        }
    }
    gGlass[id.xy] = glass;
    float3 gi = 0;
    // (un pixel sur quatre par image, a tour de role : l'accumulation fait le reste ; les autres gardent leur valeur)
    if (gF.cam.w > 0.5 && ((id.x & 1) + (id.y & 1) * 2) != (gF.size.z & 3)) return;
    if (gF.cam.w > 0.5 && p.w > 0.5)
    {
        float3 n = normalize(gNrm[id.xy].xyz * 2 - 1);
        float camDist = length(p.xyz - gF.cam.xyz);
        float3 t = normalize(abs(n.y) < 0.99 ? cross(n, float3(0, 1, 0)) : cross(n, float3(1, 0, 0)));
        float3 b = cross(n, t);
        uint seed = Hash(id.x * 7919 + id.y * 104729 + gF.size.z * 26699);   // (change a chaque image : accumule par le composite)
        float u1 = Rand(seed), u2 = Rand(seed) * 6.2831853, rr = sqrt(u1);
        RayDesc r;
        r.Origin = p.xyz + n * (0.03 + 0.002 * camDist);
        r.Direction = normalize(t * (rr * cos(u2)) + b * (rr * sin(u2)) + n * sqrt(1 - u1));
        r.TMin = 0.03;
        r.TMax = 10;
        RayQuery<RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES | RAY_FLAG_FORCE_OPAQUE> q;
        q.TraceRayInline(gScene, 0, 0x03, r);
        q.Proceed();
        if (q.CommittedStatus() == COMMITTED_TRIANGLE_HIT)
            gi = ShadeHit(q.CommittedInstanceID(), q.CommittedPrimitiveIndex(), q.CommittedTriangleBarycentrics(), q.CommittedObjectToWorld3x4(), r.Origin + r.Direction * q.CommittedRayT(), r.Direction, true);
    }
    gGI[id.xy] = float4(gi, 1);
}
