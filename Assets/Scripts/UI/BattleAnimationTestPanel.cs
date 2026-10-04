using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Local battle animation browser using the scene's actual move pools and playback.</summary>
[DisallowMultipleComponent]
public sealed class BattleAnimationTestPanel : MonoBehaviour
{
    public GameManager battle;
    public bool openOnStart;
    public bool enableInReleaseBuilds;

    public sealed class Entry
    {
        public PlayerUI.Side side;
        public CombatTripletData move;
        public bool heavy;
        public Button button;
    }

    public IReadOnlyList<Entry> Entries => entries;
    public bool IsOpen => board && board.gameObject.activeSelf;
    public bool PlayAllActive => playAll;
    public Button OpenButton { get; private set; }
    public Button ReplayButton { get; private set; }
    public Button StopButton { get; private set; }
    public Button NextButton { get; private set; }
    public Button PlayAllButton { get; private set; }
    public Button ExitButton { get; private set; }
    public Toggle KoToggle { get; private set; }

    readonly List<Entry> entries = new List<Entry>();
    readonly List<Entry> visibleEntries = new List<Entry>();
    readonly List<Button> filterButtons = new List<Button>();
    RectTransform canvasRoot, board, toolbar, content;
    Text status, count;
    Font font;
    ScrollRect scroll;
    Entry selected;
    int filter;
    bool playAll, waitingForNext;
    float nextAt;
    static readonly Color Background = new Color(.035f, .05f, .08f, .96f);
    static readonly Color Accent = new Color(.12f, .8f, .9f, 1);

    void Start()
    {
        if (!Application.isEditor && !Debug.isDebugBuild && !enableInReleaseBuilds) return;
        if (!battle) battle = GameManager.Instance;
        if (!battle) return;
        BuildUI();
        if (openOnStart) Open();
    }

    void Update()
    {
        if (!canvasRoot || !battle) return;
        if (Input.GetKeyDown(KeyCode.F8))
        {
            if (battle.IsAnimationTestMode && IsOpen) board.gameObject.SetActive(false);
            else Open();
        }
        if (battle.IsAnimationTestMode && Input.GetKeyDown(KeyCode.Escape)) Exit();
        if (!battle.IsAnimationTestMode) return;
        if (playAll && !battle.IsAnimationTestPlaying)
        {
            if (battle.QueueError != null) { playAll = false; waitingForNext = false; }
            else if (battle.uiManager && battle.uiManager.knockout &&
                     (battle.uiManager.knockout.Pending || battle.uiManager.knockout.IsShowing))
                waitingForNext = false;
            else if (!waitingForNext)
            {
                waitingForNext = true;
                nextAt = Time.unscaledTime + (KoToggle.isOn ? 2.1f : .65f);
            }
            else if (Time.unscaledTime >= nextAt)
            {
                waitingForNext = false;
                int next = visibleEntries.IndexOf(selected) + 1;
                if (next >= visibleEntries.Count) playAll = false;
                else PlayEntry(visibleEntries[next], true);
            }
        }
        RefreshStatus();
    }

    public void Open()
    {
        if (!battle || !canvasRoot) return;
        if (!battle.BeginAnimationTestMode())
        {
            OpenButton.GetComponentInChildren<Text>().text = "WAIT FOR EXCHANGE...";
            return;
        }
        OpenButton.GetComponentInChildren<Text>().text = "ANIMATION TEST  [F8]";
        OpenButton.gameObject.SetActive(false);
        toolbar.gameObject.SetActive(true);
        board.gameObject.SetActive(true);
        RebuildEntries();
        RefreshStatus();
    }

    public void Exit()
    {
        playAll = waitingForNext = false;
        battle?.EndAnimationTestMode();
        if (board) board.gameObject.SetActive(false);
        if (toolbar) toolbar.gameObject.SetActive(false);
        if (OpenButton) OpenButton.gameObject.SetActive(true);
    }

    public void Stop()
    {
        playAll = waitingForNext = false;
        battle?.StopAnimationTest();
        RefreshStatus();
    }

    public void Replay()
    {
        if (selected != null) PlayEntry(selected);
    }

    public void Next()
    {
        if (visibleEntries.Count == 0) return;
        int next = (visibleEntries.IndexOf(selected) + 1) % visibleEntries.Count;
        PlayEntry(visibleEntries[next]);
    }

