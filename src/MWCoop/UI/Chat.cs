using MWCoop.Net;

namespace MWCoop
{
    // Messages entre joueurs ; l'hote relaie. Saisie : T (ligne en bas) ou onglet TCHAT du menu F10.
    public static class Chat
    {
        public static readonly System.Collections.Generic.List<string> History = new System.Collections.Generic.List<string>();

        // Bulle au-dessus du joueur qui vient d'ecrire (suggestion de квазарик, 22/10 ; dessinee par Hud.Bubble)
        public class Said { public string Text; public float Born, Until; }
        static readonly System.Collections.Generic.Dictionary<int, Said> said = new System.Collections.Generic.Dictionary<int, Said>();
        public static Said SaidBy(int id)
        {
            Said s;
            if (!said.TryGetValue(id, out s)) return null;
            if (UnityEngine.Time.realtimeSinceStartup > s.Until) { said.Remove(id); return null; }
            return s;
        }
        public static void Say(int id, string text)
        {
            if (text.Length == 0) return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            said[id] = new Said { Text = text, Born = now, Until = now + UnityEngine.Mathf.Clamp(3.5f + text.Length * 0.06f, 4f, 10f) };
        }

        static void Add(string line)
        {
            Hud.Toast(line);
            History.Add(line);
            if (History.Count > 30) History.RemoveAt(0);
        }

        // Ligne du systeme (triches, aide) : dans l'historique et en bulle, pas envoyee.
        public static void System(string line) { Add(line); }

        public static void Send(string text)
        {
            text = Session.Clean(text, 200);
            if (text.StartsWith("/") && Kauppa.ChatCommand(text)) return;   // (/titre : cassette en main)
            if (text.StartsWith("/")) { Cheats.Command(text); return; }   // (commandes : Cheats)
            if (text.Length == 0 || !Session.Active) return;
            Add(Session.Me.Name + " : " + text);
            Session.SendAll(new NetWriter(Msg.Chat).U8(Session.LocalId).Str(text), true);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int id = r.U8();
            if (Session.IsHost) id = from.Id;
            string text = Session.Clean(r.Str(), 200);
            PlayerInfo pi;
            string name = Session.Players.TryGetValue(id, out pi) ? pi.Name : "?";
            Add(name + " : " + text);
            Say(id, text);
            if (Config.Testing && Config.GetInt("Test", "BullePhoto", 0) != 0) Autotest.CaptureSoon("bulle-" + text.Length, 1.2f);
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Chat).U8(id).Str(text), true, id);
        }
    }
}
