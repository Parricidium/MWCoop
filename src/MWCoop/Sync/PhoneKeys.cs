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
        // Combine (Phone/Handle, que le jeu eteint chez celui qui le tient) : son etat part aux autres (@combine, a chaque
        // changement et toutes les 5 s) ; chez eux, le combine du socle est cache et une copie est posee a l'oreille de son
        // avatar (Menor, 18/10 : « quand l'ami telephone, le combine ne disparait pas »).
        static readonly bool[] heldSent = new bool[3];
        static readonly float[] resendAt = new float[3], holdUntil = new float[3];
        static readonly int[] holder = { -1, -1, -1 };
        static readonly GameObject[] ear = new GameObject[3];
        static float findAt;
        static int logs;

        public static void OnLevelLoaded() { for (int i = 0; i < 3; i++) { pads[i] = null; hls[i] = null; lastKey[i] = null; lastOn[i] = false; shown[i] = null; heldSent[i] = false; resendAt[i] = holdUntil[i] = 0f; holder[i] = -1; ear[i] = null; } findAt = Time.realtimeSinceStartup + 15f; }

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
                if (pads[i] == null) continue;
                bool held = pads[i].activeSelf;   // (clavier allume ici = combine en main ici)
                if (held != heldSent[i] || held && now >= resendAt[i])
                {
                    heldSent[i] = held; resendAt[i] = now + 5f;
                    Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@combine").U8(i).Bool(held), true);
                    if (logs++ < 12) Log.Info("telephone : combine " + (held ? "pris" : "repose") + " ici (" + pads[i].transform.parent.name + "), envoye");
                }
                HeldTick(i, now);
                if (!held) { lastOn[i] = false; continue; }
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

        static Transform Handle(int i) { return pads[i] != null && pads[i].transform.parent != null ? pads[i].transform.parent.Find("Handle") : null; }

        public static void OnHandset(int who, NetReader r)
        {
            int i = r.U8();
            bool held = r.Bool();
            if (i < 0 || i >= 3) return;
            if (pads[i] == null) pads[i] = Game.FindAny(Pads[i]);
            if (pads[i] == null) return;
            if (held) { holder[i] = who; holdUntil[i] = Time.realtimeSinceStartup + 12f; }
            else if (holder[i] == who) holder[i] = -1;
            if (logs++ < 12) Log.Info("telephone : combine " + (held ? "pris" : "repose") + " par #" + who + " (" + pads[i].transform.parent.name + ")");
        }

        // Chaque image : combine du socle cache tant qu'un autre le tient (pas si c'est nous : le jeu s'en charge), copie a
        // son oreille ; rendu quand il le repose, quand il part, ou sans nouvelles depuis 12 s.
        static void HeldTick(int i, float now)
        {
            Transform handle = Handle(i);
            if (handle == null) return;
            int who = holder[i];
            Transform head = who >= 0 ? PlayerSync.HeadOf(who) : null, root = who >= 0 ? PlayerSync.RootOf(who) : null;
            bool remote = who >= 0 && now < holdUntil[i] && !pads[i].activeSelf && head != null && root != null && Session.Players.ContainsKey(who);
            if (!remote)
            {
                if (ear[i] != null) { Object.Destroy(ear[i]); ear[i] = null; if (!pads[i].activeSelf && !handle.gameObject.activeSelf) handle.gameObject.SetActive(true); if (logs++ < 12) Log.Info("telephone : combine de " + pads[i].transform.parent.name + " rendu au socle"); }
                if (who >= 0 && now >= holdUntil[i]) holder[i] = -1;
                return;
            }
            if (ear[i] == null)
            {
                var c = (GameObject)Object.Instantiate(handle.gameObject);
                foreach (PlayMakerFSM f in c.GetComponentsInChildren<PlayMakerFSM>(true)) Object.Destroy(f);
                foreach (Collider col in c.GetComponentsInChildren<Collider>(true)) Object.Destroy(col);
                foreach (Rigidbody rb in c.GetComponentsInChildren<Rigidbody>(true)) Object.Destroy(rb);
                c.name = "MWCoop combine";
                c.transform.SetParent(null, true);
                c.transform.localScale = handle.lossyScale;
                c.SetActive(true);
                ear[i] = c;
                if (handle.gameObject.activeSelf) handle.gameObject.SetActive(false);
                if (logs++ < 12) Log.Info("telephone : combine de " + pads[i].transform.parent.name + " cache sur le socle, copie a l'oreille de #" + who);
            }
            // A l'oreille droite, ecouteur en haut, le long de la joue ([Test] CombineX/Y/Z en cm, CombineInclinaison en degres).
            Vector3 at = head.position + root.right * Config.GetFloat("Test", "CombineX", 9f) * 0.01f + root.forward * Config.GetFloat("Test", "CombineY", 3f) * 0.01f + root.up * Config.GetFloat("Test", "CombineZ", -2f) * 0.01f;
            ear[i].transform.position = at;
            ear[i].transform.rotation = root.rotation * Quaternion.Euler(Config.GetFloat("Test", "CombineInclinaison", -60f), 0f, 90f);
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
