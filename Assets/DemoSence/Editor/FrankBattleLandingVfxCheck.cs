using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        [MenuItem("Tools/Battle/Inspect landing VFX rendering")]
        public static void InspectBattleLandingVfx()
        {
            const string folder = "Temp/FrankRetarget/Vfx/Landing";
            Directory.CreateDirectory(folder);
            var scene = EditorSceneManager.NewPreviewScene();
            var cameraRoot = new GameObject("Landing VFX preview");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraRoot, scene);
            var camera = cameraRoot.AddComponent<Camera>();
            camera.enabled = false;
            camera.scene = scene;
            camera.transform.position = new Vector3(0, 2, 3);
            camera.transform.LookAt(Vector3.zero);
            camera.orthographic = true;
            camera.orthographicSize = .8f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.08f, .08f, .1f);
            var target = new RenderTexture(640, 480, 24);
            var texture = new Texture2D(640, 480, TextureFormat.RGB24, false);
            var previousActive = RenderTexture.active;
            var report = new StringBuilder();
            bool hasRenderingError = false;
            try
            {
                camera.targetTexture = target;
                foreach (string name in new[] { "LandingDust", "GroundImpact" })
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VfxFolder + "/" + name + ".prefab");
                    if (!prefab) throw new Exception("Missing landing VFX: " + name);
                    var root = Object.Instantiate(prefab);
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                    root.transform.position = Vector3.zero;
                    try
                    {
                        foreach (var light in root.GetComponentsInChildren<Light>(true)) light.enabled = false;
                        foreach (float seconds in new[] { .08f, .2f, .4f })
                        {
                            foreach (var particles in root.GetComponentsInChildren<ParticleSystem>())
                                particles.Simulate(seconds, false, true, true);
                            camera.Render();
                            RenderTexture.active = target;
                            texture.ReadPixels(new Rect(0, 0, 640, 480), 0, 0);
                            texture.Apply();
                            int magenta = texture.GetPixels32().Count(c => c.r > 235 && c.g < 25 && c.b > 235);
                            hasRenderingError |= magenta > 0;
                            report.AppendLine($"{name} t={seconds:F2}: magentaPixels={magenta}");
                            File.WriteAllBytes(folder + "/" + name + "-" + seconds.ToString("F2",
                                System.Globalization.CultureInfo.InvariantCulture) + ".png", texture.EncodeToPNG());
                        }
                        foreach (var material in root.GetComponentsInChildren<ParticleSystemRenderer>(true)
                            .SelectMany(r => r.sharedMaterials).Where(m => m).Distinct())
                        {
                            var shader = material.shader;
                            hasRenderingError |= !shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader);
                            report.AppendLine(material.name + ": shader=" + (shader ? shader.name : "missing") +
                                "; path=" + (shader ? AssetDatabase.GetAssetPath(shader) : "missing") +
                                "; supported=" + (shader && shader.isSupported) +
                                "; errors=" + (shader && ShaderUtil.ShaderHasError(shader)) +
                                "; passes=" + material.passCount);
                            if (shader)
                                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                                    report.AppendLine(message.severity + ": " + message.message);
                        }
                    }
                    finally { Object.DestroyImmediate(root); }
                }
                report.AppendLine(hasRenderingError ? "FAIL landing VFX rendering." : "PASS landing VFX rendering: no error-magenta pixels or shader errors.");
                File.WriteAllText(folder + "/render-check.txt", report.ToString());
                if (hasRenderingError) throw new Exception("Landing VFX rendering failed. See " + folder + "/render-check.txt");
            }
            finally
            {
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(target);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
