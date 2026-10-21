using UnityEngine;

namespace MemeArena
{
    public sealed class MemeCrowdFan : MonoBehaviour
    {
        [Min(0)] public float bounceHeight = .045f;
        [Min(.1f)] public float speed = 2.5f;
        [Range(0, 30)] public float swayDegrees = 4;
        public float phase;
        public Animator animator;
        Vector3 origin;
        Quaternion rotation;
        Vector3 scale;
        bool cached;
        float reactionStart;
        float reactionEnd;
        float strength;
        MemeCrowdReaction reaction;
        bool awaitingIdle;

        void Awake() => CachePose();

        public void CachePose()
        {
            if (cached) return;
            origin = transform.localPosition;
            rotation = transform.localRotation;
            scale = transform.localScale;
            cached = true;
            if (!animator) animator = GetComponentInChildren<Animator>();
        }

        public void Cheer(float intensity = 2, float duration = 1.5f) =>
            React(MemeCrowdReaction.Cheer, intensity, duration, Time.time);

        public void React(MemeCrowdReaction type, float intensity, float duration, float now)
        {
            CachePose();
            reaction = type;
            strength = Mathf.Clamp(intensity, 0, 3);
            reactionStart = now + Mathf.Repeat(phase * .173f, .22f);
            reactionEnd = reactionStart + duration;
            awaitingIdle = type != MemeCrowdReaction.Idle;
            PlayState(type);
        }

        void PlayState(MemeCrowdReaction type)
        {
            if (animator && animator.isActiveAndEnabled && animator.runtimeAnimatorController)
            {
                int state = Animator.StringToHash("Base Layer." + type);
                if (animator.HasState(0, state)) animator.CrossFade(state, .1f);
            }
        }

        public void Tick(float now, MemeCrowdSettings settings)
        {
            CachePose();
            if (awaitingIdle && now >= reactionEnd)
            {
                awaitingIdle = false;
                PlayState(MemeCrowdReaction.Idle);
            }
            float t = now * speed + phase;
            bool active = now >= reactionStart && now < reactionEnd && reaction != MemeCrowdReaction.Idle;
            float bounce = settings ? settings.idleBounce : bounceHeight;
            float lean = 0;
            float squash = 1;
            if (active)
            {
                float envelope = Mathf.Clamp01((reactionEnd - now) * 3);
                float gain = strength * envelope;
                if (reaction == MemeCrowdReaction.Boo || reaction == MemeCrowdReaction.Defeat)
                {
                    lean = Mathf.Sin(t * .8f) * 9 * gain;
                    squash = 1 - .08f * gain;
                    bounce *= .2f;
                }
                else
                {
                    bounce += (settings ? settings.reactionBounce : .3f) * gain;
                    if (reaction == MemeCrowdReaction.Jump) t *= 1.45f;
                    if (reaction == MemeCrowdReaction.Victory) lean = Mathf.Sin(t * 1.8f) * 10 * gain;
                }
            }
            transform.localPosition = origin + Vector3.up * (Mathf.Abs(Mathf.Sin(t)) * bounce);
            transform.localRotation = rotation * Quaternion.Euler(0, 0, Mathf.Sin(t * .7f) * swayDegrees + lean);
            transform.localScale = Vector3.Scale(scale, new Vector3(1, squash, 1));
        }

        public void RestorePose()
        {
            if (!cached) return;
            transform.SetLocalPositionAndRotation(origin, rotation);
            transform.localScale = scale;
        }
    }
}
