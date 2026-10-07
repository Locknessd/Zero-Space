using System.Collections.Generic;
using UnityEngine;

public partial class TrumpWeaponManager
{
    private readonly HashSet<ulong> unarmedPresentationOwners = new HashSet<ulong>();
    private readonly List<TrailRenderer> unarmedTrails = new List<TrailRenderer>(8);
    private readonly List<ParticleSystem> unarmedParticles = new List<ParticleSystem>(8);
    private ulong nextUnarmedPresentationToken;
    private bool unarmedPresentationHeld;

    public bool IsUnarmedPresentation => unarmedPresentationHeld || unarmedPresentationOwners.Count != 0;

    /// <summary>Hides equipment without changing the authoritative equipment request.</summary>
    public ulong BeginUnarmedPresentation()
    {
        // Never reset the sequence: callbacks from previous interactions must remain stale.
        do
        {
            nextUnarmedPresentationToken++;
        }
        while (nextUnarmedPresentationToken == 0 || unarmedPresentationOwners.Contains(nextUnarmedPresentationToken));

        unarmedPresentationOwners.Add(nextUnarmedPresentationToken);
        HideUnarmedWeaponHierarchy();
        return nextUnarmedPresentationToken;
    }

    /// <summary>Releases only this owner; restoration uses the latest authoritative request.</summary>
    public void EndUnarmedPresentation(ulong token, bool restore)
    {
        if (token == 0 || !unarmedPresentationOwners.Remove(token)) return;
        if (!restore) unarmedPresentationHeld = true;
        ApplyRequestedUnarmedEquipment();
    }

    /// <summary>Invalidates old owners and clears an interrupted or dead interaction's hold.</summary>
    public void ResetUnarmedPresentation()
    {
        unarmedPresentationOwners.Clear();
        unarmedPresentationHeld = false;
        ApplyRequestedUnarmedEquipment();
    }

    private void ApplyRequestedUnarmedEquipment()
    {
        ApplyWeaponVisibility(activeWeapon);
        if (!IsUnarmedPresentation && CombatWeaponLocked) PrepareCombatWeapon(activeWeapon);
    }

    private void OnDisable()
    {
        // Preserve ordinary component disable behavior when no owned presentation is active.
        if (!IsUnarmedPresentation) return;
        unarmedPresentationOwners.Clear();
        unarmedPresentationHeld = true;
        HideUnarmedWeaponHierarchy();
    }

    private void LateUpdate()
    {
        if (IsUnarmedPresentation) HideUnarmedWeaponHierarchy(false);
    }

    private void HideUnarmedWeaponHierarchy(bool clearInactive = true)
    {
        HideUnarmedWeaponObject(warriorShieldSet, clearInactive);
        HideUnarmedWeaponObject(greatSwordSet, clearInactive);
        HideUnarmedWeaponObject(spearSet, clearInactive);
        HideUnarmedWeaponObject(katanaSet, clearInactive);
        HideUnarmedWeaponObject(dualDaggersSet, clearInactive);
        HideUnarmedWeaponObject(assassinSet, clearInactive);
        HideUnarmedWeaponObject(twoHandedAxeSet, clearInactive);
        HideUnarmedWeaponObject(katanaSwordMesh, clearInactive);
        HideUnarmedWeaponObject(katanaCaseMesh, clearInactive);
        HideUnarmedWeaponObject(katanaDummyHandle, clearInactive);
    }

    private void HideUnarmedWeaponObject(GameObject root, bool clearInactive)
    {
        if (root == null || (!clearInactive && !root.activeSelf)) return;

        root.GetComponentsInChildren(true, unarmedTrails);
        foreach (var trail in unarmedTrails) trail.Clear();
        unarmedTrails.Clear();
        root.GetComponentsInChildren(true, unarmedParticles);
        foreach (var particles in unarmedParticles)
        {
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        unarmedParticles.Clear();
        root.SetActive(false);
    }
}
