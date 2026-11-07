using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Carnet du copilote (suite de KauppaCarnet.cs) : reconnaissance, envoi aux autres, lampe.
    //  - roue des symboles (carnet leve, sur une speciale) : R tenue ouvre la roue (la visee est coupee, la souris deplace le
    //    choix) ; clic gauche ajoute le symbole vise a la note en cours (cote, fleche, modificateurs...), clic droit retire le
    //    dernier ; R relachee pose la note a l'endroit ou etait la voiture a l'ouverture. Retour arriere : retire la note la plus
    //    proche (80 m au plus). Le carnet actif est ecrit aussitot (Carnet.Edit).
    //  - les autres voient la vraie page : le copilote annonce son carnet (empreinte) et sa lampe toutes les 5 s, et a chaque
    //    changement (@carnetj) ; qui ne l'a pas le demande (@carnet?) ; il part en morceaux de 900 octets (@carnetp, binaire
    //    compact, Carnet.Pack, 64 Ko au plus), verifie a l'arrivee (empreinte, memes regles que le fichier).
    //  - lampe (la nuit) : petite lampe de poche, faisceau sur la page ; allumee d'office quand il fait sombre, L l'allume ou
    //    l'eteint ; les autres voient le faisceau.
    public static partial class Kauppa
    {
        // ---------------------------------------------------------------- roue
        static readonly int[] RingIn = { 10, 1, 2, 3, 4, 5, 6, 13, 11, 44, 42 };     // H, 1 a 6, fleche gauche, fleche droite, carre gauche, carre droite
        static readonly int[] RingOut = { 18, 30, 20, 31, 29, 15, 16, 17, 22, 28 };  // bosse, pont, !, tout droit, arrivee, long, resserre, s'ouvre, ne coupe pas, enchaine
        static bool cnWheel;
        static int cnWheelStage = -1, cnWheelM, cnHover = -1;
        static Vector2 cnCursor;
        static readonly List<int> cnCompose = new List<int>();
        static readonly List<Behaviour> cnBlocked = new List<Behaviour>();
        static string cnToast = ""; static float cnToastUntil;

        static void TickCarnetEdit(float now)
        {
            CarnetSivu.Page p = null;
            if (cnStage != 2 || cnEq == null || !pages.TryGetValue(cnEq, out p) || p == null) { if (cnWheel) WheelClose(false); return; }
            if (!cnWheel && Input.GetKeyDown(Keys.CarnetWheel))
            {
                if (p.Stage < 0 || p.Trk == null || Kartturi.TrackedStage(p.Trk) < 0) { Say2(Lang.T("Carnet : roule sur une spéciale pour poser une note", "Notebook: drive on a stage to add a note")); return; }
                cnWheel = true; cnWheelStage = p.Stage; cnWheelM = Mathf.RoundToInt(p.M); cnCursor = Vector2.zero; cnHover = -1; cnCompose.Clear();
                BlockLook(true);
            }
            if (cnWheel)
            {
                cnCursor += new Vector2(Input.GetAxis("Mouse X"), -Input.GetAxis("Mouse Y")) * Style.Px(14);
                float r1 = Style.Px(150), r2 = Style.Px(250);
                if (cnCursor.magnitude > r2) cnCursor = cnCursor.normalized * r2;
                cnHover = HoverOf(cnCursor, r1 * 0.3f, r1);
                if (Input.GetMouseButtonDown(0) && cnHover >= 0 && cnCompose.Count < 8) cnCompose.Add(cnHover);
                if (Input.GetMouseButtonDown(1) && cnCompose.Count > 0) cnCompose.RemoveAt(cnCompose.Count - 1);
                if (!Input.GetKey(Keys.CarnetWheel)) WheelClose(true);
                return;
            }
            if (Input.GetKeyDown(Keys.CarnetErase))
            {
                if (p.Stage < 0) { Say2(Lang.T("Carnet : hors spéciale", "Notebook: not on a stage")); return; }
                string what;
                Carnet.Edit(p.Stage, null, Mathf.RoundToInt(p.M), out what);
                Say2(Lang.T("Carnet : ", "Notebook: ") + what);
            }
        }

        static int HoverOf(Vector2 c, float dead, float r1)
        {
            float r = c.magnitude;
            if (r < dead) return -1;
            float a = Mathf.Repeat(Mathf.Atan2(c.x, -c.y) * Mathf.Rad2Deg, 360f);   // (0 en haut, sens des aiguilles)
            int[] ring = r < r1 ? RingIn : RingOut;
            return ring[Mathf.Clamp(Mathf.FloorToInt((a + 180f / ring.Length) / (360f / ring.Length)) % ring.Length, 0, ring.Length - 1)];
        }

        static void WheelClose(bool place)
        {
            cnWheel = false;
            BlockLook(false);
            if (!place || cnCompose.Count == 0) return;
            string err;
            Carnet.Note n = Compose(cnCompose, cnWheelM, out err);
            if (n == null) { Say2(Lang.T("Carnet : ", "Notebook: ") + err); return; }
            string what;
            Carnet.Edit(cnWheelStage, n, 0, out what);
            Say2(Lang.T("Carnet : ", "Notebook: ") + what + " (" + cnWheelM + " m)");
        }

        // symboles choisis -> une note (ordre libre) ; erreur si une cote ou une fleche manque
        static Carnet.Note Compose(List<int> cells, int m, out string err)
        {
            err = null;
            int grade = -1, side = 0, sq = 0, mods = 0; string other = null; bool bang = false;
            foreach (int c in cells)
            {
                if (c == 10) grade = 0;
                else if (c >= 1 && c <= 6) grade = c;
                else if (c == 13) side = -1; else if (c == 11) side = 1;
                else if (c == 44) sq = -1; else if (c == 42) sq = 1;
                else if (c == 18) other = "bosse"; else if (c == 30) other = "pont"; else if (c == 31) other = "droit"; else if (c == 29) other = "arrivee";
                else if (c == 20) bang = true;
                else if (c == 15) mods |= Carnet.MLong;
                else if (c == 16) { mods |= Carnet.MResserre; mods &= ~Carnet.MOuvre; }
                else if (c == 17) { mods |= Carnet.MOuvre; mods &= ~Carnet.MResserre; }
                else if (c == 22) mods |= Carnet.MNc;
                else if (c == 28) mods |= Carnet.MEnchaine;
            }
            var n = new Carnet.Note { M = Mathf.Clamp(m, 0, Carnet.MaxM) };
            if (sq != 0) { n.Type = "carre"; n.Side = sq; }
            else if (grade >= 0 && side != 0) { n.Type = "virage"; n.Side = side; n.Grade = grade; }
            else if (grade >= 0 || side != 0) { err = Lang.T("il faut une cote et une flèche", "a corner needs a grade and an arrow"); return null; }
            else if (other != null) n.Type = other;
            else if (bang) { n.Type = "danger"; bang = false; }
            else { err = Lang.T("rien à poser", "nothing to add"); return null; }
            if (bang) mods |= Carnet.MDanger;
            if (n.Type != "virage" && n.Type != "carre") mods &= Carnet.MDanger | Carnet.MEnchaine;
            n.Mods = mods;
            return n;
        }

        // coupe la visee du joueur (la souris deplace le choix de la roue)
        static void BlockLook(bool on)
        {
            if (on && cnBlocked.Count == 0)
            {
                GameObject pl = Game.PlayerGo;
                if (pl == null) return;
                var list = new List<Behaviour>(pl.GetComponents<Behaviour>());
                Transform cam = pl.transform.Find("Pivot/AnimPivot/Camera/FPSCamera");
                if (cam != null) list.AddRange(cam.GetComponents<Behaviour>());
                foreach (Behaviour b in list) if (b != null && b.enabled && b.GetType().Name.Contains("MouseLook")) { b.enabled = false; cnBlocked.Add(b); }
            }
            else if (!on)
            {
                foreach (Behaviour b in cnBlocked) if (b != null) b.enabled = true;
                cnBlocked.Clear();
            }
        }

        static void Say2(string s) { cnToast = s; cnToastUntil = Time.realtimeSinceStartup + 3f; L(s); }

        // Core.OnGUI (par DrawCarnetHint) : la roue et le dernier message
        static void DrawCarnetWheel()
        {
            if (Time.realtimeSinceStartup < cnToastUntil && cnToast.Length > 0)
                Style.Text(new Rect(0, Screen.height * 0.18f, Screen.width, Style.Px(30)), cnToast, 18, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.9f));
            if (!cnWheel || Event.current.type != EventType.Repaint) return;
            Texture atlas = CarnetSivu.Atlas;
            if (atlas == null) return;
            Vector2 c = new Vector2(Screen.width / 2f, Screen.height / 2f);
            float r1 = Style.Px(150), r2 = Style.Px(250), s = Style.Px(78);
            Color old = GUI.color;
            GUI.color = new Color(0.98f, 0.96f, 0.9f, 0.82f);
            GUI.DrawTexture(new Rect(c.x - r2 - s * 0.6f, c.y - r2 - s * 0.6f, 2 * r2 + 1.2f * s, 2 * r2 + 1.2f * s), CarnetSivu.Disc);
            GUI.color = old;
            for (int k = 0; k < 2; k++)
            {
                int[] ring = k == 0 ? RingIn : RingOut;
                float rr = k == 0 ? r1 * 0.68f : (r1 + r2) / 2f;
                for (int i = 0; i < ring.Length; i++)
                {
                    float a = i * 360f / ring.Length * Mathf.Deg2Rad;
                    Vector2 p = c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * rr;
                    bool hov = ring[i] == cnHover;
                    float sz = hov ? s * 1.3f : s;
                    GUI.DrawTextureWithTexCoords(new Rect(p.x - sz / 2f, p.y - sz / 2f, sz, sz), atlas, CarnetSivu.CellRect(ring[i]));
                }
            }
            // la note en cours, au centre ; le curseur
            float x = c.x - cnCompose.Count * s * 0.32f;
            foreach (int cell in cnCompose) { GUI.DrawTextureWithTexCoords(new Rect(x - s * 0.36f, c.y - s * 0.5f, s * 0.72f, s * 0.72f), atlas, CarnetSivu.CellRect(cell)); x += s * 0.64f; }
            GUI.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);
            GUI.DrawTexture(new Rect(c.x + cnCursor.x - Style.Px(5), c.y + cnCursor.y - Style.Px(5), Style.Px(10), Style.Px(10)), CarnetSivu.Disc);
            GUI.color = old;
            Style.Text(new Rect(0, c.y + r2 + s * 0.4f, Screen.width, Style.Px(26)), Lang.T("CLIC : AJOUTER  -  CLIC DROIT : RETIRER  -  RELÂCHER ", "CLICK: ADD  -  RIGHT CLICK: UNDO  -  RELEASE ") + Keys.Label(Keys.CarnetWheel) + Lang.T(" : POSER", ": PLACE"), 15, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.85f));
        }

        // ---------------------------------------------------------------- envoi aux autres
        class CnSaid { public int Who; public string Hash = ""; public bool Lamp, Up; public float At; }
        static readonly Dictionary<string, CnSaid> cnSaidBy = new Dictionary<string, CnSaid>();          // objet -> carnet annonce par qui le tient
        static readonly Dictionary<string, Carnet.Book> cnBooks = new Dictionary<string, Carnet.Book>();   // empreinte -> carnet recu
        static readonly Dictionary<string, byte[][]> cnIn = new Dictionary<string, byte[][]>();
        static readonly Dictionary<string, float> cnAsked = new Dictionary<string, float>();
        static byte[] cnMine; static string cnMineHash = ""; static int cnMineVer = -1; static Carnet.Book cnMineBook;
        static float cnNextSay, cnSentAt; static string cnSaidSig = "";
        const int CnChunk = 900, CnMaxBytes = 64 * 1024;

        static void TickCarnetNet(float now)
        {
            if (!Session.Active || cnStage != 2 || cnEq == null) return;
            if (cnMineBook != Carnet.Active || cnMineVer != Carnet.Changes)
            {
                cnMineBook = Carnet.Active; cnMineVer = Carnet.Changes;
                cnMine = Carnet.Active != null ? Carnet.Pack(Carnet.Active) : null;
                if (cnMine != null && cnMine.Length > CnMaxBytes) { L("carnet : trop gros pour les autres (" + cnMine.Length + " octets), notes automatiques chez eux"); cnMine = null; }
                cnMineHash = cnMine != null ? Carnet.Hash(cnMine) : "";
            }
            string sig = cnEq.Id + "|" + cnMineHash + "|" + cnLampOn;
            if (now < cnNextSay && sig == cnSaidSig) return;
            cnNextSay = now + 5f; cnSaidSig = sig;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@carnetj").Str(cnEq.Id).Str(cnMineHash).Bool(cnLampOn).Bool(true), true);
        }

        // carnet baisse : dit aussitot (la pose de l'avatar et la lampe chez les autres)
        static void SayCarnetDown(Esine e)
        {
            if (!Session.Active || e == null) return;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@carnetj").Str(e.Id).Str(cnMineHash).Bool(false).Bool(false), true);
            cnSaidSig = "";
        }

        // Avatar (passager) : ce joueur a-t-il son carnet leve, et sa lampe allumee ?
        public static bool CarnetRaisedBy(int who, out bool lamp)
        {
            lamp = false;
            if (Session.Me != null && who == Session.Me.Id && cnStage == 2) { lamp = cnLampOn; return true; }   // (mon avatar : 3e personne, camescope qui filme)
            foreach (CnSaid s in cnSaidBy.Values)
                if (s.Who == who && s.Up && Time.realtimeSinceStartup - s.At < 12f) { lamp = s.Lamp; return true; }
            return false;
        }

        public static void OnCarnetSaid(int who, string id, string hash, bool lamp, bool up)
        {
            if (string.IsNullOrEmpty(id) || hash == null || hash.Length > 16) return;
            CnSaid s;
            if (!cnSaidBy.TryGetValue(id, out s)) cnSaidBy[id] = s = new CnSaid();
            if (s.Hash != hash) L("carnet de #" + who + " : " + (hash.Length > 0 ? hash : "notes automatiques"));
            s.Who = who; s.Hash = hash; s.Lamp = lamp && up; s.Up = up; s.At = Time.realtimeSinceStartup;
            float t;
            if (hash.Length > 0 && !cnBooks.ContainsKey(hash) && (!cnAsked.TryGetValue(hash, out t) || Time.realtimeSinceStartup - t > 20f))
            {
                cnAsked[hash] = Time.realtimeSinceStartup;
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@carnet?").Str(hash), true);
            }
        }

        public static void OnCarnetAsk(int who, string hash)
        {
            if (hash != cnMineHash || cnMine == null || Time.realtimeSinceStartup - cnSentAt < 8f) return;
            cnSentAt = Time.realtimeSinceStartup;
            int n = (cnMine.Length + CnChunk - 1) / CnChunk;
            for (int i = 0; i < n; i++)
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@carnetp").Str(hash).U16(i).U16(n).Bytes(cnMine, i * CnChunk, Mathf.Min(CnChunk, cnMine.Length - i * CnChunk)), true);
            L("carnet : envoye a la demande de #" + who + " (" + n + " morceaux, " + cnMine.Length + " octets)");
        }

        public static void OnCarnetPart(int who, string hash, int i, int n, byte[] data)
        {
            if (string.IsNullOrEmpty(hash) || cnBooks.ContainsKey(hash) || data == null || n <= 0 || n > (CnMaxBytes + CnChunk - 1) / CnChunk || i < 0 || i >= n || data.Length > CnChunk) return;
            byte[][] parts;
            if (!cnIn.TryGetValue(hash, out parts) || parts.Length != n)
            {
                if (cnIn.Count >= 8) cnIn.Clear();   // (au plus 8 carnets en cours de reception : un pair ne remplit pas la memoire)
                cnIn[hash] = parts = new byte[n][];
            }
            if (parts[i] != null) return;
            parts[i] = data;
            int len = 0;
            foreach (byte[] x in parts) { if (x == null) return; len += x.Length; }
            cnIn.Remove(hash);
            var b = new byte[len]; int at = 0;
            foreach (byte[] x in parts) { System.Buffer.BlockCopy(x, 0, b, at, x.Length); at += x.Length; }
            if (Carnet.Hash(b) != hash) { L("carnet de #" + who + " recu abime"); return; }
            string err;
            Carnet.Book book = Carnet.Unpack(b, out err);
            if (book == null) { L("carnet de #" + who + " refuse (" + err + ")"); return; }
            if (book.Game != Carnet.GameId) { L("carnet de #" + who + " : carnet " + book.Game + ", ignore"); return; }
            cnBooks[hash] = book;
            L("carnet de #" + who + " recu : « " + book.Name + " », " + book.Stages.Count + " speciale(s), " + len + " octets");
        }

        // carnet a afficher sur l'objet e (pas le mien) : celui que son copilote a annonce, s'il est recu
        static Carnet.Book RemoteBook(Esine e)
        {
            CnSaid s; Carnet.Book b;
            if (e == null || !cnSaidBy.TryGetValue(e.Id, out s) || s.Hash.Length == 0) return null;
            return cnBooks.TryGetValue(s.Hash, out b) ? b : null;
        }
        static bool RemoteLamp(Esine e) { CnSaid s; return e != null && cnSaidBy.TryGetValue(e.Id, out s) && s.Lamp && s.Up && Time.realtimeSinceStartup - s.At < 15f; }

        // ---------------------------------------------------------------- lampe
        static bool cnLampOn, cnLampUser, cnLampForced;
        static float cnLampLook;
        static Light[] suns;
        static readonly Dictionary<Esine, GameObject> cnLamps = new Dictionary<Esine, GameObject>();

        static bool Dark()
        {
            if (suns == null || suns.Length == 0)
            {
                var l = new List<Light>();
                foreach (Light x in UnityEngine.Object.FindObjectsOfType<Light>()) if (x.type == LightType.Directional) l.Add(x);
                suns = l.ToArray();
            }
            float best = 0f; bool up = false;
            foreach (Light x in suns)
            {
                if (x == null || !x.enabled || !x.gameObject.activeInHierarchy) continue;
                best = Mathf.Max(best, x.intensity);
                if (x.transform.forward.y < -0.08f && x.intensity > 0.25f) up = true;
            }
            return !up || best < 0.25f;
        }

        static void TickCarnetLamp(float now)
        {
            if (cnStage == 2)
            {
                if (Input.GetKeyDown(Keys.CarnetLamp) && !cnWheel) { cnLampUser = true; cnLampForced = !cnLampOn; }
                if (now >= cnLampLook) { cnLampLook = now + 2f; if (!cnLampUser) cnLampForced = Dark() || (Config.Testing && Config.GetInt("Test", "CarnetLampe", 0) != 0); }
                cnLampOn = cnLampForced;
            }
            else { cnLampOn = false; cnLampUser = false; }
            // lampes allumees : la mienne (carnet leve) et celles des carnets des autres qui l'annoncent
            foreach (Esine e in esineet)
            {
                if (e.Kind != KCarnet || e.Go == null) continue;
                bool on = e == cnEq ? cnLampOn && cnStage == 2 : RemoteLamp(e);
                GameObject lg;
                bool has = cnLamps.TryGetValue(e, out lg) && lg != null;
                if (on && !has) cnLamps[e] = lg = LampModel(e);
                else if (!on && has) { UnityEngine.Object.Destroy(lg); cnLamps.Remove(e); continue; }
                if (!on || lg == null) continue;
                // Dans la main droite (JD 08/11 : « que le model du joueur tienne la lampe avec la main ») : accrochee a l'os de la main
                // de l'avatar de celui qui tient le carnet (chez les autres ; chez moi en 3e personne, mon avatar), comme la montre au
                // poignet gauche ; a la premiere personne, accrochee a la camera, en bas a droite de l'image (la main hors champ).
                // Orientee a chaque image vers le centre de la page (la copie devant mes yeux, sinon le carnet) : le faisceau y tombe
                // quel que soit l'angle du poignet. Sans avatar trouve : a cote de la page, comme avant.
                Transform parent; Vector3 aim; bool eyeHeld = false;
                if (e == cnEq)
                {
                    Avatar meA = ThirdPerson.Active ? ThirdPerson.Me : null;
                    Transform hand = meA != null ? meA.HandBone(true) : null;
                    eyeHeld = hand == null;
                    parent = hand != null ? hand : EyeT;
                    aim = hand == null && cnView != null ? cnView.transform.position : e.Go.transform.position;
                }
                else
                {
                    Avatar av = HolderAvatar(e);
                    parent = av != null ? av.HandBone(true) : null;
                    aim = e.Go.transform.position;
                }
                if (lg.transform.parent != parent)
                {
                    lg.transform.SetParent(parent, false);
                    Vector3 ps = parent != null ? parent.lossyScale : Vector3.one;   // (os a l'echelle du modele : la lampe garde sa taille)
                    lg.transform.localScale = new Vector3(1f / Mathf.Max(1e-4f, Mathf.Abs(ps.x)), 1f / Mathf.Max(1e-4f, Mathf.Abs(ps.y)), 1f / Mathf.Max(1e-4f, Mathf.Abs(ps.z)));
                }
                if (eyeHeld) lg.transform.localPosition = new Vector3(0.17f, -0.19f, 0.30f);
                else if (parent != null) lg.transform.localPosition = Vector3.zero;
                else { Transform tg = e.Go.transform; lg.transform.position = tg.position + tg.right * 0.15f + tg.up * 0.07f - tg.forward * 0.11f; }
                Vector3 dir = aim - lg.transform.position;
                if (dir.sqrMagnitude > 1e-4f) lg.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
        }

        // avatar de celui qui a leve ce carnet (son annonce)
        static Avatar HolderAvatar(Esine e)
        {
            CnSaid s;
            if (e == null || !cnSaidBy.TryGetValue(e.Id, out s)) return null;
            foreach (Avatar av in PlayerSync.Avatars) if (av != null && av.Player != null && av.Player.Id == s.Who) return av;
            return null;
        }

        // petite lampe de poche (corps, tete, verre) et son faisceau ; construite en code, sans modele (JD 07/11)
        static GameObject LampModel(Esine e)
        {
            var g = new GameObject("carnet_lampe");
            Material body = Plain(new Color(0.12f, 0.12f, 0.13f)), head = Plain(new Color(0.55f, 0.55f, 0.57f)), glass = Plain(new Color(1f, 0.97f, 0.85f));
            // (origine : la prise, au milieu du corps ; la tete 6 cm devant, vers +z)
            Piece(g, PrimitiveType.Cylinder, new Vector3(0f, 0f, 0f), new Vector3(90f, 0f, 0f), new Vector3(0.024f, 0.05f, 0.024f), body);
            Piece(g, PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.06f), new Vector3(90f, 0f, 0f), new Vector3(0.034f, 0.014f, 0.034f), head);
            Piece(g, PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.0745f), new Vector3(90f, 0f, 0f), new Vector3(0.028f, 0.0015f, 0.028f), glass);
            var lo = new GameObject("faisceau");
            lo.transform.SetParent(g.transform, false);
            lo.transform.localPosition = new Vector3(0f, 0f, 0.08f);
            Light li = lo.AddComponent<Light>();
            li.type = LightType.Spot; li.spotAngle = 65f; li.range = 2.2f; li.intensity = 1.6f; li.color = new Color(1f, 0.95f, 0.82f);   // (65 deg : toute la page depuis la main)
            li.shadows = LightShadows.None; li.renderMode = LightRenderMode.ForcePixel;
            int layer = e.Go.layer;
            foreach (Transform t in g.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            return g;
        }

        static void OnLevelLoadedCarnetNet()
        {
            cnSaidBy.Clear(); cnIn.Clear(); cnAsked.Clear(); cnMine = null; cnMineHash = ""; cnMineVer = -1; cnMineBook = null; cnSaidSig = ""; cnNextSay = 0f;
            foreach (GameObject g in cnLamps.Values) if (g != null) UnityEngine.Object.Destroy(g);
            cnLamps.Clear(); suns = null; cnLampOn = cnLampUser = cnLampForced = false;
            if (cnWheel) { cnWheel = false; BlockLook(false); }
            cnBlocked.Clear(); cnCompose.Clear();
        }
    }
}
