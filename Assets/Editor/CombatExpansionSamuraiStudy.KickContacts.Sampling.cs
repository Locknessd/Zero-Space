using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using SkinProbe = FrankRetarget.Editor.CombatExpansionAxeDenseStudy.SkinRegionProbe;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        sealed class KCProbes : IDisposable
        {
            public readonly List<SkinProbe> items = new List<SkinProbe>();
            public KCProbes(CharacterCombat source, CharacterCombat target, KCCase record)
            {
                try
                {
                    foreach (var foot in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                    foreach (var region in new[] { HumanBodyBones.Head, HumanBodyBones.Chest })
                    {
                        var probe = new SkinProbe(source, foot, target, region,
                            region == HumanBodyBones.Head ? SkinProbe.Selection.BoneAndDescendants :
                            SkinProbe.Selection.TorsoWithoutHeadNeckOrArms);
                        items.Add(probe);
                        record.selectionSummaries.Add(KCChannels[items.Count - 1] + "\n" + probe.SelectionSummary);
                    }
                }
                catch { Dispose(); throw; }
            }
            public void Dispose()
            {
                foreach (var probe in items) probe.Dispose();
                items.Clear();
            }
        }

        static float[] KCTimes()
        {
            return Enumerable.Range(156, 67).Select(i => i / 120f)
                .Concat(new[] { KCStart, 1.3958f, 1.6042f, 1.6667f, KCEnd })
                .Distinct().OrderBy(t => t).ToArray();
        }

        static void KCMeasure(FrankBattlePairPlayback pair, CharacterCombat[] fighters,
            CharacterCombat source, CharacterCombat target, KCProbes probes,
            List<(string role, string rig, string bone, Transform node)> nodes,
            KCFrame frame, ref Bounds bounds)
        {
            pair.EvaluateAt(frame.seconds);
            CheckPairWeapons(pair, pair.AttackerActor.Pose.weaponRenderers);
            for (int channel = 0; channel < probes.items.Count; channel++)
            {
                frame.diagnostics.Add("Begin " + KCChannels[channel]);
                var contact = probes.items[channel].Measure(frame.diagnostics);
                frame.gaps.Add(new KCGap
                {
                    channel = KCChannels[channel], gapM = contact.gap,
                    footWorldPoint = contact.sourcePoint, targetWorldPoint = contact.bodyPoint,
                    selectedTargetBone = contact.nearestBone.ToString(), targetBoneLocalPoint = contact.boneOffset
                });
            }
            frame.attackerFloorClearanceM = GroundingClearance(source);
            frame.victimFloorClearanceM = GroundingClearance(target);
            foreach (var node in nodes)
            {
                for (int component = 0; component < 16; component++)
                    if (!float.IsFinite(node.node.localToWorldMatrix[component]))
                        throw new InvalidOperationException("Nonfinite kick world transform: " + node.bone);
                frame.transforms.Add(new KCTransform
                {
                    role = node.role, rig = node.rig, bone = node.bone,
                    worldPosition = node.node.position, worldRotation = node.node.rotation,
                    worldScale = node.node.lossyScale
                });
                bounds.Encapsulate(node.node.position);
            }
            foreach (var fighter in fighters)
            foreach (var renderer in fighter.Animator.GetComponentsInChildren<Renderer>())
                if (renderer.enabled && renderer.gameObject.activeInHierarchy)
                    bounds.Encapsulate(renderer.bounds);
            foreach (var weapon in pair.AttackerActor.Pose.weaponRenderers)
                bounds.Encapsulate(weapon.bounds);
            if (frame.gaps.Count != 4)
                throw new InvalidOperationException("Missing kick contact channels.");
            frame.status = "MEASURED";
        }

        static string KCHeader => "sourceSeconds,status,attackerFloorClearanceM,victimFloorClearanceM," +
            string.Join(",", KCChannels.SelectMany(c => new[] { c + "_gapM", c + "_footX", c + "_footY",
                c + "_footZ", c + "_targetX", c + "_targetY", c + "_targetZ" }));

        static void KCWriteFrame(TextWriter csv, KCFrame frame)
        {
            var row = new StringBuilder(FormattableString.Invariant(
                $"{frame.seconds:R},{frame.status},{frame.attackerFloorClearanceM:R},{frame.victimFloorClearanceM:R}"));
            foreach (var gap in frame.gaps)
            {
                row.Append(FormattableString.Invariant($",{gap.gapM:R}"));
                CAVector(row, gap.footWorldPoint);
                CAVector(row, gap.targetWorldPoint);
            }
            csv.WriteLine(row);
        }
    }
}
