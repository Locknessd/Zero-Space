using System;
using System.Collections.Generic;
using FrankRetarget;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(14000)]
public sealed class BattleVfxPlayer : MonoBehaviour
{
    public enum ContactKind { Light, Heavy, Ground }

    // Contact feedback must survive a missing prefab or a saturated particle pool.
    public readonly struct Impact
    {
        public readonly FrankBattlePairPlayback playback;
        public readonly CharacterCombat attacker, receiver;
        public readonly CombatTripletData move;
        public readonly ContactKind kind;
        public readonly Vector3 position;
        public readonly float seconds;
        public readonly bool finishing;
        public readonly BattleSfxBank.Cue cue;
        public readonly ulong eventId;

        public Impact(FrankBattlePairPlayback playback, CharacterCombat attacker,
            CharacterCombat receiver, CombatTripletData move, ContactKind kind,
            Vector3 position, float seconds, bool finishing, BattleSfxBank.Cue cue = null, ulong eventId = 0)
        {
            this.playback = playback; this.attacker = attacker; this.receiver = receiver;
            this.move = move; this.kind = kind; this.position = position;
            this.seconds = seconds; this.finishing = finishing;
            this.cue = cue; this.eventId = eventId;
        }
    }

    [Header("Shared animation timeline")]
    public BattleSfxBank timeline;
    [Header("Battle effect prefabs")]
    public GameObject lightHit;
    public GameObject heavyHit;
    public GameObject landingDust;
    public GameObject bladeSlash;
    public BattleWeaponTrails weaponTrails;
    public GameObject shieldImpact;
    [Serializable]
    public sealed class WeaponSlashVariant
    {
        public TrumpWeaponManager.WeaponType weapon;
        public GameObject prefab;
        [Range(.25f, 2f)] public float scale = 1;
    }
    [Tooltip("Weapon-specific sweeps; other weapons use the default blade slash.")]
    public WeaponSlashVariant[] bladeSlashVariants = Array.Empty<WeaponSlashVariant>();
    [Serializable]
    public sealed class WeaponImpactVariant
    {
        public TrumpWeaponManager.WeaponType weapon;
        public GameObject light, heavy, stab;
        [Range(.25f, 2f)] public float scale = 1;
    }
    public WeaponImpactVariant[] impactVariants = Array.Empty<WeaponImpactVariant>();
    [Serializable]
    public sealed class SkillVariant
    {
        public BattleSkill skill;
        public GameObject cast, projectile, impact;
        [Min(.03f)] public float flightSeconds = .18f;
        [Min(.03f)] public float chargeFollowSeconds = .18f;
        [Tooltip("Summoned strikes descend onto the victim instead of travelling from the caster's hand.")]
        public bool descendingStrike;
    }
    public SkillVariant[] skillVariants = Array.Empty<SkillVariant>();
    public GameObject lightSwing;
    public GameObject thrustSwing;
    public GameObject groundImpact;
    public GameObject landingText;
    [Range(.25f, 2f)] public float effectScale = 1f;
    [Header("Contact strength")]
    [Tooltip("Additional size for light hits and thrust contacts.")]
    [Range(.5f, 2f)] public float lightContactScale = 1.4f;
    [Tooltip("Additional size for heavy hits; ground impacts keep their own size.")]
    [Range(.5f, 2f)] public float heavyContactScale = 1.55f;
    public float groundHeight;
    [Min(4)] public int maxInstances = 48;

    public event Action<string, GameObject> EffectPlayed;
    public event Action<Impact> ContactOccurred;
    public event Action EffectsCleared;
    public event Action<CharacterCombat, CombatTripletData, bool> SequenceBegan;
    public int PlayedEffectCount { get; private set; }
    public int ActiveEffectCount
    {
        get
        {
            int count = weaponTrails ? weaponTrails.ActiveTrailCount : 0;
            foreach (var instance in instances) if (instance.root && instance.root.activeSelf) count++;
            return count;
        }
    }
    public int PooledEffectCount => instances.Count + (weaponTrails ? weaponTrails.PooledTrailCount : 0);
    public bool IsFinishingContact { get; private set; }
    public int ContactCount { get; private set; }

