using MWCoop.Net;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;

namespace MWCoop
{
    // Boissons vues chez les autres (retour de JD du 05/10 : « on voit l'animation de la biere, mais pas la bouteille
    // dans la main, ni quand il la jette ») :
    //  - ce que le joueur a en main pendant qu'il boit : l'objet allume sous PLAYER/.../FPSCamera/Drink/Hand
    //    (BeerBottle, Milk, SodaPSK, Coffee... ou une main HandJuice, HandMilk...), envoye avec l'etat du joueur
    //    (PlayerState.Drink, rang dans Names) ; l'avatar en tient une copie dans la main droite (Avatar.PlaceDrink) ;
    //  - la bouteille vide jetee : l'automate Drink du jeu la cree (etats « Throw ... » : CreateObject) chez celui
    //    qui boit ; il envoie l'etat, la pose et la vitesse (Msg.Thrown) et les autres creent le meme objet, depuis le
    //    meme etat de LEUR automate Drink, au meme endroit et lance pareil. C'est une copie pour la vue (ses automates
    //    retires), qui reste ou elle tombe ; elle disparait au bout de 10 minutes.
    public static class Drinks
    {
        public static readonly string[] Names = {
            "", "BeerBottle", "BoozeBottle", "SpiritBottle", "Milk", "MilkGlass", "SodaPSK", "EnergyDrink", "ShotGlass",
            "Coffee", "CoffeePaper", "CoffeeGranny", "HandJuice", "HandMilk", "HandCoffeeHome" };

        static Transform hand;          // PLAYER/.../FPSCamera/Drink/Hand (local)
        static PlayMakerFSM drinkFsm;   // PLAYER/.../FPSCamera/Drink :: Drink
        static float sendAt = -1;
        static GameObject thrown;
        static string thrownState;
        static int sent;

        public static void OnLevelLoaded() { hand = null; drinkFsm = null; sendAt = -1; thrown = null; }

        static bool Find()
        {
            if (hand != null && drinkFsm != null) return true;
            GameObject pl = GameObject.Find("PLAYER");
            Transform d = pl != null ? pl.transform.Find("Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Drink") : null;
            if (d == null) return false;
            hand = d.Find("Hand");
            drinkFsm = Game.FsmOn(d.gameObject, "Drink");
            // Etats « Throw ... » : une action en fin d'etat note l'objet cree (l'etat ne dure pas une image).
            if (drinkFsm != null)
            {
                int n = 0;
                foreach (FsmState st in drinkFsm.Fsm.States)
                {
                    if (!st.Name.StartsWith("Throw") || Prefab(drinkFsm, st.Name) == null) continue;
                    var list = new System.Collections.Generic.List<FsmStateAction>(st.Actions);
                    list.Add(new ThrowEnd { F = drinkFsm });
                    st.Actions = list.ToArray();
                    n++;
                }
                Log.Info("boissons : " + n + " etats de lancer accroches (automate Drink du joueur)");
            }
            return hand != null;
        }

        // Rang (Names) de l'objet en main du joueur local ; 0 : rien de connu.
        public static int LocalIndex()
        {
            if (!Find() || !hand.gameObject.activeInHierarchy) return 0;
            for (int i = 1; i < Names.Length; i++)
            {
                Transform c = hand.Find(Names[i]);
                if (c != null && c.gameObject.activeSelf) return i;
            }
            return 0;
        }

        class ThrowEnd : FsmStateAction
        {
            public PlayMakerFSM F;
            public override void OnEnter()
            {
                try
                {
                    GameObject made = Created(F, State.Name) ?? Nearest(Prefab(F, State.Name));
                    if (made != null) { thrown = made; thrownState = State.Name; sendAt = Time.realtimeSinceStartup + 0.15f; }
                    else MWCoop.Log.Warn("boissons : objet jete (" + State.Name + ") introuvable");
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        // L'objet cree le plus pres de la camera (CreateObject sans variable de sortie).
        static GameObject Nearest(GameObject prefab)
        {
            if (prefab == null || Camera.main == null) return null;
            string n = prefab.name + "(Clone)";
            GameObject best = null; float bd = 9f;
            foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>())
            {
                if (rb.name != n) continue;
                float d = (rb.position - Camera.main.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = rb.gameObject; }
            }
            return best;
        }

        // Chaque image (joueur local) : envoi de la bouteille jetee notee par ThrowEnd.
        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame || !Find() || drinkFsm == null) return;
            // Un instant apres sa creation : lancee par le jeu, sa vitesse est connue.
            if (sendAt > 0 && Time.realtimeSinceStartup >= sendAt)
            {
                sendAt = -1;
                if (thrown == null) return;
                Rigidbody rb = thrown.GetComponent<Rigidbody>();
                Vector3 v = rb != null ? rb.velocity : Vector3.zero, av = rb != null ? rb.angularVelocity : Vector3.zero;
                Session.SendAll(new NetWriter(Msg.Thrown).U8(Session.LocalId).Str(thrownState).Vec(thrown.transform.position).Quat(thrown.transform.rotation).Vec(v).Vec(av), true);
                if (sent++ < 20) Log.Info("boissons : " + thrown.name + " jetee (" + thrownState + "), envoyee");
                thrown = null;
            }
        }

