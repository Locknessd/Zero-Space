using System;
using System.IO;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class PepeShowcaseCapture
    {
        static int recordedCameraTake;
        static Vector3 recordedCameraPosition;
        static Quaternion recordedCameraRotation;
        static float recordedCameraDrift;
        static int recordedLockedFrames;
        static Vector3 previousReferenceCamera;
        static Quaternion previousReferenceRotation;
        static float maximumReferenceStep;
        static float maximumReferenceTurn;
        static int referenceFrames;

        static void ResetRecordedCameraCheck()
        {
            recordedCameraTake = -1;
            recordedCameraDrift = 0;
            recordedLockedFrames = 0;
            referenceFrames = 0;
            maximumReferenceStep = 0;
            maximumReferenceTurn = 0;
            ResetRecordedBomIntro();
        }

        static void CheckRecordedCamera()
        {
            if (!recordingComplete) return;
            if (recordingBom && playShowcase.SequenceTime < playShowcase.library.introSeconds)
            {
                CheckRecordedBomIntro();
                return;
            }
            var director = playShowcase.director;
            float planned = recordingBom ? director.PlannedDistance(playShowcase) : director.SequenceDistance;
            if (Mathf.Abs(director.SafetyDistance - planned) > .001f)
                throw new InvalidOperationException("Output aspect changed the fitted sequence camera distance.");
            var camera = director.output.transform;
            if (recordingBom && director.cinematicAttackReference)
            {
                if (referenceFrames > 0)
                {
                    maximumReferenceStep = Mathf.Max(maximumReferenceStep,
                        Vector3.Distance(previousReferenceCamera, camera.position));
                    maximumReferenceTurn = Mathf.Max(maximumReferenceTurn,
                        Quaternion.Angle(previousReferenceRotation, camera.rotation));
                }
                previousReferenceCamera = camera.position;
                previousReferenceRotation = camera.rotation;
                referenceFrames++;
            }
            if (recordedCameraTake != playShowcase.TakeIndex)
            {
                recordedCameraTake = playShowcase.TakeIndex;
                recordedCameraPosition = camera.position;
                recordedCameraRotation = camera.rotation;
            }
            if (!playShowcase.library.takes[recordedCameraTake].lockCamera) return;
            recordedLockedFrames++;
            recordedCameraDrift = Mathf.Max(recordedCameraDrift,
                Vector3.Distance(recordedCameraPosition, camera.position));
            if (recordedCameraDrift > .0001f || Quaternion.Angle(recordedCameraRotation, camera.rotation) > .001f)
                throw new InvalidOperationException("Final camera moved in the actual recorded frame.");
        }

        static void WriteRecordedCameraReport()
        {
            if (!recordingComplete) return;
            File.WriteAllText(PepeShowcaseSetup.Output +
                (recordingBom ? "/BomRecordedCamera.txt" : "/CameraVfxRecordedCamera.txt"),
                (recordingBom && playShowcase.director.cinematicAttackReference ?
                    "PASS directed attack camera; fitted dolly and smooth transition to the fixed blink shot.\n" :
                    recordingBom ? "PASS closer follow camera: " + playShowcase.director.FollowDistance.ToString("F3") +
                    "m; smooth 650ms lead-in to locked blink shot: " +
                    playShowcase.director.SafetyDistance.ToString("F3") + "m.\n" :
                    "PASS fixed sequence camera distance across all recorded frames: " +
                    playShowcase.director.SequenceDistance.ToString("F3") + "m.\n") +
                "PASS final static shot: " + recordedLockedFrames + " rendered frames, maximum camera drift=" +
                recordedCameraDrift.ToString("F6") + "m.\n");
            if (recordingBom) WriteRecordedBomIntro();
            if (recordingBom && playShowcase.director.cinematicAttackReference)
                File.AppendAllText(PepeShowcaseSetup.Output + "/BomRecordedCamera.txt",
                    "PASS cinematic reference camera: " + referenceFrames + " rendered action frames; range=" +
                    playShowcase.director.ReferenceNearest.ToString("F3") + ".." +
                    playShowcase.director.ReferenceFarthest.ToString("F3") + "m; maximum step=" +
                    maximumReferenceStep.ToString("F4") + "m/frame; maximum turn=" +
                    maximumReferenceTurn.ToString("F3") + "deg/frame.\n");
        }
    }
}
