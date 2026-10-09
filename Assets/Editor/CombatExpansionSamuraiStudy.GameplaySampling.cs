using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        static readonly HumanBodyBones[] PairBones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.Chest,
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot
        };

        static void CaptureGameplayPair(CharacterCombat[] fighters, int assignment, int execution, int direction)
        {
            foreach (var fighter in fighters)
                fighter.ResetCombat();
            var source = fighters[assignment];
            var target = fighters[1 - assignment];
            source.Animator.transform.SetPositionAndRotation(Vector3.left * direction * .85f,
                Quaternion.LookRotation(Vector3.right * direction));
            target.Animator.transform.SetPositionAndRotation(Vector3.right * direction * .85f,
                Quaternion.LookRotation(Vector3.left * direction));
            var move = MakePairMove(source, target, execution);
            if (!source.ExecuteAttack(move, target))
                throw new InvalidOperationException("Runtime Samurai pair was rejected: " + move.moveName);
            var pair = source.SourcePlayback;
            try
            {
                pair.AttackerActor.Pose.transferFingers = true;
                pair.ReceiverActor.Pose.transferFingers = true;
                var nodes = PairNodes(pair, source, target);
                var rows = new StringBuilder("seconds,role,rig,bone,x,y,z\n");
                var samples = new Dictionary<float, Vector3[]>();
                var bounds = new Bounds(source.Animator.GetBoneTransform(HumanBodyBones.Hips).position, Vector3.zero);
                int count = Mathf.CeilToInt(pair.Duration * 60);
                var sheetTimes = Enumerable.Range(0, Mathf.Max(24, Mathf.CeilToInt(pair.Duration * 8) + 1))
                    .Select(i => pair.Duration * i / (Mathf.Max(24, Mathf.CeilToInt(pair.Duration * 8) + 1) - 1))
                    .Concat(new[] { move.attackAnim.length, move.hitAnim.length }).Distinct().OrderBy(t => t).ToArray();
                var times = Enumerable.Range(0, count + 1).Select(i => Mathf.Min(i / 60f, pair.Duration))
                    .Concat(sheetTimes).Distinct().OrderBy(t => t).ToArray();
                var weapons = pair.AttackerActor.Pose.weaponRenderers;
                foreach (float seconds in times)
                {
                    pair.EvaluateAt(seconds);
                    CheckPairWeapons(pair, weapons);
                    var positions = nodes.Select(n => n.node.position).ToArray();
                    samples.Add(seconds, positions);
                    for (int index = 0; index < nodes.Count; index++)
                    {
                        var p = positions[index];
                        if (!float.IsFinite(p.x) || !float.IsFinite(p.y) || !float.IsFinite(p.z))
                            throw new InvalidOperationException("Nonfinite Samurai pose at " + seconds);
                        bounds.Encapsulate(p);
                        var node = nodes[index];
                        rows.AppendLine(FormattableString.Invariant(
                            $"{seconds:R},{node.role},{node.rig},{node.bone},{p.x:R},{p.y:R},{p.z:R}"));
                    }
                    foreach (var renderer in weapons)
                        bounds.Encapsulate(renderer.bounds);
                }
                bounds.Encapsulate(new Vector3(bounds.center.x, 0, bounds.center.z));
                bounds.Expand(.65f);
                string stem = "Execution" + execution.ToString("D2") + "_" + source.name + "_" + target.name +
                    "_Lane" + (direction > 0 ? "Positive" : "Negative");
                File.WriteAllText(PairOutput + "/" + stem + ".csv", rows.ToString());
                using var rendering = new GameplayPairRendering(source.gameObject.scene, fighters, pair);
                float worstSeek = 0;
                rendering.Write(PairOutput + "/" + stem + ".png", bounds, sheetTimes, seconds =>
                {
                    pair.EvaluateAt(seconds);
                    CheckPairWeapons(pair, weapons);
                    for (int index = 0; index < nodes.Count; index++)
                        worstSeek = Mathf.Max(worstSeek,
                            Vector3.Distance(nodes[index].node.position, samples[seconds][index]));
                    if (worstSeek > .001f)
                        throw new InvalidOperationException("Samurai backwards seek changed recorded pose: " +
                            stem + " at " + seconds + " error=" + worstSeek);
                });
                File.WriteAllText(PairOutput + "/" + stem + ".txt",
                    "Candidate runtime pair; not registered gameplay.\n" +
                    "Attacker=" + source.name + "; clip=" + CombatExpansionInventory.Identity(move.attackAnim) +
                    "\nVictim=" + target.name + "; clip=" + CombatExpansionInventory.Identity(move.hitAnim) +
                    "\nVictim source renderers=0; attacker source renderers=" + weapons.Length +
                    "\nMaximum backwards-seek bone error metres=" + worstSeek.ToString("R") +
                    "\nSheet seconds=" + string.Join(",", sheetTimes.Select(t => t.ToString("R"))) + "\n");
            }
            finally
            {
                pair.Cancel();
            }
        }

        static void CheckPairWeapons(FrankBattlePairPlayback pair, Renderer[] weapons)
        {
            if (pair.ReceiverActor.GetComponentsInChildren<Renderer>(true).Length != 0 || weapons.Length != 2 ||
                weapons.Any(r => !r || !r.enabled || !r.gameObject.activeInHierarchy))
                throw new InvalidOperationException("Samurai native equipment presentation is invalid.");
            foreach (var actor in new[] { pair.AttackerActor, pair.ReceiverActor })
                if (actor.GetComponentsInChildren<Collider>(true).Any(c => c.enabled && c.gameObject.activeInHierarchy))
                    throw new InvalidOperationException("Source study has an active native weapon collider.");
        }

        static List<(string role, string rig, string bone, Transform node)> PairNodes(FrankBattlePairPlayback pair,
            CharacterCombat source, CharacterCombat target)
        {
            var result = new List<(string, string, string, Transform)>();
            var actors = new[] { pair.AttackerActor, pair.ReceiverActor };
            var fighters = new[] { source, target };
            for (int role = 0; role < 2; role++)
            {
                result.Add((Roles[role], "actor", "Root", actors[role].transform));
                result.Add((Roles[role], "driver", "Root", actors[role].activeDriver.transform));
                foreach (bool native in new[] { true, false })
                {
                    var animator = native ? actors[role].Pose.driver : fighters[role].Animator;
                    result.Add((Roles[role], native ? "native" : "fighter", "AnimatorRoot", animator.transform));
                    foreach (var bone in PairBones)
                    {
                        var node = animator.GetBoneTransform(bone);
                        if (!node)
                            throw new InvalidOperationException("Missing Samurai bone: " + bone);
                        result.Add((Roles[role], native ? "native" : "fighter", bone.ToString(), node));
                    }
                }
            }
            return result;
        }
    }
}
