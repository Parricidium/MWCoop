// MWCoopRT.dll, partie video : import d'une vraie video (mp4, avi, wmv, mov... ce que Windows sait lire) en film MWCoop
// (.mwv, meme format que le camescope du jeu : images JPEG + son mu-law, voir src\MWCoop\Sync\KauppaKamera.cs).
//
// Decodage par Media Foundation (Source Reader, conversion en RGB32 et en PCM 16 bits par Windows), images ramenees
// au format du film (4:3, bandes noires si besoin, moyenne des pixels), effet VHS, JPEG par WIC ; son ramene en mono
// 11 025 Hz. Un seul import a la fois, sur un fil a lui : le mod lance (VidImportStart) puis interroge (VidImportPoll).
// Media Foundation est chargee a la demande (/DELAYLOAD) : sans elle (Windows « N » sans Media Feature Pack), seul
// l'import echoue, avec un message.
#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <mferror.h>
#include <wincodec.h>
#include <stdio.h>
#include <stdarg.h>
#include <string>
#include <vector>
#pragma comment(lib, "mfplat.lib")
#pragma comment(lib, "mfreadwrite.lib")
#pragma comment(lib, "mfuuid.lib")
#pragma comment(lib, "windowscodecs.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "oleaut32.lib")
#pragma comment(lib, "propsys.lib")

namespace
{
    struct Job { std::wstring src, dst; int w, h, fps, maxSec, quality; };

    volatile LONG g_state;          // 0 rien, 1 en cours, 2 fini, -1 echec
    volatile LONG g_cancel;
    volatile float g_progress;
    wchar_t g_msg[512];
    CRITICAL_SECTION g_lock;
    bool g_lockInit;
    HANDLE g_thread;

    void Msg(const wchar_t *fmt, ...)
    {
        wchar_t b[512];
        va_list a; va_start(a, fmt); _vsnwprintf_s(b, _countof(b), _TRUNCATE, fmt, a); va_end(a);
        EnterCriticalSection(&g_lock); wcscpy_s(g_msg, b); LeaveCriticalSection(&g_lock);
    }

    template <class T> void Rel(T *&p) { if (p) { p->Release(); p = nullptr; } }

    // ---------------------------------------------------------------- image
    unsigned g_rnd = 12345;
    inline int Rnd(int n) { g_rnd = g_rnd * 1103515245u + 12345u; return (int)((g_rnd >> 16) % (unsigned)n); }

    // BGRA (lignes de pitch octets, du haut vers le bas) -> BGR w x h, image entiere dans le cadre (bandes noires),
    // chaque pixel du cadre = moyenne de son bloc de la source.
    void Fit(const BYTE *src, int sw, int sh, int pitch, std::vector<BYTE> &dst, int w, int h)
    {
        dst.assign((size_t)w * h * 3, 0);
        double s = (double)w / sw < (double)h / sh ? (double)w / sw : (double)h / sh;
        int dw = (int)(sw * s + 0.5), dh = (int)(sh * s + 0.5);
        if (dw < 1) dw = 1; if (dh < 1) dh = 1;
        int ox = (w - dw) / 2, oy = (h - dh) / 2;
        for (int y = 0; y < dh; y++)
        {
            int y0 = (int)((long long)y * sh / dh), y1 = (int)((long long)(y + 1) * sh / dh);
            if (y1 <= y0) y1 = y0 + 1;
            for (int x = 0; x < dw; x++)
            {
                int x0 = (int)((long long)x * sw / dw), x1 = (int)((long long)(x + 1) * sw / dw);
                if (x1 <= x0) x1 = x0 + 1;
                unsigned b = 0, g = 0, r = 0, n = 0;
                for (int yy = y0; yy < y1; yy += 1 + (y1 - y0) / 4)   // (au plus ~4 lignes et ~4 colonnes par bloc)
                {
                    const BYTE *row = src + (size_t)yy * pitch;
                    for (int xx = x0; xx < x1; xx += 1 + (x1 - x0) / 4) { const BYTE *p = row + xx * 4; b += p[0]; g += p[1]; r += p[2]; n++; }
                }
                BYTE *d = &dst[((size_t)(oy + y) * w + ox + x) * 3];
                d[0] = (BYTE)(b / n); d[1] = (BYTE)(g / n); d[2] = (BYTE)(r / n);
            }
        }
    }

