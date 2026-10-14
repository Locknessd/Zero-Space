using System;
using FrankRetarget;
using UnityEngine;

namespace ZeroSpace.Showcase
{
    [DefaultExecutionOrder(14000)]
    public sealed partial class PepeAttackShowcase : MonoBehaviour
    {
        public PepeShowcaseLibrary library;
        public Animator character;
        public GameObject customCharacterPrefab;
        public PepeShowcaseCamera director;
        public PepeShowcasePresentation presentation;
        public bool autoPlay = true;
        public bool loop = true;
        public bool showControls = true;
        [Range(.25f, 1.5f)] public float speed = 1;
        public int CharacterIndex { get; private set; }
        public int TakeIndex { get; private set; } = -1;
        public float SequenceTime { get; private set; }
        public float AttackTime { get; private set; }
        public bool Running { get; set; }
        public FrankTestActor Actor => actor;
        public string CharacterName => CharacterIndex == 1 ? "MANKEY" : CharacterIndex == 2 ? "CUSTOM" : "PEPE";
        FrankTestActor actor;
        FrankTestDriver customDriver;
        Transform[] savedBones;
        Vector3[] savedPositions;
        Quaternion[] savedRotations;
        int nextCue;
        bool ready;
        GameObject characterRoot;

        void Start()
        {
            Initialize();
            Running = autoPlay;
        }

        public void Initialize()
        {
            if (ready) return;
            if (!library || library.takes.Length == 0 || !character || !character.isHuman)
                throw new InvalidOperationException("Showcase needs attacks and one Humanoid character.");
            characterRoot = character.transform.root.gameObject;
            SaveCharacterPose();
            ready = true;
            PrepareContinuousSequence();
            Evaluate(0, 0, false);
        }

        void LateUpdate()
        {
            if (capturingFrames)
            {
                Evaluate(captureFrame / (float)captureRate, 1f / captureRate, false);
                return;
            }
            if (!Application.isPlaying || !Running || !ready) return;
            float time = SequenceTime + Time.deltaTime * speed;
            if (time >= library.Duration && loop)
            {
                time %= library.Duration;
                TakeIndex = -1;
            }
            else if (time >= library.Duration)
                Running = false;
            Evaluate(time, Time.deltaTime * speed, true);
        }

        public void Evaluate(float seconds, float deltaTime, bool audible)
        {
            if (!ready) Initialize();
            SequenceTime = Mathf.Clamp(seconds, 0, library.Duration);
            if (EvaluateTrailerIntro()) return;
            int selected = library.takes.Length - 1;
            for (int i = 0; i < library.takes.Length; i++)
                if (SequenceTime < library.TakeStart(i) + library.takes[i].Duration +
                    library.leadSeconds + library.holdSeconds)
                {
                    selected = i;
                    break;
                }
            if (TakeIndex != selected)
                BeginTake(selected);
            var take = library.takes[selected];
            float local = SequenceTime - library.TakeStart(selected) - library.leadSeconds;
            AttackTime = Mathf.Clamp(local, 0, take.Duration);
            SampleAttackPose(AttackTime);
            while (nextCue < take.cues.Length && take.cues[nextCue].seconds <= AttackTime && local >= 0)
            {
                var cue = take.cues[nextCue++];
                presentation.Trigger(cue, audible, actor);
            }
            presentation.Step(deltaTime, actor, AttackTime);
            director.Apply(this, deltaTime);
            presentation.Display(this);
        }

        void BeginTake(int index)
        {
            ReleaseActor();
            RestoreCharacterPose();
            TakeIndex = index;
            nextCue = 0;
            var take = library.takes[index];
            var source = CharacterIndex == 1 ? take.mankeyDriver : take.pepeDriver;
            if (CharacterIndex == 2)
            {
                customDriver = Instantiate(source);
                customDriver.gameObject.name = "Custom Humanoid calibration";
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(customDriver.gameObject, gameObject.scene);
                PepeShowcaseRetarget.Adapt(customDriver, character);
                customDriver.gameObject.SetActive(false);
                source = customDriver;
            }
            var root = new GameObject("Attack source skeleton (body hidden)");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, gameObject.scene);
            root.transform.SetPositionAndRotation(character.transform.position, character.transform.rotation);
            actor = root.AddComponent<FrankTestActor>();
            actor.characterName = CharacterName;
            actor.character = character;
            actor.ConfigureSource(source, take.animation, true, true, false, preserveCharacterScale: true);
            actor.activeDriver.gameObject.SetActive(true);
            if (take.attackTrack)
                actor.SetAttackTrack(take.attackTrack);
            AlignContinuousTake(index);
            foreach (var skin in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                skin.updateWhenOffscreen = true;
            presentation.ResetTake(actor, take, library, director.output);
            director.PrepareTake(this);
            presentation.PrepareMotion(this);
            presentation.PreparePowerSlashes(this);
            presentation.PrepareMuzzleBursts(this);
        }

        public void SampleAttackPose(float seconds)
        {
            var take = library.takes[TakeIndex];
            actor.Evaluate(take.sourceStartSeconds + seconds);
            ApplyGrounding(take, take.sourceStartSeconds + seconds);
            BlendContinuousEntry(seconds);
        }

        void ApplyGrounding(PepeShowcaseLibrary.Take take, float seconds)
        {
            if (!take.grounding || take.grounding.tracks == null) return;
            foreach (var track in take.grounding.tracks)
                if (track != null && !track.receiver && track.avatar == character.avatar)
                {
                    actor.Pose.targetHips.position += Vector3.up * Mathf.Max(0, track.At(seconds));
                    return;
                }
        }

        public void PlayTake(int index)
        {
            TakeIndex = -1;
            Evaluate(library.TakeStart(Mathf.Clamp(index, 0, library.takes.Length - 1)), 0, false);
            Running = true;
        }

        public void Restart()
        {
            // Recording can set a new output aspect after Start initialized the scene.
            if (ready && library.continuous) PrepareContinuousSequence();
            TakeIndex = -1;
            Evaluate(0, 0, false);
            Running = true;
        }

        public void ReleaseActor()
        {
            if (actor)
            {
                actor.Clear();
                Delete(actor.gameObject);
            }
            actor = null;
            if (customDriver)
                Delete(customDriver.gameObject);
            customDriver = null;
        }

        static void Delete(UnityEngine.Object value)
        {
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        void OnDestroy()
        {
            ReleaseActor();
        }
    }
}
