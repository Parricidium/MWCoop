using System;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;

namespace Voyage
{
    // Journal du mod : <dossier du jeu>\Mods\Voyage.log (et la console de MSCLoader)
    static class Journal
    {
        static string path;
        public static void Info(string s)
        {
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + s;
            try
            {
                if (path == null) path = Path.Combine(Path.GetDirectoryName(Application.dataPath), Path.Combine("Mods", "Voyage.log"));
                File.AppendAllText(path, line + "\r\n");
            }
            catch { }
            try { MSCLoader.ModConsole.Print("[Voyage] " + s); } catch { }
        }
        public static void Start()
        {
            try { path = Path.Combine(Path.GetDirectoryName(Application.dataPath), Path.Combine("Mods", "Voyage.log")); File.WriteAllText(path, ""); } catch { }
        }
    }

    // Le jeu : joueur, camera, globales de l'interface (texte et icones de la main, sous-titres), argent.
    static class Jeu
    {
        // My Summer Car (le meme mod pour les deux jeux, JD 24/10) : lieux, matieres et sons propres a chaque jeu
        static int msc = -1;
        public static bool Msc { get { if (msc < 0) msc = Application.dataPath.IndexOf("mysummercar", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0; return msc == 1; } }
        static Transform player;
        static int playerLook = -1;

        // Joueur local (PLAYER, avec son Pivot : assis dans une voiture, il est range sous le siege)
        public static Transform Player
        {
            get
            {
                if (player == null && Time.frameCount != playerLook)
                {
                    playerLook = Time.frameCount;
                    GameObject g = GameObject.Find("PLAYER");
                    player = g != null && g.transform.Find("Pivot") != null ? g.transform : null;
                    for (Transform t = player == null && Camera.main != null ? Camera.main.transform : null; t != null; t = t.parent)
                        if (t.name == "PLAYER" && t.Find("Pivot") != null) { player = t; break; }
                }
                return player;
            }
        }

        public static Camera Cam { get { return Camera.main; } }

        public static FsmBool GuiUse, GuiBuy, Stop, InCar;
        public static FsmString GuiText, GuiSub;
        public static FsmFloat Money;

        public static bool Ready
        {
            get
            {
                if (GuiText == null)
                {
                    var gv = FsmVariables.GlobalVariables;
                    GuiUse = gv.FindFsmBool("GUIuse"); GuiBuy = gv.FindFsmBool("GUIbuy"); GuiText = gv.FindFsmString("GUIinteraction");
                    GuiSub = gv.FindFsmString("GUIsubtitle"); Money = gv.FindFsmFloat("PlayerMoney");
                    Stop = gv.FindFsmBool("PlayerStop"); InCar = gv.FindFsmBool("PlayerInCar");
                }
                return GuiText != null && Player != null;
            }
        }

        public static void Reset() { GuiText = null; player = null; }

        public static void ClearHand() { if (GuiUse != null) GuiUse.Value = false; if (GuiBuy != null) GuiBuy.Value = false; if (GuiText != null) GuiText.Value = ""; }

        static float subUntil;
        static string subText;
        public static void Say(string fr, string en)
        {
            subText = T(fr, en);
            if (GuiSub != null) GuiSub.Value = subText;
            subUntil = Time.realtimeSinceStartup + 4f;
            Journal.Info("dit : " + subText);
        }
        public static void TickSay()
        {
            if (subUntil > 0f && Time.realtimeSinceStartup >= subUntil)
            {
                subUntil = 0f;
                if (GuiSub != null && GuiSub.Value == subText) GuiSub.Value = "";
            }
        }

        // Teleportation du joueur (pieds en 'feet', regard vers 'yaw') : controleur coupe le temps du deplacement
        public static void Teleport(Vector3 feet, float yaw)
        {
            Transform p = Player;
            if (p == null) return;
            var cc = p.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            p.position = feet + Vector3.up * 1.0f;
            p.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (cc != null) cc.enabled = true;
            Journal.Info("teleporte en " + feet);
        }

        // Rayon de visee (centre de l'ecran) et distance de la main du jeu
        public static bool Aim(out Ray ray)
        {
            Camera c = Cam;
            if (c == null) { ray = new Ray(); return false; }
            ray = c.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            return true;
        }
        public const float Reach = 2.2f;

        // Langue : celle de MWCoop s'il est la (Lang.Fr / Lang.Ru), sinon celle de Windows (francais, russe, sinon anglais).
        // Arabe : l'anglais, comme MWCoop en jeu (le texte d'Unity 5 ne lie pas les lettres et n'ecrit pas de droite a gauche).
        static int lang = -1;   // 0 anglais, 1 francais, 2 russe
        static int Code
        {
            get
            {
                if (lang < 0)
                {
                    string w = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                    lang = w == "fr" ? 1 : w == "ru" ? 2 : 0;
                    try
                    {
                        Type l = Type.GetType("MWCoop.Lang, MWCoop");
                        PropertyInfo fr = l != null ? l.GetProperty("Fr", BindingFlags.Public | BindingFlags.Static) : null;
                        PropertyInfo ru = l != null ? l.GetProperty("Ru", BindingFlags.Public | BindingFlags.Static) : null;
                        if (fr != null) lang = (bool)fr.GetValue(null, null) ? 1 : ru != null && (bool)ru.GetValue(null, null) ? 2 : 0;
                    }
                    catch { }
                }
                return lang;
            }
        }
        public static bool Fr { get { return Code == 1; } }
        public static bool Ru { get { return Code == 2; } }
        // textes ecrits avec la police du jeu (sous-titres GUIsubtitle, main GUIinteraction) : pas de cyrillique, l'anglais en russe
        public static string T(string f, string e) { return Fr ? f : e; }
        // textes en Arial (panneaux TextMesh) et de l'IMGUI (noir du vol) : le russe aussi
        public static string T(string f, string e, string r) { return Fr ? f : Ru ? r : e; }
        // ou est l'aerodrome / l'agence, pour les phrases des PNJ (My Summer Car : l'ancien aeroport du jeu)
        public static string AirFr { get { return Msc ? "à l'ancien aéroport, près de la décharge" : "près du cinéma"; } }
        public static string AirEn { get { return Msc ? "at the old airport, near the landfill" : "near the cinema"; } }
        public static string AgencyFr { get { return Msc ? "à Perajarvi, près du magasin de Teimo" : "à Perajarvi, près du musée"; } }
        public static string AgencyEn { get { return Msc ? "in Perajarvi, near Teimo's shop" : "in Perajarvi, near the museum"; } }

        // sons du jeu (MasterAudio, dans Assembly-CSharp-firstpass) : par reflexion, la DLL ne depend que de bibliotheques
        // identiques dans My Summer Car et My Winter Car (Unity, PlayMaker, MSCLoader)
        static MethodInfo playAt;
        static bool playLooked;
        public static void Sound(string group, string variation, Vector3 at)
        {
            try
            {
                if (!playLooked)
                {
                    playLooked = true;
                    Type ma = Type.GetType("DarkTonic.MasterAudio.MasterAudio, Assembly-CSharp-firstpass") ?? Type.GetType("MasterAudio, Assembly-CSharp-firstpass") ?? Type.GetType("MasterAudio, Assembly-CSharp");
                    if (ma != null)
                        foreach (MethodInfo m in ma.GetMethods(BindingFlags.Public | BindingFlags.Static))
                            if (m.Name == "PlaySound3DAtVector3AndForget" && m.GetParameters().Length == 6) { playAt = m; break; }
                    if (playAt == null) Journal.Info("sons du jeu : MasterAudio introuvable");
                }
                if (playAt != null) playAt.Invoke(null, new object[] { group, at, 1f, null, 0f, variation });
            }
            catch { }
        }
    }

