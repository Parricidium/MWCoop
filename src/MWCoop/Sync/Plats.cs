using System.Collections.Generic;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Plat de Jouni (restaurant de la PSK). Sa logique de service ne tourne que chez le joueur qui commande (Npcs, Rules) :
    // l'automate 'Work' cree le plat commande (CreateObject : « sausages and fries », « meatballs », « pizza speciale »...,
    // variable {Prefab}), le porte dans sa main (SpawnPoint) puis le laisse sur la table (Spawn plate : parent null). Chez
    // les autres, rien de tout ca n'existait : Jouni arrivait les mains vides et la table restait nue (Menor, 18/10 : « le
    // serveur de la PSK n'est pas synchronise, le truc sur la table n'apparait pas »).
    //  - Celui qui commande suit {Prefab} : nouveau plat -> @plat 1 (numero, nom du modele, pose) ; puis sa pose 2 fois par
    //    seconde (@plat 2 : pose monde, en main ou non) ; plat disparu (mange, detruit) -> @plat 0.
    //  - Les autres creent le meme modele (les actions CreateObject de l'automate 'Work' le donnent), sans automate ni
    //    physique ni collisionneur (decor : seul celui qui a commande mange son plat), en main : accroche au SpawnPoint de
    //    LEUR Jouni (son corps suit deja), pose : a la pose recue. Sans nouvelles depuis 15 s, ou le joueur parti : retire.
    public static class Plats
    {
        const string Root = "PERAPORTTI/Building/LOD100/Staff/AlaCarteRunnerPIVOT";
        const string SpawnPath = Root + "/Jouni/PhysicsPivot/Pivot/Char/skeleton/pelvis/AnimationRoot/spine_upper/collar_right/shoulder_right/arm_right/hand_right/SpawnPoint";
        static PlayMakerFSM work;
        static FsmGameObject prefabVar;
        static GameObject tracked;
        static Transform spawn;
        static float findAt, nextSend, nextSweep;
        static int seq, logs;

        class Copy { public GameObject Go; public int From, Seq; public float LastAt; public bool Held; }
        static readonly List<Copy> copies = new List<Copy>();

        public static void OnLevelLoaded()
        {
            work = null; prefabVar = null; tracked = null; spawn = null; copies.Clear();
            findAt = Time.realtimeSinceStartup + 20f;
        }

        static bool Find()
        {
            GameObject g = Game.FindAny(Root);
            if (g == null) return false;
            foreach (PlayMakerFSM f in g.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (f.FsmName != "Work" || f.Fsm.GetState("Spawn plate") == null) continue;
                FsmGameObject v = f.FsmVariables.FindFsmGameObject("Prefab");
                if (v == null) continue;
                work = f; prefabVar = v;
                Log.Info("plats : automate de Jouni trouve (" + Recon.Path(f.transform) + "::Work)");
                return true;
            }
            return false;
        }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (work == null)
            {
                if (now < findAt) return;
                findAt = now + 20f;
                if (!Find()) return;
            }
            GameObject p = prefabVar.Value;
            if (p != tracked)
            {
                if (tracked != null) Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@plat").U8(0).U8(seq), true);
                tracked = p;
                if (p != null)
                {
                    seq = (seq + 1) & 0xFF;
                    Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@plat").U8(1).U8(seq).Str(Name(p)).Vec(p.transform.position).Quat(p.transform.rotation), true);
                    nextSend = now + 0.5f;
                    if (logs++ < 10) Log.Info("plats : " + Name(p) + " commande ici, envoye (#" + seq + ")");
                }
                else if (logs++ < 10) Log.Info("plats : plat #" + seq + " disparu ici, retire chez les autres");
            }
            if (tracked != null && now >= nextSend)
            {
                nextSend = now + 0.5f;
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@plat").U8(2).U8(seq).Vec(tracked.transform.position).Quat(tracked.transform.rotation).Bool(tracked.transform.parent != null), true);
            }
            if (now >= nextSweep)
            {
                nextSweep = now + 1f;
                for (int i = copies.Count - 1; i >= 0; i--)
                {
                    Copy c = copies[i];
                    if (c.Go != null && now - c.LastAt < 15f && Session.Players.ContainsKey(c.From)) continue;
                    if (c.Go != null) Object.Destroy(c.Go);
                    copies.RemoveAt(i);
                    Log.Info("plats : plat #" + c.Seq + " de #" + c.From + " retire (" + (c.Go == null ? "detruit" : now - c.LastAt >= 15f ? "sans nouvelles" : "joueur parti") + ")");
                }
            }
        }

        static string Name(GameObject p)
        {
            string n = p.name;
            int i = n.IndexOf("(Clone)", System.StringComparison.Ordinal);
            return i > 0 ? n.Substring(0, i) : n;
        }

        // Modele du plat : l'objet que cree l'automate 'Work' (actions CreateObject de ses etats Sausages, Meatballs...).
        static GameObject Prefab(string name)
        {
            if (work == null && !Find()) return null;
            foreach (FsmState s in work.Fsm.States)
                foreach (FsmStateAction a in s.Actions)
                {
                    var c = a as CreateObject;
                    if (c != null && c.gameObject != null && c.gameObject.Value != null && c.gameObject.Value.name == name) return c.gameObject.Value;
                }
            return null;
        }

        static Copy Of(int from, int sq)
        {
            foreach (Copy c in copies) if (c.From == from && c.Seq == sq) return c;
            return null;
        }

        public static void OnMessage(int who, NetReader r)
        {
            int kind = r.U8(), sq = r.U8();
            float now = Time.realtimeSinceStartup;
            if (kind == 0)
            {
                Copy c = Of(who, sq);
                if (c == null) return;
                if (c.Go != null) Object.Destroy(c.Go);
                copies.Remove(c);
                if (logs++ < 10) Log.Info("plats : plat #" + sq + " de #" + who + " retire (mange ou jete chez lui)");
                return;
            }
            if (kind == 1)
            {
                string name = r.Str();
                Vector3 pos = r.Vec(); Quaternion rot = r.Quat();
                Copy c = Of(who, sq);
                if (c != null) { c.LastAt = now; return; }
                GameObject prefab = Prefab(name);
                if (prefab == null) { Log.Warn("plats : modele « " + name + " » introuvable ici (plat de #" + who + ")"); return; }
                var go = (GameObject)Object.Instantiate(prefab, pos, rot);
                foreach (PlayMakerFSM f in go.GetComponentsInChildren<PlayMakerFSM>(true)) Object.Destroy(f);
                foreach (Rigidbody rb in go.GetComponentsInChildren<Rigidbody>(true)) Object.Destroy(rb);
                foreach (Collider col in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(col);
                go.name = "MWCoop plat " + name;
                go.SetActive(true);
                copies.Add(new Copy { Go = go, From = who, Seq = sq, LastAt = now });
                if (logs++ < 10) Log.Info("plats : " + name + " de #" + who + " cree ici (#" + sq + ")");
                return;
            }
            if (kind == 2)
            {
                Vector3 pos = r.Vec(); Quaternion rot = r.Quat(); bool held = r.Bool();
                Copy c = Of(who, sq);
                if (c == null || c.Go == null) return;
                c.LastAt = now;
                if (spawn == null) { GameObject sp = Game.FindAny(SpawnPath); if (sp != null) spawn = sp.transform; }
                Transform t = c.Go.transform;
                if (held && spawn != null)
                {
                    if (t.parent != spawn) { t.SetParent(spawn, false); t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; }
                }
                else
                {
                    if (t.parent != null) t.SetParent(null, true);
                    t.position = pos; t.rotation = rot;
                }
                c.Held = held;
            }
        }
    }
}