    public void PlayAll()
    {
        if (visibleEntries.Count == 0) return;
        waitingForNext = false;
        playAll = true;
        PlayEntry(visibleEntries[0], true);
    }

    public bool PlayEntry(Entry entry, bool continueAll = false)
    {
        if (entry == null || !entries.Contains(entry)) return false;
        if (!continueAll) playAll = waitingForNext = false;
        if (!battle.PlayAnimationTest(entry.side, entry.move, KoToggle.isOn)) return false;
        selected = entry;
        board.gameObject.SetActive(false);
        toolbar.gameObject.SetActive(true);
        OpenButton.gameObject.SetActive(false);
        RefreshStatus();
        return true;
    }

    void RebuildEntries()
    {
        var previousMove = selected?.move;
        var previousSide = selected?.side;
        foreach (var entry in entries) if (entry.button) Destroy(entry.button.gameObject);
        entries.Clear();
        AddPool(battle.leftCombat, PlayerUI.Side.Left, false);
        AddPool(battle.leftCombat, PlayerUI.Side.Left, true);
        AddPool(battle.rightCombat, PlayerUI.Side.Right, false);
        AddPool(battle.rightCombat, PlayerUI.Side.Right, true);
        selected = entries.Find(entry => entry.move == previousMove && entry.side == previousSide);
        SetFilter(filter);
    }

    void AddPool(CharacterCombat fighter, PlayerUI.Side side, bool heavy)
    {
        if (!fighter) return;
        var pool = heavy ? fighter.heavyCombatMoves : fighter.lightCombatMoves;
        if (pool == null) return;
        foreach (var move in pool)
        {
            if (move == null) continue;
            var entry = new Entry { side = side, heavy = heavy, move = move };
            string label = FighterName(side) + "  /  " + (heavy ? "HEAVY" : "LIGHT") + "  /  " + MoveName(move);
            var button = MakeButton(content, label, Vector2.zero, new Vector2(450, 70), () => PlayEntry(entry));
            entry.button = button;
            var layout = button.gameObject.AddComponent<LayoutElement>(); layout.preferredHeight = 70;
            var text = button.GetComponentInChildren<Text>(); text.alignment = TextAnchor.UpperLeft;
            text.fontSize = 15; text.rectTransform.offsetMin = new Vector2(14, 32); text.rectTransform.offsetMax = new Vector2(-10, -9);
            string clips = (move.attackAnim ? move.attackAnim.name : "Missing attack") + "  >  " +
                           (move.hitAnim ? move.hitAnim.name : "Missing hit") + "  >  " +
                           (move.getUpAnim ? move.getUpAnim.name : "Hold pose");
            var detail = MakeText(button.transform, clips, 12, new Color(.7f, .77f, .84f));
            Place(detail.rectTransform, new Vector2(14, 8), new Vector2(425, 24), Vector2.zero);
            detail.alignment = TextAnchor.LowerLeft;
            detail.resizeTextForBestFit = true; detail.resizeTextMinSize = 9; detail.resizeTextMaxSize = 12;
            button.interactable = move.IsValid;
            entries.Add(entry);
        }
    }

    void SetFilter(int value)
    {
        filter = value;
        playAll = waitingForNext = false;
        visibleEntries.Clear();
        foreach (var entry in entries)
        {
            bool visible = filter == 0 || filter == 1 && !entry.heavy || filter == 2 && entry.heavy ||
                           filter == 3 && entry.side == PlayerUI.Side.Left || filter == 4 && entry.side == PlayerUI.Side.Right;
            entry.button.gameObject.SetActive(visible);
            if (visible && entry.move.IsValid) visibleEntries.Add(entry);
        }
        for (int i = 0; i < filterButtons.Count; i++) filterButtons[i].GetComponent<Image>().color = i == filter ? new Color(.08f, .38f, .43f) : new Color(.10f, .14f, .20f);
        count.text = visibleEntries.Count + " PAIRS  /  ATTACK + REACTION + RECOVERY";
        scroll.verticalNormalizedPosition = 1;
    }

    string FighterName(PlayerUI.Side side)
    {
        var slot = battle.uiManager ? side == PlayerUI.Side.Left ? battle.uiManager.left : battle.uiManager.right : null;
        return slot?.nameText ? slot.nameText.text.ToUpperInvariant() : side == PlayerUI.Side.Left ? "LEFT" : "RIGHT";
    }

