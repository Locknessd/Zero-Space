using System;
using UnityEngine;

public partial class GameManager
{
    /// <summary>Resolves a registered action without changing the exchange or damage authority.</summary>
    public CombatTripletData FindCombatAction(PlayerUI.Side side, string actionId)
    {
        var fighter = CombatFor(side);
        if (!fighter || string.IsNullOrWhiteSpace(actionId))
            return null;
        return FindAction(fighter.lightCombatMoves, actionId) ?? FindAction(fighter.heavyCombatMoves, actionId);
    }

    static CombatTripletData FindAction(CombatTripletData[] moves, string actionId)
    {
        if (moves == null)
            return null;
        foreach (var move in moves)
            if (move != null && move.IsValid && string.Equals(move.moveName, actionId, StringComparison.Ordinal))
                return move;
        return null;
    }

    /// <summary>Queues a named local action through the same positioning, clock and cleanup as server turns.</summary>
    public bool EnqueueCombatAction(PlayerUI.Side side, string actionId)
    {
        if (!Application.isPlaying || !isActiveAndEnabled || IsAnimationTestMode ||
            QueueError != null || _matchEnded || FindCombatAction(side, actionId) == null)
            return false;
        CloseCollectingTurn();
        _queue.Enqueue(new BattleStack
        {
            ready = true,
            localAttacker = side,
            selectedActionId = actionId
        });
        return true;
    }
}
