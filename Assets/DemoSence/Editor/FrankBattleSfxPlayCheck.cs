using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    [InitializeOnLoad]
    public static class FrankBattleSfxPlayCheck
    {
        const string Key = "BattleSfxPlayCheck";
        const string Copy = "Assets/Audio/Battle/Editor/BattleSfxPlayCheck.unity";
        const string ReportPath = "Temp/FrankRetarget/battle-sfx-play.txt";
        static readonly StringBuilder report = new StringBuilder();
        static readonly List<string> heard = new List<string>();
        static readonly List<string> effects = new List<string>();
        static readonly float[] samples = new float[256];
        static GameManager game;
        static FrankCinematicCamera cameraDirector;
        static Camera cinematicCamera;
        static int cameraFrames;
        static readonly HashSet<string> cameraShots = new HashSet<string>();
        static CharacterCombat[] fighters;
        static CombatTripletData[][] moves;
        static int step, frame = -1, outputFrames, sourceStarts, knockoutSounds;
        static float mixedPeak;
        static int effectStarts;
        static int lightingStarts;
        static int heldFrames;
        static FrankBattlePairPlayback heldPlayback;
        static float heldSample;
        static bool wasHolding;
        static bool observedKoSlow;
        static readonly HashSet<string> comicCaptures = new HashSet<string>();
        static bool started, waitingForResult, damageMode;
        static string audioFailure;
        static string vfxFailure;
        static double began, stepBegan;

        static FrankBattleSfxPlayCheck()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += Restore;
        }

        public static void Start(bool criticalOnly = false, bool checkDamage = false)
        {
            if (EditorApplication.isPlaying) throw new Exception("Start the SFX check in Edit Mode");
            if (File.Exists(Copy)) throw new Exception("Temporary SFX test scene already exists: " + Copy);
            Directory.CreateDirectory("Assets/Audio/Battle/Editor");
            File.Copy("Assets/Scenes/BattleScene.unity", Copy);
            AssetDatabase.ImportAsset(Copy);
            var scene = EditorSceneManager.OpenScene(Copy, OpenSceneMode.Additive);
            try
            {
                // The test exercises received messages locally and must not connect to a real match.
                foreach (var socket in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<WebSocketManager>(true)))
                    socket.gameObject.SetActive(false);
                EditorSceneManager.SaveScene(scene);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
            AssetDatabase.ImportAsset(Copy);
            SessionState.SetString(Key + ".previous", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(Copy);
            SessionState.SetBool(Key + ".restore", true);
            SessionState.SetBool(Key + ".criticalOnly", criticalOnly);
            SessionState.SetBool(Key + ".damage", checkDamage);
            if (checkDamage) Directory.CreateDirectory("GeneratedAssets/BattleDamageReview");
            SessionState.SetBool(Key, true);
            File.WriteAllText(ReportPath, "RUNNING: isolated Battle copy, local Q/E queue and synthetic received events.\n");
            EditorApplication.isPlaying = true;
        }

        static void OnCue(string id, AudioClip clip)
        {
            heard.Add(id);
            if (id == "ko_impact") knockoutSounds++;
            if (!game.battleSfx.GetComponentsInChildren<AudioSource>().Any(s => s.clip == clip && s.isPlaying && s.volume > 0))
                audioFailure = "Cue did not start an audible-volume AudioSource: " + id;
            sourceStarts++;
        }

        static void OnEffect(string id, GameObject root)
        {
            effects.Add(id);
            effectStarts++;
            bool ribbon = game.battleVfx.weaponTrails && game.battleVfx.weaponTrails.Owns(root);
            if (!root.activeInHierarchy || !ribbon && !root.GetComponentsInChildren<ParticleSystem>().Any(p => p.IsAlive(false)))
                vfxFailure = "VFX cue did not start a live effect: " + id;
            if (game.battleVfx.PooledEffectCount > game.battleVfx.maxInstances +
                (game.battleVfx.weaponTrails ? game.battleVfx.weaponTrails.maxTrails : 0))
                vfxFailure = "VFX pool exceeded its cap";
            var lighting = game.GetComponent<BattleLightingRig>();
            if (lighting && (id == "heavy_hit" || id == "light_hit" || id == "ground_impact"))
            {
                if (lighting.ActiveFlashCount < 1 || lighting.ActiveFlashCount > 2 || QualitySettings.pixelLightCount < 5)
                    vfxFailure = "Impact cue did not have bounded live pixel lighting: " + id;
                lightingStarts++;
            }
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            try
            {
                if (audioFailure != null) throw new Exception(audioFailure);
                if (vfxFailure != null) throw new Exception(vfxFailure);
                if (began == 0) began = EditorApplication.timeSinceStartup;
                if (EditorApplication.timeSinceStartup - began > 300) throw new Exception("SFX play check timed out");
                if (Time.frameCount == frame || EditorApplication.timeSinceStartup - began < 1) return;
                frame = Time.frameCount;
                Application.runInBackground = true;
                if (damageMode) FrankBattleDamagePlayProbe.Tick();
                if (game && game.GetComponent<BattleImpactFeedback>() is BattleImpactFeedback impact)
                {
                    if (impact.IsSlowing && !impact.IsHolding && !game.uiManager.knockout.HasShown && Time.timeScale == 1)
                        observedKoSlow = true;
                    if (game.uiManager.knockout.HasShown && (impact.IsSlowing || impact.IsHolding))
                        throw new Exception("KO lettering appeared before the finishing slow motion completed.");
                    var playback = game.leftCombat.SourcePlayback && game.leftCombat.SourcePlayback.Playing ? game.leftCombat.SourcePlayback : game.rightCombat.SourcePlayback;
                    if (impact.IsHolding)
                    {
                        if (Time.timeScale != 1 || !playback || !playback.IsContactHeld) throw new Exception("Hit-stop did not hold the participant clock independently.");
                        if (wasHolding && heldPlayback == playback && playback && Mathf.Abs(heldSample - playback.SampleTime) > .001f)
                            throw new Exception("Source pose advanced during hit-stop.");
                        heldFrames++;
                    }
                    wasHolding = impact.IsHolding; heldPlayback = playback; heldSample = playback ? playback.SampleTime : 0;
                    var cut = game.uiManager.comicCutIn;
                    if (observedKoSlow && game.uiManager.knockout.IsShowing && game.uiManager.knockout.letterK.localScale.x < 1.03f && comicCaptures.Add("AccentKO"))
                        ScreenCapture.CaptureScreenshot("GeneratedAssets/BattleCombatAccentReview/LiveKO.png");
                    if (playback && playback.Playing && playback.Move.skill != BattleSkill.None)
                    {
                        float shot = game.battleVfx.timeline.FindMove(playback.Move).cues.First(c => c.group == "skill_shot").seconds;
                        string flight = playback.name + "_" + playback.Move.skill + "_Flight";
                        if (playback.SampleTime >= shot + .07f && playback.SampleTime < shot + .14f && comicCaptures.Add(flight))
                            ScreenCapture.CaptureScreenshot("GeneratedAssets/BattleCombatAccentReview/" + flight + ".png");
                    }
                    if (cut && cut.IsShowing && playback && playback.SampleTime > .08f && playback.SampleTime < .24f)
                    {
                        string name = cut.caption.text.EndsWith("FINISHER", StringComparison.Ordinal) ? "Finisher" : cut.portrait.sprite == cut.leftPortrait ? "Mankey" : "Pepe";
                        if (comicCaptures.Add(name)) ScreenCapture.CaptureScreenshot((damageMode ? "GeneratedAssets/BattleDamageReview/" : "GeneratedAssets/BattleComicReview/") + "LiveComic_" + name + ".png");
                    }
                }
                if (!game)
                {
                    game = GameManager.Instance;
                    if (!game) return;
                    game.debugMode = false;
                    game.enableLocalInputTesting = false;
                    damageMode = SessionState.GetBool(Key + ".damage", false);
                    if (damageMode) game.ApplyRawMessage(Message("damage-created", "MATCH_CREATED", new JObject
                        { ["characterIds"] = new JArray("Mankey", "Pepe"), ["initialHpAtomic"] = 2000 }, null));
                    if (!game.battleSfx) throw new Exception("No BattleSfxPlayer in saved scene");
                    game.battleSfx.CuePlayed += OnCue;
                    if (!game.battleVfx) throw new Exception("No BattleVfxPlayer in saved scene");
                    game.battleVfx.EffectPlayed += OnEffect;
                    fighters = new[] { game.leftCombat, game.rightCombat };
                    bool criticalOnly = SessionState.GetBool(Key + ".criticalOnly", false);
                    moves = fighters.Select(f => f.lightCombatMoves.Concat(f.heavyCombatMoves)
                        .Where(m => !criticalOnly || m.weapon == TrumpWeaponManager.WeaponType.Katana ||
                            m.weapon == TrumpWeaponManager.WeaponType.Assassin).ToArray()).ToArray();
                    if (criticalOnly && moves.Any(m => m.Length != 2)) throw new Exception("Expected Katana and Assassin in both saved heavy pools.");
                    cinematicCamera = game.gameObject.scene.GetRootGameObjects()
                        .SelectMany(r => r.GetComponentsInChildren<Camera>()).Single(c => c.CompareTag("MainCamera"));
                    cameraDirector = cinematicCamera.GetComponent<FrankCinematicCamera>();
                    if (cameraDirector && (!cameraDirector.enabled || cameraDirector.battle != game))
                        throw new Exception("Live cinematic camera bindings are invalid");
                }
                if (cameraDirector)
                {
                    if (cameraDirector.LastFramingPoints.Count == 0) throw new Exception("Cinematic LateUpdate has not framed the fighters");
                    foreach (var point in cameraDirector.LastFramingPoints)
                    {
                        var viewport = cinematicCamera.WorldToViewportPoint(point);
                        if (viewport.z <= .03f || viewport.x < .049f || viewport.x > .951f || viewport.y < .049f || viewport.y > .951f)
                            throw new Exception("Live body/weapon left the cinematic frame: " + viewport);
                    }
                    if (cameraDirector.ActiveShot != null) cameraShots.Add(cameraDirector.ActiveShot.key);
                    cameraFrames++;
                }
                foreach (var source in game.battleSfx.GetComponentsInChildren<AudioSource>())
                {
                    if (!source.isPlaying) continue;
                    source.GetOutputData(samples, 0);
                    if (samples.Any(s => Mathf.Abs(s) > .00001f)) { outputFrames++; break; }
                }
                AudioListener.GetOutputData(samples, 0);
                foreach (float sample in samples) mixedPeak = Mathf.Max(mixedPeak, Mathf.Abs(sample));
                if (mixedPeak >= .999f) throw new Exception("The battle mix clipped at the listener output.");
                if (game.QueueError != null) throw new Exception(game.QueueError);
                int total = moves.Sum(m => m.Length);
                if (step > total)
                {
                    if (!waitingForResult)
                    {
                        heard.Clear();
                        var result = Message("result", "WINNER_DECLARED", new JObject { ["winnerCharacterId"] = "bot_a" }, null);
                        game.ApplyRawMessage(result);
                        game.ApplyRawMessage(result);
                        waitingForResult = true;
                        stepBegan = EditorApplication.timeSinceStartup;
                        return;
                    }
                    if (game.IsEventQueueBusy || EditorApplication.timeSinceStartup - stepBegan < 2.5) return;
                    if (game.battleVfx.ActiveEffectCount != 0) throw new Exception("Finished VFX did not return to the pool");
                    if (heard.Count(id => id == "victory") != 1) throw new Exception("Repeated result did not produce exactly one Victory");
                    if (game.uiManager && game.uiManager.knockout && knockoutSounds != 1)
                        throw new Exception("The KO lettering did not produce exactly one timed impact.");
                    game.battleSfx.PlayUiClick();
                    if (!heard.Contains("ui_click") || outputFrames == 0) throw new Exception("Missing UI cue or nonzero audio output");
                    int authoredShots = moves.SelectMany(m => m).Count(m => cameraDirector && cameraDirector.library.Find(FrankCinematicCamera.BattleKey(m)) != null);
                    if (cameraDirector && cameraShots.Count != authoredShots) throw new Exception("Live cinematic camera did not play every authored battle shot");
                    var impactCheck = game.GetComponent<BattleImpactFeedback>();
                    if (impactCheck && (impactCheck.IsHolding || Time.timeScale != 1 || heldFrames == 0)) throw new Exception("Hit-stop did not hold and restore the clock.");
                    if (impactCheck && impactCheck.knockoutSlowMotion && (impactCheck.SlowMotionCount != 1 || impactCheck.IsSlowing || !observedKoSlow))
                        throw new Exception("KO slow motion did not fire once and restore the clock.");
                    var shake = cinematicCamera ? cinematicCamera.GetComponent<BattleCameraShake>() : null;
                    if (shake && (shake.ShakeCount <= 0 || shake.IsShaking)) throw new Exception("Heavy camera shake failed to play and settle.");
                    var comicCheck = game.uiManager.comicCutIn;
                    if (comicCheck && comicCheck.ShowCount < 5) throw new Exception("Missing critical/finisher comic panels.");
                    if (impactCheck)
                    {
                        // Exercise cancellation through actual VFX, including a pre-existing slow/pause clock.
                        Time.timeScale = .5f;
                        game.battleVfx.PlayHit(game.rightCombat);
                        if (impactCheck.IsHolding || Time.timeScale != .5f) throw new Exception("Standalone VFX preview changed the external clock.");
                        game.battleVfx.ClearEffects();
                        if (impactCheck.IsHolding || Time.timeScale != .5f) throw new Exception("Cancelled contact lost the original slow clock.");
                        Time.timeScale = 0;
                        game.battleVfx.PlayHit(game.rightCombat);
                        game.battleVfx.ClearEffects();
                        if (impactCheck.IsHolding || Time.timeScale != 0) throw new Exception("Contact resumed an externally paused battle.");
                        Time.timeScale = 1;
                        game.battleVfx.PlayHit(game.rightCombat);
                        game.ResetCombatQueue();
                        if (impactCheck.IsHolding || Time.timeScale != 1 || impactCheck.flash.Progress != 1)
                            throw new Exception("Match reset left a held clock or flash.");
                        game.battleVfx.PlayHit(game.rightCombat);
                        impactCheck.enabled = false;
                        if (Time.timeScale != 1) throw new Exception("Disabling impact feedback left time paused.");
                        impactCheck.enabled = true;
                        int slowCount = impactCheck.SlowMotionCount;
                        impactCheck.BeginKnockoutSlowMotion(); impactCheck.BeginKnockoutSlowMotion();
                        if (!impactCheck.IsSlowing || Time.timeScale != 1 || impactCheck.SlowMotionCount != slowCount + 1)
                            throw new Exception("KO fallback envelope failed, changed global time or repeated.");
                        impactCheck.ResetFeedback();
                        if (Time.timeScale != 1) throw new Exception("Fallback KO cancellation left the clock slowed.");
                        game.battleVfx.ClearEffects();
                        report.AppendLine("PASS participant hit-stop, standalone preview clock isolation, external pause, match reset and disable cleanup.");
                    }
                    if (damageMode) report.AppendLine($"PASS {FrankBattleDamagePlayProbe.CheckedHits} native damage contact frames, duplicate DAMAGE_APPLIED/HP_CHANGED and late HP snapshots.");
                    Finish(true, $"PASS {total} " + (damageMode ? "server damage exchanges" : "local exchanges") + " + duplicated lethal DAMAGE_APPLIED + duplicated WINNER_DECLARED; " +
                        $"{sourceStarts} AudioSource starts, {outputFrames} frames with nonzero audio output, mixed peak {mixedPeak:F3}; " +
                        $"{effectStarts} VFX starts, {lightingStarts} synchronized impact light cues, {heldFrames} held pose frames with clock restored, {comicCheck?.ShowCount ?? 0} comic panels; one KO/UI impact with observed 32% slow motion; {shake?.ShakeCount ?? 0} settled contact shakes; fallback KO/pause/cancel checks passed; finished particles returned to pool; UI click played. " +
                        (cameraDirector ? $"Cinematic camera: {cameraShots.Count} authored shots, {cameraFrames} frames with bodies/weapons inside the safe frame, recovery/KO/victory completed." : ""));
                    return;
                }
                int side = step < moves[0].Length || step == total ? 0 : 1;
                var attacker = fighters[side];
                var receiver = fighters[1 - side];
                var move = step == total ? moves[0].First(m => m.weapon != TrumpWeaponManager.WeaponType.None) :
                    moves[side][step - (side == 0 ? 0 : moves[0].Length)];
                bool lethal = step == total;
                bool heavy = move.weapon != TrumpWeaponManager.WeaponType.None;
                if (!started)
                {
                    if (game.IsEventQueueBusy || fighters.Any(f => !f.IsIdleAndSettled)) return;
                    if (heavy) attacker.heavyCombatMoves = new[] { move };
                    else attacker.lightCombatMoves = new[] { move };
                    heard.Clear();
                    effects.Clear();
                    if (damageMode) FrankBattleDamagePlayProbe.Begin(game, attacker, move, step, lethal);
                    else if (lethal)
                    {
                        var payload = new JObject { ["actorCharacterId"] = "bot_a", ["targetCharacterId"] = "bot_b",
                            ["hpAfterAtomic"] = 0, ["damageAtomic"] = 1000, ["animationId"] = "attack_heavy" };
                        string message = Message("damage", "DAMAGE_APPLIED", payload, "lethal-turn");
                        game.ApplyRawMessage(message); game.ApplyRawMessage(message);
                    }
                    else if (side == 0 && !heavy) game.DebugTriggerQ();
                    else if (side == 1 && heavy) game.DebugTriggerE();
                    else game.EnqueueLocalAttack(side == 0 ? PlayerUI.Side.Left : PlayerUI.Side.Right, heavy);
                    started = true;
                    stepBegan = EditorApplication.timeSinceStartup;
                    return;
                }
                if (EditorApplication.timeSinceStartup - stepBegan > 35) throw new Exception("Stalled at " + move.moveName);
                if (game.IsEventQueueBusy || attacker.IsBusy || receiver.IsBusy) return;
                var expected = game.battleSfx.bank.FindMove(move).cues.Select(c => lethal && c.finalLanding ? "knockout_fall" : c.group).ToList();
                if (game.battleSfx.enableContactLayers)
                    expected = expected.SelectMany(id => new[] { id }.Concat(
                        game.battleSfx.bank.FindGroup(id)?.layers ?? Array.Empty<string>())).ToList();
                if (!lethal) expected.Add("getup");
                if (step == 0) expected.Insert(0, "fight_start");
                // KO UI uses an unscaled clock and may enter during the lethal reaction.
                if (!heard.Where(id => id != "ko_impact").SequenceEqual(expected)) throw new Exception("Live cue mismatch " + attacker.name + " " + move.moveName + ": " + string.Join(",", heard));
                var profile = game.battleSfx.bank.FindMove(move);
                int expectedHits = profile.cues.Count(c => c.group == "light_hit" || c.group == "heavy_hit" || c.group == "stab_hit");
                if (effects.Count(id => id.EndsWith("hit")) != expectedHits ||
                    effects.Count(id => id.EndsWith("fall")) != profile.cues.Count(c => c.group == "body_fall"))
                    throw new Exception("Live VFX mismatch " + attacker.name + " " + move.moveName);
                if (!attacker.LastSequenceSucceeded || !receiver.LastSequenceSucceeded || (lethal && !receiver.IsDead))
                    throw new Exception("Animation completion regressed");
                if (damageMode) report.AppendLine(FrankBattleDamagePlayProbe.Complete());
                report.AppendLine($"PASS live {attacker.name} {move.moveName} lethal={lethal}: {heard.Count} audio cues, {effects.Count} VFX, queue completed.");
                File.WriteAllText(ReportPath, report.ToString());
                step++;
                started = false;
            }
            catch (Exception exception) { Finish(false, exception.ToString()); }
        }

        static string Message(string id, string type, JObject payload, string turn) =>
            new JObject { ["type"] = "meme_battle_event", ["event"] = new JObject
                { ["matchId"] = "local-sfx-validation", ["eventId"] = id, ["eventType"] = type,
                    ["turnId"] = turn, ["payload"] = payload } }.ToString();

        static void Finish(bool passed, string detail)
        {
            report.AppendLine((passed ? "" : "FAIL ") + detail);
            File.WriteAllText(ReportPath, report.ToString());
            if (damageMode) File.WriteAllText("GeneratedAssets/BattleDamageReview/PlayValidation.txt", report.ToString());
            if (game && game.battleSfx) game.battleSfx.CuePlayed -= OnCue;
            if (game && game.battleVfx) game.battleVfx.EffectPlayed -= OnEffect;
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
            game = null; fighters = null; moves = null;
            cameraDirector = null; cinematicCamera = null; cameraFrames = 0; cameraShots.Clear();
            step = outputFrames = sourceStarts = knockoutSounds = 0; frame = -1;
            began = stepBegan = 0; started = waitingForResult = false;
            audioFailure = null;
            vfxFailure = null;
            effectStarts = 0;
            lightingStarts = 0;
            heldFrames = 0; heldPlayback = null; wasHolding = false; observedKoSlow = false; comicCaptures.Clear();
            damageMode = false; FrankBattleDamagePlayProbe.Reset();
            heard.Clear(); effects.Clear(); report.Clear();
        }
    }

    public static partial class FrankRetargetBuilder
    {
        public static void BattleSfxPlayCheck() => FrankBattleSfxPlayCheck.Start();
        public static void BattleCriticalMovesPlayCheck() => FrankBattleSfxPlayCheck.Start(true);
    }
}
