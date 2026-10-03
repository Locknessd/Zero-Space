using System;
using System.Collections.Generic;
using FrankRetarget;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(14000)]
public sealed class BattleVfxPlayer : MonoBehaviour
{
    [Header("Shared animation timeline")]
    public BattleSfxBank timeline;
    [Header("Battle effect prefabs")]
    public GameObject lightHit;
    public GameObject heavyHit;
    public GameObject landingDust;
    public GameObject bladeSlash;
    public GameObject lightSwing;
    public GameObject thrustSwing;
    public GameObject groundImpact;
    public GameObject landingText;
    [Range(.25f, 2f)] public float effectScale = 1f;
    public float groundHeight;
    [Min(4)] public int maxInstances = 32;

    public event Action<string, GameObject> EffectPlayed;
    public int PlayedEffectCount { get; private set; }
    public int ActiveEffectCount => instances.FindAll(i => i.root && i.root.activeSelf).Count;
    public int PooledEffectCount => instances.Count;

    sealed class Instance
    {
        public GameObject prefab, root;
        public ParticleSystem[] particles;
        public float age;
        public Transform follow;
        public Vector3 followOffset;
        public float followSeconds;
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
    readonly Dictionary<Transform, Vector3> previousPoints = new Dictionary<Transform, Vector3>();
    static readonly HumanBodyBones[] Strikers = { HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
        HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };
    Camera battleCamera;

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
        battleCamera = FindBattleCamera();
        nextCue = 0;
        highWaterTime = -1;
        return true;
    }

