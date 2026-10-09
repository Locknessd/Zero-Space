using UnityEngine.Playables;

namespace FrankRetarget
{
    public sealed partial class FrankTestActor
    {
        ReactionSamplingPose baseSourcePose;

        void CaptureBaseSamplingPose()
        {
            // Capture configured scale, socket and skeleton transforms before the
            // first evaluation can feed native root motion back into the source.
            baseSourcePose = new ReactionSamplingPose(activeDriver.transform, true);
        }

        void EvaluateBaseGraph(double time)
        {
            // Match the attack/reaction absolute sampling policy: root motion is
            // a delta from the previous sample, so establish its zero-time origin
            // before restoring the configured source and seeking the native clip.
            // Restore before the requested evaluation to retain authored travel.
            playable.SetTime(0);
            graph.Evaluate(0);
            baseSourcePose.Restore();
            playable.SetTime(time);
            graph.Evaluate(0);
        }

        void ClearBaseSamplingPose()
        {
            // Clear also owns cancellation and every driver reconfiguration.
            baseSourcePose = null;
        }
    }
}
