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
            byte[] b = Encoding.UTF8.GetBytes(s ?? "");
            w.Write((ushort)b.Length); w.Write(b); return this;
        }
        public NetWriter Bytes(byte[] b, int off, int n) { w.Write((ushort)n); w.Write(b, off, n); return this; }
        public NetWriter Raw(byte[] b) { w.Write(b); return this; }
        public NetWriter Raw(byte[] b, int off, int n) { w.Write(b, off, n); return this; }
        public byte[] ToArray() { w.Flush(); return ms.ToArray(); }
        public int Length { get { return (int)ms.Length; } }
    }

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
        public Vector3 Vec() { return new Vector3(F32(), F32(), F32()); }
        public Quaternion Quat() { return new Quaternion(F32(), F32(), F32(), F32()); }
        public string Str() { int n = U16(); Need(n); string s = Encoding.UTF8.GetString(b, p, n); p += n; return s; }
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
        CarDoor = 25,     // portiere / capot / coffre : ouverte (1), fermee (0), angle en temps reel (2)
    }
}
