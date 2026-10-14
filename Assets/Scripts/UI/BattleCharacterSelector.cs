using UnityEngine;

/// <summary>Compact local appearance chooser. Does not send match or damage events.</summary>
public sealed partial class BattleCharacterSelector : MonoBehaviour
{
    public GameManager battle;
    public bool showSelector = true;
    bool expanded;
    readonly string[] sideLabels = { "LEFT", "RIGHT" };

    void OnGUI()
    {
        if (!showSelector || !battle || !battle.characterCatalog)
            return;
        float scale = Mathf.Clamp(Screen.height / 900f, .75f, 1.6f);
        var saved = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
        float height = Screen.height / scale;
        float width = Screen.width / scale;
        float x = Mathf.Max(8, width - 366);
        if (GUI.Button(new Rect(width - 176, height - 42, 164, 30), "CHARACTERS"))
            expanded = !expanded;
        if (expanded)
        {
            float panelHeight = 64 + battle.characterCatalog.characters.Length * 34;
            var panel = new Rect(x, height - panelHeight - 50, 354, panelHeight);
            GUI.Box(panel, "CHOOSE CHARACTER");
            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                var side = sideIndex == 0 ? PlayerUI.Side.Left : PlayerUI.Side.Right;
                float column = x + 8 + sideIndex * 174;
                GUI.Label(new Rect(column, panel.y + 24, 166, 24), sideLabels[sideIndex]);
                for (int i = 0; i < battle.characterCatalog.characters.Length; i++)
                {
                    var entry = battle.characterCatalog.characters[i];
                    bool selected = battle.CurrentCharacter(side) == entry.id;
                    bool pending = battle.PendingCharacter(side) == entry.id;
                    GUI.enabled = battle.IsCharacterSelectionAvailable &&
                        !(sideIndex == 0 ? battle.leftCombat : battle.rightCombat).IsDead;
                    string label = entry.displayName + (pending ? " ..." : selected ? " *" : "");
                    if (GUI.Button(new Rect(column, panel.y + 50 + i * 34, 166, 30), label))
                        battle.RequestCharacter(side, entry.id);
                }
            }
        }
        GUI.enabled = true;
        GUI.matrix = saved;
    }
}
