using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        const string BattleCameraReview = "Temp/FrankRetarget/BattleCamera";

        [MenuItem("Tools/Battle/Install cinematic camera")]
        public static void InstallBattleCamera()
        {
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/BattleScene.unity");
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Additive);
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true))
                    .Single(c => c.CompareTag("MainCamera"));
                var library = AssetDatabase.LoadAssetAtPath<FrankCameraLibrary>(CameraLibraryPath);
                if (!library || !game.leftCombat || !game.rightCombat) throw new Exception("Missing battle fighters or camera library.");
                foreach (var move in new[] { game.leftCombat, game.rightCombat }.SelectMany(f => f.lightCombatMoves.Concat(f.heavyCombatMoves)))
                    if (library.Find(FrankCinematicCamera.BattleKey(move)) == null)
                        throw new Exception("Missing camera take: " + move.moveName);
                var director = camera.GetComponent<FrankCinematicCamera>();
                if (!director) director = Undo.AddComponent<FrankCinematicCamera>(camera.gameObject);
                Undo.RecordObjects(new Object[] { camera, camera.transform, director }, "Install battle cinematic camera");
                var previous = camera.GetComponent<MortalKombatCamera>();
                if (previous)
                {
                    Undo.RecordObject(previous, "Replace battle camera driver");
                    previous.enabled = false;
                    EditorUtility.SetDirty(previous);
                }
                director.enabled = true;
                director.tester = null;
                director.battle = game;
                director.library = library;
                director.cinematic = true;
                director.keepBattleCameraInFront = true;
                camera.rect = new Rect(0, 0, 1, 1);
                director.ResetView();
                if (!director.Apply(0, true)) throw new Exception("Battle cinematic camera did not initialize.");
                EditorUtility.SetDirty(camera);
                EditorUtility.SetDirty(director);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Could not save BattleScene camera.");
                Directory.CreateDirectory(BattleCameraReview);
                File.WriteAllText(BattleCameraReview + "/install.txt",
                    "Installed FrankCinematicCamera on Main Camera, bound to Battle GameManager and authored camera library.\n" +
                    "MortalKombatCamera disabled; source-pair shots use the live animation clock and battle placement.\n");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        [MenuItem("Tools/Battle/Validate cinematic camera")]
        public static void ValidateBattleCamera()
        {
            Directory.CreateDirectory(BattleCameraReview);
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
            var report = new StringBuilder();
            var landscape = new RenderTexture(1280, 720, 24);
            var portrait = new RenderTexture(720, 1280, 24);
            Camera camera = null;
            int cases = 0, samples = 0;
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>()).Single(c => c.CompareTag("MainCamera"));
                camera.scene = scene;
                var director = camera.GetComponent<FrankCinematicCamera>();
                if (!director || !director.enabled || director.battle != game || director.tester || !director.library)
                    throw new Exception("Saved camera bindings are invalid.");
                var previous = camera.GetComponent<MortalKombatCamera>();
                if (previous && previous.enabled) throw new Exception("Two camera drivers are enabled.");
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var fighter in fighters) if (!fighter.Initialize()) throw new Exception("Fighter initialization failed.");
                camera.targetTexture = landscape;
                director.Apply(0, true);
                InspectBattleCameraFrame(camera, director);
                CaptureBattleCamera(camera, BattleCameraReview + "/Idle.png");
                foreach (var attacker in fighters)
                foreach (var move in attacker.lightCombatMoves.Concat(attacker.heavyCombatMoves))
                foreach (bool mirrored in new[] { false, true })
                foreach (var target in new[] { landscape, portrait })
                {
                    foreach (var fighter in fighters) fighter.ResetCombat();
                    var receiver = fighters.Single(f => f != attacker);
                    attacker.transform.position = new Vector3(mirrored ? .5f : -.5f, 0, 0);
                    receiver.transform.position = attacker.transform.position + Vector3.right * (mirrored ? -move.attackRange : move.attackRange);
                    camera.targetTexture = target;
                    director.ResetView();
                    if (!attacker.ExecuteAttack(move, receiver)) throw new Exception("Attack rejected: " + move.moveName);
                    var playback = attacker.SourcePlayback;
                    var expected = director.library.Find(FrankCinematicCamera.BattleKey(move));
                    if (Mathf.Abs(expected.duration - playback.Duration) > .04f) throw new Exception("Shot duration differs from animation: " + expected.key);
                    int steps = Mathf.CeilToInt(playback.Duration * 15);
                    Vector3 firstPosition = default;
                    for (int sample = 0; sample <= steps; sample++)
                    {
                        float seconds = playback.Duration * sample / steps;
                        playback.EvaluateAt(seconds);
                        if (!director.Apply(0, true) || director.ActiveShot != expected)
                            throw new Exception("Wrong or missing battle shot: " + expected.key);
                        if (camera.transform.position.z <= 0) throw new Exception("Camera crossed behind the battle backdrop.");
                        InspectBattleCameraFrame(camera, director);
                        samples++;
                        if (sample == 0) firstPosition = camera.transform.position;
                        if (!mirrored && target == landscape && (sample == steps / 2 || sample == steps))
                            CaptureBattleCamera(camera, BattleCameraReview + "/" + attacker.name + "_" + move.moveName +
                                (sample == steps ? "_end" : "_action") + ".png");
                    }
                    if (Vector3.Distance(firstPosition, camera.transform.position) < .01f)
                        throw new Exception("Shot did not follow its animation: " + expected.key);
                    playback.Cancel();
                    if (!director.Apply(0, true) || director.ActiveShot != null) throw new Exception("Camera did not return to idle after cancellation.");
                    InspectBattleCameraFrame(camera, director);
                    report.AppendLine("PASS " + expected.key + "; mirrored=" + mirrored + "; aspect=" + camera.aspect.ToString("F3"));
                    cases++;
                }
                ValidateDemoCameraCompatibility(report);
                report.AppendLine($"PASS {cases} battle cases, {samples} camera frames: both attackers, both directions, landscape/portrait, cancellation and neutral framing.");
                File.WriteAllText(BattleCameraReview + "/validation.txt", report.ToString());
            }
            catch (Exception exception)
            {
                File.WriteAllText(BattleCameraReview + "/validation.txt", report + "FAIL " + exception);
                throw;
            }
            finally
            {
                if (camera) camera.targetTexture = null;
                Object.DestroyImmediate(landscape);
                Object.DestroyImmediate(portrait);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static void InspectBattleCameraFrame(Camera camera, FrankCinematicCamera director)
        {
            if (!float.IsFinite(camera.transform.position.sqrMagnitude) || camera.transform.position.y < director.minimumHeight - .001f)
                throw new Exception("Invalid camera position or floor clearance.");
            if (director.LastFramingPoints.Count == 0) throw new Exception("Camera has no fighter framing geometry.");
            foreach (var point in director.LastFramingPoints)
            {
                var viewport = camera.WorldToViewportPoint(point);
                if (viewport.z <= .03f || viewport.x < .049f || viewport.x > .951f || viewport.y < .049f || viewport.y > .951f)
                    throw new Exception("Body or weapon outside safe frame: " + viewport);
            }
        }

        static void CaptureBattleCamera(Camera camera, string path)
        {
            var target = camera.targetTexture;
            var previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }

        static void ValidateDemoCameraCompatibility(StringBuilder report)
        {
            var demo = EditorSceneManager.OpenPreviewScene(Output + "/Frank_Damages_Mankey_Pepe.unity");
            var target = new RenderTexture(1280, 720, 24);
            try
            {
                var tester = demo.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FrankCombinationTester>()).Single();
                tester.demoCamera.targetTexture = target;
                foreach (bool pepe in new[] { false, true })
                {
                    tester.pepeAttacks = pepe;
                    tester.unarmed = true;
                    tester.gunSword = tester.greatSword = false;
                    tester.unarmedMotion = 7;
                    tester.Configure();
                    tester.cinematicCamera.ResetView();
                    tester.Seek(tester.Duration * .5f);
                    if (tester.cinematicCamera.ActiveShot != tester.cinematicCamera.library.Find(FrankCinematicCamera.CurrentKey(tester)))
                        throw new Exception("Demo camera no longer selects the authored shot after reset.");
                    InspectBattleCameraFrame(tester.demoCamera, tester.cinematicCamera);
                }
                report.AppendLine("PASS demo camera after reset and seek with either attacker.");
            }
            finally { EditorSceneManager.ClosePreviewScene(demo); Object.DestroyImmediate(target); }
        }
    }
}
