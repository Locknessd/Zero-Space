using UnityEngine;
using UnityEngine.UI;

namespace MemeArena
{
    [DisallowMultipleComponent]
    public sealed class MemeArenaCombatAdapter : MonoBehaviour
    {
        public MemeCrowdReactionManager crowd;
        public GameManager game;
        public BattleVfxPlayer battleVfx;
        public MemeBattleUI ui;
        [Range(.05f, .5f)] public float lowHealthThreshold = .25f;
        CharacterCombat left, right;
        Text roundLabel;
        Slider leftHealth, rightHealth;
        string lastRound;
        int contacts;
        bool bound, knockedOut, leftLow, rightLow;
        ulong lastContact;
        public int ContactCount { get; private set; }
        public int ComboCount { get; private set; }
        public int KoCount { get; private set; }
        public bool IsBound => bound;

        void OnEnable()
        {
            if (!crowd) crowd = GetComponentInChildren<MemeCrowdReactionManager>();
            InvokeRepeating(nameof(TryBind), 0, .25f);
        }

        public void TryBind()
        {
            if (bound || !crowd) return;
            if (!game) game = GameManager.Instance;
            if (!game || !game.IsReadyForBattleEvents) return;
            if (!battleVfx) battleVfx = game.battleVfx;
            if (!ui) ui = game.uiManager ? game.uiManager : MemeBattleUI.Instance;
            left = game.leftCombat;
            right = game.rightCombat;
            if (!left || !right || !battleVfx) return;
            battleVfx.ContactOccurred += OnContact;
            battleVfx.SequenceBegan += OnSequence;
            left.SequenceEnded += OnSequenceEnded;
            right.SequenceEnded += OnSequenceEnded;
            if (ui)
            {
                leftHealth = ui.left.hpSlider;
                rightHealth = ui.right.hpSlider;
                if (leftHealth) leftHealth.onValueChanged.AddListener(OnLeftHealth);
                if (rightHealth) rightHealth.onValueChanged.AddListener(OnRightHealth);
                roundLabel = game.roundManager ? game.roundManager.roundNumberText : ui.roundNumberText;
                if (roundLabel) roundLabel.RegisterDirtyVerticesCallback(OnRoundLabelChanged);
            }
            bound = true;
            CancelInvoke(nameof(TryBind));
        }

        void OnDisable()
        {
            CancelInvoke(nameof(TryBind));
            if (!bound) return;
            if (battleVfx)
            {
                battleVfx.ContactOccurred -= OnContact;
                battleVfx.SequenceBegan -= OnSequence;
            }
            if (left) left.SequenceEnded -= OnSequenceEnded;
            if (right) right.SequenceEnded -= OnSequenceEnded;
            if (leftHealth) leftHealth.onValueChanged.RemoveListener(OnLeftHealth);
            if (rightHealth) rightHealth.onValueChanged.RemoveListener(OnRightHealth);
            if (roundLabel) roundLabel.UnregisterDirtyVerticesCallback(OnRoundLabelChanged);
            bound = false;
        }

        MemeCrowdSide Side(CharacterCombat fighter) => fighter == left ?
            MemeCrowdSide.FighterA : MemeCrowdSide.FighterB;

        void OnSequence(CharacterCombat attacker, CombatTripletData move, bool lethal)
        {
            contacts = 0;
            lastContact = 0;
        }

        void OnContact(BattleVfxPlayer.Impact impact)
        {
            // Landing and follow-through cues may arrive after the lethal contact.
            // Keep the winning/losing reactions until health or a new round resets them.
            if (knockedOut) return;
            if (!impact.attacker || !impact.receiver || (impact.eventId != 0 && lastContact == impact.eventId)) return;
            lastContact = impact.eventId;
            ContactCount++;
            var side = Side(impact.attacker);
            if (impact.kind != BattleVfxPlayer.ContactKind.Ground)
            {
                contacts++;
                if (contacts == 2)
                {
                    ComboCount++;
                    crowd.OnCombo(side);
                }
                else if (impact.kind == BattleVfxPlayer.ContactKind.Heavy) crowd.OnCritical(side);
                else crowd.OnHit(side);
            }
            if (impact.finishing && impact.receiver.IsDead) AnnounceKO(side);
        }

        void OnSequenceEnded(CharacterCombat fighter, int playback, bool completed)
        {
            if (completed && fighter.IsDead)
                AnnounceKO(fighter == left ? MemeCrowdSide.FighterB : MemeCrowdSide.FighterA);
        }

        void OnLeftHealth(float value) => ObserveHealth(leftHealth, MemeCrowdSide.FighterA, ref leftLow);
        void OnRightHealth(float value) => ObserveHealth(rightHealth, MemeCrowdSide.FighterB, ref rightLow);

        void ObserveHealth(Slider slider, MemeCrowdSide side, ref bool alreadyLow)
        {
            if (!slider) return;
            float health = slider.normalizedValue;
            if (health > .9f)
            {
                alreadyLow = false;
                if (left && right && !left.IsDead && !right.IsDead) knockedOut = false;
            }
            if (!knockedOut && health > 0 && health <= lowHealthThreshold && !alreadyLow)
            {
                alreadyLow = true;
                crowd.OnLowHealth(side);
            }
            // WINNER_DECLARED can arrive without a lethal contact. GameManager updates this same HUD.
            if (health <= 0 && ((side == MemeCrowdSide.FighterA && left.IsDead) ||
                (side == MemeCrowdSide.FighterB && right.IsDead)))
                AnnounceKO(side == MemeCrowdSide.FighterA ? MemeCrowdSide.FighterB : MemeCrowdSide.FighterA);
        }

        void OnRoundLabelChanged()
        {
            if (!roundLabel || string.IsNullOrWhiteSpace(roundLabel.text) || roundLabel.text == lastRound) return;
            lastRound = roundLabel.text;
            NotifyRoundStart();
        }

        public void NotifyRoundStart()
        {
            knockedOut = false;
            contacts = 0;
            crowd.OnRoundStart();
        }

        public void NotifyCritical(bool fighterA)
        {
            if (!knockedOut)
                crowd.OnCritical(fighterA ? MemeCrowdSide.FighterA : MemeCrowdSide.FighterB);
        }

        public void NotifyVictory(bool fighterA) =>
            AnnounceKO(fighterA ? MemeCrowdSide.FighterA : MemeCrowdSide.FighterB);

        void AnnounceKO(MemeCrowdSide winner)
        {
            if (knockedOut) return;
            knockedOut = true;
            KoCount++;
            crowd.OnKO(winner);
        }
    }
}
