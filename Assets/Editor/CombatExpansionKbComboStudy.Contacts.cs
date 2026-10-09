using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionKbComboStudy
    {
        const string ContactsOutput = "GeneratedAssets/CombatExpansion/KbComboStudy/FirstComboContacts";
        const string ContactsScope = "PROVISIONAL evidence only. No accepted hits, registration or timing changes. " +
            "Original first-combo native source tracks and reaction onsets 0.1/0.525/1.51s. " +
            "Both assignments, both directions, spacings 0.65/0.8/0.95m. " +
            "Strike1 actual LeftHand; strikes2/3 actual RightHand; each versus head and torso separately. " +
            "Head region is mapped Head and descendants. Torso uses only mapped Hips/Spine/Chest/UpperChest " +
            "weights, WITHOUT descendants, excluding head, arm and leg bones. Each selected triangle requires " +
            ">=0.5 summed region weight on every vertex. Exact unsigned skinned triangle gaps are not signed " +
            "penetration depths; zero may indicate intersection. Nearest samples are candidates, never acceptance. " +
            "60Hz entire pair plus exact endpoints/seams; 240Hz in +/-0.1s windows about 0.1/0.525/1.51. " +
            "Candidate searches restricted to [0,0.4], [0.4,0.8], [0.8,1.8166667]s. " +
            "Movement is finite-difference world hand-pivot velocity and relative hand-to-target-pivot velocity. " +
            "Body clearance means minimum full visible body Y relative to preview ground Y=0. " +
            "Reverse-time replay checks every measured gap, clearance and all diagnostic bone poses. " +
            "World closest anchors may change for equally close/intersecting faces; stability checks use gaps " +
            "and poses rather than requiring unique closest points. Existing preview-scene/session cleanup is reused. " +
            "Native equivalence was previously validated and is not repeated here.";

        public static void CaptureFirstComboContacts()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            Directory.CreateDirectory(ContactsOutput);
            var report = new ContactsReport();
            File.WriteAllText(ContactsOutput + "/Scope.txt", ContactsScope + "\n");
            SaveContactsReport(report);
            try
            {
                report.sources = ResolveSources();
                for (int assignment = 0; assignment < 2; assignment++)
                foreach (int lane in new[] { 1, -1 })
                foreach (float spacing in new[] { .65f, .8f, .95f })
                {
                    var record = new ContactsCase
                    {
                        name = "PROVISIONAL_" + (assignment == 0 ? "Mankey" : "Pepe") +
                            "_Lane" + (lane > 0 ? "Positive" : "Negative") +
                            "_Range" + Mathf.RoundToInt(spacing * 100) + "cm",
                        assignment = assignment,
                        laneSign = lane,
                        spacing = spacing
                    };
                    report.cases.Add(record);
                    SaveContactsReport(report);
                    using (var session = new SourceSession())
                    {
                        typeof(CombatPositioningController).GetProperty("Instance",
                            BindingFlags.Public | BindingFlags.Static).SetValue(null, null);
                        foreach (var fighter in session.Fighters)
                        {
                            fighter.battleSfx = null;
                            fighter.battleVfx = null;
                            fighter.hitEffect = null;
                            if (!fighter.Initialize())
                                throw new InvalidOperationException("Cannot initialize " + fighter.name);
                        }
                        CaptureContactsCase(session.Fighters, report.sources, record, report);
                    }
                    RequireSourcesUnchanged(report.sources);
                    record.status = "CAPTURED_PROVISIONAL";
                    record.completedUtc = DateTime.UtcNow.ToString("O");
                    report.completedCases++;
                    SaveContactsReport(report);
                }
                report.status = "CAPTURED_PROVISIONAL";
                report.completedUtc = DateTime.UtcNow.ToString("O");
                SaveContactsReport(report);
            }
            catch (Exception error)
            {
                report.status = "FAILED_PROVISIONAL";
                report.failure = error.ToString();
                if (report.cases.Count > 0)
                {
                    var failed = report.cases[report.cases.Count - 1];
                    failed.status = "FAILED_PROVISIONAL";
                    failed.failure = error.ToString();
                }
                SaveContactsReport(report);
                throw;
            }
        }

        static void SaveContactsReport(ContactsReport report)
        {
            report.updatedUtc = DateTime.UtcNow.ToString("O");
            File.WriteAllText(ContactsOutput + "/Study.json", JsonUtility.ToJson(report, true));
            ContactsCase current = report.cases.Count == 0 ? null : report.cases[report.cases.Count - 1];
            string detail = current is ContactsCase item ? item.name + " " + item.phase +
                " " + item.samples + " samples; " + item.reverseSamples + " reverse samples" : "";
            File.WriteAllText(ContactsOutput + "/Status.txt", report.status + " " +
                report.completedCases + "/12 " + report.updatedUtc + "\n" + detail + "\n" + report.failure);
        }
    }
}
