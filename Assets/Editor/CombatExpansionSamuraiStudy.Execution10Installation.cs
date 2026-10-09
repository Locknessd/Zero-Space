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
        internal static CombatExpansionSamurai10Setup.Measurement[] MeasureExecution10Installation(StringBuilder log)
        {
            using var guard = new Execution10InstallationGuard();
            try
            {
                var evidence = E10InstallationEvidence();
                var recovery = JsonUtility.FromJson<E10Report>(File.ReadAllText(E10Output + "/Report.json"));
                log.AppendLine("Contact report=" + Execution10ContactsOutput + "/Report.json; utc=" + evidence.utc);
                log.AppendLine("Recovery report=" + E10Output + "/Report.json");
                using var session = new SourceSession();
                InitializePairStudy(session.Fighters);
                CheckGroundingFighters(session.Fighters);
                var output = new List<CombatExpansionSamurai10Setup.Measurement>();
                foreach (var source in session.Fighters)
                {
                    var target = session.Fighters.Single(f => f != source);
                    var record = evidence.cases.Single(c => c.attacker == source.name && c.direction == 1);
                    var move = CombatExpansionSamurai10Setup.MakeExecution10Move(source, target);
                    move.actionDefinition = null;
                    var measured = E10InstallationProfile(move, record, source, target);
                    CheckGroundingTracks(move.grounding.tracks, session.Fighters);
                    log.AppendLine("Source=" + source.name + "; target=" + target.name + "; attack=" +
                        record.attackClip +
                        "; reaction=" + record.reactionClip + "; attackerAvatar=" + record.attackerAvatar +
                        "; victimAvatar=" + record.victimAvatar + "; drivers=" + record.attackerDriver + "; " +
                        record.victimDriver + "; grounding=" + evidence.grounding);
                    foreach (int direction in new[] { 1, -1 })
                    {
                        record = evidence.cases.Single(c => c.attacker == source.name && c.direction == direction);
                        ValidateInstallationIdentities(record, move, source, target, evidence.grounding);
                        E10ValidateRecoveryEvidence(recovery, record, move, target);
                        E10RecheckInstallation(session.Fighters, source, target, move, record, measured, log);
                    }
                    output.Add(measured);
                }
                return output.ToArray();
            }
            finally
            {
                guard.VerifyBeforeCommit();
            }
        }

        static CombatExpansionSamurai10Setup.Measurement E10InstallationProfile(CombatTripletData move,
            CACase record, CharacterCombat source, CharacterCombat target)
        {
            var contact = record.contacts.Single();
            var landing = new BattleSfxBank.Cue
            {
                seconds = CombatExpansionSamurai10Setup.LandingTime(source.name),
                group = "body_fall",
                finalLanding = false,
                damageOnLanding = true,
                hasContactPoint = true,
                contactSource = "Body"
            };
            var stab = InstallationCue(contact.seconds, "stab_hit", target, contact.chosenBone,
                contact.targetBoneLocalPoint, "Weapon");
            return new CombatExpansionSamurai10Setup.Measurement
            {
                attackerName = source.name,
                attacker = source.Animator.avatar,
                victim = target.Animator.avatar,
                profile = new BattleSfxBank.Move
                {
                    label = CombatExpansionSamurai10Setup.Id,
                    attack = move.attackAnim,
                    reaction = move.hitAnim,
                    cues = new[]
                    {
                        new BattleSfxBank.Cue { seconds = 1.2f, group = "grapple_release" },
                        landing,
                        new BattleSfxBank.Cue { seconds = 1.8f, group = "thrust_swing" },
                        stab
                    }
                },
                directions = new[] { Vector3.zero, contact.relativeBladeVelocityVictimMps.normalized },
                regions = new[]
                {
                    "Pending exact non-leg support vertex measurement",
                    string.Join("/", contact.tiedTriangles.SelectMany(t => t.dominantGroups).Distinct()) +
                        "; Head representative anchor on " + target.name
                },
                evidence = "Evidence " + Execution10ContactsOutput + "/Report.json; original clip, driver, Avatar, " +
                    "mesh and grounding identities verified. Exact unsigned blade triangle minimum and all ties " +
                    "within 0.00001m retained; representative Head does not erase anatomical ambiguity. " +
                    "Selected landing is first reviewed cushioned non-leg support, not the later global minimum."
            };
        }

        static void E10RecheckInstallation(CharacterCombat[] fighters, CharacterCombat source, CharacterCombat target,
            CombatTripletData move, CACase record, CombatExpansionSamurai10Setup.Measurement measured, StringBuilder log)
        {
            foreach (var fighter in fighters)
                fighter.ResetCombat();
            source.Animator.transform.SetPositionAndRotation(Vector3.left * record.direction * .85f,
                Quaternion.LookRotation(Vector3.right * record.direction));
            target.Animator.transform.SetPositionAndRotation(Vector3.right * record.direction * .85f,
                Quaternion.LookRotation(Vector3.left * record.direction));
            if (!source.ExecuteAttack(move, target) || source.SourcePlayback == null)
                throw new InvalidOperationException("Execution10 installation preview pair rejected.");
            var pair = source.SourcePlayback;
            try
            {
                CCValidatePair(pair, move.grounding, 10);
                pair.EvaluateAt(0);
                CheckPairWeapons(pair, pair.AttackerActor.Pose.weaponRenderers);
                foreach (var fighter in fighters)
                foreach (var manager in fighter.GetComponentsInChildren<TrumpWeaponManager>(true))
                    if (!manager.IsUnarmedPresentation || manager.ActiveWeapon != TrumpWeaponManager.WeaponType.None)
                        throw new InvalidOperationException("Execution10 owned equipment suppression missing.");
                if (!pair.ReceiverActor.Pose.transferFingers ||
                    Mathf.Abs(pair.Duration - CombatExpansionSamurai10Setup.Duration) > .000001f)
                    throw new InvalidOperationException("Execution10 duration or receiver finger transfer changed.");
                using var sword = new SwordRegion(pair);
                var skins = CASkins(target);
                var attackerSkins = CASkins(source);
                if (!skins.Select(s => s.triangles.Length / 3).SequenceEqual(record.targetSkinTriangleCounts))
                    throw new InvalidOperationException("Execution10 skin triangle counts changed.");
                E10MeasureInstallationLanding(pair, sword, skins, attackerSkins, target, record, measured, log);
                var stab = measured.profile.cues.Single(c => c.group == "stab_hit");
                ValidateInstallationContact(pair, sword, skins, attackerSkins, target,
                    record.contacts.Single(), stab, log, record.direction);
                pair.EvaluateAt(pair.Duration);
                ValidateInstallationContact(pair, sword, skins, attackerSkins, target,
                    record.contacts.Single(), stab, log, record.direction);
                var actual = CAMeasureContact(pair, sword, skins, attackerSkins, target, stab.seconds);
                E10FiniteEvidence(actual);
                var expected = record.contacts.Single();
                if (!actual.tiedTriangles.Select(E10TriangleKey).OrderBy(k => k)
                    .SequenceEqual(expected.tiedTriangles.Select(E10TriangleKey).OrderBy(k => k)))
                    throw new InvalidOperationException("Execution10 reviewed triangle/anatomy ties changed.");
                log.AppendLine("PASS current runtime both contact anchors within 1mm; attacker=" + source.name +
                    "; direction=" + record.direction + "; no finalLanding cue.");
            }
            finally
            {
                pair.Cancel();
            }
        }

        static string E10TriangleKey(CATriangle triangle) => triangle.mesh + "|" + triangle.renderer + "|" +
            triangle.targetTriangle + "|" + triangle.bladeRegionTriangle + "|" +
                string.Join(",", triangle.dominantGroups);
    }
}
