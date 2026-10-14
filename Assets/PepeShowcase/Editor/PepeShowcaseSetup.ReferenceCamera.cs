using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using ZeroSpace.Showcase;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        [MenuItem("Tools/Pepe Showcase/Apply cinematic attack reference camera")]
        public static void ApplyBomReferenceCamera()
        {
            const string path = PepeShowcaseSetup.BomScenePath;
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var showcase = scene.GetRootGameObjects().Select(root => root.GetComponent<PepeAttackShowcase>())
                    .Single(item => item);
                var director = showcase.director;
                Undo.RecordObject(director, "Apply cinematic attack reference camera");
                director.cinematicAttackReference = true;
                director.referenceDistance = 6.5f;
                director.referenceFollowSmoothing = .14f;
                director.referenceOrbitStrength = 1;
                EditorUtility.SetDirty(director);
                EditorSceneManager.MarkSceneDirty(scene);
                if (opened && !EditorSceneManager.SaveScene(scene))
                    throw new IOException("Could not save BOM reference camera.");
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }
    }
}
