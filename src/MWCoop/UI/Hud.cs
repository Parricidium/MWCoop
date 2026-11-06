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
        static readonly Color pill = Kit.A(Style.Shade, 0.62f);

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
            string head = Lang.Name + " " + Version.Text;   // (MSCoop sur My Summer Car)
            if (Session.Active) head += "  \u00B7  " + Lang.Status(Session.Status) + "  \u00B7  " + Lang.Players(Session.Players.Count);
            float ph = Style.Px(28), tw = Style.Width(head, 14, false);
            var hr = new Rect(Style.Px(10), Style.Px(8), tw + Style.Px(44), ph);
            Style.Round(hr, ph / 2, pill);
            bool linked = Session.Active && (Session.IsHost || Session.HostConnected);
            float dd = Style.Px(8);
            Style.Round(new Rect(hr.x + Style.Px(13), hr.center.y - dd / 2, dd, dd), dd / 2, !Session.Active ? Style.Dim : linked ? Style.Good : Style.Warn);
            Style.Text(new Rect(hr.x + Style.Px(30), hr.y, tw + Style.Px(10), ph), head, 14, TextAnchor.MiddleLeft, Kit.Soft, false);

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

            // Coups entre joueurs actives (квазарик, 21/10 : « une barre de vie et un effet quand on prend un coup ») : cinq cases en bas
            // au milieu -- les coups recus dans les 20 s en rouge (5 = mort) --, et un voile rouge qui s'estompe apres chaque coup.
            // (les cases : seulement avec [Hud] JaugeVie -- Menor, 25/10 : « la vie reapparait en jeu » malgre le reglage coupe ; le voile reste)
            if (Pvp.Allowed && Session.Active)
            {
                int got = Pvp.HitsNow, max = Pvp.MaxHits; float since = now - Pvp.LastHitAt;
                if (HudSettings.On("JaugeVie"))
                {
                    float segW = Style.Px(34), segH = Style.Px(9), gap = Style.Px(4);
                    float totalW = max * segW + (max - 1) * gap, x0 = (Screen.width - totalW) / 2f, y0 = Screen.height - Style.Px(40);
                    Style.Round(new Rect(x0 - Style.Px(10), y0 - Style.Px(7), totalW + Style.Px(20), segH + Style.Px(14)), (segH + Style.Px(14)) / 2, pill);
                    for (int i = 0; i < max; i++) Style.Round(new Rect(x0 + i * (segW + gap), y0, segW, segH), segH / 2, i < max - got ? Style.Good : Style.Warn);
                }
                if (since >= 0f && since < 0.45f) { Style.Alpha = (0.45f - since) / 0.45f * 0.35f; Style.Round(new Rect(0, 0, Screen.width, Screen.height), 0, Style.Warn); Style.Alpha = 1f; }
            }

            // Micro ouvert (vocal) : temoin en bas a gauche.
            if (Voice.Talking)
            {
                string mic = Lang.T("MICRO", "MIC");
                float mw = Style.Width(mic, 15) + Style.Px(34), mh = Style.Px(30);
                var mr = new Rect(Style.Px(20), Screen.height - mh - Style.Px(20), mw, mh);
                Style.Round(mr, mh / 2, pill);
                Style.Round(new Rect(mr.x + Style.Px(10), mr.y + mh / 2 - Style.Px(5), Style.Px(10), Style.Px(10)), Style.Px(5), Style.Good);
                Style.Text(new Rect(mr.x + Style.Px(24), mr.y, mw - Style.Px(28), mh), mic, 15, TextAnchor.MiddleCenter, Style.White);
            }

            // Marqueurs partages (Markers) : pastille de la couleur du joueur, nom et distance, par-dessus tout (OnGUI).
            Markers.DrawHud();

            // Pseudos au-dessus des joueurs : pilule sombre, effacee sur la derniere moitie de la distance choisie
            // ([Graphismes] Pseudos : 0 caches, sinon la distance en metres ; 120 par defaut).
            Camera cam = Game.MainCam;
            float far = Gfx.Get("Pseudos", 120);
            bool bubbles = HudSettings.On("BullesTchat");
            if (cam == null || (far <= 0f && !bubbles)) return;
            foreach (Avatar a in PlayerSync.Avatars)
            {
                if (a.Root == null) continue;
                Vector3 sp = cam.WorldToScreenPoint(a.HeadPosition);
                if (sp.z <= 0.5f) continue;
                if (bubbles) Bubble(a.Player.Id, sp, now, far > 0f && sp.z <= far);
                if (far <= 0f || sp.z > far) continue;
                string n = a.Player.Name;
                // Avatar Steam a gauche du pseudo (joueurs passes par Steam, ou dont le compte est connu).
                Texture2D av = MWCoop.Net.SteamNet.Avatar(a.Player.SteamId);
                float nh = Style.Px(26), isz = av != null ? nh - Style.Px(6) : 0f;
                float nw = Style.Width(n, 15) + Style.Px(22) + (av != null ? isz + Style.Px(4) : 0f);
                var r = new Rect(Mathf.Round(sp.x - nw / 2), Mathf.Round(Screen.height - sp.y - nh), nw, nh);
                Style.Alpha = Mathf.Clamp01(2f - 2f * sp.z / far);
                Style.Round(r, nh / 2, pill);
                // (filet de la couleur du joueur : la meme que sa pastille sur la carte et son marqueur -- Markers)
                Style.Round(new Rect(r.x + nh / 2, r.yMax - Style.Px(3), r.width - nh, Style.Px(3)), Style.Px(1.5f), Markers.ColorOf(a.Player.Id));
                if (Voice.Speaking(a.Player.Id)) Style.Ring(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), nh / 2 + 2, Style.Good);   // (parle : vocal)
                if (av != null)
                {
                    Color gc = GUI.color;
                    GUI.color = new Color(1f, 1f, 1f, Style.Alpha);
                    GUI.DrawTexture(new Rect(r.x + Style.Px(4), r.y + Style.Px(3), isz, isz), av);
                    GUI.color = gc;
                    Style.Text(new Rect(r.x + isz + Style.Px(4), r.y, r.width - isz - Style.Px(4), nh), n, 15, TextAnchor.MiddleCenter, Style.White);
                }
                else Style.Text(r, n, 15, TextAnchor.MiddleCenter, Style.White);
            }
            Style.Alpha = 1f;
        }

        // Message du tchat au-dessus du joueur : apparait en grandissant et en montant, s'efface en continuant de monter ; lisible
        // jusqu'a 60 m, couleur du joueur sur le bord. Au-dessus du pseudo quand il est affiche.
        const float BubbleFar = 60f;
        static Chat.Said bubbleLogged;
        static void Bubble(int id, Vector3 sp, float now, bool nameShown)
        {
            Chat.Said s = Chat.SaidBy(id);
            if (s == null || sp.z > BubbleFar) return;
            if (Config.Testing && s != bubbleLogged) { bubbleLogged = s; Log.Info("bulle : joueur " + id + " dessinee en " + sp.ToString("F0") + " sur " + Screen.width + "x" + Screen.height); }
            float age = now - s.Born, left = s.Until - now;
            float inK = Mathf.Clamp01(age / 0.22f), outK = Mathf.Clamp01(left / 0.5f);
            // (entree : leger depassement, puis taille normale)
            float pop = 0.82f + 0.18f * (1f + 1.7f * (inK - 1f) * (inK - 1f) * (inK - 1f) + 0.7f * (inK - 1f) * (inK - 1f));
            float lift = (1f - inK) * Style.Px(12) + (1f - outK) * -Style.Px(14);
            float maxW = Mathf.Min(Style.Px(320), Screen.width * 0.5f), padX = Style.Px(14), lineH = Style.Px(21), textW = Style.Width(s.Text, 15, false);
            int lines = Mathf.Max(1, Mathf.CeilToInt(textW / (maxW - padX * 2f) * 1.08f));
            float w = Mathf.Min(textW + padX * 2f + Style.Px(2), maxW) * pop, h = (lines * lineH + Style.Px(14)) * pop;
            float bottom = Screen.height - sp.y - (nameShown ? Style.Px(26) + Style.Px(12) : Style.Px(10)) + lift;
            var r = new Rect(Mathf.Round(sp.x - w / 2), Mathf.Round(bottom - h), w, h);
            r.x = Mathf.Clamp(r.x, Style.Px(6), Mathf.Max(Style.Px(6), Screen.width - w - Style.Px(6)));
            Style.Alpha = Mathf.Min(inK, outK) * Mathf.Clamp01(2f - sp.z / (BubbleFar * 0.5f));
            float rad = Mathf.Min(Style.Px(13), h / 2);
            Style.Round(r, rad, Kit.A(Style.Shade, 0.82f));
            Style.Ring(r, rad, Markers.ColorOf(id));
            // (queue : deux points vers le joueur)
            float d1 = Style.Px(8), d2 = Style.Px(4), cx = Mathf.Clamp(sp.x, r.x + rad, r.xMax - rad);
            Style.Round(new Rect(cx - d1 / 2, r.yMax + Style.Px(1), d1, d1), d1 / 2, Kit.A(Style.Shade, 0.82f));
            if (!nameShown) Style.Round(new Rect(cx - d2 / 2, r.yMax + d1 + Style.Px(2), d2, d2), d2 / 2, Kit.A(Style.Shade, 0.82f));
            if (pop > 0.97f) Style.Text(new Rect(r.x + padX, r.y, r.width - padX * 2f, r.height), s.Text, 15, TextAnchor.MiddleCenter, Style.White, false, true);
        }
    }
}