    static string MoveName(CombatTripletData move) => move.skill == BattleSkill.Archer ? "METEOR SHOT" :
        move.skill == BattleSkill.WhiteMage ? "CELESTIAL SMITE" :
        string.IsNullOrEmpty(move.moveName) ? "UNNAMED MOVE" : move.moveName.Replace('_', ' ').ToUpperInvariant();

    void RefreshStatus()
    {
        if (!status || !battle) return;
        string detail = selected == null ? "Select a pair. Each preview starts with full health." :
            FighterName(selected.side) + "  /  " + MoveName(selected.move) + "  /  " +
            (battle.IsAnimationTestPlaying ? "PLAYING" : "READY") + (KoToggle.isOn ? "  /  KO PREVIEW" : "");
        status.text = battle.QueueError == null ? (playAll ? "PLAY ALL  |  " : "LOCAL TEST  |  ") + detail : "PLAYBACK ERROR  |  PRESS STOP TO RESET";
        ReplayButton.interactable = selected != null;
        NextButton.interactable = PlayAllButton.interactable = visibleEntries.Count > 0;
        PlayAllButton.GetComponentInChildren<Text>().text = playAll ? "PLAYING ALL..." : "PLAY ALL";
    }

    void BuildUI()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvasObject = new GameObject("Animation Test Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        canvasRoot = canvasObject.GetComponent<RectTransform>();
        var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 900;
        var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;

        OpenButton = MakeButton(canvasRoot, "ANIMATION TEST  [F8]", new Vector2(16, 16), new Vector2(205, 40), Open);
        board = MakeRect(canvasRoot, "Pair Browser");
        board.anchorMin = new Vector2(0, .15f); board.anchorMax = new Vector2(0, .82f);
        board.pivot = Vector2.zero; board.offsetMin = new Vector2(16, 0); board.offsetMax = new Vector2(526, 0);
        AddBackground(board, Background);
        var heading = MakeText(board, "BATTLE / ANIMATION TEST", 22, Accent);
        Place(heading.rectTransform, new Vector2(18, -18), new Vector2(460, 30), new Vector2(0, 1));
        count = MakeText(board, "", 12, new Color(.66f, .74f, .84f));
        Place(count.rectTransform, new Vector2(18, -53), new Vector2(470, 20), new Vector2(0, 1));
        string[] labels = { "ALL", "LIGHT", "HEAVY", "LEFT", "RIGHT" };
        for (int i = 0; i < labels.Length; i++)
        {
            int option = i;
            filterButtons.Add(MakeButton(board, labels[i], new Vector2(18 + i * 96, -85), new Vector2(88, 30), () => SetFilter(option), new Vector2(0, 1)));
        }
        var scrollRoot = MakeRect(board, "Pairs");
        scrollRoot.anchorMin = Vector2.zero; scrollRoot.anchorMax = Vector2.one;
        scrollRoot.offsetMin = new Vector2(18, 48); scrollRoot.offsetMax = new Vector2(-18, -124);
        scroll = scrollRoot.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
        var viewport = MakeRect(scrollRoot, "Viewport"); Stretch(viewport); AddBackground(viewport, new Color(.025f, .035f, .055f));
        viewport.gameObject.AddComponent<RectMask2D>();
        content = MakeRect(viewport, "Content"); content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
        content.sizeDelta = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>(); layout.spacing = 7;
        layout.childControlWidth = layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        var fit = content.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport; scroll.content = content;
        var hint = MakeText(board, "Click a pair to watch. Scroll for more.\nUse LIST / F8 to return; ESC exits test mode.", 12, new Color(.66f, .74f, .84f));
        Place(hint.rectTransform, new Vector2(18, 8), new Vector2(474, 36), Vector2.zero);

        toolbar = MakeRect(canvasRoot, "Test Controls");
        toolbar.anchorMin = Vector2.zero; toolbar.anchorMax = Vector2.right; toolbar.pivot = Vector2.zero;
        toolbar.offsetMin = new Vector2(16, 12); toolbar.offsetMax = new Vector2(-16, 94); AddBackground(toolbar, Background);
        status = MakeText(toolbar, "", 14, Accent);
        status.rectTransform.anchorMin = new Vector2(0, 1); status.rectTransform.anchorMax = Vector2.one;
        status.rectTransform.pivot = new Vector2(0, 1); status.rectTransform.offsetMin = new Vector2(14, -29); status.rectTransform.offsetMax = new Vector2(-12, -5);
        MakeButton(toolbar, "LIST  [F8]", new Vector2(12, 10), new Vector2(120, 36), Open);
        ReplayButton = MakeButton(toolbar, "REPLAY", new Vector2(140, 10), new Vector2(100, 36), Replay);
        NextButton = MakeButton(toolbar, "NEXT", new Vector2(248, 10), new Vector2(90, 36), Next);
        StopButton = MakeButton(toolbar, "STOP", new Vector2(346, 10), new Vector2(90, 36), Stop);
        PlayAllButton = MakeButton(toolbar, "PLAY ALL", new Vector2(444, 10), new Vector2(142, 36), PlayAll);
        var toggleRoot = MakeRect(toolbar, "KO Preview"); Place(toggleRoot, new Vector2(603, 10), new Vector2(145, 36), Vector2.zero);
        AddBackground(toggleRoot, Color.clear);
        KoToggle = toggleRoot.gameObject.AddComponent<Toggle>();
        var box = MakeRect(toggleRoot, "Box"); Place(box, new Vector2(0, 7), new Vector2(22, 22), Vector2.zero);
        AddBackground(box, new Color(.15f, .22f, .3f));
        var check = MakeRect(box, "Check"); Stretch(check); check.offsetMin = Vector2.one * 4; check.offsetMax = Vector2.one * -4;
        AddBackground(check, Accent); KoToggle.targetGraphic = box.GetComponent<Image>(); KoToggle.graphic = check.GetComponent<Image>();
        KoToggle.SetIsOnWithoutNotify(false);
        var toggleLabel = MakeText(toggleRoot, "KO PREVIEW", 13, Color.white);
        Place(toggleLabel.rectTransform, new Vector2(30, 0), new Vector2(110, 36), Vector2.zero);
        ExitButton = MakeButton(toolbar, "EXIT TEST  [ESC]", new Vector2(758, 10), new Vector2(165, 36), Exit);
        board.gameObject.SetActive(false); toolbar.gameObject.SetActive(false);
    }

