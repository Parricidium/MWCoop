using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // My Summer Car : sauvegarde en jeu groupee (gel de ~8,5 s de l'hote a l'arrivee d'un invite, 24/10).
    // Le jeu sauve par Easy Save 2 : chaque action SaveBool/SaveInt/SaveFloat/SaveTransform... d'un automate qui ecoute
    // SAVEGAME fait ES2.Save(valeur, "defaultES2File.txt?tag=x"), et ES2Writer.Save relit TOUT le fichier pour en
    // recopier les autres etiquettes (ES2Reader.DeleteTags) puis le reecrit en entier : des centaines de lectures et
    // d'ecritures completes du meme fichier dans une seule image. ES2 sait ecrire plusieurs etiquettes d'un coup
    // (un ES2Writer, Write(valeur, etiquette) autant de fois, un seul Save) : meme format, meme chemin de code.
    // Pendant Game.SaveInPlace, chaque action de sauvegarde des automates qui ecoutent SAVEGAME est remplacee par
    // Batched, qui lit la valeur au meme moment que l'action du jeu et la range par fichier (la derniere ecriture
    // d'une etiquette gagne, comme avant) ; juste apres la diffusion, actions d'origine remises, puis une ecriture
    // par fichier, dans la meme image (les Transform sont lus a ce moment-la : rien n'a bouge depuis).
    // Les sauvegardes des etats atteints plus tard (attente avant d'ecrire) passent par les actions d'origine, une
    // par une comme avant. Les autres types (listes, tables, matieres...) ne sont pas touches.
    // Tout par reflexion : ES2.dll et les actions Easy Save sont dans les assemblies de MSC (MWC sauve autrement).
    // Repli : un fichier dont l'ecriture groupee echoue est ecrit etiquette par etiquette. [Test] SauvegardeUneParUne=1 :
    // ancien comportement (comparer les temps).
    public static class SaveBatchMsc
    {
        // Action de sauvegarde reconnue : nom du type -> champs.
        class Kind { public FieldInfo Value, Tag, File; public bool Transform; public FieldInfo Go, Ref, Proxy; public MethodInfo SetUp, Parse; }
        static readonly Dictionary<System.Type, Kind> kinds = new Dictionary<System.Type, Kind>();
        static readonly HashSet<string> Names = new HashSet<string> { "SaveBool", "SaveInt", "SaveFloat", "SaveString", "SaveVector2", "SaveVector3", "SaveQuaternion", "SaveColor", "SaveTransform" };

        class Entry { public string Tag; public object Value; public Transform T; }
        class Pending { public readonly List<Entry> List = new List<Entry>(); public readonly Dictionary<string, int> Index = new Dictionary<string, int>(); }
        static Dictionary<string, Pending> files;
        static List<KeyValuePair<FsmState, FsmStateAction[]>> swapped;
        static System.Diagnostics.Stopwatch watch;
        static int added, skipped;

        // ES2 (assembly "ES2" du jeu) : ES2Writer.Create(string), Write<T>(T, string), Save(), Dispose().
        static System.Type writerType;
        static MethodInfo create, write, save, writeList;   // (writeList : Write<string>(List<string>, string))
        static readonly Dictionary<System.Type, MethodInfo> writes = new Dictionary<System.Type, MethodInfo>();
        static bool es2Missing;

        class Batched : FsmStateAction
        {
            public FsmStateAction Orig; public Kind K;
            public override void OnEnter()
            {
                try { Add(this, Orig, K); }
                catch (System.Exception e) { skipped++; MWCoop.Log.Warn("sauvegarde groupee : " + e.Message); }
                Finish();
            }
        }

        // Lectures dans la meme diffusion (WIGA 25/10 : moto revenue a la maison, roues tournees, apres une sauvegarde en jeu) :
        // des automates enchainent « Save game » -> « Load game » dans la meme image (roues : SaveVector3 puis LoadVector3 et
        // SetRotation). Ecritures groupees en fin de diffusion, Load* relisait le fichier d'AVANT : valeurs de la sauvegarde
        // precedente remises (position, rotation...). Une etiquette deja rangee pendant cette diffusion est relue depuis le
        // lot ; les autres, depuis le fichier comme le jeu (action d'origine).
        static readonly HashSet<string> LoadNames = new HashSet<string> { "LoadBool", "LoadInt", "LoadFloat", "LoadString", "LoadVector2", "LoadVector3", "LoadQuaternion", "LoadColor", "LoadTransform" };
        class LoadKind { public FieldInfo Value, Tag, File; public bool Transform; }
        static readonly Dictionary<System.Type, LoadKind> loadKinds = new Dictionary<System.Type, LoadKind>();
        static int fromBatch;
        static LoadKind LoadKindOf(FsmStateAction a)
        {
            if (a == null) return null;
            System.Type t = a.GetType();
            LoadKind k;
            if (loadKinds.TryGetValue(t, out k)) return k;
            k = null;
            if (t.Namespace == "HutongGames.PlayMaker.Actions" && LoadNames.Contains(t.Name))
            {
                const BindingFlags F = BindingFlags.Public | BindingFlags.Instance;
                FieldInfo v = t.GetField("loadValue", F), tag = t.GetField("uniqueTag", F), file = t.GetField("saveFile", F);
                if (v != null && tag != null && file != null && tag.FieldType == typeof(FsmString) && file.FieldType == typeof(FsmString))
                    k = new LoadKind { Value = v, Tag = tag, File = file, Transform = t.Name == "LoadTransform" };
            }
            loadKinds[t] = k;
            return k;
        }
        class BatchedLoad : FsmStateAction
        {
            public FsmStateAction Orig; public LoadKind K;
            public override void OnEnter()
            {
                bool done = false;
                try { done = FromBatch(this, Orig, K); }
                catch (System.Exception e) { MWCoop.Log.Warn("sauvegarde groupee : lecture : " + e.Message); }
                if (done) { Finish(); return; }
                Orig.OnEnter();   // (l'action du jeu : lit le fichier, finit d'elle-meme)
                Finish();
            }
        }
        static bool FromBatch(BatchedLoad b, FsmStateAction a, LoadKind k)
        {
            if (files == null) return false;
            FsmString file = (FsmString)k.File.GetValue(a), tag = (FsmString)k.Tag.GetValue(a);
            Pending p; int at;
            if (file == null || tag == null || tag.Value == null || !files.TryGetValue(file.Value ?? "", out p) || !p.Index.TryGetValue(tag.Value, out at)) return false;
            Entry e = p.List[at];
            object v = k.Value.GetValue(a);
            if (k.Transform)
            {
                GameObject go = b.Fsm != null ? b.Fsm.GetOwnerDefaultTarget(v as FsmOwnerDefault) : null;
                if (go == null || e.T == null) return true;
                if (go.transform != e.T) { go.transform.position = e.T.position; go.transform.rotation = e.T.rotation; }   // (meme objet : rien ne bouge)
            }
            else if (v is FsmBool && e.Value is bool) ((FsmBool)v).Value = (bool)e.Value;
            else if (v is FsmInt && e.Value is int) ((FsmInt)v).Value = (int)e.Value;
            else if (v is FsmFloat && e.Value is float) ((FsmFloat)v).Value = (float)e.Value;
            else if (v is FsmString && e.Value is string) ((FsmString)v).Value = (string)e.Value;
            else if (v is FsmVector2 && e.Value is Vector2) ((FsmVector2)v).Value = (Vector2)e.Value;
            else if (v is FsmVector3 && e.Value is Vector3) ((FsmVector3)v).Value = (Vector3)e.Value;
            else if (v is FsmQuaternion && e.Value is Quaternion) ((FsmQuaternion)v).Value = (Quaternion)e.Value;
            else if (v is FsmColor && e.Value is Color) ((FsmColor)v).Value = (Color)e.Value;
            else return false;
            fromBatch++;
            return true;
        }

        static Kind KindOf(FsmStateAction a)
        {
            if (a == null) return null;
            System.Type t = a.GetType();
            Kind k;
            if (kinds.TryGetValue(t, out k)) return k;
            k = null;
            if (t.Namespace == "HutongGames.PlayMaker.Actions" && Names.Contains(t.Name))
            {
                const BindingFlags F = BindingFlags.Public | BindingFlags.Instance;
                FieldInfo v = t.GetField("saveValue", F), tag = t.GetField("uniqueTag", F), file = t.GetField("saveFile", F);
                if (v != null && tag != null && file != null && tag.FieldType == typeof(FsmString) && file.FieldType == typeof(FsmString))
                    k = new Kind { Value = v, Tag = tag, File = file, Transform = t.Name == "SaveTransform" };
            }
            // Listes d'objets (items.txt : sacs, caisses... 207 sur la sauvegarde Satsuma) : ArrayListEasySave fait
            // SetUpArrayListProxyPointer(cible, reference), chaque element passe par PlayMakerUtils.ParseValueToString,
            // puis ES2.Save(List<string>, saveFile?tag=uniqueTag) -- on reprend ses propres methodes.
            else if (t.Namespace == "HutongGames.PlayMaker.Actions" && t.Name == "ArrayListEasySave")
            {
                const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                System.Type pu = t.Assembly.GetType("PlayMakerUtils");
                var c = new Kind
                {
                    Go = t.GetField("gameObject", F), Ref = t.GetField("reference", F), Tag = t.GetField("uniqueTag", F), File = t.GetField("saveFile", F),
                    Proxy = t.GetField("proxy", F), SetUp = t.GetMethod("SetUpArrayListProxyPointer", F, null, new[] { typeof(GameObject), typeof(string) }, null),
                    Parse = pu != null ? pu.GetMethod("ParseValueToString", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(object) }, null) : null
                };
                if (c.Go != null && c.Ref != null && c.Tag != null && c.File != null && c.Proxy != null && c.SetUp != null && c.Parse != null && writeList != null) k = c;
                else Log.Warn("sauvegarde groupee : ArrayListEasySave different, ses listes restent ecrites une par une");
            }
            kinds[t] = k;
            return k;
        }

        static bool Es2()
        {
            if (writerType != null) return true;
            if (es2Missing) return false;
            foreach (Assembly a in System.AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetName().Name == "ES2") { writerType = a.GetType("ES2Writer"); break; }
            if (writerType != null)
            {
                create = writerType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                save = writerType.GetMethod("Save", BindingFlags.Public | BindingFlags.Instance, null, System.Type.EmptyTypes, null);
                foreach (MethodInfo m in writerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name != "Write" || !m.IsGenericMethodDefinition) continue;
                    ParameterInfo[] p = m.GetParameters();
                    if (p.Length != 2 || p[1].ParameterType != typeof(string)) continue;
                    System.Type p0 = p[0].ParameterType;
                    if (p0.IsGenericParameter) write = m;
                    else if (p0.IsGenericType && p0.GetGenericTypeDefinition() == typeof(List<>)) writeList = m.MakeGenericMethod(typeof(string));
                }
            }
            if (writerType == null || create == null || save == null || write == null)
            {
                es2Missing = true; writerType = null;
                Log.Warn("sauvegarde groupee : ES2Writer introuvable ou different, sauvegarde du jeu une par une");
                return false;
            }
            return true;
        }

        // Avant la diffusion de SAVEGAME : actions de sauvegarde des automates qui l'ecoutent remplacees par Batched.
        public static void Begin(IEnumerable<KeyValuePair<PlayMakerFSM, string>> listeners, PlayMakerFSM setup)
        {
            files = null; swapped = null; keepOff = null;
            if (!Game.Msc) return;
            if (Config.GetInt("Test", "SauvePointsBrut", 0) == 0) SnapOff(listeners);
            if (testOld || Config.GetInt("Test", "SauvegardeUneParUne", 0) != 0 || !Es2()) return;
            watch = System.Diagnostics.Stopwatch.StartNew();
            files = new Dictionary<string, Pending>();
            swapped = new List<KeyValuePair<FsmState, FsmStateAction[]>>();
            added = skipped = fromBatch = 0;
            int n = 0;
            foreach (KeyValuePair<PlayMakerFSM, string> kv in listeners) { Swap(kv.Key); n++; }
            if (setup != null) Swap(setup);
            Log.Info("sauvegarde groupee : " + swapped.Count + " etats de " + n + " automates accroches en " + watch.ElapsedMilliseconds + " ms");
        }

        // Points de montage rallumes par SAVEGAME (24/10, cause du controle de l'hote qui demontait la SATSUMA de Markos) :
        // Database/DatabaseBody/Subframe :: Data repasse par "Installed", qui active SATSUMA/Chassis/sub frame(xxxxx) avec
        // tous ses enfants (ActivateGameObject recursif) -- dont les trigger_* des pieces moteur montees (40 sur la sauvegarde
        // Satsuma), eteints par leur 'End'. Le jeu charge le menu juste apres : sans effet en solo ; en jeu, l'hote voyait ces
        // pieces demontees (Parts.MscHolds), les reproposait au montage et le disait aux invites. Avant la diffusion : enfants
        // eteints des cibles d'une activation recursive dans les automates qui ecoutent SAVEGAME ; apres : eteints de nouveau.
        // [Test] SauvePointsBrut=1 : comme avant.
        static List<GameObject> keepOff;
        static void SnapOff(IEnumerable<KeyValuePair<PlayMakerFSM, string>> listeners)
        {
            keepOff = new List<GameObject>();
            var seen = new HashSet<Transform>();
            foreach (KeyValuePair<PlayMakerFSM, string> kv in listeners)
            {
                PlayMakerFSM f = kv.Key;
                FsmState[] sts;
                try { if (f == null || f.Fsm == null) continue; sts = f.FsmStates; } catch { continue; }
                foreach (FsmState s in sts)
                {
                    FsmStateAction[] acts;
                    try { acts = s.Actions; } catch { continue; }
                    if (acts == null) continue;
                    foreach (FsmStateAction a in acts)
                    {
                        if (a == null || a.GetType().Name != "ActivateGameObject") continue;
                        System.Type t = a.GetType();
                        FsmOwnerDefault od = t.GetField("gameObject") != null ? t.GetField("gameObject").GetValue(a) as FsmOwnerDefault : null;
                        FsmBool on = t.GetField("activate") != null ? t.GetField("activate").GetValue(a) as FsmBool : null;
                        FsmBool rec = t.GetField("recursive") != null ? t.GetField("recursive").GetValue(a) as FsmBool : null;
                        if (od == null || on == null || rec == null || !on.Value || !rec.Value) continue;
                        GameObject tg = od.OwnerOption == OwnerDefaultOption.UseOwner ? f.gameObject : (od.GameObject != null ? od.GameObject.Value : null);
                        if (tg == null || !seen.Add(tg.transform)) continue;
                        foreach (Transform c in tg.GetComponentsInChildren<Transform>(true)) if (!c.gameObject.activeSelf) keepOff.Add(c.gameObject);
                    }
                }
            }
        }
        static void RestoreOff()
        {
            if (keepOff == null) return;
            int n = 0;
            var sb = new System.Text.StringBuilder();
            foreach (GameObject g in keepOff)
                if (g != null && g.activeSelf) { g.SetActive(false); if (++n <= 5) sb.Append(' ').Append(g.name).Append(';'); }
            if (n > 0) Log.Info("sauvegarde en jeu : " + n + " objet(s) rallume(s) par SAVEGAME eteint(s) de nouveau (sur " + keepOff.Count + " notes) :" + sb + (n > 5 ? " ..." : ""));
            keepOff = null;
        }

        static void Swap(PlayMakerFSM f)
        {
            if (f == null || f.Fsm == null) return;
            FsmState[] states;
            try { states = f.FsmStates; } catch { return; }
            if (states == null) return;
            foreach (FsmState s in states)
            {
                FsmStateAction[] acts;
                try { if (s == null || s.Fsm == null) continue; acts = s.Actions; } catch { continue; }
                if (acts == null) continue;
                FsmStateAction[] n = null;
                for (int i = 0; i < acts.Length; i++)
                {
                    if (acts[i] == null || !acts[i].Enabled) continue;
                    Kind k = KindOf(acts[i]);
                    LoadKind lk = k == null && Config.GetInt("Test", "SauveLecturesBrutes", 0) == 0 ? LoadKindOf(acts[i]) : null;   // ([Test] SauveLecturesBrutes=1 : comme avant)
                    if (k == null && lk == null) continue;
                    if (n == null) n = (FsmStateAction[])acts.Clone();
                    if (k != null) n[i] = new Batched { Orig = acts[i], K = k };
                    else n[i] = new BatchedLoad { Orig = acts[i], K = lk };
                }
                if (n == null) continue;
                swapped.Add(new KeyValuePair<FsmState, FsmStateAction[]>(s, acts));
                s.Actions = n;
            }
        }

        // Valeur lue comme l'action du jeu l'aurait lue (meme image, memes variables).
        static void Add(Batched b, FsmStateAction a, Kind k)
        {
            FsmString file = (FsmString)k.File.GetValue(a), tag = (FsmString)k.Tag.GetValue(a);
            string fileName = file != null ? file.Value : null, tagName = tag != null ? tag.Value : null;
            if (string.IsNullOrEmpty(fileName) || tagName == null) { skipped++; return; }
            var e = new Entry { Tag = tagName };
            if (k.Proxy != null)
            {
                a.Init(b.State);   // (l'action du jeu cherche sa liste par son propre Fsm, comme si elle etait entree)
                GameObject go = b.Fsm.GetOwnerDefaultTarget((FsmOwnerDefault)k.Go.GetValue(a));
                FsmString rf = (FsmString)k.Ref.GetValue(a);
                if (go == null || !(bool)k.SetUp.Invoke(a, new object[] { go, rf != null ? rf.Value : "" })) { skipped++; return; }
                object proxy = k.Proxy.GetValue(a);
                PropertyInfo pl = proxy != null ? proxy.GetType().GetProperty("arrayList") : null;
                System.Collections.IEnumerable arr = pl != null ? pl.GetValue(proxy, null) as System.Collections.IEnumerable : null;
                if (arr == null) { skipped++; return; }
                var l = new List<string>();
                foreach (object o in arr) l.Add((string)k.Parse.Invoke(null, new object[] { o }));
                e.Value = l;
            }
            else if (k.Transform)
            {
                GameObject go = b.Fsm != null ? b.Fsm.GetOwnerDefaultTarget(k.Value.GetValue(a) as FsmOwnerDefault) : null;
                if (go == null) { skipped++; return; }   // (l'action du jeu n'ecrit rien non plus)
                e.T = go.transform;
            }
            else
            {
                e.Value = ValueOf(k.Value.GetValue(a));
                if (e.Value == null) { skipped++; return; }   // (ES2 leverait sur une chaine nulle)
            }
            Pending p;
            if (!files.TryGetValue(fileName, out p)) files[fileName] = p = new Pending();
            int at;
            if (p.Index.TryGetValue(tagName, out at)) p.List[at] = e;
            else { p.Index[tagName] = p.List.Count; p.List.Add(e); }
            added++;
        }

        // (pas de NamedVariable.RawValue dans le PlayMaker de MSC)
        static object ValueOf(object v)
        {
            if (v is FsmBool) return ((FsmBool)v).Value;
            if (v is FsmInt) return ((FsmInt)v).Value;
            if (v is FsmFloat) return ((FsmFloat)v).Value;
            if (v is FsmString) return ((FsmString)v).Value;
            if (v is FsmVector2) return ((FsmVector2)v).Value;
            if (v is FsmVector3) return ((FsmVector3)v).Value;
            if (v is FsmQuaternion) return ((FsmQuaternion)v).Value;
            if (v is FsmColor) return ((FsmColor)v).Value;
            return null;
        }

        // Juste apres la diffusion : actions d'origine remises, puis une ecriture par fichier.
        public static void End()
        {
            RestoreOff();
            if (swapped == null) return;
            foreach (KeyValuePair<FsmState, FsmStateAction[]> kv in swapped)
                try { kv.Key.Actions = kv.Value; } catch (System.Exception e) { Log.Warn("sauvegarde groupee : etat " + kv.Key.Name + " : " + e.Message); }
            swapped = null;
            long hooked = watch.ElapsedMilliseconds;
            int tags = 0, fallback = 0;
            var sb = new System.Text.StringBuilder();
            foreach (KeyValuePair<string, Pending> kv in files)
            {
                tags += kv.Value.List.Count;
                if (!WriteAll(kv.Key, kv.Value.List)) { fallback++; foreach (Entry e in kv.Value.List) WriteAll(kv.Key, new List<Entry> { e }); }
                sb.Append(kv.Key).Append(' ').Append(kv.Value.List.Count).Append(", ");
            }
            files = null;
            Log.Info("sauvegarde groupee : " + tags + " etiquettes (" + added + " sauvegardes" + (skipped > 0 ? ", " + skipped + " sans valeur" : "") + ") ecrites en "
                     + (watch.ElapsedMilliseconds - hooked) + " ms, " + watch.ElapsedMilliseconds + " ms en tout : " + sb.ToString().TrimEnd(' ', ',')
                     + (fallback > 0 ? " ; " + fallback + " fichier(s) ecrit(s) un par un" : "") + (fromBatch > 0 ? " ; " + fromBatch + " lecture(s) dans la meme diffusion prise(s) au lot" : ""));
        }

        // [Test] Autotest=mscsauve (solo, MSC) : a 40 s sauvegarde en jeu groupee, a 55 s une par une (ancien comportement) ;
        // duree de Game.SaveInPlace et plus longue image des 5 s suivantes, fichiers copies 5 s apres chaque sauvegarde dans
        // dumps/es2-groupee.txt et dumps/es2-uneparune.txt (a comparer etiquette par etiquette : scratchpad es2diff.py).
        static bool testOld;
        static int tStep, tWatch;
        static float tCopyAt = -1, tWorst, tProbeAt = -1;
        static string tName;
        // Sonde (avec l'autotest) : points de montage « trigger_* » eteints (piece montee) avant SAVEGAME, et ceux qui se
        // rallument : juste apres la diffusion, puis apres la remise des automates (Game.Update, 2,5 s).
        static List<GameObject> probeOff;
        static void ProbeSnap()
        {
            probeOff = new List<GameObject>();
            foreach (GameObject g in Resources.FindObjectsOfTypeAll(typeof(GameObject)))
                if (g.name.StartsWith("trigger_") && !g.activeSelf && g.hideFlags == HideFlags.None) probeOff.Add(g);   // (pas de GameObject.scene dans l'Unity 5.0 de MSC)
        }
        static void ProbeCheck(string when)
        {
            if (probeOff == null) return;
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (GameObject g in probeOff) if (g != null && g.activeSelf) { n++; if (n <= 12) sb.Append(' ').Append(Recon.Path(g.transform)); }
            Log.Info("autotest mscsauve : " + when + " : " + n + " point(s) de montage rallume(s) sur " + probeOff.Count + " eteints :" + sb);
        }

        // Suivi de valeurs sauvegardees ([Test] SauveSuivi = etiquettes separees par ';') : la variable lue par l'action
        // SaveFloat/SaveInt/SaveBool de cette etiquette, journalisee a chaque etape (qui la change, et quand).
        static Dictionary<string, NamedVariable> watched;
        static string Transitions(FsmState s)
        {
            var sb = new System.Text.StringBuilder();
            if (s.Transitions != null) foreach (FsmTransition tr in s.Transitions) sb.Append(tr.EventName).Append('>').Append(tr.ToState).Append(' ');
            return sb.ToString();
        }
        static void Watch(string when)
        {
            if (watched == null)
            {
                watched = new Dictionary<string, NamedVariable>();
                var want = new HashSet<string>(Config.Get("Test", "SauveSuivi", "racing carburators(Clone)Adjust1;racing carburators(Clone)Adjust2;oilpan(Clone)OilLevel").Split(';'));
                foreach (PlayMakerFSM f in Object.FindObjectsOfType<PlayMakerFSM>())
                {
                    FsmState[] sts; try { sts = f.FsmStates; } catch { continue; }
                    foreach (FsmState s in sts)
                    {
                        FsmStateAction[] acts; try { acts = s.Actions; } catch { continue; }
                        if (acts == null) continue;
                        foreach (FsmStateAction a in acts)
                        {
                            Kind k = KindOf(a);
                            if (k == null || k.Value == null || k.Transform) continue;
                            FsmString tag = (FsmString)k.Tag.GetValue(a);
                            if (tag != null && want.Contains(tag.Value) && !watched.ContainsKey(tag.Value))
                            {
                                watched[tag.Value] = k.Value.GetValue(a) as NamedVariable;
                                Log.Info("autotest mscsauve : suivi " + tag.Value + " = " + Recon.Path(f.transform) + "::" + f.FsmName + " [" + s.Name + "] {" + (watched[tag.Value] != null ? watched[tag.Value].Name : "?") + "}, actif " + f.ActiveStateName);
                                if (Config.GetInt("Test", "SauveSuiviEtats", 0) != 0)
                                    foreach (FsmState s2 in sts) Log.Info("autotest mscsauve :   [" + s2.Name + "] " + Jobs.DumpActions(f, s2.Name) + " -> " + Transitions(s2));
                            }
                        }
                    }
                }
            }
            var sb = new System.Text.StringBuilder("autotest mscsauve : valeurs " + when + " :");
            foreach (KeyValuePair<string, NamedVariable> kv in watched) sb.Append(' ').Append(kv.Key).Append('=').Append(ValueOf(kv.Value)).Append(';');
            Log.Info(sb.ToString());
        }

        // ([Test] SauvePorte=<chemin de la poignee> : porte de la maison ouverte a 30 s, son angle et DoorOpen notes a 35, 39, 45 et
        // 52 s -- WIGA 25/10 : « les portes de la maison se ferment a la sauvegarde »)
        static int doorStep, moveStep; static float doorLogAt = 35f, moveLogAt = 35f;
        public static void Test(float t)
        {
            string dp = Config.Get("Test", "SauvePorte", "");
            if (dp.Length > 0)
            {
                PlayMakerFSM door = Game.FindFsm(dp, "Use");
                if (doorStep == 0 && t > 30f && door != null) { doorStep = 1; Game.SetState(door, "Open door"); Log.Info("autotest mscsauve : porte " + dp + " ouverte"); }
                if (doorStep == 1 && t >= doorLogAt && door != null)
                {
                    doorLogAt = doorLogAt < 39f ? 39f : doorLogAt < 45f ? 45f : doorLogAt < 52f ? 52f : 9999f;
                    FsmBool open = door.FsmVariables.FindFsmBool("DoorOpen");
                    Transform piv = door.transform.parent;
                    Log.Info("autotest mscsauve : porte t=" + t.ToString("F0") + " etat " + door.ActiveStateName + ", DoorOpen " + (open != null && open.Value) + ", angle " + (piv != null ? piv.localEulerAngles.ToString("F0") : "?"));
                }
            }
            // ([Test] SauveDeplace=<voiture> : deplacee de 20 m a 32 s ; sa position notee a 39, 45 et 52 s -- moto de WIGA)
            string dv = Config.Get("Test", "SauveDeplace", "");
            if (dv.Length > 0)
            {
                Rigidbody vb = VehicleSync.Body(dv);
                if (moveStep == 0 && t > 32f && vb != null) { moveStep = 1; vb.isKinematic = true; vb.transform.position += vb.transform.right * 20f + Vector3.up * 0.3f; vb.isKinematic = false; vb.velocity = Vector3.zero; Log.Info("autotest mscsauve : " + dv + " deplacee en " + vb.position.ToString("F1")); }
                if (moveStep == 1 && t >= moveLogAt && vb != null) { moveLogAt = moveLogAt < 39f ? 39f : moveLogAt < 45f ? 45f : moveLogAt < 52f ? 52f : 9999f; Log.Info("autotest mscsauve : " + dv + " t=" + t.ToString("F0") + " en " + vb.position.ToString("F1")); }
            }
            if (tStep == 0 && watched == null && t > 15f) Watch("au chargement");
            if (tStep == 1 && tWatch == 1 && t > 50f) { tWatch = 2; Watch("10 s apres"); }
            if (tStep == 1 && tWatch == 0 && t > 44f) { tWatch = 1; Watch("apres la remise des automates"); }
            if (tProbeAt > 0 && t >= tProbeAt) { tProbeAt = -1; ProbeCheck("apres la remise des automates"); }
            if (tCopyAt > 0)
            {
                tWorst = Mathf.Max(tWorst, Time.unscaledDeltaTime);
                if (t < tCopyAt) return;
                tCopyAt = -1;
                try
                {
                    string dir = System.IO.Path.Combine(MWCoop.Log.DataDir, "dumps");
                    System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.Copy(System.IO.Path.Combine(Net.SaveTransfer.SaveDir, Game.SaveFile), System.IO.Path.Combine(dir, "es2-" + tName + ".txt"), true);
                    Log.Info("autotest mscsauve : " + tName + ", plus longue image des 5 s suivantes " + (int)(tWorst * 1000) + " ms, fichier copie");
                }
                catch (System.Exception e) { Log.Warn("autotest mscsauve : copie : " + e.Message); }
            }
            if (tStep == 0 && t > 40f || tStep == 1 && t > 55f)
            {
                bool oldFirst = Config.GetInt("Test", "SauveInverse", 0) != 0;   // (une par une d'abord : ecarts dus au temps ou a la methode)
                testOld = (tStep == 1) != oldFirst;
                tName = testOld ? "uneparune" : "groupee";
                tStep++;
                ProbeSnap();
                if (watched != null) Watch("avant " + tName);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Game.SaveInPlace();
                testOld = false;
                Log.Info("autotest mscsauve : " + tName + ", Game.SaveInPlace en " + sw.ElapsedMilliseconds + " ms");
                ProbeCheck("juste apres SAVEGAME");
                if (watched != null) Watch("juste apres " + tName);
                tWorst = 0; tCopyAt = t + 5f; tProbeAt = t + 3.5f;
            }
        }

        static bool WriteAll(string file, List<Entry> list)
        {
            object w = null;
            try
            {
                w = create.Invoke(null, new object[] { file });
                foreach (Entry e in list)
                {
                    object v = e.T != null ? (object)e.T : e.Value;
                    if (v == null || (e.T == null && e.Value == null)) continue;
                    System.Type t = e.T != null ? typeof(Transform) : v.GetType();
                    MethodInfo m;
                    if (v is List<string>) m = writeList;
                    else if (!writes.TryGetValue(t, out m)) writes[t] = m = write.MakeGenericMethod(t);
                    m.Invoke(w, new object[] { v, e.Tag });
                }
                save.Invoke(w, null);
                return true;
            }
            catch (System.Exception ex)
            {
                if (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
                Log.Warn("sauvegarde groupee : " + file + " (" + list.Count + " etiquettes) : " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
            finally { var d = w as System.IDisposable; if (d != null) try { d.Dispose(); } catch { } }
        }
    }
}
