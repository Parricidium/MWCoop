using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Compatibilite du mod « Jonne Food System » (suggestion #29 : ᏒullΞᏒ, zov larper). Ses aliments (boulangerie, buffet,
    // distributeurs, micro-ondes, pizzas, macaronis, conserves, caisses de boissons...) sont des objets qu'il cree lui-meme,
    // chacun porte un composant VendingItemData (liste VendingItemData.AllItems) : ils n'existaient que chez celui qui les
    // achetait. Ici :
    //  - chaque objet recoit une cle (w:jfs:...) et Props suit ses deplacements ;
    //  - un objet ne ici part aux autres (@jfs 0) ; ils le recreent avec le chargement du mod lui-meme : sa sauvegarde
    //    (JonneSaveManager.Mod_OnSave, le temps d'un appel sur cet objet seul) donne son etat (VendingItemSaveInfo : etat de
    //    la pizza, parts restantes, ouvert, vide, contenu du sac...), ApplySave le recree ailleurs (le fichier du mod est
    //    remis tel quel apres). Sans ItemId (canettes et friandises des distributeurs, que le mod ne sauve pas) : le meme
    //    modele (reconnu a son maillage) clone et prepare comme le distributeur le fait (@jfs 6) ;
    //  - change d'etat (ouvert, entame, vide, cuit, perime...) : recree chez les autres avec son nouvel etat (@jfs 2 / 7) ;
    //  - detruit (mange, transforme) : retire chez les autres (@jfs 1).
    // Ce qui arrive tout seul (cuisson au four ou au micro-ondes, peremption, rayons regarnis) se passe chez chacun : seul
    // l'hote l'envoie ; un invite ne l'envoie que dans les 9 s d'un geste a lui (clic, F, E) et laisse sinon l'hote decider (ce
    // qu'il cree tout seul est remplace par l'objet de l'hote, ou retire s'il n'en vient pas). A l'arrivee, l'invite (qui
    // a recu la sauvegarde du mod avec celle de l'hote) apparie ses objets a ceux de l'hote (meme ID, meme place).
    public static class ModJonne
    {
        const BindingFlags A = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        static bool looked, present;
        static Assembly asm;
        static Type tItem, tInfo, tData;
        static object allItems, saveSys;
        static MethodInfo hsAdd, hsClear, mOnSave, mApply, mLoad, mSave, mSanitize;
        static PropertyInfo pPath;
        static FieldInfo fItemId, fItemName, fIsDrink, fIsFood, fItems, fStock;

        class Ent { public string Key; public Component C; public List<Component> Alts; public string Sig; public float Quiet; }
        static readonly Dictionary<string, Ent> byKey = new Dictionary<string, Ent>();
        static readonly Dictionary<int, Ent> byId = new Dictionary<int, Ent>();
        // Objets pas (encore) suivis : -1 = la depuis le chargement (invite, en attente du releve de l'hote) ; sinon heure de
        // naissance (invite : ne ici sans geste, en attente de l'objet de l'hote).
        static readonly Dictionary<Component, float> loose = new Dictionary<Component, float>();
        static bool started, rosterIn;
        static float startAt, nextScan, nextSig, lastAction = -99f, rosterAsk;
        static int seq;
        static readonly string session = new System.Random().Next(0x1000, 0xffff).ToString("x");
        public static bool TestAct;

        public static void OnLevelLoaded()
        {
            byKey.Clear(); byId.Clear(); loose.Clear(); catalog = null;
            started = rosterIn = false; seq = 0;
            startAt = Time.realtimeSinceStartup + 25f;   // (apres le chargement du mod : ApplySave de sa sauvegarde)
            rosterAsk = 0f;
        }

        static bool Present()
        {
            if (looked) return present;
            looked = true;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = a.GetType("Vending_Machines.ExpandedShop.VendingItemData", false); } catch { }
                if (t != null) { asm = a; tItem = t; break; }
            }
            if (asm == null) return false;
            try
            {
                tInfo = asm.GetType("Vending_Machines.VendingItemSaveInfo");
                tData = asm.GetType("Vending_Machines.VendingSaveData");
                Type sm = asm.GetType("Vending_Machines.JonneSaveManager");
                allItems = tItem.GetField("AllItems", A).GetValue(null);
                hsAdd = allItems.GetType().GetMethod("Add"); hsClear = allItems.GetType().GetMethod("Clear");
                mOnSave = sm.GetMethod("Mod_OnSave", A); mApply = sm.GetMethod("ApplySave", A); pPath = sm.GetProperty("SavePath", A);
                saveSys = sm.GetField("saveSystem", A).GetValue(null);
                mSave = saveSys.GetType().GetMethod("Save"); mLoad = saveSys.GetType().GetMethod("Load").MakeGenericMethod(tData);
                Type san = asm.GetType("Vending_Machines.Utils.ItemNameSanitizer");
                mSanitize = san != null ? san.GetMethod("SanitizeGameObject", A) : null;
                fItemId = tItem.GetField("ItemId"); fItemName = tItem.GetField("ItemName"); fIsDrink = tItem.GetField("IsDrink"); fIsFood = tItem.GetField("IsFood");
                fItems = tData.GetField("items"); fStock = tData.GetField("BakeryStock");
                present = tInfo != null && hsAdd != null && hsClear != null && mOnSave != null && mApply != null && pPath != null && mSave != null && mLoad != null && fItemId != null && fItems != null;
            }
            catch (Exception e) { Log.Warn("mods : Jonne Food System, " + e.Message); present = false; }
            Log.Info("mods : Jonne Food System " + (present ? "present, ses aliments partages" : "present mais pas reconnu (version ?) : pas partage"));
            return present;
        }

        static List<Component> Items()
        {
            var l = new List<Component>();
            foreach (object o in (IEnumerable)allItems) { Component c = o as Component; if (c != null) l.Add(c); }
            return l;
        }
        static string Id(Component c) { return (fItemId.GetValue(c) as string) ?? ""; }
        static string Name(Component c) { string id = Id(c); return id.Length > 0 ? id : c.gameObject.name; }
        static bool Acting(float now) { return TestAct || now - lastAction < 9f; }   // (ouvre-boite : 7,6 s, buffet 3,5 s, monnayeur 3 s)
        static bool MaySend(float now) { return Session.IsHost || Acting(now); }
        static string NewKey() { return "w:jfs:" + Session.LocalId + ":" + session + ":" + (++seq); }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame || !Present()) return;
            float now = Time.realtimeSinceStartup;
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.E)) lastAction = now;
            if (now < startAt) return;
            if (!started)
            {
                started = true;
                List<Component> all = Items();
                foreach (Component c in all) { if (Session.IsHost) Track(c, NewKey(), now); else loose[c] = -1f; }
                Log.Info("mods : Jonne Food System, " + all.Count + " objet(s) ici au depart" + (Session.IsHost ? ", suivis" : ", en attente du releve de l'hote"));
            }
            if (!Session.IsHost && !rosterIn && now >= rosterAsk) { rosterAsk = now + 10f; Head(4).Send(); }
            if (now >= nextScan) { nextScan = now + 0.25f; Scan(now); }
        }

        static void Track(Component c, string key, float now)
        {
            Ent e;
            if (!byKey.TryGetValue(key, out e)) { e = new Ent { Key = key }; byKey[key] = e; }
            e.C = c; e.Sig = Sig(c); e.Quiet = now + 1f;
            byId[c.GetInstanceID()] = e;
            loose.Remove(c);
            Adopt(c, key);
        }
        static void Adopt(Component c, string key) { if (c.GetComponent<Rigidbody>() != null) Props.Adopt(c.gameObject, key); }

        static void Scan(float now)
        {
            foreach (Component c in Items())
            {
                if (byId.ContainsKey(c.GetInstanceID()) || loose.ContainsKey(c)) continue;
                if (MaySend(now))
                {
                    string k = NewKey();
                    Track(c, k, now);
                    Send(byKey[k], false);
                }
                else loose[c] = now;
            }
            List<Ent> gone = null;
            foreach (Ent e in byKey.Values)
            {
                if (e.C != null) continue;
                Component alt = e.Alts != null ? e.Alts.Find(x => x != null) : null;
                if (alt != null) { e.C = alt; e.Alts = null; e.Sig = Sig(alt); e.Quiet = now + 1.5f; byId[alt.GetInstanceID()] = e; Adopt(alt, e.Key); continue; }
                if (gone == null) gone = new List<Ent>();
                gone.Add(e);
            }
            if (gone != null)
                foreach (Ent e in gone)
                {
                    Forget(e);
                    if (MaySend(now) && Session.RemoteCount > 0) { Head(1).Str(e.Key).Send(); Log.Info("mods : Jonne Food System, " + e.Key + " disparu ici, envoye"); }
                }
            if (now >= nextSig)
            {
                nextSig = now + 1f;
                foreach (Ent e in byKey.Values)
                {
                    if (e.C == null) continue;
                    string s = Sig(e.C);
                    if (s == e.Sig) continue;
                    e.Sig = s;
                    if (now >= e.Quiet && MaySend(now)) Send(e, true);
                }
            }
            if (!Session.IsHost && rosterIn && loose.Count > 0)
            {
                List<Component> drop = null;
                foreach (KeyValuePair<Component, float> kv in loose)
                    if (kv.Key == null || kv.Value > 0f && now - kv.Value > 6f) { if (drop == null) drop = new List<Component>(); drop.Add(kv.Key); }
                if (drop != null)
                    foreach (Component c in drop)
                    {
                        loose.Remove(c);
                        if (c == null) continue;
                        Log.Info("mods : Jonne Food System, " + Name(c) + " apparu tout seul ici sans equivalent chez l'hote : retire");
                        UnityEngine.Object.Destroy(c.gameObject);
                    }
            }
        }

        static void Forget(Ent e)
        {
            byKey.Remove(e.Key);
            var ids = new List<int>();
            foreach (KeyValuePair<int, Ent> kv in byId) if (kv.Value == e) ids.Add(kv.Key);
            foreach (int i in ids) byId.Remove(i);
        }

        // Etat qui compte (pas les minuteries) : drapeaux de l'objet, et ce qui se compte (parts, articles restants, etat).
        static readonly string[] SigFields = { "Empty", "IsSpoiled", "IsBroken", "IsBought", "Stage", "itemsRemaining", "Count", "slicesRemaining",
                                               "usesRemaining", "IsOpened", "isOpened", "IsEmpty", "isLidOpen", "State", "Consumed", "isBroken", "isBought" };
        static readonly Dictionary<Type, List<FieldInfo>> sigCache = new Dictionary<Type, List<FieldInfo>>();
        static string Sig(Component c)
        {
            var sb = new System.Text.StringBuilder();
            foreach (MonoBehaviour m in c.GetComponents<MonoBehaviour>())
            {
                if (m == null || m.GetType().Assembly != asm) continue;
                List<FieldInfo> fl;
                if (!sigCache.TryGetValue(m.GetType(), out fl))
                {
                    fl = new List<FieldInfo>();
                    foreach (string n in SigFields) { FieldInfo f = m.GetType().GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); if (f != null) fl.Add(f); }
                    sigCache[m.GetType()] = fl;
                }
                foreach (FieldInfo f in fl) { object v = f.GetValue(m); sb.Append(v is IList ? ((IList)v).Count.ToString() : "" + v).Append(','); }
                FieldInfo bag = m.GetType().GetField("contents", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (bag != null && bag.GetValue(m) is IList) sb.Append(((IList)bag.GetValue(m)).Count).Append(',');
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- envoi
        static NetWriter Head(int kind) { return new NetWriter(Msg.Job).U8(Session.LocalId).Str("@jfs").U8(kind); }
        static void Send(this NetWriter w) { Session.SendAll(w, true); }

        static void Send(Ent e, bool state)
        {
            if (Session.RemoteCount == 0 || e.C == null) return;
            Component c = e.C;
            object info = Id(c).Length > 0 ? Serialize(c) : null;
            if (info != null)
            {
                NetWriter w = Head(state ? 2 : 0).Str(e.Key);
                WriteObj(w, info, tInfo);
                w.Send();
                Log.Info("mods : Jonne Food System, " + Name(c) + (state ? " change ici" : " apparu ici") + ", envoye (" + e.Key + ")");
                return;
            }
            GameObject pf = PrefabOf(c.gameObject);
            if (pf == null) { Log.Warn("mods : Jonne Food System, " + Name(c) + " : modele introuvable, pas partage"); return; }
            NetWriter cw = Head(state ? 7 : 6).Str(e.Key).Str(pf.name).Str(c.gameObject.name).Str((fItemName.GetValue(c) as string) ?? "")
                .Bool(fIsDrink != null && (bool)fIsDrink.GetValue(c)).Bool(fIsFood != null && (bool)fIsFood.GetValue(c))
                .Vec(c.transform.position).Quat(c.transform.rotation);
            MonoBehaviour beh = Behaviour(c.gameObject);
            cw.Str(beh != null ? beh.GetType().FullName : "");
            if (beh != null) WriteFlat(cw, beh);
            cw.Send();
            Log.Info("mods : Jonne Food System, " + c.gameObject.name + " (" + pf.name + ")" + (state ? " change ici" : " apparu ici") + ", envoye (" + e.Key + ")");
        }

        // Etat de sauvegarde du mod pour cet objet seul : Mod_OnSave sur une liste reduite a lui, fichier du mod remis apres.
        static object Serialize(Component c)
        {
            string path = (string)pPath.GetValue(null, null);
            byte[] backup = File.Exists(path) ? File.ReadAllBytes(path) : null;
            List<Component> saved = Items();
            try
            {
                hsClear.Invoke(allItems, null);
                hsAdd.Invoke(allItems, new object[] { c });
                mOnSave.Invoke(null, null);
                object data = mLoad.Invoke(saveSys, new object[] { path });
                Array arr = data != null ? fItems.GetValue(data) as Array : null;
                return arr != null && arr.Length > 0 ? arr.GetValue(0) : null;
            }
            catch (Exception e) { Log.Warn("mods : Jonne Food System, etat de " + Name(c) + " : " + (e.InnerException ?? e).Message); return null; }
            finally
            {
                hsClear.Invoke(allItems, null);
                foreach (Component s in saved) if (s != null) hsAdd.Invoke(allItems, new object[] { s });
                Restore(path, backup);
            }
        }

        static void Restore(string path, byte[] backup)
        {
            try { if (backup != null) File.WriteAllBytes(path, backup); else if (File.Exists(path)) File.Delete(path); }
            catch (Exception e) { Log.Warn("mods : Jonne Food System, fichier du mod : " + e.Message); }
        }

        // Recree un objet depuis son etat (ApplySave du mod sur une sauvegarde d'un seul objet ; les autres objets a l'abri).
        static List<Component> Create(object info)
        {
            string path = (string)pPath.GetValue(null, null);
            byte[] backup = File.Exists(path) ? File.ReadAllBytes(path) : null;
            List<Component> saved = Items();
            var made = new List<Component>();
            try
            {
                object data = Activator.CreateInstance(tData);
                Array arr = Array.CreateInstance(tInfo, 1); arr.SetValue(info, 0);
                fItems.SetValue(data, arr);
                if (fStock != null) fStock.SetValue(data, null);
                mSave.Invoke(saveSys, new object[] { path, data });
                hsClear.Invoke(allItems, null);
                mApply.Invoke(null, null);
                made = Items();
            }
            catch (Exception e) { Log.Warn("mods : Jonne Food System, creation : " + (e.InnerException ?? e).Message); }
            finally
            {
                foreach (Component s in saved) if (s != null) hsAdd.Invoke(allItems, new object[] { s });
                Restore(path, backup);
            }
            return made;
        }

        // ---------------------------------------------------------------- objets sans ID (distributeurs)
        static Dictionary<string, GameObject> catalog;
        static Dictionary<Mesh, GameObject> byMesh;
        static void BuildCatalog()
        {
            catalog = new Dictionary<string, GameObject>(); byMesh = new Dictionary<Mesh, GameObject>();
            var seen = new HashSet<object>();
            foreach (MonoBehaviour m in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                if (m != null && m.GetType().Assembly == asm) Collect(m, m.GetType(), seen);
            foreach (Type t in asm.GetTypes()) { try { Collect(null, t, seen); } catch { } }
        }
        static void Collect(object o, Type t, HashSet<object> seen)
        {
            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | (o == null ? BindingFlags.Static : BindingFlags.Instance)))
            {
                if (f.FieldType.IsGenericTypeDefinition || t.ContainsGenericParameters) continue;
                object v; try { v = f.GetValue(o); } catch { continue; }
                if (v is GameObject) AddPrefab((GameObject)v);
                else if (v is IEnumerable && !(v is string) && seen.Add(v)) foreach (object x in (IEnumerable)v) if (x is GameObject) AddPrefab((GameObject)x);
            }
        }
        static void AddPrefab(GameObject g)
        {
            if (g == null || g.activeInHierarchy) return;   // (modeles : hors scene)
            if (!catalog.ContainsKey(g.name)) catalog[g.name] = g;
            Mesh m = MeshOf(g);
            if (m != null && !byMesh.ContainsKey(m)) byMesh[m] = g;
        }
        static Mesh MeshOf(GameObject g)
        {
            MeshFilter mf = g.GetComponent<MeshFilter>() ?? g.GetComponentInChildren<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }
        static GameObject PrefabOf(GameObject g)
        {
            if (catalog == null) BuildCatalog();
            Mesh m = MeshOf(g);
            GameObject pf;
            if (m != null && byMesh.TryGetValue(m, out pf)) return pf;
            BuildCatalog();   // (modeles charges entre-temps)
            return m != null && byMesh.TryGetValue(m, out pf) ? pf : null;
        }
        static MonoBehaviour Behaviour(GameObject g)
        {
            foreach (MonoBehaviour m in g.GetComponents<MonoBehaviour>())
                if (m != null && m.GetType().Assembly == asm && (m.GetType().Name == "VendingDrinkBehaviour" || m.GetType().Name == "VendingSnackBehaviour")) return m;
            return null;
        }

        // Comme SodaMachineController.SpawnProduct / SnackMachineController.SpawnItem.
        static List<Component> Clone(string prefab, string goName, string itemName, bool drink, bool food, Vector3 pos, Quaternion rot, string behType, NetReader r)
        {
            var made = new List<Component>();
            if (catalog == null || !catalog.ContainsKey(prefab)) BuildCatalog();
            GameObject pf;
            if (!catalog.TryGetValue(prefab, out pf)) { Log.Warn("mods : Jonne Food System, modele " + prefab + " introuvable ici"); return made; }
            GameObject go = (GameObject)UnityEngine.Object.Instantiate(pf, pos, rot);
            go.SetActive(true);
            int parts = LayerMask.NameToLayer("Parts");
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) { t.gameObject.tag = "PART"; t.gameObject.layer = parts; }
            foreach (PlayMakerFSM f in go.GetComponentsInChildren<PlayMakerFSM>(true)) UnityEngine.Object.Destroy(f);
            Component vd = go.AddComponent(tItem);
            fItemName.SetValue(vd, itemName);
            if (fIsDrink != null) fIsDrink.SetValue(vd, drink);
            if (fIsFood != null) fIsFood.SetValue(vd, food);
            // (nom de l'objet chez celui qui l'a fait sortir : le texte vise en depend ; le maillage seul ne distingue pas deux
            // modeles qui le partagent)
            if (mSanitize != null) { try { mSanitize.Invoke(null, new object[] { go, goName, "(Clone)" }); } catch (Exception ex) { Log.Warn("mods : Jonne Food System, nom : " + (ex.InnerException ?? ex).Message); } }
            Type bt = behType.Length > 0 ? asm.GetType(behType) : null;
            if (bt != null)
            {
                Component b = go.GetComponent(bt) ?? go.AddComponent(bt);
                ReadFlat(r, b);
                FieldInfo itm = bt.GetField("itm", A);
                if (itm != null) itm.SetValue(b, vd);
            }
            made.Add(vd);
            return made;
        }

        // Champs simples d'un composant (nombres, textes, drapeaux), dans l'ordre de declaration : meme version du mod partout.
        static readonly Dictionary<Type, List<FieldInfo>> flatCache = new Dictionary<Type, List<FieldInfo>>();
        static List<FieldInfo> Flat(Type t)
        {
            List<FieldInfo> l;
            if (flatCache.TryGetValue(t, out l)) return l;
            l = new List<FieldInfo>();
            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (f.FieldType == typeof(float) || f.FieldType == typeof(int) || f.FieldType == typeof(bool) || f.FieldType == typeof(string)) l.Add(f);
            l.Sort((a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
            flatCache[t] = l;
            return l;
        }
        static void WriteFlat(NetWriter w, object o) { foreach (FieldInfo f in Flat(o.GetType())) WriteVal(w, f.FieldType, f.GetValue(o)); }
        static void ReadFlat(NetReader r, object o) { foreach (FieldInfo f in Flat(o.GetType())) f.SetValue(o, ReadVal(r, f.FieldType)); }

        // ---------------------------------------------------------------- codage d'un VendingItemSaveInfo (champs publics)
        static readonly Dictionary<Type, List<FieldInfo>> objCache = new Dictionary<Type, List<FieldInfo>>();
        static List<FieldInfo> Fields(Type t)
        {
            List<FieldInfo> l;
            if (objCache.TryGetValue(t, out l)) return l;
            l = new List<FieldInfo>(t.GetFields(BindingFlags.Public | BindingFlags.Instance));
            l.Sort((a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
            objCache[t] = l;
            return l;
        }
        static void WriteObj(NetWriter w, object o, Type t) { foreach (FieldInfo f in Fields(t)) WriteVal(w, f.FieldType, f.GetValue(o)); }
        static object ReadObj(NetReader r, Type t)
        {
            object o = Activator.CreateInstance(t);
            foreach (FieldInfo f in Fields(t)) f.SetValue(o, ReadVal(r, f.FieldType));
            return o;
        }
        static void WriteVal(NetWriter w, Type t, object v)
        {
            if (t == typeof(string)) { w.Bool(v != null); if (v != null) w.Str((string)v); }
            else if (t == typeof(float)) w.F32((float)v);
            else if (t == typeof(int)) w.I32((int)v);
            else if (t == typeof(bool)) w.Bool((bool)v);
            else if (t == typeof(long)) { long l = (long)v; w.I32((int)(l & 0xffffffffL)).I32((int)(l >> 32)); }
            else if (t.IsEnum) w.I32(Convert.ToInt32(v));
            else if (t.IsArray)
            {
                Array a = v as Array;
                w.Bool(a != null);
                if (a == null) return;
                w.U16(a.Length);
                foreach (object x in a) WriteVal(w, t.GetElementType(), x);
            }
            else if (t.IsClass) { w.Bool(v != null); if (v != null) WriteObj(w, v, t); }
        }
        static object ReadVal(NetReader r, Type t)
        {
            if (t == typeof(string)) return r.Bool() ? r.Str() : null;
            if (t == typeof(float)) return r.F32();
            if (t == typeof(int)) return r.I32();
            if (t == typeof(bool)) return r.Bool();
            if (t == typeof(long)) { long lo = (uint)r.I32(), hi = r.I32(); return (hi << 32) | lo; }
            if (t.IsEnum) return Enum.ToObject(t, r.I32());
            if (t.IsArray)
            {
                if (!r.Bool()) return null;
                int n = r.U16();
                Array a = Array.CreateInstance(t.GetElementType(), n);
                for (int i = 0; i < n; i++) a.SetValue(ReadVal(r, t.GetElementType()), i);
                return a;
            }
            if (t.IsClass) return r.Bool() ? ReadObj(r, t) : null;
            return t.IsValueType ? Activator.CreateInstance(t) : null;
        }

        // ---------------------------------------------------------------- reception
        public static void OnMessage(int who, NetReader r)
        {
            if (!Present()) return;
            int kind = r.U8();
            float now = Time.realtimeSinceStartup;
            switch (kind)
            {
                case 0:
                case 2:
                {
                    string key = r.Str();
                    object info = ReadObj(r, tInfo);
                    string id = (string)tInfo.GetField("ItemId").GetValue(info) ?? "";
                    var pos = new Vector3((float)tInfo.GetField("PosX").GetValue(info), (float)tInfo.GetField("PosY").GetValue(info), (float)tInfo.GetField("PosZ").GetValue(info));
                    Arrive(who, key, kind == 2, id, pos, () => Create(info), now);
                    break;
                }
                case 6:
                case 7:
                {
                    string key = r.Str(), prefab = r.Str(), goName = r.Str(), name = r.Str(); bool drink = r.Bool(), food = r.Bool();
                    Vector3 pos = r.Vec(); Quaternion rot = r.Quat(); string beh = r.Str();
                    Arrive(who, key, kind == 7, "", pos, () => Clone(prefab, goName, name, drink, food, pos, rot, beh, r), now, prefab);
                    break;
                }
                case 1:
                {
                    string key = r.Str();
                    Ent e;
                    if (!byKey.TryGetValue(key, out e)) return;
                    Forget(e);
                    if (e.C != null) UnityEngine.Object.Destroy(e.C.gameObject);
                    if (e.Alts != null) foreach (Component a in e.Alts) if (a != null) UnityEngine.Object.Destroy(a.gameObject);
                    Log.Info("mods : Jonne Food System, " + key + " disparu chez #" + who + ", retire ici");
                    break;
                }
                case 3: if (!Session.IsHost && r.U8() == Session.LocalId) OnRoster(r, now); break;
                case 4: if (Session.IsHost) SendRoster(who); break;
                case 5:
                {
                    if (!Session.IsHost) return;
                    int n = r.U16();
                    for (int i = 0; i < n; i++) { Ent e; if (byKey.TryGetValue(r.Str(), out e)) Send(e, false); }
                    break;
                }
            }
        }

        static void Arrive(int who, string key, bool state, string id, Vector3 pos, Func<List<Component>> make, float now, string cloneOf = null)
        {
            Ent e;
            byKey.TryGetValue(key, out e);
            if (e != null && e.C != null && !state) return;   // (deja la)
            if (e == null)
            {
                // le meme, ne ici tout seul (cuisson, rayon) ou venu de la sauvegarde : pris sous cette cle
                Component lc = Claim(id, cloneOf, pos, 1.2f);
                if (lc != null && !state)
                {
                    Track(lc, key, now);
                    Log.Info("mods : Jonne Food System, " + Name(lc) + " de #" + who + " deja la ici, apparie (" + key + ")");
                    return;
                }
                if (lc != null) UnityEngine.Object.Destroy(lc.gameObject);
            }
            List<Component> made = make();
            if (made.Count == 0) { Log.Warn("mods : Jonne Food System, " + (id.Length > 0 ? id : cloneOf) + " de #" + who + " pas recree ici"); return; }
            if (e != null)
            {
                if (e.C != null) UnityEngine.Object.Destroy(e.C.gameObject);
                if (e.Alts != null) foreach (Component a in e.Alts) if (a != null) UnityEngine.Object.Destroy(a.gameObject);
                Forget(e);
            }
            Track(made[0], key, now);
            Ent ne = byKey[key];
            ne.Quiet = now + 2f;
            if (made.Count > 1)
            {
                ne.Alts = made.GetRange(1, made.Count - 1);
                foreach (Component a in ne.Alts) { byId[a.GetInstanceID()] = ne; loose.Remove(a); }
            }
            Log.Info("mods : Jonne Food System, " + Name(made[0]) + (state ? " change" : " apparu") + " chez #" + who + ", recree ici (" + key + ")");
        }

        // Objet pas suivi d'ici (meme ID, ou meme modele, a moins de 'max' metres) : le plus proche.
        static Component Claim(string id, string prefab, Vector3 pos, float max)
        {
            Component best = null; float bd = max * max;
            foreach (KeyValuePair<Component, float> kv in loose)
            {
                Component c = kv.Key;
                if (c == null) continue;
                bool same = id.Length > 0 ? Id(c) == id : prefab != null && Id(c).Length == 0 && PrefabOf(c.gameObject) != null && PrefabOf(c.gameObject).name == prefab;
                if (!same) continue;
                float d = (c.transform.position - pos).sqrMagnitude;
                if (d < bd) { bd = d; best = c; }
            }
            if (best != null) loose.Remove(best);
            return best;
        }

        // ---------------------------------------------------------------- releve de l'hote (arrivee d'un invite)
        static void SendRoster(int to)
        {
            var list = new List<Ent>();
            foreach (Ent e in byKey.Values) if (e.C != null) list.Add(e);
            int i = 0;
            do
            {
                int n = Math.Min(15, list.Count - i);
                NetWriter w = Head(3).U8(to).Bool(i + n >= list.Count).U8(n);
                for (int j = 0; j < n; j++)
                {
                    Component c = list[i + j].C;
                    string id = Id(c);
                    GameObject pf = id.Length == 0 ? PrefabOf(c.gameObject) : null;
                    w.Str(list[i + j].Key).Str(id).Str(pf != null ? pf.name : "").Vec(c.transform.position);
                }
                w.Send();
                i += n;
            } while (i < list.Count);
            Log.Info("mods : Jonne Food System, releve de " + list.Count + " objet(s) envoye a #" + to);
        }

        static readonly List<string> want = new List<string>();
        static int rosterMatched;
        static void OnRoster(NetReader r, float now)
        {
            if (rosterIn) return;
            bool last = r.Bool(); int n = r.U8();
            for (int j = 0; j < n; j++)
            {
                string key = r.Str(), id = r.Str(), prefab = r.Str(); Vector3 pos = r.Vec();
                if (byKey.ContainsKey(key)) continue;
                Component c = Claim(id, prefab.Length > 0 ? prefab : null, pos, 2f);
                if (c != null) { Track(c, key, now); rosterMatched++; }
                else want.Add(key);
            }
            if (!last) return;
            rosterIn = true;
            int extra = 0;
            var drop = new List<Component>();
            foreach (KeyValuePair<Component, float> kv in loose) if (kv.Value < 0f) drop.Add(kv.Key);
            foreach (Component c in drop) { loose.Remove(c); if (c != null) { UnityEngine.Object.Destroy(c.gameObject); extra++; } }
            for (int i = 0; i < want.Count; i += 20)
            {
                int k = Math.Min(20, want.Count - i);
                NetWriter w = Head(5).U16(k);
                for (int j = 0; j < k; j++) w.Str(want[i + j]);
                w.Send();
            }
            Log.Info("mods : Jonne Food System, releve de l'hote : " + rosterMatched + " apparie(s), " + want.Count + " demande(s), " + extra + " retire(s) (pas chez l'hote)");
            want.Clear();
        }

        // ---------------------------------------------------------------- essai [Test] Autotest=jonne
        // Hote : un distributeur fait sortir un article (40 s), puis l'objet est detruit (55 s). Invite : un article sort chez
        // lui juste apres un « geste » (65 s). Chacun note ce qu'il a.
        public static string Test(int step)
        {
            if (!Present()) return "Jonne Food System absent";
            Type mb = asm.GetType("Vending_Machines.VendingMachineBase");
            if (step == 0 || step == 2)
            {
                var ms = new List<Component>();
                if (mb != null) foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(mb)) { string tn = o.GetType().Name; if (tn == "SodaMachineController" || tn == "SnackMachineController") ms.Add((Component)o); }
                if (ms.Count == 0) return "aucun distributeur de canettes ou de friandises";
                Component m = ms[Mathf.Min(step == 0 ? 0 : 1, ms.Count - 1)];
                TestAct = step == 2;
                try { mb.GetMethod("TestSpawnItem").Invoke(m, null); }
                catch (Exception e) { return "distributeur " + m.name + " : " + (e.InnerException ?? e).Message; }
                return "article sorti de " + m.name + " (" + m.GetType().Name + ", " + ms.Count + " distributeurs)";
            }
            if (step == 1)
            {
                TestAct = false;
                Ent last = null;
                int best = -1;
                foreach (Ent e in byKey.Values)
                {
                    int n0; string[] kp = e.Key.Split(':');
                    if (e.C != null && kp[2] == Session.LocalId.ToString() && int.TryParse(kp[kp.Length - 1], out n0) && n0 > best) { best = n0; last = e; }
                }
                if (last == null) return "rien a detruire";
                string n = Name(last.C);
                UnityEngine.Object.Destroy(last.C.gameObject);
                return n + " detruit (" + last.Key + ")";
            }
            if (step == 4)
            {
                string path = (string)pPath.GetValue(null, null);
                long before = File.Exists(path) ? new FileInfo(path).Length : -1;
                foreach (Ent e in byKey.Values)
                    if (e.C != null && Id(e.C).Length > 0) { Send(e, true); return "etat de " + Name(e.C) + " envoye ; fichier du mod " + before + " -> " + (File.Exists(path) ? new FileInfo(path).Length : -1) + " octets"; }
                return "aucun objet avec ID";
            }
            TestAct = false;
            return "suivis " + byKey.Count + ", en attente " + loose.Count + ", dans le mod " + Items().Count;
        }
    }
}