    // MWCoop, s'il est installe : son API publique (CoopApi) et le corps de PNJ de ses avatars (Avatar.CreatePreview),
    // trouves par reflexion -- sans MWCoop, le mod marche seul (PNJ de remplacement, pas de messages).
    static class Coop
    {
        static Type api, avatar;
        static bool looked;

        static void Look()
        {
            if (looked) return;
            looked = true;
            try { api = Type.GetType("MWCoop.CoopApi, MWCoop"); avatar = Type.GetType("MWCoop.Avatar, MWCoop"); } catch { }
            Journal.Info("MWCoop : " + (api != null ? "present (API " + Get<int>("Version", -1, true) + ")" : "absent"));
        }

        public static bool Present { get { Look(); return api != null; } }

        static T Get<T>(string name, T def, bool field = false)
        {
            try
            {
                if (field) { FieldInfo f = api.GetField(name, BindingFlags.Public | BindingFlags.Static); return f != null ? (T)f.GetValue(null) : def; }
                PropertyInfo p = api.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
                return p != null ? (T)p.GetValue(null, null) : def;
            }
            catch { return def; }
        }

        public static bool InSession { get { return Present && Get("InSession", false); } }
        public static bool IsHost { get { return !InSession || Get("IsHost", false); } }
        public static int LocalId { get { return Present ? Get("LocalPlayerId", 0) : 0; } }

