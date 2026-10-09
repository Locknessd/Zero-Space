using System;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapSetup
    {
        static CombatActionDefinition Definition(CombatTripletData move, Spec spec, Measurement measured)
        {
            var action = ScriptableObject.CreateInstance<CombatActionDefinition>();
            action.name = spec.Key;
            action.actionId = spec.Id;
            action.displayName = "Slap Face — Sequence " + spec.sequence;
            action.presentationProfile = measured.profile;
            action.gameplayTrigger = "Explicit light action via EnqueueCombatAction(side, actionId), exact server " +
                "animationId and existing animation browser. requiresExplicitSelection keeps it out of random pools. " +
                "The server accepted exchange remains authority for damage.";
            action.entryAndInterruption = "Living available fighters approach 0.8m spacing; native receiver yaw 180; " +
                "maximum alignment error 0.15m and entry blend 0.12s. Both roles transfer fingers and are unarmed. " +
                "Existing pair cancellation owns both roles, cue cleanup and equipment restoration. " +
                "No invented stun, damage, armor or independent receiver flinch restart.";
            action.recovery = FormattableString.Invariant(
                $"Complete authored source duration: attacker {move.attackAnim.length:R}s, ") +
                FormattableString.Invariant($"receiver {move.hitAnim.length:R}s; ") +
                "paired receiver continuation gives StandingRecoil. Standing recovery is configured for 0.3s " +
                "after source completion. Runtime standing recovery Play Mode validation is pending. " +
                "Lethal visual treatment and KO validation remain pending; this is not a complete lethal adaptation.";
            action.presentation = "Per-attacker explicit timeline shared by sound and VFX; light_swing at measured " +
                "contact minus 0.10s and light_hit at measured right-palm/head skin contact. " +
                "Per-victim Head-local anchor, existing routed light_hit/body layers, hit-stop, flash, impact light " +
                "and camera feedback; no weapon trails. Established light-body presentation is interim; " +
                "slap-specific audio tuning and live gameplay feedback verification remain pending.";
            action.contacts = new[]
            {
                new CombatActionDefinition.Contact
                {
                    strikeId = spec.Id + "/01",
                    attack = move.sourcePair.attack,
                    reaction = move.sourcePair.reaction,
                    seconds = spec.seconds,
                    activeWindowSeconds = new Vector2(spec.seconds - 1f / 240, spec.seconds + 1f / 240),
                    strikingLimb = HumanBodyBones.RightHand,
                    targetRegion = "Head; measured facial skin contact on victim avatar " + measured.victim.name,
                    directionInVictimSpace = measured.direction,
                    reactionPolicy = CombatActionDefinition.ReactionPolicy.AuthoredPairContinuation,
                    result = CombatActionDefinition.Result.StandingRecoil,
                    presentationGroup = "light_hit",
                    nonDamagingInteraction = false,
                    continuation = "Native paired receiver continues on accepted exchange clock without restart. " +
                        "Direction is the measured right-palm source sweep in victim space. Server accepted damage " +
                        "remains authoritative; no extra stun or damage. Surviving standing recovery and lethal " +
                        "visual treatment require separate Play Mode validation."
                }
            };
            return action;
        }
    }
}
