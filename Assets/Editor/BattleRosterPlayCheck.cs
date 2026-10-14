using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class BattleRosterPlayCheck
{
    const string Key = "BattleRosterPlayCheck";
    const string Review = "GeneratedAssets/BattleRoster";
    static GameManager game;
    static int phase;
    static bool launched;
    static bool requested;
    static bool captured;
    static double started;
    static float actionStarted;
    static string leftAfter;
    static string rightAfter;
    static int contactsBefore;
    static int observedContacts;
    static readonly StringBuilder Report = new StringBuilder();

    static BattleRosterPlayCheck() => EditorApplication.update += Tick;

    public static void Start()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            SceneManager.GetActiveScene().path != "Assets/Scenes/BattleScene.unity")
            throw new Exception("Runtime roster validation requires BattleScene in Edit mode.");
        Directory.CreateDirectory(Review);
        phase = 0;
        launched = requested = captured = false;
        started = 0;
        game = null;
        Report.Clear();
        observedContacts = 0;
        BattlePresentationWorkbench.SetFullHdGameView();
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Review + "/BeforeRuntimeCheck.unity", true);
        File.WriteAllText(Review + "/RuntimeCheck.txt", "RUNNING");
        var view = EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView"));
        view.Show();
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }

    static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling)
            return;
        try
        {
            Application.runInBackground = true;
            if (started == 0)
                started = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - started > 360)
                throw new Exception("Runtime roster check timed out.");
            if (EditorApplication.timeSinceStartup - started < 1)
                return;
            if (!game)
            {
                game = GameManager.Instance;
                if (!game || !game.IsReadyForBattleEvents)
                    return;
                game.enableLocalInputTesting = false;
                game.battleVfx.ContactOccurred += ObserveContact;
                Time.timeScale = 2;
                game.GetComponent<BattleCharacterSelector>().showSelector = true;
                VerifyHeights();
            }
            if (game.QueueError != null)
                throw new Exception(game.QueueError);
            var fighters = new[] { game.leftCombat, game.rightCombat };
            var entries = game.characterCatalog.characters.Skip(2).ToArray();
            if (phase >= entries.Length)
            {
                if (game.IsEventQueueBusy || fighters.Any(f => f.IsBusy))
                    return;
                string leftHp = game.uiManager.left.healthText.text;
                string rightHp = game.uiManager.right.healthText.text;
                game.SetLeftCharacter("mankey");
                game.SetRightCharacter("pepe");
                VerifyHeights();
                Require(game.CurrentCharacter(PlayerUI.Side.Left) == "mankey" &&
                    game.CurrentCharacter(PlayerUI.Side.Right) == "pepe", "Cannot restore legacy appearances.");
                Require(leftHp == game.uiManager.left.healthText.text && rightHp == game.uiManager.right.healthText.text,
                    "Returning to legacy appearances changed HP.");
                Require(!game.RequestCharacter(PlayerUI.Side.Left, "missing-character"), "Invalid ID was accepted.");
                Finish(true, null);
                return;
            }
            if (!launched)
            {
                if (game.IsEventQueueBusy || fighters.Any(f => !f.IsIdleAndSettled))
                    return;
                var transforms = fighters.Select(f => f.transform).ToArray();
                var positions = transforms.Select(t => t.position).ToArray();
                string leftHp = game.uiManager.left.healthText.text;
                string rightHp = game.uiManager.right.healthText.text;
                game.SetLeftCharacter(entries[phase].id);
                game.SetRightCharacter(entries[(phase + 1) % entries.Length].id);
                VerifyHeights();
                for (int i = 0; i < fighters.Length; i++)
                    Require(fighters[i].transform == transforms[i] &&
                        Vector3.Distance(transforms[i].position, positions[i]) < .00001f, "Swap moved a fighter.");
                Require(leftHp == game.uiManager.left.healthText.text && rightHp == game.uiManager.right.healthText.text,
                    "Swap reset HP.");
                var side = phase % 2 == 0 ? PlayerUI.Side.Left : PlayerUI.Side.Right;
                var source = side == PlayerUI.Side.Left ? fighters[0] : fighters[1];
                var moves = source.lightCombatMoves.Concat(source.heavyCombatMoves).ToArray();
                string[] actions = { "Heavy_Assassin", "Light_GSWING", "Frank_GreatSword_Attack_Ambush", "SlapFace_Sequence1" };
                var move = moves.Single(m => m.moveName == actions[phase]);
                contactsBefore = observedContacts;
                Require(game.EnqueueCombatAction(side, move.moveName), "Cannot queue " + move.moveName);
                Report.AppendLine("START " + entries[phase].id + " / " + move.moveName);
                launched = true;
                requested = captured = false;
                actionStarted = Time.unscaledTime;
            }
            var playback = fighters.Select(f => f.SourcePlayback).FirstOrDefault(p => p && p.Playing);
            if (playback && !requested)
            {
                string beforeLeft = game.CurrentCharacter(PlayerUI.Side.Left);
                string beforeRight = game.CurrentCharacter(PlayerUI.Side.Right);
                leftAfter = entries[(phase + 2) % entries.Length].id;
                rightAfter = entries[(phase + 3) % entries.Length].id;
                Require(game.RequestCharacter(PlayerUI.Side.Left, leftAfter), "Deferred left swap rejected.");
                Require(game.RequestCharacter(PlayerUI.Side.Right, rightAfter), "Deferred right swap rejected.");
                Require(game.CurrentCharacter(PlayerUI.Side.Left) == beforeLeft &&
                    game.CurrentCharacter(PlayerUI.Side.Right) == beforeRight, "Swapped during an active exchange.");
                requested = true;
            }
            if (playback && !captured && playback.SampleTime > Mathf.Min(1.1f, playback.Duration * .45f))
            {
                Directory.CreateDirectory(Review + "/RuntimePreviews");
                ScreenCapture.CaptureScreenshot(Review + "/RuntimePreviews/Character_" + phase + ".png");
                captured = true;
            }
            if (!requested || game.IsEventQueueBusy || fighters.Any(f => !f.IsIdleAndSettled) ||
                Time.unscaledTime - actionStarted < .5f)
                return;
            if (game.PendingCharacter(PlayerUI.Side.Left) != null ||
                game.PendingCharacter(PlayerUI.Side.Right) != null)
                return;
            Require(game.CurrentCharacter(PlayerUI.Side.Left) == leftAfter &&
                game.CurrentCharacter(PlayerUI.Side.Right) == rightAfter, "Deferred selections were not applied.");
            Require(observedContacts > contactsBefore, "No hit VFX reached the new characters.");
            Report.AppendLine("PASS queued swaps, completed attack/reaction/get-up, hit VFX, stable fighter identity.");
            VerifyHeights();
            phase++;
            launched = false;
        }
        catch (Exception error)
        {
            Finish(false, error.ToString());
        }
    }

    static void Require(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    static void VerifyHeights()
    {
        foreach (var fighter in new[] { game.leftCombat, game.rightCombat })
        {
            var appearance = fighter.GetComponent<BattleCharacterAppearance>();
            var entry = game.characterCatalog.Find(appearance.CurrentCharacterId);
            float height = entry.referenceHeight * fighter.transform.lossyScale.y;
            Require(Mathf.Abs(height - game.characterCatalog.characterHeight) < .001f,
                "Character height changed on " + fighter.name + ": " + height);
        }
    }

    static void ObserveContact(BattleVfxPlayer.Impact impact) => observedContacts++;

    static void Finish(bool succeeded, string error)
    {
        if (game && game.battleVfx)
            game.battleVfx.ContactOccurred -= ObserveContact;
        File.WriteAllText(Review + "/RuntimeCheck.txt", (succeeded ? "PASS\n" : "FAIL\n") + Report + error);
        SessionState.EraseBool(Key);
        Time.timeScale = 1;
        EditorApplication.isPlaying = false;
    }
}

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void PlayCheckBattleRoster() => BattleRosterPlayCheck.Start();
    }
}