    public void AdvanceSequence(FrankBattlePairPlayback playback, float seconds)
    {
        if (!isActiveAndEnabled || owner != playback || sequence == null || !receiver ||
            !float.IsFinite(seconds) || seconds <= highWaterTime) return;
        highWaterTime = seconds;
        // Sample the actual cue pose, even when a slow frame crosses several different contacts.
        // Restore the displayed pose afterwards; preview sampling itself never emits effects.
        float displayedTime = playback.SampleTime;
        try
        {
            while (nextCue < sequence.cues.Length && sequence.cues[nextCue].seconds <= seconds)
            {
                var cue = sequence.cues[nextCue++];
                if (!IsVisualCue(cue.group)) continue;
                bool swing = cue.group.EndsWith("swing", StringComparison.Ordinal);
                if (swing)
                {
                    playback.EvaluateAt(Mathf.Max(0, cue.seconds - 1f / 30f));
                    RememberStrikePoints();
                }
                playback.EvaluateAt(cue.seconds);
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
                    Spawn(groundImpact, ground, Quaternion.identity, knockout ? 1.2f : .85f, "ground_impact");
                    if (cue.finalLanding)
                        Spawn(landingText, ground + Vector3.up * .35f + CameraOffset(.4f),
                            FacingCamera(), knockout ? 1.15f : 1, "landing_text");
                    // Older scenes without a ground impact retain feedback on an unarmed throw.
                    if (unarmed && cue.finalLanding && !groundImpact)
                        Spawn(lightHit, ground + Vector3.up * .12f, FacingCamera(), .8f, "light_hit");
                    break;
                case "light_swing":
                    PlaySwing(lightSwing, false, .9f, "light_swing");
                    break;
                case "thrust_swing":
                    PlaySwing(thrustSwing, true, weapon == TrumpWeaponManager.WeaponType.DualDaggers ? .75f : 1,
                        "thrust_swing");
                    break;
                case "blade_swing":
                case "heavy_swing":
                    if (unarmed || !attacker) break;
                    PlaySwing(bladeSlash, true,
                        weapon == TrumpWeaponManager.WeaponType.DualDaggers ? .65f :
                        cue.group == "heavy_swing" ? 1.15f : 1, "blade_slash");
                    break;
                }
            }
        }
        finally { if (playback.Playing) playback.EvaluateAt(displayedTime); }
    }

    static bool IsVisualCue(string group) => group == "light_hit" || group == "heavy_hit" ||
        group == "stab_hit" || group == "body_fall" || group == "knockout_fall" ||
        group == "light_swing" || group == "thrust_swing" || group == "blade_swing" || group == "heavy_swing";

    void RememberStrikePoints()
    {
        previousPoints.Clear();
        foreach (var bone in Strikers)
        {
            var anchor = BoneTransform(attacker, bone);
            if (anchor) previousPoints[anchor] = anchor.position;
        }
        var renderers = owner.AttackerActor?.Pose?.weaponRenderers;
        if (renderers == null) return;
        foreach (var blade in renderers)
            if (IsBlade(blade)) previousPoints[blade.transform] = blade.bounds.center;
    }

    bool IsBlade(Renderer renderer) => renderer && renderer.enabled && renderer.gameObject.activeInHierarchy &&
        renderer.name.IndexOf("shield", StringComparison.OrdinalIgnoreCase) < 0;

    void PlaySwing(GameObject prefab, bool useBlade, float scale, string cue)
    {
        if (!prefab || !attacker) return;
        Transform anchor = null;
        Vector3 point = BonePosition(attacker, HumanBodyBones.RightHand), movement = Vector3.zero;
        float fastest = -1;
        var renderers = owner.AttackerActor?.Pose?.weaponRenderers;
        if (useBlade && renderers != null)
            foreach (var blade in renderers)
                if (IsBlade(blade)) Consider(blade.transform, blade.bounds.center);
        if (!anchor)
            foreach (var bone in Strikers)
            {
                var limb = BoneTransform(attacker, bone);
                if (limb) Consider(limb, limb.position);
            }
        Quaternion cameraRotation = FacingCamera();
        Vector3 screenMotion = Quaternion.Inverse(cameraRotation) * movement;
        // Comic WHOOSH stays readable. The slash arc rotates along the evaluated weapon sweep.
        float angle = cue == "blade_slash" && screenMotion.sqrMagnitude > .00001f
            ? Mathf.Atan2(screenMotion.y, screenMotion.x) * Mathf.Rad2Deg : 0;
        Spawn(prefab, point + CameraOffset(.09f), cameraRotation * Quaternion.Euler(0, 0, angle),
            scale, cue, cue == "blade_slash" ? anchor : null);

        void Consider(Transform candidate, Vector3 current)
        {
            Vector3 delta = previousPoints.TryGetValue(candidate, out var previous) ? current - previous : Vector3.zero;
            if (delta.sqrMagnitude <= fastest) return;
            fastest = delta.sqrMagnitude;
            anchor = candidate;
            point = current;
            movement = delta;
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
        // Bone anchors sit inside the mesh. Bring comic letters in front of the belly/chest.
        return contact + CameraOffset(.4f);
    }

    Camera FindBattleCamera()
    {
        var main = Camera.main;
        if (main && main.gameObject.scene == gameObject.scene) return main;
        foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var camera in root.GetComponentsInChildren<Camera>(true))
                if (camera.CompareTag("MainCamera")) return camera;
        return main;
    }

    static Transform BoneTransform(CharacterCombat fighter, HumanBodyBones bone)
    {
        if (!fighter) return null;
        var animator = fighter.Animator;
        return animator && animator.isHuman ? animator.GetBoneTransform(bone) : null;
    }

    static Vector3 BonePosition(CharacterCombat fighter, HumanBodyBones bone)
    {
        var anchor = BoneTransform(fighter, bone);
        return anchor ? anchor.position : fighter ? fighter.transform.position + Vector3.up * .8f : Vector3.zero;
    }

    Quaternion FacingCamera()
    {
        if (!battleCamera) battleCamera = FindBattleCamera();
        return battleCamera ? battleCamera.transform.rotation : Quaternion.identity;
    }

    Vector3 CameraOffset(float distance) => -(FacingCamera() * Vector3.forward) * distance;

    void Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float scale, string cue, Transform follow = null)
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
        instance.follow = follow;
        instance.followOffset = follow ? position - follow.position : Vector3.zero;
        instance.followSeconds = .1f;
        instance.root.SetActive(true);
        foreach (var particles in instance.particles)
        {
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (particles.gameObject.activeInHierarchy) particles.Play(false);
        }
        PlayedEffectCount++;
        EffectPlayed?.Invoke(cue, instance.root);
    }

    // Source playback evaluates poses at order 13000. Follow the blade afterwards.
    void LateUpdate()
    {
        foreach (var instance in instances)
        {
            if (!instance.root || !instance.root.activeSelf) continue;
            instance.age += Time.deltaTime;
            if (instance.follow && instance.age <= instance.followSeconds)
                instance.root.transform.position = instance.follow.position + instance.followOffset;
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
        instance.follow = null;
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
