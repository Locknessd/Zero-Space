using System;
using System.IO;
using UnityEngine;
using ZeroSpace.Showcase;

namespace FrankRetarget.Editor
{
    public static partial class PepeShowcaseCapture
    {
        static int recordedFreezeFrames;
        static Vector3 recordedFreezeHead;
        static Vector3 recordedFreezeHand;
        static Vector3 recordedFreezeCamera;
        static Quaternion recordedFreezeRotation;
        static float recordedFreezeDrift;

        static void ResetRecordedBomIntro()
        {
            recordedFreezeFrames = 0;
            recordedFreezeDrift = 0;
        }

        static void CheckRecordedBomIntro()
        {
            float seconds = playShowcase.SequenceTime;
            if (seconds < BomTrailerIntro.FreezeStart + .3f || seconds >= BomTrailerIntro.FadeStart) return;
            var head = playShowcase.character.GetBoneTransform(HumanBodyBones.Head).position;
            var hand = playShowcase.character.GetBoneTransform(HumanBodyBones.RightHand).position;
            var camera = playShowcase.director.output.transform;
            if (recordedFreezeFrames == 0)
            {
                recordedFreezeHead = head;
                recordedFreezeHand = hand;
                recordedFreezeCamera = camera.position;
                recordedFreezeRotation = camera.rotation;
            }
            recordedFreezeFrames++;
            recordedFreezeDrift = Mathf.Max(recordedFreezeDrift, Vector3.Distance(recordedFreezeHead, head),
                Vector3.Distance(recordedFreezeHand, hand), Vector3.Distance(recordedFreezeCamera, camera.position));
            if (recordedFreezeDrift > .001f || Quaternion.Angle(recordedFreezeRotation, camera.rotation) > .001f)
                throw new InvalidOperationException("Pepe freeze changed in the actual recorded intro.");
        }

        static void WriteRecordedBomIntro()
        {
            if (recordedFreezeFrames < 200)
                throw new InvalidOperationException("The recorded Pepe introduction did not contain its frozen reveal.");
            File.AppendAllText(PepeShowcaseSetup.Output + "/BomRecordedCamera.txt",
                "PASS Pepe freeze: " + recordedFreezeFrames + " rendered frames, pose/camera drift=" +
                recordedFreezeDrift.ToString("F6") + "m.\n");
        }
    }
}
