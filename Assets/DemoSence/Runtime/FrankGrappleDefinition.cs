using System;
using UnityEngine;

namespace FrankRetarget
{
    public enum FrankGrappleOutcome { Release, Throw, Escape }

    [CreateAssetMenu(menuName = "Battle/Grapple Definition")]
    public sealed class FrankGrappleDefinition : ScriptableObject
    {
        [Serializable]
        public sealed class Continuation
        {
            public AnimationClip attack, reaction, getUp;
            public int unarmedIndex;
            public BattleSfxBank.Move presentation;
            public bool Valid => attack && reaction && presentation != null;
            public float Duration => Mathf.Max(attack.length, reaction.length);
        }

        public Vector2 escapeWindowSeconds = new Vector2(.15f, .55f);
        public float decisionSeconds = .65f;
        public float blendSeconds = .12f;
        public float entryBlendSeconds = .12f;
        public BattleSfxBank.Move releasePresentation;
        public Continuation throwing, escaping;

        public bool Valid => float.IsFinite(decisionSeconds) && float.IsFinite(blendSeconds) &&
            float.IsFinite(escapeWindowSeconds.x) && float.IsFinite(escapeWindowSeconds.y) &&
            escapeWindowSeconds.x >= 0 && escapeWindowSeconds.y > escapeWindowSeconds.x &&
            decisionSeconds > escapeWindowSeconds.y && blendSeconds > 0 && blendSeconds <= .2f &&
            float.IsFinite(entryBlendSeconds) && entryBlendSeconds > 0 && entryBlendSeconds <= escapeWindowSeconds.x &&
            throwing != null && throwing.Valid && escaping != null && escaping.Valid &&
            releasePresentation != null;

        public Continuation For(FrankGrappleOutcome outcome) => outcome == FrankGrappleOutcome.Throw
            ? throwing : outcome == FrankGrappleOutcome.Escape ? escaping : null;

        public BattleSfxBank.Move Presentation(FrankGrappleOutcome outcome) =>
            For(outcome)?.presentation ?? releasePresentation;
    }
}
