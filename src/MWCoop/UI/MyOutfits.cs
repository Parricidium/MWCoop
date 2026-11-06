using System.Collections.Generic;
using System.IO;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // « Mes tenues » du lanceur dans F10 > Apparence (suggestion 39) : <dossier de mwcoop.ini>\tenues-perso.txt, ecrit par le lanceur, jamais par le mod. Une tenue par ligne :
    // nom<TAB>haut|pantalon|visage|corps|chapeau|lunettes|cheveux (Looks.Compose), 12 au plus, nom de 28 caracteres au plus. Mettre une tenue : chaque partie prend sa valeur si
    // elle existe sur ce PC (sinon la partie reste telle quelle ; vide : celle du modele ou aucun ; « perso » : seulement si l'image existe), ecrite aussi dans [Coop] comme le lanceur.
    public static class MyOutfits
    {
        const int MaxOutfits = 12, MaxName = 28;
        class Outfit { public string Name, Look; }
        static readonly List<Outfit> list = new List<Outfit>();
        static string stamp = ""; static float nextLoad;
        static int cur = -1; static string wornSkin;

        public static int Count { get { return list.Count; } }
        public static string Current { get { return cur >= 0 && cur < list.Count && Session.Me != null && Session.Me.Skin == wornSkin ? list[cur].Name : "—"; } }

        static string FilePath { get { return Path.Combine(Path.GetDirectoryName(Config.IniPath), "tenues-perso.txt"); } }

        // Relu quand le fichier change (une fois par seconde au plus, seulement menu ouvert sur l'onglet).
        public static void Load()
        {
            if (Time.realtimeSinceStartup < nextLoad) return;
            nextLoad = Time.realtimeSinceStartup + 1f;
            try
            {
                string f = FilePath;
                string st = File.Exists(f) ? new FileInfo(f).Length + ":" + File.GetLastWriteTimeUtc(f).Ticks : "";
                if (st == stamp) return;
                stamp = st;
                list.Clear(); cur = -1;
                if (st.Length == 0) return;
                foreach (string raw in File.ReadAllLines(f, System.Text.Encoding.UTF8))
                {
                    int tab = raw.IndexOf('\t');
                    if (tab <= 0 || tab >= raw.Length - 1) continue;
                    string name = raw.Substring(0, tab).Trim(), look = raw.Substring(tab + 1).Trim();
                    if (name.Length == 0 || name.Length > MaxName || look.Length == 0 || look.IndexOf('\t') >= 0) continue;
                    bool ctl = false;
                    foreach (char c in name) if (char.IsControl(c)) ctl = true;
                    if (ctl) continue;
                    list.Add(new Outfit { Name = name, Look = look });
                    if (list.Count >= MaxOutfits) break;
                }
                Log.Info("mes tenues : " + list.Count + " lues");
            }
            catch (System.Exception e) { Log.Warn("mes tenues : " + e.Message); }
        }

        public static void Step(int d)
        {
            if (list.Count == 0) return;
            cur = ((cur < 0 ? (d > 0 ? -1 : 0) : cur) + d + list.Count) % list.Count;
            Wear(list[cur]);
        }

        static void Wear(Outfit o)
        {
            if (Session.Me == null) return;
            Looks.Look want = Looks.Parse(o.Look), now = Looks.Parse(Session.Me.Skin);
            // Haut
            if (want.Shirt == "perso")
            {
                if (CustomFace.HasImage(CustomFace.Shirt)) { Config.Save("Coop", "Apparence", "perso"); CustomFace.Forget(); now.Shirt = CustomFace.MyKeyOf(CustomFace.Shirt) ?? now.Shirt; }
            }
            else if (want.Shirt.Length == 0 || Menu.HasSkin(want.Shirt)) { Config.Save("Coop", "Apparence", want.Shirt); now.Shirt = want.Shirt.Length > 0 ? want.Shirt : Looks.DefaultShirt; }
            // Le reste
            for (int f = 0; f < Looks.PartKeys.Length; f++)
            {
                string v = Looks.Get(want, f);
                if (v == "perso")
                {
                    int img = f == 1 ? CustomFace.Pants : f == 2 ? CustomFace.Face : -1;
                    if (img < 0 || !CustomFace.HasImage(img)) continue;
                    Config.Save("Coop", Looks.PartKeys[f], "perso");
                    CustomFace.Forget();
                    Looks.Set(now, f, CustomFace.MyKeyOf(img) ?? "");
                    continue;
                }
                if (v.Length > 0 && !Looks.Choices(f).Contains(v)) continue;
                Config.Save("Coop", Looks.PartKeys[f], v);
                Looks.Set(now, f, v);
            }
            Session.Me.Skin = Looks.Compose(now);
            wornSkin = Session.Me.Skin;
            Session.SendProfile();
            Log.Info("mes tenues : " + o.Name + " -> " + Session.Me.Skin);
        }

        // Essais (Autotest=mestenues, tenues-perso.txt ecrit a la main) : a 30 s la liste, de 35 a 55 s une tenue de plus toutes les 5 s.
        static int testStep; static float testNext;
        public static void Test(string mode, float t)
        {
            if (mode != "mestenues" || !Net.Session.IsHost) return;
            if (t > 30f && testStep == 0) { testStep = 1; nextLoad = 0f; Load(); Log.Info("autotest : mestenues, " + list.Count + " tenues"); Menu.Show(Menu.TabLook); Autotest.CaptureSoon("mestenues-menu", 3f); }
            if (t > 35f && t < 55f && Time.realtimeSinceStartup >= testNext) { testNext = Time.realtimeSinceStartup + 5f; Step(1); Log.Info("autotest : mestenues, " + Current + " : " + Session.Me.Skin); }
        }
    }
}
