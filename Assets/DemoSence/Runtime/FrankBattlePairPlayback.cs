using System;
using UnityEngine;

namespace FrankRetarget
{
    [Serializable]
    public sealed class FrankBattlePair
    {
        public const int ReceiverFloorSamplesPerSecond = 480;
        public FrankTestDriver attackerDriver, receiverDriver;
        public AnimationClip attack, reaction;
        public FrankReactionTrack reactions;
        public AnimationClip getUp;
        public AnimationClip attackerGetUp;
        [Min(0)] public float entryBlendSeconds;
        [Min(0)] public float recoveryBlendSeconds;
        public FrankPairGrounding recoveryGrounding;
        public bool constrainDepthAfterSpacing;
        [Min(0)] public float maximumAlignmentError;
        public Vector3 receiverOffset;
        public Quaternion receiverRotation = Quaternion.identity;
        public FrankPairSpacing spacing;
        public float bodySpacing;
        public int unarmedIndex = -1;
        public bool pepeAttacks;
        public string cameraKey = "";
        public bool showWeapon;
        public GameObject attackerWeaponPrefab;
        public string attackerWeaponSocket = "";
        public float reactionDelay;
        [HideInInspector] public float[] receiverFloorLift;
        public bool Valid => attackerDriver && receiverDriver && attack && reaction;
    }

    // Uses the demo's actual actor/driver playback, with one clock for both roles.
    [DefaultExecutionOrder(13000)]
    public sealed partial class FrankBattlePairPlayback : MonoBehaviour
    {
        public FrankTestActor AttackerActor => attackActor;
        public FrankTestActor ReceiverActor => hitActor;
        public float SampleTime { get; private set; }
        public CombatTripletData Move { get; private set; }
        public int PlaybackId => attacker ? attacker.PlaybackId : -1;
        public float Duration => MotionDuration;
        public bool Playing { get; private set; }
        public float PresentationRate => impactFeedback ? impactFeedback.PlaybackRate(this) : 1f;
        public bool IsContactHeld => Playing && PresentationRate <= 0;
        public event Action<FrankBattlePairPlayback, float> TimelineAdvanced;
        FrankBattlePair pair;
        CharacterCombat attacker, receiver;
        FrankTestActor attackActor, hitActor;
        SavedModel attackModel, hitModel;
        bool lethal;
        bool lightDepthLocked;
        float lockedAttackHipsDepth;
        float lockedHitHipsDepth;
        float attackHipHeight, hitHipHeight;
        BattleSfxPlayer battleSfx;
        BattleVfxPlayer battleVfx;
        BattleImpactFeedback impactFeedback;
        float[] contactTimes = Array.Empty<float>();
        int nextContact;
        float timelineHighWater;
        float deferredDelta;

        public Vector3 CameraPosition(CharacterCombat fighter)
        {
            var actor = fighter == attacker ? attackActor : hitActor;
            float height = fighter == attacker ? attackHipHeight : hitHipHeight;
            return actor && actor.Pose ? actor.Pose.targetHips.position - Vector3.up * height : fighter.transform.position;
        }

        sealed class SavedModel
        {
            readonly Animator animator;
            readonly Transform[] bones;
            readonly Vector3[] positions;
            readonly Quaternion[] rotations;
            readonly Vector3 scale;
            readonly bool enabled;
            public SavedModel(Animator animator)
            {
                this.animator = animator;
                enabled = animator.enabled;
                scale = animator.transform.localScale;
                bones = animator.GetComponentsInChildren<Transform>(true);
                positions = new Vector3[bones.Length];
                rotations = new Quaternion[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                {
                    positions[i] = bones[i].localPosition;
                    rotations[i] = bones[i].localRotation;
                }
            }
            public void Restore()
            {
                if (!animator) return;
                animator.transform.localScale = scale;
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] && bones[i] != animator.transform)
                        bones[i].SetLocalPositionAndRotation(positions[i], rotations[i]);
                animator.enabled = enabled;
                animator.speed = 1;
                animator.Rebind();
                animator.Play("Base Layer.Idle", 0, 0);
                animator.Update(0);
            }
        }