    // Effet VHS (comme le camescope) : chrominance floutee et decalee a droite, saturation baissee, noirs releves, neige,
    // une ligne sur deux plus sombre, bas de l'image qui tremble. BGR du haut vers le bas.
    void VhsLook(std::vector<BYTE> &p, int w, int h, int noise)
    {
        std::vector<BYTE> src(p);
        for (int y = 0; y < h; y++)
        {
            int o = y * w * 3;
            int shift = y >= h - 6 ? Rnd(11) - 5 : 0;
            float dark = (y & 1) ? 0.93f : 1.0f;
            float cu = 0, cv = 0;
            for (int x = 0; x < w; x++)
            {
                int xa = x - shift; if (xa < 0) xa = 0; if (xa >= w) xa = w - 1;
                int xb = x - shift - 2; if (xb < 0) xb = 0; if (xb >= w) xb = w - 1;
                const BYTE *c = &src[o + xa * 3], *cc = &src[o + xb * 3];
                float yy = 0.299f * c[2] + 0.587f * c[1] + 0.114f * c[0];
                float cy = 0.299f * cc[2] + 0.587f * cc[1] + 0.114f * cc[0];
                cu = cu * 0.62f + (cc[0] - cy) * 0.38f;
                cv = cv * 0.62f + (cc[2] - cy) * 0.38f;
                yy = (14.0f + yy * 0.9f + (noise > 0 ? Rnd(2 * noise + 1) - noise : 0)) * dark;
                float u = cu * 0.78f, v = cv * 0.78f;
                float rr = yy + v + 3, bb = yy + u - 4, gg = (yy - 0.299f * (yy + v) - 0.114f * (yy + u)) / 0.587f;
                BYTE *d = &p[o + x * 3];
                d[0] = (BYTE)(bb < 0 ? 0 : bb > 255 ? 255 : bb);
                d[1] = (BYTE)(gg < 0 ? 0 : gg > 255 ? 255 : gg);
                d[2] = (BYTE)(rr < 0 ? 0 : rr > 255 ? 255 : rr);
            }
        }
    }

    bool Jpeg(IWICImagingFactory *f, std::vector<BYTE> &bgr, int w, int h, float q, std::vector<BYTE> &out)
    {
        IStream *st = nullptr; IWICBitmapEncoder *enc = nullptr; IWICBitmapFrameEncode *fr = nullptr; IPropertyBag2 *props = nullptr;
        bool ok = false;
        if (SUCCEEDED(CreateStreamOnHGlobal(nullptr, TRUE, &st)) && SUCCEEDED(f->CreateEncoder(GUID_ContainerFormatJpeg, nullptr, &enc))
            && SUCCEEDED(enc->Initialize(st, WICBitmapEncoderNoCache)) && SUCCEEDED(enc->CreateNewFrame(&fr, &props)))
        {
            PROPBAG2 opt = {}; opt.pstrName = (LPOLESTR)L"ImageQuality";
            VARIANT v; VariantInit(&v); v.vt = VT_R4; v.fltVal = q;
            props->Write(1, &opt, &v);
            WICPixelFormatGUID pf = GUID_WICPixelFormat24bppBGR;
            if (SUCCEEDED(fr->Initialize(props)) && SUCCEEDED(fr->SetSize(w, h)) && SUCCEEDED(fr->SetPixelFormat(&pf)) && pf == GUID_WICPixelFormat24bppBGR
                && SUCCEEDED(fr->WritePixels(h, w * 3, w * 3 * h, bgr.data())) && SUCCEEDED(fr->Commit()) && SUCCEEDED(enc->Commit()))
            {
                HGLOBAL hg = nullptr; STATSTG ss = {};
                if (SUCCEEDED(GetHGlobalFromStream(st, &hg)) && SUCCEEDED(st->Stat(&ss, STATFLAG_NONAME)))
                {
                    void *p = GlobalLock(hg);
                    if (p) { out.assign((BYTE *)p, (BYTE *)p + (size_t)ss.cbSize.QuadPart); GlobalUnlock(hg); ok = true; }
                }
            }
        }
        Rel(props); Rel(fr); Rel(enc); Rel(st);
        return ok;
    }

