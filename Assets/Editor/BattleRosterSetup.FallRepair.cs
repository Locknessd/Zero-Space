using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using FrankRetarget;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static partial class BattleRosterSetup
{
    static IEnumerator fallRepair;
    static bool refiningFalls;
    static readonly StringBuilder fallReport = new StringBuilder();
    const string FallBackup = Review + "/BeforeFallGrounding_20261015";

    public static void RepairFalls(bool refineOnly = false)
    {
        if (calibration != null || extras != null || fallRepair != null)
            throw new InvalidOperationException("A roster calibration is already running.");
        fallReport.Clear();
        refiningFalls = refineOnly;
        File.WriteAllText(Review + "/FallRepairStatus.txt", "RUNNING");
        EditorApplication.LockReloadAssemblies();
        fallRepair = RepairThrowFloors();
        EditorApplication.update += AdvanceFallRepair;
    }

    static void AdvanceFallRepair()
    {
        try
        {
            if (fallRepair.MoveNext()) return;
            AssetDatabase.SaveAssets();
            fallReport.Insert(0, "PASS: avatar floor curves replace legacy receiver offsets.\n");
            File.WriteAllText(Review + "/FallRepairStatus.txt", "COMPLETE");
        }
        catch (Exception error)
        {
            fallReport.Insert(0, "FAIL " + error + "\n");
            File.WriteAllText(Review + "/FallRepairStatus.txt", "FAIL " + error);
            Debug.LogException(error);
        }
        ((IDisposable)fallRepair).Dispose();
        fallRepair = null;
        EditorApplication.update -= AdvanceFallRepair;
        File.WriteAllText(Review + (refiningFalls ? "/FallRefinement.txt" : "/FallRepair.txt"), fallReport.ToString());
        EditorApplication.UnlockReloadAssemblies();
    }

    static IEnumerator RepairThrowFloors()
    {
        var scene = EditorSceneManager.OpenPreviewScene(BattlePath);
        try
        {
            var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
            var fighters = new[] { game.leftCombat, game.rightCombat };
            var bank = game.battleSfx.bank;
            game.battleSfx.enabled = false;
            BackupFallAsset(bank);
            foreach (var fighter in fighters)
            {
                fighter.battleSfx = game.battleSfx;
                fighter.battleVfx = null;
                fighter.hitEffect = null;
                fighter.Initialize();
            }
            foreach (var source in fighters)
            foreach (var move in source.lightCombatMoves.Concat(source.heavyCombatMoves).Distinct()
                .Where(m => m.sourcePair.receiverFloorLift?.Length > 0))
            {
                var target = fighters.Single(f => f != source);
                BackupFallAsset(move.grounding);
                BackupFallAsset(move.actionDefinition);
                BackupFallAsset(move.grapple);
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
                        throw new Exception("Cannot repair " + entry.id + "/" + move.moveName);
                    var pair = source.SourcePlayback;
                    try
                    {
                        var probe = new BattleRosterFloorProbe(target);
                        pair.EvaluateAt(pair.Duration);
                        float before = probe.Minimum() - target.transform.position.y;
                        if (!refiningFalls) BakeRosterFloor(pair, fighters, source, move.grounding);
                        VerifyThrowFloor(pair, fighters, source, entry.id, before);
                        BakeRosterContacts(pair, source, target, move, bank.FindMove(move));
                    }
                    finally
                    {
                        pair.Cancel();
                    }
                    File.WriteAllText(Review + "/FallRepairStatus.txt",
                        "RUNNING " + source.name + "/" + move.moveName + "/" + entry.id);
                    yield return null;
                }
                EditorUtility.SetDirty(move.grounding);
                if (move.actionDefinition) EditorUtility.SetDirty(move.actionDefinition);
                if (move.grapple) EditorUtility.SetDirty(move.grapple);
            }
            EditorUtility.SetDirty(bank);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    static void VerifyThrowFloor(FrankBattlePairPlayback pair, CharacterCombat[] fighters,
        CharacterCombat source, string id, float before)
    {
        var probes = fighters.Select(f => new BattleRosterFloorProbe(f)).ToArray();
        var target = fighters.Single(f => f != source);
        var roots = fighters.Select(f => f.transform.position).ToArray();
        int intervals = Mathf.CeilToInt(pair.Duration * 480);
        float minimum = float.PositiveInfinity;
        float maxGrip = 0;
        for (int frame = 0; frame <= intervals; frame++)
        {
            float time = pair.Duration * frame / intervals;
            pair.EvaluateAt(time);
            for (int role = 0; role < fighters.Length; role++)
            {
                float floor = probes[role].Minimum() - fighters[role].transform.position.y;
                if (floor < .002f)
                {
                    var track = pair.ActiveGrounding.tracks.Single(t =>
                        t.avatar == fighters[role].Animator.avatar && t.receiver == (fighters[role] != source));
                    float frameIndex = time / track.duration * (track.lift.Length - 1);
                    int key = Mathf.FloorToInt(frameIndex);
                    float required = track.At(time) + .004f - floor;
                    track.lift[key] = Mathf.Max(track.lift[key], required);
                    int next = Mathf.Min(key + 1, track.lift.Length - 1);
                    track.lift[next] = Mathf.Max(track.lift[next], required);
                    pair.EvaluateAt(time);
                    floor = probes[role].Minimum() - fighters[role].transform.position.y;
                }
                minimum = Mathf.Min(minimum, floor);
                if (floor < -.001f)
                    throw new Exception(id + "/" + pair.Move.moveName + " penetration=" + floor + " at " + time);
                if (fighters[role].transform.position != roots[role])
                    throw new Exception("Grounding moved the fighter root.");
            }
            if (time > pair.Move.sourcePair.entryBlendSeconds + .01f)
                foreach (var limb in pair.AttackerActor.Pose.limbs.Where(l => l.alignGrip))
                    maxGrip = Mathf.Max(maxGrip, Vector3.Distance(limb.TargetGrip, limb.SourceGrip));
        }
        float after = probes[Array.IndexOf(fighters, target)].Minimum() - target.transform.position.y;
        if (after < -.005f || after > .015f || maxGrip > .04f)
            throw new Exception(id + " terminal gap=" + after + ", grip=" + maxGrip);
        pair.EvaluateAt(0);
        pair.EvaluateAt(pair.Duration);
        pair.EvaluateAt(pair.Duration);
        float repeated = probes[Array.IndexOf(fighters, target)].Minimum() - target.transform.position.y;
        if (Mathf.Abs(after - repeated) > .0001f)
            throw new Exception("Grounding accumulated on repeat/backward seek.");
        fallReport.AppendLine(id + "/" + source.name + "/" + pair.Move.moveName +
            " terminalBefore=" + before + "m; terminalAfter=" + after + "m; minimum=" + minimum +
            "m; maximumGrip=" + maxGrip + "m; samples=" + (intervals + 1));
    }

    static void BackupFallAsset(UnityEngine.Object asset)
    {
        if (!asset) return;
        string path = AssetDatabase.GetAssetPath(asset);
        if (string.IsNullOrEmpty(path)) return;
        string backup = FallBackup + "/" + path;
        if (File.Exists(backup)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(backup));
        File.Copy(path, backup);
    }
}

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void RepairBattleRosterFalls() => BattleRosterSetup.RepairFalls();
        public static void RefineBattleRosterThrowFloors() => BattleRosterSetup.RepairFalls(true);
    }
}
