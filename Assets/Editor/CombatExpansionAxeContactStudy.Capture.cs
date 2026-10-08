using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionAxeContactStudy
    {
        static readonly float[] CaptureTimes =
        {
            .4f, 28 / 60f, .5f, .6f, .8f, .95f, 1.03f, 64 / 60f, 1.1f, 1.3f, 1.8f, 2.5f
        };

        public static void CaptureReactionCandidate()
        {
            CaptureCandidate("ReactionCandidate", 7400050, .8f, 1.2f);
        }

        public static void CaptureFrontalCandidate()
        {
            CaptureCandidate("FrontalCandidate", 7400044, 1f, 1.35f);
        }

        static void CaptureCandidate(string label, long opening, float mankeyRange, float pepeRange)
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            Directory.CreateDirectory(Output);
            var positioning = CombatPositioningController.Instance;
            var property = typeof(CombatPositioningController).GetProperty("Instance");
            var scene = EditorSceneManager.OpenPreviewScene(CombatExpansionInventory.Battle);
            var texture = RenderTexture.GetTemporary(480, 320, 24);
            var stamp = new Texture2D(480, 320, TextureFormat.RGB24, false);
            var sheet = new Texture2D(1920, 960, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var track = ScriptableObject.CreateInstance<FrankReactionTrack>();
            track.segments = new[]
            {
                new FrankReactionTrack.Segment
                {
                    strikeId = "candidate-lift", seconds = 28 / 60f, blendSeconds = .05f,
                    clip = Clip(HitGuid, opening)
                },
                new FrankReactionTrack.Segment
                {
                    strikeId = "candidate-chop", seconds = 64 / 60f, blendSeconds = .05f,
                    clip = Clip(HitGuid, 7400064), terminal = true
                }
            };
            var rows = new List<string>
            {
                "fighter,direction,seconds,hand,gap,region,contactX,contactY,contactZ,offsetX,offsetY,offsetZ"
            };
            var paths = new List<string> { "fighter,direction,seconds,attackerX,attackerY,attackerZ,victimX,victimY,victimZ" };
            try
            {
                property.SetValue(null, null);
                var roots = scene.GetRootGameObjects();
                var game = roots.SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var camera = roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true))
                    .First(c => c.CompareTag("MainCamera"));
                var framing = roots.SelectMany(r => r.GetComponentsInChildren<FrankCinematicCamera>(true)).First();
                camera.scene = scene;
                camera.targetTexture = texture;
                camera.aspect = 1.5f;
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var fighter in fighters)
                {
                    fighter.battleSfx = null;
                    fighter.battleVfx = null;
                    fighter.hitEffect = null;
                    fighter.Initialize();
                }
                foreach (var source in fighters)
                foreach (int direction in new[] { 1, -1 })
                {
                    foreach (var fighter in fighters)
                        fighter.ResetCombat();
                    var target = fighters.Single(f => f != source);
                    float range = source.name == "Mankey" ? mankeyRange : pepeRange;
                    source.Animator.transform.position = Vector3.left * direction * range * .5f;
                    target.Animator.transform.position = Vector3.right * direction * range * .5f;
                    var move = MakeMove(source, target, range);
                    move.sourcePair.reactions = track;
                    move.sourcePair.reactionDelay = 0;
                    if (!source.ExecuteAttack(move, target))
                        throw new InvalidOperationException("Axe reaction candidate could not start.");
                    var pair = source.SourcePlayback;
                    try
                    {
                        pair.ReceiverActor.Pose.transferFingers = true;
                        foreach (float seconds in new[] { 28 / 60f, 29 / 60f, .5f, 63 / 60f, 64 / 60f, 1.1f })
                        {
                            pair.EvaluateAt(seconds);
                            var probe = new BattlePresentationContactSetup.AxeContactProbe(target);
                            foreach (bool left in new[] { true, false })
                            {
                                float gap = probe.Measure(pair.AttackerActor, left, out var point,
                                    out var bone, out var offset, out _);
                                string hand = left ? "Left" : "Right";
                                rows.Add(FormattableString.Invariant(
                                    $"{source.name},{direction},{seconds:R},{hand},{gap:R},{bone},{point.x:R},{point.y:R},{point.z:R},{offset.x:R},{offset.y:R},{offset.z:R}"));
                            }
                        }
                        using var skin = new CombatExpansionPreviewSkin(fighters.Select(f => f.gameObject).ToArray());
                        for (int frame = 0; frame < CaptureTimes.Length; frame++)
                        {
                            pair.EvaluateAt(CaptureTimes[frame]);
                            var a = pair.AttackerActor.Pose.targetHips.position;
                            var b = pair.ReceiverActor.Pose.targetHips.position;
                            paths.Add(FormattableString.Invariant(
                                $"{source.name},{direction},{CaptureTimes[frame]:R},{a.x:R},{a.y:R},{a.z:R},{b.x:R},{b.y:R},{b.z:R}"));
                            skin.Sample();
                            framing.Apply(0, true);
                            camera.Render();
                            RenderTexture.active = texture;
                            stamp.ReadPixels(new Rect(0, 0, 480, 320), 0, 0);
                            stamp.Apply();
                            sheet.SetPixels(frame % 4 * 480, (2 - frame / 4) * 320, 480, 320, stamp.GetPixels());
                        }
                        sheet.Apply();
                        File.WriteAllBytes(Output + $"/{source.name}_{direction}_{label}.png",
                            sheet.EncodeToPNG());
                    }
                    finally
                    {
                        pair.Cancel();
                    }
                }
                File.WriteAllLines(Output + "/" + label + ".csv", rows);
                File.WriteAllLines(Output + "/" + label + "Paths.csv", paths);
                File.WriteAllLines(Output + "/" + label + ".txt", new[]
                {
                    "Candidate choreography only; not registered, confirmed contacts, or Play Mode approval.",
                    "Rows left to right, top to bottom; seconds: " + string.Join(", ", CaptureTimes),
                    $"Opening clip {opening} at 28/60s; MidFront_Stagger 7400064 at 64/60s.",
                    $"Mankey range {mankeyRange}m; Pepe range {pepeRange}m. Both directions. Feedback omitted.",
                    "The standing stagger is not a lethal/downed reaction. Candidate supports normal study only."
                });
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(track);
                Object.DestroyImmediate(stamp);
                Object.DestroyImmediate(sheet);
                RenderTexture.ReleaseTemporary(texture);
                EditorSceneManager.ClosePreviewScene(scene);
                property.SetValue(null, positioning);
            }
        }
    }
}
