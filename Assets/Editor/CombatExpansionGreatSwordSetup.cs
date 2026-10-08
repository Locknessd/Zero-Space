using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordSetup
    {
        const string Folder = "Assets/CombatExpansion/Actions/";

        [MenuItem("Tools/Battle/Combat Expansion/Install measured GreatSword pairs")]
        public static void Install()
        {
            if (!ContactsReviewed)
                throw new InvalidOperationException("Review and finalize GreatSword source contacts first.");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("GreatSword installation requires Edit Mode.");
            var scene = SceneManager.GetSceneByPath(CombatExpansionInventory.Battle);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Open BattleScene first.");
            ValidateSpecs();
            var game = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
            var bank = game.battleSfx ? game.battleSfx.bank : null;
            if (!bank || !game.battleVfx || game.battleVfx.timeline != bank)
                throw new InvalidOperationException("BattleScene needs its shared sound and VFX bank.");
            ValidateAudio(bank);
            var report = new StringBuilder("GreatSword installation authoring report; not gameplay acceptance.\n");
            report.AppendLine("Grounded saved BattleScene avatars, both roles and directions; source seconds.");
            report.AppendLine("Weapon mesh versus receiver surface; stored anchors are victim bone local points.");
            var profiles = MeasureProfiles(report);
            var fighters = new[] { game.leftCombat, game.rightCombat };
            var replacements = new Dictionary<CharacterCombat, CombatTripletData[]>();
            var definitions = new List<CombatActionDefinition>();
            var candidateBank = UnityEngine.Object.Instantiate(bank);
            try
            {
                candidateBank.moves = MergeProfiles(bank.moves, profiles);
                foreach (var source in fighters)
                {
                    var target = fighters.Single(f => f != source);
                    var moves = (source.heavyCombatMoves ?? Array.Empty<CombatTripletData>()).ToList();
                    foreach (var spec in Specs)
                    {
                        var move = CombatExpansionGreatSwordStudy.MakeMove(source, target, spec.index);
                        ValidateMove(move, spec);
                        var definition = definitions.SingleOrDefault(d => d.actionId == move.moveName);
                        if (!definition)
                        {
                            definition = Definition(move, spec, profiles.Single(p => p.label == move.moveName));
                            definitions.Add(definition);
                        }
                        move.actionDefinition = definition;
                        var errors = definition.Validate(move, candidateBank).ToArray();
                        if (errors.Length > 0)
                            throw new InvalidOperationException(string.Join("\n", errors));
                        moves.RemoveAll(m => m != null && (m.moveName == move.moveName ||
                            (m.attackAnim == move.attackAnim && m.hitAnim == move.hitAnim) ||
                            (m.sourcePair?.attack == move.attackAnim && m.sourcePair?.reaction == move.hitAnim)));
                        moves.Add(move);
                    }
                    replacements.Add(source, moves.ToArray());
                }
                if (!AssetDatabase.IsValidFolder(Folder.TrimEnd('/')))
                    throw new InvalidOperationException("Missing existing action asset folder: " + Folder);
                foreach (var definition in definitions)
                {
                    string path = Folder + definition.actionId + ".asset";
                    var existing = AssetDatabase.LoadMainAssetAtPath(path);
                    if (existing && !(existing is CombatActionDefinition))
                        throw new InvalidOperationException("Action asset path is occupied: " + path);
                }
                var saved = definitions.ToDictionary(d => d.actionId, SaveDefinition);
                Undo.RecordObjects(fighters.Cast<UnityEngine.Object>().Append(bank).ToArray(),
                    "Install measured GreatSword gameplay pairs");
                bank.moves = candidateBank.moves;
                foreach (var source in fighters)
                {
                    foreach (var move in replacements[source])
                        if (move != null && saved.TryGetValue(move.moveName ?? "", out var definition))
                            move.actionDefinition = definition;
                    source.heavyCombatMoves = replacements[source];
                    EditorUtility.SetDirty(source);
                }
                EditorUtility.SetDirty(bank);
                AssetDatabase.SaveAssetIfDirty(bank);
                CombatExpansionSceneActionSave.Save(scene, fighters,
                    definitions.Select(definition => definition.actionId).ToArray());
                foreach (var profile in profiles)
                {
                    report.AppendLine("Installed " + profile.label + " attack=" +
                        CombatExpansionInventory.Identity(profile.attack) + " reaction=" +
                        CombatExpansionInventory.Identity(profile.reaction));
                    foreach (var cue in profile.cues)
                        report.AppendLine(FormattableString.Invariant(
                            $"  {cue.seconds:R}s {cue.group}; source={cue.contactSource}; ") +
                            FormattableString.Invariant(
                                $"finalLanding={cue.finalLanding}; damageOnLanding={cue.damageOnLanding}"));
                }
                Directory.CreateDirectory(CombatExpansionInventory.Output);
                File.WriteAllText(CombatExpansionInventory.Output + "/GreatSwordInstallation.txt", report.ToString());
                CombatExpansionInventory.Audit();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(candidateBank);
                foreach (var definition in definitions)
                    if (!EditorUtility.IsPersistent(definition))
                        UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        static BattleSfxBank.Move[] MergeProfiles(BattleSfxBank.Move[] previous, BattleSfxBank.Move[] additions)
        {
            var result = (previous ?? Array.Empty<BattleSfxBank.Move>()).ToList();
            foreach (var profile in additions)
            {
                result.RemoveAll(p => p != null && (p.label == profile.label ||
                    p.attack == profile.attack && p.reaction == profile.reaction));
                result.Add(profile);
            }
            return result.ToArray();
        }

        static void ValidateSpecs()
        {
            if (Specs == null || Specs.Length != 4 || !Specs.Select(s => s.index).OrderBy(i => i)
                .SequenceEqual(new[] { 0, 1, 2, 3 }))
                throw new InvalidOperationException("Exactly four distinct GreatSword source specs are required.");
            foreach (var spec in Specs)
            {
                int count = spec.contacts?.Length ?? 0;
                if (count != (spec.index == 3 ? 3 : 1) || spec.groups?.Length != count ||
                    spec.regions?.Length != count || spec.directions?.Length != count || spec.swings == null ||
                    spec.swings.Length == 0 || string.IsNullOrWhiteSpace(spec.displayName))
                    throw new InvalidOperationException("Incomplete GreatSword spec " + spec.index);
                for (int i = 0; i < count; i++)
                    if (!float.IsFinite(spec.contacts[i]) || spec.contacts[i] < 1f / 60 ||
                        (i > 0 && spec.contacts[i] <= spec.contacts[i - 1]) ||
                        (spec.groups[i] != "heavy_hit" && spec.groups[i] != "stab_hit") ||
                        string.IsNullOrWhiteSpace(spec.regions[i]) || !Finite(spec.directions[i]) ||
                        spec.directions[i].sqrMagnitude < .0001f)
                        throw new InvalidOperationException("Invalid GreatSword contact " + spec.index + "/" + i);
                if (!float.IsFinite(spec.landingSeconds) || spec.landingSeconds <= spec.contacts.Last() ||
                    spec.swings.Any(t => !float.IsFinite(t) || t < 0 || t >= spec.landingSeconds))
                    throw new InvalidOperationException("Invalid GreatSword swing or landing time.");
            }
        }

        static void ValidateAudio(BattleSfxBank bank)
        {
            var required = new Queue<string>(new[] { "heavy_hit", "stab_hit", "blade_swing", "thrust_swing",
                "heavy_swing", "body_fall", "knockout_fall", "getup", "hurt_voice", "ko_impact" });
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (required.Count > 0)
            {
                string id = required.Dequeue();
                if (!visited.Add(id))
                    continue;
                var group = bank.FindGroup(id);
                if (group == null || !group.output || group.clips == null || group.clips.Length == 0 ||
                    group.clips.Any(c => !c))
                    throw new InvalidOperationException("Missing routed GreatSword audio: " + id);
                foreach (string layer in group.layers ?? Array.Empty<string>())
                    if (!string.IsNullOrWhiteSpace(layer))
                        required.Enqueue(layer);
            }
        }

        static void ValidateMove(CombatTripletData move, Spec spec)
        {
            if (move == null || !move.attackAnim || !move.hitAnim || !move.grounding || !move.getUpAnim ||
                move.sourcePair == null || !move.sourcePair.recoveryGrounding ||
                !move.sourcePair.attackerWeaponPrefab || !move.sourcePair.showWeapon ||
                move.sourcePair.attack != move.attackAnim || move.sourcePair.reaction != move.hitAnim ||
                spec.landingSeconds > Mathf.Min(move.attackAnim.length, move.hitAnim.length))
                throw new InvalidOperationException("Incomplete grounded GreatSword source pair " + spec.index);
            float duration = Mathf.Max(move.attackAnim.length, move.hitAnim.length);
            if (move.grounding.tracks == null || move.grounding.tracks.Length != 4 ||
                move.grounding.tracks.Any(track => track == null || Mathf.Abs(track.duration - duration) > .0001f))
                throw new InvalidOperationException("Rebake grounding for the current source duration: " + move.moveName);
        }

        static bool Finite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
