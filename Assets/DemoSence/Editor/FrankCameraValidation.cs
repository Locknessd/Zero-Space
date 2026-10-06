using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        sealed class CameraValidationRow
        {
            public string key, mode, firstFailure;
            public float aspect, duration, minMargin = float.PositiveInfinity, minDepth = float.PositiveInfinity;
            public float minHeight = float.PositiveInfinity, maxSpeed, maxAcceleration, maxAngularSpeed, maxFovSpeed;
            public int samples, failedSamples, points;
            public Vector3 previousPosition, previousVelocity;
            public Quaternion previousRotation;
            public float previousFov, previousTime;
        }

        struct CameraValidationPose
        {
            public Vector3 position;
            public Quaternion rotation;
            public float fov;
            public CameraValidationPose(Camera camera)
            {
                position = camera.transform.position;
                rotation = camera.transform.rotation;
                fov = camera.fieldOfView;
            }
        }

        [MenuItem("Tools/Frank Retarget/Cameras/Validate and capture all cinematic shots")]
        public static void CameraValidate()
        {
            string reportFolder = Output + "/Reports/Cinematography";
            const string reviewFolder = "/tmp/frank-camera-review";
            Directory.CreateDirectory(reportFolder);
            Directory.CreateDirectory(reviewFolder);
            Directory.CreateDirectory("Temp/FrankRetarget");
            var rows = new List<CameraValidationRow>();
            var failures = new List<string>();
            var captures = new StringBuilder("key,label,sample,time,duration,fov,path\n");
            var endpoints = new StringBuilder("key,attackerLoopDisplacement,receiverLoopDisplacement,cameraLoopDistance,cameraLoopAngle\n");
            var keys = new HashSet<string>();
            var targets = new List<RenderTexture>();
            Scene scene = default;
            FrankCombinationTester tester = null;
            int repeatChecks = 0, imageCount = 0;
            Exception fatal = null;
            try
            {
                scene = EditorSceneManager.OpenPreviewScene(Output + "/Frank_Damages_Mankey_Pepe.unity");
                tester = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<FrankCombinationTester>()).Single();
                var director = tester.cinematicCamera;
                if (!director || !director.library) throw new InvalidOperationException("The saved scene needs a cinematic camera and camera library.");
                director.tester = tester;
                director.cinematic = true;
                var camera = tester.demoCamera;
                if (!camera) throw new InvalidOperationException("The saved tester has no demo camera.");
                camera.scene = scene;
                foreach (var size in new[] { new Vector2Int(960, 720), new Vector2Int(576, 720), new Vector2Int(1280, 720) })
                    targets.Add(RenderTexture.GetTemporary(size.x, size.y, 24, RenderTextureFormat.ARGB32));
                var entries = CameraEntries(tester).ToArray();
                int expected = entries.Length * 2;
                if (expected != 94) failures.Add("Expected 94 authored role-specific shots; the scene exposes " + expected + ".");
                var libraryKeys = new HashSet<string>();
                foreach (var shot in director.library.shots)
                {
                    if (shot == null || string.IsNullOrEmpty(shot.key)) { failures.Add("The camera library contains an empty shot/key."); continue; }
                    if (!libraryKeys.Add(shot.key)) failures.Add("Duplicate camera key: " + shot.key);
                    if (shot.frames == null || shot.frames.Length < 2) failures.Add("Camera has fewer than two frames: " + shot.key);
                }
                if (libraryKeys.Count != expected) failures.Add("Camera library has " + libraryKeys.Count + " unique keys; expected " + expected + ".");

                foreach (var entry in entries)
                    for (int role = 0; role < 2; role++)
                    {
                        CameraValidationSelect(tester, director, entry.family, entry.index, role);
                        string key = FrankCinematicCamera.CurrentKey(tester);
                        keys.Add(key);
                        var shot = director.library.Find(key);
                        if (shot == null || shot.frames == null || shot.frames.Length < 2)
                        { failures.Add("Missing playable shot: " + key); continue; }
                        if (Mathf.Abs(shot.duration - tester.Duration) > .04f)
                            failures.Add(key + " duration differs from its animation pair.");
                        if (shot.frames[0].time > .001f || shot.frames[shot.frames.Length - 1].time + .001f < tester.Duration)
                            failures.Add(key + " does not cover the complete animation duration.");
                        for (int i = 1; i < shot.frames.Length; i++)
                            if (shot.frames[i].time <= shot.frames[i - 1].time)
                            { failures.Add(key + " has non-increasing frame times."); break; }

                        for (int aspectIndex = 0; aspectIndex < targets.Count; aspectIndex++)
                        {
                            var target = targets[aspectIndex];
                            var row = new CameraValidationRow { key = key, mode = "seek", aspect = (float)target.width / target.height, duration = tester.Duration };
                            int steps = Mathf.Max(1, Mathf.CeilToInt(tester.Duration * (aspectIndex == 0 ? 30 : 5)));
                            for (int sample = 0; sample <= steps; sample++)
                            {
                                float time = Mathf.Min(tester.Duration, tester.Duration * sample / steps);
                                CameraValidationEvaluate(tester, director, target, time, true, 0);
                                CameraValidationInspect(camera, director.LastFramingPoints, time, row);
                            }
                            rows.Add(row);
                            if (row.failedSamples > 0) failures.Add(key + " at aspect " + CameraValidationNumber(row.aspect) + ": " + row.firstFailure);
                        }

                        // Seek away and return in a different order; history must not affect a paused shot.
                        float[] repeatTimes = { tester.Duration * .37f, tester.Duration * .81f, tester.Duration * .12f };
                        var expectedPoses = new CameraValidationPose[repeatTimes.Length];
                        for (int i = 0; i < repeatTimes.Length; i++)
                        {
                            CameraValidationEvaluate(tester, director, targets[0], repeatTimes[i], true, 0);
                            expectedPoses[i] = new CameraValidationPose(camera);
                        }
                        for (int i = repeatTimes.Length - 1; i >= 0; i--)
                        {
                            CameraValidationEvaluate(tester, director, targets[0], repeatTimes[i], true, 0);
                            var expectedPose = expectedPoses[i];
                            repeatChecks++;
                            if (Vector3.Distance(expectedPose.position, camera.transform.position) > .00001f ||
                                Quaternion.Angle(expectedPose.rotation, camera.transform.rotation) > .001f ||
                                Mathf.Abs(expectedPose.fov - camera.fieldOfView) > .00001f)
                                failures.Add(key + " is not deterministic at " + CameraValidationNumber(repeatTimes[i]) + " seconds.");
                        }

                        CameraValidationEvaluate(tester, director, targets[0], 0, true, 0);
                        Vector3 startAttacker = tester.Attacker.Pose.targetHips.position, startReceiver = tester.Receiver.Pose.targetHips.position;
                        var startCamera = new CameraValidationPose(camera);
                        CameraValidationEvaluate(tester, director, targets[0], tester.Duration, true, 0);
                        endpoints.AppendLine(CameraValidationCsv(key) + "," + CameraValidationNumber(Vector3.Distance(startAttacker, tester.Attacker.Pose.targetHips.position)) + "," +
                            CameraValidationNumber(Vector3.Distance(startReceiver, tester.Receiver.Pose.targetHips.position)) + "," +
                            CameraValidationNumber(Vector3.Distance(startCamera.position, camera.transform.position)) + "," +
                            CameraValidationNumber(Quaternion.Angle(startCamera.rotation, camera.transform.rotation)));

                        var loopRow = new CameraValidationRow { key = key, mode = "loop blend", aspect = 4f / 3, duration = tester.Duration };
                        for (int sample = 0; sample <= 84; sample++)
                        {
                            float time = Mathf.Min(tester.Duration, sample / 60f);
                            CameraValidationEvaluate(tester, director, targets[0], time, false, 1f / 60);
                            CameraValidationInspect(camera, director.LastFramingPoints, time, loopRow);
                        }
                        rows.Add(loopRow);
                        if (loopRow.failedSamples > 0) failures.Add(key + " loop blend: " + loopRow.firstFailure);

                        float[] phases = { 0, .18f, .35f, .55f, .75f, .94f };
                        for (int i = 0; i < phases.Length; i++)
                        {
                            float time = tester.Duration * phases[i];
                            CameraValidationEvaluate(tester, director, targets[0], time, true, 0);
                            string path = reviewFolder + "/" + entry.family + "-" + entry.index + "-" + (role == 0 ? "mankey" : "pepe") + "-" + i.ToString("00") + ".png";
                            // Warm the preview scene shadow state before the captured render.
                            camera.Render();
                            Capture(camera, path, 960, 720);
                            captures.AppendLine(CameraValidationCsv(key) + "," + CameraValidationCsv(entry.label) + "," + i + "," +
                                CameraValidationNumber(time) + "," + CameraValidationNumber(tester.Duration) + "," + CameraValidationNumber(camera.fieldOfView) + "," + CameraValidationCsv(path));
                            imageCount++;
                        }
                        File.WriteAllText("Temp/FrankRetarget/camera-validation-progress", keys.Count + "/" + expected + " shots; " + imageCount + " review frames");
                        Debug.Log("Camera checked " + key + " " + entry.label);
                    }

                // Change actual library selection with blending enabled, including both role assignments.
                foreach (var entry in entries)
                    for (int role = 0; role < 2; role++)
                    {
                        CameraValidationSelect(tester, director, entry.family, entry.index, role);
                        string key = FrankCinematicCamera.CurrentKey(tester);
                        if (director.library.Find(key) == null) continue;
                        var row = new CameraValidationRow { key = key, mode = "selection blend", aspect = 4f / 3, duration = tester.Duration };
                        for (int sample = 0; sample <= 84; sample++)
                        {
                            float time = Mathf.Min(tester.Duration, sample / 60f);
                            CameraValidationEvaluate(tester, director, targets[0], time, false, 1f / 60);
                            CameraValidationInspect(camera, director.LastFramingPoints, time, row);
                        }
                        rows.Add(row);
                        if (row.failedSamples > 0) failures.Add(key + " selection blend: " + row.firstFailure);
                    }
            }
            catch (Exception exception)
            {
                fatal = exception;
                failures.Add("Validation stopped: " + exception);
            }
            finally
            {
                if (tester)
                {
                    if (tester.demoCamera) tester.demoCamera.targetTexture = null;
                    if (tester.mankey) tester.mankey.Clear();
                    if (tester.pepe) tester.pepe.Clear();
                }
                foreach (var target in targets) RenderTexture.ReleaseTemporary(target);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            }

            var csv = new StringBuilder("key,mode,aspect,duration,samples,failedSamples,projectedPoints,minViewportMargin,minDepth,minCameraHeight,maxSpeed,maxAcceleration,maxAngularSpeed,maxFovSpeed\n");
            foreach (var row in rows)
                csv.AppendLine(CameraValidationCsv(row.key) + "," + CameraValidationCsv(row.mode) + "," +
                    CameraValidationNumber(row.aspect) + "," + CameraValidationNumber(row.duration) + "," + row.samples + "," + row.failedSamples + "," + row.points + "," +
                    CameraValidationNumber(row.minMargin) + "," + CameraValidationNumber(row.minDepth) + "," + CameraValidationNumber(row.minHeight) + "," +
                    CameraValidationNumber(row.maxSpeed) + "," + CameraValidationNumber(row.maxAcceleration) + "," +
                    CameraValidationNumber(row.maxAngularSpeed) + "," + CameraValidationNumber(row.maxFovSpeed));
            var report = new StringBuilder("# Cinematic camera mathematical validation\n\n");
            report.AppendLine("Result: **" + (failures.Count == 0 ? "PASS" : "FAIL") + "**. Unity " + Application.unityVersion + ".\n");
            report.AppendLine("This report verifies camera projection and playback numerically. It is not a visual cinematography approval. Composition, readability of overlapping silhouettes, dramatic timing, lighting and perceived smoothness require reviewing the captured images and motion.\n");
            report.AppendLine("- Role-specific camera keys visited: " + keys.Count + ".");
            report.AppendLine("- Camera samples checked: " + rows.Sum(x => x.samples) + "; projected geometry corners: " + rows.Sum(x => (long)x.points) + ".");
            report.AppendLine("- Repeat seeks checked: " + repeatChecks + "; review captures: " + imageCount + ".");
            report.AppendLine("- Full-duration samples: 30 fps at 960 × 720; 5 fps at 576 × 720 and 1280 × 720, including each clip endpoint.");
            report.AppendLine("- Selection and loop blends: 60 fps for the first 1.4 seconds at 960 × 720.");
            report.AppendLine("- Requirements: finite perspective camera transform/FOV; at least 0.049 viewport margin; geometry depth above 0.025; camera height at least 0.299.\n");
            if (rows.Count > 0)
            {
                report.AppendLine("Minimum viewport margin: " + CameraValidationNumber(rows.Min(x => x.minMargin)) + ". Minimum geometry depth: " + CameraValidationNumber(rows.Min(x => x.minDepth)) + ". Minimum camera height: " + CameraValidationNumber(rows.Min(x => x.minHeight)) + ".\n");
                report.AppendLine("Maximum observed camera speed: " + CameraValidationNumber(rows.Max(x => x.maxSpeed)) + " units/s; acceleration: " + CameraValidationNumber(rows.Max(x => x.maxAcceleration)) + " units/s²; angular speed: " + CameraValidationNumber(rows.Max(x => x.maxAngularSpeed)) + " degrees/s. These are diagnostics, not universal pass/fail thresholds.\n");
            }
            report.AppendLine("Per-shot and aspect measurements are in `Validation.csv`. Source pelvis displacement at loop boundaries and endpoint camera differences are in `LoopEndpoints.csv`; these diagnose source resets separately from blended playback. Six review frames per key are listed with time and FOV in `/tmp/frank-camera-review/Captures.csv`.\n");
            if (failures.Count > 0)
            {
                report.AppendLine("## Failures\n");
                foreach (string failure in failures) report.AppendLine("- " + failure.Replace("\n", " "));
            }
            File.WriteAllText(reportFolder + "/Validation.md", report.ToString().TrimEnd() + "\n");
            File.WriteAllText(reportFolder + "/Validation.csv", csv.ToString());
            File.WriteAllText(reportFolder + "/LoopEndpoints.csv", endpoints.ToString());
            File.WriteAllText(reviewFolder + "/Captures.csv", captures.ToString());
            AssetDatabase.ImportAsset(reportFolder + "/Validation.md");
            AssetDatabase.ImportAsset(reportFolder + "/Validation.csv");
            AssetDatabase.ImportAsset(reportFolder + "/LoopEndpoints.csv");
            if (failures.Count > 0) throw new InvalidOperationException("Camera validation failed with " + failures.Count + " findings; see " + reportFolder + "/Validation.md", fatal);
            Debug.Log("Camera validation passed: " + keys.Count + " shots, " + rows.Sum(x => x.samples) + " camera samples, " + repeatChecks + " repeat seeks, " + imageCount + " review frames.");
        }

        static void CameraValidationSelect(FrankCombinationTester tester, FrankCinematicCamera director, string family, int index, int role)
        {
            director.cinematic = false;
            try { SelectCameraEntry(tester, family, index, role); }
            finally { director.cinematic = true; }
        }

        static void CameraValidationEvaluate(FrankCombinationTester tester, FrankCinematicCamera director, RenderTexture target, float time, bool immediate, float deltaTime)
        {
            director.cinematic = false;
            try { tester.Seek(time); }
            finally { director.cinematic = true; }
            tester.demoCamera.targetTexture = target;
            tester.demoCamera.rect = new Rect(0, 0, 1, 1);
            if (!director.Apply(deltaTime, immediate)) throw new InvalidOperationException("Camera director could not play " + FrankCinematicCamera.CurrentKey(tester));
        }

        static void CameraValidationInspect(Camera camera, IReadOnlyList<Vector3> points, float time, CameraValidationRow row)
        {
            string problem = null;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            if (!CameraValidationFinite(position.x, position.y, position.z, rotation.x, rotation.y, rotation.z, rotation.w, camera.fieldOfView))
                problem = "non-finite camera transform/FOV";
            else if (camera.orthographic || camera.fieldOfView < 20 || camera.fieldOfView > 75)
                problem = "invalid perspective lens";
            else if (position.y < .299f) problem = "camera is below the stage clearance";
            if (points.Count == 0) problem = "no visible geometry points";
            row.minHeight = Mathf.Min(row.minHeight, position.y);
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 viewport = camera.WorldToViewportPoint(points[i]);
                float margin = Mathf.Min(Mathf.Min(viewport.x, 1 - viewport.x), Mathf.Min(viewport.y, 1 - viewport.y));
                row.minMargin = Mathf.Min(row.minMargin, margin);
                row.minDepth = Mathf.Min(row.minDepth, viewport.z);
                row.points++;
                if (!CameraValidationFinite(viewport.x, viewport.y, viewport.z)) problem = "non-finite projected geometry";
                else if (margin < .049f || viewport.z <= .025f)
                    problem = "geometry margin=" + CameraValidationNumber(margin) + ", depth=" + CameraValidationNumber(viewport.z);
            }
            float dt = time - row.previousTime;
            if (row.samples > 0 && dt > .001f)
            {
                Vector3 velocity = (position - row.previousPosition) / dt;
                if (velocity.magnitude > 20 && row.aspect > 1.3f && row.aspect < 1.4f && row.mode != "seek")
                    Debug.Log("Camera transition diagnostic " + row.key + " " + row.mode + " t=" + time + " speed=" + velocity.magnitude + " from=" + row.previousPosition + " to=" + position);
                row.maxSpeed = Mathf.Max(row.maxSpeed, velocity.magnitude);
                if (row.samples > 1) row.maxAcceleration = Mathf.Max(row.maxAcceleration, (velocity - row.previousVelocity).magnitude / dt);
                row.maxAngularSpeed = Mathf.Max(row.maxAngularSpeed, Quaternion.Angle(row.previousRotation, rotation) / dt);
                row.maxFovSpeed = Mathf.Max(row.maxFovSpeed, Mathf.Abs(camera.fieldOfView - row.previousFov) / dt);
                row.previousVelocity = velocity;
            }
            row.previousPosition = position;
            row.previousRotation = rotation;
            row.previousFov = camera.fieldOfView;
            row.previousTime = time;
            row.samples++;
            if (problem != null)
            {
                row.failedSamples++;
                if (row.firstFailure == null) row.firstFailure = "t=" + CameraValidationNumber(time) + ": " + problem;
            }
        }

        static bool CameraValidationFinite(params float[] values)
        {
            foreach (float value in values) if (!float.IsFinite(value)) return false;
            return true;
        }

        static string CameraValidationNumber(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);
        static string CameraValidationCsv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    }
}
