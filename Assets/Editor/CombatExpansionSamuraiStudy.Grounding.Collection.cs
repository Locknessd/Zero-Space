using System;
using System.Collections.Generic;
using System.Text;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        public static void BakeRemainingExecutionGrounding()
        {
            RunRemainingGroundingCollection("Bake", "BAKED", BakeExecutionGrounding);
        }

        public static void ValidateRemainingExecutionGrounding()
        {
            RunRemainingGroundingCollection("Validation", "PASS", ValidateExecutionGrounding);
        }

        static string GroundingExecutionName(int execution)
        {
            if (execution < 1 || execution > 10)
                throw new ArgumentOutOfRangeException(nameof(execution), execution,
                    "Samurai grounding execution must be from 1 to 10.");
            return FormattableString.Invariant($"Execution{execution:00}");
        }

        static string GroundingAssetPath(int execution)
        {
            string executionName = GroundingExecutionName(execution);
            return execution == 1 ? Execution01GroundingPath :
                "Assets/CombatExpansion/SamuraiStudy/Grounding/Samurai_" + executionName + "_Grounding.asset";
        }

        static void RunRemainingGroundingCollection(string operation, string success, Action<int> run)
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            var rows = new StringBuilder("execution,status,asset,report,error\n");
            var failures = new List<Exception>();
            int completed = 0;
            for (int execution = 2; execution <= 10; execution++)
            {
                string executionName = GroundingExecutionName(execution);
                string status = success;
                string errorText = "";
                try
                {
                    run(execution);
                    completed++;
                }
                catch (Exception error)
                {
                    status = "FAIL";
                    errorText = error.ToString();
                    failures.Add(new InvalidOperationException(executionName + " grounding " + operation +
                        " failed. See its individual report.", error));
                }
                rows.AppendLine(executionName + "," + status + "," + GroundingCsv(GroundingAssetPath(execution)) +
                    "," + GroundingCsv(executionName + "Grounding" + operation + ".txt") +
                    "," + GroundingCsv(errorText));
            }
            string reportName = "RemainingExecutionGrounding" + operation;
            var summary = new StringBuilder(failures.Count == 0 ? success + "\n" : "FAIL\n");
            summary.AppendLine("Execution02..10; all nine executions attempted sequentially in separate source sessions.");
            summary.AppendLine("Succeeded=" + completed + "/9; failed=" + failures.Count + ".");
            summary.AppendLine("Execution01 assets and reports are excluded from this collection.");
            summary.AppendLine("Sampled grounding only; no contact, recovery or gameplay approval.");
            summary.Append(rows);
            try
            {
                WriteGroundingReport(reportName + ".csv", rows.ToString());
                WriteGroundingReport(reportName + ".txt", summary.ToString());
            }
            catch (Exception error)
            {
                failures.Add(new InvalidOperationException("Cannot write grounding collection reports.", error));
            }
            if (failures.Count > 0)
                throw new AggregateException("Samurai grounding " + operation +
                    " collection failed after attempting Execution02..10.", failures);
        }
    }
}
