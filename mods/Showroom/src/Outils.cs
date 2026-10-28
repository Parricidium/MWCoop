using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;

namespace Showroom
{
    // Journal du mod : <dossier du jeu>\Mods\Showroom.log (et la console de MSCLoader)
    static class Journal
    {
        static string path;
        public static void Start()
        {
            try { path = Path.Combine(Path.GetDirectoryName(Application.dataPath), Path.Combine("Mods", "Showroom.log")); File.WriteAllText(path, ""); } catch { }
        }
        public static void Info(string s)
        {
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + s;
            try { if (path != null) File.AppendAllText(path, line + "\r\n"); } catch { }
            try { MSCLoader.ModConsole.Print("[Showroom] " + s); } catch { }
        }
    }

    // Le jeu : joueur, camera, globales de l'interface (main, sous-titres), teleportation, langue.
    static class Jeu
    {
        static Transform player;
        public static Transform Player
        {
            get
            {
                if (player == null)
                {
                    GameObject g = GameObject.Find("PLAYER");
                    player = g != null && g.transform.Find("Pivot") != null ? g.transform : null;
                }
                return player;
            }
        }

        public static FsmBool GuiUse, InCar;
        public static FsmString GuiText, GuiSub;
        public static bool Ready
        {
            get
            {
                if (GuiText == null)
                {
                    var gv = FsmVariables.GlobalVariables;
                    GuiUse = gv.FindFsmBool("GUIuse"); GuiText = gv.FindFsmString("GUIinteraction"); GuiSub = gv.FindFsmString("GUIsubtitle");
                    InCar = gv.FindFsmBool("PlayerInCar");
                }
                return GuiText != null && Player != null;
            }
        }
        public static void Reset() { GuiText = null; player = null; }
        public static void ClearHand() { if (GuiUse != null) GuiUse.Value = false; if (GuiText != null) GuiText.Value = ""; }

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

        // pieds en 'feet', regard vers 'yaw' (controleur coupe le temps du deplacement)
        public static void Teleport(Vector3 feet, float yaw)
        {
            Transform p = Player;
            if (p == null) return;
            var cc = p.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            p.position = feet + Vector3.up * 1.0f;
            p.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (cc != null) cc.enabled = true;
            Journal.Info("teleporte en " + feet.ToString("F1"));
        }

        public const float Reach = 2.2f;

        // langue : celle de MWCoop s'il est la, sinon celle de Windows (francais, sinon anglais)
        static int fr = -1;
        public static bool Fr
        {
            get
            {
                if (fr < 0)
                {
                    fr = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr" ? 1 : 0;
                    try
                    {
                        Type l = Type.GetType("MWCoop.Lang, MWCoop");
                        PropertyInfo p = l != null ? l.GetProperty("Fr", BindingFlags.Public | BindingFlags.Static) : null;
                        if (p != null) fr = (bool)p.GetValue(null, null) ? 1 : 0;
                    }
                    catch { }
                }
                return fr == 1;
            }
        }
        public static string T(string f, string e) { return Fr ? f : e; }
    }

    // MWCoop (API publique CoopApi, par reflexion) : voitures du F10 (VAZ-2108 et addons), sorties a une place donnee (API 4).
    static class Coop
    {
        static Type api;
        static bool looked;
        static void Look()
        {
            if (looked) return;
            looked = true;
            try { api = Type.GetType("MWCoop.CoopApi, MWCoop"); } catch { }
            int v = -1;
            try { FieldInfo f = api != null ? api.GetField("Version", BindingFlags.Public | BindingFlags.Static) : null; if (f != null) v = (int)f.GetValue(null); } catch { }
            Journal.Info("MWCoop : " + (api != null ? "present (API " + v + ")" : "absent"));
        }
        public static bool Present { get { Look(); return api != null; } }
        public static bool HasCars { get { Look(); return api != null && api.GetMethod("ShowF10Car") != null; } }

        // [(id, nom)] des voitures du F10 installees
        public static List<KeyValuePair<int, string>> Cars()
        {
            var l = new List<KeyValuePair<int, string>>();
            if (!HasCars) return l;
            try
            {
                foreach (string s in (string[])api.GetMethod("F10Cars").Invoke(null, null))
                {
                    int k = s.IndexOf('|'), id;
                    if (k > 0 && int.TryParse(s.Substring(0, k), out id)) l.Add(new KeyValuePair<int, string>(id, s.Substring(k + 1)));
                }
            }
            catch (Exception e) { Journal.Info("voitures du F10 : " + e.Message); }
            return l;
        }

        public static bool Show(int id, Vector3 pos, float yaw)
        {
            if (!HasCars) return false;
            try { return (bool)api.GetMethod("ShowF10Car").Invoke(null, new object[] { id, pos, yaw }); }
            catch (Exception e) { Journal.Info("sortie de la voiture : " + e.Message); return false; }
        }
    }

    // Textes 3D (Arial d'Unity, lettres blanches recopiees de l'atlas : comme le mod Voyage -- pas GUI/Text Shader, qui se
    // dessine a travers tout ; non eclaires : Sprites/Default prend la couleur des sommets, celle du TextMesh)
    static class Textes
    {
        static Font arial;
        static readonly List<Material> mats = new List<Material>();
        static RenderTexture letters;
        static Material blit;
        static float next;
        static bool dirty;

        public static TextMesh Make(Transform parent, string text, Vector3 at, Quaternion rot, float size, Color c)
        {
            if (arial == null)
            {
                arial = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (arial != null) Font.textureRebuilt += f => { if (f == arial) dirty = true; };
            }
            if (arial == null) return null;
            var g = new GameObject("showroom_texte");
            g.transform.SetParent(parent, false);
            g.transform.position = at; g.transform.rotation = rot;
            var tm = g.AddComponent<TextMesh>();
            tm.font = arial; tm.fontSize = 64; tm.characterSize = size / 6f; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold; tm.color = c; tm.text = text;
            arial.RequestCharactersInTexture(text, 64, FontStyle.Bold);
            Shader sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent") ?? arial.material.shader;
            var m = new Material(sh) { name = "showroom_texte", mainTexture = Letters(true), color = Color.white };
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
            mats.Add(m);
            return tm;
        }

        public static void Set(TextMesh tm, string text)
        {
            if (tm == null || tm.text == text) return;
            arial.RequestCharactersInTexture(text, 64, FontStyle.Bold);
            tm.text = text;
            dirty = true;
        }

        static Texture Letters(bool now)
        {
            Texture src = arial.material.mainTexture;
            if (src == null) return null;
            if (letters == null || letters.width != src.width || letters.height != src.height)
            {
                if (letters != null) letters.Release();
                letters = new RenderTexture(src.width, src.height, 0, RenderTextureFormat.ARGB32) { name = "showroom_lettres", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                foreach (Material m in mats) if (m != null) m.mainTexture = letters;
                now = true;
            }
            if (now || dirty || Time.realtimeSinceStartup >= next)
            {
                next = Time.realtimeSinceStartup + 2f;
                dirty = false;
                if (blit == null) blit = new Material(arial.material) { name = "showroom_lettres", color = Color.white };
                RenderTexture keep = RenderTexture.active;
                RenderTexture.active = letters;
                GL.Clear(true, true, new Color(1f, 1f, 1f, 0f));
                RenderTexture.active = keep;
                Graphics.Blit(src, letters, blit);
            }
            return letters;
        }

        public static void Tick() { if (arial != null && mats.Count > 0) { mats.RemoveAll(m => m == null); Letters(false); } }
        public static void Clear() { mats.Clear(); }
    }
}
