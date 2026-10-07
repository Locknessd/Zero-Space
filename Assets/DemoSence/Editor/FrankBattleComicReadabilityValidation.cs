using System;
using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void ValidateBattleComicStep5()
        {
            Directory.CreateDirectory(ComicReview);
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var targets = new[]
            {
                new RenderTexture(1280, 720, 24),
                new RenderTexture(1024, 768, 24),
                new RenderTexture(720, 1280, 24)
            };
            Camera camera = null;
            int checkedCases = 0;
            try
            {
                var roots = scene.GetRootGameObjects();
                var game = roots.SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                camera = roots.SelectMany(r => r.GetComponentsInChildren<Camera>())
                    .Single(c => c.CompareTag("MainCamera"));
                camera.scene = scene;
                foreach (var canvas in roots.SelectMany(r => r.GetComponentsInChildren<Canvas>(true)))
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = 1;
                }
                game.leftCombat.Initialize();
                game.rightCombat.Initialize();
                var cut = game.uiManager.comicCutIn;
                if (!cut || !cut.leftPortrait || !cut.rightPortrait || cut.group.blocksRaycasts)
                    throw new Exception("Missing comic panel bindings.");
                if (!cut.compactPresentation || Mathf.Abs(cut.PresentationSeconds - .72f) > .001f)
                    throw new Exception("Saved scene must enable the 720ms compact announcement.");
                cut.PreviewAnimations = true;
                cut.Bind();
                Vector2 originalAnchor = cut.band.anchorMin;
                var corners = new Vector3[4];
                foreach (var target in targets)
                {
                    camera.targetTexture = target;
                    camera.GetComponent<FrankCinematicCamera>().Apply(0, true);
                    Canvas.ForceUpdateCanvases();
                    foreach (bool left in new[] { true, false })
                    for (int variant = 0; variant < 5; variant++)
                    {
                        bool lethal = variant == 2;
                        var weapon = variant == 0
                            ? TrumpWeaponManager.WeaponType.Katana : TrumpWeaponManager.WeaponType.Assassin;
                        var skill = variant == 3 ? BattleSkill.Archer :
                            variant == 4 ? BattleSkill.WhiteMage : BattleSkill.None;
                        string expected = lethal ? "FINISHING BLOW!" : variant == 0 ? "BLADE FURY!" :
                            variant == 3 ? "METEOR SHOT!" : variant == 4 ? "CELESTIAL SMITE!" : "PHANTOM STRIKE!";
                        cut.Present(left, weapon, lethal, skill);
                        cut.EvaluatePreview(cut.PresentationSeconds - .15f);
                        Canvas.ForceUpdateCanvases();
                        if (!cut.IsShowing || cut.group.alpha < .99f || cut.title.text != expected ||
                            cut.portrait.sprite != (left ? cut.leftPortrait : cut.rightPortrait))
                            throw new Exception("Announcement title, avatar or hold failed.");
                        if (cut.group.interactable || cut.group.blocksRaycasts)
                            throw new Exception("Announcement blocks battle input.");
                        cut.band.GetWorldCorners(corners);
                        foreach (var corner in corners)
                        {
                            Vector3 viewport = camera.WorldToViewportPoint(corner);
                            if (viewport.x < .005f || viewport.x > .995f ||
                                viewport.y < .72f || viewport.y > .94f)
                                throw new Exception("Announcement entered fight area or screen edge: " + viewport);
                        }
                        if (target == targets[0])
                            CaptureBattleCamera(camera, ComicReview + "/Compact_" +
                                (left ? "Mankey_" : "Pepe_") + variant + ".png");
                        cut.EvaluatePreview(cut.PresentationSeconds - .06f);
                        if (cut.group.alpha <= 0 || cut.group.alpha >= 1)
                            throw new Exception("Announcement did not fade smoothly.");
                        cut.EvaluatePreview(cut.PresentationSeconds + .1f);
                        if (cut.group.alpha > .001f) throw new Exception("Announcement did not finish.");
                        cut.Present(left, weapon, lethal, skill);
                        game.battleVfx.ClearEffects();
                        cut.EvaluatePreview(2);
                        if (cut.IsShowing || cut.group.alpha != 0 || cut.band.anchorMin != originalAnchor)
                            throw new Exception("Cancellation did not restore the banner layout.");
                        checkedCases++;
                    }
                }
                File.WriteAllText(ComicReview + "/Step5Validation.txt",
                    "PASS " + checkedCases + " compact announcements: both sides, Katana, Assassin, finisher, " +
                    "Archer and Mage at 16:9, 4:3 and portrait.\n" +
                    "PASS upper safe area, titles, portraits, 720ms timing, input pass through and cancellation.\n");
            }
            finally
            {
                if (camera) camera.targetTexture = null;
                foreach (var target in targets) Object.DestroyImmediate(target);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
