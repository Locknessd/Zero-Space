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
    public static partial class CombatExpansionSlapPlayCheck
    {
        const string Output = "GeneratedAssets/CombatExpansion/SlapStudy/PlayMode";
        enum Scenario { Normal, Interrupt, Reset, Disable, Pause, Repeated, RangeGuard }
        static readonly StringBuilder report = new StringBuilder();
        static readonly StringBuilder csv = new StringBuilder();
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
        static bool running, activeCase, loopInstalled, acted, resumed, recovered, cancelled;
        static int step, repeat, plays, frame, starts, contacts, sourceId, targetId, sourceEnded, targetEnded;
        static int audioBefore, effectsBefore, holdsBefore, lightsBefore, shakesBefore, recoveryFrames;
        static double suiteBegan, caseBegan, settledAt, pausedAt;
        static string failure, action, status;
        static Scenario Mode => (Scenario)(step / 8);
        static bool Guard => step >= 48;
        static bool Queued => Mode != Scenario.Interrupt && Mode != Scenario.Disable;
        static bool Success => Mode == Scenario.Normal || Mode == Scenario.Pause || Mode == Scenario.Repeated;
        static int Orientation => step % 4;
        static int Direction => Orientation >= 2 ? -1 : 1;
        static double Now => EditorApplication.timeSinceStartup;

        public static void Begin()
        {
            if (!EditorApplication.isPlaying || running)
                throw new InvalidOperationException("Begin once in isolated BattleScene Play Mode.");
            report.Clear();
            csv.Clear();
            csv.AppendLine("case,scenario,action,avatar,direction,play,result,contacts,recoveryFrames,snapMeters,seconds");
            report.AppendLine("48 main cases: two actions x two attacker avatars x two directions x six scenarios.");
            report.AppendLine("Repeated cases execute twice: 56 accepted plays plus 8 direct range rejections.");
            report.AppendLine("Normal/repeat/reset/pause use EnqueueCombatAction; " +
                "interrupt/disable use direct registered move for targeted injection.");
            report.AppendLine("EXCLUDED: lethal visual treatment remains unfinished (native receiver ends upright).");
            report.AppendLine("Blocks are unsupported; range rejection is tested without fabricated block reactions.");
            status = "RUNNING";
            saved = false;
            step = repeat = plays = 0;
            activeCase = false;
            failure = null;
            contactIds.Clear();
            try
            {
                game = Object.FindAnyObjectByType<GameManager>();
                Require(game && !game.IsAnimationTestMode && !game.IsEventQueueBusy && game.QueueError == null,
                    "Requires idle gameplay GameManager outside animation test mode.");
                fighters = new[] { game.leftCombat, game.rightCombat };
                Require(fighters.All(f => f && f.isActiveAndEnabled && f.IsIdleAndSettled) && Time.timeScale > 0,
                    "Requires two active settled fighters and a live clock.");
                feedback = game.GetComponent<BattleImpactFeedback>();
                lighting = game.GetComponent<BattleLightingRig>();
                shake = game.gameObject.scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<BattleCameraShake>(true)).Single();
                director = shake.GetComponent<FrankCinematicCamera>();
                Require(feedback && feedback.isActiveAndEnabled && lighting && lighting.isActiveAndEnabled &&
                    shake.isActiveAndEnabled && game.battleSfx && game.battleSfx.isActiveAndEnabled &&
                    game.battleVfx && game.battleVfx.isActiveAndEnabled && feedback.flash &&
                    feedback.flash.isActiveAndEnabled && game.battleVfx.weaponTrails &&
                    game.battleVfx.weaponTrails.isActiveAndEnabled && director && director.isActiveAndEnabled,
                    "All live battle presentation must remain enabled.");
                Require(!feedback.IsHolding && !feedback.IsSlowing && !shake.IsShaking &&
                    lighting.ActiveFlashCount == 0 && game.battleVfx.ActiveEffectCount == 0 &&
                    game.battleSfx.ActiveVoiceCount == 0, "Begin only after presentation has settled.");
                SaveState();
                running = true;
                frame = -1;
                suiteBegan = Now;
                game.enableLocalInputTesting = false;
                CreateFixtures();
                game.battleVfx.SequenceBegan += SequenceBegan;
                game.battleVfx.ContactOccurred += Contact;
                game.battleVfx.EffectPlayed += EffectPlayed;
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
            Require(items.Any(item => item.type == typeof(PostLateUpdate)), "Missing post-late frame phase.");
            items.Add(new PlayerLoopSystem { type = typeof(CombatExpansionSlapPlayCheck), updateDelegate = Tick });
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
                item.type != typeof(CombatExpansionSlapPlayCheck)).ToArray();
            PlayerLoop.SetPlayerLoop(loop);
            loopInstalled = false;
        }

        static void Watchdog()
        {
            if (running && (Now - suiteBegan >= 1200 || activeCase && Now - caseBegan >= 45))
                Finish("Wall-clock timeout: suite <=1200s, play <=45s.");
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
                    if (step == 56)
                        Finish(null);
                    else
                        StartCase();
                    return;
                }
                ObservePresentation();
                if (!Guard)
                {
                    if (!pair)
                        return;
                    ObserveRecovery();
                    ApplyScenario();
                }
                if (game.IsEventQueueBusy || source.IsBusy || target.IsBusy || pair && pair.Playing)
                {
                    settledAt = 0;
                    return;
                }
                if (settledAt == 0)
                    settledAt = Now;
                // Cancellation survives the original source duration, exposing delayed callbacks.
                double quiet = cancelled ? move.sourcePair.attack.length + .35 : .65;
                if (Now - settledAt < quiet)
                    return;
                CheckCompletion();
                Record("PASS");
                activeCase = false;
                if (!Guard)
                    plays++;
                if (Mode == Scenario.Repeated && repeat == 0)
                    repeat = 1;
                else
                {
                    repeat = 0;
                    step++;
                }
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
                    RestoreState();
                }
                catch (Exception cleanup)
                {
                    error = (error ?? "") + " Cleanup: " + cleanup;
                }
            }
            if (error != null)
                failure = error;
            status = error == null ? "PASS_NONLETHAL" : "FAIL";
            if (error != null && activeCase)
                Record("FAIL");
            activeCase = false;
            report.AppendLine(error == null ? "PASS 48 main cases / 56 plays and 8 range guards; lethal EXCLUDED."
                : $"FAIL case={step + 1} scenario={Mode} action={action}: {error}");
            Write();
        }

        static void Record(string result)
        {
            string row = $"{step + 1},{Mode},{action},{source.Animator.avatar.name},{Direction},{repeat + 1}," +
                $"{result},{contacts},{recoveryFrames},{maxSnap:F6},{Now - caseBegan:F3}";
            csv.AppendLine(row);
            report.AppendLine(row);
            report.AppendLine($"Feedback cue={expectedCue.seconds:F6}s audio={sawAudio} vfx={sawEffects} " +
                $"lightHold={sawHold} light={sawLights} shake={sawShake} flash={sawFlash} surface={sawSurface} " +
                $"callbacks={sourceEnded}/{targetEnded} pausedFrames={pausedFrames}");
        }

        static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        static void Write()
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/Status.txt", $"{status} case={step + 1}/56 plays={plays}\n" +
                "Lethal terminal visual treatment excluded.\n" + (failure ?? ""));
            File.WriteAllText(Output + "/Report.txt", report.ToString());
            File.WriteAllText(Output + "/Cases.csv", csv.ToString());
        }
    }
}
