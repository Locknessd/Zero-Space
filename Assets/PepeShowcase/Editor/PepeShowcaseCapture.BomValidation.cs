using System;
using System.Text;
using UnityEngine;
using ZeroSpace.Showcase;

namespace FrankRetarget.Editor
{
    public static partial class PepeShowcaseCapture
    {
        static void ValidateBomIntro(PepeAttackShowcase showcase, StringBuilder report)
        {
            var library = showcase.library;
            if (library.introSeconds != 17 || !library.startupAnimation ||
                !library.startupAnimation.isHumanMotion || library.slashPower < 1.5f ||
                library.slashAfterglow < .2f || library.airCutLifetime < .65f)
                throw new InvalidOperationException("BOM intro or enhanced slash configuration is missing.");
            showcase.Restart();
            showcase.Evaluate(BomTrailerIntro.TitleEnd + .3f, 0, false);
            var hand = showcase.character.GetBoneTransform(HumanBodyBones.RightHand).position;
            showcase.Evaluate(BomTrailerIntro.FreezeStart - .2f, 0, false);
            if (Vector3.Distance(hand, showcase.character.GetBoneTransform(HumanBodyBones.RightHand).position) < .04f)
                throw new InvalidOperationException("Pepe startup animation did not move.");
            showcase.Evaluate(BomTrailerIntro.FreezeStart + .4f, 0, false);
            var head = showcase.character.GetBoneTransform(HumanBodyBones.Head).position;
            hand = showcase.character.GetBoneTransform(HumanBodyBones.RightHand).position;
            var camera = showcase.director.output.transform.position;
            for (float seconds = BomTrailerIntro.FreezeStart + .5f; seconds < BomTrailerIntro.FadeStart; seconds += .1f)
            {
                showcase.Evaluate(seconds, .1f, false);
                if (Vector3.Distance(head, showcase.character.GetBoneTransform(HumanBodyBones.Head).position) > .001f ||
                    Vector3.Distance(hand, showcase.character.GetBoneTransform(HumanBodyBones.RightHand).position) > .001f ||
                    Vector3.Distance(camera, showcase.director.output.transform.position) > .001f)
                    throw new InvalidOperationException("The Pepe reveal is not a real frozen pose and camera.");
            }
            report.AppendLine("PASS 17s BOM intro: animated startup, frozen Pepe pose and fixed reveal camera.");
        }

        static void ValidateBomPowerEffects(PepeAttackShowcase showcase, int index, StringBuilder report)
        {
            var take = showcase.library.takes[index];
            int checkedSlashes = 0;
            showcase.PlayTake(index);
            foreach (var cue in take.cues)
            {
                if (!cue.swing) continue;
                showcase.Evaluate(showcase.library.TakeStart(index) + cue.seconds + .08f, 1f / 60, false);
                if (showcase.presentation.ActivePowerRibbonCount == 0)
                    throw new InvalidOperationException("Missing enlarged sword ribbon: " + take.title + " / " + cue.seconds);
                float end = cue.effectEnd > cue.effectStart ? cue.effectEnd : cue.seconds + .15f;
                showcase.Evaluate(showcase.library.TakeStart(index) + Mathf.Min(take.Duration - .001f,
                    end + showcase.library.slashAfterglow * .35f), 1f / 60, false);
                if (showcase.presentation.ActivePowerRibbonCount == 0)
                    throw new InvalidOperationException("Missing lingering slash afterglow: " + take.title);
                showcase.Evaluate(showcase.library.TakeStart(index) + end + .4f, 1f / 60, false);
                if (!showcase.presentation.IsLingeringCueVisible(cue))
                    throw new InvalidOperationException("Missing suspended air cut after sword glow: " + take.title);
                checkedSlashes++;
            }
            report.AppendLine("PASS " + showcase.CharacterName + " / " + take.title + ": " +
                checkedSlashes + " blade ribbons, afterglows and suspended air cuts at +400ms.");
        }
    }
}
