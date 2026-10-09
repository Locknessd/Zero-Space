using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
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
        static int throwIndex, throwCompleted;
        static CombatTripletData[] throwMoves;
        static readonly StringBuilder report = new StringBuilder();
        static WebSocketManager replaySocket;
        static string ReviewDirectory => SessionState.GetBool(Key + "lightThrows", false) ? "GeneratedAssets/LightThrowReview" :
            SessionState.GetBool(Key + "networkReplay", false) ? "GeneratedAssets/BattleLoadingReview" : FrankRetargetBuilder.FeelReview;
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
            Directory.CreateDirectory(ReviewDirectory);
            File.WriteAllText(ReviewDirectory + "/PlayModeValidation.txt", "RUNNING native contact and queue validation\n");
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

        public static void BeginBattleReplay()
        {
            SessionState.SetBool(Key + "networkReplay", true);
            Begin();
        }

        public static void BeginLightThrows()
        {
            SessionState.SetBool(Key + "lightThrows", true);
            Begin();
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
                    if (SessionState.GetBool(Key + "lightThrows", false))
                    {
                        throwMoves = new[] { game.leftCombat, game.rightCombat }.SelectMany(f => f.lightCombatMoves.Where(m =>
                            m.moveName == "Light_JPBOM" || m.moveName == "Light_SIHO" || m.moveName == "Light_GSWING")).ToArray();
                        if (throwMoves.Length != 6) throw new Exception("Missing new Light throw in live scene.");
                        throwIndex = 0; throwCompleted = game.CompletedAnimationTests;
                        SessionState.SetInt(Key + "phase", 3);
                        StartNextThrow();
                    }
                    else StartRealQueue();
                }
                else if (phase == 3)
                {
                    if (!string.IsNullOrEmpty(game.QueueError)) throw new Exception("Throw error: " + game.QueueError);
                    if (!game.IsAnimationTestPlaying)
                    {
                        if (game.CompletedAnimationTests != throwCompleted + throwIndex + 1 ||
                            game.leftCombat.IsBusy || game.rightCombat.IsBusy || Time.timeScale != 1 ||
                            game.leftCombat.IsDead || game.rightCombat.IsDead)
                            throw new Exception("Throw failed to complete its real GetUp recovery.");
                        report.AppendLine("PASS real " + (throwIndex < 3 ? "Mankey/" : "Meme/") + throwMoves[throwIndex].moveName +
                            ": attack, hit-stop, receiving animation, GetUp and both callbacks completed.");
                        throwIndex++;
                        if (throwIndex < throwMoves.Length) StartNextThrow();
                        else { game.EndAnimationTestMode(); StartRealQueue(); }
                    }
                    else if (EditorApplication.timeSinceStartup - queueStarted > 20)
                        throw new Exception("Real throw did not recover within 20 seconds: " + throwMoves[throwIndex].moveName);
                }
                else
                {
                    if (!string.IsNullOrEmpty(game.QueueError)) throw new Exception("Queue error: " + game.QueueError);
                    if (!game.IsEventQueueBusy && ended >= 4)
                    {
                        if (game.leftCombat.IsBusy || game.rightCombat.IsBusy || Time.timeScale <= 0)
                            throw new Exception("Queue completed with held/busy fighters.");
                        report.AppendLine("PASS real Left Light + Right Heavy FIFO playback, four completion callbacks, GetUp recovery and no stuck time scale.");
                        if (SessionState.GetBool(Key + "networkReplay", false))
                        {
                            bool finished = (bool)typeof(GameManager).GetField("_matchEnded", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game);
                            if (!finished || replaySocket.PendingBattleMessageCount != 0)
                                throw new Exception("Battle finished before replay/result queue was drained.");
                            report.AppendLine("PASS FINISHED snapshot history received before readiness, duplicate/live delivery, and result after both real combat pairs.");
                        }
                        Finish();
                    }
                    else if (EditorApplication.timeSinceStartup - queueStarted > 45)
                        throw new Exception("Real queue did not finish within 45 seconds; callbacks=" + ended);
                }
            }
            catch (Exception error) { report.AppendLine("FAIL " + error); Finish(); Debug.LogException(error); }
        }

        static void StartNextThrow()
        {
            queueStarted = EditorApplication.timeSinceStartup;
            if (!game.PlayAnimationTest(throwIndex < 3 ? PlayerUI.Side.Left : PlayerUI.Side.Right, throwMoves[throwIndex]))
                throw new Exception("Live GameManager rejected " + throwMoves[throwIndex].moveName);
            Write("RUNNING real new Light throws and GetUp recovery\n");
        }

        static void StartRealQueue()
        {
            ended = 0;
            if (SessionState.GetBool(Key + "networkReplay", false)) QueueNetworkReplay();
            else
            {
                game.EnqueueLocalAttack(PlayerUI.Side.Left, false);
                game.EnqueueLocalAttack(PlayerUI.Side.Right, true);
            }
            queueStarted = EditorApplication.timeSinceStartup;
            SessionState.SetInt(Key + "phase", 2);
            Write("RUNNING real Light + Heavy queue callbacks\n");
        }

        static void SequenceEnded(CharacterCombat fighter, int playbackId, bool succeeded)
        {
            if (!succeeded) throw new Exception("Real queue sequence failed: " + fighter.name);
            ended++;
        }

        static void QueueNetworkReplay()
        {
            var root = new GameObject("Battle Replay Test Socket");
            replaySocket = root.AddComponent<WebSocketManager>();
            replaySocket.enabled = false; // Local transport fixture; do not contact the backend.
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(GameManager).GetField("_socket", flags).SetValue(game, replaySocket);
            typeof(GameManager).GetField("<IsReadyForBattleEvents>k__BackingField", flags).SetValue(game, false);
            var history = new JArray
            {
                ReplayEvent(1, null, "MATCH_CREATED", new JObject { ["characterIds"] = new JArray("bot_a", "bot_b"), ["initialHpAtomic"] = 1000 }),
                ReplayEvent(2, "left", "ARGUMENT_SELECTED", new JObject { ["actorCharacterId"] = "bot_a", ["targetCharacterId"] = "bot_b", ["animationId"] = "attack_light" }),
                ReplayEvent(3, "left", "DAMAGE_APPLIED", new JObject { ["actorCharacterId"] = "bot_a", ["targetCharacterId"] = "bot_b", ["hpAfterAtomic"] = 900, ["damageAtomic"] = 100 }),
                ReplayEvent(4, "right", "ARGUMENT_SELECTED", new JObject { ["actorCharacterId"] = "bot_b", ["targetCharacterId"] = "bot_a", ["animationId"] = "attack_heavy" }),
                ReplayEvent(5, "right", "DAMAGE_APPLIED", new JObject { ["actorCharacterId"] = "bot_b", ["targetCharacterId"] = "bot_a", ["hpAfterAtomic"] = 800, ["damageAtomic"] = 200 }),
                ReplayEvent(6, null, "WINNER_DECLARED", new JObject())
            };
            var retain = typeof(WebSocketManager).GetMethod("RetainBattleMessage", flags);
            string snapshot = new JObject { ["type"] = "meme_battle_snapshot", ["snapshot"] = new JObject {
                ["matchId"] = "native-loading-replay", ["state"] = "FINISHED", ["latestSequence"] = 6,
                ["characterHpAtomic"] = new JObject { ["bot_a"] = 800, ["bot_b"] = 900 },
                ["characterMaxHpAtomic"] = new JObject { ["bot_a"] = 1000, ["bot_b"] = 1000 } }, ["events"] = history }.ToString();
            retain.Invoke(replaySocket, new object[] { snapshot });
            typeof(GameManager).GetMethod("ReceivePendingBattleMessages", flags).Invoke(game, null);
            if (replaySocket.PendingBattleMessageCount != 7 || game.PendingEventCount != 0)
                throw new Exception("Game consumed events before readiness.");
            // Deliver a duplicate through the same live path while the complete match is waiting.
            retain.Invoke(replaySocket, new object[] { new JObject { ["type"] = "meme_battle_event", ["event"] = history[2].DeepClone() }.ToString() });
            if (replaySocket.PendingBattleMessageCount != 7) throw new Exception("Duplicate battle event was retained.");
            typeof(GameManager).GetField("<IsReadyForBattleEvents>k__BackingField", flags).SetValue(game, true);
            report.AppendLine("OK complete native match buffered before GameManager readiness; no premature delivery or duplicate.");
        }

        static JObject ReplayEvent(long sequence, string turn, string type, JObject payload) => new JObject {
            ["matchId"] = "native-loading-replay", ["sequence"] = sequence, ["eventId"] = sequence.ToString(),
            ["eventType"] = type, ["turnId"] = turn, ["payload"] = payload };

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
                foreach (var move in attacker.lightCombatMoves.Concat(attacker.heavyCombatMoves).Where(m => m.skill == BattleSkill.None))
                foreach (bool lethal in new[] { false, true })
                {
                    game.ResetCombatQueue();
                    Time.timeScale = 1;
                    var receiver = fighters.Single(f => f != attacker);
                    attacker.transform.position = Vector3.zero;
                    receiver.transform.position = (attacker == game.leftCombat ? Vector3.right : Vector3.left) * move.attackRange;
                    int emitted = 0, slowBefore = feedback.SlowMotionCount;
                    GameObject contactEffect = null;
                    Action<BattleVfxPlayer.Impact> observe = impact => emitted++;
                    Action<string, GameObject> observeFlash = (id, root) =>
                    {
                        if (id != "light_hit" && id != "heavy_hit") return;
                        contactEffect = root;
                        foreach (var p in root.GetComponentsInChildren<ParticleSystem>().Where(p =>
                            p.name == "Contact core" || p.name == "Contact shockwave" || p.name == "Contact echo"))
                            if (p.particleCount != 1) throw new Exception("Contact entered hit-stop with an invisible flash/ring: " + p.name);
                    };
                    vfx.ContactOccurred += observe;
                    vfx.EffectPlayed += observeFlash;
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
                            if (contactEffect && contact.Any(c => c.group == "light_hit" || c.group == "heavy_hit" || c.group == "stab_hit"))
                                foreach (var p in contactEffect.GetComponentsInChildren<ParticleSystem>().Where(p =>
                                    p.name == "Contact shockwave" || p.name == "Contact echo"))
                                    if (p.particleCount != 1 || p.main.useUnscaledTime)
                                        throw new Exception("Contact ring disappeared or ignored the held contact frame.");
                            feedback.AdvanceFeedback(.5f);
                            tick.Invoke(pair, null); // Exercise the carried simulation time, not a second seek.
                        }
                        if (emitted != times.Sum(g => g.Count())) throw new Exception("A deferred contact was dropped.");
                        if (lethal && feedback.SlowMotionCount - slowBefore != 1) throw new Exception("KO slow motion was missing or replayed.");
                        pair.Cancel();
                        if (feedback.IsHolding || Time.timeScale != 1) throw new Exception("Cancel did not restore time scale.");
                    }
                    finally { vfx.ContactOccurred -= observe; vfx.EffectPlayed -= observeFlash; }
                }
                report.AppendLine("PASS every Light and Heavy move for both attackers, lethal/nonlethal: a frame crossing a whole combo holds each exact participant pose without changing global time; carried time resumes every hit once; KO slow motion once; cancel clears the local clock.");
                TestPauseLifecycle(feedback, tick);
                report.AppendLine("PASS contact core and both shockwave rings already have particles before hit-stop; rings use game time and remain visible on held contact poses.");
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

        static void Write(string suffix = "") => File.WriteAllText(ReviewDirectory + "/PlayModeValidation.txt", report + suffix);

        static void Finish()
        {
            Write(); SessionState.SetInt(Key + "phase", 0);
            SessionState.SetBool(Key + "networkReplay", false);
            SessionState.SetBool(Key + "lightThrows", false);
            SessionState.SetBool(Key + "cleanup", true);
            if (game)
            {
                game.leftCombat.SequenceEnded -= SequenceEnded;
                game.rightCombat.SequenceEnded -= SequenceEnded;
                game.EndAnimationTestMode();
                game.ResetCombatQueue();
            }
            if (replaySocket) UnityEngine.Object.Destroy(replaySocket.gameObject);
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
