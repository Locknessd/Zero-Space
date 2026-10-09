using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        const string CCOutput = BladeOutput + "/Remaining";
        const float CCNear = .03f;
        const string CCScope = "Blade-only candidate evidence; no approved contacts, hit counts or gameplay changes. " +
            "Execution02..10 use native source pairs, armed PlayerA and unarmed PlayerB, both avatar assignments " +
            "and lane directions, source offset 1.7m and baked grounding. Times are authoritative source seconds, " +
            "not normalized time; shorter source clips hold their final pose. World units are metres, Y-up, " +
            "ground Y=0. Full timeline at 30Hz including endpoints; 240Hz refinement is discovered independently " +
            "per case around blade approach, near surfaces, local/global minima and non-leg support minima. " +
            "Coarse discovery may miss subframe events; complete CSV is retained for manual review. " +
            "Candidate intervals are observed proximity runs or minimum windows, never approved damage events. " +
            "Unsigned zero distance means touch/intersection, not penetration depth or a damaging strike. " +
            "Blade surface uses native BladeR 3e685dd57e9c78b49b20bef3e8358ae3:4300002, 111 faces, " +
            "all local Z<=-0.33. Grip, guard and sheath are excluded. " +
            "Other visible body contacts require manual review. " +
            "Dense gap uses MovingBodyProbe BVH rebuilt each frame; nearestBone is only a spatial pivot label. " +
            "Representative CAMeasureContact anatomy uses normalized native skin weights and barycentric points; " +
            "triangle ties within 0.00001m and dominant group ties within 0.01 are retained, never resolved as hits. " +
            "Relative velocity differentiates the fixed blade-local and chosen bone-local anchors at +/-1/240s; " +
            "endpoints use a clamped one-sided interval with its actual denominator. Victim axes are sampled at t. " +
            "Support excludes legs: every triangle corner needs >=0.5 HeadNeck/AxialTorso/arm weight. " +
            "Torso front normal is cross(rightShoulder-leftShoulder, upperChestOrChest-hips); dot with up is " +
            "+1 face-up, -1 face-down. Near-ground 0.02m is a candidate threshold, not landing approval. " +
            "Sheets use established fixed side/oblique gameplay study cameras and a display floor at -0.015m. " +
            "Backward seeks start at clip end and must stay within 0.001m, " +
            "torso dot within 0.0001, same support bone. " +
            "No Execution01 timing, spacing adaptation or hit count is reused. Source assets/settings stay unchanged.";

        [Serializable]
        sealed class CCReport
        {
            public string scope = CCScope;
            public string utc = DateTime.UtcNow.ToString("O");
            public string unity = Application.unityVersion;
            public int execution;
            public string status = "RUNNING";
            public string error;
            public string grounding;
            public SourceRecord[] sources;
            public string[] caseFiles;
            public string[] caseStatuses;
            [NonSerialized] public List<CCCase> cases = new List<CCCase>();
        }

        [Serializable]
        sealed class CCCase
        {
            public string status = "RUNNING";
            public string error;
            public string file;
            public CACase anatomy;
            public string attackSourceAvatar;
            public string reactionSourceAvatar;
            public float attackDurationSeconds;
            public float reactionDurationSeconds;
            public string grounding;
            public float minimumAttackerY;
            public float minimumReceiverY;
            public int coarseSamples;
            public int totalSamples;
            public List<CCWindow> refinementWindows = new List<CCWindow>();
            public List<CCInterval> candidateIntervals = new List<CCInterval>();
            public List<string> geometryDiagnostics = new List<string>();
            public string[] sheets;
            public CALanding finalFrame;
            public CALanding minimumSupportFrame;
            public CALanding minimumTorsoFrame;
        }

        [Serializable]
        sealed class CCWindow
        {
            public float startSeconds;
            public float endSeconds;
            public string reason;
        }

        [Serializable]
        sealed class CCInterval
        {
            public float startSeconds;
            public float endSeconds;
            public float representativeSeconds;
            public float minimumGapM;
            public float firstTouchIntersectionSeconds = -1;
            public float lastTouchIntersectionSeconds = -1;
            public float beforeSeconds;
            public float afterSeconds;
            public float approachGapRateMps;
            public float recoilGapRateMps;
            public int minimumTiedSamples;
            public string assessment;
        }

        sealed class CCFrame
        {
            public BladeRecord blade;
            public CALanding landing;
            public bool coarse;
        }
    }
}
