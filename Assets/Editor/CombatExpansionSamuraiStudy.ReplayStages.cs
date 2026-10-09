using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        const string RSOutput = "GeneratedAssets/CombatExpansion/SamuraiStudy/ReplayStages/Execution05";

        [Serializable]
        sealed class RSReport
        {
            public string status = "RUNNING";
            public string scope = "Diagnostic of forward versus terminal-then-seek Evaluation05 poses. " +
                "Original runtime actors, native clips, grounding and 1.7m setup. Both fighter assignments " +
                "and directions. All mapped human bones and animator roots on source and fighter rigs. " +
                "World and local positions are compared independently; rotations are reported in degrees. " +
                "No pose repair, tolerance change, contact acceptance or gameplay registration.";
            public string error;
            public List<RSCase> cases = new List<RSCase>();
        }

        [Serializable]
        sealed class RSCase
        {
            public string name;
            public string attack;
            public string reaction;
            public string grounding;
            public string status = "RUNNING";
            public List<RSComparison> comparisons = new List<RSComparison>();
        }

        [Serializable]
        sealed class RSComparison
        {
            public float seconds;
            public string order;
            public string node;
            public Vector3 forwardWorld;
            public Vector3 repeatedWorld;
            public Vector3 forwardLocal;
            public Vector3 repeatedLocal;
            public float worldErrorM;
            public float localError;
            public float rotationErrorDegrees;
        }

        sealed class RSNode
        {
            public string name;
            public Transform transform;
        }

        readonly struct RSPose
        {
            public readonly Vector3 world;
            public readonly Vector3 local;
            public readonly Quaternion rotation;

            public RSPose(Transform value)
            {
                world = value.position;
                local = value.localPosition;
                rotation = value.rotation;
            }
        }

        public static void CaptureExecution05ReplayStages()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            Directory.CreateDirectory(RSOutput);
            var report = new RSReport();
            RSFlush(report);
            try
            {
                using var session = new SourceSession();
                InitializePairStudy(session.Fighters);
                var grounding = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(GroundingAssetPath(5));
                if (!grounding)
                    throw new InvalidOperationException("Missing Execution05 grounding.");
                foreach (var source in session.Fighters)
                foreach (int direction in new[] { 1, -1 })
                {
                    var target = session.Fighters.Single(f => f != source);
                    var record = new RSCase
                    {
                        name = source.name + "_" + target.name + "_" + direction,
                        grounding = CombatExpansionInventory.Identity(grounding)
                    };
                    report.cases.Add(record);
                    FrankBattlePairPlayback pair = null;
                    try
                    {
                        pair = BeginBladeStudy(session.Fighters, source, target, 5, direction, grounding, 1.7f);
                        record.attack = CombatExpansionInventory.Identity(pair.Move.sourcePair.attack);
                        record.reaction = CombatExpansionInventory.Identity(pair.Move.sourcePair.reaction);
                        var nodes = RSNodes(pair, source, target);
                        var baseline = new Dictionary<float, RSPose[]>();
                        float[] coarse = CCTimes(0, pair.Duration, 30).Distinct().ToArray();
                        foreach (float seconds in coarse)
                        {
                            pair.EvaluateAt(seconds);
                            baseline.Add(seconds, nodes.Select(n => new RSPose(n.transform)).ToArray());
                        }
                        using var csv = new StreamWriter(RSOutput + "/" + record.name + ".csv", false);
                        csv.WriteLine("seconds,order,node,worldErrorM,localError,rotationErrorDegrees," +
                            "forwardX,forwardY,forwardZ,repeatedX,repeatedY,repeatedZ," +
                            "forwardLocalX,forwardLocalY,forwardLocalZ,repeatedLocalX,repeatedLocalY,repeatedLocalZ");
                        // Match the failing verification ordering without rebuilding the graph.
                        foreach (float seconds in coarse.Reverse())
                        {
                            pair.EvaluateAt(pair.Duration);
                            pair.EvaluateAt(seconds);
                            RSCompare(record, csv, "terminal_then_seek", seconds, nodes, baseline[seconds]);
                            pair.EvaluateAt(seconds);
                            RSCompare(record, csv, "repeat_same_time", seconds, nodes, baseline[seconds]);
                        }
                        record.status = "CAPTURED_DIAGNOSTIC";
                    }
                    catch
                    {
                        record.status = "FAILED_PARTIAL";
                        throw;
                    }
                    finally
                    {
                        if (pair)
                            pair.Cancel();
                        RSFlush(report);
                    }
                }
                report.status = "CAPTURED_DIAGNOSTIC_NO_REPAIR";
            }
            catch (Exception error)
            {
                report.status = "FAILED_PARTIAL";
                report.error = error.ToString();
                throw;
            }
            finally
            {
                RSFlush(report);
            }
        }

        static List<RSNode> RSNodes(FrankBattlePairPlayback pair, CharacterCombat source, CharacterCombat target)
        {
            var result = new List<RSNode>();
            RSAddRig(result, "attacker/native", pair.AttackerActor.Pose.driver);
            RSAddRig(result, "receiver/native", pair.ReceiverActor.Pose.driver);
            RSAddRig(result, "attacker/fighter", source.Animator);
            RSAddRig(result, "receiver/fighter", target.Animator);
            return result;
        }

        static void RSAddRig(List<RSNode> result, string role, Animator animator)
        {
            if (!animator || !animator.isHuman)
                throw new InvalidOperationException("Missing diagnostic humanoid: " + role);
            result.Add(new RSNode { name = role + "/AnimatorRoot", transform = animator.transform });
            for (int index = 0; index < (int)HumanBodyBones.LastBone; index++)
            {
                var bone = animator.GetBoneTransform((HumanBodyBones)index);
                if (bone)
                    result.Add(new RSNode { name = role + "/" + (HumanBodyBones)index, transform = bone });
            }
        }

        static void RSCompare(RSCase record, TextWriter csv, string order, float seconds,
            List<RSNode> nodes, RSPose[] baseline)
        {
            for (int index = 0; index < nodes.Count; index++)
            {
                var before = baseline[index];
                var after = new RSPose(nodes[index].transform);
                float world = Vector3.Distance(before.world, after.world);
                float local = Vector3.Distance(before.local, after.local);
                float angle = Quaternion.Angle(before.rotation, after.rotation);
                if (!float.IsFinite(world) || !float.IsFinite(local) || !float.IsFinite(angle))
                    throw new InvalidOperationException("Nonfinite diagnostic pose: " + nodes[index].name);
                var comparison = new RSComparison
                {
                    seconds = seconds,
                    order = order,
                    node = nodes[index].name,
                    forwardWorld = before.world,
                    repeatedWorld = after.world,
                    forwardLocal = before.local,
                    repeatedLocal = after.local,
                    worldErrorM = world,
                    localError = local,
                    rotationErrorDegrees = angle
                };
                csv.WriteLine(FormattableString.Invariant(
                    $"{seconds:R},{order},{comparison.node},{world:R},{local:R},{angle:R},") +
                    RSVector(before.world) + "," + RSVector(after.world) + "," +
                    RSVector(before.local) + "," + RSVector(after.local));
                string rig = comparison.node.Substring(0, comparison.node.LastIndexOf('/')) + "/";
                int previous = record.comparisons.FindIndex(value =>
                    value.order == order && value.node.StartsWith(rig, StringComparison.Ordinal));
                if (previous < 0)
                    record.comparisons.Add(comparison);
                else if (world > record.comparisons[previous].worldErrorM)
                    record.comparisons[previous] = comparison;
            }
        }

        static string RSVector(Vector3 value)
        {
            return FormattableString.Invariant($"{value.x:R},{value.y:R},{value.z:R}");
        }

        static void RSFlush(RSReport report)
        {
            File.WriteAllText(RSOutput + "/Report.json", JsonUtility.ToJson(report, true));
            File.WriteAllText(RSOutput + "/Status.txt", report.status + "\n" + report.error);
        }
    }
}
