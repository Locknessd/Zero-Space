using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        const string ComicReview = "GeneratedAssets/BattleComicReview";

        static void ComicScene(Action<GameManager, Camera> apply)
        {
            Directory.CreateDirectory(ComicReview);
            if (!File.Exists(ComicReview + "/BattleSceneBefore.unity.txt")) File.Copy(SfxScene, ComicReview + "/BattleSceneBefore.unity.txt");
            var scene = SceneManager.GetSceneByPath(SfxScene);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(SfxScene, OpenSceneMode.Additive);
            var active = SceneManager.GetActiveScene();
            try
            {
                SceneManager.SetActiveScene(scene);
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single(c => c.CompareTag("MainCamera"));
                string beforeCamera = EditorJsonUtility.ToJson(camera) + EditorJsonUtility.ToJson(camera.transform) + EditorJsonUtility.ToJson(camera.GetComponent<FrankCinematicCamera>());
                apply(game, camera);
                string afterCamera = EditorJsonUtility.ToJson(camera) + EditorJsonUtility.ToJson(camera.transform) + EditorJsonUtility.ToJson(camera.GetComponent<FrankCinematicCamera>());
                if (beforeCamera != afterCamera) throw new Exception("Comic setup changed camera settings.");
                AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Could not save Battle comic setup.");
            }
            finally
            {
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        public static void InstallBattleComicStep1()
        {
            ComicScene((game, camera) =>
            {
                var host = game.uiManager.knockout.transform.parent as RectTransform;
                var root = host.Find("Contact Flash") as RectTransform;
                if (!root) root = BattleUiRect(host, "Contact Flash", new Vector2(190, 190), Vector2.zero);
                var flash = root.GetComponent<BattleImpactFlashGraphic>();
                if (!flash) flash = root.gameObject.AddComponent<BattleImpactFlashGraphic>();
                flash.raycastTarget = false; flash.Progress = 1;
                var impact = game.GetComponent<BattleImpactFeedback>();
                if (!impact) impact = Undo.AddComponent<BattleImpactFeedback>(game.gameObject);
                impact.vfx = game.battleVfx; impact.battleCamera = camera; impact.flash = flash;
                impact.lightHold = .035f; impact.heavyHold = .075f; impact.groundHold = .055f;
                EditorUtility.SetDirty(impact);
            });
            File.WriteAllText(ComicReview + "/Step1.txt", "Installed light/heavy/ground hit-stop 35/75/55ms and localized unscaled comic contact flash. Camera unchanged.\n");
        }

        public static void ValidateBattleComicStep1()
        {
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var feedback = game.GetComponent<BattleImpactFeedback>();
                if (!feedback || feedback.vfx != game.battleVfx || !feedback.flash || feedback.flash.raycastTarget) throw new Exception("Missing hit-stop/flash bindings.");
                feedback.Bind();
                game.leftCombat.Initialize(); game.rightCombat.Initialize();
                var move = game.leftCombat.heavyCombatMoves.Single(m => m.moveName == "Heavy_6");
                game.leftCombat.transform.position = new Vector3(-.5f, 0, 0);
                game.rightCombat.transform.position = game.leftCombat.transform.position + Vector3.right * move.attackRange;
                game.leftCombat.ExecuteAttack(move, game.rightCombat);
                var playback = game.leftCombat.SourcePlayback;
                var hit = game.battleVfx.timeline.FindMove(move).cues.First(c => c.group == "heavy_hit");
                playback.EvaluateAt(hit.seconds); game.battleVfx.AdvanceSequence(playback, hit.seconds);
                if (feedback.HitStopCount != 1 || feedback.flash.Progress != 0) throw new Exception("Impact cue did not produce exactly one flash.");
                game.battleVfx.AdvanceSequence(playback, hit.seconds);
                if (feedback.HitStopCount != 1) throw new Exception("Repeated cue retriggered feedback.");
                feedback.AdvanceFeedback(.15f);
                if (feedback.flash.Progress != 1) throw new Exception("Unscaled flash did not end.");
                playback.Cancel();
                if (feedback.IsHolding || feedback.flash.Progress != 1) throw new Exception("Cancelled impact retained feedback.");
                File.WriteAllText(ComicReview + "/Step1Validation.txt", "PASS heavy cue, no repeated cue, unscaled flash decay and cancellation. Live hold/clock restoration checked in Play Mode.\n");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        public static void InstallBattleComicStep5()
        {
            ComicScene((game, camera) =>
            {
                var host = game.uiManager.knockout.transform.parent as RectTransform;
                var existing = host.Find("Critical Comic Panel");
                if (existing) Object.DestroyImmediate(existing.gameObject);
                var root = BattleUiRect(host, "Critical Comic Panel", new Vector2(0, 210), new Vector2(0, 12));
                root.anchorMin = new Vector2(.07f, .63f); root.anchorMax = new Vector2(.93f, .63f);
                var presentation = root.gameObject.AddComponent<BattleComicCutIn>();
                presentation.game = game; presentation.vfx = game.battleVfx; presentation.band = root;
                presentation.group = root.gameObject.AddComponent<CanvasGroup>();
                presentation.background = root.gameObject.AddComponent<BattleComicPanelGraphic>(); presentation.background.raycastTarget = false;
                var portraitFrame = BattleUiRect(root, "Portrait frame", new Vector2(166, 166), Vector2.zero);
                portraitFrame.anchorMin = portraitFrame.anchorMax = new Vector2(.14f, .5f);
                var shadow = portraitFrame.gameObject.AddComponent<Image>(); shadow.color = new Color(.055f, .025f, .065f, 1); shadow.raycastTarget = false;
                var portrait = BattleUiImage(portraitFrame, "Fighter portrait", Color.white); StretchBattleUi(portrait.rectTransform);
                portrait.rectTransform.offsetMin = Vector2.one * 5; portrait.rectTransform.offsetMax = Vector2.one * -5; portrait.preserveAspect = true;
                presentation.portrait = portrait;
                var images = game.gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Image>(true)).ToArray();
                presentation.leftPortrait = images.First(i => i.name == "Image" && i.transform.parent.name == "AvatarBg1").sprite;
                presentation.rightPortrait = images.First(i => i.name == "Image" && i.transform.parent.name == "AvatarBg1 red").sprite;
                var font = game.uiManager.left.speechBubbleText.font;
                presentation.title = BattleUiLabel(root, "Move title", font, "", 56, Vector2.zero, new Vector2(0, 20));
                presentation.title.rectTransform.anchorMin = new Vector2(.29f, .32f); presentation.title.rectTransform.anchorMax = new Vector2(.94f, .9f);
                presentation.title.fontStyle = FontStyle.BoldAndItalic; presentation.title.resizeTextForBestFit = true;
                presentation.title.resizeTextMinSize = 24; presentation.title.resizeTextMaxSize = 56;
                var outline = presentation.title.gameObject.AddComponent<Outline>(); outline.effectDistance = new Vector2(2, -2); outline.effectColor = new Color(.055f, .025f, .065f, 1);
                presentation.caption = BattleUiLabel(root, "Fighter caption", font, "", 22, Vector2.zero, Vector2.zero);
                presentation.caption.rectTransform.anchorMin = new Vector2(.29f, .08f); presentation.caption.rectTransform.anchorMax = new Vector2(.94f, .3f);
                presentation.caption.color = new Color(.08f, .025f, .055f, 1);
                presentation.ResetPresentation(); game.uiManager.comicCutIn = presentation; EditorUtility.SetDirty(game.uiManager);
                // Keep KO above the small windup panel.
                game.uiManager.knockout.transform.SetAsLastSibling();
            });
            WriteBattleComicPresentationNotes();
        }

        public static void UpdateBattleComicPresentation()
        {
            ComicScene((game, camera) =>
            {
                var cut = game.uiManager.comicCutIn;
                if (!cut) throw new Exception("Missing Battle comic panel.");
                Undo.RecordObject(cut, "Compact battle announcements");
                cut.compactPresentation = true;
                cut.compactDisplaySeconds = .72f;
                cut.compactWidth = .48f;
                cut.compactAnchorY = .81f;
                cut.compactHeight = 110f;
                cut.ResetPresentation();
                cut.title.text = cut.caption.text = "";
                EditorUtility.SetDirty(cut); EditorUtility.SetDirty(cut.title); EditorUtility.SetDirty(cut.caption);
            });
            WriteBattleComicPresentationNotes();
        }

        static void WriteBattleComicPresentationNotes()
        {
            File.WriteAllText(ComicReview + "/Step5.txt", "Native Unity UI/Text + DOTween portrait panels for Katana/Assassin and lethal windups.\n" +
                "English titles: BLADE FURY!, PHANTOM STRIKE!, FINISHING BLOW!; fighter caption + CRITICAL/FINISHER.\n" +
                "Compact duration 720ms: 90ms entrance, 510ms hold, 120ms fade. Upper side panel keeps the contact area clear.\n" +
                "Existing avatars, unscaled animation, reset/cancel support, no raycast blocking and no camera change.\n");
        }

        public static void InstallBattleComicStep6()
        {
            ComicScene((game, camera) =>
            {
                var ui = game.uiManager;
                var host = ui.knockout.transform.parent as RectTransform;
                var hud = ui.GetComponent<BattleHudFeedback>();
                if (!hud) hud = Undo.AddComponent<BattleHudFeedback>(ui.gameObject);
                hud.ui = ui; hud.game = game; hud.vfx = game.battleVfx;
                for (int i = 0; i < 2; i++)
                {
                    var slot = i == 0 ? ui.left : ui.right;
                    var parent = slot.hpSlider.fillRect.parent;
                    var old = parent.Find("Damage chip trail"); if (old) Object.DestroyImmediate(old.gameObject);
                    var chip = BattleUiImage(parent, "Damage chip trail", new Color(1, .69f, .22f, .88f));
                    chip.rectTransform.SetAsFirstSibling(); chip.raycastTarget = false;
                    var fill = slot.hpSlider.fillRect.GetComponent<Image>(); if (fill) chip.sprite = fill.sprite;
                    var oldBadge = slot.damageText.transform.parent.Find("Critical badge"); if (oldBadge) Object.DestroyImmediate(oldBadge.gameObject);
                    var badge = BattleUiLabel(slot.damageText.transform.parent, "Critical badge", slot.damageText.font, "", 20, new Vector2(150, 28), new Vector2(i == 0 ? -150 : 150, 16));
                    badge.fontStyle = FontStyle.BoldAndItalic;
                    badge.gameObject.AddComponent<Outline>().effectColor = new Color(.08f, .02f, .03f, 1);
                    var oldCombo = host.Find(i == 0 ? "Left combo" : "Right combo"); if (oldCombo) Object.DestroyImmediate(oldCombo.gameObject);
                    var combo = BattleUiLabel(host, i == 0 ? "Left combo" : "Right combo", slot.damageText.font, "", 32, new Vector2(240, 52), Vector2.zero);
                    combo.rectTransform.anchorMin = combo.rectTransform.anchorMax = new Vector2(i == 0 ? .17f : .83f, .35f);
                    combo.fontStyle = FontStyle.BoldAndItalic;
                    var outline = combo.gameObject.AddComponent<Outline>(); outline.effectColor = new Color(.06f, .035f, .09f, 1); outline.effectDistance = new Vector2(2, -2);
                    if (i == 0) { hud.leftChip = chip; hud.leftCritical = badge; hud.leftCombo = combo; }
                    else { hud.rightChip = chip; hud.rightCritical = badge; hud.rightCombo = combo; }
                }
                ui.hudFeedback = hud; hud.ResetEffects();
                EditorUtility.SetDirty(hud); EditorUtility.SetDirty(ui);
            });
            File.WriteAllText(ComicReview + "/Step6.txt", "HP updates immediately; separate gold lost-health slice waits 200ms then drains in 450ms. Duplicate HP snapshots do not restart chips. Critical numbers pop 1.4x with an orange badge; combos count actual hit VFX cues and expire in unscaled time. Damage remains above HP; dialogue and turn-only layout preserved.\n");
        }

        public static void ValidateBattleComicStep6()
        {
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            Camera camera = null; var target = new RenderTexture(1280, 720, 24);
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>()).Single(c => c.CompareTag("MainCamera"));
                camera.scene = scene; camera.targetTexture = target;
                foreach (var canvas in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)))
                { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
                var ui = game.uiManager; var hud = ui.hudFeedback;
                ui.SetTurnNumber(1);
                ui.left.hpSlider.value = ui.right.hpSlider.value = 1; hud.Bind(); hud.ResetEffects();
                foreach (var side in new[] {MemeBattleUI.Side.Left, MemeBattleUI.Side.Right})
                {
                    hud.Health(side, .6f); hud.AdvanceHud(.15f);
                    if (hud.DelayedHealth(side) < .99f) throw new Exception("Damage chip did not hold old HP.");
                    hud.Health(side, .6f); hud.AdvanceHud(.3f);
                    if (hud.DelayedHealth(side) >= .99f || hud.DelayedHealth(side) <= .6f) throw new Exception("Damage chip duplicated or did not drain.");
                    hud.AdvanceHud(.3f);
                    if (Mathf.Abs(hud.DelayedHealth(side) - .6f) > .001f) throw new Exception("Damage chip did not converge.");
                    hud.Health(side, 1);
                    if (hud.DelayedHealth(side) != 1) throw new Exception("Healing did not reset the chip.");
                }
                game.leftCombat.Initialize(); game.rightCombat.Initialize(); camera.GetComponent<FrankCinematicCamera>().Apply(0, true);
                ui.textEffects.PreviewAnimations = true;
                ui.UpdateHealth(MemeBattleUI.Side.Left, 650, 1000); ui.UpdateHealth(MemeBattleUI.Side.Right, 450, 1000);
                ui.ShowDamage(MemeBattleUI.Side.Left, 350, true); ui.ShowDamage(MemeBattleUI.Side.Right, 550, false);
                ui.textEffects.EvaluatePreview(.16f); hud.AdvanceHud(.1f);
                var layout = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BattleHudCallouts>(true)).Single();
                Canvas.ForceUpdateCanvases(); layout.RefreshLayout(); Canvas.ForceUpdateCanvases();
                CaptureBattleCamera(camera, ComicReview + "/Step6_Hud.png");
                if (ui.left.hpSlider.value != .65f || ui.right.hpSlider.value != .45f || !hud.leftChip.enabled || hud.leftCritical.text != "CRITICAL!")
                    throw new Exception("Immediate HP/critical HUD failed.");
                ui.ResetTransientEffects();
                if (hud.leftChip.enabled || hud.rightChip.enabled || hud.leftCritical.text != "") throw new Exception("HUD reset retained feedback.");
                File.WriteAllText(ComicReview + "/Step6Validation.txt", "PASS immediate HP, delayed chips on both sides, duplicate snapshots, convergence, healing, independent critical/damage labels and reset.\n");
            }
            finally { if (camera) camera.targetTexture = null; Object.DestroyImmediate(target); EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
