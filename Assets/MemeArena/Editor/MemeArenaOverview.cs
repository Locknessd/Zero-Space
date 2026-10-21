using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MemeArenaEditor
{
    public static class MemeArenaOverview
    {
        public static void Capture()
        {
            string review = MemeArenaBuild.Root + "/Documentation/Review";
            Directory.CreateDirectory(review);
            var scene = EditorSceneManager.OpenPreviewScene(MemeArenaIntegration.LatestScene);
            var cameraObject = new GameObject("Temporary arena overview camera");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.scene = scene;
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.transform.position = new Vector3(0, 22, 52);
            camera.transform.LookAt(new Vector3(0, 5, -8));
            camera.fieldOfView = 55;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 180;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.6f, .56f, .52f);
            var target = new RenderTexture(1600, 900, 24);
            var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                target.Create();
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                File.WriteAllBytes(review + "/ColosseumOverview.png", image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(image);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
