using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        const string KoFacingReview = "GeneratedAssets/BattleKoFacingReview";

        public static void SurveyBattleKoFacing() => ValidateBattleKoFacing(false);

        [MenuItem("Tools/Battle/Check winner facing after KO")]
        public static void CheckBattleKoFacing() => ValidateBattleKoFacing(true);

        static void ValidateBattleKoFacing(bool requirePass)
        {
            Directory.CreateDirectory(KoFacingReview);
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
            var report = new StringBuilder();
            int cases = 0, failures = 0;
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var fighter in fighters)
                {
                    fighter.battleSfx = null;
                    fighter.battleVfx = null;
                    fighter.hitEffect = null;
                }
                foreach (var canvas in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)))
                    canvas.gameObject.SetActive(false);
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>()).Single(c => c.CompareTag("MainCamera"));
                camera.scene = scene;
                var finishMotion = typeof(FrankBattlePairPlayback).GetMethod("CompleteSourceMotion", BindingFlags.Instance | BindingFlags.NonPublic);
                foreach (var attacker in fighters)
                foreach (var move in attacker.lightCombatMoves.Concat(attacker.heavyCombatMoves))
                foreach (bool mirrored in new[] { false, true })
                {
                    var receiver = fighters.Single(f => f != attacker);
                    foreach (var fighter in fighters) fighter.ResetCombat();
                    attacker.transform.position = new Vector3(mirrored ? .5f : -.5f, 0, -.5f);
                    receiver.transform.position = attacker.transform.position + Vector3.right * (mirrored ? -move.attackRange : move.attackRange);
                    if (!attacker.ExecuteAttack(move, receiver, true)) throw new Exception("Lethal attack rejected: " + move.moveName);
                    var pair = attacker.SourcePlayback;
                    pair.EvaluateAt(pair.Duration);
                    finishMotion.Invoke(pair, null);
                    var fallen = receiver.Animator.GetBoneTransform(HumanBodyBones.Hips);
                    Vector3 heldPosition = fallen.position;
                    Quaternion heldRotation = fallen.rotation;
                    game.positioningController.FinishExchange(game.leftCombat, game.rightCombat);
                    if (Vector3.Distance(heldPosition, fallen.position) > .0001f || Quaternion.Angle(heldRotation, fallen.rotation) > .001f)
                        throw new Exception("Facing correction moved the held death pose.");
                    Vector3 direction = fallen.position - attacker.Animator.GetBoneTransform(HumanBodyBones.Hips).position;
                    direction.y = 0;
                    float dot = Vector3.Dot(attacker.Animator.transform.forward, direction.normalized);
                    bool passed = dot > .98f;
                    cases++;
                    if (!passed) failures++;
                    report.AppendLine($"{(passed ? "PASS" : "FAIL")} {attacker.name} {move.moveName} mirrored={mirrored}: facingDot={dot:F4}; winnerX={attacker.transform.position.x:F3}; defeatedRootX={receiver.transform.position.x:F3}; defeatedBodyX={fallen.position.x:F3}; death pose preserved.");
                    // Capture a root-crossing execution on each fighter, after its authored death pose settles.
                    if (move.moveName == "Heavy_8" && !mirrored)
                    {
                        camera.GetComponent<FrankCinematicCamera>()?.Apply(0, true);
                        Vector3 focus = (fallen.position + attacker.Animator.GetBoneTransform(HumanBodyBones.Hips).position) * .5f;
                        camera.transform.position = focus + new Vector3(0, 1.1f, 6);
                        camera.transform.LookAt(focus);
                        camera.fieldOfView = 42;
                        var target = new RenderTexture(1280, 720, 24);
                        target.Create();
                        camera.targetTexture = target;
                        try { CaptureBattleCamera(camera, KoFacingReview + "/" + (requirePass ? "After_" : "Before_") + attacker.name + ".png"); }
                        finally { camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(target); }
                    }
                }
                report.AppendLine($"{(failures == 0 ? "PASS" : "FAIL")} {cases} lethal poses, both fighters and mirrored attack directions; failures={failures}.");
                File.WriteAllText(KoFacingReview + (requirePass ? "/PoseValidation.txt" : "/Before.txt"), report.ToString());
                if (requirePass && failures != 0) throw new Exception("Winner turned away from the actual fallen body in " + failures + " cases.");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
