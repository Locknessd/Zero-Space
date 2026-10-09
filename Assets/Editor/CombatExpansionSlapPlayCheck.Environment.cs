using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapPlayCheck
    {
        static Light[] baselineLights;
        static float[] lightIntensity;
        static Color[] lightColors;
        static bool[] lightEnabled;
        static Vector3[] lightPositions;
        static Quaternion[] lightRotations;
        static Volume[] volumes;
        static float[] volumeWeights;
        static VolumeProfile[] volumeProfiles;
        static AudioMixer mixer;
        static AudioMixerUpdateMode mixerMode;
        static float masterVolume;
        static int voicePoolCount;
        static object[] originalEffectPool;
        static RuntimeAnimatorController[] controllers;
        static Transform originalPoolRoot;
        static readonly FieldInfo PoolRootField = typeof(BattleVfxPlayer)
            .GetField("poolRoot", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo InstancesField = typeof(BattleVfxPlayer)
            .GetField("instances", BindingFlags.Instance | BindingFlags.NonPublic);

        static void SaveEnvironment()
        {
            baselineLights = game.gameObject.scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Light>(true)).ToArray();
            lightIntensity = baselineLights.Select(light => light.intensity).ToArray();
            lightColors = baselineLights.Select(light => light.color).ToArray();
            lightEnabled = baselineLights.Select(light => light.enabled).ToArray();
            lightPositions = baselineLights.Select(light => light.transform.position).ToArray();
            lightRotations = baselineLights.Select(light => light.transform.rotation).ToArray();
            volumes = game.gameObject.scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Volume>(true)).ToArray();
            volumeWeights = volumes.Select(volume => volume.weight).ToArray();
            volumeProfiles = volumes.Select(volume => volume.sharedProfile).ToArray();
            mixer = game.battleSfx.bank.mixer;
            if (mixer)
                mixerMode = mixer.updateMode;
            masterVolume = game.battleSfx.masterVolume;
            voicePoolCount = game.battleSfx.PooledVoiceCount;
            Require(InstancesField != null && PoolRootField != null, "VFX pool observation fields unavailable.");
            originalPoolRoot = PoolRootField.GetValue(game.battleVfx) as Transform;
            originalEffectPool = ((IList)InstancesField.GetValue(game.battleVfx)).Cast<object>().ToArray();
            controllers = fighters.Select(f => f.Animator.runtimeAnimatorController).ToArray();
        }

        static void CheckEnvironment()
        {
            Require(game.battleSfx.masterVolume == masterVolume && game.battleSfx.bank.mixer == mixer &&
                (!mixer || mixer.updateMode == mixerMode) && game.battleSfx.PooledVoiceCount == voicePoolCount,
                "Audio mixer configuration, level or prewarmed voice pool changed.");
            var pool = ((IList)InstancesField.GetValue(game.battleVfx)).Cast<object>().ToArray();
            Require(originalEffectPool.All(entry => pool.Contains(entry)), "A baseline effect pool entry was evicted.");
            for (int i = 0; i < volumes.Length; i++)
                Require(volumes[i] && volumes[i].weight == volumeWeights[i] &&
                    volumes[i].sharedProfile == volumeProfiles[i], "Volume configuration changed.");
            for (int i = 0; i < fighters.Length; i++)
                Require(fighters[i].Animator.runtimeAnimatorController == controllers[i] &&
                    fighters[i].gameObject.scene == scenes[i], "Controller or fighter scene changed.");
            for (int i = 0; i < baselineLights.Length; i++)
            {
                var light = baselineLights[i];
                if (lighting.impactLights.Contains(light))
                    continue;
                Require(light && light.enabled == lightEnabled[i] &&
                    Mathf.Abs(light.intensity - lightIntensity[i]) < .001f && light.color == lightColors[i],
                    "Baseline battle light did not recover: " + light?.name);
            }
        }

        static void RestoreEnvironment()
        {
            // ClearEffects has released every instance. Remove only pool entries created by this validation.
            var pool = (IList)InstancesField.GetValue(game.battleVfx);
            for (int i = pool.Count - 1; i >= 0; i--)
            {
                var entry = pool[i];
                if (originalEffectPool.Contains(entry))
                    continue;
                var rootField = entry.GetType().GetField("root", BindingFlags.Instance | BindingFlags.Public);
                var root = rootField?.GetValue(entry) as GameObject;
                if (root)
                    Object.Destroy(root);
                pool.RemoveAt(i);
            }
            for (int i = 0; i < baselineLights.Length; i++)
                if (baselineLights[i])
                {
                    var light = baselineLights[i];
                    light.intensity = lightIntensity[i];
                    light.color = lightColors[i];
                    light.enabled = lightEnabled[i];
                    light.transform.SetPositionAndRotation(lightPositions[i], lightRotations[i]);
                }
            for (int i = 0; i < volumes.Length; i++)
                if (volumes[i])
                {
                    volumes[i].weight = volumeWeights[i];
                    volumes[i].sharedProfile = volumeProfiles[i];
                }
            if (mixer)
                mixer.updateMode = mixerMode;
            game.battleSfx.masterVolume = masterVolume;
            Require(pool.Cast<object>().SequenceEqual(originalEffectPool),
                "Cleanup changed the baseline effect pool entries or order.");
            if (!originalPoolRoot)
            {
                var created = PoolRootField.GetValue(game.battleVfx) as Transform;
                if (created)
                    Object.Destroy(created.gameObject);
                PoolRootField.SetValue(game.battleVfx, null);
            }
        }
    }
}
