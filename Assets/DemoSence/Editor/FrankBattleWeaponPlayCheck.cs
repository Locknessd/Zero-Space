using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    // Exercise the normal queue after Idle has advanced, without resetting between attacks.
    [InitializeOnLoad]
    public static class FrankBattleWeaponPlayCheck
    {
        const string Key = "FrankBattleWeaponPlayCheck";
        static CharacterCombat[] fighters;
        static int step, lastFrame = -1, samples;
        static bool started;
        static double start;
        static float maxGrip;
        static float[] sequenceHipsDepth;
        static readonly StringBuilder Report = new StringBuilder();

        static FrankBattleWeaponPlayCheck() { EditorApplication.update += Tick; }

        static GameManager game;
        static CombatTripletData[][] moves;
        static double attackBegan;
        static int onCameraFrames;

        public static void Start()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Single);
            fighters = null;
            game = null;
            moves = null;
            step = samples = 0;
            started = false;
            lastFrame = -1;
            sequenceHipsDepth = null;
            start = 0;
            Report.Clear();
            Directory.CreateDirectory("Temp/FrankRetarget");
            File.WriteAllText("Temp/FrankRetarget/source-rig-play.txt", "RUNNING BattleScene local input queue, normal speed, no combat resets\n");
            var view = EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView"));
            view.Show();
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            try
            {
                Application.runInBackground = true;
                if (start == 0) start = EditorApplication.timeSinceStartup;
                if (EditorApplication.timeSinceStartup - start > 300) throw new Exception("Play check timed out.");
                // The original test started straight after Rebind. Real E input comes after
                // Idle has advanced and exposed the duplicate Trigger/Play state entry.
                if (EditorApplication.timeSinceStartup - start < 2) return;
                if (lastFrame == Time.frameCount) return;
                lastFrame = Time.frameCount;
                if (fighters == null)
                {
                    fighters = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CharacterCombat>(true)).OrderBy(f => f.name).ToArray();
                    if (fighters.Length != 2) throw new Exception("Expected two battle fighters.");
                    game = GameManager.Instance;
                    moves = fighters.Select(f => f.heavyCombatMoves.Concat(f.lightCombatMoves).ToArray()).ToArray();
                    if (!game || game.leftCombat != fighters[0] || game.rightCombat != fighters[1])
                        throw new Exception("Unexpected GameManager fighter bindings.");
                }
                int count = moves[0].Length;
                if (step >= count * 2) { Finish(true, null); return; }
                var attacker = fighters[step / count];
                var receiver = fighters[1 - step / count];
                var move = moves[step / count][step % count];
                if (!started)
                {
                    if (game.IsEventQueueBusy || fighters.Any(f => !f.IsIdleAndSettled)) return;
                    sequenceHipsDepth = fighters.Select(f =>
                    {
                        var hips = f.Animator ? f.Animator.GetBoneTransform(HumanBodyBones.Hips) : null;
                        return hips ? hips.position.z : float.NaN;
                    }).ToArray();
                    bool heavy = move.weapon != TrumpWeaponManager.WeaponType.None;
                    if (heavy) attacker.heavyCombatMoves = new[] { move };
                    else attacker.lightCombatMoves = new[] { move };
                    if (attacker == game.rightCombat && heavy) game.DebugTriggerE();
                    else if (attacker == game.leftCombat && !heavy) game.DebugTriggerQ();
                    else game.EnqueueLocalAttack(attacker == game.leftCombat ? PlayerUI.Side.Left : PlayerUI.Side.Right, heavy);
                    attackBegan = EditorApplication.timeSinceStartup;
                    onCameraFrames = 0;
                    started = true;
                    samples = 0;
                    maxGrip = 0;
                    return;
                }
                if (attacker.SourcePlayback && attacker.SourcePlayback.Playing)
                {
                    var player = attacker.SourcePlayback;
                    if (receiver.SourcePlayback != player || !attacker.IsBusy || !receiver.IsBusy)
                        throw new Exception("Pair lost shared playback ownership.");
                    bool sourcePoseActive = player.AttackerActor && player.ReceiverActor;
                    if (sourcePoseActive && (attacker.Animator.enabled || receiver.Animator.enabled))
                        throw new Exception("Battle Animator is overwriting the source pose.");
                    if (Mathf.Abs(attacker.transform.position.z - receiver.transform.position.z) > .001f)
                        throw new Exception("Fighters left the battle lane.");
                    if (move.weapon == TrumpWeaponManager.WeaponType.None && sequenceHipsDepth != null)
                    {
                        for (int index = 0; index < fighters.Length; index++)
                        {
                            var hips = fighters[index].Animator ? fighters[index].Animator.GetBoneTransform(HumanBodyBones.Hips) : null;
                            if (hips && float.IsFinite(sequenceHipsDepth[index]) &&
                                Mathf.Abs(hips.position.z - sequenceHipsDepth[index]) > .001f)
                                throw new Exception($"Light pair Hips left depth lane: {fighters[index].name} expected={sequenceHipsDepth[index]:F5} current={hips.position.z:F5} delta={hips.position.z - sequenceHipsDepth[index]:F5} sourcePose={sourcePoseActive} sample={samples} time={player.SampleTime:F3}");
                        }
                    }
                    if (sourcePoseActive)
                    {
                        foreach (var actor in new[] { player.AttackerActor, player.ReceiverActor })
                        {
                            if (!float.IsFinite(actor.Pose.targetHips.position.sqrMagnitude)) throw new Exception("Non-finite pose.");
                            foreach (var limb in actor.Pose.limbs.Where(l => l.alignGrip))
                                maxGrip = Mathf.Max(maxGrip, Vector3.Distance(limb.SourceGrip, limb.TargetGrip));
                        }
                        var pose = player.AttackerActor.Pose;
                        if (move.weapon != TrumpWeaponManager.WeaponType.None && pose.weaponRenderers.Any(r => !r.enabled || !r.gameObject.activeInHierarchy))
                            throw new Exception("Source pair weapon became inactive.");
                        if (Camera.main && (move.weapon == TrumpWeaponManager.WeaponType.None || pose.weaponRenderers.Any(r => GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(Camera.main), r.bounds)))) onCameraFrames++;
                    }
                    else if (Camera.main && move.weapon == TrumpWeaponManager.WeaponType.None)
                        onCameraFrames++;
                    if (samples == 100)
                    {
                        Directory.CreateDirectory("Temp/FrankRetarget/PairScreens");
                        ScreenCapture.CaptureScreenshot("Temp/FrankRetarget/PairScreens/" + attacker.name + "-" + move.moveName + ".png");
                    }
                    samples++;
                }
                else if (attacker.weaponRig.ActiveDriver)
                {
                    var pose = attacker.weaponRig.ActiveDriver.pose;
                    if (pose.weaponRenderers.Any(r => !r.enabled || !r.gameObject.activeInHierarchy)) throw new Exception("Weapon became inactive mid attack.");
                    foreach (var hand in pose.limbs.Where(l => l.alignGrip))
                        maxGrip = Mathf.Max(maxGrip, Vector3.Distance(hand.SourceGrip, hand.TargetGrip));
                    if (Camera.main && pose.weaponRenderers.Any(r => GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(Camera.main), r.bounds))) onCameraFrames++;
                    if (samples == 12)
                    {
                        Directory.CreateDirectory("Temp/FrankRetarget/WeaponScreens");
                        ScreenCapture.CaptureScreenshot("Temp/FrankRetarget/WeaponScreens/live-" + attacker.name + "-" + move.moveName + ".png");
                    }
                    samples++;
                }
                if (!string.IsNullOrEmpty(game.QueueError)) throw new Exception(game.QueueError);
                if (samples == 0 && EditorApplication.timeSinceStartup - attackBegan > 20) throw new Exception("No weapon rig appeared in BattleScene.");
                if (samples == 0 || attacker.IsBusy || receiver.IsBusy || game.IsEventQueueBusy) return;
                if (!attacker.LastSequenceSucceeded || !receiver.LastSequenceSucceeded || samples < 5 || onCameraFrames == 0 || maxGrip > .12f)
                    throw new Exception($"Failed live sequence {attacker.name} {move.moveName}: frames={samples}, grip={maxGrip}");
                if (attacker.weaponRig.ActiveDriver || attacker.GetComponentsInChildren<FrankTestDriver>().Length > 0)
                    throw new Exception("Weapon rig was not released.");
                if (fighters.Any(f => !f.Animator.enabled || Mathf.Abs(f.transform.position.y) > .001f))
                    throw new Exception("Fighter did not return to grounded Animator playback.");
                Report.AppendLine($"PASS live {attacker.name} {move.moveName}: activeFrames={samples}, cameraFrames={onCameraFrames}, gripError={maxGrip:F4}m; shared pair complete, grounded Idle restored");
                File.WriteAllText("Temp/FrankRetarget/source-rig-play.txt", Report.ToString());
                step++;
                started = false;
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }

        static void Finish(bool success, string error)
        {
            Report.AppendLine(success ? "PASS all 16 attack/reaction pairs in Play Mode through the local input queue." : "FAIL " + error);
            File.WriteAllText("Temp/FrankRetarget/source-rig-play.txt", Report.ToString());
            SessionState.SetBool(Key, false);
            Time.timeScale = 1;
            EditorApplication.isPlaying = false;
            EditorApplication.playModeStateChanged += Restore;
            fighters = null;
            step = samples = 0;
            lastFrame = -1;
            start = 0;
            started = false;
            Report.Clear();
        }

        static void Restore(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= Restore;
            EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Single);
        }
    }

    public static partial class FrankRetargetBuilder
    {
        public static void BattleWeaponPlayCheck() { FrankBattleWeaponPlayCheck.Start(); }
    }
}
