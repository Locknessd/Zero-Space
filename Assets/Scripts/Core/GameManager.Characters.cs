using System;
using UnityEngine;

public partial class GameManager
{
    public BattleCharacterCatalog characterCatalog;
    string pendingLeftCharacter;
    string pendingRightCharacter;
    public bool IsCharacterSelectionAvailable => isActiveAndEnabled && !_matchEnded;

    void InitializeCharacterSelection()
    {
        if (UI)
        {
            foreach (var label in new[] { UI.left.nameText, UI.right.nameText })
            {
                if (!label)
                    continue;
                label.resizeTextForBestFit = true;
                label.resizeTextMaxSize = label.fontSize;
                label.resizeTextMinSize = Mathf.Min(18, label.fontSize);
            }
        }
        foreach (var fighter in new[] { leftCombat, rightCombat })
        {
            var appearance = fighter.GetComponent<BattleCharacterAppearance>();
            if (appearance && !string.IsNullOrEmpty(appearance.startupCharacterId))
                appearance.TryApply(appearance.startupCharacterId);
        }
        GetComponent<BattleCharacterSelector>()?.RefreshPortraits();
    }

    /// <summary>Accepted requests wait until the complete exchange has released both participants.</summary>
    public bool RequestCharacter(PlayerUI.Side side, string characterId)
    {
        var fighter = CombatFor(side);
        var appearance = fighter ? fighter.GetComponent<BattleCharacterAppearance>() : null;
        if (!Application.isPlaying || !isActiveAndEnabled || !characterCatalog ||
            characterCatalog.Find(characterId) == null || !appearance || fighter.IsDead || _matchEnded)
            return false;
        if (side == PlayerUI.Side.Left)
            pendingLeftCharacter = characterId;
        else
            pendingRightCharacter = characterId;
        ApplyPendingCharacters();
        return true;
    }

    // Unity WebGL SendMessage accepts one string argument. Invalid requests leave the current selection intact.
    public void SetLeftCharacter(string characterId) => RequestCharacter(PlayerUI.Side.Left, characterId);
    public void SetRightCharacter(string characterId) => RequestCharacter(PlayerUI.Side.Right, characterId);

    public string CurrentCharacter(PlayerUI.Side side) => CombatFor(side)
        ?.GetComponent<BattleCharacterAppearance>()?.CurrentCharacterId;

    public string PendingCharacter(PlayerUI.Side side) =>
        side == PlayerUI.Side.Left ? pendingLeftCharacter : pendingRightCharacter;

    void ApplyPendingCharacters()
    {
        if (!IsReadyForBattleEvents || _running || IsAnimationTestPlaying || !leftCombat || !rightCombat ||
            leftCombat.IsBusy || rightCombat.IsBusy)
            return;
        if (IsAnimationTestMode && (pendingLeftCharacter != null || pendingRightCharacter != null))
        {
            // Saved test bone references belong to the outgoing rig. Restore and recapture them after the swap.
            EndAnimationTestMode();
            ApplyPendingCharacters();
            BeginAnimationTestMode();
            return;
        }
        ApplyCharacter(PlayerUI.Side.Left, ref pendingLeftCharacter);
        ApplyCharacter(PlayerUI.Side.Right, ref pendingRightCharacter);
    }

    void ApplyCharacter(PlayerUI.Side side, ref string pending)
    {
        if (pending == null)
            return;
        var fighter = CombatFor(side);
        if (fighter.IsDead)
        {
            pending = null;
            return;
        }
        var appearance = fighter.GetComponent<BattleCharacterAppearance>();
        if (appearance.TryApply(pending))
        {
            UI?.SetFighterName(ToUISide(side), DisplayNameForSide(side));
            GetComponent<BattleCharacterSelector>()?.RefreshPortraits();
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var camera in root.GetComponentsInChildren<FrankRetarget.FrankCinematicCamera>(true))
                    camera.InvalidateCharacterGeometry();
            pending = null;
        }
        else
        {
            Debug.LogWarning("Cannot apply battle character: " + pending, this);
            pending = null;
        }
    }
}
