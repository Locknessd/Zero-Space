using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    [InitializeOnLoad]
    public static class CombatExpansionAtemiPlayCheck
    {
        static readonly string[] Actions =
        {
            "Light_1", "Vol10_CmnAtemi", "Vol10_CmnAtemi2", "Vol10_CmnAtemi3", "Heavy_6"
        };
        static readonly StringBuilder Report = new StringBuilder();
        static readonly HashSet<ulong> Ids = new HashSet<ulong>();
        static GameManager game;
        static BattleImpactFeedback feedback;
        static BattleLightingRig lighting;
        static BattleCameraShake shake;
        static int step, audioBefore, vfxBefore, holdsBefore, lightsBefore, shakeBefore, contacts, frame;
        static double began, startedCase, settledAt;
        static bool running, playing;
        static string failure;
        static Vector3 leftPosition, rightPosition;
        static Quaternion leftRotation, rightRotation;

        static CombatExpansionAtemiPlayCheck()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                if (running && state == PlayModeStateChange.ExitingPlayMode)
                    Finish("Play Mode exited during validation.");
            };
        }

        public static void Begin()
        {
            if (!EditorApplication.isPlaying || running)
                throw new InvalidOperationException("Start once in BattleScene Play Mode.");
            game = BattlePresentationWorkbench.Battle();
            if (game.IsEventQueueBusy || game.IsAnimationTestMode)
                throw new InvalidOperationException("Battle must be idle outside animation test mode.");
            feedback = game.GetComponent<BattleImpactFeedback>();
            lighting = game.GetComponent<BattleLightingRig>();
            shake = game.gameObject.scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<BattleCameraShake>(true)).Single();
            leftPosition = game.leftCombat.Animator.transform.position;
            rightPosition = game.rightCombat.Animator.transform.position;
            leftRotation = game.leftCombat.Animator.transform.rotation;
            rightRotation = game.rightCombat.Animator.transform.rotation;
            Report.Clear();
            Report.AppendLine("RUNNING: exact action IDs through GameManager.EnqueueCombatAction.");
            Ids.Clear();
            step = 0;
            frame = -1;
            began = EditorApplication.timeSinceStartup;
            running = true;
            playing = false;
            failure = null;
            game.battleVfx.ContactOccurred += Contact;
            Write();
        }

        static void Contact(BattleVfxPlayer.Impact impact)
        {
            if (!running || !playing)
                return;
            contacts++;
            if (!Ids.Add(impact.eventId))
                failure = "Duplicate contact identity " + impact.eventId;
            if (Mathf.Abs(impact.seconds - impact.playback.SampleTime) > .0001f)
                failure = "Feedback does not use the displayed contact pose.";
            if (impact.move.actionDefinition && impact.kind != BattleVfxPlayer.ContactKind.Ground)
            {
                if (!impact.cue.TryContactPosition(impact.receiver.Animator, out var expected) ||
                    Vector3.Distance(expected, impact.position) > .021f)
                    failure = "Per-avatar contact anchor was not used.";
                if (impact.playback.AttackerActor.Pose.weaponRenderers.Any(r => r && r.enabled) ||
                    impact.playback.ReceiverActor.Pose.weaponRenderers.Any(r => r && r.enabled))
                    failure = "Unarmed paired action retained a weapon renderer.";
                string path = CombatExpansionInventory.Output + "/Live_" + step + "_" + impact.move.moveName + ".png";
                ScreenCapture.CaptureScreenshot(path);
            }
        }

        static void Tick()
        {
            if (!running || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount)
                return;
            frame = Time.frameCount;
            try
            {
                if (failure != null || game.QueueError != null)
                    throw new InvalidOperationException(failure ?? game.QueueError);
                if (EditorApplication.timeSinceStartup - began > 180)
                    throw new TimeoutException("Named action validation exceeded 180 seconds.");
                if (!playing)
                {
                    if (step == Actions.Length * 4)
                    {
                        Finish(null);
                        return;
                    }
                    BeginCase();
                    return;
                }
                if (game.IsEventQueueBusy || game.leftCombat.IsBusy || game.rightCombat.IsBusy)
                {
                    settledAt = 0;
                    return;
                }
                if (settledAt == 0)
                    settledAt = EditorApplication.timeSinceStartup;
                if (EditorApplication.timeSinceStartup - settledAt < .6)
                    return;
                string action = Actions[step % Actions.Length];
                int expectedContacts = action == "Vol10_CmnAtemi" ? 2 : action.StartsWith("Vol10_") ? 1 : contacts;
                if (contacts != expectedContacts || contacts == 0)
                    throw new Exception(action + ": wrong contact count " + contacts);
                if (game.battleSfx.PlayedCueCount <= audioBefore || game.battleVfx.PlayedEffectCount <= vfxBefore ||
                    feedback.HitStopCount <= holdsBefore || lighting.PlayedFlashCount <= lightsBefore ||
                    shake.ShakeCount <= shakeBefore)
                    throw new Exception(action + ": missing presentation subsystem response.");
                if (!game.leftCombat.Animator.enabled || !game.rightCombat.Animator.enabled ||
                    feedback.IsHolding || feedback.IsSlowing || shake.IsShaking || Time.timeScale != 1)
                    throw new Exception(action + ": animation or presentation ownership did not recover.");
                Report.AppendLine($"PASS case={step} action={action} contacts={contacts} " +
                    $"seconds={EditorApplication.timeSinceStartup - startedCase:F3} " +
                    $"audio={game.battleSfx.PlayedCueCount - audioBefore} " +
                    $"vfx={game.battleVfx.PlayedEffectCount - vfxBefore}");
                step++;
                playing = false;
                Write();
            }
            catch (Exception error)
            {
                Finish(error.ToString());
            }
        }

        static void BeginCase()
        {
            int scenario = step / Actions.Length;
            bool reversed = scenario >= 2;
            var side = scenario % 2 == 0 ? PlayerUI.Side.Left : PlayerUI.Side.Right;
            game.leftCombat.Animator.transform.position = reversed ? rightPosition : leftPosition;
            game.rightCombat.Animator.transform.position = reversed ? leftPosition : rightPosition;
            audioBefore = game.battleSfx.PlayedCueCount;
            vfxBefore = game.battleVfx.PlayedEffectCount;
            holdsBefore = feedback.HitStopCount;
            lightsBefore = lighting.PlayedFlashCount;
            shakeBefore = shake.ShakeCount;
            contacts = 0;
            settledAt = 0;
            startedCase = EditorApplication.timeSinceStartup;
            playing = true;
            if (!game.EnqueueCombatAction(side, Actions[step % Actions.Length]))
                throw new Exception("Named gameplay action was rejected.");
        }

        static void Finish(string error)
        {
            running = false;
            playing = false;
            if (game)
            {
                game.battleVfx.ContactOccurred -= Contact;
                game.ResetCombatQueue();
                game.leftCombat.Animator.transform.SetPositionAndRotation(leftPosition, leftRotation);
                game.rightCombat.Animator.transform.SetPositionAndRotation(rightPosition, rightRotation);
            }
            Report.AppendLine(error == null ? "PASS all 20 cases; 12 new-action cases across both avatars and directions."
                : "FAIL " + error);
            Write();
        }

        static void Write() => File.WriteAllText(CombatExpansionInventory.Output + "/AtemiQueuePlayMode.txt", Report.ToString());
    }
}
