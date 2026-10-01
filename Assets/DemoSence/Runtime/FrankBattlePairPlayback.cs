using System;
using UnityEngine;

namespace FrankRetarget
{
    [Serializable]
    public sealed class FrankBattlePair
    {
        public FrankTestDriver attackerDriver, receiverDriver;
        public AnimationClip attack, reaction;
        public AnimationClip getUp;
        public Vector3 receiverOffset;
        public Quaternion receiverRotation = Quaternion.identity;
        public FrankPairSpacing spacing;
        public float bodySpacing;
        public int unarmedIndex = -1;
        public bool pepeAttacks;
        public bool Valid => attackerDriver && receiverDriver && attack && reaction;
    }

    // Uses the demo's actual actor/driver playback, with one clock for both roles.
    [DefaultExecutionOrder(13000)]
    public sealed class FrankBattlePairPlayback : MonoBehaviour
    {
        public FrankTestActor AttackerActor => attackActor;
        public FrankTestActor ReceiverActor => hitActor;
        public float SampleTime { get; private set; }
        public float Duration => Mathf.Max(pair.attack.length, pair.reaction.length);
        public bool Playing { get; private set; }
        FrankBattlePair pair;
        CharacterCombat attacker, receiver;
        FrankTestActor attackActor, hitActor;
        SavedModel attackModel, hitModel;
        bool lethal;
        bool waitingForGetUp;
        bool lightDepthLocked;
        float lockedAttackHipsDepth;
        float lockedHitHipsDepth;
        float attackHipHeight, hitHipHeight;

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
            pair = move.sourcePair;
            attacker = source;
            receiver = target;
            lethal = isLethal;
            waitingForGetUp = false;
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
                attackActor = Actor(source, pair.attackerDriver, pair.attack, true, move.weapon != TrumpWeaponManager.WeaponType.None, pair.unarmedIndex >= 0);
                hitActor = Actor(target, pair.receiverDriver, pair.reaction, false, false, pair.unarmedIndex >= 0);
                if (lightDepthLocked)
                {
                    attackActor.Pose.LockTargetHipsDepth(lockedAttackHipsDepth);
                    hitActor.Pose.LockTargetHipsDepth(lockedHitHipsDepth);
                }
                attacker.BeginSourceSequence(this, false);
                receiver.BeginSourceSequence(this, lethal);
                Playing = true;
                SampleTime = 0;
                EvaluateAt(0);
                if (receiver.hitEffect) receiver.hitEffect.Play();
                return true;
            }
            catch
            {
                Cancel();
                throw;
            }
        }

        static FrankTestActor Actor(CharacterCombat combat, FrankTestDriver driver, AnimationClip clip, bool attacking, bool weapons, bool unarmed)
        {
            var root = new GameObject(combat.name + (attacking ? " source attack" : " source reaction"));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, combat.gameObject.scene);
            root.transform.SetPositionAndRotation(combat.Animator.transform.position, combat.Animator.transform.rotation);
            var actor = root.AddComponent<FrankTestActor>();
            actor.characterName = combat.name;
            actor.character = combat.Animator;
            try { actor.ConfigureSource(driver, clip, attacking, weapons, unarmed); }
            catch { actor.Clear(); Destroy(root); throw; }
            return actor;
        }

        public void EvaluateAt(float seconds)
        {
            if (!Playing) return;
            SampleTime = Mathf.Clamp(seconds, 0, Duration);
            attackActor.Evaluate(SampleTime);
            hitActor.Evaluate(SampleTime);
            if (pair.spacing && pair.unarmedIndex >= 0 && pair.bodySpacing > 0)
            {
                Vector3 separation = pair.spacing.Separation(pair.unarmedIndex, pair.pepeAttacks, SampleTime) * pair.bodySpacing;
                // Battle is a single X lane. Keep the authored spacing correction in
                // the lane plane so it cannot reintroduce depth travel after the pose
                // has been retargeted.
                separation.z = 0f;
                FrankPairSpacing.Apply(attackActor, hitActor, attackActor.transform.rotation * separation);
            }
            CombatPositioningController.Instance?.ConstrainDepthNow();
            ConstrainLightHipsDepth();
        }

        void LateUpdate()
        {
            if (!Playing) return;
            if (waitingForGetUp)
            {
                ConstrainLightHipsDepth();
                // AnimationEndAction finishes GetUp and clears IsBusy. Keep this
                // playback alive until that callback has completed the recovery.
                if (receiver && !receiver.IsBusy) CompleteAfterGetUp();
                return;
            }
            // Leave the terminal pose visible for a frame, as in the demo's clamped Evaluate.
            if (SampleTime >= Duration) { CompleteSourceMotion(); return; }
            EvaluateAt(SampleTime + Time.deltaTime);
        }

        void CompleteSourceMotion()
        {
            ClearActor(ref attackActor);
            attackModel?.Restore();
            attackModel = null;
            if (!lethal && pair.getUp && receiver)
            {
                ClearActor(ref hitActor);
                hitModel?.Restore();
                hitModel = null;
                if (receiver.BeginSourceGetUp(pair.getUp))
                {
                    CombatPositioningController.Instance?.ConstrainDepthNow();
                    ConstrainLightHipsDepth();
                    waitingForGetUp = true;
                    return;
                }
            }
            CompleteNow();
        }

        void CompleteAfterGetUp()
        {
            waitingForGetUp = false;
            CompleteNow();
        }

        void CompleteNow()
        {
            Playing = false;
            if (!lethal)
            {
                ClearActor(ref hitActor);
                hitModel?.Restore();
                hitModel = null;
            }
            attacker.CompleteSourceSequence(true);
            if (receiver && receiver.IsBusy) receiver.CompleteSourceSequence(true);
            // A lethal reaction keeps hitActor and hitModel alive so the receiver
            // remains in the authored final death pose. ResetCombat/Cancel clears it.
        }

        public void Cancel()
        {
            bool interrupted = Playing;
            Playing = false;
            waitingForGetUp = false;
            ClearActor(ref attackActor);
            ClearActor(ref hitActor);
            attackModel?.Restore();
            hitModel?.Restore();
            attackModel = hitModel = null;
            if (interrupted)
            {
                if (attacker) attacker.CompleteSourceSequence(false);
                if (receiver) receiver.CompleteSourceSequence(false);
            }
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
