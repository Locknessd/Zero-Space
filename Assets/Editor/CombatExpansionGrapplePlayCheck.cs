using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    [InitializeOnLoad]
    public static partial class CombatExpansionGrapplePlayCheck
    {
        static readonly string[] Cases =
        {
            "queued release", "queued late escape throws", "queued successful escape",
            "queued resolved escape", "queued repeated escape", "server authority opt-out",
            "escape rejection guards", "direct cancel", "defender disable", "round reset"
        };
        const string ReportPath = "GeneratedAssets/CombatExpansion/GrapplePlayMode.txt";
        static readonly StringBuilder report = new StringBuilder();
        static GameManager game;
        static CharacterCombat[] fighters;
        static TrumpWeaponManager[] equipment;
        static Vector3[] positions;
        static Quaternion[] rotations;
        static CharacterCombat source, target;
        static FrankBattlePairPlayback pair;
        static CombatTripletData move;
        static PlayerUI.Side sourceSide, targetSide;
        static bool running, activeCase, acted, priorInput;
        static int step, frame, sourceId, targetId, sequenceStarts, audioBefore, effectsBefore;
        static float priorTimeScale, previousSample;
        static double began, settledAt;
        static string failure;
        static int Kind => step % Cases.Length;
        static bool Queued => Kind < 5;

        static CombatExpansionGrapplePlayCheck()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                if (running && state == PlayModeStateChange.ExitingPlayMode)
                    Finish("Play Mode exited during grapple validation.");
            };
        }

        public static void Begin()
        {
            if (!EditorApplication.isPlaying || running)
                throw new InvalidOperationException("Begin once in BattleScene Play Mode.");
            game = Object.FindAnyObjectByType<GameManager>();
            if (!game || game.IsAnimationTestMode || game.IsEventQueueBusy ||
                !game.leftCombat || !game.rightCombat || !game.leftCombat.isActiveAndEnabled ||
                !game.rightCombat.isActiveAndEnabled || game.leftCombat.IsBusy || game.rightCombat.IsBusy ||
                !game.battleVfx || !game.battleSfx || Time.timeScale <= 0)
                throw new InvalidOperationException("Battle must be idle with audio/VFX and an unpaused clock.");
            fighters = new[] { game.leftCombat, game.rightCombat };
            positions = fighters.Select(f => f.Animator.transform.position).ToArray();
            rotations = fighters.Select(f => f.Animator.transform.rotation).ToArray();
            priorInput = game.enableLocalInputTesting;
            priorTimeScale = Time.timeScale;
            game.enableLocalInputTesting = false;
            report.Clear();
            report.AppendLine("RUNNING: grapple queue, timing, authority and lifecycle checks.");
            report.AppendLine("Logic evidence only; physical contact alignment requires visual review.");
            report.AppendLine("Guards: early, wrong actor, stale ID, paused, duplicate, late, server opt-out.");
            step = 0;
            frame = -1;
            activeCase = false;
            running = true;
            try
            {
                equipment = fighters.Select(CreateEquipment).ToArray();
                healthBefore = HealthSnapshot();
                nativeBefore = Object.FindObjectsByType<FrankTestActor>().Length;
                game.battleVfx.SequenceBegan += SequenceBegan;
                game.battleVfx.ContactOccurred += Contact;
                game.battleSfx.CuePlayed += Cue;
                Write();
            }
            catch (Exception error) { Finish(error.ToString()); }
        }

        static void StartCase()
        {
            game.ResetCombatQueue();
            foreach (var fighter in fighters)
                fighter.gameObject.SetActive(true);
            foreach (var manager in equipment)
                manager.EquipCombatWeapon(TrumpWeaponManager.WeaponType.DualDaggers);
            int orientation = step / Cases.Length;
            int sourceIndex = orientation % 2;
            sourceSide = sourceIndex == 0 ? PlayerUI.Side.Left : PlayerUI.Side.Right;
            targetSide = sourceIndex == 0 ? PlayerUI.Side.Right : PlayerUI.Side.Left;
            source = fighters[sourceIndex];
            target = fighters[1 - sourceIndex];
            bool reversed = orientation >= 2;
            for (int i = 0; i < 2; i++)
                fighters[i].Animator.transform.SetPositionAndRotation(
                    positions[reversed ? 1 - i : i], rotations[i]);
            string action = Kind == 0 ? "Vol10_HOLD" : Kind == 3 ? "Vol10_NAGE_ESC" : "Vol10_HOLD_LALI";
            move = game.FindCombatAction(sourceSide, action);
            Require(move != null && move.grapple, "Registered grapple action missing: " + action);
            pair = null;
            failure = null;
            acted = false;
            sequenceStarts = 0;
            contacts = damageContacts = groundContacts = 0;
            contactIds.Clear();
            cueCounts.Clear();
            previousSample = 0;
            settledAt = 0;
            began = EditorApplication.timeSinceStartup;
            audioBefore = game.battleSfx.PlayedCueCount;
            effectsBefore = game.battleVfx.PlayedEffectCount;
            activeCase = true;
            if (Queued)
                Require(game.EnqueueCombatAction(sourceSide, action), "Named grapple queue rejected action.");
            else
            {
                PositionDirectPair();
                Require(source.ExecuteAttack(move, target), "Direct grapple rejected action.");
                Require(pair && pair.Playing, "Direct sequence did not publish its start.");
                RunDirectChecks();
            }
            Write();
        }

        static void PositionDirectPair()
        {
            var sourceRoot = source.Animator.transform;
            var targetRoot = target.Animator.transform;
            var sourcePosition = sourceRoot.position;
            var targetPosition = targetRoot.position;
            float direction = targetPosition.x >= sourcePosition.x ? 1f : -1f;
            float midpoint = (sourcePosition.x + targetPosition.x) * .5f;
            float halfGap = move.attackRange * .45f;
            sourcePosition.x = midpoint - direction * halfGap;
            targetPosition.x = midpoint + direction * halfGap;
            sourceRoot.SetPositionAndRotation(sourcePosition, Quaternion.LookRotation(Vector3.right * direction));
            targetRoot.SetPositionAndRotation(targetPosition, Quaternion.LookRotation(Vector3.left * direction));
            Require(Mathf.Abs(sourceRoot.position.x - targetRoot.position.x) <= move.attackRange,
                "Direct grapple fixture is outside the authored attack range.");
        }

        static void SequenceBegan(CharacterCombat actor, CombatTripletData action, bool lethal)
        {
            if (!running || !activeCase || actor != source || action != move)
                return;
            sequenceStarts++;
            pair = source.SourcePlayback;
            sourceId = source.PlaybackId;
            targetId = target.PlaybackId;
        }

        static void Tick()
        {
            if (!running || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount)
                return;
            frame = Time.frameCount;
            try
            {
                Require(failure == null && game.QueueError == null, failure ?? game.QueueError);
                if (!activeCase)
                {
                    if (step == Cases.Length * 4)
                        Finish(null);
                    else
                        StartCase();
                    return;
                }
                Require(EditorApplication.timeSinceStartup - began < 30, "Case exceeded 30 seconds.");
                if (!pair)
                    return;
                CheckPoseAndOwnership();
                if (pair.Playing)
                {
                    Require(pair.SampleTime >= previousSample, "Global pair clock restarted.");
                    previousSample = pair.SampleTime;
                    if (Queued)
                        RunQueuedChecks();
                    return;
                }
                if (game.IsEventQueueBusy || source.IsBusy || target.IsBusy)
                    return;
                if (settledAt == 0)
                    settledAt = EditorApplication.timeSinceStartup;
                if (EditorApplication.timeSinceStartup - settledAt < .25)
                    return;
                CheckCompletion();
                report.AppendLine($"PASS case={step} scenario={Cases[Kind]} source={source.name} " +
                    $"reversed={step / Cases.Length >= 2} outcome={pair.GrappleOutcome} " +
                    $"contacts={contacts} damage={damageContacts} ground={groundContacts} " +
                    $"audio={game.battleSfx.PlayedCueCount - audioBefore} " +
                    $"vfx={game.battleVfx.PlayedEffectCount - effectsBefore} starts={sequenceStarts}");
                step++;
                activeCase = false;
                Write();
            }
            catch (Exception error) { Finish(error.ToString()); }
        }

        static void RunQueuedChecks()
        {
            if (acted || Kind == 0 || Kind == 3)
                return;
            Require(pair.EscapeInputAllowed, "Local queue did not enable escape input.");
            if (Kind == 1 && pair.SampleTime > move.grapple.escapeWindowSeconds.y)
            {
                Require(!game.RequestThrowEscape(targetSide, targetId), "Late request accepted.");
                acted = true;
            }
            else if ((Kind == 2 || Kind == 4) && pair.EscapeWindowOpen)
            {
                AcceptEscape();
                acted = true;
            }
            else if ((Kind == 2 || Kind == 4) && pair.SampleTime > move.grapple.escapeWindowSeconds.y)
                throw new InvalidOperationException("Editor missed escape window; rerun at a stable frame rate.");
        }

        static void Finish(string error)
        {
            running = false;
            activeCase = false;
            try
            {
                Time.timeScale = priorTimeScale;
                if (game)
                {
                    game.battleVfx.SequenceBegan -= SequenceBegan;
                    game.battleVfx.ContactOccurred -= Contact;
                    game.battleSfx.CuePlayed -= Cue;
                    game.ResetCombatQueue();
                    game.enableLocalInputTesting = priorInput;
                }
                for (int i = 0; i < fighters.Length; i++)
                {
                    if (equipment != null && i < equipment.Length && equipment[i])
                        Object.Destroy(equipment[i].gameObject);
                    if (!fighters[i])
                        continue;
                    fighters[i].gameObject.SetActive(true);
                    fighters[i].Animator.transform.SetPositionAndRotation(positions[i], rotations[i]);
                }
            }
            catch (Exception cleanup) { error = (error ?? "") + " Cleanup: " + cleanup; }
            report.AppendLine(error == null ? "PASS all 40 cases across both fighters and screen directions."
                : $"FAIL case={step} scenario={Cases[Kind]}: {error}");
            Write();
        }

        static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        static void Write()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, report.ToString());
        }
    }
}
