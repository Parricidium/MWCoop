using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Affichage en jeu : etat de la session, messages temporaires, pseudos au-dessus des joueurs.
    public static class Hud
    {
        class ToastMsg { public string Text; public float Born, Until; }
        static readonly List<ToastMsg> toasts = new List<ToastMsg>();
        static readonly Color pill = new Color(0.03f, 0.06f, 0.12f, 0.62f);

        public static void Toast(string text)
        {
            Log.Info("message : " + text);
            float now = Time.realtimeSinceStartup;
            toasts.Add(new ToastMsg { Text = text, Born = now, Until = now + 6f });
            if (toasts.Count > 6) toasts.RemoveAt(0);
        }

        public static void Draw()
        {
            float now = Time.realtimeSinceStartup;
            // Statut en haut a gauche : pastille discrete (point vert : relie a l'hote).
            string head = "MWCoop " + Version.Text;
            if (Session.Active) head += "  \u00B7  " + Session.Status + "  \u00B7  " + Session.Players.Count + " joueur(s)";
            float ph = Style.Px(28), tw = Style.Width(head, 14, false);
            var hr = new Rect(Style.Px(10), Style.Px(8), tw + Style.Px(44), ph);
            Style.Round(hr, ph / 2, pill);
            bool linked = Session.Active && (Session.IsHost || Session.HostConnected);
            float dd = Style.Px(8);
            Style.Round(new Rect(hr.x + Style.Px(13), hr.center.y - dd / 2, dd, dd), dd / 2, !Session.Active ? Style.Dim : linked ? Style.Good : Style.Warn);
            Style.Text(new Rect(hr.x + Style.Px(30), hr.y, tw + Style.Px(10), ph), head, 14, TextAnchor.MiddleLeft, new Color(0.88f, 0.92f, 0.97f), false);

            // Messages : pilules empilees, entree en glissant, sortie en fondu.
            toasts.RemoveAll(t => t.Until < now);
            float y = Screen.height * 0.62f, h = Style.Px(36);
            foreach (ToastMsg t in toasts)
            {
                float inK = Mathf.Clamp01((now - t.Born) / 0.2f);
                Style.Alpha = inK * Mathf.Clamp01((t.Until - now) / 0.6f);
                float w = Mathf.Min(Style.Width(t.Text, 16, false) + Style.Px(40), Screen.width - Style.Px(48));
                var r = new Rect(Style.Px(24) - (1f - inK) * Style.Px(24), y, w, h);
                Style.Round(r, h / 2, pill);
                Style.Ring(r, h / 2, Style.Line);
                Style.Round(new Rect(r.x + Style.Px(14), r.center.y - Style.Px(3), Style.Px(6), Style.Px(6)), Style.Px(3), Style.Accent);
                Style.Text(new Rect(r.x + Style.Px(28), r.y, r.width - Style.Px(40), h), t.Text, 16, TextAnchor.MiddleLeft, Style.White, false);
                y += h + Style.Px(6);
            }
            Style.Alpha = 1f;

            // Pseudos au-dessus des joueurs : pilule sombre, effacee au-dela de 60 m.
            Camera cam = Camera.main;
            if (cam == null) return;
            foreach (Avatar a in PlayerSync.Avatars)
            {
                if (a.Root == null) continue;
                Vector3 sp = cam.WorldToScreenPoint(a.HeadPosition);
                if (sp.z <= 0.5f || sp.z > 120f) continue;
                string n = a.Player.Name;
                float nw = Style.Width(n, 15) + Style.Px(22), nh = Style.Px(26);
                var r = new Rect(Mathf.Round(sp.x - nw / 2), Mathf.Round(Screen.height - sp.y - nh), nw, nh);
                Style.Alpha = Mathf.Clamp01(1f - (sp.z - 60f) / 60f);
                Style.Round(r, nh / 2, pill);
                Style.Text(r, n, 15, TextAnchor.MiddleCenter, Style.White);
            }
            Style.Alpha = 1f;
        }
    }
}
