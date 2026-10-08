using FrankRetarget;
using UnityEngine;

/// <summary>Small visual recoil layered over the authored reaction, with no root-position drift.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(13200)]
public sealed class BattleKnockbackFeedback : MonoBehaviour
{
    public BattleVfxPlayer vfx;
    [Range(0, .15f)] public float lightDistance = .055f;
    [Range(0, .15f)] public float heavyDistance = .10f;
    [Range(.04f, .25f)] public float lightSeconds = .12f;
    [Range(.04f, .25f)] public float heavySeconds = .18f;
    public int RecoilCount { get; private set; }
    public bool IsRecoiling => hips && owner && owner.Playing && age < duration;
    public Vector3 CurrentOffset { get; private set; }

    BattleVfxPlayer subscribed;
    FrankBattlePairPlayback owner;
    Transform hips;
    float age, duration, distance, direction;
    Vector3 basePosition, appliedPosition;
    bool applied;

    public void Bind()
    {
        if (subscribed == vfx) return;
        Unbind(); subscribed = vfx;
        if (!subscribed) return;
        subscribed.ContactOccurred += Contact;
        subscribed.SequenceBegan += BeginSequence;
        subscribed.EffectsCleared += ResetRecoil;
    }

    void BeginSequence(CharacterCombat fighter, CombatTripletData move, bool lethal) => ResetRecoil();

    void Contact(BattleVfxPlayer.Impact impact)
    {
        if (impact.kind == BattleVfxPlayer.ContactKind.Ground) { ResetRecoil(); return; }
        if (!impact.playback || !impact.receiver || !impact.receiver.Animator || !impact.attacker) return;
        var profile = impact.playback.UsesLethalReceiverVariant
            ? impact.playback.PresentationProfile(vfx.timeline) : vfx.timeline ? vfx.timeline.FindMove(impact.move) : null;
        // Grabs/throws already place both bodies precisely; preserve their authored contact.
        if (profile != null)
            foreach (var cue in profile.cues) if (cue.damageOnLanding) return;

        RestoreOffset();
        hips = impact.receiver.Animator.GetBoneTransform(HumanBodyBones.Hips);
        var sourceHips = impact.attacker.Animator.GetBoneTransform(HumanBodyBones.Hips);
        if (!hips || !sourceHips) return;
        float separation = hips.position.x - sourceHips.position.x;
        if (Mathf.Abs(separation) < .001f)
            separation = impact.receiver.transform.position.x - impact.attacker.transform.position.x;
        direction = separation < 0 ? -1 : 1;
        distance = impact.kind == BattleVfxPlayer.ContactKind.Heavy ? heavyDistance : lightDistance;
        duration = impact.kind == BattleVfxPlayer.ContactKind.Heavy ? heavySeconds : lightSeconds;
        // Settle before the next physical contact, keeping weapon/hand contact alignment.
        if (profile != null)
            foreach (var cue in profile.cues)
                if (cue.seconds > impact.seconds + .001f &&
                    (cue.group == "light_hit" || cue.group == "heavy_hit" || cue.group == "stab_hit" || cue.damageOnLanding))
                { duration = Mathf.Min(duration, (cue.seconds - impact.seconds) * .8f); break; }
        duration = Mathf.Min(duration, Mathf.Max(0, impact.playback.Duration - impact.seconds - .001f));
        owner = impact.playback; age = 0;
        if (distance > 0 && duration > 0) RecoilCount++;
    }

    public void AdvanceRecoil(float scaledDelta) => age += Mathf.Max(0, scaledDelta);

    public void ApplyRecoil()
    {
        RestoreOffset();
        if (!IsRecoiling || !owner.ReceiverActor) { ResetRecoil(); return; }
        float progress = Mathf.Clamp01(age / duration);
        // Fast displacement, then a smooth return to the moving animation pose.
        const float peak = .22f;
        float envelope = progress < peak ? Mathf.SmoothStep(0, 1, progress / peak) :
            1 - Mathf.SmoothStep(0, 1, (progress - peak) / (1 - peak));
        CurrentOffset = Vector3.right * (direction * distance * envelope);
        basePosition = hips.position;
        appliedPosition = basePosition + CurrentOffset;
        hips.position = appliedPosition;
        applied = true;
    }

    public void RestoreOffset()
    {
        // Pose sampling may have replaced the previous visual pose already.
        if (applied && hips && (hips.position - appliedPosition).sqrMagnitude < .0000001f)
            hips.position = basePosition;
        applied = false; CurrentOffset = Vector3.zero;
    }

    public void ResetRecoil()
    {
        RestoreOffset(); owner = null; hips = null; age = duration = 0;
    }

    void Unbind()
    {
        if (subscribed)
        {
            subscribed.ContactOccurred -= Contact;
            subscribed.SequenceBegan -= BeginSequence;
            subscribed.EffectsCleared -= ResetRecoil;
        }
        subscribed = null;
    }
    void OnEnable() => Bind();
    // Remove the previous visual offset before source playback preserves its final root X.
    void Update() { RestoreOffset(); AdvanceRecoil(Time.deltaTime * (owner ? owner.PresentationRate : 1)); }
    // Retargeted bones have settled; the cinematic camera evaluates afterwards.
    void LateUpdate() => ApplyRecoil();
    void OnDisable() { Unbind(); ResetRecoil(); }
    void OnDestroy() { Unbind(); ResetRecoil(); }
}
