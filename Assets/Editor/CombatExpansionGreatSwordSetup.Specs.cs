using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordSetup
    {
        // Source-pose contact review only; gameplay and recovery acceptance remain separate.
        public const bool ContactsReviewed = true;

        public sealed class Spec
        {
            public int index;
            public string displayName;
            public float[] contacts, swings;
            public string[] groups, regions;
            public Vector3[] directions;
            public float landingSeconds;
        }

        public static readonly Spec[] Specs =
        {
            new Spec
            {
                index = 0,
                displayName = "GreatSword ambush",
                contacts = new[] { 138f / 240 },
                swings = new[] { .35f },
                groups = new[] { "heavy_hit" },
                regions = new[] { "Head under the descending blade" },
                directions = new[] { Vector3.down },
                landingSeconds = 148f / 240
            },
            new Spec
            {
                index = 1,
                displayName = "GreatSword thrust and throw",
                contacts = new[] { 51f / 60 },
                swings = new[] { .6f, 1.65f },
                groups = new[] { "stab_hit" },
                regions = new[] { "Upper chest and neck on the forward thrust" },
                directions = new[] { Vector3.back },
                landingSeconds = 560f / 240
            },
            new Spec
            {
                index = 2,
                displayName = "GreatSword impale and lift",
                contacts = new[] { 49f / 60 },
                swings = new[] { .55f, 1.6f },
                groups = new[] { "stab_hit" },
                regions = new[] { "Hip and upper leg on the low thrust" },
                directions = new[] { Vector3.back },
                landingSeconds = 497f / 240
            },
            new Spec
            {
                index = 3,
                displayName = "GreatSword three-cut takedown",
                contacts = new[] { 22f / 60, 41f / 60, 365f / 240 },
                swings = new[] { .17f, .5f, 1.3f },
                groups = new[] { "heavy_hit", "heavy_hit", "heavy_hit" },
                regions = new[] { "Torso and upper arm on the opening cut",
                    "Hip and upper leg on the rising cut", "Raised upper arm before the descending finisher crosses the torso" },
                directions = new[] { Vector3.left, Vector3.up, Vector3.down },
                landingSeconds = 369f / 240
            }
        };
    }
}
