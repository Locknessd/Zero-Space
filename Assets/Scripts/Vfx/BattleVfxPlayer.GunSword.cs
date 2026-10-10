using System;
using UnityEngine;

public sealed partial class BattleVfxPlayer
{
    public GameObject gunMuzzleFlash;

    void PlayGunShot(BattleSfxBank.Cue cue)
    {
        if (!gunMuzzleFlash || !owner || weapon != TrumpWeaponManager.WeaponType.GunSword)
            return;
        var renderers = owner.AttackerActor?.Pose?.weaponRenderers;
        if (renderers == null)
            return;
        foreach (var gun in renderers)
        {
            if (!gun || !gun.enabled || !gun.gameObject.activeInHierarchy ||
                gun.name.IndexOf("gun", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            var filter = gun.GetComponent<MeshFilter>();
            var mesh = filter ? filter.sharedMesh : null;
            if (!mesh)
                continue;
            Vector3 center = gun.transform.TransformPoint(mesh.bounds.center);
            Vector3 target = cue.TryContactPosition(receiver.Animator, out var contact)
                ? contact : BonePosition(receiver, HumanBodyBones.Chest);
            Vector3 direction = (target - center).normalized;
            Vector3 localDirection = gun.transform.InverseTransformVector(direction).normalized;
            var ray = new Ray(mesh.bounds.center + localDirection * mesh.bounds.size.magnitude,
                -localDirection);
            if (!mesh.bounds.IntersectRay(ray, out float distance))
                continue;
            Vector3 muzzle = gun.transform.TransformPoint(ray.GetPoint(distance));
            Spawn(gunMuzzleFlash, muzzle, Quaternion.LookRotation(direction), 1, "gun_shot");
            return;
        }
    }
}
