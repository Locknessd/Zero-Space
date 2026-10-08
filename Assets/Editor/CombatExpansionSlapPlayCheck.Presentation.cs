using System;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapPlayCheck
    {
        static BattleSfxBank.Cue expectedCue;
        static bool sawAudio, sawEffects, sawHold, sawLights, sawShake, sawFlash, sawSurface, expectSurface;
        static readonly MaterialPropertyBlock surfaceBlock = new MaterialPropertyBlock();
        static readonly int HitFlashId = Shader.PropertyToID("_HitFlash");

        static void ValidateProfile()
        {
            Require(game.battleSfx.bank && game.battleVfx.timeline == game.battleSfx.bank &&
                feedback.vfx == game.battleVfx && lighting.vfx == game.battleVfx,
                "SFX, VFX, impact feedback and lighting must share the existing battle event source.");
            Require(move.actionDefinition && move.actionDefinition.actionId == action &&
                move.actionDefinition.presentationProfile != null &&
                game.battleSfx.bank.FindMove(move) == move.actionDefinition.presentationProfile,
                "Registered Slap does not select its explicit saved per-attacker presentation profile.");
            var errors = move.actionDefinition.Validate(move, game.battleSfx.bank).ToArray();
            Require(errors.Length == 0, "Invalid action definition: " + string.Join("; ", errors));
            var profile = move.actionDefinition.presentationProfile;
            var hits = profile.cues.Where(c => c.group == "light_hit" || c.group == "heavy_hit" ||
                c.group == "stab_hit" || c.group == "body_fall" || c.group == "knockout_fall").ToArray();
            Require(hits.Length == 1 && hits[0].group == "light_hit", "Expected one authored light slap contact.");
            expectedCue = hits[0];
            Require(expectedCue.seconds > 0 && expectedCue.seconds < move.sourcePair.attack.length &&
                expectedCue.hasContactPoint && !expectedCue.damageOnLanding,
                "Slap contact lacks a valid timed receiver anchor.");
            Require(move.weapon == TrumpWeaponManager.WeaponType.None && !move.sourcePair.showWeapon &&
                !move.sourcePair.getUp && !move.sourcePair.attackerGetUp && move.grounding &&
                Mathf.Abs(move.sourcePair.standingRecoverySeconds - .3f) < .00001f,
                "Slap needs native unarmed source, grounding and .3s standing-only recovery.");
            expectSurface = target.GetComponentsInChildren<Renderer>(true).Any(renderer =>
                renderer.sharedMaterials.Any(material => material && material.HasProperty(HitFlashId))) &&
                feedback.surfaceFlashStrength > 0 && feedback.flashScale > 0;
        }

        static void Contact(BattleVfxPlayer.Impact impact)
        {
            if (!running)
                return;
            try
            {
                Require(activeCase && !Guard && !cancelled, "Rejected/cancelled play emitted a late contact.");
                contacts++;
                Require(contacts == 1 && impact.playback == pair && impact.move == move &&
                    impact.attacker == source && impact.receiver == target && impact.cue == expectedCue &&
                    impact.eventId != 0 && contactIds.Add(impact.eventId), "Duplicate or incorrectly owned contact.");
                Require(Mathf.Abs(impact.seconds - expectedCue.seconds) < .0001f &&
                    Mathf.Abs(impact.seconds - pair.SampleTime) < .0001f,
                    "Actual animation contact clock differs from per-action authored cue.");
                Require(!impact.finishing && impact.kind == BattleVfxPlayer.ContactKind.Light,
                    "Slap used heavy, ground or finishing feedback.");
                Require(expectedCue.TryContactPosition(target.Animator, out var anchor),
                    "Impact lost its accepted receiver anchor.");
                var camera = Camera.main;
                Require(camera && camera.gameObject.scene == game.gameObject.scene,
                    "The isolated battle scene lost its gameplay camera.");
                // Authored contact effects use the existing 15 mm camera-facing depth offset.
                Vector3 expectedImpact = anchor - camera.transform.forward * .015f;
                Require(Vector3.Distance(impact.position, expectedImpact) <= .002f,
                    $"Impact missed its camera-offset receiver anchor: actual={impact.position:F6}, " +
                    $"expected={expectedImpact:F6}, anchor={anchor:F6}.");
                var hand = source.Animator.GetBoneTransform(HumanBodyBones.RightHand);
                var head = target.Animator.GetBoneTransform(HumanBodyBones.Head);
                Require(hand && head && Vector3.Distance(impact.position, hand.position) <= .35f &&
                    Vector3.Distance(impact.position, head.position) <= .35f,
                    "Accepted face contact is detached from the live hand/head region.");
                Require(feedback.IsHolding && Mathf.Abs(feedback.RemainingHold - feedback.lightHold) < .001f &&
                    feedback.PlaybackRate(pair) == 0 && !feedback.IsSlowing,
                    "Existing light hit-stop did not own this actual contact.");
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
            if (!activeCase || Guard || cancelled)
                failure = "Rejected/cancelled exchange emitted a new effect: " + cue;
            if (effect && effect.activeInHierarchy)
            {
                sawEffects = true;
                if (game.battleVfx.weaponTrails.Owns(effect))
                    failure = "Unarmed slap emitted a weapon trail effect.";
            }
        }

        static void AudioPlayed(string cue, AudioClip clip)
        {
            if (!running)
                return;
            if (!activeCase || Guard || cancelled)
                failure = "Rejected/cancelled exchange emitted new audio: " + cue;
            if (cue.IndexOf("getup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                cue.IndexOf("get_up", StringComparison.OrdinalIgnoreCase) >= 0)
                failure = "Standing-only recovery emitted get-up sound: " + cue;
            if (clip)
                sawAudio = true;
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
            sawLights |= lighting.ActiveFlashCount > 0 && lighting.PlayedFlashCount > lightsBefore &&
                lighting.impactLights.Any(light => light && light.enabled && light.intensity > 0);
            sawShake |= shake.IsShaking && shake.ShakeCount > shakesBefore;
            sawFlash |= feedback.flash.isActiveAndEnabled && feedback.flash.Progress < 1 && feedback.flash.color.a > 0;
            if (expectSurface)
                foreach (var renderer in target.GetComponentsInChildren<Renderer>(false))
                {
                    renderer.GetPropertyBlock(surfaceBlock);
                    sawSurface |= surfaceBlock.GetFloat(HitFlashId) > 0;
                }
        }
    }
}
