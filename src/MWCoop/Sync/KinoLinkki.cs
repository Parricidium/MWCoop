using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace MWCoop
{
    // Bobine « lien » du cinema (demande de JD, 22/10 : « une bobine ou ils peuvent mettre un lien, et ca lit la video ») :
    // Data = "url:<lien>~<titre>". Mise sur un projecteur, chaque joueur lit le lien lui-meme (MWCoopRT.dll : VidStream*,
    // Media Foundation en continu), rien n'est envoye entre joueurs ; l'hote donne l'instant comme pour un film (@kino 2).
    // Lien direct vers une video (.mp4 surtout : archive.org, Dropbox, un serveur...) ; YouTube, VK Video ou Rutube ne
    // donnent pas leur fichier : message « pas une video directe ».
    // Montage d'une bobine (R) : IMPORTER UNE VIDEO... (fenetre de Windows, la video est convertie et partagee comme sur une
    // cassette, en plus grand et sans effet VHS) et LIEN (champ ou coller l'adresse). Tchat : « /lien <adresse> » (ou /link)
    // avec la bobine en main.
    public static partial class Kauppa
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)] delegate int DVSOpen(string url, int w, int h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)] delegate int DVSInfo(int id, int[] info, StringBuilder msg, int msgLen);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DVSSeek(int id, int ms);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DVSFrame(int id, int ms, byte[] buf, int len);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DVSAudio(int id, float[] buf, int n);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DVSClose(int id);
        static DVSOpen vsOpen; static DVSInfo vsInfo; static DVSSeek vsSeek; static DVSFrame vsFrame; static DVSAudio vsAudio; static DVSClose vsClose; static DVPick vPickFile;
        static bool vsTried;

        static bool BindStream()
        {
            if (vsTried) return vsOpen != null;
            vsTried = true;
            if (!BindVideo()) return false;
            try
            {
                string own = Path.Combine(Path.GetDirectoryName(Config.IniPath), "MWCoopRT.dll");
                if (!File.Exists(own)) own = "MWCoopRT.dll";
                IntPtr h = LoadLibraryW(own);
                Func<string, IntPtr> P = n => GetProcAddress(h, n);
                IntPtr po = P("VidStreamOpen"), pi = P("VidStreamInfo"), ps = P("VidStreamSeek"), pf = P("VidStreamFrame"), pa = P("VidStreamAudio"), pc = P("VidStreamClose"), pk = P("VidPickFileStart");
                if (po == IntPtr.Zero || pi == IntPtr.Zero || ps == IntPtr.Zero || pf == IntPtr.Zero || pa == IntPtr.Zero || pc == IntPtr.Zero) { L("cinema : MWCoopRT.dll sans lecture en continu (ancienne version ?)"); return false; }
                vsOpen = (DVSOpen)Marshal.GetDelegateForFunctionPointer(po, typeof(DVSOpen));
                vsInfo = (DVSInfo)Marshal.GetDelegateForFunctionPointer(pi, typeof(DVSInfo));
                vsSeek = (DVSSeek)Marshal.GetDelegateForFunctionPointer(ps, typeof(DVSSeek));
                vsFrame = (DVSFrame)Marshal.GetDelegateForFunctionPointer(pf, typeof(DVSFrame));
                vsAudio = (DVSAudio)Marshal.GetDelegateForFunctionPointer(pa, typeof(DVSAudio));
                vsClose = (DVSClose)Marshal.GetDelegateForFunctionPointer(pc, typeof(DVSClose));
                if (pk != IntPtr.Zero) vPickFile = (DVPick)Marshal.GetDelegateForFunctionPointer(pk, typeof(DVPick));
                return true;
            }
            catch (Exception e) { L("cinema : lecture en continu indisponible : " + e.Message); return false; }
        }

        static string LinkOf(string content) { string f = TapeFilm(content); return f.StartsWith("url:") ? f.Substring(4) : null; }

        // ---------------------------------------------------------------- sur l'ecran d'une salle (appele par KinoShow)
        class LinkPlay { public int Id = -1; public string Url; public int W, H; public byte[] Buf; public Texture2D Tex; public Material Mat; public AudioClip Clip; public bool Started, Seeked, Told; public float OpenedAt; }

        static void LinkShow(Projector p, string url, float now)
        {
            LinkPlay lp = p.Link;
            if (lp != null && lp.Url != url) { LinkStop(p); lp = null; }
            if (lp == null)
            {
                if (!BindStream()) { if (!p.LinkFailTold) { p.LinkFailTold = true; Hud.Toast(Lang.T("Lecture des liens indisponible (MWCoopRT.dll)", "Link playback unavailable (MWCoopRT.dll)")); } return; }
                int w = Mathf.Clamp(Config.GetInt("Kino", "LienLargeur", 854), 160, 1920), h = Mathf.Clamp(Config.GetInt("Kino", "LienHauteur", 480), 90, 1080);
                lp = new LinkPlay { Url = url, W = w, H = h, Buf = new byte[w * h * 4], OpenedAt = now };
                lp.Id = vsOpen(url, w, h);
                p.Link = lp;
                L("cinema : salle " + p.Sali + " ouvre le lien " + url + " (lecteur " + lp.Id + ")");
                if (lp.Id < 0) return;
            }
            if (lp.Id < 0) return;
            var info = new int[3]; var msg = new StringBuilder(256);
            int st = vsInfo(lp.Id, info, msg, 256);
            if (st < 0)
            {
                if (!lp.Told) { lp.Told = true; Hud.Toast(Lang.T("Bobine « lien » : ", "Link reel: ") + msg); Chat.System(Lang.T("Lien illisible : ", "Link not playable: ") + msg + Lang.T(" — il faut un lien direct vers une vidéo (.mp4), pas une page YouTube.", " — it needs a direct link to a video file (.mp4), not a YouTube page.")); L("cinema : lien illisible : " + msg); }
                if (BagAuthority) SetProjector(p, p.Content, false, 0, true);
                return;
            }
            if (st == 0) return;   // (ouverture : le lien se charge)
            int ms = Mathf.Max(0, Mathf.RoundToInt((now - p.Start) * 1000f));
            if (!lp.Started)
            {
                lp.Started = true;
                L("cinema : lien ouvert : " + msg);
                if (ms > 1500) { vsSeek(lp.Id, ms); lp.Seeked = true; }   // (arrive en cours de seance : la ou en est la salle)
                lp.Tex = new Texture2D(lp.W, lp.H, TextureFormat.BGRA32, false) { name = "kino_lien", wrapMode = TextureWrapMode.Clamp };
                Shader us = Shader.Find("Unlit/Texture");
                lp.Mat = new Material(us != null ? us : p.Idle.shader) { mainTexture = lp.Tex, name = "kino_lien" };
                lp.Mat.mainTextureScale = new Vector2(1f, -1f); lp.Mat.mainTextureOffset = new Vector2(0f, 1f);   // (lignes du haut vers le bas)
                int ch = Mathf.Clamp(info[0], 1, 2), rate = Mathf.Clamp(info[1], 8000, 96000);
                int id = lp.Id;
                lp.Clip = AudioClip.Create("kino_lien", rate * 600, ch, rate, true, data => { if (vsAudio != null) vsAudio(id, data, data.Length); });
                p.Audio.Stop();
                p.Audio.clip = lp.Clip; p.Audio.loop = true; p.Audio.volume = 1f;
                p.Audio.Play();
            }
            if (vsFrame(lp.Id, ms, lp.Buf, lp.Buf.Length) == 1)
            {
                lp.Tex.LoadRawTextureData(lp.Buf);
                lp.Tex.Apply(false);
                if (p.Screen.sharedMaterial != lp.Mat) p.Screen.sharedMaterial = lp.Mat;
            }
            if (st == 2 && BagAuthority && now - lp.OpenedAt > 3f) SetProjector(p, p.Content, false, 0, true);   // (fin de la video)
        }

        static void LinkStop(Projector p)
        {
            LinkPlay lp = p.Link;
            if (lp == null) return;
            p.Link = null;
            if (p.Audio != null && p.Audio.clip == lp.Clip) { p.Audio.Stop(); p.Audio.clip = null; }
            if (lp.Id >= 0 && vsClose != null) vsClose(lp.Id);
            if (lp.Clip != null) UnityEngine.Object.Destroy(lp.Clip);
            if (lp.Mat != null) UnityEngine.Object.Destroy(lp.Mat);
            if (lp.Tex != null) UnityEngine.Object.Destroy(lp.Tex);
            L("cinema : salle " + p.Sali + " ferme le lien");
        }

        // ---------------------------------------------------------------- mettre un lien sur une bobine
        static string LinkTitle(string url)
        {
            string t = url;
            try { var u = new Uri(url); t = Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(u.AbsolutePath)); if (t.Length == 0) t = u.Host; } catch { }
            foreach (char c in new[] { '|', '~', ';', '+', '\n', '\r' }) t = t.Replace(c, ' ');
            return t.Length > 24 ? t.Substring(0, 24).Trim() : t.Trim();
        }

        static bool SetLink(Esine reel, string url, out string why)
        {
            why = null;
            url = (url ?? "").Trim();
            if (!(url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))) { why = Lang.T("adresse en http:// ou https:// attendue", "an http:// or https:// address is expected"); return false; }
            if (url.IndexOfAny(new[] { '~', '|', ';', '\n', '\r', ' ' }) >= 0 || url.Length > 400) { why = Lang.T("adresse trop longue ou avec des caractères interdits (~ | ; espace)", "address too long or with forbidden characters (~ | ; space)"); return false; }
            string low = url.ToLowerInvariant();
            if (low.Contains("youtube.com/") || low.Contains("youtu.be/") || low.Contains("vk.com/video") || low.Contains("vkvideo.ru") || low.Contains("rutube.ru"))
                Chat.System(Lang.T("Attention : YouTube, VK Video et Rutube ne donnent pas leur fichier vidéo, ce lien ne sera sans doute pas lisible. Il faut un lien direct vers une vidéo (.mp4).", "Note: YouTube, VK Video and Rutube don't give out their video file, this link probably won't play. A direct link to a video file (.mp4) is needed."));
            string title = TapeTitle(reel.Data);
            if (title.Length == 0) title = LinkTitle(url);
            SetData(reel, "url:" + url + "~" + title);
            return true;
        }

        // « /lien <adresse> » (ou /link) : vrai si la commande est la sienne
        static bool LinkCommand(string text)
        {
            string low = text.ToLowerInvariant();
            int cut = low.StartsWith("/lien") ? 5 : low.StartsWith("/link") ? 5 : -1;
            if (cut < 0) return false;
            Esine reel = esineet.Find(e => e.Kind == KKela && e.Go != null && e.Go.transform.parent != null && Game.UnderPlayer(e.Go.transform));
            if (reel == null) { Chat.System(Lang.T("Prends d'abord une bobine de film en main (en vente à l'Osuuskauppa).", "Pick up a film reel first (sold at the Osuuskauppa).")); return true; }
            string why;
            if (SetLink(reel, text.Substring(cut).Trim(), out why)) Chat.System(Lang.T("Lien mis sur la bobine « ", "Link put on the reel \"") + TapeTitle(reel.Data) + Lang.T(" » : pose-la sur un projecteur du cinéma.", "\": load it on a cinema projector."));
            else Chat.System(Lang.T("Lien refusé : ", "Link refused: ") + why);
            return true;
        }

        // ---------------------------------------------------------------- panneau de montage d'une bobine
        static string mLink = ""; static Esine mLinkFor;
        static Esine pickReel;
        const string LinkField = "mwcoop-kino-lien";

        static void DrawReelExtras(float x, float y, float w, Event e)
        {
            if (mLinkFor != mTape) { mLinkFor = mTape; mLink = LinkOf(mTape.Data) ?? ""; }
            float bh = Style.Px(36), gap = Style.Px(8);
            bool busy = import != null || picking;
            string lbl = import != null ? Lang.T("CONVERSION… ", "CONVERTING… ") + Mathf.Max(0, import.LastPct) + " %" : picking ? "…" : Lang.T("IMPORTER UNE VIDÉO…", "IMPORT A VIDEO…");
            if (MButton(new Rect(x, y, w, bh), lbl, true, e, !busy))
            {
                if (!BindExport() || !BindStream() || vPickFile == null) Note(Lang.T("Choix du fichier indisponible (MWCoopRT.dll).", "File picker unavailable (MWCoopRT.dll)."), true);
                else { EnsureImportDir(); if (vPickFile(ImportDir) == 0) { picking = true; pickReel = mTape; } }
            }
            y += bh + gap;
            Style.Text(new Rect(x, y, w, Style.Px(22)), Lang.T("ou un lien direct vers une vidéo (.mp4) :", "or a direct link to a video (.mp4):"), 13, TextAnchor.MiddleLeft, Style.Dim, false);
            y += Style.Px(24);
            var rf = new Rect(x, y, w, Style.Px(34));
            bool focused = GUI.GetNameOfFocusedControl() == LinkField;
            if (e.type == EventType.Repaint) Style.FieldBack(rf, focused);
            bool enter = focused && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
            if (enter) e.Use();
            GUI.SetNextControlName(LinkField);
            mLink = GUI.TextField(rf, mLink, 400, Style.Field(14));
            y += Style.Px(34) + gap;
            if (MButton(new Rect(x, y, w, bh), Lang.T("METTRE LE LIEN", "SET THE LINK"), false, e, mLink.Trim().Length > 0 && mLink.Trim() != (LinkOf(mTape.Data) ?? "")) || enter)
            {
                string why;
                if (SetLink(mTape, mLink, out why)) { Note(Lang.T("Lien mis sur la bobine : pose-la sur un projecteur.", "Link set on the reel: load it on a projector."), false); mTitle = TapeTitle(mTape.Data); LoadMontage(); }
                else Note(Lang.T("Lien refusé : ", "Link refused: ") + why, true);
                GUIUtility.keyboardControl = 0;
            }
        }
    }
}
