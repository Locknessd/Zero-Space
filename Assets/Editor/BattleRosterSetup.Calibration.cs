using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FrankRetarget;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class BattleRosterSetup
{
    static IEnumerator calibration;
    static bool contactsOnly;
    static bool floorsOnly;
    static readonly StringBuilder calibrationReport = new StringBuilder();
    static readonly Dictionary<string, FrankPairGrounding> calibratedGrounding =
        new Dictionary<string, FrankPairGrounding>();

    public static void StartCalibration(bool refineContactsOnly = false, bool floorCorrectionsOnly = false)
    {
        if (calibration != null)
            throw new InvalidOperationException("Roster calibration is already running.");
        contactsOnly = refineContactsOnly;
        floorsOnly = floorCorrectionsOnly;
        Directory.CreateDirectory(Root + "/Grounding");
        AssetDatabase.Refresh();
        calibrationReport.Clear();
        calibratedGrounding.Clear();
        File.WriteAllText(Review + "/CalibrationStatus.txt", "RUNNING");
        EditorApplication.LockReloadAssemblies();
        calibration = Calibrate();
        EditorApplication.update += AdvanceCalibration;
    }

    static void AdvanceCalibration()
    {
        try
        {
            if (calibration.MoveNext())
                return;
            ApplyCalibratedGrounding();
            AssetDatabase.SaveAssets();
            File.WriteAllText(Review + "/CalibrationStatus.txt", "COMPLETE");
        }
        catch (Exception error)
        {
            calibrationReport.AppendLine(error.ToString());
            File.WriteAllText(Review + "/CalibrationStatus.txt", "FAIL " + error);
            Debug.LogException(error);
        }
        ((IDisposable)calibration).Dispose();
        calibration = null;
        EditorApplication.update -= AdvanceCalibration;
        File.WriteAllText(Review + (floorsOnly ? "/FloorCalibration.txt" : "/Calibration.txt"),
            calibrationReport.ToString());
        EditorApplication.UnlockReloadAssemblies();
    }

    static IEnumerator Calibrate()
    {
        var scene = EditorSceneManager.OpenPreviewScene(BattlePath);
        try
        {
            var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
            var bank = game.battleSfx.bank;
            game.battleSfx.enabled = false;
            BackupAsset(bank);
            var fighters = new[] { game.leftCombat, game.rightCombat };
            foreach (var fighter in fighters)
            {
                fighter.battleSfx = game.battleSfx;
                fighter.battleVfx = null;
                fighter.hitEffect = null;
                fighter.Initialize();
            }
            var avatars = game.characterCatalog.characters
                .Select(e => e.prefab.GetComponent<Animator>().avatar).ToArray();
            foreach (var source in fighters)
            foreach (var move in source.lightCombatMoves.Concat(source.heavyCombatMoves).Distinct())
            {
                var target = fighters.Single(f => f != source);
                if (move.actionDefinition)
                    BackupAsset(move.actionDefinition);
                if (move.grapple)
                    BackupAsset(move.grapple);
                string key = source.name + "_" + move.moveName;
                var grounding = contactsOnly ? move.grounding : PrepareGrounding(move, key, avatars);
                calibratedGrounding[key] = grounding;
                foreach (var entry in game.characterCatalog.characters)
                {
                    foreach (var fighter in fighters)
                    {
                        fighter.ResetCombat();
                        if (!fighter.GetComponent<BattleCharacterAppearance>().TryApply(entry.id))
                            throw new Exception("Cannot calibrate appearance " + entry.id);
                    }
                    source.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    target.transform.position = Vector3.right * move.attackRange;
                    if (!source.ExecuteAttack(move, target))
                        throw new Exception("Cannot calibrate " + key);
                    var pair = source.SourcePlayback;
                    try
                    {
                        if (!contactsOnly)
                            BakeRosterFloor(pair, fighters, source, grounding);
                        var profile = bank.FindMove(move);
                        if (!floorsOnly)
                            BakeRosterContacts(pair, source, target, move, profile);
                        calibrationReport.AppendLine(key + "/" + entry.id + " duration=" + pair.Duration);
                    }
                    finally
                    {
                        pair.Cancel();
                    }
                    File.WriteAllText(Review + "/CalibrationStatus.txt", "RUNNING " + key + "/" + entry.id);
                    yield return null;
                }
                EditorUtility.SetDirty(grounding);
                if (move.actionDefinition)
                {
                    BackupAsset(move.actionDefinition);
                    EditorUtility.SetDirty(move.actionDefinition);
                }
                if (move.grapple)
                {
                    BackupAsset(move.grapple);
                    EditorUtility.SetDirty(move.grapple);
                }
            }
            EditorUtility.SetDirty(bank);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    static FrankPairGrounding PrepareGrounding(CombatTripletData move, string key, Avatar[] avatars)
    {
        // Role and avatar alone cannot distinguish two differently calibrated source pairs.
        // Each fighter/move owns its curves, including when the original asset was shared.
        string path = Root + "/Grounding/" + key + ".asset";
        var grounding = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(path);
        if (!grounding)
        {
            grounding = move.grounding ? Object.Instantiate(move.grounding)
                : ScriptableObject.CreateInstance<FrankPairGrounding>();
            grounding.name = key;
            AssetDatabase.CreateAsset(grounding, path);
        }
        else
            BackupAsset(grounding);
        move.grounding = grounding;
        grounding.tracks = grounding.tracks.Where(t => !avatars.Contains(t.avatar)).ToArray();
        return grounding;
    }

    static void BakeRosterFloor(FrankBattlePairPlayback pair, CharacterCombat[] fighters,
        CharacterCombat source, FrankPairGrounding grounding)
    {
        bool replaceLegacy = pair.Move.sourcePair.receiverFloorLift?.Length > 0;
        int sampleRate = replaceLegacy ? 240 : 60;
        int intervals = Mathf.Max(1, Mathf.CeilToInt(pair.Duration * sampleRate));
        var lifts = new[] { new float[intervals + 1], new float[intervals + 1] };
        var probes = fighters.Select(f => new BattleRosterFloorProbe(f)).ToArray();
        foreach (var fighter in fighters)
        {
            bool receiver = fighter != source;
            grounding.tracks = grounding.tracks.Where(t =>
                t.avatar != fighter.Animator.avatar || t.receiver != receiver).Concat(new[]
            {
                new FrankPairGrounding.Track
                {
                    avatar = fighter.Animator.avatar,
                    receiver = receiver,
                    duration = pair.Duration,
                    lift = new float[2],
                    replacesReceiverFloorLift = receiver && replaceLegacy
                }
            }).ToArray();
        }
        for (int i = 0; i <= intervals; i++)
        {
            pair.EvaluateAt(pair.Duration * i / intervals);
            for (int role = 0; role < 2; role++)
            {
                float floor = probes[role].Minimum();
                if (i == 0)
                {
                    float reference = BattlePresentationContactSetup.EvaluatedFloor(fighters[role]);
                    if (Mathf.Abs(reference - floor) > .001f)
                        throw new Exception("Cached floor differs from evaluated skin geometry.");
                }
                lifts[role][i] = Mathf.Max(0, fighters[role].transform.position.y - floor + .008f);
            }
        }
        for (int role = 0; role < 2; role++)
        {
            var raw = lifts[role];
            var envelope = raw;
            if (!replaceLegacy)
            {
                envelope = new float[raw.Length];
                for (int i = 0; i < raw.Length; i++)
                    for (int j = Mathf.Max(0, i - 1); j <= Mathf.Min(i + 1, raw.Length - 1); j++)
                        envelope[i] = Mathf.Max(envelope[i], raw[j]);
            }
            var track = new FrankPairGrounding.Track
            {
                avatar = fighters[role].Animator.avatar,
                receiver = fighters[role] != source,
                duration = pair.Duration,
                lift = envelope,
                replacesReceiverFloorLift = fighters[role] != source && replaceLegacy
            };
            grounding.tracks = grounding.tracks.Where(t => t.avatar != track.avatar || t.receiver != track.receiver)
                .Concat(new[] { track }).ToArray();
        }
    }

    static void BackupAsset(Object asset)
    {
        string path = AssetDatabase.GetAssetPath(asset);
        if (string.IsNullOrEmpty(path))
            return;
        string backup = Review + "/BeforeCalibration/" + path;
        if (File.Exists(backup))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(backup));
        File.Copy(path, backup);
    }

    static void ApplyCalibratedGrounding()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(BattlePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened)
            scene = EditorSceneManager.OpenScene(BattlePath, OpenSceneMode.Additive);
        try
        {
            var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
            foreach (var fighter in new[] { game.leftCombat, game.rightCombat })
                foreach (var move in fighter.lightCombatMoves.Concat(fighter.heavyCombatMoves))
                    move.grounding = calibratedGrounding[fighter.name + "_" + move.moveName];
            EditorUtility.SetDirty(game.leftCombat);
            EditorUtility.SetDirty(game.rightCombat);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (opened)
                EditorSceneManager.CloseScene(scene, true);
        }
    }
}

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void CalibrateBattleRoster() => BattleRosterSetup.StartCalibration();
        public static void RefineBattleRosterContacts() => BattleRosterSetup.StartCalibration(true);
        public static void CalibrateBattleRosterFloors() => BattleRosterSetup.StartCalibration(false, true);
    }
}
