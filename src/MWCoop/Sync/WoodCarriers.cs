using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Porte-buches (EQUIPMENTS/wood carrier(itemN)). Son automate WoodTrigger::Trigger compte les buches posees dedans :
    // une buche (firewood(Clone), tag PART) lachee dans la zone -> "Parent" (immobile ? sans parent ?) -> "Check firewood"
    // (son nom) -> "State 1" (place libre ?) -> "Fetch array" (buche detruite, modele log1..log10 suivant) -> "Activae wood"
    // (modele montre, WoodsIndex + 1). Reprendre une buche (PickupTrigger::Use, clic -> PICKWOOD) : "Array" / "Check log" ->
    // "State 5" : une buche NEUVE est creee (CreateObject du modele firewood), le modele cache, WoodsIndex - 1.
    // Rien ne les suivait en propre : le monde (WorldFsms) rejouait WOOD chez l'autre, ou la buche de l'automate (variable
    // FirewoodObject, remplie par SA zone) manquait -- « Wait (voulu Parent) », la buche n'apparaissait jamais chez lui ;
    // et la copie de la buche, deplacee par Props, entrait aussi dans la zone de l'autre. La buche reprise n'existait que
    // chez celui qui cliquait, sans cle commune : posee ensuite sur le plateau d'une remorque, l'hote (qui compte le
    // plateau) ne la voyait pas (retour de vampire234, 18/10 : « les buches posees dans le porte-buches n'apparaissent pas
    // pour un joueur ; reprises et mises dans la remorque, elles ne se chargent pas »).
    //  - Le compte se fait chez celui dont la buche entre : chez les autres, la copie d'une buche qu'un autre deplace
    //    (Props.MovedByOther, moins de 3 s) est ignoree ("Fetch array" annule). Le nombre change ici part aux autres
    //    (@porte 1 : porte-buches, nombre, ID de la buche, position), qui montrent les modeles et retirent leur copie (par
    //    son ID ; sans ID, une buche sans ID a moins de 1,5 m).
    //  - Reprise : la buche creee ici recoit un ID (Jobs.TagNew : Props la suit, le plateau la compte chez l'hote) et part
    //    (@porte 2 : nombre, ID, pose) ; les autres creent la meme (modele firewood) avec cet ID.
    //  - L'hote redit toutes les 15 s le nombre de chaque porte-buches (@porte 0) aux invites.
    //  Personne d'autre ne rejoue ces automates (Replay.Claim : WorldFsms les laisse).
    public static class WoodCarriers
    {
        const string Mod = "porte-buches";
        // (My Summer Car : les porte-buches sont sous ITEMS -- Markos et Squier, 24/10 : « un porte-buches ne marche ni chez l'hote ni chez l'invite »)
        static string Root { get { return Game.Msc ? "ITEMS" : "EQUIPMENTS"; } }
        class Carrier
        {
            public string Key; public PlayMakerFSM Trigger, Pickup; public FsmInt Index; public FsmGameObject Firewood, NewWood, Prefab;
            public int Known; public float LocalAt = -9f, RemoteAt = -9f; public bool Hooked;
            public string PendingId = ""; public Vector3 PendingPos; public bool Picking;
        }
        static readonly Dictionary<string, Carrier> carriers = new Dictionary<string, Carrier>();
        static float scanAt = -1, nextSnap;
        static int logs;

        public static void OnLevelLoaded() { carriers.Clear(); scanAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 12f : -1; }

        class FetchHook : ModHook
        {
            public override string Module { get { return Mod; } }
            public Carrier C; public FsmState St;
            public override void OnEnter()
            {
                bool cancel = false;
                try { cancel = OnFetch(C, St, this); } catch (System.Exception e) { Replay.HookError(e); }
                if (!cancel) Finish();
            }
        }
        class PickHook : ModHook
        {
            public override string Module { get { return Mod; } }
            public Carrier C;
            public override void OnEnter()
            {
                try { if (Replay.Depth == 0) C.Picking = true; } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static void Scan()
        {
            GameObject eq = Game.FindAny(Root);
            if (eq == null) return;
            int found = 0;
            foreach (PlayMakerFSM f in eq.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (f.hideFlags != HideFlags.None || f.FsmName != "Trigger" || f.gameObject.name != "WoodTrigger") continue;
                Transform root = f.transform.parent;
                if (root == null || !root.name.StartsWith("wood carrier")) continue;
                string key = Parts.RankPath(f.transform);
                if (carriers.ContainsKey(key)) continue;
                var c = new Carrier { Key = key, Trigger = f, Index = f.FsmVariables.FindFsmInt("WoodsIndex"), Firewood = f.FsmVariables.FindFsmGameObject("FirewoodObject"),
                                      NewWood = f.FsmVariables.FindFsmGameObject("NewWood"), Prefab = f.FsmVariables.FindFsmGameObject("Prefab") };
                if (c.Index == null) continue;
                Transform pt = root.Find("PickupTrigger");
                if (pt != null) c.Pickup = Game.FsmOn(pt.gameObject, "Use");
                if (!Replay.Claim(f, Mod)) { Log.Warn("porte-buches : " + key + " deja suivi par " + Replay.Owner(f)); continue; }
                if (c.Pickup != null && !Replay.Claim(c.Pickup, Mod)) Log.Warn("porte-buches : prise de " + key + " deja suivie par " + Replay.Owner(c.Pickup));
                c.Known = c.Index.Value;
                carriers[key] = c;
                found++;
            }
            if (found > 0) Log.Info("porte-buches : " + found + " suivis (" + carriers.Count + " en tout)");
        }

        static bool Hook(Carrier c)
        {
            FsmState fetch = c.Trigger.Fsm.GetState("Fetch array"), pick = c.Trigger.Fsm.GetState("State 5");
            if (fetch == null || pick == null || !fetch.IsInitialized || !pick.IsInitialized) return false;
            FireTools.Insert(c.Trigger, fetch, new FetchHook { C = c, St = fetch }, false);
            FireTools.Insert(c.Trigger, pick, new PickHook { C = c }, false);
            c.Hooked = true;
            return true;
        }

        // Buche entree dans le porte-buches (debut de "Fetch array", avant sa destruction).
        static bool OnFetch(Carrier c, FsmState st, FetchHook h)
        {
            if (!Session.Active || Replay.Depth > 0) return false;
            GameObject log = c.Firewood != null ? c.Firewood.Value : null;
            float age;
            if (log != null && Props.MovedByOther(log, out age) && age < 3f)
            {
                FireTools.Cancel(c.Trigger, st, h, "Wait");
                if (logs++ < 20) Log.Info("porte-buches : buche d'un autre joueur dans " + c.Key + ", comptee chez lui");
                return true;
            }
            c.PendingId = log != null ? Props.ItemId(log) : "";
            c.PendingPos = log != null ? log.transform.position : c.Trigger.transform.position;
            return false;
        }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (scanAt > 0 && now >= scanAt) { scanAt = now + 30f; Scan(); }
            foreach (Carrier c in carriers.Values)
            {
                if (c.Trigger == null || c.Index == null) continue;
                if (!c.Hooked) Hook(c);
                int n = c.Index.Value;
                if (n == c.Known) continue;
                // Change ici (pas par un message) : on l'annonce.
                bool picked = c.Picking && n < c.Known;
                c.Known = n; c.LocalAt = now;
                if (picked)
                {
                    c.Picking = false;
                    GameObject made = c.NewWood != null ? c.NewWood.Value : null;
                    string id = made != null ? (Props.ItemId(made).Length > 0 ? Props.ItemId(made) : Jobs.TagNew(made)) : "";
                    Vector3 p = made != null ? made.transform.position : c.Trigger.transform.position;
                    Quaternion q = made != null ? made.transform.rotation : Quaternion.identity;
                    Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@porte").U8(2).Str(c.Key).I32(n).Str(id).Vec(p).Quat(q), true);
                    if (logs++ < 40) Log.Info("porte-buches : buche reprise ici dans " + c.Key + " (" + n + " restantes), " + (made != null ? made.name + " " + id : "aucune buche creee") + ", envoye");
                }
                else
                {
                    Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@porte").U8(1).Str(c.Key).I32(n).Str(c.PendingId).Vec(c.PendingPos), true);
                    if (logs++ < 40) Log.Info("porte-buches : " + c.Key + " a " + n + " buches ici" + (c.PendingId.Length > 0 ? " (buche " + c.PendingId + ")" : "") + ", envoye");
                    c.PendingId = "";
                }
            }
            if (Session.IsHost && Session.RemoteCount > 0 && now >= nextSnap)
            {
                nextSnap = now + 15f;
                foreach (Carrier c in carriers.Values)
                    if (c.Trigger != null && c.Index != null && now - c.RemoteAt > 3f)
                        Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@porte").U8(0).Str(c.Key).I32(c.Index.Value), true);
            }
        }

        // [Test] Autotest=portebuches (2 joueurs) : a 30 s l'invite lache une buche (modele firewood, marquee) dans le premier
        // porte-buches ; a 42 s il en reprend une (PICKWOOD, comme le clic). Des deux cotes, toutes les 3 s de 28 a 60 s : nombre
        // de buches et modeles montres. Attendu : +1 partout a ~31 s, puis -1 partout a ~42 s et chez l'hote une buche creee
        // avec l'ID de l'invite.
        static int tStep; static float tLog; static GameObject tLogGo;
        public static void Test(string mode, float t)
        {
            if (mode != "portebuches") return;
            Carrier c = null;
            foreach (Carrier x in carriers.Values) if (x.Trigger != null) { c = x; break; }
            if (c == null) { if (t > 30f && tStep == 0) { tStep = 9; Log.Info("autotest : portebuches, aucun porte-buches suivi"); } return; }
            if (!Session.IsHost && t > 30f && tStep == 0)
            {
                tStep = 1;
                GameObject prefab = c.Prefab != null ? c.Prefab.Value : null;
                if (prefab == null) { Log.Info("autotest : portebuches, pas de modele firewood"); return; }
                tLogGo = (GameObject)Object.Instantiate(prefab, c.Trigger.transform.position + Vector3.up * 0.25f, Quaternion.identity);
                GameObject eq = Game.FindAny(Root); if (eq != null) tLogGo.transform.SetParent(eq.transform, true);
                tLogGo.transform.SetParent(null, true);
                tLogGo.name = "firewood(Clone)";
                tLogGo.SetActive(true);
                string id = Jobs.TagNew(tLogGo);
                Rigidbody rb = tLogGo.GetComponent<Rigidbody>(); if (rb != null) { rb.isKinematic = false; rb.velocity = Vector3.down * 1.5f; }
                Log.Info("autotest : portebuches, buche " + id + " lachee dans " + c.Key + " (" + c.Index.Value + " avant)");
            }
            if (!Session.IsHost && t > 42f && tStep == 1) { tStep = 2; c.Trigger.SendEvent("PICKWOOD"); Log.Info("autotest : portebuches, PICKWOOD envoye (" + c.Trigger.ActiveStateName + ")"); }
            if (t > 28f && t < 60f && t >= tLog)
            {
                tLog = t + 3f;
                int shown = 0;
                foreach (PlayMakerArrayListProxy p in c.Trigger.GetComponents<PlayMakerArrayListProxy>())
                    if (p._arrayList != null) foreach (object o in p._arrayList) { GameObject g = o as GameObject; if (g != null && g.activeSelf) shown++; }
                Log.Info("autotest : portebuches, " + c.Key + " WoodsIndex " + c.Index.Value + ", modeles montres " + shown + ", etat " + c.Trigger.ActiveStateName);
            }
        }

        // Nombre de buches pose ici : WoodsIndex, modeles montres (les n premiers de la liste de l'automate), prise active.
        static void Apply(Carrier c, int n)
        {
            // (-1 : vide, comme le jeu apres la derniere buche reprise -- sa prise s'eteint alors ; a 0 elle ferait sortir une buche
            // de nulle part : « Check log » voit log1 cache et cree quand meme)
            n = Mathf.Clamp(n, -1, 10);
            c.Index.Value = n; c.Known = n;
            PlayMakerArrayListProxy list = null;
            foreach (PlayMakerArrayListProxy p in c.Trigger.GetComponents<PlayMakerArrayListProxy>()) { list = p; break; }
            if (list != null && list._arrayList != null)
                for (int i = 0; i < list._arrayList.Count; i++)
                {
                    GameObject g = list._arrayList[i] as GameObject;
                    if (g != null && g.activeSelf != (i < n)) g.SetActive(i < n);
                }
            if (c.Pickup != null && c.Pickup.gameObject.activeSelf != (n > 0)) c.Pickup.gameObject.SetActive(n > 0);
        }

        public static void OnMessage(int who, NetReader r)
        {
            int kind = r.U8();
            string key = r.Str();
            int n = r.I32();
            string id = kind != 0 ? r.Str() : "";
            Vector3 pos = kind != 0 ? r.Vec() : Vector3.zero;
            Quaternion rot = kind == 2 ? r.Quat() : Quaternion.identity;
            Carrier c;
            if (!carriers.TryGetValue(key, out c) || c.Trigger == null) { Scan(); carriers.TryGetValue(key, out c); }
            if (c == null || c.Trigger == null) { if (logs++ < 40) Log.Warn("porte-buches : " + key + " introuvable ici (message de #" + who + ")"); return; }
            float now = Time.realtimeSinceStartup;
            if (kind == 0)
            {
                if (now - c.LocalAt < 3f || c.Index.Value == n) return;   // (instantane de l'hote plus vieux que notre geste)
                if (logs++ < 40) Log.Info("porte-buches : " + key + " recale sur l'hote (" + c.Index.Value + " -> " + n + ")");
                Apply(c, n);
                return;
            }
            c.RemoteAt = now;
            int was = c.Index.Value;
            Apply(c, n);
            if (kind == 1)
            {
                // Copie de la buche posee par l'autre : retiree (par son ID, sinon la plus proche sans ID, a 1,5 m au plus).
                bool gone = id.Length > 0 && Jobs.Gone(id);
                if (!gone && id.Length == 0)
                {
                    Rigidbody best = null; float bd = 1.5f * 1.5f;
                    foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>())
                    {
                        if (rb == null || !rb.name.StartsWith("firewood") || Props.ItemId(rb.gameObject).Length > 0) continue;
                        float d = (rb.position - pos).sqrMagnitude;
                        if (d < bd) { bd = d; best = rb; }
                    }
                    if (best != null) { Object.Destroy(best.gameObject); gone = true; }
                }
                if (logs++ < 40) Log.Info("porte-buches : " + key + " a " + n + " buches chez #" + who + " (" + was + " ici avant)" + (gone ? ", copie de la buche retiree" : ""));
            }
            else
            {
                // Buche reprise par l'autre : la meme, avec son ID.
                GameObject prefab = c.Prefab != null ? c.Prefab.Value : null;
                if (prefab == null) prefab = Game.FindAny("firewood");
                if (id.Length > 0 && prefab != null && !Jobs.HasTag(id))
                {
                    var made = (GameObject)Object.Instantiate(prefab, pos, rot);
                    GameObject eq = Game.FindAny(Root);
                    if (eq != null) made.transform.SetParent(eq.transform, true);
                    made.SetActive(true);
                    Jobs.TagAs(made, id);
                }
                if (logs++ < 40) Log.Info("porte-buches : buche reprise par #" + who + " dans " + key + " (" + n + " restantes), " + (id.Length > 0 ? id + " creee ici" : "sans ID"));
            }
        }
    }
}
