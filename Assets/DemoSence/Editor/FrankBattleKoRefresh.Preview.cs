using System;
using System.IO;
using System.Linq;
using System.Text;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void PreviewRefreshedBattleKo()
        {
            Directory.CreateDirectory(KoReview);
            var report = new StringBuilder();
            BattleComboAuthoring.WithStudy((scene, game, fighters) =>
            {
                var ko = game.uiManager.knockout;
                if (!ko.ink || ko.letterK.GetComponent<Text>().font !=
                    UnityEditor.AssetDatabase.LoadAssetAtPath<Font>(KoFont))
                    throw new InvalidOperationException("The refreshed KO paint or rounded font is missing.");
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>())
                    .Single(c => c.CompareTag("MainCamera"));
                camera.scene = scene;
                foreach (var canvas in scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<Canvas>(true)))
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = 1;
                }
                ko.PreviewAnimations = true;
                var source = game.leftCombat;
                var target = game.rightCombat;
                var move = source.lightCombatMoves[0];
                source.transform.position = new Vector3(-.5f, 0, 0);
                target.transform.position = new Vector3(-.5f + move.attackRange, 0, 0);
                if (!source.ExecuteAttack(move, target, true))
                    throw new InvalidOperationException("Cannot pose the knockout scene.");
                source.SourcePlayback.EvaluateAt(source.SourcePlayback.Duration);
                game.uiManager.right.hpSlider.value = 0;
                game.uiManager.right.healthText.text = "0/1000";
                try
                {
                    foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(720, 1280),
                        new Vector2Int(2560, 1080) })
                    {
                        var texture = new RenderTexture(size.x, size.y, 24);
                        try
                        {
                            camera.targetTexture = texture;
                            camera.GetComponent<FrankCinematicCamera>().Apply(0, true);
                            Canvas.ForceUpdateCanvases();
                            ko.ResetPresentation();
                            if (!ko.Present(MemeBattleUI.Side.Right, "Pepe"))
                                throw new InvalidOperationException("The KO preview did not start.");
                            foreach (float time in new[] { .04f, .10f, .19f, .24f, .29f, .35f, .44f, .7f })
                            {
                                ko.EvaluatePreview(time);
                                Canvas.ForceUpdateCanvases();
                                CaptureBattleCamera(camera, KoReview + "/KO_" + size.x + "x" + size.y +
                                    "_" + Mathf.RoundToInt(time * 1000) + "ms.png");
                            }
                            var root = (RectTransform)ko.transform;
                            foreach (var letter in new[] { ko.letterK, ko.letterO })
                            {
                                if (letter.GetComponent<Text>().fontSize != 300 ||
                                    Vector3.Distance(letter.localScale, Vector3.one * KoLetterScale) > .001f)
                                    throw new InvalidOperationException(
                                        "KO lost its enlarged scale after scene loading.");
                                var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(root, letter);
                                if (bounds.min.x < root.rect.xMin + 20 || bounds.max.x > root.rect.xMax - 20 ||
                                    bounds.min.y < root.rect.yMin + 20 || bounds.max.y > root.rect.yMax - 20)
                                    throw new InvalidOperationException("KO lettering is clipped at " + size);
                            }
                            if (ko.group.blocksRaycasts || ko.group.interactable)
                                throw new InvalidOperationException("KO intercepts battle input.");
                            if (ko.band.gameObject.activeSelf || ko.burst.Progress < .999f)
                                throw new InvalidOperationException("KO band or impact rays remain during the hold.");
                            if (ko.Present(MemeBattleUI.Side.Left, "Mankey") || ko.ShowCount != 1)
                                throw new InvalidOperationException("Repeated KO bypassed the match latch.");
                            ko.EvaluatePreview(3);
                            if (ko.IsShowing || ko.group.alpha > .001f)
                                throw new InvalidOperationException("KO did not fade out.");
                            ko.ResetPresentation();
                            if (ko.HasShown || ko.IsShowing || ko.Pending || ko.group.alpha != 0 ||
                                DOTween.IsTweening(ko))
                                throw new InvalidOperationException("KO reset retained presentation state.");
                            report.AppendLine("PASS " + size + ": entrance/impact frames captured; " +
                                "enlarged rounded text fits; comic ink renders; impact rays finish; " +
                                "input passes through; duplicate KO suppressed; fade/reset clear all tweens.");
                        }
                        finally
                        {
                            camera.targetTexture = null;
                            Object.DestroyImmediate(texture);
                        }
                    }
                    ko.Present(MemeBattleUI.Side.Left, "Mankey");
                    ko.EvaluatePreview(.18f);
                    // Preview scenes do not run this MonoBehaviour's Play Mode OnDisable callbacks.
                    ko.ResetPresentation();
                    if (ko.IsShowing || ko.HasShown || ko.group.alpha != 0 || DOTween.IsTweening(ko))
                        throw new InvalidOperationException($"Reset during entrance: showing={ko.IsShowing}; " +
                            $"shown={ko.HasShown}; alpha={ko.group.alpha}; tweens={DOTween.TotalTweensById(ko)}.");
                    foreach (var letter in new[] { ko.letterK, ko.letterO })
                        if (Vector3.Distance(letter.localScale, Vector3.one * KoLetterScale) > .001f ||
                            Quaternion.Angle(letter.localRotation, Quaternion.identity) > .01f)
                            throw new InvalidOperationException("Interrupted KO retained a foreground transform.");
                    report.AppendLine("PASS interruption during entrance clears the overlay and its tweens.");
                }
                finally
                {
                    ko.ResetPresentation();
                    source.SourcePlayback?.Cancel();
                }
            });
            File.WriteAllText(KoReview + "/Validation.txt", report.ToString());
        }

        public static void CheckRefreshedBattleKoPlay() => FrankBattleUiPlayCheck.Start();
    }
}
