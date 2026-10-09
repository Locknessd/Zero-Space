using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        // Preserve finished evidence and retry only executions whose capture failed.
        // Existing completed reports retain the measurement version used to create them.
        public static void RecaptureFailedRemainingContacts()
        {
            CCRecaptureExecutions(Enumerable.Range(2, 9).Where(e => !CCCompletedExecution(e)).ToArray());
        }

        public static void RecaptureExecution03Contacts()
        {
            CCRecaptureExecutions(new[] { 3 });
        }

        static void CCRecaptureExecutions(int[] executions)
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            string status = Path.Combine(CCOutput, "Status.txt");
            if (!File.Exists(status) || File.ReadLines(status).FirstOrDefault()?.StartsWith("RUNNING") == true)
                throw new InvalidOperationException("The original collection must finish before recapture.");
            string batch = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ");
            string history = Path.Combine(CCOutput, "History", batch);
            Directory.CreateDirectory(history);
            File.Copy(status, Path.Combine(history, "OriginalCollectionStatus.txt"));
            var lines = new List<string>
            {
                "RUNNING failed-execution recapture; candidates remain unapproved.",
                "Completed prior captures are retained with their original measurement semantics.",
                "Recaptured executions use the current physical world-space anchor replay comparison.",
                "Requested executions=" + string.Join(",", executions)
            };
            string progress = Path.Combine(CCOutput, "RecaptureStatus.txt");
            File.WriteAllLines(progress, lines);
            var errors = new List<Exception>();
            foreach (int execution in executions)
            {
                string name = GroundingExecutionName(execution);
                string directory = Path.Combine(CCOutput, name);
                try
                {
                    if (Directory.Exists(directory))
                        Directory.Move(directory, Path.Combine(history, name));
                    CCCaptureExecution(execution);
                    if (!CCCompletedExecution(execution))
                        throw new InvalidOperationException("Incomplete recapture: " + name);
                    lines.Add(name + " CAPTURED; no contact or gameplay approval.");
                }
                catch (Exception error)
                {
                    errors.Add(error);
                    lines.Add(name + " FAILED: " + error);
                }
                File.WriteAllLines(progress, lines);
            }
            int completed = Enumerable.Range(2, 9).Count(CCCompletedExecution);
            lines[0] = errors.Count == 0
                ? "CAPTURED requested recaptures; prior reports retain their original measurement versions."
                : "FAILED recapture; inspect individual reports and preserved history.";
            lines.Add("Completed execution captures=" + completed + "/9; gameplay acceptance remains pending.");
            File.WriteAllLines(progress, lines);
            if (errors.Count > 0)
                throw new AggregateException("Remaining Samurai recapture failed.", errors);
        }

        static bool CCCompletedExecution(int execution)
        {
            string directory = Path.Combine(CCOutput, GroundingExecutionName(execution));
            string file = Path.Combine(directory, "Report.json");
            if (!File.Exists(file))
                return false;
            var report = JsonUtility.FromJson<CCReport>(File.ReadAllText(file));
            if (report == null || report.execution != execution || report.status == null ||
                !report.status.StartsWith("CAPTURED") || report.caseFiles == null ||
                report.caseStatuses == null || report.caseFiles.Length != 4 || report.caseStatuses.Length != 4 ||
                report.caseFiles.Distinct(StringComparer.Ordinal).Count() != 4 ||
                report.caseStatuses.Any(value => value != "CAPTURED"))
                return false;
            foreach (string name in report.caseFiles)
            {
                if (string.IsNullOrEmpty(name) || Path.GetFileName(name) != name)
                    return false;
                string caseFile = Path.Combine(directory, name);
                if (!File.Exists(caseFile))
                    return false;
                var record = JsonUtility.FromJson<CCCase>(File.ReadAllText(caseFile));
                if (record == null || record.status != "CAPTURED" || record.anatomy == null ||
                    record.totalSamples <= 0 || record.sheets == null || record.sheets.Length == 0)
                    return false;
            }
            return true;
        }
    }
}
