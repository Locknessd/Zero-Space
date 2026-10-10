using System;
using System.Collections.Generic;
using FrankRetarget;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed partial class BattleSfxPlayer : MonoBehaviour
{
    public BattleSfxBank bank;
    [Range(0f, 1f)] public float masterVolume = .95f;
    public bool enableAnnouncer = true;
    public bool enableHurtVoices;
    public bool enableUiSounds = true;
    public bool enableContactLayers = true;
    [Range(4, 24)] public int maxVoices = 12;
    [Tooltip("Use the same cosmetic variation on each match reset for comparisons.")]
    public bool reproducibleVariation;
    public int variationSeed = 1979;
    public Button[] uiButtons = Array.Empty<Button>();

    // Observable without audio hardware by the existing Editor validation jobs.
    public event Action<string, AudioClip> CuePlayed;
    public int PlayedCueCount { get; private set; }
    public int DroppedCueCount { get; private set; }
    public int PooledVoiceCount => voices.Count + (announcer ? 1 : 0);
    public int ActiveVoiceCount
    {
        get
        {
            int count = announcer && announcer.isPlaying ? 1 : 0;
            foreach (var source in voices) if (source && source.isPlaying) count++;
            return count;
        }
    }

    System.Random random = new System.Random();
    readonly Dictionary<string, int> lastVariants = new Dictionary<string, int>();
    readonly List<AudioSource> voices = new List<AudioSource>();
    readonly Dictionary<AudioSource, double> voiceStarts = new Dictionary<AudioSource, double>();
    readonly Dictionary<AudioSource, string> voiceGroups = new Dictionary<AudioSource, string>();
    readonly Dictionary<AudioSource, float> voiceGains = new Dictionary<AudioSource, float>();
    readonly HashSet<ulong> confirmedEvents = new HashSet<ulong>();
    BattleVfxPlayer contactPublisher;
    readonly HashSet<AudioSource> suspendedVoices = new HashSet<AudioSource>();
    FrankBattlePairPlayback owner;
    BattleSfxBank.Move sequence;
    int nextCue, ownerPlaybackId = -1;
    bool lethal, recoveryPlayed, fightPlayed, victoryPlayed, knockoutPlayed, menuPaused;
    float highWaterTime;
    AudioSource announcer;
    float lastUiClick = -1, previousMasterVolume = -1;

    void OnEnable()
    {
        foreach (var button in uiButtons)
            if (button) button.onClick.AddListener(PlayUiClick);
        BindContactEvents();
        if (Application.isPlaying) Prewarm();
    }

    public void BindContactEvents()
    {
        var publisher = GetComponent<BattleVfxPlayer>();
        if (contactPublisher == publisher) return;
        if (contactPublisher) contactPublisher.ContactOccurred -= ConfirmedContact;
        contactPublisher = publisher;
        if (contactPublisher) contactPublisher.ContactOccurred += ConfirmedContact;
    }

    bool UsesContactEvents => contactPublisher && contactPublisher.isActiveAndEnabled && contactPublisher.timeline == bank;

    void ConfirmedContact(BattleVfxPlayer.Impact impact)
    {
        if (!UsesContactEvents || impact.playback != owner || sequence == null ||
            !confirmedEvents.Add(impact.eventId)) return;
        var cue = impact.cue;
        if (cue != null) PlayTimelineCue(cue);
    }

    static bool IsContactCue(BattleSfxBank.Cue cue) => cue.group == "light_hit" || cue.group == "heavy_hit" ||
        cue.group == "stab_hit" || cue.group == "body_fall" || cue.group == "knockout_fall";

    public void BeginSequence(FrankBattlePairPlayback playback, CombatTripletData move, bool isLethal)
    {
        if (!isActiveAndEnabled || !bank || !playback) return;
        if (owner == playback && ownerPlaybackId == playback.PlaybackId && sequence != null) return;
        owner = playback;
        ownerPlaybackId = playback.PlaybackId;
        sequence = playback.PresentationProfile(bank);
        nextCue = 0;
        confirmedEvents.Clear();
        BindContactEvents();
        highWaterTime = -1f;
        lethal = isLethal;
        recoveryPlayed = false;
        PlayFightOnce();
        if (sequence == null) Debug.LogWarning("Battle SFX: missing cue timeline for " + move.moveName, this);
    }

    public void AdvanceSequence(FrankBattlePairPlayback playback, float seconds)
    {
        if (!isActiveAndEnabled || owner != playback || sequence == null ||
            !float.IsFinite(seconds) || seconds <= highWaterTime) return;
        highWaterTime = seconds;
        while (nextCue < sequence.cues.Length && sequence.cues[nextCue].seconds <= seconds)
        {
            var cue = sequence.cues[nextCue++];
            if (!UsesContactEvents || !IsContactCue(cue)) PlayTimelineCue(cue);
        }
    }

    public void ReplaceSequence(FrankBattlePairPlayback playback, BattleSfxBank.Move profile, float seconds)
    {
        if (owner != playback || ownerPlaybackId != playback.PlaybackId || profile == null) return;
        sequence = profile;
        nextCue = 0;
        while (nextCue < sequence.cues.Length && sequence.cues[nextCue].seconds <= seconds) nextCue++;
        highWaterTime = seconds;
    }

    void PlayTimelineCue(BattleSfxBank.Cue cue)
    {
        if (cue == null) return;
        string id = lethal && cue.finalLanding ? "knockout_fall" : bank.ResolveWeaponGroup(owner.Move, cue, sequence);
        PlayGroup(id);
        if (enableHurtVoices && (cue.group == "heavy_hit" || cue.group == "stab_hit" || cue.group == "light_hit"))
            PlayGroup("hurt_voice");
    }

    public void BeginRecovery(FrankBattlePairPlayback playback, bool allowLethalAttacker = false)
    {
        if (owner != playback || lethal && !allowLethalAttacker || recoveryPlayed) return;
        recoveryPlayed = true;
        PlayGroup("getup");
    }

    public void EndSequence(FrankBattlePairPlayback playback, bool interrupted = false)
    {
        if (owner != playback) return;
        owner = null;
        sequence = null;
        ownerPlaybackId = -1;
        if (interrupted) StopVoices();
    }

    public void PlayFightOnce()
    {
        if (fightPlayed) return;
        fightPlayed = true;
        if (enableAnnouncer) PlayGroup("fight_start", true);
    }

    public void PlayVictoryOnce()
    {
        if (victoryPlayed) return;
        victoryPlayed = true;
        if (enableAnnouncer) PlayGroup("victory", true);
    }

    public void PlayUiClick()
    {
        if (Application.isPlaying && Time.unscaledTime - lastUiClick < .06f) return;
        lastUiClick = Time.unscaledTime;
        if (enableUiSounds) PlayGroup("ui_click");
    }

    public void PlayKnockoutOnce()
    {
        if (knockoutPlayed) return;
        knockoutPlayed = true;
        PlayGroup("ko_impact");
    }

    public void ResetForMatch()
    {
        owner = null;
        sequence = null;
        ownerPlaybackId = -1;
        fightPlayed = victoryPlayed = recoveryPlayed = knockoutPlayed = false;
        lastUiClick = -1;
        lastVariants.Clear();
        confirmedEvents.Clear();
        if (reproducibleVariation) random = new System.Random(variationSeed);
        StopVoices();
    }

    void PlayGroup(string id, bool isAnnouncement = false, bool allowLayers = true)
    {
        if (!isActiveAndEnabled || !bank || string.IsNullOrEmpty(id) || menuPaused && id != "ui_click") return;
        var group = bank.FindGroup(id);
        if (group?.clips == null || group.clips.Length == 0) return;
        int index;
        // Audio variation never consumes the gameplay random stream.
        if (group.clips.Length > 1 && lastVariants.TryGetValue(id, out int last))
        {
            index = random.Next(group.clips.Length - 1);
            if (index >= last) index++;
        }
        else index = random.Next(group.clips.Length);
        var clip = group.clips[index];
        if (!clip) return;
        int priority = group.priority > 0 ? group.priority : DefaultPriority(id, isAnnouncement);
        float gain = group.clipGains != null && index < group.clipGains.Length ? group.clipGains[index] : 1f;
        float variation = (float)(random.NextDouble() * 2 - 1) * Mathf.Clamp(group.gainVariationDb, 0, 2);
        float level = Mathf.Clamp01(group.volume * Mathf.Max(0, gain) * Mathf.Pow(10, variation / 20f));
        if (Application.isPlaying)
        {
            var source = GetVoice(isAnnouncement, id, priority, Mathf.Clamp(group.maxConcurrent, 1, 8));
            if (!source)
            {
                DroppedCueCount++;
                return;
            }
            source.Stop();
            suspendedVoices.Remove(source);
            source.clip = clip;
            source.outputAudioMixerGroup = group.output;
            source.priority = priority;
            source.volume = Mathf.Clamp01(masterVolume * level);
            source.pitch = Mathf.Clamp(
                Mathf.Lerp(group.pitch.x, group.pitch.y, (float)random.NextDouble()), .5f, 2f);
            float offset = group.startOffsets != null && index < group.startOffsets.Length
                ? group.startOffsets[index] : 0;
            source.timeSamples = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(0, offset) * clip.frequency),
                0, Mathf.Max(0, clip.samples - 1));
            voiceStarts[source] = AudioSettings.dspTime;
            voiceGroups[source] = id;
            voiceGains[source] = level;
            source.Play();
        }
        lastVariants[id] = index;
        PlayedCueCount++;
        CuePlayed?.Invoke(id, clip);
        if (allowLayers && enableContactLayers && group.layers != null)
            foreach (string layer in group.layers)
                if (!string.IsNullOrEmpty(layer) && layer != id) PlayGroup(layer, false, false);
    }

    static int DefaultPriority(string id, bool announcement) => announcement ? 32 : id == "ko_impact" ? 48 :
        id.EndsWith("hit", StringComparison.Ordinal) || id.EndsWith("fall", StringComparison.Ordinal) ? 64 :
        id.EndsWith("swing", StringComparison.Ordinal) ? 100 : id == "hurt_voice" ? 56 : 160;

    void OnDisable()
    {
        foreach (var button in uiButtons) if (button) button.onClick.RemoveListener(PlayUiClick);
        if (contactPublisher) contactPublisher.ContactOccurred -= ConfirmedContact;
        contactPublisher = null;
        menuPaused = false;
        ResetForMatch();
    }
}
