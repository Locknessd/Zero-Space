using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static class CombatExpansionReactionTrackCheck
    {
        const string Hits = "Assets/Selected/FightingAnimsetPro/Animations/KB_Hits.fbx";

        public static void Validate()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            var scene = EditorSceneManager.OpenPreviewScene(CombatExpansionInventory.Battle);
            var positioning = CombatPositioningController.Instance;
            var property = typeof(CombatPositioningController).GetProperty("Instance");
            var log = new List<string> { "Isolated actual-avatar pose checks; not contact or gameplay approval." };
            var track = ScriptableObject.CreateInstance<FrankReactionTrack>();
            track.segments = new[]
            {
                new FrankReactionTrack.Segment
                {
                    strikeId = "continuity-probe-a", seconds = .4f, blendSeconds = .05f,
                    clip = Clip("KB_Hit_m_MidRight_Med")
                },
                new FrankReactionTrack.Segment
                {
                    strikeId = "continuity-probe-b", seconds = .95f, blendSeconds = .05f,
                    clip = Clip("KB_Hit_m_MidFront_Stagger"), terminal = true, lethalHoldSeconds = 1.2f
                }
            };
            try
            {
                property.SetValue(null, null);
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
                foreach (int direction in new[] { 1, -1 })
                foreach (bool lethal in new[] { false, true })
                {
                    foreach (var fighter in fighters)
                        fighter.ResetCombat();
                    var target = fighters.Single(f => f != source);
                    source.Animator.transform.position = Vector3.left * direction * .6f;
                    target.Animator.transform.position = Vector3.right * direction * .6f;
                    var move = CombatExpansionAxeContactStudy.MakeMove(source, target, 1.2f);
                    move.sourcePair.reactions = track;
                    move.sourcePair.reactionDelay = 0;
                    if (!source.ExecuteAttack(move, target, lethal))
                        throw new InvalidOperationException("Reaction probe failed to start.");
                    var pair = source.SourcePlayback;
                    try
                    {
                        Verify(pair, target, track, lethal, out float positionError, out float rotationError);
                        log.Add(FormattableString.Invariant(
                            $"PASS {source.name} direction={direction} lethal={lethal}: repeated/backward pose error={positionError:R}m/{rotationError:R}deg; contact continuity and precontact hold."));
                        log.Add(MeasureSampling(pair));
                    }
                    finally
                    {
                        pair.Cancel();
                    }
                    if (source.IsBusy || target.IsBusy || pair.Playing || pair.AttackerActor || pair.ReceiverActor)
                        throw new InvalidOperationException("Reaction probe retained actor ownership after cancellation.");
                }
                log.Add("PASS all 8 avatar/direction/outcome pose cases; no action registration implied.");
            }
            catch (Exception error)
            {
                log.Add("FAIL " + error);
                throw;
            }
            finally
            {
                File.WriteAllLines(CombatExpansionInventory.Output + "/ReactionTrackValidation.txt", log);
                Object.DestroyImmediate(track);
                EditorSceneManager.ClosePreviewScene(scene);
                property.SetValue(null, positioning);
            }
        }

        static void Verify(FrankBattlePairPlayback pair, CharacterCombat victim, FrankReactionTrack track,
            bool lethal, out float positionError, out float rotationError)
        {
            var bones = Enumerable.Range(0, (int)HumanBodyBones.LastBone)
                .Select(i => victim.Animator.GetBoneTransform((HumanBodyBones)i)).Where(t => t).ToArray();
            var times = new[] { 0f, .2f, .399f, .4f, .425f, .7f, .949f, .95f, .975f, 1.5f, 2.2f, 3.6f };
            var driver = pair.ReceiverActor.activeDriver;
            var poses = new List<(Vector3[] positions, Quaternion[] rotations)>();
            var diagnostic = new List<string> { "phase,time,sourceHips,sourceDriver,targetHips,targetLegLocal" };
            void Record(string phase, float seconds)
            {
                var pose = pair.ReceiverActor.Pose;
                var leg = victim.Animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                diagnostic.Add(phase + "," + seconds.ToString("R") + "," + pose.sourceHips.position.ToString("R") +
                    "," + pose.driver.transform.position.ToString("R") + "," + pose.targetHips.position.ToString("R") +
                    "," + leg.localPosition.ToString("R"));
            }
            foreach (float seconds in times)
            {
                pair.EvaluateAt(seconds);
                Record("forward", seconds);
                poses.Add((bones.Select(b => b.position).ToArray(), bones.Select(b => b.rotation).ToArray()));
            }
            Require(poses[0].positions.Zip(poses[1].positions, Vector3.Distance).Max() < .0001f,
                "Receiver moved before its first confirmed contact.");
            positionError = rotationError = 0;
            string worst = "";
            for (int i = times.Length - 1; i >= 0; i--)
            {
                pair.EvaluateAt(times[i]);
                Record("backward", times[i]);
                for (int bone = 0; bone < bones.Length; bone++)
                {
                    float distance = Vector3.Distance(bones[bone].position, poses[i].positions[bone]);
                    if (distance > positionError)
                        worst = bones[bone].name + " at " + times[i];
                    positionError = Mathf.Max(positionError, distance);
                    rotationError = Mathf.Max(rotationError, Quaternion.Angle(bones[bone].rotation, poses[i].rotations[bone]));
                }
            }
            File.WriteAllLines(CombatExpansionInventory.Output + "/ReactionSamplingDiagnostic.csv", diagnostic);
            Require(positionError < .0001f && rotationError < .1f,
                $"Reaction sampling depends on sampling order: {positionError:R}m/{rotationError:R}deg; {worst}.");
            foreach (var segment in track.segments)
            {
                pair.EvaluateAt(segment.seconds - .00001f);
                var before = bones.Select(b => b.position).ToArray();
                pair.EvaluateAt(segment.seconds + .00001f);
                Require(bones.Select((b, i) => Vector3.Distance(b.position, before[i])).Max() < .001f,
                    "Reaction contact introduced a pose discontinuity.");
            }
            if (lethal)
            {
                pair.EvaluateAt(2.2f);
                var held = bones.Select(b => b.position).ToArray();
                pair.EvaluateAt(3.6f);
                Require(bones.Select((b, i) => Vector3.Distance(b.position, held[i])).Max() < .0001f,
                    "Lethal terminal reaction advanced beyond its authored hold.");
            }
            Require(pair.ReceiverActor.activeDriver == driver,
                "Reaction sampling replaced the source actor or driver.");
        }

        static AnimationClip Clip(string name) => AssetDatabase.LoadAllAssetsAtPath(Hits)
            .OfType<AnimationClip>().Single(c => c.name == name);

        static string MeasureSampling(FrankBattlePairPlayback pair)
        {
            const int samples = 240;
            for (int i = 0; i < samples; i++)
                pair.EvaluateAt(i / 60f);
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long began = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int i = 0; i < samples; i++)
                pair.EvaluateAt(i / 60f);
            double milliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - began) * 1000d /
                System.Diagnostics.Stopwatch.Frequency / samples;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            return FormattableString.Invariant(
                $"Editor manual paired sampling: {samples} warmed samples, {milliseconds:F4}ms/sample, {allocated} managed bytes; excludes Play Mode feedback/rendering.");
        }

        static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
