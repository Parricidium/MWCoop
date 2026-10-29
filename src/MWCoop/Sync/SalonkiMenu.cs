using System;
using UnityEngine;

namespace MWCoop
{
    // Menu de location de l'AUTOSALONKI (JD 29/10 : « le menu serait la sur chaque pancarte concernant la voiture, choix des couleurs...
    // avec une belle presentation ») : ouvert au clic sur la pancarte d'un stand. A gauche l'affiche de la voiture et sa fiche (puissance,
    // poids, rapport puissance / poids, transmission) ; a droite la couleur (les 16 de MWCoop ; prises par d'autres : grisees), la
    // livree (celle du lanceur si le joueur en a une), le prix et LOUER : l'argent du joueur (PlayerMoney, une depense reste a lui),
    // la voiture garee sur une place de livraison devant la salle (Vaz.RequestAt, au numero choisi) ; louer remplace la sienne.
    // RENDRE MA VOITURE : Vaz.Release. Echap ou la croix : ferme. Pendant qu'il est ouvert, le jeu n'a plus la souris (Menu.Block).
    public static partial class Kauppa
    {
        public static bool SalonkiOpen;
        static Stand saMenu;
        static int saColor;
        static float saOpenedAt;
        static string saNote; static float saNoteUntil; static bool saNoteBad;

        // un panneau du coop ouvert par la boutique (montage de cassette, location) : souris au menu, joueur immobile
        public static bool PanelOpen { get { return MontageOpen || SalonkiOpen; } }

        static void OpenSalonki(Stand s)
        {
            saMenu = s; SalonkiOpen = true; saOpenedAt = Time.realtimeSinceStartup; saNote = null;
            int mine = Vaz.Mine;
            saColor = mine > 0 ? mine : s.Number;
            if (saColor < 1 || !Vaz.IsFree(saColor) && saColor != mine) { saColor = 0; for (int i = 1; i <= Vaz.Count && saColor == 0; i++) if (Vaz.IsFree(i)) saColor = i; }
            WalletPanel.Open = false; CheatPanel.Open = false; MapPanel.Open = false;
            L("salon : pancarte " + s.Name + " lue");
        }

        static void SaNote(string s, bool bad) { saNote = s; saNoteBad = bad; saNoteUntil = Time.realtimeSinceStartup + 5f; }

        static void SalonkiKeys()
        {
            if (!SalonkiOpen) return;
            if (!PlayerSync.InGame || salonki == null) { SalonkiOpen = false; return; }
            if (Input.GetKeyDown(KeyCode.Escape)) SalonkiOpen = false;
        }

        static void RentSalonki(Stand s)
        {
            if (saColor < 1) { SaNote(Lang.T("Aucune couleur libre", "No free colour"), true); return; }
            if (money == null || money.Value < s.Price) { SaNote(Lang.T("Pas assez d'argent", "Not enough money"), true); return; }
            Vector3 pos; float yaw;
            if (!SalonkiBay(out pos, out yaw)) SaNote(Lang.T("Parking plein : la voiture est posée sur la première place", "Car park full: the car is put on the first bay"), false);
            if (!Vaz.RequestAt(s.Model, pos, yaw, saColor)) { SaNote(Lang.T("Location refusée", "Rental refused"), true); return; }
            money.Value -= s.Price;   // (Wallet : une depense reste au joueur)
            AudioSource cash = null;
            try { GameObject reg = Game.FindAny("STORE/StoreCashRegister/Register"); if (reg != null) cash = reg.GetComponentInChildren<AudioSource>(); } catch { }
            if (cash != null) cash.Play();
            L("salon : " + s.Name + " louee " + s.Price + " mk (couleur " + saColor + "), reste " + money.Value.ToString("F0") + " mk");
            SaNote(Lang.T("Louée ! Elle t'attend devant la salle, sur le parking", "Rented! It is waiting for you in front, on the car park"), false);
        }

        static float SP(float v) { return Style.Px(v); }

        public static void DrawSalonki()
        {
            if (!SalonkiOpen || saMenu == null) return;
            Event e = Event.current;
            float k = Mathf.Clamp01((Time.realtimeSinceStartup - saOpenedAt) / 0.16f);
            Kit.Begin(1f - (1f - k) * (1f - k));
            try { DrawSalonkiPanel(e, k); }
            finally { Kit.End(); }
        }

