using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using FrankRetarget;
using UnityEditor;
using UnityEngine;

public static partial class BattlePresentationWorkbench
{
    public static void AuditCameraBounds()
    {
        var camera = UnityEngine.Object.FindFirstObjectByType<FrankCinematicCamera>();
        var report = new StringBuilder();
        report.AppendLine($"Camera={camera.transform.position}; dolly={camera.SafetyDolly}; zoom={camera.Zoom}");
        var points = new List<Vector3>();
        camera.CollectFramingPoints(points);
        var bounds = new Bounds(points[0], Vector3.zero);
        foreach (var point in points)
            bounds.Encapsulate(point);
        report.AppendLine("Collected bounds=" + bounds);
        foreach (var fighter in new[] { Battle().leftCombat, Battle().rightCombat })
        foreach (var skin in fighter.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh = new Mesh();
            skin.BakeMesh(mesh, false);
            report.AppendLine($"{skin.name}: scale={skin.transform.lossyScale}; renderer={skin.bounds}; baked={mesh.bounds}");
            UnityEngine.Object.DestroyImmediate(mesh);
        }
        File.WriteAllText(Review + "/CameraBounds.txt", report.ToString());
    }

    public static void SetFullHdGameView() => SetGameViewSize(1920, 1080, "Battle review 1080p");

    public static void SetBaselineGameView() => SetGameViewSize(1853, 780, "Battle review baseline");

    static void SetGameViewSize(int width, int height, string label)
    {
        var assembly = typeof(Editor).Assembly;
        var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
        var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        var sizes = singleton.GetProperty("instance").GetValue(null);
        var groupType = assembly.GetType("UnityEditor.GameViewSizeGroupType");
        var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new[] { Enum.Parse(groupType, "Standalone") });
        var sizeType = assembly.GetType("UnityEditor.GameViewSize");
        var typeEnum = assembly.GetType("UnityEditor.GameViewSizeType");
        var size = Activator.CreateInstance(sizeType,
            new object[] { Enum.Parse(typeEnum, "FixedResolution"), width, height, label });
        var groupClass = group.GetType();
        int index = (int)groupClass.GetMethod("GetTotalCount").Invoke(group, null);
        groupClass.GetMethod("AddCustomSize").Invoke(group, new[] { size });
        var viewType = assembly.GetType("UnityEditor.GameView");
        var view = EditorWindow.GetWindow(viewType);
        viewType.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic).SetValue(view, index);
        view.Focus();
    }

    public static void BeginCleanConsoleCheck()
    {
        typeof(Editor).Assembly.GetType("UnityEditor.LogEntries")
            .GetMethod("Clear", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
        File.WriteAllText(Review + "/ConsoleCheckStart.txt", DateTime.UtcNow.ToString("O"));
    }
}
