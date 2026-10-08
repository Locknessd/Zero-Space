using UnityEngine;

namespace FrankRetarget
{
    public sealed partial class FrankBattlePairPlayback
    {
        public FrankGrappleOutcome GrappleOutcome { get; private set; }
        public bool EscapeInputAllowed { get; private set; }
        public bool IsGrapple => Move != null && Move.grapple;
        public bool IsGrappleDefender(CharacterCombat fighter) => Playing && IsGrapple && fighter == receiver;
        public bool EscapeWindowOpen => Playing && EscapeInputAllowed && !lethal &&
            GrappleOutcome == FrankGrappleOutcome.Throw &&
            timelineHighWater >= Move.grapple.escapeWindowSeconds.x &&
            timelineHighWater <= Move.grapple.escapeWindowSeconds.y;

        public void AllowLocalEscape(bool allowed)
        {
            EscapeInputAllowed = allowed && IsGrapple && !lethal;
        }

        public bool RequestThrowEscape(CharacterCombat defender, int expectedPlaybackId)
        {
            if (!IsGrapple || defender != receiver || !receiver || receiver.IsDead ||
                receiver.PlaybackId != expectedPlaybackId || !EscapeWindowOpen ||
                Application.isPlaying && Time.timeScale <= 0 || IsContactHeld)
                return false;
            GrappleOutcome = FrankGrappleOutcome.Escape;
            ConfigureGrappleContinuation();
            var profile = PresentationProfile(battleSfx ? battleSfx.bank : battleVfx ? battleVfx.timeline : null);
            if (battleSfx) battleSfx.ReplaceSequence(this, profile, SampleTime);
            if (battleVfx) battleVfx.ReplaceSequence(this, profile, SampleTime);
            BuildContactTimes(profile);
            while (nextContact < contactTimes.Length && contactTimes[nextContact] <= timelineHighWater)
                nextContact++;
            return true;
        }

        public BattleSfxBank.Move PresentationProfile(BattleSfxBank bank) => IsGrapple
            ? Move.grapple.Presentation(GrappleOutcome) : lethalVariant != null
                ? lethalVariant.presentationProfile : bank ? bank.FindMove(Move) : null;

        float MotionDuration => IsGrapple && Move.grapple.For(GrappleOutcome) != null
            ? Move.grapple.decisionSeconds + Move.grapple.For(GrappleOutcome).Duration
            : lethalVariant != null ? lethalVariant.Duration(pair)
            : Mathf.Max(pair.attacks ? pair.attacks.Duration : pair.attack.length, pair.reactions ? pair.reactions.Duration
                : pair.reactionDelay + pair.reaction.length);

        AnimationClip RecoveryClip => IsGrapple
            ? Move.grapple.For(GrappleOutcome)?.getUp : pair.getUp;

        void InitializeGrapple()
        {
            EscapeInputAllowed = false;
            GrappleOutcome = Move.grappleOutcome;
            ConfigureGrappleContinuation();
        }

        void ConfigureGrappleContinuation()
        {
            if (!IsGrapple) return;
            var next = Move.grapple.For(GrappleOutcome);
            attackActor.SetContinuation(next?.attack, Move.grapple.decisionSeconds, Move.grapple.blendSeconds);
            hitActor.SetContinuation(next?.reaction, Move.grapple.decisionSeconds, Move.grapple.blendSeconds);
        }

        Vector3 PairSeparation(float seconds)
        {
            Vector3 entry = pair.spacing.Separation(pair.unarmedIndex, pair.pepeAttacks, seconds);
            if (!IsGrapple) return entry;
            var next = Move.grapple.For(GrappleOutcome);
            if (next == null || seconds < Move.grapple.decisionSeconds) return entry;
            float elapsed = seconds - Move.grapple.decisionSeconds;
            entry = pair.spacing.Separation(pair.unarmedIndex, pair.pepeAttacks, Move.grapple.decisionSeconds);
            var exit = pair.spacing.Separation(next.unarmedIndex, pair.pepeAttacks, elapsed);
            return Vector3.Lerp(entry, exit, Mathf.SmoothStep(0, 1, elapsed / Move.grapple.blendSeconds));
        }
    }
}