        public bool Begin(CharacterCombat source, CharacterCombat target, CombatTripletData move, bool isLethal)
        {
            if (move.sourcePair == null || !move.sourcePair.Valid) return false;
            if (move.sourcePair.reactions)
            {
                if (move.grapple)
                    throw new ArgumentException("A pair cannot combine a reaction track with a grapple.", nameof(move));
                if (!move.sourcePair.reactions.TryValidate(out string error))
                    throw new ArgumentException(error, nameof(move));
            }
            pair = move.sourcePair;
            Move = move;
            attacker = source;
            receiver = target;
            lethal = isLethal;
            ResetRecovery();
            lightDepthLocked = pair.unarmedIndex >= 0;
            float initialAttackHipsDepth = source.Animator.GetBoneTransform(HumanBodyBones.Hips).position.z;
            float initialHitHipsDepth = target.Animator.GetBoneTransform(HumanBodyBones.Hips).position.z;
            source.weaponRig?.Release();
            target.weaponRig?.Release();
            var rotation = Quaternion.LookRotation(target.transform.position.x >= source.transform.position.x
                ? Vector3.right : Vector3.left);
            // Rotate the source pair as a whole. Do not clamp or offset individual bones.
            source.Animator.transform.rotation = rotation;
            target.Animator.transform.SetPositionAndRotation(
                source.Animator.transform.position + rotation * pair.receiverOffset,
                rotation * pair.receiverRotation);
            attackModel = new SavedModel(source.Animator);
            hitModel = new SavedModel(target.Animator);
            attackHipHeight = source.Animator.GetBoneTransform(HumanBodyBones.Hips).position.y - source.transform.position.y;
            hitHipHeight = target.Animator.GetBoneTransform(HumanBodyBones.Hips).position.y - target.transform.position.y;
            if (lightDepthLocked)
            {
                lockedAttackHipsDepth = initialAttackHipsDepth;
                lockedHitHipsDepth = initialHitHipsDepth;
            }
            try
            {
                AcquireEquipment(source, target);
                float entryBlend = IsGrapple ? Move.grapple.entryBlendSeconds : pair.entryBlendSeconds;
                attackActor = Actor(source, pair.attackerDriver, pair.attack, true,
                    pair.showWeapon || move.weapon != TrumpWeaponManager.WeaponType.None, pair.unarmedIndex >= 0,
                    entryBlend, pair.attackerWeaponPrefab, pair.attackerWeaponSocket);
                hitActor = Actor(target, pair.receiverDriver, pair.reaction, false, false, pair.unarmedIndex >= 0, entryBlend);
                hitActor.SetReactionTrack(pair.reactions, lethal);
                // Grapples constrain the complete pair after spacing. Applying a
                // pelvis lock before optional spacing would move the hands twice
                // when the baked separation passes through zero.
                if (lightDepthLocked && !IsGrapple && !pair.constrainDepthAfterSpacing)
                {
                    attackActor.Pose.LockTargetHipsDepth(lockedAttackHipsDepth);
                    hitActor.Pose.LockTargetHipsDepth(lockedHitHipsDepth);
                }
                attacker.BeginSourceSequence(this, false);
                receiver.BeginSourceSequence(this, lethal);
                attackPlaybackId = attacker.PlaybackId;
                hitPlaybackId = receiver.PlaybackId;
                InitializeGrapple();
                Playing = true;
                SampleTime = 0;
                EvaluateAt(0);
                battleSfx = source.battleSfx;
                if (battleSfx) battleSfx.BeginSequence(this, move, lethal);
                battleVfx = source.battleVfx;
                impactFeedback = battleVfx ? battleVfx.GetComponent<BattleImpactFeedback>() : null;
                var timeline = battleSfx ? battleSfx.bank : battleVfx ? battleVfx.timeline : null;
                var profile = PresentationProfile(timeline);
                BuildContactTimes(profile);
                timelineHighWater = -1;
                deferredDelta = 0;
                bool hasVfxTimeline = battleVfx && battleVfx.BeginSequence(this, source, target, move, lethal);
                if (!hasVfxTimeline && receiver.hitEffect)
                {
                    receiver.hitEffect.gameObject.SetActive(true);
                    receiver.hitEffect.Play(true);
                }
                return true;
            }
            catch
            {
                Cancel();
                throw;
            }
        }

