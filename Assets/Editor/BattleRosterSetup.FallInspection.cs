using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using FrankRetarget;
using UnityEditor.SceneManagement;
using UnityEngine;

public static partial class BattleRosterSetup
{
    public static void InspectFalls()
    {
        var scene = EditorSceneManager.OpenPreviewScene(BattlePath);
        var report = new StringBuilder("character,side,move,time,duration,floor,hips,torsoUp,legacyLift,groundCue,lethal\n");
        var recoveryReport = new StringBuilder("character,side,move,time,floor,torsoUp\n");
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
            foreach (var entry in game.characterCatalog.characters.Skip(2))
            foreach (var source in fighters)
            {
                foreach (var fighter in fighters)
                {
                    fighter.ResetCombat();
                    fighter.GetComponent<BattleCharacterAppearance>().TryApply(entry.id);
                }
                var target = fighters.Single(f => f != source);
                var probe = new BattleRosterFloorProbe(target);
                foreach (var move in source.lightCombatMoves.Concat(source.heavyCombatMoves).Distinct())
                foreach (bool lethal in new[] { false, true })
                {
                    foreach (var fighter in fighters)
                        fighter.ResetCombat();
                    source.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    target.transform.position = Vector3.right * move.attackRange;
                    if (!source.ExecuteAttack(move, target, lethal))
                        throw new Exception("Fall inspection rejected " + move.moveName);
                    var pair = source.SourcePlayback;
                    try
                    {
                        var profile = pair.UsesLethalReceiverVariant
                            ? move.actionDefinition.lethalPairVariant.presentationProfile : game.battleSfx.bank.FindMove(move);
                        var landings = profile?.cues
                            .Where(c => c.group == "body_fall" || c.group == "knockout_fall")
                            .Select(c => c.seconds).ToArray() ?? Array.Empty<float>();
                        var times = Enumerable.Range(0, 13).Select(i => pair.Duration * i / 12)
                            .Concat(landings.SelectMany(t => new[] { t, t + .1f, t + .3f }))
                            .Select(t => Mathf.Clamp(t, 0, pair.Duration)).Distinct().OrderBy(t => t);
                        foreach (float time in times)
                        {
                            pair.EvaluateAt(time);
                            var hips = target.Animator.GetBoneTransform(HumanBodyBones.Hips);
                            var head = target.Animator.GetBoneTransform(HumanBodyBones.Head);
                            float legacy = LegacyLift(move.sourcePair, time);
                            report.AppendLine(string.Join(",", entry.id, source.name, move.moveName,
                                time, pair.Duration, probe.Minimum() - target.transform.position.y,
                                hips.position.y - target.transform.position.y,
                                (head.position - hips.position).normalized.y, legacy,
                                landings.Length == 0 ? -1 : landings.Last(), lethal));
                        }
                        if (!lethal && source == fighters[1] &&
                            (move.moveName == "Heavy_5" || move.moveName == "Light_SIHO"))
                            CaptureFallPose(scene, target, entry.id + "_" + move.moveName);
                        if (!lethal && (move.sourcePair.getUp || move.sourcePair.attackerGetUp))
                            InspectFallRecovery(pair, target, probe, entry.id, source.name, recoveryReport);
                        if (lethal)
                        {
                            float before = probe.Minimum();
                            finishSource.Invoke(pair, null);
                            if (Mathf.Abs(probe.Minimum() - before) > .0001f)
                                throw new Exception("Defeated receiver lost its grounded terminal pose.");
                        }
                    }
                    finally
                    {
                        pair.Cancel();
                    }
                }
            }
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            File.WriteAllText(Review + "/FallInspection.csv", report.ToString());
            File.WriteAllText(Review + "/FallRecoveryInspection.csv", recoveryReport.ToString());
        }
    }

    static float LegacyLift(FrankBattlePair pair, float time)
    {
        var samples = pair.receiverFloorLift;
        if (samples == null || samples.Length == 0)
            return 0;
        float frame = Mathf.Min(time * FrankBattlePair.ReceiverFloorSamplesPerSecond, samples.Length - 1);
        int index = (int)frame;
        return Mathf.Lerp(samples[index], samples[Mathf.Min(index + 1, samples.Length - 1)], frame - index);
    }

    static void CaptureFallPose(UnityEngine.SceneManagement.Scene scene, CharacterCombat target, string name)
    {
        string folder = Review + "/FallPreviews";
        Directory.CreateDirectory(folder);
        var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>())
            .Single(c => c.CompareTag("MainCamera"));
        var bounds = BattlePresentationContactSetup.EvaluatedBounds(target.Animator);
        Vector3 focus = bounds.center;
        focus.y = Mathf.Max(.25f, focus.y);
        Vector3 position = focus + new Vector3(0, .7f, -3.2f);
        camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position));
        camera.scene = scene;
        foreach (var canvas in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)))
            canvas.enabled = false;
        var render = new RenderTexture(1280, 720, 24);
        var previous = RenderTexture.active;
        var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = render;
            camera.Render();
            RenderTexture.active = render;
            pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(folder + "/" + name + ".png", pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(pixels);
            UnityEngine.Object.DestroyImmediate(render);
        }
    }

    static void InspectFallRecovery(FrankBattlePairPlayback pair, CharacterCombat target,
        BattleRosterFloorProbe probe, string id, string side, StringBuilder report)
    {
        typeof(FrankBattlePairPlayback).GetMethod("CompleteSourceMotion", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(pair, null);
        for (int frame = 0; frame <= 30 && pair.IsRecovering; frame++)
        {
            if (frame > 0)
            {
                pair.PrepareRecoveryPoseEvaluation();
                target.Animator.Update(1f / 60);
                pair.EvaluateRecoveryPose();
            }
            var hips = target.Animator.GetBoneTransform(HumanBodyBones.Hips);
            var head = target.Animator.GetBoneTransform(HumanBodyBones.Head);
            report.AppendLine(string.Join(",", id, side, pair.Move.moveName, frame / 60f,
                probe.Minimum() - target.transform.position.y, (head.position - hips.position).normalized.y));
        }
    }
}

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void InspectBattleRosterFalls() => BattleRosterSetup.InspectFalls();
    }
}
