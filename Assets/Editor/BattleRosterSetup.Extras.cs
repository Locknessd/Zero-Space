using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FrankRetarget;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public static partial class BattleRosterSetup
{
    static IEnumerator extras;
    static readonly HashSet<string> recovered = new HashSet<string>();

    public static void StartExtras()
    {
        if (extras != null || calibration != null)
            throw new Exception("Another character calibration is running.");
        recovered.Clear();
        EditorApplication.LockReloadAssemblies();
        extras = CalibrateExtras();
        File.WriteAllText(Review + "/ExtraStatus.txt", "RUNNING");
        EditorApplication.update += AdvanceExtras;
    }

    static void AdvanceExtras()
    {
        try
        {
            if (extras.MoveNext())
                return;
            AssetDatabase.SaveAssets();
            File.WriteAllText(Review + "/ExtraStatus.txt", "COMPLETE");
        }
        catch (Exception error)
        {
            File.WriteAllText(Review + "/ExtraStatus.txt", "FAIL " + error);
            Debug.LogException(error);
        }
        ((IDisposable)extras).Dispose();
        extras = null;
        EditorApplication.update -= AdvanceExtras;
        EditorApplication.UnlockReloadAssemblies();
    }

    static IEnumerator CalibrateExtras()
    {
        var scene = EditorSceneManager.OpenPreviewScene(BattlePath);
        try
        {
            var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
            var fighters = new[] { game.leftCombat, game.rightCombat };
            game.battleSfx.enabled = false;
            foreach (var fighter in fighters)
            {
                fighter.battleSfx = game.battleSfx;
                fighter.battleVfx = null;
                fighter.hitEffect = null;
                fighter.Initialize();
            }
            foreach (var source in fighters)
            foreach (var move in source.lightCombatMoves.Concat(source.heavyCombatMoves).Distinct())
            {
                var target = fighters.Single(f => f != source);
                var lethal = move.actionDefinition ? move.actionDefinition.lethalPairVariant : null;
                foreach (var entry in game.characterCatalog.characters)
                {
                    foreach (var fighter in fighters)
                    {
                        fighter.ResetCombat();
                        fighter.GetComponent<BattleCharacterAppearance>().TryApply(entry.id);
                    }
                    if (lethal != null)
                    {
                        BackupAsset(lethal.grounding);
                        BackupAsset(move.actionDefinition);
                        var avatar = source.Animator.avatar;
                        float duration = lethal.Duration(move.sourcePair);
                        lethal.grounding.tracks = lethal.grounding.tracks.Where(t => t.avatar != avatar)
                            .Concat(new[]
                            {
                                new FrankPairGrounding.Track { avatar = avatar, duration = duration, lift = new float[2] },
                                new FrankPairGrounding.Track
                                {
                                    avatar = avatar,
                                    receiver = true,
                                    duration = duration,
                                    lift = new float[2]
                                }
                            }).ToArray();
                        source.transform.position = Vector3.zero;
                        target.transform.position = Vector3.right * move.attackRange;
                        if (!source.ExecuteAttack(move, target, true))
                            throw new Exception("Lethal appearance rejected: " + entry.id + "/" + move.moveName);
                        var pair = source.SourcePlayback;
                        try
                        {
                            BakeRosterFloor(pair, fighters, source, lethal.grounding);
                            BakeRosterContacts(pair, source, target, move, lethal.presentationProfile);
                            EditorUtility.SetDirty(lethal.grounding);
                            EditorUtility.SetDirty(move.actionDefinition);
                        }
                        finally
                        {
                            pair.Cancel();
                            foreach (var fighter in fighters)
                                fighter.ResetCombat();
                        }
                    }
                    if (move.sourcePair.recoveryGrounding)
                    {
                        BakeRecovery(source, move.sourcePair.attackerGetUp, move.sourcePair.recoveryGrounding, false);
                        BakeRecovery(target, move.sourcePair.getUp, move.sourcePair.recoveryGrounding, true);
                    }
                    File.WriteAllText(Review + "/ExtraStatus.txt", "RUNNING " + source.name + "/" +
                        move.moveName + "/" + entry.id);
                    yield return null;
                }
            }
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    static void BakeRecovery(CharacterCombat fighter, AnimationClip clip, FrankPairGrounding grounding, bool receiver)
    {
        if (!clip)
            return;
        var animator = fighter.Animator;
        string key = AssetDatabase.GetAssetPath(grounding) + animator.avatar.GetInstanceID() + receiver;
        if (!recovered.Add(key))
            return;
        if (!clip.humanMotion)
            throw new Exception("Recovery is not Humanoid: " + clip.name);
        BackupAsset(grounding);
        var controller = animator.runtimeAnimatorController;
        animator.runtimeAnimatorController = null;
        animator.enabled = true;
        var graph = PlayableGraph.Create("Roster recovery calibration");
        try
        {
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetSpeed(0);
            var output = AnimationPlayableOutput.Create(graph, "Recovery", animator);
            output.SetSourcePlayable(playable);
            graph.Play();
            var probe = new BattleRosterFloorProbe(fighter);
            int intervals = Mathf.Max(1, Mathf.CeilToInt(clip.length * 60));
            var lifts = new float[intervals + 1];
            for (int i = 0; i <= intervals; i++)
            {
                playable.SetTime(Mathf.Min(clip.length - .00001f, clip.length * i / intervals));
                graph.Evaluate(0);
                lifts[i] = Mathf.Max(0, fighter.transform.position.y - probe.Minimum() + .008f);
            }
            var envelope = new float[lifts.Length];
            for (int i = 0; i < lifts.Length; i++)
                for (int j = Math.Max(0, i - 1); j <= Math.Min(i + 1, lifts.Length - 1); j++)
                    envelope[i] = Mathf.Max(envelope[i], lifts[j]);
            grounding.tracks = grounding.tracks.Where(t => t.avatar != animator.avatar || t.receiver != receiver)
                .Concat(new[] { new FrankPairGrounding.Track
                {
                    avatar = animator.avatar,
                    receiver = receiver,
                    duration = clip.length,
                    lift = envelope
                } }).ToArray();
            EditorUtility.SetDirty(grounding);
        }
        finally
        {
            graph.Destroy();
            animator.runtimeAnimatorController = controller;
            fighter.ResetCombat();
        }
    }
}

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void CalibrateBattleRosterExtras() => BattleRosterSetup.StartExtras();
    }
}
