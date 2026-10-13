// MWCoopRT.dll, partie video : import d'une vraie video (mp4, avi, wmv, mov... ce que Windows sait lire) en film MWCoop
// (.mwv, meme format que le camescope du jeu : images JPEG + son mu-law, voir src\MWCoop\Sync\KauppaKamera.cs).
//
// Decodage par Media Foundation (Source Reader, conversion en RGB32 et en PCM 16 bits par Windows), images ramenees
// au format du film (4:3, bandes noires si besoin, moyenne des pixels), effet VHS, JPEG par WIC ; son ramene en mono
// 11 025 Hz. Un seul import a la fois, sur un fil a lui : le mod lance (VidImportStart) puis interroge (VidImportPoll).
// Media Foundation est chargee a la demande (/DELAYLOAD) : sans elle (Windows « N » sans Media Feature Pack), seul
// l'import echoue, avec un message.
#include <windows.h>
#include <shobjidl.h>
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
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "user32.lib")

namespace
{
    struct Job { std::wstring src, dst; int w, h, fps, maxSec, quality; int kind = 0, scale = 3; std::wstring logo; };   // kind : 0 import, 1 export ; logo : PNG pose en haut a gauche (export)

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


    // ---------------------------------------------------------------- export : film MWCoop (.mwv) -> .mp4 (H.264 + AAC)
    // Images JPEG decodees par WIC, agrandies (bilineaire, x scale), converties en NV12 ; cadence du film gardee (une
    // image par pas de 1/fps : la derniere image du film a cet instant). Son mu-law 11 025 Hz -> PCM 44 100 Hz mono
    // (interpolation lineaire) -> AAC. Ecrit par le Sink Writer de Media Foundation.
    bool DecodeJpeg(IWICImagingFactory *f, const BYTE *data, int n, std::vector<BYTE> &bgr, int &w, int &h)
    {
        IWICStream *st = nullptr; IWICBitmapDecoder *dec = nullptr; IWICBitmapFrameDecode *fr = nullptr; IWICFormatConverter *cv = nullptr;
        bool ok = false;
        if (SUCCEEDED(f->CreateStream(&st)) && SUCCEEDED(st->InitializeFromMemory((BYTE *)data, (DWORD)n))
            && SUCCEEDED(f->CreateDecoderFromStream(st, nullptr, WICDecodeMetadataCacheOnDemand, &dec)) && SUCCEEDED(dec->GetFrame(0, &fr))
            && SUCCEEDED(f->CreateFormatConverter(&cv)) && SUCCEEDED(cv->Initialize(fr, GUID_WICPixelFormat24bppBGR, WICBitmapDitherTypeNone, nullptr, 0, WICBitmapPaletteTypeCustom)))
        {
            UINT ww = 0, hh = 0;
            cv->GetSize(&ww, &hh);
            bgr.resize((size_t)ww * hh * 3);
            if (ww > 0 && hh > 0 && SUCCEEDED(cv->CopyPixels(nullptr, ww * 3, (UINT)bgr.size(), bgr.data()))) { w = (int)ww; h = (int)hh; ok = true; }
        }
        Rel(cv); Rel(fr); Rel(dec); Rel(st);
        return ok;
    }

    // Logo du mod (BGRA deja a sa taille), pose en haut a gauche, semi-transparent (demande de JD, 13/10)
    struct Logo { std::vector<BYTE> px; int w = 0, h = 0, x = 0, y = 0; float alpha = 0.6f; };

