using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapStudy
    {
        const string FaceOutput = "GeneratedAssets/CombatExpansion/SlapStudy/FaceContacts";

        public static void CaptureFaceContacts()
        {
            CaptureFaceContacts(false);
        }

        public static void CaptureGroundedFaceContacts()
        {
            CaptureFaceContacts(true);
        }

        static void CaptureFaceContacts(bool grounded)
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            string output = FaceOutput + (grounded ? "Grounded" : "");
            Directory.CreateDirectory(output);
            string status = output + "/Status.txt";
            var scope = new List<string>
            {
                "Two native SlapFace pairs; both actual fighter assignments and both lane directions.",
                "Candidate setup: constant 0.8m spacing, receiver source yaw 180, entry blend 0.12s.",
                "Sequence1 gives with A and receives with B; Sequence2 gives with B and receives with A.",
                "Both participants use runtime-owned unarmed equipment; source finger transfer is enabled.",
                "Exact moving right-hand skin region versus moving head skin region, including descendants.",
                "Region faces require at least 0.5 relevant bone weight on all three vertices.",
                "Unsigned triangle distance: zero can mean touching or intersection, not penetration depth.",
                "Full source duration at 30Hz, candidate contact windows at 240Hz, endpoints included.",
                "Sequence1 contact window [1.25,1.6]s; Sequence2 [0.78,1.12]s from paired motion review.",
                "Full visible body minimum Y is measured separately for both participants at every sample.",
                grounded ? "Prepared source clips with authored per-avatar grounding correction." :
                    "Original prepared source clips and initial placement only; no grounding correction yet.",
                "No accepted damage, gameplay registration or presentation profile is assigned by this capture."
            };
            File.WriteAllText(status, "RUNNING face-contact study 0/8.\n");
            SourceRecord[] sources = null;
            try
            {
                sources = ResolveSources();
                int completed = 0;
                for (int sequence = 1; sequence <= 2; sequence++)
                for (int assignment = 0; assignment < 2; assignment++)
                foreach (int direction in new[] { 1, -1 })
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
                            throw new InvalidOperationException("Cannot initialize Slap fighter " + fighter.name);
                    }
                    var grounding = grounded ? AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(
                        FaceGroundingPath(sequence)) : null;
                    if (grounded && !grounding)
                        throw new InvalidOperationException("Bake and validate Slap grounding before this capture.");
                    CaptureFacePair(session.Fighters, sources, sequence, assignment, direction,
                        scope, output, grounding);
                    File.WriteAllLines(output + "/Scope.txt", scope);
                    File.WriteAllText(status, "RUNNING face-contact study " + ++completed + "/8.\n");
                }
                File.WriteAllText(status, "CAPTURED eight face-contact pairs.\n" +
                    "Contact acceptance, presentation and gameplay integration remain pending.\n");
            }
            catch (Exception error)
            {
                File.WriteAllText(status, "FAILED: " + error + "\nPartial results are incomplete evidence.\n");
                throw;
            }
            finally
            {
                File.WriteAllLines(output + "/Scope.txt", scope);
                if (sources != null)
                    RequireSourceFilesUnchanged(sources);
            }
        }

        static void CaptureFacePair(CharacterCombat[] fighters, SourceRecord[] sources, int sequence,
            int assignment, int direction, List<string> scope, string output, FrankPairGrounding grounding)
        {
            var source = fighters[assignment];
            var target = fighters[1 - assignment];
            string label = "Sequence" + sequence;
            var attack = sources.Single(s => s.label == label + (sequence == 1 ? "_A" : "_B"));
            var reaction = sources.Single(s => s.label == label + (sequence == 1 ? "_B" : "_A"));
            var move = CandidateMove(source, target, attack, reaction, 180, .8f);
            move.grounding = grounding;
            source.Animator.transform.SetPositionAndRotation(Vector3.left * direction * .4f,
                Quaternion.LookRotation(Vector3.right * direction));
            target.Animator.transform.SetPositionAndRotation(Vector3.right * direction * .4f,
                Quaternion.LookRotation(Vector3.left * direction));
            if (!source.ExecuteAttack(move, target) || !source.SourcePlayback)
                throw new InvalidOperationException("Runtime rejected Slap face-contact pair.");
            var pair = source.SourcePlayback;
            try
            {
                pair.AttackerActor.Pose.transferFingers = true;
                pair.ReceiverActor.Pose.transferFingers = true;
                pair.EvaluateAt(0);
                CheckCandidateEquipment(pair, fighters);
                using var face = new CombatExpansionAxeDenseStudy.SkinRegionProbe(source,
                    HumanBodyBones.RightHand, target, HumanBodyBones.Head);
                var attackerBody = new CombatExpansionAxeDenseStudy.MovingBodyProbe(source);
                var receiverBody = new CombatExpansionAxeDenseStudy.MovingBodyProbe(target);
                var hand = source.Animator.GetBoneTransform(HumanBodyBones.RightHand);
                var head = target.Animator.GetBoneTransform(HumanBodyBones.Head);
                string stem = label + "_" + source.name + "_" + target.name + "_" +
                    (direction > 0 ? "Positive" : "Negative");
                scope.Add(stem + " attack=" + CombatExpansionInventory.Identity(attack.asset) +
                    "; reaction=" + CombatExpansionInventory.Identity(reaction.asset) +
                    "; grounding=" + (grounding ? CombatExpansionInventory.Identity(grounding) : "none"));
                scope.Add(face.SelectionSummary);
                var record = new CandidateRecord
                {
                    name = stem,
                    attacker = source.name,
                    receiver = target.name,
                    sequence = sequence,
                    receiverYaw = 180,
                    spacing = .8f,
                    duration = pair.Duration,
                    sheetSeconds = FaceSheetTimes(sequence, pair.Duration)
                };
                var rows = new StringBuilder(FaceHeader);
                var records = new Dictionary<float, FaceRow>();
                var diagnostics = new List<string>();
                var nodes = CandidateNodes(pair, source, target);
                var bounds = new Bounds(source.Animator.GetBoneTransform(HumanBodyBones.Hips).position, Vector3.zero);
                foreach (float seconds in FaceTimes(sequence, pair.Duration, record.sheetSeconds))
                {
                    pair.EvaluateAt(seconds);
                    diagnostics.Clear();
                    var contact = face.Measure(diagnostics);
                    attackerBody.Update(diagnostics);
                    receiverBody.Update(diagnostics);
                    var row = new FaceRow(contact, attackerBody.MinimumY, receiverBody.MinimumY, hand, head);
                    records.Add(seconds, row);
                    AppendFaceRow(rows, seconds, row, source, target);
                    foreach (var node in nodes)
                        bounds.Encapsulate(node.node.position);
                    if (seconds == 0)
                        scope.AddRange(diagnostics);
                }
                File.WriteAllText(output + "/" + stem + ".csv", rows.ToString());
                DescribeFaceCapture(stem, sequence, records, scope);
                bounds.Encapsulate(new Vector3(bounds.center.x, 0, bounds.center.z));
                bounds.Expand(.4f);
                using var rendering = new CandidateRendering(source.gameObject.scene, fighters, pair);
                float worst = 0;
                float worstAngle = 0;
                rendering.Write(output + "/" + stem, bounds, record, seconds =>
                {
                    pair.EvaluateAt(seconds);
                    diagnostics.Clear();
                    var actual = face.Measure(diagnostics);
                    var expected = records[seconds];
                    // Intersecting faces can have several equally close points; compare the gap and stable poses.
                    worst = Mathf.Max(worst, Mathf.Abs(actual.gap - expected.contact.gap),
                        Vector3.Distance(hand.position, expected.hand),
                        Vector3.Distance(head.position, expected.head));
                    worstAngle = Mathf.Max(worstAngle, Quaternion.Angle(hand.rotation, expected.handRotation),
                        Quaternion.Angle(head.rotation, expected.headRotation));
                    if (worst > .001f || worstAngle > .1f)
                        throw new InvalidOperationException("Hand/head geometry changed on seek: " + stem);
                });
                scope.Add(FormattableString.Invariant($"{stem} maxReseekGapOrPivotError={worst:R}m; ") +
                    FormattableString.Invariant($"maxReseekRotationError={worstAngle:R}deg"));
            }
            finally
            {
                pair.Cancel();
            }
        }
    }
}
