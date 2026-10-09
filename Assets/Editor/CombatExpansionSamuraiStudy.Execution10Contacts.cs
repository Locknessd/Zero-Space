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
        const string Execution10ContactsOutput =
            "GeneratedAssets/CombatExpansion/SamuraiStudy/Execution10ContactAuthoring";

        public static void CaptureExecution10ContactAuthoring()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            Directory.CreateDirectory(Execution10ContactsOutput);
            var grounding = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(GroundingAssetPath(10));
            if (!grounding)
                throw new InvalidOperationException("Execution10 grounding is required.");
            string snapshot = NativeReferenceSourceSnapshot(ResolveSources());
            var guarded = NativeReferenceGuard(ResolveSources());
            var report = new CAReport
            {
                scope = "Execution10 provisional contact authoring on four actual fighter/lane pairs. " +
                    "Native 1.7m relationship and full source duration retained. Grab then throw then downward stab. " +
                    "Early blade overlap during the grab is excluded from damaging sword candidates. " +
                    "One selected downward blade candidate per actor; ground support sampled at 240Hz from " +
                    "1.3s through full duration, including independently selected first cushioned landing poses. " +
                    "Selected landing seconds are Mankey attacker 1.45, Pepe attacker 1.4625; not global minima. " +
                    "Source seconds; world metres; Y=0 floor. Exact unsigned blade triangle minimum and all " +
                    "anatomical ties retained. Relative velocity subtracts the selected target anchor velocity. " +
                    "Non-leg support uses existing weighted skin triangle eligibility; rendering floor -0.015m. " +
                    "Same anatomy, anchor and backward replay helpers as the validated collection. " +
                    "Geometry and screenshots are provisional; no damage or gameplay action is registered.",
                utc = DateTime.UtcNow.ToString("O"),
                unity = Application.unityVersion,
                grounding = CombatExpansionInventory.Identity(grounding),
                scene = CombatExpansionInventory.Battle
            };
            string status = "RUNNING; partial contact evidence.";
            Flush();
            try
            {
                using var session = new SourceSession();
                InitializePairStudy(session.Fighters);
                CheckGroundingFighters(session.Fighters);
                CheckGroundingTracks(grounding.tracks, session.Fighters);
                foreach (var source in session.Fighters)
                foreach (int direction in new[] { 1, -1 })
                {
                    report.cases.Add(CaptureExecution10ContactPair(session.Fighters, source, direction, grounding));
                    Flush();
                }
                CACompareDirections(report);
                if (report.cases.Count != 4 || report.cases.Any(c => c.backwardSeeks.Count == 0 ||
                    c.backwardSeeks.Any(s => !s.withinOneMillimeter)))
                    throw new InvalidOperationException("Incomplete or unstable Execution10 contact evidence.");
                status = "CAPTURED four cases; backward seeks stable. Contact/presentation approval pending.";
            }
            catch (Exception error)
            {
                status = "FAILED; partial evidence.\n" + error;
                throw;
            }
            finally
            {
                try
                {
                    if (snapshot != NativeReferenceSourceSnapshot(ResolveSources()) ||
                        guarded.Any(f => !File.Exists(f.path) || NativeReferenceHash(f.path) != f.sha256))
                        throw new InvalidOperationException("Execution10 original source preservation failed.");
                }
                catch (Exception error)
                {
                    status = "FAILED preservation guard.\n" + error;
                    throw;
                }
                finally
                {
                    Flush();
                }
            }

            void Flush()
            {
                File.WriteAllText(Execution10ContactsOutput + "/Status.txt", status + "\n");
                File.WriteAllText(Execution10ContactsOutput + "/Report.json", JsonUtility.ToJson(report, true));
                File.WriteAllText(Execution10ContactsOutput + "/Scope.txt", report.scope);
            }
        }

        static CACase CaptureExecution10ContactPair(CharacterCombat[] fighters, CharacterCombat source,
            int direction, FrankPairGrounding grounding)
        {
            var target = fighters.Single(f => f != source);
            var pair = BeginBladeStudy(fighters, source, target, 10, direction, grounding, 1.7f);
            try
            {
                CCValidatePair(pair, grounding, 10);
                pair.EvaluateAt(0);
                CheckPairWeapons(pair, pair.AttackerActor.Pose.weaponRenderers);
                using var sword = new SwordRegion(pair);
                var skins = CASkins(target);
                var attackerSkins = CASkins(source);
                var record = CANewCase(pair, source, target, direction, 1.7f, sword, skins);
                var bounds = new Bounds(Vector3.zero, Vector3.zero);
                float stab = source.name == "Mankey" ? 1.879166722f : 1.862499952f;
                float landing = source.name == "Mankey" ? 1.45f : 1.4625f;
                var contact = CAMeasureContact(pair, sword, skins, attackerSkins, target, stab);
                if (!float.IsFinite(contact.minimumGapM) || contact.minimumGapM > .001f ||
                    contact.anchorRoundtripErrorM > .001f || contact.tiedTriangles.Count == 0)
                    throw new InvalidOperationException("Reviewed downward blade candidate no longer intersects.");
                record.contacts.Add(contact);
                var times = CCTimes(1.3f, pair.Duration, 240).Append(landing).Distinct().OrderBy(t => t);
                foreach (float seconds in times)
                    record.landing.Add(CALandingFrame(pair, sword, skins, attackerSkins, target, seconds, ref bounds));
                CASelectLanding(record);
                var support = record.landing.Single(f => f.seconds == landing);
                float cushion = GroundingCushionFor(10, target, true);
                if (!float.IsFinite(support.bodySupportMinimumY) ||
                    Mathf.Abs(support.bodySupportMinimumY - cushion) > .002f ||
                    support.torsoFacingUp < .75f || record.finalTorsoFacingUp < .97f)
                    throw new InvalidOperationException("Reviewed face-up ground support no longer matches.");
                record.imageTimes = new[]
                {
                    0, .395833343f, .666666687f, 1.333333373f, landing - .025f, landing, landing + .033333333f,
                    1.75f, stab - .025f, stab, stab + .025f, 2.0333333f, pair.Duration
                }.Distinct().OrderBy(t => t).ToArray();
                var baselines = new Dictionary<float, CALanding>();
                foreach (float time in record.imageTimes)
                    baselines.Add(time, CALandingFrame(pair, sword, skins, attackerSkins, target, time, ref bounds));
                foreach (float time in record.imageTimes.Reverse())
                {
                    pair.EvaluateAt(pair.Duration);
                    var frame = CALandingFrame(pair, sword, skins, attackerSkins, target, time, ref bounds);
                    var seek = CACompareSeek(baselines[time], frame);
                    if (time == stab)
                    {
                        var repeated = CAMeasureContact(pair, sword, skins, attackerSkins, target, time);
                        seek.contactGapErrorM = Mathf.Abs(contact.minimumGapM - repeated.minimumGapM);
                        seek.contactAnchorErrorM = CAContactAnchorWorldError(contact, repeated);
                    }
                    seek.withinOneMillimeter = Mathf.Max(seek.maxTrajectoryErrorM, seek.torsoMinimumErrorM,
                        seek.fullBodyMinimumErrorM, seek.bladeTipErrorM, seek.contactGapErrorM,
                        seek.contactAnchorErrorM, seek.supportMinimumErrorM, seek.supportPointErrorM) <= .001f &&
                        seek.torsoFacingUpError <= .0001f && seek.sameSupportBone;
                    record.backwardSeeks.Add(seek);
                }
                string stem = source.name + "_" + target.name + "_" + (direction > 0 ? "Positive" : "Negative");
                CAWriteCsv(record, stem, Execution10ContactsOutput);
                File.WriteAllText(Execution10ContactsOutput + "/" + stem + ".json", JsonUtility.ToJson(record, true));
                bounds.Expand(.55f);
                sword.ShowOverlay();
                using var rendering = new GameplayPairRendering(source.gameObject.scene, fighters, pair);
                rendering.Write(Execution10ContactsOutput + "/" + stem + ".png",
                    bounds, record.imageTimes, pair.EvaluateAt);
                return record;
            }
            finally
            {
                pair.Cancel();
            }
        }
    }
}
