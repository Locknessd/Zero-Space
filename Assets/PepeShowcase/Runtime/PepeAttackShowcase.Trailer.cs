using UnityEngine;

namespace ZeroSpace.Showcase
{
    public sealed partial class PepeAttackShowcase
    {
        BomTrailerIntro trailerIntro;

        bool EvaluateTrailerIntro()
        {
            if (!library.bomTrailer) return false;
            if (SequenceTime >= library.introSeconds)
            {
                EndTrailerIntro();
                return false;
            }
            if (!trailerIntro) trailerIntro = GetComponent<BomTrailerIntro>();
            if (!trailerIntro.IsActive)
            {
                if (TakeIndex != 0) BeginTake(0);
                actor.activeDriver.gameObject.SetActive(false);
                RestoreCharacterPose();
                trailerIntro.Begin(this);
            }
            AttackTime = 0;
            trailerIntro.Evaluate(SequenceTime);
            return true;
        }

        void EndTrailerIntro()
        {
            if (!trailerIntro || !trailerIntro.IsActive) return;
            trailerIntro.End();
            RestoreCharacterPose();
            TakeIndex = -1;
        }
    }
}
