using UnityEngine;

namespace MWCoop
{
    // Quete de la boutique s21, cote boutique (JD 31/10) : un client devant la vitrine des consoles (pas un vendeur : il regarde les
    // jeux), a l'apparence de JD (sa tenue choisie dans le mod), prete sa voiture. Clic (« TALK ») : il se tourne vers le joueur et
    // parle (sous-titres, en anglais), tend la cle (copie de la cle de la voiture de la quete, dans sa main) -- tous les joueurs la
    // recoivent (QueteGolf.GiveKey) --, puis « elle est garee chez Teimo ». Quete deja faite : seulement la derniere phrase.
    // Sans quete ici (QueteGolf.On faux) : pas de client.
    public static partial class Kauppa
    {
        static Avatar asiakas;
        static Collider asiakasCol;
        static Quaternion asiakasRest;
        static float talkT = -1f, talkYaw;
        static bool talkGive, keyGiven;
        static int talkStep;
        static GameObject keyHand;
        // (tenue de JD dans le mod, MSC, 31/10 : haut et visage offerts -- pack de Dom, telecharge par le lanceur chez tous --, pantalon et
        // lunettes du jeu ; sans le beret, comme sur sa capture)
        const string JdLook = "dom_haut_28|char_pants08|dom_visage_44|||eye_glasses@CHROME";
        const string Who = "s21: ";
        static readonly string[] Lines1 = {
            "Hey, hi! I heard you're trying to rebuild your car?...",
            "I really like this project, so to help you out, I'm lending you my car.",
            "It was my dad's, so if you wreck it, I'll cut your balls off." };
        const string Line2Teimo = "It's parked at Teimo's, take good care of it!";
        const string Line2Village = "It's parked right next door, take good care of it!";
        static string Line2 { get { return QueteGolf.ParkedInVillage ? Line2Village : Line2Teimo; } }

        static void ResetAsiakas() { asiakas = null; asiakasCol = null; talkT = -1f; keyGiven = false; if (keyHand != null) Object.Destroy(keyHand); keyHand = null; }

        // (boutique construite ; appele par Build quand la quete est possible ici)
        static void Asiakas()
        {
            if (konsoliCab == null) return;
            asiakas = Avatar.CreatePreview("OSUUSKAUPPA-asiakas", 0);
            if (asiakas == null) { L("client : corps de PNJ indisponible"); return; }
            asiakas.PreviewSkin(JdLook);
            Transform r = asiakas.Root.transform;
            r.SetParent(root.transform, false);
            // devant la vitrine des consoles (mur gauche, face interieure -XIn, 0,5 m de profondeur), tourne vers elle
            r.localPosition = new Vector3(-XIn + 0.5f + 0.5f, 0f, konsoliCab.Mid);
            r.localRotation = Quaternion.Euler(0f, -90f, 0f);
            asiakasRest = r.localRotation;
            var cg = new GameObject("osuus_asiakas_coll");
            cg.transform.SetParent(r, false);
            cg.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            var cc = cg.AddComponent<CapsuleCollider>(); cc.height = 1.8f; cc.radius = 0.28f;
            asiakasCol = cc;
            L("client devant les consoles (quete s21)");
        }

        static void StartTalk()
        {
            talkT = Time.realtimeSinceStartup;
            talkGive = !QueteGolf.Done;
            keyGiven = false; talkStep = 0;
            Transform pl = Game.PlayerT;
            if (pl != null && asiakas != null)
            {
                Vector3 d = asiakas.Root.transform.parent.InverseTransformDirection(pl.position - asiakas.Root.transform.position); d.y = 0f;
                talkYaw = d.sqrMagnitude > 1e-4f ? Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg : asiakasRest.eulerAngles.y;
            }
            if (talkGive) SayFor(Who + Lines1[0], 3.2f); else SayFor(Who + Line2, 4f);
            L("client : on lui parle" + (talkGive ? " (cle a donner)" : " (quete faite)"));
        }

