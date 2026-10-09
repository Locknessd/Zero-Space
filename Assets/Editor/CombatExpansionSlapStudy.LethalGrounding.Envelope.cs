using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapStudy
    {
        static float[] LgEnvelope(float[] raw, FrankPairGrounding.Track original, LgPhase phase)
        {
            int count = raw.Length;
            float dt = original.duration / (count - 1);
            int radius = Mathf.CeilToInt(.05f / dt);
            var need = raw.Select(y => Mathf.Max(0, GroundingCushion - y)).ToArray();
            var dilated = new float[count];
            var result = new float[count];
            // Dilation over twice the smoothing radius keeps two box convolutions above measured need.
            // This is an offline envelope, not synthetic poses or fabricated clearance measurements.
            for (int i = 0; i < count; i++)
                for (int j = Mathf.Max(0, i - 2 * radius); j <= Mathf.Min(count - 1, i + 2 * radius); j++)
                    dilated[i] = Mathf.Max(dilated[i], need[j]);
            var smooth = LgBoxFilter(LgBoxFilter(dilated, radius), radius);
            for (int i = 0; i < count; i++)
            {
                float seconds = dt * i;
                float u = Mathf.Clamp01((seconds - phase.transitionStart) /
                    (phase.transitionEnd - phase.transitionStart));
                float blend = u * u * (3 - 2 * u);
                result[i] = Mathf.Lerp(original.lift[i], smooth[i], blend);
            }
            return result;
        }

        static float[] LgBoxFilter(float[] source, int radius)
        {
            var result = new float[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                double sum = 0;
                for (int j = -radius; j <= radius; j++)
                    sum += source[Mathf.Clamp(i + j, 0, source.Length - 1)];
                result[i] = (float)(sum / (2 * radius + 1));
            }
            return result;
        }

        static LgTrack LgDescribeTrack(FrankPairGrounding.Track track, FrankPairGrounding.Track original,
            float[] raw, LgPhase phase, string fighter)
        {
            var result = new LgTrack
            {
                sequence = phase.sequence,
                fighter = fighter,
                avatar = CombatExpansionInventory.Identity(track.avatar),
                receiver = track.receiver,
                samples = raw.Length,
                duration = track.duration,
                minimumRawY = raw.Min(),
                requiredMaximum = Mathf.Max(0, GroundingCushion - raw.Min()),
                maximumLift = track.lift.Max()
            };
            float dt = track.duration / (track.lift.Length - 1);
            result.minimumRawSeconds = Array.IndexOf(raw, result.minimumRawY) * dt;
            result.maximumLiftSeconds = Array.IndexOf(track.lift, result.maximumLift) * dt;
            for (int i = 0; i < raw.Length; i++)
            {
                float seconds = i * dt;
                float lift = track.lift[i];
                float delta = lift - original.lift[i];
                if (!float.IsFinite(lift) || lift < 0 || lift > (track.receiver ? LgMaximumLift : .2f))
                    throw new InvalidOperationException("Invalid or excessive lethal lift: " + fighter);
                if (raw[i] + lift < GroundingMinimumClearance)
                {
                    result.floorViolations++;
                    if (seconds <= phase.preserve)
                        result.prefixViolations++;
                }
                if (seconds <= phase.transitionStart)
                    result.maximumPrefixLiftError = Mathf.Max(result.maximumPrefixLiftError, Mathf.Abs(delta));
                if (i > 0)
                {
                    float priorDelta = track.lift[i - 1] - original.lift[i - 1];
                    result.maximumDeltaSpeed = Mathf.Max(result.maximumDeltaSpeed, Mathf.Abs(delta - priorDelta) / dt);
                    result.maximumLiftSpeed = Mathf.Max(result.maximumLiftSpeed,
                        Mathf.Abs(lift - track.lift[i - 1]) / dt);
                    if (track.receiver && seconds > phase.transitionStart)
                        result.maximumAuthoredSpeed = Mathf.Max(result.maximumAuthoredSpeed,
                            Mathf.Abs(lift - track.lift[i - 1]) / dt);
                    if (i > 1)
                    {
                        float earlierDelta = track.lift[i - 2] - original.lift[i - 2];
                        if (track.receiver && seconds > phase.transitionStart)
                            result.maximumAuthoredAcceleration = Mathf.Max(result.maximumAuthoredAcceleration,
                                Mathf.Abs(lift - 2 * track.lift[i - 1] + track.lift[i - 2]) / (dt * dt));
                        result.maximumDeltaAcceleration = Mathf.Max(result.maximumDeltaAcceleration,
                            Mathf.Abs(delta - 2 * priorDelta + earlierDelta) / (dt * dt));
                        result.maximumLiftAcceleration = Mathf.Max(result.maximumLiftAcceleration,
                            Mathf.Abs(lift - 2 * track.lift[i - 1] + track.lift[i - 2]) / (dt * dt));
                    }
                }
            }
            if (result.maximumPrefixLiftError > .000001f)
                throw new InvalidOperationException("Native contact-prefix lift changed.");
            if (result.maximumLift > .2f && (!track.receiver || result.requiredMaximum <= .2f ||
                result.maximumLift > Mathf.Max(.2f, result.requiredMaximum) + .000001f))
                throw new InvalidOperationException("Lift above 0.2m lacks measured raw-clearance justification.");
            result.justification = result.maximumLift <= .2f ? "Within existing 0.2m limit." :
                FormattableString.Invariant($"Measured worst raw body Y={result.minimumRawY:R}m at ") +
                FormattableString.Invariant($"{result.minimumRawSeconds:R}s across BOTH directions; ") +
                FormattableString.Invariant($"raw need plus 10mm cushion={result.requiredMaximum:R}m; ") +
                "stored maximum never exceeds that measured need; absolute cap 0.30m. " +
                "A larger clearance correction is provisional and still requires support/choreography review.";
            return result;
        }

        static void LgWriteTrack(string stem, int role, FrankPairGrounding.Track track,
            FrankPairGrounding.Track original, float[] raw, LgPhase phase)
        {
            var csv = new StringBuilder("seconds,minimumRawYBothDirections,nativeLift,storedLift," +
                "addedCorrection,predictedBodyY,preservedPrefix\n");
            for (int i = 0; i < raw.Length; i++)
            {
                float time = track.duration * i / (raw.Length - 1);
                csv.AppendLine(FormattableString.Invariant(
                    $"{time:R},{raw[i]:R},{original.lift[i]:R},{track.lift[i]:R},") +
                    FormattableString.Invariant($"{track.lift[i] - original.lift[i]:R},") +
                    FormattableString.Invariant($"{raw[i] + track.lift[i]:R},{(time <= phase.preserve ? 1 : 0)}"));
            }
            File.WriteAllText(LgOutput + "/" + stem + "_" + GroundingRole(role) + ".Track.csv", csv.ToString());
        }
    }
}
