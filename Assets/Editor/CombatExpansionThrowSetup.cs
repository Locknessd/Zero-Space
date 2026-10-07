using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    public static class CombatExpansionThrowSetup
    {
        public sealed class Spec
        {
            public int index;
            public string name, region;
            public float grip, swing;
            public int[] landingFrames;
            public Spec(int index, string name, string region, float grip, float swing, params int[] frames)
            {
                this.index = index;
                this.name = name;
                this.region = region;
                this.grip = grip;
                this.swing = swing;
                landingFrames = frames;
            }
        }

        public static readonly Spec[] Specs =
        {
            new Spec(1, "Shoulder throw", "Shoulder roll into back landing", .15f, .65f, 57),
            new Spec(2, "Brainbuster", "Inverted lift into upper-back landing", .15f, .65f, 78),
            new Spec(6, "Fisherman suplex", "Captured leg and waist; back landing", .15f, .55f, 58),
            new Spec(10, "Jumping powerbomb", "Overhead waist lift; shoulder landing", .15f, .5f, 66),
            new Spec(11, "Dragon screw", "Captured raised leg; rotating side landing", .1f, .2f, 26),
            new Spec(13, "Neck breaker", "Head-and-shoulder control; back landing", .3f, .5f, 47),
            new Spec(14, "Four-way throw", "Arm turn and shoulder roll; back landing", .15f, .65f, 56),
            new Spec(16, "German suplex", "Rear waist lift; shoulder landing", .15f, .6f, 54),
            new Spec(17, "Giant swing", "Ankle capture; takedown then thrown shoulder landing", .15f, .25f, 21, 96)
        };
        const string Folder = "Assets/CombatExpansion/Actions/";

        public static CombatTripletData MakeMove(CharacterCombat source, CharacterCombat target, int index)
        {
            var move = CombatExpansionVol10Study.MakeMove(source, target, index);
            var getUp = source.lightCombatMoves.First(m => m.sourcePair?.getUp).sourcePair.getUp;
            move.getUpAnim = getUp;
            move.sourcePair.getUp = getUp;
            move.sourcePair.attackerGetUp = index == 2 ? getUp : null;
            move.sourcePair.entryBlendSeconds = .12f;
            move.sourcePair.constrainDepthAfterSpacing = true;
            move.sourcePair.maximumAlignmentError = .15f;
            move.grounding = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(Folder + move.moveName + "_Grounding.asset");
            return move;
        }

        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before installing throws.");
            var scene = SceneManager.GetSceneByPath(CombatExpansionInventory.Battle);
            if (!scene.IsValid() || !scene.isLoaded || scene.isDirty)
                throw new InvalidOperationException("Open the clean saved BattleScene first.");
            CombatExpansionThrowGrounding.Bake();
            var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
            var bank = game.battleSfx.bank;
            var profiles = bank.moves.ToList();
            foreach (var spec in Specs)
            {
                var move = MakeMove(game.leftCombat, game.rightCombat, spec.index);
                var cues = new List<BattleSfxBank.Cue>
                {
                    new BattleSfxBank.Cue { seconds = spec.grip, group = "grapple_grip" },
                    new BattleSfxBank.Cue { seconds = spec.swing, group = "heavy_swing" }
                };
                for (int i = 0; i < spec.landingFrames.Length; i++)
                    cues.Add(new BattleSfxBank.Cue
                    {
                        seconds = spec.landingFrames[i] / 60f,
                        group = "body_fall",
                        damageOnLanding = true,
                        finalLanding = i == spec.landingFrames.Length - 1
                    });
                if (spec.index == 17)
                    cues.Add(new BattleSfxBank.Cue { seconds = 1.15f, group = "heavy_swing" });
                profiles.RemoveAll(p => p.attack == move.attackAnim && p.reaction == move.hitAnim);
                profiles.Add(new BattleSfxBank.Move
                {
                    label = move.moveName,
                    attack = move.attackAnim,
                    reaction = move.hitAnim,
                    cues = cues.OrderBy(c => c.seconds).ToArray()
                });
            }
            Undo.RecordObject(bank, "Configure measured throw presentation");
            bank.moves = profiles.ToArray();
            foreach (var source in new[] { game.leftCombat, game.rightCombat })
            {
                var target = source == game.leftCombat ? game.rightCombat : game.leftCombat;
                Undo.RecordObject(source, "Register remaining Vol10 throws");
                var moves = source.heavyCombatMoves.ToList();
                foreach (var spec in Specs)
                {
                    var move = MakeMove(source, target, spec.index);
                    move.actionDefinition = Definition(move, spec, bank.FindMove(move));
                    var errors = move.actionDefinition.Validate(move, bank).ToArray();
                    if (errors.Length > 0)
                        throw new InvalidOperationException(string.Join("\n", errors));
                    moves.RemoveAll(m => m.moveName == move.moveName);
                    moves.Add(move);
                }
                source.heavyCombatMoves = moves.ToArray();
                EditorUtility.SetDirty(source);
            }
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssetIfDirty(bank);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            CombatExpansionInventory.Audit();
        }

        static CombatActionDefinition Definition(CombatTripletData move, Spec spec, BattleSfxBank.Move profile)
        {
            string path = Folder + move.moveName + ".asset";
            var action = AssetDatabase.LoadAssetAtPath<CombatActionDefinition>(path);
            if (!action)
            {
                action = ScriptableObject.CreateInstance<CombatActionDefinition>();
                AssetDatabase.CreateAsset(action, path);
            }
            action.actionId = move.moveName;
            action.displayName = spec.name;
            action.gameplayTrigger = "Heavy exchange selection, exact server animationId, " +
                "EnqueueCombatAction(side, ID), and the existing animation browser.";
            action.entryAndInterruption = "Living available participants, normal lane approach to authored range; " +
                "maximum 0.15m alignment error; authored receiver orientation and 0.12s entry blend. " +
                "Unarmed source actors and owned equipment state. Cancel/reset/disable/death releases both roles.";
            action.recovery = spec.index == 2 ? "Both living actors use existing GetUp with shared ownership; " +
                "accepted lethal victim retains its authored pose while attacker recovers." :
                "Attacker stands through its authored clip; surviving victim uses existing GetUp. " +
                "Accepted lethal victim retains its authored final pose.";
            action.presentation = "Grip cloth/dust, heavy movement whoosh, grounded body impact, shared hit-stop, " +
                "flash, impact light and camera impulse. Final landing accent; no unarmed weapon trail. " +
                "Health distributes only accepted server totals; local previews do not invent damage.";
            action.contacts = profile.cues.Where(c => c.damageOnLanding || c.group == "grapple_grip")
                .Select((c, i) => new CombatActionDefinition.Contact
                {
                    strikeId = move.moveName + "/" + i,
                    attack = move.attackAnim,
                    reaction = move.hitAnim,
                    seconds = c.seconds,
                    activeWindowSeconds = new Vector2(c.seconds - 1f / 60, c.seconds + 1f / 60),
                    strikingLimb = c.damageOnLanding ? HumanBodyBones.Hips : HumanBodyBones.LeftHand,
                    targetRegion = spec.region,
                    directionInVictimSpace = c.damageOnLanding ? Vector3.down : Vector3.forward,
                    reactionPolicy = CombatActionDefinition.ReactionPolicy.AuthoredPairContinuation,
                    result = c.damageOnLanding ? CombatActionDefinition.Result.Knockdown : CombatActionDefinition.Result.Hold,
                    presentationGroup = c.group,
                    nonDamagingInteraction = !c.damageOnLanding,
                    continuation = "Authored paired receiver track continues on the existing accepted exchange clock. " +
                        "Capture is non-damaging; impact damage occurs only at the listed ground contacts. " +
                        "No reaction restarts, extra armor, unconditional kills, or separate cosmetic damage."
                }).ToArray();
            EditorUtility.SetDirty(action);
            AssetDatabase.SaveAssetIfDirty(action);
            return action;
        }
    }
}
