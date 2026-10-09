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
    public static partial class CombatExpansionSamurai10Setup
    {
        internal const string Id = "Samurai_Execution10";
        internal const float Duration = 2.666666746f;
        internal const string Folder = "Assets/CombatExpansion/Actions/";
        internal const string GroundingPath =
            "Assets/CombatExpansion/SamuraiStudy/Grounding/Samurai_Execution10_Grounding.asset";
        internal const string RecoveryPath = Folder + "Frank_GreatSword_SupineRecovery_Grounding.asset";
        internal const string GroundingId = "54888ce163d25b68b81efc494e6b1403:11400000";
        internal const string RecoveryId = "0e3315c06eebc2f62b838fffb249aca3:11400000";
        internal const string GetUpId = "f83ba830485ff2b46ba1d67ec650f1e0:1827226128182048838";
        internal const string Output = "GeneratedAssets/CombatExpansion/SamuraiStudy/Installation";
        internal static float LandingTime(string source) => source == "Mankey" ? 1.45f : 1.4625f;
        internal static float StabTime(string source) => source == "Mankey" ? 1.879166722f : 1.862499952f;
        internal static string AttackerDriver(string source) => source == "Mankey"
            ? "224e94b27f72de65aa8403d8a294fde0:2627981156790044268"
            : "e843971f00482b8af9b3e546a40aa446:7468503520573832048";
        internal static string ReceiverDriver(string target) => target == "Pepe"
            ? "22b682ea72ad141548c935b490a1b6ba:5840121589346898076"
            : "e9bda2f6da685faedac3c08af217f164:4668455661232588296";

        public static CombatTripletData MakeExecution10Move(CharacterCombat source, CharacterCombat target)
        {
            if (!source || !target || source == target || source.name == target.name ||
                !new[] { "Mankey", "Pepe" }.Contains(source.name) ||
                !new[] { "Mankey", "Pepe" }.Contains(target.name))
                throw new InvalidOperationException("Execution10 requires the measured Mankey/Pepe pair.");
            var move = CombatExpansionSamuraiStudy.MakePairMove(source, target, 10);
            move.moveName = Id;
            move.requiresExplicitSelection = true;
            move.weapon = TrumpWeaponManager.WeaponType.Katana;
            move.sourcePair.transferReceiverFingers = true;
            move.grounding = CombatExpansionSamuraiSetup.RequiredAsset<FrankPairGrounding>(GroundingPath);
            move.getUpAnim = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(GetUpId.Split(':')[0]))
                .OfType<AnimationClip>().Single(c => CombatExpansionInventory.Identity(c) == GetUpId);
            move.sourcePair.getUp = move.getUpAnim;
            move.sourcePair.recoveryGrounding =
                CombatExpansionSamuraiSetup.RequiredAsset<FrankPairGrounding>(RecoveryPath);
            move.sourcePair.recoveryBlendSeconds = .12f;
            move.actionDefinition = AssetDatabase.LoadAssetAtPath<CombatActionDefinition>(ActionPath(source.name));
            ValidateMove(move, source, target);
            return move;
        }

        [MenuItem("Tools/Battle/Combat Expansion/Install measured Samurai Execution10")]
        public static void InstallExecution10()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Execution10 installation requires Edit Mode.");
            var scene = SceneManager.GetSceneByPath(CombatExpansionInventory.Battle);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Open BattleScene first.");
            var game = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
            var fighters = new[] { game.leftCombat, game.rightCombat };
            if (fighters.Any(f => !f || !f.Animator || !f.Animator.avatar) ||
                !fighters.Select(f => f.name).OrderBy(n => n).SequenceEqual(new[] { "Mankey", "Pepe" }))
                throw new InvalidOperationException("BattleScene requires saved Mankey and Pepe fighters.");
            var bank = game.battleSfx ? game.battleSfx.bank : null;
            if (!bank || !game.battleVfx || game.battleVfx.timeline != bank)
                throw new InvalidOperationException("Execution10 requires the existing shared sound/VFX bank.");
            ValidateAudio(bank);
            CombatExpansionSamuraiStudy.ValidateExecution10Presentation(game.battleVfx);
            ValidateAssetPaths();
            CombatExpansionSceneActionSave.ValidatePreservation();
            var report = new StringBuilder("Samurai Execution10 installation; gameplay acceptance PENDING.\n");
            var measured = CombatExpansionSamuraiStudy.MeasureExecution10Installation(report);
            var definitions = new Dictionary<string, CombatActionDefinition>();
            var replacements = new Dictionary<CharacterCombat, CombatTripletData[]>();
            try
            {
                foreach (var source in fighters)
                {
                    var target = fighters.Single(f => f != source);
                    var move = MakeExecution10Move(source, target);
                    var evidence = measured.Single(m => m.attackerName == source.name);
                    if (source.Animator.avatar != evidence.attacker || target.Animator.avatar != evidence.victim)
                        throw new InvalidOperationException("Live avatars differ from measured saved avatars.");
                    var definition = Definition(move, evidence);
                    definitions.Add(source.name, definition);
                    move.actionDefinition = definition;
                    ValidateDefinition(move, bank);
                    var moves = (source.heavyCombatMoves ?? Array.Empty<CombatTripletData>()).ToList();
                    moves.RemoveAll(m => m != null && m.moveName == Id);
                    moves.Add(move);
                    replacements.Add(source, moves.ToArray());
                    report.AppendLine("Action=" + ActionPath(source.name));
                    report.AppendLine(definition.gameplayTrigger);
                    report.AppendLine(definition.entryAndInterruption);
                    report.AppendLine(definition.recovery);
                    report.AppendLine(definition.presentation);
                }
                report.AppendLine("PENDING actual Play Mode: explicit enqueue/browser/server animationId, both roles " +
                    "and directions, two server-owned damage contacts, lethal/surviving endings, cancellation, " +
                    "supine getup, owned equipment cleanup, listening and visible sound/VFX/trail review.");
                report.AppendLine("Only matching heavy move IDs persisted; shared bank and other attacks preserved. " +
                    "Evidence is provisional presentation evidence, not gameplay acceptance.");
                ValidateAssetPaths();
                Commit(scene, fighters, definitions, replacements, report.ToString());
            }
            finally
            {
                foreach (var definition in definitions.Values)
                    if (definition && !EditorUtility.IsPersistent(definition))
                        UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        static string ActionPath(string attacker) => Folder + Id + "_" + attacker + ".asset";

        internal static void RequireStatus(string path, string prefix)
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
    }
}
