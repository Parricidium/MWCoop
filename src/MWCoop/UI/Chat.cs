using MWCoop.Net;

namespace MWCoop
{
    // Messages entre joueurs ; l'hote relaie. La saisie au clavier viendra avec le menu F10.
    public static class Chat
    {
        public static void Send(string text)
        {
            text = Session.Clean(text, 200);
            if (text.Length == 0 || !Session.Active) return;
            Hud.Toast(Session.Me.Name + " : " + text);
            Session.SendAll(new NetWriter(Msg.Chat).U8(Session.LocalId).Str(text), true);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int id = r.U8();
            if (Session.IsHost) id = from.Id;
            string text = Session.Clean(r.Str(), 200);
            PlayerInfo pi;
            string name = Session.Players.TryGetValue(id, out pi) ? pi.Name : "?";
            Hud.Toast(name + " : " + text);
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Chat).U8(id).Str(text), true, id);
        }
    }
}