        // dialogue : 0 s phrase 1, 3,2 s phrase 2, 6,8 s phrase 3 et la cle tendue (7,4 a 9,6 s, donnee a 8,4 s), 10 s « garee chez Teimo »
        static void TickAsiakas(float now)
        {
            if (asiakas == null || asiakas.Root == null) return;
            Transform r = asiakas.Root.transform;
            if (talkT < 0f) return;
            float e = now - talkT, end = talkGive ? 14f : 4.5f;
            // se tourne vers le joueur, puis vers les consoles a la fin
            Quaternion face = Quaternion.Euler(0f, talkYaw, 0f);
            r.localRotation = Quaternion.RotateTowards(r.localRotation, e < end - 0.8f ? face : asiakasRest, 240f * Time.deltaTime);
            if (talkGive)
            {
                if (talkStep == 0 && e >= 3.2f) { talkStep = 1; SayFor(Who + Lines1[1], 3.6f); }
                if (talkStep == 1 && e >= 6.8f) { talkStep = 2; SayFor(Who + Lines1[2], 3.4f); }
                float w = e < 7.4f ? 0f : e < 7.8f ? (e - 7.4f) / 0.4f : e < 9.2f ? 1f : e < 9.6f ? 1f - (e - 9.2f) / 0.4f : 0f;
                Transform pl = Game.MainCam != null ? Game.MainCam.transform : Game.PlayerT;
                Vector3 at = pl != null ? pl.position - Vector3.up * 0.45f : r.position + r.forward * 0.6f + Vector3.up * 1.1f;
                Vector3 sh = r.position + Vector3.up * 1.35f;   // (vers le joueur depuis l'epaule, a longueur de bras)
                at = sh + (at - sh).normalized * 0.62f;
                if (w > 0f) asiakas.PoseGive(at, w * w * (3f - 2f * w)); else if (talkStep == 2 && e > 9.6f) { talkStep = 3; asiakas.PoseGive(at, 0f); }
                // la cle dans sa main, du bras tendu a la remise
                if (e >= 7.6f && e < 8.4f && keyHand == null) KeyInHand();
                if (e >= 8.4f && !keyGiven) { keyGiven = true; if (keyHand != null) { Object.Destroy(keyHand); keyHand = null; } QueteGolf.GiveKey(); }
                if (talkStep == 3 && e >= 10f) { talkStep = 4; SayFor(Who + Line2, 4f); }
            }
            if (e >= end) { talkT = -1f; r.localRotation = asiakasRest; asiakas.PoseGive(r.position, 0f); }
        }

        static void KeyInHand()
        {
            Transform hand = asiakas.HandRight;
            GameObject k = StandaloneCar.QuestKeyCopy();
            if (hand == null || k == null) { L("client : pas de cle a tendre (" + (hand == null ? "main" : "cle de la voiture") + " introuvable)"); if (k != null) Object.Destroy(k); return; }
            keyHand = k;
            k.transform.SetParent(hand, false);
            k.transform.localPosition = new Vector3(0f, 0f, 0f);
            k.transform.localRotation = Quaternion.identity; k.transform.localScale *= 1.8f;   // (cle de 3 cm : visible dans la main)
            foreach (Collider c in k.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            foreach (Transform t in k.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = asiakas.Root.layer;
            Bounds kb = new Bounds(); bool any = false;
            foreach (Renderer rr in k.GetComponentsInChildren<Renderer>(true)) { if (!any) { kb = rr.bounds; any = true; } else kb.Encapsulate(rr.bounds); }
            L("client : cle dans la main, " + (any ? "taille " + kb.size.ToString("F3") + " a " + (kb.center - hand.position).magnitude.ToString("F2") + " m de la main" : "sans rendu"));
        }

        // Essais (QueteGolf) : le joueur d'ici pose devant le client, a 1,2 m dans l'allee, et lui parle.
        public static string TestTalk()
        {
            if (asiakas == null || asiakas.Root == null) return "pas de client";
            Transform pl = Game.PlayerT, r = asiakas.Root.transform;
            if (pl == null) return "pas de joueur";
            var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
            pl.position = r.parent.TransformPoint(r.localPosition + new Vector3(0.1f, 0.1f, 1.1f));   // (dans l'allee, entre la vitrine et la gondole)
            if (cc != null) cc.enabled = true;
            StartTalk();
            return "on parle au client (" + r.position.ToString("F1") + "), joueur en " + pl.position.ToString("F1");
        }
        public static bool TestTalkView(out Vector3 eye, out Vector3 head)
        {
            eye = head = Vector3.zero;
            if (asiakas == null || asiakas.Root == null) return false;
            Transform r = asiakas.Root.transform;
            head = r.position + Vector3.up * 1.55f;
            Transform hb = asiakas.HeadBone; var sb = new System.Text.StringBuilder();
            if (hb != null) foreach (Transform c in hb) sb.Append(c.name).Append(c.gameObject.activeInHierarchy ? "" : "(eteint)").Append(' ');
            L("client : sur la tete : " + sb);
            eye = r.parent.TransformPoint(r.localPosition + new Vector3(0.25f, 1.6f, 1.2f));
            return true;
        }

        // sous-titre tenu 'secs' secondes (Say : 3,5 s)
        static void SayFor(string en, float secs)
        {
            Say(en, en);
            subUntil = Time.realtimeSinceStartup + secs;
        }
    }
}
