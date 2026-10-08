using System;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiSetup
    {
        internal sealed class Measurement
        {
            internal string attackerName;
            internal Avatar attacker;
            internal Avatar victim;
            internal BattleSfxBank.Move profile;
            internal Vector3[] directions;
            internal string[] regions;
            internal string evidence;
        }

        static CombatActionDefinition Definition(CombatTripletData move, Measurement measured)
        {
            var action = ScriptableObject.CreateInstance<CombatActionDefinition>();
            action.name = Id + "_" + measured.attackerName;
            action.actionId = Id;
            action.displayName = "Samurai Execution 01";
            action.presentationProfile = measured.profile;
            action.gameplayTrigger = "Explicit heavy action via EnqueueCombatAction(side, Samurai_Execution01), " +
                "exact server animationId Samurai_Execution01 or the existing animation browser. " +
                "requiresExplicitSelection excludes random pools. Normal server outcome rules own accepted damage; " +
                "no extra local damage, armor or instant kill.";
            action.entryAndInterruption = FormattableString.Invariant($"Approach measured {move.attackRange:R}m; ") +
                "native PlayerA armed Katana, renderer/collider-free unarmed PlayerB, native receiver yaw 180, " +
                "entry blend 0.12s, alignment error 0.15m, receiver finger transfer enabled. Unscaled source motion, " +
                "no additional depth constraint. Existing owned equipment suppression and pair cancellation restore " +
                "both roles and clean up feedback. Intermediate stagger continues the native receiver timeline.";
            action.recovery = "Preserve complete native 3.1666667s source pair and authored terminal knockdown. " +
                "Final geometric torso front normal dot up is approximately -0.976 (prone). Existing ProneRecovery " +
                "157da7dff6b3b3148a29246060747ce5:1827226128182048838 uses measured recovery grounding with 0.12s " +
                "blend. Actual getup transition and lethal/nonlethal paths require Play Mode validation.";
            action.presentation = "Explicit per-attacker timeline, existing shared sound/VFX feedback, measured " +
                "stab_hit then heavy_hit, native weapon trails and contact feedback. Restrained existing thrust_swing " +
                "at 0.27s and blade_swing at 1.9s are provisional pending gameplay listening. Non-damaging final " +
                "body_fall is anchored to the measured supporting skin vertex, including 0.01m grounding cushion. " +
                "Active strike windows are measured time +/-1/240s. " + measured.evidence;
            var cues = measured.profile.cues.Where(c => c.hasContactPoint).ToArray();
            action.contacts = cues.Select((cue, index) => new CombatActionDefinition.Contact
            {
                strikeId = Id + "/" + (index + 1).ToString("D2"),
                attack = move.attackAnim,
                reaction = move.hitAnim,
                seconds = cue.seconds,
                activeWindowSeconds = new Vector2(cue.seconds - 1f / 240, cue.seconds + 1f / 240),
                strikingLimb = index < 2 ? HumanBodyBones.RightHand : cue.contactBone,
                targetRegion = measured.regions[index],
                directionInVictimSpace = measured.directions[index],
                reactionPolicy = CombatActionDefinition.ReactionPolicy.AuthoredPairContinuation,
                result = index == 0 ? CombatActionDefinition.Result.Stagger : CombatActionDefinition.Result.Knockdown,
                presentationGroup = cue.group,
                nonDamagingInteraction = index == 2,
                continuation = index < 2
                    ? "Native paired receiver continues without flinch restart. Direction is normalized corrected " +
                        "relativeBladeVelocityVictimMps, not raw blade velocity. Server outcome alone owns damage. " +
                        "Unsigned triangle minimum preserves anatomical ties described above; no added stun or armor."
                    : "Final non-damaging ground support; authored prone continuation and existing grounded getup. " +
                        "damageOnLanding false, finalLanding true; no third damage tick or independent reaction."
            }).ToArray();
            return action;
        }
    }
}
