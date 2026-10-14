using System;
using System.Text;
using UnityEngine;
using ZeroSpace.Showcase;

namespace FrankRetarget.Editor
{
    public static partial class PepeShowcaseCapture
    {
        static void ValidateBomMuzzle(PepeAttackShowcase showcase, int index, StringBuilder report)
        {
            var take = showcase.library.takes[index];
            int shots = 0;
            foreach (var cue in take.cues)
            {
                if (!cue.gun) continue;
                showcase.PlayTake(index);
                float start = showcase.library.TakeStart(index) + cue.seconds;
                showcase.Evaluate(start - .001f, 0, false);
                if (showcase.presentation.IsMuzzleCueVisible(cue))
                    throw new InvalidOperationException("Muzzle explosion fired before its shot cue.");
                showcase.Evaluate(start + 1f / 60, 1f / 60, false);
                if (!showcase.presentation.IsMuzzleCueVisible(cue) ||
                    showcase.presentation.ActiveMuzzleParticleCount == 0)
                    throw new InvalidOperationException("Missing muzzle explosion on the firing frame.");
                if (!PepeShowcasePresentation.TryMuzzlePose(showcase.Actor, out var gun,
                    out var position, out var direction) || Vector3.Distance(position,
                    showcase.presentation.RenderedMuzzlePosition) > .01f)
                    throw new InvalidOperationException("The explosion is not attached to the actual barrel tip.");
                if (direction.sqrMagnitude < .9f)
                    throw new InvalidOperationException("Invalid firing direction.");
                showcase.Evaluate(start + .2f, .2f, false);
                if (!showcase.presentation.IsMuzzleCueVisible(cue))
                    throw new InvalidOperationException("Missing muzzle sparks and smoke tail.");
                showcase.Evaluate(start + .4f, .2f, false);
                if (showcase.presentation.IsMuzzleCueVisible(cue))
                    throw new InvalidOperationException("Muzzle explosion did not fade out.");
                shots++;
            }
            if (shots > 0)
                report.AppendLine("PASS " + showcase.CharacterName + " / " + take.title + ": " + shots +
                    " muzzle explosions; no early flashes; barrel error <1cm; sparks/smoke fade correctly.");
        }
    }
}
