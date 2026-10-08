using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        static float GroundingCushionFor(int execution, CharacterCombat fighter, bool receiver)
        {
            // The measured head/body envelope fits the existing 0.3m correction bound
            // with 5mm of positive clearance. Independent validation keeps its -1mm gate.
            bool measuredException = receiver && (execution == 6 || execution == 10) &&
                CombatExpansionInventory.Identity(fighter.Animator.avatar) ==
                    "59dc3f26824c72872aa4cf6baeec671e:9000000";
            return measuredException ? .005f : GroundingCushion;
        }

        public static void BakeExecution06And10Grounding()
        {
            RunGroundingExceptions("Bake", "BAKED", BakeExecutionGrounding);
        }

        public static void ValidateExecution06And10Grounding()
        {
            RunGroundingExceptions("Validation", "PASS", ValidateExecutionGrounding);
        }

        static void RunGroundingExceptions(string operation, string success, Action<int> run)
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            var report = new StringBuilder();
            var failures = new List<Exception>();
            foreach (int execution in new[] { 6, 10 })
            {
                try
                {
                    run(execution);
                    report.AppendLine(GroundingExecutionName(execution) + " " + success);
                }
                catch (Exception error)
                {
                    report.AppendLine(GroundingExecutionName(execution) + " FAIL: " + error);
                    failures.Add(error);
                }
            }
            report.Insert(0, failures.Count == 0 ? success + "\n" : "FAIL\n");
            report.AppendLine("Only Mankey receiver cushion changes from 0.01m to 0.005m.");
            report.AppendLine("Native poses, correction bound and validation threshold are unchanged.");
            report.AppendLine("Sampled floor evidence only; contacts, support and gameplay require review.");
            WriteGroundingReport("Execution06And10Grounding" + operation + ".txt", report.ToString());
            if (failures.Count > 0)
                throw new AggregateException("Grounding exception " + operation + " failed.", failures);
        }
    }
}
