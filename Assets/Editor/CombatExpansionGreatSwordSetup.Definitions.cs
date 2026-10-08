using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordSetup
    {
        static CombatActionDefinition Definition(CombatTripletData move, Spec spec, BattleSfxBank.Move profile)
        {
            var action = ScriptableObject.CreateInstance<CombatActionDefinition>();
            action.actionId = move.moveName;
            action.displayName = spec.displayName;
            if (spec.index == 0)
                action.authoredAttackSources = new[]
                {
                    NativeClip("610fba6d346a64f4583237c3ef4f7e46"),
                    NativeClip("12987da400bbe864aae42fdf303bc647"),
                    NativeClip("ed2751f54f474b74397a144408a8fe03")
                };
            action.gameplayTrigger = "Heavy exchange queue, exact server animationId, " +
                "EnqueueCombatAction(side, ID), and the existing animation browser.";
            action.entryAndInterruption = "Living available participants approach the authored range in the " +
                "existing combat lane; bounded 0.15m alignment and 0.12s entry blend. " +
                "Source-owned GreatSword attacker equipment and unarmed victim state. " +
                "Cancel/reset/disable cleanly releases both roles, cues, trails, camera and equipment ownership. " +
                "No instantkill, extra armor, or invented local damage; server health is unchanged in local preview.";
            action.recovery = "The native authored receiver track continues through knockdown. " +
                "A surviving victim uses the grounded existing getup recovery and its automatic recovery sound; " +
                "an accepted lethal victim holds the authored final pose. Attacker completes its source clip.";
            if (spec.index == 0)
                action.recovery += " The authored attacker variant preserves the original Ambush then appends " +
                    "native backward-step start and stop motion before the surviving victim gets up.";
            action.presentation = "Exact source-second shared sound and VFX cues with measured per-victim-avatar " +
                "Weapon anchors; established GreatSword weapon variant and trail routing, routed audio, hitstop, " +
                "impact light, flash, cinematic camera impulse and final landing accent. " +
                "Existing cancellation and recovery clean up transient feedback. " +
                "Landing is non-damaging; only accepted server damage is distributed over weapon contacts.";
            var contacts = profile.cues.Where(c => c.hasContactPoint).ToArray();
            action.contacts = contacts.Select((cue, i) => new CombatActionDefinition.Contact
            {
                strikeId = move.moveName + "/" + (i + 1).ToString("D2"),
                attack = move.attackAnim,
                reaction = move.hitAnim,
                seconds = cue.seconds,
                activeWindowSeconds = new Vector2(cue.seconds - 1f / 60, cue.seconds + 1f / 60),
                strikingLimb = HumanBodyBones.RightHand,
                targetRegion = spec.regions[i] + "; measured avatar anchors: " +
                    string.Join(", ", cue.avatarContacts.Select(a => a.avatar.name + "/" + a.bone)),
                directionInVictimSpace = spec.directions[i],
                reactionPolicy = CombatActionDefinition.ReactionPolicy.AuthoredPairContinuation,
                result = spec.index == 3 && i < contacts.Length - 1 ?
                    i == 0 ? CombatActionDefinition.Result.StandingRecoil : CombatActionDefinition.Result.Stagger :
                    CombatActionDefinition.Result.Knockdown,
                presentationGroup = cue.group,
                nonDamagingInteraction = false,
                continuation = "Native authored paired receiver continuation on the accepted exchange clock; " +
                    "contact does not restart the reaction. Early Ex3 cuts remain standing/staggered until the " +
                    "final knockdown. Single-hit actions continue through their native takedown and landing. " +
                    "No independent reaction, extra armor, or unconditional kill is introduced."
            }).Append(new CombatActionDefinition.Contact
            {
                strikeId = move.moveName + "/" + (contacts.Length + 1).ToString("D2") + "-landing",
                attack = move.attackAnim,
                reaction = move.hitAnim,
                seconds = spec.landingSeconds,
                activeWindowSeconds = new Vector2(spec.landingSeconds - 1f / 60, spec.landingSeconds + 1f / 60),
                strikingLimb = HumanBodyBones.Hips,
                targetRegion = "Victim body contacting the ground in the native receiver continuation",
                directionInVictimSpace = Vector3.down,
                reactionPolicy = CombatActionDefinition.ReactionPolicy.AuthoredPairContinuation,
                result = CombatActionDefinition.Result.Knockdown,
                presentationGroup = "body_fall",
                nonDamagingInteraction = true,
                continuation = "Final non-damaging ground contact accents the existing receiver continuation; " +
                    "survivors recover through getup and accepted lethal victims retain their final pose."
            }).ToArray();
            return action;
        }

        static AnimationClip NativeClip(string guid)
        {
            return AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid))
                .OfType<AnimationClip>().Single(clip => AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip,
                    out string sourceGuid, out long localId) && sourceGuid == guid && localId == 7400000);
        }

        static CombatActionDefinition SaveDefinition(CombatActionDefinition prepared)
        {
            string path = Folder + prepared.actionId + ".asset";
            var saved = AssetDatabase.LoadAssetAtPath<CombatActionDefinition>(path);
            if (!saved)
            {
                AssetDatabase.CreateAsset(prepared, path);
                saved = prepared;
            }
            else
            {
                Undo.RecordObject(saved, "Configure measured GreatSword action");
                EditorUtility.CopySerialized(prepared, saved);
                EditorUtility.SetDirty(saved);
            }
            AssetDatabase.SaveAssetIfDirty(saved);
            return saved;
        }
    }
}
