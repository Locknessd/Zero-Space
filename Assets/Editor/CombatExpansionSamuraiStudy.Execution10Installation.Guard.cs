using System;
using System.IO;
using System.Linq;
using UnityEditor;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        internal sealed class Execution10InstallationGuard : IDisposable
        {
            readonly E10Guard guard = new E10Guard();
            readonly string sourceSnapshot = NativeReferenceSourceSnapshot(ResolveSources());

            internal void VerifyBeforeCommit() => guard.Verify(new E10Report());

            internal void VerifySourcesAfterCommit()
            {
                var files = guard.Files.Where(f => f.path != CombatExpansionInventory.Battle &&
                    !f.path.StartsWith(CombatExpansionSamurai10Setup.Folder + CombatExpansionSamurai10Setup.Id + "_",
                        StringComparison.Ordinal));
                if (sourceSnapshot != NativeReferenceSourceSnapshot(ResolveSources()) ||
                    files.Any(f => !File.Exists(f.path) || NativeReferenceHash(f.path) != f.sha256))
                    throw new InvalidOperationException("Execution10 source/bank/other asset preservation failed.");
            }

            public void Dispose() => guard.RestoreSelection();
        }

        static void E10ValidateRecoveryEvidence(E10Report report, CACase record, CombatTripletData move, CharacterCombat target)
        {
            if (report == null || report.status != "PASS numeric checks for four cases; visual review required" ||
                !string.IsNullOrEmpty(report.error) || !report.fileGuardsPassed || !report.sceneGuardsPassed ||
                report.sampleRate != 361 || report.entryLimit != .01f || report.clearanceLimit != -.025f ||
                report.driftLimit != .00002f || report.cases == null || report.cases.Count != 4 ||
                report.guardedFiles == null || report.guardedFiles.Length == 0)
                throw new InvalidOperationException("Complete original Execution10 recovery evidence required.");
            E10FiniteEvidence(report);
            var recovery = report.cases.Single(c => c.source == record.attacker && c.direction == record.direction);
            if (recovery.target != record.victim || recovery.status != "PASS numeric checks; visual review required" ||
                !string.IsNullOrEmpty(recovery.error) || !recovery.recoveryStarted || !recovery.cancellationPassed ||
                recovery.attack != record.attackClip || recovery.reaction != record.reactionClip ||
                recovery.sourceAvatar != record.attackerAvatar || recovery.targetAvatar != record.victimAvatar ||
                recovery.attackerDriver != record.attackerDriver || recovery.receiverDriver != record.victimDriver ||
                recovery.getUp != CombatExpansionSamurai10Setup.GetUpId ||
                recovery.grounding != CombatExpansionSamurai10Setup.GroundingId ||
                recovery.recoveryGrounding != CombatExpansionSamurai10Setup.RecoveryId ||
                MathfAbs(recovery.sourceDuration - CombatExpansionSamurai10Setup.Duration) > .000001f ||
                recovery.endpointSeconds != recovery.sourceDuration ||
                MathfAbs(recovery.recoveryDuration - move.getUpAnim.length) > .000001f ||
                MathfAbs(recovery.lastRecoverySeconds - (recovery.recoveryDuration - .001f)) > .000001f ||
                recovery.samples != UnityEngine.Mathf.CeilToInt(recovery.lastRecoverySeconds * 361) + 1 ||
                recovery.stoppedSamples < 3 || recovery.mappedVictimBones != E10Bones(target).Length ||
                recovery.entryJump < 0 || recovery.entryJump > .01f || recovery.minimumClearance < -.025f ||
                recovery.repeatDrift < 0 || recovery.repeatDrift > .00002f ||
                recovery.stoppedDrift < 0 || recovery.stoppedDrift > .00002f)
                throw new InvalidOperationException("Execution10 actual controller recovery gate failed.");
            // Scene registration may evolve after capture. Source/grounding/driver bytes must not.
            foreach (var file in report.guardedFiles.Where(f => !f.path.EndsWith(".unity", StringComparison.Ordinal) &&
                !f.path.StartsWith("Assets/CombatExpansion/Actions/Samurai_Execution", StringComparison.Ordinal)))
                if (!File.Exists(file.path) || NativeReferenceHash(file.path) != file.sha256)
                    throw new InvalidOperationException("Stale Execution10 recovery source evidence: " + file.path);
        }

        static float MathfAbs(float value) => UnityEngine.Mathf.Abs(value);
    }
}
