using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using ZeroSpace.Showcase;

namespace FrankRetarget.Editor
{
    internal sealed class PepeShowcaseSession : IDisposable
    {
        readonly Scene previous;
        readonly string path;
        readonly Scene scene;
        readonly Dictionary<Scene, ulong> masks = new Dictionary<Scene, ulong>();
        public readonly PepeAttackShowcase showcase;

        public PepeShowcaseSession(string scenePath = PepeShowcaseSetup.ScenePath)
        {
            previous = SceneManager.GetActiveScene();
            path = PepeShowcaseSetup.Root + "/Editor/Capture-" + Guid.NewGuid().ToString("N") + ".unity";
            if (!AssetDatabase.CopyAsset(scenePath, path))
                throw new InvalidOperationException("Create the showcase scene before recording.");
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            EditorSceneManager.SetSceneCullingMask(scene, 1UL << 62);
            SceneManager.SetActiveScene(scene);
            foreach (var root in scene.GetRootGameObjects())
            {
                showcase = root.GetComponent<PepeAttackShowcase>();
                if (showcase) break;
            }
            if (!showcase) throw new InvalidOperationException("Showcase component is missing.");
            showcase.showControls = false;
            showcase.director.output.aspect = 16f / 9;
            showcase.Initialize();
            showcase.director.output.scene = scene;
            showcase.director.output.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var other = SceneManager.GetSceneAt(i);
                if (other == scene) continue;
                ulong original = EditorSceneManager.GetSceneCullingMask(other);
                masks.Add(other, original);
                EditorSceneManager.SetSceneCullingMask(other, original & ~(1UL << 62));
            }
        }

