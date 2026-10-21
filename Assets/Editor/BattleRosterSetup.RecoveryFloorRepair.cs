using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using FrankRetarget;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class BattleRosterSetup
{
    static IEnumerator recoveryRepair;
    static readonly Dictionary<string, FrankPairGrounding> recoveryAssets = new();
    static readonly StringBuilder recoveryFloorReport = new();
    static readonly MethodInfo finishSource = typeof(FrankBattlePairPlayback).GetMethod(
        "CompleteSourceMotion", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly PropertyInfo receiverRecovery = typeof(FrankBattlePairPlayback).GetProperty(
        "RecoveryClip", BindingFlags.Instance | BindingFlags.NonPublic);

    public static void RepairRecoveryFloors()
    {
        if (recoveryRepair != null || fallRepair != null || calibration != null || extras != null)
            throw new InvalidOperationException("A roster calibration is already running.");
        Directory.CreateDirectory(Root + "/RecoveryGrounding");
        AssetDatabase.Refresh();
        recoveryAssets.Clear();
        recoveryFloorReport.Clear();
        File.WriteAllText(Review + "/RecoveryFloorStatus.txt", "RUNNING");
        EditorApplication.LockReloadAssemblies();
        recoveryRepair = BakeRecoveryFloors();
        EditorApplication.update += AdvanceRecoveryRepair;
    }

    static void AdvanceRecoveryRepair()
    {
        try
        {
            if (recoveryRepair.MoveNext()) return;
            ApplyRecoveryFloorAssets();
            AssetDatabase.SaveAssets();
            recoveryFloorReport.Insert(0, "PASS: measured controller recovery, including the entry blend.\n");
            File.WriteAllText(Review + "/RecoveryFloorStatus.txt", "COMPLETE");
        }
        catch (Exception error)
        {
            recoveryFloorReport.Insert(0, "FAIL " + error + "\n");
            File.WriteAllText(Review + "/RecoveryFloorStatus.txt", "FAIL " + error);
            Debug.LogException(error);
        }
        ((IDisposable)recoveryRepair).Dispose();
        recoveryRepair = null;
        EditorApplication.update -= AdvanceRecoveryRepair;
        File.WriteAllText(Review + "/RecoveryFloors.txt", recoveryFloorReport.ToString());
        EditorApplication.UnlockReloadAssemblies();
    }

    static IEnumerator BakeRecoveryFloors()
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
            foreach (var move in source.lightCombatMoves.Concat(source.heavyCombatMoves).Distinct()
                .Where(m => m.sourcePair.getUp || m.sourcePair.attackerGetUp))
            {
                string key = source.name + "_" + move.moveName;
                var grounding = PrepareRecoveryFloorAsset(move, key);
                recoveryAssets[key] = grounding;
                var target = fighters.Single(f => f != source);
                foreach (var entry in game.characterCatalog.characters)
                {
                    foreach (var fighter in fighters)
                    {
                        fighter.ResetCombat();
                        if (!fighter.GetComponent<BattleCharacterAppearance>().TryApply(entry.id))
                            throw new Exception("Cannot apply " + entry.id);
                    }
                    source.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    target.transform.position = Vector3.right * move.attackRange;
                    if (!source.ExecuteAttack(move, target))
                        throw new Exception("Recovery rejected " + entry.id + "/" + key);
                    var pair = source.SourcePlayback;
                    try
                    {
                        var clips = fighters.Select(f => f == source ? move.sourcePair.attackerGetUp
                            : (AnimationClip)receiverRecovery.GetValue(pair)).ToArray();
                        var tracks = fighters.Select((f, i) => clips[i] ? new FrankPairGrounding.Track
                        {
                            avatar = f.Animator.avatar,
                            receiver = f != source,
                            duration = clips[i].length,
                            lift = new float[Mathf.CeilToInt(clips[i].length * 240) + 1]
                        } : null).ToArray();
                        grounding.tracks = grounding.tracks.Where(t => t.avatar != source.Animator.avatar)
                            .Concat(tracks.Where(t => t != null)).ToArray();
                        pair.EvaluateAt(pair.Duration);
                        finishSource.Invoke(pair, null);
                        if (!pair.IsRecovering) throw new Exception("Expected controller recovery " + key);
                        BakeRecoveryController(pair, fighters, clips, tracks, entry.id + "/" + key);
                    }
                    finally
                    {
                        pair.Cancel();
                    }
                    EditorUtility.SetDirty(grounding);
                    File.WriteAllText(Review + "/RecoveryFloorStatus.txt", "RUNNING " + key + "/" + entry.id);
                    yield return null;
                }
            }
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    static FrankPairGrounding PrepareRecoveryFloorAsset(CombatTripletData move, string key)
    {
        string path = Root + "/RecoveryGrounding/" + key + ".asset";
        var result = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(path);
        if (!result)
        {
            result = move.sourcePair.recoveryGrounding ? Object.Instantiate(move.sourcePair.recoveryGrounding)
                : ScriptableObject.CreateInstance<FrankPairGrounding>();
            result.name = key + " recovery";
            result.tracks = Array.Empty<FrankPairGrounding.Track>();
            AssetDatabase.CreateAsset(result, path);
        }
        else BackupFallAsset(result);
        move.sourcePair.recoveryGrounding = result;
        return result;
    }

    static void BakeRecoveryController(FrankBattlePairPlayback pair, CharacterCombat[] fighters,
        AnimationClip[] clips, FrankPairGrounding.Track[] tracks, string label)
    {
        var probes = fighters.Select(f => new BattleRosterFloorProbe(f)).ToArray();
        for (int role = 0; role < fighters.Length; role++)
        {
            var track = tracks[role];
            if (track == null) continue;
            for (int frame = 0; frame < track.lift.Length; frame++)
            {
                float time = track.duration * frame / (track.lift.Length - 1);
                SampleRecoveryController(pair, fighters, clips, time);
                float blendSeconds = pair.Move.sourcePair.recoveryBlendSeconds;
                float weight = blendSeconds > 0 ? Mathf.SmoothStep(0, 1, time / blendSeconds) : 1;
                if (weight < .00001f) continue;
                float floor = probes[role].Minimum() - fighters[role].transform.position.y;
                track.lift[frame] += (.008f - floor) / weight;
                SampleRecoveryController(pair, fighters, clips, time);
                float corrected = probes[role].Minimum() - fighters[role].transform.position.y;
                if (Mathf.Abs(corrected - .008f) > .002f)
                    throw new Exception(label + " recovery calibration failed at " + time + ": " + corrected);
            }
            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            int intervals = Mathf.CeilToInt(track.duration * 120);
            for (int frame = 0; frame <= intervals; frame++)
            {
                float time = track.duration * frame / intervals;
                SampleRecoveryController(pair, fighters, clips, time);
                float floor = probes[role].Minimum() - fighters[role].transform.position.y;
                min = Mathf.Min(min, floor);
                max = Mathf.Max(max, floor);
                if (floor < -.015f || floor > .025f)
                    throw new Exception(label + " recovery floor=" + floor + " at " + time);
            }
            recoveryFloorReport.AppendLine(label + " role=" + role + " minimum=" + min + " maximum=" + max);
        }
    }

    static void SampleRecoveryController(FrankBattlePairPlayback pair, CharacterCombat[] fighters,
        AnimationClip[] clips, float time)
    {
        pair.PrepareRecoveryPoseEvaluation();
        for (int role = 0; role < fighters.Length; role++)
            if (clips[role])
            {
                float seconds = Mathf.Min(time, clips[role].length - .00001f);
                fighters[role].Animator.Play("Base Layer.GetUp", 0, seconds / clips[role].length);
                fighters[role].Animator.Update(0);
            }
        pair.EvaluateRecoveryPose();
    }

    static void ApplyRecoveryFloorAssets()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(BattlePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(BattlePath, OpenSceneMode.Additive);
        try
        {
            string backup = FallBackup + "/BattleScene.unity";
            Directory.CreateDirectory(FallBackup);
            if (!File.Exists(backup)) File.Copy(BattlePath, backup);
            var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
            foreach (var fighter in new[] { game.leftCombat, game.rightCombat })
            {
                foreach (var move in fighter.lightCombatMoves.Concat(fighter.heavyCombatMoves))
                    if (recoveryAssets.TryGetValue(fighter.name + "_" + move.moveName, out var grounding))
                        move.sourcePair.recoveryGrounding = grounding;
                EditorUtility.SetDirty(fighter);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }
}

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void RepairBattleRosterRecoveryFloors() => BattleRosterSetup.RepairRecoveryFloors();
    }
}
