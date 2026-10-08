using System;
using System.Collections;
using UnityEngine;

public partial class GameManager
{
    public bool IsAnimationTestMode { get; private set; }
    public bool IsAnimationTestPlaying { get; private set; }
    public CombatTripletData AnimationTestMove { get; private set; }
    public PlayerUI.Side AnimationTestAttacker { get; private set; }
    public bool AnimationTestLethal { get; private set; }
    public int CompletedAnimationTests { get; private set; }
    public event Action AnimationTestFinished;

    Coroutine animationTestRoutine;
    TestFighterState[] animationTestFighters;

    sealed class TestFighterState
    {
        readonly CharacterCombat fighter;
        readonly Vector3 position;
        readonly Quaternion rotation;
        readonly bool dead;
        readonly Transform[] bones;
        readonly Vector3[] bonePositions;
        readonly Quaternion[] boneRotations;
        readonly MemeBattleUI.Side side;
        readonly long hp, maxHp;

        public TestFighterState(CharacterCombat actor, MemeBattleUI ui, MemeBattleUI.Side actorSide)
        {
            fighter = actor;
            side = actorSide;
            var model = actor.Animator.transform;
            position = model.position;
            rotation = model.rotation;
            dead = actor.IsDead;
            bones = model.GetComponentsInChildren<Transform>(true);
            bonePositions = new Vector3[bones.Length];
            boneRotations = new Quaternion[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                bonePositions[i] = bones[i].localPosition;
                boneRotations[i] = bones[i].localRotation;
            }
            maxHp = ui ? Math.Max(1, ui.defaultInitialMaxHpAtomic) : 1000;
            hp = maxHp;
            var slot = ui ? actorSide == MemeBattleUI.Side.Left ? ui.left : ui.right : null;
            string[] health = slot?.healthText ? slot.healthText.text.Split('/') : null;
            if (health != null && health.Length == 2 && long.TryParse(health[0], out long savedHp) &&
                long.TryParse(health[1], out long savedMax) && savedMax > 0)
            { hp = savedHp; maxHp = savedMax; }
        }

        public void RestorePosition()
        {
            if (fighter && fighter.Animator) fighter.Animator.transform.SetPositionAndRotation(position, rotation);
        }

