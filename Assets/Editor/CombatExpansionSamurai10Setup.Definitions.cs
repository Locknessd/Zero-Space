using System;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamurai10Setup
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
            action.displayName = "Samurai Execution 10";
            action.presentationProfile = measured.profile;
            action.gameplayTrigger = "Explicit heavy action through EnqueueCombatAction(side, Samurai_Execution10), " +
                "exact server animationId Samurai_Execution10 or the existing animation browser. Explicit selection " +
                "excludes random pools. Existing server outcome alone owns accepted damage; no local damage authority.";
            action.entryAndInterruption = "Native 1.7m spacing, receiver yaw 180, entry blend 0.12s, alignment error " +
                "0.15m; original clips and calibrated drivers; Katana attacker and owned unarmed receiver, receiver " +
                "finger transfer enabled. No trajectory scaling, clamping or depth constraint. Existing cancellation " +
                "restores both roles and owned equipment and cleans feedback. No added armor, stun or reaction restart.";
            action.recovery = "Preserve full 2.666666746s native pair and face-up knockdown after throw and stab. " +
                "Existing supine getup " + GetUpId + ", grounding " + RecoveryPath + ", recovery blend 0.12s. " +
                "Four controller recovery cases must pass entry 0.01m, floor -0.025m, repeat/held 0.00002m gates. " +
                "Actual gameplay surviving/lethal recovery remains pending.";
            action.presentation = "Shared bank/VFX with per-attacker measured anchors: damaging body_fall at " +
                FormattableString.Invariant($"{LandingTime(measured.attackerName):R}s then stab_hit at ") +
                FormattableString.Invariant($"{StabTime(measured.attackerName):R}s. Both finalLanding=false; ") +
                "only body_fall has damageOnLanding=true. Early grab blade overlap is excluded. Existing grapple_release " +
                "at 1.2s supplies throw movement dust/whoosh without a sword arc; thrust_swing at 1.8s fits stab preparation " +
                "(1.75..1.84s). Exact noncontact sound timing is provisional pending gameplay listening. " +
                "Native weapon trails and measured contact feedback require visible gameplay review. " +
                    measured.evidence;
            action.contacts = measured.profile.cues.Where(c => c.hasContactPoint).Select((cue, index) =>
                new CombatActionDefinition.Contact
                {
                    strikeId = Id + (index == 0 ? "/throw_landing" : "/downward_stab"),
                    attack = move.attackAnim,
                    reaction = move.hitAnim,
                    seconds = cue.seconds,
                    activeWindowSeconds = new Vector2(cue.seconds - 1f / 240, cue.seconds + 1f / 240),
                    strikingLimb = HumanBodyBones.RightHand,
                    targetRegion = measured.regions[index],
                    directionInVictimSpace = measured.directions[index],
                    reactionPolicy = CombatActionDefinition.ReactionPolicy.AuthoredPairContinuation,
                    result = CombatActionDefinition.Result.Knockdown,
                    presentationGroup = cue.group,
                    nonDamagingInteraction = false,
                    continuation = "Native receiver continues in knockdown without restart or added stun/armor. " +
                        "Server outcome owns damage. " + (index == 0
                            ? "First cushioned non-leg support, not global minimum; direction from measured support " +
                                "vertex approach velocity in victim space. finalLanding=false preserves later stab VFX."
                            : "Downward blade direction from target-relative velocity; Head is representative, " +
                                "all tied anatomy retained in evidence. Maintain supine continuation through endpoint.")
                }).ToArray();
            return action;
        }

        static void ValidateDefinition(CombatTripletData move, BattleSfxBank bank)
        {
            var definition = move.actionDefinition;
            var errors = definition.Validate(move, bank).ToArray();
            var profile = definition.presentationProfile;
            if (errors.Length != 0 || bank.FindMove(move) != profile)
                throw new InvalidOperationException("Invalid Execution10 definition: " + string.Join("\n", errors));
            var contact = profile.cues.Where(c => c.hasContactPoint).ToArray();
            if (contact.Length != 2 || contact[0].group != "body_fall" || !contact[0].damageOnLanding ||
                contact[1].group != "stab_hit" || contact[1].damageOnLanding || profile.cues.Any(c => c.finalLanding) ||
                !BattleHitDamageSequence.ContactTimes(profile, Duration).SequenceEqual(contact.Select(c => c.seconds)) ||
                definition.contacts.Any(c => c.result != CombatActionDefinition.Result.Knockdown ||
                    c.nonDamagingInteraction || c.directionInVictimSpace.sqrMagnitude < .99f))
                throw new InvalidOperationException("Execution10 requires precisely throw and stab damaging contacts.");
        }
    }
}