    sealed class Instance
    {
        public GameObject prefab, root;
        public ParticleSystem[] particles;
        public float age;
        public Transform follow;
        public Vector3 followLocalPoint;
        public float followCameraOffset;
        public Renderer followRenderer;
        public Vector3 followRendererOffset;
        public float followSeconds;
        public Transform flightTarget;
        public Vector3 flightFrom, flightOffset;
        public float flightSeconds;
        public FrankBattlePairPlayback flightOwner;
        public float flightBirth;
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
    readonly Dictionary<Renderer, Vector3> followedBladePoints = new Dictionary<Renderer, Vector3>();
    readonly List<Vector3> contactVertices = new List<Vector3>();
    readonly Vector3[] contactTargets = new Vector3[5];
    Mesh bladeMesh;
    static readonly HumanBodyBones[] Strikers = { HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
        HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };
    Camera battleCamera;
    GameObject sequenceSlash;
    float sequenceSlashScale;
    GameObject sequenceLightHit, sequenceHeavyHit, sequenceStabHit;
    float sequenceImpactScale, finishingTime;
    int ownerPlaybackId = -1;
    uint presentationSequence;
    SkillVariant sequenceSkill;
    static readonly HumanBodyBones[] ContactBones = { HumanBodyBones.Head, HumanBodyBones.Chest,
        HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg };

