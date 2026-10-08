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
        public static void CaptureRemainingExecutionContacts()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            Directory.CreateDirectory(CCOutput);
            string statusPath = CCOutput + "/Status.txt";
            var progress = new List<string> { "RUNNING Execution02..10; partial evidence only." };
            File.WriteAllLines(statusPath, progress);
            var failures = new List<Exception>();
            for (int execution = 2; execution <= 10; execution++)
            {
                try
                {
                    CCCaptureExecution(execution);
                    progress.Add(GroundingExecutionName(execution) + " CAPTURED; candidates unapproved.");
                }
                catch (Exception error)
                {
                    progress.Add(GroundingExecutionName(execution) + " FAILED: " + error.Message);
                    failures.Add(error);
                }
                File.WriteAllLines(statusPath, progress);
            }
            progress[0] = failures.Count == 0 ? "CAPTURED all nine executions; candidates unapproved." :
                "FAILED collection; individual reports identify partial and successful executions.";
            File.WriteAllLines(statusPath, progress);
            if (failures.Count > 0)
                throw new AggregateException("Remaining Samurai contact capture failed.", failures);
        }

        public static void CaptureExecution02Contacts()
        {
            CCCaptureExecution(2);
        }

        static void CCCaptureExecution(int execution)
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            string output = CCOutput + "/" + GroundingExecutionName(execution);
            Directory.CreateDirectory(output);
            var report = new CCReport { execution = execution };
            CCFlush(output, report);
            File.WriteAllText(output + "/Scope.txt", CCScope);
            try
            {
                var grounding = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(GroundingAssetPath(execution));
                if (!grounding)
                    throw new InvalidOperationException("Bake grounding first: " + GroundingAssetPath(execution));
                report.grounding = CombatExpansionInventory.Identity(grounding);
                report.sources = ResolveSources().Where(s => s.localId == 7400000 + execution * 2).ToArray();
                using var session = new SourceSession();
                InitializePairStudy(session.Fighters);
                CheckGroundingFighters(session.Fighters);
                CheckGroundingTracks(grounding.tracks, session.Fighters);
                foreach (var source in session.Fighters)
                foreach (int direction in new[] { 1, -1 })
                {
                    var record = new CCCase { grounding = report.grounding };
                    report.cases.Add(record);
                    CCFlush(output, report);
                    CCCapturePair(session.Fighters, source, direction, execution, grounding, output, record);
                    CCFlush(output, report);
                }
                report.status = "CAPTURED four cases; candidates and recovery remain unapproved.";
            }
            catch (Exception error)
            {
                report.status = "FAILED; partial evidence is not a completed capture.";
                report.error = error.ToString();
                throw;
            }
            finally
            {
                CCFlush(output, report);
            }
        }

        static void CCFlush(string output, CCReport report)
        {
            report.caseFiles = report.cases.Select(c => c.file).ToArray();
            report.caseStatuses = report.cases.Select(c => c.status).ToArray();
            File.WriteAllText(output + "/Report.json", JsonUtility.ToJson(report, true));
            File.WriteAllText(output + "/Status.txt", report.status + "\nCompleted cases=" +
                report.cases.Count(c => c.status == "CAPTURED") + "/4\n" + report.error);
        }

        static void CCCapturePair(CharacterCombat[] fighters, CharacterCombat source, int direction,
            int execution, FrankPairGrounding grounding, string output, CCCase record)
        {
            var target = fighters.Single(f => f != source);
            FrankBattlePairPlayback pair = null;
            string stem = source.name + "_" + target.name + "_" + (direction > 0 ? "Positive" : "Negative");
            record.file = stem + ".json";
            try
            {
                pair = BeginBladeStudy(fighters, source, target, execution, direction, grounding, 1.7f);
                CCValidatePair(pair, grounding, execution);
                pair.EvaluateAt(0);
                CheckPairWeapons(pair, pair.AttackerActor.Pose.weaponRenderers);
                using var sword = new SwordRegion(pair);
                var skins = CASkins(target);
                var attackerSkins = CASkins(source);
                record.anatomy = CANewCase(pair, source, target, direction, 1.7f, sword, skins);
                record.attackDurationSeconds = pair.Move.sourcePair.attack.length;
                record.reactionDurationSeconds = pair.Move.sourcePair.reaction.length;
                record.attackSourceAvatar = CombatExpansionInventory.Identity(
                    pair.Move.sourcePair.attackerDriver.pose.sourceHumanAvatar);
                record.reactionSourceAvatar = CombatExpansionInventory.Identity(
                    pair.Move.sourcePair.receiverDriver.pose.sourceHumanAvatar);
                var probe = new CombatExpansionAxeDenseStudy.MovingBodyProbe(target);
                var frames = new SortedDictionary<float, CCFrame>();
                var bounds = new Bounds(Vector3.zero, Vector3.zero);
                var diagnostics = new List<string>();
                var csvPath = output + "/" + stem + "_Timeline.csv";
                using (var csv = new StreamWriter(csvPath, false))
                {
                    csv.AutoFlush = true;
                    csv.WriteLine(CCHeader);
                    Action<float, bool> sample = (seconds, coarse) =>
                    {
                        if (frames.ContainsKey(seconds))
                            return;
                        var frame = CCMeasure(pair, sword, skins, attackerSkins, target, probe,
                            seconds, coarse, diagnostics, ref bounds);
                        frames.Add(seconds, frame);
                        CCWriteFrame(csv, frame);
                        if (frames.Count == 1)
                            record.geometryDiagnostics.AddRange(diagnostics);
                    };
                    foreach (float seconds in CCTimes(0, pair.Duration, 30))
                        sample(seconds, true);
                    record.coarseSamples = frames.Count;
                    record.refinementWindows = CCDiscover(frames.Values.ToArray(), pair.Duration);
                    foreach (var window in record.refinementWindows)
                    foreach (float seconds in CCTimes(window.startSeconds, window.endSeconds, 240))
                        sample(seconds, false);
                }
                // Replace acquisition-order partial CSV with the complete ordered timeline.
                using (var csv = new StreamWriter(csvPath, false))
                {
                    csv.WriteLine(CCHeader);
                    foreach (var frame in frames.Values)
                        CCWriteFrame(csv, frame);
                }
                record.totalSamples = frames.Count;
                record.minimumAttackerY = frames.Values.Min(f => f.landing.attackerMinimumY);
                record.minimumReceiverY = frames.Values.Min(f => f.landing.victimMinimumY);
                record.anatomy.landing = frames.Values.Select(f => f.landing).ToList();
                CASelectLanding(record.anatomy);
                record.finalFrame = frames.Values.Last().landing;
                record.minimumSupportFrame = frames.Values.OrderBy(f => f.landing.bodySupportMinimumY).First().landing;
                record.minimumTorsoFrame = frames.Values.OrderBy(f => f.landing.torsoMinimumY).First().landing;
                record.anatomy.landing.Clear();
                record.candidateIntervals = CCIntervals(frames, record.refinementWindows);
                foreach (float seconds in record.candidateIntervals.Select(i => i.representativeSeconds).Distinct())
                    record.anatomy.contacts.Add(CCContact(pair, sword, skins, attackerSkins, target, seconds));
                CCVerifyAndRender(pair, sword, skins, attackerSkins, source, target, fighters, probe,
                    frames, record, bounds, output, stem);
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
                    if (pair)
                        pair.Cancel();
                    else if (source.SourcePlayback)
                        source.SourcePlayback.Cancel();
                }
                catch (Exception error)
                {
                    record.status = "FAILED cleanup";
                    record.error += "\n" + error;
                    throw;
                }
                finally
                {
                    File.WriteAllText(output + "/" + stem + ".json", JsonUtility.ToJson(record, true));
                }
            }
        }

        static void CCValidatePair(FrankBattlePairPlayback pair, FrankPairGrounding grounding, int execution)
        {
            var native = pair.Move.sourcePair;
            long id = 7400000 + execution * 2;
            if (CombatExpansionInventory.Identity(native.attack) != Guids[0] + ":" + id ||
                CombatExpansionInventory.Identity(native.reaction) != Guids[1] + ":" + id ||
                pair.Move.grounding != grounding || !float.IsFinite(pair.Duration) || pair.Duration <= 0 ||
                Mathf.Abs(pair.Duration - Mathf.Max(native.attack.length, native.reaction.length)) > .0001f)
                throw new InvalidOperationException("Unexpected source identity, grounding or full duration.");
            foreach (var track in grounding.tracks)
                if (Mathf.Abs(track.duration - pair.Duration) > .0001f)
                    throw new InvalidOperationException("Stale grounding duration.");
        }
    }
}
