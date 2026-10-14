using System;
using System.Collections;
using UnityEngine;

namespace ZeroSpace.Showcase
{
    public sealed partial class PepeAttackShowcase
    {
        bool capturingFrames;
        int captureFrame;
        int captureRate;

        // Read the rendered frame after Unity updates both the body skin and props.
        public void CaptureRenderedFrames(int frameRate, Func<bool> capture)
        {
            if (!Application.isPlaying || capturingFrames || frameRate <= 0)
                throw new InvalidOperationException("Start one frame capture in Play mode.");
            Running = false;
            captureFrame = 0;
            captureRate = frameRate;
            capturingFrames = true;
            StartCoroutine(CaptureAfterRendering(capture));
        }

        IEnumerator CaptureAfterRendering(Func<bool> capture)
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (capturingFrames)
            {
                yield return endOfFrame;
                if (!capture())
                {
                    capturingFrames = false;
                    yield break;
                }
                captureFrame++;
            }
        }

        void SaveCharacterPose()
        {
            savedBones = character.GetComponentsInChildren<Transform>(true);
            savedPositions = new Vector3[savedBones.Length];
            savedRotations = new Quaternion[savedBones.Length];
            for (int i = 0; i < savedBones.Length; i++)
            {
                savedPositions[i] = savedBones[i].localPosition;
                savedRotations[i] = savedBones[i].localRotation;
            }
            character.enabled = false;
        }

        void RestoreCharacterPose()
        {
            for (int i = 0; i < savedBones.Length; i++)
                if (savedBones[i])
                    savedBones[i].SetLocalPositionAndRotation(savedPositions[i], savedRotations[i]);
        }

        public void SetCharacter(int index)
        {
            EndTrailerIntro();
            var prefab = index == 1 ? library.mankeyPrefab : index == 2 ? customCharacterPrefab : library.pepePrefab;
            if (!prefab) return;
            var replacement = Instantiate(prefab);
            replacement.name = index == 1 ? "Mankey" : index == 2 ? "Custom character" : "Pepe";
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(replacement, gameObject.scene);
            var animator = replacement.GetComponentInChildren<Animator>(true);
            if (!animator || !animator.avatar || !animator.avatar.isValid || !animator.isHuman)
            {
                Delete(replacement);
                throw new ArgumentException("Use a model imported with a valid Humanoid Avatar.");
            }
            ReleaseActor();
            characterRoot.SetActive(false);
            Delete(characterRoot);
            replacement.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            character = animator;
            characterRoot = replacement;
            CharacterIndex = index;
            TakeIndex = -1;
            SaveCharacterPose();
            PrepareContinuousSequence();
            Evaluate(SequenceTime, 0, false);
        }

        void OnGUI()
        {
            if (!showControls || !ready) return;
            GUILayout.BeginArea(new Rect(Screen.width - 296, 96, 272, 200 + library.takes.Length * 30), GUI.skin.box);
            GUILayout.Label("ATTACK SHOWCASE");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Pepe")) SetCharacter(0);
            if (GUILayout.Button("Mankey")) SetCharacter(1);
            if (customCharacterPrefab && GUILayout.Button("Custom")) SetCharacter(2);
            GUILayout.EndHorizontal();
            for (int i = 0; i < library.takes.Length; i++)
                if (GUILayout.Button(library.takes[i].title)) PlayTake(i);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Running ? "Pause" : "Play")) Running = !Running;
            if (GUILayout.Button("Restart")) Restart();
            GUILayout.EndHorizontal();
            GUILayout.Label("Speed: " + speed.ToString("0.00") + "x");
            speed = GUILayout.HorizontalSlider(speed, .25f, 1.5f);
            loop = GUILayout.Toggle(loop, "Loop all attacks");
            GUILayout.EndArea();
        }
    }
}
