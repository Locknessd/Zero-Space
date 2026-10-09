using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        [Serializable]
        sealed class FOReport
        {
            public string scope = FOScope;
            public string stage, status = "RUNNING; partial evidence only", error;
            public string utc = DateTime.UtcNow.ToString("O");
            public bool sourceGuardsPassed;
            public SourceRecord[] originals, variants;
            public NativeReferenceFile[] guardedFiles;
            public List<OrientationVariantAsset> provenance = new List<OrientationVariantAsset>();
        }

        static void FORun(string stage, Action<AnimationClip[], SourceRecord[], string> run)
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            string output = FOOutput + "/" + stage;
            Directory.CreateDirectory(output);
            var report = new FOReport { stage = stage };
            var active = SceneManager.GetActiveScene();
            var selection = Selection.objects;
            var selected = Selection.activeObject;
            string sceneState = FOSceneState();
            string snapshot = null;
            try
            {
                File.WriteAllText(output + "/Scope.txt", FOScope + CCScope);
                var originals = ResolveSources();
                snapshot = NativeReferenceSourceSnapshot(originals);
                report.originals = originals.Where(s => s.localId == 7400004).ToArray();
                report.guardedFiles = FOFiles(originals, stage == "Bake");
                FOFlush(output, report);
                var clips = new AnimationClip[2];
                for (int role = 0; role < clips.Length; role++)
                {
                    if (!File.Exists(FOClipPath(role)) || !File.Exists(FOClipPath(role) + ".meta"))
                        throw new InvalidOperationException("Prepare the existing orientation variant first: " +
                            FOClipPath(role));
                    var source = originals.Single(s => s.role == Roles[role] && s.localId == 7400004);
                    var provenance = new OrientationVariantAsset { role = Roles[role] };
                    report.provenance.Add(provenance);
                    clips[role] = OrientationVariantCopy(source, provenance);
                    FOFlush(output, report);
                }
                FOValidateOverrides(2, clips[0], clips[1]);
                report.variants = clips.Select((clip, role) => FOVariantSource(
                    originals.Single(s => s.role == Roles[role] && s.localId == 7400004), clip)).ToArray();
                FOFlush(output, report);
                run(clips, report.variants, output);
                report.status = "COMPLETED " + stage + "; evidence only, gameplay approval remains required.";
            }
            catch (Exception error)
            {
                report.status = "FAILED " + stage + "; partial evidence is not a completed result.";
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
                                !File.Exists(f.path) || NativeReferenceHash(f.path) != f.sha256) ||
                            sceneState != FOSceneState())
                            throw new InvalidOperationException("Source/driver/grounding/scene preservation failed.");
                        report.sourceGuardsPassed = true;
                        foreach (var provenance in report.provenance)
                            provenance.status = "GUARDED original and variant source preservation passed";
                    }
                }
                catch (Exception error)
                {
                    report.status = "FAILED preservation guard; evidence invalid.";
                    report.error += "\n" + error;
                    throw;
                }
                finally
                {
                    if (active.IsValid() && active.isLoaded)
                        SceneManager.SetActiveScene(active);
                    Selection.objects = selection;
                    Selection.activeObject = selected;
                    FOFlush(output, report);
                }
            }
        }

        static NativeReferenceFile[] FOFiles(SourceRecord[] sources, bool baking)
        {
            var paths = new HashSet<string>(NativeReferenceGuard(sources).Select(f => f.path));
            foreach (string path in AssetDatabase.GetDependencies(CombatExpansionInventory.Battle, true))
                FOAddFile(paths, path);
            foreach (string folder in new[] { AssetsRoot + "/Drivers", AssetsRoot + "/Grounding" })
                if (Directory.Exists(folder))
                    foreach (string path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                        FOAddFile(paths, path.Replace('\\', '/'));
            for (int role = 0; role < 2; role++)
                FOAddFile(paths, FOClipPath(role));
            for (int index = 0; index < SceneManager.sceneCount; index++)
                FOAddFile(paths, SceneManager.GetSceneAt(index).path);
            if (!baking)
                FOAddFile(paths, FOGroundingPath);
            return paths.OrderBy(p => p).Select(p => new NativeReferenceFile
            {
                path = p,
                sha256 = NativeReferenceHash(p)
            }).ToArray();
        }

        static void FOAddFile(HashSet<string> paths, string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
                return;
            foreach (string file in new[] { path, path + ".meta" })
                if (File.Exists(file))
                    paths.Add(file);
        }

        static string FOSceneState()
        {
            return string.Join("\n", Enumerable.Range(0, SceneManager.sceneCount).Select(index =>
            {
                var scene = SceneManager.GetSceneAt(index);
                return scene.handle + ":" + scene.path + ":" + scene.isLoaded + ":" + scene.isDirty;
            }));
        }

        static void FOFlush(string output, FOReport report)
        {
            File.WriteAllText(output + "/Provenance.json", JsonUtility.ToJson(report, true));
            File.WriteAllText(output + "/WorkflowStatus.txt", report.status + "\n" + report.error);
            string contactReport = output + "/Report.json";
            if (report.stage == "Contacts" && report.status.StartsWith("FAILED", StringComparison.Ordinal) &&
                File.Exists(contactReport))
            {
                var capture = JsonUtility.FromJson<CCReport>(File.ReadAllText(contactReport));
                capture.status = report.status;
                capture.error += "\n" + report.error;
                // Serialized reports retain case file/status arrays, but not their in-memory case list.
                File.WriteAllText(contactReport, JsonUtility.ToJson(capture, true));
                File.WriteAllText(output + "/Status.txt", report.status + "\n" + report.error);
            }
        }
    }
}
