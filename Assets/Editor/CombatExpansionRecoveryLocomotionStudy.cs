using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionRecoveryLocomotionStudy
    {
        const string Movement = "Assets/Selected/FightingAnimsetPro/Animations/KB_Movement.fbx";
        const string Output = "GeneratedAssets/CombatExpansion/SamuraiStudy/RecoveryLocomotionCandidates";
        const string Scope = "Unpaired diagnostic only; no motion suitability or gameplay acceptance. " +
            "Actual saved fighter humanoid Animator, fresh combatController overrides: IdlePlaceholder=idleAnim, " +
            "WalkPlaceholder=candidate. Base Layer.Walk, Speed=1, animator speed=1; no scripted turn or translation. " +
            "Identity rotation, zero translation, original world scale; applyRootMotion=true extracts native travel. " +
            "120Hz forward Animator.Update steps plus exact length-0.0001 endpoint, without loop wrapping. " +
            "CSV world positions and root quaternion; render pass replays identical steps and checks positions. " +
            "12 chronological row-major checkpoints: left view SIDE (-X), right view OBLIQUE (-X,-Z). " +
            "Forward is +Z; lateral is X. Fixed fitted views per candidate; numeric footers are seconds. " +
            "CPU character skins only, preview floor/light; no weapon props, importer edits or gameplay assets.";

        public static void CaptureCandidates()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Capture requires edit mode.");
            var hashes = new Dictionary<string, string>();
            Guard(hashes, CombatExpansionInventory.Battle);
            Guard(hashes, Movement);
            var report = new Report { unityVersion = Application.unityVersion, scope = Scope };
            try
            {
                Directory.CreateDirectory(Output);
                using var session = new SourceSession();
                var side = AssetDatabase.LoadAllAssetsAtPath(Movement).OfType<AnimationClip>().ToArray();
                foreach (var fighter in session.Fighters)
                {
                    Guard(hashes, AssetDatabase.GetAssetPath(fighter.combatController));
                    Guard(hashes, AssetDatabase.GetAssetPath(fighter.idleAnim));
                    Guard(hashes, AssetDatabase.GetAssetPath(fighter.Animator.avatar));
                    var clips = new[] { side.Single(c => c.name == "KB_Sidestep_L"),
                        side.Single(c => c.name == "KB_Sidestep_R"), fighter.walkAnim };
                    foreach (var clip in clips)
                    {
                        if (!clip || !clip.humanMotion || clip.length <= .0001f)
                            throw new InvalidOperationException("Candidate is not a valid humanoid clip.");
                        Guard(hashes, AssetDatabase.GetAssetPath(clip));
                        using var actor = new Actor(fighter, clip);
                        var record = new Candidate
                        {
                            avatar = fighter.name, clip = clip.name, source = Identity(clip),
                            controller = Identity(fighter.combatController), idle = Identity(fighter.idleAnim),
                            humanoidAvatar = Identity(fighter.Animator.avatar), humanoid = clip.humanMotion,
                            duration = clip.length, endpoint = clip.length - .0001f, averageSpeed = clip.averageSpeed,
                            originalWorldScale = fighter.Animator.transform.lossyScale,
                            forwardReference = clip == fighter.walkAnim
                        };
                        var samples = new List<Sample>();
                        actor.Reset();
                        for (int step = 0; ; step++)
                        {
                            float time = Mathf.Min(step / 120f, record.endpoint);
                            actor.Advance(time);
                            samples.Add(actor.Read(time));
                            if (time == record.endpoint)
                                break;
                        }
                        record.initialRoot = samples[0].positions[0];
                        record.endRoot = samples.Last().positions[0];
                        record.translation = record.endRoot - record.initialRoot;
                        record.sampleCount = samples.Count;
                        string stem = Output + "/" + fighter.name + "_" + clip.name;
                        WriteCsv(stem + ".csv", samples);
                        using var rendering = new Rendering(actor);
                        rendering.Write(stem + ".png", samples, record);
                        report.candidates.Add(record);
                    }
                }
            }
            finally
            {
                foreach (var entry in hashes)
                    if (Hash(entry.Key) != entry.Value)
                        throw new InvalidOperationException("Source bytes changed: " + entry.Key);
            }
            report.sourceHashes = hashes.Select(x => new SourceHash { path = x.Key, sha256 = x.Value }).ToArray();
            report.sourceBytesUnchanged = true;
            File.WriteAllText(Output + "/Report.json", JsonUtility.ToJson(report, true));
            File.WriteAllText(Output + "/Scope.txt", Scope + "\nUnity " + Application.unityVersion + "\n");
            Debug.Log("Recovery locomotion diagnostic written: " + Output);
        }

        static string Identity(Object value)
        {
            if (!value || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long id))
                throw new InvalidOperationException("Missing persistent source identity.");
            return AssetDatabase.GetAssetPath(value) + "|" + guid + "|" + id;
        }

        static string Hash(string path)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }

        static void Guard(Dictionary<string, string> hashes, string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("Expected persistent source asset.");
            foreach (string file in new[] { path, path + ".meta" })
                if (!hashes.ContainsKey(file))
                    hashes.Add(file, Hash(file));
        }

        sealed class SourceSession : IDisposable
        {
            readonly Scene active = SceneManager.GetActiveScene();
            readonly PropertyInfo[] singletons = new[]
            {
                typeof(GameManager), typeof(MemeBattleUI), typeof(WebSocketManager),
                typeof(MortalKombatCamera), typeof(CombatPositioningController)
            }.Select(t => t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)).ToArray();
            readonly object[] values;
            readonly bool dirty;
            Scene scene;
            public CharacterCombat[] Fighters { get; private set; }

            public SourceSession()
            {
                dirty = active.isDirty;
                values = singletons.Select(p => p.GetValue(null)).ToArray();
                try
                {
                    scene = EditorSceneManager.OpenPreviewScene(CombatExpansionInventory.Battle);
                    var fighters = scene.GetRootGameObjects()
                        .SelectMany(r => r.GetComponentsInChildren<CharacterCombat>(true)).ToArray();
                    Fighters = new[] { "Mankey", "Pepe" }.Select(name => fighters.Single(f => f.name == name)).ToArray();
                    foreach (var fighter in Fighters)
                        if (!fighter.Animator || !fighter.Animator.avatar || !fighter.Animator.avatar.isValid ||
                            !fighter.Animator.avatar.isHuman || !fighter.combatController || !fighter.idleAnim)
                            throw new InvalidOperationException("Missing retargetable BattleScene fighter.");
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                try
                {
                    if (scene.IsValid())
                        EditorSceneManager.ClosePreviewScene(scene);
                }
                finally
                {
                    for (int i = 0; i < singletons.Length; i++)
                        singletons[i].SetValue(null, values[i]);
                    if (active.IsValid() && active.isLoaded)
                        SceneManager.SetActiveScene(active);
                }
                if (SceneManager.GetActiveScene() != active || active.isDirty != dirty)
                    throw new InvalidOperationException("Active scene state changed during capture.");
            }
        }

        sealed class Actor : IDisposable
        {
            public readonly Scene scene;
            public readonly Animator animator;
            public readonly SkinnedMeshRenderer[] skins;
            readonly AnimatorOverrideController controller;
            readonly AnimationClip clip;
            readonly Mesh boundsMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            readonly Vector3 scale;
            readonly Transform[] bones;
            float current;

            public Actor(CharacterCombat fighter, AnimationClip candidate)
            {
                scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    clip = candidate;
                    scale = fighter.Animator.transform.lossyScale;
                    animator = Object.Instantiate(fighter.Animator.gameObject).GetComponent<Animator>();
                    SceneManager.MoveGameObjectToScene(animator.gameObject, scene);
                    FrankRetargetBuilder.StripStudyComponents(animator.gameObject, false);
                    foreach (var other in animator.GetComponentsInChildren<Animator>(true))
                        other.enabled = other == animator;
                    controller = new AnimatorOverrideController(fighter.combatController);
                    controller.hideFlags = HideFlags.HideAndDontSave;
                    var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                    controller.GetOverrides(overrides);
                    foreach (string slot in new[] { "IdlePlaceholder", "WalkPlaceholder" })
                        if (overrides.Count(pair => pair.Key.name == slot) != 1)
                            throw new InvalidOperationException("Missing unique controller slot: " + slot);
                    controller["IdlePlaceholder"] = fighter.idleAnim;
                    controller["WalkPlaceholder"] = candidate;
                    animator.runtimeAnimatorController = controller;
                    animator.applyRootMotion = true;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.enabled = true;
                    animator.gameObject.SetActive(true);
                    animator.Rebind();
                    bones = new[] { animator.transform }.Concat(new[] { HumanBodyBones.Hips,
                        HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes,
                        HumanBodyBones.RightToes }.Select(animator.GetBoneTransform)).ToArray();
                    if (bones.Any(b => !b))
                        throw new InvalidOperationException("Missing required humanoid measurement bone.");
                    var body = new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Head }
                        .Select(animator.GetBoneTransform).Where(t => t).ToArray();
                    skins = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .Where(r => r.enabled && r.sharedMesh && r.bones.Any(b => body.Contains(b))).ToArray();
                    if (skins.Length == 0)
                        throw new InvalidOperationException("No character skins in renderer allowlist.");
                    foreach (var renderer in animator.GetComponentsInChildren<Renderer>(true))
                        renderer.enabled = skins.Contains(renderer);
                    foreach (var skin in skins)
                        skin.updateWhenOffscreen = true;
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Reset()
            {
                animator.Rebind();
                animator.transform.localScale = scale;
                animator.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                animator.speed = 1;
                animator.SetFloat("Speed", 1);
                animator.Play("Base Layer.Walk", 0, 0);
                animator.Update(0);
                animator.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                current = 0;
                Verify();
            }

            public void Advance(float target)
            {
                if (target < current || target > clip.length - .0001f)
                    throw new InvalidOperationException("Nonchronological or wrapped sample.");
                while (current < target)
                {
                    float delta = Mathf.Min(1f / 120, target - current);
                    animator.Update(delta);
                    current = Mathf.Min(target, current + delta);
                    Verify();
                }
            }

            void Verify()
            {
                var state = animator.GetCurrentAnimatorStateInfo(0);
                var clips = animator.GetCurrentAnimatorClipInfo(0);
                if (!state.IsName("Base Layer.Walk") || animator.IsInTransition(0) ||
                    clips.Length != 1 || clips[0].clip != clip || state.normalizedTime >= 1 ||
                    Mathf.Abs(state.speed * state.speedMultiplier - 1) > .0001f)
                    throw new InvalidOperationException("Expected unwrapped candidate in stable Walk at speed 1.");
            }

            public Sample Read(float time) => new Sample { time = time,
                positions = bones.Select(b => b.position).ToArray(), rotation = animator.transform.rotation,
                bounds = SkinBounds(skins, boundsMesh) };

            public void Dispose()
            {
                if (scene.IsValid())
                    EditorSceneManager.ClosePreviewScene(scene);
                if (controller)
                    Object.DestroyImmediate(controller);
                Object.DestroyImmediate(boundsMesh);
            }
        }
    }
}
