using System;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionKbComboStudy
    {
        static FrankPairGrounding BakeHighReactionGrounding(FrankBattlePairPlayback pair,
            CharacterCombat source, CharacterCombat target, StringBuilder report)
        {
            var actors = new[] { source, target };
            int intervals = Mathf.CeilToInt(pair.Duration * 240);
            var tracks = actors.Select((actor, index) => new FrankPairGrounding.Track
            {
                avatar = actor.Animator.avatar,
                receiver = index == 1,
                duration = pair.Duration,
                lift = new float[intervals + 1]
            }).ToArray();
            for (int frame = 0; frame <= intervals; frame++)
            {
                pair.EvaluateAt(pair.Duration * frame / intervals);
                for (int role = 0; role < 2; role++)
                {
                    float minimum = BattlePresentationContactSetup.MeasureGroundClearance(actors[role]);
                    if (!float.IsFinite(minimum))
                        throw new InvalidOperationException("Nonfinite high-reaction body clearance.");
                    tracks[role].lift[frame] = Mathf.Max(0, .01f - minimum);
                }
            }
            foreach (var track in tracks)
            {
                var raw = track.lift;
                var envelope = new float[raw.Length];
                for (int frame = 0; frame <= intervals; frame++)
                    envelope[frame] = Mathf.Max(raw[frame], raw[Mathf.Max(0, frame - 1)],
                        raw[Mathf.Min(intervals, frame + 1)]);
                if (envelope.Max() > .2f)
                    throw new InvalidOperationException("High-reaction floor adaptation exceeds 0.2m.");
                track.lift = envelope;
                report.AppendLine(FormattableString.Invariant(
                    $"240Hz transient grounding avatar={track.avatar.name}; receiver={track.receiver}; ") +
                    FormattableString.Invariant($"maxLift={envelope.Max():R}m."));
            }
            var result = ScriptableObject.CreateInstance<FrankPairGrounding>();
            result.hideFlags = HideFlags.HideAndDontSave;
            result.tracks = tracks;
            return result;
        }
    }
}
