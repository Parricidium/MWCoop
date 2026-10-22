using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Difficulte de depart (suggestion du 14/10 : « choisir au debut : carburant du tracteur et de la Sorbet, argent liquide et en
    // banque ; facile, moyen, difficile, standard, tout au hasard »). [Coop] Difficulte, decidee par l'hote (lanceur, Options du
    // jeu > Coop) : 0 le jeu, 1 facile (argent x2, reservoirs pleins), 2 moyen (argent x0,5, reservoirs a moitie), 3 difficile
    // (argent x0,1, reservoirs a 15 %), 4 au hasard (tire d'apres l'identifiant de la partie : le meme pour tous). Appliquee par
    // l'hote au premier chargement d'une NOUVELLE partie (Flow.StartedNew), une fois : argent (PlayerMoney, PlayerBankAccount) et
    // chaque reservoir FuelTank* :: Data (FuelLevel, MaxCapacity). Les invites : leur argent de premiere venue suit le meme
    // facteur (Wallet.FirstVisit -> MoneyFactor).
    public static class Difficulty
    {
        static bool pending, done; static float at;

        public static void OnLevelLoaded()
        {
            // (au chargement de GAME : le joueur n'est pas encore en jeu, Update attend ; Flow.StartedNew consomme ici)
            pending = Game.LevelName == "GAME" && Flow.StartedNew;
            if (Game.LevelName == "GAME") Flow.StartedNew = false;
            at = Time.realtimeSinceStartup + 6f; done = false;
            if (pending) Log.Info("difficulte : nouvelle partie, niveau " + Level + " applique d'ici quelques secondes");
        }

        public static int Level { get { return Mathf.Clamp(Config.GetInt("Coop", "Difficulte", 0), 0, 4); } }

        // Facteur sur l'argent (0 : le jeu). Au hasard : 0,1 a 2, tire du nom de la partie (world) -- identique chez tous.
        public static float MoneyFactor(string world)
        {
            switch (Level) { case 1: return 2f; case 2: return 0.5f; case 3: return 0.1f; case 4: return 0.1f + 1.9f * Hash01(world, 1); }
            return 1f;
        }
        static float FuelFactor(string world)
        {
            switch (Level) { case 1: return 1f; case 2: return 0.5f; case 3: return 0.15f; case 4: return 0.1f + 0.9f * Hash01(world, 2); }
            return -1f;
        }
        static float Hash01(string s, int salt)
        {
            uint h = 2166136261u;
            foreach (char c in (s ?? "") + "#" + salt) { h ^= c; h *= 16777619u; }
            return (h % 10000u) / 10000f;
        }

        public static void Update()
        {
            if (!pending || done || Time.realtimeSinceStartup < at || !PlayerSync.InGame) return;
            if (!Session.Active || !Session.IsHost) { pending = false; return; }
            done = true; pending = false;
            int lvl = Level;
            if (lvl == 0) return;
            string world = Wallet.WorldId;
            float money = MoneyFactor(world), fuel = FuelFactor(world);
            var sb = new System.Text.StringBuilder("difficulte : niveau " + lvl + " (argent x" + money.ToString("F2") + ", reservoirs " + Mathf.RoundToInt(fuel * 100) + " %) :");
            FsmFloat cash = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney"), bank = FsmVariables.GlobalVariables.FindFsmFloat("PlayerBankAccount");
            if (cash != null) { cash.Value = Mathf.Round(cash.Value * money); sb.Append(" liquide ").Append(cash.Value); }
            if (bank != null) { bank.Value = Mathf.Round(bank.Value * money); sb.Append(", banque ").Append(bank.Value); }
            int tanks = 0;
            foreach (PlayMakerFSM f in Object.FindObjectsOfType<PlayMakerFSM>())
            {
                if (f == null || f.FsmName != "Data" || !f.gameObject.name.StartsWith("FuelTank")) continue;
                FsmFloat lv = f.FsmVariables.FindFsmFloat("FuelLevel"), mx = f.FsmVariables.FindFsmFloat("MaxCapacity");
                if (lv == null || mx == null || mx.Value <= 0f) continue;
                lv.Value = Mathf.Round(mx.Value * fuel); tanks++;
                sb.Append(", ").Append(f.gameObject.name).Append(' ').Append(lv.Value).Append('/').Append(mx.Value);
            }
            sb.Append(" (").Append(tanks).Append(" reservoir(s))");
            Log.Info(sb.ToString());
            Hud.Toast(Lang.T("Difficulté de départ : ", "Start difficulty: ") + Name(lvl));
        }

        public static string Name(int lvl)
        {
            switch (lvl) { case 1: return Lang.T("facile", "easy"); case 2: return Lang.T("moyenne", "medium"); case 3: return Lang.T("difficile", "hard"); case 4: return Lang.T("au hasard", "random"); }
            return Lang.T("le jeu", "the game");
        }
    }
}
