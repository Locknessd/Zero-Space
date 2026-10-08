using System;
using System.Collections.Generic;
using System.Linq;
using FrankRetarget;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    /// <summary>In-memory contract checks; does not assign or accept a Slap animation asset.</summary>
    public static class CombatExpansionLethalVariantCheck
    {
        [MenuItem("Tools/Combat Expansion/Validate Optional Lethal Pair Contract")]
        public static void Run() => Debug.Log(Validate());

        public static string Validate()
        {
            var owned = new List<Object>();
            try
            {
                T Own<T>(T item) where T : Object { owned.Add(item); return item; }
                var bank = AssetDatabase.LoadAssetAtPath<BattleSfxBank>("Assets/Audio/Battle/BattleSfxBank.asset");
                Require(bank, "Existing routed battle bank is required.");
                var root = Own(new GameObject("Lethal variant contract fixture"));
                var avatar = Own(AvatarBuilder.BuildGenericAvatar(root, root.name));
                var attack = Own(Clip("attack", 1));
                var originalReaction = Own(Clip("native receiver", 2));
                var fullReaction = Own(Clip("fixture full receiver", 3));
                var definition = Own(ScriptableObject.CreateInstance<CombatActionDefinition>());
                var grounding = Own(ScriptableObject.CreateInstance<FrankPairGrounding>());
                grounding.tracks = new[]
                {
                    Track(avatar, false), Track(avatar, true)
                };
                var originalProfile = new BattleSfxBank.Move
                {
                    attack = attack, reaction = originalReaction,
                    cues = new[] { Cue(.4f, "light_hit"), Cue(.8f, "light_hit") }
                };
                definition.presentationProfile = originalProfile;
                definition.contacts = new[]
                {
                    Contact("first", .4f, "light_hit", attack, originalReaction, false),
                    Contact("second", .8f, "light_hit", attack, originalReaction, false)
                };
                var move = new CombatTripletData
                {
                    actionDefinition = definition,
                    sourcePair = new FrankBattlePair { attack = attack, reaction = originalReaction }
                };
                bool Select(bool lethal, out CombatLethalPairVariant result) =>
                    CombatLethalPairVariant.TrySelect(move, lethal, bank, avatar, avatar, out result, out _);
                Require(Select(true, out var selected) && selected == null && bank.FindMove(move) == originalProfile,
                    "Absent variant changed legacy lookup or lethal selection.");
                var absentTimes = BattleHitDamageSequence.ContactTimes(bank.FindMove(move), 2);
                Require(absentTimes.SequenceEqual(new[] { .4f, .8f }), "Absent variant changed legacy damage contacts.");
                CombatLethalPairVariant Variant() => new CombatLethalPairVariant
                {
                    reaction = fullReaction,
                    grounding = grounding,
                    presentationProfile = new BattleSfxBank.Move
                    {
                        attack = attack, reaction = fullReaction,
                        cues = new[] { Cue(.4f, "light_hit"), Cue(.8f, "light_hit"), Cue(2.5f, "body_fall") }
                    },
                    contacts = new[]
                    {
                        Contact("first", .4f, "light_hit", attack, fullReaction, false),
                        Contact("second", .8f, "light_hit", attack, fullReaction, false),
                        Contact("landing", 2.5f, "body_fall", attack, fullReaction, true)
                    }
                };
                definition.lethalPairVariant = Variant();
                Require(Select(false, out selected) && selected == null, "Nonlethal selection used optional data.");
                Require(Select(true, out selected) && selected == definition.lethalPairVariant,
                    "Complete lethal outcome was rejected.");
                Require(bank.FindMove(move) == originalProfile && move.sourcePair.reaction == originalReaction &&
                    definition.contacts[0].reaction == originalReaction, "Selection mutated shared native authoring.");
                Require(selected.Duration(move.sourcePair) == 3, "Full authored receiver duration was truncated.");
                var times = BattleHitDamageSequence.ContactTimes(selected.presentationProfile, selected.Duration(move.sourcePair));
                Require(times.SequenceEqual(absentTimes), "Nondamaging landing changed primary damage times/count.");
                var displayed = new List<(long hp, long damage)>();
                var damage = new BattleHitDamageSequence(101, 0, 101, times, (hp, amount) => displayed.Add((hp, amount)));
                damage.Advance(.39f);
                Require(displayed.Count == 0, "Damage occurred before contact.");
                damage.Advance(.4f);
                damage.Advance(.4f);
                damage.Advance(3);
                damage.Complete();
                Require(displayed.SequenceEqual(new[] { (51L, 50L), (0L, 51L) }),
                    "Slow-frame/landing presentation duplicated or repartitioned accepted damage.");
                int rejected = 0;
                void Reject(Action<CombatLethalPairVariant> mutate, string reason)
                {
                    definition.lethalPairVariant = Variant();
                    mutate(definition.lethalPairVariant);
                    Require(!Select(true, out var invalid) && invalid == null, "Accepted invalid variant: " + reason);
                    Require(Select(false, out var native) && native == null,
                        "Invalid optional data altered nonlethal playback: " + reason);
                    rejected++;
                }
                Reject(v => v.reaction = null, "missing full clip");
                Reject(v => v.grounding = null, "missing grounding");
                Reject(v => v.presentationProfile.reaction = originalReaction, "native receiver profile");
                Reject(v => v.presentationProfile.attack = fullReaction, "changed attacker");
                Reject(v => v.presentationProfile.cues[1].seconds = .81f, "shifted primary contact");
                Reject(v => v.presentationProfile.cues[2].damageOnLanding = true, "added landing damage");
                Reject(v => v.presentationProfile.cues[0].hasContactPoint = false, "missing measured anchor");
                Reject(v => v.presentationProfile.cues[0].contactOffset.x = float.NaN, "invalid anchor");
                Reject(v => v.presentationProfile.cues[0] = null, "null cue");
                Reject(v => v.contacts[0].strikeId = "replacement", "changed damage identity");
                Reject(v => v.contacts = v.contacts.Take(2).ToArray(), "unmapped landing");
                Reject(v => v.contacts[0].nonDamagingInteraction = true, "removed primary damage");
                Reject(v => v.contacts[1].activeWindowSeconds.y = float.NaN, "invalid contact window");
                definition.lethalPairVariant = Variant();
                grounding.tracks[1].duration = 2;
                Require(!Select(true, out selected), "Incomplete receiver grounding accepted.");
                grounding.tracks[1].duration = 3;
                var missingAvatar = Own(AvatarBuilder.BuildGenericAvatar(root, root.name));
                Require(!CombatLethalPairVariant.TrySelect(move, true, bank, avatar, missingAvatar,
                    out selected, out _), "Unmeasured participant avatar accepted.");
                return "PASS optional lethal pair contract: absent/nonlethal compatibility, full receiver selection, " +
                    "stable damage IDs/times/count/total, nondamaging landing, " + (rejected + 2) +
                    " invalid cases rejected. Infrastructure only; no Slap adaptation is accepted or assigned.";
            }
            finally
            {
                for (int i = owned.Count - 1; i >= 0; i--)
                    if (owned[i]) Object.DestroyImmediate(owned[i]);
            }
        }

        static AnimationClip Clip(string name, float duration)
        {
            var clip = new AnimationClip { name = name, legacy = true };
            clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, duration, 0));
            return clip;
        }

        static FrankPairGrounding.Track Track(Avatar avatar, bool receiver) => new FrankPairGrounding.Track
        {
            avatar = avatar, receiver = receiver, duration = 3, lift = new[] { 0f, 0f }
        };

        static BattleSfxBank.Cue Cue(float seconds, string group) => new BattleSfxBank.Cue
        {
            seconds = seconds, group = group, hasContactPoint = true,
            contactBone = HumanBodyBones.Chest, contactSource = "RightHand", finalLanding = group == "body_fall"
        };

        static CombatActionDefinition.Contact Contact(string id, float time, string group,
            AnimationClip attack, AnimationClip reaction, bool nondamaging) => new CombatActionDefinition.Contact
        {
            strikeId = id, attack = attack, reaction = reaction, seconds = time,
            activeWindowSeconds = new Vector2(time, time), presentationGroup = group,
            nonDamagingInteraction = nondamaging, targetRegion = "fixture receiver",
            continuation = "Full authored fixture receiver continues on the pair clock."
        };

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
