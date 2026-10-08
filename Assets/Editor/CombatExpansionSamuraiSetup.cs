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
    public static partial class CombatExpansionSamuraiSetup
    {
        internal const string Id = "Samurai_Execution01";
        internal const string Folder = "Assets/CombatExpansion/Actions/";
        internal const string GroundingPath =
            "Assets/CombatExpansion/SamuraiStudy/Grounding/Samurai_Execution01_Grounding.asset";
        internal const string RecoveryPath = Folder + "Frank_GreatSword_ProneRecovery_Grounding.asset";
        internal const string Output = "GeneratedAssets/CombatExpansion/SamuraiStudy/Installation";
        internal const float Duration = 3.1666667f;
        internal const float AttackDuration = 2.8666668f;

        public static CombatTripletData MakeExecution01Move(CharacterCombat source, CharacterCombat target)
        {
            if (!source || !target || !new[] { "Mankey", "Pepe" }.Contains(source.name) ||
                source.name == target.name || !new[] { "Mankey", "Pepe" }.Contains(target.name))
                throw new InvalidOperationException("Execution01 requires the measured Mankey/Pepe pairing.");
            var move = CombatExpansionSamuraiStudy.MakePairMove(source, target, 1);
            move.moveName = Id;
            move.requiresExplicitSelection = true;
            move.weapon = TrumpWeaponManager.WeaponType.Katana;
            move.attackRange = source.name == "Mankey" ? 1.64f : 1.7f;
            move.sourcePair.receiverOffset = Vector3.forward * move.attackRange;
            move.sourcePair.transferReceiverFingers = true;
            move.grounding = RequiredAsset<FrankPairGrounding>(GroundingPath);
            string path = AssetDatabase.GUIDToAssetPath("157da7dff6b3b3148a29246060747ce5");
            move.getUpAnim = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Single(c => CombatExpansionInventory.Identity(c) ==
                    "157da7dff6b3b3148a29246060747ce5:1827226128182048838");
            move.sourcePair.getUp = move.getUpAnim;
            move.sourcePair.recoveryGrounding = RequiredAsset<FrankPairGrounding>(RecoveryPath);
            move.sourcePair.recoveryBlendSeconds = .12f;
            move.actionDefinition = AssetDatabase.LoadAssetAtPath<CombatActionDefinition>(ActionPath(source.name));
            ValidateMove(move, source, target);
            return move;
        }

        [MenuItem("Tools/Battle/Combat Expansion/Install measured Samurai Execution01")]
        public static void InstallExecution01()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Execution01 installation requires Edit Mode.");
            RequireStatus("GeneratedAssets/CombatExpansion/SamuraiStudy/BladeContacts/Presentation/Status.txt", "PASS");
            RequireStatus("GeneratedAssets/CombatExpansion/SamuraiStudy/BladeContacts/" +
                "Execution01GroundingValidation.txt", "PASS");
            RequireStatus("GeneratedAssets/CombatExpansion/SamuraiStudy/BladeContacts/ContactAuthoring/Status.txt",
                "CAPTURED four cases; backward seeks stable.");
            var scene = SceneManager.GetSceneByPath(CombatExpansionInventory.Battle);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Open BattleScene first.");
            var game = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
            var fighters = new[] { game.leftCombat, game.rightCombat };
            if (fighters.Any(f => !f || !f.Animator || !f.Animator.avatar) ||
                !fighters.Select(f => f.name).OrderBy(n => n).SequenceEqual(new[] { "Mankey", "Pepe" }))
                throw new InvalidOperationException("BattleScene requires the saved Mankey and Pepe fighters.");
            var bank = game.battleSfx ? game.battleSfx.bank : null;
            if (!bank || !game.battleVfx || game.battleVfx.timeline != bank)
                throw new InvalidOperationException("Execution01 requires shared sound and VFX timelines.");
            ValidateAudio(bank);
            ValidateAssetPaths();
            CombatExpansionSceneActionSave.ValidatePreservation();
            var report = new StringBuilder("Samurai Execution01 installation; Play Mode acceptance pending.\n");
            var measured = CombatExpansionSamuraiStudy.MeasureExecution01Installation(report);
            var definitions = new Dictionary<string, CombatActionDefinition>();
            var replacements = new Dictionary<CharacterCombat, CombatTripletData[]>();
            try
            {
                foreach (var source in fighters)
                {
                    var target = fighters.Single(f => f != source);
                    var move = MakeExecution01Move(source, target);
                    var evidence = measured.Single(m => m.attackerName == source.name);
                    if (source.Animator.avatar != evidence.attacker || target.Animator.avatar != evidence.victim)
                        throw new InvalidOperationException("Live avatar differs from measured saved avatar.");
                    var definition = Definition(move, evidence);
                    definitions.Add(source.name, definition);
                    move.actionDefinition = definition;
                    var errors = definition.Validate(move, bank).ToArray();
                    if (errors.Length != 0 || bank.FindMove(move) != definition.presentationProfile)
                        throw new InvalidOperationException(
                            "Invalid Execution01 definition: " + string.Join("\n", errors));
                    if (!BattleHitDamageSequence.ContactTimes(evidence.profile, Duration)
                        .SequenceEqual(evidence.profile.cues.Where(c => c.group == "stab_hit" ||
                            c.group == "heavy_hit").Select(c => c.seconds)) ||
                        BattleHitDamageSequence.ContactTimes(evidence.profile, Duration).Length != 2)
                        throw new InvalidOperationException("Execution01 must contain exactly two damaging contacts.");
                    var moves = (source.heavyCombatMoves ?? Array.Empty<CombatTripletData>()).ToList();
                    moves.RemoveAll(m => m != null && m.moveName == Id);
                    moves.Add(move);
                    replacements.Add(source, moves.ToArray());
                }
                ValidateAssetPaths();
                var saved = definitions.ToDictionary(e => e.Key, e => SaveDefinition(e.Key, e.Value));
                Undo.RecordObjects(fighters.Cast<UnityEngine.Object>().ToArray(), "Install Samurai Execution01");
                foreach (var source in fighters)
                {
                    var installed = replacements[source].Single(m => m != null && m.moveName == Id);
                    installed.actionDefinition = saved[source.name];
                    source.heavyCombatMoves = replacements[source];
                    EditorUtility.SetDirty(source);
                    report.AppendLine("Installed " + ActionPath(source.name));
                }
                CombatExpansionSceneActionSave.Save(scene, fighters, new[] { Id });
                report.AppendLine("Only matching heavy move IDs persisted; unrelated scene edits remain live.");
                report.AppendLine("Explicit per-attacker profiles; shared bank unchanged; " +
                    "server outcome rules retained.");
                report.AppendLine("PENDING Play Mode: enqueue/browser/exact animationId, both directions and roles, " +
                    "prone getup transition, surviving/lethal outcomes, cancellation, equipment and feedback cleanup.");
                report.AppendLine("Whooshes at 0.27 and 1.9 seconds are provisional; " +
                    "validate sound and VFX in gameplay.");
                Directory.CreateDirectory(Output);
                File.WriteAllText(Output + "/Execution01Installation.txt", report.ToString());
            }
            finally
            {
                foreach (var definition in definitions.Values)
                    if (definition && !EditorUtility.IsPersistent(definition))
                        UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        internal static T RequiredAsset<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset || EditorUtility.IsDirty(asset))
                throw new InvalidOperationException("Missing or dirty required asset: " + path);
            return asset;
        }

        static string ActionPath(string attacker) => Folder + Id + "_" + attacker + ".asset";

        static void RequireStatus(string path, string prefix)
        {
            if (!File.Exists(path) || !File.ReadAllText(path).StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidOperationException("Required evidence missing or incomplete: " + path);
        }

        static void ValidateAssetPaths()
        {
            if (!AssetDatabase.IsValidFolder(Folder.TrimEnd('/')))
                throw new InvalidOperationException("Missing action folder: " + Folder);
            foreach (string attacker in new[] { "Mankey", "Pepe" })
            {
                string path = ActionPath(attacker);
                var existing = AssetDatabase.LoadMainAssetAtPath(path);
                var action = existing as CombatActionDefinition;
                if (existing && (!action || action.actionId != Id || EditorUtility.IsDirty(existing)) ||
                    !existing && (File.Exists(path) || File.Exists(path + ".meta")))
                    throw new InvalidOperationException("Occupied or dirty action asset: " + path);
            }
        }

        static CombatActionDefinition SaveDefinition(string attacker, CombatActionDefinition prepared)
        {
            string path = ActionPath(attacker);
            var saved = AssetDatabase.LoadAssetAtPath<CombatActionDefinition>(path);
            if (!saved)
            {
                AssetDatabase.CreateAsset(prepared, path);
                saved = prepared;
            }
            else
            {
                Undo.RecordObject(saved, "Configure measured Samurai Execution01");
                EditorUtility.CopySerialized(prepared, saved);
                EditorUtility.SetDirty(saved);
            }
            AssetDatabase.SaveAssetIfDirty(saved);
            return saved;
        }
    }
}
