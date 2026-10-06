using System;
using System.Collections.Generic;
using FrankRetarget;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BattleVfxPlayer : MonoBehaviour
{
    [Header("Shared animation timeline")]
    public BattleSfxBank timeline;
    [Header("Battle effect prefabs")]
    public GameObject lightHit;
    public GameObject heavyHit;
    public GameObject landingDust;
    public GameObject bladeSlash;
    [Range(.25f, 2f)] public float effectScale = 1f;
    public float groundHeight;
    [Min(4)] public int maxInstances = 24;

    public event Action<string, GameObject> EffectPlayed;
    public int PlayedEffectCount { get; private set; }
    public int ActiveEffectCount => instances.FindAll(i => i.root && i.root.activeSelf).Count;
    public int PooledEffectCount => instances.Count;

    sealed class Instance
    {
        public GameObject prefab, root;
        public ParticleSystem[] particles;
        public float age;
    }

    readonly List<Instance> instances = new List<Instance>();
    FrankBattlePairPlayback owner;
    BattleSfxBank.Move sequence;
    CharacterCombat attacker, receiver;
    TrumpWeaponManager.WeaponType weapon;
    Transform poolRoot;
    int nextCue;
    bool lethal, unarmed;
    float highWaterTime;

    public bool BeginSequence(FrankBattlePairPlayback playback, CharacterCombat source,
        CharacterCombat target, CombatTripletData move, bool isLethal)
    {
        if (!isActiveAndEnabled || !timeline || !lightHit || !heavyHit || !landingDust || !bladeSlash)
            return false;
        var profile = timeline.FindMove(move);
        if (profile == null) return false;
        owner = playback;
        sequence = profile;
        attacker = source;
        receiver = target;
        weapon = move.weapon;
        lethal = isLethal;
        unarmed = weapon == TrumpWeaponManager.WeaponType.None;
        nextCue = 0;
        highWaterTime = -1;
        return true;
    }

    public void AdvanceSequence(FrankBattlePairPlayback playback, float seconds)
    {
        if (!isActiveAndEnabled || owner != playback || sequence == null || !receiver ||
            !float.IsFinite(seconds) || seconds <= highWaterTime) return;
        highWaterTime = seconds;
        // The same monotonic clock as SFX handles skipped frames and repeated/backwards seeks.
        while (nextCue < sequence.cues.Length && sequence.cues[nextCue].seconds <= seconds)
        {
            var cue = sequence.cues[nextCue++];
            switch (cue.group)
            {
                case "light_hit":
                case "stab_hit":
                    Spawn(lightHit, ContactPoint(), FacingCamera(), 1, "light_hit");
                    break;
                case "heavy_hit":
                    Spawn(heavyHit, ContactPoint(), FacingCamera(), 1, "heavy_hit");
                    break;
                case "body_fall":
                case "knockout_fall":
                    Vector3 ground = BonePosition(receiver, HumanBodyBones.Hips);
                    ground.y = groundHeight + .035f;
                    bool knockout = lethal && cue.finalLanding;
                    Spawn(landingDust, ground, Quaternion.identity, knockout ? 1.35f : 1,
                        knockout ? "knockout_fall" : "body_fall");
                    // The light pool contains throws. Their contact is the landing, not the windup.
                    if (unarmed && cue.finalLanding)
                        Spawn(lightHit, ground + Vector3.up * .12f, FacingCamera(), .8f, "light_hit");
                    break;
                case "blade_swing":
                case "heavy_swing":
                    if (unarmed || !attacker) break;
                    float direction = Mathf.Sign(BonePosition(receiver, HumanBodyBones.Hips).x -
                        BonePosition(attacker, HumanBodyBones.Hips).x);
                    var angle = FacingCamera() * Quaternion.Euler(0, 0, direction * -35f);
                    Spawn(bladeSlash, BladePoint(), angle,
                        weapon == TrumpWeaponManager.WeaponType.DualDaggers ? .65f : 1, "blade_slash");
                    break;
            }
        }
    }

    // Controller-only moves retain feedback when no source-pair timeline is available.
    public void PlayHit(CharacterCombat target)
    {
        if (isActiveAndEnabled && target && lightHit)
            Spawn(lightHit, BonePosition(target, HumanBodyBones.Chest), FacingCamera(), 1, "light_hit");
    }

    Vector3 ContactPoint()
    {
        Vector3 contact = BonePosition(receiver, HumanBodyBones.Chest);
        float closest = float.PositiveInfinity;
        var targets = new[] { HumanBodyBones.Head, HumanBodyBones.Chest, HumanBodyBones.Hips };
        var strikers = new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };
        foreach (var target in targets)
        {
            Vector3 point = BonePosition(receiver, target);
            foreach (var striker in strikers)
            {
                float distance = (point - BonePosition(attacker, striker)).sqrMagnitude;
                if (distance < closest) { closest = distance; contact = point; }
            }
            // Native weapon meshes follow the evaluated source pose, even while Animator is disabled.
            var renderers = owner ? owner.AttackerActor?.Pose?.weaponRenderers : null;
            if (renderers == null) continue;
            foreach (var renderer in renderers)
            {
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                float distance = (point - renderer.bounds.ClosestPoint(point)).sqrMagnitude;
                if (distance < closest) { closest = distance; contact = point; }
            }
        }
        // Keep the burst slightly in front of the body for the side-on battle camera.
        var camera = Camera.main;
        return contact + (camera ? -camera.transform.forward : Vector3.forward) * .08f;
    }

    Vector3 BladePoint()
    {
        var renderers = owner ? owner.AttackerActor?.Pose?.weaponRenderers : null;
        Renderer blade = null;
        if (renderers != null)
            foreach (var renderer in renderers)
                if (renderer && renderer.enabled && renderer.gameObject.activeInHierarchy &&
                    (!blade || renderer.bounds.size.sqrMagnitude > blade.bounds.size.sqrMagnitude)) blade = renderer;
        // For sword + shield rigs the longer mesh is the blade. Emit over the weapon, not the wrist.
        return blade ? blade.bounds.center : BonePosition(attacker, HumanBodyBones.RightHand);
    }

    static Vector3 BonePosition(CharacterCombat fighter, HumanBodyBones bone)
    {
        if (!fighter) return Vector3.zero;
        var animator = fighter.Animator;
        var transform = animator && animator.isHuman ? animator.GetBoneTransform(bone) : null;
        return transform ? transform.position : fighter.transform.position + Vector3.up * .8f;
    }

    static Quaternion FacingCamera() => Camera.main ? Camera.main.transform.rotation : Quaternion.identity;

    void Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float scale, string cue)
    {
        if (!prefab) return;
        var instance = instances.Find(i => i.prefab == prefab && i.root && !i.root.activeSelf);
        if (instance == null)
        {
            if (instances.Count >= maxInstances) return;
            if (!poolRoot)
            {
                var pool = new GameObject("Battle VFX (runtime)");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(pool, gameObject.scene);
                poolRoot = pool.transform;
            }
            var root = Instantiate(prefab, poolRoot);
            root.SetActive(false);
            instance = new Instance { prefab = prefab, root = root,
                particles = root.GetComponentsInChildren<ParticleSystem>(true) };
            instances.Add(instance);
        }
        instance.root.transform.SetPositionAndRotation(position, rotation * prefab.transform.localRotation);
        instance.root.transform.localScale = prefab.transform.localScale * (effectScale * scale);
        instance.age = 0;
        instance.root.SetActive(true);
        foreach (var particles in instance.particles)
        {
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.Play(false);
        }
        PlayedEffectCount++;
        EffectPlayed?.Invoke(cue, instance.root);
    }

    void Update()
    {
        foreach (var instance in instances)
        {
            if (!instance.root || !instance.root.activeSelf) continue;
            instance.age += Time.deltaTime;
            bool alive = false;
            foreach (var particles in instance.particles)
                if (particles && particles.IsAlive(false)) { alive = true; break; }
            if (!alive || instance.age > 4f) Release(instance);
        }
    }

    static void Release(Instance instance)
    {
        foreach (var particles in instance.particles)
            if (particles) particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (instance.root) instance.root.SetActive(false);
    }

    public void EndSequence(FrankBattlePairPlayback playback, bool interrupted = false)
    {
        if (owner != playback) return;
        owner = null;
        sequence = null;
        attacker = receiver = null;
        if (interrupted) ClearEffects();
    }

    public void ClearEffects()
    {
        foreach (var instance in instances) Release(instance);
    }

    public void ResetForMatch()
    {
        owner = null;
        sequence = null;
        attacker = receiver = null;
        ClearEffects();
    }

    void OnDisable() => ResetForMatch();
    void OnDestroy()
    {
        if (!poolRoot) return;
        if (Application.isPlaying) Destroy(poolRoot.gameObject);
        else DestroyImmediate(poolRoot.gameObject);
    }
}
