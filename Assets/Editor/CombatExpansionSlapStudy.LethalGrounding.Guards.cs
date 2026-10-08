using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapStudy
    {
        static LgPhase LgReadPhase(int sequence)
        {
            string path = CombatExpansionSlapLethalAuthoring.Output + "/Sequence" + sequence + ".TimeMap.csv";
            var rows = File.ReadAllLines(path).Skip(1).Where(line => line.Length > 0)
                .Select(line => line.Split(',')).ToArray();
            float Time(string[] row) => float.Parse(row[0], CultureInfo.InvariantCulture);
            float preserve = rows.Where(row => row[4] == "original").Max(Time);
            float hold = rows.Where(row => row[4] == "hold").Min(Time);
            if (!float.IsFinite(preserve) || !float.IsFinite(hold) || preserve <= .12f || hold <= preserve)
                throw new InvalidOperationException("Invalid authored receiver source-time map: " + path);
            return new LgPhase
            {
                sequence = sequence,
                anchor = preserve - .12f,
                preserve = preserve,
                hold = hold,
                timeMap = path,
                timeMapHash = FileHash(path)
            };
        }

        static LgFile LgSnapshot(string path)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset && EditorUtility.IsDirty(asset))
                throw new InvalidOperationException("Save existing asset edits before identified study: " + path);
            return new LgFile
            {
                path = path,
                bytes = FileHash(path),
                meta = File.Exists(path + ".meta") ? FileHash(path + ".meta") : "",
                serialized = asset ? LethalObjectHash(asset) : ""
            };
        }

        static void LgAssertFiles(LgFile[] files, bool sameEditorSession = true)
        {
            foreach (var file in files)
            {
                var now = LgSnapshot(file.path);
                if (now.bytes != file.bytes || now.meta != file.meta ||
                    (sameEditorSession && now.serialized != file.serialized))
                    throw new InvalidOperationException("Study provenance changed: " + file.path);
            }
        }

        static LgReport LgPrepare(string operation, out LethalAssetGuard[] guards)
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            Directory.CreateDirectory(LgOutput);
            File.WriteAllText(LgOutput + "/" + operation + ".Status.txt", "PREPARING\n");
            File.WriteAllText(LgOutput + "/" + operation + ".json",
                JsonUtility.ToJson(new LgReport { operation = operation, status = "PREPARING" }, true));
            var sources = ResolveSources();
            var clips = sources.Select(s => s.asset).Concat(new[]
                { LoadLethalCandidate(1), LoadLethalCandidate(2), LoadLethalFall() }).Distinct().ToArray();
            var paths = clips.Select(AssetDatabase.GetAssetPath)
                .Concat(sources.Select(s => s.originalPath))
                .Concat(sources.SelectMany(s => new[] { DriverPath("Mankey", s), DriverPath("Pepe", s) }))
                .Concat(new[] { FaceGroundingPath(1), FaceGroundingPath(2), CombatExpansionInventory.Battle })
                .Distinct().ToArray();
            guards = paths.Select(path => new LethalAssetGuard(path)).ToArray();
            var report = new LgReport
            {
                operation = operation,
                sources = sources,
                clips = clips.Select(LethalClipSnapshot).ToArray(),
                phases = new[] { LgReadPhase(1), LgReadPhase(2) },
                guardedFiles = paths.Select(LgSnapshot).ToArray()
            };
            File.WriteAllText(LgOutput + "/Scope.txt", LgScope + "\n");
            return report;
        }

        static void LgFinish(LgReport report, LethalAssetGuard[] guards)
        {
            try
            {
                RequireSourceFilesUnchanged(report.sources);
                foreach (var guard in guards)
                    guard.AssertUnchanged();
                LgAssertFiles(report.guardedFiles);
                foreach (var phase in report.phases)
                    if (FileHash(phase.timeMap) != phase.timeMapHash)
                        throw new InvalidOperationException("Authored phase map changed during study.");
                report.sourceGuardsPassed = true;
            }
            catch (Exception error)
            {
                report.failure = (report.failure ?? "") + "\nSOURCE GUARD: " + error;
                report.status = "FAILED_SOURCE_GUARD";
                throw;
            }
            finally
            {
                LgWriteReport(report);
            }
        }

        static void LgWriteReport(LgReport report)
        {
            File.WriteAllText(LgOutput + "/" + report.operation + ".json", JsonUtility.ToJson(report, true));
            File.WriteAllText(LgOutput + "/" + report.operation + ".Status.txt",
                report.status + "\n" + report.failure + "\n" + LgScope + "\n");
        }

        static FrankPairGrounding LgLoadNative(int sequence, CharacterCombat[] fighters)
        {
            var native = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(FaceGroundingPath(sequence));
            if (!native)
                throw new InvalidOperationException("Missing native contact-prefix grounding: " + sequence);
            CheckGroundingTracks(native.tracks, fighters);
            return native;
        }

        static FrankPairGrounding.Track LgTrackFor(FrankPairGrounding grounding, CharacterCombat fighter,
            bool receiver)
        {
            return grounding.tracks.Single(track => track.avatar == fighter.Animator.avatar &&
                track.receiver == receiver);
        }

        static FrankBattlePairPlayback LgBegin(CharacterCombat[] fighters, SourceRecord[] sources,
            int sequence, int assignment, int direction, FrankPairGrounding grounding, bool candidate)
        {
            var source = fighters[assignment];
            var target = fighters[1 - assignment];
            var attack = sources.Single(s => s.label == "Sequence" + sequence + (sequence == 1 ? "_A" : "_B"));
            var reaction = sources.Single(s => s.label == "Sequence" + sequence + (sequence == 1 ? "_B" : "_A"));
            foreach (var fighter in fighters)
                fighter.ResetCombat();
            source.Animator.transform.SetPositionAndRotation(Vector3.left * direction * .4f,
                Quaternion.LookRotation(Vector3.right * direction));
            target.Animator.transform.SetPositionAndRotation(Vector3.right * direction * .4f,
                Quaternion.LookRotation(Vector3.left * direction));
            var move = CandidateMove(source, target, attack, reaction, 180, .8f);
            var receiverClip = candidate ? LoadLethalCandidate(sequence) : reaction.asset;
            if (Mathf.Abs(receiverClip.length - reaction.durationSeconds) > .0001f)
                throw new InvalidOperationException("Candidate receiver changed original duration.");
            move.hitAnim = receiverClip;
            move.sourcePair.reaction = receiverClip;
            move.sourcePair.transferReceiverFingers = true;
            move.grounding = grounding;
            if (!source.ExecuteAttack(move, target) || !source.SourcePlayback || !source.SourcePlayback.Playing)
                throw new InvalidOperationException("Native pair rejected lethal grounding study.");
            var pair = source.SourcePlayback;
            if (pair.Move.attackAnim != attack.asset || pair.Move.sourcePair.attack != attack.asset ||
                pair.Move.hitAnim != receiverClip || pair.ActiveGrounding != grounding)
                throw new InvalidOperationException("Unexpected substitution in lethal grounding study.");
            pair.AttackerActor.Pose.transferFingers = true;
            pair.ReceiverActor.Pose.transferFingers = true;
            return pair;
        }
    }
}
