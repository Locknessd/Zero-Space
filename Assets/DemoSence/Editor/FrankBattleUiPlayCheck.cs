using System;
using System.IO;
using System.Linq;
using System.Text;
using DG.Tweening;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    [InitializeOnLoad]
    public static class FrankBattleUiPlayCheck
    {
        const string Key = "BattleUiPlayCheck";
        const string Copy = "Assets/DemoSence/Editor/BattleUiPlayCheck.unity";
        const string ReportPath = "Temp/FrankRetarget/BattleUi/play.txt";
        const string Dialogue = "Đòn này vui ghê!";
        static readonly StringBuilder report = new StringBuilder();
        static GameManager game;
        static MemeBattleUI ui;
        static int step, frame = -1, pendingFrames;
        static double began, phaseBegan;
        static Vector3 heldHipsPosition;
        static Quaternion heldHipsRotation;

        static FrankBattleUiPlayCheck()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += Restore;
        }

        public static void Start()
        {
            if (EditorApplication.isPlaying) throw new Exception("Run the UI check in Edit Mode.");
            if (File.Exists(Copy)) throw new Exception("Temporary UI test scene already exists.");
            File.Copy("Assets/Scenes/BattleScene.unity", Copy);
            AssetDatabase.ImportAsset(Copy);
            var scene = EditorSceneManager.OpenScene(Copy, OpenSceneMode.Additive);
            try
            {
                foreach (var socket in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<WebSocketManager>(true)))
                    socket.gameObject.SetActive(false);
                EditorSceneManager.SaveScene(scene);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
            SessionState.SetString(Key + ".previous", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(Copy);
            SessionState.SetBool(Key + ".restore", true);
            SessionState.SetBool(Key, true);
            Directory.CreateDirectory("Temp/FrankRetarget/BattleUi");
            File.WriteAllText(ReportPath, "RUNNING: isolated Battle copy, text tweens and duplicate lethal events on both sides.\n");
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            try
            {
                if (began == 0) began = EditorApplication.timeSinceStartup;
                if (EditorApplication.timeSinceStartup - began > 90) throw new Exception("UI play check timed out at phase " + step);
                if (frame == Time.frameCount || EditorApplication.timeSinceStartup - began < .5) return;
                frame = Time.frameCount;
                Application.runInBackground = true;
                if (!game)
                {
                    game = GameManager.Instance;
                    if (!game) return;
                    game.enableLocalInputTesting = game.debugMode = false;
                    ui = game.uiManager;
                    if (!ui || !ui.textEffects || !ui.knockout) throw new Exception("Missing saved text/KO components.");
                    NewMatch("ui-first");
                    phaseBegan = EditorApplication.timeSinceStartup;
                }
                if (game.QueueError != null) throw new Exception(game.QueueError);
                double elapsed = EditorApplication.timeSinceStartup - phaseBegan;
                switch (step)
                {
                    case 0:
                        if (game.IsEventQueueBusy || elapsed < .45) return;
                        ui.SetDialogue(MemeBattleUI.Side.Left, Dialogue);
                        ui.SetDialogue(MemeBattleUI.Side.Right, "Thử xem ai thắng nhé!");
                        ui.UpdateHealth(MemeBattleUI.Side.Right, 750, 1000);
                        ui.ShowDamage(MemeBattleUI.Side.Left, 123);
                        if (ui.left.damageText)
                        {
                            ui.ShowDamage(MemeBattleUI.Side.Left, 87);
                            ui.SetDialogue(MemeBattleUI.Side.Left, Dialogue);
                            ui.ShowDamage(MemeBattleUI.Side.Right, 64);
                            ui.ClearDialogue(MemeBattleUI.Side.Right);
                            ui.SetDialogue(MemeBattleUI.Side.Right, "Thử xem ai thắng nhé!");
                            if (ui.left.damageText == ui.left.speechBubbleText || ui.left.damageText.text != "-87" ||
                                ui.right.damageText.text != "-64" || ui.left.speechBubble.Content != Dialogue)
                                throw new Exception("Chat updates interrupted dedicated damage numbers.");
                        }
                        game.roundManager.StartTurnTimer(3, DateTime.UtcNow.ToString("O"), DateTime.UtcNow.AddSeconds(8).ToString("O"));
                        game.roundManager.ShowMultiplier(1.5f);
                        if (ui.right.healthText.text != "750/1000" || Mathf.Abs(ui.right.hpSlider.value - .75f) > .001f)
                            throw new Exception("Text animation delayed the authoritative HP value.");
                        Next();
                        break;
                    case 1:
                        if (elapsed < 2) return;
                        if (ui.left.speechBubbleText.text != Dialogue) throw new Exception("Damage popup did not restore Vietnamese dialogue.");
                        if (ui.left.damageText && (!string.IsNullOrEmpty(ui.left.damageText.text) ||
                            !string.IsNullOrEmpty(ui.right.damageText.text) || !ui.left.speechBubble.IsVisible))
                            throw new Exception("Repeated damage did not clear independently of the visible chat card.");
                        if (ui.knockout.HasShown || ui.knockout.group.alpha != 0) throw new Exception("KO appeared with positive HP.");
                        if (ui.textEffects.StartedAnimations < 6 || game.roundManager.roundNumberText.text != "Turn 3" || game.roundManager.multiplierText.gameObject.activeInHierarchy)
                            throw new Exception("Turn/text animation did not run or the hidden multiplier became visible.");
                        report.AppendLine("PASS live HUD text: repeated damage clears independently; changing/clearing chat does not erase damage; Vietnamese dialogue stays visible; HP stays immediate; positive HP does not show KO.");
                        Lethal("ui-first", false);
                        Next();
                        break;
                    case 2:
                        if (game.rightCombat.IsBusy)
                        {
                            if (ui.knockout.HasShown || ui.knockout.group.alpha != 0) throw new Exception("KO appeared before the lethal sequence ended.");
                            if (ui.knockout.Pending) pendingFrames++;
                            return;
                        }
                        if (!ui.knockout.HasShown) return;
                        if (!game.rightCombat.IsDead || ui.knockout.ShowCount != 1 || pendingFrames < 2)
                            throw new Exception("Right-side KO did not follow the complete lethal sequence.");
                        CaptureDeathPose(game.rightCombat);
                        game.ApplyRawMessage(Message("ui-first", "late-hp", "HP_CHANGED",
                            new JObject { ["characterId"] = "bot_b", ["hpAfterAtomic"] = 0 }, "lethal"));
                        string winner = Message("ui-first", "winner", "WINNER_DECLARED", new JObject { ["winnerCharacterId"] = "bot_a" });
                        game.ApplyRawMessage(winner); game.ApplyRawMessage(winner);
                        report.AppendLine("PASS right KO after zero HP and death animation; duplicate DAMAGE_APPLIED suppressed.");
                        Next();
                        break;
                    case 3:
                        if (elapsed < .7 || game.IsEventQueueBusy) return;
                        ValidateWinnerFacing(game.leftCombat, game.rightCombat);
                        if (ui.knockout.ShowCount != 1 || ui.knockout.group.alpha < .9f) throw new Exception("Late HP/winner events restarted or hid KO.");
                        if (ui.knockout.group.blocksRaycasts || ui.knockout.group.interactable) throw new Exception("KO intercepts UI input.");
                        Time.timeScale = 0;
                        if (ui.right.damageText) ui.ShowDamage(MemeBattleUI.Side.Right, 456);
                        Next();
                        break;
                    case 4:
                        if (elapsed < 3) return;
                        ValidateWinnerFacing(game.leftCombat, game.rightCombat);
                        if (ui.knockout.IsShowing || ui.knockout.group.alpha > .001f) throw new Exception("KO did not finish at timeScale zero.");
                        if (ui.right.damageText && !string.IsNullOrEmpty(ui.right.damageText.text))
                            throw new Exception("Damage popup did not clear at timeScale zero.");
                        Time.timeScale = 1;
                        game.ResetCombatQueue();
                        if (DOTween.TotalTweensById(ui.knockout) != 0 || ui.knockout.Pending || ui.knockout.HasShown)
                            throw new Exception("KO tween/state survived reset.");
                        report.AppendLine("PASS duplicate HP/winner did not replay KO; input passed through; unscaled exit and queue reset cleared KO tweens.");
                        NewMatch("ui-second");
                        Next();
                        break;
                    case 5:
                        if (game.IsEventQueueBusy || elapsed < .5) return;
                        if (ui.left.healthText.text != "1000/1000" || ui.knockout.HasShown) throw new Exception("New match did not restore the HUD.");
                        Lethal("ui-second", true);
                        Next();
                        break;
                    case 6:
                        if (game.leftCombat.IsBusy)
                        {
                            if (ui.knockout.HasShown) throw new Exception("Left KO appeared before its death animation ended.");
                            return;
                        }
                        if (!ui.knockout.HasShown) return;
                        if (!game.leftCombat.IsDead || ui.knockout.ShowCount != 1 || !ui.knockout.defeatedLabel.text.StartsWith("MANKEY"))
                            throw new Exception("New-match left KO had the wrong fighter or replay count.");
                        CaptureDeathPose(game.leftCombat);
                        string secondWinner = Message("ui-second", "winner", "WINNER_DECLARED", new JObject { ["winnerCharacterId"] = "bot_b" });
                        game.ApplyRawMessage(secondWinner); game.ApplyRawMessage(secondWinner);
                        Next();
                        break;
                    case 7:
                        if (elapsed < .7 || game.IsEventQueueBusy) return;
                        ValidateWinnerFacing(game.rightCombat, game.leftCombat);
                        game.ResetCombatQueue();
                        if (ui.knockout.group.alpha != 0 || DOTween.TotalTweensById(ui.knockout) != 0)
                            throw new Exception("Reset during the KO entrance left a visible overlay/tween.");
                        if (ui.knockout.HasShown || ui.knockout.IsShowing || ui.knockout.Pending) throw new Exception("Stale KO returned after reset.");
                        report.AppendLine("PASS second match resets KO latch; left-side death shows correct fighter; reset during KO entrance clears presentation.");
                        report.AppendLine("PASS both winners face the visible fallen body after KO and duplicate winner events; the held death pose stays unchanged, including at timeScale zero.");
                        NewMatch("ui-winner-only");
                        Next();
                        break;
                    case 8:
                        if (game.IsEventQueueBusy || elapsed < .5) return;
                        string result = Message("ui-winner-only", "winner", "WINNER_DECLARED", new JObject { ["winnerCharacterId"] = "bot_b" });
                        game.ApplyRawMessage(result); game.ApplyRawMessage(result);
                        Next();
                        break;
                    case 9:
                        if (!ui.knockout.HasShown || elapsed < .5) return;
                        if (!game.leftCombat.IsDead || ui.left.healthText.text != "0/1000" || ui.knockout.ShowCount != 1)
                            throw new Exception("Winner-only death did not synchronize HP and KO.");
                        ValidateWinnerFacing(game.rightCombat, game.leftCombat, false);
                        game.ResetCombatQueue();
                        report.AppendLine("PASS winner-only event marks the loser dead, sets HP to zero and shows KO once even without a damage/HP event.");
                        NewMatch("ui-chat");
                        Next();
                        break;
                    case 10:
                        if (game.IsEventQueueBusy || elapsed < .5) return;
                        game.ApplyRawMessage(Message("ui-chat", "argument-a", "ARGUMENT_SELECTED",
                            new JObject { ["actorCharacterId"] = "bot_a", ["memeText"] = "Không dễ nuốt đâu!" }, "chat-a"));
                        game.ApplyRawMessage(Message("ui-chat", "resolved-a", "TURN_RESOLVED", new JObject(), "chat-a"));
                        Next();
                        break;
                    case 11:
                        if (game.IsEventQueueBusy || elapsed < .7) return;
                        if (ui.right.speechBubble && (ui.right.speechBubble.Content != "Không dễ nuốt đâu!" ||
                            ui.right.speechBubbleText.text != "Không dễ nuốt đâu!" || !ui.right.speechBubble.IsVisible))
                            throw new Exception("ARGUMENT_SELECTED did not reach the styled dialogue card.");
                        if (ui.left.speechBubble && (ui.left.memeResultObject || ui.right.memeResultObject ||
                            ui.left.memeResultText || ui.right.memeResultText ||
                            game.gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                                .Any(t => t.name == "SpeechBubble_TrumpVictim" || t.name == "ScoreCyan" || t.name == "ScoreRed")))
                            throw new Exception("A received argument can still activate an obsolete speech bubble.");
                        game.ApplyRawMessage(Message("ui-chat", "argument-b", "ARGUMENT_SELECTED",
                            new JObject { ["actorCharacterId"] = "bot_b", ["memeText"] = Dialogue }, "chat-b"));
                        game.ApplyRawMessage(Message("ui-chat", "resolved-b", "TURN_RESOLVED", new JObject(), "chat-b"));
                        Next();
                        break;
                    case 12:
                        if (game.IsEventQueueBusy || elapsed < .7) return;
                        if (ui.left.speechBubble && (ui.left.speechBubble.Content != Dialogue ||
                            ui.left.speechBubbleText.text != Dialogue || !ui.left.speechBubble.IsVisible))
                            throw new Exception("The other actor's argument did not reach its styled card.");
                        report.AppendLine("PASS received ARGUMENT_SELECTED for both actors: only styled HUD cards display; old world bubbles and result bindings are absent.");
                        NewMatch("ui-damage-cancel");
                        Next();
                        break;
                    case 13:
                        if (game.IsEventQueueBusy || elapsed < .5) return;
                        // Damage owns the sampled source clock even with audio/VFX disabled.
                        game.battleSfx.enabled = game.battleVfx.enabled = false;
                        game.leftCombat.heavyCombatMoves = new[] {game.leftCombat.heavyCombatMoves.Single(m => m.moveName == "Heavy_Katana")};
                        game.ApplyRawMessage(Message("ui-damage-cancel", "damage", "DAMAGE_APPLIED", new JObject
                        { ["actorCharacterId"] = "bot_a", ["targetCharacterId"] = "bot_b", ["hpAfterAtomic"] = 899,
                            ["damageAtomic"] = 101, ["animationId"] = "attack_heavy" }, "cancel"));
                        Next();
                        break;
                    case 14:
                        var hp = long.Parse(ui.right.healthText.text.Split('/')[0]);
                        var source = game.leftCombat.SourcePlayback;
                        if (hp == 1000) return;
                        if (hp != 967 || !source || !source.Playing || source.SampleTime < 1.57f || ui.right.damageText.text != "-33")
                            throw new Exception("Muted source playback did not apply exactly the first 33 damage at contact.");
                        game.ResetCombatQueue();
                        game.battleSfx.enabled = game.battleVfx.enabled = true;
                        NewMatch("ui-after-damage-cancel");
                        Next();
                        break;
                    case 15:
                        if (game.IsEventQueueBusy || elapsed < 1.5) return;
                        if (ui.right.healthText.text != "1000/1000" || ui.left.healthText.text != "1000/1000" ||
                            !string.IsNullOrEmpty(ui.right.damageText.text) || ui.knockout.HasShown)
                            throw new Exception("Interrupted damage leaked into the next match.");
                        report.AppendLine("PASS damage follows source contact with audio/VFX disabled; reset after first hit clears remaining damage and the next match stays at full HP.");
                        Finish(true, $"PASS all UI/KO play checks; {ui.textEffects.StartedAnimations} text animations; {pendingFrames} frames with KO deferred during lethal playback; both fighters, duplicate events, new match, interrupted overlay and winner-only death tested.");
                        break;
                }
            }
            catch (Exception exception) { Finish(false, exception.ToString()); }
        }

        static void Next()
        {
            step++;
            phaseBegan = EditorApplication.timeSinceStartup;
            File.WriteAllText(ReportPath, report.ToString());
        }

        static void CaptureDeathPose(CharacterCombat defeated)
        {
            var hips = defeated.Animator.GetBoneTransform(HumanBodyBones.Hips);
            heldHipsPosition = hips.position;
            heldHipsRotation = hips.rotation;
        }

        static void ValidateWinnerFacing(CharacterCombat winner, CharacterCombat defeated, bool checkHeldPose = true)
        {
            var hips = defeated.Animator.GetBoneTransform(HumanBodyBones.Hips);
            Vector3 direction = hips.position - winner.Animator.GetBoneTransform(HumanBodyBones.Hips).position;
            direction.y = 0;
            if (!defeated.IsDead || winner.IsDead || Vector3.Dot(winner.Animator.transform.forward, direction.normalized) < .98f)
                throw new Exception("Winner faces away from the visible defeated body after KO: " + winner.name);
            if (checkHeldPose && (Vector3.Distance(heldHipsPosition, hips.position) > .0001f ||
                Quaternion.Angle(heldHipsRotation, hips.rotation) > .001f))
                throw new Exception("Winner facing changed the held death pose.");
        }

        static void NewMatch(string match) => game.ApplyRawMessage(Message(match, "created", "MATCH_CREATED",
            new JObject { ["characterIds"] = new JArray("Mankey", "Pepe"), ["initialHpAtomic"] = 1000 }));

        static void Lethal(string match, bool leftDies)
        {
            var attacker = leftDies ? game.rightCombat : game.leftCombat;
            attacker.lightCombatMoves = new[] { attacker.lightCombatMoves[0] };
            string json = Message(match, "damage", "DAMAGE_APPLIED", new JObject
            {
                ["actorCharacterId"] = leftDies ? "bot_b" : "bot_a",
                ["targetCharacterId"] = leftDies ? "bot_a" : "bot_b",
                ["hpAfterAtomic"] = 0, ["damageAtomic"] = 1000, ["animationId"] = "attack_light"
            }, "lethal");
            game.ApplyRawMessage(json); game.ApplyRawMessage(json);
        }

        static string Message(string match, string id, string type, JObject payload, string turn = null) =>
            new JObject { ["type"] = "meme_battle_event", ["event"] = new JObject
            { ["matchId"] = match, ["eventId"] = id, ["eventType"] = type, ["turnId"] = turn, ["payload"] = payload } }.ToString();

        static void Finish(bool passed, string detail)
        {
            Time.timeScale = 1;
            report.AppendLine((passed ? "" : "FAIL ") + detail);
            File.WriteAllText(ReportPath, report.ToString());
            SessionState.SetBool(Key, false);
            EditorApplication.isPlaying = false;
        }

        static void Restore(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key + ".restore", false)) return;
            SessionState.SetBool(Key, false);
            SessionState.SetBool(Key + ".restore", false);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key + ".previous", ""));
            AssetDatabase.DeleteAsset(Copy);
            Time.timeScale = 1;
            game = null; ui = null;
            frame = -1; step = pendingFrames = 0;
            began = phaseBegan = 0;
            report.Clear();
        }
    }

    public static partial class FrankRetargetBuilder
    {
        [MenuItem("Tools/Battle/Check animated text and KO in Play Mode")]
        public static void BattleUiPlayCheck() => FrankBattleUiPlayCheck.Start();
    }
}
