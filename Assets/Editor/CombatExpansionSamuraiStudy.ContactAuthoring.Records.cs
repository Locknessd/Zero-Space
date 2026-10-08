using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        const float CATie = .00001f;
        const float CADelta = 1f / 240;
        static readonly string[] CAGroups =
            { "HeadNeck", "AxialTorso", "LeftArm", "RightArm", "LeftLeg", "RightLeg", "Unmapped" };

        [Serializable]
        sealed class CAReport
        {
            public string scope;
            public string utc;
            public string unity;
            public string grounding;
            public string scene;
            public List<CACase> cases = new List<CACase>();
            public List<CAAgreement> directionAgreement = new List<CAAgreement>();
        }

        [Serializable]
        sealed class CACase
        {
            public string attacker;
            public string victim;
            public int direction;
            public float spacing;
            public float duration;
            public string attackerAvatar;
            public string victimAvatar;
            public string attackClip;
            public string reactionClip;
            public string attackerDriver;
            public string victimDriver;
            public string bladeMesh;
            public string bladeRenderer;
            public string[] targetSkinMeshes;
            public int[] targetSkinTriangleCounts;
            public int torsoTriangleCount;
            public int supportTriangleCount;
            public List<CAContact> contacts = new List<CAContact>();
            public List<CALanding> landing = new List<CALanding>();
            public List<CASeek> backwardSeeks = new List<CASeek>();
            public float[] imageTimes;
            public float nearGroundThresholdM = .02f;
            public float firstNearGroundSeconds = -1;
            public float firstSupportNearGroundSeconds = -1;
            public float globalMinimumSupportSeconds;
            public float globalMinimumSupportY;
            public float finalTorsoFacingUp;
            public string supportAssessment;
            public float globalMinimumTorsoSeconds;
            public float globalMinimumTorsoY;
            public List<float> localMinimaSeconds = new List<float>();
            public string landingAssessment = "Candidate evidence only; torso clearance does not prove impact.";
        }

        [Serializable]
        sealed class CAContact
        {
            public float seconds;
            public float minimumGapM;
            public float attackerMinimumY;
            public float victimMinimumY;
            public string chosenBone;
            public Vector3 targetBoneLocalPoint;
            public Vector3 targetHipsLocalPoint;
            public Vector3 sourceBladeLocalPoint;
            public Vector3 bladeVelocityVictimMps;
            public Vector3 bladeVelocityWorldMps;
            public Vector3 relativeBladeVelocityVictimMps;
            public Vector3 targetAnchorVelocityWorldMps;
            public float finiteDifferenceDeltaSeconds = CADelta;
            public float anchorRoundtripErrorM;
            public float tieToleranceM = CATie;
            public List<CATriangle> tiedTriangles = new List<CATriangle>();
            public string assessment = "Unsigned surface minimum; anatomical and directional review required.";
        }

        [Serializable]
        sealed class CATriangle
        {
            public string renderer;
            public string mesh;
            public int targetTriangle;
            public int bladeRegionTriangle;
            public int[] targetVertexIndices;
            public int[] bladeVertexIndices;
            public float gapM;
            public Vector3 bladeWorldPoint;
            public Vector3 targetWorldPoint;
            public Vector3 barycentric;
            public float[] pointNormalizedHumanWeights;
            public float[] triangleMeanNormalizedHumanWeights;
            public float[] pointNormalizedGroupWeights;
            public float[] triangleMeanNormalizedGroupWeights;
            public string[] dominantGroups;
        }

        [Serializable]
        sealed class CALanding
        {
            public float seconds;
            public float torsoMinimumY;
            public float bodySupportMinimumY;
            public Vector3 supportWorldPoint;
            public string supportBone;
            public string supportRenderer;
            public string supportMesh;
            public int supportTriangle;
            public int supportVertex;
            public float torsoFacingUp;
            public float victimMinimumY;
            public float attackerMinimumY;
            public Vector3 hips;
            public Vector3 head;
            public Vector3 leftFoot;
            public Vector3 rightFoot;
            public Vector3 bladeTip;
        }

        [Serializable]
        sealed class CASeek
        {
            public float seconds;
            public float maxTrajectoryErrorM;
            public float torsoMinimumErrorM;
            public float supportMinimumErrorM;
            public float supportPointErrorM;
            public bool sameSupportBone;
            public float torsoFacingUpError;
            public float fullBodyMinimumErrorM;
            public float bladeTipErrorM;
            public float contactGapErrorM;
            public float contactAnchorErrorM;
            public bool withinOneMillimeter;
        }

        [Serializable]
        sealed class CAAgreement
        {
            public string attacker;
            public float seconds;
            public float gapDifferenceM;
            public bool sameChosenBone;
            public float boneLocalAnchorDifferenceM;
            public float hipsLocalAnchorDifferenceM;
            public float bladeLocalAnchorDifferenceM;
            public float victimLocalVelocityDifferenceMps;
            public float victimFloorDifferenceM;
            public float attackerFloorDifferenceM;
            public string interpretation = "Measured comparison only; differing zero-gap triangle ties remain ambiguous.";
        }
    }
}
