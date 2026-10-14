using System;
using System.IO;
using System.Linq;
using System.Text;
using FrankRetarget;
using UnityEditor;
using UnityEngine;
using ZeroSpace.Showcase;

namespace FrankRetarget.Editor
{
    public static partial class PepeShowcaseCapture
    {
        [MenuItem("Tools/Pepe Showcase/Validate Assassin long combo")]
        public static void ValidateAssassinCombo()
        {
            var report = new StringBuilder();
            using (var session = new PepeShowcaseSession(PepeShowcaseSetup.ComboScenePath))
            {
                var showcase = session.showcase;
                var take = showcase.library.takes.Single();
                if (!take.attackTrack || !take.attackTrack.TryValidate(out _))
                    throw new InvalidOperationException("Invalid combo attack track.");
                if (take.attackTrack.steps.Length != 5 || take.cues.Count(cue => cue.swing) != 31)
                    throw new InvalidOperationException("Combo must retain five segments and all 31 native swings.");
                var floor = showcase.gameObject.scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true))
                    .Where(renderer => renderer.name.IndexOf("floor tile", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(renderer => renderer.bounds).ToArray();
                if (floor.Length == 0) throw new InvalidOperationException("Combo floor is missing.");
                for (int character = 0; character < 3; character++)
                {
                    showcase.customCharacterPrefab = showcase.library.mankeyPrefab;
                    showcase.SetCharacter(character);
                    var actor = showcase.Actor;
                    int samples = Mathf.CeilToInt(take.Duration * 60);
                    for (int sample = 0; sample <= samples; sample++)
                    {
                        SampleCombo(showcase, Mathf.Min(sample / 60f, take.Duration));
                        if (showcase.Actor != actor)
                            throw new InvalidOperationException("Combo recreated its driver between attacks.");
                        CheckPose(showcase);
                        foreach (var bone in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                        {
                            var foot = showcase.character.GetBoneTransform(bone).position;
                            bool supported = floor.Any(tile => foot.x >= tile.min.x && foot.x <= tile.max.x &&
                                foot.z >= tile.min.z && foot.z <= tile.max.z);
                            if (!supported) throw new InvalidOperationException("Combo leaves the stage at " + foot);
                        }
                    }
                    var baked = new Mesh();
                    try
                    {
                        foreach (var cue in take.cues.Where(cue => cue.swing))
                        {
                            SampleCombo(showcase, cue.seconds + 1f / 60);
                            bool visible = actor.Pose.weaponRenderers
                                .Where(renderer => PepeShowcaseSetup.IsNativeEffect(renderer.name))
                                .Any(renderer => PepeShowcaseSetup.NativeEffectVisible(renderer, baked));
                            if (!visible)
                                throw new InvalidOperationException("Missing combo slash at " + cue.seconds);
                        }
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(baked);
                    }
                    float maxHips = 0;
                    float maxHand = 0;
                    foreach (var step in take.attackTrack.steps.Skip(1))
                        foreach (float boundary in new[] { step.seconds, step.seconds + step.blendSeconds })
                        {
                            SampleCombo(showcase, boundary - 1f / 600);
                            var hips = showcase.character.GetBoneTransform(HumanBodyBones.Hips).position;
                            var hand = showcase.character.GetBoneTransform(HumanBodyBones.RightHand).position;
                            SampleCombo(showcase, boundary + 1f / 600);
                            maxHips = Mathf.Max(maxHips, Vector3.Distance(hips,
                                showcase.character.GetBoneTransform(HumanBodyBones.Hips).position));
                            maxHand = Mathf.Max(maxHand, Vector3.Distance(hand,
                                showcase.character.GetBoneTransform(HumanBodyBones.RightHand).position));
                            SampleCombo(showcase, take.Duration * .75f);
                            SampleCombo(showcase, boundary - 1f / 600);
                            if (Vector3.Distance(hips,
                                showcase.character.GetBoneTransform(HumanBodyBones.Hips).position) > .0002f ||
                                Vector3.Distance(hand,
                                showcase.character.GetBoneTransform(HumanBodyBones.RightHand).position) > .0002f)
                                throw new InvalidOperationException("Scrubbing changes the combo's sampled pose.");
                        }
                    if (maxHips > .06f || maxHand > .12f)
                        throw new InvalidOperationException("Discontinuous combo boundary: hips=" + maxHips +
                            ", hand=" + maxHand);
                    report.AppendLine("PASS " + showcase.CharacterName + ": " + (samples + 1) +
                        " poses; 31 visible slash cues; same driver; deterministic seeking; framing/materials/floor valid.");
                    report.AppendLine("Boundary deltas over 1/300s: hips=" + maxHips.ToString("F5") +
                        "m; hand=" + maxHand.ToString("F5") + "m; camera distance=" +
                        showcase.director.SafetyDistance.ToString("F3"));
                    if (character == 0)
                        for (int i = 0; i < take.attackTrack.steps.Length; i++)
                        {
                            var step = take.attackTrack.steps[i];
                            SampleCombo(showcase, step.seconds + .2f);
                            SaveVfxFrame(showcase, "ComboSegment" + (i + 1));
                        }
                }
            }
            File.WriteAllText(PepeShowcaseSetup.Output + "/ComboValidation.txt", report.ToString());
        }

        static void SampleCombo(PepeAttackShowcase showcase, float seconds)
        {
            showcase.Evaluate(showcase.library.TakeStart(0) + showcase.library.leadSeconds + seconds,
                1f / 60, false);
        }
    }
}
