using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static class CombatExpansionActionProfileCheck
    {
        const string Output = "GeneratedAssets/CombatExpansion/ActionProfiles/Validation.txt";
        const string AudioError = ": missing routed contact audio.";
        const string CueError = ": missing positioned presentation cue on the shared clock.";

        public static void ValidateLookup()
        {
            var temporary = new List<Object>();
            var report = new List<string>();
            T Keep<T>(T value) where T : Object
            {
                temporary.Add(value);
                return value;
            }
            try
            {
                var attack = Keep(new AnimationClip { name = "Shared opener" });
                var reaction = Keep(new AnimationClip { name = "Shared reaction" });
                var other = Keep(new AnimationClip { name = "Continuation" });
                var bank = Keep(ScriptableObject.CreateInstance<BattleSfxBank>());
                var first = Keep(ScriptableObject.CreateInstance<CombatActionDefinition>());
                var second = Keep(ScriptableObject.CreateInstance<CombatActionDefinition>());
                var restored = Keep(ScriptableObject.CreateInstance<CombatActionDefinition>());
                var grapple = Keep(ScriptableObject.CreateInstance<FrankGrappleDefinition>());
                var legacy = Profile(attack, reaction, .2f);
                bank.moves = new[] { legacy };
                var a = Move("profile-a", attack, reaction, first);
                var b = Move("profile-b", attack, reaction, second);
                Require(first.presentationProfile == null && second.presentationProfile == null,
                    "New definitions must retain optional null profiles.");
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(first), restored);
                Require(restored.presentationProfile == null, "Serialization replaced an absent profile.");
                Require(ReferenceEquals(bank.FindMove(a), legacy) && ReferenceEquals(bank.FindMove(b), legacy),
                    "Legacy actions sharing source clips must return the same bank object.");
                a.actionDefinition = null;
                Require(ReferenceEquals(bank.FindMove(a), legacy), "Definition-free legacy fallback changed.");
                a.actionDefinition = first;
                Require(bank.FindMove(null) == null, "Null move fallback changed.");
                report.Add("PASS legacy lookup identity and optional null-profile serialization.");

                first.presentationProfile = Profile(attack, reaction, .2f, .65f);
                second.presentationProfile = Profile(attack, reaction, .2f, .45f, .9f);
                SetContacts(first);
                SetContacts(second);
                Require(ReferenceEquals(bank.FindMove(a), first.presentationProfile) &&
                    ReferenceEquals(bank.FindMove(b), second.presentationProfile),
                    "Shared source clips aliased explicit timelines or lookup copied a profile.");
                Require(!ReferenceEquals(bank.FindMove(a).cues, bank.FindMove(b).cues) &&
                    bank.FindMove(a).cues.Length == 2 && bank.FindMove(b).cues.Length == 3 &&
                    bank.FindMove(a).cues[1].seconds == .65f && bank.FindMove(b).cues[1].seconds == .45f,
                    "Distinct per-contact timelines were not preserved.");
                RequireOnlyAudioErrors(first, a, bank);
                RequireOnlyAudioErrors(second, b, bank);
                for (int i = 0; i < 16; i++)
                    bank.FindMove(a);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 256; i++)
                    bank.FindMove(a);
                Require(GC.GetAllocatedBytesForCurrentThread() == before, "Explicit lookup allocated managed memory.");
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(first), restored);
                Require(restored.presentationProfile != null && restored.presentationProfile.cues.Length == 2 &&
                    restored.presentationProfile.cues[1].seconds == .65f,
                    "Serialization lost the explicit timeline.");
                report.Add("PASS shared clips resolve independent explicit timelines by reference; serialization preserved.");
                report.Add("PASS 256 warmed explicit lookups allocate zero managed bytes.");

                var profile = first.presentationProfile;
                profile.attack = other;
                Require(ReferenceEquals(bank.FindMove(a), profile), "Wrong-source explicit profile fell back to bank.");
                RequireError(first, a, bank, "Explicit presentation profile attack disagrees");
                profile.attack = attack;
                profile.reaction = other;
                RequireError(first, a, bank, "Explicit presentation profile reaction disagrees");
                profile.reaction = reaction;
                var sourcePair = a.sourcePair;
                a.sourcePair = null;
                Require(ReferenceEquals(bank.FindMove(a), profile), "Missing source silently discarded explicit profile.");
                RequireError(first, a, bank, "Explicit presentation profile requires a source pair.");
                a.sourcePair = sourcePair;
                report.Add("PASS wrong attack/reaction and absent source pair produce validation errors without fallback.");

                var cues = profile.cues;
                profile.cues = null;
                RequireError(first, a, bank, "Explicit presentation profile has no cues.");
                RequireError(first, a, bank, CueError);
                profile.cues = Array.Empty<BattleSfxBank.Cue>();
                RequireError(first, a, bank, "Explicit presentation profile has no cues.");
                profile.cues = new BattleSfxBank.Cue[] { null };
                RequireError(first, a, bank, "Explicit presentation profile has a null cue record.");
                profile.cues = cues;
                report.Add("PASS null/empty cue arrays and null cue records report errors without exceptions.");

                cues[0].seconds += .01f;
                RequireError(first, a, bank, CueError);
                cues[0].seconds = first.contacts[0].seconds;
                cues[0].group = "wrong-group";
                RequireError(first, a, bank, CueError);
                cues[0].group = first.contacts[0].presentationGroup;
                cues[0].hasContactPoint = false;
                RequireError(first, a, bank, CueError);
                cues[0].damageOnLanding = true;
                RequireOnlyAudioErrors(first, a, bank);
                cues[0].damageOnLanding = false;
                first.contacts[0].nonDamagingInteraction = true;
                RequireOnlyAudioErrors(first, a, bank);
                first.contacts[0].nonDamagingInteraction = false;
                cues[0].hasContactPoint = true;
                report.Add("PASS clock/group/contact-anchor checks, landing/interaction exemptions and audio routing retained.");

                grapple.releasePresentation = Profile(attack, reaction, .2f, .65f);
                grapple.throwing = Continuation(other, reaction);
                grapple.escaping = Continuation(attack, other);
                Require(grapple.Valid, "Transient grapple fixture is invalid.");
                a.grapple = grapple;
                profile.attack = other;
                profile.cues = null;
                foreach (FrankGrappleOutcome outcome in Enum.GetValues(typeof(FrankGrappleOutcome)))
                {
                    a.grappleOutcome = outcome;
                    Require(ReferenceEquals(bank.FindMove(a), grapple.Presentation(outcome)),
                        "Explicit profile displaced grapple outcome " + outcome + ".");
                    RequireOnlyAudioErrors(first, a, bank);
                }
                first.presentationProfile = null;
                RequireOnlyAudioErrors(first, a, bank);
                report.Add("PASS valid grapple release/throw/escape retain priority and continuation source validation.");
                report.Add("PASS all lookup checks; transient fixtures only, no scene or asset changes.");
            }
            catch (Exception error)
            {
                report.Add("FAIL " + error);
                throw;
            }
            finally
            {
                foreach (var value in temporary)
                    Object.DestroyImmediate(value);
                Directory.CreateDirectory(Path.GetDirectoryName(Output));
                File.WriteAllLines(Output, report);
            }
        }

        static CombatTripletData Move(string id, AnimationClip attack, AnimationClip reaction,
            CombatActionDefinition definition)
        {
            definition.actionId = id;
            return new CombatTripletData
            {
                moveName = id,
                actionDefinition = definition,
                sourcePair = new FrankBattlePair { attack = attack, reaction = reaction }
            };
        }

        static BattleSfxBank.Move Profile(AnimationClip attack, AnimationClip reaction, params float[] times)
        {
            return new BattleSfxBank.Move
            {
                attack = attack,
                reaction = reaction,
                cues = times.Select(time => new BattleSfxBank.Cue
                {
                    seconds = time,
                    group = "contact",
                    hasContactPoint = true
                }).ToArray()
            };
        }

        static FrankGrappleDefinition.Continuation Continuation(AnimationClip attack, AnimationClip reaction)
        {
            return new FrankGrappleDefinition.Continuation
            {
                attack = attack,
                reaction = reaction,
                presentation = Profile(attack, reaction, .2f, .65f)
            };
        }

        static void SetContacts(CombatActionDefinition definition)
        {
            var profile = definition.presentationProfile;
            definition.contacts = profile.cues.Select((cue, index) => new CombatActionDefinition.Contact
            {
                strikeId = "contact-" + index,
                attack = profile.attack,
                reaction = profile.reaction,
                seconds = cue.seconds,
                activeWindowSeconds = new Vector2(cue.seconds - .01f, cue.seconds + .01f),
                presentationGroup = cue.group,
                targetRegion = "Chest",
                continuation = "Continue on the action clock."
            }).ToArray();
        }

        static void RequireOnlyAudioErrors(CombatActionDefinition definition, CombatTripletData move, BattleSfxBank bank)
        {
            // A transient bank has no mixer routing. Require that expected error for every contact,
            // while rejecting any additional timeline/source error without depending on project assets.
            var errors = definition.Validate(move, bank).ToArray();
            Require(errors.Length == definition.contacts.Length && errors.All(e => e.EndsWith(AudioError)),
                "Unexpected validation result: " + string.Join("; ", errors));
        }

        static void RequireError(CombatActionDefinition definition, CombatTripletData move, BattleSfxBank bank,
            string expected)
        {
            var errors = definition.Validate(move, bank).ToArray();
            Require(errors.Any(error => error.Contains(expected)), "Missing validation error: " + expected);
        }

        static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
