using UnityEngine;

public partial class GameManager
{
    // Local player and AI use the same actor identity and timing validation.
    // A server-resolved exchange never enables this local decision path.
    public bool RequestThrowEscape(PlayerUI.Side defenderSide, int expectedPlaybackId)
    {
        var defender = CombatFor(defenderSide);
        return defender && defender.SourcePlayback &&
            defender.SourcePlayback.RequestThrowEscape(defender, expectedPlaybackId);
    }

    void EscapeOrEnqueue(PlayerUI.Side side, bool heavy)
    {
        var defender = CombatFor(side);
        if (defender && defender.SourcePlayback && defender.SourcePlayback.IsGrappleDefender(defender))
        {
            RequestThrowEscape(side, defender.PlaybackId);
            return;
        }
        EnqueueLocalAttack(side, heavy);
    }
}
