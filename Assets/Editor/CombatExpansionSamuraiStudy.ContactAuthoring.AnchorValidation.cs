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
        const string CAAnchorOutput = "GeneratedAssets/CombatExpansion/SamuraiStudy/AnchorReplayValidation";

        // CAMeasureContact already orders these ties and attaches to exactly element zero.
        // Read the captured world point; local coordinates are attachment data, not metres.
        static Vector3 CASelectedAnchorWorldPoint(CAContact contact)
        {
            if (contact == null || contact.tiedTriangles == null || contact.tiedTriangles.Count == 0 ||
                contact.tiedTriangles[0] == null)
                throw new InvalidOperationException("Missing selected contact triangle anchor evidence.");
            var point = contact.tiedTriangles[0].targetWorldPoint;
            if (!float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z))
                throw new InvalidOperationException("Nonfinite selected contact triangle world point.");
            return point;
        }

        static float CAContactAnchorWorldError(CAContact original, CAContact repeated)
        {
            float error = Vector3.Distance(CASelectedAnchorWorldPoint(original),
                CASelectedAnchorWorldPoint(repeated));
            if (!float.IsFinite(error))
                throw new InvalidOperationException("Nonfinite contact anchor world displacement.");
            return error;
        }

        [Serializable]
        sealed class CAAnchorValidationReport
        {
            public string utc = DateTime.UtcNow.ToString("O");
            public string unity = Application.unityVersion;
            public string status = "RUNNING; actual rig verification incomplete.";
            public string scope = "Execution03 anchor replay units only; no gameplay assignment or acceptance. " +
                "World error uses stored selected triangle points. Raw hips local distance has no metre units. " +
                "The unchanged 0.001m anchor gate is checked; full collection gates require a separate recapture.";
            public string grounding;
            public string error;
            public List<string> fixtures = new List<string>();
            public List<CAAnchorReplayRow> actualRigQueries = new List<CAAnchorReplayRow>();
        }

        [Serializable]
        sealed class CAAnchorReplayRow
        {
            public string attacker;
            public string victim;
            public int laneDirection;
            public int pass;
            public string queryOrder;
            public float seconds;
            public string attackClip;
            public string reactionClip;
            public Vector3 baselineWorldPoint;
            public Vector3 repeatedWorldPoint;
            public Vector3 baselineHipsLocalPoint;
            public Vector3 repeatedHipsLocalPoint;
            public Vector3 baselineHipsLossyScale;
            public Vector3 repeatedHipsLossyScale;
            public float rawHipsLocalDistance;
            public float contactAnchorErrorM;
            public float contactGapErrorM;
            public bool withinOneMillimeter;
            public bool legacyLocalComparisonWithinThreshold;
            public string baselineRenderer;
            public string repeatedRenderer;
            public int baselineTriangle;
            public int repeatedTriangle;
        }

        public static void ValidateContactAnchorWorldUnits()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            Directory.CreateDirectory(CAAnchorOutput);
            var report = new CAAnchorValidationReport();
            CAWriteAnchorValidation(report);
            try
            {
                CAValidateAnchorFixtures(report.fixtures);
                var grounding = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(GroundingAssetPath(3));
                if (!grounding)
                    throw new InvalidOperationException("Execution03 grounding is required for anchor validation.");
                report.grounding = CombatExpansionInventory.Identity(grounding);
                using (var session = new SourceSession())
                {
                    InitializePairStudy(session.Fighters);
                    CheckGroundingFighters(session.Fighters);
                    CheckGroundingTracks(grounding.tracks, session.Fighters);
                    foreach (var source in session.Fighters)
                    foreach (int direction in new[] { 1, -1 })
                    {
                        CAValidateAnchorPair(session.Fighters, source, direction, grounding, report);
                        CAWriteAnchorValidation(report);
                    }
                }
                if (report.actualRigQueries.Count != 48)
                    throw new InvalidOperationException("Incomplete actual rig anchor replay coverage.");
                if (report.actualRigQueries.Any(r => !r.withinOneMillimeter))
                    throw new InvalidOperationException("Actual rig anchor displacement exceeds unchanged 0.001m gate.");
                report.status = "PASS anchor units fixtures and 48 actual rig queries; full capture gates unverified.";
            }
            catch (Exception error)
            {
                report.status = "FAILED anchor replay validation; inspect partial evidence.";
                report.error = error.ToString();
                throw;
            }
            finally
            {
                CAWriteAnchorValidation(report);
            }
        }

        static void CAWriteAnchorValidation(CAAnchorValidationReport report)
        {
            File.WriteAllText(CAAnchorOutput + "/Report.json", JsonUtility.ToJson(report, true));
            File.WriteAllText(CAAnchorOutput + "/Status.txt", report.status + "\n" + report.error);
        }

        static void CAValidateAnchorPair(CharacterCombat[] fighters, CharacterCombat source, int direction,
            FrankPairGrounding grounding, CAAnchorValidationReport report)
        {
            var target = fighters.Single(f => f != source);
            FrankBattlePairPlayback pair = null;
            try
            {
                pair = BeginBladeStudy(fighters, source, target, 3, direction, grounding, 1.7f);
                CCValidatePair(pair, grounding, 3);
                pair.EvaluateAt(0);
                CheckPairWeapons(pair, pair.AttackerActor.Pose.weaponRenderers);
                using var sword = new SwordRegion(pair);
                var skins = CASkins(target);
                var attackerSkins = CASkins(source);
                var hips = target.Animator.GetBoneTransform(HumanBodyBones.Hips);
                if (!hips)
                    throw new InvalidOperationException("Anchor replay diagnostic requires target hips.");
                var times = new[] { .475f, 2.3875f };
                if (pair.Duration < times.Last())
                    throw new InvalidOperationException("Execution03 does not contain diagnostic query times.");
                var baselines = new Dictionary<float, CAContact>();
                var scales = new Dictionary<float, Vector3>();
                foreach (float time in times)
                {
                    baselines.Add(time, CCContact(pair, sword, skins, attackerSkins, target, time));
                    CASelectedAnchorWorldPoint(baselines[time]);
                    scales.Add(time, hips.lossyScale);
                }
                for (int pass = 1; pass <= 3; pass++)
                foreach (bool backward in new[] { false, true })
                {
                    pair.EvaluateAt(backward ? pair.Duration : 0);
                    foreach (float time in backward ? times.Reverse() : times)
                    {
                        var repeated = CCContact(pair, sword, skins, attackerSkins, target, time);
                        var baseline = baselines[time];
                        float worldError = CAContactAnchorWorldError(baseline, repeated);
                        float rawLocal = Vector3.Distance(baseline.targetHipsLocalPoint,
                            repeated.targetHipsLocalPoint);
                        report.actualRigQueries.Add(new CAAnchorReplayRow
                        {
                            attacker = source.name,
                            victim = target.name,
                            laneDirection = direction,
                            pass = pass,
                            queryOrder = backward ? "backward from clip end" : "forward from clip start",
                            seconds = time,
                            attackClip = CombatExpansionInventory.Identity(pair.Move.sourcePair.attack),
                            reactionClip = CombatExpansionInventory.Identity(pair.Move.sourcePair.reaction),
                            baselineWorldPoint = CASelectedAnchorWorldPoint(baseline),
                            repeatedWorldPoint = CASelectedAnchorWorldPoint(repeated),
                            baselineHipsLocalPoint = baseline.targetHipsLocalPoint,
                            repeatedHipsLocalPoint = repeated.targetHipsLocalPoint,
                            baselineHipsLossyScale = scales[time],
                            repeatedHipsLossyScale = hips.lossyScale,
                            rawHipsLocalDistance = rawLocal,
                            contactAnchorErrorM = worldError,
                            contactGapErrorM = Mathf.Abs(baseline.minimumGapM - repeated.minimumGapM),
                            withinOneMillimeter = worldError <= .001f,
                            legacyLocalComparisonWithinThreshold = rawLocal <= .001f,
                            baselineRenderer = baseline.tiedTriangles[0].renderer,
                            repeatedRenderer = repeated.tiedTriangles[0].renderer,
                            baselineTriangle = baseline.tiedTriangles[0].targetTriangle,
                            repeatedTriangle = repeated.tiedTriangles[0].targetTriangle
                        });
                    }
                }
            }
            finally
            {
                if (pair)
                    pair.Cancel();
                else if (source.SourcePlayback)
                    source.SourcePlayback.Cancel();
            }
        }
    }
}
