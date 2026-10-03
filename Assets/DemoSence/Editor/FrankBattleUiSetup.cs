using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using DG.Tweening;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        const string UiReviewFolder = "Temp/FrankRetarget/BattleUi";

        [MenuItem("Tools/Battle/Install animated text and KO")]
        public static void InstallBattleUi()
        {
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/BattleScene.unity");
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Additive);
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var ui = game.uiManager;
                if (!ui || !game.roundManager) throw new Exception("Missing battle HUD bindings.");
                var effects = ui.GetComponent<BattleUiTextEffects>();
                if (!effects) effects = Undo.AddComponent<BattleUiTextEffects>(ui.gameObject);
                Undo.RecordObjects(new Object[] { ui, game.roundManager }, "Bind battle text animations");
                ui.textEffects = effects;
                game.roundManager.textEffects = effects;
                var font = ui.left.nameText.font ? ui.left.nameText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                var previous = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "Battle Text Presentation");
                if (previous) Undo.DestroyObjectImmediate(previous);
                var canvasRoot = new GameObject("Battle Text Presentation", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                Undo.RegisterCreatedObjectUndo(canvasRoot, "Build battle text presentation");
                SceneManager.MoveGameObjectToScene(canvasRoot, scene);
                canvasRoot.layer = LayerMask.NameToLayer("UI");
                var canvas = canvasRoot.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 500;
                var scaler = canvasRoot.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = .5f;

                var round = BattleUiLabel(canvasRoot.transform, "Round", font, "", 30, new Vector2(300, 46), new Vector2(0, -28));
                round.rectTransform.anchorMin = round.rectTransform.anchorMax = new Vector2(.5f, 1);
                round.color = new Color(1, .89f, .66f);
                var clock = BattleUiLabel(canvasRoot.transform, "Turn timer", font, "", 24, new Vector2(260, 42), new Vector2(0, -71));
                clock.rectTransform.anchorMin = clock.rectTransform.anchorMax = new Vector2(.5f, 1);
                var multiplier = BattleUiLabel(canvasRoot.transform, "Multiplier", font, "", 24, new Vector2(260, 40), new Vector2(0, -111));
                multiplier.rectTransform.anchorMin = multiplier.rectTransform.anchorMax = new Vector2(.5f, 1);
                multiplier.color = new Color(1, .71f, .26f);
                game.roundManager.roundNumberText = ui.roundNumberText = round;
                game.roundManager.timerText = ui.timerText = clock;
                game.roundManager.multiplierText = ui.multiplierText = multiplier;
                ApplyBattleTurnLayout(game);
                foreach (var speech in new[] { ui.left.speechBubbleText, ui.right.speechBubbleText }.Where(t => t))
                {
                    Undo.RecordObject(speech, "Fit animated dialogue text");
                    speech.resizeTextForBestFit = true;
                    speech.resizeTextMinSize = 18;
                    speech.resizeTextMaxSize = speech.fontSize = 32;
                    speech.horizontalOverflow = HorizontalWrapMode.Wrap;
                    speech.verticalOverflow = VerticalWrapMode.Truncate;
                    speech.raycastTarget = false;
                    EditorUtility.SetDirty(speech);
                }

                var koRoot = BattleUiRect(canvasRoot.transform, "KO", Vector2.zero, Vector2.zero);
                StretchBattleUi(koRoot);
                var presentation = koRoot.gameObject.AddComponent<BattleKoPresentation>();
                presentation.game = game;
                presentation.group = koRoot.gameObject.AddComponent<CanvasGroup>();
                presentation.backdrop = BattleUiImage(koRoot, "Dark backdrop", new Color(.025f, .008f, .018f, 0));
                StretchBattleUi(presentation.backdrop.rectTransform);
                var band = BattleUiImage(koRoot, "Impact band", new Color(.045f, .014f, .024f, .9f));
                presentation.band = band.rectTransform;
                presentation.band.anchorMin = new Vector2(.04f, .5f);
                presentation.band.anchorMax = new Vector2(.96f, .5f);
                presentation.band.sizeDelta = new Vector2(0, 335);
                presentation.band.anchoredPosition = new Vector2(0, 14);
                foreach (float edge in new[] { -1f, 1f })
                {
                    var line = BattleUiImage(presentation.band, edge < 0 ? "Lower gold edge" : "Upper gold edge", new Color(1, .53f, .11f, .8f));
                    line.rectTransform.anchorMin = new Vector2(0, edge < 0 ? 0 : 1);
                    line.rectTransform.anchorMax = new Vector2(1, edge < 0 ? 0 : 1);
                    line.rectTransform.sizeDelta = new Vector2(0, 3);
                }
                var burstRoot = BattleUiRect(koRoot, "Impact rays", Vector2.zero, Vector2.zero);
                StretchBattleUi(burstRoot);
                presentation.burst = burstRoot.gameObject.AddComponent<BattleKoBurstGraphic>();
                presentation.burst.raycastTarget = false;
                presentation.flash = BattleUiImage(koRoot, "Gold impact flash", new Color(1, .72f, .2f, 0));
                StretchBattleUi(presentation.flash.rectTransform);
                presentation.word = BattleUiRect(koRoot, "KO lettering", new Vector2(780, 340), new Vector2(0, 26));
                var k = BattleUiLabel(presentation.word, "K", font, "K", 252, new Vector2(250, 278), new Vector2(-119, 4));
                var o = BattleUiLabel(presentation.word, "O", font, "O", 252, new Vector2(250, 278), new Vector2(119, 4));
                foreach (var letter in new[] { k, o })
                {
                    letter.fontStyle = FontStyle.BoldAndItalic;
                    letter.gameObject.AddComponent<BattleTextGradient>();
                    var outline = letter.gameObject.AddComponent<Outline>();
                    outline.effectColor = new Color(.035f, .005f, .008f, 1);
                    outline.effectDistance = new Vector2(4, -4);
                    var shadow = letter.gameObject.AddComponent<Shadow>();
                    shadow.effectColor = new Color(.45f, .018f, .01f, 1);
                    shadow.effectDistance = new Vector2(8, -9);
                }
                presentation.letterK = k.rectTransform;
                presentation.letterO = o.rectTransform;
                presentation.subtitle = BattleUiLabel(presentation.word, "Knockout subtitle", font,
                    "K N O C K O U T", 26, new Vector2(640, 54), new Vector2(0, -113));
                presentation.defeatedLabel = BattleUiLabel(presentation.word, "Defeated fighter", font,
                    "", 20, new Vector2(640, 42), new Vector2(0, -160));
                presentation.ResetPresentation();
                ui.knockout = presentation;
                EditorUtility.SetDirty(ui);
                EditorUtility.SetDirty(game.roundManager);
                InstallBattleChatUi();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Could not save animated battle UI.");
                Directory.CreateDirectory(UiReviewFolder);
                File.WriteAllText(UiReviewFolder + "/install.txt", "Installed default Unity UI/Text + DOTween HUD animations and a canvas-native gold/red KO overlay.\nKO waits for the zero-HP fighter's sequence to finish, latches once per match and resets on a new match/queue reset.\n");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        [MenuItem("Tools/Battle/Show only turn number")]
        public static void SimplifyBattleTurnUi()
        {
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/BattleScene.unity");
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Additive);
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                ApplyBattleTurnLayout(game);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Could not save the turn-only battle HUD.");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        static void ApplyBattleTurnLayout(GameManager game)
        {
            // Keep references for server updates while displaying only the turn number.
            foreach (var label in new[] { game.uiManager.timerText, game.uiManager.multiplierText,
                game.uiManager.finalTotalsText, game.roundManager.timerText,
                game.roundManager.multiplierText, game.roundManager.finalTotalsText }.Where(t => t).Distinct())
            {
                Undo.RecordObject(label.gameObject, "Simplify battle turn UI");
                label.gameObject.SetActive(false);
                EditorUtility.SetDirty(label.gameObject);
            }
        }

        static RectTransform BattleUiRect(Transform parent, string name, Vector2 size, Vector2 position)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.layer = LayerMask.NameToLayer("UI");
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        static void StretchBattleUi(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        static Image BattleUiImage(Transform parent, string name, Color color)
        {
            var rect = BattleUiRect(parent, name, Vector2.zero, Vector2.zero);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static Text BattleUiLabel(Transform parent, string name, Font font, string content, int size, Vector2 area, Vector2 position)
        {
            var rect = BattleUiRect(parent, name, area, position);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = font;
            label.text = content;
            label.fontSize = size;
            label.fontStyle = FontStyle.Bold;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            return label;
        }

        [MenuItem("Tools/Battle/Preview text animations and KO")]
        public static void PreviewBattleUi()
        {
            Directory.CreateDirectory(UiReviewFolder);
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
            var landscape = new RenderTexture(1280, 720, 24);
            var portrait = new RenderTexture(720, 1280, 24);
            Camera camera = null;
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var ui = game.uiManager;
                if (!ui.textEffects || !ui.knockout || ui.knockout.game != game) throw new Exception("Missing animated UI bindings.");
                camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>()).Single(c => c.CompareTag("MainCamera"));
                camera.scene = scene;
                camera.targetTexture = landscape;
                foreach (var canvas in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)))
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = 1;
                }
                ui.textEffects.PreviewAnimations = ui.knockout.PreviewAnimations = true;
                if (ui.left.speechBubble) ui.left.speechBubble.PreviewAnimations = true;
                if (ui.right.speechBubble) ui.right.speechBubble.PreviewAnimations = true;
                game.roundManager.StartTurnTimer(3, null, DateTime.UtcNow.AddSeconds(8).ToString("O"));
                game.roundManager.ShowMultiplier(2);
                game.roundManager.ShowFinalTotals(new System.Collections.Generic.Dictionary<string, long> { ["turn_3_C"] = 42 });
                foreach (var fighter in new[] { game.leftCombat, game.rightCombat }) fighter.Initialize();
                camera.GetComponent<FrankCinematicCamera>().Apply(0, true);
                ui.left.healthText.text = "620/1000";
                ui.right.healthText.text = "850/1000";
                ui.textEffects.Pop(ui.left.healthText, true);
                ui.SetDialogue(MemeBattleUI.Side.Left, "Đòn này đau nha!");
                var damageLabel = ui.right.damageText ? ui.right.damageText : ui.right.speechBubbleText;
                damageLabel.text = "-380";
                ui.textEffects.Damage(damageLabel);
                ui.left.speechBubble?.EvaluatePreview(.3f);
                ui.textEffects.EvaluatePreview(.24f);
                Canvas.ForceUpdateCanvases();
                ui.left.speechBubble?.GetComponentInParent<BattleHudCallouts>()?.RefreshLayout();
                CaptureBattleCamera(camera, UiReviewFolder + "/TextAnimation.png");
                ui.textEffects.ResetEffects();
                ui.ClearDialogue(MemeBattleUI.Side.Left); ui.ClearDialogue(MemeBattleUI.Side.Right);
                if (ui.right.damageText) ui.right.damageText.text = string.Empty;
                ui.left.speechBubble?.EvaluatePreview(.2f); ui.right.speechBubble?.EvaluatePreview(.2f);
                var attacker = game.leftCombat;
                var receiver = game.rightCombat;
                var move = attacker.lightCombatMoves[0];
                attacker.transform.position = new Vector3(-.5f, 0, 0);
                receiver.transform.position = new Vector3(-.5f + move.attackRange, 0, 0);
                if (!attacker.ExecuteAttack(move, receiver, true)) throw new Exception("KO preview attack failed.");
                attacker.SourcePlayback.EvaluateAt(attacker.SourcePlayback.Duration);
                camera.GetComponent<FrankCinematicCamera>().Apply(0, true);
                ui.right.healthText.text = "0/1000";
                ui.right.hpSlider.value = 0;
                if (!ui.knockout.Present(MemeBattleUI.Side.Right, "Pepe")) throw new Exception("KO preview did not start.");
                ui.knockout.EvaluatePreview(.16f);
                Canvas.ForceUpdateCanvases();
                CaptureBattleCamera(camera, UiReviewFolder + "/KO_impact.png");
                ui.knockout.EvaluatePreview(.72f);
                Canvas.ForceUpdateCanvases();
                CaptureBattleCamera(camera, UiReviewFolder + "/KO_hold.png");
                camera.targetTexture = portrait;
                camera.GetComponent<FrankCinematicCamera>().Apply(0, true);
                Canvas.ForceUpdateCanvases();
                CaptureBattleCamera(camera, UiReviewFolder + "/KO_portrait.png");
                if (ui.knockout.group.blocksRaycasts || ui.knockout.group.interactable) throw new Exception("KO blocks input.");
                if (ui.knockout.Present(MemeBattleUI.Side.Left, "Mankey")) throw new Exception("Repeated KO was not suppressed.");
                ui.knockout.ResetPresentation();
                if (ui.knockout.group.alpha != 0 || ui.knockout.HasShown) throw new Exception("KO reset left an overlay active.");
                File.WriteAllText(UiReviewFolder + "/preview.txt", "PASS default Unity UI/Text renders with DOTween text/KO animation; landscape and portrait captured; duplicate KO suppressed; overlay reset and input pass-through verified.\n");
            }
            finally
            {
                if (camera) camera.targetTexture = null;
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(landscape);
                Object.DestroyImmediate(portrait);
                if (DOTween.TotalActiveTweens() == 0) DOTween.Clear(true);
            }
        }

        public static void SurveyBattleUi()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
            var report = new StringBuilder();
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var ui = game.uiManager;
                report.AppendLine("UI=" + ui + "; Round=" + game.roundManager);
                foreach (var label in new[] { ui.left.nameText, ui.left.healthText, ui.left.speechBubbleText,
                    ui.right.nameText, ui.right.healthText, ui.right.speechBubbleText,
                    game.roundManager.roundNumberText, game.roundManager.timerText, game.roundManager.multiplierText })
                {
                    if (!label) { report.AppendLine("Missing label"); continue; }
                    var canvas = label.GetComponentInParent<Canvas>(true);
                    report.AppendLine(AnimationUtility.CalculateTransformPath(label.transform, null) +
                        "; active=" + label.gameObject.activeInHierarchy + "; font=" + label.font +
                        "; size=" + label.fontSize + "; text=" + label.text +
                        "; rect=" + label.rectTransform.rect + "; canvas=" + canvas);
                }
                foreach (var canvas in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)))
                    report.AppendLine("Canvas " + canvas.name + ": active=" + canvas.gameObject.activeInHierarchy + "; render=" + canvas.renderMode + "; scale=" + canvas.transform.localScale);
                File.WriteAllText("Temp/FrankRetarget/battle-ui-survey.txt", report.ToString());
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