    public bool BeginSequence(FrankBattlePairPlayback playback, CharacterCombat source,
        CharacterCombat target, CombatTripletData move, bool isLethal)
    {
        if (!isActiveAndEnabled || !timeline)
            return false;
        var profile = playback.PresentationProfile(timeline);
        if (profile == null) return false;
        if (owner == playback && ownerPlaybackId == playback.PlaybackId && sequence != null) return true;
        owner = playback;
        presentationSequence++;
        ownerPlaybackId = playback.PlaybackId;
        sequence = profile;
        attacker = source;
        receiver = target;
        weapon = move.weapon;
        sequenceLightHit = lightHit; sequenceHeavyHit = heavyHit; sequenceStabHit = null; sequenceImpactScale = 1;
        foreach (var variant in impactVariants ?? Array.Empty<WeaponImpactVariant>())
            if (variant != null && variant.weapon == weapon)
            {
                if (variant.light) sequenceLightHit = variant.light;
                if (variant.heavy) sequenceHeavyHit = variant.heavy;
                sequenceStabHit = variant.stab;
                sequenceImpactScale = Mathf.Clamp(variant.scale, .25f, 2);
                break;
            }
        finishingTime = -1;
        sequenceSkill = Array.Find(skillVariants ?? Array.Empty<SkillVariant>(), v => v != null && v.skill == move.skill && move.skill != BattleSkill.None);
        if (sequenceSkill != null && sequenceSkill.impact) sequenceLightHit = sequenceHeavyHit = sequenceSkill.impact;
        foreach (var cue in profile.cues)
            if (cue.group == "light_hit" || cue.group == "heavy_hit" || cue.group == "stab_hit" || cue.damageOnLanding) finishingTime = cue.seconds;
        if (finishingTime < 0)
            foreach (var cue in profile.cues) if (cue.finalLanding) finishingTime = cue.seconds;
        sequenceSlash = bladeSlash;
        sequenceSlashScale = 1;
        if (bladeSlashVariants != null)
            foreach (var variant in bladeSlashVariants)
                if (variant != null && variant.weapon == weapon && variant.prefab)
                {
                    sequenceSlash = variant.prefab;
                    sequenceSlashScale = Mathf.Clamp(variant.scale, .25f, 2);
                    break;
                }
        lethal = isLethal;
        unarmed = weapon == TrumpWeaponManager.WeaponType.None;
        battleCamera = FindBattleCamera();
        nextCue = 0;
        highWaterTime = -1;
        if (!weaponTrails) weaponTrails = GetComponent<BattleWeaponTrails>();
        if (weaponTrails) weaponTrails.Begin(playback, source, profile, battleCamera);
        SequenceBegan?.Invoke(source, move, isLethal);
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
                bool hit = cue.group == "light_hit" || cue.group == "heavy_hit" || cue.group == "stab_hit";
                if (swing || hit)
                {
                    playback.EvaluateAt(Mathf.Max(0, cue.seconds - 1f / 30f));
                    RememberStrikePoints();
                    if (Enum.TryParse(cue.contactSource, out HumanBodyBones sourceBone))
                    {
                        var sourceAnchor = BoneTransform(attacker, sourceBone);
                        if (sourceAnchor) previousPoints[sourceAnchor] = sourceAnchor.position;
                    }
                }
                playback.EvaluateAt(cue.seconds);
                IsFinishingContact = lethal && Mathf.Abs(cue.seconds - finishingTime) < .001f;
                switch (cue.group)
                {
                case "grapple_grip":
                case "grapple_release":
                case "grapple_break":
                    // A grip or break is not damage: restrained movement dust, with
                    // no hurt flash, hit-stop, health cue or full-strength impact.
                    var feet = BonePosition(receiver, HumanBodyBones.LeftFoot);
                    feet.y = groundHeight + .025f;
                    Spawn(landingDust, feet, Quaternion.identity, cue.group == "grapple_grip" ? .25f : .4f,
                        cue.group);
                    break;
                case "skill_cast":
                    if (sequenceSkill != null)
                    {
                        var hand = BoneTransform(attacker, sequenceSkill.skill == BattleSkill.Archer ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                        var charge = Spawn(sequenceSkill.cast, hand ? hand.position : attacker.transform.position + Vector3.up,
                            FacingCamera(), 1, "skill_cast", hand);
                        if (charge != null) charge.followSeconds = sequenceSkill.chargeFollowSeconds;
                    }
                    break;
                case "skill_shot":
                    if (sequenceSkill != null)
                    {
                        Vector3 from = BonePosition(attacker, sequenceSkill.skill == BattleSkill.Archer ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                        Vector3 to = BonePosition(receiver, HumanBodyBones.Chest) + CameraOffset(.35f);
                        if (sequenceSkill.descendingStrike) from = to + Vector3.up * 2.4f;
                        var flight = Spawn(sequenceSkill.projectile, from, Quaternion.LookRotation(to - from), 1, "skill_shot");
                        if (flight != null)
                        {
                            flight.flightFrom = from; flight.flightTarget = BoneTransform(receiver, HumanBodyBones.Chest);
                            flight.flightOffset = CameraOffset(.35f); flight.flightSeconds = sequenceSkill.flightSeconds;
                            flight.flightOwner = playback; flight.flightBirth = cue.seconds;
                        }
                    }
                    break;
                case "light_hit":
                case "stab_hit":
                    Vector3 lightPoint = ContactPoint(cue);
                    Spawn(ContactPrefab(cue, false), lightPoint, ContactRotation(cue),
                        sequenceImpactScale * lightContactScale, "light_hit");
                    ContactCount++;
                    PublishContact(ContactKind.Light, lightPoint, cue);
                    break;
                case "heavy_hit":
                    Vector3 heavyPoint = ContactPoint(cue);
                    Spawn(ContactPrefab(cue, true), heavyPoint, ContactRotation(cue),
                        sequenceImpactScale * heavyContactScale, "heavy_hit");
                    ContactCount++;
                    PublishContact(ContactKind.Heavy, heavyPoint, cue);
                    break;
                case "body_fall":
                case "knockout_fall":
                    Vector3 ground = cue.TryContactPosition(receiver ? receiver.Animator : null,
                        out Vector3 landingPoint) ? landingPoint : BonePosition(receiver, HumanBodyBones.Hips);
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
                    PublishContact(ContactKind.Ground, ground, cue);
                    break;
                case "light_swing":
                    PlaySwing(lightSwing, false, .9f, "light_swing", cue);
                    break;
                case "thrust_swing":
                    PlaySwing(thrustSwing, true, weapon == TrumpWeaponManager.WeaponType.DualDaggers ? .75f : 1,
                        "thrust_swing", cue);
                    break;
                case "blade_swing":
                case "heavy_swing":
                    if (unarmed || !attacker) break;
                    PlaySwing(sequenceSlash, true, sequenceSlashScale *
                        (weapon == TrumpWeaponManager.WeaponType.DualDaggers ? .65f :
                        cue.group == "heavy_swing" ? 1.15f : 1), "blade_slash", cue);
                    break;
                }
            }
            if (weaponTrails) weaponTrails.Advance(seconds);
        }
        finally { IsFinishingContact = false; if (playback.Playing) playback.EvaluateAt(displayedTime); }
    }

