using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Replaces only the visible rig; the fighter, match state and animation clock keep their identity.</summary>
[DisallowMultipleComponent]
public sealed class BattleCharacterAppearance : MonoBehaviour
{
    public BattleCharacterCatalog catalog;
    public string initialCharacterId;
    public string startupCharacterId;
    public Transform[] visualRoots = Array.Empty<Transform>();
    public Vector3 motionScale = Vector3.one;
    public float referenceLegLength = 1;
    public string CurrentCharacterId => string.IsNullOrEmpty(currentId) ? initialCharacterId : currentId;
    public string DisplayName => catalog ? catalog.Find(CurrentCharacterId)?.displayName : null;
    public Vector3 MotionScale => motionScale;
    public Vector3 SourceMotionScale(Vector3 calibratedScale) => UsesUniformHeight
        ? calibratedScale * catalog.sourceMotionScale : motionScale;
    bool UsesUniformHeight => catalog && catalog.Find(CurrentCharacterId)?.referenceHeight > .001f;
    public event Action Changed;
    string currentId;

    public bool TryApply(string id)
    {
        var entry = catalog ? catalog.Find(id) : null;
        var combat = GetComponent<CharacterCombat>();
        if (entry == null || !entry.prefab || !combat || combat.IsBusy || combat.IsDead ||
            !combat.Initialize())
            return false;
        if (string.Equals(CurrentCharacterId, entry.id, StringComparison.OrdinalIgnoreCase))
        {
            ApplyScale(entry, RetainedChildren(combat.Animator.transform));
            return true;
        }
        var source = entry.prefab.GetComponent<Animator>();
        if (!source || !source.avatar || !source.avatar.isValid || !source.avatar.isHuman ||
            entry.referenceLegLength <= 0 || referenceLegLength <= 0)
            return false;
        var animator = combat.Animator;
        var instance = Instantiate(entry.prefab);
        instance.SetActive(false);
        instance.name = entry.displayName + " visual staging";
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(instance, gameObject.scene);
        var incoming = new List<Transform>();
        foreach (Transform child in instance.transform)
            incoming.Add(child);
        if (incoming.Count == 0)
        {
            Retire(instance);
            return false;
        }
        animator.enabled = false;
        combat.weaponRig?.Release();
        var retained = RetainedChildren(animator.transform);
        foreach (var root in visualRoots)
        {
            if (!root)
                continue;
            root.gameObject.SetActive(false);
            root.SetParent(instance.transform, false);
        }
        foreach (var child in incoming)
            child.SetParent(animator.transform, false);
        ApplyScale(entry, retained);
        animator.avatar = source.avatar;
        animator.enabled = true;
        animator.Rebind();
        animator.Play("Base Layer.Idle", 0, 0);
        animator.Update(0);
        visualRoots = incoming.ToArray();
        currentId = entry.id;
        Retire(instance);
        Changed?.Invoke();
        return true;
    }

    List<Transform> RetainedChildren(Transform root)
    {
        var retained = new List<Transform>();
        foreach (Transform child in root)
            if (Array.IndexOf(visualRoots, child) < 0)
                retained.Add(child);
        return retained;
    }

    void ApplyScale(BattleCharacterCatalog.Entry entry, List<Transform> retained)
    {
        float scale = entry.referenceHeight > .001f
            ? catalog.characterHeight / entry.referenceHeight : referenceLegLength / entry.referenceLegLength;
        var previousScale = transform.localScale;
        transform.localScale = Vector3.one * scale;
        var ratio = new Vector3(previousScale.x / scale, previousScale.y / scale, previousScale.z / scale);
        foreach (var child in retained)
        {
            child.localPosition = Vector3.Scale(child.localPosition, ratio);
            child.localScale = Vector3.Scale(child.localScale, ratio);
        }
    }

    static void Retire(GameObject instance)
    {
        if (Application.isPlaying)
            Destroy(instance);
        else
            DestroyImmediate(instance);
    }
}
