using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static partial class BattlePresentationContactSetup
{
    const string ScenePath = "Assets/Scenes/BattleScene.unity";
    const string ReportFolder = "GeneratedAssets/ContactPlacementRepair";
    const float MaximumSourceGap = .15f;
    const float MaximumReceiverGap = .02f;

    [MenuItem("Tools/Battle/Contact placement/Preview repair")]
    public static void PreviewContactPlacement() => Run(false, false);

    [MenuItem("Tools/Battle/Contact placement/Repair saved anchors")]
    public static void RepairContactPlacement() => Run(true, false);

    [MenuItem("Tools/Battle/Contact placement/Validate saved anchors")]
    public static void ValidateContactPlacement() => Run(false, true);

    public static void PreviewGreatswordContact() => Run(false, false, "Heavy_6");

    sealed class Proposal
    {
        public BattleSfxBank.Cue cue;
        public HumanBodyBones bone;
        public Vector3 offset;
        public bool accepted;
        public string reason;
        public readonly List<string> rows = new List<string>();
    }

    static bool ContactCue(BattleSfxBank.Cue cue) => cue != null &&
        (cue.group == "heavy_hit" || cue.group == "light_hit" || cue.group == "stab_hit");

    static void Run(bool apply, bool validate, string moveFilter = null)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Contact calibration requires Edit Mode.");
        Directory.CreateDirectory(ReportFolder);
        string mode = validate ? "Validation" : apply ? "Repair" : "Preview";
        if (moveFilter != null) mode += "_" + moveFilter;
        var report = new StringBuilder("Exact authored seconds; evaluated triangle surfaces; distances in metres.\n");
        var csv = new StringBuilder("fighter,move,cue,seconds,source,mirrored,oldBone,newBone,offsetX,offsetY," +
            "offsetZ,beforeSource,beforeReceiver,afterSource,afterReceiver,worldX,worldY,worldZ,accepted,reason\n");
        var geometry = new List<string>();
        var proposals = new Dictionary<BattleSfxBank.Cue, Proposal>();
        var scene = EditorSceneManager.OpenPreviewScene(ScenePath);
        int failures = 0;
        try
        {
            var game = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
            var bank = game.battleVfx.timeline;
            string original = EditorJsonUtility.ToJson(bank);
            var fighters = new[] { game.leftCombat, game.rightCombat };
            foreach (var fighter in fighters)
            {
                fighter.battleSfx = null;
                fighter.battleVfx = null;
                fighter.hitEffect = null;
            }
            foreach (var source in fighters)
            foreach (var move in source.lightCombatMoves.Concat(source.heavyCombatMoves).Distinct())
            {
                if (move == null || move.skill != BattleSkill.None || move.sourcePair == null) continue;
                if (moveFilter != null && move.moveName != moveFilter) continue;
                var profile = bank.FindMove(move);
                if (profile?.cues == null) continue;
                var cues = profile.cues.Where(ContactCue).ToArray();
                var authored = cues.Where(c => c.hasContactPoint && !c.damageOnLanding &&
                    !string.IsNullOrEmpty(c.contactSource)).ToArray();
                foreach (var cue in cues.Except(authored))
                    report.AppendLine(source.name + "/" + move.moveName + " " + cue.seconds.ToString("R") +
                        "s unchanged: no existing eligible authored anchor.");
                if (authored.Length == 0) continue;
                var target = fighters.Single(f => f != source);
                foreach (bool mirrored in new[] { false, true })
                {
                    foreach (var fighter in fighters) fighter.ResetCombat();
                    source.Animator.transform.position = Vector3.zero;
                    target.Animator.transform.position = Vector3.right * move.attackRange * (mirrored ? -1 : 1);
                    if (!source.ExecuteAttack(move, target))
                        throw new InvalidOperationException("Cannot evaluate " + source.name + "/" + move.moveName);
                    var pair = source.SourcePlayback;
                    try
                    {
                        foreach (var cue in authored)
                        {
                            if (!proposals.TryGetValue(cue, out var proposal))
                            {
                                proposal = new Proposal
                                {
                                    cue = cue,
                                    bone = cue.contactBone,
                                    offset = cue.contactOffset
                                };
                                proposals.Add(cue, proposal);
                            }
                            try
                            {
                                if (!float.IsFinite(cue.seconds) || cue.seconds < 0 || cue.seconds > pair.Duration)
                                    throw new InvalidOperationException("Cue is outside source duration.");
                                pair.EvaluateAt(cue.seconds);
                                var body = Receiver(target, geometry);
                                var striker = Striker(pair, source, cue.contactSource, geometry);
                                Inspect(source, target, move, Array.IndexOf(cues, cue), proposal,
                                    body, striker, mirrored, validate);
                            }
                            catch (Exception error)
                            {
                                proposal.accepted = false;
                                proposal.reason = error.Message;
                                report.AppendLine(source.name + "/" + move.moveName + " " +
                                    cue.seconds.ToString("R") + "s unchanged: " + error.Message);
                                failures++;
                            }
                        }
                    }
                    finally
                    {
                        pair.Cancel();
                    }
                }
            }
            // Nothing has touched the bank while evaluating either facing direction.
            if (EditorJsonUtility.ToJson(bank) != original)
                throw new InvalidOperationException("Evaluation unexpectedly mutated the timeline; refusing to save.");
            foreach (var proposal in proposals.Values)
            {
                foreach (string row in proposal.rows)
                    csv.AppendLine(row + "," + proposal.accepted + "," + Csv(proposal.reason));
                report.AppendLine(proposal.cue.seconds.ToString("R") + "s " + proposal.cue.contactSource +
                    ": " + (proposal.accepted ? "accepted" : "unchanged") + "; " + proposal.reason);
                if (validate && !proposal.accepted) failures++;
            }
            int changed = proposals.Values.Count(p => p.accepted);
            if (apply && changed > 0)
            {
                Undo.RecordObject(bank, "Repair battle contact placement");
                foreach (var proposal in proposals.Values.Where(p => p.accepted))
                {
                    proposal.cue.contactBone = proposal.bone;
                    proposal.cue.contactOffset = proposal.offset;
                }
                EditorUtility.SetDirty(bank);
                AssetDatabase.SaveAssetIfDirty(bank);
            }
            report.AppendLine(mode + ": " + proposals.Count + " authored cues; " + changed +
                (validate ? " valid" : apply ? " saved" : " proposed") + "; evaluation failures=" + failures);
            Debug.Log("Contact placement " + mode + ": " + ReportFolder + "/" + mode + ".csv");
            if (validate && failures > 0)
                throw new InvalidOperationException("Saved contact validation has failures; see " + ReportFolder);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            File.WriteAllText(ReportFolder + "/" + mode + ".txt", report.ToString());
            File.WriteAllText(ReportFolder + "/" + mode + ".csv", csv.ToString());
            File.WriteAllLines(ReportFolder + "/" + mode + "Geometry.txt", geometry.Distinct());
        }
    }

    static void Inspect(CharacterCombat source, CharacterCombat target, CombatTripletData move,
        int index, Proposal proposal, Surface body, Surface striker, bool mirrored, bool validate)
    {
        var cue = proposal.cue;
        var previousBone = target.Animator.GetBoneTransform(cue.contactBone);
        if (!previousBone) throw new InvalidOperationException("Existing anchor bone is missing.");
        Vector3 before = previousBone.TransformPoint(cue.contactOffset);
        float beforeSource = striker.Distance(before);
        float beforeReceiver = body.Distance(before);
        if (!mirrored && !validate)
        {
            Vector3 contact = Contact(striker, body);
            var anchor = NearestAnchor(target, contact, out proposal.bone);
            proposal.offset = anchor.InverseTransformPoint(contact);
        }
        var chosen = target.Animator.GetBoneTransform(proposal.bone);
        if (!chosen) throw new InvalidOperationException("Proposed anchor bone is missing.");
        Vector3 after = chosen.TransformPoint(proposal.offset);
        float afterSource = striker.Distance(after);
        float afterReceiver = body.Distance(after);
        bool nearby = afterSource <= MaximumSourceGap && afterReceiver <= MaximumReceiverGap;
        bool improves = afterSource + .01f < beforeSource || afterReceiver + .01f < beforeReceiver;
        bool safe = nearby && afterSource <= beforeSource + .005f && afterReceiver <= beforeReceiver + .005f;
        if (!mirrored)
        {
            proposal.accepted = validate ? nearby : safe && improves;
            proposal.reason = validate ? nearby ? "Saved anchor near both surfaces" : "Saved anchor exceeds gap limit" :
                !nearby ? "No close contact at fixed time; preserved" : !safe ? "Would worsen placement; preserved" :
                !improves ? "Already close; preserved" : "Improves evaluated geometry";
        }
        else if (proposal.accepted && !(validate ? nearby : safe))
        {
            proposal.accepted = false;
            proposal.reason = "Mirrored placement fails distance limits; preserved";
        }
        proposal.rows.Add(FormattableString.Invariant($"{Csv(source.name)},{Csv(move.moveName)},{index},") +
            FormattableString.Invariant($"{cue.seconds:R},{Csv(cue.contactSource)},{mirrored},") +
            FormattableString.Invariant($"{cue.contactBone},{proposal.bone},") +
            FormattableString.Invariant($"{proposal.offset.x:R},{proposal.offset.y:R},{proposal.offset.z:R},") +
            FormattableString.Invariant($"{beforeSource:R},{beforeReceiver:R},{afterSource:R},{afterReceiver:R},") +
            FormattableString.Invariant($"{after.x:R},{after.y:R},{after.z:R}"));
    }

    static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
}
