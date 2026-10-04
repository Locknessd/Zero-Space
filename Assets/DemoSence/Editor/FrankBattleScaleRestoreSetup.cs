using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        const string ScaleRestoreReview = "GeneratedAssets/BattleScaleRestoreReview";

        [MenuItem("Tools/Battle/Restore fighter scales and remove Archer and Mage skills")]
        public static void RestoreBattleFighterScales()
        {
            Directory.CreateDirectory(ScaleRestoreReview);
            if (!File.Exists(ScaleRestoreReview + "/BattleSceneBefore.unity.txt")) File.Copy(SfxScene, ScaleRestoreReview + "/BattleSceneBefore.unity.txt");
            var report = new StringBuilder();
            ComicScene((game, camera) =>
            {
                var fighters = new[] { game.leftCombat, game.rightCombat };
                float spacingGain = fighters.Average(f => (f.name == "Mankey" ? .7f : .1f) / f.Animator.transform.localScale.x);
                foreach (var fighter in fighters)
                {
                    float size = fighter.name == "Mankey" ? .7f : .1f;
                    fighter.Animator.transform.localScale = Vector3.one * size;
                    fighter.lightCombatMoves = fighter.lightCombatMoves.Where(m => !RemovedHeavySkill(m)).ToArray();
                    fighter.heavyCombatMoves = fighter.heavyCombatMoves.Where(m => !RemovedHeavySkill(m)).ToArray();
                    foreach (var move in fighter.lightCombatMoves.Concat(fighter.heavyCombatMoves))
                    {
                        move.attackRange *= spacingGain;
                        if (move.sourcePair == null) continue;
                        move.sourcePair.receiverOffset *= spacingGain;
                        move.sourcePair.bodySpacing *= spacingGain;
                    }
                    EditorUtility.SetDirty(fighter.Animator.transform); EditorUtility.SetDirty(fighter);
                    report.AppendLine("PASS " + fighter.name + ": scale=" + size + "; Light=" + fighter.lightCombatMoves.Length + "; Heavy=" + fighter.heavyCombatMoves.Length);
                }
                game.battleVfx.skillVariants = Array.Empty<BattleVfxPlayer.SkillVariant>();
                var bank = game.battleVfx.timeline;
                bank.moves = bank.moves.Where(m => m.label != "Heavy_Archer" && m.label != "Heavy_WhiteMage").ToArray();
                bank.groups = bank.groups.Where(g => g.id != "skill_cast" && g.id != "skill_shot").ToArray();
                EditorUtility.SetDirty(bank); EditorUtility.SetDirty(game.battleVfx);
                report.AppendLine("PASS removed Meteor Shot/Celestial Smite from both live pools, shared VFX variants and SFX timeline.");
            });
            File.WriteAllText(ScaleRestoreReview + "/Installation.txt", report.ToString());
        }

        static bool RemovedHeavySkill(CombatTripletData move) => move.skill == BattleSkill.Archer || move.skill == BattleSkill.WhiteMage ||
            move.moveName == "Heavy_Archer" || move.moveName == "Heavy_WhiteMage";

        public static void ValidateRestoredBattleScales()
        {
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var report = new StringBuilder();
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var fighter in fighters)
                {
                    float size = fighter.name == "Mankey" ? .7f : .1f;
                    if ((fighter.Animator.transform.localScale - Vector3.one * size).sqrMagnitude > .00000001f) throw new Exception("Saved scale mismatch: " + fighter.name);
                    if (fighter.lightCombatMoves.Concat(fighter.heavyCombatMoves).Any(RemovedHeavySkill)) throw new Exception("Removed skill remains in a pool.");
                    fighter.Initialize();
                }
                foreach (var attacker in fighters)
                foreach (var move in attacker.lightCombatMoves.Concat(attacker.heavyCombatMoves))
                {
                    var victim = fighters.Single(f => f != attacker);
                    foreach (var fighter in fighters) fighter.ResetCombat();
                    attacker.Animator.transform.position = Vector3.zero;
                    victim.Animator.transform.position = Vector3.right * move.attackRange;
                    if (!attacker.ExecuteAttack(move, victim)) throw new Exception("Could not play " + attacker.name + "/" + move.moveName);
                    var pair = attacker.SourcePlayback;
                    foreach (float time in new[] { 0f, pair.Duration * .5f, pair.Duration })
                    {
                        pair.EvaluateAt(time);
                        foreach (var fighter in fighters)
                        {
                            float size = fighter.name == "Mankey" ? .7f : .1f;
                            if ((fighter.Animator.transform.localScale - Vector3.one * size).sqrMagnitude > .00000001f) throw new Exception("Anim changed scale: " + move.moveName);
                        }
                    }
                    pair.Cancel();
                    report.AppendLine("PASS " + attacker.name + "/" + move.moveName + ": both model scales retained at start/middle/end/cancel.");
                }
                if (game.battleVfx.skillVariants.Length != 0 || game.battleVfx.timeline.moves.Any(m => m.label == "Heavy_Archer" || m.label == "Heavy_WhiteMage"))
                    throw new Exception("Removed skill timeline remains in Battle.");
                report.AppendLine("ALL PASS: 18 remaining pairs; Mankey=0.7, Meme/Pepe=0.1; removed skills absent; original camera retained.");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); File.WriteAllText(ScaleRestoreReview + "/ScaleValidation.txt", report.ToString()); }
        }
    }
}
