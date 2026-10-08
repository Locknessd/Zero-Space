using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordPlayCheck
    {
        static readonly HashSet<BattleSfxBank.Cue> observedCues = new HashSet<BattleSfxBank.Cue>();
        static BattleSfxBank.Cue[] expectedCues;
        static bool sawAudio, sawEffects, sawHold, sawLights, sawShake, sawFlash, sawTrail, sawSourceSword;
        static readonly MaterialPropertyBlock surfaceBlock = new MaterialPropertyBlock();
        static readonly int HitFlashId = Shader.PropertyToID("_HitFlash");
        static bool expectSurface, sawSurface;

        static bool Primary(BattleSfxBank.Cue cue) => cue.group == "light_hit" ||
            cue.group == "heavy_hit" || cue.group == "stab_hit";

        static void ValidateProfile()
        {
            Require(game.battleSfx.bank && game.battleVfx.timeline == game.battleSfx.bank,
                "Audio and VFX must use the actual shared bank.");
            var profile = game.battleSfx.bank.FindMove(move);
            Require(profile != null && profile.cues != null && profile.cues.Length > 0,
                "Registered action has no authored presentation profile.");
            float previous = -1;
            foreach (var cue in profile.cues)
            {
                Require(cue != null && float.IsFinite(cue.seconds) && cue.seconds >= previous &&
                    cue.seconds >= 0 && cue.seconds <= move.sourcePair.attack.length,
                    "Presentation cue schedule is invalid or unsorted.");
                previous = cue.seconds;
            }
            expectedCues = profile.cues.Where(cue => Primary(cue) || cue.group == "body_fall" ||
                cue.group == "knockout_fall").ToArray();
            Require(expectedCues.Length > 0, "Legacy or new action must retain authored contact feedback.");
            if (GreatSword)
            {
                Require(move.weapon == TrumpWeaponManager.WeaponType.GreatSword && move.sourcePair.getUp &&
                    !move.sourcePair.attackerGetUp, "GreatSword requires its weapon and receiver-only recovery.");
                Require(move.actionDefinition && move.actionDefinition.actionId == action,
                    "Registered GreatSword action has no matching action definition.");
                var errors = move.actionDefinition.Validate(move, game.battleSfx.bank).ToArray();
                Require(errors.Length == 0, "Invalid action definition: " + string.Join("; ", errors));
                Require(move.sourcePair.showWeapon && move.sourcePair.attackerWeaponPrefab &&
                    !string.IsNullOrEmpty(move.sourcePair.attackerWeaponSocket) && move.grounding &&
                    move.sourcePair.recoveryGrounding && move.sourcePair.recoveryBlendSeconds > 0,
                    "GreatSword definition lacks source sword attachment, grounding or receiver recovery blend.");
                int primaryCount = action == Actions[4] ? 3 : 1;
                var hits = expectedCues.Where(Primary).ToArray();
                var landings = expectedCues.Where(cue => cue.group == "body_fall").ToArray();
                Require(hits.Length == primaryCount && expectedCues.Length == primaryCount + 1 &&
                    landings.Length == 1 && landings[0].finalLanding && !landings[0].damageOnLanding &&
                    landings[0].seconds > hits.Last().seconds, "Wrong primary contacts or final nondamaging landing.");
                foreach (var cue in hits)
                {
                    Require(cue.contactSource == "Weapon" && cue.hasContactPoint && !cue.damageOnLanding,
                        "GreatSword strike must use a Weapon contact anchor.");
                    foreach (var fighter in fighters)
                    {
                        var anchors = cue.avatarContacts.Where(entry => entry != null &&
                            entry.avatar == fighter.Animator.avatar).ToArray();
                        Require(anchors.Length == 1 && fighter.Animator.GetBoneTransform(anchors[0].bone) &&
                            Finite(anchors[0].offset), "Missing unique finite per-avatar contact anchor.");
                    }
                }
            }
            expectSurface = target.GetComponentsInChildren<Renderer>(true).Any(renderer =>
                renderer.sharedMaterials.Any(material => material && material.HasProperty(HitFlashId))) &&
                feedback.surfaceFlashStrength > 0 && feedback.flashScale > 0;
            sawSurface = false;
        }

        static bool Finite(Vector3 point) => float.IsFinite(point.x) && float.IsFinite(point.y) &&
            float.IsFinite(point.z);

        static void Contact(BattleVfxPlayer.Impact impact)
        {
            if (!running)
                return;
            try
            {
                Require(activeCase && !Guard && !Interrupt, "Rejected or interrupted action emitted a late contact.");
                contacts++;
                Require(impact.playback == pair && impact.move == move &&
                    impact.attacker == source && impact.receiver == target, "Wrong contact actor ownership.");
                Require(impact.eventId != 0 && contactIds.Add(impact.eventId) && observedCues.Add(impact.cue),
                    "Duplicate contact identity or authored cue.");
                Require(contacts <= expectedCues.Length && impact.cue == expectedCues[contacts - 1] &&
                    Mathf.Abs(impact.seconds - impact.cue.seconds) < .0001f &&
                    Mathf.Abs(impact.seconds - pair.SampleTime) < .0001f,
                    "Contact order or sample time differs from the authored cue schedule.");
                Require(Finite(impact.position), "Nonfinite contact placement.");
                if (Primary(impact.cue) && impact.cue.hasContactPoint)
                {
                    Require(impact.cue.TryContactPosition(target.Animator, out var anchor) &&
                        Vector3.Distance(impact.position, anchor) <= .02f,
                        "Live contact is detached from its authored animated receiver anchor.");
                }
                else if (impact.kind == BattleVfxPlayer.ContactKind.Ground)
                {
                    var hips = target.Animator.GetBoneTransform(HumanBodyBones.Hips).position;
                    hips.y = game.battleVfx.groundHeight + .035f;
                    Require(Vector3.Distance(impact.position, hips) < .001f, "Landing is detached from receiver hips.");
                }
                if (GreatSword)
                {
                    Require(Primary(impact.cue) ? impact.kind != BattleVfxPlayer.ContactKind.Ground :
                        impact.kind == BattleVfxPlayer.ContactKind.Ground && !impact.cue.damageOnLanding,
                        "GreatSword published an unsupported contact kind.");
                    bool finalStrike = impact.cue == expectedCues.Where(Primary).Last();
                    Require(impact.finishing == (Lethal && finalStrike), "Incorrect accepted-lethal finishing contact.");
                }
                ObservePresentation();
            }
            catch (Exception error)
            {
                failure = error.Message;
            }
        }

        static void EffectPlayed(string cue, GameObject effect)
        {
            if (!running)
                return;
            if (!activeCase || Guard || interrupted)
                failure = "Rejected or interrupted exchange emitted a new effect.";
            if (effect && effect.activeInHierarchy)
            {
                sawEffects = true;
                if (game.battleVfx.weaponTrails.Owns(effect))
                    sawTrail = true;
            }
        }

        static void AudioPlayed(string cue, AudioClip clip)
        {
            if (running && (!activeCase || Guard || interrupted))
                failure = "Rejected or interrupted action emitted late audio: " + cue;
        }

        static void UnexpectedLog(string message, string stack, LogType type)
        {
            if (running && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
                failure = "Unexpected runtime error: " + message;
        }

        static void ObservePresentation()
        {
            sawAudio |= game.battleSfx.ActiveVoiceCount > 0 && game.battleSfx.PlayedCueCount > audioBefore;
            sawEffects |= game.battleVfx.ActiveEffectCount > 0 && game.battleVfx.PlayedEffectCount > effectsBefore;
            sawHold |= feedback.IsHolding && feedback.HitStopCount > holdsBefore;
            sawLights |= lighting.ActiveFlashCount > 0 && lighting.PlayedFlashCount > lightsBefore;
            sawShake |= shake.IsShaking && shake.ShakeCount > shakesBefore;
            sawFlash |= feedback.flash.isActiveAndEnabled && feedback.flash.Progress < 1 && feedback.flash.color.a > 0;
            sawTrail |= game.battleVfx.weaponTrails.ActiveTrailCount > 0 &&
                game.battleVfx.weaponTrails.SampledPoseCount > trailsBefore;
            if (expectSurface)
                foreach (var renderer in target.GetComponentsInChildren<Renderer>(false))
                {
                    renderer.GetPropertyBlock(surfaceBlock);
                    sawSurface |= surfaceBlock.GetFloat(HitFlashId) > 0;
                }
        }
    }
}
