using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordPlayCheck
    {
        static bool pauseActive, pauseResumed, samuraiPresentationResumed;
        static int pausedSamuraiFrames;
        static double samuraiPausedAt;
        static float pausedRecoveryProgress, pausedSourceSample;
        static Transform[] samuraiPausedBones;
        static Vector3[] samuraiPausedPositions;
        static Quaternion[] samuraiPausedRotations;
        static float[] samuraiPausedAnimators;
        static AudioSource[] samuraiPausedVoices;
        static int[] samuraiPausedAudioSamples;
        static readonly List<PausedClock> samuraiPausedClocks = new List<PausedClock>();

        sealed class PausedClock
        {
            public object owner;
            public FieldInfo field;
            public float value;
            public string label;
        }

        static FieldInfo ObservationField(object owner, string name)
        {
            var field = owner.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Require(field != null, "Missing read-only diagnostic field: " + name);
            return field;
        }

        static void RememberClock(object owner, string name, string label)
        {
            var field = ObservationField(owner, name);
            samuraiPausedClocks.Add(new PausedClock
            {
                owner = owner,
                field = field,
                value = (float)field.GetValue(owner),
                label = label
            });
        }

        static void RememberActiveClocks(object owner, string collection, string rootField, string clock)
        {
            var items = (IEnumerable)ObservationField(owner, collection).GetValue(owner);
            foreach (var item in items)
            {
                if (item == null)
                    continue;
                var root = ObservationField(item, rootField).GetValue(item) as GameObject;
                if (root && root.activeInHierarchy)
                    RememberClock(item, clock, collection + "/" + root.name);
            }
        }

        static void ResetSamuraiPause()
        {
            pauseActive = pauseResumed = samuraiPresentationResumed = false;
            pausedSamuraiFrames = 0;
            samuraiPausedClocks.Clear();
        }

        static void ApplySamuraiPause()
        {
            if (pauseResumed)
            {
                var flashClock = samuraiPausedClocks.First(clock => clock.label == "flash");
                samuraiPresentationResumed |= (float)flashClock.field.GetValue(flashClock.owner) > flashClock.value;
                return;
            }
            if (!pauseActive)
            {
                if (!pair.IsRecovering || !targetRecovered)
                    return;
                Require(targetRecoveryProgress * move.sourcePair.getUp.length < move.sourcePair.recoveryBlendSeconds,
                    "Missed the real-frame prone recovery blend window; pause coverage cannot be claimed.");
                samuraiPausedBones = fighters.SelectMany(f =>
                    f.Animator.GetComponentsInChildren<Transform>(true)).ToArray();
                samuraiPausedPositions = samuraiPausedBones.Select(b => b.position).ToArray();
                samuraiPausedRotations = samuraiPausedBones.Select(b => b.rotation).ToArray();
                samuraiPausedAnimators = fighters.Select(f =>
                    f.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime).ToArray();
                pausedSourceSample = pair.SampleTime;
                pausedRecoveryProgress = targetRecoveryProgress;
                samuraiPausedVoices = game.battleSfx.GetComponentsInChildren<AudioSource>(true)
                    .Where(voice => voice.isPlaying).ToArray();
                game.battleSfx.SetMenuPaused(true);
                samuraiPausedAudioSamples = samuraiPausedVoices.Select(voice => voice.timeSamples).ToArray();
                RememberClock(feedback, "flashAge", "flash");
                RememberClock(feedback, "slowAge", "slow motion");
                RememberClock(feedback, "remaining", "hit stop");
                if (shake.IsShaking)
                    RememberClock(shake, "age", "active camera shake");
                RememberActiveClocks(game.battleVfx, "instances", "root", "age");
                RememberActiveClocks(lighting, "pulses", "effect", "elapsed");
                samuraiPausedAt = Now;
                pauseActive = true;
                Time.timeScale = 0;
                report.AppendLine($"PAUSE source={pausedSourceSample:F6} recovery={pausedRecoveryProgress:F6} " +
                    $"presentationClocks={samuraiPausedClocks.Count} liveVoices={samuraiPausedVoices.Length}");
                return;
            }
            Require(Time.timeScale == 0 && pair.IsRecovering &&
                Mathf.Abs(pair.SampleTime - pausedSourceSample) < .00001f,
                "Pause released source ownership or advanced its clock.");
            for (int i = 0; i < fighters.Length; i++)
                Require(Mathf.Abs(fighters[i].Animator.GetCurrentAnimatorStateInfo(0).normalizedTime -
                    samuraiPausedAnimators[i]) < .00001f, "Paused Animator clock advanced.");
            for (int i = 0; i < samuraiPausedBones.Length; i++)
                Require(Vector3.Distance(samuraiPausedBones[i].position, samuraiPausedPositions[i]) < .0001f &&
                    Quaternion.Angle(samuraiPausedBones[i].rotation, samuraiPausedRotations[i]) < .02f,
                    "Paused bone changed: " + samuraiPausedBones[i].name);
            foreach (var clock in samuraiPausedClocks)
            {
                float current = (float)clock.field.GetValue(clock.owner);
                Require(current.Equals(clock.value) || Mathf.Abs(current - clock.value) < .00001f,
                    $"Paused presentation clock advanced: {clock.label} before={clock.value:F6} now={current:F6}");
            }
            for (int i = 0; i < samuraiPausedVoices.Length; i++)
                Require(samuraiPausedVoices[i] && !samuraiPausedVoices[i].isPlaying &&
                    samuraiPausedVoices[i].timeSamples == samuraiPausedAudioSamples[i],
                    "Menu-paused audio voice advanced or resumed early.");
            pausedSamuraiFrames++;
            if (Now - samuraiPausedAt < .25 || pausedSamuraiFrames < 2)
                return;
            Time.timeScale = priorTimeScale;
            game.battleSfx.SetMenuPaused(false);
            Require(samuraiPausedVoices.All(voice => voice && voice.isPlaying),
                "Previously playing audio did not resume with the menu pause clock.");
            pauseActive = false;
            pauseResumed = true;
            report.AppendLine($"RESUME pausedFrames={pausedSamuraiFrames} wallSeconds={Now - samuraiPausedAt:F3} " +
                $"resumedVoices={samuraiPausedVoices.Count(voice => voice && voice.isPlaying)}");
        }
    }
}
