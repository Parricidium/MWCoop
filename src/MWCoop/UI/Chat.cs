using MWCoop.Net;

namespace MWCoop
{
    // Messages entre joueurs ; l'hote relaie. Saisie : T (ligne en bas) ou onglet TCHAT du menu F10.
    public static class Chat
    {
        public static readonly System.Collections.Generic.List<string> History = new System.Collections.Generic.List<string>();

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
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Chat).U8(id).Str(text), true, id);
        }
    }
}
