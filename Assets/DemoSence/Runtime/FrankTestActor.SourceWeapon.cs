using System;
using UnityEngine;
using UnityEngine.Playables;

namespace FrankRetarget
{
    public sealed partial class FrankTestActor
    {
        GameObject sourceWeapon;
        Vector3 sourceWeaponLift;
        FrankPoseRetarget.Limb sourceWeaponGrip;

        public void ApplySourceWeaponEntryGrip(float seconds)
        {
            if (!sourceWeapon || sourceWeaponGrip == null) return;
            var weapon = sourceWeapon.transform;
            weapon.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            if (entryBones == null || seconds >= entrySeconds) return;

            // Entry blends the visible arm after retargeting. Carry the authored
            // prop with that hand, using the existing palm-frame calibration.
            // Always start at socket identity so seeking never integrates offsets.
            var grip = sourceWeaponGrip;
            Quaternion authoredHand = grip.sourceEnd.rotation * grip.rotationOffset;
            Quaternion correction = grip.end.rotation * Quaternion.Inverse(authoredHand);
            Vector3 position = grip.TargetGrip + correction * (weapon.position - grip.SourceGrip);
            weapon.SetPositionAndRotation(position, correction * weapon.rotation);
        }

        public void ApplySourceWeaponGrounding(float lift)
        {
            ApplySourceWeaponDisplacement(Vector3.up * lift);
        }

        public void ApplySourceWeaponDisplacement(Vector3 displacement)
        {
            if (!sourceWeapon || !activeDriver) return;
            activeDriver.transform.position += displacement - sourceWeaponLift;
            sourceWeaponLift = displacement;
        }

        void ResetSourceWeaponGrounding()
        {
            if (sourceWeapon)
                sourceWeapon.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            if (activeDriver) activeDriver.transform.position -= sourceWeaponLift;
            sourceWeaponLift = Vector3.zero;
        }

        public void AttachSourceWeapon(GameObject prefab, string socketPath)
        {
            if (!prefab) throw new ArgumentNullException(nameof(prefab));
            if (string.IsNullOrEmpty(socketPath))
                throw new ArgumentException("An authored weapon requires an exact socket path.", nameof(socketPath));
            var pose = Pose;
            if (!pose || !pose.driver)
                throw new InvalidOperationException("Configure the source driver before attaching its weapon.");
            var socket = pose.driver.transform.Find(socketPath);
            if (!socket)
                throw new ArgumentException($"Source weapon socket '{socketPath}' was not found.", nameof(socketPath));

            var weapon = Instantiate(prefab, socket, false);
            weapon.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            // The calibrated source driver supplies the complete weapon scale.
            weapon.transform.localScale = Vector3.one;
            foreach (var collider in weapon.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var collider in weapon.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
            var renderers = weapon.GetComponentsInChildren<Renderer>(true);
            var previousRenderers = pose.weaponRenderers;
            if (sourceWeapon)
            {
                sourceWeapon.SetActive(false);
                if (Application.isPlaying) Destroy(sourceWeapon);
                else DestroyImmediate(sourceWeapon);
            }
            sourceWeapon = weapon;
            sourceWeaponGrip = null;
            pose.weaponRenderers = renderers;
            var rightHand = character.GetBoneTransform(HumanBodyBones.RightHand);
            foreach (var limb in pose.limbs)
            {
                if (limb.sourceKnuckle) limb.alignGrip = true;
                if (limb.end == rightHand && limb.sourceKnuckle && limb.sourceFingerJoint &&
                    limb.targetKnuckle && limb.targetFingerJoint)
                    sourceWeaponGrip = limb;
            }

            // Include the newly parented renderers in the existing Animator bindings,
            // then restore the current source sample after Rebind resets the skeleton.
            float sampleTime = graph.IsValid() ? (float)playable.GetTime() : 0;
            pose.driver.Rebind();
            if (graph.IsValid()) Evaluate(sampleTime);
            if (previousRenderers != null)
                foreach (var renderer in previousRenderers)
                    if (renderer) renderer.enabled = false;
            foreach (var renderer in renderers)
            {
                renderer.enabled = true;
                if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
            }
            // Clear owns this attachment through the activeDriver hierarchy.
        }
    }
}
