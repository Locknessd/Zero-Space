using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapSetup
    {
        const string Folder = "Assets/CombatExpansion/Actions/";
        const string Output = "GeneratedAssets/CombatExpansion/SlapStudy";

        public static CombatTripletData MakeMove(CharacterCombat source, CharacterCombat target, int sequence)
        {
            var move = CombatExpansionSlapStudy.MakeGroundedFaceMove(source, target, sequence);
            var spec = FindSpec(source.name, sequence);
            move.actionDefinition = AssetDatabase.LoadAssetAtPath<CombatActionDefinition>(Folder + spec.Key + ".asset");
            return move;
        }

        [MenuItem("Tools/Battle/Combat Expansion/Install measured SlapFace pairs")]
        public static void Install()
        {
            if (!ContactsReviewed)
                throw new InvalidOperationException("Review and finalize grounded SlapFace contacts first.");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("SlapFace installation requires Edit Mode.");
            RequireEvidence();
            var scene = SceneManager.GetSceneByPath(CombatExpansionInventory.Battle);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Open BattleScene first.");
            var game = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<GameManager>(true)).Single();
            var bank = game.battleSfx ? game.battleSfx.bank : null;
            if (!bank || !game.battleVfx || game.battleVfx.timeline != bank)
                throw new InvalidOperationException("BattleScene needs the same sound and VFX timeline bank.");
            ValidateAudio(bank);
            var fighters = new[] { game.leftCombat, game.rightCombat };
            if (fighters.Any(f => !f || !f.Animator || !f.Animator.avatar) ||
                !fighters.Select(f => f.name).OrderBy(n => n).SequenceEqual(new[] { "Mankey", "Pepe" }))
                throw new InvalidOperationException("BattleScene must contain saved Mankey and Pepe fighters.");
            var report = new StringBuilder("SlapFace installation authoring report; gameplay acceptance pending.\n");
            report.AppendLine("Two explicit actions, each with per-attacker presentation assets; random pool disabled.");
            report.AppendLine("Source seconds; measured skin surfaces; no bone edits or enlarged hitboxes.");
            var measurements = MeasureProfiles(report);
            var definitions = new Dictionary<string, CombatActionDefinition>();
            var replacements = new Dictionary<CharacterCombat, CombatTripletData[]>();
            try
            {
                foreach (var source in fighters)
                {
                    var target = fighters.Single(f => f != source);
                    var moves = (source.lightCombatMoves ?? Array.Empty<CombatTripletData>()).ToList();
                    foreach (var spec in Specs.Where(s => s.attacker == source.name))
                    {
                        var move = CombatExpansionSlapStudy.MakeGroundedFaceMove(source, target, spec.sequence);
                        ValidateMove(move, spec);
                        var measured = measurements[spec.Key];
                        if (source.Animator.avatar != measured.attacker || target.Animator.avatar != measured.victim)
                            throw new InvalidOperationException("Live fighter avatar differs from saved calibration.");
                        var definition = Definition(move, spec, measured);
                        definitions.Add(spec.Key, definition);
                        move.actionDefinition = definition;
                        var errors = definition.Validate(move, bank).ToArray();
                        if (errors.Length > 0 || bank.FindMove(move) != definition.presentationProfile)
                            throw new InvalidOperationException("Invalid " + spec.Key + ": " + string.Join("\n", errors));
                        moves.RemoveAll(existing => existing != null && existing.moveName == spec.Id);
                        moves.Add(move);
                    }
                    replacements.Add(source, moves.ToArray());
                }
                ValidateAssetPaths();
                var saved = definitions.ToDictionary(entry => entry.Key,
                    entry => SaveDefinition(entry.Key, entry.Value));
                Undo.RecordObjects(fighters.Cast<UnityEngine.Object>().ToArray(), "Install measured SlapFace actions");
                foreach (var source in fighters)
                {
                    foreach (var move in replacements[source])
                    {
                        if (move == null)
                            continue;
                        string key = move.moveName + "_" + source.name;
                        if (saved.TryGetValue(key, out var definition))
                            move.actionDefinition = definition;
                    }
                    source.lightCombatMoves = replacements[source];
                    EditorUtility.SetDirty(source);
                }
                CombatExpansionSceneActionSave.SaveLight(scene, fighters, Specs.Select(s => s.Id).Distinct().ToArray());
                foreach (var spec in Specs)
                    report.AppendLine("Installed " + Folder + spec.Key + ".asset; actionId=" + spec.Id);
                report.AppendLine("Server accepted exchange owns damage; no invented stun or damage.");
                report.AppendLine("Existing light_hit and routed body layers are interim light-body presentation; " +
                    "slap-specific audio tuning remains pending.");
                report.AppendLine("PENDING: live gameplay, queue/browser/exact animationId, cancellation, both roles " +
                    "and directions, surviving standing recovery and lethal visual treatment/KO validation.");
                report.AppendLine("Full source duration is preserved; no shortened native continuation. " +
                    "Standing recovery is configured at 0.3s after source completion, not gameplay-verified here.");
                Directory.CreateDirectory(Output);
                File.WriteAllText(Output + "/Installation.txt", report.ToString());
            }
            finally
            {
                foreach (var definition in definitions.Values)
                    if (definition && !EditorUtility.IsPersistent(definition))
                        UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        static void RequireEvidence()
        {
            RequireStatus(Output + "/FaceContactsGrounded/Status.txt", "CAPTURED");
            for (int sequence = 1; sequence <= 2; sequence++)
                RequireStatus(Output + "/Grounding/Sequence" + sequence + "GroundingValidation.txt", "PASS");
        }

        static void RequireStatus(string path, string prefix)
        {
            if (!File.Exists(path) || !File.ReadAllText(path).StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidOperationException("Required evidence missing or incomplete: " + path);
        }

        static void ValidateAssetPaths()
        {
            if (!AssetDatabase.IsValidFolder(Folder.TrimEnd('/')))
                throw new InvalidOperationException("Missing existing action folder: " + Folder);
            foreach (var spec in Specs)
            {
                string path = Folder + spec.Key + ".asset";
                var existing = AssetDatabase.LoadMainAssetAtPath(path);
                if (existing && !(existing is CombatActionDefinition) || !existing && File.Exists(path))
                    throw new InvalidOperationException("Action path is occupied by an unexpected asset: " + path);
            }
        }

        static CombatActionDefinition SaveDefinition(string key, CombatActionDefinition prepared)
        {
            string path = Folder + key + ".asset";
            var saved = AssetDatabase.LoadAssetAtPath<CombatActionDefinition>(path);
            if (!saved)
            {
                AssetDatabase.CreateAsset(prepared, path);
                saved = prepared;
            }
            else
            {
                Undo.RecordObject(saved, "Configure measured SlapFace action");
                EditorUtility.CopySerialized(prepared, saved);
                EditorUtility.SetDirty(saved);
            }
            AssetDatabase.SaveAssetIfDirty(saved);
            return saved;
        }
    }
}