    // BGR (sw x sh) -> NV12 (ow x oh), agrandi en bilineaire, image entiere dans le cadre (bandes noires si besoin)
    void ToNv12(const std::vector<BYTE> &src, int sw, int sh, std::vector<BYTE> &out, int ow, int oh, const Logo *logo)
    {
        out.assign((size_t)ow * oh * 3 / 2, 0);
        std::vector<BYTE> rgb((size_t)ow * oh * 3, 0);
        double s = (double)ow / sw < (double)oh / sh ? (double)ow / sw : (double)oh / sh;
        int dw = (int)(sw * s), dh = (int)(sh * s), ox = (ow - dw) / 2, oy = (oh - dh) / 2;
        for (int y = 0; y < dh; y++)
        {
            double fy = (y + 0.5) / s - 0.5; if (fy < 0) fy = 0; int y0 = (int)fy, y1 = y0 + 1 < sh ? y0 + 1 : y0; double ty = fy - y0;
            for (int x = 0; x < dw; x++)
            {
                double fx = (x + 0.5) / s - 0.5; if (fx < 0) fx = 0; int x0 = (int)fx, x1 = x0 + 1 < sw ? x0 + 1 : x0; double tx = fx - x0;
                const BYTE *a = &src[((size_t)y0 * sw + x0) * 3], *b = &src[((size_t)y0 * sw + x1) * 3], *c = &src[((size_t)y1 * sw + x0) * 3], *d = &src[((size_t)y1 * sw + x1) * 3];
                BYTE *o = &rgb[((size_t)(oy + y) * ow + ox + x) * 3];
                for (int k = 0; k < 3; k++) o[k] = (BYTE)((a[k] * (1 - tx) + b[k] * tx) * (1 - ty) + (c[k] * (1 - tx) + d[k] * tx) * ty + 0.5);
            }
        }
        if (logo && logo->w > 0)
            for (int y = 0; y < logo->h && logo->y + y < oh; y++)
                for (int x = 0; x < logo->w && logo->x + x < ow; x++)
                {
                    const BYTE *l = &logo->px[((size_t)y * logo->w + x) * 4];
                    float a = l[3] / 255.0f * logo->alpha;
                    if (a <= 0.004f) continue;
                    BYTE *o = &rgb[((size_t)(logo->y + y) * ow + logo->x + x) * 3];
                    for (int k = 0; k < 3; k++) o[k] = (BYTE)(o[k] * (1 - a) + l[k] * a + 0.5f);
                }
        BYTE *Y = out.data(), *UV = out.data() + (size_t)ow * oh;
        for (int y = 0; y < oh; y++)
            for (int x = 0; x < ow; x++)
            {
                const BYTE *p = &rgb[((size_t)y * ow + x) * 3];
                int yy = (66 * p[2] + 129 * p[1] + 25 * p[0] + 128) / 256 + 16;
                Y[(size_t)y * ow + x] = (BYTE)(yy < 0 ? 0 : yy > 255 ? 255 : yy);
            }
        for (int y = 0; y < oh / 2; y++)
            for (int x = 0; x < ow / 2; x++)
            {
                int r = 0, g = 0, b = 0;
                for (int k = 0; k < 4; k++) { const BYTE *p = &rgb[((size_t)(y * 2 + (k >> 1)) * ow + x * 2 + (k & 1)) * 3]; b += p[0]; g += p[1]; r += p[2]; }
                r /= 4; g /= 4; b /= 4;
                int u = (-38 * r - 74 * g + 112 * b + 128) / 256 + 128, v = (112 * r - 94 * g - 18 * b + 128) / 256 + 128;
                UV[(size_t)y * ow + x * 2] = (BYTE)(u < 0 ? 0 : u > 255 ? 255 : u);
                UV[(size_t)y * ow + x * 2 + 1] = (BYTE)(v < 0 ? 0 : v > 255 ? 255 : v);
            }
    }

    float MuLawDecode(BYTE b)
    {
        int u = ~b & 0xFF, sign = u & 0x80, exp = (u >> 4) & 7, mant = u & 0x0F;
        int s = (((mant << 3) + 0x84) << exp) - 0x84;
        return (sign ? -s : s) / 32768.0f;
    }

