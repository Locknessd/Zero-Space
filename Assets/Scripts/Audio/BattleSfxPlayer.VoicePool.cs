using UnityEngine;
using UnityEngine.Audio;

public sealed partial class BattleSfxPlayer
{
    public void Prewarm()
    {
        if (!Application.isPlaying) return;
        if (bank)
        {
            if (bank.mixer) bank.mixer.updateMode = AudioMixerUpdateMode.UnscaledTime;
            foreach (var group in bank.groups)
                if (group?.clips != null)
                    foreach (var clip in group.clips)
                        if (clip && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        }
        int limit = Mathf.Clamp(maxVoices, 4, 24);
        while (voices.Count < limit) voices.Add(CreateVoice("Battle SFX " + voices.Count));
        if (!announcer) announcer = CreateVoice("Battle announcer");
    }

    public void SetMenuPaused(bool paused)
    {
        if (menuPaused == paused) return;
        menuPaused = paused;
        if (paused)
        {
            foreach (var source in voices) Suspend(source);
            Suspend(announcer);
        }
        else
        {
            foreach (var source in suspendedVoices) if (source) source.UnPause();
            suspendedVoices.Clear();
        }
    }

    void Suspend(AudioSource source)
    {
        if (!source || !source.isPlaying) return;
        source.Pause();
        suspendedVoices.Add(source);
    }

    AudioSource GetVoice(bool announcement, string id, int priority, int cueLimit)
    {
        if (announcement)
        {
            if (!announcer) announcer = CreateVoice("Battle announcer");
            return announcer;
        }
        int matching = 0;
        AudioSource oldestInGroup = null, free = null, expendable = null;
        foreach (var source in voices)
        {
            if (!source) continue;
            if (!source.isPlaying && !suspendedVoices.Contains(source))
            {
                if (!free) free = source;
                continue;
            }
            if (suspendedVoices.Contains(source)) continue;
            if (voiceGroups.TryGetValue(source, out string playingId) && playingId == id)
            {
                matching++;
                if (!oldestInGroup || voiceStarts[source] < voiceStarts[oldestInGroup]) oldestInGroup = source;
            }
            if (!expendable || source.priority > expendable.priority ||
                source.priority == expendable.priority && voiceStarts[source] < voiceStarts[expendable]) expendable = source;
        }
        if (matching >= cueLimit) return oldestInGroup;
        if (free) return free;
        if (voices.Count < Mathf.Clamp(maxVoices, 4, 24))
        {
            var source = CreateVoice("Battle SFX " + voices.Count);
            voices.Add(source);
            return source;
        }
        // A new fabric or swing cue must never evict a more important contact or voice.
        return expendable && expendable.priority >= priority ? expendable : null;
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

    void Update()
    {
        if (Mathf.Approximately(previousMasterVolume, masterVolume)) return;
        previousMasterVolume = masterVolume;
        foreach (var entry in voiceGains) if (entry.Key) entry.Key.volume = Mathf.Clamp01(masterVolume * entry.Value);
    }

    void StopVoices()
    {
        foreach (var source in voices)
        {
            if (!source) continue;
            source.Stop();
            source.clip = null;
            source.pitch = 1;
        }
        if (announcer)
        {
            announcer.Stop();
            announcer.clip = null;
            announcer.pitch = 1;
        }
        suspendedVoices.Clear();
        voiceStarts.Clear();
        voiceGroups.Clear();
        voiceGains.Clear();
    }
}