        static FrankTestActor Actor(CharacterCombat combat, FrankTestDriver driver, AnimationClip clip,
            bool attacking, bool weapons, bool unarmed, float entryBlend = 0,
            GameObject weaponPrefab = null, string weaponSocket = "")
        {
            var root = new GameObject(combat.name + (attacking ? " source attack" : " source reaction"));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, combat.gameObject.scene);
            root.transform.SetPositionAndRotation(combat.Animator.transform.position, combat.Animator.transform.rotation);
            var actor = root.AddComponent<FrankTestActor>();
            actor.characterName = combat.name;
            actor.character = combat.Animator;
            actor.PrepareEntryBlend(entryBlend);
            try
            {
                actor.ConfigureSource(driver, clip, attacking, weapons && !weaponPrefab, unarmed,
                    preserveCharacterScale: true);
                if (weaponPrefab) actor.AttachSourceWeapon(weaponPrefab, weaponSocket);
            }
            catch { ClearActor(ref actor); throw; }
            return actor;
        }

        public void EvaluateAt(float seconds)
        {
            if (!Playing || waitingForGetUp) return;
            SampleTime = Mathf.Clamp(seconds, 0, Duration);
            attackActor.Evaluate(SampleTime);
            hitActor.Evaluate(pair.reactions ? SampleTime : Mathf.Max(0, SampleTime - pair.reactionDelay));
            if (pair.spacing && pair.unarmedIndex >= 0 && pair.bodySpacing > 0)
            {
                Vector3 separation = PairSeparation(SampleTime) * pair.bodySpacing;
                // Battle is a single X lane. Keep the authored spacing correction in
                // the lane plane so it cannot reintroduce depth travel after the pose
                // has been retargeted.
                separation.z = 0f;
                FrankPairSpacing.Apply(attackActor, hitActor, attackActor.transform.rotation * separation);
            }
            CombatPositioningController.Instance?.ConstrainDepthNow();
            ConstrainLightHipsDepth();
            if (Move.grounding)
                Move.grounding.Apply(SampleTime, attacker.Animator, receiver.Animator, attackActor);
            ApplyReceiverFloorLift();
        }

        void ApplyReceiverFloorLift()
        {
            var liftSamples = pair.receiverFloorLift;
            if (liftSamples == null || liftSamples.Length == 0 || !hitActor || !hitActor.Pose) return;
            float frame = Mathf.Min(SampleTime * FrankBattlePair.ReceiverFloorSamplesPerSecond, liftSamples.Length - 1);
            int index = (int)frame;
            float lift = Mathf.Lerp(liftSamples[index], liftSamples[Mathf.Min(index + 1, liftSamples.Length - 1)], frame - index);
            if (lift <= 0) return;
            // These offsets are baked from the receiving character's body mesh.
            // Only the three new throws opt in; runtime never skins a body mesh.
            hitActor.Pose.targetHips.position += Vector3.up * lift;
            if (!attackActor || !attackActor.Pose) return;
            foreach (var arm in attackActor.Pose.limbs)
            {
                if (!arm.sourceKnuckle) continue;
                float distance = Vector3.Distance(arm.sourceEnd.position, hitActor.Pose.sourceHips.position);
                foreach (var held in hitActor.Pose.limbs)
                    distance = Mathf.Min(distance, Vector3.Distance(arm.sourceEnd.position, held.sourceEnd.position));
                float holding = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.10f, .28f, distance));
                if (holding > 0) arm.Solve(arm.Goal + Vector3.up * (lift * holding));
            }
        }

        void LateUpdate()
        {
            if (!Playing) return;
            if (!ParticipantsValid() || recoveryFailed)
            {
                Cancel();
                return;
            }
            if (Application.isPlaying && (Time.timeScale <= 0 || IsContactHeld))
            {
                // The source Animator can refresh native weapon bones between manual samples.
                // Reassert the held pose without advancing time or redispatching contact events.
                if (!waitingForGetUp) EvaluateAt(SampleTime);
                return;
            }
            if (waitingForGetUp)
            {
                ConstrainLightHipsDepth();
                if (!attackRecoveryPending && !hitRecoveryPending) CompleteNow();
                return;
            }
            // Leave the terminal pose visible for a frame, as in the demo's clamped Evaluate.
            if (SampleTime >= Duration) { CompleteSourceMotion(); return; }
            if (Time.deltaTime <= 0) return;
            float carriedDelta = deferredDelta;
            deferredDelta = 0;
            AdvanceTo(SampleTime + Time.deltaTime * PresentationRate + carriedDelta);
        }

        // Health, impact audio and VFX all observe the same contact pose. A slow
        // frame crossing multiple strikes still presents each hit exactly once.
        public void AdvanceTo(float seconds)
        {
            if (!Playing || waitingForGetUp || !float.IsFinite(seconds) || seconds <= timelineHighWater ||
                Application.isPlaying && (Time.timeScale <= 0 || IsContactHeld)) return;
            float displayedTime = Mathf.Clamp(seconds, 0, Duration);
            float settledTime = displayedTime;
            deferredDelta = 0;
            try
            {
                while (nextContact < contactTimes.Length && contactTimes[nextContact] <= displayedTime)
                {
                    EvaluateAt(contactTimes[nextContact++]);
                    timelineHighWater = SampleTime;
                    TimelineAdvanced?.Invoke(this, SampleTime);
                    if (!Playing) return;
                    if (battleSfx) battleSfx.AdvanceSequence(this, SampleTime);
                    if (battleVfx) battleVfx.AdvanceSequence(this, SampleTime);
                    if (!Playing) return;
                    if (Application.isPlaying && (Time.timeScale <= 0 || IsContactHeld))
                    {
                        // Hold the actual contact pose, including a slow frame crossing
                        // several hits. Resume the unconsumed time after the hold ends.
                        settledTime = SampleTime;
                        deferredDelta = Mathf.Max(0, displayedTime - settledTime);
                        return;
                    }
                }
                EvaluateAt(displayedTime);
                timelineHighWater = displayedTime;
                TimelineAdvanced?.Invoke(this, SampleTime);
                if (!Playing) return;
                if (battleSfx) battleSfx.AdvanceSequence(this, SampleTime);
                if (battleVfx) battleVfx.AdvanceSequence(this, SampleTime);
            }
            finally { if (Playing) EvaluateAt(settledTime); }
        }

        void ConstrainLightHipsDepth()
        {
            if (!lightDepthLocked) return;
            SetHipsDepth(attacker ? attacker.Animator : null, lockedAttackHipsDepth);
            SetHipsDepth(receiver ? receiver.Animator : null, lockedHitHipsDepth);
        }

        static void SetHipsDepth(Animator animator, float depth)
        {
            if (!animator || !float.IsFinite(depth)) return;
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (!hips) return;
            Vector3 position = hips.position;
            if (Mathf.Abs(position.z - depth) <= 0.00001f) return;
            position.z = depth;
            hips.position = position;
        }

        static void ClearActor(ref FrankTestActor actor)
        {
            if (!actor) return;
            actor.Clear();
            if (Application.isPlaying) Destroy(actor.gameObject);
            else DestroyImmediate(actor.gameObject);
            actor = null;
        }
        void OnDisable() { Cancel(); }
        void OnDestroy() { Cancel(); }
    }
}
