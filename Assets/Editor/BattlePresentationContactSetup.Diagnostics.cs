using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public static partial class BattlePresentationContactSetup
{
    // Optional call from the root-owned event capture. It never resamples the animation.
    public static void AuditLiveContact(BattleVfxPlayer.Impact impact)
    {
        if (impact.cue == null || !impact.cue.hasContactPoint || !ContactCue(impact.cue)) return;
        Directory.CreateDirectory(ReportFolder);
        string label = impact.attacker.name + "_" + impact.move.moveName + "_" + impact.seconds.ToString("R");
        var geometry = new List<string>();
        var body = Receiver(impact.receiver, geometry);
        var source = Striker(impact.playback, impact.attacker, impact.cue.contactSource, geometry);
        var report = new StringBuilder();
        report.AppendLine(FormattableString.Invariant($"event seconds={impact.seconds:R}; ") +
            FormattableString.Invariant($"displayed seconds={impact.playback.SampleTime:R}; event={impact.eventId}"));
        report.AppendLine("Emitted effect=" + impact.position.ToString("R"));
        report.AppendLine("Source world bounds=" + source.Bounds + "; receiver world bounds=" + body.Bounds);
        report.AppendLine(FormattableString.Invariant($"Effect distance to rendered source={source.Distance(impact.position):R}; ") +
            FormattableString.Invariant($"receiver={body.Distance(impact.position):R}"));
        report.AppendLine("Closest source world point=" + source.Nearest(impact.position).ToString("R"));
        var camera = Camera.main;
        if (camera)
        {
            report.AppendLine("Effect screen=" + camera.WorldToScreenPoint(impact.position).ToString("R"));
            report.AppendLine("Source triangle screen bounds=" + ProjectedBounds(source, camera));
            report.AppendLine("Receiver triangle screen bounds=" + ProjectedBounds(body, camera));
        }
        foreach (var line in geometry) report.AppendLine(line);
        File.WriteAllText(ReportFolder + "/Live_" + label + ".txt", report.ToString());
        WriteSurface(source, ReportFolder + "/Live_" + label + "_source.obj");
        WriteSurface(body, ReportFolder + "/Live_" + label + "_receiver.obj");
    }

    static Bounds ProjectedBounds(Surface surface, Camera camera)
    {
        var result = new Bounds(camera.WorldToScreenPoint(surface.vertices[surface.triangles[0]]), Vector3.zero);
        foreach (int index in surface.triangles)
            result.Encapsulate(camera.WorldToScreenPoint(surface.vertices[index]));
        return result;
    }

    static void WriteSurface(Surface surface, string path)
    {
        using (var writer = new StreamWriter(path))
        {
            foreach (Vector3 point in surface.vertices)
                writer.WriteLine(FormattableString.Invariant($"v {point.x:R} {point.y:R} {point.z:R}"));
            for (int i = 0; i < surface.triangles.Count; i += 3)
                writer.WriteLine($"f {surface.triangles[i] + 1} {surface.triangles[i + 1] + 1} " +
                    $"{surface.triangles[i + 2] + 1}");
        }
    }
}
