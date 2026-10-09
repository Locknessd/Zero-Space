using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        public static void CaptureExecution10SupineRecovery()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            Directory.CreateDirectory(E10Output);
            var report = new E10Report();
            var samples = new StringBuilder("source,target,direction,seconds,clearance,frameDisplacement," +
                "repeatDrift,stoppedDrift\n");
            var entries = new StringBuilder("source,target,direction,bone,beforeX,beforeY,beforeZ," +
                "afterX,afterY,afterZ,jump\n");
            E10Guard guard = null;
            try
            {
                E10Flush(report, samples, entries);
                guard = new E10Guard();
                report.guardedFiles = guard.Files;
                E10Flush(report, samples, entries);
                using (var session = new SourceSession())
                {
                    InitializePairStudy(session.Fighters);
                    CheckGroundingFighters(session.Fighters);
                    foreach (var source in session.Fighters)
                    foreach (int direction in new[] { 1, -1 })
                    {
                        var record = new E10Case
                        {
                            source = source.name,
                            target = session.Fighters.Single(f => f != source).name,
                            direction = direction
                        };
                        report.cases.Add(record);
                    }
                    foreach (var record in report.cases)
                    {
                        record.status = "RUNNING; partial evidence";
                        E10Flush(report, samples, entries);
                        try
                        {
                            var source = session.Fighters.Single(f => f.name == record.source);
                            var target = session.Fighters.Single(f => f.name == record.target);
                            E10Measure(session.Fighters, source, target, record, samples, entries);
                            E10Render(session.Fighters, source, target, record);
                            if (!record.recoveryStarted || !record.cancellationPassed || record.samples == 0 ||
                                record.entryJump > .01f || record.minimumClearance < -.025f ||
                                record.repeatDrift > .00002f || record.stoppedDrift > .00002f)
                                throw new InvalidOperationException("Recovery numeric gate failed; see measurements.");
                            record.status = "PASS numeric checks; visual review required";
                        }
                        catch (Exception error)
                        {
                            record.status = "FAIL; partial evidence";
                            record.error = error.ToString();
                        }
                        finally
                        {
                            E10Flush(report, samples, entries);
                        }
                    }
                }
                if (report.cases.Count != 4 || report.cases.Any(c => !c.status.StartsWith("PASS")))
                    throw new InvalidOperationException("Execution10 recovery has failed or incomplete cases.");
                report.status = "PASS numeric checks for four cases; visual review required";
            }
            catch (Exception error)
            {
                report.status = "FAIL; partial evidence";
                report.error = error.ToString();
                throw;
            }
            finally
            {
                try
                {
                    guard?.Verify(report);
                }
                catch (Exception error)
                {
                    report.status = "FAIL preservation guard";
                    report.error += "\n" + error;
                    throw;
                }
                finally
                {
                    guard?.RestoreSelection();
                    E10Flush(report, samples, entries);
                }
            }
        }

        static void E10Flush(E10Report report, StringBuilder samples, StringBuilder entries)
        {
            File.WriteAllText(E10Output + "/Report.json", JsonUtility.ToJson(report, true));
            File.WriteAllText(E10Output + "/Samples.csv", samples.ToString());
            File.WriteAllText(E10Output + "/Entries.csv", entries.ToString());
            File.WriteAllText(E10Output + "/Status.txt", report.status + "\n" + report.error + "\n" + E10Scope);
        }
    }
}
