using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Compatibilite du mod « Jack Stands » (benjammin4dayz, Nexus 1988 ; demande de .nolly, 12/10) : 4 chandelles
    // (« Jack Stand(itemx) », pieces OASIS) qui se fixent aux points d'attache poses sous la CORRIS (OASIS : la piece devient
    // l'enfant du point, son corps physique est retire ; detachee, il revient) et dont la tige se regle par crans (Use).
    // Sauvees par le mod (MSCLoader SaveLoad) : chez chacun les memes, dans le meme ordre (sauvegarde de l'hote).
    // Ici : chaque chandelle suivie par Props sous w:mod:jack:<rang> (deplacements, prise en main ; reprise quand son corps
    // revient) ; fixation (point d'attache) et cran envoyes a chaque changement fait ici (@jack), rejoues chez les autres
    // par le mod lui-meme (JackStand.AttachedTo / CurrentStep, puis ses reglages d'apres fixation).
    public static class ModJackStands
    {
        const BindingFlags A = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        class Stand { public object Js; public GameObject Go; public int Att = -99, Step = -99; public Rigidbody Rb; }
        static readonly List<Stand> stands = new List<Stand>();
        static bool looked;
        static Type tManager, tStand;
        static float findAt, nextPoll, nextFull;

        public static void OnLevelLoaded() { stands.Clear(); findAt = Time.realtimeSinceStartup + 15f; }

        static bool Find()
        {
            if (stands.Count > 0) return true;
            if (Time.realtimeSinceStartup < findAt) return false;
            findAt = Time.realtimeSinceStartup + 10f;
            if (!looked)
            {
                looked = true;
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = null; try { t = a.GetType("JackStands.JackStandManager", false); } catch { }
                    if (t != null) { tManager = t; tStand = a.GetType("JackStands.JackStand"); break; }
                }
            }
            if (tManager == null || tStand == null) return false;
            try
            {
                object mgr = tManager.GetProperty("Instance", A).GetValue(null, null);
                IList list = tManager.GetField("_jackStands", A).GetValue(mgr) as IList;
                if (list == null || list.Count == 0) return false;
                FieldInfo go = tStand.GetField("_go", A);
                foreach (object js in list) stands.Add(new Stand { Js = js, Go = go.GetValue(js) as GameObject });
                Log.Info("mods : Jack Stands present, " + stands.Count + " chandelle(s) suivie(s)");
            }
            catch (Exception e) { Log.Warn("mods : Jack Stands, " + e.Message); stands.Clear(); }
            return stands.Count > 0;
        }

        static int Att(Stand s) { return (int)tStand.GetProperty("AttachedTo", A).GetValue(s.Js, null); }
        static int Step(Stand s) { return (int)tStand.GetProperty("CurrentStep", A).GetValue(s.Js, null); }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame || !Find()) return;
            float now = Time.realtimeSinceStartup;
            if (now < nextPoll) return;
            nextPoll = now + 0.2f;
            bool full = now >= nextFull && Session.IsHost;
            if (full) nextFull = now + 10f;
            for (int i = 0; i < stands.Count; i++)
            {
                Stand s = stands[i];
                if (s.Go == null) continue;
                // suivi par Props tant qu'elle a un corps (detachee) ; corps recree a chaque detachement
                Rigidbody rb = s.Go.GetComponent<Rigidbody>();
                if (rb != null && rb != s.Rb) Props.Adopt(s.Go, "w:mod:jack:" + i);
                s.Rb = rb;
                int att, step;
                try { att = Att(s); step = Step(s); } catch { continue; }
                bool first = s.Att == -99;
                bool changed = !first && (att != s.Att || step != s.Step);
                s.Att = att; s.Step = step;
                if ((changed || full) && Session.RemoteCount > 0)
                {
                    Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@jack").U8(i).I32(att).I32(step), true);
                    if (changed) Log.Info("mods : chandelle " + i + (att >= 0 ? " fixee au point " + att : " libre") + ", cran " + step + ", envoye");
                }
            }
        }

        public static void OnMessage(int who, NetReader r)
        {
            int i = r.U8(), att = r.I32(), step = r.I32();
            if (!Find() || i >= stands.Count) return;
            Stand s = stands[i];
            try
            {
                int cur = Att(s);
                if (cur != att)
                {
                    if (cur >= 0 && att >= 0) tStand.GetProperty("AttachedTo", A).SetValue(s.Js, -1, null);   // (d'un point a l'autre : detachee d'abord)
                    tStand.GetProperty("AttachedTo", A).SetValue(s.Js, att, null);
                    if (att >= 0) { tStand.GetMethod("ApplyAttachmentOffset", A).Invoke(s.Js, null); tStand.GetMethod("SetInteractionState", A).Invoke(s.Js, new object[] { false }); }
                    else tStand.GetMethod("SetInteractionState", A).Invoke(s.Js, new object[] { true });
                    Log.Info("mods : chandelle " + i + (att >= 0 ? " fixee au point " + att : " detachee") + " par #" + who + ", rejoue ici");
                }
                if (Step(s) != step && att < 0) tStand.GetProperty("CurrentStep", A).SetValue(s.Js, step, null);
                s.Att = Att(s); s.Step = Step(s);
            }
            catch (Exception e) { Log.Warn("mods : chandelle " + i + " : " + (e.InnerException ?? e).Message); }
        }

        // Essai [Test] Autotest=chandelles : l'hote fixe la chandelle 0 au point 0 a 40 s, monte la 1 d'un cran a 45 s, et
        // detache la 0 a 55 s ; chacun note l'etat des chandelles a 50 et 65 s.
        static int tStep;
        public static void Test(string mode, float t)
        {
            if (mode != "chandelles" || !Find()) return;
            if (Session.IsHost && t > 40f && tStep == 0) { tStep = 1; tStand.GetProperty("AttachedTo", A).SetValue(stands[0].Js, 0, null); tStand.GetMethod("ApplyAttachmentOffset", A).Invoke(stands[0].Js, null); Log.Info("autotest : chandelles, 0 fixee ici"); }
            if (Session.IsHost && t > 45f && tStep == 1 && stands.Count > 1) { tStep = 2; tStand.GetProperty("CurrentStep", A).SetValue(stands[1].Js, Step(stands[1]) + 1, null); Log.Info("autotest : chandelles, 1 montee ici"); }
            if (Session.IsHost && t > 55f && tStep == 2) { tStep = 3; tStand.GetProperty("AttachedTo", A).SetValue(stands[0].Js, -1, null); Log.Info("autotest : chandelles, 0 detachee ici"); }
            if ((t > 50f && t < 51f) || (t > 65f && t < 66f))
            {
                if (Time.frameCount % 60 != 0) return;
                var sb = new System.Text.StringBuilder("autotest : chandelles");
                for (int i = 0; i < stands.Count; i++) sb.Append(" | ").Append(i).Append(" point ").Append(Att(stands[i])).Append(" cran ").Append(Step(stands[i])).Append(" en ").Append(stands[i].Go.transform.position.ToString("F2"));
                Log.Info(sb.ToString());
            }
        }
    }
}
