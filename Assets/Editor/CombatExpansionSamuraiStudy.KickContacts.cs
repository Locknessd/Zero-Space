using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        public static void CaptureExecution08KickContacts()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            Directory.CreateDirectory(KCOutput);
            var report = new KCReport();
            KCFlush(report);
            File.WriteAllText(KCOutput + "/Scope.txt", KCScope);
            try
            {
                var grounding = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(GroundingAssetPath(8));
                if (!grounding)
                    throw new InvalidOperationException("Missing Execution08 grounding: " + GroundingAssetPath(8));
                report.grounding = CombatExpansionInventory.Identity(grounding);
                using (var session = new SourceSession())
                {
                    InitializePairStudy(session.Fighters);
                    CheckGroundingFighters(session.Fighters);
                    CheckGroundingTracks(grounding.tracks, session.Fighters);
                    // Predeclare all four cases so a failure cannot hide pending coverage.
                    foreach (var source in session.Fighters)
                    foreach (int direction in new[] { 1, -1 })
                    {
                        var target = session.Fighters.Single(f => f != source);
                        report.cases.Add(new KCCase
                        {
                            attacker = source.name, victim = target.name, direction = direction,
                            grounding = report.grounding,
                            file = source.name + "_" + target.name + "_" +
                                (direction > 0 ? "Positive" : "Negative")
                        });
                    }
                    foreach (var record in report.cases)
                    {
                        var source = session.Fighters.Single(f => f.name == record.attacker);
                        KCCapturePair(session.Fighters, source, grounding, record, report);
                    }
                }
                if (report.cases.Count != 4 || report.cases.Any(c => c.status != "CAPTURED"))
                    throw new InvalidOperationException("Incomplete Execution08 kick coverage.");
                report.status = "CAPTURED four cases; all candidates UNAPPROVED.";
            }
            catch (Exception error)
            {
                report.status = "FAILED; partial evidence is not a complete capture.";
                report.error = error.ToString();
                throw;
            }
            finally { KCFlush(report); }
        }

        static void KCCapturePair(CharacterCombat[] fighters, CharacterCombat source,
            FrankPairGrounding grounding, KCCase record, KCReport report)
        {
            var target = fighters.Single(f => f != source);
            FrankBattlePairPlayback pair = null;
            record.status = "RUNNING";
            KCFlush(report);
            try
            {
                pair = BeginBladeStudy(fighters, source, target, 8, record.direction, grounding, 1.7f);
                CCValidatePair(pair, grounding, 8);
                if (pair.Duration <= KCEnd || pair.Move.sourcePair.receiverOffset != Vector3.forward * 1.7f)
                    throw new InvalidOperationException("Execution08 window/native spacing mismatch.");
                pair.EvaluateAt(0);
                CheckPairWeapons(pair, pair.AttackerActor.Pose.weaponRenderers);
                KCIdentity(pair, source, target, record);
                using (var probes = new KCProbes(source, target, record))
                {
                    var nodes = PairNodes(pair, source, target);
                    var bounds = new Bounds(source.Animator.transform.position, Vector3.zero);
                    using (var csv = new StreamWriter(KCOutput + "/" + record.file + ".csv", false))
                    {
                        csv.AutoFlush = true;
                        csv.WriteLine(KCHeader);
                        foreach (float time in KCTimes())
                        {
                            var frame = new KCFrame { seconds = time };
                            record.samples.Add(frame);
                            KCMeasure(pair, fighters, source, target, probes, nodes, frame, ref bounds);
                            record.minimumAttackerFloorClearanceM = record.floorMinimaAvailable
                                ? Mathf.Min(record.minimumAttackerFloorClearanceM, frame.attackerFloorClearanceM)
                                : frame.attackerFloorClearanceM;
                            record.minimumVictimFloorClearanceM = record.floorMinimaAvailable
                                ? Mathf.Min(record.minimumVictimFloorClearanceM, frame.victimFloorClearanceM)
                                : frame.victimFloorClearanceM;
                            record.floorMinimaAvailable = true;
                            KCWriteFrame(csv, frame);
                            KCFlush(report);
                        }
                    }
                    KCSelectCandidates(record);
                    KCVerify(pair, fighters, source, target, probes, nodes, record, report, ref bounds);
                    KCRender(pair, fighters, source, record, report, bounds);
                }
                record.status = "CAPTURED";
            }
            catch (Exception error)
            {
                record.status = "FAILED";
                record.error = error.ToString();
                throw;
            }
            finally
            {
                try
                {
                    if (pair) pair.Cancel();
                    else if (source.SourcePlayback) source.SourcePlayback.Cancel();
                }
                catch (Exception error)
                {
                    record.status = "FAILED cleanup";
                    record.error += "\n" + error;
                    throw;
                }
                finally { KCFlush(report); }
            }
        }

        static void KCIdentity(FrankBattlePairPlayback pair, CharacterCombat source,
            CharacterCombat target, KCCase record)
        {
            var native = pair.Move.sourcePair;
            record.durationSeconds = pair.Duration;
            record.attackClip = CombatExpansionInventory.Identity(native.attack);
            record.reactionClip = CombatExpansionInventory.Identity(native.reaction);
            record.attackDurationSeconds = native.attack.length;
            record.reactionDurationSeconds = native.reaction.length;
            record.attackDriver = CombatExpansionInventory.Identity(native.attackerDriver);
            record.reactionDriver = CombatExpansionInventory.Identity(native.receiverDriver);
            record.attackerAvatar = CombatExpansionInventory.Identity(source.Animator.avatar);
            record.victimAvatar = CombatExpansionInventory.Identity(target.Animator.avatar);
            record.attackSourceAvatar = CombatExpansionInventory.Identity(native.attackerDriver.pose.sourceHumanAvatar);
            record.reactionSourceAvatar = CombatExpansionInventory.Identity(native.receiverDriver.pose.sourceHumanAvatar);
        }

        static void KCFlush(KCReport report)
        {
            File.WriteAllText(KCOutput + "/Report.json", JsonUtility.ToJson(report, true));
            foreach (var record in report.cases)
                File.WriteAllText(KCOutput + "/" + record.file + ".json", JsonUtility.ToJson(record, true));
            File.WriteAllText(KCOutput + "/Status.txt", report.status + "\nCompleted cases=" +
                report.cases.Count(c => c.status == "CAPTURED") + "/4\n" + report.error);
        }
    }
}
