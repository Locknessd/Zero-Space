using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using ZeroSpace.Showcase;

namespace FrankRetarget.Editor
{
    public static partial class PepeShowcaseCapture
    {
        [MenuItem("Tools/Pepe Showcase/Validate complete combo video")]
        public static void ValidateCompleteCombo()
            => ValidateCompilation(PepeShowcaseSetup.CompleteScenePath, "/CompleteValidation.txt");

        static void ValidateCompilation(string scenePath, string reportPath)
        {
            var report = new StringBuilder();
            using (var session = new PepeShowcaseSession(scenePath))
            {
                var showcase = session.showcase;
                var library = showcase.library;
                if (library.bomTrailer) ValidateBomIntro(showcase, report);
                if (!library.continuous || showcase.loop || library.takes.Length != 5 ||
                    library.takes.Select(take => take.animation).Distinct().Count() != 5)
                    throw new InvalidOperationException("Expected five unique connected attacks without looping.");
                if (library.takes.Sum(take => take.cues.Count(cue => cue.swing)) != 30 ||
                    library.takes.Sum(take => take.cues.Count(cue => cue.gun)) != 3)
                    throw new InvalidOperationException("Missing slash or gun cue in the complete sequence.");
                var floor = showcase.gameObject.scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true))
                    .Where(renderer => renderer.name.IndexOf("floor tile", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(renderer => renderer.bounds).ToArray();
                for (int character = 0; character < 3; character++)
                {
                    showcase.customCharacterPrefab = library.mankeyPrefab;
                    showcase.SetCharacter(character);
                    for (int index = 0; index < library.takes.Length; index++)
                    {
                        var take = library.takes[index];
                        int samples = Mathf.CeilToInt(take.Duration * 60);
                        Vector3 lockedPosition = Vector3.zero;
                        Quaternion lockedRotation = Quaternion.identity;
                        float cameraTravel = 0;
                        for (int sample = 0; sample <= samples; sample++)
                        {
                            float local = Mathf.Min(sample / 60f, take.Duration - .0001f);
                            showcase.Evaluate(library.TakeStart(index) + local, 1f / 60, false);
                            CheckPose(showcase);
                            var cameraTransform = showcase.director.output.transform;
                            if (sample == 0)
                            {
                                lockedPosition = cameraTransform.position;
                                lockedRotation = cameraTransform.rotation;
                            }
                            if (take.lockCamera)
                            {
                                cameraTravel = Mathf.Max(cameraTravel,
                                    Vector3.Distance(lockedPosition, cameraTransform.position));
                                if (cameraTravel > .0001f ||
                                    Quaternion.Angle(lockedRotation, cameraTransform.rotation) > .001f)
                                    throw new InvalidOperationException("Final shot is chasing the character.");
                            }
                            if (!library.bomTrailer && showcase.director.SafetyDistance < 9)
                                throw new InvalidOperationException("Camera is closer than the requested wide view.");
                            bool leadIntoBlink = index + 1 < library.takes.Length &&
                                library.takes[index + 1].lockCamera && local > take.Duration - .65f;
                            if (library.bomTrailer && character == 0 && !take.lockCamera && !leadIntoBlink &&
                                showcase.director.SafetyDistance > 9.7f)
                                throw new InvalidOperationException("The BOM camera did not move closer than the prior shot.");
                            if (library.bomTrailer && character == 0 && take.lockCamera &&
                                showcase.director.SafetyDistance >= 11.793f)
                                throw new InvalidOperationException("The locked blink shot is still as far as the prior video.");
                            foreach (var bone in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                            {
                                var foot = showcase.character.GetBoneTransform(bone).position;
                                if (!floor.Any(tile => foot.x >= tile.min.x && foot.x <= tile.max.x &&
                                    foot.z >= tile.min.z && foot.z <= tile.max.z))
                                    throw new InvalidOperationException("Complete combo leaves the floor at " + foot);
                            }
                        }
                        ValidateCompleteEffects(showcase, index, character == 0);
                        int bladeBursts = showcase.presentation.BladeAccentCount;
                        int groundAccents = showcase.presentation.GroundAccentCount;
                        float shotDistance = library.bomTrailer && !take.lockCamera ?
                            showcase.director.FollowDistance : showcase.director.SafetyDistance;
                        if (library.bomTrailer) ValidateBomPowerEffects(showcase, index, report);
                        if (library.bomTrailer) ValidateBomMuzzle(showcase, index, report);
                        report.AppendLine("PASS " + showcase.CharacterName + " / " + take.title +
                            ": " + (samples + 1) + " poses; " + take.cues.Count(cue => cue.swing) +
                            " slash cues; " + take.cues.Count(cue => cue.gun) + " gun flashes; camera=" +
                            shotDistance.ToString("F3") + "m; framing/materials/floor valid.");
                        report.AppendLine("Motion VFX: " + bladeBursts +
                            " blade bursts; " + groundAccents + " ground accents." +
                            (take.lockCamera ? " Locked camera travel=" + cameraTravel.ToString("F6") + "m." : ""));
                    }
                    float maxHips = 0;
                    float maxHand = 0;
                    float maxCamera = 0;
                    for (int i = 1; i < library.takes.Length; i++)
                    {
                        float boundary = library.TakeStart(i);
                        showcase.Evaluate(boundary - .0001f, 0, false);
                        var hips = showcase.character.GetBoneTransform(HumanBodyBones.Hips).position;
                        var hand = showcase.character.GetBoneTransform(HumanBodyBones.RightHand).position;
                        var camera = showcase.director.output.transform.position;
                        showcase.Evaluate(boundary + .0001f, 0, false);
                        maxHips = Mathf.Max(maxHips, Vector3.Distance(hips,
                            showcase.character.GetBoneTransform(HumanBodyBones.Hips).position));
                        maxHand = Mathf.Max(maxHand, Vector3.Distance(hand,
                            showcase.character.GetBoneTransform(HumanBodyBones.RightHand).position));
                        maxCamera = Mathf.Max(maxCamera, Vector3.Distance(camera,
                            showcase.director.output.transform.position));
                        showcase.Evaluate(library.TakeStart(0), 0, false);
                        showcase.Evaluate(boundary - .0001f, 0, false);
                        if (Vector3.Distance(hips,
                            showcase.character.GetBoneTransform(HumanBodyBones.Hips).position) > .0002f)
                            throw new InvalidOperationException("Seeking changed the carried motion.");
                    }
                    if (maxHips > .03f || maxHand > .05f || maxCamera > .05f)
                        throw new InvalidOperationException("Discontinuous rig change: hips=" + maxHips +
                            "; hand=" + maxHand + "; camera=" + maxCamera);
                    report.AppendLine("PASS rig transitions and deterministic seeking; boundary deltas: hips=" +
                        maxHips.ToString("F5") + "; hand=" + maxHand.ToString("F5") +
                        "; camera=" + maxCamera.ToString("F5"));
                }
            }
            File.WriteAllText(PepeShowcaseSetup.Output + reportPath, report.ToString());
        }

        static void ValidateCompleteEffects(PepeAttackShowcase showcase, int index, bool saveFrames)
        {
            var take = showcase.library.takes[index];
            showcase.PlayTake(index);
            var baked = new Mesh();
            bool savedSlash = false;
            bool savedGun = false;
            try
            {
                foreach (var cue in take.cues)
                {
                    showcase.Evaluate(showcase.library.TakeStart(index) + cue.seconds + 1f / 60, 1f / 60, false);
                    if (cue.swing && take.nativeWeaponEffects)
                    {
                        bool visible = showcase.Actor.Pose.weaponRenderers
                            .Where(renderer => PepeShowcaseSetup.IsNativeEffect(renderer.name))
                            .Any(renderer => PepeShowcaseSetup.NativeEffectVisible(renderer, baked));
                        if (!visible) throw new InvalidOperationException("Missing native slash: " + take.title);
                    }
                    else if (cue.swing && cue.effectEnd > cue.effectStart)
                    {
                        int blades = index == 0 ? 2 : 1;
                        if (showcase.presentation.ActiveSweepCount < blades ||
                            showcase.presentation.ActiveParticleCount == 0)
                            throw new InvalidOperationException("Missing ribbon/particles: " + take.title);
                    }
                    if (cue.gun && showcase.presentation.ActiveMuzzleParticleCount == 0)
                        throw new InvalidOperationException("Missing gun flash: " + take.title);
                    if (cue.swing && (showcase.presentation.BladeAccentCount < take.cues.Count(item => item.swing) ||
                        showcase.presentation.ActiveAccentTriangles == 0))
                        throw new InvalidOperationException("Missing synchronized blade accents: " + take.title);
                    if (saveFrames && cue.swing && !savedSlash)
                    {
                        SaveVfxFrame(showcase, "CompleteAttack" + (index + 1));
                        savedSlash = true;
                    }
                    if (saveFrames && cue.gun && !savedGun)
                    {
                        SaveVfxFrame(showcase, "CompleteGun");
                        savedGun = true;
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(baked);
            }
        }
    }
}