    GameObject ContactPrefab(BattleSfxBank.Cue cue, bool heavy)
    {
        if (sequenceSkill == null)
        {
            if (cue.contactSource == "Shield" && shieldImpact) return shieldImpact;
            if (!string.IsNullOrEmpty(cue.contactSource) && cue.contactSource != "Weapon" && cue.contactSource != "Shield")
                return heavy ? heavyHit : lightHit;
            if (cue.group == "stab_hit" && sequenceStabHit) return sequenceStabHit;
        }
        return heavy ? sequenceHeavyHit : sequenceLightHit;
    }

    Quaternion ContactRotation(BattleSfxBank.Cue cue)
    {
        Quaternion facing = FacingCamera();
        Vector3 motion = Vector3.zero;
        if (Enum.TryParse(cue.contactSource, out HumanBodyBones limb))
        {
            var bone = BoneTransform(attacker, limb);
            if (bone && previousPoints.TryGetValue(bone, out var prior)) motion = bone.position - prior;
        }
        else
        {
            float fastest = -1;
            var renderers = owner.AttackerActor?.Pose?.weaponRenderers;
            if (renderers != null) foreach (var blade in renderers)
                if (IsBlade(blade) && previousPoints.TryGetValue(blade.transform, out var prior))
                {
                    Vector3 delta = BladeCenter(blade) - prior;
                    if (delta.sqrMagnitude > fastest) { fastest = delta.sqrMagnitude; motion = delta; }
                }
        }
        Vector3 screen = Quaternion.Inverse(facing) * motion;
        float angle = screen.sqrMagnitude > .000001f ? Mathf.Atan2(screen.y, screen.x) * Mathf.Rad2Deg : 0;
        return facing * Quaternion.Euler(0, 0, angle);
    }

    void PublishContact(ContactKind kind, Vector3 position, BattleSfxBank.Cue cue)
    {
        if (!owner || !receiver) return;
        ContactOccurred?.Invoke(new Impact(owner, attacker, receiver, owner.Move, kind,
            position, cue.seconds, IsFinishingContact, cue, ((ulong)presentationSequence << 32) | (uint)nextCue));
    }

    public void ReplaceSequence(FrankBattlePairPlayback playback, BattleSfxBank.Move profile, float seconds)
    {
        if (owner != playback || ownerPlaybackId != playback.PlaybackId || profile == null) return;
        sequence = profile;
        nextCue = 0;
        while (nextCue < sequence.cues.Length && sequence.cues[nextCue].seconds <= seconds) nextCue++;
        highWaterTime = seconds;
        finishingTime = -1;
        foreach (var cue in profile.cues)
            if (cue.group == "light_hit" || cue.group == "heavy_hit" || cue.group == "stab_hit" || cue.damageOnLanding)
                finishingTime = cue.seconds;
    }

    static bool IsVisualCue(string group) => group == "light_hit" || group == "heavy_hit" ||
        group == "stab_hit" || group == "body_fall" || group == "knockout_fall" ||
        group == "light_swing" || group == "thrust_swing" || group == "blade_swing" || group == "heavy_swing" ||
        group == "skill_cast" || group == "skill_shot" || group == "grapple_grip" ||
        group == "grapple_release" || group == "grapple_break";

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
            if (IsBlade(blade)) previousPoints[blade.transform] = BladeCenter(blade);
    }

    bool IsBlade(Renderer renderer) => renderer && renderer.enabled && renderer.gameObject.activeInHierarchy &&
        (!weaponTrails || weaponTrails.IsRendererMeshEligible(renderer)) &&
        renderer.name.IndexOf("shield", StringComparison.OrdinalIgnoreCase) < 0 &&
        renderer.name.IndexOf("case", StringComparison.OrdinalIgnoreCase) < 0;

