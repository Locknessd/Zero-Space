using System.Collections.Generic;
using FrankRetarget;
using UnityEngine;
using UnityEngine.UI;

namespace ZeroSpace.Showcase
{
    public sealed partial class PepeShowcasePresentation : MonoBehaviour
    {
        public Text heading;
        public Text takeTitle;
        public Text counter;
        public Text introTitle;
        public CanvasGroup intro;
        public Image progress;
        public GameObject muzzlePrefab;
        public Material trailMaterial;
        readonly List<AudioSource> voices = new List<AudioSource>();
        readonly List<Flash> flashes = new List<Flash>();
        GameObject currentMuzzlePrefab;
        public Vector3 LastMuzzlePosition { get; private set; }
        public Vector3 LastMuzzleDirection { get; private set; }

        sealed class Flash
        {
            public GameObject root;
            public float age;
            public float lifetime;
            public Sweep follow;
            public ParticleSystem[] particles;
        }

        public void Display(PepeAttackShowcase showcase)
        {
            heading.text = showcase.CharacterName + " / ATTACK SHOWCASE";
            takeTitle.text = showcase.library.takes[showcase.TakeIndex].title;
            counter.text = (showcase.TakeIndex + 1).ToString("00") + " / " +
                showcase.library.takes.Length.ToString("00");
            var track = showcase.library.takes[showcase.TakeIndex].attackTrack;
            if (track)
            {
                int step = 0;
                while (step + 1 < track.steps.Length && showcase.AttackTime >= track.steps[step + 1].seconds)
                    step++;
                counter.text = (step + 1).ToString("00") + " / " + track.steps.Length.ToString("00");
            }
            progress.fillAmount = showcase.SequenceTime / showcase.library.Duration;
            float time = showcase.SequenceTime;
            float end = showcase.library.Duration - showcase.library.outroSeconds;
            intro.alpha = time < showcase.library.introSeconds
                ? 1 - Mathf.SmoothStep(0, 1, time / showcase.library.introSeconds)
                : Mathf.SmoothStep(0, 1, (time - end) / showcase.library.outroSeconds);
            introTitle.text = showcase.CharacterName + "\nATTACK SHOWCASE";
            if (showcase.library.bomTrailer)
            {
                heading.text = "BOM / Battle of meme";
                introTitle.text = "BOM\nBattle of meme";
            }
        }

        public void ResetTake(FrankTestActor actor, PepeShowcaseLibrary.Take take,
            PepeShowcaseLibrary library, Camera camera)
        {
            foreach (var flash in flashes)
                if (flash.root) Remove(flash.root);
            flashes.Clear();
            ActiveParticleCount = 0;
            ActiveMuzzleParticleCount = 0;
            foreach (var source in voices)
                if (source) source.Stop();
            currentMuzzlePrefab = library.muzzlePrefab ? library.muzzlePrefab : muzzlePrefab;
            ResetWeaponTrails(actor, take, library, camera);
        }

        public void Trigger(PepeShowcaseLibrary.Cue cue, bool audible, FrankTestActor actor)
        {
            if (cue.swing && !currentTake.nativeWeaponEffects) SpawnSlash(cue);
            if (cue.gun && currentMuzzlePrefab) SpawnMuzzle(actor);
            if (audible)
                foreach (var sound in cue.sounds)
                    Play(sound);
        }

        void Play(PepeShowcaseLibrary.Sound sound)
        {
            if (!sound.clip) return;
            var source = voices.Find(voice => voice && !voice.isPlaying);
            if (!source)
            {
                source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0;
                voices.Add(source);
            }
            source.clip = sound.clip;
            source.volume = sound.volume;
            source.pitch = sound.pitch;
            source.timeSamples = Mathf.Clamp(Mathf.RoundToInt(sound.offset * sound.clip.frequency),
                0, sound.clip.samples - 1);
            source.Play();
        }

        void AddFlash(GameObject prefab, Vector3 position, Quaternion rotation, float scale,
            float lifetime, Sweep follow = null)
        {
            var root = Instantiate(prefab);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, gameObject.scene);
            root.SetActive(true);
            root.transform.SetPositionAndRotation(position, rotation * prefab.transform.localRotation);
            root.transform.localScale = prefab.transform.localScale * scale;
            var systems = root.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var particles in systems)
            {
                var main = particles.main;
                main.stopAction = ParticleSystemStopAction.None;
                particles.useAutoRandomSeed = false;
                particles.randomSeed = 42;
                particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            flashes.Add(new Flash { root = root, lifetime = lifetime, follow = follow, particles = systems });
        }

        public void Step(float deltaTime, FrankTestActor actor, float seconds)
        {
            DrawWeaponTrails(seconds);
            DrawMotion(seconds);
            ActiveParticleCount = 0;
            ActiveMuzzleParticleCount = 0;
            for (int i = flashes.Count - 1; i >= 0; i--)
            {
                var flash = flashes[i];
                flash.age += deltaTime;
                if (flash.age > flash.lifetime)
                {
                    Remove(flash.root);
                    flashes.RemoveAt(i);
                    continue;
                }
                if (flash.follow != null && flash.follow.blade && flash.age < .12f)
                {
                    Segment(flash.follow, out var bladeBase, out var tip);
                    flash.root.transform.position = Vector3.Lerp(bladeBase, tip, .6f);
                }
                foreach (var particles in flash.particles)
                {
                    particles.Simulate(flash.age, false, true, false);
                    ActiveParticleCount += particles.particleCount;
                    if (flash.follow == null) ActiveMuzzleParticleCount += particles.particleCount;
                }
            }
        }

        static Renderer FindGun(FrankTestActor actor)
        {
            foreach (var renderer in actor.Pose.weaponRenderers)
                if (renderer && renderer.name.ToLowerInvariant().Contains("gun")) return renderer;
            return null;
        }

        static void Remove(Object value)
        {
            if (value is GameObject root) root.SetActive(false);
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        void OnDestroy()
        {
            ClearMotion();
            ClearWeaponTrails();
            foreach (var flash in flashes)
                if (flash.root) Remove(flash.root);
            flashes.Clear();
        }
    }
}
