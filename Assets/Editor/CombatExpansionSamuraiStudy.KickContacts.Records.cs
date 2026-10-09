using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        const string KCOutput = "GeneratedAssets/CombatExpansion/SamuraiStudy/BodyContacts/Execution08Kick";
        const float KCStart = 1.30f, KCEnd = 1.85f, KCSeekTolerance = .001f;
        static readonly string[] KCChannels = { "LeftFoot_Head", "LeftFoot_Torso", "RightFoot_Head", "RightFoot_Torso" };
        const string KCScope = "Execution08 front-kick measurement; all candidates UNAPPROVED.\n" +
            "Both actual Mankey/Pepe assignments and both lane directions; native pair and grounding retained.\n" +
            "Entry spacing and native receiver offset remain 1.7m. No source edits or gameplay registration.\n" +
            "Source-clock 120Hz grid [1.30,1.85], endpoints plus exact 1.3958,1.6042,1.6667 seconds.\n" +
            "Both current foot skin surfaces against head and bounded torso; all four unsigned gaps retained.\n" +
            "Zero is touch/intersection, not proof of damaging contact. Positive gaps remain measured misses.\n" +
            "No reaction onset, damage, approved timing, or approved contacts are inferred.\n" +
            "Floor clearance is minimum visible evaluated body skin Y against world Y=0.\n" +
            "Each forward sample is explicitly remeasured after seeking to clip end, at unchanged 0.001m tolerance.\n" +
            "Closest surface points and all PairNodes world anchors/feet are checked in world metres.\n" +
            "Sheets use existing fixed side/oblique study cameras with one bounds volume per case.\n" +
            "Preview scene only; imported content and open dirty scenes are never saved or replaced.\n";

        [Serializable] sealed class KCReport
        {
            public string status = "RUNNING; partial evidence only";
            public string error;
            public string utc = DateTime.UtcNow.ToString("O");
            public string unity = Application.unityVersion;
            public string grounding;
            public string scope = KCScope;
            public List<KCCase> cases = new List<KCCase>();
        }

        [Serializable] sealed class KCCase
        {
            public string status = "PENDING";
            public string error, file, attacker, victim, grounding;
            public int direction;
            public float spacingM = 1.7f;
            public float durationSeconds;
            public string attackClip, reactionClip, attackDriver, reactionDriver;
            public string attackerAvatar, victimAvatar, attackSourceAvatar, reactionSourceAvatar;
            public float attackDurationSeconds, reactionDurationSeconds;
            public bool floorMinimaAvailable;
            public float minimumAttackerFloorClearanceM, minimumVictimFloorClearanceM;
            public List<string> selectionSummaries = new List<string>();
            public List<KCFrame> samples = new List<KCFrame>();
            public List<KCCandidate> candidates = new List<KCCandidate>();
            public List<KCSeek> backwardSeeks = new List<KCSeek>();
            public float[] imageTimes;
            public List<string> sheets = new List<string>();
        }

        [Serializable] sealed class KCFrame
        {
            public float seconds;
            public string status = "MEASURING";
            public float attackerFloorClearanceM, victimFloorClearanceM;
            public List<KCGap> gaps = new List<KCGap>();
            public List<KCTransform> transforms = new List<KCTransform>();
            public List<string> diagnostics = new List<string>();
        }

        [Serializable] sealed class KCGap
        {
            public string channel;
            public float gapM;
            public Vector3 footWorldPoint, targetWorldPoint;
            public string selectedTargetBone;
            public Vector3 targetBoneLocalPoint;
        }

        [Serializable] sealed class KCTransform
        {
            public string role, rig, bone;
            public Vector3 worldPosition;
            public Quaternion worldRotation;
            public Vector3 worldScale;
        }

        [Serializable] sealed class KCCandidate
        {
            public string channel, kind;
            public string assessment = "UNAPPROVED unsigned surface minimum; inspect neighbors and retained misses. " +
                "Duplicate endpoint neighbors indicate a truncated observation window, not an approach/recoil. " +
                "Tied samples use 0.00001m numerical tolerance; no timing is approved.";
            public int sampleIndex, previousIndex, nextIndex;
            public float seconds, gapM, previousSeconds, previousGapM, nextSeconds, nextGapM;
            public int tiedMinimumSamples;
        }

        [Serializable] sealed class KCSeek
        {
            public float seconds, fromSeconds;
            public float toleranceM = KCSeekTolerance;
            public float maxGapErrorM, maxClosestPointErrorM, maxAnchorErrorM, maxFloorErrorM;
            public float maxRotationErrorDegrees, maxScaleError;
            public bool withinOneMillimeter;
            public KCFrame repeated;
        }
    }
}
