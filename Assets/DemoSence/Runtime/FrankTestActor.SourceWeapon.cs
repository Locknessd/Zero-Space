using System;
using UnityEngine;
using UnityEngine.Playables;

namespace FrankRetarget
{
    public sealed partial class FrankTestActor
    {
        GameObject sourceWeapon;
        Vector3 sourceWeaponLift;

        public void ApplySourceWeaponGrounding(float lift)
        {
            if (!sourceWeapon || !activeDriver) return;
            var next = Vector3.up * lift;
            activeDriver.transform.position += next - sourceWeaponLift;
            sourceWeaponLift = next;
        }

        void ResetSourceWeaponGrounding()
        {
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
            pose.weaponRenderers = renderers;
            foreach (var limb in pose.limbs)
                if (limb.sourceKnuckle) limb.alignGrip = true;

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
