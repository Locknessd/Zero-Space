using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionKbComboStudy
    {
        public static void CaptureBodyJabCandidates()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            bodyJabVariants = true;
            try
            {
                CaptureSequentialContactCandidates();
            }
            finally
            {
                bodyJabVariants = false;
            }
        }

        public static void CaptureSequentialContactCandidates()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            Directory.CreateDirectory(SequentialOutput);
            File.WriteAllText(SequentialOutput + "/Scope.txt", SequentialScope + "\n");
            var study = new SequentialStudy();
            SaveSequentialStudy(study);
            try
            {
                foreach (long finalReaction in new long[] { 7400006, 7400008 })
                {
                    var sources = HighReactionSources(finalReaction);
                    for (int assignment = 0; assignment < 2; assignment++)
                    foreach (float spacing in new[] { .70f, .78f, .86f })
                    {
                        sources[3] = bodyJabVariants && assignment == 1
                            ? ResolveSources()[3] : HighReactionSources(finalReaction)[3];
                        var positive = NewSequentialCase(sources, assignment, spacing, 1);
                        study.cases.Add(positive);
                        RunSequentialCase(positive);
                        study.completedCases++;
                        SaveSequentialStudy(study);
                        Debug.Log("Sequential contacts: " + positive.name + " " + positive.status);
                        var negative = NewSequentialCase(sources, assignment, spacing, -1);
                        study.cases.Add(negative);
                        if (positive.onsets.All(onset => onset >= 0))
                        {
                            negative.onsets = positive.onsets.ToArray();
                            RunSequentialCase(negative);
                        }
                        else
                        {
                            negative.status = "SKIPPED_POSITIVE_SEQUENCE_INCOMPLETE";
                            negative.phase = "requires_three_positive_onsets";
                            SaveSequentialCase(negative);
                        }
                        study.completedCases++;
                        SaveSequentialStudy(study);
                        Debug.Log("Sequential contacts: " + negative.name + " " + negative.status);
                    }
                    RequireSourcesUnchanged(sources);
                }
                study.status = study.cases.Any(c => c.status == "FAILED") ?
                    "PARTIAL_FAILURE_PROVISIONAL" : "CAPTURED_PROVISIONAL_WITH_MISSES_RETAINED";
            }
            catch (Exception error)
            {
                study.status = "FAILED_PARTIAL_EVIDENCE_RETAINED";
                study.failure = error.ToString();
                throw;
            }
            finally
            {
                SaveSequentialStudy(study);
            }
        }

        static SequentialCase NewSequentialCase(SourceRecord[] sources, int assignment, float spacing, int lane)
        {
            string attacker = assignment == 0 ? "Mankey" : "Pepe";
            return new SequentialCase
            {
                name = attacker + (lane > 0 ? "_Positive" : "_Negative") + "_Range" +
                    Mathf.RoundToInt(spacing * 100) + "_Reaction" + sources[4].localId,
                attacker = attacker, receiver = assignment == 0 ? "Pepe" : "Mankey",
                assignment = assignment, lane = lane, spacing = spacing, sources = sources.ToArray(),
                entryBlendSeconds = SequentialEntryBlend, linkBlendSeconds = SequentialLinkBlend,
                targetRegions = new[]
                {
                    bodyJabVariants && assignment == 1 ? "TorsoWithoutHeadNeckOrArms" : "Head",
                    bodyJabVariants && assignment == 1 ? "TorsoWithoutHeadNeckOrArms" : "Head", "Head"
                }
            };
        }

        static void RunSequentialCase(SequentialCase record)
        {
            try
            {
                using var player = new SequentialPlayer(record.sources, record);
                if (record.lane > 0)
                    for (int index = 0; index < 3; index++)
                        SearchSequentialStrike(player, record, index);
                player.Rebuild(3);
                VerifySequentialContacts(player, record);
                CaptureSequentialSheets(player, record);
                bool complete = record.onsets.All(onset => onset >= 0);
                bool verified = record.verification.All(sample => sample.eligible);
                record.status = complete && verified ? "COMPLETE_PROVISIONAL_REVIEW_REQUIRED" :
                    complete ? "ONSET_VERIFICATION_MISS_PROVISIONAL" : "INCOMPLETE_CONTACT_MISS_PROVISIONAL";
                record.phase = "finished";
            }
            catch (Exception error)
            {
                record.status = "FAILED";
                record.failure = error.ToString();
                Debug.LogError("Sequential candidate failed; partial evidence retained: " + record.name + "\n" + error);
            }
            finally
            {
                SaveSequentialCase(record);
                RequireSourcesUnchanged(record.sources);
            }
        }

        static void SaveSequentialCase(SequentialCase record)
        {
            File.WriteAllText(SequentialOutput + "/" + record.name + ".json", JsonUtility.ToJson(record, true));
        }

        static void SaveSequentialStudy(SequentialStudy study)
        {
            study.updatedUtc = DateTime.UtcNow.ToString("O");
            File.WriteAllText(SequentialOutput + "/Study.json", JsonUtility.ToJson(study, true));
            File.WriteAllText(SequentialOutput + "/Status.txt", study.status + " " + study.completedCases + "/24\n" +
                "Provisional only; misses, skipped negative lanes and per-case failures are retained.\n");
        }
    }
}
