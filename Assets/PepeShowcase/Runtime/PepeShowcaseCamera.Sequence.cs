using UnityEngine;

namespace ZeroSpace.Showcase
{
    public sealed partial class PepeShowcaseCamera
    {
        Vector3?[] lockedShotFocus;

        public void ResetSequencePlanning(int takes)
        {
            SequenceDistance = 0;
            lockedShotFocus = new Vector3?[takes];
            ResetBomDistances(takes);
            ResetReferenceShots(takes);
        }

        Vector3 PlanLockedFocus(PepeAttackShowcase showcase, Vector3[] raw, Bounds[] frames)
        {
            if (!showcase.library.takes[showcase.TakeIndex].lockCamera) return raw[0];
            var trajectory = frames[0];
            foreach (var frame in frames) trajectory.Encapsulate(frame);
            // Compose the entire travel once, rather than anchoring to one end of the movement.
            var focus = trajectory.center;
            if (lockedShotFocus != null) lockedShotFocus[showcase.TakeIndex] = focus;
            return focus;
        }

        Vector3 PrepareNextLockedShot(PepeAttackShowcase showcase, Vector3 focus, float seconds)
        {
            int next = showcase.TakeIndex + 1;
            if (!showcase.library.continuous || lockedShotFocus == null || next >= lockedShotFocus.Length ||
                !lockedShotFocus[next].HasValue || showcase.library.takes[showcase.TakeIndex].lockCamera)
                return focus;
            float duration = showcase.library.takes[showcase.TakeIndex].Duration;
            float blend = Mathf.SmoothStep(0, 1, Mathf.Clamp01((seconds - duration + .65f) / .65f));
            return Vector3.Lerp(focus, lockedShotFocus[next].Value, blend);
        }
    }
}
