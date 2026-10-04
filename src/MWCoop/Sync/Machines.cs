using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Machines a ecran (automates des pompes a essence : carte, code, montant, pompe, reçu...). Leur
    // logique touche a l'argent et a l'ecran du joueur : on ne la rejoue pas. Celui qui s'en sert (a
    // moins de 3,5 m, vient de cliquer ou d'appuyer sur une touche) envoie ce qu'on en VOIT : textes de
    // l'ecran, pieces visibles ou cachees (carte, billet, boutons, chiffres), position des pieces qui
    // bougent. Chez les autres ces valeurs l'emportent, apres la logique du jeu, tant qu'il s'en sert ;
    // 3 s apres son dernier envoi la machine revient a son propre etat. Ni paiement en double, ni
    // carte avalee chez l'autre.
    public static class Machines
    {
        class Part
        {
            public string Key; public Transform T; public TextMesh Text;
            public bool Active; public Vector3 Pos; public Quaternion Rot; public string Txt;          // dernier envoye
            public bool Held; public bool HActive; public bool HPose; public Vector3 HPos; public Quaternion HRot; public string HTxt; public float HeldAt;
        }
        class Machine { public string Key; public Transform Root; public List<Part> Parts = new List<Part>(); public Dictionary<string, Part> ByKey = new Dictionary<string, Part>(); public float OwnerUntil, NextFull; }

        static readonly Dictionary<string, Machine> machines = new Dictionary<string, Machine>();
        static float nextScan = -1, nextSend, lastInput = -100;
        static Transform player;

        static bool IsMachine(Transform t) { return t.name.StartsWith("FuelPumps_"); }

        public static void OnLevelLoaded()
        {
            machines.Clear(); player = null;
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 15f : -1;
        }

        static void Scan()
        {
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(Transform)))
            {
                var t = (Transform)o;
                if (t.gameObject.hideFlags != HideFlags.None || !IsMachine(t) || !t.root.gameObject.activeInHierarchy) continue;
                string key = Recon.Path(t);
                if (machines.ContainsKey(key) || t.GetComponentsInChildren<PlayMakerFSM>(true).Length == 0) continue;
                var m = new Machine { Key = key, Root = t };
                var seen = new Dictionary<string, int>();
                foreach (Transform c in t.GetComponentsInChildren<Transform>(true))
                {
                    if (c == t) continue;
                    string rel = Recon.Path(c).Substring(key.Length);
                    int k; seen.TryGetValue(rel, out k); seen[rel] = k + 1;
                    var p = new Part { Key = rel + "#" + k, T = c, Text = c.GetComponent<TextMesh>(), Active = c.gameObject.activeSelf, Pos = c.localPosition, Rot = c.localRotation };
                    if (p.Text != null) p.Txt = p.Text.text;
                    m.Parts.Add(p);
                    m.ByKey[p.Key] = p;
                }
                machines[key] = m;
            }
            if (machines.Count > 0) Log.Info("machines a ecran : " + machines.Count + " suivies");
        }

        public static void Update()
        {
            if (!Session.Active || nextScan < 0 || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = machines.Count == 0 ? now + 30f : float.MaxValue; Scan(); }
            if (machines.Count == 0) return;
            if (player == null) { GameObject g = GameObject.Find("PLAYER"); if (g == null) return; player = g.transform; }
            if (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) lastInput = now;
            if (now < nextSend || Session.RemoteCount == 0) return;
            nextSend = now + 0.2f;
            foreach (Machine m in machines.Values)
            {
                if (m.Root == null) continue;
                if (now - lastInput < 1f && Near(m)) m.OwnerUntil = now + 15f;   // il s'en sert
                if (now > m.OwnerUntil) continue;
                bool full = now >= m.NextFull;
                if (full) m.NextFull = now + 2f;
                NetWriter w = null;
                foreach (Part p in m.Parts)
                {
                    if (p.T == null || p.Held || !p.T.IsChildOf(m.Root)) continue;   // pistolet decroche : il n'est plus a la machine
                    bool act = p.T.gameObject.activeSelf;
                    bool pose = Quaternion.Angle(p.T.localRotation, p.Rot) > 0.5f || (p.T.localPosition - p.Pos).sqrMagnitude > 1e-6f;
                    string txt = p.Text != null ? p.Text.text : null;
                    bool txtCh = p.Text != null && txt != p.Txt;
                    if (!full && act == p.Active && !pose && !txtCh) continue;   // toutes les 2 s : tout
                    p.Active = act; p.Pos = p.T.localPosition; p.Rot = p.T.localRotation; p.Txt = txt;
                    bool withPose = pose || full;
                    int len = 12 + p.Key.Length + (withPose ? 28 : 0) + (txt != null ? txt.Length * 2 + 2 : 0);
                    if (w != null && w.Length + len > 900) { Session.SendAll(w, false); w = null; }
                    if (w == null) w = new NetWriter(Msg.Machine).U8(Session.LocalId).Str(m.Key);
                    w.Str(p.Key).U8((act ? 1 : 0) | (withPose ? 2 : 0) | (txt != null ? 4 : 0));
                    if (withPose) w.Vec(p.Pos).Quat(p.Rot);
                    if (txt != null) w.Str(txt);
                }
                if (w != null) Session.SendAll(w, false);
            }
        }

        static bool Near(Machine m)
        {
            Vector3 c = m.Root.position;
            foreach (PlayMakerFSM f in m.Root.GetComponentsInChildren<PlayMakerFSM>()) { c = f.transform.position; break; }
            return (player.position - c).sqrMagnitude < 3.5f * 3.5f;
        }

        // Apres la logique du jeu : ce que voit celui qui s'en sert l'emporte.
        public static void LateUpdate()
        {
            if (!Session.Active || machines.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            foreach (Machine m in machines.Values)
                foreach (Part p in m.Parts)
                {
                    if (!p.Held || p.T == null) continue;
                    if (now - p.HeldAt > 3f) { p.Held = false; p.Active = p.T.gameObject.activeSelf; p.Pos = p.T.localPosition; p.Rot = p.T.localRotation; p.Txt = p.Text != null ? p.Text.text : null; continue; }
                    if (p.T.gameObject.activeSelf != p.HActive) p.T.gameObject.SetActive(p.HActive);
                    if (p.HPose) { p.T.localPosition = p.HPos; p.T.localRotation = p.HRot; }
                    if (p.Text != null && p.HTxt != null && p.Text.text != p.HTxt) p.Text.text = p.HTxt;
                }
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string key = r.Str();
            NetWriter relay = Session.IsHost ? new NetWriter(Msg.Machine).U8(who).Str(key) : null;
            Machine m;
            machines.TryGetValue(key, out m);
            float now = Time.realtimeSinceStartup;
            while (r.More)
            {
                string pk = r.Str();
                int fl = r.U8();
                Vector3 pos = Vector3.zero; Quaternion rot = Quaternion.identity; string txt = null;
                if ((fl & 2) != 0) { pos = r.Vec(); rot = r.Quat(); }
                if ((fl & 4) != 0) txt = r.Str();
                if (relay != null) { relay.Str(pk).U8(fl); if ((fl & 2) != 0) relay.Vec(pos).Quat(rot); if (txt != null) relay.Str(txt); }
                Part p;
                if (m == null || !m.ByKey.TryGetValue(pk, out p) || p.T == null) continue;
                p.Held = true; p.HeldAt = now; p.HActive = (fl & 1) != 0;
                if ((fl & 2) != 0) { p.HPose = true; p.HPos = pos; p.HRot = rot; }
                if (txt != null) p.HTxt = txt;
            }
            if (relay != null) Session.Broadcast(relay, false, who);
            if (m != null) m.OwnerUntil = 0f;   // un autre s'en sert : on n'envoie pas en meme temps
        }
    }
}
