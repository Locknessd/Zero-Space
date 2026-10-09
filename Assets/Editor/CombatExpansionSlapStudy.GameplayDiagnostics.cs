using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapStudy
    {
        static readonly HumanBodyBones[] CandidateBones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder,
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot
        };

        static List<(string role, string rig, string bone, Transform node)> CandidateNodes(
            FrankBattlePairPlayback pair, CharacterCombat source, CharacterCombat target)
        {
            var result = new List<(string, string, string, Transform)>();
            var actors = new[] { pair.AttackerActor, pair.ReceiverActor };
            var fighters = new[] { source, target };
            for (int role = 0; role < 2; role++)
            {
                string label = role == 0 ? "giver" : "receiver";
                result.Add((label, "actor", "Root", actors[role].transform));
                result.Add((label, "driver", "Root", actors[role].activeDriver.transform));
                result.Add((label, "model", "Root", fighters[role].transform));
                foreach (bool native in new[] { true, false })
                {
                    var animator = native ? actors[role].Pose.driver : fighters[role].Animator;
                    string rig = native ? "native" : "fighter";
                    result.Add((label, rig, "AnimatorRoot", animator.transform));
                    foreach (var bone in CandidateBones)
                    {
                        var node = animator.GetBoneTransform(bone);
                        if (!node)
                            throw new InvalidOperationException("Missing candidate " + rig + " bone " + bone);
                        result.Add((label, rig, bone.ToString(), node));
                    }
                }
            }
            return result;
        }

        static Dictionary<float, Vector3[]> CandidateReadCsv(string path, int expectedNodes)
        {
            var rows = new Dictionary<float, List<Vector3>>();
            foreach (string line in File.ReadLines(path).Skip(1))
            {
                var cells = line.Split(',');
                float seconds = float.Parse(cells[0], CultureInfo.InvariantCulture);
                if (!rows.TryGetValue(seconds, out var positions))
                {
                    positions = new List<Vector3>();
                    rows.Add(seconds, positions);
                }
                positions.Add(new Vector3(float.Parse(cells[5], CultureInfo.InvariantCulture),
                    float.Parse(cells[6], CultureInfo.InvariantCulture),
                    float.Parse(cells[7], CultureInfo.InvariantCulture)));
            }
            if (rows.Values.Any(p => p.Count != expectedNodes))
                throw new InvalidOperationException("Candidate CSV is missing bone rows: " + path);
            return rows.ToDictionary(row => row.Key, row => row.Value.ToArray());
        }

        static float CandidateForwardDot(Animator animator)
        {
            var shoulders = animator.GetBoneTransform(HumanBodyBones.RightShoulder).position -
                animator.GetBoneTransform(HumanBodyBones.LeftShoulder).position;
            var upright = animator.GetBoneTransform(HumanBodyBones.Head).position -
                animator.GetBoneTransform(HumanBodyBones.Hips).position;
            var forward = Vector3.Cross(shoulders, upright);
            if (forward.sqrMagnitude < .00000001f)
                throw new InvalidOperationException("Degenerate candidate anatomical orientation.");
            return Vector3.Dot(forward.normalized, Vector3.right);
        }

        static void CandidateMeasurements(StringBuilder rows, CandidateRecord record, float seconds,
            Animator source, Animator target, string rig)
        {
            float sourceDot = CandidateForwardDot(source);
            float targetDot = CandidateForwardDot(target);
            var head = target.GetBoneTransform(HumanBodyBones.Head).position;
            float left = Vector3.Distance(source.GetBoneTransform(HumanBodyBones.LeftHand).position, head);
            float right = Vector3.Distance(source.GetBoneTransform(HumanBodyBones.RightHand).position, head);
            rows.AppendLine(FormattableString.Invariant($"{seconds:R},{rig},{sourceDot:R},{targetDot:R},{left:R},{right:R}"));
            if (rig != "fighter")
                return;
            if (seconds == 0)
            {
                record.entryAttackerFacingDot = sourceDot;
                record.entryReceiverFacingDot = targetDot;
            }
            if (left < record.minimumLeftJointDistance)
            {
                record.minimumLeftJointDistance = left;
                record.minimumLeftJointSeconds = seconds;
            }
            if (right < record.minimumRightJointDistance)
            {
                record.minimumRightJointDistance = right;
                record.minimumRightJointSeconds = seconds;
            }
        }

        static void CheckCandidateEquipment(FrankBattlePairPlayback pair, CharacterCombat[] fighters)
        {
            foreach (var actor in new[] { pair.AttackerActor, pair.ReceiverActor })
            {
                if (actor.Pose.weaponRenderers.Length != 0 ||
                    actor.GetComponentsInChildren<Renderer>(true).Any(r => r.enabled && r.gameObject.activeInHierarchy))
                    throw new InvalidOperationException("Candidate has an active native renderer.");
                if (actor.GetComponentsInChildren<Collider>(true).Any(c => c.enabled && c.gameObject.activeInHierarchy))
                    throw new InvalidOperationException("Candidate has an active native collider.");
                if (!actor.Pose.transferFingers)
                    throw new InvalidOperationException("Candidate fingers are not transferred.");
            }
            foreach (var fighter in fighters)
            foreach (var manager in fighter.GetComponentsInChildren<TrumpWeaponManager>(true))
                if (!manager.IsUnarmedPresentation || manager.ActiveWeapon != TrumpWeaponManager.WeaponType.None)
                    throw new InvalidOperationException("Candidate lacks runtime-owned unarmed equipment state.");
        }
    }
}
