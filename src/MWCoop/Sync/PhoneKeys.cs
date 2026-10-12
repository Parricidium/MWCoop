using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Touches du telephone vues par les autres (Menor, 12/10 : « le bouton vert quand l'ami tape sur le telephone ne s'affiche
    // pas »). Le clavier (KeypadPhone1/2, allume seulement chez celui qui tient le combine) : 'Input' (AnyKeyStoreString) pose
    // son Highlight vert sur la touche tapee (enfant 0..9) et 'Close' l'eteint 0,3 s plus tard. Composer reste a celui qui
    // appelle (Calls) ; ici seulement la touche : chez lui, chaque Highlight qui s'allume (ou change de touche) part
    // (@touche) ; chez les autres, dont le clavier est eteint (l'allumer lirait LEUR clavier), une copie du Highlight est
    // posee 0,3 s sur la meme touche.
    public static class PhoneKeys
    {
        static readonly string[] Pads = {
            "HOMENEW/Functions/FunctionsDisable/Telephone/Phone/KeypadPhone2",
            "YARD/Building/LIVINGROOM/Telephone 1/Phone/KeypadPhone1",
            "JOBS/TAXIJOB/MACHTWAGEN/TaxiFunctions/Carphone/KeypadPhone2" };
        static readonly GameObject[] pads = new GameObject[3];
        static readonly Transform[] lastKey = new Transform[3];
        static readonly bool[] lastOn = new bool[3];
        static readonly GameObject[] shown = new GameObject[3];
        static float findAt;
        static int logs;

        public static void OnLevelLoaded() { for (int i = 0; i < 3; i++) { pads[i] = null; hls[i] = null; lastKey[i] = null; lastOn[i] = false; shown[i] = null; } findAt = Time.realtimeSinceStartup + 15f; }

        static readonly Transform[] hls = new Transform[3];
        // Le Highlight du clavier : enfant direct au depart, puis range sous la derniere touche tapee (SetParent du jeu).
        static Transform Highlight(int i)
        {
            if (pads[i] == null) return null;
            if (hls[i] != null) return hls[i];
            Transform h = pads[i].transform.Find("Highlight");
            if (h == null) foreach (Transform k in pads[i].transform) { h = k.Find("Highlight"); if (h != null) break; }
            return hls[i] = h;
        }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (now >= findAt) { findAt = now + 20f; for (int i = 0; i < 3; i++) if (pads[i] == null) pads[i] = Game.FindAny(Pads[i]); }
            for (int i = 0; i < 3; i++)
            {
                if (pads[i] == null || !pads[i].activeSelf) { lastOn[i] = false; continue; }   // (clavier en main ici seulement : allume quand on tient le combine)
                Transform h = Highlight(i);
                bool on = h != null && h.gameObject.activeSelf && h.parent != null && h.parent != pads[i].transform;
                if (on && (!lastOn[i] || h.parent != lastKey[i]))
                {
                    Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@touche").U8(i).Str(h.parent.name), true);
                    if (logs++ < 6) Log.Info("telephone : touche " + h.parent.name + " envoyee (" + pads[i].transform.parent.parent.name + ")");
                }
                lastOn[i] = on; lastKey[i] = on ? h.parent : null;
            }
        }

        public static void OnMessage(int who, NetReader r)
        {
            int i = r.U8();
            string key = r.Str();
            if (i < 0 || i >= 3) return;
            if (pads[i] == null) pads[i] = Game.FindAny(Pads[i]);
            if (pads[i] == null || pads[i].activeInHierarchy) return;   // (clavier en main ici : le notre)
            Transform h = Highlight(i), k = pads[i].transform.Find(key);
            if (h == null || k == null) return;
            if (shown[i] != null) Object.Destroy(shown[i]);
            var c = (GameObject)Object.Instantiate(h.gameObject);
            foreach (PlayMakerFSM f in c.GetComponentsInChildren<PlayMakerFSM>(true)) Object.Destroy(f);
            c.name = "MWCoop touche";
            c.transform.SetParent(pads[i].transform.parent, false);   // (le clavier eteint ne montre rien : a cote, meme pose)
            c.transform.position = k.position; c.transform.rotation = k.rotation; Vector3 ws = h.lossyScale, ps = c.transform.parent.lossyScale; c.transform.localScale = new Vector3(ws.x / ps.x, ws.y / ps.y, ws.z / ps.z);   // (meme taille a l'ecran)
            c.SetActive(true);
            Object.Destroy(c, 0.3f);
            shown[i] = c;
            if (logs++ < 6) Log.Info("telephone : touche " + key + " de #" + who + " montree");
        }

        // [Test] Autotest=touche (2 joueurs) : a 40 s l'invite allume le clavier de l'appartement et sa touche 5 (comme 'Input'),
        // l'eteint a 42 s ; l'hote doit noter « touche 5 de #1 montree ».
        static int tStep;
        public static void Test(string mode, float t)
        {
            if (mode != "touche" || Session.IsHost) return;
            if (pads[0] == null) pads[0] = Game.FindAny(Pads[0]);
            if (pads[0] == null) return;
            if (tStep == 0 && t > 40f)
            {
                tStep = 1;
                pads[0].SetActive(true);
                foreach (PlayMakerFSM f in pads[0].GetComponents<PlayMakerFSM>()) f.enabled = false;   // (pas de lecture du clavier)
                Transform h = Highlight(0), k = pads[0].transform.Find("5");
                if (h != null && k != null) { h.SetParent(k, false); h.localPosition = Vector3.zero; h.localRotation = Quaternion.identity; h.gameObject.SetActive(true); }
                Log.Info("autotest : touche, 5 allumee ici (" + (h != null && k != null) + ")");
            }
            if (tStep == 1 && t > 42f) { tStep = 2; pads[0].SetActive(false); }
        }
    }
}
