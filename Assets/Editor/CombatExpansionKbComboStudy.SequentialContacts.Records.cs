using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionKbComboStudy
    {
        static bool bodyJabVariants;
        static float SequentialEntryBlend => bodyJabVariants ? .04f : .12f;
        static float SequentialLinkBlend => bodyJabVariants ? .04f : .08f;
        static string SequentialOutput => "GeneratedAssets/CombatExpansion/KbComboStudy/" +
            (bodyJabVariants ? "BodyJabCandidates" : "SequentialContacts");
        static string SequentialScope => bodyJabVariants
            ? "PROVISIONAL short blend and anatomical target variants. Entry/link blends 0.04 seconds. " +
                "Mankey first jabs retain Head/HighFront; Pepe first jabs test Chest/MidFront. " +
                "Final hook tests Head/HighRight or HighLeft. Explicit source identities and target regions " +
                "are recorded per case. Exact hand/region triangle gaps, sequential reaction onsets, " +
                "bounded instantaneous grounding and reverse checks remain required. " +
                "No gameplay registration, enlarged hitboxes or source asset edits; misses remain misses."
            : SequentialDefaultScope;
        const string SequentialDefaultScope = "PROVISIONAL authoring evidence, never contact acceptance or registration. " +
            "Exact selected LeftHand/RightHand against Head skin triangles; unsigned zero may be penetration. " +
            "Positive search 120Hz plus 240Hz midpoint refinement, sequential frozen prior reactions. " +
            "A miss disables that reaction; later windows remain diagnostic and cannot complete the sequence. " +
            "Native complete clip ranges with attack links 0/.4/.8 and blends 0/.08/.08 seconds. " +
            "HighFront weak for first two; final HighRight/HighLeft comparison remains provisional. " +
            "Instantaneous full visible body grounding: restored pair sample, hips-only lift, .01m cushion, " +
            "maximum .2m. No saved grounding envelope validity is claimed. " +
            "Entry/seam blends, window-entry overlap, recoil and nonadvancing samples cannot set onsets. " +
            "Velocities are grounded hand/head bone pivot finite differences; exact gap is skinned geometry. " +
            "Same positive onsets verified on negative lane only for complete positive sequences. " +
            "No source assets, gameplay scenes, drivers or animation assets are saved.";
        static readonly float[] SequentialStarts = { .06f, .46f, .88f };
        static readonly float[] SequentialEnds = { .30f, .70f, 1.12f };

        [Serializable]
        sealed class SequentialStudy
        {
            public string status = "RUNNING_PROVISIONAL";
            public string scope = SequentialScope;
            public string startedUtc = DateTime.UtcNow.ToString("O");
            public string updatedUtc;
            public int completedCases;
            public List<SequentialCase> cases = new List<SequentialCase>();
            public string failure;
        }

        [Serializable]
        sealed class SequentialCase
        {
            public string name, attacker, receiver;
            public string status = "RUNNING_PROVISIONAL";
            public string phase, failure;
            public int assignment, lane;
            public float spacing, duration, entryBlendSeconds, linkBlendSeconds;
            public string[] targetRegions;
            public SourceRecord[] sources;
            public TrackRecord[] attacks, reactions;
            public float[] onsets = { -1, -1, -1 };
            public List<SequentialStrike> strikes = new List<SequentialStrike>();
            public List<string> geometry = new List<string>();
            public List<SequentialSample> verification = new List<SequentialSample>();
            public string[] sheets;
            public float[] sheetSeconds;
            public int reverseSamples;
            public float worstPoseMetres, worstPoseDegrees, worstGapReseek, worstLiftReseek;
        }

        [Serializable]
        sealed class SequentialStrike
        {
            public int strike;
            public float start, end;
            public string status = "SEARCHING";
            public float onset = -1;
            public SequentialSample minimum;
            public List<SequentialSample> localMinima = new List<SequentialSample>();
            public List<SequentialSample> samples = new List<SequentialSample>();
        }

        [Serializable]
        sealed class SequentialSample
        {
            public ContactEvidence contact;
            public SequentialFloor floor;
            public Vector3 targetVelocity;
            public float forwardSpeed, gapClosingSpeed;
            public bool refined, inBlend, windowEntry, advancing, nearSurface, eligible, selected;
            public string flags;
        }

        [Serializable]
        sealed class SequentialFloor
        {
            public float attackerRaw, receiverRaw, attackerLift, receiverLift;
            public float attackerAfter, receiverAfter;
        }
    }
}
