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
    public static class NearDoors
    {
        class Probe { public Transform Ref, Proxy; }
        static readonly List<Probe> probes = new List<Probe>();
        static readonly HashSet<PlayMakerFSM> seen = new HashSet<PlayMakerFSM>();
        static float nextScan = -1;
        static Transform player;

        public static void OnLevelLoaded()
        {
            foreach (Probe p in probes) if (p.Proxy != null) Object.Destroy(p.Proxy.gameObject);
            probes.Clear(); seen.Clear(); player = null;
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 15f : -1;
        }

        public static void Update()
        {
            if (nextScan < 0 || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = now + 90f; Scan(); }
            if (probes.Count == 0) return;
            if (player == null) { GameObject g = GameObject.Find("PLAYER"); if (g == null) return; player = g.transform; }
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

        static void Scan()
        {
            int before = probes.Count;
            foreach (Object o in Game.AllFsms())
            {
                var f = (PlayMakerFSM)o; if (f == null) continue;
                if (f.hideFlags != HideFlags.None || seen.Contains(f) || !f.transform.root.gameObject.activeInHierarchy) continue;
                string root = f.transform.root.name;
                if (root == "PLAYER" || root == "GUI" || root.StartsWith("MWCoop")) continue;
                FsmState[] states;
                try { states = f.Fsm.States; } catch { continue; }
                bool open = false;
                foreach (FsmState st in states) if (st.Name.StartsWith("Open")) open = true;
                if (!open) { seen.Add(f); continue; }
                if (Retarget(f, states) >= 0) seen.Add(f);
            }
            if (probes.Count != before) Log.Info("portes automatiques : " + probes.Count + " suivent le joueur le plus proche");
        }

        // Les mesures de distance au joueur (GetDistance vers PLAYER) de cet automate visent le joueur le plus proche
        // (Events : evenements decides par l'hote). Nombre d'actions redirigees ; -1 : actions pas encore chargees.
        public static int Retarget(PlayMakerFSM f) { try { return Retarget(f, f.Fsm.States); } catch { return -1; } }

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
                    if (tgt == null || tgt.Value == null || tgt.Value.name != "PLAYER") continue;
                    if (probe == null)
                    {
                        var od = gf != null ? gf.GetValue(a) as FsmOwnerDefault : null;
                        Transform r = od == null || od.OwnerOption == OwnerDefaultOption.UseOwner || od.GameObject.Value == null ? f.transform : od.GameObject.Value.transform;
                        probe = new Probe { Ref = r, Proxy = new GameObject("MWCoop-JoueurProche").transform };
                        probe.Proxy.position = tgt.Value.transform.position;
                        probes.Add(probe);
                    }
                    tf.SetValue(a, new FsmGameObject { Value = probe.Proxy.gameObject });
                    n++;
                }
            }
            return n;
        }
    }
}
