using UnityEngine.UI;

public sealed partial class BattleCharacterSelector
{
    public Image leftPortrait;
    public Image rightPortrait;

    public void RefreshPortraits()
    {
        if (!battle || !battle.characterCatalog)
            return;
        var left = battle.characterCatalog.Find(battle.CurrentCharacter(PlayerUI.Side.Left));
        var right = battle.characterCatalog.Find(battle.CurrentCharacter(PlayerUI.Side.Right));
        if (left?.portrait && leftPortrait)
            leftPortrait.sprite = left.portrait;
        if (right?.portrait && rightPortrait)
            rightPortrait.sprite = right.portrait;
        var cutIn = battle.uiManager ? battle.uiManager.comicCutIn : null;
        if (cutIn)
        {
            if (left?.portrait)
                cutIn.leftPortrait = left.portrait;
            if (right?.portrait)
                cutIn.rightPortrait = right.portrait;
        }
    }
}
