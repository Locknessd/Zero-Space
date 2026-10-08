using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    public static class CombatExpansionSceneSnapshot
    {
        public static void Capture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Capture unsaved state in Edit Mode.");
            string folder = "Library/CombatExpansionTools/SceneSnapshots/" +
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            Directory.CreateDirectory(folder);
            var report = new StringBuilder();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isDirty)
                    continue;
                string copy = folder + "/" + i + "_" + scene.name + ".unity";
                if (!EditorSceneManager.SaveScene(scene, copy, true))
                    throw new IOException("Could not preserve " + scene.path);
                report.AppendLine(scene.path + "\t" + copy);
            }
            File.WriteAllText("Library/CombatExpansionTools/SceneSnapshot.txt", report.ToString());
        }
    }
}
