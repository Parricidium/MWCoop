using System.Collections.Generic;

namespace MWCoop.Net
{
    // Cles longues (chemins d'objets) envoyees en boucle par un flux non fiable (Particles, CopySounds) : l'hote donne un
    // numero a chaque cle et envoie la table une fois, en fiable (et de nouveau toute entiere a l'arrivee d'un joueur) ;
    // les mises a jour ne portent plus que le numero (2 octets au lieu de 40 a 80). Une mise a jour dont le numero n'est pas
    // encore connu ici est ignoree : le tout suivant (quelques secondes) la redonne. (Nightly 5, cles courtes.)
    public class KeyTable
    {
        readonly string kind;   // type du message de table ("@emitk", "@sonk")
        readonly Dictionary<string, int> ids = new Dictionary<string, int>();
        readonly List<string> keys = new List<string>();
        readonly List<int> pending = new List<int>();
        readonly Dictionary<int, string> recv = new Dictionary<int, string>();

        public KeyTable(string kind) { this.kind = kind; }

        public void Clear() { ids.Clear(); keys.Clear(); pending.Clear(); recv.Clear(); }

        // hote : numero de la cle (nouvelle : a envoyer)
        public int Id(string key)
        {
            int id;
            if (ids.TryGetValue(key, out id)) return id;
            id = keys.Count;
            keys.Add(key); ids[key] = id; pending.Add(id);
            return id;
        }

        // hote : toute la table de nouveau (un joueur arrive)
        public void ResendAll() { pending.Clear(); for (int i = 0; i < keys.Count; i++) pending.Add(i); }

        // hote : nouvelles cles, en fiable, par paquets de 12
        public void SendDefs()
        {
            if (pending.Count == 0 || !Session.Active || Session.RemoteCount == 0) return;
            for (int i = 0; i < pending.Count; i += 12)
            {
                int n = System.Math.Min(12, pending.Count - i);
                var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str(kind).U8(n);
                for (int k = 0; k < n; k++) w.U16(pending[i + k]).Str(keys[pending[i + k]]);
                Session.SendAll(w, true);
            }
            pending.Clear();
        }

        // invite : table recue
        public void OnDefs(NetReader r)
        {
            int n = r.U8();
            for (int i = 0; i < n; i++) { int id = r.U16(); recv[id] = r.Str(); }
        }

        public string Key(int id) { string k; return recv.TryGetValue(id, out k) ? k : null; }
    }
}