    RectTransform MakeRect(Transform parent, string label)
    {
        var node = new GameObject(label, typeof(RectTransform)); node.transform.SetParent(parent, false);
        return node.GetComponent<RectTransform>();
    }

    Text MakeText(Transform parent, string value, int size, Color tint)
    {
        var rect = MakeRect(parent, "Label"); var text = rect.gameObject.AddComponent<Text>();
        text.font = font; text.text = value; text.fontSize = size; text.color = tint;
        text.alignment = TextAnchor.MiddleLeft; text.raycastTarget = false;
        return text;
    }

    Button MakeButton(Transform parent, string label, Vector2 position, Vector2 size, Action clicked, Vector2? anchor = null)
    {
        var rect = MakeRect(parent, label); Place(rect, position, size, anchor ?? Vector2.zero);
        var background = AddBackground(rect, new Color(.10f, .14f, .20f));
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = background;
        var colors = button.colors; colors.highlightedColor = new Color(.42f, .95f, 1); colors.pressedColor = Accent; colors.disabledColor = new Color(.4f, .4f, .4f); button.colors = colors;
        button.onClick.AddListener(() => clicked());
        var text = MakeText(rect, label, 13, Color.white); Stretch(text.rectTransform); text.alignment = TextAnchor.MiddleCenter;
        return button;
    }

    static Image AddBackground(RectTransform rect, Color color)
    {
        var image = rect.gameObject.AddComponent<Image>(); image.color = color; return image;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    static void Place(RectTransform rect, Vector2 position, Vector2 size, Vector2 anchor)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor; rect.sizeDelta = size; rect.anchoredPosition = position;
    }

    void OnDisable()
    {
        playAll = waitingForNext = false;
        if (Application.isPlaying) battle?.EndAnimationTestMode();
        if (canvasRoot) canvasRoot.gameObject.SetActive(false);
    }

    void OnEnable()
    {
        if (canvasRoot) { canvasRoot.gameObject.SetActive(true); Exit(); }
    }
}
