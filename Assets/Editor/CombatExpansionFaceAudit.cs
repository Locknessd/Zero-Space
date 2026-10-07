using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static class CombatExpansionFaceAudit
    {
        static readonly HumanBodyBones[] Face =
        {
            HumanBodyBones.LeftEye, HumanBodyBones.RightEye, HumanBodyBones.Jaw
        };

        public static void Audit()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Face audit requires Edit Mode.");
            var scene = EditorSceneManager.OpenPreviewScene(CombatExpansionInventory.Battle);
            var report = new StringBuilder("fighter,move,role,bone,sourceMapped,maxLocalRotationDegrees,maxLocalPositionMetres\n");
            try
            {
                var game = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var fighter in fighters)
                {
                    fighter.battleSfx = null;
                    fighter.battleVfx = null;
                    fighter.hitEffect = null;
                    fighter.Initialize();
                }
                foreach (var source in fighters)
                foreach (int index in new[] { 3, 4, 5, 8, 9, 12 })
                {
                    var target = fighters.Single(f => f != source);
                    foreach (var fighter in fighters)
                        fighter.ResetCombat();
                    var move = CombatExpansionVol10Study.MakeMove(source, target, index);
                    source.Animator.transform.position = Vector3.left * move.attackRange * .5f;
                    target.Animator.transform.position = Vector3.right * move.attackRange * .5f;
                    if (!source.ExecuteAttack(move, target))
                        throw new InvalidOperationException("Could not start " + move.moveName);
                    var pair = source.SourcePlayback;
                    try
                    {
                        foreach (bool attacking in new[] { true, false })
                        {
                            var actor = attacking ? pair.AttackerActor : pair.ReceiverActor;
                            var animator = attacking ? source.Animator : target.Animator;
                            var skeleton = animator.avatar.humanDescription.skeleton
                                .GroupBy(b => b.name).ToDictionary(g => g.Key, g => g.First());
                            foreach (var bone in Face)
                            {
                                var node = animator.GetBoneTransform(bone);
                                if (!node || !skeleton.TryGetValue(node.name, out var reference))
                                    continue;
                                float maxRotation = 0;
                                float maxPosition = 0;
                                int frames = Mathf.CeilToInt(pair.Duration * 60);
                                for (int frame = 0; frame <= frames; frame++)
                                {
                                    pair.EvaluateAt(Mathf.Min(pair.Duration, frame / 60f));
                                    maxRotation = Mathf.Max(maxRotation,
                                        Quaternion.Angle(reference.rotation, node.localRotation));
                                    maxPosition = Mathf.Max(maxPosition,
                                        node.parent.TransformVector(node.localPosition - reference.position).magnitude);
                                }
                                bool mapped = actor.Pose.sourceHumanAvatar.humanDescription.human
                                    .Any(h => h.humanName == bone.ToString());
                                string role = attacking ? "attacker" : "receiver";
                                report.AppendLine(FormattableString.Invariant(
                                    $"{animator.name},{move.moveName},{role},{bone},{mapped},{maxRotation:R},{maxPosition:R}"));
                            }
                        }
                    }
                    finally
                    {
                        pair.Cancel();
                    }
                }
                Directory.CreateDirectory(CombatExpansionInventory.Output);
                File.WriteAllText(CombatExpansionInventory.Output + "/FaceAudit.csv", report.ToString());
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
