using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    [InitializeOnLoad]
    public static class CombatFeelPlayValidation
    {
        const string Key = "ZeroSpace.CombatFeelValidation.";
        static GameManager game;
        static double queueStarted;
        static int ended;
        static readonly StringBuilder report = new StringBuilder();
        [Serializable]
        sealed class IsolatedRoots
        {
            public string[] ids;
            public string[] cleanScenes;
        }

        static CombatFeelPlayValidation()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += ModeChanged;
        }

        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Begin validation from Edit mode.");
            Directory.CreateDirectory(FrankRetargetBuilder.FeelReview);
            File.WriteAllText(FrankRetargetBuilder.FeelReview + "/PlayModeValidation.txt", "RUNNING native contact and queue validation\n");
            SessionState.SetString(Key + "active", SceneManager.GetActiveScene().path);
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/BattleScene.unity");
            bool opened = !scene.IsValid() || !scene.isLoaded;
            SessionState.SetBool(Key + "opened", opened);
            if (opened) scene = EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Additive);
            var isolated = new List<string>(); var clean = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var other = SceneManager.GetSceneAt(i);
                if (!other.isLoaded || other == scene) continue;
                if (!other.isDirty) clean.Add(other.path);
                foreach (var root in other.GetRootGameObjects())
                    if (root.activeSelf)
                    {
                        isolated.Add(GlobalObjectId.GetGlobalObjectIdSlow(root).ToString());
                        root.SetActive(false);
                    }
            }
            SessionState.SetString(Key + "isolation",JsonUtility.ToJson(new IsolatedRoots { ids=isolated.ToArray(),cleanScenes=clean.ToArray() }));
            SceneManager.SetActiveScene(scene);
            SessionState.SetInt(Key + "phase", 1);
            EditorApplication.isPlaying = true;
        }

        public static void CancelPending()
        {
            Finish();
            if (!EditorApplication.isPlayingOrWillChangePlaymode) ModeChanged(PlayModeStateChange.EnteredEditMode);
        }

        static void Tick()
        {
            int phase = SessionState.GetInt(Key + "phase", 0);
            if (phase == 0 || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            try
            {
                if (phase == 1)
                {
                    if (Time.frameCount < 3 || Time.deltaTime <= 0) return;
                    var scene = SceneManager.GetSceneByPath("Assets/Scenes/BattleScene.unity");
                    game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                    report.Clear();
                    TestContacts();
                    game.ResetCombatQueue(); game.enabled = true; ended = 0;
                    game.leftCombat.SequenceEnded += SequenceEnded;
                    game.rightCombat.SequenceEnded += SequenceEnded;
                    game.EnqueueLocalAttack(PlayerUI.Side.Left, false);
                    game.EnqueueLocalAttack(PlayerUI.Side.Right, true);
                    queueStarted = EditorApplication.timeSinceStartup;
                    SessionState.SetInt(Key + "phase", 2);
                    Write("RUNNING real Light + Heavy queue callbacks\n");
                }
                else
                {
                    if (!string.IsNullOrEmpty(game.QueueError)) throw new Exception("Queue error: " + game.QueueError);
                    if (!game.IsEventQueueBusy && ended >= 4)
                    {
                        if (game.leftCombat.IsBusy || game.rightCombat.IsBusy || Time.timeScale <= 0)
                            throw new Exception("Queue completed with held/busy fighters.");
                        report.AppendLine("PASS real Left Light + Right Heavy FIFO playback, four completion callbacks, GetUp recovery and no stuck time scale.");
                        Finish();
                    }
                    else if (EditorApplication.timeSinceStartup - queueStarted > 45)
                        throw new Exception("Real queue did not finish within 45 seconds; callbacks=" + ended);
                }
            }
            catch (Exception error) { report.AppendLine("FAIL " + error); Finish(); Debug.LogException(error); }
        }

        static void SequenceEnded(CharacterCombat fighter, int playbackId, bool succeeded)
        {
            if (!succeeded) throw new Exception("Real queue sequence failed: " + fighter.name);
            ended++;
        }

        static void TestContacts()
        {
            game.enabled = false;
            var feedback = game.GetComponent<BattleImpactFeedback>();
            var vfx = game.battleVfx;
            var tick = typeof(FrankBattlePairPlayback).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            var fighters = new[] { game.leftCombat, game.rightCombat };
            float volume = game.battleSfx.masterVolume;
            game.battleSfx.masterVolume = 0;
            try
            {
                foreach (var attacker in fighters)
                foreach (bool lethal in new[] { false, true })
                {
                    game.ResetCombatQueue();
                    Time.timeScale = 1;
                    var receiver = fighters.Single(f => f != attacker);
                    var move = attacker.heavyCombatMoves.First(m => m.skill == BattleSkill.None &&
                        vfx.timeline.FindMove(m).cues.Count(c => c.group == "heavy_hit" || c.group == "light_hit" || c.group == "stab_hit") >= 2);
                    attacker.transform.position = Vector3.zero;
                    receiver.transform.position = (attacker == game.leftCombat ? Vector3.right : Vector3.left) * move.attackRange;
                    int emitted = 0, slowBefore = feedback.SlowMotionCount;
                    Action<BattleVfxPlayer.Impact> observe = impact => emitted++;
                    vfx.ContactOccurred += observe;
                    try
                    {
                        if (!attacker.ExecuteAttack(move, receiver, lethal)) throw new Exception("Native test attack rejected.");
                        var pair = attacker.SourcePlayback;
                        var times = vfx.timeline.FindMove(move).cues.Where(c => c.group == "heavy_hit" || c.group == "light_hit" ||
                            c.group == "stab_hit" || c.group == "body_fall" || c.group == "knockout_fall").GroupBy(c => c.seconds).OrderBy(g => g.Key).ToArray();
                        pair.AdvanceTo(pair.Duration); // Deliberately cross the entire combo in one slow frame.
                        int expected = 0;
                        foreach (var contact in times)
                        {
                            expected += contact.Count();
                            if (!feedback.IsHolding || !pair.IsContactHeld || Time.timeScale != 1 || Mathf.Abs(pair.SampleTime - contact.Key) > .0001f || emitted != expected)
                                throw new Exception($"Slow frame did not stop at exactly one contact pose: {attacker.name}/{move.moveName}, lethal={lethal}, expectedTime={contact.Key}, actualTime={pair.SampleTime}, scale={Time.timeScale}, holding={feedback.IsHolding}, contacts={emitted}/{expected}.");
                            tick.Invoke(pair, null);
                            if (emitted != expected || Mathf.Abs(pair.SampleTime - contact.Key) > .0001f)
                                throw new Exception("Playback advanced or replayed a hit during its hold.");
                            feedback.AdvanceFeedback(.5f);
                            tick.Invoke(pair, null); // Exercise the carried simulation time, not a second seek.
                        }
                        if (emitted != times.Sum(g => g.Count())) throw new Exception("A deferred contact was dropped.");
                        if (lethal && feedback.SlowMotionCount - slowBefore != 1) throw new Exception("KO slow motion was missing or replayed.");
                        pair.Cancel();
                        if (feedback.IsHolding || Time.timeScale != 1) throw new Exception("Cancel did not restore time scale.");
                    }
                    finally { vfx.ContactOccurred -= observe; }
                }
                report.AppendLine("PASS both attackers, lethal/nonlethal: a frame crossing a whole combo holds each exact participant pose without changing global time; carried time resumes every hit once; KO slow motion once; cancel clears the local clock.");
                TestPauseLifecycle(feedback, tick);
                Time.timeScale = .5f; feedback.ResetFeedback();
                Time.timeScale = 0; feedback.ResetFeedback();
                if (Time.timeScale != 0) throw new Exception("Feedback reset overwrote an external pause.");
                Time.timeScale = 1;
                report.AppendLine("PASS resetting feedback preserves an external pause.");
            }
            finally
            {
                feedback.ResetFeedback(); Time.timeScale = 1;
                game.battleSfx.masterVolume = volume;
                game.ResetCombatQueue();
            }
        }

        static void TestPauseLifecycle(BattleImpactFeedback feedback, MethodInfo tick)
        {
            var attacker = game.leftCombat;
            var receiver = game.rightCombat;
            var move = attacker.heavyCombatMoves.First(m => m.skill == BattleSkill.None);
            float fixedStep = Time.fixedDeltaTime;
            var contactCallback = typeof(BattleImpactFeedback).GetMethod("Contact", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (string cleanup in new[] { "cancel", "reset", "disable", "fighter-disable" })
            {
                game.ResetCombatQueue();
                Time.timeScale = 1;
                attacker.transform.position = Vector3.zero;
                receiver.transform.position = Vector3.right * move.attackRange;
                if (!attacker.ExecuteAttack(move, receiver)) throw new Exception("Pause test attack rejected.");
                var pair = attacker.SourcePlayback;
                pair.AdvanceTo(pair.Duration);
                if (!feedback.IsHolding || !pair.IsContactHeld || Time.timeScale != 1)
                    throw new Exception("Contact must hold participants without owning global time.");
                float sample = pair.SampleTime;
                Vector3 hips = receiver.Animator.GetBoneTransform(HumanBodyBones.Hips).position;
                // A second request extends by max rather than summing two holds.
                var impact = new BattleVfxPlayer.Impact(pair, attacker, receiver, move,
                    BattleVfxPlayer.ContactKind.Heavy, hips, sample, false);
                float expectedHold = Mathf.Max(feedback.RemainingHold, feedback.heavyHold);
                contactCallback.Invoke(feedback, new object[] { impact });
                if (!Mathf.Approximately(feedback.RemainingHold, expectedHold))
                    throw new Exception("Overlapping holds accumulated instead of extending to max.");
                Time.timeScale = 0;
                feedback.AdvanceFeedback(.5f);
                tick.Invoke(pair, null);
                if (Time.timeScale != 0 || feedback.IsHolding || pair.SampleTime != sample ||
                    receiver.Animator.GetBoneTransform(HumanBodyBones.Hips).position != hips)
                    throw new Exception("A hold expiry resumed menu pause or advanced participant state.");
                if (cleanup == "cancel") pair.Cancel();
                else if (cleanup == "reset") game.ResetCombatQueue();
                else if (cleanup == "disable") feedback.enabled = false;
                else attacker.gameObject.SetActive(false);
                if (Time.timeScale != 0 || Time.fixedDeltaTime != fixedStep || feedback.IsHolding)
                    throw new Exception("Cleanup changed menu pause or physics timestep: " + cleanup);
                feedback.enabled = true;
                attacker.gameObject.SetActive(true);
                pair.Cancel();
                Time.timeScale = 1;
            }
            game.ResetCombatQueue();
            Time.timeScale = 1;
            feedback.BeginKnockoutSlowMotion();
            Time.timeScale = 0;
            feedback.AdvanceFeedback(feedback.knockoutSeconds + feedback.knockoutRecovery + 1);
            if (!feedback.IsSlowing || Time.timeScale != 0)
                throw new Exception("Menu pause consumed the KO slow-motion envelope.");
            feedback.ResetFeedback();
            if (Time.timeScale != 0 || Time.fixedDeltaTime != fixedStep)
                throw new Exception("KO cleanup resumed pause or altered the physics timestep.");
            Time.timeScale = 1;
            game.enabled = true;
            if (!game.BeginAnimationTestMode()) throw new Exception("Preview cleanup test could not begin.");
            Time.timeScale = 0;
            game.StopAnimationTest();
            game.EndAnimationTestMode();
            game.enabled = false;
            if (Time.timeScale != 0) throw new Exception("Stopping a preview resumed a paused game.");
            Time.timeScale = 1;
            report.AppendLine("PASS overlapping max holds; menu pause during hold; unchanged pose, sample and physics timestep; cancel/reset/disable/fighter-disable cleanup; paused KO envelope; paused preview stop/exit.");
        }

        static void Write(string suffix = "") => File.WriteAllText(FrankRetargetBuilder.FeelReview + "/PlayModeValidation.txt", report + suffix);

        static void Finish()
        {
            Write(); SessionState.SetInt(Key + "phase", 0);
            SessionState.SetBool(Key + "cleanup", true);
            if (game)
            {
                game.leftCombat.SequenceEnded -= SequenceEnded;
                game.rightCombat.SequenceEnded -= SequenceEnded;
                game.ResetCombatQueue();
            }
            EditorApplication.isPlaying = false;
        }

        static void ModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            if (!SessionState.GetBool(Key + "cleanup", false) && SessionState.GetInt(Key + "phase",0) == 0) return;
            SessionState.SetInt(Key + "phase",0);
            var isolated = JsonUtility.FromJson<IsolatedRoots>(SessionState.GetString(Key + "isolation", "{}"));
            if (isolated?.ids != null) foreach (string id in isolated.ids)
                if (GlobalObjectId.TryParse(id,out var global) && GlobalObjectId.GlobalObjectIdentifierToObjectSlow(global) is GameObject root)
                    root.SetActive(true);
            if (isolated?.cleanScenes != null) foreach (string path in isolated.cleanScenes)
            {
                var other = SceneManager.GetSceneByPath(path);
                if (other.IsValid() && other.isLoaded)
                    typeof(EditorSceneManager).GetMethod("ClearSceneDirtiness",BindingFlags.Static | BindingFlags.NonPublic)
                        ?.Invoke(null,new object[] { other });
            }
            SessionState.EraseString(Key + "isolation");
            var previous = SceneManager.GetSceneByPath(SessionState.GetString(Key + "active", ""));
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            var battle = SceneManager.GetSceneByPath("Assets/Scenes/BattleScene.unity");
            if (SessionState.GetBool(Key + "opened", false) && battle.IsValid() && battle.isLoaded)
                EditorSceneManager.CloseScene(battle, true);
            SessionState.SetBool(Key + "cleanup", false);
        }
    }
}
