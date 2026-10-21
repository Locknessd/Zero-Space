using System;
using UnityEngine;

namespace MemeArena
{
    [DisallowMultipleComponent]
    public sealed class MemeArenaFloorFracture : MonoBehaviour
    {
        [Serializable]
        public sealed class Slot
        {
            public GameObject root;
            public MeshRenderer scar;
            public ParticleSystem fragments;
            [NonSerialized] public float born;
        }

        public BattleVfxPlayer battleVfx;
        public MemeArenaStage stage;
        public Slot[] pool = Array.Empty<Slot>();
        public bool effectsEnabled = true;
        [Range(.5f, 3)] public float radius = 1.5f;
        [Range(1, 10)] public float holdSeconds = 4;
        [Range(.2f, 3)] public float fadeSeconds = 1.25f;
        MaterialPropertyBlock block;
        BattleVfxPlayer subscribed;
        int next;
        ulong lastEvent;
        bool hasEvent;
        static readonly int Opacity = Shader.PropertyToID("_Opacity");
        public int PlayedCount { get; private set; }
        public Vector3 LastPosition { get; private set; }
        public ulong LastEventId { get; private set; }
        public bool IsBound => subscribed;
        public int ActiveCount
        {
            get
            {
                int count = 0;
                foreach (var slot in pool) if (slot.root && slot.root.activeSelf) count++;
                return count;
            }
        }

        void OnEnable()
        {
            block ??= new MaterialPropertyBlock();
            if (!stage) stage = GetComponent<MemeArenaStage>();
            InvokeRepeating(nameof(TryBind), 0, .25f);
        }

        void TryBind()
        {
            if (!battleVfx && GameManager.Instance) battleVfx = GameManager.Instance.battleVfx;
            if (!battleVfx || subscribed) return;
            subscribed = battleVfx;
            subscribed.ContactOccurred += OnContact;
            subscribed.EffectsCleared += Clear;
            CancelInvoke(nameof(TryBind));
        }

        void OnDisable()
        {
            CancelInvoke(nameof(TryBind));
            if (subscribed)
            {
                subscribed.ContactOccurred -= OnContact;
                subscribed.EffectsCleared -= Clear;
            }
            subscribed = null;
            Clear();
        }

        void OnContact(BattleVfxPlayer.Impact impact)
        {
            if (!effectsEnabled || impact.kind != BattleVfxPlayer.ContactKind.Ground || pool.Length == 0) return;
            if (impact.eventId != 0 && hasEvent && impact.eventId == lastEvent) return;
            if (stage)
            {
                var bounds = stage.CombatBounds;
                if (impact.position.x < bounds.min.x || impact.position.x > bounds.max.x ||
                    impact.position.z < bounds.min.z || impact.position.z > bounds.max.z) return;
            }
            hasEvent = true;
            lastEvent = impact.eventId;
            LastEventId = impact.eventId;
            var slot = pool[next];
            next = (next + 1) % pool.Length;
            if (!slot.root || !slot.scar || !slot.fragments) return;
            float strength = impact.finishing ? 1.3f : impact.cue != null && impact.cue.damageOnLanding ? 1.15f : 1;
            Vector3 position = impact.position;
            position.y = (subscribed ? subscribed.groundHeight : transform.position.y) + .018f;
            slot.root.transform.SetPositionAndRotation(position,
                Quaternion.Euler(0, Mathf.Repeat(PlayedCount * 137.508f, 360), 0));
            slot.scar.transform.localScale = Vector3.one * radius * strength;
            slot.born = Time.time;
            block.SetFloat(Opacity, 1);
            slot.scar.SetPropertyBlock(block);
            slot.root.SetActive(true);
            slot.fragments.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            slot.fragments.Play(true);
            LastPosition = position;
            PlayedCount++;
        }

        void Update()
        {
            foreach (var slot in pool)
            {
                if (!slot.root || !slot.root.activeSelf) continue;
                float age = Time.time - slot.born;
                if (age >= holdSeconds + fadeSeconds)
                {
                    slot.root.SetActive(false);
                    continue;
                }
                block.SetFloat(Opacity, 1 - Mathf.Clamp01((age - holdSeconds) / fadeSeconds));
                slot.scar.SetPropertyBlock(block);
            }
        }

        public void Clear()
        {
            hasEvent = false;
            foreach (var slot in pool)
            {
                if (slot.fragments) slot.fragments.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (slot.root) slot.root.SetActive(false);
            }
        }
    }
}
