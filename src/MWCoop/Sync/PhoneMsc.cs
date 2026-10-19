using System.Collections.Generic;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Telephone de My Summer Car (vidage v.250908-04, 19/10). PhoneLogic::Ring (YARD/.../Telephone/Logic/PhoneLogic) choisit
    // l'appel d'apres ses booleens (Shit1..5, Wood1, Moving, Mummo1/2, Tohvakka1/2, Drunk...) et allume l'objet Ring ; decrocher
    // mene son automate 'Ring' de "State 2" a l'etat du sujet ("Shit 1", "Wood1", "Moving"...) : voix, sous-titre, booleen de
    // PhoneLogic remis a faux, boulot note (Level.Called, flechette de la carte du salon) et evenement au boulot (GLOBALEVENT,
    // MOVING, FISH...). Chacun a son telephone : les deux sonnaient, et le boulot n'etait pris que chez celui qui decrochait
    // (fosse : Called seulement chez lui -- l'autre ne voyait jamais le client payer).
    // Decroche ici : "@tel" + etat du sujet. Chez les autres : sonnerie coupee si c'est le meme appel, puis les actions de l'etat
    // rejouees sans la voix ni le sous-titre (booleens, entiers, objets allumes, evenements) ; les evenements vers un automate que
    // Jobs suit deja (HouseWood1, Moving, Farm/Job) ne sont pas renvoyes : Jobs rejoue la transition.
    public static class PhoneMsc
    {
        const string RingPath = "YARD/Building/LIVINGROOM/Telephone/Logic/Ring";
        static PlayMakerFSM ring;
        static bool hooked, applying;
        static float nextLook;
        static readonly Dictionary<string, int> owners = new Dictionary<string, int>();   // sujet -> joueur qui a decroche

        public static void OnLevelLoaded() { ring = null; hooked = false; nextLook = 0f; owners.Clear(); }

        // Joueur qui a decroche pour ce sujet ("Shit 1"...) : -1 inconnu.
        public static int Owner(string topic) { int w; return owners.TryGetValue(topic, out w) ? w : -1; }

        class Hook : ModHook
        {
            public override string Module { get { return "quetes (telephone)"; } }
            public string Topic;
            public override void OnEnter()
            {
                try
                {
                    FsmState prev = Fsm.PreviousActiveState;
                    if (!applying && Replay.Depth == 0 && prev != null && prev.Name == "State 2" && Session.Active)
                    {
                        owners[Topic] = Session.LocalId;
                        MWCoop.Log.Info("telephone : decroche ici (" + Topic + ")");
                        Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@tel").Str(Topic), true);
                    }
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        // Etats des sujets : ceux ou mene "State 2" par un autre evenement que FINISHED.
        static List<string> Topics(PlayMakerFSM f)
        {
            var l = new List<string>();
            FsmState s2 = f.Fsm.GetState("State 2");
            if (s2 != null) foreach (FsmTransition t in s2.Transitions) if (t.EventName != "FINISHED" && !l.Contains(t.ToState)) l.Add(t.ToState);
            return l;
        }

        static PlayMakerFSM Ring()
        {
            if (ring == null && Time.realtimeSinceStartup >= nextLook)
            {
                nextLook = Time.realtimeSinceStartup + 5f;
                GameObject g = Game.FindAny(RingPath);
                if (g != null) ring = Game.FsmOn(g, "Ring");
            }
            return ring;
        }

        public static void Update()
        {
            if (!Game.Msc || hooked || !PlayerSync.InGame) return;
            PlayMakerFSM f = Ring();
            if (f == null || !f.gameObject.activeInHierarchy) return;   // (actions chargees au premier allumage : premiere sonnerie)
            int n = 0;
            try
            {
                foreach (string t in Topics(f))
                {
                    FsmState s = f.Fsm.GetState(t);
                    if (s == null || !s.IsInitialized || System.Array.Exists(s.Actions, a => a is Hook)) continue;
                    FsmHooks.Prepend(s, new Hook { Topic = t });
                    n++;
                }
            }
            catch { return; }
            hooked = true;
            Log.Info("telephone : " + n + " sujets d'appel suivis");
        }

        public static void OnMessage(int who, NetReader r)
        {
            string topic = r.Str();
            owners[topic] = who;
            PlayMakerFSM f = Ring();
            if (f == null) { Log.Warn("telephone : appel " + topic + " pris par #" + who + ", telephone introuvable ici"); return; }
            FsmState s = f.Fsm.GetState(topic);
            if (s == null) { Log.Warn("telephone : sujet " + topic + " inconnu ici"); return; }
            // Meme appel en train de sonner ici : raccroche (l'objet Ring s'eteint, comme "Disable phone").
            FsmString cur = f.FsmVariables.FindFsmString("Topic");
            bool ringingHere = f.gameObject.activeInHierarchy;
            if (ringingHere && f.ActiveStateName != topic) f.gameObject.SetActive(false);
            int done = 0, skipped = 0;
            EnsureLoaded(s);
            applying = true; Replay.Depth++;
            try
            {
                FsmStateAction[] acts = null;
                try { acts = s.Actions; } catch { }
                if (acts == null || acts.Length == 0) done = Fallback(topic);   // (telephone jamais allume ici : actions pas chargees)
                else
                    foreach (FsmStateAction a in acts)
                    {
                        if (a == null || a is Hook) continue;
                        try { if (Apply(f, a)) done++; else skipped++; }
                        catch (System.Exception e) { Log.Warn("telephone : " + topic + " / " + a.GetType().Name + " : " + e.Message); }
                    }
            }
            finally { applying = false; Replay.Depth--; }
            Log.Info("telephone : appel " + topic + " pris par #" + who + " : " + done + " effets rejoues ici" + (ringingHere ? " (sonnait ici" + (cur != null ? ", " + cur.Value : "") + " : raccroche)" : ""));
        }

        // Actions d'un etat jamais visite (objet Ring jamais allume ici : PlayMaker les charge a l'initialisation de l'automate).
        static void EnsureLoaded(FsmState s)
        {
            try { if (s.Actions != null) return; }   // (le chargement paresseux de PlayMaker echoue sur un automate jamais initialise)
            catch (System.Exception e) { Log.Info("telephone : actions de " + s.Name + " pas chargeables ici (" + e.GetType().Name + ")"); }
        }

        // Sans les actions : l'essentiel d'apres le vidage -- booleen de PhoneLogic du sujet remis a faux, fosse notee (Level.Called),
        // et les evenements vers des automates que Jobs ne suit pas (grand-mere, Jokke a ramener du bar). Le reste (bois,
        // demenagement, ferme) est rejoue par Jobs.
        static int Fallback(string topic)
        {
            int n = 0;
            PlayMakerFSM pl = Game.FindFsm("YARD/Building/LIVINGROOM/Telephone/Logic/PhoneLogic", "Ring");
            string flag = topic == "Mummo3" ? "Mummo3Church" : topic == "Drunk lift" ? "Drunk" : topic.Replace(" ", "");
            FsmBool b = pl != null ? pl.FsmVariables.FindFsmBool(flag) : null;
            if (b != null) { b.Value = false; n++; }
            if (topic.StartsWith("Shit "))
            {
                string k = topic.Substring(5);
                PlayMakerFSM lv = Game.FindFsm("JOBS/HouseShit" + k + "/WasteWell_2000litre/Shit/Level" + k + "/ShitLevelTrigger", "Level");
                FsmBool c = lv != null ? lv.FsmVariables.FindFsmBool("Called") : null;
                if (c != null) { c.Value = true; n++; }
            }
            PlayMakerFSM t = null; string ev = null;
            if (topic == "Mummo1") { t = Game.FindFsm("JOBS/Mummola", "Logic"); ev = "FISH"; }
            else if (topic == "Mummo2") { t = Game.FindFsm("JOBS/Mummola", "Logic"); ev = "GROCERIES"; }
            else if (topic == "Mummo3") { t = Game.FindFsm("JOBS/Mummola", "Logic"); ev = "CHURCH"; }
            else if (topic == "Drunk lift") { t = Game.FindFsm("KILJUGUY/HikerPivot/JokkeHiker2", "Logic"); ev = "GLOBALEVENT"; }
            if (t != null && !Jobs.Tracks(t)) { t.SendEvent(ev); n++; }
            Log.Info("telephone : " + topic + " applique sans ses actions (telephone jamais allume ici) : " + n + " effets");
            return n;
        }

        // Une action de l'etat du sujet, sans la voix, le sous-titre ni l'attente. Vrai : appliquee.
        static bool Apply(PlayMakerFSM self, FsmStateAction a)
        {
            Fsm fsm = self.Fsm;
            var sb = a as SetFsmBool;
            if (sb != null)
            {
                PlayMakerFSM t = Target(fsm, sb.gameObject, sb.fsmName.Value);
                FsmBool v = t != null ? t.FsmVariables.FindFsmBool(sb.variableName.Value) : null;
                if (v == null) return false;
                v.Value = sb.setValue.Value;
                return true;
            }
            var si = a as SetFsmInt;
            if (si != null)
            {
                PlayMakerFSM t = Target(fsm, si.gameObject, si.fsmName.Value);
                FsmInt v = t != null ? t.FsmVariables.FindFsmInt(si.variableName.Value) : null;
                if (v == null) return false;
                v.Value = si.setValue.Value;
                return true;
            }
            var ag = a as ActivateGameObject;
            if (ag != null)
            {
                GameObject g = fsm.GetOwnerDefaultTarget(ag.gameObject);
                if (g == null || g == self.gameObject) return false;
                g.SetActive(ag.activate.Value);
                return true;
            }
            var se = a as SendEvent;
            if (se != null && se.sendEvent != null) return SendTo(fsm, se.eventTarget, se.sendEvent.Name);
            var sn = a as SendEventByName;
            if (sn != null && sn.sendEvent != null) return SendTo(fsm, sn.eventTarget, sn.sendEvent.Value);
            return false;   // (voix, sous-titre, attente)
        }

        static PlayMakerFSM Target(Fsm fsm, FsmOwnerDefault owner, string fsmName)
        {
            GameObject g = fsm.GetOwnerDefaultTarget(owner);
            return g != null ? Game.FsmOn(g, fsmName) : null;
        }

        static bool SendTo(Fsm fsm, FsmEventTarget et, string ev)
        {
            if (et == null || string.IsNullOrEmpty(ev) || et.target != FsmEventTarget.EventTarget.GameObjectFSM) return false;
            GameObject g = fsm.GetOwnerDefaultTarget(et.gameObject);
            PlayMakerFSM t = g != null ? Game.FsmOn(g, et.fsmName.Value) : null;
            if (t == null) return false;
            if (Jobs.Tracks(t)) return false;   // (Jobs rejoue deja la transition de ce boulot)
            t.SendEvent(ev);
            return true;
        }

        // [Test] Autotest=msctel : l'invite decroche un appel de fosse septique (Shit 1) a 60 s ; chacun note Called et Shit1.
        // (comme un decroche : Topic = l'evenement du sujet, puis "State 2" qui l'envoie)
        public static string TestAnswer(string ev)
        {
            PlayMakerFSM f = Ring();
            if (f == null) return "telephone introuvable";
            if (!f.gameObject.activeSelf) f.gameObject.SetActive(true);
            Update();
            f.FsmVariables.GetFsmString("Topic").Value = ev;
            Game.SetState(f, "State 2");
            return "decroche " + ev + " -> " + f.ActiveStateName + (hooked ? "" : " (sujets pas encore suivis)");
        }

        public static string State(int n)
        {
            PlayMakerFSM pl = Game.FindFsm("YARD/Building/LIVINGROOM/Telephone/Logic/PhoneLogic", "Ring");
            PlayMakerFSM lv = Game.FindFsm("JOBS/HouseShit" + n + "/WasteWell_2000litre/Shit/Level" + n + "/ShitLevelTrigger", "Level");
            FsmBool sh = pl != null ? pl.FsmVariables.FindFsmBool("Shit" + n) : null, called = lv != null ? lv.FsmVariables.FindFsmBool("Called") : null;
            return "PhoneLogic.Shit" + n + " " + (sh != null ? sh.Value.ToString() : "?") + ", Level" + n + ".Called " + (called != null ? called.Value.ToString() : "?") + ", proprietaire " + Owner("Shit " + n);
        }
    }
}
