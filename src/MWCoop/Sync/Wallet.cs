using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Argent (demande de JD) : chaque joueur a son porte-monnaie (achats chacun de son cote), mais
    // tout ce qui RENTRE (paie, ventes, recompenses) est recu par tous. Globales PlayerMoney (liquide)
    // et PlayerBankAccount (banque), comparees deux fois par seconde :
    //  - une hausse de la richesse totale est un revenu, envoye aux autres (sur le meme compte) ;
    //  - une baisse est une depense : elle reste locale ;
    //  - liquide <-> banque (distributeur, depot) est un transfert : rien a envoyer.
    //  - l'invite recoit la sauvegarde de l'hote a chaque connexion : son porte-monnaie est garde a
    //    part dans son profil (porte-monnaie.ini, une ligne par monde = PlayerID de la sauvegarde)
    //    et retabli a son retour dans ce monde.
    public static class Wallet
    {
        // Boulots rejoues (Jobs) : la paie qu'ils versent ici n'est pas renvoyee, et elle « consomme »
        // le meme revenu annonce par le joueur qui a vraiment fait le boulot (pas de double paie).
        static float suppressUntil;
        static readonly System.Collections.Generic.List<KeyValuePair<float, float>> suppressed = new System.Collections.Generic.List<KeyValuePair<float, float>>();

        public static void Suppress(float seconds) { suppressUntil = Mathf.Max(suppressUntil, Time.realtimeSinceStartup + seconds); }

        static FsmFloat cash, bank;
        static float lastCash, lastBank, next, readyAt = -1, nextStore;
        static string world;

        static string StorePath { get { return System.IO.Path.Combine(Log.DataDir, "porte-monnaie.ini"); } }

        static void Restore()
        {
            FsmFloat pid = FsmVariables.GlobalVariables.FindFsmFloat("PlayerID");
            world = pid != null ? pid.Value.ToString("F0") : "";
            if (Session.IsHost || world.Length == 0 || !System.IO.File.Exists(StorePath)) return;
            foreach (string l in System.IO.File.ReadAllLines(StorePath))
            {
                string[] f = l.Split('=');
                if (f.Length != 2 || f[0] != world) continue;
                string[] v = f[1].Split(';');
                float c, b;
                if (v.Length == 2 && float.TryParse(v[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out c)
                    && float.TryParse(v[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out b))
                {
                    cash.Value = c; bank.Value = b;
                    Log.Info("argent : porte-monnaie retrouve pour ce monde (" + world + ")");
                    Hud.Toast("Votre porte-monnaie : " + Mathf.RoundToInt(c) + " mk");
                }
            }
        }

        static void Store()
        {
            if (Session.IsHost || world == null || world.Length == 0) return;
            var lines = new System.Collections.Generic.List<string>();
            if (System.IO.File.Exists(StorePath))
                foreach (string l in System.IO.File.ReadAllLines(StorePath)) if (!l.StartsWith(world + "=")) lines.Add(l);
            lines.Add(world + "=" + cash.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" + bank.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            try { System.IO.File.WriteAllLines(StorePath, lines.ToArray()); } catch { }
        }
        static bool ready;

        public static void OnLevelLoaded()
        {
            cash = bank = null;
            ready = false;
            readyAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 15f : -1;   // apres le chargement de la sauvegarde
        }

        public static void Update()
        {
            if (!Session.Active || readyAt < 0 || Time.realtimeSinceStartup < readyAt) return;
            if (!ready)
            {
                cash = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney");
                bank = FsmVariables.GlobalVariables.FindFsmFloat("PlayerBankAccount");
                if (cash == null || bank == null) { readyAt = -1; Log.Warn("argent : globales absentes"); return; }
                Restore();
                lastCash = cash.Value; lastBank = bank.Value;
                ready = true;
                Log.Info("argent : liquide " + lastCash + ", banque " + lastBank);
            }
            if (Time.realtimeSinceStartup >= nextStore) { nextStore = Time.realtimeSinceStartup + 10f; Store(); }
            if (Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + 0.5f;
            float dC = cash.Value - lastCash, dB = bank.Value - lastBank;
            if (Mathf.Abs(dC) < 0.005f && Mathf.Abs(dB) < 0.005f) return;
            float transfer = 0;
            if (dC > 0 && dB < 0) transfer = Mathf.Min(dC, -dB);
            else if (dC < 0 && dB > 0) transfer = Mathf.Min(-dC, dB);
            float inC = dC > 0 ? dC - (dB < 0 ? transfer : 0) : 0;
            float inB = dB > 0 ? dB - (dC < 0 ? transfer : 0) : 0;
            lastCash = cash.Value; lastBank = bank.Value;
            if (inC < 0.005f && inB < 0.005f) return;
            if (Time.realtimeSinceStartup < suppressUntil)
            {
                suppressed.Add(new KeyValuePair<float, float>(Time.realtimeSinceStartup, inC + inB));
                Log.Info("argent : revenu " + (inC + inB) + " venu d'un boulot rejoue, garde pour soi");
                return;
            }
            Log.Info("argent : revenu " + inC + " (liquide) + " + inB + " (banque), partage");
            Session.SendAll(new NetWriter(Msg.Income).U8(Session.LocalId).F32(inC).F32(inB), true);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            float inC = r.F32(), inB = r.F32();
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Income).U8(who).F32(inC).F32(inB), true, who);
            if (!ready || inC < 0 || inB < 0 || inC + inB > 1e6f) return;
            // Deja recu par le boulot rejoue ici ?
            suppressed.RemoveAll(s => Time.realtimeSinceStartup - s.Key > 20f);
            int hit = suppressed.FindIndex(s => Mathf.Abs(s.Value - (inC + inB)) < 0.5f);
            if (hit >= 0) { suppressed.RemoveAt(hit); Log.Info("argent : revenu " + (inC + inB) + " deja verse par le boulot rejoue"); return; }
            cash.Value += inC; bank.Value += inB;
            lastCash += inC; lastBank += inB;   // pas de renvoi
            PlayerInfo pi;
            string name = Session.Players.TryGetValue(who, out pi) ? pi.Name : "?";
            Hud.Toast("+" + Mathf.RoundToInt(inC + inB) + " mk (revenu de " + name + ")");
            Log.Info("argent : +" + inC + " liquide, +" + inB + " banque, de " + name);
        }

        // Essais : gagne (ou depense) 'amount' en liquide, comme une paie ou un achat.
        public static string Test(float amount)
        {
            if (!ready) return "argent pas pret";
            cash.Value += amount;
            return "liquide " + cash.Value;
        }

        public static string State() { return ready ? "liquide " + cash.Value + ", banque " + bank.Value : "?"; }
    }
}
