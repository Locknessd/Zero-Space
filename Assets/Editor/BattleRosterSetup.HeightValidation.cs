using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

public static partial class BattleRosterSetup
{
    public static void ValidateHeights()
    {
        var scene = EditorSceneManager.OpenPreviewScene(BattlePath);
        var report = new StringBuilder();
        try
        {
            var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
            var fighters = new[] { game.leftCombat, game.rightCombat };
            foreach (var fighter in fighters)
            {
                fighter.battleSfx = null;
                fighter.battleVfx = null;
                fighter.hitEffect = null;
                fighter.Initialize();
            }
            foreach (var entry in game.characterCatalog.characters)
            {
                float previous = 0;
                foreach (var fighter in fighters)
                {
                    var appearance = fighter.GetComponent<BattleCharacterAppearance>();
                    if (!appearance.TryApply(entry.id))
                        throw new Exception("Cannot apply " + entry.id);
                    RestoreReferencePose(fighter.Animator);
                    float height = ReferenceBounds(fighter.Animator).size.y;
                    if (Mathf.Abs(height - game.characterCatalog.characterHeight) > .001f)
                        throw new Exception(entry.id + " height=" + height);
                    if (previous > 0 && Mathf.Abs(previous - height) > .0001f)
                        throw new Exception(entry.id + " differs between sides.");
                    previous = height;
                    report.AppendLine(entry.id + "/" + fighter.name + " height=" + height +
                        " scale=" + fighter.transform.localScale.x);
                }
                for (int repeat = 0; repeat < 5; repeat++)
                    foreach (var fighter in fighters)
                        fighter.GetComponent<BattleCharacterAppearance>().TryApply(entry.id);
                foreach (var fighter in fighters)
                    if (Mathf.Abs(ReferenceBounds(fighter.Animator).size.y - previous) > .0001f)
                        throw new Exception("Repeated selection accumulated scale: " + entry.id);
            }
            report.Insert(0, "PASS: six characters, identical height on both sides, repeated selections stable.\n");
        }
        catch (Exception error)
        {
            report.Insert(0, "FAIL: " + error + "\n");
            throw;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            File.WriteAllText(Review + "/HeightValidation.txt", report.ToString());
        }
    }

    public static void InspectSlapHeights(bool calibrate = false)
    {
        var scene = EditorSceneManager.OpenPreviewScene(BattlePath);
        var report = new StringBuilder();
        int samples = 0;
        float maxPalmError = 0;
        float maxSurfaceGap = 0;
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
                fighter.Initialize();
            }
            foreach (var entry in game.characterCatalog.characters)
            foreach (var fighter in fighters)
            foreach (var victim in calibrate ? new[] { entry } : game.characterCatalog.characters)
            {
                fighter.ResetCombat();
                fighter.GetComponent<BattleCharacterAppearance>().TryApply(entry.id);
                var other = fighters.Single(f => f != fighter);
                other.ResetCombat();
                other.GetComponent<BattleCharacterAppearance>().TryApply(victim.id);
                foreach (var move in fighter.lightCombatMoves.Where(m => m.moveName.StartsWith("SlapFace")))
                {
                    fighter.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    other.transform.position = Vector3.right * move.attackRange;
                    if (!fighter.ExecuteAttack(move, other))
                        throw new Exception("Cannot play " + move.moveName);
                    var pair = fighter.SourcePlayback;
                    try
                    {
                        if (calibrate)
                        {
                            BakeRosterContacts(pair, fighter, other, move, bank.FindMove(move));
                            if (move.actionDefinition)
                                UnityEditor.EditorUtility.SetDirty(move.actionDefinition);
                        }
                        foreach (var cue in bank.FindMove(move).cues.Where(c => c.group == "light_hit"))
                        {
                            pair.EvaluateAt(cue.seconds);
                            var probe = new BattlePresentationContactSetup.ContactProbe(pair, fighter, other,
                                cue.contactSource);
                            float gap = probe.MeasureSparse(out var point, out var bone, out _);
                            if (!cue.TryContactPosition(other.Animator, out var face))
                                throw new Exception("Missing slap face anchor: " + victim.id);
                            var palm = Vector3.Lerp(fighter.Animator.GetBoneTransform(HumanBodyBones.RightHand).position,
                                fighter.Animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal).position, .7f);
                            float palmError = Vector3.Distance(palm, face);
                            float faceGap = probe.SourceDistance(face);
                            maxPalmError = Mathf.Max(maxPalmError, palmError);
                            maxSurfaceGap = Mathf.Max(maxSurfaceGap, faceGap);
                            if (palmError > .005f || faceGap > .045f || probe.BodyDistance(face) > .002f)
                                throw new Exception("Slap contact mismatch: " + entry.id + " -> " + victim.id);
                            var wrist = fighter.Animator.GetBoneTransform(HumanBodyBones.RightHand);
                            var position = wrist.position;
                            pair.EvaluateAt(Mathf.Max(0, cue.seconds - .1f));
                            pair.EvaluateAt(cue.seconds);
                            if (Vector3.Distance(position, wrist.position) > .0001f)
                                throw new Exception("Slap contact depends on previous sample: " + entry.id);
                            samples++;
                            report.AppendLine(entry.id + "->" + victim.id + "/" + fighter.name + "/" + move.moveName +
                                " t=" + cue.seconds + " source=" + cue.contactSource + " gap=" + gap +
                                " bone=" + bone + " point=" + point + " palmError=" + palmError +
                                " faceGap=" + faceGap);
                        }
                    }
                    finally
                    {
                        pair.Cancel();
                    }
                }
            }
            if (calibrate)
            {
                UnityEditor.EditorUtility.SetDirty(bank);
                UnityEditor.AssetDatabase.SaveAssets();
            }
            report.Insert(0, "PASS: " + samples + " slap impacts; max palm error=" + maxPalmError +
                "m; max distance from face anchor to hand skin=" + maxSurfaceGap + "m.\n");
        }
        catch (Exception error)
        {
            report.Insert(0, "FAIL: " + error + "\n");
            throw;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            File.WriteAllText(Review + "/SlapHeights.txt", report.ToString());
        }
    }
}

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void ValidateBattleRosterHeights() => BattleRosterSetup.ValidateHeights();
        public static void InspectBattleRosterSlaps() => BattleRosterSetup.InspectSlapHeights();
        public static void CalibrateBattleRosterSlaps() => BattleRosterSetup.InspectSlapHeights(true);
    }
}
