using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FrankRetarget.Editor
{
    [InitializeOnLoad]
    public static class FrankBattleAnimationTestPlayCheck
    {
        const string Key = "BattleAnimationTestPlayCheck";
        const string Copy = "Assets/DemoSence/Editor/BattleAnimationTestPlayCheck.unity";
        const string Review = FrankRetargetBuilder.AnimationTestReview;
        static GameManager game;
        static BattleAnimationTestPanel panel;
        static readonly StringBuilder report = new StringBuilder();
        static readonly HashSet<string> observed = new HashSet<string>();
        static int phase, lastFrame = -1, targetCompleted, contactsChecked;
        static double began, phaseBegan;
        static string leftHp, rightHp;
        static Vector3 leftPosition, rightPosition;
        static bool koCaptured, browserCaptured;
        static int timingCase, timingSlowCount, timingPendingFrames;
        static bool timingSawSlow, timingSawAnimationWait, timingSawSlowWait, timingResumed;
        static double timingShownAt;

        static FrankBattleAnimationTestPlayCheck()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += Restore;
        }

        static string ReportPath => SessionState.GetBool(Key + ".timing", false) ? FrankRetargetBuilder.KoTimingReview + "/PlayValidation.txt" :
            Review + (SessionState.GetBool(Key + ".cleanup", false) ? "/CleanupValidation.txt" : "/PlayValidation.txt");

        public static void Start(bool cleanupOnly = false, bool timingOnly = false)
        {
            if (EditorApplication.isPlaying) throw new Exception("Run this check in Edit Mode.");
            if (File.Exists(Copy)) throw new Exception("Temporary animation browser scene already exists.");
            File.Copy("Assets/Scenes/BattleScene.unity", Copy);
            AssetDatabase.ImportAsset(Copy);
            var scene = EditorSceneManager.OpenScene(Copy, OpenSceneMode.Additive);
            try
            {
                foreach (var socket in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<WebSocketManager>(true)))
                    socket.gameObject.SetActive(false);
                EditorSceneManager.SaveScene(scene);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
            SessionState.SetString(Key + ".previous", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(Copy);
            SessionState.SetBool(Key + ".restore", true);
            SessionState.SetBool(Key, true);
            SessionState.SetBool(Key + ".cleanup", cleanupOnly);
            SessionState.SetBool(Key + ".timing", timingOnly);
            Directory.CreateDirectory(Review);
            if (timingOnly) Directory.CreateDirectory(FrankRetargetBuilder.KoTimingReview);
            File.WriteAllText(ReportPath, "RUNNING: actual Battle panel, pointer clicks, animation pairs, KO and reset.\n");
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            try
            {
                if (began == 0) began = EditorApplication.timeSinceStartup;
                if (EditorApplication.timeSinceStartup - began > 300) throw new Exception("Animation browser check timed out at phase " + phase);
                if (lastFrame == Time.frameCount || EditorApplication.timeSinceStartup - began < 1) return;
                lastFrame = Time.frameCount;
                Application.runInBackground = true;
                if (!game)
                {
                    game = GameManager.Instance;
                    if (!game) return;
                    panel = game.gameObject.scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BattleAnimationTestPanel>()).Single();
                    game.debugMode = false;
                    if (!panel.OpenButton) return;
                    leftHp = game.uiManager.left.healthText.text; rightHp = game.uiManager.right.healthText.text;
                    leftPosition = game.leftCombat.Animator.transform.position; rightPosition = game.rightCombat.Animator.transform.position;
                    phaseBegan = EditorApplication.timeSinceStartup;
                }
                if (game.QueueError != null) throw new Exception(game.QueueError);
                if (SessionState.GetBool(Key + ".timing", false)) { TickTiming(); return; }
                if (SessionState.GetBool(Key + ".cleanup", false)) { TickCleanup(); return; }
                CheckContactHealth();
                switch (phase)
                {
                    case 0:
                        Click(panel.OpenButton);
                        int expected = new[] { game.leftCombat, game.rightCombat }.Sum(f => f.lightCombatMoves.Length + f.heavyCombatMoves.Length);
                        if (!game.IsAnimationTestMode || !panel.IsOpen || panel.Entries.Count != expected) throw new Exception("Browser does not expose both saved move pools.");
                        if (panel.KoToggle.isOn) throw new Exception("KO preview must start disabled.");
                        report.AppendLine("PASS pointer-click open; " + expected + " pairs loaded from both fighters' live pools.");
                        Advance();
                        break;
                    case 1:
                        if (Elapsed < .25) return;
                        if (!browserCaptured)
                        {
                            // Newly enabled UI must render once before its Graphics acquire raycast depth.
                            foreach (var option in new[] { "LIGHT", "HEAVY", "LEFT", "RIGHT", "ALL" })
                            {
                                Click(panel.GetComponentsInChildren<Button>().Single(button => button.name == option));
                                int visible = panel.Entries.Count(entry => entry.button.gameObject.activeSelf);
                                int expectedVisible = panel.Entries.Count(entry => option == "ALL" || option == "LIGHT" && !entry.heavy || option == "HEAVY" && entry.heavy ||
                                    option == "LEFT" && entry.side == PlayerUI.Side.Left || option == "RIGHT" && entry.side == PlayerUI.Side.Right);
                                if (visible != expectedVisible) throw new Exception("Incorrect filter " + option);
                            }
                            report.AppendLine("PASS all five filter buttons and KO disabled by default.");
                            ScreenCapture.CaptureScreenshot(Review + "/PairBrowser.png");
                            browserCaptured = true;
                            return;
                        }
                        Click(panel.Entries[0].button);
                        if (!ReferenceEquals(game.AnimationTestMove, panel.Entries[0].move) || panel.IsOpen) throw new Exception("Row did not choose the exact move or collapse the browser.");
                        Advance();
                        break;
                    case 2:
                        if (Elapsed < .45) return;
                        if (!game.IsAnimationTestPlaying) throw new Exception("Expected an active pair before the Stop test.");
                        Click(panel.StopButton);
                        AssertStopped();
                        report.AppendLine("PASS row pointer click, exact selection, auto-collapse, mid-animation Stop and pose/clock cleanup.");
                        Click(panel.ReplayButton);
                        targetCompleted = game.CompletedAnimationTests + 1;
                        Advance();
                        break;
                    case 3:
                        if (game.IsAnimationTestPlaying) return;
                        if (game.CompletedAnimationTests != targetCompleted) throw new Exception("Replay did not complete.");
                        report.AppendLine("PASS Replay completes the selected pair and get-up with preview damage.");
                        targetCompleted = game.CompletedAnimationTests + panel.Entries.Count;
                        Click(panel.PlayAllButton);
                        Advance();
                        break;
                    case 4:
                        if (panel.PlayAllActive || game.IsAnimationTestPlaying) return;
                        if (game.CompletedAnimationTests != targetCompleted || observed.Count != panel.Entries.Count)
                            throw new Exception("Play All omitted or repeated a move: observed=" + observed.Count + " completed=" + game.CompletedAnimationTests);
                        report.AppendLine("PASS Play All: every " + panel.Entries.Count + " pair played once with the correct attacker, exact source pair, VFX/SFX and recovery.");
                        Click(panel.NextButton);
                        if (game.AnimationTestAttacker != panel.Entries[0].side || game.AnimationTestMove != panel.Entries[0].move)
                            throw new Exception("Next did not wrap to the first pair.");
                        Click(panel.StopButton);
                        ToggleKo();
                        if (!panel.PlayEntry(KoEntry(PlayerUI.Side.Left, BattleSkill.Archer)))
                            throw new Exception("Could not start heavy KO preview.");
                        Advance();
                        break;
                    case 5:
                        if (game.uiManager.knockout.HasShown && !koCaptured)
                        {
                            ScreenCapture.CaptureScreenshot(Review + "/KoPreview.png");
                            koCaptured = true;
                        }
                        if (game.IsAnimationTestPlaying || game.GetComponent<BattleImpactFeedback>().IsSlowing ||
                            !game.uiManager.knockout.HasShown) return;
                        if (!koCaptured || !game.rightCombat.IsDead || game.uiManager.right.healthText.text.Split('/')[0] != "0")
                            throw new Exception("KO preview did not reach the death pose at zero HP.");
                        Click(panel.StopButton);
                        AssertStopped();
                        report.AppendLine("PASS heavy KO preview, KO UI, death pose, slow motion, Next wrap and Stop cleanup.");
                        // Server messages must wait while the browser owns the actors.
                        game.ApplyRawMessage(new JObject { ["type"] = "meme_battle_event", ["event"] = new JObject
                        { ["matchId"] = "animation-browser-check", ["eventId"] = "queued-turn", ["eventType"] = "TURN_STARTED",
                          ["payload"] = new JObject { ["turnNumber"] = 7 } } }.ToString());
                        Advance();
                        break;
                    case 6:
                        if (Elapsed < .3) return;
                        if (game.PendingEventCount != 1 || game.uiManager.roundNumberText.text == "Turn 7")
                            throw new Exception("A server event interleaved with animation test playback.");
                        Click(panel.ExitButton);
                        if (game.IsAnimationTestMode || game.IsAnimationTestPlaying || !panel.OpenButton.gameObject.activeSelf ||
                            game.uiManager.left.healthText.text != leftHp || game.uiManager.right.healthText.text != rightHp)
                            throw new Exception("Exit did not restore the original battle HUD.");
                        if (Vector3.Distance(game.leftCombat.Animator.transform.position, leftPosition) > .001f ||
                            Vector3.Distance(game.rightCombat.Animator.transform.position, rightPosition) > .001f)
                            throw new Exception("Exit changed the original fighter positions.");
                        Advance();
                        break;
                    case 7:
                        if (Elapsed < .3 || game.IsEventQueueBusy) return;
                        if (game.uiManager.roundNumberText.text != "Turn 7") throw new Exception("Queued server event did not resume on exit.");
                        // Disable also restores an existing user pause after a preview.
                        Time.timeScale = 0;
                        Click(panel.OpenButton);
                        if (!panel.PlayEntry(panel.Entries[0])) throw new Exception("Paused browser could not start.");
                        panel.enabled = false;
                        if (game.IsAnimationTestMode || game.IsAnimationTestPlaying || Time.timeScale != 0 ||
                            game.leftCombat.IsBusy || game.rightCombat.IsBusy) throw new Exception("Disable failed to restore the original pause.");
                        report.AppendLine("PASS exit restores HP/roots; pending server event resumes; disable cancels preview and restores pre-existing pause.");
                        Finish(true, "PASS native animation test browser; " + contactsChecked + " sampled contact frames verified against the move's damage timeline.");
                        break;
                }
            }
            catch (Exception exception) { Finish(false, exception.ToString()); }
        }

        static double Elapsed => EditorApplication.timeSinceStartup - phaseBegan;
        static BattleAnimationTestPanel.Entry KoEntry(PlayerUI.Side side, BattleSkill preferred) =>
            panel.Entries.FirstOrDefault(e => e.side == side && e.move.skill == preferred) ??
            panel.Entries.First(e => e.side == side && e.heavy);
        static void Advance()
        {
            phase++; phaseBegan = EditorApplication.timeSinceStartup;
            File.WriteAllText(ReportPath, report.ToString() + "RUNNING phase " + phase + "\n");
        }

        static void CheckContactHealth()
        {
            if (!game || !game.IsAnimationTestPlaying) return;
            var fighter = game.AnimationTestAttacker == PlayerUI.Side.Left ? game.leftCombat : game.rightCombat;
            if ((game.leftCombat.Animator.transform.localScale - Vector3.one * .7f).sqrMagnitude > .00000001f ||
                (game.rightCombat.Animator.transform.localScale - Vector3.one * .1f).sqrMagnitude > .00000001f)
                throw new Exception("Battle model scale changed during animation preview.");
            var playback = fighter.SourcePlayback;
            if (!playback || !playback.Playing || playback.Move != game.AnimationTestMove) return;
            if (phase == 4) observed.Add(game.AnimationTestAttacker + ":" + playback.Move.moveName);
            var profile = game.battleVfx.timeline.FindMove(playback.Move);
            float[] times = BattleHitDamageSequence.ContactTimes(profile, playback.Duration);
            int hits = times.Count(time => time <= playback.SampleTime);
            long full = Math.Max(10, game.uiManager.defaultInitialMaxHpAtomic);
            long damage = game.AnimationTestLethal ? full : full / 10 * 3 + full % 10 * 3 / 10;
            long expected = hits == times.Length ? full - damage : full - (damage / times.Length * hits + Math.Max(0, hits - (times.Length - damage % times.Length)));
            var slot = game.AnimationTestAttacker == PlayerUI.Side.Left ? game.uiManager.right : game.uiManager.left;
            if (long.Parse(slot.healthText.text.Split('/')[0]) != expected) throw new Exception("Preview HP not at contact time for " + playback.Move.moveName);
            contactsChecked++;
        }

        static void AssertStopped()
        {
            if (game.IsAnimationTestPlaying || game.leftCombat.IsBusy || game.rightCombat.IsBusy ||
                game.leftCombat.IsDead || game.rightCombat.IsDead || game.battleVfx.ActiveEffectCount != 0 ||
                game.GetComponent<BattleImpactFeedback>().IsHolding || game.GetComponent<BattleImpactFeedback>().IsSlowing || Time.timeScale != 1)
                throw new Exception("Stop retained busy/dead/VFX/slow-clock state.");
        }

        static void Click(Button button)
        {
            if (!button || !button.interactable || !button.gameObject.activeInHierarchy) throw new Exception("Button unavailable.");
            Canvas.ForceUpdateCanvases();
            var rect = button.transform as RectTransform;
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)), button = PointerEventData.InputButton.Left };
            var results = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, results);
            if (results.Count == 0 || results[0].gameObject != button.gameObject && !results[0].gameObject.transform.IsChildOf(button.transform))
                throw new Exception("Button is occluded: " + button.name + " by " + (results.Count == 0 ? "no rendered graphic" : results[0].gameObject.name));
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        static void ToggleKo()
        {
            var box = panel.KoToggle.targetGraphic.rectTransform;
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, box.TransformPoint(box.rect.center)) };
            ExecuteEvents.Execute(panel.KoToggle.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            if (!panel.KoToggle.isOn) throw new Exception("KO toggle did not enable.");
        }

        static void TickCleanup()
        {
            if (phase == 0)
            {
                Click(panel.OpenButton);
                panel.KoToggle.SetIsOnWithoutNotify(true);
                panel.PlayEntry(KoEntry(PlayerUI.Side.Left, BattleSkill.Archer));
                Advance();
                return;
            }
            if (!game.uiManager.knockout.HasShown) return;
            panel.Exit();
            var ko = game.uiManager.knockout;
            UnityEngine.Object.DestroyImmediate(ko.word.gameObject);
            if (ko.band) UnityEngine.Object.DestroyImmediate(ko.band.gameObject);
            if (ko.subtitle) UnityEngine.Object.DestroyImmediate(ko.subtitle.gameObject);
            game.uiManager.ResetTransientEffects();
            if (game.IsAnimationTestMode || game.IsAnimationTestPlaying || Time.timeScale != 1 || game.leftCombat.IsBusy || game.rightCombat.IsBusy)
                throw new Exception("KO exit did not release the test actors/clock.");
            Finish(true, "PASS exit during active KO and HUD cleanup after KO child RectTransforms are destroyed; native scene unload follows.");
        }

        static void StartTimingCase()
        {
            panel.Stop();
            var feedback = game.GetComponent<BattleImpactFeedback>();
            feedback.knockoutSlowMotion = timingCase != 4;
            // Test both orderings independently of the selected skill's current
            // duration. These overrides exist only in the isolated test scene.
            feedback.knockoutSeconds = timingCase == 1 ? .12f : timingCase == 0 ? 3f : 1.8f;
            feedback.knockoutRecovery = timingCase == 1 ? .08f : .3f;
            timingSlowCount = feedback.SlowMotionCount;
            timingPendingFrames = 0;
            timingSawSlow = timingSawAnimationWait = timingSawSlowWait = timingResumed = false;
            timingShownAt = 0;
            phaseBegan = EditorApplication.timeSinceStartup;
            if (timingCase == 3 || timingCase == 5)
            {
                if (timingCase == 5) Time.timeScale = 0;
                game.rightCombat.MarkDead();
                game.uiManager.UpdateHealth(MemeBattleUI.Side.Right, 0, game.uiManager.defaultInitialMaxHpAtomic);
                return;
            }
            var entry = timingCase == 1 ? panel.Entries.First(e => e.side == PlayerUI.Side.Right && e.move.weapon == TrumpWeaponManager.WeaponType.GreatSword) :
                timingCase == 2 ? KoEntry(PlayerUI.Side.Left, BattleSkill.WhiteMage) :
                timingCase == 4 ? panel.Entries.First(e => e.side == PlayerUI.Side.Left && e.move.weapon == TrumpWeaponManager.WeaponType.Katana) :
                KoEntry(PlayerUI.Side.Left, BattleSkill.Archer);
            if (timingCase == 0)
            {
                var pair = entry.move.sourcePair;
                float duration = Mathf.Max(pair.attack.length, pair.reactionDelay + pair.reaction.length);
                float contact = BattleHitDamageSequence.ContactTimes(game.battleVfx.timeline.FindMove(entry.move), duration).Last();
                feedback.knockoutSeconds = Mathf.Max(3, (duration - contact) / feedback.knockoutSpeed + .3f);
            }
            if (!panel.PlayEntry(entry)) throw new Exception("Could not begin KO timing case " + timingCase);
        }

        static void TickTiming()
        {
            if (phase == 0)
            {
                Click(panel.OpenButton);
                panel.KoToggle.SetIsOnWithoutNotify(true);
                phase = 1;
                StartTimingCase();
                return;
            }
            var ko = game.uiManager.knockout;
            var feedback = game.GetComponent<BattleImpactFeedback>();
            bool busy = game.leftCombat.IsBusy || game.rightCombat.IsBusy;
            if (ko.Pending)
            {
                timingPendingFrames++;
                if (!busy && feedback.IsSlowing) timingSawSlowWait = true;
                if (busy && feedback.HasPlayedKnockoutSlowMotion && !feedback.IsSlowing && !feedback.IsHolding) timingSawAnimationWait = true;
            }
            if (feedback.IsSlowing && !feedback.IsHolding) timingSawSlow = true;
            if (ko.HasShown && (busy || feedback.IsSlowing || feedback.IsHolding))
                throw new Exception("KO overlapped an unfinished animation/slow motion in case " + timingCase);
            if (ko.Pending && (ko.group.alpha != 0 || ko.HasShown)) throw new Exception("Pending KO became visible.");
            if (timingCase == 0 && ko.Pending && feedback.IsSlowing && !koCaptured)
            {
                ScreenCapture.CaptureScreenshot(FrankRetargetBuilder.KoTimingReview + "/FinisherBeforeKo.png");
                koCaptured = true;
            }
            if (timingCase == 5 && !timingResumed)
            {
                if (ko.HasShown || feedback.HasPlayedKnockoutSlowMotion || Time.timeScale != 0)
                    throw new Exception("Paused fallback KO started before resume.");
                if (Elapsed < .35) return;
                Time.timeScale = 1;
                timingResumed = true;
                return;
            }
            if (timingCase == 6 && ko.Pending)
            {
                panel.Stop();
                if (ko.HasShown || ko.Pending || feedback.IsSlowing || Time.timeScale != 1)
                    throw new Exception("Cancelled finisher retained its pending KO.");
                report.AppendLine("PASS cancel while KO is pending: no panel and clock restored.");
                panel.Exit();
                Finish(true, "PASS seven KO timing cases: animations and slow recovery precede KO, both fighter roles, result-only fallback, slow disabled, external pause/resume, duplicate suppression and cancellation.");
                return;
            }
            if (!ko.HasShown) return;
            if (timingShownAt == 0)
            {
                if (ko.ShowCount != 1 || timingPendingFrames == 0 || Time.timeScale != 1 ||
                    feedback.SlowMotionCount != timingSlowCount + (timingCase == 4 ? 0 : 1) || timingCase != 4 && !timingSawSlow)
                    throw new Exception("KO timing/slow count did not complete once in case " + timingCase);
                if (timingCase == 0 && !timingSawSlowWait) throw new Exception("Archer did not exercise animation ending before slow recovery.");
                if (timingCase == 1 && !timingSawAnimationWait) throw new Exception("GreatSword did not exercise animation continuing after slow recovery.");
                timingShownAt = EditorApplication.timeSinceStartup;
                if (timingCase == 0) ScreenCapture.CaptureScreenshot(FrankRetargetBuilder.KoTimingReview + "/KoAfterFinisher.png");
                game.uiManager.UpdateHealth(timingCase == 1 ? MemeBattleUI.Side.Left : MemeBattleUI.Side.Right,
                    0, game.uiManager.defaultInitialMaxHpAtomic);
                return;
            }
            if (EditorApplication.timeSinceStartup - timingShownAt < .4) return;
            if (ko.ShowCount != 1 || feedback.SlowMotionCount != timingSlowCount + (timingCase == 4 ? 0 : 1) || Time.timeScale != 1)
                throw new Exception("KO presentation or duplicate HP restarted slow motion.");
            report.AppendLine("PASS timing case " + timingCase + ": panel at normal speed after both actors finish; " + timingPendingFrames + " hidden pending frames; no repeated KO/slow.");
            timingCase++;
            File.WriteAllText(ReportPath, report.ToString() + "RUNNING case " + timingCase + "\n");
            StartTimingCase();
        }

        static void Finish(bool passed, string detail)
        {
            if (game) game.EndAnimationTestMode();
            Time.timeScale = 1;
            report.AppendLine((passed ? "" : "FAIL ") + detail);
            File.WriteAllText(ReportPath, report.ToString());
            SessionState.SetBool(Key, false);
            EditorApplication.isPlaying = false;
        }

        static void Restore(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key + ".restore", false)) return;
            SessionState.SetBool(Key, false); SessionState.SetBool(Key + ".restore", false);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key + ".previous", ""));
            AssetDatabase.DeleteAsset(Copy);
            Time.timeScale = 1; game = null; panel = null;
            phase = contactsChecked = 0; lastFrame = -1; began = phaseBegan = 0; koCaptured = browserCaptured = false; observed.Clear(); report.Clear();
            timingCase = timingPendingFrames = 0; timingShownAt = 0;
        }
    }
}
