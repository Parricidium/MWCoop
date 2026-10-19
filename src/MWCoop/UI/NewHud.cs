using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Nouveau HUD en jeu (demande de JD, 18/10 ; maquette validee : Carte, Anneaux, Bandeau, et Vanilla = celui du jeu).
    // Le HUD du jeu (GUI/HUD : une ligne par jauge, textes 3D et barres, mis a jour par ses automates d'apres les globales
    // Player*) n'est pas touche dans sa logique : seuls ses rendus sont eteints tant qu'un autre style est choisi (rallumes en
    // revenant a Vanilla). Le notre lit les memes valeurs :
    //  - PlayerThirst, nomme PROBLEM par le jeu quand PlayerAlcoholism > 1 (Soif / Probleme ici) ; PlayerHunger, PlayerStress,
    //    PlayerUrine, PlayerFatigue, PlayerDirtiness (ligne cachee par le jeu, montree ici sur option), PlayerSweat : 0..100 ;
    //  - PlayerTemp (temperature du corps, 0 gele .. 100) et sa tendance (PLAYER/BodyTemp 'Calculations' Temperature : < 0 se
    //    refroidit) ; PlayerMoney ; le jour (texte de GUI/HUD/Day/HUDValue) ; la prison (GUI/HUD/Jailtime allume, jours) ;
    //    la mort definitive (GUI/HUD/Mortal allume).
    // Montre seulement quand le jeu montrerait le sien : GUI et sa camera GUI/CAM allumes (option HUD du jeu, mode photo,
    // mort...), pas pendant le choix de reapparition. Panneau Equipe (jauges des autres joueurs : @besoins, 2 s) et alertes
    // (tres faim, epuise, gele... pour soi et ses amis) : aussi avec Vanilla. Reglages : UI/HudSettings.cs.
    public static class NewHud
    {
        static GameObject guiRoot, hudRoot, guiCamGo, dayGo, jailGo, mortalGo, fpsGo;
        static Camera guiCam;
        static TextMesh dayText, jailText, fpsText;
        // Compteur d'images du jeu (option « FPS » des reglages du jeu : GUI/HUD/FPS allume, valeur moyennee par son automate
        // dans HUDValue) : repris dans chaque style (demande de JD, 18/10), cache avec le reste du HUD du jeu.
        static bool fpsOn;
        static int fps;
        static float fpsOwn;
        static FsmFloat bodyTempTrend;
        static readonly List<Renderer> hidden = new List<Renderer>();
        static float nextFind, nextHide, nextSend, nextAlertCheck;
        static int logs;

        public static void OnLevelLoaded()
        {
            guiRoot = hudRoot = guiCamGo = dayGo = jailGo = mortalGo = fpsGo = null; guiCam = null; dayText = jailText = fpsText = null; bodyTempTrend = null;
            hidden.Clear(); mates.Clear(); alerts.Clear(); armed.Clear(); nextFind = 0f;
        }

        // ---------------------------------------------------------------- valeurs du jeu
        class Gauge { public string Key, Icon, Fr, En; public float V; public bool Low; }   // Low : le danger est en bas (temperature)
        static readonly List<Gauge> gauges = new List<Gauge>();
        static float money, temp, trend;
        static bool alcoholic, inJail, mortal;
        static string dayLabel = "", jailDays = "";

        static void Find()
        {
            guiRoot = Game.FindAny("GUI");
            hudRoot = Game.FindAny("GUI/HUD");
            guiCamGo = Game.FindAny("GUI/CAM");
            guiCam = guiCamGo != null ? guiCamGo.GetComponent<Camera>() : null;
            dayGo = Game.FindAny("GUI/HUD/Day/HUDValue");
            dayText = dayGo != null ? dayGo.GetComponent<TextMesh>() : null;
            jailGo = Game.FindAny("GUI/HUD/Jailtime");
            GameObject jv = Game.FindAny("GUI/HUD/Jailtime/HUDValue");
            jailText = jv != null ? jv.GetComponent<TextMesh>() : null;
            mortalGo = Game.FindAny("GUI/HUD/Mortal");
            fpsGo = Game.FindAny("GUI/HUD/FPS");
            GameObject fv = Game.FindAny("GUI/HUD/FPS/HUDValue");
            fpsText = fv != null ? fv.GetComponent<TextMesh>() : null;
            GameObject bt = Game.FindAny("PLAYER/BodyTemp");
            PlayMakerFSM calc = bt != null ? Game.FsmOn(bt, "Calculations") : null;
            bodyTempTrend = calc != null ? calc.FsmVariables.FindFsmFloat("Temperature") : null;
            if (logs++ < 3) Log.Info("hud : HUD du jeu " + (hudRoot != null ? "trouve" : "ABSENT") + ", camera GUI " + (guiCam != null) + ", jour " + (dayText != null) + ", tendance de temperature " + (bodyTempTrend != null));
        }

        static void Read()
        {
            gauges.Clear();
            alcoholic = !Game.Msc && Game.GlobalFloat("PlayerAlcoholism") > 1f;   // (MSC : pas d'alcoolisme, ni de temperature, ni de sueur)
            Add("JaugeSoif", alcoholic ? "probleme" : "soif", alcoholic ? "Problème" : "Soif", alcoholic ? "Problem" : "Thirst", Game.GlobalFloat("PlayerThirst"));
            Add("JaugeFaim", "faim", "Faim", "Hunger", Game.GlobalFloat("PlayerHunger"));
            Add("JaugeStress", "stress", "Stress", "Stress", Game.GlobalFloat("PlayerStress"));
            Add("JaugeUrine", "urine", "Urine", "Urine", Game.GlobalFloat("PlayerUrine"));
            Add("JaugeFatigue", "fatigue", "Fatigue", "Fatigue", Game.GlobalFloat("PlayerFatigue"));
            Add("JaugeSalete", "salete", "Saleté", "Dirtiness", Game.GlobalFloat("PlayerDirtiness"));
            if (!Game.Msc) Add("JaugeSueur", "sueur", "Sueur", "Sweat", Game.GlobalFloat("PlayerSweat"));
            money = Game.GlobalFloat("PlayerMoney");
            temp = Game.Msc ? 70f : Game.GlobalFloat("PlayerTemp");   // (MSC : neutre, jamais « gele »)
            trend = bodyTempTrend != null ? bodyTempTrend.Value : 0f;
            dayLabel = dayText != null ? DayName(dayText.text) : "";
            inJail = jailGo != null && jailGo.activeInHierarchy;
            jailDays = jailText != null ? jailText.text : "";
            mortal = mortalGo != null && mortalGo.activeInHierarchy;
            fpsOn = fpsGo != null && fpsGo.activeSelf;
            if (fpsOn)
            {
                // la valeur du jeu (texte « 60 », ou « 60 FPS ») ; a defaut, notre propre moyenne
                float dt = Time.unscaledDeltaTime;
                if (dt > 0f) fpsOwn = fpsOwn <= 0f ? 1f / dt : Mathf.Lerp(fpsOwn, 1f / dt, 0.1f);
                int v = 0;
                string t = fpsText != null ? fpsText.text : null;
                if (t != null) foreach (char ch in t) { if (ch >= '0' && ch <= '9') v = v * 10 + (ch - '0'); else if (v > 0) break; }
                fps = v > 0 ? v : Mathf.RoundToInt(fpsOwn);
            }
        }

        static readonly Color FpsGood = new Color(0.42f, 0.86f, 0.56f);
        static Color FpsColor { get { return fps >= 50 ? FpsGood : fps >= 30 ? Warn : Bad; } }
        static string FpsText { get { return fps + " FPS"; } }

        static void Add(string key, string icon, string fr, string en, float v)
        {
            if (!HudSettings.On(key)) return;
            gauges.Add(new Gauge { Key = key, Icon = icon, Fr = fr, En = en, V = Mathf.Clamp(v, 0f, 100f) });
        }

        static string DayName(string game)
        {
            switch ((game ?? "").Trim().ToUpperInvariant())
            {
                case "MONDAY": return Lang.T("Lundi", "Monday");
                case "TUESDAY": return Lang.T("Mardi", "Tuesday");
                case "WEDNESDAY": return Lang.T("Mercredi", "Wednesday");
                case "THURSDAY": return Lang.T("Jeudi", "Thursday");
                case "FRIDAY": return Lang.T("Vendredi", "Friday");
                case "SATURDAY": return Lang.T("Samedi", "Saturday");
                case "SUNDAY": return Lang.T("Dimanche", "Sunday");
            }
            return game ?? "";
        }

        static string TempWord(float t)
        {
            return t < 20f ? Lang.T("gelé", "freezing") : t < 40f ? Lang.T("froid", "cold") : t < 85f ? Lang.T("bien", "fine") : Lang.T("chaud", "hot");
        }

        // Le jeu montrerait-il son HUD ? (GUI, sa camera, la ligne du HUD)
        static bool GameShows()
        {
            return guiRoot != null && guiRoot.activeInHierarchy && hudRoot != null && hudRoot.activeInHierarchy
                   && guiCamGo != null && guiCamGo.activeInHierarchy && (guiCam == null || guiCam.enabled);
        }

        // ---------------------------------------------------------------- chaque image
        public static void Update()
        {
            if (!PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if ((hudRoot == null || guiRoot == null) && now >= nextFind) { nextFind = now + 3f; Find(); }
            bool replace = HudSettings.StyleNow != HudSettings.Vanilla;
            if (replace && hudRoot != null && now >= nextHide) { nextHide = now + 1f; HideGame(); }
            else if (!replace && hidden.Count > 0) RestoreGame();
            Read();
            if (Session.Active && Session.RemoteCount > 0 && now >= nextSend) { nextSend = now + 2f; SendNeeds(); }
            if (now >= nextAlertCheck) { nextAlertCheck = now + 0.5f; CheckAlerts(now); }
        }

        static void HideGame()
        {
            int n = 0;
            foreach (Renderer r in hudRoot.GetComponentsInChildren<Renderer>(true))
                if (r != null && r.enabled) { r.enabled = false; hidden.Add(r); n++; }
            if (n > 0 && logs++ < 10) Log.Info("hud : " + n + " rendus du HUD du jeu eteints (style " + HudSettings.StyleNow + ")");
        }

        static void RestoreGame()
        {
            foreach (Renderer r in hidden) if (r != null) r.enabled = true;
            if (logs++ < 10) Log.Info("hud : HUD du jeu rendu (" + hidden.Count + " rendus)");
            hidden.Clear();
        }

        // ---------------------------------------------------------------- jauges des autres joueurs (@besoins)
        class Mate { public int Id; public float[] V = new float[7]; public bool Alcoholic; public float At; }
        static readonly Dictionary<int, Mate> mates = new Dictionary<int, Mate>();
        // ordre sur le fil : soif, faim, stress, urine, fatigue, salete, temperature
        static readonly string[] Globals = { "PlayerThirst", "PlayerHunger", "PlayerStress", "PlayerUrine", "PlayerFatigue", "PlayerDirtiness", "PlayerTemp" };

        static void SendNeeds()
        {
            var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@besoins");
            foreach (string g in Globals) w.U8((int)Mathf.Clamp(Game.GlobalFloat(g), 0f, 255f));
            w.Bool(alcoholic);
            Session.SendAll(w, false);
        }

        public static void OnNeeds(int who, NetReader r)
        {
            Mate m;
            if (!mates.TryGetValue(who, out m)) { m = new Mate { Id = who }; mates[who] = m; }
            for (int i = 0; i < m.V.Length; i++) m.V[i] = r.U8();
            m.Alcoholic = r.Bool();
            m.At = Time.realtimeSinceStartup;
        }

        // ---------------------------------------------------------------- alertes
        class Alert { public string Text; public float Born, Until; public bool Bad; }
        static readonly List<Alert> alerts = new List<Alert>();
        static readonly Dictionary<string, bool> armed = new Dictionary<string, bool>();

        // Seuil franchi vers le haut (90, ou 20 vers le bas pour la temperature) : une alerte ; reamorcee sous 75 (au-dessus de 30).
        static void Watch(string key, float v, bool low, string text)
        {
            bool over = low ? v < 20f : v >= 90f, calm = low ? v > 30f : v < 75f;
            bool a;
            if (!armed.TryGetValue(key, out a)) a = true;
            if (a && over)
            {
                armed[key] = false;
                float now = Time.realtimeSinceStartup;
                alerts.Add(new Alert { Text = text, Born = now, Until = now + 6f, Bad = true });
                if (alerts.Count > 3) alerts.RemoveAt(0);
                Log.Info("hud : alerte « " + text + " »");
            }
            else if (!a && calm) armed[key] = true;
        }

        static void CheckAlerts(float now)
        {
            alerts.RemoveAll(x => x.Until < now);
            if (!HudSettings.On("Alertes")) return;
            Watch("moi.soif", Game.GlobalFloat("PlayerThirst"), false, alcoholic ? Lang.T("Le manque se fait sentir", "The craving is getting strong") : Lang.T("Vous avez très soif", "You are very thirsty"));
            Watch("moi.faim", Game.GlobalFloat("PlayerHunger"), false, Lang.T("Vous avez très faim", "You are very hungry"));
            Watch("moi.fatigue", Game.GlobalFloat("PlayerFatigue"), false, Lang.T("Vous tombez de fatigue", "You are exhausted"));
            Watch("moi.urine", Game.GlobalFloat("PlayerUrine"), false, Lang.T("Envie très pressante", "You badly need to pee"));
            Watch("moi.stress", Game.GlobalFloat("PlayerStress"), false, Lang.T("Stress au maximum", "Stress at its peak"));
            if (!Game.Msc) Watch("moi.temp", Game.GlobalFloat("PlayerTemp"), true, Lang.T("Vous êtes gelé", "You are freezing"));
            foreach (Mate m in mates.Values)
            {
                if (now - m.At > 10f) continue;
                string n = MateName(m.Id);
                if (n == null) continue;
                Watch(m.Id + ".faim", m.V[1], false, string.Format(Lang.T("{0} a très faim", "{0} is very hungry"), n));
                Watch(m.Id + ".soif", m.V[0], false, string.Format(Lang.T("{0} a très soif", "{0} is very thirsty"), n));
                Watch(m.Id + ".fatigue", m.V[4], false, string.Format(Lang.T("{0} tombe de fatigue", "{0} is exhausted"), n));
                if (!Game.Msc) Watch(m.Id + ".temp", m.V[6], true, string.Format(Lang.T("{0} est gelé", "{0} is freezing"), n));
            }
        }

        static string MateName(int id)
        {
            PlayerInfo p;
            return Session.Players.TryGetValue(id, out p) && p != null && !p.Local ? p.Name : null;
        }

        // ---------------------------------------------------------------- dessin
        static float K;                                   // taille choisie (1 = 100 %)
        static float P(float v) { return Mathf.Round(v * Style.S * K); }
        static int F(float size) { return Mathf.Max(8, Mathf.RoundToInt(size * K)); }
        static Color accent;
        static readonly Color Warn = new Color(1f, 0.71f, 0.36f), Bad = new Color(1f, 0.42f, 0.48f), Txt = Style.White, Dim = Style.Dim,
                              Track = new Color(1f, 1f, 1f, 0.12f), Edge = Style.M(new Color(0.80f, 0.89f, 1f, 0.18f), new Color(1f, 1f, 1f, 0.16f)), ColdC = new Color(0.56f, 0.79f, 1f);
        // (My Summer Car : les deux premiers accents en jaune et orange, comme le HUD du jeu, au lieu des bleus)
        static readonly Color[] Accents = { Style.M(new Color(0.65f, 0.85f, 1f), new Color(1f, 0.84f, 0.30f)), Style.M(new Color(0.47f, 0.74f, 1f), new Color(1f, 0.62f, 0.22f)),
                                            new Color(0.28f, 0.94f, 0.69f), new Color(0.79f, 0.65f, 1f) };

        static Color Panel { get { Color c = Style.Shade; c.a = HudSettings.Get("Fond") / 100f; return c; } }
        static Color Tone(float v, bool low) { return low ? (v < 20f ? Bad : v < 40f ? Warn : accent) : (v >= 90f ? Bad : v >= 70f ? Warn : accent); }
        static bool Resting(float v) { return HudSettings.On("Estomper") && v < 10f; }

        public static void Draw()
        {
            if (!PlayerSync.InGame || Respawn.Choosing || !GameShows()) { DrawAlerts(); return; }
            int style = HudSettings.StyleNow;
            K = HudSettings.Get("Taille") / 100f;
            int ci = Mathf.Clamp(HudSettings.Get("Couleur"), 0, Accents.Length - 1);
            accent = Accents[ci];
            int pos = HudSettings.Get("Position");
            if (style == HudSettings.Carte) DrawCard(pos < 0 ? 0 : pos);
            else if (style == HudSettings.Anneaux) DrawRings(pos < 0 ? 2 : pos);
            else if (style == HudSettings.Bandeau) DrawStrip(pos < 0 ? 4 : pos);
            if (HudSettings.On("Equipe")) DrawTeam(style, pos);
            DrawAlerts();
            Style.Alpha = 1f;
        }

        // Place d'un bloc w x h : 0 haut gauche, 1 haut droite, 2 bas gauche, 3 bas droite, 4 haut centre, 5 bas centre. En haut
        // a gauche, sous la pastille d'etat du mod ; en bas, au-dessus de la ligne du tchat et du temoin du micro.
        static Rect Place(int pos, float w, float h)
        {
            float m = Style.Px(20), top = Style.Px(48), bottom = Style.Px(72);
            float x = pos == 1 || pos == 3 ? Screen.width - w - m : pos == 4 || pos == 5 ? Mathf.Round((Screen.width - w) / 2) : m;
            float y = pos == 2 || pos == 3 || pos == 5 ? Screen.height - h - bottom : pos == 0 ? top : m;
            return new Rect(x, y, w, h);
        }

        static void Icon(Rect r, string name, Color c)
        {
            Texture2D t = IconTex(name);
            if (t == null || Event.current.type != EventType.Repaint) return;
            Color old = GUI.color;
            c.a *= Style.Alpha;
            GUI.color = c;
            GUI.DrawTexture(r, t, ScaleMode.ScaleToFit, true);
            GUI.color = old;
        }

        static string Money() { return Mathf.FloorToInt(money).ToString("#,0").Replace(",", " ").Replace(" ", " "); }

        // ---- Carte : en-tete (jour, argent), une ligne par jauge, temperature en echelle froid -> chaud ----
        static void DrawCard(int pos)
        {
            bool head = HudSettings.On("JaugeJour") || HudSettings.On("JaugeArgent") || mortal || fpsOn;
            bool tempOn = HudSettings.On("JaugeTemperature");
            float rowH = P(26), pad = P(14), w = P(300);
            int rows = gauges.Count + (tempOn ? 1 : 0) + (inJail ? 1 : 0);
            if (rows == 0 && !head) return;
            float h = pad * 2 + rows * rowH + (head ? P(30) + (rows > 0 ? P(10) : 0) : 0) - (rows > 0 ? 0 : 0);
            Rect r = Place(pos, w, h);
            Style.Round(r, P(18), Panel);
            Style.Ring(r, P(18), Edge);
            float x = r.x + pad, y = r.y + pad, iw = P(18), lw = P(96), vw = HudSettings.On("Valeurs") ? P(34) : 0f;
            float bx = x + iw + P(8) + lw, bw = r.xMax - pad - bx - vw;
            if (head)
            {
                var hr = new Rect(x, y, w - 2 * pad, P(30));
                float cx = hr.x;
                if (HudSettings.On("JaugeJour") && dayLabel.Length > 0)
                {
                    Icon(new Rect(cx, hr.y + P(6), iw, iw), "jour", Dim);
                    cx += iw + P(8);
                    Style.Text(new Rect(cx, hr.y, P(140), hr.height), dayLabel, F(16), TextAnchor.MiddleLeft, Txt);
                }
                if (mortal) Style.Text(new Rect(cx + Style.Width(dayLabel, F(16)) + P(8), hr.y, P(80), hr.height), Lang.T("Mortel", "Mortal"), F(12), TextAnchor.MiddleLeft, Bad);
                float rx = hr.xMax;
                if (HudSettings.On("JaugeArgent"))
                {
                    string s = Money() + " mk";
                    float pw = Style.Width(s, F(15)) + P(22), ph = P(26);
                    var pr = new Rect(hr.xMax - pw, hr.center.y - ph / 2, pw, ph);
                    Style.Round(pr, ph / 2, new Color(accent.r, accent.g, accent.b, 0.16f));
                    Style.Ring(pr, ph / 2, new Color(accent.r, accent.g, accent.b, 0.35f));
                    Style.Text(pr, s, F(15), TextAnchor.MiddleCenter, accent);
                    rx = pr.x - P(6);
                }
                if (fpsOn) FpsPill(rx, hr.center.y, P(24), F(12), true);
                y += P(30);
                if (rows > 0) { Style.Fill(new Rect(x, y + P(4), w - 2 * pad, Mathf.Max(1f, Style.Px(1))), Edge); y += P(10); }
            }
            foreach (Gauge g in gauges)
            {
                Style.Alpha = Resting(g.V) ? 0.4f : 1f;
                Color c = Tone(g.V, false);
                bool hot = g.V >= 70f;
                Icon(new Rect(x, y + (rowH - iw) / 2, iw, iw), g.Icon, hot ? c : new Color(0.79f, 0.84f, 0.90f));
                Style.Text(new Rect(x + iw + P(8), y, lw, rowH), Lang.T(g.Fr, g.En), F(15), TextAnchor.MiddleLeft, hot ? Color.Lerp(Txt, c, 0.5f) : Txt);
                Style.Bar(new Rect(bx, y + rowH / 2 - P(4), bw, P(8)), g.V / 100f, c);
                if (vw > 0) Style.Text(new Rect(r.xMax - pad - vw, y, vw, rowH), Mathf.RoundToInt(g.V).ToString(), F(13), TextAnchor.MiddleRight, Dim);
                y += rowH;
            }
            Style.Alpha = 1f;
            if (tempOn)
            {
                Color c = Tone(temp, true);
                Icon(new Rect(x, y + (rowH - iw) / 2, iw, iw), "temperature", temp < 40f ? ColdC : new Color(0.79f, 0.84f, 0.90f));
                Style.Text(new Rect(x + iw + P(8), y, lw, rowH), Lang.T("Température", "Temperature"), F(15), TextAnchor.MiddleLeft, Txt);
                float tw = P(52);
                TempScale(new Rect(bx, y + rowH / 2 - P(4), bw + vw - tw, P(8)), temp / 100f);
                Style.Text(new Rect(r.xMax - pad - tw, y, tw, rowH), TempWord(temp), F(13), TextAnchor.MiddleRight, temp < 40f ? ColdC : temp >= 85f ? Warn : Dim);
                if (Mathf.Abs(trend) > 0.01f) Icon(new Rect(r.xMax - pad - tw - P(14), y + rowH / 2 - P(6), P(12), P(12)), trend > 0 ? "monte" : "descend", trend > 0 ? Warn : ColdC);
                y += rowH;
            }
            if (inJail)
            {
                Icon(new Rect(x, y + (rowH - iw) / 2, iw, iw), "prison", new Color(0.79f, 0.65f, 1f));
                Style.Text(new Rect(x + iw + P(8), y, lw, rowH), Lang.T("Prison", "Jail"), F(15), TextAnchor.MiddleLeft, Txt);
                Style.Text(new Rect(bx, y, r.xMax - pad - bx, rowH), jailDays + Lang.T(" j", " d"), F(15), TextAnchor.MiddleRight, new Color(0.86f, 0.78f, 1f));
            }
        }

        // Pastille du compteur d'images : point de couleur (vert >= 50, orange >= 30, rouge), « 60 FPS ». right : x est son bord
        // droit, sinon son bord gauche ; renvoie sa largeur.
        static float FpsPill(float x, float cy, float h, int size, bool right)
        {
            string s = FpsText;
            Color c = FpsColor;
            float w = Style.Width(s, size) + P(28);
            var r = new Rect(right ? x - w : x, cy - h / 2, w, h);
            Style.Round(r, h / 2, new Color(1f, 1f, 1f, 0.06f));
            Style.Ring(r, h / 2, new Color(c.r, c.g, c.b, 0.35f));
            float d = P(7);
            Style.Round(new Rect(r.x + P(9), cy - d / 2, d, d), d / 2, c);
            Style.Text(new Rect(r.x + P(20), r.y, w - P(24), h), s, size, TextAnchor.MiddleLeft, Color.Lerp(Txt, c, 0.35f));
            return w;
        }

        // Echelle de temperature : froid (bleu) -> chaud (orange), repere blanc a la valeur.
        static Texture2D tempTex;
        static void TempScale(Rect r, float frac)
        {
            if (tempTex == null)
            {
                tempTex = new Texture2D(64, 1, TextureFormat.ARGB32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                Color[] stops = { new Color(0.35f, 0.66f, 1f), new Color(0.65f, 0.85f, 1f), new Color(0.81f, 0.90f, 0.85f), new Color(1f, 0.71f, 0.36f), new Color(1f, 0.42f, 0.48f) };
                for (int i = 0; i < 64; i++)
                {
                    float t = i / 63f * (stops.Length - 1);
                    int a = Mathf.Min((int)t, stops.Length - 2);
                    tempTex.SetPixel(i, 0, Color.Lerp(stops[a], stops[a + 1], t - a));
                }
                tempTex.Apply();
            }
            if (Event.current.type != EventType.Repaint) return;
            Style.Round(r, r.height / 2, Track);
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Style.Alpha);
            GUI.DrawTexture(new Rect(r.x + r.height / 2, r.y, r.width - r.height, r.height), tempTex);
            GUI.color = old;
            float mx = r.x + Mathf.Clamp01(frac) * r.width;
            var mk = new Rect(mx - P(2), r.y - P(4), P(4), r.height + P(8));
            Style.Round(new Rect(mk.x - P(1), mk.y - P(1), mk.width + P(2), mk.height + P(2)), P(2), Kit.A(Style.Shade, 0.8f));
            Style.Round(mk, P(2), Color.white);
        }

        // ---- Anneaux : jour et argent a gauche, un anneau par jauge (au repos : masque), la temperature en dernier ----
        static void DrawRings(int pos)
        {
            var show = new List<Gauge>();
            foreach (Gauge g in gauges) if (!Resting(g.V)) show.Add(g);
            bool tempOn = HudSettings.On("JaugeTemperature"), head = HudSettings.On("JaugeJour") || HudSettings.On("JaugeArgent");
            bool names = HudSettings.On("Valeurs");
            float ring = P(50), cell = P(60), pad = P(12);
            int n = show.Count + (tempOn ? 1 : 0) + (fpsOn ? 1 : 0);
            float hw = head ? P(108) : 0f;
            float w = pad * 2 + hw + n * cell, h = pad * 2 + ring + P(18);
            if (n == 0 && !head) return;
            Rect r = Place(pos, w, h);
            Style.Round(r, P(22), Panel);
            Style.Ring(r, P(22), Edge);
            float x = r.x + pad;
            if (head)
            {
                if (HudSettings.On("JaugeJour")) Style.Text(new Rect(x, r.y + pad, hw - P(8), P(24)), dayLabel, F(15), TextAnchor.MiddleLeft, Txt);
                if (HudSettings.On("JaugeArgent"))
                {
                    Style.Text(new Rect(x, r.y + pad + P(26), hw - P(8), P(28)), Money(), F(19), TextAnchor.MiddleLeft, accent);
                    Style.Text(new Rect(x, r.y + pad + P(50), hw - P(8), P(16)), "mk", F(12), TextAnchor.MiddleLeft, Dim);
                }
                Style.Fill(new Rect(x + hw - P(8), r.y + pad, Mathf.Max(1f, Style.Px(1)), h - 2 * pad), Edge);
                x += hw;
            }
            foreach (Gauge g in show)
            {
                Color c = Tone(g.V, false);
                RingCell(new Rect(x, r.y + pad, cell, ring + P(18)), ring, g.V / 100f, c, g.Icon, g.V >= 70f ? c : new Color(0.86f, 0.90f, 0.95f), names || g.V >= 70f ? Lang.T(g.Fr, g.En) : null);
                x += cell;
            }
            if (tempOn)
            {
                Color c = temp < 40f ? ColdC : temp >= 85f ? Warn : accent;
                RingCell(new Rect(x, r.y + pad, cell, ring + P(18)), ring, temp / 100f, c, "temperature", c, names || temp < 40f ? TempWord(temp) : null);
                x += cell;
            }
            if (fpsOn)
            {
                Color c = FpsColor;
                var rr = new Rect(x + (cell - ring) / 2, r.y + pad, ring, ring);
                Arc(rr, 1f, Track);
                Arc(rr, Mathf.Clamp01(fps / 60f), c);
                Style.Text(rr, fps.ToString(), F(fps >= 100 ? 13 : 15), TextAnchor.MiddleCenter, Color.Lerp(Txt, c, 0.3f));
                Style.Text(new Rect(x - P(6), rr.yMax + P(1), cell + P(12), P(17)), "FPS", F(12), TextAnchor.MiddleCenter, Dim);
            }
        }

        static void RingCell(Rect cell, float size, float frac, Color c, string icon, Color ic, string label)
        {
            var rr = new Rect(cell.x + (cell.width - size) / 2, cell.y, size, size);
            Arc(rr, 1f, Track);
            Arc(rr, frac, c);
            float isz = size * 0.42f;
            Icon(new Rect(rr.center.x - isz / 2, rr.center.y - isz / 2, isz, isz), icon, ic);
            if (label != null) Style.Text(new Rect(cell.x - P(6), rr.yMax + P(1), cell.width + P(12), P(17)), label, F(12), TextAnchor.MiddleCenter, Dim);
        }

        // Anneau partiel (depuis le haut, sens horaire) : textures faites a la demande, une par 2,5 %.
        static readonly Dictionary<int, Texture2D> arcs = new Dictionary<int, Texture2D>();
        static void Arc(Rect r, float frac, Color c)
        {
            int k = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(frac) * 40f), 0, 40);
            if (k == 0 && frac > 0.001f) k = 1;
            if (k == 0 || Event.current.type != EventType.Repaint) return;
            Texture2D t;
            if (!arcs.TryGetValue(k, out t))
            {
                const int N = 96;
                t = new Texture2D(N, N, TextureFormat.ARGB32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                float rOut = N / 2f - 1f, rIn = rOut - N * 0.11f, lim = k / 40f * 360f;
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = x + 0.5f - N / 2f, dy = (N - 1 - y) + 0.5f - N / 2f;   // (y de la texture vers le haut)
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(rOut - d + 0.5f) * Mathf.Clamp01(d - rIn + 0.5f);
                        float ang = Mathf.Atan2(dx, -dy) * Mathf.Rad2Deg;   // 0 en haut, sens horaire
                        if (ang < 0) ang += 360f;
                        if (k < 40 && ang > lim) a = 0f;
                        px[y * N + x] = new Color(1f, 1f, 1f, a);
                    }
                t.SetPixels(px);
                t.Apply();
                arcs[k] = t;
            }
            Color old = GUI.color;
            c.a *= Style.Alpha;
            GUI.color = c;
            GUI.DrawTexture(r, t);
            GUI.color = old;
        }

        // ---- Bandeau : pilule fine, jour, argent, une pastille par jauge (nom en alerte, ou toujours), temperature ----
        static void DrawStrip(int pos)
        {
            bool names = HudSettings.On("Valeurs"), tempOn = HudSettings.On("JaugeTemperature");
            float h = P(40), chipH = P(30), gap = P(6), pad = P(5);
            var parts = new List<float>();
            string day = HudSettings.On("JaugeJour") ? dayLabel : "", mon = HudSettings.On("JaugeArgent") ? Money() + " mk" : "";
            float dayW = day.Length > 0 ? Style.Width(day, F(14)) + P(24) : 0f, monW = mon.Length > 0 ? Style.Width(mon, F(14)) + P(24) : 0f;
            float w = pad * 2 + dayW + monW;
            int chips = 0;
            foreach (Gauge g in gauges)
            {
                float cw = P(16) + P(8) + P(42) + P(20);
                if (names || g.V >= 70f) cw += Style.Width(Lang.T(g.Fr, g.En), F(12)) + P(8);
                parts.Add(cw); w += cw; chips++;
            }
            float tw = tempOn ? P(16) + P(8) + Style.Width(TempWord(temp), F(12)) + P(22) : 0f;
            float fw = fpsOn ? Style.Width(FpsText, F(12)) + P(28) : 0f;
            w += tw + fw + gap * (chips + (dayW > 0 ? 1 : 0) + (monW > 0 ? 1 : 0) + (tempOn ? 1 : 0) + (fpsOn ? 1 : 0) - 1);
            if (w <= pad * 2) return;
            Rect r = Place(pos == 0 || pos == 1 || pos == 4 ? 4 : 5, w, h);
            if (pos == 0 || pos == 1) r.x = pos == 0 ? Style.Px(20) : Screen.width - w - Style.Px(20);
            if (pos == 2 || pos == 3) r.x = pos == 2 ? Style.Px(20) : Screen.width - w - Style.Px(20);
            Style.Round(r, h / 2, Panel);
            Style.Ring(r, h / 2, Edge);
            float x = r.x + pad, cy = r.y + (h - chipH) / 2;
            if (dayW > 0)
            {
                var c = new Rect(x, cy, dayW, chipH);
                Style.Round(c, chipH / 2, new Color(1f, 1f, 1f, 0.06f));
                Style.Text(c, day, F(14), TextAnchor.MiddleCenter, Txt);
                x += dayW + gap;
            }
            if (monW > 0)
            {
                var c = new Rect(x, cy, monW, chipH);
                Style.Round(c, chipH / 2, new Color(accent.r, accent.g, accent.b, 0.16f));
                Style.Text(c, mon, F(14), TextAnchor.MiddleCenter, accent);
                x += monW + gap;
            }
            for (int i = 0; i < gauges.Count; i++)
            {
                Gauge g = gauges[i];
                Style.Alpha = Resting(g.V) ? 0.4f : 1f;
                Color col = Tone(g.V, false);
                bool hot = g.V >= 70f;
                var c = new Rect(x, cy, parts[i], chipH);
                Style.Round(c, chipH / 2, hot ? new Color(col.r, col.g, col.b, 0.16f) : new Color(1f, 1f, 1f, 0.05f));
                if (hot) Style.Ring(c, chipH / 2, new Color(col.r, col.g, col.b, 0.45f));
                float ix = c.x + P(10);
                Icon(new Rect(ix, c.center.y - P(8), P(16), P(16)), g.Icon, hot ? col : new Color(0.79f, 0.84f, 0.90f));
                Style.Bar(new Rect(ix + P(24), c.center.y - P(3), P(42), P(6)), g.V / 100f, col);
                if (names || hot) Style.Text(new Rect(ix + P(72), c.y, c.xMax - ix - P(72), chipH), Lang.T(g.Fr, g.En), F(12), TextAnchor.MiddleLeft, hot ? Color.Lerp(Txt, col, 0.5f) : Dim);
                x += parts[i] + gap;
            }
            Style.Alpha = 1f;
            if (tempOn)
            {
                Color col = temp < 40f ? ColdC : temp >= 85f ? Warn : accent;
                var c = new Rect(x, cy, tw, chipH);
                Style.Round(c, chipH / 2, new Color(col.r, col.g, col.b, 0.14f));
                Icon(new Rect(c.x + P(10), c.center.y - P(8), P(16), P(16)), "temperature", col);
                Style.Text(new Rect(c.x + P(32), c.y, c.width - P(36), chipH), TempWord(temp), F(12), TextAnchor.MiddleLeft, Color.Lerp(Txt, col, 0.4f));
                x += tw + gap;
            }
            if (fpsOn) FpsPill(x, cy + chipH / 2, chipH, F(12), false);
        }

        // ---- Equipe : une petite carte par autre joueur (nom, ou il est, soif / faim / fatigue) ----
        static void DrawTeam(int style, int pos)
        {
            float now = Time.realtimeSinceStartup;
            var list = new List<Mate>();
            foreach (Mate m in mates.Values) if (now - m.At < 10f && MateName(m.Id) != null) list.Add(m);
            if (list.Count == 0) return;
            float kOld = K;
            K = Mathf.Min(K, 1f);   // (toujours discret)
            float w = P(240), cardH = P(104), gap = P(8);
            bool right = !(style != HudSettings.Vanilla && (pos == 1 || pos == 3));
            float x = right ? Screen.width - w - Style.Px(20) : Style.Px(20), y = Style.Px(48);
            if (!right && style == HudSettings.Carte) y = Screen.height * 0.45f;
            Style.Text(new Rect(x + P(4), y, w, P(20)), Lang.T("ÉQUIPE", "TEAM"), F(12), TextAnchor.MiddleLeft, Dim);
            y += P(22);
            Transform me = Game.PlayerT;
            foreach (Mate m in list)
            {
                var r = new Rect(x, y, w, cardH);
                bool tired = m.V[4] >= 90f || m.V[1] >= 90f || (!Game.Msc && m.V[6] < 20f);
                Style.Round(r, P(14), Panel);
                Style.Ring(r, P(14), tired ? new Color(Warn.r, Warn.g, Warn.b, 0.5f) : Edge);
                string name = MateName(m.Id);
                float av = P(28);
                var ar = new Rect(r.x + P(10), r.y + P(10), av, av);
                Style.Round(ar, av / 2, accent);
                Style.Text(ar, name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?", F(14), TextAnchor.MiddleCenter, Style.OnAccent);
                Style.Text(new Rect(ar.xMax + P(8), r.y + P(6), w - av - P(28), P(20)), name, F(14), TextAnchor.MiddleLeft, Txt);
                Style.Text(new Rect(ar.xMax + P(8), r.y + P(24), w - av - P(28), P(16)), Where(m.Id, me), F(11), TextAnchor.MiddleLeft, Dim, false);
                float by = r.y + P(48);
                int[] idx = { 0, 1, 4 };
                string[] ic = { m.Alcoholic ? "probleme" : "soif", "faim", "fatigue" };
                for (int i = 0; i < 3; i++)
                {
                    float v = m.V[idx[i]];
                    Icon(new Rect(r.x + P(10), by + i * P(17), P(14), P(14)), ic[i], new Color(0.79f, 0.84f, 0.90f));
                    Style.Bar(new Rect(r.x + P(32), by + i * P(17) + P(4), w - P(44), P(6)), v / 100f, Tone(v, false));
                }
                y += cardH + gap;
            }
            K = kOld;
        }

        public static string Where(int id, Transform me)
        {
            Avatar a = null;
            foreach (Avatar x in PlayerSync.Avatars) if (x != null && x.Player != null && x.Player.Id == id) { a = x; break; }
            if (a == null || a.Root == null || me == null) return "";
            float d = Vector3.Distance(a.Root.transform.position, me.position);
            string dist = d < 1000f ? Mathf.RoundToInt(d) + " m" : (d / 1000f).ToString("0.0") + " km";
            int f = a.Player.State.Flags;
            string what = (f & PlayerSync.F_Dead) != 0 ? Lang.T("Mort", "Dead") : (f & PlayerSync.F_Sleep) != 0 ? Lang.T("Dort", "Sleeping")
                        : a.InVehicle ? Lang.T("En voiture", "In a car") : Lang.T("À pied", "On foot");
            return what + " · " + dist;
        }

        // ---- Alertes : pilules en haut au centre (sous le bandeau s'il y est) ----
        static void DrawAlerts()
        {
            if (alerts.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            float kOld = K; K = 1f;
            bool stripTop = HudSettings.StyleNow == HudSettings.Bandeau && (HudSettings.Get("Position") < 0 || HudSettings.Get("Position") == 4);
            float y = stripTop ? Style.Px(64) : Style.Px(14), h = P(38);
            foreach (Alert a in alerts)
            {
                float inK = Mathf.Clamp01((now - a.Born) / 0.25f);
                Style.Alpha = inK * Mathf.Clamp01((a.Until - now) / 0.6f);
                float w = Style.Width(a.Text, F(15)) + P(54);
                var r = new Rect(Mathf.Round((Screen.width - w) / 2), y - (1f - inK) * P(10), w, h);
                Style.Round(r, P(14), new Color(0.16f, 0.09f, 0.03f, 0.78f));
                Style.Ring(r, P(14), new Color(Warn.r, Warn.g, Warn.b, 0.6f));
                Icon(new Rect(r.x + P(14), r.center.y - P(9), P(18), P(18)), "stress", Warn);
                Style.Text(new Rect(r.x + P(40), r.y, w - P(48), h), a.Text, F(15), TextAnchor.MiddleLeft, new Color(1f, 0.89f, 0.76f));
                y += h + P(6);
            }
            Style.Alpha = 1f;
            K = kOld;
        }

        // [Test] Autotest=hud : jauges forcees a 22 s (faim 76, fatigue 93 : alerte, temperature 30 : froid), puis un style toutes
        // les 6 s avec une capture : Carte, Anneaux, Bandeau, Carte 125 % en haut a droite couleur Aurore, Vanilla ; tout remis a
        // la fin (style Vanilla, personnalisation par defaut).
        static int tStep;
        public static void Test(string mode, float t)
        {
            if (mode != "hud") return;
            if (t > 22f && tStep == 0)
            {
                tStep = 1;
                Game.SetGlobalFloat("PlayerHunger", 76f); Game.SetGlobalFloat("PlayerFatigue", 93f); Game.SetGlobalFloat("PlayerThirst", 35f);
                Game.SetGlobalFloat("PlayerStress", 12f); Game.SetGlobalFloat("PlayerUrine", 48f); Game.SetGlobalFloat("PlayerTemp", 30f); Game.SetGlobalFloat("PlayerSweat", 4f);
                // [Test] HudFps=1 : l'option FPS du jeu allumee (GUI/HUD/FPS), comme depuis ses reglages
                if (Config.GetInt("Test", "HudFps", 0) != 0 && fpsGo != null) fpsGo.SetActive(true);
                Log.Info("autotest : hud, jauges posees (HUD du jeu " + (hudRoot != null) + ", montre par le jeu " + GameShows() + ", FPS " + (fpsGo != null ? fpsGo.activeSelf.ToString() : "absent") + ")");
            }
            int[][] steps = { new[] { 1, 100, -1, 0 }, new[] { 2, 100, -1, 0 }, new[] { 3, 100, -1, 0 }, new[] { 1, 125, 1, 2 }, new[] { 0, 100, -1, 0 } };
            string[] names = { "hud-carte", "hud-anneaux", "hud-bandeau", "hud-carte-grande", "hud-vanilla" };
            for (int i = 0; i < steps.Length; i++)
            {
                if (tStep != i + 1 || t < 25f + i * 6f) continue;
                tStep = i + 2;
                HudSettings.Set("Style", steps[i][0]); HudSettings.Set("Taille", steps[i][1]); HudSettings.Set("Position", steps[i][2]); HudSettings.Set("Couleur", steps[i][3]);
                Autotest.CaptureSoon(names[i], 2.5f);
                Log.Info("autotest : hud, style " + steps[i][0] + " taille " + steps[i][1] + " (" + gauges.Count + " jauges, " + hidden.Count + " rendus du jeu eteints, " + alerts.Count + " alertes)");
            }
            if (tStep == steps.Length + 1 && t > 25f + steps.Length * 6f) { tStep = 99; HudSettings.Set("Style", 0); HudSettings.ResetCustom(); Log.Info("autotest : hud, remis en Vanilla (" + hidden.Count + " rendus encore eteints)"); }
        }

        // ---------------------------------------------------------------- icones (ressources du DLL : assets/hud/*.png)
        static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
        static Texture2D IconTex(string name)
        {
            Texture2D t;
            if (icons.TryGetValue(name, out t)) return t;
            t = null;
            try
            {
                using (var s = typeof(NewHud).Assembly.GetManifestResourceStream("MWCoop.hud." + name + ".png"))
                    if (s != null)
                    {
                        var b = new byte[s.Length];
                        s.Read(b, 0, b.Length);
                        t = new Texture2D(2, 2, TextureFormat.ARGB32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                        if (!t.LoadImage(b)) t = null;
                    }
            }
            catch (System.Exception e) { Log.Warn("hud : icone " + name + " illisible : " + e.Message); }
            if (t == null) Log.Warn("hud : icone " + name + " absente");
            icons[name] = t;
            return t;
        }
    }
}
