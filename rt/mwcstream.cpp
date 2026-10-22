// MWCoopRT.dll, lecture en continu d'une video (bobine « lien » du cinema KINO TAHTI, demande de JD, 22/10) : un lien
// direct vers une video (http/https, .mp4 en general) ou un fichier, lu par Media Foundation (Source Reader) sur un fil a
// lui, sans conversion ni transfert entre joueurs : chaque joueur lit le lien lui-meme, l'hote donne l'instant.
//  - image : decodee en RGB32 par Windows, ramenee a w x h (image entiere, bandes noires), BGRA du haut vers le bas ;
//    quelques images d'avance en file. Le mod donne l'instant voulu (VidStreamFrame) et recoit la derniere image de cet
//    instant.
//  - son : PCM 16 bits converti en flottants entrelaces (voies et frequence de la video), dans un anneau de quelques
//    secondes que le mod vide depuis le fil audio d'Unity (VidStreamAudio).
//  - saut (VidStreamSeek) : arrivee en cours de seance ; la file et l'anneau repartent de la.
// Media Foundation est chargee a la demande (/DELAYLOAD) : sans elle, l'ouverture echoue avec un message.
#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <mferror.h>
#include <propvarutil.h>
#include <stdio.h>
#include <string>
#include <vector>
#include <deque>

namespace
{
    template <class T> void Rel(T *&p) { if (p) { p->Release(); p = nullptr; } }

    struct Frame { LONGLONG ts; std::vector<BYTE> px; };

    struct Stream
    {
        std::wstring url; int w = 0, h = 0;
        volatile LONG stop = 0, state = 0;   // state : 0 ouverture, 1 lecture, 2 fin du fichier, -1 echec
        volatile LONGLONG want = 0;          // instant voulu par le mod (100 ns)
        volatile LONGLONG seekTo = -1;       // saut demande (100 ns), -1 : rien
        CRITICAL_SECTION cs;
        std::deque<Frame> q;                 // images decodees d'avance
        LONGLONG lastGiven = -1;
        std::vector<float> ring; size_t head = 0, count = 0;   // son, entrelace
        int ach = 2, arate = 44100;
        LONGLONG dur = 0;
        wchar_t msg[256] = {};
        HANDLE th = nullptr;
    };

    CRITICAL_SECTION g_slock; bool g_slockInit;
    Stream *g_s[8];

    void SMsg(Stream *s, const wchar_t *fmt, ...)
    {
        wchar_t b[256];
        va_list a; va_start(a, fmt); _vsnwprintf_s(b, _countof(b), _TRUNCATE, fmt, a); va_end(a);
        EnterCriticalSection(&s->cs); wcscpy_s(s->msg, b); LeaveCriticalSection(&s->cs);
    }

    // BGRA source (lignes de pitch octets) -> BGRA w x h, image entiere au milieu (bandes noires), plus proche voisin
    void FitBgra(const BYTE *src, int sw, int sh, LONG pitch, std::vector<BYTE> &dst, int w, int h)
    {
        dst.assign((size_t)w * h * 4, 0);
        for (size_t i = 3; i < dst.size(); i += 4) dst[i] = 255;
        double s = (double)w / sw < (double)h / sh ? (double)w / sw : (double)h / sh;
        int dw = (int)(sw * s + 0.5), dh = (int)(sh * s + 0.5);
        if (dw < 1) dw = 1; if (dh < 1) dh = 1;
        int ox = (w - dw) / 2, oy = (h - dh) / 2;
        for (int y = 0; y < dh; y++)
        {
            const BYTE *row = src + (LONG_PTR)((long long)y * sh / dh) * pitch;
            BYTE *d = &dst[((size_t)(oy + y) * w + ox) * 4];
            for (int x = 0; x < dw; x++)
            {
                const BYTE *p = row + (size_t)((long long)x * sw / dw) * 4;
                d[0] = p[0]; d[1] = p[1]; d[2] = p[2]; d[3] = 255; d += 4;
            }
        }
    }

    void PushAudio(Stream *s, const short *pcm, size_t n)
    {
        EnterCriticalSection(&s->cs);
        size_t cap = s->ring.size();
        for (size_t i = 0; i < n; i++)
        {
            if (s->count == cap) { s->head = (s->head + 1) % cap; s->count--; }   // (plein : le plus ancien saute)
            s->ring[(s->head + s->count) % cap] = pcm[i] / 32768.0f;
            s->count++;
        }
        LeaveCriticalSection(&s->cs);
    }

