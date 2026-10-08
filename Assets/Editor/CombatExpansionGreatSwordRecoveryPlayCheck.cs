using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordRecoveryPlayCheck
    {
        const string ReportPath = "GeneratedAssets/CombatExpansion/GreatSwordRecoveryPlayMode.txt";
        const int TotalCases = 48;
        enum Scenario { Natural, Cancel, Pause, Reset, Disable, Lethal }
        static readonly StringBuilder report = new StringBuilder();
        static GameManager game;
        static CharacterCombat[] fighters;
        static CharacterCombat source, target;
        static FrankBattlePairPlayback pair;
        static CombatTripletData move;
        static bool running, activeCase, acted, recovered, resumed, loopInstalled;
        static int step, sourceId, targetId, sourceEnded, targetEnded, recoveryFrames, previousFrame;
        static float previousSample, progress, previousProgress;
        static double suiteBegan, caseBegan, settledAt, pausedAt;
        static string failure;
        static Scenario Mode => step < 16 ? Scenario.Natural : step < 32 ? Scenario.Cancel :
            (Scenario)(2 + (step - 32) / 4);
        static int Orientation => step % 4;
        static int MoveIndex => step < 32 ? step % 16 / 4 : 0;
        static bool Success => Mode == Scenario.Natural || Mode == Scenario.Pause || Mode == Scenario.Lethal;
        static double Now => EditorApplication.timeSinceStartup;

        public static void Begin()
        {
            if (!EditorApplication.isPlaying || running)
                throw new InvalidOperationException("Begin once in the isolated BattleScene Play Mode session.");
            report.Clear();
            report.AppendLine("RUNNING: 48 GreatSword real-frame recovery lifecycle cases.");
            report.AppendLine("Four moves x two attacker avatars x two directions x natural/cancel = 32.");
            report.AppendLine("Ambush x both avatars/directions x pause/reset/disable/lethal = 16.");
            report.AppendLine("Live presentation remains enabled. Full contact/presentation profiles are absent; " +
                "gameplay registration, contact quality, floor clearance and presentation acceptance " +
                "remain unverified.");
            Write();
            try
            {
                game = Object.FindAnyObjectByType<GameManager>();
                Require(game && !game.IsAnimationTestMode && !game.IsEventQueueBusy && game.QueueError == null,
                    "Requires idle GameManager outside animation test mode.");
                fighters = new[] { game.leftCombat, game.rightCombat };
                Require(fighters.All(f => f && f.isActiveAndEnabled && f.IsIdleAndSettled) && Time.timeScale > 0,
                    "Requires two active, living, settled fighters and a live clock.");
                Require(game.battleSfx && game.battleSfx.isActiveAndEnabled && game.battleVfx &&
                    game.battleVfx.isActiveAndEnabled, "Existing audio and VFX must remain enabled.");
                var feedback = game.GetComponent<BattleImpactFeedback>();
                var lighting = game.GetComponent<BattleLightingRig>();
                var shakes = game.gameObject.scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<BattleCameraShake>(true)).ToArray();
                Require(feedback && feedback.isActiveAndEnabled && lighting && lighting.isActiveAndEnabled &&
                    shakes.Length > 0 && shakes.All(shake => shake.isActiveAndEnabled),
                    "Existing hit-stop, lighting and camera presentation must remain enabled.");
                SaveState();
                running = true;
                step = 0;
                activeCase = false;
                failure = null;
                previousFrame = -1;
                suiteBegan = Now;
                game.enableLocalInputTesting = false;
                CreateFixtures();
                foreach (var fighter in fighters)
                    fighter.SequenceEnded += SequenceEnded;
                InstallLoop();
                EditorApplication.update += Watchdog;
                EditorApplication.playModeStateChanged += PlayModeChanged;
                AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            }
            catch (Exception error)
            {
                Finish(error.ToString());
            }
        }

        // Appended after PostLateUpdate: every pose observation sees runtime LateUpdate corrections.
        static void InstallLoop()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            var items = loop.subSystemList.ToList();
            Require(items.Any(item => item.type == typeof(PostLateUpdate)), "Missing real frame post-late phase.");
            items.Add(new PlayerLoopSystem { type = typeof(CombatExpansionGreatSwordRecoveryPlayCheck),
                updateDelegate = FrameTick });
            loop.subSystemList = items.ToArray();
            PlayerLoop.SetPlayerLoop(loop);
            loopInstalled = true;
        }

        static void RemoveLoop()
        {
            if (!loopInstalled)
                return;
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            loop.subSystemList = loop.subSystemList.Where(item =>
                item.type != typeof(CombatExpansionGreatSwordRecoveryPlayCheck)).ToArray();
            PlayerLoop.SetPlayerLoop(loop);
            loopInstalled = false;
        }

        static void Watchdog()
        {
            if (!running)
                return;
            if (Now - suiteBegan >= 600 || activeCase && Now - caseBegan >= 30)
                Finish("Wall-clock timeout: suite <=600s, case <=30s.");
        }

        static void FrameTick()
        {
            if (!running || !EditorApplication.isPlaying || previousFrame == Time.frameCount)
                return;
            previousFrame = Time.frameCount;
            try
            {
                Require(failure == null && game && game.QueueError == null, failure ?? "Game or queue failed.");
                Require(Now - suiteBegan < 600, "Suite exceeded 600 seconds.");
                if (!activeCase)
                {
                    CheckReleasedObjects();
                    if (step == TotalCases)
                        Finish(null);
                    else
                        StartCase();
                    return;
                }
                Require(Now - caseBegan < 30, "Case exceeded 30 seconds.");
                Observe();
                ApplyScenario();
                if (pair.Playing || source.IsBusy || target.IsBusy)
                    return;
                if (settledAt == 0)
                    settledAt = Now;
                if (Now - settledAt < .2)
                    return;
                CheckCompletion();
                activeCase = false;
                ResetCase();
                report.AppendLine($"PASS case={step + 1}/{TotalCases} scenario={Mode} move={move.moveName} " +
                    $"source={source.name} direction={(Orientation >= 2 ? -1 : 1)} " +
                    $"callbacks={sourceEnded}/{targetEnded} getUpFrames={recoveryFrames} progress={progress:F3} " +
                    $"seconds={Now - caseBegan:F3}");
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
                Finish("Play Mode exited before completion.");
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
            EditorApplication.update -= Watchdog;
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
            try
            {
                RemoveLoop();
                RestoreState();
            }
            catch (Exception cleanup)
            {
                error = (error ?? "") + " Cleanup: " + cleanup;
            }
            report.AppendLine(error == null ? $"PASS all {TotalCases} lifecycle cases; acceptance remains unverified."
                : $"FAIL case={step + 1} scenario={Mode}: {error.Replace('\n', ' ').Replace('\r', ' ')}");
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
