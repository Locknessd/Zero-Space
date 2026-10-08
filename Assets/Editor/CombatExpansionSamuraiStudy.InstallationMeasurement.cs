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
        internal static CombatExpansionSamuraiSetup.Measurement[] MeasureExecution01Installation(StringBuilder log)
        {
            var evidence = JsonUtility.FromJson<CAReport>(File.ReadAllText(CAOutput + "/Report.json"));
            ValidateInstallationEvidence(evidence);
            log.AppendLine("Evidence=" + CAOutput + "/Report.json; utc=" + evidence.utc);
            using var session = new SourceSession();
            InitializePairStudy(session.Fighters);
            var output = new List<CombatExpansionSamuraiSetup.Measurement>();
            foreach (var source in session.Fighters)
            {
                var target = session.Fighters.Single(f => f != source);
                var selected = evidence.cases.Single(c => c.attacker == source.name && c.direction == 1);
                var move = CombatExpansionSamuraiSetup.MakeExecution01Move(source, target);
                move.actionDefinition = null;
                var measured = InstallationProfile(move, selected, source, target);
                log.AppendLine("Source=" + source.name + "; target=" + target.name + "; attack=" + selected.attackClip +
                    "; reaction=" + selected.reactionClip + "; attackerAvatar=" + selected.attackerAvatar +
                    "; victimAvatar=" + selected.victimAvatar);
                log.AppendLine("Drivers=" + selected.attackerDriver + "; " + selected.victimDriver);
                log.AppendLine("Grounding=" + CombatExpansionSamuraiSetup.GroundingPath + "; " + evidence.grounding);
                log.AppendLine("Recovery=" + CombatExpansionInventory.Identity(move.getUpAnim) + "; grounding=" +
                    CombatExpansionSamuraiSetup.RecoveryPath + "; blend=0.12s; native duration=3.1666667s.");
                foreach (int direction in new[] { 1, -1 })
                {
                    var record = evidence.cases.Single(c => c.attacker == source.name && c.direction == direction);
                    ValidateInstallationIdentities(record, move, source, target, evidence.grounding);
                    foreach (var fighter in session.Fighters)
                        fighter.ResetCombat();
                    source.Animator.transform.SetPositionAndRotation(
                        Vector3.left * direction * move.attackRange * .5f,
                        Quaternion.LookRotation(Vector3.right * direction));
                    target.Animator.transform.SetPositionAndRotation(
                        Vector3.right * direction * move.attackRange * .5f,
                        Quaternion.LookRotation(Vector3.left * direction));
                    if (!source.ExecuteAttack(move, target) || source.SourcePlayback == null)
                        throw new InvalidOperationException("Execution01 installation preview pair rejected.");
                    var pair = source.SourcePlayback;
                    try
                    {
                        pair.EvaluateAt(0);
                        CheckPairWeapons(pair, pair.AttackerActor.Pose.weaponRenderers);
                        foreach (var fighter in session.Fighters)
                        foreach (var manager in fighter.GetComponentsInChildren<TrumpWeaponManager>(true))
                            if (!manager.IsUnarmedPresentation ||
                                manager.ActiveWeapon != TrumpWeaponManager.WeaponType.None)
                                throw new InvalidOperationException("Owned equipment suppression is missing.");
                        if (!pair.ReceiverActor.Pose.transferFingers)
                            throw new InvalidOperationException("Receiver fingers were not transferred.");
                        using var sword = new SwordRegion(pair);
                        var skins = CASkins(target);
                        var attackerSkins = CASkins(source);
                        for (int index = 0; index < 2; index++)
                            ValidateInstallationContact(pair, sword, skins, attackerSkins, target,
                                record.contacts[index], measured.profile.cues.Where(c => c.hasContactPoint)
                                    .ElementAt(index), log, direction);
                        MeasureInstallationLanding(pair, sword, skins, attackerSkins, target, record,
                            measured.profile.cues.Single(c => c.finalLanding), direction, log);
                    }
                    finally
                    {
                        pair.Cancel();
                    }
                }
                output.Add(measured);
            }
            return output.ToArray();
        }

        static CombatExpansionSamuraiSetup.Measurement InstallationProfile(CombatTripletData move,
            CACase record, CharacterCombat source, CharacterCombat target)
        {
            var cues = record.contacts.Select((contact, index) => InstallationCue(contact.seconds,
                index == 0 ? "stab_hit" : "heavy_hit", target, contact.chosenBone,
                contact.targetBoneLocalPoint, "Weapon")).ToList();
            cues.Add(new BattleSfxBank.Cue { seconds = .27f, group = "thrust_swing" });
            cues.Add(new BattleSfxBank.Cue { seconds = 1.9f, group = "blade_swing" });
            cues.Add(new BattleSfxBank.Cue
            {
                seconds = source.name == "Mankey" ? 2.666666746f : 2.712500095f,
                group = "body_fall",
                finalLanding = true,
                damageOnLanding = false,
                hasContactPoint = true,
                contactSource = "Body"
            });
            string second = source.name == "Mankey"
                ? "LeftArm; LeftShoulder representative anchor on Pepe"
                : "Mixed AxialTorso/LeftArm zero-gap ties; UpperChest representative anchor on Mankey";
            return new CombatExpansionSamuraiSetup.Measurement
            {
                attackerName = source.name,
                attacker = source.Animator.avatar,
                victim = target.Animator.avatar,
                profile = new BattleSfxBank.Move
                {
                    label = CombatExpansionSamuraiSetup.Id,
                    attack = move.attackAnim,
                    reaction = move.hitAnim,
                    cues = cues.OrderBy(c => c.seconds).ToArray()
                },
                directions = record.contacts.Select(c => c.relativeBladeVelocityVictimMps.normalized)
                    .Append(Vector3.down).ToArray(),
                regions = new[] { "AxialTorso; UpperChest representative anchor on " + target.name, second,
                    source.name == "Mankey" ? "Pepe LeftUpperArm supporting skin vertex"
                        : "Mankey Head supporting skin vertex" },
                evidence = "Evidence " + CAOutput + "/Report.json; exact native identities validated. " +
                    "All triangle-pair ties within 0.00001m remain in the report; unsigned zero-gap minima do not " +
                    "distinguish touching from intersection. Opening contact is AxialTorso; second contact: " +
                    second + ". Representative selection never erases mixed anatomical ties."
            };
        }

        static BattleSfxBank.Cue InstallationCue(float time, string group, CharacterCombat target,
            string bone, Vector3 offset, string contactSource)
        {
            var human = (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), bone);
            return new BattleSfxBank.Cue
            {
                seconds = time,
                group = group,
                hasContactPoint = true,
                contactBone = human,
                contactOffset = offset,
                contactSource = contactSource,
                avatarContacts = new[] { new BattleSfxBank.ContactAnchor
                {
                    avatar = target.Animator.avatar,
                    bone = human,
                    offset = offset
                } }
            };
        }

        static void ValidateInstallationContact(FrankBattlePairPlayback pair, SwordRegion sword, CASkin[] skins,
            CASkin[] attackerSkins, CharacterCombat target, CAContact expected, BattleSfxBank.Cue cue,
            StringBuilder log, int direction)
        {
            var actual = CAMeasureContact(pair, sword, skins, attackerSkins, target, expected.seconds);
            if (actual.chosenBone != expected.chosenBone ||
                Vector3.Distance(actual.targetBoneLocalPoint, expected.targetBoneLocalPoint) > .0001f ||
                Vector3.Distance(actual.relativeBladeVelocityVictimMps,
                    expected.relativeBladeVelocityVictimMps) > .01f ||
                Mathf.Abs(actual.minimumGapM - expected.minimumGapM) > .001f)
                throw new InvalidOperationException("Execution01 contact evidence no longer matches source playback.");
            pair.EvaluateAt(expected.seconds);
            if (!cue.TryContactPosition(target.Animator, out var world) ||
                Vector3.Distance(world, actual.tiedTriangles[0].targetWorldPoint) > .001f)
                throw new InvalidOperationException("Stored blade contact anchor misses its selected skin point.");
            log.AppendLine(FormattableString.Invariant($"Contact victim={target.name} direction={direction} ") +
                FormattableString.Invariant($"time={cue.seconds:R} bone={cue.contactBone} source=Weapon; ") +
                "corrected relative velocity and stored selected-bone anchor validated.");
        }

        static void MeasureInstallationLanding(FrankBattlePairPlayback pair, SwordRegion sword, CASkin[] skins,
            CASkin[] attackerSkins, CharacterCombat target, CACase record, BattleSfxBank.Cue cue,
            int direction, StringBuilder log)
        {
            var expected = record.landing.Single(r => Mathf.Abs(r.seconds - cue.seconds) < .000001f);
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            var actual = CALandingFrame(pair, sword, skins, attackerSkins, target, cue.seconds, ref bounds);
            if (actual.supportBone != expected.supportBone || actual.supportMesh != expected.supportMesh ||
                actual.supportRenderer != expected.supportRenderer || actual.supportVertex != expected.supportVertex ||
                actual.supportTriangle != expected.supportTriangle ||
                Vector3.Distance(actual.supportWorldPoint, expected.supportWorldPoint) > .001f ||
                actual.bodySupportMinimumY < .009f || actual.bodySupportMinimumY > .012f)
                throw new InvalidOperationException(
                    "Execution01 supporting skin vertex differs from reviewed landing.");
            var bone = (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), actual.supportBone);
            var transform = target.Animator.GetBoneTransform(bone);
            var offset = transform.InverseTransformPoint(actual.supportWorldPoint);
            if (direction == 1)
            {
                cue.contactBone = bone;
                cue.contactOffset = offset;
                cue.avatarContacts = new[] { new BattleSfxBank.ContactAnchor
                {
                    avatar = target.Animator.avatar,
                    bone = bone,
                    offset = offset
                } };
            }
            if (cue.contactBone != bone || !cue.TryContactPosition(target.Animator, out var point) ||
                Vector3.Distance(point, actual.supportWorldPoint) > .001f)
                throw new InvalidOperationException("Stored landing anchor fails the opposite lane direction.");
            pair.EvaluateAt(pair.Duration);
            pair.EvaluateAt(cue.seconds);
            if (!cue.TryContactPosition(target.Animator, out var repeated) || Vector3.Distance(point, repeated) > .001f)
                throw new InvalidOperationException("Landing anchor is not stable after backward seeking.");
            log.AppendLine(FormattableString.Invariant($"Landing victim={target.name} direction={direction} ") +
                FormattableString.Invariant($"time={cue.seconds:R} supportY={point.y:R} bone={bone} ") +
                FormattableString.Invariant($"local=({offset.x:R},{offset.y:R},{offset.z:R}) ") +
                $"mesh={actual.supportMesh} renderer={actual.supportRenderer} " +
                $"triangle={actual.supportTriangle} vertex={actual.supportVertex}; non-damaging, finalLanding.");
        }
    }
}
