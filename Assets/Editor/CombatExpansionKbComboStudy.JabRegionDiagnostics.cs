using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using RegionProbe = FrankRetarget.Editor.CombatExpansionAxeDenseStudy.SkinRegionProbe;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionKbComboStudy
    {
        const string JabRegionOutput = "GeneratedAssets/CombatExpansion/KbComboStudy/PepeJabRegionDiagnostics";
        const string JabRegionScope = "PROVISIONAL DIAGNOSTICS ONLY. Pepe first jab, positive lane, " +
            "spacing 0.78/0.86m crossed with entry blend 0/0.04s; 0..0.30s at 240Hz. " +
            "Compare bounded Chest torso against Head descendants on the same restored grounded pose. " +
            "Torso excludes mapped neck/head/shoulder/arm/hand branches; each triangle vertex needs " +
            ">=0.5 summed retained-bone influence. Exact unsigned triangle gaps; zero can be penetration. " +
            "All samples are diagnostic: no onsets are selected and no reactions occur in this capture. " +
            "Eligibility uses the variant entry end and window start 0; historical start 0.06s is flagged. " +
            "First sample has no velocity and cannot be eligible. No negative lane or combo acceptance. " +
            "Native source equivalence, pose snapshots, grounded reverse seeks and sheets are retained. " +
            "No source assets, gameplay assets or registrations are changed.";

        [Serializable]
        sealed class JabRegionStudy
        {
            public string status = "RUNNING_DIAGNOSTIC";
            public string scope = JabRegionScope;
            public string failure;
            public List<JabRegionCase> cases = new List<JabRegionCase>();
        }

        [Serializable]
        sealed class JabRegionCase
        {
            public SequentialCase playback;
            public CandidateRecord presentation;
            public List<JabRegionTrace> regions = new List<JabRegionTrace>();
        }

        [Serializable]
        sealed class JabRegionTrace
        {
            public string region;
            public List<SequentialSample> samples = new List<SequentialSample>();
        }

        public static void CapturePepeJabRegionDiagnostics()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            Directory.CreateDirectory(JabRegionOutput);
            File.WriteAllText(JabRegionOutput + "/Scope.txt", JabRegionScope + "\n");
            var study = new JabRegionStudy();
            try
            {
                foreach (float spacing in new[] { .78f, .86f })
                foreach (float entry in new[] { 0f, .04f })
                {
                    var sources = HighReactionSources(7400006);
                    var record = NewSequentialCase(sources, 1, spacing, 1);
                    record.name = "Pepe_Range" + Mathf.RoundToInt(spacing * 100) +
                        "_Entry" + Mathf.RoundToInt(entry * 1000) + "ms";
                    record.entryBlendSeconds = entry;
                    record.linkBlendSeconds = .04f;
                    record.targetRegions = new[]
                    {
                        "TorsoWithoutHeadNeckOrArms|Head diagnostic alternatives", "NotMeasured", "NotMeasured"
                    };
                    var result = new JabRegionCase { playback = record };
                    study.cases.Add(result);
                    try
                    {
                        CaptureJabRegionCase(result);
                        record.status = "CAPTURED_DIAGNOSTIC_NO_ONSETS";
                    }
                    catch (Exception error)
                    {
                        record.status = "FAILED_PARTIAL_DIAGNOSTICS_RETAINED";
                        record.failure = error.ToString();
                        throw;
                    }
                    finally
                    {
                        File.WriteAllText(JabRegionOutput + "/" + record.name + ".json",
                            JsonUtility.ToJson(result, true));
                        RequireSourcesUnchanged(sources);
                    }
                }
                study.status = "CAPTURED_DIAGNOSTIC_NO_CONTACT_ACCEPTANCE";
            }
            catch (Exception error)
            {
                study.status = "FAILED_PARTIAL_DIAGNOSTICS_RETAINED";
                study.failure = error.ToString();
                throw;
            }
            finally
            {
                File.WriteAllText(JabRegionOutput + "/Study.json", JsonUtility.ToJson(study, true));
            }
        }

        static void CaptureJabRegionCase(JabRegionCase result)
        {
            var record = result.playback;
            using var player = new SequentialPlayer(record.sources, record);
            player.Rebuild(0);
            record.phase = "first_jab_diagnostics_no_reaction";
            using var torso = new RegionProbe(player.Source, HumanBodyBones.LeftHand,
                player.Target, HumanBodyBones.Chest, RegionProbe.Selection.TorsoWithoutHeadNeckOrArms);
            using var head = new RegionProbe(player.Source, HumanBodyBones.LeftHand,
                player.Target, HumanBodyBones.Head);
            record.geometry.Add(torso.SelectionSummary);
            record.geometry.Add(head.SelectionSummary);
            var torsoTrace = new JabRegionTrace { region = "TorsoWithoutHeadNeckOrArms" };
            var headTrace = new JabRegionTrace { region = "Head" };
            result.regions.Add(torsoTrace);
            result.regions.Add(headTrace);
            var candidate = new CandidateRecord
            {
                name = record.name, attacker = record.attacker, receiver = record.receiver,
                spacing = record.spacing, laneSign = 1, duration = .3f,
                attacks = record.attacks, reactions = record.reactions,
                sheetSeconds = Enumerable.Range(0, 13).Select(frame => frame / 40f).ToArray()
            };
            result.presentation = candidate;
            var nodes = Nodes(player.Pair, player.Source, player.Target);
            var bounds = new Bounds(player.Source.Animator.transform.position, Vector3.zero);
            var nativeRows = new StringBuilder("seconds,step,bone,localRotationErrorDegrees," +
                "worldPositionErrorAfterHorizontalCarryMetres,carryX,carryY,carryZ\n");
            var poseRows = new StringBuilder("seconds,role,rig,bone,x,y,z,qx,qy,qz,qw\n");
            var snapshots = new Dictionary<int, PoseSample[]>();
            using (var native = new NativeReference(player.Pair.AttackerActor, record.sources, candidate))
            {
                for (int frame = 0; frame <= 72; frame++)
                {
                    float seconds = frame / 240f;
                    AddJabRegionSample(player, torso, torsoTrace, HumanBodyBones.Chest, seconds, record);
                    AddJabRegionSample(player, head, headTrace, HumanBodyBones.Head, seconds, record);
                    native.Compare(seconds, nativeRows);
                    snapshots[frame] = nodes.Select(node =>
                        new PoseSample(node.node.position, node.node.rotation)).ToArray();
                    foreach (var node in nodes)
                    {
                        var p = node.node.position;
                        var q = node.node.rotation;
                        bounds.Encapsulate(p);
                        poseRows.AppendLine(FormattableString.Invariant(
                            $"{seconds:R},{node.role},{node.rig},{node.bone},{p.x:R},{p.y:R},{p.z:R},") +
                            FormattableString.Invariant($"{q.x:R},{q.y:R},{q.z:R},{q.w:R}"));
                    }
                    SaveJabRegionEvidence(result, nativeRows, poseRows);
                }
            }
            Action<float> verify = seconds =>
            {
                int frame = Mathf.RoundToInt(seconds * 240);
                var sample = MeasureJabRegion(player, torso, HumanBodyBones.Chest, torsoTrace.region, seconds);
                var saved = torsoTrace.samples[frame];
                CompareSequentialFloor(record, saved.floor, sample.floor);
                record.worstGapReseek = Mathf.Max(record.worstGapReseek,
                    Mathf.Abs(saved.contact.gap - sample.contact.gap));
                var headSample = MeasureJabRegion(player, head, HumanBodyBones.Head, headTrace.region, seconds);
                record.worstGapReseek = Mathf.Max(record.worstGapReseek,
                    Mathf.Abs(headTrace.samples[frame].contact.gap - headSample.contact.gap));
                var poses = snapshots[frame];
                for (int index = 0; index < nodes.Count; index++)
                {
                    record.worstPoseMetres = Mathf.Max(record.worstPoseMetres,
                        Vector3.Distance(nodes[index].node.position, poses[index].position));
                    record.worstPoseDegrees = Mathf.Max(record.worstPoseDegrees,
                        Quaternion.Angle(nodes[index].node.rotation, poses[index].rotation));
                }
                if (record.worstGapReseek > .0001f || record.worstPoseMetres > .001f ||
                    record.worstPoseDegrees > .1f)
                    throw new InvalidOperationException("Jab diagnostic reverse seek exceeded tolerance.");
                record.reverseSamples++;
            };
            foreach (float seconds in candidate.sheetSeconds.Reverse())
                verify(seconds);
            bounds.Encapsulate(new Vector3(bounds.center.x, 0, bounds.center.z));
            bounds.Expand(.45f);
            using var rendering = new CandidateRendering(player.Source.gameObject.scene, player.Fighters, player.Pair);
            record.sheets = rendering.Write(JabRegionOutput + "/" + record.name, bounds, candidate, verify);
            record.sheetSeconds = candidate.sheetSeconds;
            record.phase = "diagnostics_finished_no_onsets";
        }

        static void AddJabRegionSample(SequentialPlayer player, RegionProbe probe, JabRegionTrace trace,
            HumanBodyBones root, float seconds, SequentialCase record)
        {
            var sample = MeasureJabRegion(player, probe, root, trace.region, seconds);
            if (trace.samples.Count > 0)
                ClassifySequential(sample, trace.samples.Last(), 0, 1, record.entryBlendSeconds, 0);
            else
            {
                sample.windowEntry = true;
                sample.inBlend = record.entryBlendSeconds > 0;
                sample.nearSurface = sample.contact.gap <= .001f;
                sample.flags = "WINDOW_ENTRY_NO_VELOCITY";
            }
            if (seconds < .06f)
                sample.flags += ";BEFORE_HISTORICAL_SEARCH_START";
            sample.contact.status = "DIAGNOSTIC_NO_ONSET_" + sample.flags;
            trace.samples.Add(sample);
        }

        static SequentialSample MeasureJabRegion(SequentialPlayer player, RegionProbe probe,
            HumanBodyBones root, string region, float seconds)
        {
            var floor = player.Sample(seconds);
            var contact = probe.Measure(new List<string>());
            return new SequentialSample
            {
                floor = floor,
                contact = ContactRow(seconds, player.Pair.SampleTime, 1, region, contact,
                    player.Source.Animator.GetBoneTransform(HumanBodyBones.LeftHand),
                    player.Target.Animator.GetBoneTransform(root), floor.attackerAfter, floor.receiverAfter, null)
            };
        }

        static void SaveJabRegionEvidence(JabRegionCase result, StringBuilder nativeRows, StringBuilder poseRows)
        {
            string stem = JabRegionOutput + "/" + result.playback.name;
            File.WriteAllText(stem + ".json", JsonUtility.ToJson(result, true));
            File.WriteAllText(stem + "_Native.csv", nativeRows.ToString());
            File.WriteAllText(stem + "_Poses.csv", poseRows.ToString());
            foreach (var trace in result.regions)
                File.WriteAllText(stem + "_" + trace.region + ".csv",
                    ContactCsv(trace.samples.Select(sample => sample.contact)));
        }
    }
}
