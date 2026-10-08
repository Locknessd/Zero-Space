using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionKbComboStudy
    {
        sealed class Node
        {
            public string role, rig, bone;
            public Transform node;
        }
        readonly struct PoseSample
        {
            public readonly Vector3 position;
            public readonly Quaternion rotation;
            public PoseSample(Vector3 p, Quaternion q)
            {
                position = p;
                rotation = q;
            }
        }
        static readonly HumanBodyBones[] Bones = { HumanBodyBones.Hips, HumanBodyBones.Chest,
            HumanBodyBones.Head, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };

        static List<Node> Nodes(FrankBattlePairPlayback pair, CharacterCombat source, CharacterCombat target)
        {
            var result = new List<Node>();
            var actors = new[] { pair.AttackerActor, pair.ReceiverActor };
            var fighters = new[] { source, target };
            for (int role = 0; role < 2; role++)
            {
                string label = role == 0 ? "attacker" : "receiver";
                result.Add(new Node { role = label, rig = "actor", bone = "Root", node = actors[role].transform });
                result.Add(new Node { role = label, rig = "model", bone = "Root", node = fighters[role].transform });
                foreach (bool native in new[] { false, true })
                {
                    var animator = native ? actors[role].Pose.driver : fighters[role].Animator;
                    string rig = native ? "native" : "fighter";
                    result.Add(new Node { role = label, rig = rig, bone = "Root", node = animator.transform });
                    foreach (var bone in Bones)
                    {
                        var node = animator.GetBoneTransform(bone);
                        if (!node)
                            throw new InvalidOperationException("Missing " + rig + " bone " + bone);
                        result.Add(new Node { role = label, rig = rig, bone = bone.ToString(), node = node });
                    }
                }
            }
            return result;
        }

        static Dictionary<float, PoseSample[]> ReadCsv(string path, List<Node> nodes)
        {
            var rows = new Dictionary<float, List<PoseSample>>();
            foreach (string line in File.ReadLines(path).Skip(1))
            {
                var cells = line.Split(',');
                float seconds = Parse(cells[0]);
                if (!rows.TryGetValue(seconds, out var poses))
                {
                    poses = new List<PoseSample>();
                    rows.Add(seconds, poses);
                }
                if (poses.Count >= nodes.Count || nodes[poses.Count].role != cells[2] ||
                    nodes[poses.Count].rig != cells[3] || nodes[poses.Count].bone != cells[4])
                    throw new InvalidOperationException("CSV node identity/order mismatch.");
                poses.Add(new PoseSample(new Vector3(Parse(cells[5]), Parse(cells[6]), Parse(cells[7])),
                    new Quaternion(Parse(cells[8]), Parse(cells[9]), Parse(cells[10]), Parse(cells[11]))));
            }
            if (rows.Values.Any(r => r.Count != nodes.Count))
                throw new InvalidOperationException("CSV missing bone rows.");
            return rows.ToDictionary(r => r.Key, r => r.Value.ToArray());
        }

        static float Parse(string value) => float.Parse(value, CultureInfo.InvariantCulture);

        static void Verify(FrankBattlePairPlayback pair, CharacterCombat[] fighters, List<Node> nodes,
            PoseSample[] saved, float seconds, CandidateRecord record)
        {
            pair.EvaluateAt(seconds);
            CheckEquipment(pair, fighters);
            for (int i = 0; i < nodes.Count; i++)
            {
                record.worstBackwardsSeekMetres = Mathf.Max(record.worstBackwardsSeekMetres,
                    Vector3.Distance(nodes[i].node.position, saved[i].position));
                record.worstBackwardsSeekDegrees = Mathf.Max(record.worstBackwardsSeekDegrees,
                    Quaternion.Angle(nodes[i].node.rotation, saved[i].rotation));
            }
            if (record.worstBackwardsSeekMetres > .001f || record.worstBackwardsSeekDegrees > .1f)
                throw new InvalidOperationException("Backwards pose seek exceeds 0.001m/0.1deg at " + seconds +
                    " for " + record.name + ": " + record.worstBackwardsSeekMetres + "m / " +
                    record.worstBackwardsSeekDegrees + "deg");
        }

        static void WriteSeams(string path, Dictionary<float, PoseSample[]> saved, List<Node> nodes)
        {
            var rows = new StringBuilder("linkSeconds,seconds,role,rig,bone,x,y,z," +
                "dxFromBefore,dyFromBefore,dzFromBefore,displacementFromBefore,rotationFromBeforeDegrees\n");
            foreach (float seam in new[] { .4f, .8f })
            {
                float before = seam == .4f ? .399f : .799f;
                float after = seam == .4f ? .401f : .801f;
                foreach (float seconds in new[] { before, seam, after })
                for (int i = 0; i < nodes.Count; i++)
                {
                    var node = nodes[i];
                    if (node.bone != "Root" && node.bone != "Hips")
                        continue;
                    var p = saved[seconds][i].position;
                    var d = p - saved[before][i].position;
                    float angle = Quaternion.Angle(saved[seconds][i].rotation, saved[before][i].rotation);
                    rows.AppendLine(FormattableString.Invariant(
                        $"{seam:R},{seconds:R},{node.role},{node.rig},{node.bone},{p.x:R},{p.y:R},{p.z:R},{d.x:R},{d.y:R},{d.z:R},{d.magnitude:R},{angle:R}"));
                }
            }
            File.WriteAllText(path, rows.ToString());
        }

        static void CheckEquipment(FrankBattlePairPlayback pair, CharacterCombat[] fighters)
        {
            foreach (var actor in new[] { pair.AttackerActor, pair.ReceiverActor })
            {
                if (actor.Pose.weaponRenderers.Length != 0 ||
                    actor.GetComponentsInChildren<Renderer>(true).Any(r => r.enabled && r.gameObject.activeInHierarchy) ||
                    actor.GetComponentsInChildren<Collider>(true).Any(c => c.enabled && c.gameObject.activeInHierarchy) ||
                    !actor.Pose.transferFingers)
                    throw new InvalidOperationException("KB actor must be unarmed with finger transfer.");
            }
            foreach (var fighter in fighters)
            foreach (var manager in fighter.GetComponentsInChildren<TrumpWeaponManager>(true))
                if (!manager.IsUnarmedPresentation || manager.ActiveWeapon != TrumpWeaponManager.WeaponType.None)
                    throw new InvalidOperationException("Runtime unarmed presentation missing.");
        }
    }
}