    Vector3 BladeCenter(Renderer renderer)
    {
        // Renderer.bounds can still describe the previous rendered frame after the
        // source graph is sampled. Bake only the small weapon mesh for the cue pose.
        if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh)
        {
            if (!bladeMesh)
            {
                bladeMesh = new Mesh { name = "Battle slash pose sample", hideFlags = HideFlags.HideAndDontSave };
                bladeMesh.MarkDynamic();
            }
            bladeMesh.Clear();
            skin.BakeMesh(bladeMesh, true);
            return skin.transform.TransformPoint(bladeMesh.bounds.center);
        }
        return renderer.bounds.center;
    }

    void PlaySwing(GameObject prefab, bool useBlade, float scale, string cue, BattleSfxBank.Cue timing)
    {
        var trail = weaponTrails ? weaponTrails.Play(timing) : null;
        if (trail)
        {
            PlayedEffectCount++;
            EffectPlayed?.Invoke(cue, trail);
            return;
        }
        if (!prefab || !attacker) return;
        Transform anchor = null;
        Renderer bladeAnchor = null;
        Vector3 point = BonePosition(attacker, HumanBodyBones.RightHand), movement = Vector3.zero;
        float fastest = -1;
        var renderers = owner.AttackerActor?.Pose?.weaponRenderers;
        if (useBlade && renderers != null)
            foreach (var blade in renderers)
                if (IsBlade(blade)) Consider(blade.transform, BladeCenter(blade), blade);
        if (!anchor && timing.contactSource == "Receiver")
        {
            var body = BoneTransform(receiver, timing.contactBone);
            if (body) Consider(body, body.TransformPoint(timing.contactOffset));
        }
        if (!anchor && Enum.TryParse(timing.contactSource, out HumanBodyBones requestedLimb))
        {
            var limb = BoneTransform(attacker, requestedLimb);
            if (limb) Consider(limb, limb.position);
        }
        if (!anchor)
            foreach (var bone in Strikers)
            {
                var limb = BoneTransform(attacker, bone);
                if (limb) Consider(limb, limb.position);
            }
        Quaternion cameraRotation = FacingCamera();
        Vector3 screenMotion = Quaternion.Inverse(cameraRotation) * movement;
        // WHOOSH stays readable. Pack slash arcs rotate along the evaluated weapon sweep.
        float angle = cue == "blade_slash" && screenMotion.sqrMagnitude > .00001f
            ? Mathf.Atan2(screenMotion.y, screenMotion.x) * Mathf.Rad2Deg : 0;
        bool bodyWhoosh = !useBlade && (timing.contactSource == "Receiver" || Enum.TryParse<HumanBodyBones>(timing.contactSource, out _));
        float screenOffset = bodyWhoosh ? .35f : .09f;
        Spawn(prefab, point + CameraOffset(screenOffset), cameraRotation * Quaternion.Euler(0, 0, angle),
            scale, cue, cue == "blade_slash" || bodyWhoosh ? anchor : null, cue == "blade_slash" ? bladeAnchor : null,
            bodyWhoosh ? screenOffset : 0);

        void Consider(Transform candidate, Vector3 current, Renderer blade = null)
        {
            Vector3 delta = previousPoints.TryGetValue(candidate, out var previous) ? current - previous : Vector3.zero;
            if (delta.sqrMagnitude <= fastest) return;
            fastest = delta.sqrMagnitude;
            anchor = candidate;
            bladeAnchor = blade;
            point = current;
            movement = delta;
        }
    }

    // Controller-only moves retain feedback when no source-pair timeline is available.
    public void PlayHit(CharacterCombat target)
    {
        if (isActiveAndEnabled && target && lightHit)
            Spawn(lightHit, BonePosition(target, HumanBodyBones.Chest), FacingCamera(), lightContactScale, "light_hit");
    }

    Vector3 ContactPoint(BattleSfxBank.Cue cue)
    {
        if (cue.TryContactPosition(receiver.Animator, out var authoredContact))
        {
            return authoredContact + CameraOffset(.015f);
        }
        if (sequenceSkill != null) return BonePosition(receiver, HumanBodyBones.Chest) + CameraOffset(.35f);
        Vector3 contact = BonePosition(receiver, HumanBodyBones.Chest);
        float closest = float.PositiveInfinity;
        var blades = owner ? owner.AttackerActor?.Pose?.weaponRenderers : null;
        bool armed = !unarmed && blades != null && Array.Exists(blades, IsBlade);
        if (armed)
        {
            for (int i = 0; i < ContactBones.Length; i++) contactTargets[i] = BonePosition(receiver, ContactBones[i]);
            foreach (var renderer in blades)
            {
                if (!IsBlade(renderer)) continue;
                Mesh mesh;
                if (renderer is SkinnedMeshRenderer skin) { BladeCenter(skin); mesh = bladeMesh; }
                else { var filter = renderer.GetComponent<MeshFilter>(); mesh = filter ? filter.sharedMesh : null; }
                if (!mesh) continue;
                mesh.GetVertices(contactVertices);
                foreach (var vertex in contactVertices)
                {
                    Vector3 world = renderer.transform.TransformPoint(vertex);
                    foreach (var target in contactTargets)
                    {
                        float distance = (world - target).sqrMagnitude;
                        if (distance < closest) { closest = distance; contact = target; }
                    }
                }
            }
            return contact + CameraOffset(.35f);
        }
        float fastest = 0;
        foreach (var striker in Strikers)
        {
            var anchor = BoneTransform(attacker, striker);
            if (anchor && previousPoints.TryGetValue(anchor, out var previous))
                fastest = Mathf.Max(fastest, Vector3.Distance(anchor.position, previous));
        }
        foreach (var target in ContactBones)
        {
            Vector3 point = BonePosition(receiver, target);
            if (!armed) foreach (var striker in Strikers)
            {
                var anchor = BoneTransform(attacker, striker);
                if (!anchor) continue;
                float movement = previousPoints.TryGetValue(anchor, out var previous) ? Vector3.Distance(anchor.position, previous) : 0;
                if (fastest > .02f && movement < fastest * .3f) continue;
                float distance = (point - anchor.position).sqrMagnitude;
                if (distance < closest) { closest = distance; contact = point; }
            }
        }
        // Bone anchors sit inside the mesh. Bring comic letters in front of the belly/chest.
        return contact + CameraOffset(.35f);
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

    Instance Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float scale, string cue,
        Transform follow = null, Renderer followRenderer = null, float followCameraOffset = 0)
    {
        if (!prefab) return null;
        var instance = instances.Find(i => i.prefab == prefab && i.root && !i.root.activeSelf);
        if (instance == null)
        {
            if (instances.Count >= maxInstances)
            {
                // A varied weapon pool must not reserve every slot forever for
                // old prefab types and silently lose a new contact effect.
                var spare = instances.Find(i => i.root && !i.root.activeSelf);
                if (spare == null) return null;
                if (Application.isPlaying) Destroy(spare.root); else DestroyImmediate(spare.root);
                instances.Remove(spare);
            }
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
            foreach (var particles in instance.particles)
            {
                var main = particles.main; main.loop = false; main.playOnAwake = false;
                main.stopAction = ParticleSystemStopAction.None;
                main.useUnscaledTime = true;
            }
            instances.Add(instance);
        }
        instance.root.transform.SetPositionAndRotation(position, rotation * prefab.transform.localRotation);
        instance.root.transform.localScale = prefab.transform.localScale * (effectScale * scale);
        instance.age = 0;
        instance.flightTarget = null;
        instance.flightOwner = null;
        instance.follow = follow;
        // Native skinned weapons move within their renderer; rigid weapons rotate
        // around the grip. Track their current bounds rather than the grip position.
        instance.followRenderer = followRenderer;
        instance.followRendererOffset = followRenderer ? position - BladeCenter(followRenderer) : Vector3.zero;
        instance.followCameraOffset = followCameraOffset;
        instance.followLocalPoint = follow ? follow.InverseTransformPoint(position - CameraOffset(followCameraOffset)) : Vector3.zero;
        instance.followSeconds = .18f;
        instance.root.SetActive(true);
        bool contact = cue == "light_hit" || cue == "heavy_hit" || cue == "ground_impact" || cue == "body_fall" || cue == "knockout_fall";
        foreach (var particles in instance.particles)
        {
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (!particles.gameObject.activeInHierarchy) continue;
            // Contact is evaluated late in the frame, after Shuriken's update.
            // Emit the zero-time burst now, before ContactOccurred starts hit-stop.
            // Otherwise the held contact pose has no flash until simulation resumes.
            if (contact) particles.Simulate(.001f, false, true, false);
            particles.Play(false);
        }
        PlayedEffectCount++;
        EffectPlayed?.Invoke(cue, instance.root);
        return instance;
    }

    // Source playback evaluates poses at order 13000. Follow the blade afterwards.
    void LateUpdate()
    {
        followedBladePoints.Clear();
        foreach (var instance in instances)
        {
            if (!instance.root || !instance.root.activeSelf) continue;
            instance.age += Time.unscaledDeltaTime;
            if (instance.flightTarget)
            {
                if (instance.flightOwner) instance.age = Mathf.Max(0, instance.flightOwner.SampleTime - instance.flightBirth);
                Vector3 to = instance.flightTarget.position + instance.flightOffset;
                instance.root.transform.position = Vector3.Lerp(instance.flightFrom, to, instance.age / instance.flightSeconds);
                Vector3 direction = to - instance.flightFrom;
                if (direction.sqrMagnitude > .0001f) instance.root.transform.rotation = Quaternion.LookRotation(direction) * instance.prefab.transform.localRotation;
                // Arrival is the authored damage cue; the contact burst takes over immediately.
                if (instance.age >= instance.flightSeconds) { Release(instance); continue; }
            }
            if (instance.follow && instance.age <= instance.followSeconds)
            {
                if (instance.followRenderer)
                {
                    if (!followedBladePoints.TryGetValue(instance.followRenderer, out var point))
                    {
                        point = BladeCenter(instance.followRenderer);
                        followedBladePoints.Add(instance.followRenderer, point);
                    }
                    instance.root.transform.position = point + instance.followRendererOffset;
                }
                else
                {
                    // Body turns must not swing readable throw WHOOSH letters
                    // behind the character; keep their offset toward the camera.
                    instance.root.transform.position = instance.follow.TransformPoint(instance.followLocalPoint) + CameraOffset(instance.followCameraOffset);
                    if (instance.followCameraOffset > 0) instance.root.transform.rotation = FacingCamera() * instance.prefab.transform.localRotation;
                }
            }
            bool alive = false;
            foreach (var particles in instance.particles)
                if (particles && particles.IsAlive(false)) { alive = true; break; }
            if ((!alive && !instance.flightTarget) || instance.age > 4f) Release(instance);
        }
    }

    static void Release(Instance instance)
    {
        foreach (var particles in instance.particles)
            if (particles) particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (instance.root) instance.root.SetActive(false);
        instance.follow = null;
        instance.followRenderer = null;
        instance.flightTarget = null;
        instance.flightOwner = null;
    }

    public void EndSequence(FrankBattlePairPlayback playback, bool interrupted = false)
    {
        if (owner != playback) return;
        owner = null;
        sequence = null;
        attacker = receiver = null;
        if (weaponTrails) weaponTrails.End(playback);
        if (interrupted) ClearEffects();
    }

    public void ClearEffects()
    {
        foreach (var instance in instances) Release(instance);
        if (weaponTrails) weaponTrails.Clear();
        EffectsCleared?.Invoke();
    }

    public void ResetForMatch()
    {
        owner = null;
        sequence = null;
        attacker = receiver = null;
        ownerPlaybackId = -1; IsFinishingContact = false; ContactCount = 0;
        ClearEffects();
    }

    void OnDisable() => ResetForMatch();
    void OnDestroy()
    {
        if (bladeMesh)
        {
            if (Application.isPlaying) Destroy(bladeMesh);
            else DestroyImmediate(bladeMesh);
        }
        if (!poolRoot) return;
        if (Application.isPlaying) Destroy(poolRoot.gameObject);
        else DestroyImmediate(poolRoot.gameObject);
    }
}
