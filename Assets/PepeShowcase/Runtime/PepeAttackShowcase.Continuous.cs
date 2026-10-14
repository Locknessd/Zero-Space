using UnityEngine;

namespace ZeroSpace.Showcase
{
    public sealed partial class PepeAttackShowcase
    {
        sealed class EntryPose
        {
            public Vector3[] positions;
            public Quaternion[] rotations;
            public Vector3 hips;
        }

        EntryPose[] continuousEnds;

        // Cache boundaries once per character so arbitrary seeking carries the same authored travel.
        void PrepareContinuousSequence()
        {
            EndTrailerIntro();
            continuousEnds = null;
            director.ResetSequencePlanning(library.takes.Length);
            if (!library.continuous) return;
            continuousEnds = new EntryPose[library.takes.Length];
            for (int i = 0; i < library.takes.Length; i++)
            {
                BeginTake(i);
                SampleAttackPose(library.takes[i].Duration);
                var frame = new EntryPose
                {
                    positions = new Vector3[savedBones.Length],
                    rotations = new Quaternion[savedBones.Length],
                    hips = character.GetBoneTransform(HumanBodyBones.Hips).position
                };
                for (int bone = 0; bone < savedBones.Length; bone++)
                {
                    frame.positions[bone] = savedBones[bone].localPosition;
                    frame.rotations[bone] = savedBones[bone].localRotation;
                }
                continuousEnds[i] = frame;
            }
            // Fit the preceding shot's smooth lead-in after the final static composition is known.
            float sequenceDistance = 0;
            for (int i = 0; i < library.takes.Length; i++)
            {
                BeginTake(i);
                sequenceDistance = Mathf.Max(sequenceDistance, director.TakeDistance);
            }
            director.SequenceDistance = sequenceDistance;
            director.FinishBomDistances(this);
            TakeIndex = -1;
        }

        void AlignContinuousTake(int index)
        {
            if (!library.continuous || index == 0 || continuousEnds?[index - 1] == null) return;
            actor.Evaluate(library.takes[index].sourceStartSeconds);
            Vector3 offset = continuousEnds[index - 1].hips - actor.Pose.targetHips.position;
            offset.y = 0;
            actor.transform.position += offset;
        }

        void BlendContinuousEntry(float seconds)
        {
            if (!library.continuous || TakeIndex <= 0 || continuousEnds?[TakeIndex - 1] == null ||
                seconds >= library.entryBlendSeconds) return;
            var frame = continuousEnds[TakeIndex - 1];
            float weight = Mathf.SmoothStep(0, 1, Mathf.Clamp01(seconds / library.entryBlendSeconds));
            for (int i = 0; i < savedBones.Length; i++)
            {
                var bone = savedBones[i];
                if (!bone || bone == character.transform) continue;
                bone.localPosition = Vector3.Lerp(frame.positions[i], bone.localPosition, weight);
                bone.localRotation = Quaternion.Slerp(frame.rotations[i], bone.localRotation, weight);
            }
        }
    }
}