        public void RestoreMatch(MemeBattleUI ui)
        {
            RestorePosition();
            if (!fighter || !fighter.Animator) return;
            if (dead)
            {
                fighter.Animator.enabled = true;
                fighter.MarkDead();
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] && bones[i] != fighter.Animator.transform)
                        bones[i].SetLocalPositionAndRotation(bonePositions[i], boneRotations[i]);
            }
            ui?.UpdateHealth(side, hp, maxHp);
        }
    }

    public bool BeginAnimationTestMode()
    {
#if UNITY_EDITOR
        if (IsAnimationTestMode) return true;
        // Finish the current exchange before handing its shared clock/actors to the test panel.
        if (!Application.isPlaying || !isActiveAndEnabled || _running || !leftCombat || !rightCombat ||
            leftCombat.IsBusy || rightCombat.IsBusy || !leftCombat.Initialize() || !rightCombat.Initialize()) return false;
        var feedback = GetComponent<BattleImpactFeedback>();
        if (feedback && (feedback.IsHolding || feedback.IsSlowing)) return false;
        animationTestFighters = new[]
        {
            new TestFighterState(leftCombat, UI, MemeBattleUI.Side.Left),
            new TestFighterState(rightCombat, UI, MemeBattleUI.Side.Right)
        };
        IsAnimationTestMode = true;
        StopAnimationTest();
        return true;
#else
        return false;
#endif
    }

    public bool PlayAnimationTest(PlayerUI.Side side, CombatTripletData move, bool lethal = false)
    {
        var fighter = CombatFor(side);
        if (!fighter || move == null || !move.IsValid ||
            !(ContainsTestMove(fighter.lightCombatMoves, move) || ContainsTestMove(fighter.heavyCombatMoves, move))) return false;
        if (!BeginAnimationTestMode()) return false;
        StopAnimationTest();
        lethal &= AnimationTestHasDamageContacts(move);
        AnimationTestMove = move;
        AnimationTestAttacker = side;
        AnimationTestLethal = lethal;
        IsAnimationTestPlaying = true;
        animationTestRoutine = StartCoroutine(RunAnimationTest(side, move, lethal));
        return true;
    }

    static bool ContainsTestMove(CombatTripletData[] moves, CombatTripletData move) =>
        moves != null && Array.Exists(moves, item => ReferenceEquals(item, move));

    bool AnimationTestHasDamageContacts(CombatTripletData move)
    {
        var pair = move.sourcePair;
        if (pair == null || !pair.Valid) return false;
        var bank = battleSfx ? battleSfx.bank : battleVfx ? battleVfx.timeline : null;
        var profile = move.grapple ? move.grapple.Presentation(move.grappleOutcome) : bank ? bank.FindMove(move) : null;
        var continuation = move.grapple ? move.grapple.For(move.grappleOutcome) : null;
        float duration = continuation != null
            ? move.grapple.decisionSeconds + continuation.Duration
            : Mathf.Max(pair.attack.length, pair.reactionDelay + pair.reaction.length);
        return float.IsFinite(duration) && duration >= 0 &&
            BattleHitDamageSequence.ContactTimes(profile, duration).Length > 0;
    }

    IEnumerator RunAnimationTest(PlayerUI.Side side, CombatTripletData move, bool lethal)
    {
        try
        {
            yield return RunSafely(RunExchange(side, ContainsTestMove(CombatFor(side).heavyCombatMoves, move), lethal, null, move));
            if (QueueError == null) CompletedAnimationTests++;
        }
        finally
        {
            IsAnimationTestPlaying = false;
            animationTestRoutine = null;
            AnimationTestFinished?.Invoke();
        }
    }

    void BeginAnimationTestDamage(PlayerUI.Side attacker, CombatTripletData move,
        FrankRetarget.FrankBattlePairPlayback playback, bool lethal)
    {
        var side = attacker == PlayerUI.Side.Left ? MemeBattleUI.Side.Right : MemeBattleUI.Side.Left;
        long before = Math.Max(10, UI ? UI.defaultInitialMaxHpAtomic : defaultInitialMaxHpAtomic);
        long damage = lethal ? before : Math.Max(1, before / 10 * 3 + before % 10 * 3 / 10);
        long after = before - damage;
        var bank = battleSfx ? battleSfx.bank : battleVfx ? battleVfx.timeline : null;
        float[] times = playback && playback.Playing
            ? BattleHitDamageSequence.ContactTimes(playback.PresentationProfile(bank), playback.Duration) : Array.Empty<float>();
        void Present(long hp, long portion)
        {
            UI?.UpdateHealth(side, hp, before);
            if (portion > 0) UI?.ShowDamage(side, portion, ContainsTestMove(CombatFor(attacker).heavyCombatMoves, move));
        }
        // Preview health is presentation only; authoritative HP and match snapshots stay intact.
        if (times.Length == 0) { Present(before, 0); return; }
        _hitDamage = new BattleHitDamageSequence(before, after, damage, times, Present);
        _damagePlayback = playback;
        playback.TimelineAdvanced += AdvanceDamagePresentation;
    }

    public void StopAnimationTest()
    {
        if (!IsAnimationTestMode) return;
        if (animationTestRoutine != null) StopCoroutine(animationTestRoutine);
        animationTestRoutine = null;
        IsAnimationTestPlaying = false;
        ClearDamagePresentation();
        if (battleSfx) battleSfx.ResetForMatch();
        if (battleVfx) battleVfx.ResetForMatch();
        if (leftCombat) leftCombat.ResetCombat();
        if (rightCombat) rightCombat.ResetCombat();
        if (UI)
        {
            UI.ResetTransientEffects();
            UI.HideResult();
        }
        QueueError = null;
        if (animationTestFighters != null)
            foreach (var fighter in animationTestFighters) fighter.RestorePosition();
        if (positioningController && leftCombat && rightCombat)
            positioningController.FinishExchange(leftCombat, rightCombat);
        long full = Math.Max(10, UI ? UI.defaultInitialMaxHpAtomic : defaultInitialMaxHpAtomic);
        if (UI)
        {
            UI.UpdateHealth(MemeBattleUI.Side.Left, full, full);
            UI.UpdateHealth(MemeBattleUI.Side.Right, full, full);
        }
    }

    public void EndAnimationTestMode()
    {
        if (!IsAnimationTestMode) return;
        StopAnimationTest();
        IsAnimationTestMode = false;
        if (animationTestFighters != null)
            foreach (var fighter in animationTestFighters) fighter.RestoreMatch(UI);
        // Restoring an already defeated fighter's HUD must not replay its old KO.
        if (UI && UI.knockout) UI.knockout.ResetPresentation();
        animationTestFighters = null;
        // Queued socket events resume in Update; no test event is sent to the server.
    }
}
