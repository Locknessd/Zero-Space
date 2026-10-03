using System;
using System.IO;
using System.Linq;
using System.Text;
using DG.Tweening;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        const string ChatReview = "GeneratedAssets/BattleChatUiReview";

        [MenuItem("Tools/Battle/Style chat bubbles and HP damage numbers")]
        public static void InstallBattleChatUi()
        {
            Directory.CreateDirectory(ChatReview);
            if (!File.Exists(ChatReview + "/BattleSceneBefore.unity.txt"))
                File.Copy(SfxScene, ChatReview + "/BattleSceneBefore.unity.txt");
            var scene = SceneManager.GetSceneByPath(SfxScene);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(SfxScene, OpenSceneMode.Additive);
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var ui = game.uiManager;
                if (!ui || !ui.textEffects || !ui.roundNumberText) throw new Exception("Install the battle text presentation first.");
                RemoveLegacyBattleChat(scene, ui);
                var canvas = ui.roundNumberText.GetComponentInParent<Canvas>();
                var previous = canvas.transform.Find("Battle HUD Callouts");
                if (previous) Undo.DestroyObjectImmediate(previous.gameObject);
                var root = BattleUiRect(canvas.transform, "Battle HUD Callouts", Vector2.zero, Vector2.zero);
                StretchBattleUi(root);
                root.SetSiblingIndex(1); // The KO overlay stays above the HUD cards.
                Undo.RegisterCreatedObjectUndo(root.gameObject, "Restyle battle dialogue and damage");
                var layout = root.gameObject.AddComponent<BattleHudCallouts>();
                layout.ui = ui;
                Undo.RecordObject(ui, "Bind dedicated chat and damage labels");
                var bodyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                foreach (bool right in new[] { false, true })
                {
                    var slot = right ? ui.right : ui.left;
                    // The original score panel contains only the shared dialogue/damage label.
                    if (slot.speechBubbleText && !slot.speechBubble)
                    {
                        var oldPanel = slot.speechBubbleText.transform.parent;
                        if (oldPanel && oldPanel.GetComponentsInChildren<Slider>(true).Length == 0)
                        { Undo.RecordObject(oldPanel.gameObject, "Hide original shared speech panel"); oldPanel.gameObject.SetActive(false); }
                    }
                    slot.speechBubble = BuildBattleChatCard(root, right, bodyFont);
                    slot.speechBubbleText = slot.speechBubble.dialogue;
                    var damageAnchor = BattleUiRect(root, right ? "Right HP damage anchor" : "Left HP damage anchor",
                        new Vector2(180, 58), Vector2.zero);
                    damageAnchor.pivot = new Vector2(.5f, 0);
                    var damage = BattleUiLabel(damageAnchor, "Damage number", slot.nameText.font,
                        string.Empty, 44, new Vector2(180, 58), Vector2.zero);
                    damage.rectTransform.anchorMin = damage.rectTransform.anchorMax = new Vector2(.5f, 0);
                    damage.rectTransform.pivot = new Vector2(.5f, 0);
                    damage.alignment = TextAnchor.LowerCenter;
                    damage.alignByGeometry = true;
                    damage.fontStyle = FontStyle.BoldAndItalic;
                    damage.color = new Color(1, .76f, .2f);
                    var outline = damage.gameObject.AddComponent<Outline>();
                    outline.effectColor = new Color(.09f, .025f, .035f, 1);
                    outline.effectDistance = new Vector2(2, -2);
                    var shadow = damage.gameObject.AddComponent<Shadow>();
                    shadow.effectColor = new Color(.07f, .025f, .015f, .6f);
                    shadow.effectDistance = new Vector2(0, -3);
                    slot.damageText = damage;
                    if (right) layout.rightDamageAnchor = damageAnchor; else layout.leftDamageAnchor = damageAnchor;
                }
                Canvas.ForceUpdateCanvases();
                layout.RefreshLayout();
                EditorUtility.SetDirty(ui);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Could not save styled battle chat UI.");
                File.WriteAllText(ChatReview + "/Install.txt", "Saved rounded cream dialogue cards with team borders, speaker labels, tail and shadow.\nDamage uses separate outlined gold labels 8 HUD units above the actual HP bars, independently of dialogue.\nCards wrap/resize from the full message, hide when empty and use unscaled DOTween entrance/typewriter animation.\n");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        [MenuItem("Tools/Battle/Remove legacy chat bubbles")]
        public static void RemoveLegacyBattleChatUi()
        {
            var scene = SceneManager.GetSceneByPath(SfxScene);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(SfxScene, OpenSceneMode.Additive);
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                int removed = RemoveLegacyBattleChat(scene, game.uiManager);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Could not save legacy chat cleanup.");
                Directory.CreateDirectory(ChatReview);
                File.WriteAllText(ChatReview + "/LegacyCleanup.txt",
                    $"Removed {removed} obsolete speech/score panels from BattleScene; cleared both legacy meme-result references.\n" +
                    "ARGUMENT_SELECTED uses the styled HUD card only. New card tails point up/out towards the left/right portraits.\n");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        static int RemoveLegacyBattleChat(Scene scene, MemeBattleUI ui)
        {
            if (!ui) throw new Exception("Battle HUD is missing.");
            Undo.RecordObject(ui, "Remove obsolete meme-result bindings");
            foreach (var slot in new[] { ui.left, ui.right })
            {
                slot.memeResultObject = null;
                slot.memeResultText = null;
            }
            var obsolete = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .Where(t => t.name == "SpeechBubble_TrumpVictim" || t.name == "ScoreCyan" || t.name == "ScoreRed")
                .Where(t => t is RectTransform && !t.GetComponentInParent<BattleSpeechBubble>() &&
                    t.GetComponentsInChildren<Slider>(true).Length == 0).ToArray();
            foreach (var panel in obsolete) Undo.DestroyObjectImmediate(panel.gameObject);
            EditorUtility.SetDirty(ui);
            return obsolete.Length;
        }

        static BattleSpeechBubble BuildBattleChatCard(Transform parent, bool right, Font font)
        {
            var root = BattleUiRect(parent, right ? "Right chat bubble" : "Left chat bubble", new Vector2(500, 118), Vector2.zero);
            root.pivot = new Vector2(.5f, 1);
            var bubble = root.gameObject.AddComponent<BattleSpeechBubble>();
            var panel = BattleUiRect(root, "Dialogue card", Vector2.zero, Vector2.zero);
            StretchBattleUi(panel);
            bubble.panel = panel;
            bubble.group = panel.gameObject.AddComponent<CanvasGroup>();
            var frame = panel.gameObject.AddComponent<BattleSpeechBubbleGraphic>();
            frame.raycastTarget = false;
            frame.tailOnRight = right;
            frame.border = right ? new Color(.91f, .2f, .37f) : new Color(.08f, .65f, .75f);
            var speaker = BattleUiLabel(panel, "Speaker", font, string.Empty, 18, new Vector2(450, 25), Vector2.zero);
            speaker.rectTransform.anchorMin = new Vector2(0, 1); speaker.rectTransform.anchorMax = Vector2.one;
            speaker.rectTransform.pivot = new Vector2(.5f, 1);
            speaker.rectTransform.sizeDelta = new Vector2(-52, 25);
            speaker.rectTransform.anchoredPosition = new Vector2(0, -16);
            speaker.alignment = TextAnchor.MiddleLeft;
            speaker.color = right ? new Color(.65f, .1f, .25f) : new Color(.04f, .42f, .52f);
            bubble.speaker = speaker;
            var dialogue = BattleUiLabel(panel, "Dialogue", font, string.Empty, 32, Vector2.zero, Vector2.zero);
            StretchBattleUi(dialogue.rectTransform);
            dialogue.rectTransform.offsetMin = new Vector2(24, 22);
            dialogue.rectTransform.offsetMax = new Vector2(-24, -50);
            dialogue.alignment = TextAnchor.MiddleLeft;
            dialogue.fontStyle = FontStyle.Normal;
            dialogue.color = new Color(.08f, .1f, .15f);
            dialogue.horizontalOverflow = HorizontalWrapMode.Wrap;
            dialogue.verticalOverflow = VerticalWrapMode.Truncate;
            dialogue.resizeTextForBestFit = true;
            dialogue.resizeTextMinSize = 24;
            dialogue.resizeTextMaxSize = 32;
            dialogue.lineSpacing = 1.04f;
            bubble.dialogue = dialogue;
            bubble.ResetPresentation();
            return bubble;
        }

        [MenuItem("Tools/Battle/Preview styled chat and HP damage numbers")]
        public static void PreviewBattleChatUi()
        {
            Directory.CreateDirectory(ChatReview);
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            Camera camera = null;
            var report = new StringBuilder();
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var ui = game.uiManager;
                if (ui.left.memeResultObject || ui.right.memeResultObject || ui.left.memeResultText || ui.right.memeResultText ||
                    scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                        .Any(t => t.name == "SpeechBubble_TrumpVictim" || t.name == "ScoreCyan" || t.name == "ScoreRed"))
                    throw new Exception("Legacy chat panels or activation bindings remain in BattleScene.");
                report.AppendLine("PASS legacy world bubbles, old score panels and their activation bindings removed.");
                var layout = ui.left.speechBubble.GetComponentInParent<BattleHudCallouts>();
                camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>()).Single(c => c.CompareTag("MainCamera"));
                camera.scene = scene;
                foreach (var canvas in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)))
                { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
                foreach (var fighter in new[] { game.leftCombat, game.rightCombat }) fighter.Initialize();
                ui.textEffects.PreviewAnimations = true;
                ui.left.speechBubble.PreviewAnimations = ui.right.speechBubble.PreviewAnimations = true;
                ui.roundNumberText.text = "Turn 3";
                foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(1024, 768) })
                {
                    var target = new RenderTexture(size.x, size.y, 24);
                    try
                    {
                        camera.targetTexture = target;
                        camera.GetComponent<FrankCinematicCamera>().Apply(0, true);
                        Canvas.ForceUpdateCanvases(); layout.RefreshLayout();
                        ui.SetDialogue(MemeBattleUI.Side.Left, "Đòn này nhẹ thôi... đừng ngã nha!");
                        ui.SetDialogue(MemeBattleUI.Side.Right, "Đợi đã! Cho tôi uống miếng trà rồi đánh tiếp!");
                        ui.left.damageText.text = "-125"; ui.right.damageText.text = "-380";
                        ui.textEffects.Damage(ui.left.damageText); ui.textEffects.Damage(ui.right.damageText);
                        ui.textEffects.EvaluatePreview(.2f);
                        ui.left.speechBubble.EvaluatePreview(.3f); ui.right.speechBubble.EvaluatePreview(.3f);
                        // Render the complete dialogue while retaining the damage's pop/rise pose.
                        ui.left.speechBubbleText.text = ui.left.speechBubble.Content;
                        ui.right.speechBubbleText.text = ui.right.speechBubble.Content;
                        Canvas.ForceUpdateCanvases(); layout.RefreshLayout(); Canvas.ForceUpdateCanvases();
                        ValidateBattleChatGeometry(ui, camera, report, size.ToString());
                        CaptureBattleCamera(camera, ChatReview + "/ChatDamage_" + size.x + "x" + size.y + ".png");
                        ui.textEffects.ResetEffects();
                    }
                    finally { camera.targetTexture = null; Object.DestroyImmediate(target); }
                }
                ui.SetDialogue(MemeBattleUI.Side.Left, string.Empty); ui.SetDialogue(MemeBattleUI.Side.Right, string.Empty);
                ui.left.speechBubble.EvaluatePreview(.2f); ui.right.speechBubble.EvaluatePreview(.2f);
                if (ui.left.speechBubble.IsVisible || ui.right.speechBubble.IsVisible) throw new Exception("Empty chat card remained visible.");
                report.AppendLine("PASS empty dialogue hides both cards; independent damage labels, wrapped Vietnamese text and input pass-through verified.");
                File.WriteAllText(ChatReview + "/PreviewValidation.txt", report.ToString());
            }
            catch (Exception error) { File.WriteAllText(ChatReview + "/PreviewValidation.txt", report + "FAIL " + error); throw; }
            finally
            {
                if (camera) camera.targetTexture = null;
                EditorSceneManager.ClosePreviewScene(scene);
                if (DOTween.TotalActiveTweens() == 0) DOTween.Clear(true);
            }
        }

        static void ValidateBattleChatGeometry(MemeBattleUI ui, Camera camera, StringBuilder report, string resolution)
        {
            var corners = new Vector3[4];
            foreach (var slot in new[] { ui.left, ui.right })
            {
                if (!slot.damageText || slot.damageText == slot.speechBubbleText || !slot.speechBubble.IsVisible)
                    throw new Exception("Missing independent chat/damage bindings.");
                ((RectTransform)slot.hpSlider.transform).GetWorldCorners(corners);
                float barTop = RectTransformUtility.WorldToScreenPoint(camera, corners[1]).y;
                slot.damageText.rectTransform.GetWorldCorners(corners);
                float damageBottom = RectTransformUtility.WorldToScreenPoint(camera, corners[0]).y;
                if (damageBottom <= barTop) throw new Exception("Damage is not above its HP bar.");
                slot.speechBubble.dialogue.rectTransform.GetWorldCorners(corners);
                if (slot.speechBubble.group.blocksRaycasts || slot.speechBubble.group.interactable)
                    throw new Exception("Speech card blocks battle input.");
                var settings = slot.speechBubbleText.GetGenerationSettings(slot.speechBubbleText.rectTransform.rect.size);
                float preferred = slot.speechBubbleText.cachedTextGeneratorForLayout.GetPreferredHeight(slot.speechBubbleText.text, settings) / slot.speechBubbleText.pixelsPerUnit;
                if (preferred > slot.speechBubbleText.rectTransform.rect.height + 2)
                    throw new Exception("Preview dialogue does not fit its card.");
                report.AppendLine($"PASS {resolution} {slot.nameText.text}: damage {damageBottom - barTop:F1}px above HP; dialogue fits, card visible and input passes through.");
            }
        }
    }
}
