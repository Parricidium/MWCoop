using System;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Coin video de la boutique (demande de JD, 12/10) : objets vendus a l'Osuuskauppa.
    //  - cassette VHS ("vhs"), magnetoscope ("nauhuri"), television ("tv", modele tv_14 du jeu copie).
    //  - objets du jeu (couche 19, etiquette PART : on les prend en main) ; pose suivie par Props (cle "esine:<id>").
    //    Crees par l'hote (ou le joueur seul) qui tient la liste (MWCoop-esineet.txt) et la renvoie ; un invite demande
    //    (@esineosto). Etat (Data) change par n'importe qui et envoye a tous (@esine).
    //  - magnetoscope : branche sur la TV la plus proche (achetee, ou une TV du jeu : maison, prison, maison de retraite,
    //    chez Jokke), a moins de 2,5 m. Cassette tenue en main + clic sur le magnetoscope = inseree (la main du jeu la
    //    lache, puis elle entre). F = lecture / stop ; F tenu = ejecter. Cassette vierge : neige et souffle a l'ecran.
    // Data : cassette = contenu ("tyhja" = vierge, puis le film) ; magnetoscope = "<contenu de la cassette>|<1 lecture>".
    public static partial class Kauppa
    {
        public const string KVhs = "vhs", KNauhuri = "nauhuri", KTv = "tv";

        class Esine { public string Id, Kind, Data = ""; public GameObject Go; public Rigidbody Rb; public BoxCollider Col; public Transform Screen; }
        static readonly List<Esine> esineet = new List<Esine>();
        static bool esineLoaded;
        static float nextEsineSync;

        public static string ItemKeyOf(Rigidbody rb)
        {
            foreach (Esine e in esineet) if (e.Rb == rb) return "esine:" + e.Id;
            return null;
        }

        static string ItemFile { get { string d = Config.Get("Test", "DossierSacs", ""); return Path.Combine(d.Length > 0 ? d : Application.persistentDataPath, "MWCoop-esineet.txt"); } }

        // ---------------------------------------------------------------- modeles (maison, sauf la TV : copie du jeu)
        static Material plainMat;
        static Material Plain(Color c)
        {
            if (plainMat == null) { Material b = GameMat("wood_panel7"); plainMat = new Material(b != null ? b.shader : Shader.Find("Legacy Shaders/Diffuse")) { mainTexture = null }; }
            return new Material(plainMat) { color = c };
        }

        static readonly Dictionary<string, Material> texMats = new Dictionary<string, Material>();
        static Material TexMat(string png)
        {
            Material m;
            if (texMats.TryGetValue(png, out m) && m != null) return m;
            Texture2D t = Res(png, TextureFormat.RGB24);
            t.wrapMode = TextureWrapMode.Clamp;
            Material b = GameMat("wood_panel7");
            m = new Material(b != null ? b.shader : Shader.Find("Legacy Shaders/Diffuse")) { mainTexture = t, color = Color.white, name = "kauppa_" + png };
            texMats[png] = m;
            return m;
        }

        static GameObject Piece(GameObject parent, PrimitiveType type, Vector3 pos, Vector3 rot, Vector3 scale, Material m)
        {
            var g = GameObject.CreatePrimitive(type);
            UnityEngine.Object.Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent.transform, false);
            g.transform.localPosition = pos; g.transform.localRotation = Quaternion.Euler(rot); g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }

        static Material blackPlastic, greyMetal;

        // Modele seul (sans physique) : pour les objets et pour l'etalage. Taille de la boite renvoyee.
        static GameObject Model(string kind, out Vector3 size, out Transform screen)
        {
            screen = null;
            var g = new GameObject("osuus_" + kind);
            if (blackPlastic == null) { blackPlastic = Plain(new Color(0.07f, 0.07f, 0.08f)); greyMetal = Plain(new Color(0.2f, 0.2f, 0.21f)); }
            if (kind == KVhs)
            {
                size = new Vector3(0.188f, 0.025f, 0.103f);
                Piece(g, PrimitiveType.Cube, Vector3.zero, Vector3.zero, size, blackPlastic);
                Piece(g, PrimitiveType.Quad, new Vector3(0, size.y / 2 + 0.0008f, 0), new Vector3(90, 0, 0), new Vector3(size.x * 0.96f, size.z * 0.94f, 1), TexMat("vhs-kansi.png"));
                Piece(g, PrimitiveType.Quad, new Vector3(0, 0, -size.z / 2 - 0.0008f), Vector3.zero, new Vector3(size.x * 0.97f, size.y * 0.86f, 1), TexMat("vhs-selka.png"));
            }
            else if (kind == KNauhuri)
            {
                size = new Vector3(0.36f, 0.09f, 0.28f);
                Piece(g, PrimitiveType.Cube, Vector3.zero, Vector3.zero, size, greyMetal);
                Piece(g, PrimitiveType.Quad, new Vector3(0, 0, -size.z / 2 - 0.0008f), Vector3.zero, new Vector3(size.x * 0.995f, size.y * 0.97f, 1), TexMat("nauhuri-etu.png"));
                foreach (float x in new[] { -0.15f, 0.15f })   // pieds
                    foreach (float z in new[] { -0.11f, 0.11f })
                        Piece(g, PrimitiveType.Cube, new Vector3(x, -size.y / 2 - 0.005f, z), Vector3.zero, new Vector3(0.03f, 0.01f, 0.03f), blackPlastic);
            }
            else if (kind == KKamera) KameraModel(g, out size);
            else if (kind == KKartturi) KartturiModel(g, out size, out screen);
            else
            {
                // television : copie des maillages de la TV de la prison (tv_14 + son ecran)
                size = new Vector3(0.5f, 0.45f, 0.45f);
                GameObject src = FindPath("JAIL/TV");
                if (src != null)
                {
                    foreach (MeshRenderer r in src.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        if (mf == null || mf.sharedMesh == null || mf.sharedMesh.name.StartsWith("Combined Mesh")) continue;
                        // (ni le cache noir « mask » ni la vitre « Lens » : au meme endroit que la dalle, ils se battaient avec elle --
                        // taches noires qui bougent avec la camera, video de JD du 13/10)
                        if (r.name == "mask" || r.name == "Lens") continue;
                        var c = new GameObject(r.name);
                        c.transform.SetParent(g.transform, false);
                        // (orientation du monde, pas celle de JAIL/TV dont les axes sont couches : la TV etait sur le cote)
                        c.transform.localPosition = r.transform.position - src.transform.position;
                        c.transform.localRotation = r.transform.rotation;
                        c.transform.localScale = r.transform.lossyScale;
                        c.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                        var cr = c.AddComponent<MeshRenderer>(); cr.sharedMaterials = r.sharedMaterials;
                        cr.shadowCastingMode = r.shadowCastingMode; cr.receiveShadows = r.receiveShadows;   // (dalle : sans ombres, comme le jeu)
                        if (r.name == "TVScreen") screen = c.transform;
                    }
                    // recentre : boite des maillages autour de l'origine, posee sur y = -size.y/2
                    Bounds b = new Bounds(); bool first = true;
                    foreach (MeshRenderer r in g.GetComponentsInChildren<MeshRenderer>())
                    { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
                    if (!first)
                    {
                        foreach (Transform c in g.transform) c.localPosition -= b.center;
                        // ecran tourne vers -z, comme la facade du magnetoscope
                        Vector3 f = ScreenFront(g, screen); f.y = 0f;
                        if (f.sqrMagnitude > 1e-6f)
                        {
                            Quaternion q = Quaternion.FromToRotation(f.normalized, Vector3.back);
                            foreach (Transform c in g.transform) { c.localPosition = q * c.localPosition; c.localRotation = q * c.localRotation; }
                        }
                        first = true;
                        foreach (MeshRenderer r in g.GetComponentsInChildren<MeshRenderer>())
                        { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
                        size = b.size;
                        foreach (Transform c in g.transform) c.localPosition -= b.center;
                    }
                }
                else L("video : TV du jeu introuvable (JAIL/TV)");
            }
            return g;
        }

        // ---------------------------------------------------------------- objets physiques
        static Esine FindItem(string id) { foreach (Esine e in esineet) if (e.Id == id) return e; return null; }

        static Esine CreateItem(string id, string kind, Vector3 pos, Quaternion rot, string data)
        {
            Vector3 size; Transform screen;
            GameObject g = Model(kind, out size, out screen);
            g.name = (kind == KVhs ? "vhs tape" : kind == KNauhuri ? "vcr" : kind == KKamera ? "camcorder" : kind == KKartturi ? "kartturi gps" : "television") + " osuuskauppa(itemx)";
            g.transform.position = pos; g.transform.rotation = rot;
            var e = new Esine { Id = id, Kind = kind, Data = data ?? "", Go = g, Screen = screen };
            e.Col = g.AddComponent<BoxCollider>(); e.Col.size = size;
            e.Rb = g.AddComponent<Rigidbody>(); e.Rb.mass = kind == KVhs ? 0.25f : kind == KNauhuri ? 4f : kind == KKamera ? 2.5f : kind == KKartturi ? 0.4f : 9f;
            e.Rb.collisionDetectionMode = CollisionDetectionMode.Continuous;   // (cassette fine : ne traverse pas le sol en tombant)
            g.layer = 19; g.tag = "PART";
            esineet.Add(e);
            Props.SoonScan();
            return e;
        }

        static void SendItem(Esine e)
        {
            if (!Session.Active || e.Go == null) return;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@esine").U8(1).Str(e.Id).Str(e.Kind).Str(e.Data).Vec(e.Go.transform.position).Quat(e.Go.transform.rotation), true);
        }

        static void SendRemove(string id)
        {
            if (Session.Active) Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@esine").U8(2).Str(id), true);
        }

        public static void OnItem(int who, NetReader r)
        {
            int op = r.U8(); string id = r.Str();
            if (op == 2) { Esine x = FindItem(id); if (x != null) RemoveItem(x, false); return; }
            string kind = r.Str(), data = r.Str(); Vector3 p = r.Vec(); Quaternion q = r.Quat();
            if (!Active || root == null) return;
            Esine e = FindItem(id);
            if (e == null) CreateItem(id, kind, p, q, data);
            else if (e.Data != data) { e.Data = data; }
        }

        // Nouvel objet : cree ici par l'hote ou le joueur seul, sinon demande a l'hote.
        static void SpawnItem(string kind, string data, Vector3 pos, Quaternion rot)
        {
            if (BagAuthority)
            {
                string id;
                do id = "e" + DateTime.Now.Ticks.ToString("x") + (++idSeq).ToString("x"); while (FindItem(id) != null);   // (horloge a ~1 ms : plusieurs objets du meme sac dans la meme image)
                Esine e = CreateItem(id, kind, pos, rot, data);
                SendItem(e);
                L("video : " + kind + " " + e.Id + " pose en " + pos.ToString("F1"));
                return;
            }
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@esineosto").Str(kind).Str(data ?? "").Vec(pos).Quat(rot), true);
        }

        public static void OnSpawnRequest(int who, NetReader r)
        {
            string kind = r.Str(), data = r.Str(); Vector3 p = r.Vec(); Quaternion q = r.Quat();
            if (Session.IsHost && root != null) SpawnItem(kind, data, p, q);
        }

        static void RemoveItem(Esine e, bool send)
        {
            StopScreen(e);
            if (e.Go != null) UnityEngine.Object.Destroy(e.Go);
            esineet.Remove(e);
            if (send) SendRemove(e.Id);
        }

        static void SetData(Esine e, string data)
        {
            e.Data = data;
            SendItem(e);
        }

        // Sauvegarde du jeu (SaveHook) : seul moment ou les objets s'ecrivent (JD, 14/10) ; boutique coupee (objets jamais lus) : rien.
        public static void OnGameSaveVideo() { if (esineLoaded) SaveItems(); }

        static void SaveItems()
        {
            if (!BagAuthority) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            foreach (Esine e in esineet)
            {
                if (e.Go == null) continue;
                Vector3 p = e.Go.transform.position; Quaternion q = e.Go.transform.rotation;
                sb.Append(e.Id).Append(';').Append(e.Kind).Append(';').Append(e.Data.Replace(";", ",").Replace("\n", " "));
                foreach (float f in new[] { p.x, p.y, p.z, q.x, q.y, q.z, q.w }) sb.Append(';').Append(f.ToString("R", ci));
                sb.Append('\n');
            }
            try { File.WriteAllText(ItemFile, sb.ToString()); } catch (Exception ex) { L("video : ecriture " + ex.Message); }
        }

        static void LoadItems()
        {
            EnsureImportDir();   // (le dossier ou mettre ses videos, cree des le debut pour qu'on le trouve)
            WorldFiles.Check(Path.GetDirectoryName(ItemFile));   // (nouvelle partie : les objets du monde d'avant ne reviennent pas)
            if (!BagAuthority || !File.Exists(ItemFile)) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            try
            {
                foreach (string l in File.ReadAllLines(ItemFile))
                {
                    string[] c = l.Split(';');
                    if (c.Length < 10) continue;
                    CreateItem(c[0], c[1], new Vector3(float.Parse(c[3], ci), float.Parse(c[4], ci), float.Parse(c[5], ci)),
                               new Quaternion(float.Parse(c[6], ci), float.Parse(c[7], ci), float.Parse(c[8], ci), float.Parse(c[9], ci)), c[2]);
                }
                if (esineet.Count > 0) L("video : " + esineet.Count + " objet(s) repris de la sauvegarde");
            }
            catch (Exception ex) { L("video : lecture " + ex.Message); }
        }

        static void TickItems(float now)
        {
            if (!esineLoaded) { esineLoaded = true; LoadItems(); }
            if (BagAuthority && esineet.Count > 0)
            {
                if (Session.Active && Session.RemoteCount > 0 && now >= nextEsineSync) { nextEsineSync = now + 12f; foreach (Esine e in esineet) SendItem(e); }
            }
            TickInsert();
            TickScreens(now);
            TickKamera(now);
            TickKartturi(now);
            TickFilms(now);
            TickLabels();
            TickMontageKey(KameraBusy || Viewfinder ? null : aimedTape ?? HeldTape(), now);
            TickMontage(now);
        }

        // ---------------------------------------------------------------- magnetoscope et ecran
        static bool Held(Esine e) { return e.Go != null && e.Go.transform.parent != null; }   // (dans la main du jeu)

        static string Tape(Esine vcr) { int i = vcr.Data.IndexOf('|'); return i < 0 ? vcr.Data : vcr.Data.Substring(0, i); }
        static bool Playing(Esine vcr) { return vcr.Data.EndsWith("|1"); }

        static Esine pendingTape, pendingVcr;
        static int pendingFrame;
        static int idSeq;

        // Clic sur le magnetoscope avec une cassette en main : la main du jeu la lache (clic gauche), puis on l'insere.
        static void TickInsert()
        {
            if (pendingTape == null) return;
            if (Time.frameCount - pendingFrame > 30 || pendingTape.Go == null || pendingVcr.Go == null) { pendingTape = null; return; }
            if (Held(pendingTape)) return;
            Esine t = pendingTape, v = pendingVcr;
            pendingTape = null;
            string content = string.IsNullOrEmpty(t.Data) ? "tyhja" : t.Data;
            RemoveItem(t, true);
            SetData(v, content + "|0");
            try { MasterAudio.PlaySound3DAtVector3AndForget("HouseFoley", v.Go.transform.position, 0.6f, null, 0f, "mailbox_open"); } catch { }
            L("video : cassette inseree dans " + v.Id);
        }

        static void Eject(Esine vcr)
        {
            if (rec != null && rec.Cam == vcr) StopRec("ejection");
            string tape = Tape(vcr);
            if (tape.Length == 0) return;
            Transform t = vcr.Go.transform;
            SetData(vcr, "|0");
            // sur le dessus de l'appareil : toujours libre (devant, elle naissait parfois dans un mur, une table ou le sol
            // et passait au travers ; JD, 12/10)
            Bounds b = vcr.Col != null ? vcr.Col.bounds : new Bounds(t.position, Vector3.one * 0.2f);
            Vector3 fw = t.forward; fw.y = 0f;
            Quaternion rot = fw.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(fw.normalized) : Quaternion.identity;
            SpawnItem(KVhs, tape, new Vector3(b.center.x, b.max.y + 0.045f, b.center.z), rot);
            L("video : cassette ejectee de " + vcr.Id);
        }

        // ecrans du jeu (une fois) + TV achetees
        static List<Transform> gameScreens;
        static Transform ScreenNear(Vector3 p)
        {
            if (gameScreens == null)
            {
                gameScreens = new List<Transform>();
                foreach (string s in new[] { "HOMENEW/Functions/FunctionsDisable/TV/TVScreen", "JAIL/TV/TVScreen", "PERAJARVI/Kunnalliskoti/Functions/RoomVenttiPig/LOD1/TV/TVScreen", "JOBS/JOKKEHOME/HouseDrunkNew/LOD1/Stuff/table/tv/TVScreen" })
                { GameObject g = FindPath(s); if (g != null) gameScreens.Add(g.transform); }
                byName = null;
            }
            Transform best = null; float bd = 2.5f * 2.5f;
            foreach (Esine e in esineet) if (e.Kind == KTv && e.Screen != null && !Held(e)) { float d = (e.Screen.position - p).sqrMagnitude; if (d < bd) { bd = d; best = e.Screen; } }
            foreach (Transform s in gameScreens) if (s != null) { float d = (s.position - p).sqrMagnitude; if (d < bd) { bd = d; best = s; } }
            return best;
        }

        class Showing { public Transform Screen; public Material[] Saved; public AudioSource Audio; public Esine Vcr; public Film Film; public int Frame = -1; public Texture2D Tex; public Material Mat; }
        static readonly List<Showing> showing = new List<Showing>();
        static Texture2D noiseTex;
        static Material noiseMat;
        static AudioClip hissClip;
        static float nextNoise;
        static Color32[] noisePx;
        static System.Random noiseRnd = new System.Random(7);

        static void TickScreens(float now)
        {
            // ce qui doit jouer : chaque magnetoscope en lecture, sur la TV la plus proche
            foreach (Esine v in esineet)
            {
                if (v.Kind != KNauhuri || v.Go == null) continue;
                bool on = Playing(v) && Tape(v).Length > 0 && !Held(v);
                Showing s = showing.Find(x => x.Vcr == v);
                Transform scr = on ? ScreenNear(v.Go.transform.position) : null;
                if (s != null && s.Screen != scr) { StopShow(s); s = null; }
                if (scr != null && s == null) StartShow(v, scr);
            }
            for (int i = showing.Count - 1; i >= 0; i--) if (showing[i].Vcr.Go == null || !esineet.Contains(showing[i].Vcr)) StopShow(showing[i]);
            foreach (Showing s in showing) ShowFilm(s, now);
            if (showing.Exists(s => s.Film == null) && now >= nextNoise) { nextNoise = now + 0.04f; Noise(); }
        }

        static void StartShow(Esine vcr, Transform scr)
        {
            Renderer r = scr.GetComponent<Renderer>();
            if (r == null) return;
            if (noiseMat == null)
            {
                noiseTex = new Texture2D(128, 96, TextureFormat.RGB24, false) { filterMode = FilterMode.Point, name = "kauppa_kohina" };
                noisePx = new Color32[128 * 96];
                Shader us = Shader.Find("Unlit/Texture");
                noiseMat = new Material(us != null ? us : r.sharedMaterial.shader) { mainTexture = noiseTex, name = "kauppa_kohina" };
                Noise();
            }
            var s = new Showing { Screen = scr, Saved = r.sharedMaterials, Vcr = vcr };
            r.sharedMaterial = noiseMat;   // (cassette vierge : neige ; les films viendront ici)
            if (hissClip == null)
            {
                int n = 22050 * 2;
                var d = new float[n]; float lp = 0f;
                for (int i = 0; i < n; i++) { lp = lp * 0.55f + ((float)noiseRnd.NextDouble() * 2f - 1f) * 0.45f; d[i] = lp * 0.5f; }
                hissClip = AudioClip.Create("kauppa_kohina", n, 1, 22050, false);
                hissClip.SetData(d, 0);
            }
            var a = scr.gameObject.AddComponent<AudioSource>();
            a.clip = hissClip; a.loop = true; a.volume = 0.3f; a.spatialBlend = 1f; a.minDistance = 1f; a.maxDistance = 14f; a.rolloffMode = AudioRolloffMode.Linear;
            a.Play();
            s.Audio = a;
            showing.Add(s);
            L("video : lecture de " + vcr.Id + " sur " + scr.name);
        }

        static void StopShow(Showing s)
        {
            if (s.Screen != null)
            {
                Renderer r = s.Screen.GetComponent<Renderer>();
                if (r != null && s.Saved != null) r.sharedMaterials = s.Saved;
            }
            if (s.Audio != null) UnityEngine.Object.Destroy(s.Audio);
            if (s.Mat != null) UnityEngine.Object.Destroy(s.Mat);
            if (s.Tex != null) UnityEngine.Object.Destroy(s.Tex);
            showing.Remove(s);
        }

        static void StopScreen(Esine e)
        {
            for (int i = showing.Count - 1; i >= 0; i--)
                if (showing[i].Vcr == e || (e.Screen != null && showing[i].Screen == e.Screen)) StopShow(showing[i]);
        }

        static void Noise()
        {
            if (noiseTex == null) return;
            for (int i = 0; i < noisePx.Length; i++) { byte b = (byte)noiseRnd.Next(256); noisePx[i] = new Color32(b, b, b, 255); }
            noiseTex.SetPixels32(noisePx); noiseTex.Apply(false);
        }

        // ---------------------------------------------------------------- visee des objets
        static Esine lookedItem;
        static float vcrFDown = -1f;

        // Appele par Interact quand rien de la boutique n'est vise. Renvoie vrai si un objet video est vise.
        static bool InteractItems(Ray ray, float now)
        {
            aimedTape = null;
            if (Viewfinder || MontageOpen) { if (lookedItem != null) { ClearGui(); lookedItem = null; } return false; }
            Esine hit = null; float best = 1.6f; RaycastHit h;
            Esine held = null;
            foreach (Esine e in esineet) if (e.Kind == KVhs && Held(e)) held = e;
            foreach (Esine e in esineet)
                if ((e.Kind == KNauhuri || e.Kind == KKamera || e.Kind == KVhs) && e.Go != null && !Held(e) && e.Col.Raycast(ray, out h, best)) { best = h.distance; hit = e; }
            if (hit != lookedItem) { if (lookedItem != null) ClearGui(); lookedItem = hit; vcrFDown = -1f; }
            if (hit == null) return false;
            if (hit.Kind == KVhs)
            {
                // cassette posee : son titre, et R = montage
                string ti = TapeTitle(hit.Data);
                guiUse.Value = true;
                guiText.Value = Lang.TG("CASSETTE", "TAPE") + (ti.Length > 0 ? " « " + ti.ToUpperInvariant() + " »" : "") + " - " + Keys.Label(Keys.CameraRec) + Lang.TG(" : MONTAGE", ": EDITING");
                aimedTape = hit;
                return true;
            }
            FsmBool asm = FsmVariables.GlobalVariables.FindFsmBool("GUIassemble");
            string tape = Tape(hit);
            if (held != null && tape.Length == 0)
            {
                if (asm != null) asm.Value = true;
                guiText.Value = Lang.TG("INSÉRER LA CASSETTE", "INSERT THE TAPE");
                if (Input.GetMouseButtonDown(0)) { pendingTape = held; pendingVcr = hit; pendingFrame = Time.frameCount; }
                return true;
            }
            if (asm != null) asm.Value = false;
            guiUse.Value = true;
            if (hit.Kind == KKamera)
            {
                // pose : R = enregistrer / arreter (pour se filmer a plusieurs ; JD, 12/10), F tenu = ejecter
                bool recording = hit.Data.EndsWith("|1") || hit.Data.EndsWith("|2");
                string rk = Keys.Label(Keys.CameraRec);
                if (tape.Length == 0) guiText.Value = Lang.TG("CAMÉSCOPE (PAS DE CASSETTE)", "CAMCORDER (NO TAPE)");
                else guiText.Value = rk + (recording ? Lang.TG(" : ARRÊTER", ": STOP") : Lang.TG(" : ENREGISTRER", ": RECORD")) + Lang.TG(" - F TENU : ÉJECTER", " - HOLD F: EJECT");
                if (tape.Length == 0) return true;
                if (Input.GetKeyDown(Keys.CameraRec))
                {
                    if (rec != null && rec.Cam == hit) StopRec("touche");
                    else if (recording) SetData(hit, tape + "|0");   // (filme chez un autre joueur : il s'arrete chez lui)
                    else StartRec(hit);
                }
                if (Input.GetKeyDown(KeyCode.F)) vcrFDown = now;
                if (vcrFDown >= 0f)
                {
                    if (now - vcrFDown >= 0.8f) { vcrFDown = -1f; Eject(hit); }
                    else if (Input.GetKeyUp(KeyCode.F)) vcrFDown = -1f;
                }
                return true;
            }
            bool tv = ScreenNear(hit.Go.transform.position) != null;
            string title = TapeTitle(tape);
            if (tape.Length == 0) guiText.Value = Lang.TG("MAGNÉTOSCOPE (VIDE)", "VCR (EMPTY)");
            else guiText.Value = (title.Length > 0 ? "« " + title.ToUpperInvariant() + " » " : "") + (Playing(hit) ? Lang.TG("F : STOP", "F: STOP") : Lang.TG("F : LECTURE", "F: PLAY")) + Lang.TG(" - F TENU : ÉJECTER", " - HOLD F: EJECT")
                                 + (tv ? "" : Lang.TG(" (PAS DE TV À CÔTÉ)", " (NO TV NEARBY)"));
            if (tape.Length == 0) return true;
            if (Input.GetKeyDown(KeyCode.F)) vcrFDown = now;
            if (vcrFDown >= 0f)
            {
                if (now - vcrFDown >= 0.8f) { vcrFDown = -1f; Eject(hit); }
                else if (Input.GetKeyUp(KeyCode.F)) { vcrFDown = -1f; SetData(hit, tape + (Playing(hit) ? "|0" : "|1")); }
            }
            return true;
        }

        // essais : TV et magnetoscope poses cote a cote dans la boutique (coin cafe), cassette inseree, lecture
        static void TestVcr()
        {
            Esine tv = esineet.Find(e => e.Kind == KTv), vcr = esineet.Find(e => e.Kind == KNauhuri), tape = esineet.FindLast(e => e.Kind == KVhs && TapeFilm(e.Data).Length > 0) ?? esineet.Find(e => e.Kind == KVhs);
            L("essai : objets video " + esineet.Count + " (tv " + (tv != null) + ", magnetoscope " + (vcr != null) + ", cassette " + (tape != null) + ")");
            if (tv == null || vcr == null) return;
            Quaternion r = root.transform.rotation;   // (facades vers -z, vers la camera d'essai)
            tv.Go.transform.position = root.transform.TransformPoint(new Vector3(0.6f, 0.3f, 2.3f)); tv.Go.transform.rotation = r;
            vcr.Go.transform.position = root.transform.TransformPoint(new Vector3(0.6f, 0.1f, 1.7f)); vcr.Go.transform.rotation = r;
            tv.Rb.velocity = vcr.Rb.velocity = Vector3.zero;
            string content = tape != null ? tape.Data : Tape(vcr).Length > 0 ? Tape(vcr) : "tyhja";
            if (tape != null) RemoveItem(tape, true);
            SetData(vcr, content + "|1");
        }

        // ---------------------------------------------------------------- en rayon
        static void VideoOffers()
        {
            // cassettes : tablette du haut de la gondole, cote porte, couchees, tranche vers les clients
            float top = 0.14f + 3f * (1.45f - 0.2f) / 3.3f + 0.025f;
            var vhs = new Offer { Item = KVhs, Price = 19, Fr = "CASSETTE VHS", En = "VHS TAPE" };
            for (int i = 0; i < 6; i++) Display(vhs, KVhs, new Vector3(-2.52f + i * 0.2f, top, -0.02f), 0f, 78f);   // debout, etiquette vers les clients
            OfferCol(vhs, new Vector3(-2.02f, top + 0.08f, -0.02f), new Vector3(1.3f, 0.22f, 0.35f));
            Plane("hlvhs", new Vector3(-2.02f, top - 0.075f, 0.152f), 0.18f, 0.09f, Vector3.forward);
            // magnetoscopes : etagere basse du mur gauche (niveau du milieu), facade vers la piece ; TV dessus
            float wx = -W2 + Wall + 0.225f, front = -W2 + Wall + 0.453f;
            var vcr = new Offer { Item = KNauhuri, Price = 390, Fr = "MAGNÉTOSCOPE", En = "VCR" };
            foreach (float z in new[] { 0.5f, 1.1f }) Display(vcr, KNauhuri, new Vector3(wx, 0.525f, z), -90f);
            OfferCol(vcr, new Vector3(wx, 0.62f, 0.8f), new Vector3(0.45f, 0.25f, 1.15f));
            Plane("hlnauhuri", new Vector3(front, 0.46f, 0.8f), 0.18f, 0.09f, Vector3.right);
            var tv = new Offer { Item = KTv, Price = 590, Fr = "TÉLÉVISION", En = "TELEVISION" };
            Display(tv, KTv, new Vector3(wx, 0.9f, 0.8f), -90f);
            OfferCol(tv, new Vector3(wx, 1.15f, 0.8f), new Vector3(0.5f, 0.5f, 0.6f));
            Plane("hltv", new Vector3(front, 0.83f, 0.8f), 0.18f, 0.09f, Vector3.right);
            // camescopes : etagere haute de droite (troisieme planche), objectif vers la piece
            float kt = 0.1f + 2f * (1.9f - 0.1f) / 4f + 0.025f;
            var kam = new Offer { Item = KKamera, Price = 890, Fr = "CAMÉSCOPE", En = "CAMCORDER" };
            foreach (float z in new[] { 1.78f, 2.27f }) Display(kam, KKamera, new Vector3(wx, kt, z), 90f);
            OfferCol(kam, new Vector3(wx, kt + 0.12f, 2.02f), new Vector3(0.45f, 0.3f, 1.0f));
            Plane("hlkamera", new Vector3(front, kt - 0.06f, 2.02f), 0.18f, 0.09f, Vector3.right);
            KartturiOffers(wx, front);
            L("en rayon : cassettes 19 mk, magnetoscopes 390 mk, television 590 mk, camescopes 890 mk, KARTTURI " + KartturiPrice + " mk");
        }

        // un article expose (sans physique), pose par le bas en "at" ; yaw NaN : ecran de la TV vers la piece (+x)
        static void Display(Offer o, string kind, Vector3 at, float yaw, float tilt = 0f)
        {
            Vector3 size; Transform screen;
            GameObject g = Model(kind, out size, out screen);
            g.transform.SetParent(root.transform, false);
            g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(tilt, 0f, 0f);
            Bounds b = LocalBounds(g);
            g.transform.localPosition = new Vector3(at.x - b.center.x, at.y - b.min.y + 0.002f, at.z - b.center.z);
            o.Shown.Add(g);
        }

        // cote ecran de la TV, dans le repere du modele : normale de la dalle tournee vers l'exterieur de la boite
        static Vector3 ScreenFront(GameObject g, Transform screen)
        {
            if (screen == null) return Vector3.forward;
            var mf = screen.GetComponent<MeshFilter>();
            Vector3 c = screen.localPosition, n = Vector3.zero;
            if (mf != null && mf.sharedMesh != null)
            {
                Vector3[] nr = mf.sharedMesh.normals;
                foreach (Vector3 v in nr) n += v;
                if (n.sqrMagnitude > 1e-6f) n = screen.localRotation * n.normalized;
                if (mf.sharedMesh.vertexCount > 0) c = screen.localPosition + screen.localRotation * Vector3.Scale(mf.sharedMesh.bounds.center, screen.localScale);
            }
            if (n.sqrMagnitude < 1e-6f) return c;
            return Vector3.Dot(n, c) < 0f ? -n : n;   // (normale du cote oppose au centre de la boite)
        }

        static void OfferCol(Offer o, Vector3 c, Vector3 s)
        {
            var g = new GameObject("osuus_osto_" + o.Item);
            g.layer = 2;
            g.transform.SetParent(root.transform, false);
            g.transform.localPosition = c;
            var bc = g.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = s;
            o.Col = bc;
            offers.Add(o);
        }

        public static void OnLevelLoadedVideo()
        {
            OnLevelLoadedKamera();
            OnLevelLoadedKartturi();
            OnLevelLoadedFilms();
            OnLevelLoadedMontage();
            esineet.Clear(); esineLoaded = false; showing.Clear(); gameScreens = null; lookedItem = null; pendingTape = null;
            texMats.Clear(); blackPlastic = null; greyMetal = null; plainMat = null; noiseMat = null; noiseTex = null;
        }
    }
}