    int RunStream(Stream *s)
    {
        IMFAttributes *at = nullptr; IMFSourceReader *rd = nullptr; IMFMediaType *t = nullptr;
        HRESULT hr = MFStartup(MF_VERSION, MFSTARTUP_FULL);
        if (FAILED(hr)) { SMsg(s, L"Media Foundation indisponible (0x%08X)", (unsigned)hr); return -1; }
        int ret = -1;
        do
        {
            MFCreateAttributes(&at, 2);
            at->SetUINT32(MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, TRUE);
            hr = MFCreateSourceReaderFromURL(s->url.c_str(), at, &rd);
            if (FAILED(hr)) { SMsg(s, hr == MF_E_UNSUPPORTED_BYTESTREAM_TYPE || hr == MF_E_UNSUPPORTED_SCHEME ? L"ce lien n'est pas une video directe (0x%08X)" : L"video illisible ou lien injoignable (0x%08X)", (unsigned)hr); break; }
            rd->SetStreamSelection((DWORD)MF_SOURCE_READER_ALL_STREAMS, FALSE);
            bool hasVideo = false, hasAudio = false;
            MFCreateMediaType(&t);
            t->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video); t->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_RGB32);
            if (SUCCEEDED(rd->SetCurrentMediaType((DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM, nullptr, t))) { rd->SetStreamSelection((DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM, TRUE); hasVideo = true; }
            Rel(t);
            MFCreateMediaType(&t);
            t->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio); t->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_PCM); t->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
            if (SUCCEEDED(rd->SetCurrentMediaType((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM, nullptr, t))) { rd->SetStreamSelection((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM, TRUE); hasAudio = true; }
            Rel(t);
            if (!hasVideo) { SMsg(s, L"pas d'image lisible a ce lien"); break; }
            UINT32 sw = 0, sh = 0; LONG stride = 0;
            auto readVideoType = [&]() {
                IMFMediaType *ct = nullptr;
                if (SUCCEEDED(rd->GetCurrentMediaType((DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM, &ct)))
                {
                    MFGetAttributeSize(ct, MF_MT_FRAME_SIZE, &sw, &sh);
                    stride = (LONG)MFGetAttributeUINT32(ct, MF_MT_DEFAULT_STRIDE, 0);
                    if (stride == 0) MFGetStrideForBitmapInfoHeader(MFVideoFormat_RGB32.Data1, sw, &stride);
                    ct->Release();
                }
            };
            readVideoType();
            if (hasAudio)
            {
                IMFMediaType *ct = nullptr;
                if (SUCCEEDED(rd->GetCurrentMediaType((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM, &ct)))
                { s->ach = (int)MFGetAttributeUINT32(ct, MF_MT_AUDIO_NUM_CHANNELS, 2); s->arate = (int)MFGetAttributeUINT32(ct, MF_MT_AUDIO_SAMPLES_PER_SECOND, 44100); ct->Release(); }
                if (s->ach < 1) s->ach = 1; if (s->ach > 2) s->ach = 2;   // (au-dela : les deux premieres voies -- voir plus bas)
            }
            EnterCriticalSection(&s->cs); s->ring.assign((size_t)s->arate * s->ach * 4, 0.0f); s->head = s->count = 0; LeaveCriticalSection(&s->cs);
            int srcCh = 2;
            if (hasAudio) { IMFMediaType *ct = nullptr; if (SUCCEEDED(rd->GetCurrentMediaType((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM, &ct))) { srcCh = (int)MFGetAttributeUINT32(ct, MF_MT_AUDIO_NUM_CHANNELS, 2); ct->Release(); } if (srcCh < 1) srcCh = 1; }
            PROPVARIANT pv; PropVariantInit(&pv);
            if (SUCCEEDED(rd->GetPresentationAttribute((DWORD)MF_SOURCE_READER_MEDIASOURCE, MF_PD_DURATION, &pv))) s->dur = (LONGLONG)pv.uhVal.QuadPart;
            PropVariantClear(&pv);
            InterlockedExchange(&s->state, 1);
            SMsg(s, L"%ux%u, son %d voie(s) %d Hz, %.0f s", sw, sh, hasAudio ? s->ach : 0, s->arate, s->dur / 1e7);
            bool vDone = false, aDone = !hasAudio;
            LONGLONG vts = 0, ats = 0;
            std::vector<BYTE> img; std::vector<short> mix;
            ret = 0;
            while (!s->stop)
            {
                LONGLONG seek = InterlockedExchange64(&s->seekTo, -1);
                if (seek >= 0)
                {
                    PROPVARIANT p; InitPropVariantFromInt64(seek, &p);
                    rd->SetCurrentPosition(GUID_NULL, p);
                    PropVariantClear(&p);
                    EnterCriticalSection(&s->cs); s->q.clear(); s->head = s->count = 0; s->lastGiven = -1; LeaveCriticalSection(&s->cs);
                    vDone = false; aDone = !hasAudio; vts = ats = seek;
                }
                if (vDone && aDone) { InterlockedExchange(&s->state, 2); Sleep(20); continue; }
                // ce qui manque le plus : images (une seconde d'avance) ou son (deux secondes dans l'anneau)
                size_t qn, an; LONGLONG qlast;
                EnterCriticalSection(&s->cs); qn = s->q.size(); qlast = qn ? s->q.back().ts : -1; an = s->count; LeaveCriticalSection(&s->cs);
                LONGLONG want = s->want;
                bool needVideo = !vDone && qn < 48 && (qlast < 0 || qlast < want + 10000000LL);
                bool needAudio = !aDone && an < (size_t)s->arate * s->ach * 2;
                if (!needVideo && !needAudio) { Sleep(4); continue; }
                bool doVideo = needVideo && (!needAudio || vts <= ats);
                DWORD stream = doVideo ? (DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM : (DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM;
                DWORD flags = 0; LONGLONG ts = 0; IMFSample *smp = nullptr;
                hr = rd->ReadSample(stream, 0, nullptr, &flags, &ts, &smp);
                if (FAILED(hr)) { if (doVideo) vDone = true; else aDone = true; continue; }
                if (flags & MF_SOURCE_READERF_ENDOFSTREAM) { if (doVideo) vDone = true; else aDone = true; }
                if ((flags & MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED) && doVideo) readVideoType();
                if (!smp) continue;
                IMFMediaBuffer *mb = nullptr;
                if (doVideo)
                {
                    vts = ts;
                    // (image deja passee : pas la peine de la convertir, sauf la derniere avant l'instant voulu)
                    if (ts + 2000000LL >= s->want && sw > 0 && sh > 0 && SUCCEEDED(smp->ConvertToContiguousBuffer(&mb)))
                    {
                        IMF2DBuffer *b2 = nullptr; BYTE *scan0 = nullptr; LONG pitch = 0; BYTE *raw = nullptr; DWORD len = 0;
                        bool locked2d = SUCCEEDED(mb->QueryInterface(IID_PPV_ARGS(&b2))) && SUCCEEDED(b2->Lock2D(&scan0, &pitch));
                        if (!locked2d && SUCCEEDED(mb->Lock(&raw, nullptr, &len))) { pitch = stride; scan0 = stride < 0 ? raw + (size_t)(-stride) * (sh - 1) : raw; }
                        if (scan0)
                        {
                            Frame f; f.ts = ts;
                            FitBgra(scan0, (int)sw, (int)sh, pitch, f.px, s->w, s->h);
                            EnterCriticalSection(&s->cs); s->q.push_back(std::move(f)); LeaveCriticalSection(&s->cs);
                        }
                        if (locked2d) b2->Unlock2D(); else if (raw) mb->Unlock();
                        Rel(b2); Rel(mb);
                    }
                }
                else
                {
                    ats = ts;
                    if (SUCCEEDED(smp->ConvertToContiguousBuffer(&mb)))
                    {
                        BYTE *raw = nullptr; DWORD len = 0;
                        if (SUCCEEDED(mb->Lock(&raw, nullptr, &len)))
                        {
                            const short *pcm = (const short *)raw;
                            size_t frames = len / 2 / srcCh;
                            if (srcCh == s->ach) PushAudio(s, pcm, frames * srcCh);
                            else
                            {
                                mix.resize(frames * s->ach);
                                for (size_t i = 0; i < frames; i++)
                                    for (int c = 0; c < s->ach; c++) mix[i * s->ach + c] = pcm[i * srcCh + (c < srcCh ? c : 0)];
                                PushAudio(s, mix.data(), mix.size());
                            }
                            mb->Unlock();
                        }
                        Rel(mb);
                    }
                }
                smp->Release();
            }
        } while (false);
        Rel(rd); Rel(at); Rel(t);
        MFShutdown();
        return ret;
    }

    int SafeStream(Stream *s)
    {
        __try { return RunStream(s); }
        __except (EXCEPTION_EXECUTE_HANDLER) { SMsg(s, L"Media Foundation absente de ce Windows (editions N : installer le Media Feature Pack)"); return -1; }
    }

    DWORD WINAPI StreamThread(LPVOID p)
    {
        Stream *s = (Stream *)p;
        CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        int r = SafeStream(s);
        CoUninitialize();
        if (r != 0) InterlockedExchange(&s->state, -1);
        return 0;
    }

    Stream *Get(int id) { return id >= 0 && id < 8 ? g_s[id] : nullptr; }
}

// Ouvre une lecture en continu (lien ou fichier), image w x h. Rend un numero (0-7), -1 si plus de place.
extern "C" __declspec(dllexport) int VidStreamOpen(const wchar_t *url, int w, int h)
{
    if (!g_slockInit) { InitializeCriticalSection(&g_slock); g_slockInit = true; }
    EnterCriticalSection(&g_slock);
    int id = -1;
    for (int i = 0; i < 8; i++) if (!g_s[i]) { id = i; break; }
    if (id >= 0)
    {
        Stream *s = new Stream();
        InitializeCriticalSection(&s->cs);
        s->url = url ? url : L""; s->w = w < 16 ? 16 : w > 1920 ? 1920 : w; s->h = h < 16 ? 16 : h > 1080 ? 1080 : h;
        s->th = CreateThread(nullptr, 0, StreamThread, s, 0, nullptr);
        if (!s->th) { DeleteCriticalSection(&s->cs); delete s; id = -1; }
        else g_s[id] = s;
    }
    LeaveCriticalSection(&g_slock);
    return id;
}

// Etat : 0 ouverture, 1 lecture, 2 fin du fichier, -1 echec ; info = { voies, frequence, duree en ms } ; message.
extern "C" __declspec(dllexport) int VidStreamInfo(int id, int *info, wchar_t *msg, int msgLen)
{
    Stream *s = Get(id);
    if (!s) return -1;
    if (info) { info[0] = s->ach; info[1] = s->arate; info[2] = (int)(s->dur / 10000); }
    if (msg && msgLen > 0) { EnterCriticalSection(&s->cs); wcsncpy_s(msg, msgLen, s->msg, _TRUNCATE); LeaveCriticalSection(&s->cs); }
    return s->state;
}

// Saut a ms millisecondes (arrivee en cours de seance, reprise).
extern "C" __declspec(dllexport) void VidStreamSeek(int id, int ms)
{
    Stream *s = Get(id);
    if (s) { InterlockedExchange64(&s->seekTo, (LONGLONG)ms * 10000LL); s->want = (LONGLONG)ms * 10000LL; }
}

// Image de l'instant ms : la derniere image decodee a cet instant, copiee dans out (BGRA w x h, du haut vers le bas)
// si elle est nouvelle. 1 : copiee ; 0 : rien de neuf.
extern "C" __declspec(dllexport) int VidStreamFrame(int id, int ms, BYTE *out, int outLen)
{
    Stream *s = Get(id);
    if (!s) return 0;
    LONGLONG t = (LONGLONG)ms * 10000LL;
    s->want = t;
    int got = 0;
    EnterCriticalSection(&s->cs);
    Frame best; bool have = false;
    while (!s->q.empty() && s->q.front().ts <= t) { best = std::move(s->q.front()); s->q.pop_front(); have = true; }
    if (have && best.ts != s->lastGiven && (int)best.px.size() <= outLen) { memcpy(out, best.px.data(), best.px.size()); s->lastGiven = best.ts; got = 1; }
    LeaveCriticalSection(&s->cs);
    return got;
}

// Son : n flottants entrelaces pour le fil audio d'Unity (silence s'il n'y en a pas assez). Rend le nombre fourni.
extern "C" __declspec(dllexport) int VidStreamAudio(int id, float *out, int n)
{
    Stream *s = Get(id);
    if (!s) { for (int i = 0; i < n; i++) out[i] = 0; return 0; }
    EnterCriticalSection(&s->cs);
    size_t cap = s->ring.size(), k = cap == 0 ? 0 : (size_t)n < s->count ? (size_t)n : s->count;
    for (size_t i = 0; i < k; i++) out[i] = s->ring[(s->head + i) % cap];
    if (cap) { s->head = (s->head + k) % cap; s->count -= k; }
    LeaveCriticalSection(&s->cs);
    for (int i = (int)k; i < n; i++) out[i] = 0;
    return (int)k;
}

extern "C" __declspec(dllexport) void VidStreamClose(int id)
{
    if (!g_slockInit) return;
    EnterCriticalSection(&g_slock);
    Stream *s = Get(id);
    if (s) g_s[id] = nullptr;
    LeaveCriticalSection(&g_slock);
    if (!s) return;
    InterlockedExchange(&s->stop, 1);
    if (s->th) { WaitForSingleObject(s->th, 5000); CloseHandle(s->th); }
    DeleteCriticalSection(&s->cs);
    delete s;
}
