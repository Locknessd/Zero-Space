using System.Linq;
using FrankRetarget;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class BattleUrpPolishSetup
{
    static void ConfigureFeedback(Scene scene, GameManager game, Camera camera)
    {
        var director = camera.GetComponent<FrankCinematicCamera>();
        if (director)
        {
            director.readableBattleFraming = true;
            director.battleMaximumPitch = 12;
            director.battleMaximumYaw = 12;
            director.battleMaximumAuthoredDistance = 6.8f;
            director.battleActionCenterBlend = .75f;
            director.battleTransitionSeconds = .36f;
            director.battleDistanceScale = .90f;
            EditorUtility.SetDirty(director);
        }
        var shake = camera.GetComponent<BattleCameraShake>();
        if (shake)
        {
            shake.screenSpaceImpulse = true;
            shake.motionScale = .75f;
            shake.lightPixels = 2;
            shake.heavyPixels = 6;
            shake.groundPixels = 4;
            shake.finishingPixels = 9;
            shake.maximumPixels = 9;
            shake.rollDegrees = 0;
            shake.lightDuration = .09f;
            shake.duration = .18f;
            EditorUtility.SetDirty(shake);
        }
        var feedback = game.GetComponent<BattleImpactFeedback>();
        if (feedback)
        {
            feedback.lightHold = 3f / 60;
            feedback.heavyHold = 6f / 60;
            feedback.groundHold = 4f / 60;
            feedback.finishingHold = 8f / 60;
            feedback.knockoutSeconds = .3f;
            feedback.knockoutRecovery = .16f;
            feedback.flashScale = .65f;
            feedback.surfaceFlashStrength = .55f;
            feedback.surfaceFlashSeconds = .065f;
            EditorUtility.SetDirty(feedback);
        }
        foreach (var banner in scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<BattleComicCutIn>(true)))
        {
            banner.compactPresentation = true;
            banner.compactDisplaySeconds = .72f;
            banner.compactWidth = .48f;
            banner.compactAnchorY = .81f;
            banner.compactHeight = 110;
            EditorUtility.SetDirty(banner);
        }
        if (game.battleSfx)
        {
            game.battleSfx.maxVoices = 12;
            game.battleSfx.enableContactLayers = true;
            game.battleSfx.reproducibleVariation = true;
            EditorUtility.SetDirty(game.battleSfx);
        }
    }
}
