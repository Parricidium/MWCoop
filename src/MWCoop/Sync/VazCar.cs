using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Construction d'une VAZ-2108 (Vaz.cs : qui a quelle couleur). Modele Â« VAZ-2108 Rally Â» de matthew0451 (Sketchfab,
    // CC BY-NC 4.0), converti et allege hors du jeu (~54 000 triangles, livree retiree) dans MWCoop/vaz : carrosserie,
    // portieres, capot, volant (.p205 : maillages + matieres), textures PNG, infos.txt (centres des roues, charnieres,
    // volant, feux, repere de la voiture : x a droite, y haut, z avant), plaques numero-01..16.png.
    // Meme technique que les Jokkis (Jokkis.Build) : copie de la SORBET (automates de sauvegarde vides, identifiants
    // prefixes, rendus caches, portieres arriere et hayon eteints, degats coupes), essieux deplaces a l'empattement de la VAZ ;
    // puis la VAZ posee pour que le milieu de ses roues coincide avec celui de la SORBET, ses portieres accrochees aux portieres
    // avant de la SORBET (charnieres deplacees sur les siennes : elles s'ouvrent avec les poignees du jeu), roues visibles du
    // rallye du jeu (RALLYCAR3), volant qui suit celui de la SORBET, feux allumes avec les lumieres de la copie, peinture a la
    // couleur du joueur et son numero sur les portieres.
    public static class VazCar
    {
        // Modeles des voitures des joueurs (JD, 20/10) : la VAZ-2108 (MWCoop/vaz, dans la version publiee) et les addons --
        // Evo III Gr. A (MWCoop/addons/evo3), 205 T16 Evo 1 Gr. B (MWCoop/addons/t16, JD 21/10) : EXTENSIONS PRIVEES de JD,
        // jamais dans le depot ni les versions publiees (modeles de mods rFactor 1 sans licence connue). Meme construction ; ce
        // qui change : dossier, poste de conduite, commandes du tableau de bord, frein a main, moteur, tenue de route (Spec,
        // VazHandling), sons (MWCoop/addons/<dossier>/sons, VazSound).
        public class Model
        {
            public int Id; public string Folder;
            // nom affiche : NomCourt de <dossier>\addon.ini (addons en extension, comme l'onglet Addons du lanceur), sinon celui-ci
            string name; bool nameRead;
            public string Name
            {
                get { Read(); return Tag != null ? name + Tag : name; }
                set { name = value; }
            }
            public string Tag;   // (suffixe du nom affiche)
            public bool Standalone { get { return Id >= ModelR5S; } }   // (etiquette « Standalone » au F10)
            public int Copy { get { return CopyOf(Id); } }                // (standalone : numero de la copie dont elle reprend le modele)
            // meme modele (dossier, poste, tenue) sous un autre numero : la R5 standalone (StandaloneCar)
            public Model CloneAs(int id, string tag) { var m = (Model)MemberwiseClone(); m.Id = id; m.Tag = tag; return m; }
            public Vector3 Head, Engine, BrakePivot, BrakeGrip;
            public KeyValuePair<string, Vector3>[] Controls;
            public Knob[] Knobs;              // boutons visibles poses sur le modele (commande du jeu dessous)
            public string[] Off;              // commandes de la SORBET coupees (colliders)
            public Vector3 Dome; public bool HasDome;   // plafonnier (Functions/InteriorLight) pose ici
            public float BrakeAngle = 1f;     // amplitude du levier (KnobPos 0-20 degres x ceci)
            public float EyeDrop;             // yeux (DriverHeadPivot) plus bas que Head, sans bouger l'assise (zone d'entree, SpawnPlayer)
            public float SoloEyeDrop;         // standalone : yeux de depart (tenue.ini) plus bas -- habitacle detaille du mod, pavillon plus bas (JD 29/10)
            public bool SeatEyes;             // vue de conduite recalee sur le pivot de tete a l'entree (SeatView)
            public float SteerDeg;            // volant : degres a fond de braquage, fixes (0 : rapport releve sur la SORBET)
            public float ControlSize;         // taille maxi (m) des zones cliquables des commandes deplacees (0 : celles du jeu)
            public Dictionary<string, Vector3> Info;
            public Spec Sp;                   // tenue de route propre (null : Rivett rallye, VazHandling)
            // description : Description / DescriptionEn de addon.ini (panneau F10)
            public string Desc { get { Read(); return Lang.T(desc ?? "", descEn ?? desc ?? ""); } }
            string desc, descEn, group;
            // categorie (filtre du F10 et de l'onglet Addons du lanceur -- JD, 23/10) : Categorie= de addon.ini, sinon le « Gr. X »
            // du nom de la tenue de route ; la VAZ-2108, livree avec le mod : « MWCoop »
            public string Group
            {
                get
                {
                    if (Folder == "vaz") return "MWCoop";
                    Read();
                    if (!string.IsNullOrEmpty(group)) return group;
                    string n = Sp != null ? Sp.Name ?? "" : "";
                    int i = n.IndexOf("Gr. ");
                    return i >= 0 && i + 4 < n.Length ? n.Substring(i, 5) : Lang.T("Autres", "Others");
                }
            }
            void Read()
            {
                if (nameRead || Folder == "vaz") return;
                nameRead = true;
                try
                {
                    string f = Path.Combine(Dir, "addon.ini");
                    if (File.Exists(f))
                        foreach (string l in File.ReadAllLines(f, System.Text.Encoding.UTF8))
                        {
                            if (l.StartsWith("NomCourt=") && l.Length > 9) name = l.Substring(9).Trim();
                            else if (l.StartsWith("DescriptionEn=")) descEn = l.Substring(14).Trim();
                            else if (l.StartsWith("Description=")) desc = l.Substring(12).Trim();
                            else if (l.StartsWith("Categorie=")) group = l.Substring(10).Trim();
                        }
                }
                catch { }
            }
            // (addons : MWCoop\addons\<dossier> depuis 1.5.1 -- dossier ouvert par le lanceur, onglet Addons ; avant : MWCoop\<dossier>)
            public string Dir
            {
                get
                {
                    string root = System.Environment.GetEnvironmentVariable("MWCOOP_DIR") ?? "MWCoop";
                    if (Folder != "vaz") { string a = Path.Combine(Path.Combine(root, "addons"), Folder); if (Directory.Exists(a)) return a; }
                    return Path.Combine(root, Folder);
                }
            }
            public bool HasFiles { get { return File.Exists(Path.Combine(Dir, "carrosserie.p205")); } }
            public bool Retired;   // (retiree de la liste meme installee : ni F10 ni demande ; un ancien client qui la demande montre une VAZ)
        }
        public class Knob { public string Path; public Vector3 At, Normal; public Color Col; public bool Choke; }
        // Tenue de route d'un addon (VazHandling.Spec) : moteur, boite, masse, repartition (part sur l'avant), ressorts.
        public class Spec
        {
            public string Name; public float Power, PowerRpm, Torque, TorqueRpm, MaxRpm, Inertia = 0.15f, Final = 4.4f, Clutch = 420f;
            public float Mass, Com, Steer, Spring, Damp; public float[] Gears;
            public float AntiRoll, ComDrop;   // barres antiroulis (N/m, 0 : celles du jeu) ; centre de gravite abaisse (m) -- JD 21/10, tonneaux trop faciles
            public float Handbrake;           // couple du frein a main sur l'essieu arriere (N.m, 0 : celui du jeu) -- JD 21/10, partir en travers
            // comportement de rallye moderne (JD 23/10 : « les voitures addons survirent beaucoup, et le frein a main ne l'aide pas a
            // partir en glisse ») : adherence de l'essieu arriere (x, 1 : comme l'avant), adherence arriere pendant le frein a main
            // (x, 1 : rien), barres antiroulis avant / arriere (N/m, 0 : AntiRoll), amortissement du lacet (Rigidbody.angularDrag,
            // 0 : celui de la Rivett)
            public float RearGrip = 1f, HandbrakeSlide = 1f, AntiRollFront, AntiRollRear, YawDamp;
            public bool Rwd;                  // propulsion (false : integrale, comme les autres addons) -- Rihno 5 Maxi, JD 28/10
            public bool Fwd;                  // traction (standalone : Transmission=traction) -- Golf GTI, 31/10
        }
        public const int ModelVaz = 0, ModelEvo = 1, ModelT16 = 2, ModelImp = 3, ModelSq = 4, ModelT16b = 5, ModelR5 = 6, ModelR5S = 7;
        // standalones des autres addons (JD 29/10 : « on va faire les autres en standalone, memes criteres ») : 8 a 12, copies 1 a 5
        public const int ModelEvoS = 8, ModelT16S = 9, ModelImpS = 10, ModelSqS = 11, ModelT16bS = 12;
        // standalones sans copie (leur numero est aussi leur « copie ») : Golf GTI 1983
        public const int ModelGolf = 13;
        public static int StandaloneOf(int copy) { return copy == ModelR5 ? ModelR5S : copy >= ModelEvo && copy <= ModelT16b ? ModelR5S + copy : -1; }
        public static int CopyOf(int id) { return id == ModelR5S ? ModelR5 : id >= ModelEvoS && id <= ModelT16bS ? id - ModelR5S : id; }
        public static readonly Model Vaz2108 = new Model { Id = ModelVaz, Folder = "vaz", Name = "VAZ-2108" };
        // Evo III : tete du mod a part de JD (MitsubishiEvo3GrA, -0,35 ; 0,66 ; -0,20 dans la voiture) ramenee au repere du modele
        // (carrosserie posee 0,249 plus bas et 0,099 en arriere) ; moteur transversal avant.
        // Poste de conduite (JD, 20/10, capture annotee) : le levier du frein a main est la tige carbone a droite du volant
        // (frein.p205, pivot a sa base, serre = penche vers l'arriere, 28 degres) ; quatre boutons sur le panneau entre la jante
        // et le levier vus du siege (face arriere du boitier de console, z 0,49), de haut en bas : contact (rouge), starter (bleu :
        // tire / pousse comme la SORBET, il sort de la console), phares (blanc), warning (orange), zones cliquables du jeu
        // reduites a 2,4 cm sous chacun ; toutes les autres commandes du tableau de bord
        // coupees (chauffage, essuie-glaces, degivrage, radio, pare-soleil, des) ; plafonnier gardé, sous le pavillon.
        static readonly Vector3 EvoFace = new Vector3(-0.19f, 0.10f, -0.97f).normalized;   // (face arriere du boitier, vers le pilote)
        public static readonly Model Evo3 = new Model { Id = ModelEvo, Folder = "evo3", Name = "Ebo III",
            Head = new Vector3(-0.35f, 0.909f, -0.101f), SeatEyes = true, EyeDrop = -0.10f, SteerDeg = 450f, Engine = new Vector3(0f, 0.75f, 1.55f),
            BrakePivot = new Vector3(-0.05f, 0.275f, 0.313f), BrakeGrip = new Vector3(-0.075f, 0.60f, 0.355f), BrakeAngle = 1.4f,
            Controls = new[] {
                new KeyValuePair<string, Vector3>("Functions/SteeringColumnPivot/SteeringColumn/IGNITIONxSorbet", new Vector3(-0.095f, 0.690f, 0.482f)),
                new KeyValuePair<string, Vector3>("Functions/Dashboard/Choke/ButtonChoke", new Vector3(-0.095f, 0.660f, 0.480f)),
                new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonLightModes", new Vector3(-0.095f, 0.630f, 0.477f)),
                new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonHazard", new Vector3(-0.095f, 0.600f, 0.475f)) },
            Knobs = new[] {
                new Knob { At = new Vector3(-0.095f, 0.690f, 0.4895f), Normal = EvoFace, Col = new Color(0.80f, 0.06f, 0.05f) },
                new Knob { At = new Vector3(-0.095f, 0.660f, 0.4870f), Normal = EvoFace, Col = new Color(0.10f, 0.25f, 0.85f), Choke = true, Path = "Functions/Dashboard/Choke/ButtonChoke" },
                new Knob { At = new Vector3(-0.095f, 0.630f, 0.4845f), Normal = EvoFace, Col = new Color(0.92f, 0.92f, 0.90f) },
                new Knob { At = new Vector3(-0.095f, 0.600f, 0.4825f), Normal = EvoFace, Col = new Color(0.95f, 0.45f, 0.05f) } },
            Off = new[] { "Functions/Dashboard/Buttons/ButtonWipers", "Functions/Dashboard/Buttons/ButtonWindowHeater", "Functions/Dashboard/Buttons/ButtonHeaterTemp",
                          "Functions/Dashboard/Buttons/ButtonHeaterDirection", "Functions/Dashboard/Buttons/ButtonHeaterBlower", "Functions/Sunvisors",
                          "Functions/VINP_RadioSorbet", "Functions/VINP_FurDiceSorbet" },
            Dome = new Vector3(0f, 1.10f, -0.05f), HasDome = true, ControlSize = 0.024f,
            // (mod a part de JD, Grupo A engine.ini du mod rFactor) : 370 ch a 5 900 tr/min, 495 Nm a 4 200, rupteur 7 500,
            // integrale, 1 230 kg, 61 % sur l'avant (moteur transversal avant), braquage 35
            Sp = new Spec { Name = "Evo III Gr. A", Power = 370f, PowerRpm = 5900f, Torque = 495f, TorqueRpm = 4200f, MaxRpm = 7500f,
                            Mass = 1230f, Com = 0.61f, Steer = 35f, Spring = 45300f, Damp = 2850f, Gears = new[] { -3.3f, 0f, 2.95f, 2.0f, 1.52f, 1.2f, 0.95f },
                            Handbrake = 6000f, RearGrip = 1.15f, HandbrakeSlide = 0.45f, AntiRollFront = 32000f, AntiRollRear = 16000f, YawDamp = 0.6f, ComDrop = 0.04f } };   // (rallye moderne, comme l'Ebo 2 -- JD 23/10)
        // 205 T16 Evo 1 Gr. B (JD 21/10, rally cars GrB/205T162 : « Peugeot 205 T16 1985 », Salonen) : moteur central arriere,
        // integrale ; poste de conduite d'apres
        // le volant du pilote (le meme que l'Evo III, 0,19 plus en arriere et 0,06 plus haut) ; frein a main : la tige carbone de
        // l'Evo III deplacee de meme (outils/rf1/t16e2_frein.py) ; commandes comme l'Evo III : contact (rouge), starter (bleu,
        // tire / pousse), phares (blanc), warning (orange) en colonne sur la face de la planche (z 0,54, tournee vers le pilote)
        // juste a droite du volant, vue depuis la tete (sous le bord du collider front1 de la SORBET, qui cachait le contact
        // a 0,70) ; le reste du tableau de bord coupe. Tenue de route : base de la 2052 du mod
        // prive de JD validee en jeu (46 % sur l'avant, braquage 40, boite et pont), moteur et masse de l'Evo 1 (JD : « c'est
        // l'Evo 1 ») : 350 ch a 7 500 tr/min, 450 Nm a 5 000, rupteur 8 000, 940 kg (Mass du .hdv rFactor).
        // (JD 29/10 : « la Ebo 1 on va la supprimer, l'interieur et tout bug, on va l'enlever de la liste tout court » -- retiree,
        // copie et standalone ; numeros 2 et 9 gardes pour ne decaler aucun autre modele)
        public static readonly Model T16 = new Model { Id = ModelT16, Folder = "t16", Name = "205 Ebo 1", Retired = true,
            Head = new Vector3(-0.32f, 0.969f, -0.291f), SeatEyes = true, EyeDrop = -0.10f, SoloEyeDrop = 0.06f, SteerDeg = 450f, Engine = new Vector3(0f, 0.55f, -0.85f),
            BrakePivot = new Vector3(-0.02f, 0.335f, 0.123f), BrakeGrip = new Vector3(-0.045f, 0.66f, 0.165f), BrakeAngle = 1.4f,
            Controls = new[] {
                new KeyValuePair<string, Vector3>("Functions/SteeringColumnPivot/SteeringColumn/IGNITIONxSorbet", new Vector3(-0.03f, 0.665f, 0.530f)),
                new KeyValuePair<string, Vector3>("Functions/Dashboard/Choke/ButtonChoke", new Vector3(-0.03f, 0.630f, 0.530f)),
                new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonLightModes", new Vector3(-0.03f, 0.595f, 0.530f)),
                new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonHazard", new Vector3(-0.03f, 0.560f, 0.530f)) },
            Knobs = new[] {
                new Knob { At = new Vector3(-0.03f, 0.665f, 0.537f), Normal = Vector3.back, Col = new Color(0.80f, 0.06f, 0.05f) },
                new Knob { At = new Vector3(-0.03f, 0.630f, 0.537f), Normal = Vector3.back, Col = new Color(0.10f, 0.25f, 0.85f), Choke = true, Path = "Functions/Dashboard/Choke/ButtonChoke" },
                new Knob { At = new Vector3(-0.03f, 0.595f, 0.537f), Normal = Vector3.back, Col = new Color(0.92f, 0.92f, 0.90f) },
                new Knob { At = new Vector3(-0.03f, 0.560f, 0.537f), Normal = Vector3.back, Col = new Color(0.95f, 0.45f, 0.05f) } },
            Off = new[] { "Functions/Dashboard/Buttons/ButtonWipers", "Functions/Dashboard/Buttons/ButtonWindowHeater", "Functions/Dashboard/Buttons/ButtonHeaterTemp",
                          "Functions/Dashboard/Buttons/ButtonHeaterDirection", "Functions/Dashboard/Buttons/ButtonHeaterBlower", "Functions/Sunvisors",
                          "Functions/VINP_RadioSorbet", "Functions/VINP_FurDiceSorbet" },
            Dome = new Vector3(0f, 1.05f, -0.25f), HasDome = true, ControlSize = 0.024f,
            Sp = new Spec { Name = "205 T16 Evo 1 Gr. B", Power = 350f, PowerRpm = 7500f, Torque = 450f, TorqueRpm = 5000f, MaxRpm = 8000f,
                            Mass = 940f, Com = 0.46f, Steer = 40f, Spring = 34000f, Damp = 2200f, Gears = new[] { -3.3f, 0f, 2.95f, 2.0f, 1.52f, 1.2f, 0.95f },
                            Handbrake = 6000f, RearGrip = 1.15f, HandbrakeSlide = 0.45f, AntiRollFront = 25000f, AntiRollRear = 12500f, YawDamp = 0.6f, ComDrop = 0.04f } };   // (rallye moderne, comme l'Ebo 2 -- JD 23/10)
        // Subaru Impreza 96 Gr. A (JD 21/10, rally cars GrA8/Subaru_96, « 555 » McRae 1995) : moteur avant, integrale ; poste
        // de conduite d'apres le volant du pilote (le meme que l'Evo III, decale de (0,044 ; 0,114 ; -0,177)) ; frein a main :
        // son propre levier (le long du tunnel, poignee vers l'avant, pivot a l'arriere : se leve quand il est serre, comme la
        // VAZ) ; boutons comme l'Evo III, en ligne sur la face inclinee de la console centrale (y 0,65, z 0,43), de gauche a
        // droite contact, starter, phares, warning (capture annotee de JD, 21/10). Tenue de route : 300 ch a 5 500 tr/min,
        // 470 Nm a 4 000, rupteur 7 500, 1 130 kg (Mass du .hdv rFactor), 60 % sur l'avant, braquage 35.
        static readonly Vector3 ImpFace = new Vector3(0f, 0.50f, -0.87f).normalized;
        public static readonly Model Imp96 = new Model { Id = ModelImp, Folder = "imp96", Name = "Imprezza 95",
            Head = new Vector3(-0.306f, 1.023f, -0.278f), SeatEyes = true, EyeDrop = -0.10f, SoloEyeDrop = 0.06f, SteerDeg = 450f, Engine = new Vector3(0f, 0.55f, 1.55f),
            BrakePivot = new Vector3(0f, 0.36f, -0.10f), BrakeGrip = new Vector3(0f, 0.47f, 0.10f),
            Controls = new[] {
                new KeyValuePair<string, Vector3>("Functions/SteeringColumnPivot/SteeringColumn/IGNITIONxSorbet", new Vector3(-0.065f, 0.653f, 0.424f)),
                new KeyValuePair<string, Vector3>("Functions/Dashboard/Choke/ButtonChoke", new Vector3(-0.030f, 0.653f, 0.424f)),
                new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonLightModes", new Vector3(0.005f, 0.653f, 0.424f)),
                new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonHazard", new Vector3(0.040f, 0.653f, 0.424f)) },
            Knobs = new[] {
                new Knob { At = new Vector3(-0.065f, 0.651f, 0.429f), Normal = ImpFace, Col = new Color(0.80f, 0.06f, 0.05f) },
                new Knob { At = new Vector3(-0.030f, 0.651f, 0.429f), Normal = ImpFace, Col = new Color(0.10f, 0.25f, 0.85f), Choke = true, Path = "Functions/Dashboard/Choke/ButtonChoke" },
                new Knob { At = new Vector3(0.005f, 0.651f, 0.429f), Normal = ImpFace, Col = new Color(0.92f, 0.92f, 0.90f) },
                new Knob { At = new Vector3(0.040f, 0.651f, 0.429f), Normal = ImpFace, Col = new Color(0.95f, 0.45f, 0.05f) } },
            Off = new[] { "Functions/Dashboard/Buttons/ButtonWipers", "Functions/Dashboard/Buttons/ButtonWindowHeater", "Functions/Dashboard/Buttons/ButtonHeaterTemp",
                          "Functions/Dashboard/Buttons/ButtonHeaterDirection", "Functions/Dashboard/Buttons/ButtonHeaterBlower", "Functions/Sunvisors",
                          "Functions/VINP_RadioSorbet", "Functions/VINP_FurDiceSorbet" },
            Dome = new Vector3(0f, 1.20f, -0.30f), HasDome = true, ControlSize = 0.024f,
            Sp = new Spec { Name = "Impreza 96 Gr. A", Power = 300f, PowerRpm = 5500f, Torque = 470f, TorqueRpm = 4000f, MaxRpm = 7500f,
                            Mass = 1130f, Com = 0.60f, Steer = 35f, Spring = 41600f, Damp = 2700f, Gears = new[] { -3.3f, 0f, 2.95f, 2.0f, 1.52f, 1.2f, 0.95f },
                            Handbrake = 6000f, RearGrip = 1.15f, HandbrakeSlide = 0.45f, AntiRollFront = 30000f, AntiRollRear = 15000f, YawDamp = 0.6f, ComDrop = 0.04f } };   // (rallye moderne, comme l'Ebo 2 -- JD 23/10)
        // Audi Sport Quattro 1984 Gr. B (JD 21/10, rally cars GrB/Squattro, Blomqvist) : moteur avant, integrale ; habitacle de
        // la vue interieure du modele (redescendu de 10 cm sur la caisse) ; yeux au point de vue du modele rFactor (Eyepoint du
        // cockpitinfo, redescendu comme l'habitacle : -0,36 ; 0,90 ; -0,10), tete 27 cm derriere ; frein a main : pas de levier modelise, la tige carbone de l'Evo III posee sur le tunnel
        // (outils/rf1/t16e2_frein.py, decalee de (-0,05 ; -0,025 ; -0,333)) ; boutons en ligne sur le bloc d'interrupteurs du
        // centre de la planche (y 0,435, z 0,335, face au pilote, a droite de la jante vue du siege), contact, starter, phares, warning. Tenue de route : 450 ch a
        // 7 500 tr/min, 490 Nm a 5 500, rupteur 8 000, 1 100 kg, 58 % sur l'avant (cinq cylindres en porte-a-faux), braquage 38 ;
        // suspension durcie a l'essai (JD 21/10 : trop molle, tonneaux trop faciles) : ressorts 52 000, amortisseurs 3 400,
        // barres antiroulis 22 000, centre de gravite 5 cm plus bas (le reglage plus dur de la 1.8.4 : « bizarre », retire) ;
        // frein a main 6 000 N.m sur les roues arriere (« plus fort, pour partir en travers ») -- a etendre aux autres addons
        public static readonly Model Sq84 = new Model { Id = ModelSq, Folder = "sq84", Name = "Sport Quatro",
            Head = new Vector3(-0.36f, 0.93f, -0.37f), SeatEyes = true, SteerDeg = 450f, Engine = new Vector3(0f, 0.55f, 1.45f),
            BrakePivot = new Vector3(-0.10f, 0.25f, -0.02f), BrakeGrip = new Vector3(-0.125f, 0.575f, 0.022f), BrakeAngle = 1.4f,
            Controls = new[] {
                new KeyValuePair<string, Vector3>("Functions/SteeringColumnPivot/SteeringColumn/IGNITIONxSorbet", new Vector3(-0.020f, 0.435f, 0.342f)),
                new KeyValuePair<string, Vector3>("Functions/Dashboard/Choke/ButtonChoke", new Vector3(0.012f, 0.435f, 0.342f)),
                new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonLightModes", new Vector3(0.044f, 0.435f, 0.342f)),
                new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonHazard", new Vector3(0.076f, 0.435f, 0.342f)) },
            Knobs = new[] {
                new Knob { At = new Vector3(-0.020f, 0.435f, 0.331f), Normal = Vector3.back, Col = new Color(0.80f, 0.06f, 0.05f) },
                new Knob { At = new Vector3(0.012f, 0.435f, 0.331f), Normal = Vector3.back, Col = new Color(0.10f, 0.25f, 0.85f), Choke = true, Path = "Functions/Dashboard/Choke/ButtonChoke" },
                new Knob { At = new Vector3(0.044f, 0.435f, 0.331f), Normal = Vector3.back, Col = new Color(0.92f, 0.92f, 0.90f) },
                new Knob { At = new Vector3(0.076f, 0.435f, 0.331f), Normal = Vector3.back, Col = new Color(0.95f, 0.45f, 0.05f) } },
            Off = new[] { "Functions/Dashboard/Buttons/ButtonWipers", "Functions/Dashboard/Buttons/ButtonWindowHeater", "Functions/Dashboard/Buttons/ButtonHeaterTemp",
                          "Functions/Dashboard/Buttons/ButtonHeaterDirection", "Functions/Dashboard/Buttons/ButtonHeaterBlower", "Functions/Sunvisors",
                          "Functions/VINP_RadioSorbet", "Functions/VINP_FurDiceSorbet" },
            Dome = new Vector3(0f, 1.06f, -0.20f), HasDome = true, ControlSize = 0.024f,
            Sp = new Spec { Name = "Sport Quattro Gr. B", Power = 450f, PowerRpm = 7500f, Torque = 490f, TorqueRpm = 5500f, MaxRpm = 8000f,
                            Mass = 1100f, Com = 0.58f, Steer = 38f, Spring = 52000f, Damp = 3400f, AntiRoll = 22000f, ComDrop = 0.05f, Handbrake = 6000f, Gears = new[] { -3.3f, 0f, 2.95f, 2.0f, 1.52f, 1.2f, 0.95f },
                            RearGrip = 1.15f, HandbrakeSlide = 0.45f, AntiRollFront = 29000f, AntiRollRear = 14500f, YawDamp = 0.6f } };   // (rallye moderne, comme l'Ebo 2 -- JD 23/10)
        // 205 T16 Evo 2 1986 Gr. B (JD 23/10, rally cars GrB/205T16, Kankkunen : « Peujot 205 T16 Ebo 2 ») : moteur central
        // arriere, integrale ; habitacle de la vue interieure du modele (remonte de 3 cm sur la caisse) ; yeux au point de vue du
        // modele rFactor) : assise de l'Ebo 1 (la meme caisse ; JD 23/10 : avec la tete descendue, on ne pouvait plus s'asseoir
        // qu'en se collant au volant), volant a 450 degres de chaque cote (SteerDeg : un tour et quart), yeux seuls 4 cm plus bas (EyeDrop) et vue recalee a l'entree sur le point de vue du
        // modele rFactor (SeatEyes : la vue etait a la hauteur ou l'on se tenait -- « dans le toit ») ; frein
        // a main : son propre levier (sur le tunnel, pivot a l'arriere, poignee vers l'avant : se leve quand il est serre, comme
        // l'Impreza) ; boutons comme l'Ebo 1 : contact (rouge), starter (bleu, tire / pousse), phares (blanc), warning (orange) en
        // colonne sur la face de la planche (z 0,518, tournee vers le pilote) juste a droite du volant. Tenue de route : celle de
        // l'Ebo 1 validee par JD, moteur et masse de l'Evo 2 : 450 ch a 7 500 tr/min, 490 Nm a 5 500, rupteur 8 000, 910 kg (Mass
        // du .hdv rFactor) ; frein a main de la Sport Quatro (6 000 N.m).
        public static readonly Model T16b = new Model { Id = ModelT16b, Folder = "t16e2", Name = "205 Ebo 2",
            Head = new Vector3(-0.32f, 0.969f, -0.291f), EyeDrop = 0.04f, SeatEyes = true, SteerDeg = 450f, Engine = new Vector3(0f, 0.55f, -0.85f),
            BrakePivot = new Vector3(-0.012f, 0.297f, -0.125f), BrakeGrip = new Vector3(-0.012f, 0.349f, 0.095f),
            Controls = new[] {
                new KeyValuePair<string, Vector3>("Functions/SteeringColumnPivot/SteeringColumn/IGNITIONxSorbet", new Vector3(-0.05f, 0.640f, 0.511f)),
                new KeyValuePair<string, Vector3>("Functions/Dashboard/Choke/ButtonChoke", new Vector3(-0.05f, 0.605f, 0.511f)),
                new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonLightModes", new Vector3(-0.05f, 0.570f, 0.511f)),
                new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonHazard", new Vector3(-0.05f, 0.535f, 0.511f)) },
            Knobs = new[] {
                new Knob { At = new Vector3(-0.05f, 0.640f, 0.518f), Normal = Vector3.back, Col = new Color(0.80f, 0.06f, 0.05f) },
                new Knob { At = new Vector3(-0.05f, 0.605f, 0.518f), Normal = Vector3.back, Col = new Color(0.10f, 0.25f, 0.85f), Choke = true, Path = "Functions/Dashboard/Choke/ButtonChoke" },
                new Knob { At = new Vector3(-0.05f, 0.570f, 0.518f), Normal = Vector3.back, Col = new Color(0.92f, 0.92f, 0.90f) },
                new Knob { At = new Vector3(-0.05f, 0.535f, 0.518f), Normal = Vector3.back, Col = new Color(0.95f, 0.45f, 0.05f) } },
            Off = new[] { "Functions/Dashboard/Buttons/ButtonWipers", "Functions/Dashboard/Buttons/ButtonWindowHeater", "Functions/Dashboard/Buttons/ButtonHeaterTemp",
                          "Functions/Dashboard/Buttons/ButtonHeaterDirection", "Functions/Dashboard/Buttons/ButtonHeaterBlower", "Functions/Sunvisors",
                          "Functions/VINP_RadioSorbet", "Functions/VINP_FurDiceSorbet" },
            Dome = new Vector3(0f, 1.10f, -0.20f), HasDome = true, ControlSize = 0.024f,
            Sp = new Spec { Name = "205 T16 Evo 2 Gr. B", Power = 450f, PowerRpm = 7500f, Torque = 490f, TorqueRpm = 5500f, MaxRpm = 8000f,
                            Mass = 910f, Com = 0.46f, Steer = 40f, Spring = 34000f, Damp = 2200f, Handbrake = 6000f, Gears = new[] { -3.3f, 0f, 2.95f, 2.0f, 1.52f, 1.2f, 0.95f },
                            // (rallye moderne, JD 23/10 -- a valider sur l'Ebo 2 avant les autres) : arriere +15 % d'adherence laterale,
                            // 45 % au frein a main ; barre avant plus raide que l'arriere ; lacet amorti ; centre de gravite 4 cm plus bas
                            RearGrip = 1.15f, HandbrakeSlide = 0.45f, AntiRollFront = 24000f, AntiRollRear = 12000f, YawDamp = 0.6f, ComDrop = 0.04f } };
        // Renault 5 Maxi Turbo 1985 Gr. B (JD 28/10, rally cars GrB/R5MAXI, Philips : « Rihno 5 Maxi ») : moteur central arriere,
        // PROPULSION (WheelDrive=REAR du .hdv) ; habitacle de la vue interieure du modele (redescendu de 10 cm sur la caisse, comme
        // la Sport Quatro du meme pack) ; tete au point de vue du pilote, 60 cm derriere le volant ; frein a main : la tige carbone de
        // l'Evo III sur le tunnel, a droite du siege (outils/rf1/t16e2_frein.py, decalee de (-0,02 ; -0,03 ; -0,34)) ; boutons en
        // colonne sur la face de la planche (z 0,46, tournee vers le pilote) juste a droite du volant, de haut en bas : contact
        // (rouge), starter (bleu, tire / pousse), phares (blanc), warning (orange) ; le reste du tableau de bord coupe. Tenue de
        // route : moteur et masse du mod rFactor (maxi turbo engine.ini : 355 ch a 6 750 tr/min, 398 Nm a 5 500, rupteur 7 000 ;
        // 905 kg, 49 % sur l'avant, braquage 36), boite et reglages de rallye des autres addons (Ebo 2).
        public static readonly Model R5 = new Model { Id = ModelR5, Folder = "r5maxi", Name = "Rihno 5 Maxi",
            Head = new Vector3(-0.32f, 0.95f, -0.32f), SeatEyes = true, SteerDeg = 450f, Engine = new Vector3(0f, 0.55f, -0.90f),
            BrakePivot = new Vector3(-0.07f, 0.245f, -0.027f), BrakeGrip = new Vector3(-0.095f, 0.57f, 0.015f), BrakeAngle = 1.4f,
            Controls = new[] {
                new KeyValuePair<string, Vector3>("Functions/SteeringColumnPivot/SteeringColumn/IGNITIONxSorbet", new Vector3(-0.065f, 0.735f, 0.448f)),
                new KeyValuePair<string, Vector3>("Functions/Dashboard/Choke/ButtonChoke", new Vector3(-0.065f, 0.700f, 0.448f)),
                new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonLightModes", new Vector3(-0.065f, 0.665f, 0.448f)),
                new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonHazard", new Vector3(-0.065f, 0.630f, 0.448f)) },
            Knobs = new[] {
                new Knob { At = new Vector3(-0.065f, 0.735f, 0.456f), Normal = Vector3.back, Col = new Color(0.80f, 0.06f, 0.05f) },
                new Knob { At = new Vector3(-0.065f, 0.700f, 0.456f), Normal = Vector3.back, Col = new Color(0.10f, 0.25f, 0.85f), Choke = true, Path = "Functions/Dashboard/Choke/ButtonChoke" },
                new Knob { At = new Vector3(-0.065f, 0.665f, 0.456f), Normal = Vector3.back, Col = new Color(0.92f, 0.92f, 0.90f) },
                new Knob { At = new Vector3(-0.065f, 0.630f, 0.456f), Normal = Vector3.back, Col = new Color(0.95f, 0.45f, 0.05f) } },
            Off = new[] { "Functions/Dashboard/Buttons/ButtonWipers", "Functions/Dashboard/Buttons/ButtonWindowHeater", "Functions/Dashboard/Buttons/ButtonHeaterTemp",
                          "Functions/Dashboard/Buttons/ButtonHeaterDirection", "Functions/Dashboard/Buttons/ButtonHeaterBlower", "Functions/Sunvisors",
                          "Functions/VINP_RadioSorbet", "Functions/VINP_FurDiceSorbet" },
            Dome = new Vector3(0f, 1.12f, -0.25f), HasDome = true, ControlSize = 0.024f,
            Sp = new Spec { Name = "R5 Maxi Turbo Gr. B", Power = 355f, PowerRpm = 6750f, Torque = 398f, TorqueRpm = 5500f, MaxRpm = 7000f,
                            Mass = 905f, Com = 0.49f, Steer = 36f, Spring = 33000f, Damp = 2150f, Handbrake = 6000f, Gears = new[] { -3.3f, 0f, 2.95f, 2.0f, 1.52f, 1.2f, 0.95f },
                            Rwd = true, RearGrip = 1.15f, HandbrakeSlide = 0.45f, AntiRollFront = 24000f, AntiRollRear = 12000f, YawDamp = 0.6f, ComDrop = 0.04f } };
        // R5 standalone (pilote, spec du 28/10, lot 4) : numero de modele a elle dans la table des voitures du F10 ; construite par
        // StandaloneCar la ou [Test] AddonStandalone=1, ailleurs la R5 copie (n 6) de la meme couleur et du meme numero (Vaz.BuildModel).
        public static readonly Model R5S = R5.CloneAs(ModelR5S, null);
        // Golf GTI 1983 (modele de SamGrey achete par JD sur RenderHub le 31/10, MWCoop-voiture/golf83) : standalone sans copie, pas
        // encore proposee aux joueurs (JD : « j'ai un autre projet pour elle ») -- au F10 seulement la ou son dossier addons\golf83 est
        // pose a la main. Traction (1,8 l de 112 ch, 840 kg, 61 % sur l'avant). Poste mesure sur le modele (rayons dans la caisse) :
        // volant (-0,31 ; 0,83 ; 0,34), assise vers y 0,57, appui-tete z -0,30, face de la planche a droite du volant z 0,53 ; boutons
        // contact / starter / phares / warning en colonne sur cette face, comme la R5.
        public static readonly Model Golf83 = new Model { Id = ModelGolf, Folder = "golf83", Name = "Golf GTI",
            Head = new Vector3(-0.31f, 1.03f, -0.35f), SeatEyes = true, SteerDeg = 540f, Engine = new Vector3(0f, 0.60f, 1.45f),
            // (levier du frein a main du modele, frein.p205 : sur le tunnel, poignee vers l'arriere, pivot a l'avant -- angle negatif : la
            // poignee monte)
            BrakePivot = new Vector3(0f, 0.39f, 0.23f), BrakeGrip = new Vector3(0f, 0.40f, 0.01f), BrakeAngle = -1.3f,
            Controls = new[] {
                new KeyValuePair<string, Vector3>("Functions/SteeringColumnPivot/SteeringColumn/IGNITIONxSorbet", new Vector3(-0.07f, 0.865f, 0.520f)),
                new KeyValuePair<string, Vector3>("Functions/Dashboard/Choke/ButtonChoke", new Vector3(-0.07f, 0.835f, 0.520f)),
                new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonLightModes", new Vector3(-0.07f, 0.805f, 0.520f)),
                new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonHazard", new Vector3(-0.07f, 0.775f, 0.520f)) },
            Knobs = new[] {
                new Knob { At = new Vector3(-0.07f, 0.865f, 0.528f), Normal = Vector3.back, Col = new Color(0.80f, 0.06f, 0.05f) },
                new Knob { At = new Vector3(-0.07f, 0.835f, 0.528f), Normal = Vector3.back, Col = new Color(0.10f, 0.25f, 0.85f), Choke = true, Path = "Functions/Dashboard/Choke/ButtonChoke" },
                new Knob { At = new Vector3(-0.07f, 0.805f, 0.528f), Normal = Vector3.back, Col = new Color(0.92f, 0.92f, 0.90f) },
                new Knob { At = new Vector3(-0.07f, 0.775f, 0.528f), Normal = Vector3.back, Col = new Color(0.95f, 0.45f, 0.05f) } },
            Off = new[] { "Functions/Dashboard/Buttons/ButtonWipers", "Functions/Dashboard/Buttons/ButtonWindowHeater", "Functions/Dashboard/Buttons/ButtonHeaterTemp",
                          "Functions/Dashboard/Buttons/ButtonHeaterDirection", "Functions/Dashboard/Buttons/ButtonHeaterBlower", "Functions/Sunvisors",
                          "Functions/VINP_RadioSorbet", "Functions/VINP_FurDiceSorbet" },
            Dome = new Vector3(0f, 1.27f, -0.20f), HasDome = true, ControlSize = 0.024f,
            Sp = new Spec { Name = "Golf GTI 1.8 (1983)", Power = 112f, PowerRpm = 5800f, Torque = 153f, TorqueRpm = 3500f, MaxRpm = 6600f, Inertia = 0.12f,
                            Final = 3.89f, Mass = 840f, Com = 0.61f, Steer = 33f, Gears = new[] { -3.17f, 0f, 3.45f, 2.12f, 1.44f, 1.13f, 0.91f }, Fwd = true } };
        public static readonly Model[] Models = { null, Evo3, T16, Imp96, Sq84, T16b, R5, R5S, Evo3.CloneAs(ModelEvoS, null), T16.CloneAs(ModelT16S, null),
                                                   Imp96.CloneAs(ModelImpS, null), Sq84.CloneAs(ModelSqS, null), T16b.CloneAs(ModelT16bS, null), Golf83 };   // (rempli : Vaz2108 au constructeur statique)
        public static Model ModelOf(int id) { return id > 0 && id < Models.Length ? Models[id] : Vaz2108; }
        // addon installe ici (la VAZ l'est toujours)
        // (My Summer Car : la R5 copie retiree, la standalone a sa place ; My Winter Car : la copie, pas encore de standalone)
        public static bool Has(int id) { return id == ModelVaz || (id > 0 && id < Models.Length && !Models[id].Retired && Models[id].HasFiles && (!Models[id].Standalone || (CopyOf(id) == id ? StandaloneCar.On : StandaloneCar.Ready(CopyOf(id)))) && !StandaloneCar.Ready(id)); }
        // modele demande pour cette partie : sur My Summer Car, la R5 copie (n 6, anciens clients, anciens choix) devient la standalone
        public static int Current(int id) { return StandaloneCar.Ready(id) ? StandaloneOf(id) : id; }
        static Model cur = Vaz2108;   // (modele en cours de construction : fichiers, matieres, textures)

        public static string Dir { get { return Vaz2108.Dir; } }
        public static bool HasFiles { get { return Vaz2108.HasFiles; } }

        // Yeux du pilote, repere du modele : siege conducteur a x -0,35, toit interieur vers 1,33, volant en (-0,35 ; 0,86 ; 0,64).
        // DriverHeadPivot est pose 27 cm DERRIERE et 3 cm au-dessus (les yeux sont la, Seats.EyeFromPivot, mesure sur la
        // SORBET) -- retour de JD, 18/10 : pivot pose sur les yeux, conducteur colle au volant, zone d'entree et place du
        // passager trop en avant.
        static readonly Vector3 EyesModel = new Vector3(-0.35f, 1.10f, 0.02f);
        static readonly Vector3 HeadModel = EyesModel - new Vector3(0f, -0.03f, 0.27f);
        static VazCar() { Models[0] = Vaz2108; Vaz2108.SteerDeg = 450f; Vaz2108.Head = HeadModel; Vaz2108.Controls = Controls; Vaz2108.BrakePivot = BrakePivotModel; Vaz2108.BrakeGrip = BrakeGripModel; Vaz2108.Engine = EngineModel; }

        // Commandes de la SORBET posees sur les boutons du tableau de bord de la VAZ (centres des pieces du modele, repere
        // du modele ; photo annotee de JD, 18/10) : contact, essuie-glaces et feux sur les trois interrupteurs a gauche des
        // compteurs ; warning et starter dans les rangees de boutons carres de la console ; soufflerie, degivrage sur les
        // interrupteurs du bas ; temperature et direction du chauffage sur les molettes rondes.
        static readonly KeyValuePair<string, Vector3>[] Controls = {
            new KeyValuePair<string, Vector3>("Functions/SteeringColumnPivot/SteeringColumn/IGNITIONxSorbet", new Vector3(-0.206f, 0.830f, 0.790f)),
            new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonWipers", new Vector3(-0.169f, 0.830f, 0.790f)),
            new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonLightModes", new Vector3(-0.132f, 0.830f, 0.790f)),
            new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonHazard", new Vector3(0.026f, 0.854f, 0.787f)),
            new KeyValuePair<string, Vector3>("Functions/Dashboard/Choke/ButtonChoke", new Vector3(0.003f, 0.818f, 0.777f)),
            new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonHeaterBlower", new Vector3(-0.018f, 0.717f, 0.736f)),
            new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonWindowHeater", new Vector3(-0.067f, 0.717f, 0.736f)),
            new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonHeaterTemp", new Vector3(0.002f, 0.751f, 0.761f)),
            new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonHeaterDirection", new Vector3(-0.068f, 0.751f, 0.761f)),
        };
        // Frein a main : levier du modele (frein.p205) pivotant a sa base (sur la console), poignee vers l'avant ; le levier
        // cliquable du jeu (Functions/HandBrake/LeverPivot) pose sur la poignee.
        static readonly Vector3 BrakePivotModel = new Vector3(0.001f, 0.509f, 0.05f), BrakeGripModel = new Vector3(-0.069f, 0.533f, 0.24f);
        // Son du moteur (facultatif, VazSound) : moteur transversal avant.
        static readonly Vector3 EngineModel = new Vector3(0f, 0.60f, 1.50f);

        public class Built
        {
            public GameObject Car;
            public Transform Body, SteerPivot, SorbetSteer, BrakePivot, SorbetBrake;
            public Quaternion SorbetSteerRest, SorbetBrakeRest;
            public float SeatAt = -1f; public bool SeatSet;   // (SeatView : entree vue a, vue recalee)
            public float SteerTurn, SteerRatio;   // (volant : angle pose, degres par unite de direction)
            public VazSound Sound;
            public float IdleTrim;   // (My Summer Car : gaz de ralenti appris par le regulateur, VazHandling.Idle)
            public VazFlaps Flaps;
            public VazAntennas Antennas;   // (antennes souples, sans collision : antenne_1 / antenne_2.p205)
            public FsmFloat BrakeKnob;
            public PlayMakerFSM DriveFsm;     // (My Summer Car : PlayerTrigger de la zone conducteur, MscDriven)
            public int Number, Shown;   // (Shown : numero sur les portieres -- celui de la course s'il y en a un, SetNumber)
            public Transform Roof; public BoxCollider RoofZone; public bool RoofOpen, RoofHover; public float RoofAngle;
            public Vector3 SteerAxis = Vector3.forward;
            public Component Axis;
            public Drivetrain Dt;
            public Rigidbody Rb;
            public Wheel[] FrontWheels;   // (My Summer Car : braquage selon la vitesse, VazHandling.SteerBySpeed)
            public object FrontAxle;
            public readonly List<Renderer> Hidden = new List<Renderer>();
            public readonly List<KeyValuePair<MeshRenderer, string>> Lamps = new List<KeyValuePair<MeshRenderer, string>>();
            public readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>(), Lit = new Dictionary<string, Material>(), Dim = new Dictionary<string, Material>();
            public readonly List<Light> Front = new List<Light>(), Rear = new List<Light>(), Brake = new List<Light>(), Reverse = new List<Light>();
            public bool LampsInit, FrontOn, RearOn, BrakeOn, ReverseOn;
            public float NextLamp, NextHide, NextSpec, NextMask, NextGrip, NextStart, NextStartLog;
            public PlayMakerFSM Priming, Power, Tank;   // (VazHandling.ReadyToStart)
            public readonly float[] LastGrip = { -1f, -1f };
            public bool Handbraking;          // (frein a main tire : adherence arriere reduite, Spec.HandbrakeSlide)
            public int MaskLogs;
            public Model M = Vaz2108;
            public readonly List<Collider> OffColl = new List<Collider>();
            public Transform ChokeKnob; public Vector3 ChokeRest, ChokeDir; public FsmFloat ChokeVal; public Transform ChokeButton;
            public Color Paint = Color.white;
            public string LiveryHash;   // livree posee (empreinte), null : couleur du joueur
        }

        // ---------------------------------------------------------------- fichiers
        class MatDef { public string Key, Kind, Tex; public Color Col; }
        class Part { public string Mat; public Mesh Mesh; }
        static readonly Dictionary<string, List<Part>> meshCache = new Dictionary<string, List<Part>>();
        static readonly Dictionary<string, MatDef> matDefs = new Dictionary<string, MatDef>();
        static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();

        // Maillages lus une fois (partages par toutes les VAZ : seules les matieres de peinture et de numero sont a chacune).
        static List<Part> Load(string file)
        {
            List<Part> list;
            string path = Path.Combine(cur.Dir, file);
            if (meshCache.TryGetValue(path, out list)) return list;
            list = new List<Part>();
            if (File.Exists(path))
                using (var r = new BinaryReader(File.OpenRead(path), System.Text.Encoding.UTF8))
                {
                    if (new string(r.ReadChars(4)) != "P205") throw new System.Exception("vaz : " + file + " : format inconnu");
                    r.ReadInt32();
                    int nm = r.ReadInt32();
                    for (int i = 0; i < nm; i++)
                    {
                        var m = new MatDef { Key = r.ReadString(), Kind = r.ReadString(), Tex = r.ReadString() };
                        r.ReadString();
                        m.Col = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        matDefs[cur.Folder + "|" + m.Key] = m;
                    }
                    int np = r.ReadInt32();
                    for (int i = 0; i < np; i++)
                    {
                        string key = r.ReadString();
                        int nv = r.ReadInt32();
                        var p = new Vector3[nv]; var nn = new Vector3[nv]; var uv = new Vector2[nv];
                        for (int v = 0; v < nv; v++) p[v] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        for (int v = 0; v < nv; v++) nn[v] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        for (int v = 0; v < nv; v++) uv[v] = new Vector2(r.ReadSingle(), r.ReadSingle());
                        int ni = r.ReadInt32();
                        var idx = new int[ni];
                        for (int k = 0; k < ni; k++) idx[k] = r.ReadInt32();
                        var mesh = new Mesh { name = "vaz_" + key };
                        mesh.vertices = p; mesh.normals = nn; mesh.uv = uv; mesh.triangles = idx;
                        mesh.RecalculateBounds();
                        list.Add(new Part { Mat = key, Mesh = mesh });
                    }
                }
            meshCache[path] = list;
            return list;
        }

        static Dictionary<string, Vector3> Info()
        {
            if (cur.Info != null) return cur.Info;
            var info = cur.Info = new Dictionary<string, Vector3>();
            string f = Path.Combine(cur.Dir, "infos.txt");
            if (!File.Exists(f)) return info;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (string l in File.ReadAllLines(f))
            {
                string[] p = l.Split(' ');
                float x, y, z;
                if (p.Length >= 4 && float.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out x) && float.TryParse(p[2], System.Globalization.NumberStyles.Float, ci, out y)
                    && float.TryParse(p[3], System.Globalization.NumberStyles.Float, ci, out z)) info[p[0]] = new Vector3(x, y, z);
            }
            return info;
        }

        // Numero affiche sur les portieres (JD 21/10 : « le numero differe » -- inscrit a la course sur glace a la place de la
        // voiture IA n 10, sa 205 portait toujours son 5) : n de la place, ou celui de la course (Vaz.Update) ; plaques
        // numero-01..99 de MWCoop/vaz (run/vaz-numeros.py).
        public static void SetNumber(Built b, int n)
        {
            if (b == null || n < 1 || n > 99 || b.Shown == n) return;
            b.Shown = n;
            Material m;
            if (!b.Mats.TryGetValue("numero", out m) || m == null) return;
            Model was = cur; cur = b.M ?? Vaz2108;
            Texture2D t = Tex("numero-" + n.ToString("00"), true);
            cur = was;
            if (t != null) { m.mainTexture = t; Log.Info("vaz : " + (b.Car != null ? b.Car.name : "?") + " porte le numero " + n); }
        }

        static Texture2D Tex(string name, bool clamp = false)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Texture2D t;
            string ck = cur.Folder + "|" + name;
            if (texCache.TryGetValue(ck, out t)) return t;
            string f = Path.Combine(cur.Dir, name + ".png");
            if (!File.Exists(f) && name.StartsWith("numero-")) f = Path.Combine(Vaz2108.Dir, name + ".png");   // (plaques communes)
            t = null;
            if (File.Exists(f))
            {
                t = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "vaz_" + name, hideFlags = HideFlags.HideAndDontSave };
                t.LoadImage(File.ReadAllBytes(f));
                t.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat; t.anisoLevel = 4;
            }
            else Log.Warn("vaz : texture " + name + ".png absente");
            texCache[ck] = t;
            return t;
        }

        // ---------------------------------------------------------------- matieres
        static Shader transp, cutout, unlitTex, unlitTransp;
        static Shader FindShader(params string[] names) { foreach (string n in names) { Shader s = Shader.Find(n); if (s != null) return s; } return null; }

        static Material MaterialFor(Built b, string key, Material basePaint, Color paint, int number)
        {
            Material m;
            if (b.Mats.TryGetValue(key, out m)) return m;
            if (transp == null)
            {
                transp = FindShader("Legacy Shaders/Transparent/Diffuse", "Transparent/Diffuse", "Particles/Alpha Blended", "Unlit/Transparent");
                cutout = FindShader("Legacy Shaders/Transparent/Cutout/Diffuse", "Transparent/Cutout/Diffuse", "Unlit/Transparent Cutout");
                unlitTex = FindShader("Unlit/Texture", "Legacy Shaders/Self-Illumin/Diffuse", "Self-Illumin/Diffuse");
                unlitTransp = FindShader("Unlit/Transparent", "Particles/Additive", "Legacy Shaders/Particles/Additive");
            }
            MatDef d; matDefs.TryGetValue(cur.Folder + "|" + key, out d);
            string kind = d != null ? d.Kind : "opaque";
            Texture2D t = key == "numero" ? Tex("numero-" + number.ToString("00"), true) : d != null ? Tex(d.Tex) : null;
            if ((kind == "glass" || kind == "fade" || kind == "lampfade") && transp != null)
            {
                m = new Material(transp) { mainTexture = kind == "glass" ? null : t };
                m.color = kind == "glass" ? d.Col : Color.white;
            }
            else if ((kind == "cutout" || kind == "lampcut") && cutout != null)
            {
                m = new Material(cutout) { mainTexture = t, color = Color.white };
                if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.4f);
            }
            else
            {
                m = new Material(basePaint);
                // (la peinture de la SORBET porte des cartes de salete et de detail : retirees)
                foreach (string tp in new[] { "_DetailAlbedoMap", "_DetailNormalMap", "_DetailMask", "_OcclusionMap", "_BumpMap", "_SpecGlossMap", "_ParallaxMap", "_EmissionMap" })
                    if (m.HasProperty(tp)) m.SetTexture(tp, null);
                bool shiny = kind == "paint";
                if (m.HasProperty("_SpecColor")) m.SetColor("_SpecColor", shiny ? new Color(0.24f, 0.24f, 0.24f) : new Color(0.08f, 0.08f, 0.08f));
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", shiny ? 0.78f : 0.35f);
                m.mainTexture = t;
                m.color = kind == "paint" ? paint : d != null ? new Color(d.Col.r, d.Col.g, d.Col.b, 1f) : Color.white;
            }
            m.name = "vaz_" + key;
            b.Mats[key] = m;
            // feux : variante allumee (sans eclairage de la scene), et pour l'arriere une variante faible (veilleuses)
            if (kind.StartsWith("lamp") && unlitTex != null)
            {
                Shader ls = kind == "lamp" ? unlitTex : unlitTransp ?? unlitTex;
                b.Lit[key] = new Material(ls) { mainTexture = t, color = Color.white, name = "vaz_" + key + "_allume" };
                if (key.EndsWith("_ar") && t != null)
                {
                    var dimTex = new Texture2D(t.width, t.height, TextureFormat.RGBA32, true) { wrapMode = t.wrapMode, name = t.name + "_faible", hideFlags = HideFlags.HideAndDontSave };
                    Color32[] px = t.GetPixels32();
                    for (int i = 0; i < px.Length; i++) { px[i].r = (byte)(px[i].r * 0.45f); px[i].g = (byte)(px[i].g * 0.45f); px[i].b = (byte)(px[i].b * 0.45f); }
                    dimTex.SetPixels32(px); dimTex.Apply();
                    b.Dim[key] = new Material(ls) { mainTexture = dimTex, color = Color.white, name = "vaz_" + key + "_faible" };
                }
            }
            return m;
        }

        static void AddParts(Built b, Transform parent, string file, Material basePaint, Color paint, int number)
        {
            foreach (Part p in Load(file))
            {
                var go = new GameObject("vaz_" + p.Mat);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = p.Mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = MaterialFor(b, p.Mat, basePaint, paint, number);
                MatDef d;
                if (matDefs.TryGetValue(cur.Folder + "|" + p.Mat, out d) && d.Kind.StartsWith("lamp")) b.Lamps.Add(new KeyValuePair<MeshRenderer, string>(mr, p.Mat));
            }
        }

        // Pilote « addon standalone » (spec du 28/10, StandaloneCar) : la voiture construite par notre code reprend le modele, ses
        // matieres et la peinture du joueur d'ici (Ruscko/SORBET de la scene : matiere de base de la peinture, valeurs UnityCar).
        public static GameObject Template() { return FindSorbet(); }
        public static void AddModel(Built b, Transform parent, string file)
        {
            Model was = cur; cur = b.M ?? Vaz2108;
            try
            {
                GameObject s = FindSorbet();
                Material bp = s != null ? (FindMat(s, "paint") ?? FindMat(s, "")) : null;
                AddParts(b, parent, file, bp, b.Paint, b.Number);
            }
            finally { cur = was; }
        }
        public static bool ModelBounds(Model m, string file, out Bounds bounds)
        {
            Model was = cur; cur = m; bounds = new Bounds();
            try
            {
                List<Part> ps = Load(file);
                if (ps.Count == 0) return false;
                bounds = ps[0].Mesh.bounds;
                foreach (Part q in ps) bounds.Encapsulate(q.Mesh.bounds);
                return true;
            }
            finally { cur = was; }
        }
        public static Dictionary<string, Vector3> ModelInfo(Model m) { Model was = cur; cur = m; try { return Info(); } finally { cur = was; } }

        static Material FindMat(GameObject root, string part)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    if (m != null && m.name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0) return m;
            return null;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { Transform r = FindDeep(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }

        static GameObject FindSorbet()
        {
            if (Msc)
            {
                GameObject r = Game.FindAny("RCO_RUSCKO12(270)");
                if (r != null && r.GetComponent("CarDynamics") != null) return r;
                foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>())
                    if (rb.transform.parent == null && rb.name.StartsWith("RCO_RUSCKO") && rb.GetComponent("CarDynamics") != null) return rb.gameObject;
                return null;
            }
            GameObject g = Game.FindAny("SORBET(190-200psi)");
            if (g != null && g.GetComponent("CarDynamics") != null) return g;
            foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>())
                if (rb.transform.parent == null && rb.name.StartsWith("SORBET") && rb.GetComponent("CarDynamics") != null) return rb.gameObject;
            return null;
        }

        // ---------------------------------------------------------------- voiture de base : SORBET (MWC) ou Ruscko (MSC)
        // My Summer Car (JD 24/10 : « les addons, mais pour MSC ») : copie de la RUSCKO (RCO_RUSCKO12 : boite manuelle, memes
        // composants UnityCar que la SORBET -- comme le pack rallye MSC du 19/10, MWCoop-voiture/mod/Peugeot205.cs). Ses objets
        // n'ont ni les memes noms ni la meme place (vidage ref/msc-v.250908-04, RCO_RUSCKO12) : chemins ci-dessous ; commandes
        // de la SORBET traduites par Ctl (null : rien de tel sur la Ruscko -- ni starter, ni warning, ni chauffage, ni
        // plafonnier ; le bouton orange des addons y est le klaxon).
        static bool Msc { get { return Game.Msc; } }
        public static string PDriveTrigger { get { return Msc ? "LOD/PlayerTrigger/DriveTrigger" : "Functions/PlayerTrigger/DriveTriggerX"; } }
        public static string PHeadPivot { get { return Msc ? "LOD/DriverHeadPivot" : "Functions/PlayerTrigger/DriverHeadPivot"; } }
        static string PCamPivot { get { return Msc ? "LOD/DriverHeadPivot/CameraPivot/Pivot" : "Functions/PlayerTrigger/DriverHeadPivot/CameraPivotPLR/Pivot"; } }
        public static string PHatch { get { return Msc ? "RearDoor/doorear" : "Hatch/Hatch"; } }
        public static string PDoorL { get { return Msc ? "DriverDoors/doorl" : "Doors/DoorFront(leftx)"; } }
        public static string PDoorR { get { return Msc ? "DriverDoors/doorr" : "Doors/DoorFront(right)"; } }
        public static string PTank { get { return Msc ? "FuelTank" : "Simulation/FuelTankSorbett"; } }
        public static string PLever { get { return Msc ? "LOD/Dashboard/ParkingBrake" : "Functions/HandBrake/LeverPivot"; } }
        static string NSteer { get { return Msc ? "RusckoSteeringPivot" : "SteeringPivotSorbett"; } }
        static string[] PRearDoors { get { return Msc ? new string[0] : new[] { "Doors/DoorRear(leftx)", "Doors/DoorRear(right)" }; } }
        static string[] PSeats { get { return Msc ? new string[0] : new[] { "Colliders/seats1", "Colliders/seats2", "Colliders/seats3", "Colliders/seats4", "Colliders/bench", "RearSeat/Pivot/Coll" }; } }
        static string[] PDriver
        {
            get
            {
                return Msc ? new[] { "LOD/PlayerTrigger/DriveTrigger", "LOD/DriverHeadPivot", "LOD/PlayerTrigger/SpawnPlayer", "MassDriver" }
                           : new[] { "Functions/PlayerTrigger/DriveTriggerX", "Functions/PlayerTrigger/DriverHeadPivot", "Functions/PlayerTrigger/SpawnPlayer", "Functions/MassDriver", "Functions/SteeringColumnPivot" };
            }
        }
        public static string Ctl(string sorbet)
        {
            if (!Msc || sorbet == null) return sorbet;
            switch (sorbet)
            {
                case "Functions/SteeringColumnPivot/SteeringColumn/IGNITIONxSorbet": return "LOD/Dashboard/Knobs/Ignition";
                case "Functions/Dashboard/Buttons/ButtonLightModes": return "LOD/Dashboard/Knobs/Lights";
                case "Functions/Dashboard/Buttons/ButtonWipers": return "LOD/Dashboard/Knobs/ButtonWipers";
                case "Functions/Dashboard/Buttons/ButtonHazard": return "LOD/Dashboard/Knobs/ButtonHorn";
                default: return null;
            }
        }

        // Ruscko copiee (pack rallye MSC du 19/10, retours de JD) : nid de guepes retire (son automate Starter le reveille au
        // demarrage : des guepes dans la voiture) ; automates 'Angle' des roues arriere coupes (essieu oscillant : roues
        // inclinees) ; automate 'Brake' de la tirette coupe (il ecrit handbrakeInput a chaque image, par-dessus la touche du
        // frein a main ; son 'Use' -- clic gauche tire, droit relache -- reste : tirette posee sur le levier du modele, voir
        // MscBrake) ; refroidissement coupe (Cooling : un moteur de rallye a fond chauffait, le
        // frottement du moteur montait) ; contact sans la cle de la Ruscko ('Check key' : PlayerKeyRuscko) ; demarreur de
        // diesel fatigue rendu sur ('Check clutch' grince 4 fois sur 5, 'Starting engine' lance de 0,5 a 15 s : 0,6-1,4 s) ;
        // FixedJoint de la racine retire (pose par l'automate LOD de la vraie Ruscko loin de la camera : la copie serait
        // clouee sur place).
        static void MscFixes(GameObject car)
        {
            Transform ct = car.transform;
            int n = 0;
            Transform hive = ct.Find("WaspHive");
            if (hive != null) { Object.DestroyImmediate(hive.gameObject); n++; }
            foreach (FixedJoint fj in car.GetComponents<FixedJoint>()) { Object.DestroyImmediate(fj); n++; }
            foreach (PlayMakerFSM f in car.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                string on = f.transform.name;
                if ((f.FsmName == "Angle" && f.GetComponent<Wheel>() != null) || (on == "ParkingBrake" && f.FsmName == "Brake") || (f.FsmName == "Cooling" && on == "Cooling")) { f.enabled = false; n++; continue; }
                // (moteur noye herite : la Ruscko de la partie restee dans l'eau a InWater vrai sur son contact -- sa copie ne
                // demarrerait jamais ; l'automate InWater de la prise d'air, repart de son etat de depart sur la copie, garde le vrai
                // noyage quand on roule dans l'eau)
                if (f.FsmName == "Use" && on == "Ignition") { FsmBool w = f.FsmVariables.FindFsmBool("InWater"); if (w != null && w.Value) { w.Value = false; n++; } }
                if (f.FsmName == "Use" && on == "Ignition")
                    foreach (FsmState st in f.FsmStates)
                        if (st.Name == "Check key")
                            foreach (FsmStateAction ac in st.Actions)
                            {
                                var ic = ac as HutongGames.PlayMaker.Actions.IntCompare;
                                if (ic != null) { ic.integer1 = new FsmInt { Value = ic.integer2 != null ? ic.integer2.Value : 1 }; n++; }
                            }
                if (f.FsmName == "Starter" && on == "Starter")
                    foreach (FsmState st in f.FsmStates)
                        foreach (FsmStateAction ac in st.Actions)
                        {
                            var re = ac as HutongGames.PlayMaker.Actions.SendRandomEvent;
                            if (st.Name == "Check clutch" && re != null && re.weights != null && re.weights.Length == 2) { re.weights[0].Value = 0f; re.weights[1].Value = 1f; n++; }
                            var rf = ac as HutongGames.PlayMaker.Actions.RandomFloat;
                            if (st.Name == "Starting engine" && rf != null) { rf.min.Value = 0.6f; rf.max.Value = 1.4f; n++; }
                        }
                // touche du frein a main (Espace) : la Ruscko du jeu n'a que sa tirette -- l'action « handbrakeAxis := Handbrake » de
                // son entree au volant (DriveTrigger 'PlayerTrigger', etat 'Player in car') est desactivee dans le jeu ; rallumee sur nos
                // copies, comme sur la Satsuma (JD 24/10 : « ESPACE pour tirer le frein a main, ca ne marche pas » sur la 205 Ebo 2)
                if (f.FsmName == "PlayerTrigger" && on == "DriveTrigger")
                    foreach (FsmState st in f.FsmStates)
                        if (st.Name == "Player in car")
                            foreach (FsmStateAction ac in st.Actions)
                            {
                                var sp = ac as HutongGames.PlayMaker.Actions.SetProperty;
                                if (sp != null && !ac.Enabled && sp.targetProperty != null && sp.targetProperty.PropertyName == "handbrakeAxis") { ac.Enabled = true; n++; }
                            }
            }
            Log.Info("vaz : Ruscko copiee : " + n + " correction(s) (guepes, roues arriere, tirette, refroidissement, contact sans cle, demarreur, touche du frein a main, moteur pas noye)");
        }

        // Voiture du rallye du jeu (RACES/.../RALLYCAR3 ; chez l'invite, Races la sort de son parent) : recherche parmi tous les
        // objets de la scene, eteints compris.
        public static GameObject RallyCar()
        {
            foreach (Transform t in Resources.FindObjectsOfTypeAll(typeof(Transform)))
                if (t != null && t.name == "RALLYCAR3" && t.gameObject.hideFlags == HideFlags.None && t.GetComponent("CarDynamics") != null) return t.gameObject;
            return null;
        }

        // Ce qui est sous la SORBET sans etre a elle : le joueur local (assis ou au volant) et les pivots des places de Seats.
        static List<KeyValuePair<Transform, Transform>> SetAside(Transform car)
        {
            var l = new List<KeyValuePair<Transform, Transform>>();
            if (Config.GetInt("Test", "VazSansMiseDeCote", 0) != 0) return l;   // (essais : comme avant)
            Transform pl = Game.PlayerT;
            if (pl != null && pl != car && pl.IsChildOf(car)) l.Add(new KeyValuePair<Transform, Transform>(pl, pl.parent));
            foreach (Transform t in car.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("MWCoop-Siege")) l.Add(new KeyValuePair<Transform, Transform>(t, t.parent));
            foreach (KeyValuePair<Transform, Transform> kv in l) kv.Key.parent = null;   // (pose du monde gardee)
            if (l.Count > 0) Log.Info("vaz : copie de la SORBET : " + l.Count + " objet(s) mis de cote (joueur assis ou au volant)");
            return l;
        }
        static void PutBack(List<KeyValuePair<Transform, Transform>> l)
        {
            for (int i = l.Count - 1; i >= 0; i--)
                if (l[i].Key != null && l[i].Value != null) l[i].Key.parent = l[i].Value;
        }

        // ---------------------------------------------------------------- construction
        // name : nom de l'objet (le meme chez tous) ; number : 1-16 (numero et couleur). Rend null si la SORBET ou les fichiers manquent.
        public static Built Build(string name, int number, Color paint, Vector3 pos, Quaternion rot, Model carModel = null)
        {
            GameObject sorbet = FindSorbet();
            if (sorbet == null) { Log.Warn("vaz : SORBET introuvable"); return null; }
            cur = carModel ?? Vaz2108;
            if (!cur.HasFiles) { Log.Warn("vaz : fichiers du modele absents (" + cur.Dir + ")"); return null; }
            Dictionary<string, Vector3> inf = Info();
            var b = new Built { Number = number, Shown = number, M = cur, Paint = paint };
            ModSafe.Snapshot snap = ModSafe.Before(sorbet);
            // (le joueur local assis dans la SORBET -- passager : Seats le rattache a elle, conducteur : sous son CameraPivotPLR --
            // partait dans la copie : un second PLAYER dans la VAZ, sa camera et ses commandes, le vrai joueur reste sur son siege,
            // vue teleportee dans la nouvelle voiture et positions desynchronisees ; JD et Ali BERTA, 25/10. Detache le temps de la copie.)
            List<KeyValuePair<Transform, Transform>> aside = SetAside(sorbet.transform);
            GameObject car;
            try { car = Object.Instantiate(sorbet); }
            finally { PutBack(aside); }
            car.SetActive(false);   // (reglee eteinte : ses automates demarrent sur la version modifiee)
            ModSafe.Strip(car, snap, "vaz " + name);   // (scripts des mods de la SORBET : Sorbet Collection, JD 23/10)
            car.name = name;
            b.Car = car;
            Transform ct = car.transform;
            ct.position = sorbet.transform.position; ct.rotation = sorbet.transform.rotation;

            // 1. Automates : etats Save/Load vides, SaveOptions coupe, identifiants prefixes (jamais ceux de la SORBET).
            int emptied = 0, ids = 0;
            string pre = "V" + number + "-";
            Transform saveOpt = ct.Find("SaveOptions");
            foreach (PlayMakerFSM f in car.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (saveOpt != null && f.transform.IsChildOf(saveOpt)) { f.enabled = false; emptied++; continue; }
                try
                {
                    f.Fsm.InitData();
                    foreach (FsmState s in f.FsmStates)
                        if (s.Name.IndexOf("Save", System.StringComparison.OrdinalIgnoreCase) >= 0 || s.Name.IndexOf("Load", System.StringComparison.OrdinalIgnoreCase) >= 0) { s.Actions = new FsmStateAction[0]; emptied++; }
                    FsmString[] strs = f.FsmVariables.StringVariables;
                    if (strs != null)
                        foreach (FsmString sv in strs)
                            if (sv != null && (sv.Name == "ID" || sv.Name.StartsWith("UniqueTag")) && !string.IsNullOrEmpty(sv.Value) && !sv.Value.StartsWith(pre)) { sv.Value = pre + sv.Value; ids++; }
                }
                catch (System.Exception e) { Log.Warn("vaz : automate " + f.FsmName + " : " + e.Message); }
            }
            if (Msc) MscFixes(car);

            // 2. Visuel de la SORBET cache (colliders et fumee d'echappement gardes), LODGroup retire, portieres arriere et hayon
            //    eteints (trois portes), degats coupes.
            foreach (Renderer rd in car.GetComponentsInChildren<Renderer>(true))
            {
                if (rd is ParticleRenderer || rd is ParticleSystemRenderer) continue;
                rd.enabled = false; b.Hidden.Add(rd);
            }
            foreach (LODGroup lg in car.GetComponentsInChildren<LODGroup>(true)) Object.Destroy(lg);
            // (hayon de la SORBET garde quand le modele a le sien : il porte celui de la VAZ, voir 4b)
            bool hasHatch = File.Exists(Path.Combine(cur.Dir, "hayon.p205"));
            var offParts = new List<string>(PRearDoors);
            if (!hasHatch) offParts.Add(PHatch);
            foreach (string dp in offParts)
            {
                Transform d = ct.Find(dp);
                if (d != null && d.gameObject.activeSelf) d.gameObject.SetActive(false);
            }
            foreach (Component c in car.GetComponentsInChildren<Component>(true))
                if (c != null && c.GetType().Name == "Deformable" && c is Behaviour) ((Behaviour)c).enabled = false;
            // Sieges de la SORBET (colliders invisibles : ses deux sieges avant, sa banquette) : pas aux places des baquets de la
            // VAZ, ils occupaient la zone d'entree du conducteur et la place du passager une fois reculees -- plus moyen d'y
            // entrer (retour de JD, 18/10 ; releve autotest vaz, VazPlaces).
            int seatColl = 0;
            foreach (string sp in PSeats)
            {
                Transform s = ct.Find(sp);
                if (s == null) continue;
                foreach (Collider k in s.GetComponents<Collider>()) { k.enabled = false; seatColl++; }
            }

            // 3. Alignement : milieu des roues de la VAZ (infos.txt) sur celui de la SORBET ; essieux a l'empattement de la VAZ.
            Wheel[] wheels = car.GetComponentsInChildren<Wheel>(true);
            Vector3 sMid = Vector3.zero, vMid = Vector3.zero; int nv = 0;
            foreach (Wheel w in wheels) sMid += ct.InverseTransformPoint((w.model != null ? w.model.transform : w.transform).position);
            if (wheels.Length > 0) sMid /= wheels.Length;
            foreach (string wn in new[] { "wheel_lf", "wheel_rf", "wheel_lr", "wheel_rr" }) { Vector3 p; if (inf.TryGetValue(wn, out p)) { vMid += p; nv++; } }
            if (nv > 0) vMid /= nv;
            Vector3 offset = sMid - vMid;
            foreach (string[] ax in new[] { new[] { "FRONT", "wheel_lf", "wheel_rf" }, new[] { "REAR", "wheel_lr", "wheel_rr" } })
            {
                Transform at = ct.Find(ax[0]); Vector3 a1, a2;
                if (at == null || !inf.TryGetValue(ax[1], out a1) || !inf.TryGetValue(ax[2], out a2)) continue;
                at.localPosition = new Vector3(at.localPosition.x, at.localPosition.y, offset.z + (a1.z + a2.z) * 0.5f);
            }
            // (Ruscko : roues directement sous la voiture -- chacune avancee ou reculee a l'essieu du modele, pack rallye MSC)
            if (Msc)
                foreach (Wheel w in wheels)
                {
                    Vector3 lp = ct.InverseTransformPoint(w.transform.position), a;
                    if (!inf.TryGetValue("wheel_" + (lp.x < 0 ? "l" : "r") + (lp.z > 0 ? "f" : "r"), out a)) continue;
                    w.transform.position = ct.TransformPoint(new Vector3(lp.x, lp.y, offset.z + a.z));
                }

            // 4. Carrosserie et capot (soude : pas de moteur dessous), portieres, volant.
            Material basePaint = FindMat(sorbet, "paint") ?? FindMat(sorbet, "");
            var body = new GameObject("VAZ");
            body.transform.SetParent(ct, false);
            body.transform.localPosition = offset; body.transform.localRotation = Quaternion.identity;
            b.Body = body.transform;
            AddParts(b, body.transform, "carrosserie.p205", basePaint, paint, number);
            AddParts(b, body.transform, "capot.p205", basePaint, paint, number);
            int doors = 0;
            foreach (string[] dd in new[] { new[] { "portiere_g.p205", PDoorL, "door_dside_f" }, new[] { "portiere_d.p205", PDoorR, "door_pside_f" } })
            {
                Transform sd = ct.Find(dd[1]);
                if (sd == null) { Log.Warn("vaz : " + dd[1] + " de la voiture de base introuvable"); continue; }
                Vector3 hl;
                if (Msc && inf.TryGetValue(dd[2], out hl)) MscHinge(ct, body.transform, sd, dd[0], hl);
                else if (inf.TryGetValue(dd[2], out hl))
                {
                    // charniere de la SORBET sur celle de la VAZ (x et z ; axe vertical garde), attache de l'articulation recalee
                    Vector3 was = ct.InverseTransformPoint(sd.position), lw = ct.InverseTransformPoint(body.transform.TransformPoint(hl));
                    sd.position = ct.TransformPoint(new Vector3(lw.x, was.y, lw.z));
                    var hj = sd.GetComponent<HingeJoint>();
                    if (hj != null)
                    {
                        Vector3 aw = sd.TransformPoint(hj.anchor);
                        hj.autoConfigureConnectedAnchor = false;
                        hj.connectedAnchor = hj.connectedBody != null ? hj.connectedBody.transform.InverseTransformPoint(aw) : aw;
                    }
                }
                var door = new GameObject("VAZ_" + dd[0].Replace(".p205", ""));
                door.transform.SetParent(body.transform, false);
                AddParts(b, door.transform, dd[0], basePaint, paint, number);
                door.transform.SetParent(sd, true);
                doors++;
            }

            // 4b. Hayon (coffre) : celui de la SORBET (corps sur charniere : clic pour ouvrir, ses automates, synchro du jeu) pose
            //     sur la charniere de la VAZ (haut du hayon), le hayon du modele accroche dessous (demande de JD, 18/10 soir).
            Transform sh = ct.Find(PHatch);
            Vector3 hh;
            if (hasHatch && sh != null && inf.TryGetValue("hayon_charniere", out hh))
            {
                var hj = sh.GetComponent<HingeJoint>();
                Vector3 aw = hj != null ? sh.TransformPoint(hj.anchor) : sh.position;
                sh.position += body.transform.TransformPoint(hh) - aw;
                if (hj != null)
                {
                    Vector3 naw = sh.TransformPoint(hj.anchor);
                    hj.autoConfigureConnectedAnchor = false;
                    hj.connectedAnchor = hj.connectedBody != null ? hj.connectedBody.transform.InverseTransformPoint(naw) : naw;
                }
                var hatch = new GameObject("VAZ_hayon");
                hatch.transform.SetParent(body.transform, false);
                AddParts(b, hatch.transform, "hayon.p205", basePaint, paint, number);
                hatch.transform.SetParent(sh, true);
            }

            // 4c. Trappe de toit (a nous : clic pour l'ouvrir vers l'exterieur, synchronisee par Vaz) : charniere au bord avant.
            Vector3 th;
            if (File.Exists(Path.Combine(cur.Dir, "trappe.p205")) && inf.TryGetValue("trappe_charniere", out th))
            {
                b.Roof = new GameObject("VAZ_trappe").transform;
                b.Roof.SetParent(body.transform, false);
                b.Roof.localPosition = th;
                var inner = new GameObject("pieces").transform;
                inner.SetParent(b.Roof, false);
                inner.localPosition = -th;
                AddParts(b, inner, "trappe.p205", basePaint, paint, number);
                Bounds rb = new Bounds(); bool first = true;
                foreach (MeshFilter mf in inner.GetComponentsInChildren<MeshFilter>())
                {
                    Bounds mb = mf.sharedMesh.bounds;
                    if (first) { rb = mb; first = false; } else rb.Encapsulate(mb);
                }
                var zone = new GameObject("VAZ_trappe_zone");
                zone.layer = 2;   // (Ignore Raycast : ne gene pas les clics du jeu ; visee par Collider.Raycast)
                zone.transform.SetParent(inner, false);
                b.RoofZone = zone.AddComponent<BoxCollider>();
                b.RoofZone.isTrigger = true;
                b.RoofZone.center = rb.center;
                b.RoofZone.size = new Vector3(rb.size.x, Mathf.Max(0.08f, rb.size.y), rb.size.z);
            }
            Vector3 sc;
            if (inf.TryGetValue("volant_centre", out sc))
            {
                b.SteerPivot = new GameObject("VAZ_volant").transform;
                b.SteerPivot.SetParent(body.transform, false);
                b.SteerPivot.localPosition = sc;
                var inner = new GameObject("pieces").transform;
                inner.SetParent(b.SteerPivot, false);
                inner.localPosition = -sc;
                AddParts(b, inner, "volant.p205", basePaint, paint, number);
                Vector3 axv;
                if (inf.TryGetValue("volant_axe", out axv) && axv.sqrMagnitude > 0.1f) b.SteerAxis = axv.normalized;
                b.SorbetSteer = FindDeep(ct, NSteer);
                if (b.SorbetSteer != null) b.SorbetSteerRest = b.SorbetSteer.localRotation;
                b.Axis = car.GetComponent("AxisCarController");
            }

            // 5. Roues visibles : jantes et pneus de RALLYCAR3 sur les modeles de roue de la SORBET, au rayon de la SORBET, a la
            //    voie de la VAZ (roue visible decalee le long de l'essieu ; les roues physiques ne bougent pas).
            GameObject rc = Msc ? null : RallyCar();
            int rw = Msc ? MscWheels(b, ct, wheels, inf, offset, basePaint, paint, number) : 0;
            Wheel[] rcw = rc != null ? rc.GetComponentsInChildren<Wheel>(true) : new Wheel[0];
            foreach (Wheel w in Msc ? new Wheel[0] : wheels)
            {
                Wheel mate = null;
                foreach (Wheel x in rcw) if (x.wheelPos == w.wheelPos) { mate = x; break; }
                GameObject rm = mate != null ? mate.model : null;
                if (rm == null) continue;
                Transform model = w.model != null ? w.model.transform : w.transform;
                GameObject cp = CopyVisual(rm, model, "VAZ_roue");
                float k = mate.radius > 0.05f ? w.radius / mate.radius : 1f;
                Vector3 ls = rm.transform.lossyScale * k, ms = model.lossyScale;
                cp.transform.localPosition = Vector3.zero; cp.transform.localRotation = Quaternion.identity;
                cp.transform.localScale = new Vector3(ls.x / ms.x, ls.y / ms.y, ls.z / ms.z);
                Vector3 lp = ct.InverseTransformPoint(model.position), target;
                string wn = "wheel_" + (lp.x < 0 ? "l" : "r") + (lp.z > 0 ? "f" : "r");
                if (inf.TryGetValue(wn, out target)) cp.transform.position += ct.right * ((offset.x + target.x) - lp.x);
                rw++;
            }

            // 6. Poste de conduite : zone d'entree, pivot de tete, SpawnPlayer, MassDriver et colonne de direction de la SORBET
            //    glissent du meme vecteur (tete de la SORBET -> tete du pilote de la VAZ).
            Transform hp = FindDeep(ct, "DriverHeadPivot");
            Vector3 delta = Vector3.zero;
            if (hp != null)
            {
                delta = (offset + cur.Head) - ct.InverseTransformPoint(hp.position);
                foreach (string mp in PDriver)
                {
                    Transform m = ct.Find(mp);
                    if (m != null) m.position = ct.TransformPoint(ct.InverseTransformPoint(m.position) + delta);
                }
                // (205 Ebo 2, JD 23/10 : camera plus basse, mais l'assise de l'Ebo 1 -- la zone d'entree descendue avec la tete
                // n'etait plus atteignable qu'en se collant au volant)
                if (cur.EyeDrop != 0f) hp.position -= ct.up * cur.EyeDrop;
                foreach (Joint j in hp.GetComponents<Joint>())
                {
                    Vector3 aw = hp.TransformPoint(j.anchor);
                    j.autoConfigureConnectedAnchor = false;
                    j.connectedAnchor = j.connectedBody != null ? j.connectedBody.transform.InverseTransformPoint(aw) : aw;
                }
                // Zone d'entree du conducteur (DriveTriggerX : capsule de 10 cm de rayon, 50 cm de long, au niveau du siege) : le
                // volume invisible de l'habitacle de la SORBET (Colliders/PlayerColl) ne laisse approcher depuis la portiere que
                // l'avant du siege ; reculee avec la tete, elle n'etait plus atteignable (retour de JD, 18/10). Ramenee 15 cm
                // en arriere de sa place d'origine (avant : il fallait se coller au volant), 8 cm vers la portiere, et agrandie
                // (rayon 26 cm, 70 cm de long) : elle couvre l'ancien point et l'avant du siege.
                Transform dtx = ct.Find(PDriveTrigger);
                if (dtx != null)
                {
                    float side = ct.InverseTransformPoint(dtx.position).x < 0f ? -1f : 1f;
                    dtx.position = ct.TransformPoint(ct.InverseTransformPoint(dtx.position) + new Vector3(side * 0.08f, 0f, Config.GetFloat("Test", "VazZoneAvance", 0.12f)));
                    var cap = dtx.GetComponent<CapsuleCollider>();
                    if (cap != null) { cap.radius = Mathf.Max(cap.radius, 0.26f); cap.height = Mathf.Max(cap.height, 0.7f); }
                }
            }

            // 6b. Commandes du tableau de bord et frein a main sur ceux du modele : chaque objet du jeu deplace pour que le centre de
            //     ses colliders tombe sur la piece de la VAZ.
            int ctl = 0;
            foreach (KeyValuePair<string, Vector3> kv in cur.Controls)
            {
                string cp = Ctl(kv.Key);
                if (cp == null) continue;   // (rien de tel sur la Ruscko)
                Transform t = ct.Find(cp);
                if (t == null) { Log.Warn("vaz : commande " + cp + " introuvable"); continue; }
                if (cur.ControlSize > 0f) Shrink(t, cur.ControlSize);   // (boutons a 3 cm : le starter de la SORBET couvrait les phares)
                if (MoveOnto(t, body.transform.TransformPoint(kv.Value))) ctl++;
            }
            if (cur.Off != null)
                foreach (string op in cur.Off)
                {
                    string cp = Ctl(op);
                    Transform o = cp != null ? ct.Find(cp) : null;
                    if (o == null) continue;
                    foreach (Collider k in o.GetComponentsInChildren<Collider>(true)) { k.enabled = false; b.OffColl.Add(k); }
                }
            if (cur.Knobs != null)
                for (int i = 0; i < cur.Knobs.Length; i++)
                {
                    Knob kn = cur.Knobs[i];
                    GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Object.DestroyImmediate(g.GetComponent<Collider>());   // (le clic va a la commande du jeu dessous)
                    g.name = "VAZ_bouton_" + i;
                    g.transform.SetParent(body.transform, false);
                    g.transform.localPosition = kn.At;
                    g.transform.localRotation = Quaternion.FromToRotation(Vector3.up, kn.Normal);
                    g.transform.localScale = new Vector3(0.017f, 0.006f, 0.017f);   // (cylindre d'Unity : 1 x 2 x 1)
                    // (plastique brillant : la peinture de la voiture, avec ses reflets, les rendait pastel)
                    Shader ks = Shader.Find("Legacy Shaders/Specular") ?? Shader.Find("Specular");
                    var mat = ks != null ? new Material(ks) { color = kn.Col, name = "vaz_bouton_" + i }
                                         : new Material(basePaint) { mainTexture = null, color = kn.Col, name = "vaz_bouton_" + i };
                    if (mat.HasProperty("_Shininess")) mat.SetFloat("_Shininess", 0.6f);
                    if (mat.HasProperty("_SpecColor")) mat.SetColor("_SpecColor", new Color(0.35f, 0.35f, 0.35f));
                    g.GetComponent<MeshRenderer>().sharedMaterial = mat;
                    b.Mats["bouton_" + i] = mat;
                    if (kn.Choke)
                    {
                        b.ChokeKnob = g.transform; b.ChokeRest = kn.At; b.ChokeDir = kn.Normal;
                        b.ChokeButton = Ctl(kn.Path) != null ? ct.Find(Ctl(kn.Path)) : null;
                    }
                }
            if (cur.HasDome && !Msc)
            {
                Transform dome = ct.Find("Functions/InteriorLight");
                if (dome != null) MoveOnto(dome, body.transform.TransformPoint(cur.Dome));
            }
            Transform lever = ct.Find(PLever);
            if (lever != null)
            {
                // (addons : zone cliquable ramenee a la poignee -- celle de la SORBET masquait le bouton du warning de la Subaru)
                // (My Summer Car : la tirette de la Ruscko aussi -- JD 24/10 : « la voiture n'a pas de frein a main » --, sa
                // position lue dans MscBrake)
                if (cur.ControlSize > 0f) Shrink(lever, cur.ControlSize * 2f);
                MoveOnto(lever, body.transform.TransformPoint(cur.BrakeGrip));
                b.SorbetBrake = lever; b.SorbetBrakeRest = lever.localRotation;
                b.BrakePivot = new GameObject("VAZ_frein").transform;
                b.BrakePivot.SetParent(body.transform, false);
                b.BrakePivot.localPosition = cur.BrakePivot;
                var inner = new GameObject("pieces").transform;
                inner.SetParent(b.BrakePivot, false);
                inner.localPosition = -cur.BrakePivot;
                AddParts(b, inner, "frein.p205", basePaint, paint, number);
                FitLever(lever, b.BrakePivot);
                if (Msc && MscKnob(b) > 0f) b.BrakeKnob.Value = 0f;   // (tirette de la Ruscko copiee serree : relachee, l'embrayage serait coupe)
            }
            else Log.Warn("vaz : frein a main du jeu (" + PLever + ") introuvable");

            // 7. Lumieres de la SORBET sur les feux de la VAZ ; listes pour allumer les feux du modele.
            int lights = 0;
            foreach (Light l in car.GetComponentsInChildren<Light>(true))
            {
                Vector3 lp = ct.InverseTransformPoint(l.transform.position);
                if (l.name.Contains("Brake")) b.Brake.Add(l);
                else if (l.name.Contains("Reverse")) b.Reverse.Add(l);
                else if (l.type == LightType.Spot && lp.z > 0.5f) b.Front.Add(l);
                else if (lp.z < -0.5f && l.color.r > l.color.g * 1.5f) b.Rear.Add(l);
                if (Mathf.Abs(lp.z) < 1.2f) continue;   // (voyants du tableau de bord, plafonnier)
                bool orange = l.color.r > 0.8f && l.color.g > 0.35f && l.color.g < 0.8f && l.color.b < 0.3f;
                bool red = l.color.r > l.color.g * 1.8f && !orange;
                string side = lp.x < 0 ? "l" : "r", bone = null;
                if (lp.z > 0.5f) bone = l.type == LightType.Spot || (!red && !orange) ? "headlight_" + side : null;
                else if (lp.z < -0.5f && red) bone = "taillight_" + side;
                Vector3 target;
                if (bone == null || !inf.TryGetValue(bone, out target)) continue;
                l.transform.position = body.transform.TransformPoint(target) + ct.forward * (lp.z > 0 ? 0.08f : -0.06f);
                lights++;
            }

            // 8. Mecanique, place, allumee ; VehicleSync la releve tout de suite.
            b.Rb = car.GetComponent<Rigidbody>();
            b.Dt = car.GetComponent<Drivetrain>();
            VazHandling.Apply(b, true);
            var eng = new GameObject("VAZ_moteur_son").transform;
            eng.SetParent(body.transform, false); eng.localPosition = cur.Engine;
            b.Sound = VazSound.Create(car, eng, cur);
            // bavettes souples (addons : bavette_*.p205 du dossier du modele, VazFlaps)
            foreach (string bn in new[] { "bavette_ag", "bavette_ad", "bavette_vg", "bavette_vd" })
            {
                if (!File.Exists(Path.Combine(cur.Dir, bn + ".p205"))) continue;
                var bg = new GameObject("VAZ_" + bn).transform;
                bg.SetParent(body.transform, false);
                AddParts(b, bg, bn + ".p205", basePaint, paint, number);
                if (b.Flaps == null) b.Flaps = new VazFlaps(body.transform, b.Rb);
                b.Flaps.Add(bg);
            }
            if (b.Flaps != null) Log.Info("vaz : " + b.Flaps.Count + " bavette(s) souple(s)");
            // antennes souples (antenne_1 / antenne_2.p205, VazAntennas ; hors de la carrosserie, donc sans collision -- JD 29/10)
            foreach (string an in new[] { "antenne_1", "antenne_2" })
            {
                if (!File.Exists(Path.Combine(cur.Dir, an + ".p205"))) continue;
                var ag = new GameObject("VAZ_" + an).transform;
                ag.SetParent(body.transform, false);
                AddParts(b, ag, an + ".p205", basePaint, paint, number);
                if (b.Antennas == null) b.Antennas = new VazAntennas(body.transform, b.Rb);
                b.Antennas.Add(ag);
            }
            if (b.Antennas != null) Log.Info("vaz : " + b.Antennas.Count + " antenne(s) souple(s)");
            if (b.Axis == null) b.Axis = car.GetComponent("AxisCarController");
            Transform tank = ct.Find(PTank);
            PlayMakerFSM tf = tank != null ? Game.FsmOn(tank.gameObject, "Data") : null;
            FsmFloat fuel = tf != null ? tf.FsmVariables.FindFsmFloat("FuelLevel") : null;
            if (fuel != null) fuel.Value = 40f;
            ct.position = pos; ct.rotation = rot;
            if (b.Rb != null) { b.Rb.velocity = Vector3.zero; b.Rb.angularVelocity = Vector3.zero; }
            car.SetActive(true);
            VehicleSync.RescanSoon();
            b.NextMask = 0f;
            Log.Info("vaz : " + name + " (" + cur.Name + ") construite en " + pos.ToString("F1") + " : " + emptied + " etats de sauvegarde vides, " + ids + " identifiants prefixes, " + b.Hidden.Count + " rendus SORBET caches, "
                     + doors + " portieres, " + ctl + " commandes posees, " + seatColl + " colliders de sieges SORBET coupes, " + rw + " roues du rallye" + (rc == null ? " (RALLYCAR3 introuvable)" : "") + ", " + lights + " lumieres posees, " + b.Lamps.Count + " pieces de feux, decalage " + offset.ToString("F2") + ", tete " + delta.ToString("F2"));
            return b;
        }

        // Portiere de la Ruscko sur la charniere du modele (pack rallye MSC, retours de JD du 19/10) : charniere sur l'arete avant
        // EXTERIEURE de la portiere du modele (celle d'infos.txt est en retrait ; la Ruscko ouvre plus grand que la SORBET et le
        // bord avant se decollait du montant) ; c'est l'ANCRE du HingeJoint (pas l'origine de la portiere, comme sur la SORBET)
        // qui y va, axe de la charniere remis vertical dans la voiture (celui de la Ruscko est dans le repere de sa portiere).
        static void MscHinge(Transform ct, Transform body, Transform sd, string file, Vector3 hl)
        {
            float front = float.MinValue, ox = 0f;
            foreach (Part p in Load(file)) foreach (Vector3 v in p.Mesh.vertices) front = Mathf.Max(front, v.z);
            foreach (Part p in Load(file)) foreach (Vector3 v in p.Mesh.vertices) if (v.z > front - 0.06f) ox = Mathf.Max(ox, Mathf.Abs(v.x));
            if (ox > 0f) hl = new Vector3(Mathf.Sign(hl.x) * (ox - 0.015f), hl.y, front - 0.03f);
            var hj = sd.GetComponent<HingeJoint>();
            Vector3 al = ct.InverseTransformPoint(hj != null ? sd.TransformPoint(hj.anchor) : sd.position), lw = ct.InverseTransformPoint(body.TransformPoint(hl));
            sd.position += ct.TransformVector(new Vector3(lw.x - al.x, 0f, lw.z - al.z));
            if (hj == null) return;
            Vector3 axW = ct.InverseTransformDirection(sd.TransformDirection(hj.axis));
            hj.axis = sd.InverseTransformDirection(ct.up * (axW.y < 0f ? -1f : 1f));
            Vector3 aw = sd.TransformPoint(hj.anchor);
            hj.autoConfigureConnectedAnchor = false;
            hj.connectedAnchor = hj.connectedBody != null ? hj.connectedBody.transform.InverseTransformPoint(aw) : aw;
            Log.Info("vaz : " + sd.name + " : charniere en " + hl.ToString("F3") + " (arete avant du modele), axe " + axW.ToString("F2") + " -> vertical");
        }

        // Roues sur My Summer Car (JD 24/10 : « leurs roues d'origine et pneus, vu que ce n'est pas l'hiver ») : roue.p205 du
        // modele (jante et pneu de terre de l'addon) sur chaque roue de la Ruscko, rayon et largeur physiques pris sur la roue du
        // modele, flanc du pneu aligne sur l'aile (pack rallye MSC). Sans roue.p205 (la VAZ) : les roues de la Ruscko restent.
        static int MscWheels(Built b, Transform ct, Wheel[] wheels, Dictionary<string, Vector3> inf, Vector3 offset, Material basePaint, Color paint, int number)
        {
            List<Part> rp = Load("roue.p205");
            if (rp.Count == 0)
            {
                // (roues de la Ruscko sorties a la voie du modele le long de leur axe : elles restaient rentrees sous les ailes de la VAZ)
                foreach (Wheel w in wheels)
                {
                    foreach (Renderer r in w.GetComponentsInChildren<Renderer>(true)) { b.Hidden.Remove(r); r.enabled = true; }
                    Transform model = w.model != null ? w.model.transform : w.transform;
                    Vector3 lp = ct.InverseTransformPoint(model.position), target;
                    if (!inf.TryGetValue("wheel_" + (lp.x < 0 ? "l" : "r") + (lp.z > 0 ? "f" : "r"), out target)) continue;
                    Vector3 d = ct.right * ((offset.x + target.x) - lp.x);
                    foreach (Transform c in model) c.position += d;
                }
                Log.Info("vaz : pas de roue.p205 : roues de la Ruscko gardees, a la voie du modele");
                return 0;
            }
            Bounds wb = rp[0].Mesh.bounds;
            foreach (Part p in rp) wb.Encapsulate(p.Mesh.bounds);
            float mr = Mathf.Max(wb.extents.y, wb.extents.z);
            List<Part> bodyParts = Load("carrosserie.p205");
            int n = 0;
            foreach (Wheel w in wheels)
            {
                w.radius = mr; w.width = wb.size.x;
                Transform model = w.model != null ? w.model.transform : w.transform;
                Vector3 lp = ct.InverseTransformPoint(model.position), target;
                Quaternion q = lp.x > 0f ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
                var rw = new GameObject("VAZ_roue").transform;
                rw.SetParent(model, false);
                rw.localRotation = q; rw.localPosition = -(q * wb.center);
                AddParts(b, rw, "roue.p205", basePaint, paint, number);
                if (inf.TryGetValue("wheel_" + (lp.x < 0 ? "l" : "r") + (lp.z > 0 ? "f" : "r"), out target))
                {
                    float extra = Mathf.Clamp(FenderGap(bodyParts, target, wb.extents.x, mr) - 0.006f, 0f, 0.12f);
                    rw.position += ct.right * ((offset.x + target.x) - lp.x + (lp.x < 0 ? -extra : extra));
                }
                n++;
            }
            Log.Info("vaz : " + n + " roues du modele (rayon " + mr.ToString("F3") + ", largeur " + wb.size.x.ToString("F3") + ")");
            return n;
        }

        // Ecart entre le flanc exterieur du pneu (moyeu + demi-largeur) et l'aile au-dessus : sommets de la carrosserie a moins de
        // 0,9 rayon du moyeu en z, entre 5 cm et rayon + 15 cm au-dessus du moyeu ; |x| le plus grand.
        static float FenderGap(List<Part> body, Vector3 hub, float halfWidth, float radius)
        {
            float fx = 0f;
            foreach (Part p in body)
                foreach (Vector3 v in p.Mesh.vertices)
                    if (Mathf.Abs(v.z - hub.z) < radius * 0.9f && v.y > hub.y + 0.05f && v.y < hub.y + radius + 0.15f && Mathf.Abs(v.x) > fx) fx = Mathf.Abs(v.x);
            return fx > 0f ? fx - (Mathf.Abs(hub.x) + halfWidth) : 0f;
        }

        // Copie d'un sous-arbre reduite a Transform + MeshFilter + MeshRenderer, tout allume.
        static GameObject CopyVisual(GameObject src, Transform parent, string name)
        {
            GameObject cp = Object.Instantiate(src);
            cp.name = name;
            for (int pass = 0; pass < 3; pass++)
                foreach (Component c in cp.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer) continue;
                    try { Object.DestroyImmediate(c); } catch { }
                }
            foreach (Transform t in cp.GetComponentsInChildren<Transform>(true)) t.gameObject.SetActive(true);
            cp.transform.SetParent(parent, false);
            return cp;
        }

        // ---------------------------------------------------------------- livree (Evo III : image du joueur, EvoLivery)
        // Les matieres de peinture de la voiture prennent l'image (couleur blanche : l'image telle quelle) ; tex null : retour a
        // la peinture sans livree a la couleur du joueur. Le numero (decalque) reste par-dessus.
        public static void SetLivery(Built b, Texture2D tex, string hash)
        {
            if (b == null) return;
            cur = b.M;
            int n = 0;
            foreach (KeyValuePair<string, Material> kv in b.Mats)
            {
                MatDef d;
                if (kv.Value == null || !matDefs.TryGetValue(cur.Folder + "|" + kv.Key, out d) || d.Kind != "paint") continue;
                kv.Value.mainTexture = tex != null ? tex : Tex(d.Tex);
                kv.Value.color = tex != null ? Color.white : b.Paint;
                n++;
            }
            b.LiveryHash = tex != null ? hash : null;
            Log.Info("vaz : " + (b.Car != null ? b.Car.name : "apercu") + " : " + (tex != null ? "livree " + hash : "couleur du joueur") + " (" + n + " matiere(s))");
        }

        // ---------------------------------------------------------------- vie de la voiture (chaque image)
        public static void Tick(Built b, float now)
        {
            if (b.Car == null) return;
            if (now >= b.NextHide)
            {
                b.NextHide = now + 0.5f;
                foreach (Renderer rd in b.Hidden) if (rd != null && rd.enabled) rd.enabled = false;   // (des automates rallument feux, vitres givrees...)
                foreach (Collider k in b.OffColl) if (k != null && k.enabled) k.enabled = false;
            }
            Steering(b);
            Brake(b);
            SeatView(b, now);
            ChokeKnob(b);
            Roof(b);
            if (b.Sound != null) b.Sound.Update();
            if (b.Flaps != null) b.Flaps.Update();
            if (b.Antennas != null) b.Antennas.Update();
            Lamps(b, now);
            VazHandling.Tick(b, now);
            if (now >= b.NextMask) { b.NextMask = now + 1f; WheelMask(b); }
        }

        // Trappe de toit : visee (camera, 1,6 m), icone et texte du jeu, clic gauche -> Vaz.RoofToggle (hote : applique et
        // diffuse) ; s'ouvre vers l'exterieur, bord arriere leve de 35 degres.
        static FsmBool guiUse; static FsmString guiText;
        static void Roof(Built b)
        {
            if (b.Roof == null) return;
            float want = b.RoofOpen ? 35f : 0f;
            if (!Mathf.Approximately(b.RoofAngle, want))
            {
                b.RoofAngle = Mathf.MoveTowards(b.RoofAngle, want, 90f * Time.deltaTime);
                b.Roof.localRotation = Quaternion.AngleAxis(b.RoofAngle, Vector3.right);
            }
            if (b.RoofZone == null) return;
            Camera cam = Game.MainCam;
            bool hover = false;
            if (cam != null && !Game.GlobalBool("PlayerSleeps"))
            {
                RaycastHit h;
                hover = b.RoofZone.Raycast(new Ray(cam.transform.position, cam.transform.forward), out h, 1.6f);
            }
            if (guiUse == null) { guiUse = FsmVariables.GlobalVariables.FindFsmBool("GUIuse"); guiText = FsmVariables.GlobalVariables.FindFsmString("GUIinteraction"); }
            if (hover)
            {
                if (guiUse != null) guiUse.Value = true;
                if (guiText != null) guiText.Value = b.RoofOpen ? Lang.TG("FERMER LA TRAPPE", "CLOSE ROOF HATCH") : Lang.TG("OUVRIR LA TRAPPE", "OPEN ROOF HATCH");
                if (Input.GetMouseButtonDown(0)) Vaz.RoofToggle(b.Number, !b.RoofOpen);
            }
            else if (b.RoofHover)
            {
                if (guiUse != null) guiUse.Value = false;
                if (guiText != null) guiText.Value = "";
            }
            b.RoofHover = hover;
        }

        // Objet du jeu deplace (sans le tourner) pour que le centre de ses colliders soit en 'world'.
        // Zones cliquables d'une commande ramenees a 'size' metres au plus dans chaque direction (centres gardes).
        // Zone cliquable du frein a main du jeu (LeverPivot, automate Use : MousePickEvent sur SON collider) posee sur le levier
        // visible du modele -- JD 23/10 : « je n'arrive pas a le cibler » (seule la Subaru allait) : la zone etait un cube de
        // 5 cm sur la poignee (Shrink), hors du levier qu'on vise sur la plupart des voitures. Boite du levier (au repos), 1,5 cm
        // de marge, 6 cm d'epaisseur au moins.
        static void FitLever(Transform lever, Transform visual)
        {
            bool any = false; Bounds wb = new Bounds();
            foreach (Renderer r in visual.GetComponentsInChildren<Renderer>(true)) { if (!any) { wb = r.bounds; any = true; } else wb.Encapsulate(r.bounds); }
            if (!any) return;
            bool trig = false;
            foreach (Collider c in lever.GetComponents<Collider>()) { trig |= c.isTrigger; Object.DestroyImmediate(c); }
            Bounds lb = new Bounds(); bool first = true;
            for (int i = 0; i < 8; i++)
            {
                Vector3 w = new Vector3((i & 1) == 0 ? wb.min.x : wb.max.x, (i & 2) == 0 ? wb.min.y : wb.max.y, (i & 4) == 0 ? wb.min.z : wb.max.z);
                Vector3 l = lever.InverseTransformPoint(w);
                if (first) { lb = new Bounds(l, Vector3.zero); first = false; } else lb.Encapsulate(l);
            }
            Vector3 sc = lever.lossyScale;
            float pad = 0.015f, min = 0.06f;
            Vector3 size = lb.size;
            for (int k = 0; k < 3; k++)
            {
                float s = Mathf.Max(Mathf.Abs(sc[k]), 1e-4f);
                size[k] = Mathf.Max(size[k] + 2f * pad / s, min / s);
            }
            var bc = lever.gameObject.AddComponent<BoxCollider>();
            bc.isTrigger = trig; bc.center = lb.center; bc.size = size;
            Log.Info("vaz : frein a main cliquable sur le levier du modele (" + Vector3.Scale(size, sc).ToString("F2") + " m)");
        }

        static void Shrink(Transform t, float size)
        {
            foreach (Collider k in t.GetComponentsInChildren<Collider>(true))
            {
                Vector3 s = k.transform.lossyScale;
                float sx = Mathf.Max(Mathf.Abs(s.x), 1e-4f), sy = Mathf.Max(Mathf.Abs(s.y), 1e-4f), sz = Mathf.Max(Mathf.Abs(s.z), 1e-4f);
                if (k is BoxCollider)
                {
                    var bc = (BoxCollider)k;
                    bc.size = new Vector3(Mathf.Min(bc.size.x, size / sx), Mathf.Min(bc.size.y, size / sy), Mathf.Min(bc.size.z, size / sz));
                }
                else if (k is SphereCollider)
                {
                    var sc = (SphereCollider)k;
                    sc.radius = Mathf.Min(sc.radius, size * 0.5f / Mathf.Max(sx, Mathf.Max(sy, sz)));
                }
                else if (k is CapsuleCollider)
                {
                    var cc = (CapsuleCollider)k;
                    float m = Mathf.Max(sx, Mathf.Max(sy, sz));
                    cc.radius = Mathf.Min(cc.radius, size * 0.5f / m); cc.height = Mathf.Min(cc.height, size / m);
                }
                else if (k is MeshCollider && ((MeshCollider)k).sharedMesh != null)
                {
                    // (maillage : remplace par une boite a sa place)
                    Bounds mb = ((MeshCollider)k).sharedMesh.bounds;
                    var bc = k.gameObject.AddComponent<BoxCollider>();
                    bc.isTrigger = k.isTrigger; bc.center = mb.center;
                    bc.size = new Vector3(Mathf.Min(mb.size.x, size / sx), Mathf.Min(mb.size.y, size / sy), Mathf.Min(mb.size.z, size / sz));
                    Object.DestroyImmediate(k);
                }
            }
        }

        static bool MoveOnto(Transform t, Vector3 world)
        {
            // (voiture encore eteinte : Collider.bounds est vide -> centre local de chaque collider, moyenne)
            Vector3 sum = Vector3.zero; int n = 0;
            foreach (Collider k in t.GetComponentsInChildren<Collider>(true))
            {
                Vector3 lc = k is BoxCollider ? ((BoxCollider)k).center : k is SphereCollider ? ((SphereCollider)k).center : k is CapsuleCollider ? ((CapsuleCollider)k).center
                           : k is MeshCollider && ((MeshCollider)k).sharedMesh != null ? ((MeshCollider)k).sharedMesh.bounds.center : Vector3.zero;
                sum += k.transform.TransformPoint(lc); n++;
            }
            Vector3 c = n > 0 ? sum / n : t.position;
            t.position += world - c;
            return n > 0;
        }

        // Levier du modele : position du levier du jeu (automate Use, KnobPos : 0 desserre, 20 serre = son angle en degres) ;
        // poignee vers l'avant -> elle monte quand on serre. (Avant : angle compte depuis la pose a la construction -- la SORBET
        // copiee est garee frein serre, tout etait a l'envers : retour de JD, 18/10.)
        // Vue de conduite (205 Ebo 2, JD 23/10 : « je suis dans le toit ») : en entrant, le jeu range PLAYER sous
        // DriverHeadPivot/CameraPivotPLR/Pivot SANS le deplacer (PlayerTrigger, etat Reset view) -- la vue reste a la hauteur
        // ou l'on se tenait (siege, bas de caisse...). Model.SeatEyes : 0,8 s apres l'entree (le temps de s'accroupir), PLAYER
        // recale pour que les yeux soient au pivot de tete + Seats.EyeFromPivot (la place du pilote du modele).
        static GameObject seatPl;
        static void SeatView(Built b, float now)
        {
            if (b.M == null || !b.M.SeatEyes) return;
            if (seatPl == null) seatPl = GameObject.Find("PLAYER");
            Transform piv = b.Car.transform.Find(PCamPivot);
            if (seatPl == null || piv == null || seatPl.transform.parent != piv) { b.SeatAt = -1f; b.SeatSet = false; return; }
            if (b.SeatSet) return;
            if (b.SeatAt < 0f) { b.SeatAt = now; return; }
            if (now - b.SeatAt < 0.8f) return;
            b.SeatSet = true;
            Transform hp = b.Car.transform.Find(PHeadPivot), cam = seatPl.transform.Find("Pivot/AnimPivot/Camera/FPSCamera");
            if (hp == null || cam == null) return;
            Transform ct = b.Car.transform;
            Vector3 want = ct.TransformPoint(ct.InverseTransformPoint(hp.position) + Seats.EyeFromPivot), was = cam.position;
            seatPl.transform.position += want - was;
            Log.Info("vaz : vue de conduite recalee de " + ct.InverseTransformPoint(was).ToString("F2") + " a " + ct.InverseTransformPoint(want).ToString("F2") + " (repere de la voiture)");
        }

        // My Summer Car : touche de frein a main du jeu (CarController : wheel.handbrake = handbrakeInput tant qu'on conduit) ET
        // tirette de la Ruscko posee sur le levier du modele (automate 'Use' : KnobPos 0-0,1 ; son 'Brake' coupe, MscFixes --
        // JD 24/10 : « la voiture n'a pas de frein a main, il n'est pas possible de leur en mettre un ? ») ; personne au volant
        // ici et voiture pas suivie pour un autre : frein serre sur les roues arriere (elle ne roule pas seule) ; le levier du
        // modele suit (20 degres serre).
        static float MscKnob(Built b)
        {
            if (b.BrakeKnob == null && b.SorbetBrake != null)
            {
                PlayMakerFSM f = Game.FsmOn(b.SorbetBrake.gameObject, "Use");
                b.BrakeKnob = f != null ? f.FsmVariables.FindFsmFloat("KnobPos") : null;
            }
            return b.BrakeKnob != null ? Mathf.Clamp01(b.BrakeKnob.Value * 10f) : 0f;
        }

        // (au volant ici : la liste des voitures de VehicleSync n'est tenue qu'en partie a plusieurs -- en solo, l'automate
        // PlayerTrigger de la zone conducteur, comme VehicleSync)
        static bool MscDriven(Built b)
        {
            if (VehicleSync.LocalDrivingRoot == b.Car.transform) return true;
            if (b.DriveFsm == null) { Transform dt = b.Car.transform.Find(PDriveTrigger); b.DriveFsm = dt != null ? Game.FsmOn(dt.gameObject, "PlayerTrigger") : null; }
            return b.DriveFsm != null && b.DriveFsm.ActiveStateName == "Player in car";
        }

        static void MscBrake(Built b)
        {
            bool driven = MscDriven(b);
            float hb = MscKnob(b);
            foreach (Wheel w in b.Car.GetComponentsInChildren<Wheel>(true))
            {
                if (!w.wheelPos.ToString().StartsWith("REAR")) continue;
                if (!driven && (b.Rb == null || !b.Rb.isKinematic)) w.handbrake = 1f;
                hb = Mathf.Max(hb, w.handbrake);
            }
            if (b.BrakePivot != null) b.BrakePivot.localRotation = Quaternion.AngleAxis(-Mathf.Clamp01(hb) * 20f * (b.M != null ? b.M.BrakeAngle : 1f), Vector3.right);
        }

        // (apres CarController.Update, qui relit la touche : tirette serree, handbrakeInput tenu pour la physique suivante)
        static System.Reflection.FieldInfo hbInput;
        public static void MscBrakeLate(Built b)
        {
            if (!Msc || b.Axis == null || b.Car == null || !MscDriven(b)) return;
            float k = MscKnob(b);
            if (k <= 0f) return;
            if (hbInput == null) hbInput = b.Axis.GetType().GetField("handbrakeInput");
            if (hbInput == null) return;
            object v = hbInput.GetValue(b.Axis);
            if (!(v is float) || (float)v < k) hbInput.SetValue(b.Axis, k);
        }

        static void Brake(Built b)
        {
            if (Msc) { MscBrake(b); return; }
            if (b.BrakePivot == null || b.SorbetBrake == null) return;
            if (b.BrakeKnob == null)
            {
                PlayMakerFSM f = Game.FsmOn(b.SorbetBrake.gameObject, "Use");
                b.BrakeKnob = f != null ? f.FsmVariables.FindFsmFloat("KnobPos") : null;
                if (b.BrakeKnob == null) return;
            }
            float a = Mathf.Clamp(b.BrakeKnob.Value, 0f, 20f) * Config.GetFloat("Test", "VazFreinSens", 1f) * (b.M != null ? b.M.BrakeAngle : 1f);
            b.BrakePivot.localRotation = Quaternion.AngleAxis(-a, Vector3.right);
        }

        // Starter (Ebo III) : le bouton bleu sort de la console comme celui de la SORBET (automate 'Use' du bouton : Choke de 1,
        // pousse, a 2, tire) -- 2,5 cm.
        static void ChokeKnob(Built b)
        {
            if (b.ChokeKnob == null || b.ChokeButton == null) return;
            if (b.ChokeVal == null)
            {
                PlayMakerFSM f = Game.FsmOn(b.ChokeButton.gameObject, "Use");
                b.ChokeVal = f != null ? f.FsmVariables.FindFsmFloat("Choke") : null;
                if (b.ChokeVal == null) return;
            }
            b.ChokeKnob.localPosition = b.ChokeRest + b.ChokeDir * (Mathf.Clamp(b.ChokeVal.Value, 1f, 2f) - 1f) * 0.025f;
        }

        // Volant : angle de celui de la SORBET (cache), autour de l'axe de la colonne de la VAZ.
        // Volant du modele : tourne de SteerRatio degres par unite de direction du controleur (AxisCarController.steering,
        // -1..1, pose aussi par VehicleSync sur la voiture d'un autre joueur), sens de celui de la SORBET. Avant le 21/10 il
        // recopiait la rotation du volant de la SORBET -- qui fait 270 degres de chaque cote et passe par le plus court
        // chemin : au-dela d'un demi-tour il repartait dans l'autre sens (video de Vaydon). SteerRatio : releve sur la SORBET
        // tant qu'elle est sous le demi-tour (270 sur la SORBET).
        public static bool SteerLog;   // ([Test] VazBraquer : angle image par image)
        static void Steering(Built b)
        {
            if (b.SteerPivot == null || b.SorbetSteer == null) return;
            float input = 0f;
            if (b.Axis != null)
            {
                if (steerField == null) steerField = b.Axis.GetType().GetField("steering");
                object v = steerField != null ? steerField.GetValue(b.Axis) : null;
                if (v is float) input = Mathf.Clamp((float)v, -1f, 1f);
            }
            // (rapport fixe du modele : le releve sur la SORBET varie de 170 a 450 -- 205 Ebo 2, JD 23/10 : « il tourne a peine et
            // dans le mauvais sens » ; a 270 degres, une jante a trois branches braquee a fond ressemble a un quart de tour a l'envers)
            if (b.M != null && b.M.SteerDeg > 0f) b.SteerRatio = b.M.SteerDeg;
            else if (Mathf.Abs(input) > 0.1f && Mathf.Abs(input) < 0.6f)
            {
                Quaternion d = Quaternion.Inverse(b.SorbetSteerRest) * b.SorbetSteer.localRotation;
                float a; Vector3 ax;
                d.ToAngleAxis(out a, out ax);
                if (a > 180f) a = 360f - a;
                if (a > 5f && a < 175f) b.SteerRatio = Mathf.Lerp(b.SteerRatio > 0f ? b.SteerRatio : a / Mathf.Abs(input), a / Mathf.Abs(input), 0.2f);
            }
            float turn = -input * (b.SteerRatio > 0f ? b.SteerRatio : 270f);
            if (SteerLog) Log.Info("volant : direction " + input.ToString("F2") + " rapport " + b.SteerRatio.ToString("F0") + " angle " + turn.ToString("F0"));
            b.SteerTurn = turn;
            b.SteerPivot.localRotation = Quaternion.AngleAxis(turn, b.SteerAxis);
        }

        static System.Reflection.FieldInfo steerField;

        static bool AnyOn(List<Light> ls)
        {
            foreach (Light l in ls) if (l != null && l.enabled && l.gameObject.activeInHierarchy && l.intensity > 0.01f) return true;
            return false;
        }

        // Feux du modele : allumes (sans eclairage) quand les lumieres de la copie le sont ; arriere faible avec les feux, fort au
        // freinage.
        static void Lamps(Built b, float now)
        {
            if (now < b.NextLamp) return;
            b.NextLamp = now + 0.1f;
            bool f = AnyOn(b.Front), r = AnyOn(b.Rear) || f, br = AnyOn(b.Brake), rv = AnyOn(b.Reverse);
            if (b.LampsInit && f == b.FrontOn && r == b.RearOn && br == b.BrakeOn && rv == b.ReverseOn) return;
            b.LampsInit = true; b.FrontOn = f; b.RearOn = r; b.BrakeOn = br; b.ReverseOn = rv;
            foreach (KeyValuePair<MeshRenderer, string> kv in b.Lamps)
            {
                if (kv.Key == null) continue;
                string k = kv.Value;
                Material m = null;
                if (k.EndsWith("_ar")) { if (br) b.Lit.TryGetValue(k, out m); else if (r && !b.Dim.TryGetValue(k, out m)) b.Lit.TryGetValue(k, out m); }
                else if ((k.EndsWith("_av") && f) || (k.EndsWith("_re") && rv)) b.Lit.TryGetValue(k, out m);
                kv.Key.sharedMaterial = m ?? b.Mats[k];
            }
        }

        // Rayons des roues : couches des colliders de la voiture croises par la course d'une roue retirees de leur masque (voir
        // Jokkis.WheelMask : essieux deplaces, un collider de caisse dans la course d'une roue fait tourner la voiture sur elle-meme).
        static void WheelMask(Built b)
        {
            Transform ct = b.Car.transform;
            Wheel[] wheels = b.Car.GetComponentsInChildren<Wheel>(true);
            int own = 0;
            foreach (Wheel w in wheels)
            {
                float travel = w.suspensionTravel > 0f ? w.suspensionTravel : 0.2f, rad = w.radius > 0f ? w.radius : 0.3f;
                for (float d = 0f; d <= travel + rad + 0.001f; d += 0.05f)
                    foreach (Collider c in Physics.OverlapSphere(w.transform.position - ct.up * d, 0.06f))
                    {
                        if (c == null || !c.transform.IsChildOf(ct)) continue;
                        int l = c.gameObject.layer;
                        string ln = LayerMask.LayerToName(l);
                        if (l == b.Car.layer || l == 0 || ln == "Terrain" || ln == "Water") continue;
                        own |= 1 << l;
                    }
            }
            if (own == 0) return;
            System.Reflection.FieldInfo lm = typeof(Wheel).GetField("layerMask", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (lm == null) return;
            foreach (Wheel w in wheels)
            {
                object v = lm.GetValue(w);
                int m = v is LayerMask ? ((LayerMask)v).value : v is int ? (int)v : 0;
                if ((m & own) == 0) continue;
                int nm = m & ~own;
                if (v is LayerMask) { LayerMask lk = (LayerMask)v; lk.value = nm; lm.SetValue(w, lk); } else lm.SetValue(w, nm);
                if (b.MaskLogs++ < 4) Log.Info("vaz : roue " + w.wheelPos + " : masque " + m + " -> " + nm);
            }
        }

        // Voiture detruite : ce qui n'est pas a elle en sort d'abord (joueur, cameras des retroviseurs, objets MWCoop).
        public static void Destroy(Built b)
        {
            if (b == null || b.Car == null) return;
            if (StandaloneCar.Owns(b)) { StandaloneCar.Remove(b); return; }   // (standalone : joueur sorti proprement d'abord)
            Transform ct = b.Car.transform;
            if (VehicleSync.LocalDrivingRoot == ct) VehicleSync.ExitLocal();
            GameObject mirrors = Game.FindAny("Systems/Mirrors/Disable");
            foreach (Transform t in b.Car.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t == ct) continue;
                if (t.name == "PLAYER") t.SetParent(null, true);
                else if (t.name.EndsWith("MirrorCam")) t.SetParent(mirrors != null ? mirrors.transform : null, true);
                else if (t.name.StartsWith("MWCoop-")) t.SetParent(null, true);
            }
            foreach (Material m in b.Mats.Values) if (m != null) Object.Destroy(m);
            foreach (Material m in b.Lit.Values) if (m != null) Object.Destroy(m);
            foreach (Material m in b.Dim.Values) if (m != null) { Object.Destroy(m.mainTexture); Object.Destroy(m); }
            Object.Destroy(b.Car);
            b.Car = null;
        }

        // ---------------------------------------------------------------- apercu (F10)
        // Copie visuelle seule (carrosserie, capot, portieres, volant, roues du rallye aux centres des roues), sur cette couche,
        // a l'origine ; matieres a liberer avec DestroyPreview.
        public static GameObject BuildPreview(int number, Color paint, int layer, out Built holder, Model carModel = null)
        {
            holder = null;
            GameObject sorbet = FindSorbet();
            cur = carModel ?? Vaz2108;
            if (sorbet == null || !cur.HasFiles) return null;
            Material basePaint = FindMat(sorbet, "paint") ?? FindMat(sorbet, "");
            var b = new Built { M = cur, Paint = paint };
            var root = new GameObject("MWCoop-VazApercu-" + number);
            b.Car = root;
            foreach (string f in new[] { "carrosserie.p205", "capot.p205", "portiere_g.p205", "portiere_d.p205", "volant.p205", "frein.p205", "hayon.p205", "trappe.p205", "bavette_ag.p205", "bavette_ad.p205", "bavette_vg.p205", "bavette_vd.p205", "antenne_1.p205", "antenne_2.p205" }) AddParts(b, root.transform, f, basePaint, paint, number);
            Dictionary<string, Vector3> inf = Info();
            // (My Summer Car : roue.p205 du modele, sinon les roues de la Ruscko -- pas de RALLYCAR3)
            List<Part> rp = Msc ? Load("roue.p205") : new List<Part>();
            GameObject rc = Msc ? (rp.Count > 0 ? null : sorbet) : RallyCar();
            if (rp.Count > 0)
            {
                Bounds wb = rp[0].Mesh.bounds;
                foreach (Part p in rp) wb.Encapsulate(p.Mesh.bounds);
                foreach (string wn in new[] { "wheel_lf", "wheel_rf", "wheel_lr", "wheel_rr" })
                {
                    Vector3 c;
                    if (!inf.TryGetValue(wn, out c)) continue;
                    Quaternion q = wn[6] == 'r' ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
                    var rw = new GameObject("roue" + wn).transform;
                    rw.SetParent(root.transform, false);
                    rw.localRotation = q; rw.localPosition = c - q * wb.center;
                    AddParts(b, rw, "roue.p205", basePaint, paint, number);
                }
            }
            if (rc != null)
                foreach (Wheel w in rc.GetComponentsInChildren<Wheel>(true))
                {
                    if (w.model == null) continue;
                    string wp = w.wheelPos.ToString();
                    string wn = "wheel_" + (wp.Contains("LEFT") ? "l" : "r") + (wp.StartsWith("FRONT") ? "f" : "r");
                    Vector3 c;
                    if (!inf.TryGetValue(wn, out c)) continue;
                    GameObject cp = CopyVisual(w.model, root.transform, "roue" + wp);
                    cp.transform.localPosition = c;
                    cp.transform.localRotation = Quaternion.Inverse(rc.transform.rotation) * w.model.transform.rotation;
                    float k = w.radius > 0.05f ? 0.29f / w.radius : 1f;   // (rayon des roues de la VAZ)
                    Vector3 ls = w.model.transform.lossyScale * k;
                    cp.transform.localScale = ls;
                }
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            holder = b;
            return root;
        }

        public static void DestroyPreview(Built b)
        {
            if (b == null) return;
            foreach (Material m in b.Mats.Values) if (m != null) Object.Destroy(m);
            foreach (Material m in b.Lit.Values) if (m != null) Object.Destroy(m);
            foreach (Material m in b.Dim.Values) if (m != null) { Object.Destroy(m.mainTexture); Object.Destroy(m); }
            if (b.Car != null) Object.Destroy(b.Car);
            b.Car = null;
        }
    }
}
