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

        static void ResetRecordedCameraCheck()
        {
            recordedCameraTake = -1;
            recordedCameraDrift = 0;
            recordedLockedFrames = 0;
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
                (recordingBom ? "PASS closer follow camera: " + playShowcase.director.FollowDistance.ToString("F3") +
                    "m; smooth 650ms lead-in to locked blink shot: " +
                    playShowcase.director.SafetyDistance.ToString("F3") + "m.\n" :
                    "PASS fixed sequence camera distance across all recorded frames: " +
                    playShowcase.director.SequenceDistance.ToString("F3") + "m.\n") +
                "PASS final static shot: " + recordedLockedFrames + " rendered frames, maximum camera drift=" +
                recordedCameraDrift.ToString("F6") + "m.\n");
            if (recordingBom) WriteRecordedBomIntro();
        }
    }
}
