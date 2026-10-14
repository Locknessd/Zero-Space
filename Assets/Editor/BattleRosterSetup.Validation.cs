using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class BattleRosterSetup
{
    [MenuItem("Tools/Battle/Characters/Validate all characters and moves")]
    public static void Validate()
    {
        Directory.CreateDirectory(Review);
        var report = new StringBuilder();
        var scene = EditorSceneManager.OpenPreviewScene(BattlePath);
        int sequences = 0;
        int poses = 0;
        float maxGrip = 0;
        float minimumFloor = float.PositiveInfinity;
        try
        {
            var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
            var fighters = new[] { game.leftCombat, game.rightCombat };
            var bank = game.battleSfx.bank;
            game.battleSfx.enabled = false;
            foreach (var fighter in fighters)
            {
                fighter.battleSfx = game.battleSfx;
                fighter.battleVfx = null;
                fighter.hitEffect = null;
                if (!fighter.Initialize())
                    throw new Exception("Cannot initialize " + fighter.name);
            }
            foreach (var entry in game.characterCatalog.characters)
            {
                foreach (var fighter in fighters)
                {
                    fighter.ResetCombat();
                    if (!fighter.GetComponent<BattleCharacterAppearance>().TryApply(entry.id))
                        throw new Exception("Cannot swap " + entry.id);
                    var animator = fighter.Animator;
                    if (!animator.isHuman || !animator.avatar.isValid)
                        throw new Exception("Invalid avatar " + entry.id);
                    int fingers = 0;
                    foreach (var mapping in animator.avatar.humanDescription.human)
                        if (mapping.humanName.Contains("Proximal") || mapping.humanName.Contains("Intermediate") ||
                            mapping.humanName.Contains("Distal"))
                            fingers++;
                    if (Models.Any(id => id.ToLowerInvariant() == entry.id) && fingers != 30)
                        throw new Exception("Incomplete finger mapping " + entry.id);
                    foreach (var material in fighter.GetComponentsInChildren<Renderer>()
                        .Where(r => r.enabled).SelectMany(r => r.sharedMaterials))
                        if (!material || !material.shader || !material.shader.isSupported ||
                            ShaderUtil.ShaderHasError(material.shader))
                            throw new Exception("Invalid character material " + entry.id);
                }
                foreach (var source in fighters)
                {
                    var target = fighters.Single(f => f != source);
                    foreach (var move in source.lightCombatMoves.Concat(source.heavyCombatMoves).Distinct())
                    {
                        foreach (var fighter in fighters)
                            fighter.ResetCombat();
                        source.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                        target.transform.position = Vector3.right * move.attackRange;
                        if (!source.ExecuteAttack(move, target))
                            throw new Exception(entry.id + " rejected " + move.moveName);
                        var pair = source.SourcePlayback;
                        try
                        {
                            if (!pair || !pair.Playing)
                                throw new Exception("Expected source playback for " + move.moveName);
                            var profile = bank.FindMove(move);
                            var times = Enumerable.Range(0, 33).Select(i => pair.Duration * i / 33f)
                                .Concat(profile?.cues.Select(c => c.seconds) ?? Array.Empty<float>()).Distinct().OrderBy(t => t);
                            float actionGrip = 0;
                            var floorProbes = fighters.Select(f => new BattleRosterFloorProbe(f)).ToArray();
                            int sampleIndex = 0;
                            foreach (float time in times)
                            {
                                pair.EvaluateAt(time);
                                if (sampleIndex++ % 4 == 0)
                                    for (int role = 0; role < fighters.Length; role++)
                                    {
                                        float floor = floorProbes[role].Minimum() - fighters[role].transform.position.y;
                                        minimumFloor = Mathf.Min(minimumFloor, floor);
                                        if (floor < -.015f)
                                            throw new Exception(entry.id + "/" + move.moveName +
                                                " floor=" + floor + "m at " + time + "s, role=" + role);
                                    }
                                foreach (var actor in new[] { pair.AttackerActor, pair.ReceiverActor })
                                {
                                    foreach (var limb in actor.Pose.limbs)
                                    {
                                        if (!float.IsFinite(limb.end.position.sqrMagnitude))
                                            throw new Exception("Nonfinite limb in " + move.moveName);
                                        if (limb.alignGrip && time > move.sourcePair.entryBlendSeconds + .01f)
                                            actionGrip = Mathf.Max(actionGrip, Vector3.Distance(limb.TargetGrip, limb.SourceGrip));
                                    }
                                }
                                poses++;
                            }
                            maxGrip = Mathf.Max(maxGrip, actionGrip);
                            if (actionGrip > .04f)
                                throw new Exception(entry.id + "/" + move.moveName + " weapon grip gap=" + actionGrip);
                            report.AppendLine(entry.id + "/" + source.name + "/" + move.moveName +
                                " duration=" + pair.Duration + " maxGrip=" + actionGrip);
                            sequences++;
                        }
                        finally
                        {
                            pair?.Cancel();
                        }
                    }
                }
            }
            report.Insert(0, "PASS: avatars, materials, all current light/heavy pairs in both roles.\n" +
                "Sequences=" + sequences + "; poses=" + poses + "; maximum grip gap=" + maxGrip +
                "m; minimum skin height above floor=" + minimumFloor + "m.\n");
        }
        catch (Exception error)
        {
            report.Insert(0, "FAIL: " + error + "\n");
            throw;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            File.WriteAllText(Review + "/Validation.txt", report.ToString());
        }
    }
}

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void ValidateBattleRoster() => BattleRosterSetup.Validate();
    }
}
