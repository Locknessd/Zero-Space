using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionThrowPlayCheck
    {
        static readonly string[] Actions =
        {
            "Light_1", "Vol10_SEOI", "Vol10_BRAIN", "Vol10_FISH", "Vol10_JPBOM",
            "Vol10_LC_DSCREW", "Vol10_NECK_BREAK", "Vol10_SIHO", "Vol10_GS1", "Vol10_GSWING", "Heavy_6"
        };
        const string ReportPath = "GeneratedAssets/CombatExpansion/ThrowPlayMode.txt";
        const int GuardCases = 18;
        const int QueueCases = 44;
        const int TotalCases = GuardCases + QueueCases + 8;
        static readonly StringBuilder report = new StringBuilder();
        static readonly HashSet<ulong> contactIds = new HashSet<ulong>();
        static GameManager game;
        static CharacterCombat[] fighters;
        static CharacterCombat source, target;
        static BattleImpactFeedback feedback;
        static BattleLightingRig lighting;
        static BattleCameraShake shake;
        static FrankCinematicCamera director;
        static FrankBattlePairPlayback pair;
        static CombatTripletData move;
        static PlayerUI.Side sourceSide;
        static bool running, activeCase, priorInput, interrupted;
        static bool sourceRecovered, targetRecovered;
        static int step, frame, starts, contacts, sourceId, targetId, sourceEnded, targetEnded;
        static int audioBefore, effectsBefore, holdsBefore, lightsBefore, shakesBefore;
        static float priorTimeScale, previousSample, sourceRecoveryProgress, targetRecoveryProgress;
        static double suiteBegan, caseBegan, settledAt;
        static string failure, action, healthBefore;
        static bool Guard => step < GuardCases;
        static bool Queued => step >= GuardCases && step < GuardCases + QueueCases;
        static bool Lethal => step >= GuardCases + QueueCases && step < GuardCases + QueueCases + 4;
        static bool Interrupt => step >= GuardCases + QueueCases + 4;
        static bool Throw => action.StartsWith("Vol10_", StringComparison.Ordinal);
        static int Orientation => Guard ? step / 9 : Queued ? (step - GuardCases) / Actions.Length :
            (step - GuardCases - QueueCases) % 4;
        static string Scenario => Guard ? "range guard" : Queued ? "queued" : Lethal ? "lethal" : "recovery cancel";

        public static void Begin()
        {
            if (!EditorApplication.isPlaying || running)
                throw new InvalidOperationException("Begin once in BattleScene Play Mode.");
            game = Object.FindAnyObjectByType<GameManager>();
            Require(game && !game.IsAnimationTestMode && !game.IsEventQueueBusy && game.QueueError == null &&
                game.leftCombat && game.rightCombat && game.leftCombat.isActiveAndEnabled &&
                game.rightCombat.isActiveAndEnabled && game.leftCombat.IsIdleAndSettled &&
                game.rightCombat.IsIdleAndSettled && game.battleSfx && game.battleVfx && Time.timeScale > 0,
                "Both fighters must be idle and alive, outside animation test mode, with audio/VFX and a live clock.");
            feedback = game.GetComponent<BattleImpactFeedback>();
            lighting = game.GetComponent<BattleLightingRig>();
            shake = game.gameObject.scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<BattleCameraShake>(true)).Single();
            director = shake.GetComponent<FrankCinematicCamera>();
            Require(feedback && feedback.isActiveAndEnabled && lighting && lighting.isActiveAndEnabled &&
                shake.isActiveAndEnabled && game.battleSfx.isActiveAndEnabled && game.battleVfx.isActiveAndEnabled,
                "All presentation subsystems must be enabled.");
            fighters = new[] { game.leftCombat, game.rightCombat };
            SaveState();
            report.Clear();
            report.AppendLine("RUNNING: 18 pre-sequence range guards; 44 queued; " +
                "4 lethal; 4 recovery cancellations.");
            report.AppendLine("Contact events validate accepted source-paired exchanges and lifecycle only.");
            report.AppendLine("Visual contact alignment and subjective animation quality require separate visual review.");
            report.AppendLine("Each queue orientation runs Light_1, all nine throws, then Heavy_6.");
            step = 0;
            frame = -1;
            activeCase = false;
            failure = null;
            contactIds.Clear();
            suiteBegan = EditorApplication.timeSinceStartup;
            running = true;
            try
            {
                game.enableLocalInputTesting = false;
                CreateFixtures();
                game.battleVfx.SequenceBegan += SequenceBegan;
                game.battleVfx.ContactOccurred += Contact;
                foreach (var fighter in fighters)
                    fighter.SequenceEnded += SequenceEnded;
                EditorApplication.update += Tick;
                EditorApplication.playModeStateChanged += PlayModeChanged;
                AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
                Write();
            }
            catch (Exception error)
            {
                Finish(error.ToString());
            }
        }

        static void Tick()
        {
            if (!running || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount)
                return;
            frame = Time.frameCount;
            try
            {
                Require(failure == null && game && game.QueueError == null, failure ?? "Game or queue failed.");
                Require(EditorApplication.timeSinceStartup - suiteBegan < 600, "Suite exceeded 600 seconds.");
                if (!activeCase)
                {
                    if (step == TotalCases)
                        Finish(null);
                    else
                        StartCase();
                    return;
                }
                Require(EditorApplication.timeSinceStartup - caseBegan < 30, "Case exceeded 30 seconds.");
                if (!Guard)
                {
                    ObservePresentation();
                    if (!pair)
                        return;
                    CheckOwnership();
                    ObserveRecovery();
                    if (Interrupt && pair.IsRecovering && sourceRecovered && targetRecovered && !interrupted)
                    {
                        interrupted = true;
                        pair.Cancel();
                    }
                    if (pair.Playing)
                    {
                        Require(pair.SampleTime >= previousSample, "Shared source time moved backwards.");
                        previousSample = pair.SampleTime;
                    }
                }
                if (game.IsEventQueueBusy || source.IsBusy || target.IsBusy || pair && pair.Playing)
                {
                    settledAt = 0;
                    return;
                }
                if (settledAt == 0)
                    settledAt = EditorApplication.timeSinceStartup;
                if (EditorApplication.timeSinceStartup - settledAt < .65)
                    return;
                CheckCompletion();
                report.AppendLine($"PASS case={step + 1}/{TotalCases} scenario={Scenario} action={action} " +
                    $"source={source.name} reversed={Orientation >= 2} contacts={contacts} starts={starts} " +
                    $"getUp={sourceRecovered}/{targetRecovered} progress=" +
                    $"{sourceRecoveryProgress:F3}/{targetRecoveryProgress:F3} " +
                    $"seconds={EditorApplication.timeSinceStartup - caseBegan:F3}");
                if (Lethal)
                    ResetLethalCase();
                activeCase = false;
                step++;
                Write();
            }
            catch (Exception error)
            {
                Finish(error.ToString());
            }
        }

        static void PlayModeChanged(PlayModeStateChange state)
        {
            if (running && state == PlayModeStateChange.ExitingPlayMode)
                Finish("Play Mode exited before validation completed.");
        }

        static void BeforeReload()
        {
            if (running)
                Finish("Assembly reload interrupted validation.");
        }

        static void Finish(string error)
        {
            running = false;
            activeCase = false;
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
            try
            {
                RestoreState();
            }
            catch (Exception cleanup)
            {
                error = (error ?? "") + " Cleanup: " + cleanup;
            }
            report.AppendLine(error == null ? $"PASS all {TotalCases} cases; visual review remains separate."
                : $"FAIL case={step + 1} action={action}: {error}");
            Write();
        }

        static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        static void Write()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, report.ToString());
        }
    }
}
