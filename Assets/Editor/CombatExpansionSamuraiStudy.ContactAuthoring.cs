using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        const string CAOutput = BladeOutput + "/ContactAuthoring";
        const string CAScope = "Execution01 authoring evidence only: no gameplay registration, damage, scene saves, " +
            "source edits, or root motion edits. Four grounded runtime pairs; native armed PlayerA and unarmed PlayerB. " +
            "Mankey spacing 1.64m, Pepe spacing 1.7m, both lane directions. Times are authoritative source seconds. " +
            "World coordinates are Unity metres, Y-up, ground Y=0; image display floor is Y=-0.015. " +
            "All native skin influences are normalized and applied as current bone*bindpose matrices plus blend shapes. " +
            "Body selection follows established visible enabled skinned meshes with >=1000 vertices; weapons excluded. " +
            "Native BladeR identity 3e685dd57e9c78b49b20bef3e8358ae3:4300002, exactly 111 blade faces, local Z<=-0.33. " +
            "Exact triangle surface minimum is unsigned and does not distinguish touch from intersection. " +
            "All triangle-pair ties within 0.00001m retained; one closest-point representative per triangle pair. " +
            "Human weight arrays index HumanBodyBones enum including LastBone as Unmapped. Nonhuman influences " +
            "map to their nearest mapped human ancestor, never nearest spatial pivot. Group order: HeadNeck, " +
            "AxialTorso, LeftArm, RightArm, LeftLeg, RightLeg, Unmapped. Jaw/eyes map to HeadNeck; fingers to arms. " +
            "Point weights use closest-point barycentrics; triangle weights average normalized corner weights. " +
            "Dominant group ties within 0.01 normalized weight preserved. Chosen anchor uses highest mapped human " +
            "point weight then enum index; exact closest triangle wins, then renderer path, triangle and blade face. " +
            "Anchor coordinates use full chosen-bone/hips inverse transforms; blade local uses native renderer transform. " +
            "Blade velocity follows the same blade-local point at t+-1/240s: world derivative rotated into victim " +
            "Animator axes at t. Relative velocity subtracts the world derivative of the chosen target-bone anchor " +
            "using the same fixed targetBoneLocalPoint at t+-1/240s, then rotates by inverse victim Animator " +
            "rotation at t without scale. sourceBladeLocalPoint stays fixed across both samples. " +
            "Landing samples start 2.15s at 240Hz including exact duration. Axial triangle inclusion requires every " +
            "corner raw native sum for EXACT Hips/Spine/Chest/UpperChest bones >=0.5, no descendants. " +
            "Non-leg support triangles require every corner combined normalized HeadNeck/AxialTorso/LeftArm/RightArm " +
            "weight >=0.5; legs/feet do not contribute to eligibility. Minimum support world Y selects a vertex, " +
            "then dominant mapped human weight (ties by enum index). Equal minima use renderer path, face, corner order. " +
            "Support renderer/mesh/triangle/vertex identify the surface. torsoFacingUp is the dot with world up of " +
            "normalized cross(rightShoulder-leftShoulder, (UpperChest else Chest)-Hips); +1 means that geometric " +
            "front normal faces up, -1 down, 0 horizontal. Missing or degenerate mapped axes throw explicitly. " +
            "Near-ground threshold 0.02m is candidate-only; cushion/geometry may preclude actual impact. " +
            "Images use fixed side/oblique views and timestamp labels; exact timestamps also in JSON. " +
            "Backward seeks compare selected frames after evaluating clip end; tolerance 0.001m plus torso facing " +
            "dot tolerance 0.0001 and unchanged support bone. " +
            "Original clips, drivers, settings and static singletons are preserved by owned isolated preview cleanup.";

        public static void CaptureExecution01ContactAuthoring()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            var grounding = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(
                "Assets/CombatExpansion/SamuraiStudy/Grounding/Samurai_Execution01_Grounding.asset");
            if (!grounding)
                throw new InvalidOperationException("Execution01 grounding is required for contact authoring.");
            Directory.CreateDirectory(CAOutput);
            File.WriteAllText(CAOutput + "/Status.txt", "RUNNING contact authoring; partial files are incomplete.\n");
            var report = new CAReport
            {
                scope = CAScope,
                utc = DateTime.UtcNow.ToString("O"),
                unity = Application.unityVersion,
                grounding = CombatExpansionInventory.Identity(grounding),
                scene = CombatExpansionInventory.Battle
            };
            File.WriteAllText(CAOutput + "/Scope.txt", CAScope);
            try
            {
                using var session = new SourceSession();
                InitializePairStudy(session.Fighters);
                foreach (var source in session.Fighters)
                foreach (int direction in new[] { 1, -1 })
                {
                    report.cases.Add(CACapturePair(session.Fighters, source, direction, grounding));
                    File.WriteAllText(CAOutput + "/Report.json", JsonUtility.ToJson(report, true));
                }
                CACompareDirections(report);
                File.WriteAllText(CAOutput + "/Report.json", JsonUtility.ToJson(report, true));
                bool stable = report.cases.All(c => c.backwardSeeks.All(s => s.withinOneMillimeter));
                File.WriteAllText(CAOutput + "/Status.txt", stable
                    ? "CAPTURED four cases; backward seeks stable. Anatomy/landing remain unapproved.\n"
                    : "CAPTURED WITH UNSTABLE BACKWARD SEEKS; review JSON, no acceptance.\n");
            }
            catch (Exception error)
            {
                File.WriteAllText(CAOutput + "/Status.txt", "FAILED: " + error + "\nPartial evidence only.\n");
                throw;
            }
        }

        static CACase CACapturePair(CharacterCombat[] fighters, CharacterCombat source, int direction,
            FrankPairGrounding grounding)
        {
            var target = fighters.Single(f => f != source);
            float spacing = source.name == "Mankey" ? 1.64f : 1.7f;
            var pair = BeginBladeStudy(fighters, source, target, 1, direction, grounding, spacing);
            try
            {
                pair.EvaluateAt(0);
                CheckPairWeapons(pair, pair.AttackerActor.Pose.weaponRenderers);
                using var sword = new SwordRegion(pair);
                var skins = CASkins(target);
                var attackerSkins = CASkins(source);
                var record = CANewCase(pair, source, target, direction, spacing, sword, skins);
                var bounds = new Bounds(Vector3.zero, Vector3.zero);
                float[] contacts = source.name == "Mankey"
                    ? new[] { .395833343f, 2.0333333f } : new[] { .3875f, 2.0291667f };
                foreach (float time in contacts)
                    record.contacts.Add(CAMeasureContact(pair, sword, skins, attackerSkins, target, time));
                if (pair.Duration <= 2.15f)
                    throw new InvalidOperationException("Execution01 clip does not contain terminal landing.");
                int frames = Mathf.CeilToInt((pair.Duration - 2.15f) * 240);
                for (int frame = 0; frame <= frames; frame++)
                {
                    float seconds = Mathf.Min(2.15f + frame / 240f, pair.Duration);
                    record.landing.Add(CALandingFrame(pair, sword, skins, attackerSkins, target, seconds, ref bounds));
                }
                CASelectLanding(record);
                record.imageTimes = CAImageTimes(record);
                var baselines = new Dictionary<float, CALanding>();
                foreach (float time in record.imageTimes)
                    baselines.Add(time, CALandingFrame(pair, sword, skins, attackerSkins, target, time, ref bounds));
                foreach (float time in record.imageTimes.Reverse())
                {
                    pair.EvaluateAt(pair.Duration);
                    var frame = CALandingFrame(pair, sword, skins, attackerSkins, target, time, ref bounds);
                    var seek = CACompareSeek(baselines[time], frame);
                    var original = record.contacts.FirstOrDefault(c => c.seconds == time);
                    if (original != null)
                    {
                        var repeated = CAMeasureContact(pair, sword, skins, attackerSkins, target, time);
                        seek.contactGapErrorM = Mathf.Abs(original.minimumGapM - repeated.minimumGapM);
                        seek.contactAnchorErrorM = CAContactAnchorWorldError(original, repeated);
                    }
                    seek.withinOneMillimeter = Mathf.Max(seek.maxTrajectoryErrorM, seek.torsoMinimumErrorM,
                        seek.fullBodyMinimumErrorM, seek.bladeTipErrorM, seek.contactGapErrorM,
                        seek.contactAnchorErrorM, seek.supportMinimumErrorM, seek.supportPointErrorM) <= .001f &&
                        seek.torsoFacingUpError <= .0001f && seek.sameSupportBone;
                    record.backwardSeeks.Add(seek);
                }
                string stem = source.name + "_" + target.name + "_" + (direction > 0 ? "Positive" : "Negative");
                CAWriteCsv(record, stem);
                File.WriteAllText(CAOutput + "/" + stem + ".json", JsonUtility.ToJson(record, true));
                bounds.Expand(.55f);
                sword.ShowOverlay();
                using var rendering = new GameplayPairRendering(source.gameObject.scene, fighters, pair);
                rendering.Write(CAOutput + "/" + stem + ".png", bounds, record.imageTimes, pair.EvaluateAt);
                return record;
            }
            finally
            {
                pair.Cancel();
            }
        }
    }
}
