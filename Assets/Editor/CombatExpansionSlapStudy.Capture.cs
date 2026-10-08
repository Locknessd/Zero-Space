using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapStudy
    {
        public static void CaptureSources()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            var sources = ResolveSources();
            using var session = new SourceSession();
            foreach (var fighter in session.Fighters)
            foreach (var source in sources)
            {
                string path = DriverPath(fighter.name, source);
                ValidateDriver(AssetDatabase.LoadAssetAtPath<FrankTestDriver>(path), source, path);
            }
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/Scope.txt", new Report().scope);
            var captures = new List<CaptureRecord>();
            WriteReport(sources, captures, "Progress.json");
            foreach (var fighter in session.Fighters)
            foreach (var source in sources)
            {
                var scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    using var actor = new Actor(scene, fighter, source);
                    string stem = fighter.name + "_" + source.label + "_" + source.guid;
                    var record = new CaptureRecord
                    {
                        fighter = fighter.name,
                        label = source.label,
                        guid = source.guid,
                        localId = source.localId,
                        driverPath = DriverPath(fighter.name, source),
                        sourceAvatar = source.avatar,
                        fighterAvatar = CombatExpansionInventory.Identity(fighter.Animator.avatar),
                        calibratedNativeScale = actor.Driver.transform.localScale,
                        durationSeconds = source.durationSeconds,
                        trajectory = stem + ".csv"
                    };
                    using var rendering = new SourceRendering(scene, actor);
                    Bounds bounds = WriteMeasurements(actor, record, rendering);
                    rendering.WriteSheets(stem, actor, record, bounds);
                    captures.Add(record);
                    WriteReport(sources, captures, "Progress.json");
                }
                catch (Exception error)
                {
                    throw new InvalidOperationException("SlapFace individual capture failed: " +
                        fighter.name + " / " + source.label, error);
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }
            if (captures.Count != 8)
                throw new InvalidOperationException("Expected exactly eight SlapFace captures");
            WriteReport(sources, captures, "Study.json");
            WriteIndex(captures);
        }

        static Bounds WriteMeasurements(Actor actor, CaptureRecord record, SourceRendering rendering)
        {
            actor.Reset();
            record.initialBones = actor.Bones.Select(b => new BoneRecord
            {
                rig = b.rig,
                bone = b.bone,
                path = AnimationUtility.CalculateTransformPath(b.node,
                    b.rig == "native" ? actor.Driver.transform : actor.Character.transform),
                position = b.node.position,
                rotation = b.node.rotation,
                localPosition = b.node.localPosition,
                localRotation = b.node.localRotation
            }).ToArray();
            var bounds = new Bounds(actor.Driver.pose.sourceHips.position, Vector3.one * .1f);
            using var writer = new StreamWriter(Output + "/" + record.trajectory);
            writer.WriteLine("guid,localId,fighter,seconds,rig,bone,x,y,z,dx,dy,dz,qx,qy,qz,qw");
            int last = Mathf.CeilToInt(record.durationSeconds * 60);
            for (int frame = 0; frame <= last; frame++)
            {
                float seconds = Mathf.Min(frame / 60f, record.durationSeconds);
                actor.Evaluate(seconds);
                for (int index = 0; index < actor.Bones.Count; index++)
                {
                    var bone = actor.Bones[index];
                    var p = bone.node.position;
                    var q = bone.node.rotation;
                    var d = p - record.initialBones[index].position;
                    if (!float.IsFinite(p.x) || !float.IsFinite(p.y) || !float.IsFinite(p.z) ||
                        !float.IsFinite(q.x) || !float.IsFinite(q.y) || !float.IsFinite(q.z) || !float.IsFinite(q.w))
                        throw new InvalidOperationException("Nonfinite SlapFace pose: " + bone.bone);
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1},{2},{3:R},{4},{5},{6:R},{7:R},{8:R},{9:R},{10:R},{11:R}," +
                        "{12:R},{13:R},{14:R},{15:R}", record.guid, record.localId, actor.Fighter,
                        seconds, bone.rig, bone.bone, p.x, p.y, p.z, d.x, d.y, d.z, q.x, q.y, q.z, q.w));
                    bounds.Encapsulate(p);
                }
                rendering.Sample();
                foreach (var renderer in rendering.Renderers)
                    if (renderer && renderer.enabled && renderer.gameObject.activeInHierarchy)
                        bounds.Encapsulate(renderer.bounds);
                record.trajectorySamples++;
            }
            bounds.Expand(.2f);
            return bounds;
        }

        static void WriteReport(SourceRecord[] sources, List<CaptureRecord> captures, string filename)
        {
            File.WriteAllText(Output + "/" + filename, JsonUtility.ToJson(new Report
            {
                clips = sources,
                captures = captures.ToArray()
            }, true));
        }

        static void WriteIndex(List<CaptureRecord> captures)
        {
            var html = new StringBuilder("<!doctype html><meta charset='utf-8'>" +
                "<title>SlapFace individual source evidence</title>" +
                "<style>body{background:#18202b;color:#eee;font:16px sans-serif}img{max-width:100%}" +
                "a{color:#8bd}section{margin:2em 0}</style><h1>SlapFace individual source evidence</h1><p>" +
                System.Net.WebUtility.HtmlEncode(new Report().scope) + "</p>");
            foreach (var item in captures)
            {
                html.Append("<section><h2>").Append(item.fighter).Append(" / ").Append(item.label)
                    .Append("</h2><p>").Append(item.guid).Append(":").Append(item.localId)
                    .Append("</p><p>Side view left; oblique right. Read left to right, top to bottom.</p>");
                foreach (string sheet in item.sheets)
                    html.Append("<img loading='lazy' src='").Append(sheet).Append("'>");
                html.Append("<p><a href='").Append(item.trajectory)
                    .Append("'>60 Hz native and fighter root, hips, hands, head, feet</a></p></section>");
            }
            File.WriteAllText(Output + "/index.html", html.ToString());
        }
    }
}
