using MWCoop.Net;

namespace MWCoop
{
    // Etat du monde dicte par l'hote (heure, meteo...). A venir.
    public static class World
    {
        public static void OnMessage(Msg type, Peer from, NetReader r)
        {
            Log.Warn("message non gere : " + type);
        }
    }
}
