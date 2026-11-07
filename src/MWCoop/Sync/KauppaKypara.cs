using System;
using System.Collections.Generic;
using System.IO;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Casque personnalise (JD 07/11) : un casque de plus, vendu au magasin CARDINAL (presentoir de 16 casques, aile CARDINAL), qui ne
    // remplace pas celui du jeu. C'est une copie du casque du jeu (Wear, casques achetes) : on le met et on l'enleve comme lui, avec son
    // bord a l'ecran en premiere personne ; seul son dessin change -- le modele « casque » de objets.pak (forme du « F1 Helmet » de nv,
    // CC BY 4.0, deco retiree), coque peinte selon son style :
    //   ""                     : dessin du casque du jeu (casques achetes a l'Osuuskauppa, sauvegardes d'avant)
    //   "c:RRGGBB:RRGGBB"      : couleur de fond et de bande (masque de la deco par defaut, dans objets.pak)
    //   "p:<12 hex>"           : image d'un joueur (lanceur, Personnage > Casque : %LOCALAPPDATA%\MWCoop\casque.png ou .jpg, [Coop]
    //                            Casque=perso), empreinte MD5 de ses octets ; tant qu'elle n'est pas la : rouge a bande blanche.
    // Images des joueurs : chacun dit la sienne a tous (@kyparaj, toutes les 15 s) ; les autres la demandent (@kypara?), recue par
    // morceaux de 900 octets (@kyparap), gardee en cache (%LOCALAPPDATA%\MWCoop\casques\<empreinte>.png|jpg). Image recue comme les
    // vitres : 2 100 000 octets au plus, PNG ou JPEG verifie a l'en-tete (dimensions 16..2048 lues avant de decoder), rien d'autre ;
    // le chemin ne vient que de l'empreinte (12 chiffres hexa), jamais des donnees recues ; refusee ou illisible : casque rouge a bande
    // blanche, sans plantage. Reduite a 1024 au plus.
    // Presentoir : les casques des joueurs presents qui en ont un (ordre des numeros), puis des couleurs tirees au hasard, renouvelees
    // tous les 3 jours du jeu (comme l'occasion) ; chacun voit et peut acheter ceux des autres. Achat : panier, caisse, palette NOUTO.
    public static partial class Kauppa
    {
        public const string KyparaVis = "MWCoop-kypara";
        const int KyparaChunk = 900, KyparaMaxBytes = 2100000, KyparaMaxSide = 2048, KyparaTexSide = 1024;
        static readonly Color32 KyDefA = new Color32(209, 31, 26, 255), KyDefB = new Color32(242, 242, 235, 255);   // (comme defaut-casque.png)

        // ---------------------------------------------------------------- styles
        static bool Hex(string s, int at, int n)
        {
            if (s.Length < at + n) return false;
            for (int i = at; i < at + n; i++) { char c = s[i]; if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false; }
            return true;
        }
        static bool KyHashOk(string h) { if (h == null || h.Length != 12) return false; foreach (char c in h) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false; return true; }

        public static bool KyparaValid(string s)
        {
            if (s == null) return false;
            if (s.Length == 0) return true;
            if (s.Length == 15 && s.StartsWith("c:") && s[8] == ':' && Hex(s, 2, 6) && Hex(s, 9, 6)) return true;
            return s.Length == 14 && s.StartsWith("p:") && KyHashOk(s.Substring(2));
        }
        public static string KyparaClean(string s) { return KyparaValid(s) ? s : ""; }

        static Color32 HexCol(string s, int at)
        {
            int v = Convert.ToInt32(s.Substring(at, 6), 16);
            return new Color32((byte)(v >> 16), (byte)(v >> 8), (byte)v, 255);
        }
        static string ColHex(Color32 c) { return c.r.ToString("x2") + c.g.ToString("x2") + c.b.ToString("x2"); }

        // ---------------------------------------------------------------- dessin
        static readonly Dictionary<string, Material> kyMats = new Dictionary<string, Material>();
        static readonly Dictionary<string, Texture2D> kyColTex = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, string> kyWait = new Dictionary<string, string>();   // style "p:" -> empreinte dont l'image manque

        static Material KyBase()
        {
            LoadObjModels();
            ObjModel m;
            if (objModels == null || !objModels.TryGetValue("casque", out m)) return null;
            foreach (var p in m.Parts) if (p.Key == "coque") return p.Value.Value;
            return null;
        }

        // coque aux deux couleurs : masque de la deco (blanc = bande), 256 x 256
        static Texture2D KyColors(Color32 a, Color32 b)
        {
            string k = ColHex(a) + ColHex(b);
            Texture2D t;
            if (kyColTex.TryGetValue(k, out t) && t != null) return t;
            Material bm = KyBase();
            var mask = bm != null ? bm.mainTexture as Texture2D : null;
            if (mask == null) return null;
            Color32[] src;
            try { src = mask.GetPixels32(); } catch (Exception e) { L("casque : masque illisible " + e.Message); return null; }
            int mw = mask.width, mh = mask.height, w = Mathf.Max(1, mw / 2), h = Mathf.Max(1, mh / 2);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int s = (y * 2) * mw + x * 2;
                    float m = (src[s].r + src[s + 1].r + src[s + mw].r + src[s + mw + 1].r) / (4f * 255f);
                    px[y * w + x] = Color32.Lerp(a, b, m);
                }
            t = new Texture2D(w, h, TextureFormat.RGB24, true) { name = "kypara_" + k, wrapMode = TextureWrapMode.Clamp, anisoLevel = 2, hideFlags = HideFlags.HideAndDontSave };
            t.SetPixels32(px); t.Apply(true);
            kyColTex[k] = t;
            return t;
        }

        // matiere de la coque pour ce style (partagee par tous les casques de ce style, presentoir, objet et avatars)
        static Material KyShell(string style)
        {
            Material m;
            if (kyMats.TryGetValue(style, out m) && m != null) return m;
            Material bm = KyBase();
            if (bm == null) return null;
            m = new Material(bm) { name = "kypara_" + style, hideFlags = HideFlags.HideAndDontSave };   // (gardee d'une partie a l'autre)
            if (style.StartsWith("c:")) m.mainTexture = KyColors(HexCol(style, 2), HexCol(style, 9));
            else
            {
                string h = style.Substring(2);
                Texture2D t = KyPlayerTex(h);
                m.mainTexture = t != null ? t : KyColors(KyDefA, KyDefB);
                if (t == null && !kyBad.Contains(h)) kyWait[style] = h;
            }
            kyMats[style] = m;
            return m;
        }

        // le casque dessine dans ce style (repere du mod : haut +y, avant -z, centre) ; null sans modele
        public static GameObject KyparaModel(string style)
        {
            if (!KyparaValid(style) || style.Length == 0) return null;
            var g = new GameObject("kypara");
            Vector3 size;
            if (!ModelFile("casque", g, out size)) { UnityEngine.Object.Destroy(g); return null; }
            Material shell = KyShell(style);
            Transform c = g.transform.Find("coque");
            if (c != null && shell != null) c.GetComponent<MeshRenderer>().sharedMaterial = shell;
            return g;
        }

        // Copie du casque du jeu (Wear) habillee : son dessin cache (rendus coupes), le notre a la place, a sa taille (plus grand cote)
        // et au centre de son dessin. Le modele du jeu a son avant vers -y et son haut vers +z ([Test] KyparaRot = x,y,z : autre rotation).
        public static bool KyparaDress(GameObject go, string style)
        {
            if (go == null || string.IsNullOrEmpty(style) || !KyparaValid(style)) return false;
            Transform old = go.transform.Find(KyparaVis);
            if (old != null) UnityEngine.Object.Destroy(old.gameObject);
            GameObject vis = KyparaModel(style);
            if (vis == null) return false;
            Transform gt = go.transform;
            bool any = false; Bounds gb = new Bounds();
            foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.transform.IsChildOf(vis.transform)) continue;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    Bounds mb = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 p = gt.InverseTransformPoint(r.transform.TransformPoint(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                        if (!any) { gb = new Bounds(p, Vector3.zero); any = true; } else gb.Encapsulate(p);
                    }
                }
                r.enabled = false;
            }
            Vector3 ms = ModelSize("casque", new Vector3(0.26f, 0.27f, 0.32f));
            float big = Mathf.Max(ms.x, Mathf.Max(ms.y, ms.z));
            float k = any && big > 0.01f ? Mathf.Clamp(Mathf.Max(gb.size.x, Mathf.Max(gb.size.y, gb.size.z)) / big, 0.5f, 2f) : 1f;
            Quaternion q = Quaternion.LookRotation(Vector3.up, Vector3.forward);   // (notre -z -> -y, notre +y -> +z)
            string rs = Config.Get("Test", "KyparaRot", "");
            if (rs.Length > 0) { string[] a = rs.Split(','); if (a.Length == 3) q = Quaternion.Euler(F(a[0]), F(a[1]), F(a[2])); }
            vis.name = KyparaVis;
            vis.transform.SetParent(gt, false);
            vis.transform.localRotation = q;
            vis.transform.localScale = Vector3.one * k;
            vis.transform.localPosition = any ? gb.center : Vector3.zero;
            foreach (Transform c in vis.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = go.layer;
            return true;
        }

        // ---------------------------------------------------------------- images des joueurs
        static readonly Dictionary<string, Texture2D> kyTex = new Dictionary<string, Texture2D>();
        static readonly HashSet<string> kyBad = new HashSet<string>();
        static readonly Dictionary<string, float> kyAsked = new Dictionary<string, float>(), kySent = new Dictionary<string, float>();
        class KyParts { public byte[][] P; public int Got, Bytes; }
        static readonly Dictionary<string, KyParts> kyIncoming = new Dictionary<string, KyParts>();
        class KyPlayer { public string Hash; public float At; }
        static readonly Dictionary<int, KyPlayer> kyPlayers = new Dictionary<int, KyPlayer>();
        static string kyMine = ""; static DateTime kyStamp = DateTime.MinValue; static string kyMineFile;
        static float kyNextLook, kyNextSay, kyNextWait; static string kySaid;

        // ([Test] RacineVisages : un dossier par instance d'essai, comme les visages et les livrees)
        static string KyRoot { get { string o = Config.Get("Test", "RacineVisages", ""); return o.Length > 0 ? o : Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? ".", "MWCoop"); } }
        static string KyCacheDir { get { return Path.Combine(KyRoot, "casques"); } }
        static string KyCache(string h, bool png) { return Path.Combine(KyCacheDir, h + (png ? ".png" : ".jpg")); }
        static string KyCached(string h)
        {
            if (!KyHashOk(h)) return null;
            string f = KyCache(h, true); if (File.Exists(f)) return f;
            f = KyCache(h, false); return File.Exists(f) ? f : null;
        }

        static string KyHash(byte[] b)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] h = md5.ComputeHash(b);
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < 6; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        // PNG ou JPEG, dimensions lues dans l'en-tete (avant tout decodage) : 16..2048 ; png : vrai si PNG
        static bool KyImageOk(byte[] b, out bool png, out int w, out int h)
        {
            png = false; w = h = 0;
            if (b == null || b.Length < 100 || b.Length > KyparaMaxBytes) return false;
            if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A)
            {
                if (b[12] != (byte)'I' || b[13] != (byte)'H' || b[14] != (byte)'D' || b[15] != (byte)'R') return false;
                w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
                h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
                png = true;
            }
            else if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
            {
                int i = 2;
                while (i + 9 < b.Length)
                {
                    if (b[i] != 0xFF) return false;
                    int mk = b[i + 1];
                    if (mk == 0xFF) { i++; continue; }
                    if (mk == 0xD8 || mk == 0x01 || (mk >= 0xD0 && mk <= 0xD7)) { i += 2; continue; }
                    if (mk == 0xD9 || mk == 0xDA) return false;   // (fin, ou donnees avant toute dimension)
                    int len = (b[i + 2] << 8) | b[i + 3];
                    if (len < 2) return false;
                    if (mk >= 0xC0 && mk <= 0xCF && mk != 0xC4 && mk != 0xC8 && mk != 0xCC)
                    {
                        h = (b[i + 5] << 8) | b[i + 6];
                        w = (b[i + 7] << 8) | b[i + 8];
                        break;
                    }
                    i += 2 + len;
                }
            }
            else return false;
            return w >= 16 && h >= 16 && w <= KyparaMaxSide && h <= KyparaMaxSide;
        }

        // texture de l'image h (cache), ou null (pas encore la : demandee ; refusee : jamais)
        static Texture2D KyPlayerTex(string h)
        {
            Texture2D t;
            if (kyTex.TryGetValue(h, out t) && t != null) return t;
            if (kyBad.Contains(h)) return null;
            string f = KyCached(h);
            if (f == null) { KyAsk(h); return null; }
            try
            {
                byte[] b = File.ReadAllBytes(f);
                bool png; int w, hh;
                if (!KyImageOk(b, out png, out w, out hh) || KyHash(b) != h) { kyBad.Add(h); L("casque : image " + h + " refusee (format, taille ou empreinte)"); return null; }
                t = new Texture2D(2, 2, TextureFormat.RGB24, true) { name = "kypara_" + h };
                if (!t.LoadImage(b)) { UnityEngine.Object.Destroy(t); kyBad.Add(h); L("casque : image " + h + " illisible"); return null; }
                t = KyShrink(KyOpaque(t), KyparaTexSide);
                t.wrapMode = TextureWrapMode.Clamp; t.anisoLevel = 4; t.hideFlags = HideFlags.HideAndDontSave;
                kyTex[h] = t;
                L("casque : image " + h + " chargee (" + w + "x" + hh + (t.width != w ? " -> " + t.width + "x" + t.height : "") + ")");
            }
            catch (Exception e) { kyBad.Add(h); L("casque : image " + h + " : " + e.Message); t = null; }
            return t;
        }

        // PNG avec alpha (LoadImage le garde) : en RGB, l'alpha n'entre pas dans la matiere (lanceur : alpha ignore aussi)
        static Texture2D KyOpaque(Texture2D t)
        {
            if (t.format == TextureFormat.RGB24) return t;
            var o = new Texture2D(t.width, t.height, TextureFormat.RGB24, true) { name = t.name };
            o.SetPixels32(t.GetPixels32());
            o.Apply(true);
            UnityEngine.Object.Destroy(t);
            return o;
        }

        static Texture2D KyShrink(Texture2D t, int max)
        {
            int w = t.width, h = t.height;
            if (w <= max && h <= max) return t;
            float k = (float)max / Mathf.Max(w, h);
            int nw = Mathf.Max(16, Mathf.RoundToInt(w * k)), nh = Mathf.Max(16, Mathf.RoundToInt(h * k));
            RenderTexture rt = RenderTexture.GetTemporary(nw, nh, 0, RenderTextureFormat.ARGB32);
            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(t, rt);
            RenderTexture.active = rt;
            var o = new Texture2D(nw, nh, TextureFormat.RGB24, true) { name = t.name };
            o.ReadPixels(new Rect(0, 0, nw, nh), 0, 0);
            o.Apply(true);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.Destroy(t);
            return o;
        }

        // empreinte de l'image du joueur d'ici ("" : pas de casque perso), relue si le fichier change (toutes les 10 s)
        public static string KyparaMine()
        {
            float now = Time.realtimeSinceStartup;
            if (now < kyNextLook) return kyMine;
            kyNextLook = now + 10f;
            if (Config.Get("Coop", "Casque", "") != "perso") { kyMine = ""; kyStamp = DateTime.MinValue; return kyMine; }
            string f = null;
            foreach (string n in new[] { "casque.png", "casque.jpg" }) { string p = Path.Combine(KyRoot, n); if (File.Exists(p)) { f = p; break; } }
            if (f == null) { if (kyMine.Length > 0 || kyMineFile != "") L("casque : « perso » choisi, mais pas d'image (" + Path.Combine(KyRoot, "casque.png") + ")"); kyMine = ""; kyMineFile = ""; return kyMine; }
            DateTime st;
            try { st = File.GetLastWriteTimeUtc(f); } catch { return kyMine; }
            if (f == kyMineFile && st == kyStamp) return kyMine;
            kyMineFile = f; kyStamp = st; kyMine = "";
            try
            {
                byte[] b = File.ReadAllBytes(f);
                bool png; int w, h;
                if (!KyImageOk(b, out png, out w, out h)) { L("casque : mon image refusee (" + Path.GetFileName(f) + ", " + b.Length + " octets ; PNG ou JPEG, 2 100 000 octets et 2048 pixels au plus)"); return kyMine; }
                string hh = KyHash(b);
                Directory.CreateDirectory(KyCacheDir);
                if (KyCached(hh) == null) File.WriteAllBytes(KyCache(hh, png), b);
                kyMine = hh;
                L("casque : mon image " + hh + " (" + w + "x" + h + ", " + b.Length + " octets)");
            }
            catch (Exception e) { L("casque : mon image : " + e.Message); }
            return kyMine;
        }

        static void KyAsk(string h)
        {
            float t;
            if (!Session.Active || (kyAsked.TryGetValue(h, out t) && Time.realtimeSinceStartup - t < 30f)) return;
            kyAsked[h] = Time.realtimeSinceStartup;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@kypara?").Str(h), true);
            L("casque : image " + h + " demandee");
        }

        public static void OnKyparaPlayer(int who, string h)
        {
            if (h == null || (h.Length > 0 && !KyHashOk(h))) return;
            KyPlayer p;
            if (!kyPlayers.TryGetValue(who, out p)) kyPlayers[who] = p = new KyPlayer();
            if (p.Hash != h) L("casque de #" + who + " : " + (h.Length > 0 ? h : "aucun"));
            p.Hash = h; p.At = Time.realtimeSinceStartup;
        }

        public static void OnKyparaAsk(int who, string h)
        {
            float t;
            if (!KyHashOk(h)) return;
            string f = KyCached(h);
            if (f == null || (kySent.TryGetValue(h, out t) && Time.realtimeSinceStartup - t < 20f)) return;
            if (!Session.IsHost && KyparaMine() != h) return;   // (celui qui l'a choisie repond, et l'hote qui l'a en cache)
            byte[] b;
            try { b = File.ReadAllBytes(f); } catch { return; }
            bool png; int w, hh;
            if (!KyImageOk(b, out png, out w, out hh)) return;
            kySent[h] = Time.realtimeSinceStartup;
            int n = (b.Length + KyparaChunk - 1) / KyparaChunk;
            for (int i = 0; i < n; i++)
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@kyparap").Str(h).U16(i).U16(n).Bytes(b, i * KyparaChunk, Mathf.Min(KyparaChunk, b.Length - i * KyparaChunk)), true);
            L("casque : image " + h + " envoyee a la demande de #" + who + " (" + n + " morceaux)");
        }

        public static void OnKyparaPart(int who, string h, int i, int n, byte[] data)
        {
            if (!KyHashOk(h) || KyCached(h) != null || kyBad.Contains(h) || data == null || n <= 0 || n > (KyparaMaxBytes + KyparaChunk - 1) / KyparaChunk || i < 0 || i >= n || data.Length > KyparaChunk) return;
            KyParts p;
            if (!kyIncoming.TryGetValue(h, out p) || p.P.Length != n) kyIncoming[h] = p = new KyParts { P = new byte[n][] };
            if (p.P[i] != null) return;
            p.P[i] = data; p.Got++; p.Bytes += data.Length;
            if (p.Bytes > KyparaMaxBytes) { kyIncoming.Remove(h); kyBad.Add(h); L("casque : image " + h + " de #" + who + " trop grosse, refusee"); return; }
            if (p.Got < n) return;
            kyIncoming.Remove(h);
            var b = new byte[p.Bytes];
            int at = 0;
            foreach (byte[] x in p.P) { Buffer.BlockCopy(x, 0, b, at, x.Length); at += x.Length; }
            bool png; int w, hh;
            if (KyHash(b) != h) { L("casque : image " + h + " recue abimee (de #" + who + ")"); return; }
            if (!KyImageOk(b, out png, out w, out hh)) { kyBad.Add(h); L("casque : image " + h + " de #" + who + " refusee (pas un PNG ou JPEG de 16 a 2048 pixels)"); return; }
            try { Directory.CreateDirectory(KyCacheDir); File.WriteAllBytes(KyCache(h, png), b); }
            catch (Exception e) { L("casque : image " + h + " : " + e.Message); return; }
            L("casque : image " + h + " recue de #" + who + " (" + b.Length + " octets, " + w + "x" + hh + ")");
        }

        // chaque image (meme sans magasin) : dire mon image, poser celles qui arrivent
        static void TickKypara()
        {
            if (!PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            string mine = KyparaMine();
            if (Session.Active && (now >= kyNextSay || mine != kySaid))
            {
                kyNextSay = now + 15f; kySaid = mine;
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@kyparaj").Str(mine), true);
            }
            if (kyWait.Count > 0 && now >= kyNextWait)
            {
                kyNextWait = now + 1f;
                foreach (var kv in new List<KeyValuePair<string, string>>(kyWait))
                {
                    Texture2D t = KyPlayerTex(kv.Value);
                    if (t == null && !kyBad.Contains(kv.Value)) continue;
                    kyWait.Remove(kv.Key);
                    Material m;
                    if (t != null && kyMats.TryGetValue(kv.Key, out m) && m != null) { m.mainTexture = t; L("casque : " + kv.Key + " peint"); }
                }
            }
        }

        static void ResetKypara()
        {
            kyPlayers.Clear(); kyIncoming.Clear(); kyAsked.Clear(); kySent.Clear();
            kyNextLook = kyNextSay = kyNextWait = 0f; kySaid = null;
            kyRack.Clear(); kyRackSig = null; kyRackNext = 0f;
        }

        // ---------------------------------------------------------------- presentoir (aile CARDINAL)
        static readonly Vector3 KyparaTorni = new Vector3(-3.35f, 0f, 0.35f);   // (entre l'etagere du pignon et les courroies, devant la palette)
        static readonly float[] KyLevels = { 0.42f, 0.86f, 1.30f, 1.74f };
        static readonly int[] KyOrder = { 2, 1, 3, 0 };   // (quatre par quatre, une par face : a hauteur des yeux d'abord)
        static readonly Vector3[] KyFaces = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
        static readonly string[] KyPalette = {
            "f2f2eb:d11f1a", "141416:f2c40c", "1d4fa8:f2f2eb", "f2c40c:141416", "1f7a3a:f2f2eb", "e8650f:141416", "d11f1a:f2f2eb", "a9adb3:1d4fa8",
            "1b2a4a:e8650f", "5a2a82:f2c40c", "137a7f:f2f2eb", "141416:d11f1a", "f2f2eb:1d4fa8", "8cc63f:141416", "8a5a2b:efe4c8", "e06aa0:f2f2eb",
            "c9a227:141416", "6fb7e8:d11f1a", "3a3d42:e8650f", "efe4c8:1f7a3a" };
        class KyRack { public string Style, Name; }
        static readonly List<KyRack> kyRack = new List<KyRack>();
        static string kyRackSig; static float kyRackNext;

        // le presentoir (decor fixe, avec le magasin) : socle, colonne bleue, 16 tablettes, enseigne
        static void OsatKyparaTorni()
        {
            Vector3 c = KyparaTorni;
            Box(c + new Vector3(-0.33f, 0f, -0.33f), c + new Vector3(0.33f, 0.1f, 0.33f), "os_musta", false);
            Box(c + new Vector3(-0.13f, 0.1f, -0.13f), c + new Vector3(0.13f, 2.05f, 0.13f), "os_sininen", false);
            Box(c + new Vector3(-0.2f, 2.05f, -0.2f), c + new Vector3(0.2f, 2.1f, 0.2f), "os_valko", false);
            foreach (Vector3 f in KyFaces)
            {
                Vector3 s = Vector3.Cross(Vector3.up, f);
                foreach (float y in KyLevels)
                {
                    Vector3 a = c + f * 0.13f + s * -0.15f + Vector3.up * (y - 0.02f), b = c + f * 0.43f + s * 0.15f + Vector3.up * y;
                    Box(Vector3.Min(a, b), Vector3.Max(a, b), "os_taso", false);
                    Vector3 la = c + f * 0.41f + s * -0.15f + Vector3.up * (y - 0.05f), lb = c + f * 0.44f + s * 0.15f + Vector3.up * y;
                    Box(Vector3.Min(la, lb), Vector3.Max(la, lb), "os_palkki", false);
                }
            }
            cols.Add(new Bounds(c + new Vector3(0f, 1.05f, 0f), new Vector3(0.9f, 2.1f, 0.9f)));
            // enseigne sur le dessus, quatre faces
            if (arial == null) arial = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (arial == null) return;
            Shader sh = Shader.Find("Unlit/Transparent") ?? Shader.Find("Legacy Shaders/Transparent/Diffuse") ?? arial.material.shader;
            var m = new Material(sh) { name = "osat_kypara_kyltti", color = new Color(1f, 0.92f, 0.3f), mainTexture = arial.material.mainTexture };
            kinoTexts.Add(m);
            foreach (Vector3 f in KyFaces)
            {
                var g = new GameObject("osat_kypara_kyltti");
                g.transform.SetParent(osat.transform, false);
                g.transform.localPosition = c + f * 0.205f + Vector3.up * 2.075f;
                g.transform.localRotation = Quaternion.LookRotation(-f);
                var tm = g.AddComponent<TextMesh>();
                tm.font = arial; tm.fontSize = 64; tm.characterSize = 0.035f / 6f; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
                tm.fontStyle = FontStyle.Bold; tm.color = Color.white; tm.text = "KYPÄRÄT";
                g.GetComponent<MeshRenderer>().sharedMaterial = m;
            }
        }

        static string KyName(int id)
        {
            PlayerInfo p;
            if (Session.Players.TryGetValue(id, out p) && p != null && !string.IsNullOrEmpty(p.Name) && p.Name != "?") return p.Name;
            return null;
        }

        // ce que montre le presentoir : casques des joueurs (numeros croissants, une fois par image), puis couleurs du moment
        static List<KyRack> KyRackWanted()
        {
            var r = new List<KyRack>();
            var seen = new HashSet<string>();
            var ids = new List<int>();
            int me = Session.Active ? Session.LocalId : 0;
            string mine = KyparaMine();
            if (mine.Length > 0) ids.Add(me);
            float now = Time.realtimeSinceStartup;
            foreach (var kv in kyPlayers)
                if (kv.Key != me && kv.Value.Hash.Length > 0 && Session.Active && Session.Players.ContainsKey(kv.Key) && now - kv.Value.At < 60f) ids.Add(kv.Key);
            ids.Sort();
            foreach (int id in ids)
            {
                string h = id == me ? mine : kyPlayers[id].Hash;
                if (!seen.Add(h) || kyBad.Contains(h)) continue;
                string n = KyName(id);
                r.Add(new KyRack { Style = "p:" + h, Name = id == me ? Lang.TG("MON CASQUE", "MY HELMET") : n != null ? Lang.TG("CASQUE DE " + n, n + "'S HELMET") : Lang.TG("CASQUE", "HELMET") });
                if (r.Count >= 16) return r;
            }
            var pal = new List<string>(KyPalette);
            var rnd = new System.Random(DaysPassed / OsatRestockDays * 7919 + 17);
            for (int i = pal.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); string t = pal[i]; pal[i] = pal[j]; pal[j] = t; }
            for (int i = 0; r.Count < 16 && i < pal.Count; i++) r.Add(new KyRack { Style = "c:" + pal[i], Name = Lang.TG("CASQUE", "HELMET") });
            return r;
        }

        // casques du presentoir : remis quand la liste change (joueur arrive, image choisie, nouveaux jours)
        static void OsatKyparat(bool force)
        {
            float now = Time.realtimeSinceStartup;
            if (!force && now < kyRackNext) return;
            kyRackNext = now + 2f;
            List<KyRack> want = KyRackWanted();
            var sb = new System.Text.StringBuilder();
            foreach (KyRack k in want) sb.Append(k.Style).Append('=').Append(k.Name).Append(';');
            string sig = sb.ToString();
            if (!force && sig == kyRackSig) return;
            kyRackSig = sig;
            foreach (OsatOffer o in osatOffers.FindAll(x => x.Kypara != null))
            {
                if (o.Display != null) UnityEngine.Object.Destroy(o.Display);
                if (o.Col != null) UnityEngine.Object.Destroy(o.Col.gameObject);
                osatOffers.Remove(o);
                if (osatLooked == o) osatLooked = null;
            }
            Vector3 size = ModelSize("casque", new Vector3(0.26f, 0.27f, 0.32f));
            int n = 0, players = 0;
            for (int i = 0; i < want.Count && i < 16; i++)
            {
                Vector3 f = KyFaces[i % 4];
                float y = KyLevels[KyOrder[i / 4]];
                Vector3 at = KyparaTorni + f * 0.28f + Vector3.up * (y + size.y / 2f + 0.004f);
                var o = new OsatOffer { Wing = 0, Name = want[i].Name, Price = KyparaPrice, Kypara = want[i].Style };
                GameObject vis = KyparaModel(want[i].Style);
                if (vis != null)
                {
                    vis.name = "osat_kypara_" + i;
                    vis.transform.SetParent(osat.transform, false);
                    vis.transform.localPosition = at;
                    vis.transform.localRotation = Quaternion.LookRotation(-f);   // (visiere vers l'allee)
                    o.Display = vis;
                }
                var cg = new GameObject("osat_article_kypara");
                cg.transform.SetParent(osat.transform, false);
                cg.transform.localPosition = at;
                cg.transform.localRotation = Quaternion.LookRotation(-f);
                o.Col = cg.AddComponent<BoxCollider>();
                o.Col.size = new Vector3(0.3f, 0.3f, 0.32f);
                o.Col.isTrigger = true;
                osatOffers.Add(o);
                n++;
                if (want[i].Style.StartsWith("p:")) players++;
            }
            L("magasin de pieces : presentoir des casques, " + n + " casque(s) dont " + players + " de joueur(s)");
        }

        // essais (kyparaverif) : en-tetes acceptes ou refuses, image recue abimee, trop de morceaux, image recue juste
        static string KyparaVerif()
        {
            var sb = new System.Text.StringBuilder("verification des images de casque :");
            var t = new Texture2D(32, 32, TextureFormat.RGB24, false);
            var px = new Color32[32 * 32];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32((byte)(i * 7), (byte)(i * 3), 90, 255);
            t.SetPixels32(px); t.Apply();
            byte[] okPng = t.EncodeToPNG();
            UnityEngine.Object.Destroy(t);
            bool png; int w, h;
            Func<byte[], string> chk = x => KyImageOk(x, out png, out w, out h) ? "accepte " + w + "x" + h : "refuse";
            sb.Append(" png 32x32 ").Append(chk(okPng));
            byte[] big = (byte[])okPng.Clone(); big[18] = 0x10; big[19] = 0x00;   // (largeur 4096)
            sb.Append(" ; png 4096 ").Append(chk(big));
            var jpg = new byte[120];
            byte[] head = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
                            0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x40, 0x00, 0x30, 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01 };
            Buffer.BlockCopy(head, 0, jpg, 0, head.Length);
            sb.Append(" ; jpeg 48x64 ").Append(chk(jpg));
            var junk = new byte[500]; new System.Random(5).NextBytes(junk);
            sb.Append(" ; octets au hasard ").Append(chk(junk));
            byte[] cut = new byte[60]; Buffer.BlockCopy(okPng, 0, cut, 0, Mathf.Min(60, okPng.Length));
            sb.Append(" ; png tronque ").Append(chk(cut));
            // recues : empreinte fausse (rien en cache), trop de morceaux (ignore), juste (en cache, lisible)
            string good = KyHash(okPng), wrong = good.Substring(0, 11) + (good[11] == '0' ? "1" : "0");
            OnKyparaPart(99, wrong, 0, 1, okPng);
            sb.Append(" ; empreinte fausse : ").Append(KyCached(wrong) == null ? "pas gardee" : "GARDEE");
            OnKyparaPart(99, "abcdefabcdef", 0, 60000, okPng);
            sb.Append(" ; 60000 morceaux : ").Append(kyIncoming.ContainsKey("abcdefabcdef") ? "ATTENDUS" : "ignores");
            OnKyparaPart(99, "../../x", 0, 1, okPng);
            sb.Append(" ; empreinte chemin : ").Append(File.Exists(Path.Combine(KyCacheDir, "../../x.png")) ? "ECRIT" : "ignoree");
            OnKyparaPart(99, good, 0, 1, okPng);
            Texture2D got = KyPlayerTex(good);
            sb.Append(" ; juste : ").Append(got != null ? "chargee " + got.width + "x" + got.height : "PAS CHARGEE");
            return sb.ToString();
        }

        // essais : etat du presentoir et des casques des joueurs
        static string KyparaState()
        {
            var sb = new System.Text.StringBuilder("casques : le mien " + (kyMine.Length > 0 ? kyMine : "aucun") + " ; joueurs");
            foreach (var kv in kyPlayers) sb.Append(" #").Append(kv.Key).Append('=').Append(kv.Value.Hash.Length > 0 ? kv.Value.Hash : "-").Append(KyCached(kv.Value.Hash) != null ? "(en cache)" : "");
            sb.Append(" ; presentoir :");
            foreach (OsatOffer o in osatOffers) if (o.Kypara != null) sb.Append(' ').Append(o.Kypara).Append(kyWait.ContainsKey(o.Kypara) ? "(attend)" : "");
            sb.Append(" ; refusees ").Append(kyBad.Count);
            return sb.ToString();
        }
    }
}
