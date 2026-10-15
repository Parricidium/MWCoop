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

        // Toutes les tables (Particles, CopySounds) : les tables recues sont oubliees a la deconnexion (Session),
        // sinon un numero de l'hote precedent designait ici un autre son ou emetteur (relecture du 15/10, 3.1).
        static readonly List<KeyTable> all = new List<KeyTable>();

        public KeyTable(string kind) { this.kind = kind; all.Add(this); }

        public void Clear() { ids.Clear(); keys.Clear(); pending.Clear(); recv.Clear(); }

        // invite : tables numero -> cle recues de l'hote, toutes oubliees (deconnexion)
        public static void ClearReceived() { foreach (KeyTable t in all) t.recv.Clear(); }

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

        // hote : nouvelles cles, en fiable, par paquets d'au plus ~1000 octets (relecture du 15/10, #11 : 12 cles de
        // ~95 octets depassaient le paquet, le message fiable etait refuse et les cles restaient en attente pour de bon)
        public void SendDefs()
        {
            if (pending.Count == 0 || !Session.Active || Session.RemoteCount == 0) return;
            while (pending.Count > 0)
            {
                // Combien de cles tiennent : en-tete (type, joueur, kind, compte) puis numero + longueur + UTF-8 par cle.
                int n = 0, bytes = 12 + kind.Length;
                while (n < pending.Count && n < 255)
                {
                    int b = 4 + System.Text.Encoding.UTF8.GetByteCount(keys[pending[n]]);
                    if (n > 0 && bytes + b > 1000) break;
                    bytes += b; n++;
                }
                var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str(kind).U8(n);
                for (int k = 0; k < n; k++) w.U16(pending[k]).Str(keys[pending[k]]);
                pending.RemoveRange(0, n);   // (avant l'envoi : une cle trop longue a elle seule ne bloque pas les autres)
                Session.SendAll(w, true);
            }
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