        static void DrawSalonkiPanel(Event e, float k)
        {
            Stand s = saMenu;
            VazCar.Model m = VazCar.ModelOf(s.Model);
            bool paint = e.type == EventType.Repaint;
            float W = Mathf.Min(SP(1020), Screen.width - SP(32)), H = Mathf.Min(SP(600), Screen.height - SP(32));
            float x = Mathf.Round((Screen.width - W) / 2), y = Mathf.Round((Screen.height - H) / 2 + (1f - k) * SP(12));
            Kit.Frame(new Rect(x, y, W, H));
            float pad = SP(22);
            // entete : nom de la salle, croix
            var hr = new Rect(x + pad, y + SP(14), W - 2 * pad, SP(28));
            if (paint) Style.Text(new Rect(hr.x, hr.y, hr.width, hr.height), "AUTOSALONKI  ·  " + Lang.T("LOCATION", "RENTAL"), 12, TextAnchor.MiddleLeft, Kit.Ice);
            if (Kit.Button(new Rect(hr.xMax - SP(26), hr.center.y - SP(13), SP(26), SP(26)), "", 0, false, true, "croix")) { SalonkiOpen = false; return; }
            float top = y + SP(52), colGap = SP(22);
            float lw = Mathf.Round((W - 2 * pad - colGap) * 0.58f), rw = W - 2 * pad - colGap - lw;
            float lx = x + pad, rx = lx + lw + colGap;
            // ---- a gauche : affiche, fiche
            float ph = Mathf.Round(lw * 9f / 16f);
            var pr = new Rect(lx, top, lw, ph);
            if (paint)
            {
                Style.Round(pr, SP(14), new Color(0f, 0f, 0f, 0.35f));
                if (s.Poster != null) { Color oc = GUI.color; GUI.color = new Color(1, 1, 1, Style.Alpha); GUI.DrawTexture(pr, s.Poster, ScaleMode.ScaleAndCrop); GUI.color = oc; }
                else Style.Text(pr, s.Name, 22, TextAnchor.MiddleCenter, Kit.Muted);
                Style.Ring(pr, SP(14), Kit.Edge);
            }
            float sy = pr.yMax + SP(16);
            if (paint)
            {
                float rowH = SP(26), lab = SP(150), val = SP(110);
                VazCar.Spec sp = m.Sp;
                float power = sp != null ? sp.Power : 75f, mass = sp != null && sp.Mass > 0f ? sp.Mass : 900f, chT = power / (mass / 1000f);
                string drive = sp == null ? Lang.T("Traction", "Front-wheel drive") : sp.Rwd ? Lang.T("Propulsion", "Rear-wheel drive") : Lang.T("4 roues motrices", "All-wheel drive");
                string[] labs = { Lang.T("Puissance", "Power"), Lang.T("Poids", "Weight"), Lang.T("Puissance / poids", "Power / weight") };
                string[] vals = { Mathf.RoundToInt(power) + " ch", Mathf.RoundToInt(mass) + " kg", Mathf.RoundToInt(chT) + " ch/t" };
                float[] fr = { power / 500f, 1f - Mathf.Clamp01((mass - 700f) / 800f), chT / 520f };
                for (int i = 0; i < 3; i++)
                {
                    float ry = sy + i * rowH;
                    Style.Text(new Rect(lx, ry, lab, rowH), labs[i], 13, TextAnchor.MiddleLeft, Kit.Muted, false);
                    Style.Text(new Rect(lx + lab, ry, val, rowH), vals[i], 13, TextAnchor.MiddleLeft, Kit.Ink);
                    Style.Bar(new Rect(lx + lab + val, ry + rowH / 2 - SP(4), lw - lab - val, SP(8)), fr[i], Kit.Ice);
                }
                Style.Text(new Rect(lx, sy + 3 * rowH, lab, rowH), Lang.T("Transmission", "Drivetrain"), 13, TextAnchor.MiddleLeft, Kit.Muted, false);
                Style.Text(new Rect(lx + lab, sy + 3 * rowH, lw - lab, rowH), drive, 13, TextAnchor.MiddleLeft, Kit.Ink);
            }
            // ---- a droite : nom, categorie, couleur, livree, prix, boutons
            float cy = top;
            if (paint) Style.Title(new Rect(rx, cy, rw, SP(40)), s.Name, Kit.Ink, TextAnchor.MiddleLeft, 26);
            cy += SP(42);
            float px = rx + Kit.Pill(rx, cy + SP(9), CategoryTitle(s.Group), Kit.Amber) + SP(8);
            string desc = m.Desc;
            if (paint && desc.Length > 0) Style.Text(new Rect(px, cy, rw - (px - rx), SP(18)), desc, 12, TextAnchor.MiddleLeft, Kit.Muted, false);
            cy += SP(34);
            Kit.Section(new Rect(rx, cy, rw, SP(18)), Lang.T("COULEUR", "COLOUR"));
            cy += SP(24);
            int mine = Vaz.Mine;
            float sw = Mathf.Floor((rw - 7 * SP(8)) / 8f); sw = Mathf.Min(sw, SP(34));
            for (int i = 1; i <= Vaz.Count; i++)
            {
                int col = (i - 1) % 8, row = (i - 1) / 8;
                var r = new Rect(rx + col * (sw + SP(8)), cy + row * (sw + SP(8)), sw, sw);
                bool free = Vaz.IsFree(i) || i == mine, sel = i == saColor, over = r.Contains(e.mousePosition);
                if (paint)
                {
                    Style.Round(r, sw / 2, Kit.A(Vaz.ColorOf(i), free ? 1f : 0.25f));
                    Style.Ring(r, sw / 2, sel ? Kit.Ice : Kit.A(Color.white, over && free ? 0.6f : 0.25f));
                    if (sel) Style.Ring(new Rect(r.x - SP(3), r.y - SP(3), r.width + SP(6), r.height + SP(6)), sw / 2 + SP(3), Kit.Ice);
                }
                if (free && over && e.type == EventType.MouseDown && e.button == 0) { saColor = i; e.Use(); }
            }
            cy += 2 * (sw + SP(8)) + SP(2);
            if (paint && saColor > 0) Style.Text(new Rect(rx, cy, rw, SP(18)), Vaz.ColorName(saColor) + Lang.T(", numéro ", ", number ") + saColor, 12, TextAnchor.MiddleLeft, Kit.Soft, false);
            cy += SP(22);
            string liv = EvoLivery.MyHash(s.Model);
            if (paint) Style.Text(new Rect(rx, cy, rw, SP(18)), liv.Length > 0 ? Lang.T("Ta livrée perso (lanceur, Addons)", "Your custom livery (launcher, Add-ons)") : Lang.T("Couleur + numéro (livrée perso : lanceur, Addons)", "Colour + number (custom livery: launcher, Add-ons)"), 11, TextAnchor.MiddleLeft, Kit.Faint, false);
            // prix
            float by = y + H - pad - SP(40);
            float priceY = by - SP(110);
            Kit.Section(new Rect(rx, priceY, rw, SP(18)), Lang.T("LOCATION", "RENTAL"));
            if (paint)
            {
                Style.Title(new Rect(rx, priceY + SP(18), rw, SP(46)), s.Price + " mk", Kit.Amber, TextAnchor.MiddleLeft, 34);
                float cash = money != null ? money.Value : 0f;
                Style.Text(new Rect(rx, priceY + SP(64), rw, SP(18)), Lang.T("Ton argent : ", "Your money: ") + Mathf.FloorToInt(cash) + " mk", 12, TextAnchor.MiddleLeft, cash >= s.Price ? Kit.Green : Kit.Red, false);
            }
            if (saNote != null && Time.realtimeSinceStartup < saNoteUntil && paint)
                Style.Text(new Rect(rx, by - SP(26), rw, SP(20)), saNote, 12, TextAnchor.MiddleLeft, saNoteBad ? Kit.Red : Kit.Green);
            bool rich = money != null && money.Value >= s.Price;
            float bw = mine > 0 ? Mathf.Floor((rw - SP(10)) / 2f) : rw;
            if (Kit.Button(new Rect(rx, by, bw, SP(40)), rich ? Lang.T("LOUER", "RENT") : Lang.T("PAS ASSEZ D'ARGENT", "NOT ENOUGH MONEY"), 1, false, rich && saColor > 0, null, 15)) RentSalonki(s);
            if (mine > 0 && Kit.Button(new Rect(rx + bw + SP(10), by, rw - bw - SP(10), SP(40)), Lang.T("RENDRE LA MIENNE", "RETURN MINE"), 0, false, true, null, 13))
            { Vaz.Release(); SaNote(Lang.T("Voiture rendue", "Car returned"), false); }
            if (paint && mine > 0) Style.Text(new Rect(lx, y + H - pad - SP(18), lw, SP(18)), Lang.T("Louer remplace ta voiture actuelle (n° ", "Renting replaces your current car (no. ") + mine + ")", 11, TextAnchor.MiddleLeft, Kit.Faint, false);
        }
    }
}
