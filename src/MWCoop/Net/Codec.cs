using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace MWCoop.Net
{
    // Ecriture/lecture binaire des messages (petit-boutiste, textes UTF-8 prefixes par leur longueur).
    public class NetWriter
    {
        readonly MemoryStream ms = new MemoryStream(256);
        readonly BinaryWriter w;
        public NetWriter(Msg type) { w = new BinaryWriter(ms); w.Write((byte)type); }
        public NetWriter U8(int v) { w.Write((byte)v); return this; }
        public NetWriter U16(int v) { w.Write((ushort)v); return this; }
        public NetWriter I32(int v) { w.Write(v); return this; }
        public NetWriter F32(float v) { w.Write(v); return this; }
        public NetWriter F64(double v) { w.Write(v); return this; }
        public NetWriter Bool(bool v) { w.Write(v); return this; }
        public NetWriter Vec(Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); return this; }
        public NetWriter Quat(Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); return this; }
        public NetWriter Str(string s)
        {
            int id;
            if (s != null && s.Length > 0 && s[0] == '@' && Strings.Ids.TryGetValue(s, out id)) { w.Write((ushort)(Strings.Mark + id)); return this; }
            byte[] b = Encoding.UTF8.GetBytes(s ?? "");
            w.Write((ushort)b.Length); w.Write(b); return this;
        }
        public NetWriter Bytes(byte[] b, int off, int n) { w.Write((ushort)n); w.Write(b, off, n); return this; }
        public NetWriter Raw(byte[] b) { w.Write(b); return this; }
        public NetWriter Raw(byte[] b, int off, int n) { w.Write(b, off, n); return this; }
        public byte[] ToArray() { w.Flush(); return ms.ToArray(); }
        public int Length { get { return (int)ms.Length; } }
    }

    // Textes connus : les types de messages "@..." (Jobs.OnSpecial et les autres) partent sur 2 octets -- longueur
    // >= Mark = rang dans la liste (un message fait au plus 1150 octets : jamais une vraie longueur). Liste triee, la meme
    // des deux cotes (meme version du mod, Session.NetVersion). Un "@" absent de la liste part en entier, comme avant.
    public static class Strings
    {
        public const int Mark = 0xF000;
        public static readonly string[] List = { "@300d","@300f","@300k","@accident","@ajouts","@arrestation","@attelage","@bagages","@benne","@bout","@carbu","@cbx","@cbxx","@client","@colis","@coup","@cree","@don","@drone","@emit","@esine","@esineosto","@etape","@fend","@fendattel","@filmi","@flash","@fuite","@jack","@jfs","@kassi","@kovi","@mw2","@outil","@parti","@percute","@poste","@pousse","@pvp","@ram","@renverse","@sac","@sacitem","@sacosto","@sacs","@tas","@tasse","@taxicarte","@touche","@tow","@triche","@triches","@tuyau","@vente","@vet","@vin","@visagep","@vitre", "@vapeur", "@son", "@emitk", "@sonk" };
        public static readonly System.Collections.Generic.Dictionary<string, int> Ids = Build();
        static System.Collections.Generic.Dictionary<string, int> Build()
        {
            var d = new System.Collections.Generic.Dictionary<string, int>();
            for (int i = 0; i < List.Length; i++) d[List[i]] = i;
            return d;
        }
    }

    public class BadNumberException : Exception { public BadNumberException(string m) : base(m) { } }

    public class NetReader
    {
        readonly byte[] b;
        int p;
        readonly int end;
        public NetReader(byte[] data, int start, int len) { b = data; p = start; end = start + len; }
        public bool More { get { return p < end; } }
        // Copie de ce qui reste a lire (sans avancer).
        public byte[] Rest() { var r = new byte[end - p]; Buffer.BlockCopy(b, p, r, 0, r.Length); return r; }
        void Need(int n) { if (p + n > end) throw new EndOfStreamException("message tronque"); }
        public int U8() { Need(1); return b[p++]; }
        public int U16() { Need(2); int v = b[p] | b[p + 1] << 8; p += 2; return v; }
        public int I32() { Need(4); int v = BitConverter.ToInt32(b, p); p += 4; return v; }
        public float F32() { Need(4); float v = BitConverter.ToSingle(b, p); p += 4; return v; }
        public double F64() { Need(8); double v = BitConverter.ToDouble(b, p); p += 8; return v; }
        public bool Bool() { return U8() != 0; }
        // Positions et rotations recues : jamais de NaN / infini / valeur folle appliques (revue du 14/10 : seul VehicleSync
        // verifiait ; Props, Traffic, TowCart, Garage, SleepingBags, Kauppa posaient tel quel -- une pose NaN fait partir la
        // physique en valeurs infinies et fige le jeu). Le message entier est alors ignore (Session.Message).
        public Vector3 Vec()
        {
            var v = new Vector3(F32(), F32(), F32());
            if (!Ok(v.x, 1e6f) || !Ok(v.y, 1e6f) || !Ok(v.z, 1e6f)) throw new BadNumberException("vecteur " + v);
            return v;
        }
        public Quaternion Quat()
        {
            var q = new Quaternion(F32(), F32(), F32(), F32());
            if (!Ok(q.x, 2f) || !Ok(q.y, 2f) || !Ok(q.z, 2f) || !Ok(q.w, 2f)) throw new BadNumberException("rotation " + q);
            return q;
        }
        static bool Ok(float f, float max) { return !float.IsNaN(f) && f <= max && f >= -max; }
        public string Str()
        {
            int n = U16();
            if (n >= Strings.Mark) { int id = n - Strings.Mark; if (id >= Strings.List.Length) throw new Exception("texte connu inconnu " + id); return Strings.List[id]; }
            Need(n); string s = Encoding.UTF8.GetString(b, p, n); p += n; return s;
        }
        public byte[] Bytes() { int n = U16(); Need(n); var r = new byte[n]; Buffer.BlockCopy(b, p, r, 0, n); p += n; return r; }
    }

    // Types de messages. Ajouter a la fin ; changer le sens d'un message = monter Session.NetVersion.
    public enum Msg : byte
    {
        Hello = 1,        // invite -> hote : pseudo, apparence, version du mod
        Roster = 2,       // hote -> tous : liste des joueurs
        PlayerState = 3,  // non fiable : position, regard, posture d'un joueur
        Chat = 4,
        World = 5,        // hote -> invites : heure, jour, meteo
        SaveBegin = 6,    // hote -> invite : debut d'envoi de la sauvegarde
        SaveChunk = 7,
        SaveEnd = 8,
        StartGame = 9,    // (reserve)
        Profile = 10,     // un joueur change de pseudo ou d'apparence
        Interact = 11,    // porte, interrupteur... actionne par un joueur
        Vehicle = 12,     // non fiable : position et vitesses d'une voiture
        Part = 13,        // piece montee ou demontee
        Bolt = 14,        // vis serree ou desserree d'un cran
        Prop = 15,        // non fiable : piece libre deplacee (tenue, lachee, au repos)
        Income = 16,      // argent gagne par un joueur, recu par tous
        Purchase = 17,    // achat paye a une caisse (contenu du panier)
        Paint = 18,       // piece repeinte (bombe) ou couleur tiree au hasard chez l'hote
        Setting = 19,     // reglage d'une piece (carburateur, repartiteur...)
        Traffic = 20,     // non fiable : lot de vehicules de la circulation et de passants (hote)
        Job = 21,         // boulot : changement d'etat d'un automate de progression
        Fluid = 22,       // liquides et usure : lot de (cle, valeur) changees
        Consume = 23,     // objet mange, bu ou jete (ID) : il disparait chez tous
        Voice = 24,       // replique d'un PNJ ou juron d'un joueur (groupe/variante MasterAudio)
        WorldFsm = 28,    // action d'un joueur sur un automate du monde (facture payee, cuisiniere...)
        WorldVars = 29,   // hote -> invites : variables des automates sauvegardes et globales House*
        Seat = 27,        // joueur assis a une place passager (voiture, place) ou sorti
        CarVisual = 26,   // tableau de bord, voyants, vitres, leviers... : lot de (cle, drapeaux, pose)
        CarDoor = 25,     // portiere / capot / coffre : fermee (0), ouverte (1), angle (2), saisie (3), verrouillee (4) ; ordre de l'hote
        Machine = 30,     // non fiable : ce qu'on voit d'une machine a ecran (pompe, poker, machine a sous) chez celui qui s'en sert
        Stock = 31,       // magasin : panier d'un joueur et stock d'un produit qui ont change
        Npc = 32,         // non fiable, hote -> invites : PNJ proches des invites (pose, clip, objets tenus)
        Parked = 33,      // hote -> invites : voitures garees du decor (parent, pose, visible)
        Audit = 34,       // audit de la synchro : hote -> invites « SNAP n », invites -> hote leurs etats changes (cle hachee, etat)
        MachineLock = 35, // verrou d'une machine a jeu (poker, machine a sous), arbitre par l'hote : libre, demande, tenue, refus ; jeu lance sur l'ordinateur
        Frost = 36,       // givre et buee des vitres, grattage du pare-brise (a remplir : lot 2)
        Tow = 37,         // corde de remorquage entre deux vehicules (a remplir : lot 2)
        Call = 38,        // appels sortants (telephone fixe, telephone du taxi) (a remplir : lot 2)
        Wear = 39,        // vetements portes (veste, combinaison, casque) (a remplir : lot 2)
        Cook = 40,        // cuisine : saucisses et viande grillees, cafe, peremption (a remplir : lot 4)
        Fire = 41,        // feux : bois dans les cheminees, poeles, sauna, grill ; incendies (a remplir : lot 4)
        Garage = 42,      // atelier : crics, pont, palan, degats d'accident, chargeur du tracteur (a remplir : lot 4)
        Home = 43,        // maison : fusibles et electricite, kilju (a remplir : lot 4)
        Gesture = 44,     // gestes du joueur : coup, doigt, pouce, pipi, montre, pencher, ivresse, assis (a remplir : lot 4)
        Thrown = 45,      // bouteille vide jetee par un joueur qui a bu (etat de l'automate Drink, pose, vitesse)
        Payout = 46,      // enveloppe de paie d'un boulot prise par un joueur : retiree chez les autres
        Race = 47,        // courses : voitures IA (hote), table des resultats du rallye, empreinte de la liste
        PushDoor = 48,    // porte poussee hors vehicule (garage) : angle de celui qui pousse
        Summon = 49,      // hote -> un invite : viens a moi (position, cap) ; menu F10 ou lanceur (Admin)
        Radio = 50,       // hote -> invites : morceau, position et lecture de chaque canal radio (Radio)
        ModData = 51,     // donnees d'un autre mod (CoopApi) : canal, morceaux ; l'hote relaie
        Event = 53,       // evenement du monde decide par l'hote : objets allumes, valeurs reglees (Events)
        Talk = 54,        // vocal de proximite : paquet de voix compresse par Steam (Voice) ; l'hote relaie
        Knob = 52,        // commande tenue d'un vehicule (starter, frein a main) : valeur de celui qui la tient (Knobs)
    }
}
