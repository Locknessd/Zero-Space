using System;
using System.Collections.Generic;
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
    public static partial class CombatExpansionGreatSwordPlayCheck
    {
        static readonly string[] GreatSwordActions =
        {
            "Light_1", "Frank_GreatSword_Attack_Ambush", "Frank_GreatSword_Attack_Execution1",
            "Frank_GreatSword_Attack_Execution2", "Frank_GreatSword_Attack_Execution3", "Heavy_6"
        };
        static SamuraiConfiguration samuraiConfiguration;
        static bool samuraiSuite => samuraiConfiguration != null;
        static string[] Actions => samuraiSuite ? samuraiConfiguration.actions : GreatSwordActions;
        static string ReportPath => samuraiSuite
            ? samuraiConfiguration.directory + "/Report.txt"
            : "GeneratedAssets/CombatExpansion/GreatSwordPlayMode.txt";
        static int GuardCases => samuraiSuite ? 4 : 16;
        static int QueueCases => Actions.Length * 4;
        static int BasicCases => GuardCases + QueueCases + 8;
        static int TotalCases => BasicCases + (samuraiSuite ? 16 : 0);
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
        static bool running, activeCase, interrupted, targetRecovered, loopInstalled;
        static int step, frame, starts, contacts, sourceId, targetId, sourceEnded, targetEnded, recoveryFrames;
        static int audioBefore, effectsBefore, holdsBefore, lightsBefore, shakesBefore, trailsBefore;
        static float previousSample, targetRecoveryProgress;
        static double suiteBegan, caseBegan, settledAt;
        static string failure, action;
        static bool Guard => step < GuardCases;
        static bool Queued => step >= GuardCases && step < GuardCases + QueueCases;
        static bool Lethal => step >= GuardCases + QueueCases && step < GuardCases + QueueCases + 4;
        static bool Interrupt => step >= GuardCases + QueueCases + 4 && step < BasicCases;
        static bool GreatSword => action != null && action.StartsWith("Frank_GreatSword_", StringComparison.Ordinal);
        static int Orientation => Guard ? step / (samuraiSuite ? 1 : 4) :
            Queued ? (step - GuardCases) / Actions.Length : step % 4;
        static string Scenario => Lifecycle ? LifecycleScenario : Guard ? "range guard" :
            Queued ? "queued" : Lethal ? "lethal" : "source interrupt";
        static double Now => EditorApplication.timeSinceStartup;

        public static void Begin() => Begin(null);
        public static void BeginSamurai() => Begin(Samurai01Configuration);
        public static void BeginSamurai10() => Begin(Samurai10Configuration);

        static void Begin(SamuraiConfiguration configuration)
        {
            if (!EditorApplication.isPlaying || running)
                throw new InvalidOperationException("Begin once in isolated BattleScene Play Mode.");
            samuraiConfiguration = configuration;
            report.Clear();
            ResetSamuraiSuite();
            report.AppendLine($"RUNNING: {GuardCases} range guards; {QueueCases} queued old-new-old; " +
                "4 accepted lethal; 4 source interrupts.");
            if (samuraiSuite)
                report.AppendLine("Extension: 4 reset after contact; 4 receiver disable after contact; " +
                    $"4 {SamuraiRecoveryPose} pause/resume; " +
                    "4 repeated pairs of queued Samurai activations (44 plays total).");
            report.AppendLine("Each queue orientation runs " + string.Join(", ", Actions) + ".");
            report.AppendLine("Visual contact choreography and subjective quality require separate visual review.");
            saved = false;
            step = 0;
            activeCase = false;
            action = null;
            try
            {
                game = Object.FindAnyObjectByType<GameManager>();
                Require(game && !game.IsAnimationTestMode && !game.IsEventQueueBusy && game.QueueError == null,
                    "Requires an idle gameplay GameManager outside animation test mode.");
                fighters = new[] { game.leftCombat, game.rightCombat };
                Require(fighters.All(f => f && f.isActiveAndEnabled && f.IsIdleAndSettled) && Time.timeScale > 0,
                    "Requires two active, living, idle fighters and a live clock.");
                feedback = game.GetComponent<BattleImpactFeedback>();
                lighting = game.GetComponent<BattleLightingRig>();
                shake = game.gameObject.scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<BattleCameraShake>(true)).Single();
                director = shake.GetComponent<FrankCinematicCamera>();
                Require(feedback && feedback.isActiveAndEnabled && lighting && lighting.isActiveAndEnabled &&
                    shake.isActiveAndEnabled && game.battleSfx && game.battleSfx.isActiveAndEnabled &&
                    game.battleVfx && game.battleVfx.isActiveAndEnabled && feedback.flash &&
                    feedback.flash.isActiveAndEnabled && game.battleVfx.weaponTrails &&
                    game.battleVfx.weaponTrails.isActiveAndEnabled && director && director.isActiveAndEnabled &&
                    director.cinematic, "All live presentation must remain enabled.");
                Require(!feedback.IsHolding && !feedback.IsSlowing && !shake.IsShaking &&
                    lighting.ActiveFlashCount == 0 && game.battleVfx.ActiveEffectCount == 0 &&
                    game.battleSfx.ActiveVoiceCount == 0, "Begin only after existing presentation has settled.");
                SaveState();
                frame = -1;
                failure = null;
                contactIds.Clear();
                suiteBegan = Now;
                running = true;
                game.enableLocalInputTesting = false;
                CreateFixtures();
                game.battleVfx.SequenceBegan += SequenceBegan;
                game.battleVfx.ContactOccurred += Contact;
                game.battleVfx.EffectPlayed += EffectPlayed;
                game.battleVfx.EffectsCleared += SamuraiCleanupCleared;
                game.battleSfx.CuePlayed += AudioPlayed;
                Application.logMessageReceived += UnexpectedLog;
                foreach (var fighter in fighters)
                    fighter.SequenceEnded += SequenceEnded;
                InstallLoop();
                EditorApplication.update += Watchdog;
                EditorApplication.playModeStateChanged += PlayModeChanged;
                AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
                Write();
            }
            catch (Exception error)
            {
                Finish(error.ToString());
            }
        }

        static void InstallLoop()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            var items = loop.subSystemList.ToList();
            Require(items.Any(item => item.type == typeof(PostLateUpdate)), "Missing real-frame post-late phase.");
            items.Add(new PlayerLoopSystem
            {
                type = typeof(CombatExpansionGreatSwordPlayCheck),
                updateDelegate = Tick
            });
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
                item.type != typeof(CombatExpansionGreatSwordPlayCheck)).ToArray();
            PlayerLoop.SetPlayerLoop(loop);
            loopInstalled = false;
        }

        static void Watchdog()
        {
            if (running && (Now - suiteBegan >= 600 || activeCase && Now - caseBegan >= 30))
                Finish("Wall-clock timeout: suite <=600s, case <=30s.");
        }

        static void Tick()
        {
            if (!running || !EditorApplication.isPlaying || frame == Time.frameCount)
                return;
            frame = Time.frameCount;
            try
            {
                Require(failure == null && game && game.QueueError == null, failure ?? "Game or queue failed.");
                if (!activeCase)
                {
                    CheckReleasedObjects();
                    if (step == TotalCases)
                        Finish(null);
                    else
                        StartCase();
                    return;
                }
                MeasureSamuraiFrame();
                ObserveSamuraiCleanup();
                if (!Guard)
                {
                    ObservePresentation();
                    if (!pair)
                        return;
                    CheckOwnership();
                    ObserveRecovery();
                    ObserveSamuraiEvidence();
                    ApplySamuraiLifecycle();
                    if (Interrupt && pair.Playing && !interrupted)
                    {
                        Require(!pair.IsRecovering && contacts == 0 && pair.SampleTime < expectedCues[0].seconds,
                            "Missed source-motion cancellation before the first authored strike.");
                        if (pair.SampleTime > 0)
                        {
                            interrupted = true;
                            pair.Cancel();
                        }
                    }
                }
                if (game.IsEventQueueBusy || source.IsBusy || target.IsBusy || pair && pair.Playing)
                {
                    settledAt = 0;
                    return;
                }
                if (settledAt == 0)
                    settledAt = Now;
                double quietSeconds = Interrupt || LifecycleCancelled
                    ? Math.Max(.65, move.sourcePair.attack.length + .25) : .65;
                if (Now - settledAt < quietSeconds)
                    return;
                CheckCompletion();
                if (!AwaitSamuraiCleanup())
                    return;
                CheckSamuraiCompletion();
                report.AppendLine($"PASS case={step + 1}/{TotalCases} scenario={Scenario} action={action} " +
                    $"source={source.name} avatar={source.Animator.avatar.name} direction={Direction} " +
                    $"contacts={contacts} starts={starts} callbacks={sourceEnded}/{targetEnded} " +
                    $"getUpFrames={recoveryFrames} progress={targetRecoveryProgress:F3} " +
                    $"audio={game.battleSfx.PlayedCueCount - audioBefore} " +
                    $"vfx={game.battleVfx.PlayedEffectCount - effectsBefore} " +
                    $"hold={feedback.HitStopCount - holdsBefore} light={lighting.PlayedFlashCount - lightsBefore} " +
                    $"shake={shake.ShakeCount - shakesBefore} trail={sawTrail} flash={sawFlash} " +
                    $"seconds={Now - caseBegan:F3}");
                activeCase = false;
                if (Lethal)
                    ResetLethalCase();
                if (CompleteSamuraiPlay())
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
            if (game && game.battleVfx)
                game.battleVfx.EffectsCleared -= SamuraiCleanupCleared;
            EditorApplication.update -= Watchdog;
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
            try
            {
                RemoveLoop();
            }
            finally
            {
                try
                {
                        RestoreSamuraiLifecycle();
                    RestoreState();
                }
                catch (Exception cleanup)
                {
                    error = (error ?? "") + " Cleanup: " + cleanup;
                }
            }
            report.AppendLine(error == null ? $"PASS all {TotalCases} cases; visual review remains separate."
                : $"FAIL case={step + 1} action={action}: {error.Replace('\n', ' ').Replace('\r', ' ')}");
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
