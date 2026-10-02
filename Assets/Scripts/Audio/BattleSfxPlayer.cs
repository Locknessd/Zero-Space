using System;
using System.Collections.Generic;
using FrankRetarget;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class BattleSfxPlayer : MonoBehaviour
{
    public BattleSfxBank bank;
    [Range(0f, 1f)] public float masterVolume = .85f;
    public bool enableAnnouncer = true;
    public bool enableHurtVoices;
    public bool enableUiSounds = true;
    public Button[] uiButtons = Array.Empty<Button>();

    // Also observable without audio output by the Editor validation job.
    public event Action<string, AudioClip> CuePlayed;
    public int PlayedCueCount { get; private set; }

    readonly System.Random random = new System.Random();
    readonly Dictionary<string, int> lastVariants = new Dictionary<string, int>();
    readonly List<AudioSource> voices = new List<AudioSource>();
    FrankBattlePairPlayback owner;
    BattleSfxBank.Move sequence;
    int nextCue;
    bool lethal, recoveryPlayed, fightPlayed, victoryPlayed;
    float highWaterTime;
    AudioSource announcer;

    void OnEnable()
    {
        foreach (var button in uiButtons)
            if (button) button.onClick.AddListener(PlayUiClick);
    }

    public void BeginSequence(FrankBattlePairPlayback playback, CombatTripletData move, bool isLethal)
    {
        if (!isActiveAndEnabled || !bank) return;
        if (owner == playback && sequence != null) return;
        owner = playback;
        sequence = bank.FindMove(move);
        nextCue = 0;
        highWaterTime = -1f;
        lethal = isLethal;
        recoveryPlayed = false;
        PlayFightOnce();
        if (sequence == null)
            Debug.LogWarning("Battle SFX: missing cue timeline for " + move.moveName, this);
    }

    public void AdvanceSequence(FrankBattlePairPlayback playback, float seconds)
    {
        if (!isActiveAndEnabled || owner != playback || sequence == null ||
            !float.IsFinite(seconds) || seconds <= highWaterTime) return;
        highWaterTime = seconds;
        // Consume every crossed cue, including frames that skip past an exact timestamp.
        // The cursor never rewinds on repeated evaluation or a backwards preview seek.
        while (nextCue < sequence.cues.Length && sequence.cues[nextCue].seconds <= seconds)
        {
            var cue = sequence.cues[nextCue++];
            string id = lethal && cue.finalLanding ? "knockout_fall" : cue.group;
            PlayGroup(id);
            if (enableHurtVoices && (id == "heavy_hit" || id == "stab_hit" || id == "light_hit"))
                PlayGroup("hurt_voice");
        }
    }

    public void BeginRecovery(FrankBattlePairPlayback playback)
    {
        if (owner != playback || lethal || recoveryPlayed) return;
        recoveryPlayed = true;
        PlayGroup("getup");
    }

    public void EndSequence(FrankBattlePairPlayback playback, bool interrupted = false)
    {
        if (owner != playback) return;
        owner = null;
        sequence = null;
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
        if (enableUiSounds) PlayGroup("ui_click");
    }

    public void ResetForMatch()
    {
        owner = null;
        sequence = null;
        fightPlayed = victoryPlayed = recoveryPlayed = false;
        StopVoices();
    }

    void PlayGroup(string id, bool isAnnouncement = false)
    {
        if (!isActiveAndEnabled || !bank) return;
        var group = bank.FindGroup(id);
        if (group == null || group.clips == null || group.clips.Length == 0) return;
        int index;
        // Audio variation must not consume Unity's combat move random stream.
        if (group.clips.Length > 1 && lastVariants.TryGetValue(id, out int last))
        {
            index = random.Next(group.clips.Length - 1);
            if (index >= last) index++;
        }
        else index = random.Next(group.clips.Length);
        var clip = group.clips[index];
        if (!clip) return;
        lastVariants[id] = index;
        float gain = index < group.clipGains.Length ? group.clipGains[index] : 1f;
        if (Application.isPlaying)
        {
            var source = GetVoice(isAnnouncement);
            source.Stop();
            source.clip = clip;
            source.volume = Mathf.Clamp01(masterVolume * group.volume * gain);
            source.pitch = Mathf.Lerp(group.pitch.x, group.pitch.y, (float)random.NextDouble());
            source.Play();
        }
        PlayedCueCount++;
        CuePlayed?.Invoke(id, clip);
    }

    AudioSource GetVoice(bool announcement)
    {
        if (announcement)
        {
            if (!announcer) announcer = CreateVoice("Battle announcer");
            return announcer;
        }
        foreach (var source in voices)
            if (!source.isPlaying) return source;
        if (voices.Count < 12)
        {
            var source = CreateVoice("Battle SFX " + voices.Count);
            voices.Add(source);
            return source;
        }
        // Saturation cannot cut off the announcer; reuse the oldest pooled voice.
        var reused = voices[0];
        voices.RemoveAt(0);
        voices.Add(reused);
        return reused;
    }

    AudioSource CreateVoice(string label)
    {
        var child = new GameObject(label);
        child.transform.SetParent(transform, false);
        var source = child.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        return source;
    }

    void StopVoices()
    {
        foreach (var source in voices) if (source) source.Stop();
        if (announcer) announcer.Stop();
    }

    void OnDisable()
    {
        foreach (var button in uiButtons)
            if (button) button.onClick.RemoveListener(PlayUiClick);
        ResetForMatch();
    }
}