        public static string LocalName
        {
            get
            {
                if (!InSession) return "";
                try { return (string)api.GetMethod("PlayerName").Invoke(null, new object[] { LocalId }) ?? ""; } catch { return ""; }
            }
        }

        // messages entre joueurs (CoopApi.Send / Listen, canal « Voyage ») : depart et retour de l'avion
        const string Channel = "Voyage";
        public static float LastMessageAt = -1f;
        public static void Send(string msg)
        {
            if (!InSession) return;
            try { api.GetMethod("Send").Invoke(null, new object[] { Channel, System.Text.Encoding.UTF8.GetBytes(msg) }); } catch (Exception e) { Journal.Info("envoi : " + e.Message); }
        }
        public static void Listen(Action<string> handler)
        {
            if (!Present) return;
            try
            {
                api.GetMethod("Listen").Invoke(null, new object[] { Channel, new Action<int, byte[]>((from, data) => { string m = System.Text.Encoding.UTF8.GetString(data); Journal.Info("message de #" + from + " : " + m); LastMessageAt = Time.realtimeSinceStartup; handler(m); }) });
            }
            catch (Exception e) { Journal.Info("ecoute : " + e.Message); }
        }

        // lieu sur la carte M et dans /lieu de MWCoop (API 3 : AddMapPlace ; rien avec un MWCoop plus ancien ou absent)
        public static void MapPlace(string key, string alias, string fr, string en, Vector3 at, Vector3 face)
        {
            if (!Present) return;
            try { MethodInfo m = api.GetMethod("AddMapPlace"); if (m != null) m.Invoke(null, new object[] { key, alias, fr, en, at, face }); else Journal.Info("lieu sur la carte : MWCoop trop ancien (API < 3)"); }
            catch (Exception e) { Journal.Info("lieu sur la carte : " + e.Message); }
        }

        // corps de PNJ de MWCoop (le meme que le vendeur de son epicerie) ; null sans MWCoop
        public static GameObject Npc(string name)
        {
            Look();
            if (avatar == null) return null;
            try
            {
                MethodInfo m = avatar.GetMethod("CreatePreview", BindingFlags.Public | BindingFlags.Static);
                if (m == null) return null;
                object[] args = new object[m.GetParameters().Length];
                args[0] = name; args[1] = 0;
                for (int i = 2; i < args.Length; i++) args[i] = null;
                object a = m.Invoke(null, args);
                if (a == null) return null;
                FieldInfo root = avatar.GetField("Root");
                return root != null ? root.GetValue(a) as GameObject : null;
            }
            catch (Exception e) { Journal.Info("PNJ de MWCoop : " + e.Message); return null; }
        }
    }
}
