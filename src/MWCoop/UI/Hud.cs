using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Affichage en jeu : etat de la session, messages temporaires, pseudos au-dessus des joueurs.
    public static class Hud
    {
        class ToastMsg { public string Text; public float Until; }
        static readonly List<ToastMsg> toasts = new List<ToastMsg>();
        static GUIStyle label, shadow, tag, tagShadow;

        public static void Toast(string text)
        {
            Log.Info("message : " + text);
            toasts.Add(new ToastMsg { Text = text, Until = Time.realtimeSinceStartup + 6f });
            if (toasts.Count > 6) toasts.RemoveAt(0);
        }

        static void Styles()
        {
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            label.normal.textColor = Color.white;
            shadow = new GUIStyle(label);
            shadow.normal.textColor = new Color(0, 0, 0, 0.8f);
            tag = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 15, fontStyle = FontStyle.Bold };
            tagShadow = new GUIStyle(tag);
            tagShadow.normal.textColor = new Color(0, 0, 0, 0.8f);
        }

        static void Text(Rect r, string s, GUIStyle st, GUIStyle sh)
        {
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), s, sh);
            GUI.Label(r, s, st);
        }

        public static void Draw()
        {
            Styles();
            string head = "MWCoop " + Version.Text;
            if (Session.Active) head += "  -  " + Session.Status + "  -  " + Session.Players.Count + " joueur(s)";
            Text(new Rect(8, 4, 900, 22), head, label, shadow);

            float now = Time.realtimeSinceStartup;
            toasts.RemoveAll(t => t.Until < now);
            float y = Screen.height * 0.62f;
            foreach (ToastMsg t in toasts)
            {
                Text(new Rect(20, y, Screen.width - 40, 24), t.Text, label, shadow);
                y += 22;
            }

            Camera cam = Camera.main;
            if (cam == null) return;
            foreach (Avatar a in PlayerSync.Avatars)
            {
                if (a.Root == null) continue;
                Vector3 sp = cam.WorldToScreenPoint(a.HeadPosition);
                if (sp.z <= 0.5f || sp.z > 120f) continue;
                var r = new Rect(sp.x - 100, Screen.height - sp.y - 12, 200, 24);
                Text(r, a.Player.Name, tag, tagShadow);
            }
        }
    }
}
