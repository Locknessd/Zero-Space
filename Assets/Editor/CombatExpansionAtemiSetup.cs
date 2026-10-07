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
    public static class CombatExpansionAtemiSetup
    {
        const string AssetFolder = "Assets/CombatExpansion/Actions";
        static readonly float[] Contacts = { .5f, 31f / 60, .4f };
        static readonly string[] Regions = { "Lower torso", "Upper body interception", "Shoulder and upper torso" };

        public static void ReloadVerifiedSavedBattle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before reloading BattleScene.");
            var scene = SceneManager.GetSceneByPath(CombatExpansionInventory.Battle);
            const string copy = "Temp/CombatExpansionBattleSnapshot.unity";
            if (!EditorSceneManager.SaveScene(scene, copy, true) ||
                File.ReadAllText(copy) != File.ReadAllText(CombatExpansionInventory.Battle))
                throw new InvalidOperationException("BattleScene has unsaved changes; snapshot retained at " + copy);
            EditorSceneManager.SaveScene(scene);
            EditorSceneManager.CloseScene(scene, true);
            scene = EditorSceneManager.OpenScene(CombatExpansionInventory.Battle, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
        }

        [MenuItem("Tools/Battle/Combat Expansion/Install measured Atemi pairs")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before installing actions.");
            var battle = SceneManager.GetSceneByPath(CombatExpansionInventory.Battle);
            if (!battle.IsValid() || !battle.isLoaded || battle.isDirty)
                throw new InvalidOperationException("Open the saved clean BattleScene before installing actions.");
            Directory.CreateDirectory(AssetFolder);
            AssetDatabase.Refresh();
            var game = battle.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
            var bank = game.battleSfx.bank;
            var profiles = MeasureProfiles();
            Undo.RecordObject(bank, "Configure Atemi contact presentations");
            var allProfiles = bank.moves.ToList();
            foreach (var profile in profiles)
            {
                allProfiles.RemoveAll(p => p.attack == profile.attack && p.reaction == profile.reaction);
                allProfiles.Add(profile);
            }
            bank.moves = allProfiles.ToArray();
            var fighters = new[] { game.leftCombat, game.rightCombat };
            foreach (var source in fighters)
            {
                var target = fighters.Single(f => f != source);
                Undo.RecordObject(source, "Register Atemi pairs in gameplay");
                var moves = source.lightCombatMoves.ToList();
                for (int index = 3; index <= 5; index++)
                {
                    var move = CombatExpansionVol10Study.MakeMove(source, target, index);
                    if (index == 3)
                    {
                        move.getUpAnim = source.lightCombatMoves.First(m => m.sourcePair.getUp).sourcePair.getUp;
                        move.sourcePair.getUp = move.getUpAnim;
                    }
                    move.actionDefinition = Definition(move, index - 3);
                    var errors = move.actionDefinition.Validate(move, bank).ToArray();
                    if (errors.Length > 0)
                        throw new InvalidOperationException(string.Join("\n", errors));
                    moves.RemoveAll(m => m.moveName == move.moveName);
                    moves.Add(move);
                }
                source.lightCombatMoves = moves.ToArray();
                EditorUtility.SetDirty(source);
            }
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssetIfDirty(bank);
            EditorSceneManager.MarkSceneDirty(battle);
            EditorSceneManager.SaveScene(battle);
            CombatExpansionInventory.Audit();
        }

        static BattleSfxBank.Move[] MeasureProfiles()
        {
            var scene = EditorSceneManager.OpenPreviewScene(CombatExpansionInventory.Battle);
            var profiles = new BattleSfxBank.Move[3];
            var report = new StringBuilder("fighter,action,seconds,source,anchor,gapMetres,avatar\n");
            try
            {
                var game = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var fighter in fighters)
                {
                    fighter.battleSfx = null;
                    fighter.battleVfx = null;
                    fighter.hitEffect = null;
                }
                foreach (var source in fighters)
                for (int index = 3; index <= 5; index++)
                {
                    var target = fighters.Single(f => f != source);
                    foreach (var fighter in fighters)
                        fighter.ResetCombat();
                    var move = CombatExpansionVol10Study.MakeMove(source, target, index);
                    source.Animator.transform.position = Vector3.zero;
                    target.Animator.transform.position = Vector3.right * move.attackRange;
                    if (!source.ExecuteAttack(move, target))
                        throw new InvalidOperationException("Cannot calibrate " + move.moveName);
                    var pair = source.SourcePlayback;
                    try
                    {
                        float seconds = Contacts[index - 3];
                        pair.EvaluateAt(seconds);
                        if (!BattlePresentationContactSetup.TryMeasureContact(pair, source, target, "LeftHand",
                            out var bone, out var offset, out float gap, out _))
                            throw new InvalidOperationException(move.moveName + " misses its victim by " + gap + "m.");
                        var profile = profiles[index - 3];
                        if (profile == null)
                        {
                            var cues = new List<BattleSfxBank.Cue>
                            {
                                new BattleSfxBank.Cue { seconds = seconds - .1f, group = "light_swing" },
                                new BattleSfxBank.Cue
                                {
                                    seconds = seconds, group = "light_hit", hasContactPoint = true,
                                    contactSource = "LeftHand", contactBone = bone, contactOffset = offset
                                }
                            };
                            if (index == 3)
                                cues.Add(new BattleSfxBank.Cue
                                {
                                    seconds = 1.2f, group = "body_fall", finalLanding = true
                                });
                            profile = new BattleSfxBank.Move
                            {
                                label = move.moveName,
                                attack = move.sourcePair.attack,
                                reaction = move.sourcePair.reaction,
                                cues = cues.ToArray()
                            };
                            profiles[index - 3] = profile;
                        }
                        var cue = profile.cues.Single(c => c.group == "light_hit");
                        var anchors = cue.avatarContacts.ToList();
                        anchors.Add(new BattleSfxBank.ContactAnchor
                        {
                            avatar = target.Animator.avatar,
                            bone = bone,
                            offset = offset
                        });
                        cue.avatarContacts = anchors.ToArray();
                        report.Append(FormattableString.Invariant(
                            $"{source.name},{move.moveName},{seconds:R},LeftHand,{bone},{gap:R},"));
                        report.AppendLine(CombatExpansionInventory.Identity(target.Animator.avatar));
                    }
                    finally
                    {
                        pair.Cancel();
                    }
                }
            }
            finally
            {
                File.WriteAllText(CombatExpansionInventory.Output + "/AtemiContactCalibration.csv", report.ToString());
                EditorSceneManager.ClosePreviewScene(scene);
            }
            return profiles;
        }

        static CombatActionDefinition Definition(CombatTripletData move, int index)
        {
            string path = AssetFolder + "/" + move.moveName + ".asset";
            var result = AssetDatabase.LoadAssetAtPath<CombatActionDefinition>(path);
            if (!result)
            {
                result = ScriptableObject.CreateInstance<CombatActionDefinition>();
                AssetDatabase.CreateAsset(result, path);
            }
            result.actionId = move.moveName;
            result.displayName = "Atemi " + (index + 1);
            result.gameplayTrigger = "Resolved light exchange pool; exact animationId; EnqueueCombatAction(side, ID).";
            result.entryAndInterruption = "Living idle participants in the existing grounded lane; unarmed paired " +
                "drivers; normal 0.22 second approach. Cancel/reset/disable releases both source actors.";
            result.recovery = index == 0 ? "Authored knockdown followed by the existing GetUp state for survivors." :
                "Authored stagger and separation, then the existing idle and facing restoration.";
            result.presentation = "Shared light_swing and light_hit; per-avatar surface anchor; existing body audio " +
                "layer, light hit-stop, flash, impact light and camera impulse. No weapon trail or cinematic override. " +
                "Health follows the accepted server total; local queue playback does not invent server damage.";
            result.contacts = new[]
            {
                new CombatActionDefinition.Contact
                {
                    strikeId = move.moveName + "/1",
                    attack = move.sourcePair.attack,
                    reaction = move.sourcePair.reaction,
                    seconds = Contacts[index],
                    activeWindowSeconds = new Vector2(Contacts[index] - 1f / 60, Contacts[index] + 1f / 60),
                    strikingLimb = HumanBodyBones.LeftHand,
                    targetRegion = Regions[index],
                    directionInVictimSpace = index == 2 ? Vector3.left : Vector3.back,
                    reactionPolicy = CombatActionDefinition.ReactionPolicy.AuthoredPairContinuation,
                    result = index == 0 ? CombatActionDefinition.Result.Knockdown : CombatActionDefinition.Result.Stagger,
                    presentationGroup = "light_hit",
                    continuation = "The authored receiver clip shares the pair clock from entry through recoil. " +
                        "Do not restart it at contact. Interruptions cancel the clock and pending cues."
                }
            };
            EditorUtility.SetDirty(result);
            AssetDatabase.SaveAssetIfDirty(result);
            return result;
        }
    }
}
