using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        const string PairOutput = "GeneratedAssets/CombatExpansion/SamuraiStudy/UnarmedPairs";

        public static CombatTripletData MakePairMove(CharacterCombat source, CharacterCombat target, int execution,
            AnimationClip attackOverride = null, AnimationClip reactionOverride = null)
        {
            if (execution < 1 || execution > 10 || !source || !target || source == target)
                throw new ArgumentException("A Samurai pair needs two fighters and an execution from 1 to 10.");
            var clips = ResolveSources();
            long localId = 7400000 + 2 * execution;
            var attack = clips.Single(c => c.role == Roles[0] && c.localId == localId).asset;
            var reaction = clips.Single(c => c.role == Roles[1] && c.localId == localId).asset;
            FOValidateOverrides(execution, attackOverride, reactionOverride);
            attack = attackOverride ? attackOverride : attack;
            reaction = reactionOverride ? reactionOverride : reaction;
            string attackPath = DriverPath(source.name, 0, true);
            string reactionPath = DriverPath(target.name, 1, false);
            var attackDriver = AssetDatabase.LoadAssetAtPath<FrankTestDriver>(attackPath);
            var reactionDriver = AssetDatabase.LoadAssetAtPath<FrankTestDriver>(reactionPath);
            ValidateDriver(attackDriver, 0, true, attackPath);
            ValidateDriver(reactionDriver, 1, false, reactionPath);
            return new CombatTripletData
            {
                moveName = "Samurai_Execution" + execution.ToString("D2") + "_PairStudy",
                attackAnim = attack,
                hitAnim = reaction,
                attackRange = 1.7f,
                sourcePair = new FrankBattlePair
                {
                    attack = attack,
                    reaction = reaction,
                    attackerDriver = attackDriver,
                    receiverDriver = reactionDriver,
                    receiverOffset = Vector3.forward * 1.7f,
                    receiverRotation = Quaternion.Euler(0, 180, 0),
                    maximumAlignmentError = .15f,
                    entryBlendSeconds = .12f,
                    showWeapon = true
                }
            };
        }

        public static void CaptureGameplayPairs()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            Directory.CreateDirectory(PairOutput);
            string status = PairOutput + "/Status.txt";
            File.WriteAllText(status, "RUNNING unarmed Samurai pair capture.\n");
            File.WriteAllText(PairOutput + "/Scope.txt",
                "Unregistered Samurai pairing study using CharacterCombat.ExecuteAttack and FrankBattlePairPlayback.\n" +
                "PlayerA source is the candidate initiator; PlayerB uses a renderer-free unarmed driver.\n" +
                "Both fighters use existing owned equipment suppression and cancellation cleanup.\n" +
                "No feedback profile, damage, accepted contact, recovery or gameplay approval is assigned.\n" +
                "Source offset remains unscaled 1.7m with PlayerB yaw 180; entry blend is 0.12s.\n" +
                "No grounding, pair spacing correction or forced depth lock is applied.\n" +
                "Both avatar assignments and lane directions; authoritative source seconds, sampled at 60Hz.\n" +
                "Images use fixed side/oblique study cameras and original lighting in the BattleScene preview.\n" +
                "Foreground scenery and UI are hidden only in that preview; a temporary neutral floor is shown.\n" +
                "Images and CSVs sample the same runtime pair, including explicit backwards-seek checks.\n");
            try
            {
                using var session = new SourceSession();
                foreach (var type in new[] { typeof(GameManager), typeof(MemeBattleUI), typeof(WebSocketManager),
                    typeof(MortalKombatCamera), typeof(CombatPositioningController) })
                    type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static).SetValue(null, null);
                foreach (var fighter in session.Fighters)
                {
                    fighter.battleSfx = null;
                    fighter.battleVfx = null;
                    fighter.hitEffect = null;
                    fighter.Initialize();
                }
                int completed = 0;
                for (int execution = 1; execution <= 10; execution++)
                for (int assignment = 0; assignment < 2; assignment++)
                foreach (int direction in new[] { 1, -1 })
                {
                    CaptureGameplayPair(session.Fighters, assignment, execution, direction);
                    completed++;
                    File.WriteAllText(status, "RUNNING completed pairs=" + completed + "/40.\n");
                }
                File.WriteAllText(status, "CAPTURED all 40 runtime pairs with unarmed victims.\n" +
                    "Contact alignment, grounding, reactions, presentation and gameplay integration remain required.\n");
            }
            catch (Exception error)
            {
                File.WriteAllText(status, "FAILED: " + error + "\nPartial output is not a complete capture.\n");
                throw;
            }
        }
    }
}
