using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using ZeroSpace.Showcase;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class PepeShowcaseCapture
    {
        [MenuItem("Tools/Pepe Showcase/Validate weapon VFX")]
        public static void ValidateWeaponVfx()
        {
            var report = new StringBuilder();
            using (var test = new PepeShowcaseSession())
            {
                var showcase = test.showcase;
                for (int character = 0; character < 3; character++)
                {
                    showcase.customCharacterPrefab = showcase.library.mankeyPrefab;
                    showcase.SetCharacter(character);
                    for (int take = 0; take < showcase.library.takes.Length; take++)
                    {
                        var entry = showcase.library.takes[take];
                        int swings = 0;
                        int shots = 0;
                        showcase.Evaluate(showcase.library.TakeStart(take), 0, false);
                        foreach (var cue in entry.cues)
                        {
                            float time = cue.seconds + 1f / 60;
                            showcase.Evaluate(showcase.library.TakeStart(take) +
                                showcase.library.leadSeconds + time, 1f / 60, false);
                            if (cue.swing && entry.nativeWeaponEffects)
                            {
                                var baked = new Mesh();
                                try
                                {
                                    bool visible = showcase.Actor.Pose.weaponRenderers
                                        .Where(r => PepeShowcaseSetup.IsNativeEffect(r.name))
                                        .Any(r => PepeShowcaseSetup.NativeEffectVisible(r, baked));
                                    if (!visible) throw new InvalidOperationException("Missing native slash: " + entry.title);
                                    swings++;
                                }
                                finally
                                {
                                    Object.DestroyImmediate(baked);
                                }
                                if (character == 0 && swings == 1) SaveVfxFrame(showcase, "NativeTake" + (take + 1));
                            }
                            else if (cue.swing && cue.effectEnd > cue.effectStart)
                            {
                                int minimum = entry.animation.name.Contains("Critical_Dual") ? 2 : 1;
                                if (showcase.presentation.ActiveSweepCount < minimum)
                                    throw new InvalidOperationException("Missing blade ribbon: " + entry.title);
                                if (showcase.presentation.ActiveParticleCount == 0)
                                    throw new InvalidOperationException("Missing slash particles: " + entry.title);
                                swings++;
                            }
                            if (cue.gun)
                            {
                                if (showcase.presentation.ActiveMuzzleParticleCount == 0)
                                    throw new InvalidOperationException("Missing muzzle particles: " + time);
                                report.AppendLine("Muzzle " + time.ToString("F3") + " position=" +
                                    showcase.presentation.LastMuzzlePosition.ToString("F3") + " direction=" +
                                    showcase.presentation.LastMuzzleDirection.ToString("F3"));
                                shots++;
                                if (character == 0) SaveVfxFrame(showcase, "GunShot" + shots);
                            }
                            foreach (var root in showcase.gameObject.scene.GetRootGameObjects())
                                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                                    CheckMaterial(renderer);
                        }
                        if (swings == 0 || shots != entry.cues.Count(cue => cue.gun))
                            throw new InvalidOperationException("Incomplete weapon VFX timeline.");
                        report.AppendLine("PASS " + showcase.CharacterName + " / " + entry.title +
                            " : " + swings + " weapon swings, " + shots + " gun flashes; materials valid.");
                    }
                }
            }
            File.WriteAllText(PepeShowcaseSetup.Output + "/WeaponVfxValidation.txt", report.ToString());
        }

        static void SaveVfxFrame(PepeAttackShowcase showcase, string name)
        {
            var camera = showcase.director.output;
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var texture = new RenderTexture(1280, 720, 24);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                texture.Create();
                camera.targetTexture = texture;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0, false);
                File.WriteAllBytes(PepeShowcaseSetup.Output + "/" + name + ".png", image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(texture);
            }
        }

        [MenuItem("Tools/Pepe Showcase/Validate attacks and character replacement")]
        public static void Validate()
        {
            Directory.CreateDirectory(PepeShowcaseSetup.Output);
            var report = new StringBuilder();
            using (var test = new PepeShowcaseSession())
            {
                var texture = new RenderTexture(1280, 720, 24);
                var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                texture.Create();
                var showcase = test.showcase;
                showcase.director.output.targetTexture = texture;
                try
                {
                    for (int character = 0; character < 3; character++)
                    {
                        showcase.customCharacterPrefab = showcase.library.mankeyPrefab;
                        showcase.SetCharacter(character);
                        for (int take = 0; take < showcase.library.takes.Length; take++)
                        {
                            var entry = showcase.library.takes[take];
                            int samples = Mathf.CeilToInt(entry.Duration * 60);
                            Vector3 firstHand = Vector3.zero;
                            float motion = 0;
                            float firstDistance = 0;
                            float maxCameraTurn = 0;
                            Quaternion previousRotation = Quaternion.identity;
                            for (int sample = 0; sample <= samples; sample++)
                            {
                                float time = Mathf.Min(sample / 60f, entry.Duration);
                                showcase.Evaluate(showcase.library.TakeStart(take) +
                                    showcase.library.leadSeconds + time, 1f / 60, false);
                                CheckPose(showcase);
                                Vector3 hand = showcase.character.GetBoneTransform(HumanBodyBones.RightHand).position;
                                var rotation = showcase.director.output.transform.rotation;
                                if (sample == 0)
                                {
                                    firstHand = hand;
                                    firstDistance = showcase.director.SafetyDistance;
                                }
                                else maxCameraTurn = Mathf.Max(maxCameraTurn,
                                    Quaternion.Angle(previousRotation, rotation) * 60);
                                previousRotation = rotation;
                                if (Mathf.Abs(firstDistance - showcase.director.SafetyDistance) > .0001f)
                                    throw new InvalidOperationException("Camera zoom changed during an attack.");
                                motion = Mathf.Max(motion, Vector3.Distance(firstHand, hand));
                            }
                            if (motion < .15f) throw new InvalidOperationException("Attack animation did not advance.");
                            report.AppendLine(showcase.CharacterName + " / " + entry.animation.name +
                                " : PASS " + (samples + 1) + " poses, materials, framing and attacker-only.");
                            report.AppendLine("Stable camera distance=" + firstDistance.ToString("F3") +
                                "; maximum orbit speed=" + maxCameraTurn.ToString("F2") + " degrees/s.");
                            if (character == 0)
                            {
                                showcase.Evaluate(showcase.library.TakeStart(take) +
                                    showcase.library.leadSeconds + entry.Duration * .45f, 1f / 60, false);
                                report.AppendLine("Camera distance: " + showcase.director.SafetyDistance);
                                foreach (var bone in new[] { HumanBodyBones.Head, HumanBodyBones.Hips,
                                    HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder,
                                    HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
                                    HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                                    report.AppendLine(bone + ": " + showcase.character.GetBoneTransform(bone).position);
                                var method = showcase.director.GetType().GetMethod("WeaponBounds",
                                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                                foreach (var renderer in showcase.Actor.Pose.weaponRenderers)
                                {
                                    object[] arguments = { renderer, new Bounds() };
                                    bool visible = (bool)method.Invoke(showcase.director, arguments);
                                    report.AppendLine(renderer.name + " visible=" + visible + " geometry=" + arguments[1]);
                                }
                                Canvas.ForceUpdateCanvases();
                                showcase.director.output.Render();
                                var previous = RenderTexture.active;
                                RenderTexture.active = texture;
                                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0, false);
                                RenderTexture.active = previous;
                                File.WriteAllBytes(PepeShowcaseSetup.Output + "/Take" + (take + 1) + ".png",
                                    image.EncodeToPNG());
                            }
                        }
                    }
                }
                finally
                {
                    showcase.director.output.targetTexture = null;
                    Object.DestroyImmediate(texture);
                    Object.DestroyImmediate(image);
                }
            }
            File.WriteAllText(PepeShowcaseSetup.Output + "/Validation.txt", report.ToString());
        }

        static void CheckPose(PepeAttackShowcase showcase)
        {
            ulong mask = showcase.director.output.overrideSceneCullingMask;
            if (mask == 0 || mask != UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(
                showcase.gameObject.scene))
                throw new InvalidOperationException("Camera must render only the isolated showcase scene.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var other = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (other != showcase.gameObject.scene && (mask &
                    UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(other)) != 0)
                    throw new InvalidOperationException("Another loaded scene can enter the recording.");
            }
            foreach (var root in showcase.gameObject.scene.GetRootGameObjects())
                if (root.GetComponentsInChildren<CharacterCombat>(true).Length != 0)
                    throw new InvalidOperationException("Battle components entered the standalone showcase.");
            foreach (var body in showcase.Actor.Pose.originalBody)
                if (body && body.enabled) throw new InvalidOperationException("Source body is visible.");
            foreach (var renderer in showcase.character.GetComponentsInChildren<Renderer>(true))
                CheckMaterial(renderer);
            foreach (var renderer in showcase.Actor.Pose.weaponRenderers)
                CheckMaterial(renderer);
            foreach (var bone in showcase.character.GetComponentsInChildren<Transform>(true))
                if (!float.IsFinite(bone.position.x) || !float.IsFinite(bone.position.y) ||
                    !float.IsFinite(bone.position.z))
                    throw new InvalidOperationException("Invalid character pose: " + bone.name);
            foreach (var bone in new[] { HumanBodyBones.Hips, HumanBodyBones.Head,
                HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
            {
                var position = showcase.character.GetBoneTransform(bone).position;
                var point = showcase.director.output.WorldToViewportPoint(position);
                if (point.z <= 0 || point.x < .025f || point.x > .975f || point.y < .025f || point.y > .975f)
                    throw new InvalidOperationException("Camera clips " + bone + " in " + showcase.TakeIndex);
            }
        }

        static void CheckMaterial(Renderer renderer)
        {
            if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) return;
            foreach (var material in renderer.sharedMaterials)
                if (!material || !material.shader || !material.shader.isSupported)
                    throw new InvalidOperationException("Missing/unsupported material on " + renderer.name);
        }
    }
}
