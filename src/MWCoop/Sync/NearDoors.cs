using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Portes automatiques et barrieres du monde (portes coulissantes du magasin, barriere du market...) :
    // leur automate s'ouvre quand LE joueur local approche (GetDistance vers PLAYER). Chez chacun, on leur
    // fait mesurer la distance au joueur le plus proche -- local, ou avatar d'un autre -- : elles
    // s'ouvrent et se referment pour tout le monde, partout, sans aucun message.
    // Concerne : les automates qui ont un etat « Open... » et une action GetDistance visant le joueur.
    // Et tous ceux des boulots (racine JOBS ; demande de RullER, 11/10 : « un seul joueur pour lancer un boulot ») : leur
    // site s'allume (LOD), l'usine reste ouverte (« Wait player » : ferme au-dela de 200 m), les PNJ attendent... d'apres
    // la distance au joueur local -- l'hote loin, le boulot qu'un invite prenait a l'autre bout de la carte s'eteignait
    // chez lui. Ils visent maintenant le joueur le plus proche, chez chacun (le regard des PNJ vers la camera reste local).
    public static class NearDoors
    {
        class Probe { public Transform Ref, Proxy; }
        static readonly List<Probe> probes = new List<Probe>();
        static readonly HashSet<PlayMakerFSM> seen = new HashSet<PlayMakerFSM>();
        static float nextScan = -1;
        static Transform player;
        static int jobProbes;

        public static void OnLevelLoaded()
        {
            foreach (Probe p in probes) if (p.Proxy != null) Object.Destroy(p.Proxy.gameObject);
            probes.Clear(); seen.Clear(); player = null; jobProbes = 0;
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 15f : -1;
        }

        public static void Update()
        {
            if (nextScan < 0 || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = now + 90f; Scan(); }
            if (probes.Count == 0) return;
            if (player == null) { GameObject g = Game.PlayerGo; if (g == null) return; player = g.transform; }
            // Hauteur du point PLAYER au-dessus des pieds (les avatars sont places par les pieds).
            float lift = Session.Me != null ? player.position.y - Session.Me.State.Feet.y : 0f;
            if (lift < 0f || lift > 2f) lift = 0.2f;
            foreach (Probe p in probes)
            {
                if (p.Ref == null || p.Proxy == null) continue;
                Vector3 best = player.position;
                float bd = (best - p.Ref.position).sqrMagnitude;
                if (Session.Active)
                    foreach (Avatar a in PlayerSync.Avatars)
                    {
                        if (a.Player == null || a.Player.Level != 1) continue;
                        Vector3 q = a.Player.State.Feet + Vector3.up * lift;
                        float d = (q - p.Ref.position).sqrMagnitude;
                        if (d < bd) { bd = d; best = q; }
                    }
                p.Proxy.position = best;
            }
        }

        // Essais : pour les automates dont l'objet de reference est sous 'path', distance de la reference au joueur vise.
        public static string Describe(string path)
        {
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (Probe p in probes)
            {
                if (p.Ref == null || p.Proxy == null || !Recon.Path(p.Ref).StartsWith(path)) continue;
                if (n++ < 6) sb.Append(p.Ref.name).Append(' ').Append((p.Proxy.position - p.Ref.position).magnitude.ToString("F0")).Append(" m ; ");
            }
            return n + " automates : " + sb;
        }

        static void Scan()
        {
            int before = probes.Count;
            foreach (Object o in Game.AllFsms())
            {
                var f = (PlayMakerFSM)o; if (f == null) continue;
                if (f.hideFlags != HideFlags.None || seen.Contains(f) || !f.transform.root.gameObject.activeInHierarchy) continue;
                string root = Game.RootName(f.transform);
                if (root == "PLAYER" || root == "GUI" || root.StartsWith("MWCoop")) continue;
                FsmState[] states;
                try { states = f.Fsm.States; } catch { continue; }
                bool open = root == "JOBS" && VehicleSync.CarRoot(f.transform) == null;   // (pas le taxi range sous JOBS : vehicule)
                foreach (FsmState st in states) if (st.Name.StartsWith("Open")) open = true;
                if (!open) { seen.Add(f); continue; }
                int n = f.Fsm.Initialized ? Retarget(f, states) : -1;
                if (n >= 0) { seen.Add(f); if (n > 0 && root == "JOBS") jobProbes++; }   // (pas initialise : plus tard, sans avertissement)
            }
            if (probes.Count != before) Log.Info("portes automatiques et boulots : " + probes.Count + " automates suivent le joueur le plus proche (" + jobProbes + " des boulots)");
        }

        // Les mesures de distance au joueur (GetDistance vers PLAYER) de cet automate visent le joueur le plus proche
        // (Events : evenements decides par l'hote). Nombre d'actions redirigees ; -1 : actions pas encore chargees.
        public static int Retarget(PlayMakerFSM f) { try { return f.Fsm == null || !f.Fsm.Initialized ? -1 : Retarget(f, f.Fsm.States); } catch { return -1; } }

        static int Retarget(PlayMakerFSM f, FsmState[] states)
        {
            Probe probe = null;
            int n = 0;
            foreach (FsmState st in states)
            {
                FsmStateAction[] acts;
                try { acts = st.Actions; } catch { return -1; }
                foreach (FsmStateAction a in acts)
                {
                    if (a == null || a.GetType().Name != "GetDistance") continue;
                    FieldInfo tf = a.GetType().GetField("target"), gf = a.GetType().GetField("gameObject");
                    var tgt = tf != null ? tf.GetValue(a) as FsmGameObject : null;
                    var od = gf != null ? gf.GetValue(a) as FsmOwnerDefault : null;
                    bool toPlayer = tgt != null && tgt.Value != null && tgt.Value.name == "PLAYER";
                    // (sens inverse : du joueur vers un objet -- placards de l'usine, contrat sur la table du patron)
                    bool fromPlayer = !toPlayer && od != null && od.OwnerOption != OwnerDefaultOption.UseOwner && od.GameObject != null && od.GameObject.Value != null
                                      && od.GameObject.Value.name == "PLAYER" && tgt != null && tgt.Value != null;
                    if (!toPlayer && !fromPlayer) continue;
                    if (probe == null)
                    {
                        Transform r = fromPlayer ? tgt.Value.transform
                                    : od == null || od.OwnerOption == OwnerDefaultOption.UseOwner || od.GameObject.Value == null ? f.transform : od.GameObject.Value.transform;
                        probe = new Probe { Ref = r, Proxy = new GameObject("MWCoop-JoueurProche").transform };
                        probe.Proxy.position = (toPlayer ? tgt.Value : od.GameObject.Value).transform.position;
                        probes.Add(probe);
                    }
                    if (toPlayer) tf.SetValue(a, new FsmGameObject { Value = probe.Proxy.gameObject });
                    else gf.SetValue(a, new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, GameObject = new FsmGameObject { Value = probe.Proxy.gameObject } });
                    n++;
                }
            }
            return n;
        }
    }
}
