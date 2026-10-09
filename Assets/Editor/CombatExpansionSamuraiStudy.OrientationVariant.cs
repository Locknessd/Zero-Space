using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        const string OrientationVariantAssets = AssetsRoot + "/OrientationVariants";
        const string OrientationVariantOutput = "GeneratedAssets/CombatExpansion/SamuraiStudy/OrientationVariant";
        const string OrientationVariantScope = "PROVISIONAL Execution02 orientation hypothesis only. " +
            "Project-owned byte-identical FBX copies change only Execution2 role keepOriginalOrientation to true. " +
            "Original imported prefab, Avatar and blade/body meshes remain in use at native 1.7m spacing, " +
            "A yaw0/B yaw180, whole-pair yaw +90/-90; no fighter calibration or geometry correction. " +
            "Original Execution02 measured 0.684654m minimum on both lanes; causality is unproven. " +
            "Uses existing native exact triangle contact, root-motion sampling and pose/sheet capture. " +
            "A variant miss remains a miss. Overlap is not acceptance or approved damaging contact. " +
            "No gameplay registration. Every JSON, CSV and sheet in this directory is provisional.";

        public static void CaptureExecution02OriginalOrientationVariant()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            var active = SceneManager.GetActiveScene();
            var selection = Selection.objects;
            var activeSelection = Selection.activeObject;
            var report = new OrientationVariantReport();
            string snapshot = null;
            try
            {
                Directory.CreateDirectory(OrientationVariantOutput);
                File.WriteAllText(OrientationVariantOutput + "/Scope.txt", OrientationVariantScope);
                report.capture.sources = ResolveSources();
                snapshot = NativeReferenceSourceSnapshot(report.capture.sources);
                report.capture.guardedFiles = NativeReferenceGuard(report.capture.sources);
                OrientationVariantFlush(report);
                CombatExpansionHumanoidStudy.EnsureFolder(OrientationVariantAssets);
                var clips = new AnimationClip[2];
                for (int role = 0; role < 2; role++)
                {
                    var source = report.capture.sources.Single(s => s.role == Roles[role] && s.localId == 7400004);
                    var record = new OrientationVariantAsset { role = source.role };
                    report.variants.Add(record);
                    try
                    {
                        clips[role] = OrientationVariantCopy(source, record);
                    }
                    finally
                    {
                        OrientationVariantFlush(report);
                    }
                }
                foreach (int direction in new[] { 1, -1 })
                {
                    var record = new NativeReferenceCase { execution = 2, direction = direction };
                    report.capture.cases.Add(record);
                    try
                    {
                        OrientationVariantFlush(report);
                        NativeReferenceCapture(record, report.capture.sources, OrientationVariantOutput,
                            clips[0], clips[1]);
                        record.status = "PROVISIONAL CAPTURE; no damaging contacts approved";
                    }
                    catch (Exception error)
                    {
                        record.status = "PROVISIONAL FAILED; partial evidence invalid";
                        record.error = error.ToString();
                        throw;
                    }
                    finally
                    {
                        OrientationVariantFlush(report);
                    }
                }
                report.capture.status = "PROVISIONAL CAPTURED both Execution02 lanes; acceptance unproven";
            }
            catch (Exception error)
            {
                report.capture.status = "PROVISIONAL FAILED; partial evidence invalid";
                report.capture.error = error.ToString();
                throw;
            }
            finally
            {
                try
                {
                    if (snapshot != null)
                    {
                        if (snapshot != NativeReferenceSourceSnapshot(ResolveSources()) ||
                            report.capture.guardedFiles == null || report.capture.guardedFiles.Any(f =>
                                !File.Exists(f.path) || NativeReferenceHash(f.path) != f.sha256))
                            throw new InvalidOperationException("Native source bytes, identities or settings changed.");
                        report.capture.sourceGuardsPassed = true;
                    }
                }
                catch (Exception error)
                {
                    report.capture.status = "PROVISIONAL FAILED source preservation guard";
                    report.capture.error += "\n" + error;
                    throw;
                }
                finally
                {
                    try
                    {
                        if (active.IsValid() && active.isLoaded)
                            SceneManager.SetActiveScene(active);
                    }
                    finally
                    {
                        Selection.objects = selection;
                        Selection.activeObject = activeSelection;
                        if (Directory.Exists(OrientationVariantOutput))
                            OrientationVariantFlush(report);
                    }
                }
            }
        }

        static void OrientationVariantFlush(OrientationVariantReport report)
        {
            File.WriteAllText(OrientationVariantOutput + "/Report.json", JsonUtility.ToJson(report, true));
            File.WriteAllText(OrientationVariantOutput + "/Status.txt",
                report.capture.status + "\n" + report.capture.error);
        }
    }
}
