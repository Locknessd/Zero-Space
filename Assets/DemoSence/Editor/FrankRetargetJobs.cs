using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    // Local, explicit file jobs allow the same checks in an already-open editor or batch mode.
    [InitializeOnLoad]
    public static class FrankRetargetJobs
    {
        const string Jobs = "Temp/FrankRetarget/";
        static FrankRetargetJobs() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Jobs + "request")) return;
            string job = File.ReadAllText(Jobs + "request").Trim();
            File.Delete(Jobs + "request");
            if(File.Exists(Jobs+"error"))File.Delete(Jobs+"error");
            if(File.Exists(Jobs+"done"))File.Delete(Jobs+"done");
            try
            {
                // Asset jobs must run in edit mode because they open/save scenes and rewrite imported
                // meshes.  If the user left BattleScene in Play mode, stop it automatically and put the
                // request back for the next editor tick instead of silently reporting a failed repair.
                if (EditorApplication.isPlaying && job != "status" && job != "refresh" && job != "stop")
                {
                    EditorApplication.isPlaying = false;
                    File.WriteAllText(Jobs + "request", job);
                    File.WriteAllText(Jobs + "done", job + " queued until Play mode stops");
                    return;
                }
                if (job == "stop") EditorApplication.isPlaying=false;
                else if (job == "refresh") AssetDatabase.Refresh();
                else if (job == "status") File.WriteAllText(Jobs+"status", "playing="+EditorApplication.isPlaying+" scene="+UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
                else if (job == "inspect") Inspect();
                else
                {
                    var method = typeof(FrankRetargetJobs).Assembly.GetType("FrankRetarget.Editor.FrankRetargetBuilder")?.GetMethod(job);
                    if (method == null) throw new Exception("Unknown job " + job);
                    method.Invoke(null, null);
                }
                File.WriteAllText(Jobs + "done", job + " completed");
            }
            catch (Exception e) { File.WriteAllText(Jobs + "error", e.ToString()); Debug.LogException(e); }
        }
        public static void Inspect()
        {
            var sb = new StringBuilder();
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Selected/Frank_Damages/Demo/Frank_Damages.unity");
            foreach (var root in scene.GetRootGameObjects())
            {
                sb.AppendLine("ROOT " + root.name + " position=" + root.transform.position + " scale=" + root.transform.localScale);
                foreach (var a in root.GetComponentsInChildren<Animator>(true))
                {
                    sb.AppendLine(" ANIMATOR " + a.name + " avatar=" + a.avatar + " human=" + a.isHuman + " rootMotion=" + a.applyRootMotion + " controller=" + AssetDatabase.GetAssetPath(a.runtimeAnimatorController));
                    if(a.runtimeAnimatorController) foreach(var c in a.runtimeAnimatorController.animationClips.Distinct())
                        sb.AppendLine("  CLIP " + c.name + " length=" + c.length + " human=" + c.humanMotion + " loop=" + c.isLooping + " events=" + c.events.Length + " path=" + AssetDatabase.GetAssetPath(c));
                }
                foreach(var r in root.GetComponentsInChildren<Renderer>(true))
                    sb.AppendLine(" RENDER " + AnimationUtility.CalculateTransformPath(r.transform, root.transform) + " " + r.GetType().Name + " materials=" + string.Join(",",r.sharedMaterials.Select(m=>m?m.name+":"+m.shader.name:"null")) + (r is SkinnedMeshRenderer sk ? " bones="+string.Join(",",sk.bones.Select(b=>b?b.name:"null")) : ""));
                sb.AppendLine(" COMPONENTS " + string.Join(",", root.GetComponentsInChildren<Component>(true).Where(c=>c && !(c is Transform)).Select(c=>c.GetType().Name).GroupBy(x=>x).Select(g=>g.Key+"="+g.Count())));
            }
            EditorSceneManager.ClosePreviewScene(scene);
            foreach(string path in new[]{"Assets/Mankeys_sushishop/FBX/Mankey.FBX", "Assets/pepe/source/pepFBX@Taunt.fbx", "Assets/Selected/Frank_Damages/Asset/Meshes/Frank_Damage_FS3_Skin.FBX", "Assets/Selected/Frank_Damages/Asset/Meshes/Frank_Damage@Damage_Critical_Spear.FBX"})
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                sb.AppendLine("MODEL " + path);
                foreach(var t in model.GetComponentsInChildren<Transform>(true)) sb.AppendLine(" " + AnimationUtility.CalculateTransformPath(t,model.transform) + " pos=" + t.localPosition + " rot="+t.localEulerAngles+" scale="+t.localScale);
                foreach(var a in model.GetComponentsInChildren<Animator>(true)) sb.AppendLine(" AVATAR " + a.avatar + " valid=" + (a.avatar && a.avatar.isValid) + " human=" + a.isHuman);
            }
            File.WriteAllText(Jobs + "inspection.txt",sb.ToString());
        }
    }
}
