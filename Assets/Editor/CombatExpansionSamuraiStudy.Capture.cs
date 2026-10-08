using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        public static void CaptureSources()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            Directory.CreateDirectory(Output);
            var sources = ResolveSources();
            File.WriteAllText(Output + "/Scope.txt", new Report().scope);
            var captures = new List<CaptureRecord>();
            using var session = new SourceSession();
            var drivers = new List<DriverRecord>();
            foreach (var fighter in session.Fighters)
            for (int role = 0; role < 2; role++)
            foreach (bool armed in new[] { true, false })
            {
                string path = DriverPath(fighter.name, role, armed);
                var driver = AssetDatabase.LoadAssetAtPath<FrankTestDriver>(path);
                ValidateDriver(driver, role, armed, path);
                drivers.Add(new DriverRecord
                {
                    fighter = fighter.name, role = Roles[role], variant = armed ? "Armed" : "Unarmed", path = path,
                    sourceAvatar = CombatExpansionInventory.Identity(driver.pose.sourceHumanAvatar),
                    fighterAvatar = CombatExpansionInventory.Identity(fighter.Animator.avatar),
                    scale = driver.transform.localScale, retainedRenderers = RendererIdentities(driver)
                });
            }
            for (int execution = 1; execution <= 10; execution++)
            for (int assignment = 0; assignment < 2; assignment++)
            foreach (int direction in new[] { 1, -1 })
            {
                var scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    var pair = sources.Where(s => s.localId == 7400000 + 2 * execution).ToArray();
                    using var a = new Actor(scene, session.Fighters[assignment], pair[0], 0, direction);
                    using var b = new Actor(scene, session.Fighters[1 - assignment], pair[1], 1, direction);
                    string stem = "Execution" + execution.ToString("D2") + "_PlayerA_" + a.Fighter +
                        "_PlayerB_" + b.Fighter + "_Lane" + (direction > 0 ? "Positive" : "Negative");
                    var record = new CaptureRecord
                    {
                        execution = execution, direction = direction, playerA = a.Fighter, playerB = b.Fighter,
                        playerAAvatar = CombatExpansionInventory.Identity(a.Character.avatar),
                        playerBAvatar = CombatExpansionInventory.Identity(b.Character.avatar),
                        durationSeconds = Mathf.Max(pair[0].durationSeconds, pair[1].durationSeconds),
                        trajectory = stem + ".csv", sheet = stem + ".png"
                    };
                    var actors = new[] { a, b };
                    using var rendering = new PairRendering(scene, actors);
                    Bounds bounds = WriteMeasurements(actors, record, rendering);
                    rendering.WriteSheet(Output + "/" + record.sheet, actors, record, bounds);
                    captures.Add(record);
                    WriteReport(sources, drivers, captures, "Progress.json");
                }
                catch (Exception error)
                {
                    throw new InvalidOperationException("Samurai capture failed: Execution" + execution +
                        ", PlayerA=" + session.Fighters[assignment].name + ", lane=" + direction, error);
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }
            WriteReport(sources, drivers, captures, "Study.json");
            WriteIndex(captures);
        }

        static Bounds WriteMeasurements(Actor[] actors, CaptureRecord record, PairRendering rendering)
        {
            var bounds = new Bounds(new Vector3(.85f * record.direction, .9f, 0), new Vector3(2, 2, 1));
            foreach (var actor in actors)
                actor.Reset();
            var starts = actors.Select(a => a.Bones.Select(b => b.node.position).ToArray()).ToArray();
            using var writer = new StreamWriter(Output + "/" + record.trajectory);
            writer.WriteLine("execution,lane,actor,fighter,guid,localId,seconds,clipSeconds,rig,bone," +
                "x,y,z,dx,dy,dz,qx,qy,qz,qw");
            int samples = Mathf.CeilToInt(record.durationSeconds * 60);
            for (int frame = 0; frame <= samples; frame++)
            {
                float seconds = Mathf.Min(frame / 60f, record.durationSeconds);
                for (int actorIndex = 0; actorIndex < actors.Length; actorIndex++)
                {
                    var actor = actors[actorIndex];
                    actor.Evaluate(seconds);
                    for (int index = 0; index < actor.Bones.Count; index++)
                    {
                        var bone = actor.Bones[index];
                        var p = bone.node.position;
                        var q = bone.node.rotation;
                        var d = p - starts[actorIndex][index];
                        if (!float.IsFinite(p.x) || !float.IsFinite(p.y) || !float.IsFinite(p.z))
                            throw new InvalidOperationException("Nonfinite " + actor.Source.clip + " " + bone.bone);
                        float clipSeconds = Mathf.Min(seconds, actor.Source.durationSeconds);
                        writer.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{0},{1},{2},{3},{4},{5},{6:R},{7:R},{8},{9},{10:R},{11:R},{12:R}," +
                            "{13:R},{14:R},{15:R},{16:R},{17:R},{18:R},{19:R}", record.execution,
                            record.direction, actor.Source.role, actor.Fighter, actor.Source.guid,
                            actor.Source.localId, seconds, clipSeconds, bone.rig, bone.bone,
                            p.x, p.y, p.z, d.x, d.y, d.z, q.x, q.y, q.z, q.w));
                        bounds.Encapsulate(p);
                    }
                }
                rendering.Sample();
                foreach (var renderer in rendering.Renderers)
                    if (renderer.enabled && renderer.gameObject.activeInHierarchy)
                        bounds.Encapsulate(renderer.bounds);
            }
            bounds.Encapsulate(new Vector3(bounds.min.x, 0, bounds.min.z));
            bounds.Expand(.25f);
            return bounds;
        }

        static void WriteReport(SourceRecord[] sources, List<DriverRecord> drivers,
            List<CaptureRecord> captures, string filename)
        {
            File.WriteAllText(Output + "/" + filename, JsonUtility.ToJson(new Report
            {
                clips = sources, drivers = drivers.ToArray(), captures = captures.ToArray()
            }, true));
        }

        static void WriteIndex(List<CaptureRecord> captures)
        {
            var html = new StringBuilder("<!doctype html><meta charset='utf-8'>" +
                "<title>Samurai source choreography study</title>" +
                "<style>body{background:#18202b;color:#eee;font:16px sans-serif}img{max-width:100%}" +
                "a{color:#8bd}section{margin:2em 0}</style><h1>Samurai paired source study</h1><p>" +
                System.Net.WebUtility.HtmlEncode(new Report().scope) + "</p>" +
                "<p>Each tile shows side view (left) and oblique view (right). Labels above each head identify PlayerA and PlayerB. " +
                "Read timed tiles left to right, top to bottom.</p>");
            foreach (var item in captures)
            {
                html.Append("<section><h2>Execution ").Append(item.execution).Append(" / PlayerA: ")
                    .Append(item.playerA).Append(" / PlayerB: ").Append(item.playerB)
                    .Append(" / lane ").Append(item.direction).Append("</h2><img src='")
                    .Append(item.sheet).Append("'><p><a href='").Append(item.trajectory)
                    .Append("'>60 Hz roots, hips, hands, feet and sword trajectories</a></p></section>");
            }
            File.WriteAllText(Output + "/index.html", html.ToString());
        }
    }
}
