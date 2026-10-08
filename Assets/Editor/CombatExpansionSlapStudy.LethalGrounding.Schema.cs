using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    // Stable executeMethod entry points; implementation shares the existing native preview helpers.
    public static class CombatExpansionSlapLethalPreview
    {
        public static void BakeCandidateGrounding() => CombatExpansionSlapStudy.BakeCandidateGrounding();
        public static void ValidateCandidateGrounding() => CombatExpansionSlapStudy.ValidateCandidateGrounding();
        public static void CaptureGroundedCandidates() => CombatExpansionSlapStudy.CaptureGroundedCandidates();
    }

    public static partial class CombatExpansionSlapStudy
    {
        const string LgOutput = "GeneratedAssets/CombatExpansion/SlapStudy/LethalCandidates/Grounded";
        const string LgAssets = CombatExpansionSlapLethalAuthoring.AssetRoot + "/Grounding";
        const float LgMaximumLift = .30f;
        const float LgMaximumSpeed = 2f;
        const float LgMaximumAcceleration = 40f;
        const float LgSupportBand = .025f;
        const float LgContactTolerance = .002f;
        const string LgScope = "PROVISIONAL GEOMETRY EVIDENCE ONLY; no gameplay registration or choreography " +
            "acceptance. Native attacker and drivers, candidate receiver, original clocks and fingers. " +
            "Native grounding is copied exactly through the authored preserved prefix; attacker track stays " +
            "native throughout. Receiver transitions after the prefix via smoothstep to a smoothed measured " +
            "clearance envelope. No contact cue is inferred from the authoring anchor. Contact is remeasured " +
            "against native skin surfaces; unsigned gap cannot resolve penetration depth. World floor Y=0. " +
            "Support means a selected feet/hands/head skin region within 25mm of floor; body clearance alone " +
            "does not establish support. Landing is first sustained non-foot support for 100ms after prefix, " +
            "reported as a sampled bracket, never a gameplay cue. Foot float is reported even if body clears. " +
            "Preview neutral plane lies 15mm below measured Y=0. Native 0.2m bound is retained for attacker; " +
            "receiver lift above 0.2m requires measured raw need, " +
            "with absolute 0.30m cap. Authored lift after prefix: speed <=2m/s, " +
            "acceleration <=40m/s2 at stored knots. " +
            "Prefix fidelity <=2mm; body floor >=-1mm; reverse seek <=1mm and <=0.1deg. " +
            "Bake >=240Hz; independent validation >=361Hz; capture >=61Hz plus dense contact and sheet times. " +
            "Support diagnostics and images must be reviewed even when numerical gates pass.";

        [Serializable]
        sealed class LgPhase
        {
            public int sequence;
            public float anchor, preserve, transitionStart, transitionEnd, hold;
            public string timeMap, timeMapHash;
        }

        [Serializable]
        sealed class LgFile
        {
            public string path, bytes, meta, serialized;
        }

        [Serializable]
        sealed class LgReport
        {
            public string status = "RUNNING", operation, scope = LgScope, failure;
            public string utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion;
            public SourceRecord[] sources;
            public LethalClipRecord[] clips;
            public LgFile[] guardedFiles;
            public LgPhase[] phases;
            public List<LgTrack> tracks = new List<LgTrack>();
            public List<LgCase> cases = new List<LgCase>();
            public bool sourceGuardsPassed;
        }

        [Serializable]
        sealed class LgTrack
        {
            public int sequence;
            public string fighter, avatar, justification;
            public bool receiver;
            public int samples, floorViolations, prefixViolations;
            public float duration, minimumRawY, minimumRawSeconds, requiredMaximum, maximumLift;
            public float maximumDeltaSpeed, maximumDeltaAcceleration, maximumLiftSpeed, maximumLiftAcceleration;
            public float maximumAuthoredSpeed, maximumAuthoredAcceleration;
            public float maximumPrefixLiftError, maximumLiftSeconds;
        }

        [Serializable]
        sealed class LgCase
        {
            public CandidateRecord pair;
            public string status = "MEASURING", failure, metrics, trajectory, contactRows;
            public int direction, samples, floorViolations, prefixViolations, unsupportedSamples, footFloatSamples;
            public int receiverUnsupportedHoldSamples, reverseSamples;
            public float minimumAttackerY = float.MaxValue, minimumReceiverY = float.MaxValue;
            public float maximumAttackerLift, maximumReceiverLift, maximumPrefixPositionError;
            public float maximumContactGapError, maximumReversePositionError, maximumReverseAngleError;
            public float maximumReverseFloorError, maximumReverseSupportError, maximumReverseContactError;
            public float minimumContactGap = float.MaxValue, measuredContactSeconds = -1;
            public Vector3 measuredHandPoint, measuredFacePoint;
            public float nativeMinimumContactGap = float.MaxValue, nativeContactSeconds = -1;
            public float landingPreviousSeconds = -1, landingSeconds = -1;
            public Vector3 landingSupportPoint, landingHips;
            public float maximumBothFeetHeight, maximumHoldSupportGap, maximumHoldMotion;
            public bool geometryGatesPassed, choreographyAccepted;
            public string supportSelection;
        }

        sealed class LgPose
        {
            public Vector3[] positions;
            public Quaternion[] rotations;
            public Vector3[] support;
            public float attackerY, receiverY, contactGap = -1;
        }

        static string LgPath(int sequence) => LgAssets + "/SlapSequence" + sequence +
            "_LethalReceiver_Provisional_Grounding.asset";
    }
}