    HRESULT WriteBuffer(IMFSinkWriter *sw, DWORD stream, const BYTE *data, DWORD n, LONGLONG time, LONGLONG dur)
    {
        IMFMediaBuffer *buf = nullptr; IMFSample *s = nullptr; BYTE *p = nullptr;
        HRESULT hr = MFCreateMemoryBuffer(n, &buf);
        if (SUCCEEDED(hr)) hr = buf->Lock(&p, nullptr, nullptr);
        if (SUCCEEDED(hr)) { memcpy(p, data, n); buf->Unlock(); hr = buf->SetCurrentLength(n); }
        if (SUCCEEDED(hr)) hr = MFCreateSample(&s);
        if (SUCCEEDED(hr)) hr = s->AddBuffer(buf);
        if (SUCCEEDED(hr)) hr = s->SetSampleTime(time);
        if (SUCCEEDED(hr)) hr = s->SetSampleDuration(dur);
        if (SUCCEEDED(hr)) hr = sw->WriteSample(stream, s);
        Rel(s); Rel(buf);
        return hr;
    }

    int RunExport(Job &j)
    {
        std::vector<BYTE> all;
        {
            FILE *fi = nullptr;
            if (_wfopen_s(&fi, j.src.c_str(), L"rb") != 0 || !fi) { Msg(L"film introuvable"); return -1; }
            fseek(fi, 0, SEEK_END); long n = ftell(fi); fseek(fi, 0, SEEK_SET);
            all.resize(n > 0 ? n : 0);
            if (n > 0) fread(all.data(), 1, n, fi);
            fclose(fi);
        }
        if (all.size() < 11 || memcmp(all.data(), "MWV1", 4) != 0) { Msg(L"film illisible"); return -1; }
        int fps = all[8] > 0 ? all[8] : 10, rate = all[9] | all[10] << 8;
        if (rate <= 0) rate = 11025;
        struct Fr { size_t off; int len; unsigned ms; };
        std::vector<Fr> frames; std::vector<BYTE> mu;
        for (size_t p = 11; p + 9 <= all.size();)
        {
            int kind = all[p]; unsigned ms; int n;
            memcpy(&ms, &all[p + 1], 4); memcpy(&n, &all[p + 5], 4);
            p += 9;
            if (n < 0 || p + n > all.size()) break;
            if (kind == 1) frames.push_back({ p, n, ms });
            else if (kind == 2) mu.insert(mu.end(), all.begin() + p, all.begin() + p + n);
            p += n;
        }
        if (frames.empty()) { Msg(L"aucune image dans ce film"); return -1; }
        IWICImagingFactory *wic = nullptr; IMFSinkWriter *sw = nullptr; IMFMediaType *t = nullptr;
        int ret = -1;
        HRESULT hr = MFStartup(MF_VERSION, MFSTARTUP_LITE);
        if (FAILED(hr)) { Msg(L"Media Foundation indisponible (0x%08X)", (unsigned)hr); return -1; }
        do
        {
            if (FAILED(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&wic)))) { Msg(L"WIC indisponible"); break; }
            std::vector<BYTE> img; int sw0 = 0, sh0 = 0;
            if (!DecodeJpeg(wic, &all[frames[0].off], frames[0].len, img, sw0, sh0)) { Msg(L"premiere image illisible"); break; }
            int ow = (sw0 * j.scale) & ~1, oh = (sh0 * j.scale) & ~1;
            Logo logo;
            if (!j.logo.empty())
            {
                IWICBitmapDecoder *ld = nullptr; IWICBitmapFrameDecode *lf = nullptr; IWICFormatConverter *lc = nullptr;
                if (SUCCEEDED(wic->CreateDecoderFromFilename(j.logo.c_str(), nullptr, GENERIC_READ, WICDecodeMetadataCacheOnDemand, &ld)) && SUCCEEDED(ld->GetFrame(0, &lf))
                    && SUCCEEDED(wic->CreateFormatConverter(&lc)) && SUCCEEDED(lc->Initialize(lf, GUID_WICPixelFormat32bppBGRA, WICBitmapDitherTypeNone, nullptr, 0, WICBitmapPaletteTypeCustom)))
                {
                    UINT lw0 = 0, lh0 = 0; lc->GetSize(&lw0, &lh0);
                    std::vector<BYTE> raw((size_t)lw0 * lh0 * 4);
                    if (lw0 > 0 && lh0 > 0 && SUCCEEDED(lc->CopyPixels(nullptr, lw0 * 4, (UINT)raw.size(), raw.data())))
                    {
                        // ~ un quart de la largeur, marge de 2,5 % ; reduit par moyenne des pixels (alpha pondere)
                        logo.w = ow * 24 / 100; logo.h = (int)((double)logo.w * lh0 / lw0);
                        logo.x = logo.y = ow * 25 / 1000;
                        logo.px.assign((size_t)logo.w * logo.h * 4, 0);
                        for (int y = 0; y < logo.h; y++)
                            for (int x = 0; x < logo.w; x++)
                            {
                                int x0 = (int)((long long)x * lw0 / logo.w), x1 = (int)((long long)(x + 1) * lw0 / logo.w), y0 = (int)((long long)y * lh0 / logo.h), y1 = (int)((long long)(y + 1) * lh0 / logo.h);
                                if (x1 <= x0) x1 = x0 + 1; if (y1 <= y0) y1 = y0 + 1;
                                double b = 0, g = 0, r = 0, a = 0; int n = 0;
                                for (int yy = y0; yy < y1; yy++) for (int xx = x0; xx < x1; xx++) { const BYTE *s = &raw[((size_t)yy * lw0 + xx) * 4]; double al = s[3]; b += s[0] * al; g += s[1] * al; r += s[2] * al; a += al; n++; }
                                BYTE *d = &logo.px[((size_t)y * logo.w + x) * 4];
                                if (a > 0) { d[0] = (BYTE)(b / a); d[1] = (BYTE)(g / a); d[2] = (BYTE)(r / a); }
                                d[3] = (BYTE)(a / n);
                            }
                    }
                }
                Rel(lc); Rel(lf); Rel(ld);
            }
            const UINT32 outRate = 44100;
            DeleteFileW(j.dst.c_str());
            hr = MFCreateSinkWriterFromURL(j.dst.c_str(), nullptr, nullptr, &sw);
            if (FAILED(hr)) { Msg(L"fichier .mp4 pas cree (0x%08X)", (unsigned)hr); break; }
            DWORD vIdx = 0, aIdx = 0;
            bool audio = !mu.empty();
            MFCreateMediaType(&t);
            t->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video); t->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_H264);
            t->SetUINT32(MF_MT_AVG_BITRATE, 3000000); t->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
            MFSetAttributeSize(t, MF_MT_FRAME_SIZE, ow, oh); MFSetAttributeRatio(t, MF_MT_FRAME_RATE, fps, 1); MFSetAttributeRatio(t, MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
            hr = sw->AddStream(t, &vIdx); Rel(t);
            if (FAILED(hr)) { Msg(L"encodeur H.264 indisponible (0x%08X)", (unsigned)hr); break; }
            MFCreateMediaType(&t);
            t->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video); t->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_NV12);
            t->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
            MFSetAttributeSize(t, MF_MT_FRAME_SIZE, ow, oh); MFSetAttributeRatio(t, MF_MT_FRAME_RATE, fps, 1); MFSetAttributeRatio(t, MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
            hr = sw->SetInputMediaType(vIdx, t, nullptr); Rel(t);
            if (FAILED(hr)) { Msg(L"format d'image refuse par l'encodeur (0x%08X)", (unsigned)hr); break; }
            if (audio)
            {
                MFCreateMediaType(&t);
                t->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio); t->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_AAC);
                t->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16); t->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, outRate);
                t->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, 1); t->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, 16000);
                hr = sw->AddStream(t, &aIdx); Rel(t);
                if (SUCCEEDED(hr))
                {
                    MFCreateMediaType(&t);
                    t->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio); t->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_PCM);
                    t->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16); t->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, outRate);
                    t->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, 1); t->SetUINT32(MF_MT_AUDIO_BLOCK_ALIGNMENT, 2); t->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, outRate * 2);
                    hr = sw->SetInputMediaType(aIdx, t, nullptr); Rel(t);
                }
                if (FAILED(hr)) { Msg(L"encodeur AAC indisponible (0x%08X)", (unsigned)hr); break; }
            }
            hr = sw->BeginWriting();
            if (FAILED(hr)) { Msg(L"ecriture refusee (0x%08X)", (unsigned)hr); break; }
            // duree : derniere image (+ un pas) ou fin du son
            double durMs = frames.back().ms + 1000.0 / fps;
            double sndMs = mu.size() * 1000.0 / rate;
            if (sndMs > durMs) durMs = sndMs;
            int nFrames = (int)(durMs * fps / 1000.0 + 0.5); if (nFrames < 1) nFrames = 1;
            LONGLONG step = 10000000LL / fps;
            std::vector<BYTE> nv12, pcm;
            size_t fi = 0, shown = (size_t)-1, muPos = 0;
            LONGLONG audioT = 0;
            double ratio = (double)rate / outRate;
            bool bad = false;
            for (int i = 0; i < nFrames && !g_cancel; i++)
            {
                double tms = i * 1000.0 / fps;
                while (fi + 1 < frames.size() && frames[fi + 1].ms <= tms) fi++;
                if (fi != shown)
                {
                    int w2, h2;
                    if (DecodeJpeg(wic, &all[frames[fi].off], frames[fi].len, img, w2, h2)) { sw0 = w2; sh0 = h2; ToNv12(img, sw0, sh0, nv12, ow, oh, &logo); }
                    else if (nv12.empty()) nv12.assign((size_t)ow * oh * 3 / 2, 128);
                    shown = fi;
                }
                if (FAILED(WriteBuffer(sw, vIdx, nv12.data(), (DWORD)nv12.size(), i * step, step))) { Msg(L"image %d refusee", i); bad = true; break; }
                // son jusqu'a la fin de cette image (par paquets de 0,1 s)
                LONGLONG until = (i + 1) * step;
                while (audio && audioT < until)
                {
                    const int chunk = 4410;
                    pcm.resize(chunk * 2);
                    for (int k = 0; k < chunk; k++)
                    {
                        double src = (double)(muPos + k) * ratio;
                        size_t a0 = (size_t)src; double fr2 = src - a0;
                        float v0 = a0 < mu.size() ? MuLawDecode(mu[a0]) : 0.f, v1 = a0 + 1 < mu.size() ? MuLawDecode(mu[a0 + 1]) : v0;
                        float v = (float)(v0 * (1 - fr2) + v1 * fr2);
                        int sv = (int)(v * 32767.0f); if (sv > 32767) sv = 32767; if (sv < -32768) sv = -32768;
                        pcm[k * 2] = (BYTE)(sv & 0xFF); pcm[k * 2 + 1] = (BYTE)((sv >> 8) & 0xFF);
                    }
                    LONGLONG d = (LONGLONG)chunk * 10000000LL / outRate;
                    if (FAILED(WriteBuffer(sw, aIdx, pcm.data(), (DWORD)pcm.size(), audioT, d))) { audio = false; break; }
                    audioT += d; muPos += chunk;
                }
                g_progress = (float)(i + 1) / nFrames;
            }
            if (g_cancel) { Msg(L"annule"); sw->Finalize(); break; }
            if (bad) break;
            hr = sw->Finalize();
            if (FAILED(hr)) { Msg(L"fin d'ecriture refusee (0x%08X)", (unsigned)hr); break; }
            Msg(L"%d images, %.1f s, %dx%d", nFrames, durMs / 1000.0, ow, oh);
            ret = 0;
        } while (false);
        Rel(sw); Rel(wic); Rel(t);
        MFShutdown();
        if (ret != 0) DeleteFileW(j.dst.c_str());
        return ret;
    }

    // ---------------------------------------------------------------- choix d'un dossier (fenetre de Windows, fil a elle)
    volatile LONG g_pickState;   // 0 rien, 1 ouverte, 2 choisi, -1 annule
    wchar_t g_pickPath[1024];
    struct Pick { std::wstring init; HWND owner; };

    DWORD WINAPI PickThread(LPVOID p)
    {
        Pick *pk = (Pick *)p;
        LONG res = -1;
        if (SUCCEEDED(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE)))
        {
            IFileOpenDialog *d = nullptr;
            if (SUCCEEDED(CoCreateInstance(CLSID_FileOpenDialog, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&d))))
            {
                FILEOPENDIALOGOPTIONS o = 0;
                d->GetOptions(&o);
                d->SetOptions(o | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
                d->SetTitle(L"MWCoop : dossier des videos");
                IShellItem *it = nullptr;
                if (!pk->init.empty() && SUCCEEDED(SHCreateItemFromParsingName(pk->init.c_str(), nullptr, IID_PPV_ARGS(&it)))) { d->SetFolder(it); it->Release(); it = nullptr; }
                if (SUCCEEDED(d->Show(pk->owner)) && SUCCEEDED(d->GetResult(&it)))
                {
                    PWSTR path = nullptr;
                    if (SUCCEEDED(it->GetDisplayName(SIGDN_FILESYSPATH, &path)) && path)
                    {
                        EnterCriticalSection(&g_lock); wcsncpy_s(g_pickPath, path, _TRUNCATE); LeaveCriticalSection(&g_lock);
                        CoTaskMemFree(path); res = 2;
                    }
                    it->Release();
                }
                d->Release();
            }
            CoUninitialize();
        }
        delete pk;
        InterlockedExchange(&g_pickState, res);
        return 0;
    }

    // Sans Media Foundation, le chargement differe leve une exception SEH : attrapee ici (pas d'objets C++ dans ce cadre).
    int SafeRun(Job *j)
    {
        __try { return j->kind == 1 ? RunExport(*j) : Run(*j); }
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

// Lance l'export d'un film MWCoop (.mwv) en .mp4 (image agrandie x scale, logo PNG en haut a gauche si donne). Meme suivi que l'import (VidImportPoll).
// 0 : parti ; -1 : un import ou un export est deja en cours ; -2 : fil pas cree.
extern "C" __declspec(dllexport) int VidExportStart(const wchar_t *src, const wchar_t *dst, int scale, const wchar_t *logo)
{
    if (!g_lockInit) { InitializeCriticalSection(&g_lock); g_lockInit = true; }
    if (g_state == 1) return -1;
    if (g_thread) { CloseHandle(g_thread); g_thread = nullptr; }
    Job *j = new Job{ src, dst, 0, 0, 0, 0, 0 };
    j->kind = 1; j->scale = scale < 1 ? 1 : scale > 6 ? 6 : scale; j->logo = logo ? logo : L"";
    g_cancel = 0; g_progress = 0; g_msg[0] = 0;
    InterlockedExchange(&g_state, 1);
    g_thread = CreateThread(nullptr, 0, Thread, j, 0, nullptr);
    if (!g_thread) { delete j; InterlockedExchange(&g_state, -1); return -2; }
    return 0;
}

// Ouvre la fenetre de Windows pour choisir un dossier (sur un fil a elle : le jeu continue). 0 : ouverte ; -1 : deja ouverte.
extern "C" __declspec(dllexport) int VidPickFolderStart(const wchar_t *initial)
{
    if (!g_lockInit) { InitializeCriticalSection(&g_lock); g_lockInit = true; }
    if (g_pickState == 1) return -1;
    Pick *pk = new Pick{ initial ? initial : L"", GetForegroundWindow() };
    InterlockedExchange(&g_pickState, 1);
    HANDLE h = CreateThread(nullptr, 0, PickThread, pk, 0, nullptr);
    if (!h) { delete pk; InterlockedExchange(&g_pickState, -1); return -2; }
    CloseHandle(h);
    return 0;
}

// Etat du choix : 0 rien, 1 ouverte, 2 dossier choisi (dans out), -1 annule. Apres 2 ou -1, revient a 0.
extern "C" __declspec(dllexport) int VidPickFolderPoll(wchar_t *out, int n)
{
    if (!g_lockInit) { InitializeCriticalSection(&g_lock); g_lockInit = true; }
    LONG s = g_pickState;
    if (s == 2 && out && n > 0) { EnterCriticalSection(&g_lock); wcsncpy_s(out, n, g_pickPath, _TRUNCATE); LeaveCriticalSection(&g_lock); }
    if (s == 2 || s == -1) InterlockedExchange(&g_pickState, 0);
    return s;
}
