using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapStudy
    {
        const string CandidateOutput = "GeneratedAssets/CombatExpansion/SlapStudy/PairCandidates";
        const string CandidateScope = "CANDIDATES ONLY: no facing, contact, gameplay hitbox, damage or action " +
            "registration is approved. Joint distances measure hand pivots to head pivots, not skin contact. " +
            "Native source clocks are clamped to each clip endpoint. Anatomical forward is the normalized " +
            "cross(RightShoulder-LeftShoulder, Head-Hips), dotted with positive world X for both roles. " +
            "This diagnostic does not constrain orientation. Entry range matches candidate spacing; " +
            "maximum alignment error 0.15m, entry blend 0.12s, positive lane only. No grounding, recovery, " +
            "feedback profile, timing offset, depth lock or pair correction. Both native drivers are unarmed; " +
            "equipment suppression is owned and released by runtime pair lifecycle. Original BattleScene " +
            "lighting, temporary neutral floor, CPU preview skins on both fighters and native actors. " +
            "Scenery/UI flags are restored. Original scene (including unsaved state) stays open. " +
            "SourceSession restores singleton references; only the positioning connection is detached " +
            "during this synchronous capture to prevent runtime evaluation touching a live scene. " +
            "No live scene component is disabled. Capture-local fighter VFX/SFX connections are cleared. " +
            "CSV includes 60Hz samples, sheet times, blend endpoint and exact clip endpoints. " +
            "Strike/reaction sheets use 16Hz focused windows plus entry, post-reaction and exact endpoints.";

        [Serializable]
        sealed class CandidateReport
        {
            public string status = "CANDIDATE";
            public string scope = CandidateScope;
            public string utc = DateTime.UtcNow.ToString("O");
            public string unityVersion = Application.unityVersion;
            public SourceRecord[] sources;
            public List<CandidateRecord> candidates = new List<CandidateRecord>();
        }

        [Serializable]
        sealed class CandidateRecord
        {
            public string status = "CANDIDATE";
            public string name, attacker, receiver, attackerDriver, receiverDriver, trajectory, measurements;
            public string attackOriginalGuid, reactionOriginalGuid, attackAdaptedIdentity, reactionAdaptedIdentity;
            public long attackOriginalLocalId, reactionOriginalLocalId;
            public int sequence, sampleCount;
            public float receiverYaw, spacing, duration, entryBlend = .12f, maximumAlignmentError = .15f;
            public Vector3 receiverOffset, lane = Vector3.right, initialAttackerPosition, initialReceiverPosition;
            public Quaternion initialAttackerRotation, initialReceiverRotation;
            public float entryAttackerFacingDot, entryReceiverFacingDot, worstBackwardsSeekMetres;
            public float minimumLeftJointDistance = float.PositiveInfinity;
            public float minimumRightJointDistance = float.PositiveInfinity;
            public float minimumLeftJointSeconds, minimumRightJointSeconds;
            public int backwardsSeekSamples;
            public float[] sheetSeconds;
            public string[] sheets;
        }

        public static void CapturePairCandidates()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            Directory.CreateDirectory(CandidateOutput);
            string status = CandidateOutput + "/Status.txt";
            File.WriteAllText(status, "RUNNING candidate grid 0/24.\n");
            File.WriteAllText(CandidateOutput + "/Scope.txt", CandidateScope + "\n");
            SourceRecord[] sources = null;
            var report = new CandidateReport();
            try
            {
                sources = ResolveSources();
                report.sources = sources;
                for (int sequence = 1; sequence <= 2; sequence++)
                for (int assignment = 0; assignment < 2; assignment++)
                foreach (float yaw in new[] { 0f, 180f })
                foreach (float spacing in new[] { .8f, 1f, 1.2f })
                {
                    using var session = new SourceSession();
                    typeof(CombatPositioningController).GetProperty("Instance",
                        BindingFlags.Public | BindingFlags.Static).SetValue(null, null);
                    foreach (var fighter in session.Fighters)
                    {
                        fighter.battleSfx = null;
                        fighter.battleVfx = null;
                        fighter.hitEffect = null;
                        if (!fighter.Initialize())
                            throw new InvalidOperationException("Could not initialize capture fighter " + fighter.name);
                    }
                    var record = CaptureCandidate(session.Fighters, sources, sequence, assignment, yaw, spacing);
                    report.candidates.Add(record);
                    File.WriteAllText(CandidateOutput + "/Study.json", JsonUtility.ToJson(report, true));
                    File.WriteAllText(status, "RUNNING candidate grid " + report.candidates.Count + "/24.\n");
                }
                RequireSourceFilesUnchanged(sources);
                File.WriteAllText(status, "CAPTURED 24/24 CANDIDATES; backwards-seek tolerance <=0.001m.\n" +
                    "No facing assumption, contact, damage or gameplay registration accepted.\n");
            }
            catch (Exception error)
            {
                report.status = "FAILED_PARTIAL_CANDIDATES";
                File.WriteAllText(CandidateOutput + "/Study.json", JsonUtility.ToJson(report, true));
                File.WriteAllText(status, "FAILED: " + error + "\nPartial evidence remains candidate-only.\n");
                throw;
            }
            finally
            {
                if (sources != null)
                    RequireSourceFilesUnchanged(sources);
            }
        }

        static CombatTripletData CandidateMove(CharacterCombat source, CharacterCombat target,
            SourceRecord attack, SourceRecord reaction, float yaw, float spacing)
        {
            string attackerPath = DriverPath(source.name, attack);
            string receiverPath = DriverPath(target.name, reaction);
            var attackerDriver = AssetDatabase.LoadAssetAtPath<FrankTestDriver>(attackerPath);
            var receiverDriver = AssetDatabase.LoadAssetAtPath<FrankTestDriver>(receiverPath);
            ValidateDriver(attackerDriver, attack, attackerPath);
            ValidateDriver(receiverDriver, reaction, receiverPath);
            foreach (var driver in new[] { attackerDriver, receiverDriver })
                if (driver.GetComponentsInChildren<Collider>(true).Any(c => c.enabled && c.gameObject.activeSelf))
                    throw new InvalidOperationException("Candidate driver has an enabled native collider.");
            return new CombatTripletData
            {
                moveName = "SlapFace_CANDIDATE_" + attack.label,
                attackAnim = attack.asset,
                hitAnim = reaction.asset,
                attackRange = spacing,
                requiresExplicitSelection = true,
                sourcePair = new FrankBattlePair
                {
                    attack = attack.asset,
                    reaction = reaction.asset,
                    attackerDriver = attackerDriver,
                    receiverDriver = receiverDriver,
                    receiverOffset = Vector3.forward * spacing,
                    receiverRotation = Quaternion.Euler(0, yaw, 0),
                    maximumAlignmentError = .15f,
                    entryBlendSeconds = .12f,
                    showWeapon = false
                }
            };
        }
    }
}
