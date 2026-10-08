using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapSetup
    {
        sealed class Measurement
        {
            public Avatar attacker;
            public Avatar victim;
            public BattleSfxBank.Move profile;
            public Vector3 direction;
        }

        static Dictionary<string, Measurement> MeasureProfiles(StringBuilder report)
        {
            var result = new Dictionary<string, Measurement>();
            using var session = new PreviewSession();
            foreach (var spec in Specs)
            {
                var source = session.Fighters.Single(f => f.name == spec.attacker);
                var target = session.Fighters.Single(f => f != source);
                var measurement = new Measurement { attacker = source.Animator.avatar, victim = target.Animator.avatar };
                foreach (int direction in new[] { 1, -1 })
                {
                    foreach (var fighter in session.Fighters)
                        fighter.ResetCombat();
                    source.Animator.transform.SetPositionAndRotation(Vector3.left * direction * .4f,
                        Quaternion.LookRotation(Vector3.right * direction));
                    target.Animator.transform.SetPositionAndRotation(Vector3.right * direction * .4f,
                        Quaternion.LookRotation(Vector3.left * direction));
                    var move = CombatExpansionSlapStudy.MakeGroundedFaceMove(source, target, spec.sequence);
                    ValidateMove(move, spec);
                    try
                    {
                        if (!source.ExecuteAttack(move, target) || !source.SourcePlayback)
                            throw new InvalidOperationException("Cannot measure " + spec.Key);
                        var pair = source.SourcePlayback;
                        pair.EvaluateAt(spec.seconds);
                        ValidateEquipment(pair, session.Fighters);
                        using var probe = new CombatExpansionAxeDenseStudy.SkinRegionProbe(source,
                            HumanBodyBones.RightHand, target, HumanBodyBones.Head);
                        var diagnostics = new List<string>();
                        var contact = probe.Measure(diagnostics);
                        if (!Within(contact.gap, .001f) || !Finite(contact.boneOffset))
                            throw new InvalidOperationException(spec.Key + " hand/head gap exceeds 1mm: " + contact.gap);
                        float sourceY = BattlePresentationContactSetup.MeasureGroundClearance(source);
                        float targetY = BattlePresentationContactSetup.MeasureGroundClearance(target);
                        if (!float.IsFinite(sourceY) || !float.IsFinite(targetY) || sourceY < -.001f || targetY < -.001f)
                            throw new InvalidOperationException(spec.Key + " body surface falls below ground tolerance.");
                        var head = target.Animator.GetBoneTransform(HumanBodyBones.Head);
                        if (direction == 1)
                        {
                            measurement.profile = MakeProfile(move, spec, target.Animator.avatar, contact.boneOffset);
                            measurement.direction = MeasureSweep(pair, source, target, spec.seconds, contact.sourcePoint);
                            report.AppendLine(spec.Key + " attack=" + CombatExpansionInventory.Identity(move.attackAnim) +
                                "; reaction=" + CombatExpansionInventory.Identity(move.hitAnim));
                            report.AppendLine(FormattableString.Invariant(
                                $"  attackDuration={move.attackAnim.length:R}s reactionDuration={move.hitAnim.length:R}s ") +
                                FormattableString.Invariant($"pairDuration={pair.Duration:R}s entry=0.12s recovery=0.3s"));
                            report.AppendLine(probe.SelectionSummary);
                        }
                        var anchor = measurement.profile.cues.Single(cue => cue.hasContactPoint).avatarContacts.Single();
                        float anchorError = Vector3.Distance(head.TransformPoint(anchor.offset), contact.bodyPoint);
                        if (!Within(anchorError, .003f))
                        {
                            // Intersecting triangles may produce distinct equally valid zero-distance contacts.
                            if (!Within(contact.gap, .000001f))
                                throw new InvalidOperationException(spec.Key + " reverse anchor differs by " + anchorError);
                            report.AppendLine("  Zero-gap contact tie: stored positive anchor differs from selected " +
                                "reverse surface point; geometry gap alone validates this reverse sample.");
                        }
                        report.AppendLine(FormattableString.Invariant(
                            $"{spec.Key} victim={target.name} lane={direction} contact={spec.seconds:R}s ") +
                            FormattableString.Invariant($"gap={contact.gap:R}m anchorPointDifference={anchorError:R}m ") +
                            FormattableString.Invariant($"bodyClearanceAttacker={sourceY:R}m victim={targetY:R}m"));
                        report.AppendLine(FormattableString.Invariant(
                            $"  Head local anchor=({anchor.offset.x:R},{anchor.offset.y:R},{anchor.offset.z:R}); ") +
                            FormattableString.Invariant($"palm sweep victim space=({measurement.direction.x:R},") +
                            FormattableString.Invariant($"{measurement.direction.y:R},{measurement.direction.z:R})"));
                    }
                    finally
                    {
                        source.SourcePlayback?.Cancel();
                    }
                }
                result.Add(spec.Key, measurement);
            }
            return result;
        }

        static Vector3 MeasureSweep(FrankBattlePairPlayback pair, CharacterCombat source,
            CharacterCombat target, float seconds, Vector3 palmPoint)
        {
            var hand = source.Animator.GetBoneTransform(HumanBodyBones.RightHand);
            var local = hand.InverseTransformPoint(palmPoint);
            pair.EvaluateAt(seconds - 1f / 120);
            var before = hand.TransformPoint(local);
            pair.EvaluateAt(seconds + 1f / 120);
            var after = hand.TransformPoint(local);
            pair.EvaluateAt(seconds);
            var direction = target.Animator.transform.InverseTransformDirection(after - before);
            if (!Finite(direction) || direction.sqrMagnitude < .00000001f)
                throw new InvalidOperationException("Measured SlapFace right-palm sweep is degenerate.");
            return direction.normalized;
        }

        static BattleSfxBank.Move MakeProfile(CombatTripletData move, Spec spec, Avatar victim, Vector3 offset)
        {
            return new BattleSfxBank.Move
            {
                label = spec.Id,
                attack = move.sourcePair.attack,
                reaction = move.sourcePair.reaction,
                cues = new[]
                {
                    new BattleSfxBank.Cue { seconds = spec.seconds - .1f, group = "light_swing" },
                    new BattleSfxBank.Cue
                    {
                        seconds = spec.seconds,
                        group = "light_hit",
                        hasContactPoint = true,
                        contactSource = "RightHand",
                        contactBone = HumanBodyBones.Head,
                        contactOffset = offset,
                        avatarContacts = new[]
                        {
                            new BattleSfxBank.ContactAnchor
                            {
                                avatar = victim,
                                bone = HumanBodyBones.Head,
                                offset = offset
                            }
                        }
                    }
                }
            };
        }

        static bool Within(float value, float maximum) => float.IsFinite(value) && value >= 0 && value <= maximum;

        static bool Finite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
