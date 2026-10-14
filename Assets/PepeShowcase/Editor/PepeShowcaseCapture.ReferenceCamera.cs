using System;
using System.Text;
using UnityEngine;
using ZeroSpace.Showcase;

namespace FrankRetarget.Editor
{
    public static partial class PepeShowcaseCapture
    {
        static void ValidateReferenceCamera(PepeAttackShowcase showcase, StringBuilder report)
        {
            var library = showcase.library;
            var director = showcase.director;
            if (!director.ReferenceReady) throw new InvalidOperationException("Reference camera was not prepared.");
            float maximumStep = 0;
            float maximumTurn = 0;
            float minimumDistance = float.MaxValue;
            float maximumDistance = 0;
            for (int take = 0; take < library.takes.Length; take++)
            {
                Vector3 previous = default;
                Quaternion previousRotation = default;
                var entry = library.takes[take];
                int frames = Mathf.CeilToInt(entry.Duration * 60);
                for (int frame = 0; frame <= frames; frame++)
                {
                    float time = library.TakeStart(take) + Mathf.Min(frame / 60f, entry.Duration - .0001f);
                    showcase.Evaluate(time, 1f / 60, false);
                    var camera = director.output;
                    float distance = director.SafetyDistance;
                    if (!float.IsFinite(distance) || distance <= camera.nearClipPlane)
                        throw new InvalidOperationException("Invalid reference camera distance.");
                    minimumDistance = Mathf.Min(minimumDistance, distance);
                    maximumDistance = Mathf.Max(maximumDistance, distance);
                    if (frame > 0)
                    {
                        maximumStep = Mathf.Max(maximumStep,
                            Vector3.Distance(previous, camera.transform.position));
                        maximumTurn = Mathf.Max(maximumTurn,
                            Quaternion.Angle(previousRotation, camera.transform.rotation));
                    }
                    previous = camera.transform.position;
                    previousRotation = camera.transform.rotation;
                    foreach (var bone in new[] { HumanBodyBones.Head, HumanBodyBones.Hips,
                        HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
                        HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                    {
                        var viewport = camera.WorldToViewportPoint(showcase.character.GetBoneTransform(bone).position);
                        if (viewport.z <= 0 || viewport.x < .02f || viewport.x > .98f ||
                            viewport.y < .02f || viewport.y > .98f)
                            throw new InvalidOperationException("Reference camera clips " + bone + " in " + entry.title);
                    }
                }
                float seekTime = library.TakeStart(take) + entry.Duration * .45f;
                showcase.Evaluate(seekTime, 0, false);
                previous = director.output.transform.position;
                previousRotation = director.output.transform.rotation;
                showcase.Evaluate(library.TakeStart(0), 0, false);
                showcase.Evaluate(seekTime, 0, false);
                if (Vector3.Distance(previous, director.output.transform.position) > .0001f ||
                    Quaternion.Angle(previousRotation, director.output.transform.rotation) > .001f)
                    throw new InvalidOperationException("Reference camera depends on seek order.");
            }
            if (maximumStep > .65f || maximumTurn > 4 || maximumDistance - minimumDistance < .25f)
                throw new InvalidOperationException("Reference camera has abrupt motion or no dolly: " +
                    maximumStep + "m/frame, " + maximumTurn + "deg/frame.");
            report.AppendLine("PASS " + showcase.CharacterName + " cinematic reference camera: framing, dolly, " +
                "smooth motion and deterministic seeking; distance=" + minimumDistance.ToString("F3") + ".." +
                maximumDistance.ToString("F3") + "m; max step=" + maximumStep.ToString("F4") +
                "m/frame; max turn=" + maximumTurn.ToString("F3") + "deg/frame.");
        }
    }
}
