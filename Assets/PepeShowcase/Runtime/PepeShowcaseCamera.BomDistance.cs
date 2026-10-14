using UnityEngine;

namespace ZeroSpace.Showcase
{
    public sealed partial class PepeShowcaseCamera
    {
        float[] bomTakeDistances;
        bool bomDistanceReady;
        float bomShotDistance;
        public float FollowDistance { get; private set; }

        void ResetBomDistances(int takes)
        {
            bomDistanceReady = false;
            bomTakeDistances = new float[takes];
            FollowDistance = 0;
        }

        void CacheBomDistance(PepeAttackShowcase showcase)
        {
            if (!showcase.library.bomTrailer || bomTakeDistances == null) return;
            bomTakeDistances[showcase.TakeIndex] = distance;
        }

        public void FinishBomDistances(PepeAttackShowcase showcase)
        {
            if (!showcase.library.bomTrailer) return;
            // Keep one steady closer distance for the following shots.
            FollowDistance = 9.6f;
            for (int index = 0; index < bomTakeDistances.Length; index++)
                if (!showcase.library.takes[index].lockCamera)
                    FollowDistance = Mathf.Max(FollowDistance, bomTakeDistances[index]);
            bomShotDistance = FollowDistance;
            bomDistanceReady = true;
            FinishReferenceShots(showcase);
        }

        public float PlannedDistance(PepeAttackShowcase showcase)
        {
            if (UsesReference(showcase) && ReferenceReady) return ReferenceDistance(showcase);
            if (!bomDistanceReady) return Mathf.Max(distance, SequenceDistance);
            int index = showcase.TakeIndex;
            var takes = showcase.library.takes;
            if (takes[index].lockCamera) return Mathf.Max(FollowDistance, bomTakeDistances[index]);
            int next = index + 1;
            if (next >= takes.Length || !takes[next].lockCamera) return FollowDistance;
            float blend = Mathf.SmoothStep(0, 1,
                Mathf.Clamp01((showcase.AttackTime - takes[index].Duration + .65f) / .65f));
            return Mathf.Lerp(FollowDistance, Mathf.Max(FollowDistance, bomTakeDistances[next]), blend);
        }
    }
}
