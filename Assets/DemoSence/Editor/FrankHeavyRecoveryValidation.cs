using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void ValidateHeavyRecovery()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
            var report = new StringBuilder();
            var tick = typeof(FrankBattlePairPlayback).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                int count = 0;
                foreach (var a in fighters)
                foreach (var move in a.heavyCombatMoves)
                foreach (bool lethal in new[] { false, true })
                {
                    var b = fighters.Single(f => f != a);
                    foreach (var f in fighters) f.ResetCombat();
                    a.transform.position = Vector3.zero;
                    b.transform.position = Vector3.right * move.attackRange;
                    if (!move.sourcePair.getUp || move.getUpAnim != move.sourcePair.getUp)
                        throw new Exception("Missing or inconsistent GetUp: " + move.moveName);
                    int aid = a.PlaybackId + 1, bid = b.PlaybackId + 1;
                    int endsA = 0, endsB = 0;
                    Action<CharacterCombat, int, bool> onEnd = (f, id, ok) =>
                    {
                        if (!ok || id != (f == a ? aid : bid)) throw new Exception("Invalid completion ID/result");
                        if (f == a) endsA++; else endsB++;
                    };
                    a.SequenceEnded += onEnd;
                    b.SequenceEnded += onEnd;
                    try
                    {
                        if (!a.ExecuteAttack(move, b, lethal)) throw new Exception("Attack rejected");
                        var player = a.SourcePlayback;
                        player.EvaluateAt(player.Duration);
                        var bones = b.Animator.GetComponentsInChildren<Transform>();
                        var terminal = bones.Select(t => t.position).ToArray();
                        tick.Invoke(player, null);
                        if (lethal)
                        {
                            b.MarkDead();
                            for (int frame = 0; frame < 60; frame++) tick.Invoke(player, null);
                            if (player.Playing || !player.ReceiverActor || b.Animator.enabled || !b.IsDead)
                                throw new Exception("Lethal reaction failed to hold its final pose");
                            for (int i = 0; i < bones.Length; i++)
                                if (Vector3.Distance(bones[i].position, terminal[i]) > .0001f)
                                    throw new Exception("Death pose moved after completion");
                        }
                        else
                        {
                            if (!player.Playing || !a.IsBusy || !b.IsBusy || !b.Animator.enabled || b.PlaybackId != bid)
                                throw new Exception("Recovery lost sequence ownership");
                            if (!b.Animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.GetUp") ||
                                !b.Animator.GetCurrentAnimatorClipInfo(0).Any(c => c.clip == move.sourcePair.getUp))
                                throw new Exception("GetUp state/override was not applied");
                            float dt = move.sourcePair.getUp.length / 120f;
                            for (int frame = 0; frame < 60; frame++)
                            {
                                b.Animator.Update(dt);
                                tick.Invoke(player, null);
                            }
                            if (!b.IsBusy || endsA != 0 || endsB != 0)
                                throw new Exception("GetUp completed before half its duration");
                            for (int frame = 0; frame < 70; frame++)
                            {
                                b.Animator.Update(dt);
                                tick.Invoke(player, null);
                            }
                            if (player.Playing || !a.IsIdleAndSettled || !b.IsIdleAndSettled)
                                throw new Exception("Recovery failed to return both fighters to Idle");
                        }
                        if (endsA != 1 || endsB != 1) throw new Exception("Expected one completion per fighter");
                        report.AppendLine($"PASS {a.name} {move.moveName} lethal={lethal}: correct completion IDs, " +
                            (lethal ? "death pose retained" : $"GetUp {move.sourcePair.getUp.name} ({move.sourcePair.getUp.length:F3}s) then Idle"));
                        count++;
                    }
                    finally
                    {
                        a.SequenceEnded -= onEnd;
                        b.SequenceEnded -= onEnd;
                        foreach (var f in fighters) f.ResetCombat();
                    }
                }
                report.AppendLine($"PASS {count} heavy recovery/death cases; reset restored both Animators.");
            }
            catch (Exception e) { report.AppendLine("FAIL " + e); throw; }
            finally
            {
                Directory.CreateDirectory("Temp/FrankRetarget");
                File.WriteAllText("Temp/FrankRetarget/heavy-recovery-validation.txt", report.ToString());
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
