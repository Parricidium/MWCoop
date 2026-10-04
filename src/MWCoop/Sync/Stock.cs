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
    public static class Stock
    {
        class Store
        {
            public string Key; public Hashtable Stocked, Carried;
            public Dictionary<string, int> SentStocked = new Dictionary<string, int>(), SentCarried = new Dictionary<string, int>();
            public Dictionary<int, Dictionary<string, int>> Remote = new Dictionary<int, Dictionary<string, int>>();   // paniers des autres
            public Dictionary<string, List<Transform>> Shelves = new Dictionary<string, List<Transform>>();          // produit -> listes d'objets
            public HashSet<string> Touched = new HashSet<string>();                                                   // etageres a recalculer
        }
        static readonly Dictionary<string, Store> stores = new Dictionary<string, Store>();
        static float nextScan = -1, nextCheck;

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
                Hashtable carried = null;
                foreach (PlayMakerHashTableProxy c in h.GetComponents<PlayMakerHashTableProxy>()) if (c.referenceName == "Carried") carried = c._hashTable;
                if (h._hashTable == null || carried == null) continue;
                var s = new Store { Key = key, Stocked = h._hashTable, Carried = carried };
                foreach (DictionaryEntry e in s.Stocked) s.SentStocked[e.Key.ToString()] = ToInt(e.Value);
                foreach (DictionaryEntry e in s.Carried) s.SentCarried[e.Key.ToString()] = ToInt(e.Value);
                stores[key] = s;
            }
            // Etageres : automates 'Buy' (Name = produit, ProductList = objets visibles, Inventory = magasin).
            int shelves = 0;
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.hideFlags != HideFlags.None || f.FsmName != "Buy") continue;
                FsmString name = f.FsmVariables.FindFsmString("Name");
                FsmGameObject list = f.FsmVariables.FindFsmGameObject("ProductList"), inv = f.FsmVariables.FindFsmGameObject("Inventory");
                if (name == null || list == null || inv == null || list.Value == null || inv.Value == null) continue;
                Store s;
                if (!stores.TryGetValue(Recon.Path(inv.Value.transform), out s)) continue;
                List<Transform> l;
                if (!s.Shelves.TryGetValue(name.Value, out l)) s.Shelves[name.Value] = l = new List<Transform>();
                if (!l.Contains(list.Value.transform)) { l.Add(list.Value.transform); shelves++; }
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
            foreach (Store s in stores.Values)
            {
                // Panier prete au rejeu de l'achat d'un autre (Shop) : ce n'est pas celui du joueur local, ni a
                // envoyer (les rayons des autres se videraient une 2e fois) ni a deduire des etageres ici ; le
                // vrai panier revient a la fin du rejeu.
                bool borrowed = Shop.Borrowed(s.Carried);
                // Ce qui a change ici (panier du joueur local, stock) depuis le dernier envoi.
                var changed = new List<string>();
                foreach (DictionaryEntry e in s.Stocked) { string k = e.Key.ToString(); int v = ToInt(e.Value), old; if (!s.SentStocked.TryGetValue(k, out old) || old != v) { s.SentStocked[k] = v; if (!changed.Contains(k)) changed.Add(k); } }
                if (!borrowed)
                    foreach (DictionaryEntry e in s.Carried) { string k = e.Key.ToString(); int v = ToInt(e.Value), old; if (!s.SentCarried.TryGetValue(k, out old) || old != v) { s.SentCarried[k] = v; if (!changed.Contains(k)) changed.Add(k); } }
                if (changed.Count > 0 && Session.RemoteCount > 0)
                {
                    var w = new NetWriter(Msg.Stock).U8(Session.LocalId).Str(s.Key).U8(Mathf.Min(changed.Count, 255));
                    for (int i = 0; i < changed.Count && i < 255; i++)
                        w.Str(changed[i]).I32(s.SentStocked.ContainsKey(changed[i]) ? s.SentStocked[changed[i]] : -1).I32(s.SentCarried.ContainsKey(changed[i]) ? s.SentCarried[changed[i]] : 0);
                    Session.SendAll(w, true);
                }
                // Produit que d'autres ont aussi dans leur panier : l'etagere se recalcule.
                foreach (string k in changed)
                    foreach (Dictionary<string, int> rr in s.Remote.Values) { int c; if (rr.TryGetValue(k, out c) && c > 0) { s.Touched.Add(k); break; } }
                if (borrowed) continue;   // etageres recalculees quand le panier local est revenu
                foreach (string it in s.Touched) Refresh(s, it);
                s.Touched.Clear();
            }
        }

        // Etagere d'un produit : objets '1'..'N' visibles, N = stock - panier local - paniers des autres.
        static void Refresh(Store s, string item)
        {
            List<Transform> lists;
            if (!s.Shelves.TryGetValue(item, out lists)) return;
            int visible = ToInt(s.Stocked[item]) - ToInt(s.Carried[item]);
            foreach (Dictionary<string, int> r in s.Remote.Values) { int c; if (r.TryGetValue(item, out c)) visible -= c; }
            foreach (Transform l in lists)
            {
                if (l == null) continue;
                foreach (Transform c in l)
                {
                    int n;
                    if (!int.TryParse(c.name, out n)) continue;
                    bool on = n <= visible;
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
                if (s == null) continue;
                if (stocked >= 0 && s.Stocked.ContainsKey(item) && ToInt(s.Stocked[item]) != stocked) { s.Stocked[item] = stocked; s.SentStocked[item] = stocked; }
                Dictionary<string, int> rc;
                if (!s.Remote.TryGetValue(who, out rc)) s.Remote[who] = rc = new Dictionary<string, int>();
                rc[item] = carried;
                s.Touched.Add(item);
            }
            if (relay != null) Session.Broadcast(relay, true, who);
        }

        public static string State(string item)
        {
            foreach (Store s in stores.Values)
                if (s.Stocked.ContainsKey(item))
                {
                    int remote = 0;
                    foreach (Dictionary<string, int> r in s.Remote.Values) { int c; if (r.TryGetValue(item, out c)) remote += c; }
                    int vis = 0;
                    List<Transform> lists;
                    if (s.Shelves.TryGetValue(item, out lists)) foreach (Transform l in lists) if (l != null) foreach (Transform c in l) if (c.gameObject.activeSelf) vis++;
                    return s.Key + " " + item + " : stock " + s.Stocked[item] + ", panier " + s.Carried[item] + ", paniers des autres " + remote + ", visibles " + vis;
                }
            return "?";
        }

        // Essais : le joueur local met 'count' articles 'item' dans son panier (comme en rayon).
        public static string TestCarry(string item, int count)
        {
            foreach (Store s in stores.Values)
                if (s.Carried.ContainsKey(item)) { s.Carried[item] = ToInt(s.Carried[item]) + count; s.Touched.Add(item); return State(item); }
            return "?";
        }
    }
}
