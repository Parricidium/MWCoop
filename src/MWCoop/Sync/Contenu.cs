using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Contenu personnalisable recu de l'hote (CD1-3, Radio, Images, Extra : modsync.inc du lanceur), lu par le jeu de
    // l'invite. Demande de JD (09/10) : beaucoup d'invites recevaient le contenu (telecharge) mais ne voyaient rien en jeu.
    // Le lanceur reliait les dossiers du jeu au contenu recu dans la copie de lancement de l'invite -- seulement si tout
    // etait recu au moment du lancement, et seulement dans cette copie.
    // Ici le jeu de l'invite lit directement le contenu recu : chaque automate qui charge un fichier du jeu y prend le
    // dossier du jeu par GetAppPath (AppFolderResult), puis BuildString "file:///" + dossier + "Images/" + "poster1.png"
    // (ou {FolderName} "CD1/", "Radio/", "Extra/"). Pour ce que l'hote a envoye (lancement.ini : ContenuDossier,
    // ContenuRecu, ContenuImages), GetAppPath est coupe et la variable pointe sur le dossier recu ; puis l'automate est
    // relance : les images (Get, ImportPNG, hockey) se rechargent, les musiques deja importees sont reimportees.
    public static class Contenu
    {
        static float applyAt = -1, reapplyAt = -1;
        static readonly HashSet<PlayMakerFSM> done = new HashSet<PlayMakerFSM>();
        static bool dumped;

        public static void OnLevelLoaded()
        {
            done.Clear();
            cds = null; roots = null;
            bool on = PlayerSync.InGame && Folder().Length > 0;
            applyAt = on ? Time.realtimeSinceStartup + 1f : -1;
            reapplyAt = on ? Time.realtimeSinceStartup + 12f : -1;
        }

        static string Folder()
        {
            string d = Config.Get("Coop", "ContenuDossier", "");
            if (d.Length == 0 || !Directory.Exists(d)) return "";
            return d.TrimEnd('\\', '/') + "/";
        }

        public static void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (applyAt > 0 && now >= applyAt) { applyAt = -1; Apply("chargement"); }
            if (reapplyAt > 0 && now >= reapplyAt) { reapplyAt = -1; Apply("relecture"); Report(); }   // (automates crees ou allumes plus tard)
            WatchCds();
        }

        // Lecteur possible : variable de dossier (Folder, rempli par GetAppPath) ou chargement des musiques. (Lire les actions
        // de TOUS les automates force PlayMaker a les charger une a une : des dizaines de secondes, jeu fige.)
        static bool Reader(PlayMakerFSM f)
        {
            if (f.Fsm == null || f.Fsm.States == null) return false;
            return f.FsmName == "LoadSongs" || f.FsmVariables.FindFsmString("Folder") != null;
        }

        static HashSet<string> roots, images;
        static GameObject[] cds;   // CD/CD1-3 : leurs LoadSongs ne s'initialisent qu'a l'import (objets eteints jusque-la)

        static void Apply(string why)
        {
            string dir = Folder();
            if (dir.Length == 0 || Session.IsHost) return;   // (l'hote lit le sien)
            roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string r in Config.Get("Coop", "ContenuRecu", "").Split(',')) if (r.Trim().Length > 0) roots.Add(r.Trim().TrimEnd('/', '\\') + "/");
            images = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string r in Config.Get("Coop", "ContenuImages", "").Split(',')) if (r.Trim().Length > 0) images.Add(r.Trim());
            if (roots.Count == 0) return;
            var cdList = new List<GameObject>();
            int n = 0;
            foreach (UnityEngine.Object o in Game.AllFsms())
            {
                PlayMakerFSM f = o as PlayMakerFSM;
                if (f == null) continue;
                // (CD/CD1-3 : objet persistant, introuvable par son chemin ; reconnus a leur LoadSongs)
                if (f.FsmName == "LoadSongs" && f.gameObject.name.StartsWith("CD") && f.gameObject.name.Length == 3) cdList.Add(f.gameObject);
                if (!done.Contains(f) && Reader(f) && Point(f, dir)) n++;
            }
            cds = cdList.ToArray();
            if (n > 0) Log.Info("contenu de l'hote (" + why + ") : " + n + " lecteur(s) du jeu pointes sur " + dir);
        }

        // CD importes en cours de partie : leur lecteur pointe des qu'il s'allume (avant sa premiere piste : Init attend 0,2 s).
        static void WatchCds()
        {
            if (cds == null || roots == null) return;
            foreach (GameObject go in cds)
            {
                if (go == null || !go.activeInHierarchy) continue;
                PlayMakerFSM f = Game.FsmOn(go, "LoadSongs");
                if (f == null || done.Contains(f)) continue;
                string dir = Folder();
                if (dir.Length > 0 && Point(f, dir)) Log.Info("contenu de l'hote : musiques de " + go.name + " lues dans " + dir);
                else done.Add(f);   // (pas envoye par l'hote : ses propres musiques)
            }
        }

        // Un lecteur du jeu : s'il lit un fichier que l'hote a envoye, GetAppPath coupe, dossier recu, relance.
        static bool Point(PlayMakerFSM f, string dir)
        {
            if (f.Fsm == null || f.Fsm.States == null) return false;
            FsmStateAction getPath = null;
            string root = null, file = null;
            foreach (FsmState s in f.Fsm.States)
            {
                FsmStateAction[] acts = null;
                try { acts = s.Actions; } catch { }
                if (acts == null) continue;
                foreach (FsmStateAction a in acts)
                {
                    if (a == null) continue;
                    string tn = a.GetType().Name;
                    if (tn == "GetAppPath" && getPath == null) getPath = a;
                    if (tn != "BuildString" || root != null) continue;
                    FieldInfo pf = a.GetType().GetField("stringParts");
                    FsmString[] parts = pf != null ? pf.GetValue(a) as FsmString[] : null;
                    if (parts == null) continue;
                    for (int i = 0; i < parts.Length; i++)
                    {
                        string v = parts[i] != null ? parts[i].Value : null;
                        if (string.IsNullOrEmpty(v)) continue;
                        v = v.Replace('\\', '/');
                        foreach (string r in new[] { "CD1/", "CD2/", "CD3/", "Radio/", "Extra/", "Images/" })
                            if (string.Equals(v, r, StringComparison.OrdinalIgnoreCase))
                            {
                                root = r;
                                if (i + 1 < parts.Length && parts[i + 1] != null) file = parts[i + 1].Value;
                            }
                    }
                }
            }
            if (getPath == null || root == null || !roots.Contains(root)) return false;
            if (root == "Images/" && (file == null || !images.Contains(file))) return false;   // (image que l'hote n'envoie pas : la sienne)
            FieldInfo rf = getPath.GetType().GetField("AppFolderResult");
            FsmString var = rf != null ? rf.GetValue(getPath) as FsmString : null;
            if (var == null) return false;
            done.Add(f);
            getPath.Enabled = false;
            var.Value = dir;
            // relance : rechargement avec le dossier recu
            FsmString path = f.FsmVariables.FindFsmString("Path");
            bool songs = f.FsmName == "LoadSongs";
            bool imported = songs && path != null && path.Value != null && path.Value.Contains("IMPORTED") && path.Value != "NOT IMPORTED";
            if (songs && !imported) return true;   // (pas encore importees : le seront depuis le dossier recu)
            if (!f.enabled) f.enabled = true;   // (PlayMaker repart du debut ; objet eteint : a son allumage)
            else if (f.gameObject.activeInHierarchy && f.Fsm.StartState != null) Game.SetState(f, songs ? "Init" : f.Fsm.StartState);
            return true;
        }

        // Ce que lisent les lecteurs pointes (journal : adresse, et l'image chargee).
        static void Report()
        {
            foreach (PlayMakerFSM f in done)
            {
                if (f == null) continue;
                FsmString addr = f.FsmVariables.FindFsmString("Address");
                FsmTexture tex = f.FsmVariables.FindFsmTexture("Poster") ?? f.FsmVariables.FindFsmTexture("CarCustomPaint");
                Texture2D t2 = tex != null ? tex.Value as Texture2D : null;
                string px = "";
                if (t2 != null) { try { Color c = t2.GetPixel(t2.width / 2, t2.height / 2); px = ", image " + t2.width + "x" + t2.height + " (centre " + c.r.ToString("F2") + "/" + c.g.ToString("F2") + "/" + c.b.ToString("F2") + ")"; } catch { px = ", image " + t2.width + "x" + t2.height; } }
                Log.Info("contenu de l'hote : " + Recon.Path(f.transform) + "::" + f.FsmName + " [" + f.ActiveStateName + "] " + (addr != null ? addr.Value : "") + px);
            }
        }

        // ---- essais : [Test] Autotest=contenu -- les automates qui lisent un fichier du jeu, et ou ils lisent
        static string Val(object v)
        {
            var nv = v as NamedVariable;
            if (nv != null) return nv.UseVariable && !string.IsNullOrEmpty(nv.Name) ? "{" + nv.Name + "}" : nv.ToString();
            if (v is FsmEvent) return ((FsmEvent)v).Name;
            if (v is bool || v is int || v is float || v is Enum) return v.ToString();
            var arr = v as Array;
            if (arr != null)
            {
                var parts = new List<string>();
                foreach (object o in arr) parts.Add(o == null ? "null" : Val(o) ?? o.GetType().Name);
                return "[" + string.Join(", ", parts.ToArray()) + "]";
            }
            return null;
        }

        static string Dump(FsmState s)
        {
            var sb = new System.Text.StringBuilder(s.Name + " :");
            FsmStateAction[] acts = null;
            try { acts = s.Actions; } catch { }
            if (acts != null) foreach (FsmStateAction a in acts)
            {
                if (a == null) continue;
                sb.Append(" [").Append(a.GetType().Name);
                foreach (FieldInfo fi in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    string txt = Val(fi.GetValue(a));
                    if (txt != null) sb.Append(' ').Append(fi.Name).Append('=').Append(txt);
                }
                sb.Append(']');
            }
            return sb.ToString();
        }

        static int cdStep;
        public static void Test(string mode, float t)
        {
            if (mode == "contenucd" && !Session.IsHost)
            {   // import des CD (comme le jeu : LOAD a CD::Playlist), puis d'ou le lecteur de CD1 a lu ses pistes
                if (t > 30f && cdStep == 0) { cdStep = 1; PlayMakerFSM pl = null; foreach (UnityEngine.Object o in Game.AllFsms()) { PlayMakerFSM f = o as PlayMakerFSM; if (f != null && f.FsmName == "Playlist" && f.gameObject.name == "CD") pl = f; } if (pl != null) { foreach (FsmState st in pl.Fsm.States) Log.Info("autotest : contenucd, Playlist " + Dump(st)); foreach (FsmTransition tr in pl.Fsm.GlobalTransitions) Log.Info("autotest : contenucd, Playlist global " + tr.EventName + " -> " + tr.ToState); Log.Info("autotest : contenucd, Playlist [" + pl.ActiveStateName + "]"); FsmBool imp = pl.FsmVariables.FindFsmBool("Import"); if (imp != null) imp.Value = true; if (!pl.enabled) pl.enabled = true; Game.SetState(pl, "State 3"); Log.Info("autotest : contenucd, import des CD"); } else Log.Info("autotest : contenucd, CD::Playlist introuvable"); }
                if (t > 45f && cdStep == 1)
                {
                    cdStep = 2;
                    foreach (GameObject go in cds ?? new GameObject[0])
                    {
                        string cn = go != null ? go.name : "?";
                        PlayMakerFSM f = go != null ? Game.FsmOn(go, "LoadSongs") : null;
                        FsmString p = f != null ? f.FsmVariables.FindFsmString("Path") : null;
                        Log.Info("autotest : contenucd, " + cn + " " + (f != null && done.Contains(f) ? "POINTE" : "jeu") + " : " + (p != null ? p.Value : "?"));
                    }
                }
                return;
            }
            if (mode != "contenu" || dumped || t < 40f) return;
            dumped = true;
            int n = 0;
            foreach (UnityEngine.Object o in Game.AllFsms())
            {
                PlayMakerFSM f = o as PlayMakerFSM;
                if (f == null || !Reader(f)) continue;
                bool reads = false;
                foreach (FsmState s in f.Fsm.States)
                {
                    FsmStateAction[] acts = null;
                    try { acts = s.Actions; } catch { }
                    if (acts != null) foreach (FsmStateAction a in acts) if (a != null && a.GetType().Name == "GetAppPath") reads = true;
                }
                if (!reads) continue;
                n++;
                FsmString addr = f.FsmVariables.FindFsmString("Address"), path = f.FsmVariables.FindFsmString("Path");
                FsmTexture tex = f.FsmVariables.FindFsmTexture("Poster");
                string px = "";
                Texture2D t2 = tex != null ? tex.Value as Texture2D : null;
                if (t2 != null) { try { Color c = t2.GetPixel(t2.width / 2, t2.height / 2); px = ", image " + t2.width + "x" + t2.height + " centre " + c.r.ToString("F2") + "/" + c.g.ToString("F2") + "/" + c.b.ToString("F2"); } catch { px = ", image " + t2.width + "x" + t2.height; } }
                Log.Info("autotest : contenu, " + Recon.Path(f.transform) + "::" + f.FsmName + " [" + f.ActiveStateName + "] " + (done.Contains(f) ? "POINTE" : "jeu")
                         + " : " + (addr != null ? addr.Value : path != null ? path.Value : "?") + px);
                if (f.FsmName == "LoadSongs") foreach (FsmState s in f.Fsm.States) Log.Info("autotest : contenu,    " + Dump(s));
            }
            Log.Info("autotest : contenu, " + n + " automates lisent un fichier du jeu");
        }
    }
}
