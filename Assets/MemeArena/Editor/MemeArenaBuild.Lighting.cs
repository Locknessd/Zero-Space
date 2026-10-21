using MemeArena;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MemeArenaEditor
{
    public static partial class MemeArenaBuild
    {
        static void BuildLightingAndVfx(MemeArenaStage stage)
        {
            var lighting = Group(stage.transform, "Warm arena lighting and pooled VFX");
            if (settings.includeLighting)
            {
                var sun = Group(lighting, "Arena key light").gameObject.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.color = settings.lighting == MemeArenaLighting.WarmSunset ?
                    new Color(1, .88f, .74f) : new Color(.96f, .97f, 1);
                sun.intensity = .9f;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = .75f;
                sun.transform.localRotation = Quaternion.Euler(44, settings.cameraOnPositiveZ ? 145 : -35, 0);
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "Meme arena restrained comic grade";
                AssetDatabase.CreateAsset(profile, folder + "/ArenaVolume.asset");
                var bloom = profile.Add<Bloom>(true);
                bloom.intensity.Override(.13f);
                bloom.threshold.Override(1.15f);
                bloom.scatter.Override(.45f);
                var grading = profile.Add<ColorAdjustments>(true);
                grading.postExposure.Override(-.1f);
                grading.saturation.Override(5);
                grading.contrast.Override(4);
                foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
                var volume = lighting.gameObject.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 8;
                volume.sharedProfile = profile;
                EditorUtility.SetDirty(profile);
            }
            var flame = new Material(Shader.Find("MemeArena/Soft Particle")) { name = "Stylized warm fire" };
            var confetti = new Material(flame) { name = "Comic confetti shards" };
            confetti.SetFloat("_Softness", 0);
            AssetDatabase.CreateAsset(flame, folder + "/Materials/Torch fire.mat");
            AssetDatabase.CreateAsset(confetti, folder + "/Materials/Confetti.mat");
            for (int side = -1; side <= 1; side += 2)
                for (int row = 0; row < 2; row++)
                {
                    var socket = Group(lighting, "Roman brazier");
                    socket.localPosition = new Vector3(side * (settings.arenaWidth * .5f - 2.2f), 0,
                        settings.RearSign * (3 + row * 5));
                    Shape(socket, "Brazier pillar", PrimitiveType.Cylinder, new Vector3(0, 1.1f, 0),
                        new Vector3(.22f, 1.1f, .22f), gold);
                    Shape(socket, "Brazier bowl", PrimitiveType.Sphere, new Vector3(0, 2.2f, 0),
                        new Vector3(.7f, .35f, .7f), dark);
                    if (row == 0 && settings.torchLights > (side < 0 ? 0 : 1))
                    {
                        var point = socket.gameObject.AddComponent<Light>();
                        point.type = LightType.Point;
                        point.range = 4;
                        point.intensity = .6f;
                        point.color = new Color(1, .55f, .2f);
                        point.shadows = LightShadows.None;
                    }
                    if (!settings.enableVfx) continue;
                    var fire = CreateParticles(socket, "Torch flame", flame, true);
                    fire.transform.localPosition = new Vector3(0, 2.35f, 0);
                    var main = fire.main;
                    main.startColor = new ParticleSystem.MinMaxGradient(new Color(1, .25f, .035f, .7f),
                        new Color(1, .8f, .15f, .8f));
                    main.startSize = new ParticleSystem.MinMaxCurve(.2f, .43f);
                    main.startSpeed = .7f;
                    main.startLifetime = .65f;
                    main.maxParticles = 12;
                    var emission = fire.emission;
                    emission.rateOverTime = 12;
                }
            var celebration = lighting.gameObject.AddComponent<MemeArenaCelebration>();
            celebration.vfxEnabled = settings.enableVfx && settings.enableConfetti;
            var cannons = new ParticleSystem[2];
            for (int i = 0; i < cannons.Length; i++)
            {
                var cannon = CreateParticles(lighting, "Pooled edge confetti " + i, confetti, false);
                var main = cannon.main;
                main.duration = 2;
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.8f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(.035f, .09f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                main.gravityModifier = .25f;
                main.maxParticles = settings.quality == MemeArenaQuality.WebGL ? 32 : 64;
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(new Color(1, .2f, .25f), 0),
                    new GradientColorKey(new Color(1, .75f, .15f), .33f),
                    new GradientColorKey(new Color(.2f, .8f, .9f), .66f),
                    new GradientColorKey(new Color(.9f, .8f, 1), 1) },
                    new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
                main.startColor = new ParticleSystem.MinMaxGradient(gradient)
                    { mode = ParticleSystemGradientMode.RandomColor };
                var emission = cannon.emission;
                emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0, (short)main.maxParticles) });
                cannons[i] = cannon;
            }
            celebration.confetti = cannons;
            stage.crowd.GetComponent<MemeCrowdReactionManager>().celebration = celebration;
        }

        static ParticleSystem CreateParticles(Transform parent, string name, Material material, bool loop)
        {
            var transform = Group(parent, name);
            transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var system = transform.gameObject.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.playOnAwake = loop;
            main.loop = loop;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = loop ? 9 : 18;
            shape.radius = loop ? .04f : .12f;
            var fade = system.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            fade.color = gradient;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return system;
        }
    }
}
