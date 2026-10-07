using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGrapplePlayCheck
    {
        static readonly HashSet<ulong> contactIds = new HashSet<ulong>();
        static readonly Dictionary<string, int> cueCounts = new Dictionary<string, int>();
        static string healthBefore;
        static int nativeBefore, contacts, damageContacts, groundContacts;

        static void RunDirectChecks()
        {
            Require(!pair.EscapeInputAllowed, "Direct playback enabled local decisions by default.");
            CheckPoseAndOwnership();
            if (Kind >= 7)
                pair.AdvanceTo(.3f);
            if (Kind == 5)
            {
                pair.AdvanceTo(.3f);
                pair.AllowLocalEscape(false);
                Require(!game.RequestThrowEscape(targetSide, targetId), "Server authority opt-out ignored.");
            }
            else if (Kind == 6)
            {
                pair.AllowLocalEscape(true);
                Require(!game.RequestThrowEscape(targetSide, targetId), "Early request accepted.");
                pair.AdvanceTo(.3f);
                Require(pair.EscapeWindowOpen, "Guard fixture missed the authored escape window.");
                Require(!game.RequestThrowEscape(sourceSide, sourceId), "Attacker accepted as defender.");
                Require(!game.RequestThrowEscape(targetSide, targetId - 1), "Stale playback accepted.");
                float oldTimeScale = Time.timeScale;
                try
                {
                    Time.timeScale = 0;
                    Require(!game.RequestThrowEscape(targetSide, targetId), "Paused request accepted.");
                }
                finally { Time.timeScale = oldTimeScale; }
                AcceptEscape();
            }
            else if (Kind == 7)
                pair.Cancel();
            else if (Kind == 8)
                target.gameObject.SetActive(false);
            else if (Kind == 9)
                game.ResetCombatQueue();
            acted = true;
        }

        static void AcceptEscape()
        {
            int audio = game.battleSfx.PlayedCueCount;
            int effects = game.battleVfx.PlayedEffectCount;
            float sample = pair.SampleTime;
            Require(game.RequestThrowEscape(targetSide, targetId), "Valid defender escape rejected.");
            Require(pair.GrappleOutcome == FrankGrappleOutcome.Escape, "Escape did not latch.");
            Require(!game.RequestThrowEscape(targetSide, targetId), "Duplicate escape accepted.");
            Require(pair.SampleTime == sample && sample < move.grapple.decisionSeconds,
                "Decision reset the clock or arrived after the branch.");
            Require(game.battleSfx.PlayedCueCount == audio && game.battleVfx.PlayedEffectCount == effects,
                "Changing outcome replayed presentation cues immediately.");
        }

        static void Contact(BattleVfxPlayer.Impact impact)
        {
            if (!running || !activeCase || impact.playback != pair)
                return;
            contacts++;
            if (impact.kind == BattleVfxPlayer.ContactKind.Ground)
                groundContacts++;
            if (impact.kind != BattleVfxPlayer.ContactKind.Ground || impact.cue != null && impact.cue.damageOnLanding)
                damageContacts++;
            if (!contactIds.Add(impact.eventId))
                failure = "Duplicate contact event identity.";
            if (Mathf.Abs(impact.seconds - pair.SampleTime) > .0001f)
                failure = "Contact did not use the shared displayed pose.";
            if (pair.GrappleOutcome != FrankGrappleOutcome.Throw)
                failure = "Release or escape emitted a throw contact.";
        }

        static void Cue(string group, AudioClip clip)
        {
            if (!running || !activeCase || !pair || !pair.Playing)
                return;
            cueCounts.TryGetValue(group, out int count);
            cueCounts[group] = count + 1;
        }

        static void CheckPoseAndOwnership()
        {
            // Round reset deliberately changes IDs as it invalidates outstanding work.
            if (Kind != 9)
                Require(source.PlaybackId == sourceId && target.PlaybackId == targetId,
                    "Participant playback identity changed within a sequence.");
            foreach (var fighter in fighters)
            {
                foreach (var bone in fighter.Animator.GetComponentsInChildren<Transform>(true))
                {
                    var p = bone.position;
                    var q = bone.rotation;
                    Require(float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z) &&
                        float.IsFinite(q.x) && float.IsFinite(q.y) && float.IsFinite(q.z) && float.IsFinite(q.w),
                        "Nonfinite combat pose: " + bone.name);
                }
            }
            if (!pair.Playing)
                return;
            Require(source.SourcePlayback == pair && target.SourcePlayback == pair,
                "Participants stopped sharing one pair playback.");
            Require(source.IsBusy && target.IsBusy, "One participant released the shared sequence early.");
            foreach (var manager in equipment)
                Require(manager.IsUnarmedPresentation && !manager.dualDaggersSet.activeSelf &&
                    manager.ActiveWeapon == TrumpWeaponManager.WeaponType.None &&
                    !manager.GetComponentsInChildren<Collider>(false).Any(c => c.enabled),
                    "Equipment escaped unarmed ownership.");
            foreach (var actor in new[] { pair.AttackerActor, pair.ReceiverActor })
                if (actor && actor.Pose != null)
                    Require(!actor.Pose.weaponRenderers.Any(r => r && r.enabled),
                        "Native source weapon remained visible.");
        }

        static void CheckCompletion()
        {
            Require(sequenceStarts == 1, "Presentation sequence restarted at the branch.");
            Require(!pair.AttackerActor && !pair.ReceiverActor, "Native paired actors survived completion.");
            Require(Object.FindObjectsByType<FrankTestActor>().Length == nativeBefore,
                "Native actor count leaked after completion.");
            Require(HealthSnapshot() == healthBefore, "Grapple validation mutated authoritative HP.");
            Require(!source.IsBusy && !target.IsBusy && source.Animator.enabled && target.Animator.enabled,
                "Busy flags or animator ownership did not recover.");
            for (int i = 0; i < 2; i++)
            {
                if (!fighters[i].isActiveAndEnabled)
                    continue;
                Require(!equipment[i].IsUnarmedPresentation &&
                    equipment[i].ActiveWeapon == TrumpWeaponManager.WeaponType.DualDaggers,
                    "Equipment suppression survived completion.");
            }
            if (Kind == 8)
            {
                target.gameObject.SetActive(true);
                target.ResetCombat();
                Require(equipment.All(manager => !manager.IsUnarmedPresentation),
                    "Disabled fighter retained suppression after reactivation and reset.");
            }
            if (Kind >= 7)
                return;
            var expected = Kind == 0 ? FrankGrappleOutcome.Release : Kind == 1 || Kind == 5
                ? FrankGrappleOutcome.Throw : FrankGrappleOutcome.Escape;
            Require(pair.GrappleOutcome == expected, "Wrong resolved grapple outcome.");
            Require(Kind == 0 || Kind == 3 || acted, "Expected input scenario never ran.");
            if (expected == FrankGrappleOutcome.Throw)
                Require(contacts == 2 && groundContacts == 1 && damageContacts >= 1,
                    "Throw must present one forearm contact and one landing.");
            else
                Require(contacts == 0 && damageContacts == 0, "Release or escape emitted damaging contact.");
            var profile = pair.PresentationProfile(game.battleSfx.bank);
            Require(profile != null, "Missing resolved presentation profile.");
            foreach (var cue in cueCounts)
            {
                if (cue.Key == "fight" || cue.Key == "getup" || cue.Key == "hurt_voice")
                    continue;
                int authored = profile.cues.Count(c => c.group == cue.Key);
                authored += profile.cues.Sum(c =>
                    game.battleSfx.bank.FindGroup(c.group)?.layers?.Count(layer => layer == cue.Key) ?? 0);
                Require(cue.Value <= authored, "Cue replayed across branch: " + cue.Key);
            }
            Require(game.battleSfx.PlayedCueCount > audioBefore, "No audible grapple cues were dispatched.");
        }

        static string HealthSnapshot()
        {
            // Read only: the public API intentionally does not offer health mutation for local decisions.
            var field = typeof(GameManager).GetField("_characterHp", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null, "Authoritative health observation unavailable.");
            var health = (Dictionary<PlayerUI.Side, long>)field.GetValue(game);
            return string.Join(";", health.OrderBy(item => item.Key).Select(item => item.Key + "=" + item.Value));
        }

        static TrumpWeaponManager CreateEquipment(CharacterCombat fighter)
        {
            var root = new GameObject("Grapple validation equipment");
            root.transform.SetParent(fighter.transform, false);
            var manager = root.AddComponent<TrumpWeaponManager>();
            manager.autoEquipWithAnimation = false;
            manager.dualDaggersSet = Prop(root.transform, "Dual daggers");
            manager.katanaSet = Prop(root.transform, "Katana");
            manager.katanaSwordMesh = Prop(root.transform, "Sword");
            manager.katanaCaseMesh = Prop(root.transform, "Sheath");
            manager.katanaDummyHandle = Prop(root.transform, "Handle");
            return manager;
        }

        static GameObject Prop(Transform parent, string name)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.AddComponent<BoxCollider>().isTrigger = true;
            root.AddComponent<TrailRenderer>().emitting = false;
            return root;
        }
    }
}
