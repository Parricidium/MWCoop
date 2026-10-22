using System;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Cinema KINO TAHTI : les salles.
    //  - fauteuils : chacun a le declencheur des chaises du jeu (CrouchTriggerBasic du canape de la maison, copie) : on s'y
    //    assoit comme sur une chaise ou le canape (s'accroupir devant), et les autres voient l'avatar assis (Gestures).
    //  - portes : au fond du hall, en haut des marches ; clic = ouvrir / fermer avec un billet du jour (KinoKassa), ou de
    //    l'interieur. Etat envoye a tous (@kino 1).
    //  - projecteur dans la cabine (KONEHUONE) : bobine en main + clic = mise en place ; F = projection / arret ; F tenu =
    //    retirer la bobine. Le film (memes films que les cassettes : KauppaFilmi) passe sur l'ecran de la salle, chez tous en
    //    meme temps, son a l'ecran ; lumieres de la salle baissees, faisceau du projecteur. Etat envoye a tous (@kino 2),
    //    redit par l'hote toutes les 10 s (arrivants : la projection reprend au bon endroit) ; bobines en place gardees a
    //    la sauvegarde (MWCoop-kino.txt).
    public static partial class Kauppa
    {
        class SeatSpot { public int Salle, Row, Index; public Vector3 Pos; public float Yaw; }
        static readonly List<SeatSpot> kinoSeatSpots = new List<SeatSpot>();

        class KinoDoor { public int Sali; public Transform Leaf; public Collider Col; public bool Open; public float Angle, OpenedAt; }
        class Projector
        {
            public int Sali; public float S; public Collider Col; public Transform[] Reels; public string Content = ""; public bool Playing; public float Start;
            public Renderer Screen; public Material Idle, Mat; public Texture2D Tex; public AudioSource Audio; public Film Film; public int Frame = -1;
            public GameObject Beam; public float FDown = -1f;
            public LinkPlay Link; public bool LinkFailTold;   // (bobine « lien » : KinoLinkki)
        }
        static readonly KinoDoor[] kinoDoors = new KinoDoor[3];
        static readonly Projector[] projectors = new Projector[3];
        static float kinoNextSync, kinoLights = 1f;
        static Esine pendingReel; static Projector pendingProj; static int pendingReelFrame;
        static bool kinoLoaded, seatHintDone;
        public static void OnLevelLoadedKino() { for (int i = 1; i <= 2; i++) if (projectors[i] != null) LinkStop(projectors[i]); }   // (lecteurs natifs fermes avant le changement de scene)

        static void ResetKinoSali()
        {
            kinoSeatSpots.Clear(); kinoTexts.Clear();
            for (int i = 0; i < 3; i++) { kinoDoors[i] = null; projectors[i] = null; }
            pendingReel = null; pendingProj = null; kinoLoaded = false; seatHintDone = false;
        }

        static void KinoSaliSetup()
        {
            int trig = KinoSeatTriggers();
            foreach (float s in new[] { -1f, 1f })
            {
                int sali = s < 0 ? 1 : 2;
                kinoDoors[sali] = KinoMakeDoor(s, sali);
                projectors[sali] = KinoMakeProjector(s, sali);
            }
            L("cinema : " + kinoSeatSpots.Count + " places (" + trig + " declencheurs de siege), portes et projecteurs en place");
        }

        // ---------------------------------------------------------------- fauteuils : declencheurs des chaises du jeu
        static int KinoSeatTriggers()
        {
            GameObject src = FindPath("HOMENEW/FurnitureDisable/sofa/CrouchTriggerBasic");
            if (src == null) foreach (string p in new[] { "HOMENEW/FurnitureDisable/chair/CrouchTriggerBasic", "COTTAGE/LOD/Chair/CrouchTriggerBasic" }) { src = FindPath(p); if (src != null) break; }
            if (src == null) { L("cinema : pas de declencheur de chaise du jeu a copier, fauteuils sans place assise"); return 0; }
            var holder = new GameObject("kino_istuimet");
            holder.transform.SetParent(kino.transform, false);
            int n = 0;
            foreach (SeatSpot sp in kinoSeatSpots)
            {
                var g = (GameObject)UnityEngine.Object.Instantiate(src);
                g.name = "CrouchTriggerKino";
                g.SetActive(false);
                // (un parent par fauteuil, a sa taille : Gestures lit le sens du siege sur le meuble, parent du declencheur)
                var seat = new GameObject("kino_istuin");
                seat.transform.SetParent(holder.transform, false);
                seat.transform.localPosition = sp.Pos;
                seat.transform.localRotation = Quaternion.Euler(0f, sp.Yaw, 0f);
                g.transform.SetParent(seat.transform, false);
                g.transform.localPosition = Vector3.zero; g.transform.localRotation = Quaternion.identity; g.transform.localScale = Vector3.one;
                var cap = g.GetComponent<CapsuleCollider>();
                if (cap != null) { cap.direction = 1; cap.radius = 0.24f; cap.height = 1.7f; cap.center = new Vector3(0f, 0.35f, 0.1f); }
                foreach (PlayMakerFSM f in g.GetComponents<PlayMakerFSM>())
                {
                    if (f.FsmName != "PlayerTrigger") continue;
                    FsmBool added = f.FsmVariables.FindFsmBool("Added");
                    if (added != null) added.Value = false;   // (la copie s'inscrit elle-meme dans Systems/CrouchTriggers)
                }
                g.SetActive(true);
                n++;
            }
            return n;
        }

        // ---------------------------------------------------------------- portes
        static KinoDoor KinoMakeDoor(float s, int sali)
        {
            float z0 = -KD + 0.35f, w = 1.5f, h = 2.2f, th = 0.06f;
            var pivot = new GameObject("kino_saliovi" + sali);
            pivot.transform.SetParent(kino.transform, false);
            pivot.transform.localPosition = new Vector3(s * KPart, KBack + 0.01f, z0 + 0.02f);
            parts.Clear(); cols.Clear();
            var keep = root; root = pivot;
            // battant rembourre rouge (capitonne : losanges en laiton), hublot, poignee
            Box(new Vector3(-th / 2, 0f, 0f), new Vector3(th / 2, h - 0.02f, w - 0.04f), "ki_istuin", false);
            Box(new Vector3(-th / 2 - 0.01f, 0f, 0f), new Vector3(th / 2 + 0.01f, 0.08f, w - 0.04f), "ki_kulta", false);
            for (float yy = 0.4f; yy < h - 0.3f; yy += 0.35f)
                for (float zz = 0.25f; zz < w - 0.2f; zz += 0.35f)
                    foreach (float sx in new[] { -1f, 1f })
                        Box(new Vector3(sx * (th / 2) - 0.006f, yy, zz), new Vector3(sx * (th / 2) + 0.006f, yy + 0.025f, zz + 0.025f), "ki_kulta", false);
            foreach (float sx in new[] { -1f, 1f })
                Box(new Vector3(sx * (th / 2 + 0.05f) - 0.015f, 0.95f, w - 0.25f), new Vector3(sx * (th / 2 + 0.05f) + 0.015f, 1.25f, w - 0.21f), "ki_kulta", false);
            Flush();
            root = keep;
            var cg = new GameObject("kino_saliovi_coll");
            cg.transform.SetParent(pivot.transform, false);
            cg.transform.localPosition = new Vector3(0f, h / 2, (w - 0.04f) / 2);
            var bc = cg.AddComponent<BoxCollider>(); bc.size = new Vector3(0.1f, h, w - 0.04f);
            return new KinoDoor { Sali = sali, Leaf = pivot.transform, Col = bc };
        }

        static void SetKinoDoor(KinoDoor d, bool open, bool send)
        {
            if (d == null || d.Open == open) return;
            d.Open = open; d.OpenedAt = Time.realtimeSinceStartup;
            try { MasterAudio.PlaySound3DAtVector3AndForget("Store", d.Leaf.position, 0.9f, null, 0f, open ? "door_open" : "door_close"); } catch { }
            if (send && Session.Active) Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@kino").U8(1).U8(d.Sali).Bool(open), true);
        }

        // ---------------------------------------------------------------- projecteur et ecran
        static Projector KinoMakeProjector(float s, int sali)
        {
            var p = new Projector { Sali = sali, S = s, Reels = new Transform[2] };
            float x = s * 10.15f;
            parts.Clear(); cols.Clear();
            var keep = root; root = kino;
            // pied, carter (vert sombre), lanterne, objectif vers la fenetre de projection
            Box(new Vector3(x - 0.25f, KBack, -0.35f), new Vector3(x + 0.25f, 1.9f, 0.35f), "ki_projektori");   // (sur le plancher de la cabine)
            Box(new Vector3(x - 0.3f, 1.9f, -0.22f), new Vector3(x + 0.3f, 2.6f, 0.22f), "ki_projektori");
            Box(new Vector3(x - 0.22f, 2.6f, -0.18f), new Vector3(x + 0.1f, 2.95f, 0.18f), "ki_musta");
            Flush();
            root = keep;
            var lens = new GameObject("kino_objektiivi");
            lens.transform.SetParent(kino.transform, false);
            lens.AddComponent<MeshFilter>().sharedMesh = Cylinder(new Vector3(x + s * 0.3f, 2.4f, 0f), new Vector3(x + s * 0.55f, 2.4f, 0f), 0.07f, 16);
            lens.AddComponent<MeshRenderer>().sharedMaterial = mats["ki_alu"];
            // bras et bobines (montrees seulement avec une bobine en place, tournent pendant la projection)
            Material reelMat = mats["ki_kulta"];
            for (int k = 0; k < 2; k++)
            {
                float zc = k == 0 ? -0.32f : 0.32f;
                var arm = new GameObject("kino_varsi");
                arm.transform.SetParent(kino.transform, false);
                arm.AddComponent<MeshFilter>().sharedMesh = Cylinder(new Vector3(x, 2.6f, zc * 0.4f), new Vector3(x, 2.95f, zc), 0.02f, 8);
                arm.AddComponent<MeshRenderer>().sharedMaterial = mats["ki_alu"];
                var reel = new GameObject("kino_kela");
                reel.transform.SetParent(kino.transform, false);
                reel.transform.localPosition = new Vector3(x, 2.95f, zc);
                var disc = new GameObject("levy");
                disc.transform.SetParent(reel.transform, false);
                disc.AddComponent<MeshFilter>().sharedMesh = Cylinder(new Vector3(-0.03f, 0f, 0f), new Vector3(0.03f, 0f, 0f), 0.28f, 24);
                disc.AddComponent<MeshRenderer>().sharedMaterial = greyMetalKino;
                var hub = new GameObject("napa");
                hub.transform.SetParent(reel.transform, false);
                hub.AddComponent<MeshFilter>().sharedMesh = Cylinder(new Vector3(-0.035f, 0f, 0f), new Vector3(0.035f, 0f, 0f), 0.06f, 12);
                hub.AddComponent<MeshRenderer>().sharedMaterial = reelMat;
                reel.SetActive(false);
                p.Reels[k] = reel.transform;
            }
            // zone de clic sur le projecteur
            var cg = new GameObject("kino_projektori_coll");
            cg.layer = 2;
            cg.transform.SetParent(kino.transform, false);
            cg.transform.localPosition = new Vector3(x, 2.3f, 0f);
            var bc = cg.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = new Vector3(0.8f, 1.3f, 1.0f);
            p.Col = bc;
            // ecran : sa propre surface (la projection y remplace la toile)
            float sx = s * (KL - Wall - 0.03f);
            var scr = GameObject.CreatePrimitive(PrimitiveType.Quad);
            UnityEngine.Object.Destroy(scr.GetComponent<Collider>());
            scr.name = "kino_valkokangas" + sali;
            scr.transform.SetParent(kino.transform, false);
            scr.transform.localPosition = new Vector3(sx, 3.3f, 0f);
            scr.transform.localRotation = Quaternion.Euler(0f, s > 0 ? 90f : -90f, 0f);   // (face vers les spectateurs)
            scr.transform.localScale = new Vector3(9.2f, 4.0f, 1f);
            p.Screen = scr.GetComponent<Renderer>();
            p.Idle = mats["ki_valkokangas"];
            p.Screen.sharedMaterial = p.Idle;
            p.Screen.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // son a l'ecran
            p.Audio = scr.AddComponent<AudioSource>();
            p.Audio.spatialBlend = 1f; p.Audio.minDistance = 8f; p.Audio.maxDistance = 34f; p.Audio.rolloffMode = AudioRolloffMode.Linear; p.Audio.playOnAwake = false;
            // faisceau : cone de lumiere de l'objectif vers l'ecran (additif, discret)
            p.Beam = KinoBeam(new Vector3(x + s * 0.55f, 2.4f, 0f), sx, s);
            p.Beam.SetActive(false);
            return p;
        }

        static Material greyMetalKino { get { if (greyMetal == null) { blackPlastic = Plain(new Color(0.07f, 0.07f, 0.08f)); greyMetal = Plain(new Color(0.2f, 0.2f, 0.21f)); } return greyMetal; } }

        static GameObject KinoBeam(Vector3 from, float sx, float s)
        {
            var g = new GameObject("kino_sade");
            g.transform.SetParent(kino.transform, false);
            var m = new Mesh { name = "kino_sade" };
            Vector3[] c = { new Vector3(sx, 1.3f, -4.6f), new Vector3(sx, 1.3f, 4.6f), new Vector3(sx, 5.3f, 4.6f), new Vector3(sx, 5.3f, -4.6f) };
            var v = new List<Vector3>(); var col = new List<Color>(); var t = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = c[i], b = c[(i + 1) % 4];
                int k = v.Count;
                v.Add(from); v.Add(a); v.Add(b);
                col.Add(new Color(1f, 0.95f, 0.8f, 0.05f)); col.Add(new Color(1f, 0.95f, 0.8f, 0f)); col.Add(new Color(1f, 0.95f, 0.8f, 0f));   // (moitie moins opaque qu'avant : JD, 22/10)
                t.Add(k); t.Add(k + 1); t.Add(k + 2); t.Add(k); t.Add(k + 2); t.Add(k + 1);   // (deux faces)
            }
            m.vertices = v.ToArray(); m.colors = col.ToArray(); m.triangles = t.ToArray(); m.RecalculateBounds();
            g.AddComponent<MeshFilter>().sharedMesh = m;
            Shader sh = Shader.Find("Particles/Additive") ?? Shader.Find("Legacy Shaders/Particles/Additive");
            var r = g.AddComponent<MeshRenderer>();
            if (sh != null) r.sharedMaterial = new Material(sh) { name = "kino_sade", mainTexture = Texture2D.whiteTexture };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return g;
        }

        static void SetProjector(Projector p, string content, bool playing, int elapsedMs, bool send)
        {
            if (p == null) return;
            bool changed = p.Content != content || p.Playing != playing;
            if (p.Content != content || (playing && !p.Playing)) { p.Film = null; p.Frame = -1; }
            p.Content = content ?? "";
            if (playing && (!p.Playing || Mathf.Abs((Time.realtimeSinceStartup - p.Start) * 1000f - elapsedMs) > 1500f)) p.Start = Time.realtimeSinceStartup - elapsedMs / 1000f;
            p.Playing = playing && p.Content.Length > 0;
            foreach (Transform r in p.Reels) if (r != null) r.gameObject.SetActive(p.Content.Length > 0);
            if (changed) L("cinema : salle " + p.Sali + " : bobine '" + p.Content + "', " + (p.Playing ? "projection" : "arret"));
            if (send) SendProjector(p);
        }

        static void SendProjector(Projector p)
        {
            if (!Session.Active || p == null) return;
            int el = p.Playing ? Mathf.RoundToInt((Time.realtimeSinceStartup - p.Start) * 1000f) : 0;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@kino").U8(2).U8(p.Sali).Str(p.Content).Bool(p.Playing).I32(el), true);
        }

        public static void OnKino(int who, NetReader r)
        {
            int op = r.U8();
            if (op == 1) { int sali = r.U8(); bool open = r.Bool(); if (kino != null && sali >= 1 && sali <= 2) SetKinoDoor(kinoDoors[sali], open, false); return; }
            if (op == 2)
            {
                int sali = r.U8(); string content = r.Str(); bool playing = r.Bool(); int el = r.I32();
                if (kino != null && sali >= 1 && sali <= 2) SetProjector(projectors[sali], content, playing, el, false);
            }
        }

        // ---------------------------------------------------------------- chaque image
        static void TickKinoSali(float now)
        {
            if (!kinoLoaded) { kinoLoaded = true; LoadKino(); }
            // visee : articles du comptoir, portes, projecteurs
            Camera cam = Game.MainCam;
            object hit = null;
            if (cam != null && !Game.GlobalBool("PlayerSleeps") && (cam.transform.position - kino.transform.position).sqrMagnitude < 45f * 45f)
            {
                var ray = new Ray(cam.transform.position, cam.transform.forward);
                float best = 2.0f; RaycastHit h;
                KinoOffer o;
                if (KinoBuyAim(ray, ref best, out o)) hit = o;
                for (int i = 1; i <= 2; i++)
                {
                    if (kinoDoors[i] != null && kinoDoors[i].Col.Raycast(ray, out h, best)) { best = h.distance; hit = kinoDoors[i]; }
                    if (projectors[i] != null && projectors[i].Col.Raycast(ray, out h, best)) { best = h.distance; hit = projectors[i]; }
                }
            }
            if (hit != kinoLooked) { if (kinoLooked != null) KinoClearGui(); kinoLooked = hit; if (hit is Projector) ((Projector)hit).FDown = -1f; }
            if (hit is KinoOffer) KinoOfferGui((KinoOffer)hit, now);
            else if (hit is KinoDoor) KinoDoorGui((KinoDoor)hit, now);
            else if (hit is Projector) KinoProjectorGui((Projector)hit, now);
            TickReelInsert();
            // en entrant dans une salle : comment s'asseoir (une fois par partie)
            int inSali = InSalle(1) ? 1 : InSalle(2) ? 2 : 0;
            if (inSali != 0 && !seatHintDone) { seatHintDone = true; Hud.Toast(Lang.T("Pour t'asseoir : place-toi devant un fauteuil et accroupis-toi (C), comme sur une chaise", "To sit: stand in front of a seat and crouch (C), like on a chair")); }
            // portes : rotation, fermeture toute seule apres 15 s
            for (int i = 1; i <= 2; i++)
            {
                KinoDoor d = kinoDoors[i];
                if (d == null) continue;
                if (d.Open && now - d.OpenedAt > 15f && (BagAuthority)) SetKinoDoor(d, false, true);
                d.Angle = Mathf.MoveTowards(d.Angle, d.Open ? 100f : 0f, Time.deltaTime * 170f);
                d.Leaf.localRotation = Quaternion.Euler(0f, (d.Leaf.localPosition.x > 0 ? -1f : 1f) * d.Angle, 0f);
            }
            // projection
            bool anyPlaying = false;
            for (int i = 1; i <= 2; i++) { Projector p = projectors[i]; if (p == null) continue; KinoShow(p, now); anyPlaying |= p.Playing; }
            KinoDim(now);
            if (BagAuthority && Session.Active && Session.RemoteCount > 0 && now >= kinoNextSync)
            {
                kinoNextSync = now + 10f;
                for (int i = 1; i <= 2; i++) { SendProjector(projectors[i]); if (kinoDoors[i] != null && kinoDoors[i].Open) Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@kino").U8(1).U8(i).Bool(true), true); }
            }
        }

        static bool InSalle(int sali)
        {
            Transform pl = Game.PlayerT;
            if (pl == null) return false;
            Vector3 lp = kino.transform.InverseTransformPoint(pl.position);
            return (sali == 1 ? lp.x < -KPart : lp.x > KPart) && Mathf.Abs(lp.z) < KD && lp.y > -1f && lp.y < KSalCeil;
        }

        static void KinoDoorGui(KinoDoor d, float now)
        {
            guiUse.Value = true;
            bool inside = InSalle(d.Sali);
            if (!d.Open && !inside && !HasTicket)
            {
                guiText.Value = Lang.TG("SALLE " + d.Sali + " : BILLET D'ABORD (CAISSE)", "SCREEN " + d.Sali + ": TICKET FIRST (COUNTER)");
                if (Input.GetMouseButtonDown(0)) Say("Myyjä : Lippu ensin ! Le billet d'abord, au comptoir.", "Vendor: Lippu ensin! Ticket first, at the counter.");
                return;
            }
            guiText.Value = d.Open ? Lang.TG("FERMER", "CLOSE") : Lang.TG("SALLE " + d.Sali, "SCREEN " + d.Sali);
            if (Input.GetMouseButtonDown(0)) SetKinoDoor(d, !d.Open, true);
        }

        static void KinoProjectorGui(Projector p, float now)
        {
            Esine held = null;
            foreach (Esine e in esineet) if (e.Kind == KKela && e.Go != null && Held(e) && Game.UnderPlayer(e.Go.transform)) held = e;
            FsmBool asm = FsmVariables.GlobalVariables.FindFsmBool("GUIassemble");
            if (p.Content.Length == 0)
            {
                if (held != null)
                {
                    if (asm != null) asm.Value = true;
                    guiText.Value = Lang.TG("METTRE LA BOBINE", "LOAD THE REEL");
                    if (Input.GetMouseButtonDown(0)) { pendingReel = held; pendingProj = p; pendingReelFrame = Time.frameCount; }
                }
                else { guiUse.Value = true; guiText.Value = Lang.TG("PROJECTEUR (PAS DE BOBINE)", "PROJECTOR (NO REEL)"); }
                return;
            }
            if (asm != null) asm.Value = false;
            guiUse.Value = true;
            string title = TapeTitle(p.Content);
            guiText.Value = (title.Length > 0 ? "« " + title.ToUpperInvariant() + " » " : "") + (p.Playing ? Lang.TG("F : ARRÊTER", "F: STOP") : Lang.TG("F : PROJETER", "F: PLAY")) + Lang.TG(" - F TENU : RETIRER", " - HOLD F: UNLOAD");
            if (Input.GetKeyDown(KeyCode.F)) p.FDown = now;
            if (p.FDown >= 0f)
            {
                if (now - p.FDown >= 0.8f) { p.FDown = -1f; UnloadReel(p); }
                else if (Input.GetKeyUp(KeyCode.F)) { p.FDown = -1f; SetProjector(p, p.Content, !p.Playing, 0, true); }
            }
        }

        // bobine lachee par la main du jeu (clic gauche) : elle va sur le projecteur
        static void TickReelInsert()
        {
            if (pendingReel == null) return;
            if (Time.frameCount - pendingReelFrame > 30 || pendingReel.Go == null) { pendingReel = null; return; }
            if (Held(pendingReel)) return;
            Esine reel = pendingReel; Projector p = pendingProj;
            pendingReel = null;
            string content = string.IsNullOrEmpty(reel.Data) ? "tyhja" : reel.Data;
            RemoveItem(reel, true);
            SetProjector(p, content, false, 0, true);
            try { MasterAudio.PlaySound3DAtVector3AndForget("HouseFoley", p.Col.transform.position, 0.6f, null, 0f, "mailbox_open"); } catch { }
        }

        static void UnloadReel(Projector p)
        {
            string content = p.Content;
            if (content.Length == 0) return;
            SetProjector(p, "", false, 0, true);
            Vector3 at = kino.transform.TransformPoint(new Vector3(p.S * 9.5f, KBack + 0.3f, 1.2f));   // (au sol de la cabine, pres de la porte)
            SpawnItem(KKela, content, at, kino.transform.rotation);
        }

        // image du film a l'instant (comme ShowFilm des cassettes) ; toile blanche sans film
        static void KinoShow(Projector p, float now)
        {
            // son : dans la salle, il la remplit (enceintes partout : presque sans direction, meme volume au fond -- JD, 22/10) ;
            // du hall ou de dehors, il vient de l'ecran et s'eteint avec la distance
            bool inside = InSalle(p.Sali);
            p.Audio.spatialBlend = inside ? 0.25f : 1f;
            p.Audio.minDistance = inside ? 30f : 8f;
            if (p.Playing)
                foreach (Transform r in p.Reels) if (r != null) r.Rotate(Vector3.right, Time.deltaTime * 140f, Space.Self);
            if (p.Beam != null && p.Beam.activeSelf != p.Playing) p.Beam.SetActive(p.Playing);
            string link = LinkOf(p.Content);
            if (p.Link != null && (!p.Playing || link == null || p.Link.Url != link)) LinkStop(p);
            if (p.Playing && link != null) { LinkShow(p, link, now); return; }
            if (!p.Playing)
            {
                if (p.Screen.sharedMaterial != p.Idle) p.Screen.sharedMaterial = p.Idle;
                if (p.Audio.isPlaying) p.Audio.Stop();
                p.Film = null; p.Frame = -1;
                return;
            }
            float t = now - p.Start;
            List<string> clips = Clips(p.Content);
            Film f = null; float clipStart = 0f, at = 0f;
            bool all = clips.Count > 0;
            var loaded = new List<Film>();
            foreach (string c in clips) { Film x = LoadFilm(c); if (x == null) { AskFilm(c, now); all = false; } else loaded.Add(x); }
            if (all)
                foreach (Film x in loaded)
                {
                    float len = x.LengthMs / 1000f;
                    if (t < at + len) { f = x; clipStart = at; break; }
                    at += len;
                }
            if (f == null)
            {
                // bobine vierge, film pas encore recu, ou fin du film : toile eclairee par la lampe
                if (p.Screen.sharedMaterial != p.Idle) p.Screen.sharedMaterial = p.Idle;
                if (p.Audio.isPlaying) p.Audio.Stop();
                if (all && clips.Count > 0 && t > at + 2f && BagAuthority) SetProjector(p, p.Content, false, 0, true);   // (fin : la lumiere revient)
                return;
            }
            float tc = t - clipStart;
            if (p.Film != f)
            {
                p.Film = f; p.Frame = -1;
                if (p.Tex == null) p.Tex = new Texture2D(2, 2, TextureFormat.RGB24, false) { name = "kino_elokuva", wrapMode = TextureWrapMode.Clamp };
                if (p.Mat == null) { Shader us = Shader.Find("Unlit/Texture"); p.Mat = new Material(us != null ? us : p.Idle.shader) { mainTexture = p.Tex, name = "kino_elokuva" }; }
                p.Audio.Stop();
                p.Audio.clip = f.Clip; p.Audio.loop = false; p.Audio.volume = 1f;
                if (f.Clip != null && tc < f.Clip.length - 0.05f) { p.Audio.time = Mathf.Max(0f, tc); p.Audio.Play(); }
                L("cinema : salle " + p.Sali + " projette " + f.Id + " depuis " + tc.ToString("F1") + " s");
            }
            if (p.Screen.sharedMaterial != p.Mat) p.Screen.sharedMaterial = p.Mat;
            uint ms = (uint)Mathf.Max(0f, tc * 1000f);
            int lo = 0, hi = f.Ms.Count - 1, idx = -1;
            while (lo <= hi) { int mid = (lo + hi) / 2; if (f.Ms[mid] <= ms) { idx = mid; lo = mid + 1; } else hi = mid - 1; }
            if (idx >= 0 && idx != p.Frame)
            {
                p.Frame = idx;
                var jpg = new byte[f.Len[idx]];
                Buffer.BlockCopy(f.Data, f.Off[idx], jpg, 0, jpg.Length);
                p.Tex.LoadImage(jpg);
            }
        }

        // lumieres de chaque salle : baissees pendant la projection
        static void KinoDim(float now)
        {
            foreach (Light l in kinoSalLights)
            {
                if (l == null) continue;
                int sali = l.transform.localPosition.x < 0 ? 1 : 2;
                Projector p = projectors[sali];
                float want = p != null && p.Playing ? 0.08f : 1.0f;
                l.intensity = Mathf.MoveTowards(l.intensity, want, Time.deltaTime * 0.5f);
            }
        }

        // ---------------------------------------------------------------- sauvegarde des bobines en place
        static string KinoFile { get { return WorldFiles.PathOf("MWCoop-kino.txt"); } }

        public static void OnGameSaveKino()
        {
            if (kino == null || !kinoLoaded || !BagAuthority) return;
            try
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 1; i <= 2; i++) if (projectors[i] != null) sb.Append(i).Append(';').Append(projectors[i].Content.Replace(";", ",")).Append('\n');
                File.WriteAllText(KinoFile, sb.ToString());
            }
            catch (Exception e) { L("cinema : ecriture " + e.Message); }
        }

        static void LoadKino()
        {
            if (!BagAuthority || !File.Exists(KinoFile)) return;
            try
            {
                foreach (string l in File.ReadAllLines(KinoFile))
                {
                    string[] c = l.Split(';');
                    int sali;
                    if (c.Length >= 2 && int.TryParse(c[0], out sali) && sali >= 1 && sali <= 2 && c[1].Length > 0) SetProjector(projectors[sali], c[1], false, 0, true);
                }
            }
            catch (Exception e) { L("cinema : lecture " + e.Message); }
        }

        // ---------------------------------------------------------------- essais
        static void KinoEssai2(string[] a, float now)
        {
            switch (a[1])
            {
                case "rendus":
                {
                    // rendus x y z r : objets dessines dont la boite touche la sphere (repere du cinema) -- arbres restes...
                    if (kino == null) break;
                    Vector3 c = kino.transform.TransformPoint(new Vector3(F(a[2]), F(a[3]), F(a[4])));
                    float r = F(a[5]);
                    int n = 0;
                    foreach (Renderer rd in UnityEngine.Object.FindObjectsOfType<Renderer>())
                    {
                        if (!rd.enabled || rd.bounds.SqrDistance(c) > r * r || rd.transform.IsChildOf(kino.transform)) continue;
                        MeshFilter mf = rd.GetComponent<MeshFilter>();
                        L("essai : rendu " + ObjPath(rd.transform) + " (" + rd.GetType().Name + ", " + (rd.sharedMaterial != null ? rd.sharedMaterial.name : "-") + ", maillage " + (mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-") + ") boite " + rd.bounds.center.ToString("F0") + " " + rd.bounds.size.ToString("F0"));
                        if (++n >= 60) break;
                    }
                    break;
                }
                case "lippu": ticketDay = GameDay; L("essai : billet du jour " + ticketDay); break;
                case "kartta": MapPanel.Open = a.Length < 3 || a[2] != "0"; break;   // kartta [0] : carte du F10 (M) ouverte / fermee
                case "sonde":
                {
                    // sonde x z0 z1 : le sol le long d'une ligne nord-sud (collider touche et hauteur, aux changements) -- emplacement
                    float x = F(a[2]), z0 = F(a[3]), z1 = F(a[4]);
                    var sb = new System.Text.StringBuilder("essai : sonde x " + x + " :");
                    string last = null;
                    for (float z = z0; z <= z1; z += 1f)
                    {
                        RaycastHit best = new RaycastHit(); bool got = false;
                        foreach (RaycastHit h in Physics.RaycastAll(new Vector3(x, 300f, z), Vector3.down, 400f))
                            if (!h.collider.isTrigger && (!got || h.distance < best.distance)) { best = h; got = true; }
                        string n = got ? best.collider.name : "-";
                        if (n != last) { sb.Append(" | z" + z + " " + n + " " + (got ? best.point.y.ToString("F1") : "")); last = n; }
                        else if (((int)z) % 10 == 0) sb.Append(" .z" + z + " " + (got ? best.point.y.ToString("F1") : ""));
                    }
                    L(sb.ToString());
                    break;
                }
                case "kelamontage": { Esine k = esineet.FindLast(e => e.Kind == KKela); if (k != null) OpenMontage(k); L("essai : montage de la bobine " + (k != null ? k.Data : "-")); break; }
                case "linkki": SetProjector(projectors[int.Parse(a[2])], "url:" + a[3] + "~" + LinkTitle(a[3]), true, a.Length > 4 ? int.Parse(a[4]) : 0, true); break;   // linkki <sali> <adresse> [ms]
                case "tuokela":
                {
                    // tuokela <video> : bobine vierge creee devant les yeux, tenue le temps de lancer /importer <video>
                    Transform eye = EyeT;
                    if (eye == null) break;
                    SpawnItem(KKela, "tyhja", eye.position + eye.forward * 0.6f, Quaternion.identity);
                    Esine k = esineet.FindLast(e => e.Kind == KKela);
                    if (k == null) break;
                    k.Rb.isKinematic = true; k.Go.transform.SetParent(eye, true);
                    ChatCommand("/importer " + string.Join(" ", a, 2, a.Length - 2));
                    k.Go.transform.SetParent(null, true); k.Rb.isKinematic = false;
                    break;
                }
                case "osta": { KinoOffer o = kinoOffers.Find(x => x.Item == a[2]); if (o != null) { kinoLastBuy = -10f; KinoBuy(o, now); } break; }
                case "syo": { Esine f = esineet.FindLast(e => IsFood(e.Kind)); if (f != null) { int left; if (!int.TryParse(f.Data, out left)) left = Portions(f.Kind); Bite(f.Kind); L("essai : bouchee de " + f.Kind + ", faim " + Game.GlobalFloat("PlayerHunger").ToString("F1") + ", soif " + Game.GlobalFloat("PlayerThirst").ToString("F1")); SetData(f, (left - 1).ToString()); } break; }
                case "ovi": { int s = int.Parse(a[2]); SetKinoDoor(kinoDoors[s], a.Length < 4 || a[3] != "0", true); break; }
                case "kela":
                {
                    // kela <sali> [<contenu>] : bobine posee sur le projecteur (contenu : celui de la derniere cassette ou bobine
                    // qui porte un film, sinon vierge) puis projection
                    int s = int.Parse(a[2]);
                    string content = a.Length > 3 ? a[3] : null;
                    if (content == null) { Esine src = esineet.FindLast(e => (e.Kind == KVhs || e.Kind == KKela) && TapeFilm(e.Data).Length > 0); content = src != null ? src.Data : "tyhja"; }
                    SetProjector(projectors[s], content, true, 0, true);
                    break;
                }
                case "istu":
                {
                    // istu <sali> <rang> <place> : joueur pose sur le fauteuil, declencheur force en « Press return » (comme
                    // l'essai assis-chaise de Gestures)
                    int s = int.Parse(a[2]), row = int.Parse(a[3]), idx = int.Parse(a[4]);
                    SeatSpot sp = kinoSeatSpots.Find(x => x.Salle == s && x.Row == row && x.Index == idx);
                    Transform pl = Game.PlayerT;
                    if (sp == null || pl == null) break;
                    var cc = pl.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;
                    pl.position = kino.transform.TransformPoint(sp.Pos + Vector3.up * 0.05f);
                    pl.rotation = kino.transform.rotation * Quaternion.Euler(0f, sp.Yaw, 0f);
                    if (cc != null) cc.enabled = true;
                    PlayMakerFSM best = null; float bd = 2f;
                    foreach (PlayMakerFSM f in kino.GetComponentsInChildren<PlayMakerFSM>(true))
                    {
                        if (f.FsmName != "PlayerTrigger") continue;
                        float d = (f.transform.position - pl.position).magnitude;
                        if (d < bd) { bd = d; best = f; }
                    }
                    if (best != null) { best.gameObject.SetActive(true); Game.SetState(best, "Press return"); }
                    L("essai : assis salle " + s + " rang " + row + " place " + idx + (best != null ? " (declencheur a " + bd.ToString("F2") + " m, etat " + best.ActiveStateName + ")" : " (pas de declencheur)"));
                    break;
                }
            }
        }
    }
}