        public void Dispose()
        {
            if (showcase) showcase.ReleaseActor();
            foreach (var entry in masks)
                if (entry.Key.IsValid() && entry.Key.isLoaded)
                    EditorSceneManager.SetSceneCullingMask(entry.Key, entry.Value);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.DeleteAsset(path);
        }
    }

    // Check the same Start/LateUpdate lifecycle that users get when pressing Play.
    [InitializeOnLoad]
    internal static partial class PepeShowcasePlayCheck
    {
        const string Key = "PepeShowcase.PlayCheck";
        const string Report = "GeneratedAssets/PepeShowcase/PlayModeValidation.txt";
        static PepeAttackShowcase showcase;
        static Mesh mesh;
        static SkinnedMeshRenderer skin;
        static Vector3[] initialVertices;
        static Vector3 initialHand;
        static Vector3 initialSourceHand;
        static Quaternion initialArm;
        static float handMotion;
        static float sourceMotion;
        static float deformation;
        static float armMotion;
        static int current;
        static int samples;
        static int failures;
        static bool imageSaved;

        static PepeShowcasePlayCheck()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += Changed;
        }

        [MenuItem("Tools/Pepe Showcase/Check actual Play mode animation")]
        public static void Begin() => BeginSession(false);

        public static void BeginRecording(string scenePath = PepeShowcaseSetup.ScenePath) => BeginSession(true, scenePath);

        static void BeginSession(bool recording, string scenePath = PepeShowcaseSetup.ScenePath)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetInt(Key, 0) != 0)
                throw new InvalidOperationException("Start the Play check in Edit mode.");
            Directory.CreateDirectory(PepeShowcaseSetup.Output);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isDirty) continue;
                string folder = PepeShowcaseSetup.Output + "/UnsavedScenes/" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                Directory.CreateDirectory(folder);
                if (!EditorSceneManager.SaveScene(scene, folder + "/" + i + "_" + scene.name + ".unity", true))
                    throw new IOException("Could not preserve unsaved scene " + scene.name);
            }
            string copy = PepeShowcaseSetup.Root + "/Editor/PlayCheck-" + Guid.NewGuid().ToString("N") + ".unity";
            if (!AssetDatabase.CopyAsset(scenePath, copy))
                throw new IOException("Could not copy showcase scene.");
            SessionState.SetString(Key + ".copy", copy);
            SessionState.SetString(Key + ".previous", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetFloat(Key + ".began", (float)EditorApplication.timeSinceStartup);
            SessionState.SetInt(Key, 1);
            SessionState.SetBool(Key + ".record", recording);
            File.WriteAllText(Report, "RUNNING actual Start/LateUpdate, body mesh and weapon motion checks.\n");
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(copy);
            EditorApplication.isPlaying = true;
        }

        static void Changed(PlayModeStateChange state)
        {
            if (SessionState.GetInt(Key, 0) == 0) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                current = -1;
                failures = 0;
                SessionState.SetInt(Key, 2);
            }
            if (state == PlayModeStateChange.ExitingPlayMode && SessionState.GetInt(Key, 0) != 3)
            {
                if (SessionState.GetBool(Key + ".record", false)) PepeShowcaseCapture.AbortPlayRecording();
                File.AppendAllText(Report, "FAIL interrupted before completion.\n");
            }
            if (state != PlayModeStateChange.EnteredEditMode) return;
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(
                SessionState.GetString(Key + ".previous", ""));
            AssetDatabase.DeleteAsset(SessionState.GetString(Key + ".copy", ""));
            SessionState.SetInt(Key, 0);
            showcase = null;
            if (mesh) Object.DestroyImmediate(mesh);
            mesh = null;
        }

        static void Tick()
        {
            if (SessionState.GetInt(Key, 0) != 2 || !EditorApplication.isPlaying || EditorApplication.isPaused)
                return;
            try
            {
                double timeout = SessionState.GetBool(Key + ".record", false) ? 900 :
                    showcase ? Mathf.Max(180, showcase.library.Duration * 3 + 60) : 240;
                if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + ".began", 0) > timeout)
                    throw new TimeoutException("Showcase Play check did not complete.");
                if (!showcase)
                {
                    if (SceneManager.sceneCount != 1 || SceneManager.GetActiveScene().path !=
                        SessionState.GetString(Key + ".copy", ""))
                        throw new InvalidOperationException("Showcase Play scene isolation failed.");
                    var candidate = Object.FindAnyObjectByType<PepeAttackShowcase>();
                    if (!candidate || !candidate.Actor) return;
                    showcase = candidate;
                    if (SessionState.GetBool(Key + ".record", false))
                    {
                        PepeShowcaseCapture.StartPlayRecording(showcase);
                        return;
                    }
                    showcase.showControls = false;
                    showcase.loop = false;
                    showcase.customCharacterPrefab = showcase.library.mankeyPrefab;
                    mesh = new Mesh();
                    Next();
                }
                if (SessionState.GetBool(Key + ".record", false)) return;
                Sample();
                int take = current % showcase.library.takes.Length;
                if (showcase.AttackTime >= showcase.library.takes[take].Duration - .02f)
                {
                    bool pass = samples > 10 && sourceMotion > .15f && handMotion > .15f &&
                        deformation > .08f && armMotion > 10;
                    if (!pass) failures++;
                    File.AppendAllText(Report, (pass ? "PASS " : "FAIL ") + showcase.CharacterName + " / " +
                        showcase.library.takes[take].animation.name + " samples=" + samples +
                        " sourceHand=" + sourceMotion.ToString("F3") + " targetHand=" + handMotion.ToString("F3") +
                        " meshRms=" + deformation.ToString("F3") + " armDegrees=" + armMotion.ToString("F1") +
                        " targetAnimatorInitialized=" + showcase.character.isInitialized + "\n");
                    if (current == showcase.library.takes.Length * 3 - 1)
                        Finish(failures == 0 ? "PASS all " + (current + 1) + " actual Play mode attacks." :
                            "FAIL " + failures + " attacks.");
                    else Next();
                }
            }
            catch (Exception error)
            {
                if (SessionState.GetBool(Key + ".record", false)) PepeShowcaseCapture.AbortPlayRecording();
                Finish("FAIL " + error);
            }
        }

        static void Next()
        {
            current++;
            int takes = showcase.library.takes.Length;
            if (current > 0 && current % takes == 0) showcase.SetCharacter(current / takes);
            showcase.PlayTake(current % takes);
            skin = showcase.character.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .OrderByDescending(renderer => renderer.sharedMesh ? renderer.sharedMesh.vertexCount : 0).First();
            skin.BakeMesh(mesh);
            initialVertices = mesh.vertices;
            initialHand = showcase.character.GetBoneTransform(HumanBodyBones.RightHand).position;
            initialSourceHand = showcase.Actor.Pose.limbs[1].sourceEnd.position;
            initialArm = showcase.character.GetBoneTransform(HumanBodyBones.RightUpperArm).localRotation;
            handMotion = sourceMotion = deformation = armMotion = 0;
            samples = 0;
            imageSaved = false;
        }

        static void Sample()
        {
            samples++;
            handMotion = Mathf.Max(handMotion, Vector3.Distance(initialHand,
                showcase.character.GetBoneTransform(HumanBodyBones.RightHand).position));
            sourceMotion = Mathf.Max(sourceMotion, Vector3.Distance(initialSourceHand,
                showcase.Actor.Pose.limbs[1].sourceEnd.position));
            armMotion = Mathf.Max(armMotion, Quaternion.Angle(initialArm,
                showcase.character.GetBoneTransform(HumanBodyBones.RightUpperArm).localRotation));
            skin.BakeMesh(mesh);
            var vertices = mesh.vertices;
            float squared = 0;
            int count = 0;
            for (int i = 0; i < vertices.Length; i += Mathf.Max(1, vertices.Length / 64))
            {
                squared += (vertices[i] - initialVertices[i]).sqrMagnitude;
                count++;
            }
            deformation = Mathf.Max(deformation, Mathf.Sqrt(squared / Mathf.Max(1, count)));
            if (current < showcase.library.takes.Length && !imageSaved && showcase.AttackTime >=
                showcase.library.takes[current].Duration * .45f)
            {
                imageSaved = true;
                SaveFrame();
            }
        }

        static void SaveFrame()
        {
            var camera = showcase.director.output;
            var texture = new RenderTexture(1280, 720, 24);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var previousTarget = camera.targetTexture;
            try
            {
                texture.Create();
                camera.targetTexture = texture;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0, false);
                File.WriteAllBytes(PepeShowcaseSetup.Output + "/PlayModeTake" + (current + 1) + ".png",
                    image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = previousTarget;
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(image);
            }
        }

        internal static void Finish(string result)
        {
            File.AppendAllText(Report, result + "\n");
            SessionState.SetInt(Key, 3);
            EditorApplication.isPlaying = false;
        }
    }
}
