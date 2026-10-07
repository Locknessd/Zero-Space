using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

public static partial class BattlePresentationWorkbench
{
    const string RunKey = "BattleReview.Run";
    static readonly List<float> FrameMilliseconds = new List<float>(20000);
    static double runStart;
    static double lastMoveEnded;
    static int reviewMove;
    static int reviewFrame = -1;
    static int peakEffects;
    static int peakVoices;
    static long startMemory;
    static long endMemory;
    static long warmMemory;
    static int focusedFrames;
    static bool reviewStarted;
    static int capturedContacts;
    static GameManager reviewGame;
    static readonly string[] ReviewMoves = { "Light_1", "Heavy_6", "Heavy_Katana", "Heavy_Assassin", "Heavy_5" };

    [MenuItem("Tools/Battle/Presentation/Run two minute combat review")]
    public static void BeginReview()
    {
        if (!EditorApplication.isPlaying)
            throw new InvalidOperationException("Enter BattleScene Play Mode first.");
        reviewGame = Battle();
        reviewGame.StopAnimationTest();
        if (!reviewGame.BeginAnimationTestMode())
            throw new InvalidOperationException("Wait for the current exchange to finish.");
        FrameMilliseconds.Clear();
        reviewMove = 0;
        reviewFrame = -1;
        peakEffects = 0;
        peakVoices = 0;
        capturedContacts = 0;
        focusedFrames = 0;
        warmMemory = 0;
        runStart = EditorApplication.timeSinceStartup;
        lastMoveEnded = runStart;
        startMemory = Profiler.GetTotalAllocatedMemoryLong();
        reviewStarted = true;
        BeginMetrics();
        reviewGame.battleVfx.ContactOccurred -= ReviewContact;
        reviewGame.battleVfx.ContactOccurred += ReviewContact;
        SessionState.SetBool(RunKey, true);
        File.WriteAllText(Review + "/SustainedReview.txt", "RUNNING real combat preview for 120 seconds\n" + Status());
    }

    static void ReviewContact(BattleVfxPlayer.Impact impact)
    {
        if (capturedContacts >= 6 || reviewMove > 3)
            return;
        capturedContacts++;
        capturePath = Review + "/After_" + impact.move.moveName + "_" + impact.kind + "_" + capturedContacts + ".png";
        captureAfter = Time.frameCount + 1;
        File.AppendAllText(Review + "/EventTiming.tsv",
            $"{impact.move.moveName}\t{impact.kind}\t{impact.seconds:F4}\t{Time.frameCount}\t{impact.eventId}\n");
    }

    static void ReviewTick()
    {
        if (!SessionState.GetBool(RunKey, false))
            return;
        if (!EditorApplication.isPlaying)
        {
            SessionState.SetBool(RunKey, false);
            reviewStarted = false;
            StopMetrics();
            return;
        }
        if (!reviewStarted || !reviewGame || EditorApplication.isPaused || EditorApplication.isCompiling)
            return;
        if (Time.frameCount == reviewFrame)
            return;
        reviewFrame = Time.frameCount;
        double elapsed = EditorApplication.timeSinceStartup - runStart;
        // First ten seconds include loading, capture and prewarming; measure steady repeated combat afterwards.
        if (elapsed > 10)
        {
            if (warmMemory == 0)
                warmMemory = Profiler.GetTotalAllocatedMemoryLong();
            if (Application.isFocused)
                focusedFrames++;
            FrameMilliseconds.Add(Time.unscaledDeltaTime * 1000);
            SampleMetrics();
        }
        peakEffects = Mathf.Max(peakEffects, reviewGame.battleVfx.ActiveEffectCount);
        peakVoices = Mathf.Max(peakVoices, reviewGame.battleSfx.ActiveVoiceCount);
        if (!string.IsNullOrEmpty(reviewGame.QueueError))
        {
            FinishReview("FAIL queue: " + reviewGame.QueueError);
            return;
        }
        if (elapsed >= 120)
        {
            FinishReview("COMPLETED sustained real-combat review");
            return;
        }
        if (reviewGame.IsAnimationTestPlaying)
        {
            lastMoveEnded = EditorApplication.timeSinceStartup;
            return;
        }
        if (EditorApplication.timeSinceStartup - lastMoveEnded < .45)
            return;
        int side = (reviewMove / ReviewMoves.Length) % 2;
        var fighter = side == 0 ? reviewGame.leftCombat : reviewGame.rightCombat;
        string name = ReviewMoves[reviewMove % ReviewMoves.Length];
        var move = fighter.lightCombatMoves.Concat(fighter.heavyCombatMoves).FirstOrDefault(m => m.moveName == name);
        if (move == null)
            move = fighter.heavyCombatMoves.First();
        bool accepted = reviewGame.PlayAnimationTest(side == 0 ? PlayerUI.Side.Left : PlayerUI.Side.Right, move);
        if (!accepted)
        {
            FinishReview("FAIL preview rejected " + name);
            return;
        }
        reviewMove++;
    }

    static void FinishReview(string result)
    {
        reviewGame.battleVfx.ContactOccurred -= ReviewContact;
        endMemory = Profiler.GetTotalAllocatedMemoryLong();
        var sorted = FrameMilliseconds.OrderBy(t => t).ToArray();
        var report = new StringBuilder(result + "\n" + Status());
        report.AppendLine($"Moves started={reviewMove}; frames measured={sorted.Length}; warmup excluded=10 s");
        if (sorted.Length > 0)
        {
            report.AppendLine($"Frame ms median={sorted[sorted.Length / 2]:F2}; p95={sorted[(int)(sorted.Length * .95)]:F2}; p99={sorted[(int)(sorted.Length * .99)]:F2}; max={sorted.Last():F2}");
            report.AppendLine($"Frames over 16.67 ms={sorted.Count(t => t > 16.67f)}; average ms={sorted.Average():F2}");
        }
        report.AppendLine($"Peak active VFX={peakEffects}; peak voices={peakVoices}; pooled voices={reviewGame.battleSfx.PooledVoiceCount}");
        report.AppendLine($"Unity allocated memory start={startMemory}; end={endMemory}; delta={endMemory - startMemory}");
        report.AppendLine($"Focused measured frames={focusedFrames}/{sorted.Length}; " +
            $"memory after warmup={warmMemory}; post warmup delta={endMemory - warmMemory}");
        WriteMetrics(report);
        StopMetrics();
        report.AppendLine("Editor Game view measurements, no Deep Profile. Includes Editor and capture overhead; not a player-build benchmark.");
        report.AppendLine("GPU timing and transparent overdraw are not measured by this counter run.");
        File.WriteAllText(Review + "/SustainedReview.txt", report.ToString());
        reviewGame.StopAnimationTest();
        SessionState.SetBool(RunKey, false);
        reviewStarted = false;
    }
}
