using System;
using System.Reflection;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Compatibilite du mod « Second Machtwagen » (Morri, Nexus 534 ; demande de JD, 10/10). Le mod copie la Machtwagen du taxi
    // (SECONDMACHTWAGEN, garage de Fleetari), pose une feuille de vente sur le comptoir (SALE(Clone) : un clic, 25 000 mk,
    // KeyTrue -- contact et poignees -- feuille detruite, feuille de preparation TUNING(Clone) a la place), et tire au hasard
    // couleur, interieur et plaque a la premiere partie (chez chacun : differents). Ici, s'il est charge :
    //  - l'etat du mod (achetee, couleur, interieur, plaque, preparation, transmission integrale, caisson de basses) est
    //    envoye a chaque changement (@mw2) et par l'hote toutes les 10 s ; les autres l'appliquent (feuille de vente retiree,
    //    KeyTrue, peinture, plaque...). Seul l'acheteur paie (son porte-monnaie).
    //  - un invite prend d'abord l'etat de l'hote (pas d'envoi tant qu'il ne l'a pas recu : ses tirages au hasard ne
    //    comptent pas).
    // La voiture elle-meme (CarDynamics a la racine) est suivie par VehicleSync comme les autres.
    public static class ModSecondMachtwagen
    {
        class State
        {
            public bool Own, Tuning, Awd, Sabuf; public int Color, ColorInt, Plate; public string Text = "";
            public bool Same(State o) { return o != null && Own == o.Own && Tuning == o.Tuning && Awd == o.Awd && Sabuf == o.Sabuf && Color == o.Color && ColorInt == o.ColorInt && Plate == o.Plate && Text == o.Text; }
            public override string ToString() { return (Own ? "achetee" : "a vendre") + ", couleur " + Color + "/" + ColorInt + ", plaque " + Text + " (" + Plate + ")" + (Tuning ? ", preparee" : "") + (Awd ? ", 4x4" : "") + (Sabuf ? ", basses" : ""); }
        }

        static Type type;
        static bool looked;
        static object inst;
        static State last;              // dernier etat connu de tous (envoye ou recu)
        static bool gotHost;            // invite : etat de l'hote recu
        static float next, nextHost;
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public static void OnLevelLoaded() { inst = null; last = null; gotHost = false; }

        static object Instance()
        {
            if (!looked)
            {
                looked = true;
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = null;
                    try { t = a.GetType("SecondMachtwagen.SecondMachtwagen"); } catch { }
                    if (t != null) { type = t; Log.Info("mods : Second Machtwagen present, synchronise (achat, couleurs, plaque, preparation)"); break; }
                }
            }
            if (type == null) return null;
            if (inst == null)
            {
                FieldInfo f = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                inst = f != null ? f.GetValue(null) : null;
                // (pas encore charge : la voiture et ses pieces sont posees a OnLoad. Pas « inited » : son PostLoad plante sur une
                // sauvegarde ou il n'a encore rien ecrit -- lecture d'un pare-soleil absent -- et ne le met jamais a vrai.)
                if (inst != null && (Get<GameObject>("MyMachtwagen") == null || Get<Transform>("LOD") == null)) inst = null;
            }
            return inst;
        }

        static T Get<T>(string name) { FieldInfo f = type.GetField(name, Any); object v = f != null && inst != null ? f.GetValue(inst) : null; return v is T ? (T)v : default(T); }
        static void Set(string name, object v) { FieldInfo f = type.GetField(name, Any); if (f != null) f.SetValue(inst, v); }
        static void Call(string name, params object[] args) { MethodInfo m = type.GetMethod(name, Any); if (m != null) try { m.Invoke(inst, args); } catch (Exception e) { Log.Warn("mods : Second Machtwagen " + name + " : " + (e.InnerException ?? e).Message); } }

        static State Read()
        {
            return new State { Own = Get<bool>("owncar"), Tuning = Get<bool>("tuning"), Awd = Get<bool>("m_awd"), Sabuf = Get<bool>("sabuf"),
                               Color = Get<int>("MWColor"), ColorInt = Get<int>("MWColorInt"), Plate = Get<int>("Plate"), Text = Get<string>("LicensePlate") ?? "" };
        }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame || Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + 0.5f;
            if (Instance() == null) return;
            State s = Read();
            if (!Session.IsHost && !gotHost) return;   // (d'abord l'etat de l'hote)
            bool changed = !s.Same(last);
            if (changed || (Session.IsHost && Time.realtimeSinceStartup >= nextHost))
            {
                if (changed) Log.Info("mods : Second Machtwagen " + s + (last == null ? "" : " (change ici)"));
                last = s;
                nextHost = Time.realtimeSinceStartup + 10f;
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@mw2").Bool(s.Own).U8(s.Color).U8(s.ColorInt).U8(s.Plate).Str(s.Text).Bool(s.Tuning).Bool(s.Awd).Bool(s.Sabuf), true);
            }
        }

        // [Test] Autotest=mw2 : a 45 s l'invite « achete » (feuille detruite, KeyTrue, comme BuyCar) ; a 55 s chacun note
        // l'etat du mod, la feuille de vente, et si VehicleSync suit la voiture.
        static int testStep;
        public static void Test(string mode, float t)
        {
            if (mode != "mw2" || Instance() == null) return;
            if (testStep == 0 && t > 45f)
            {
                testStep = 1;
                if (!Session.IsHost)
                {
                    GameObject sale = GameObject.Find("SALE(Clone)");
                    if (sale != null) UnityEngine.Object.Destroy(sale);
                    Call("KeyTrue");
                    Log.Info("autotest : mw2, achat ici (feuille " + (sale != null ? "detruite" : "absente") + ")");
                }
            }
            if (testStep == 1 && t > 55f)
            {
                testStep = 2;
                Log.Info("autotest : mw2, " + Read() + ", feuille de vente " + (GameObject.Find("SALE(Clone)") != null ? "presente" : "absente") + ", preparation " + (GameObject.Find("TUNING(Clone)") != null ? "presente" : "absente")
                         + ", voiture suivie " + (VehicleSync.Body("SECONDMACHTWAGEN") != null));
            }
        }

        public static void OnMessage(int who, NetReader r)
        {
            var s = new State { Own = r.Bool(), Color = r.U8(), ColorInt = r.U8(), Plate = r.U8(), Text = r.Str(), Tuning = r.Bool(), Awd = r.Bool(), Sabuf = r.Bool() };
            if (Instance() == null) return;
            if (who == 0) gotHost = true;
            State cur = Read();
            if (s.Same(cur)) { last = s; return; }
            Log.Info("mods : Second Machtwagen de #" + who + " : " + s + " (ici : " + cur + ")");
            if (s.Own && !cur.Own)
            {   // achetee ailleurs : feuille de vente retiree, cles (KeyTrue pose aussi la feuille de preparation)
                GameObject sale = GameObject.Find("SALE(Clone)");
                if (sale != null) UnityEngine.Object.Destroy(sale);
                Call("KeyTrue");
            }
            if (s.Color != cur.Color || s.ColorInt != cur.ColorInt)
            {
                Set("MWColor", s.Color); Set("MWColorInt", s.ColorInt);
                Call("SetBodyColors"); Call("SetInteriorColors");
            }
            if (s.Plate != cur.Plate || s.Text != cur.Text)
            {
                Set("Plate", s.Plate); Set("LicensePlate", s.Text);
                Transform lod = Get<Transform>("LOD");
                GameObject car = Get<GameObject>("MyMachtwagen");
                if (lod != null) Call("ApplyLicensePlate", lod.Find("RegPlateGen"));
                if (car != null) Call("ApplyLicensePlate", car.transform.Find("Hatch/Hatch/RegPlateGen"));
            }
            if (s.Tuning != cur.Tuning) { Set("tuning", s.Tuning); Call("Tuning"); }
            if (s.Awd != cur.Awd) Set("m_awd", s.Awd);
            if (s.Sabuf != cur.Sabuf) { Set("sabuf", s.Sabuf); Call("CDPlayer"); }
            last = Read();
        }
    }
}
