using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static partial class BattlePresentationWorkbench
{
    public static void AuditScene()
    {
        var b = new StringBuilder(Status());
        var scene = SceneManager.GetSceneByPath(BattlePath);
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer)
                    continue;
                b.AppendLine($"Renderer {r.name}; type={r.GetType().Name}; position={r.transform.position}; " +
                    $"bounds={r.bounds}; active={r.gameObject.activeInHierarchy}");
                foreach (var m in r.sharedMaterials)
                {
                    if (!m)
                    {
                        b.AppendLine("  MISSING MATERIAL");
                        continue;
                    }
                    b.AppendLine($"  {AssetDatabase.GetAssetPath(m)}; shader={m.shader.name}; " +
                        $"supported={m.shader.isSupported}; errors={ShaderUtil.ShaderHasError(m.shader)}; " +
                        $"color={(m.HasProperty("_Color") ? m.GetColor("_Color").ToString() : "none")}");
                    foreach (var name in m.GetTexturePropertyNames())
                    {
                        if (m.GetTexture(name))
                            b.AppendLine($"    {name}={AssetDatabase.GetAssetPath(m.GetTexture(name))}");
                    }
                }
            }
        }
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                if (count > 0)
                    b.AppendLine($"MISSING SCRIPTS {t.name}: {count}");
            }
        }
        b.AppendLine("CAMERAS " + string.Join("; ", scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Camera>(true))
            .Select(c => $"{c.name} enabled={c.enabled} ortho={c.orthographic} FOV={c.fieldOfView} " +
                $"listeners={c.GetComponents<AudioListener>().Length}")));
        File.WriteAllText(Review + "/SceneAudit.txt", b.ToString());
    }

    [MenuItem("Tools/Battle/Presentation/Audit five VFX demo scenes")]
    public static void AuditDemos()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new Exception("Exit Play Mode before demo inspection.");
        Directory.CreateDirectory(Review + "/Demos");
        var report = new StringBuilder();
        string[] paths =
        {
            "Assets/ErbGameArt/Sword slash FX/Demo scene/Slash demo mesh-path(old).unity",
            "Assets/ErbGameArt/Sword slash FX/Demo scene/Slash demo new.unity",
            "Assets/JMO Assets/Cartoon FX (legacy)/Demo/CFX1 Demo.unity",
            "Assets/JMO Assets/Cartoon FX (legacy)/Demo/CFX2 Demo.unity",
            "Assets/JMO Assets/Cartoon FX (legacy)/Demo/CFX3 Demo.unity"
        };
        foreach (var path in paths)
        {
            report.AppendLine("\nSCENE " + path);
            var scene = EditorSceneManager.OpenPreviewScene(path);
            try
            {
                var roots = scene.GetRootGameObjects();
                var components = roots.SelectMany(r => r.GetComponentsInChildren<Component>(true)).ToArray();
                report.AppendLine("Roots=" + roots.Length + "; missing scripts=" + components.Count(c => !c));
                foreach (var m in components.OfType<Renderer>()
                    .SelectMany(r => r.sharedMaterials).Where(m => m).Distinct())
                {
                    report.AppendLine($"Material {AssetDatabase.GetAssetPath(m)}; shader={m.shader.name}; " +
                        $"supported={m.shader.isSupported}; shaderErrors={ShaderUtil.ShaderHasError(m.shader)}");
                }
                foreach (var s in components.OfType<MonoBehaviour>())
                    report.AppendLine("Script " + s.GetType().FullName);
                foreach (var ps in components.OfType<ParticleSystem>())
                {
                    if (!ps.gameObject.activeInHierarchy)
                        continue;
                    ps.Simulate(.18f, false, true, true);
                }
                var camera = components.OfType<Camera>().FirstOrDefault(c => c.enabled) ??
                    components.OfType<Camera>().FirstOrDefault();
                if (camera)
                {
                    camera.scene = scene;
                    RenderReview(camera,
                        Review + "/Demos/" + Path.GetFileNameWithoutExtension(path) + ".png", 1280, 720);
                    report.AppendLine("OPENED and rendered authored camera; " +
                        "active particle systems sampled at 180 ms. " +
                        "This is an Editor preview, not a Play Mode trigger check.");
                }
                else
                    report.AppendLine("No authored camera available.");
            }
            catch (Exception e)
            {
                report.AppendLine("FAILED " + e);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        File.WriteAllText(Review + "/Demos/Audit.txt", report.ToString());
    }

    static void RenderReview(Camera camera, string path, int width, int height)
    {
        var target = RenderTexture.GetTemporary(width, height, 24);
        var originalTarget = camera.targetTexture;
        var active = RenderTexture.active;
        var aspect = camera.aspect;
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.aspect = width / (float)height;
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = originalTarget;
            camera.aspect = aspect;
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(texture);
        }
    }
}
