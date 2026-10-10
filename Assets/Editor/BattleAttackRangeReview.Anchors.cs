using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        sealed class RangeContactFrame
        {
            public BattlePresentationContactSetup.BladeContactProbe probe;
            public Matrix4x4 boneFrame;
            public Vector3 offset;
        }

        [Serializable]
        sealed class RangeAnchorProposal
        {
            public string move;
            public float seconds;
            public string avatar;
            public int bone;
            public Vector3 offset;
            public float bladeGap;
            public float bodyGap;
        }

        [Serializable]
        sealed class RangeAnchorReport
        {
            public RangeAnchorProposal[] anchors;
        }

        [MenuItem("Tools/Battle/Measure Execution range contact anchors")]
        public static void MeasureExecutionRangeAnchors()
        {
            Directory.CreateDirectory(RangeReview);
            var proposals = new List<RangeAnchorProposal>();
            BattleComboAuthoring.WithStudy((scene, game, fighters) =>
            {
                foreach (var source in fighters)
                foreach (var move in source.heavyCombatMoves.Where(IsExecutionRangeMove))
                {
                    var target = fighters.Single(f => f != source);
                    var profile = game.battleVfx.timeline.FindMove(move);
                    var cues = profile.cues.Where(IsMeleeRangeContact).ToArray();
                    var frames = cues.ToDictionary(c => c, c => new List<RangeContactFrame>());
                    foreach (int direction in new[] { 1, -1 })
                    {
                        PrepareRangePair(fighters, source, target, move.attackRange, direction);
                        if (!source.ExecuteAttack(move, target))
                            throw new InvalidOperationException("Cannot measure " + move.moveName);
                        var pair = source.SourcePlayback;
                        try
                        {
                            foreach (var cue in cues)
                            {
                                pair.EvaluateAt(cue.seconds);
                                var anchor = cue.avatarContacts.Single(a => a.avatar == target.Animator.avatar);
                                var bone = target.Animator.GetBoneTransform(anchor.bone);
                                var probe = new BattlePresentationContactSetup.BladeContactProbe(pair, source, target);
                                // Project near the existing contact so the authored target region is retained.
                                Vector3 point = bone.TransformPoint(anchor.offset);
                                for (int i = 0; i < 24; i++)
                                    point = probe.BodyPoint(probe.BladePoint(point));
                                frames[cue].Add(new RangeContactFrame
                                {
                                    probe = probe,
                                    boneFrame = bone.localToWorldMatrix,
                                    offset = bone.InverseTransformPoint(point)
                                });
                            }
                        }
                        finally
                        {
                            pair.Cancel();
                        }
                    }
                    foreach (var cue in cues)
                    {
                        var anchor = cue.avatarContacts.Single(a => a.avatar == target.Animator.avatar);
                        var samples = frames[cue];
                        float best = float.PositiveInfinity;
                        RangeAnchorProposal chosen = null;
                        for (int step = 0; step <= 20; step++)
                        {
                            Vector3 offset = Vector3.Lerp(samples[0].offset, samples[1].offset, step / 20f);
                            float blade = samples.Max(s => s.probe.BladeDistance(
                                s.boneFrame.MultiplyPoint3x4(offset)));
                            float body = samples.Max(s => s.probe.BodyDistance(
                                s.boneFrame.MultiplyPoint3x4(offset)));
                            if (!float.IsFinite(offset.sqrMagnitude) || blade > .10f || body > .015f)
                                continue;
                            float score = blade + body * 4;
                            if (score >= best)
                                continue;
                            best = score;
                            chosen = new RangeAnchorProposal
                            {
                                move = move.moveName,
                                seconds = cue.seconds,
                                avatar = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(anchor.avatar)),
                                bone = (int)anchor.bone,
                                offset = offset,
                                bladeGap = blade,
                                bodyGap = body
                            };
                        }
                        if (chosen == null)
                            throw new InvalidOperationException("No common contact at the new range: " + move.moveName);
                        proposals.Add(chosen);
                    }
                }
            });
            File.WriteAllText(RangeReview + "/ContactAnchors.json",
                JsonUtility.ToJson(new RangeAnchorReport { anchors = proposals.ToArray() }, true));
        }
    }
}
