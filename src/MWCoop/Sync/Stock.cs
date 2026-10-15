using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Rayons des magasins. Chaque magasin tient son stock dans des tables (objet INVENTORY... : 'Stocked'
    // = en rayon, 'Carried' = dans le panier du joueur local) ; chaque produit a un automate 'Buy' (nom du
    // produit, liste des objets visibles '1'..'N' sur l'etagere). Chacun envoie, quand ils changent :
    //  - son panier (ce qu'il a pris en rayon et pas encore paye) ;
    //  - le stock d'un produit, quand il change chez lui (achat paye, reassort).
    // Chez les autres : le stock recu est recopie, et les articles dans le panier des autres sont
    // retires de l'etagere -- on voit le rayon se vider en direct, et se remplir s'il les repose.
    // Pendant le rejeu d'un achat (Shop pose le panier de l'acheteur dans 'Carried' le temps de 'Spawn bag'),
    // le panier n'est ni envoye ni deduit des etageres : il n'est pas celui du joueur local.
    // Les tables sont relues dans leur proxy a chaque passage : 'Reset purchase' (HashTableRevertSnapShot) met
    // une table NEUVE dans le proxy a chaque passage en caisse -- une table gardee depuis le releve restait figee
    // sur le panier deja paye (rayons des autres toujours vides, panier suivant jamais envoye).
    // Rayon vu par le jeu (13/10) : 'Stocked' ne change jamais (c'est la quantite livree, NumberOfProducts) ; ce qui
    // compte est 'Bought' -- pris en rayon depuis le reassort, panier compris, paye ou non. L'automate 'Buy' cache
    // l'objet numero Bought+1 a chaque article pris (« 1 » = haut de la premiere pile, puis 2, 3...), le remontre au
    // depot, et le reassort revient a la copie de 'Bought' (vide). Avant, le mod montrait 1..(Stocked - paniers) : il
    // cachait les numeros HAUTS d'abord (le bas de la 2e pile : caisses de biere qui flottent) et oubliait ce qui etait
    // deja paye (retour de CBetoy, 13/10 : caisses differentes chez l'un et l'autre). Maintenant 'Bought' est commun :
    // un invite envoie a l'hote ce qu'il a pris ou repose (ecart), l'hote l'ajoute et renvoie la valeur a tous (aussi
    // toutes les 60 s, en entier) ; le rayon montre les numeros au-dessus de Bought, comme le jeu, et la variable
    // Quantity des automates 'Buy' (relue seulement a la prise) est tenue a jour pour le depot.
    public static class Stock
    {
        class Store
        {
            public string Key; public GameObject Inventory; public PlayMakerHashTableProxy StockedProxy, CarriedProxy, BoughtProxy;
            public Hashtable Stocked { get { return StockedProxy != null ? StockedProxy._hashTable : null; } }
            public Hashtable Carried { get { return CarriedProxy != null ? CarriedProxy._hashTable : null; } }
            public Hashtable Bought { get { return BoughtProxy != null ? BoughtProxy._hashTable : null; } }
            public Dictionary<string, int> SentBought = new Dictionary<string, int>();
            public Dictionary<string, List<PlayMakerFSM>> Buys = new Dictionary<string, List<PlayMakerFSM>>();   // produit -> automates 'Buy'
            public Dictionary<string, int> SentStocked = new Dictionary<string, int>(), SentCarried = new Dictionary<string, int>();
            public Dictionary<int, Dictionary<string, int>> Remote = new Dictionary<int, Dictionary<string, int>>();   // paniers des autres
            public Dictionary<string, List<Transform>> Shelves = new Dictionary<string, List<Transform>>();          // produit -> listes d'objets
            public HashSet<string> Touched = new HashSet<string>();                                                   // etageres a recalculer
        }
        static readonly Dictionary<string, Store> stores = new Dictionary<string, Store>();
        static float nextScan = -1, nextCheck, nextFull;
        const byte BoughtDelta = 1, BoughtAbs = 2;

        public static void OnLevelLoaded()
        {
            stores.Clear();
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 18f : -1;
        }

        public static void PlayerLeft(int id)
        {
            foreach (Store s in stores.Values)
                if (s.Remote.ContainsKey(id)) { foreach (string it in s.Remote[id].Keys) s.Touched.Add(it); s.Remote.Remove(id); }
        }

        static void Scan()
        {
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerHashTableProxy)))
            {
                var h = (PlayMakerHashTableProxy)o;
                if (h.hideFlags != HideFlags.None || h.referenceName != "Stocked" || !h.transform.root.gameObject.activeInHierarchy) continue;
                string key = Recon.Path(h.transform);
                if (stores.ContainsKey(key)) continue;
                PlayMakerHashTableProxy carried = null, bought = null;
                foreach (PlayMakerHashTableProxy c in h.GetComponents<PlayMakerHashTableProxy>()) { if (c.referenceName == "Carried") carried = c; else if (c.referenceName == "Bought") bought = c; }
                if (h._hashTable == null || carried == null || carried._hashTable == null) continue;
                var s = new Store { Key = key, Inventory = h.gameObject, StockedProxy = h, CarriedProxy = carried, BoughtProxy = bought != null && bought._hashTable != null ? bought : null };
                foreach (DictionaryEntry e in s.Stocked) s.SentStocked[e.Key.ToString()] = ToInt(e.Value);
                foreach (DictionaryEntry e in s.Carried) s.SentCarried[e.Key.ToString()] = ToInt(e.Value);
                if (s.Bought != null) foreach (DictionaryEntry e in s.Bought) s.SentBought[e.Key.ToString()] = ToInt(e.Value);
                stores[key] = s;
            }
            // Etageres : automates 'Buy' (Name = produit, ProductList = objets visibles, Inventory = magasin).
            int shelves = 0;
            foreach (Object o in Game.FsmsNamed("Buy"))
            {
                var f = (PlayMakerFSM)o; if (f == null) continue;
                if (f.hideFlags != HideFlags.None || f.FsmName != "Buy") continue;
                FsmString name = f.FsmVariables.FindFsmString("Name");
                FsmGameObject list = f.FsmVariables.FindFsmGameObject("ProductList"), inv = f.FsmVariables.FindFsmGameObject("Inventory");
                if (name == null || list == null || inv == null || list.Value == null || inv.Value == null) continue;
                Store s;
                if (!stores.TryGetValue(Recon.Path(inv.Value.transform), out s)) continue;
                List<Transform> l;
                if (!s.Shelves.TryGetValue(name.Value, out l)) s.Shelves[name.Value] = l = new List<Transform>();
                if (!l.Contains(list.Value.transform)) { l.Add(list.Value.transform); shelves++; }
                List<PlayMakerFSM> bl;
                if (!s.Buys.TryGetValue(name.Value, out bl)) s.Buys[name.Value] = bl = new List<PlayMakerFSM>();
                if (!bl.Contains(f)) bl.Add(f);
            }
            if (stores.Count > 0) Log.Info("magasins : " + stores.Count + " stocks suivis, " + shelves + " etageres");
        }

        static int ToInt(object v) { return v is int ? (int)v : v is float ? (int)(float)v : 0; }

        public static void Update()
        {
            if (!Session.Active || nextScan < 0 || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = now + (stores.Count == 0 ? 30f : 300f); Scan(); }
            if (now < nextCheck || stores.Count == 0) return;
            nextCheck = now + 0.5f;
            bool fullRound = now >= nextFull;
            if (fullRound) nextFull = now + 60f;   // (marque AVANT l'envoi : un envoi rate ne doit pas etre retente a chaque tour)
            foreach (Store s in stores.Values)
            {
                Hashtable stocked = s.Stocked, carried = s.Carried;
                if (stocked == null || carried == null) continue;   // inventaire detruit
                // Panier prete au rejeu de l'achat d'un autre (Shop) : ce n'est pas celui du joueur local, ni a
                // envoyer (les rayons des autres se videraient une 2e fois) ni a deduire des etageres ici ; le
                // vrai panier revient a la fin du rejeu.
                bool borrowed = Shop.Borrowed(s.Inventory);
                // Ce qui a change ici (panier du joueur local, stock) depuis le dernier envoi.
                var changed = new List<string>();
                foreach (DictionaryEntry e in stocked) { string k = e.Key.ToString(); int v = ToInt(e.Value), old; if (!s.SentStocked.TryGetValue(k, out old) || old != v) { s.SentStocked[k] = v; if (!changed.Contains(k)) changed.Add(k); } }
                if (!borrowed)
                    foreach (DictionaryEntry e in carried) { string k = e.Key.ToString(); int v = ToInt(e.Value), old; if (!s.SentCarried.TryGetValue(k, out old) || old != v) { s.SentCarried[k] = v; if (!changed.Contains(k)) changed.Add(k); } }
                // Pris en rayon ici (ou repose, ou reassort) : l'ecart part a l'hote ; l'hote envoie la valeur.
                var bch = new List<KeyValuePair<string, int>>();
                Hashtable bought = s.Bought;
                if (bought != null)
                    foreach (DictionaryEntry e in bought)
                    {
                        string k = e.Key.ToString(); int v = ToInt(e.Value), old;
                        if (!s.SentBought.TryGetValue(k, out old)) old = v;
                        if (old == v) { s.SentBought[k] = v; continue; }
                        s.SentBought[k] = v;
                        s.Touched.Add(k);
                        bch.Add(new KeyValuePair<string, int>(k, Session.IsHost ? v : v - old));
                    }
                bool full = Session.IsHost && bought != null && now >= nextFull && Session.RemoteCount > 0;
                if ((changed.Count > 0 || bch.Count > 0 || full) && Session.RemoteCount > 0)
                {
                    if (full)
                    {
                        bch.Clear();
                        foreach (DictionaryEntry e in bought) bch.Add(new KeyValuePair<string, int>(e.Key.ToString(), ToInt(e.Value)));
                    }
                    SendChunks(s, changed, Session.IsHost ? BoughtAbs : BoughtDelta, bch);
                }
                // Produit que d'autres ont aussi dans leur panier : l'etagere se recalcule.
                foreach (string k in changed)
                    foreach (Dictionary<string, int> rr in s.Remote.Values) { int c; if (rr.TryGetValue(k, out c) && c > 0) { s.Touched.Add(k); break; } }
                if (borrowed) continue;   // etageres recalculees quand le panier local est revenu
                foreach (string it in s.Touched) Refresh(s, it);
                s.Touched.Clear();
            }
        }

        // Un message tient dans un paquet (Transport.MaxPayload) : les etageres changees, puis les achats, par morceaux.
        // Journaux de CBetoy (14/10) : « message fiable trop long : 1248 » deux fois par seconde pendant 80 min -- la liste
        // complete des achats du magasin depassait, l'exception sortait avant que l'envoi complet soit marque fait, et les
        // stocks ne partaient jamais aux invites.
        const int ChunkBytes = 900;
        static int Bytes(string t) { return System.Text.Encoding.UTF8.GetByteCount(t ?? ""); }
        static void SendChunks(Store s, List<string> changed, byte mode, List<KeyValuePair<string, int>> bought)
        {
            int head = 4 + Bytes(s.Key);
            for (int i = 0; i < changed.Count;)
            {
                var w = new NetWriter(Msg.Stock).U8(Session.LocalId).Str(s.Key);
                int size = head, n = 0, start = i;
                while (i < changed.Count && n < 255) { int e = 10 + Bytes(changed[i]); if (n > 0 && size + e > ChunkBytes) break; size += e; i++; n++; }
                w.U8(n);
                for (int k = start; k < i; k++) w.Str(changed[k]).I32(s.SentStocked.ContainsKey(changed[k]) ? s.SentStocked[changed[k]] : -1).I32(s.SentCarried.ContainsKey(changed[k]) ? s.SentCarried[changed[k]] : 0);
                Session.SendAll(w, true);
            }
            for (int i = 0; i < bought.Count;)
            {
                var part = new List<KeyValuePair<string, int>>();
                int size = head + 3;
                while (i < bought.Count) { int e = 6 + Bytes(bought[i].Key); if (part.Count > 0 && size + e > ChunkBytes) break; size += e; part.Add(bought[i]); i++; }
                var w = new NetWriter(Msg.Stock).U8(Session.LocalId).Str(s.Key).U8(0);
                WriteBought(w, mode, part);
                Session.SendAll(w, true);
            }
        }

        static void WriteBought(NetWriter w, byte mode, List<KeyValuePair<string, int>> l)
        {
            int n = Mathf.Min(l.Count, 65535);
            w.U8(mode).U16(n);
            for (int i = 0; i < n; i++) w.Str(l[i].Key).I32(l[i].Value);
        }

        // 'Bought' d'un produit pose ici (recu) : table, valeur deja connue (pas renvoyee), automates 'Buy', rayon.
        static void SetBought(Store s, string item, int v)
        {
            Hashtable b = s.Bought;
            if (b == null || !b.ContainsKey(item)) return;
            if (v < 0) v = 0;
            if (ToInt(b[item]) != v) b[item] = v;
            s.SentBought[item] = v;
            List<PlayMakerFSM> fs;
            if (s.Buys.TryGetValue(item, out fs))
                foreach (PlayMakerFSM f in fs)
                {
                    FsmInt q = f != null ? f.FsmVariables.FindFsmInt("Quantity") : null;
                    if (q != null) q.Value = v;
                }
            s.Touched.Add(item);
        }

        // Etagere d'un produit : objets '1'..'N' visibles, N = stock - panier local - paniers des autres.
        static void Refresh(Store s, string item)
        {
            List<Transform> lists;
            Hashtable stocked = s.Stocked, carried = s.Carried;
            if (!s.Shelves.TryGetValue(item, out lists) || stocked == null || carried == null) return;
            int visible = ToInt(stocked[item]) - ToInt(carried[item]);
            foreach (Dictionary<string, int> r in s.Remote.Values) { int c; if (r.TryGetValue(item, out c)) visible -= c; }
            Hashtable bought = s.Bought;
            int taken = bought != null && bought.ContainsKey(item) ? ToInt(bought[item]) : -1;   // (comme le jeu : 1..Bought caches)
            foreach (Transform l in lists)
            {
                if (l == null) continue;
                foreach (Transform c in l)
                {
                    int n;
                    if (!int.TryParse(c.name, out n)) continue;
                    bool on = taken >= 0 ? n > taken : n <= visible;
                    if (c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
                }
            }
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string key = r.Str();
            int n = r.U8();
            NetWriter relay = Session.IsHost ? new NetWriter(Msg.Stock).U8(who).Str(key).U8(n) : null;
            Store s;
            stores.TryGetValue(key, out s);
            for (int i = 0; i < n; i++)
            {
                string item = r.Str();
                int stocked = r.I32(), carried = r.I32();
                if (relay != null) relay.Str(item).I32(stocked).I32(carried);
                Hashtable st = s != null ? s.Stocked : null;
                if (st == null) continue;
                if (stocked >= 0 && st.ContainsKey(item) && ToInt(st[item]) != stocked) { st[item] = stocked; s.SentStocked[item] = stocked; }
                Dictionary<string, int> rc;
                if (!s.Remote.TryGetValue(who, out rc)) s.Remote[who] = rc = new Dictionary<string, int>();
                rc[item] = carried;
                s.Touched.Add(item);
            }
            if (relay != null) Session.Broadcast(relay, true, who);
            if (!r.More) return;
            // 'Bought' : ecarts d'un invite (hote : ajoutes, puis la valeur a tous, lui compris) ou valeurs de l'hote.
            int mode = r.U8();
            int m = r.U16();
            var abs = Session.IsHost ? new List<KeyValuePair<string, int>>() : null;
            for (int i = 0; i < m; i++)
            {
                string item = r.Str(); int v = r.I32();
                if (s == null || s.Bought == null || !s.Bought.ContainsKey(item)) continue;
                if (mode == BoughtDelta && Session.IsHost)
                {
                    int nv = Mathf.Max(0, ToInt(s.Bought[item]) + v);
                    SetBought(s, item, nv);
                    abs.Add(new KeyValuePair<string, int>(item, nv));
                }
                else if (mode == BoughtAbs && !Session.IsHost) SetBought(s, item, v);
            }
            if (abs != null && abs.Count > 0 && s != null) SendChunks(s, new List<string>(), BoughtAbs, abs);
        }

        public static string State(string item)
        {
            foreach (Store s in stores.Values)
                if (s.Stocked != null && s.Carried != null && s.Stocked.ContainsKey(item))
                {
                    int remote = 0;
                    foreach (Dictionary<string, int> r in s.Remote.Values) { int c; if (r.TryGetValue(item, out c)) remote += c; }
                    int vis = 0;
                    List<Transform> lists;
                    if (s.Shelves.TryGetValue(item, out lists)) foreach (Transform l in lists) if (l != null) foreach (Transform c in l) if (c.gameObject.activeSelf) vis++;
                    string on = "";
                    if (s.Shelves.TryGetValue(item, out lists)) foreach (Transform l in lists) if (l != null) foreach (Transform c in l) if (c.gameObject.activeSelf) on += c.name + " ";
                    return s.Key + " " + item + " : stock " + s.Stocked[item] + ", pris " + (s.Bought != null && s.Bought.ContainsKey(item) ? s.Bought[item].ToString() : "?") + ", panier " + s.Carried[item] + ", paniers des autres " + remote + ", visibles " + vis + " [" + on.Trim() + "]";
                }
            return "?";
        }

        // Essais : le joueur local met 'count' articles 'item' dans son panier (comme en rayon).
        public static string TestCarry(string item, int count)
        {
            foreach (Store s in stores.Values)
                if (s.Carried != null && s.Carried.ContainsKey(item))
                {
                    s.Carried[item] = ToInt(s.Carried[item]) + count;
                    if (s.Bought != null && s.Bought.ContainsKey(item)) s.Bought[item] = Mathf.Max(0, ToInt(s.Bought[item]) + count);   // (comme l'automate 'Buy')
                    s.Touched.Add(item); return State(item);
                }
            return "?";
        }
    }
}
