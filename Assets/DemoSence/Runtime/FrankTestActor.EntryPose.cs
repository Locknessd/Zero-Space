using UnityEngine;

namespace FrankRetarget
{
    public sealed partial class FrankTestActor
    {
        Transform[] entryBones;
        Vector3[] entryPositions;
        Quaternion[] entryRotations;
        float entrySeconds;

        public void PrepareEntryBlend(float seconds)
        {
            if (!character || seconds <= 0) return;
            entrySeconds = seconds;
            entryBones = character.GetComponentsInChildren<Transform>(true);
            entryPositions = new Vector3[entryBones.Length];
            entryRotations = new Quaternion[entryBones.Length];
            for (int i = 0; i < entryBones.Length; i++)
            {
                entryPositions[i] = entryBones[i].localPosition;
                entryRotations[i] = entryBones[i].localRotation;
            }
        }

        void BlendEntryPose(float seconds)
        {
            if (entryBones == null || seconds >= entrySeconds) return;
            float weight = Mathf.SmoothStep(0, 1, seconds / entrySeconds);
            for (int i = 0; i < entryBones.Length; i++)
            {
                var bone = entryBones[i];
                if (!bone || bone == character.transform) continue;
                bone.localPosition = Vector3.Lerp(entryPositions[i], bone.localPosition, weight);
                bone.localRotation = Quaternion.Slerp(entryRotations[i], bone.localRotation, weight);
            }
        }
    }
}
