using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

public static partial class BattleRosterSetup
{
    public static void ValidateClips()
    {
        var scene = EditorSceneManager.OpenPreviewScene(BattlePath);
        var report = new StringBuilder();
        int samples = 0;
        try
        {
            var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
            foreach (var fighter in new[] { game.leftCombat, game.rightCombat })
            foreach (var entry in game.characterCatalog.characters.Skip(2))
            {
                fighter.ResetCombat();
                if (!fighter.GetComponent<BattleCharacterAppearance>().TryApply(entry.id))
                    throw new Exception("Cannot sample " + entry.id);
                var clips = new[] { fighter.idleAnim, fighter.walkAnim, fighter.victoryAnim }
                    .Concat(fighter.lightCombatMoves.Concat(fighter.heavyCombatMoves).SelectMany(m =>
                        new[] { m.getUpAnim, m.sourcePair?.getUp, m.sourcePair?.attackerGetUp }))
                    .Where(c => c).Distinct().ToArray();
                foreach (var clip in clips)
                {
                    if (!clip.humanMotion)
                        throw new Exception("Controller clip is not Humanoid: " + clip.name);
                    var copy = Object.Instantiate(clip);
                    copy.events = Array.Empty<AnimationEvent>();
                    var animator = fighter.Animator;
                    var controller = animator.runtimeAnimatorController;
                    animator.runtimeAnimatorController = null;
                    animator.enabled = true;
                    var graph = PlayableGraph.Create("Roster controller clip validation");
                    try
                    {
                        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                        var playable = AnimationClipPlayable.Create(graph, copy);
                        playable.SetSpeed(0);
                        var output = AnimationPlayableOutput.Create(graph, "Pose", animator);
                        output.SetSourcePlayable(playable);
                        graph.Play();
                        for (int frame = 0; frame <= 16; frame++)
                        {
                            playable.SetTime(Mathf.Min(copy.length - .00001f, copy.length * frame / 16));
                            graph.Evaluate(0);
                            foreach (var bone in new[] { HumanBodyBones.Head, HumanBodyBones.Hips,
                                HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
                                HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                            {
                                var target = animator.GetBoneTransform(bone);
                                if (!target || !float.IsFinite(target.position.sqrMagnitude) ||
                                    Vector3.Distance(target.position, animator.transform.position) > 7)
                                    throw new Exception("Invalid controller pose: " + entry.id + "/" + copy.name);
                            }
                            samples++;
                        }
                        report.AppendLine(entry.id + "/" + fighter.name + "/" + clip.name + " Humanoid PASS");
                    }
                    finally
                    {
                        graph.Destroy();
                        animator.runtimeAnimatorController = controller;
                        fighter.ResetCombat();
                        Object.DestroyImmediate(copy);
                    }
                }
            }
            report.Insert(0, "PASS idle/walk/victory and every registered controller recovery clip.\n" +
                "Pose samples=" + samples + ".\n");
        }
        catch (Exception error)
        {
            report.Insert(0, "FAIL " + error + "\n");
            throw;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            File.WriteAllText(Review + "/ControllerClips.txt", report.ToString());
        }
    }
}

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void ValidateBattleRosterClips() => BattleRosterSetup.ValidateClips();
    }
}
