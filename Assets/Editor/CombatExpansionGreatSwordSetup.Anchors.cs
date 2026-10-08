using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordSetup
    {
        sealed class ContactSample
        {
            public Vector3 world;
            public readonly Dictionary<HumanBodyBones, Matrix4x4> bones =
                new Dictionary<HumanBodyBones, Matrix4x4>();
        }

        static BattleSfxBank.Move[] MeasureProfiles(StringBuilder report)
        {
            var profiles = new BattleSfxBank.Move[Specs.Length];
            var positioning = CombatPositioningController.Instance;
            var property = typeof(CombatPositioningController).GetProperty("Instance");
            var scene = default(UnityEngine.SceneManagement.Scene);
            try
            {
                scene = EditorSceneManager.OpenPreviewScene(CombatExpansionInventory.Battle);
                property.SetValue(null, null);
                var game = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var fighter in fighters)
                {
                    fighter.battleSfx = null;
                    fighter.battleVfx = null;
                    fighter.hitEffect = null;
                    fighter.Initialize();
                }
                if (fighters.Any(f => !f.Animator || !f.Animator.avatar) ||
                    fighters[0].Animator.avatar == fighters[1].Animator.avatar)
                    throw new InvalidOperationException("Two distinct saved fighter avatars are required.");
                foreach (var source in fighters)
                foreach (var spec in Specs)
                {
                    var target = fighters.Single(f => f != source);
                    var samples = new ContactSample[spec.contacts.Length];
                    foreach (int direction in new[] { 1, -1 })
                    {
                        foreach (var fighter in fighters)
                            fighter.ResetCombat();
                        var move = CombatExpansionGreatSwordStudy.MakeMove(source, target, spec.index);
                        ValidateMove(move, spec);
                        source.Animator.transform.position = Vector3.left * direction * move.attackRange * .5f;
                        target.Animator.transform.position = Vector3.right * direction * move.attackRange * .5f;
                        try
                        {
                            if (!source.ExecuteAttack(move, target) || source.SourcePlayback == null)
                                throw new InvalidOperationException("Cannot measure " + move.moveName);
                            var pair = source.SourcePlayback;
                            if (spec.landingSeconds > pair.Duration)
                                throw new InvalidOperationException("Landing exceeds source pair duration.");
                            var profile = profiles[spec.index];
                            if (profile == null)
                            {
                                profile = MakeProfile(move, spec);
                                profiles[spec.index] = profile;
                            }
                            if (profile.attack != move.attackAnim || profile.reaction != move.hitAnim)
                                throw new InvalidOperationException("Source clip identity differs across avatars.");
                            var contacts = profile.cues.Where(c => c.hasContactPoint).ToArray();
                            for (int i = 0; i < spec.contacts.Length; i++)
                            {
                                pair.EvaluateAt(spec.contacts[i]);
                                if (!BattlePresentationContactSetup.TryMeasureContact(pair, source, target, "Weapon",
                                    out var bone, out var offset, out float gap, out var world) ||
                                    !float.IsFinite(gap) || gap < 0 || gap > .12f || !Finite(offset) || !Finite(world))
                                    throw new InvalidOperationException(move.moveName + " invalid contact " + i +
                                        " on " + target.name + "; gap=" + gap);
                                var transform = target.Animator.GetBoneTransform(bone);
                                if (!transform)
                                    throw new InvalidOperationException("Measured victim bone is missing.");
                                var cue = contacts[i];
                                float crossGap = 0;
                                if (direction == 1)
                                {
                                    var sample = new ContactSample { world = world };
                                    for (int b = 0; b < (int)HumanBodyBones.LastBone; b++)
                                    {
                                        var anchor = target.Animator.GetBoneTransform((HumanBodyBones)b);
                                        if (anchor)
                                            sample.bones.Add((HumanBodyBones)b, anchor.localToWorldMatrix);
                                    }
                                    samples[i] = sample;
                                    if (cue.avatarContacts.Any(a => a.avatar == target.Animator.avatar))
                                        throw new InvalidOperationException("Repeated victim avatar anchor.");
                                    cue.avatarContacts = cue.avatarContacts.Append(new BattleSfxBank.ContactAnchor
                                    {
                                        avatar = target.Animator.avatar,
                                        bone = bone,
                                        offset = offset
                                    }).ToArray();
                                    if (cue.avatarContacts.Length == 1)
                                    {
                                        cue.contactBone = bone;
                                        cue.contactOffset = offset;
                                    }
                                }
                                else
                                {
                                    var anchor = cue.avatarContacts.Single(a => a.avatar == target.Animator.avatar);
                                    var savedBone = target.Animator.GetBoneTransform(anchor.bone);
                                    var sample = samples[i];
                                    if (!savedBone || !sample.bones.TryGetValue(bone, out var forwardMatrix))
                                        throw new InvalidOperationException("Cannot cross-project victim anchors.");
                                    crossGap = Mathf.Max(
                                        Vector3.Distance(savedBone.TransformPoint(anchor.offset), world),
                                        Vector3.Distance(forwardMatrix.MultiplyPoint3x4(offset), sample.world));
                                    if (!float.IsFinite(crossGap) || crossGap > .025f)
                                        throw new InvalidOperationException(move.moveName +
                                            " facing anchor mismatch: " + crossGap + "m on " + target.name +
                                            " contact " + i);
                                }
                                report.AppendLine(FormattableString.Invariant(
                                    $"{move.moveName}/{i + 1} attacker={source.name} victim={target.name} ") +
                                    FormattableString.Invariant(
                                        $"direction={direction} seconds={spec.contacts[i]:R} bone={bone} ") +
                                    FormattableString.Invariant($"gap={gap:R}m crossProjection={crossGap:R}m ") +
                                    FormattableString.Invariant($"offset=({offset.x:R},{offset.y:R},{offset.z:R})"));
                            }
                        }
                        finally
                        {
                            source.SourcePlayback?.Cancel();
                        }
                    }
                }
                if (profiles.Any(p => p == null || p.cues.Where(c => c.hasContactPoint)
                    .Any(c => c.avatarContacts.Length != fighters.Length)))
                    throw new InvalidOperationException("Incomplete GreatSword avatar contact coverage.");
                return profiles;
            }
            finally
            {
                try
                {
                    if (scene.IsValid())
                        EditorSceneManager.ClosePreviewScene(scene);
                }
                finally
                {
                    property.SetValue(null, positioning);
                }
            }
        }

        static BattleSfxBank.Move MakeProfile(CombatTripletData move, Spec spec)
        {
            var cues = new List<BattleSfxBank.Cue>();
            for (int i = 0; i < spec.contacts.Length; i++)
                cues.Add(new BattleSfxBank.Cue
                {
                    seconds = spec.contacts[i],
                    group = spec.groups[i],
                    hasContactPoint = true,
                    contactSource = "Weapon"
                });
            for (int i = 0; i < spec.swings.Length; i++)
                cues.Add(new BattleSfxBank.Cue
                {
                    seconds = spec.swings[i],
                    group = spec.index == 1 || spec.index == 2 ?
                        i == 0 ? "thrust_swing" : "heavy_swing" : "blade_swing"
                });
            cues.Add(new BattleSfxBank.Cue
            {
                seconds = spec.landingSeconds,
                group = "body_fall",
                finalLanding = true,
                damageOnLanding = false
            });
            return new BattleSfxBank.Move
            {
                label = move.moveName,
                attack = move.attackAnim,
                reaction = move.hitAnim,
                cues = cues.OrderBy(c => c.seconds).ToArray()
            };
        }
    }
}
