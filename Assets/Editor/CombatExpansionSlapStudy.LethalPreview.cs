using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapStudy
    {
        const string LethalRawOutput = "GeneratedAssets/CombatExpansion/SlapStudy/LethalCandidates/Raw";
        const string LethalRawScope = "RAW AUTHORING EVIDENCE ONLY. Eight actual baked receiver runtime pairs: " +
            "two sequences, both BattleScene fighter assignments and both lane directions. Original attacker " +
            "and both original drivers; only hitAnim and sourcePair.reaction are replaced. Range 0.8m, " +
            "receiver yaw 180, entry blend 0.12s; both roles transfer fingers. No original grounding is applied " +
            "because the receiver motion changed. Negative body minimum Y is floor penetration evidence, " +
            "not a reason to suppress images. No registration, damage, feedback or recovery is approved. " +
            "Synchronous EvaluateAt sampling does not advance gameplay. World positions are metres; CSV " +
            "includes 60Hz full-duration samples and every shown timestamp. Body floor minimum uses the " +
            "existing visible rendered-body geometry helper; joint positions are not skin-contact evidence. " +
            "Shown frames must reproduce within 0.001m after backward seeks. The source clocks and phase " +
            "map follow the staged baker; contact + 0.12s prefix, 0.22s blend, fall offsets 0.70/0.86/1.41/1.81s. " +
            "Only preview-scene objects are evaluated; SourceSession restores singleton references and scene state.";

        [Serializable]
        sealed class LethalRawReport
        {
            public string status = "RUNNING", scope = LethalRawScope;
            public string utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion;
            public SourceRecord[] sources;
            public LethalClipRecord[] clips;
            public List<LethalRawCase> cases = new List<LethalRawCase>();
        }

        [Serializable]
        sealed class LethalRawCase
        {
            public CandidateRecord pair;
            public string status = "RAW_AUTHORING_EVIDENCE", candidateIdentity, candidatePath, candidateHash;
            public string sourceTimeMap, jointMeasurements;
            public float contact, preserveEnd, blendEnd, compressedStart, compressedEnd, terminalStart, holdStart;
            public float minimumAttackerY = float.PositiveInfinity, minimumReceiverY = float.PositiveInfinity;
            public float minimumAttackerSeconds, minimumReceiverSeconds, worstBackwardFloorMetres;
            public int attackerPenetratingSamples, receiverPenetratingSamples;
        }

        [MenuItem("Tools/Combat Expansion/Slap/Capture Raw Lethal Candidates")]
        public static void CaptureRawLethalCandidates()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            Directory.CreateDirectory(LethalRawOutput);
            var report = new LethalRawReport();
            File.WriteAllText(LethalRawOutput + "/Scope.txt", LethalRawScope + "\n");
            WriteLethalRawStatus(report, "RUNNING 0/8\n");
            try
            {
                var sources = ResolveSources();
                report.sources = sources;
                var candidates = Enumerable.Range(1, 2).Select(LoadLethalCandidate).ToArray();
                var fall = LoadLethalFall();
                var clips = sources.Select(s => s.asset).Concat(candidates).Append(fall).Distinct().ToArray();
                report.clips = clips.Select(LethalClipSnapshot).ToArray();
                var guards = LethalGuards(sources, clips);
                try
                {
                    for (int sequence = 1; sequence <= 2; sequence++)
                    for (int assignment = 0; assignment < 2; assignment++)
                    foreach (int direction in new[] { 1, -1 })
                    {
                        using (var session = new SourceSession())
                        {
                            InitializeFaceGroundingFighters(session.Fighters);
                            CheckGroundingFighters(session.Fighters);
                            report.cases.Add(CaptureLethalRawCase(session.Fighters, sources,
                                candidates[sequence - 1], fall.length, sequence, assignment, direction));
                        }
                        WriteLethalRawStatus(report, "RUNNING " + report.cases.Count + "/8\n");
                    }
                }
                finally
                {
                    RequireSourceFilesUnchanged(sources);
                    foreach (var guard in guards)
                        guard.AssertUnchanged();
                }
                report.status = "CAPTURED_RAW_AUTHORING_EVIDENCE";
                WriteLethalRawStatus(report, "CAPTURED 8/8 raw candidates; source/candidate guards passed.\n" +
                    "Floor penetration is recorded in Study.json and measurements CSV; " +
                    "inspect images before acceptance.\n" +
                    "No gameplay, damage, feedback, grounding or recovery approval.\n");
            }
            catch (Exception error)
            {
                report.status = "FAILED_PARTIAL_RAW_EVIDENCE";
                WriteLethalRawStatus(report, "FAILED: " + error + "\nPartial evidence is incomplete.\n");
                throw;
            }
        }

        static void WriteLethalRawStatus(LethalRawReport report, string status)
        {
            File.WriteAllText(LethalRawOutput + "/Study.json", JsonUtility.ToJson(report, true));
            File.WriteAllText(LethalRawOutput + "/Status.txt", status);
        }

        static AnimationClip LoadLethalCandidate(int sequence)
        {
            string name = "SlapSequence" + sequence + "_LethalReceiver_Provisional";
            string path = CombatExpansionSlapLethalAuthoring.AssetRoot + "/" + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (!clip || clip.name != name || !clip.humanMotion || !float.IsFinite(clip.length) || clip.length <= 0)
                throw new InvalidOperationException("Bake the actual lethal receiver candidate first: " + path);
            if (EditorUtility.IsDirty(clip))
                throw new InvalidOperationException(
                    "Save candidate edits before capturing byte-identified evidence: " + path);
            return clip;
        }

        static AnimationClip LoadLethalFall()
        {
            const string guid = "b01b411d366a5a344bddf3428a7bc8f7";
            var clip = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid))
                .OfType<AnimationClip>().SingleOrDefault(c =>
                    CombatExpansionInventory.Identity(c) == guid + ":7400004");
            if (!clip || Mathf.Abs(clip.length - 3.566667f) > .0001f)
                throw new InvalidOperationException("Missing staged-baker fall source identity/duration.");
            return clip;
        }
    }
}