        // L'objet que la derniere action CreateObject de cet etat vient de creer (sa variable storeObject).
        static GameObject Created(PlayMakerFSM f, string state)
        {
            FsmState s = f.Fsm.GetState(state);
            if (s == null) return null;
            foreach (FsmStateAction a in s.Actions)
            {
                var c = a as CreateObject;
                if (c != null && c.storeObject != null && c.storeObject.Value != null) return c.storeObject.Value;
            }
            return null;
        }

        static GameObject Prefab(PlayMakerFSM f, string state)
        {
            FsmState s = f != null ? f.Fsm.GetState(state) : null;
            if (s == null) return null;
            foreach (FsmStateAction a in s.Actions)
            {
                var c = a as CreateObject;
                if (c != null && c.gameObject != null && c.gameObject.Value != null) return c.gameObject.Value;
            }
            return null;
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string state = r.Str();
            Vector3 pos = r.Vec(); Quaternion rot = r.Quat(); Vector3 v = r.Vec(), av = r.Vec();
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Thrown).U8(who).Str(state).Vec(pos).Quat(rot).Vec(v).Vec(av), true, who);
            if (!PlayerSync.InGame) return;
            Find();
            GameObject prefab = Prefab(drinkFsm, state);
            if (prefab == null) { Log.Warn("boissons : objet jete (" + state + ") introuvable ici"); return; }
            GameObject go = (GameObject)Object.Instantiate(prefab, pos, rot);
            go.name = "MWCoop-jete-" + prefab.name;
            foreach (PlayMakerFSM pf in go.GetComponentsInChildren<PlayMakerFSM>(true)) Object.Destroy(pf);
            go.SetActive(true);
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null) { rb.isKinematic = false; rb.velocity = v; rb.angularVelocity = av; }
            Object.Destroy(go, 600f);
            Log.Info("boissons : " + prefab.name + " jetee par #" + who + " recreee ici");
        }

        // Copie (pour l'avatar d'un autre) de l'objet en main de rang i, sans main animee ni logique ; hauteur ~h m.
        // Essais : ce que contient la main du joueur local (objets de boisson).
        public static string HandChildren()
        {
            if (!Find()) return "main du joueur introuvable";
            var l = new System.Collections.Generic.List<string>();
            foreach (Transform c in hand) l.Add(c.name);
            return string.Join(", ", l.ToArray());
        }

        public static GameObject Model(int i, Transform parent)
        {
            if (i <= 0 || i >= Names.Length || !Find()) return null;
            Transform src = hand.Find(Names[i]);
            if (src == null) return null;
            GameObject go = (GameObject)Object.Instantiate(src.gameObject);
            go.name = "MWCoop-Boisson-" + Names[i];
            // (DestroyImmediate : l'avatar mesure la bouteille tout de suite ; avec Destroy, la main animee comptait encore dans sa taille
            // et son axe -- bouteille trop petite, decalee ou de travers a la bouche, retour d'un joueur 08/10.)
            foreach (SkinnedMeshRenderer s in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (s != null) Object.DestroyImmediate(s.gameObject == go ? (Object)s : s.gameObject);
            foreach (Animation a in go.GetComponentsInChildren<Animation>(true)) Object.Destroy(a);
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            foreach (PlayMakerFSM f in go.GetComponentsInChildren<PlayMakerFSM>(true)) Object.Destroy(f);
            go.transform.parent = parent;
            go.transform.localScale = src.lossyScale;
            go.SetActive(true);
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) { t.gameObject.SetActive(true); t.gameObject.layer = parent.gameObject.layer; }
            return go;
        }
    }
}
