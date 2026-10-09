using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        public static void CaptureNativePairReference()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            var active = SceneManager.GetActiveScene();
            var selection = Selection.objects;
            var activeSelection = Selection.activeObject;
            var report = new NativeReferenceReport();
            string snapshot = null;
            Directory.CreateDirectory(NativeReferenceOutput);
            File.WriteAllText(NativeReferenceOutput + "/Scope.txt", NativeReferenceScope);
            try
            {
                report.sources = ResolveSources();
                snapshot = NativeReferenceSourceSnapshot(report.sources);
                report.guardedFiles = NativeReferenceGuard(report.sources);
                foreach (int execution in new[] { 1, 2, 3 })
                foreach (int direction in new[] { 1, -1 })
                {
                    var record = new NativeReferenceCase { execution = execution, direction = direction };
                    report.cases.Add(record);
                    NativeReferenceFlush(report);
                    try
                    {
                        NativeReferenceCapture(record, report.sources);
                        record.status = "CAPTURED native source only; no damaging contacts approved";
                    }
                    catch (Exception error)
                    {
                        record.status = "FAILED; partial evidence invalid";
                        record.error = error.ToString();
                        throw;
                    }
                    finally
                    {
                        NativeReferenceFlush(report);
                    }
                }
                report.status = "CAPTURED all six native source reference cases";
            }
            catch (Exception error)
            {
                report.status = "FAILED; partial evidence is not a completed capture";
                report.error = error.ToString();
                throw;
            }
            finally
            {
                try
                {
                    if (snapshot != null)
                    {
                        if (snapshot != NativeReferenceSourceSnapshot(ResolveSources()) ||
                            report.guardedFiles == null || report.guardedFiles.Any(f =>
                                !File.Exists(f.path) || NativeReferenceHash(f.path) != f.sha256))
                            throw new InvalidOperationException("Native source bytes, identities or settings changed.");
                        report.sourceGuardsPassed = true;
                    }
                }
                catch (Exception error)
                {
                    report.status = "FAILED source preservation guard";
                    report.error += "\n" + error;
                    throw;
                }
                finally
                {
                    if (active.IsValid() && active.isLoaded)
                        SceneManager.SetActiveScene(active);
                    Selection.objects = selection;
                    Selection.activeObject = activeSelection;
                    NativeReferenceFlush(report);
                }
            }
        }

        static void NativeReferenceFlush(NativeReferenceReport report)
        {
            File.WriteAllText(NativeReferenceOutput + "/Report.json", JsonUtility.ToJson(report, true));
            File.WriteAllText(NativeReferenceOutput + "/Status.txt", report.status + "\n" + report.error);
        }

        static void NativeReferenceCapture(NativeReferenceCase record, SourceRecord[] sources,
            string outputDirectory = null, AnimationClip attackOverride = null, AnimationClip receiverOverride = null)
        {
            string output = outputDirectory ?? NativeReferenceOutput;
            Directory.CreateDirectory(output);
            var scene = EditorSceneManager.NewPreviewScene();
            var actors = new List<NativeReferenceActor>();
            bool provisional = attackOverride || receiverOverride;
            string stem = (provisional ? "PROVISIONAL_OrientationVariant_" : "") + "NativeReference_Execution" + record.execution.ToString("D2") + "_" +
                (record.direction > 0 ? "Positive" : "Negative");
            try
            {
                for (int role = 0; role < 2; role++)
                    actors.Add(new NativeReferenceActor(scene, sources.Single(s => s.role == Roles[role] &&
                        s.localId == 7400000 + record.execution * 2), role, record.direction,
                        role == 0 ? attackOverride : receiverOverride));
                var pair = actors.ToArray();
                record.prefabs = pair.Select(a => a.Source.path).ToArray();
                record.avatars = pair.Select(a => CombatExpansionInventory.Identity(a.Animator.avatar)).ToArray();
                record.prefabScales = pair.Select(a => a.PrefabScale).ToArray();
                record.animatorLossyScales = pair.Select(a => a.Animator.transform.lossyScale).ToArray();
                record.avatarHumanScales = pair.Select(a => a.Animator.humanScale).ToArray();
                if (record.avatarHumanScales.Any(v => !float.IsFinite(v) || v <= 0))
                    throw new InvalidOperationException("Invalid native Avatar scale.");
                record.attackDuration = pair[0].Source.durationSeconds;
                record.receiverDuration = pair[1].Source.durationSeconds;
                record.duration = Mathf.Max(record.attackDuration, record.receiverDuration);
                if (!float.IsFinite(record.duration) || record.duration <= 0)
                    throw new InvalidOperationException("Invalid native clip duration.");
                var probe = new CombatExpansionAxeDenseStudy.NativePairProbe(pair[0].Animator, pair[1].Animator);
                record.bladeMesh = probe.BladeMeshIdentity;
                record.bodyMeshes = probe.MeshIdentities;
                var bounds = new Bounds(Vector3.zero, Vector3.zero);
                int steps = Mathf.CeilToInt(record.duration * 30);
                var sampleTimes = Enumerable.Range(0, steps + 1)
                    .Select(i => Mathf.Min(i / 30f, record.duration))
                    .Concat(new[] { record.attackDuration, record.receiverDuration })
                    .Concat(new[] { .25f, .5f, .75f }.Select(f => f * record.duration))
                    .Distinct().OrderBy(t => t).ToArray();
                using (var csv = new StreamWriter(output + "/" + stem + "_Gaps.csv"))
                {
                    csv.WriteLine("seconds,gap_m,blade_x,blade_y,blade_z,body_x,body_y,body_z");
                    for (int step = 0; step < sampleTimes.Length; step++)
                    {
                        float seconds = sampleTimes[step];
                        foreach (var actor in pair)
                            actor.Evaluate(seconds);
                        var diagnostics = new List<string>();
                        var contact = probe.Measure(diagnostics);
                        var frame = new NativeReferenceFrame
                        {
                            seconds = seconds, gap = contact.gap, bladePoint = contact.sourcePoint,
                            bodyPoint = contact.bodyPoint, poses = NativeReferencePoses(pair)
                        };
                        record.frames.Add(frame);
                        if (step == 0)
                            record.geometry.AddRange(diagnostics);
                        bounds.Encapsulate(probe.BodyBounds);
                        foreach (var renderer in pair.SelectMany(a =>
                            a.Animator.GetComponentsInChildren<Renderer>(true)).Where(r => r.enabled))
                        {
                            var box = renderer.bounds;
                            for (int axis = 0; axis < 3; axis++)
                                if (!float.IsFinite(box.center[axis]) || !float.IsFinite(box.size[axis]))
                                    throw new InvalidOperationException("Nonfinite native render bounds.");
                            bounds.Encapsulate(box);
                        }
                        csv.WriteLine(string.Join(",", new[] { seconds, contact.gap, contact.sourcePoint.x,
                            contact.sourcePoint.y, contact.sourcePoint.z, contact.bodyPoint.x,
                            contact.bodyPoint.y, contact.bodyPoint.z }.Select(v =>
                                v.ToString("R", CultureInfo.InvariantCulture))));
                    }
                }
                record.start = record.frames.First();
                record.end = record.frames.Last();
                record.minimum = record.frames.OrderBy(f => f.gap).First();
                var times = new List<float> { 0, record.duration, record.minimum.seconds };
                for (int index = 1; index < record.frames.Count - 1; index++)
                    if (record.frames[index].gap <= record.frames[index - 1].gap &&
                        record.frames[index].gap < record.frames[index + 1].gap)
                        times.Add(record.frames[index].seconds);
                foreach (float fraction in new[] { .25f, .5f, .75f })
                    times.Add(record.duration * fraction);
                times.Add(record.attackDuration);
                times.Add(record.receiverDuration);
                record.sheetSeconds = times.Distinct().OrderBy(t => t).ToArray();
                bounds.Expand(.3f);
                using var rendering = new NativeReferenceRendering(scene, pair);
                var sheets = new List<string>();
                foreach (var actor in pair)
                    actor.Reset();
                for (int first = 0; first < record.sheetSeconds.Length; first += 9)
                {
                    string sheet = stem + "_Sheet" + (first / 9 + 1).ToString("D2") + ".png";
                    rendering.Write(output + "/" + sheet, bounds,
                        record.sheetSeconds.Skip(first).Take(9).ToArray(), seconds =>
                        {
                            foreach (var actor in pair)
                                actor.Evaluate(seconds);
                        });
                    sheets.Add(sheet);
                }
                record.sheets = sheets.ToArray();
            }
            finally
            {
                try
                {
                    foreach (var actor in actors)
                        actor.Dispose();
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }
        }
    }
}
