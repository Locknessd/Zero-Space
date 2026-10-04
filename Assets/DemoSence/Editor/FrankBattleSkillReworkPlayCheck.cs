using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    [InitializeOnLoad]
    static class FrankBattleSkillReworkPlayCheck
    {
        const string Key = "Frank.BattleSkillReworkCheck";
        const string Review = "GeneratedAssets/BattleSkillReworkReview";
        const string Copy = "Assets/DemoSence/Editor/BattleSkillReworkPlayCheck.unity";
        static GameManager game;
        static BattleAnimationTestPanel panel;
        static BattleAnimationTestPanel.Entry[] entries;
        static readonly StringBuilder report = new StringBuilder();
        static readonly Dictionary<string, int> cues = new Dictionary<string, int>();
        static readonly HashSet<string> captures = new HashSet<string>();
        static int index = -1, lastFrame = -1, samples;
        static double began, caseBegan;
        static bool sawSlow, sawPending;
        static string FileLabel => entries[index].side + "_" + entries[index].move.skill;

        static FrankBattleSkillReworkPlayCheck()
        { EditorApplication.update += Tick; EditorApplication.playModeStateChanged += Restore; }

        public static void Start()
        {
            if (EditorApplication.isPlaying || File.Exists(Copy)) throw new Exception("Skill check needs Edit Mode and a clean temporary scene.");
            File.Copy("Assets/Scenes/BattleScene.unity", Copy); AssetDatabase.ImportAsset(Copy);
            var scene = EditorSceneManager.OpenScene(Copy, OpenSceneMode.Additive);
            try
            {
                foreach (var socket in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<WebSocketManager>(true))) socket.gameObject.SetActive(false);
                EditorSceneManager.SaveScene(scene);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
            SessionState.SetString(Key + ".previous", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(Key + ".restore", true); SessionState.SetBool(Key, true);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(Copy);
            Directory.CreateDirectory(Review); File.WriteAllText(Review + "/PlayValidation.txt", "RUNNING 8 native skill cases.\n");
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            try
            {
                if (began == 0) began = EditorApplication.timeSinceStartup;
                if (EditorApplication.timeSinceStartup - began > 180) throw new Exception("Native skill check timed out.");
                if (lastFrame == Time.frameCount || EditorApplication.timeSinceStartup - began < 1) return;
                lastFrame = Time.frameCount; Application.runInBackground = true;
                if (!game)
                {
                    game = GameManager.Instance; if (!game) return;
                    game.debugMode = false;
                    panel = game.gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BattleAnimationTestPanel>()).Single();
                    if (!panel.OpenButton) { game = null; return; }
                    panel.Open();
                    entries = panel.Entries.Where(e => e.move.skill != BattleSkill.None).ToArray();
                    if (entries.Length != 4) throw new Exception("Both skill pairs must be present for both fighters.");
                    game.battleVfx.EffectPlayed += Observe;
                    Next(); return;
                }
                if (game.QueueError != null) throw new Exception(game.QueueError);
                var feedback = game.GetComponent<BattleImpactFeedback>();
                var ko = game.uiManager.knockout;
                sawSlow |= feedback.IsSlowing; sawPending |= ko.Pending;
                if (ko.HasShown && (feedback.IsSlowing || feedback.IsHolding || game.leftCombat.IsBusy || game.rightCombat.IsBusy))
                    throw new Exception("KO panel appeared before the animation/slow-motion ended.");
                var entry = entries[index % entries.Length];
                var fighter = entry.side == PlayerUI.Side.Left ? game.leftCombat : game.rightCombat;
                var victim = fighter == game.leftCombat ? game.rightCombat : game.leftCombat;
                var playback = fighter.SourcePlayback;
                if (playback && playback.Playing && playback.Move == entry.move)
                {
                    var profile = game.battleVfx.timeline.FindMove(entry.move);
                    float hit = profile.cues.Single(c => c.group == "heavy_hit").seconds;
                    long full = Math.Max(10, game.uiManager.defaultInitialMaxHpAtomic);
                    long damage = game.AnimationTestLethal ? full : full / 10 * 3 + full % 10 * 3 / 10;
                    long expected = playback.SampleTime >= hit ? full - damage : full;
                    var slot = entry.side == PlayerUI.Side.Left ? game.uiManager.right : game.uiManager.left;
                    if (long.Parse(slot.healthText.text.Split('/')[0]) != expected) throw new Exception("Damage does not match the single contact.");
                    samples++;
                    foreach (string moment in new[] { "Charge", "Flight", "Impact" })
                    {
                        float time = moment == "Charge" ? (entry.move.skill == BattleSkill.Archer ? .72f : .70f) :
                            moment == "Flight" ? profile.cues.Single(c => c.group == "skill_shot").seconds + .09f : hit + .075f;
                        if (index < 4 && playback.SampleTime >= time && captures.Add(FileLabel + moment))
                            ScreenCapture.CaptureScreenshot(Review + "/" + FileLabel + "_" + moment + ".png");
                    }
                }
                if (game.IsAnimationTestPlaying || (game.AnimationTestLethal && !ko.HasShown) || EditorApplication.timeSinceStartup - caseBegan < .25) return;
                foreach (string cue in new[] { "skill_cast", "skill_shot", "heavy_hit", "ground_impact", "landing_text" })
                    if (!cues.TryGetValue(cue, out int count) || count != 1) throw new Exception("Expected exactly one " + cue + "; received " + count);
                if (game.AnimationTestLethal && (!victim.IsDead || !sawSlow || !sawPending)) throw new Exception("KO/slow-motion/death pose not exercised.");
                report.AppendLine("PASS " + entry.side + " " + entry.move.attackAnim.name + " KO=" + game.AnimationTestLethal + ": one cast/shot/contact/landing; HP aligned; recovery and clock clean.");
                File.WriteAllText(Review + "/PlayValidation.txt", report.ToString() + "RUNNING\n");
                if (index == 7) Finish(true, "ALL PASS: 8 native cases, " + samples + " sampled damage frames, both fighters, normal and KO.");
                else Next();
            }
            catch (Exception error) { Finish(false, error.ToString()); }
        }

        static void Observe(string cue, GameObject root)
        {
            cues[cue] = cues.TryGetValue(cue, out int count) ? count + 1 : 1;
            foreach (var p in root.GetComponentsInChildren<ParticleSystem>(true))
                if (p.main.loop) throw new Exception("Looping skill effect " + root.name);
            foreach (var r in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
                if (!r.sharedMaterial || !r.sharedMaterial.shader || !r.sharedMaterial.shader.isSupported)
                    throw new Exception("Missing/unsupported material " + root.name);
        }

        static void Next()
        {
            game.StopAnimationTest();
            if (game.battleVfx.ActiveEffectCount != 0 || Time.timeScale != 1) throw new Exception("Reset did not clear effects/restore clock.");
            index++; cues.Clear(); sawSlow = sawPending = false;
            panel.KoToggle.isOn = index >= 4;
            if (!panel.PlayEntry(entries[index % entries.Length])) throw new Exception("Skill row could not start.");
            caseBegan = EditorApplication.timeSinceStartup;
        }

        static void Finish(bool passed, string detail)
        {
            if (game) { game.battleVfx.EffectPlayed -= Observe; game.EndAnimationTestMode(); }
            Time.timeScale = 1; report.AppendLine((passed ? "" : "FAIL ") + detail);
            File.WriteAllText(Review + "/PlayValidation.txt", report.ToString()); SessionState.SetBool(Key, false); EditorApplication.isPlaying = false;
        }

        static void Restore(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key + ".restore", false)) return;
            SessionState.SetBool(Key, false); SessionState.SetBool(Key + ".restore", false);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key + ".previous", ""));
            AssetDatabase.DeleteAsset(Copy); Time.timeScale = 1;
            game = null; panel = null; entries = null; index = lastFrame = -1; samples = 0; began = caseBegan = 0; report.Clear(); captures.Clear(); cues.Clear();
        }
    }

    public static partial class FrankRetargetBuilder
    {
        [MenuItem("Tools/Battle/Check replacement skills in Play Mode")]
        public static void BattleReplacementSkillsPlayCheck() => FrankBattleSkillReworkPlayCheck.Start();
    }
}
