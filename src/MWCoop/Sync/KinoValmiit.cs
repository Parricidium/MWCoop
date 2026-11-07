using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MWCoop
{
    // Films prets (JD 07/11 : un onglet « Bobines » du lanceur pour « pouvoir tout gerer de la ») : le lanceur importe des
    // videos dans le dossier des films de la partie (videot\fl-<hex>.mwv, MWCoopRT.dll comme en jeu) et ecrit leur titre dans
    // videot\titres.txt (« film<TAB>titre[<TAB>duree ms] », UTF-8). En jeu, une bobine ou une cassette en main, R : FILMS PRETS
    // liste les films faits sur ce PC (ceux du lanceur, fl- ou fL-, d'abord) (pas dans recus.txt) qui ne sont sur aucune cassette, bobine ou projecteur ; un clic le
    // met sur la bobine (a la suite, comme un import ; sur une cassette, partage a tous comme un import).
    public static partial class Kauppa
    {
        class ReadyFilm { public string Id, Title; public long Bytes; public DateTime When; public uint Ms; }
        static List<ReadyFilm> readyList;
        static bool mReady;
        static Vector2 readyScroll;

        static string TitlesFile { get { return Path.Combine(FilmDir, "titres.txt"); } }

        // titres.txt : film -> (titre, duree ms ou 0)
        static Dictionary<string, KeyValuePair<string, uint>> ReadTitles()
        {
            var d = new Dictionary<string, KeyValuePair<string, uint>>();
            try
            {
                if (!File.Exists(TitlesFile)) return d;
                foreach (string l in File.ReadAllLines(TitlesFile, Encoding.UTF8))
                {
                    string[] c = l.Split('\t');
                    if (c.Length < 2 || c[0].Trim().Length == 0) continue;
                    uint ms = 0;
                    if (c.Length > 2) uint.TryParse(c[2].Trim(), out ms);
                    d[c[0].Trim()] = new KeyValuePair<string, uint>(Clean(c[1]), ms);
                }
            }
            catch (Exception e) { L("video : titres.txt : " + e.Message); }
            return d;
        }

        // films faits sur ce PC, sur aucune cassette, bobine ou projecteur ; ceux du lanceur d'abord, puis les plus recents
        static List<ReadyFilm> ReadyFilms()
        {
            var list = new List<ReadyFilm>();
            try
            {
                if (!Directory.Exists(FilmDir)) return list;
                var used = new HashSet<string>();
                foreach (Esine e in esineet) foreach (string part in e.Data.Split('|')) foreach (string f in Clips(part)) used.Add(f);
                foreach (Projector p in projectors) if (p != null) foreach (string f in Clips(p.Content)) used.Add(f);
                var recus = new HashSet<string>();
                if (File.Exists(RecusFile)) foreach (string l in File.ReadAllLines(RecusFile)) recus.Add(l.Trim());
                Dictionary<string, KeyValuePair<string, uint>> titles = ReadTitles();
                foreach (FileInfo fi in new DirectoryInfo(FilmDir).GetFiles("*.mwv"))
                {
                    string id = Path.GetFileNameWithoutExtension(fi.Name);
                    if (id.StartsWith("export-") || used.Contains(id) || recus.Contains(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Length > 64) continue;
                    var r = new ReadyFilm { Id = id, Bytes = fi.Length, When = fi.LastWriteTime, Title = "" };
                    KeyValuePair<string, uint> t;
                    if (titles.TryGetValue(id, out t)) { r.Title = t.Key; r.Ms = t.Value; }
                    Film cached;
                    if (r.Ms == 0 && filmCache.TryGetValue(id, out cached) && cached != null && cached.Done) r.Ms = cached.LengthMs;
                    list.Add(r);
                }
                list.Sort((a, b) =>
                {
                    bool la = FromLauncher(a.Id), lb = FromLauncher(b.Id);
                    return la != lb ? (la ? -1 : 1) : b.When.CompareTo(a.When);
                });
            }
            catch (Exception e) { L("video : films prets : " + e.Message); }
            return list;
        }

        // importe par le lanceur : fl-<hex> (onglet Bobines), fL-<hex> (onglet VHS)
        static bool FromLauncher(string id) { return id.StartsWith("fl-", StringComparison.OrdinalIgnoreCase); }

        static void OpenReady() { readyList = ReadyFilms(); readyScroll = Vector2.zero; mReady = true; }

        // bouton de la colonne de droite (bobine : a cote d'IMPORTER ; cassette : seul)
        static void DrawReadyButton(Rect r, Event e)
        {
            if (readyList == null) readyList = ReadyFilms();
            if (MButton(r, string.Format(Lang.T("FILMS PRÊTS ({0})", "READY FILMS ({0})"), readyList.Count), false, e, !mReady, false, 13)) OpenReady();
        }

        // liste a la place de l'ecran du montage
        static void DrawReadyList(Rect pr, Event e)
        {
            bool reel = mTape.Kind == KKela;
            if (e.type == EventType.Repaint) Style.Fill(pr, new Color(0f, 0f, 0f, 0.55f));
            float pad = Style.Px(10), rh = Style.Px(46), bh = Style.Px(34);
            Style.Text(new Rect(pr.x + pad, pr.y + pad, pr.width - 2 * pad, Style.Px(24)),
                       reel ? Lang.T("Clique sur un film pour le mettre sur la bobine.", "Click a film to put it on the reel.")
                            : Lang.T("Clique sur un film pour l'ajouter sur la cassette.", "Click a film to add it to the tape."), 14, TextAnchor.MiddleLeft, Style.Dim, false);
            var back = new Rect(pr.xMax - pad - Style.Px(120), pr.yMax - pad - bh, Style.Px(120), bh);
            var area = new Rect(pr.x + pad, pr.y + pad + Style.Px(30), pr.width - 2 * pad, back.y - pr.y - pad - Style.Px(38));
            if (readyList == null || readyList.Count == 0)
                Style.Text(area, Lang.T("Aucun film prêt : importe une vidéo depuis le lanceur (onglet Bobines) ou avec IMPORTER UNE VIDÉO…", "No film ready: import a video from the launcher (Reels tab) or with IMPORT A VIDEO…"), 15, TextAnchor.MiddleCenter, Style.Dim, false, true);
            else
            {
                var view = new Rect(0f, 0f, area.width - Style.Px(18), readyList.Count * (rh + Style.Px(6)));
                readyScroll = GUI.BeginScrollView(area, readyScroll, view);
                Event ev = Event.current;
                ReadyFilm pick = null;
                for (int i = 0; i < readyList.Count; i++)
                {
                    ReadyFilm f = readyList[i];
                    var rr = new Rect(0f, i * (rh + Style.Px(6)), view.width, rh);
                    if (rr.yMax < readyScroll.y || rr.y > readyScroll.y + area.height) continue;
                    string info = (f.Ms > 0 ? Clock(f.Ms / 1000f) + "  ·  " : "") + SizeText(f.Bytes / 1024) + "  ·  " + f.When.ToString("dd/MM HH:mm");
                    string name = f.Title.Length > 0 ? f.Title : Lang.T("sans titre", "untitled") + " (" + f.Id + ")";
                    if (MButton(rr, name + "      " + info, FromLauncher(f.Id), ev)) pick = f;
                }
                GUI.EndScrollView();
                if (pick != null) PutReady(pick);
            }
            if (MButton(back, Lang.T("RETOUR", "BACK"), false, e)) mReady = false;
        }

        // le film va sur la cassette ou la bobine du montage, a la suite (comme un import)
        static void PutReady(ReadyFilm f)
        {
            Esine tape = mTape;
            if (tape == null || !HaveFilm(f.Id)) { readyList = ReadyFilms(); return; }
            if (tape.Data.StartsWith("url:")) SetData(tape, "tyhja~" + TapeTitle(tape.Data));   // (bobine « lien » : redevient une bobine de films)
            if (Clips(tape.Data).Count >= MaxClips) { Note(Lang.T("Cette cassette est pleine.", "This tape is full."), true); return; }
            string films = TapeFilm(tape.Data), title = TapeTitle(tape.Data);
            if (films.Length == 0 && f.Title.Length > 0) title = f.Title;   // (bobine vide : elle prend le titre du film)
            else if (title.Length == 0) title = f.Title.Length > 0 ? f.Title : Clean(mTitle);
            SetData(tape, (films.Length > 0 ? films + "+" : "") + f.Id + "~" + title);
            if (tape.Kind != KKela) SendFilm(f.Id);   // (bobine : propose aux autres au projecteur, avec leur accord -- KinoJako)
            L("video : film pret " + f.Id + " mis sur " + (tape.Kind == KKela ? "la bobine " : "la cassette ") + tape.Id);
            mTitle = TapeTitle(tape.Data); mReady = false; readyList = null;
            LoadMontage(); mA = 0f; mB = mTotal; Seek(0f);
            Note(string.Format(tape.Kind == KKela ? Lang.T("Film mis sur la bobine : « {0} ».", "Film put on the reel: \"{0}\".") : Lang.T("Film ajouté sur la cassette : « {0} ».", "Film added to the tape: \"{0}\"."), f.Title.Length > 0 ? f.Title : f.Id), false);
        }
    }
}