    BYTE MuLaw(float f)
    {
        if (f > 1) f = 1; if (f < -1) f = -1;
        int s = (int)(f * 32635.0f), sign = 0;
        if (s < 0) { sign = 0x80; s = -s; }
        s += 0x84; if (s > 32767) s = 32767;
        int exp = 7;
        for (int mask = 0x4000; (s & mask) == 0 && exp > 0; exp--, mask >>= 1) {}
        int mant = (s >> (exp + 3)) & 0x0F;
        return (BYTE)~(sign | (exp << 4) | mant);
    }

    void Block(FILE *o, int kind, LONGLONG ts, const BYTE *d, int n)
    {
        BYTE k = (BYTE)kind; unsigned ms = (unsigned)(ts > 0 ? ts / 10000 : 0);
        fwrite(&k, 1, 1, o); fwrite(&ms, 4, 1, o); fwrite(&n, 4, 1, o); fwrite(d, 1, n, o);
    }

    // ---------------------------------------------------------------- import
    int Run(Job &j)
    {
        const int rate = 11025;
        IMFAttributes *at = nullptr; IMFSourceReader *rd = nullptr; IWICImagingFactory *wic = nullptr; IMFMediaType *t = nullptr;
        FILE *o = nullptr;
        int ret = -1, frames = 0; long long sound = 0;
        std::wstring tmp = j.dst + L".osa";
        HRESULT hr = MFStartup(MF_VERSION, MFSTARTUP_LITE);
        if (FAILED(hr)) { Msg(L"Media Foundation indisponible (0x%08X)", (unsigned)hr); return -1; }
        do
        {
            if (FAILED(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&wic)))) { Msg(L"WIC indisponible"); break; }
            MFCreateAttributes(&at, 2);
            at->SetUINT32(MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, TRUE);
            hr = MFCreateSourceReaderFromURL(j.src.c_str(), at, &rd);
            if (FAILED(hr)) { Msg(L"fichier illisible par Windows (0x%08X)", (unsigned)hr); break; }
            rd->SetStreamSelection((DWORD)MF_SOURCE_READER_ALL_STREAMS, FALSE);
            // image : RGB32
            bool hasVideo = false, hasAudio = false;
            MFCreateMediaType(&t);
            t->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video); t->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_RGB32);
            if (SUCCEEDED(rd->SetCurrentMediaType((DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM, nullptr, t))) { rd->SetStreamSelection((DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM, TRUE); hasVideo = true; }
            Rel(t);
            // son : PCM 16 bits (frequence et voies d'origine)
            MFCreateMediaType(&t);
            t->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio); t->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_PCM); t->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
            if (SUCCEEDED(rd->SetCurrentMediaType((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM, nullptr, t))) { rd->SetStreamSelection((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM, TRUE); hasAudio = true; }
            Rel(t);
            if (!hasVideo) { Msg(L"pas d'image lisible dans ce fichier"); break; }
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
            UINT32 ach = 2, arate = 44100;
            if (hasAudio)
            {
                IMFMediaType *ct = nullptr;
                if (SUCCEEDED(rd->GetCurrentMediaType((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM, &ct)))
                { ach = MFGetAttributeUINT32(ct, MF_MT_AUDIO_NUM_CHANNELS, 2); arate = MFGetAttributeUINT32(ct, MF_MT_AUDIO_SAMPLES_PER_SECOND, 44100); ct->Release(); }
                if (ach == 0) ach = 1;
            }
            LONGLONG dur = 0;
            PROPVARIANT pv; PropVariantInit(&pv);
            if (SUCCEEDED(rd->GetPresentationAttribute((DWORD)MF_SOURCE_READER_MEDIASOURCE, MF_PD_DURATION, &pv))) dur = (LONGLONG)pv.uhVal.QuadPart;
            PropVariantClear(&pv);
            LONGLONG maxTs = (LONGLONG)j.maxSec * 10000000LL;
            LONGLONG total = dur > 0 && dur < maxTs ? dur : maxTs;
            if (_wfopen_s(&o, tmp.c_str(), L"wb") != 0 || !o) { Msg(L"ecriture impossible : %s", tmp.c_str()); break; }
            fwrite("MWV1", 1, 4, o);
            unsigned short hw = (unsigned short)j.w, hh = (unsigned short)j.h, hr16 = (unsigned short)rate; BYTE hf = (BYTE)j.fps;
            fwrite(&hw, 2, 1, o); fwrite(&hh, 2, 1, o); fwrite(&hf, 1, 1, o); fwrite(&hr16, 2, 1, o);
            LONGLONG period = 10000000LL / j.fps, nextT = 0, vts = 0, ats = 0;
            bool vDone = false, aDone = !hasAudio;
            double step = (double)arate / rate, acc = 0; float sum = 0; int sn = 0;
            std::vector<BYTE> img, jpg, mu;
            ret = 0;
            while ((!vDone || !aDone) && !g_cancel)
            {
                bool doVideo = !vDone && (aDone || vts <= ats);
                DWORD stream = doVideo ? (DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM : (DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM;
                DWORD flags = 0; LONGLONG ts = 0; IMFSample *s = nullptr;
                hr = rd->ReadSample(stream, 0, nullptr, &flags, &ts, &s);
                if (FAILED(hr)) { if (doVideo) vDone = true; else aDone = true; continue; }
                if (flags & MF_SOURCE_READERF_ENDOFSTREAM) { if (doVideo) vDone = true; else aDone = true; }
                if (flags & MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED && doVideo) readVideoType();
                if (!s) { if (doVideo) vts += period; else ats += period; continue; }
                if (ts > maxTs) { if (doVideo) vDone = true; else aDone = true; s->Release(); continue; }
                if (doVideo)
                {
                    vts = ts;
                    if (ts + period / 2 >= nextT && sw > 0 && sh > 0)
                    {
                        IMFMediaBuffer *mb = nullptr;
                        if (SUCCEEDED(s->ConvertToContiguousBuffer(&mb)))
                        {
                            IMF2DBuffer *b2 = nullptr; BYTE *scan0 = nullptr; LONG pitch = 0; BYTE *raw = nullptr; DWORD len = 0;
                            bool locked2d = SUCCEEDED(mb->QueryInterface(IID_PPV_ARGS(&b2))) && SUCCEEDED(b2->Lock2D(&scan0, &pitch));
                            if (!locked2d && SUCCEEDED(mb->Lock(&raw, nullptr, &len)))
                            {
                                pitch = stride;
                                scan0 = stride < 0 ? raw + (size_t)(-stride) * (sh - 1) : raw;
                            }
                            if (scan0)
                            {
                                // (pitch negatif : image de bas en haut ; Fit lit ligne par ligne a partir de scan0)
                                if (pitch < 0)
                                {
                                    std::vector<BYTE> flip((size_t)sw * 4 * sh);
                                    for (UINT32 y = 0; y < sh; y++) memcpy(&flip[(size_t)y * sw * 4], scan0 + (LONG_PTR)y * pitch, (size_t)sw * 4);
                                    Fit(flip.data(), sw, sh, sw * 4, img, j.w, j.h);
                                }
                                else Fit(scan0, sw, sh, pitch, img, j.w, j.h);
                                VhsLook(img, j.w, j.h, 3);   // (neige legere : elle pese lourd en JPEG)
                                if (Jpeg(wic, img, j.w, j.h, j.quality / 100.0f, jpg)) { Block(o, 1, ts, jpg.data(), (int)jpg.size()); frames++; }
                            }
                            if (locked2d) b2->Unlock2D(); else if (raw) mb->Unlock();
                            Rel(b2); mb->Release();
                        }
                        nextT += period;
                        if (ts > nextT + period) nextT = ts + period;   // (trou dans la video)
                    }
                }
                else
                {
                    ats = ts;
                    IMFMediaBuffer *mb = nullptr;
                    if (SUCCEEDED(s->ConvertToContiguousBuffer(&mb)))
                    {
                        BYTE *raw = nullptr; DWORD len = 0;
                        if (SUCCEEDED(mb->Lock(&raw, nullptr, &len)))
                        {
                            const short *pcm = (const short *)raw;
                            DWORD n = len / 2 / ach;
                            mu.clear();
                            for (DWORD i = 0; i < n; i++)
                            {
                                float m = 0; for (UINT32 c = 0; c < ach; c++) m += pcm[i * ach + c] / 32768.0f;
                                sum += m / ach; sn++; acc += 1.0;
                                if (acc >= step) { acc -= step; mu.push_back(MuLaw(sum / sn)); sum = 0; sn = 0; }
                            }
                            if (!mu.empty()) { Block(o, 2, ts, mu.data(), (int)mu.size()); sound += (long long)mu.size(); }
                            mb->Unlock();
                        }
                        mb->Release();
                    }
                }
                s->Release();
                LONGLONG cur = vts > ats || aDone ? vts : ats;
                if (total > 0) g_progress = (float)((double)cur / total > 1.0 ? 1.0 : (double)cur / total);
            }
            if (g_cancel) { Msg(L"annule"); ret = -1; break; }
            if (frames == 0) { Msg(L"aucune image decodee"); ret = -1; break; }
            Msg(L"%d images, %.1f s, son %.1f s", frames, (double)(vts > ats ? vts : ats) / 1e7, (double)sound / rate);
        } while (false);
        if (o) fclose(o);
        Rel(rd); Rel(at); Rel(wic); Rel(t);
        MFShutdown();
        if (ret == 0) { if (!MoveFileExW(tmp.c_str(), j.dst.c_str(), MOVEFILE_REPLACE_EXISTING)) { Msg(L"renommage impossible : %s", j.dst.c_str()); ret = -1; } }
        if (ret != 0) DeleteFileW(tmp.c_str());
        return ret;
    }

    // Sans Media Foundation, le chargement differe leve une exception SEH : attrapee ici (pas d'objets C++ dans ce cadre).
    int SafeRun(Job *j)
    {
        __try { return Run(*j); }
        __except (EXCEPTION_EXECUTE_HANDLER) { Msg(L"Media Foundation absente de ce Windows (editions N : installer le Media Feature Pack)"); return -1; }
    }

    DWORD WINAPI Thread(LPVOID p)
    {
        Job *j = (Job *)p;
        CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        int r = SafeRun(j);
        CoUninitialize();
        delete j;
        g_progress = 1.0f;
        InterlockedExchange(&g_state, r == 0 ? 2 : -1);
        return 0;
    }
}

// Lance l'import (src -> dst, image w x h, fps images/s, maxSec secondes au plus, qualite JPEG 1-100).
// 0 : parti ; -1 : un import est deja en cours ; -2 : fil pas cree.
extern "C" __declspec(dllexport) int VidImportStart(const wchar_t *src, const wchar_t *dst, int w, int h, int fps, int maxSec, int quality)
{
    if (!g_lockInit) { InitializeCriticalSection(&g_lock); g_lockInit = true; }
    if (g_state == 1) return -1;
    if (g_thread) { CloseHandle(g_thread); g_thread = nullptr; }
    Job *j = new Job{ src, dst, w < 16 ? 16 : w, h < 16 ? 16 : h, fps < 1 ? 1 : fps > 30 ? 30 : fps, maxSec < 1 ? 1 : maxSec, quality < 10 ? 10 : quality > 95 ? 95 : quality };
    g_cancel = 0; g_progress = 0; g_msg[0] = 0;
    InterlockedExchange(&g_state, 1);
    g_thread = CreateThread(nullptr, 0, Thread, j, 0, nullptr);
    if (!g_thread) { delete j; InterlockedExchange(&g_state, -1); return -2; }
    return 0;
}

// Etat : 0 rien, 1 en cours, 2 fini, -1 echec ; progression 0..1 ; message (resume ou raison de l'echec).
extern "C" __declspec(dllexport) int VidImportPoll(float *progress, wchar_t *msg, int msgLen)
{
    if (!g_lockInit) { InitializeCriticalSection(&g_lock); g_lockInit = true; }
    if (progress) *progress = g_progress;
    if (msg && msgLen > 0) { EnterCriticalSection(&g_lock); wcsncpy_s(msg, msgLen, g_msg, _TRUNCATE); LeaveCriticalSection(&g_lock); }
    return g_state;
}

extern "C" __declspec(dllexport) void VidImportCancel() { InterlockedExchange(&g_cancel, 1); }
