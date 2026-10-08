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
    public static partial class CombatExpansionPreviewDamageCheck
    {
        const string Key = "CombatExpansion.PreviewDamage";
        const string ReportPath = "GeneratedAssets/CombatExpansion/PreviewDamagePlayMode.txt";
        const string Copy = "Assets/Editor/CombatExpansionPreviewDamageCheck.unity";
        const int TotalCases = 16;
        static readonly string[] Actions = { "Vol10_HOLD", "Vol10_NAGE_ESC", "Light_1" };
        static readonly StringBuilder report = new StringBuilder();
        static GameManager game;
        static CharacterCombat source, target;
        static MemeBattleUI ui;
        static BattleImpactFeedback feedback;
        static FrankBattlePairPlayback pair;
        static CombatTripletData move;
        static int step, lastFrame = -1, completedBefore, pendingBefore;
        static bool activeCase, sawPair, sawDamage, sawDamageUi;
        static double began, caseBegan, settledAt;
        static long full;
        static string authoritativeBefore, failure;
        static PlayerUI.Side Side => (step < 12 ? step / 6 : (step - 12) / 2) == 0
            ? PlayerUI.Side.Left : PlayerUI.Side.Right;
        static string Action => step < 12 ? Actions[step % 6 / 2] : "Light_1";
        static bool RequestedLethal => step % 2 == 1;
        static bool Damaging => step >= 12 || step % 6 / 2 == 2;
        static bool Interrupted => step >= 12;

        static CombatExpansionPreviewDamageCheck()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Key + ".run", false) || !EditorApplication.isPlaying ||
                EditorApplication.isCompiling || lastFrame == Time.frameCount)
                return;
            lastFrame = Time.frameCount;
            try
            {
                if (began == 0)
                    began = EditorApplication.timeSinceStartup;
                Require(EditorApplication.timeSinceStartup - began < 300, "Suite exceeded 300 seconds.");
                if (!game)
                {
                    if (EditorApplication.timeSinceStartup - began < 1)
                        return;
                    Initialize();
                }
                Require(failure == null && game.QueueError == null, failure ?? game.QueueError);
                if (!activeCase)
                {
                    if (step == TotalCases)
                        Finish(null);
                    else
                        StartCase();
                    return;
                }
                Require(EditorApplication.timeSinceStartup - caseBegan < 35, "Case exceeded 35 seconds.");
                Observe();
                if (Interrupted && sawDamage && game.IsAnimationTestPlaying)
                {
                    CompleteCase();
                    return;
                }
                if (game.IsAnimationTestPlaying || feedback.IsHolding || feedback.IsSlowing)
                    return;
                Require(!Interrupted, "Interrupted preview finished before damage could be interrupted.");
                if (settledAt == 0)
                    settledAt = EditorApplication.timeSinceStartup;
                if (EditorApplication.timeSinceStartup - settledAt < .35)
                    return;
                CompleteCase();
            }
            catch (Exception error)
            {
                Finish(error.ToString());
            }
        }

        static void Initialize()
        {
            game = Object.FindAnyObjectByType<GameManager>();
            Require(game && game.gameObject.scene.path == Copy && !game.IsEventQueueBusy &&
                !game.IsAnimationTestMode && game.leftCombat && game.rightCombat &&
                game.leftCombat.IsIdleAndSettled && game.rightCombat.IsIdleAndSettled,
                "Isolated BattleScene must contain two idle fighters and no active queue.");
            ui = game.uiManager;
            feedback = game.GetComponent<BattleImpactFeedback>();
            Require(ui && ui.knockout && ui.left.healthText && ui.right.healthText &&
                ui.left.damageText && ui.right.damageText && feedback && game.battleVfx && game.battleSfx,
                "Battle health, damage labels, KO and presentation components are required.");
            SaveState();
            game.enableLocalInputTesting = false;
            Application.runInBackground = true;
            full = Math.Max(10, ui.defaultInitialMaxHpAtomic);
            authoritativeBefore = HealthSnapshot();
            pendingBefore = game.PendingEventCount;
            game.battleVfx.SequenceBegan += SequenceBegan;
        }

        static void StartCase()
        {
            Require(!game.IsAnimationTestMode, "Previous case retained preview mode.");
            ui.UpdateHealth(MemeBattleUI.Side.Left, 731, 1000);
            ui.UpdateHealth(MemeBattleUI.Side.Right, 419, 1000);
            source = Side == PlayerUI.Side.Left ? game.leftCombat : game.rightCombat;
            target = Side == PlayerUI.Side.Left ? game.rightCombat : game.leftCombat;
            move = game.FindCombatAction(Side, Action);
            Require(move != null && move.IsValid && move.sourcePair != null && move.sourcePair.Valid,
                "Missing authored action " + Action);
            pair = null;
            sawPair = sawDamage = sawDamageUi = false;
            settledAt = 0;
            completedBefore = game.CompletedAnimationTests;
            caseBegan = EditorApplication.timeSinceStartup;
            activeCase = true;
            report.AppendLine($"START case={step + 1}/{TotalCases} action={Action} side={Side} " +
                $"lethalRequest={RequestedLethal} interrupted={Interrupted}");
            Write();
            Require(game.PlayAnimationTest(Side, move, RequestedLethal), "Preview API rejected action.");
            Require(game.AnimationTestLethal == (Damaging && RequestedLethal),
                "Effective lethal preview does not match authored damage contacts.");
            Observe();
        }

        static void SequenceBegan(CharacterCombat actor, CombatTripletData data, bool lethal)
        {
            if (!activeCase)
                return;
            if (actor != source || data != move || lethal != (Damaging && RequestedLethal))
                failure = "Unexpected sequence actor, move or lethal state.";
            pair = source.SourcePlayback;
        }

        static void Observe()
        {
            Require(game.IsAnimationTestMode && game.PendingEventCount == pendingBefore &&
                !game.IsEventQueueBusy && HealthSnapshot() == authoritativeBefore,
                "Preview changed authoritative health or gameplay queue ownership.");
            long hp = ReadHp(TargetSlot());
            sawDamage |= hp < full;
            sawDamageUi |= !string.IsNullOrEmpty(TargetSlot().damageText.text);
            Require(ReadHp(SourceSlot()) == full, "Attacker health changed during preview.");
            if (!Damaging)
                Require(hp == full && !sawDamageUi && string.IsNullOrEmpty(SourceSlot().damageText.text) &&
                    !source.IsDead && !target.IsDead &&
                    !ui.knockout.Pending && !ui.knockout.HasShown && ui.knockout.ShowCount == 0 &&
                    !feedback.HasPlayedKnockoutSlowMotion && !feedback.IsSlowing,
                    "Nondamaging preview emitted HP loss, damage UI, death or KO.");
            if (!pair || !pair.Playing)
                return;
            sawPair = true;
            var bank = game.battleSfx ? game.battleSfx.bank : game.battleVfx.timeline;
            var times = BattleHitDamageSequence.ContactTimes(pair.PresentationProfile(bank), pair.Duration);
            Require((times.Length > 0) == Damaging, "Authored profile does not match the selected case.");
            long damage = RequestedLethal ? full : full / 10 * 3 + full % 10 * 3 / 10;
            int hits = times.Count(time => time <= pair.SampleTime);
            long loss = times.Length == 0 ? 0 : damage / times.Length * hits +
                Math.Max(0L, hits - (times.Length - damage % times.Length));
            Require(hp == full - loss, "Preview health does not follow the actual shared contact timeline.");
        }

        static void CompleteCase()
        {
            Require(sawPair && (!Damaging || sawDamage && sawDamageUi),
                "No active pair or actual damaging-preview HP/UI change was observed.");
            if (!Interrupted)
            {
                Require(game.CompletedAnimationTests == completedBefore + 1, "Preview did not finish exactly once.");
                long damage = RequestedLethal ? full : full / 10 * 3 + full % 10 * 3 / 10;
                Require(ReadHp(TargetSlot()) == (Damaging ? full - damage : full), "Incorrect final preview HP.");
                Require(target.IsDead == (Damaging && RequestedLethal), "Incorrect final death state.");
                if (Damaging && RequestedLethal)
                    Require(ui.knockout.HasShown && ui.knockout.ShowCount == 1, "Lethal regression lost KO.");
            }
            if (!Interrupted || !RequestedLethal)
            {
                game.StopAnimationTest();
                CheckStopped();
                Require(ReadHp(ui.left) == full && ReadHp(ui.right) == full, "Stop did not refill preview HP.");
            }
            game.EndAnimationTestMode();
            CheckStopped();
            Require(!game.IsAnimationTestMode && ui.left.healthText.text == "731/1000" &&
                ui.right.healthText.text == "419/1000", "Exit failed to restore the saved health snapshot.");
            Require(HealthSnapshot() == authoritativeBefore && game.PendingEventCount == pendingBefore,
                "Stop/exit changed authoritative health or queue contents.");
            report.AppendLine($"PASS case={step + 1} action={Action} side={Side} " +
                $"lethalRequest={RequestedLethal} interrupted={Interrupted}; HP/UI, ownership and restoration.");
            activeCase = false;
            step++;
            Write();
        }

        static MemeBattleUI.FighterSlot TargetSlot() => Side == PlayerUI.Side.Left ? ui.right : ui.left;
        static MemeBattleUI.FighterSlot SourceSlot() => Side == PlayerUI.Side.Left ? ui.left : ui.right;
        static long ReadHp(MemeBattleUI.FighterSlot slot) => long.Parse(slot.healthText.text.Split('/')[0]);

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
