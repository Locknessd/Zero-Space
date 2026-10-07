using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static partial class BattlePresentationWorkbench
{
    static string contactCaptureLabel;
    static int contactCaptureCount;
    static bool capturePreferencesSaved;
    static float savedMotion;
    static float savedFlash;
    static bool savedBloom;

    public static void PrepareCleanCapture()
    {
        var panel = UnityEngine.Object.FindFirstObjectByType<BattleAnimationTestPanel>();
        if (panel && panel.OpenButton)
            panel.OpenButton.gameObject.SetActive(false);
    }

    public static void CaptureHeavyLeft() => CaptureMove("Heavy_6", 0, false);
    public static void CaptureHeavyRight() => CaptureMove("Heavy_6", 1, false);
    public static void CaptureReducedHeavy() => CaptureMove("Heavy_6", 0, true);
    public static void CaptureReducedLight() => CaptureMove("Light_1", 0, true);
    public static void CaptureLight() => CaptureMove("Light_1", 0, false);

    static void CaptureMove(string name, int side, bool reduced)
    {
        if (!EditorApplication.isPlaying)
            throw new InvalidOperationException("Capture requires BattleScene Play Mode.");
        var game = Battle();
        game.battleVfx.ContactOccurred -= CaptureContact;
        game.StopAnimationTest();
        ConfigureCapturePreferences(reduced);
        if (!game.BeginAnimationTestMode())
            throw new InvalidOperationException("Battle is busy.");
        contactCaptureLabel = reduced ? "Reduced" : "Final";
        contactCaptureCount = 0;
        game.battleVfx.ContactOccurred += CaptureContact;
        var fighter = side == 0 ? game.leftCombat : game.rightCombat;
        var move = fighter.lightCombatMoves.Concat(fighter.heavyCombatMoves).Single(m => m.moveName == name);
        if (!game.PlayAnimationTest(side == 0 ? PlayerUI.Side.Left : PlayerUI.Side.Right, move))
            throw new InvalidOperationException("Replay was rejected.");
        PrepareCleanCapture();
    }

    static void ConfigureCapturePreferences(bool reduced)
    {
        var shake = UnityEngine.Object.FindFirstObjectByType<BattleCameraShake>();
        var flash = UnityEngine.Object.FindFirstObjectByType<BattleImpactFeedback>();
        var volume = UnityEngine.Object.FindFirstObjectByType<Volume>();
        if (!volume.profile.TryGet<Bloom>(out var bloom))
            throw new InvalidOperationException("Battle Bloom override missing.");
        if (!capturePreferencesSaved)
        {
            savedMotion = shake.motionScale;
            savedFlash = flash.flashScale;
            savedBloom = bloom.active;
            capturePreferencesSaved = true;
        }
        shake.motionScale = reduced ? 0 : savedMotion;
        flash.flashScale = reduced ? 0 : savedFlash;
        bloom.active = !reduced && savedBloom;
    }

    public static void RestoreCapturePreferences()
    {
        Battle().battleVfx.ContactOccurred -= CaptureContact;
        if (capturePreferencesSaved)
            ConfigureCapturePreferences(false);
        PrepareCleanCapture();
    }

    static void CaptureContact(BattleVfxPlayer.Impact impact)
    {
        contactCaptureCount++;
        PrepareCleanCapture();
        string name = contactCaptureLabel + "_" + impact.attacker.name + "_" + impact.move.moveName +
            "_" + impact.kind + "_" + contactCaptureCount;
        capturePath = Review + "/" + name + ".png";
        captureAfter = Time.frameCount + 1;
        captureEarliest = EditorApplication.timeSinceStartup + .045;
        var report = new StringBuilder();
        report.AppendLine($"{name}: event={impact.eventId}; seconds={impact.seconds:R}; " +
            $"sample={impact.playback.SampleTime:R}; point={impact.position}; frame={Time.frameCount}");
        foreach (var renderer in impact.playback.AttackerActor.Pose.weaponRenderers)
        {
            if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;
            report.AppendLine($"Weapon {renderer.name}: bounds={renderer.bounds}; " +
                $"position={renderer.transform.position}; rotation={renderer.transform.rotation.eulerAngles}; " +
                $"scale={renderer.transform.lossyScale}");
            if (renderer is SkinnedMeshRenderer skin)
            {
                var mesh = new Mesh();
                skin.BakeMesh(mesh, false);
                report.AppendLine($"Baked {skin.name}: bounds={mesh.bounds}");
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
        File.AppendAllText(Review + "/RuntimeContacts.txt", report.ToString());
    }

    public static void ReloadSavedBattle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before reloading.");
        var scene = SceneManager.GetSceneByPath(BattlePath);
        if (scene.isDirty)
            throw new InvalidOperationException("Save intended BattleScene changes before reload.");
        EditorSceneManager.CloseScene(scene, true);
        scene = EditorSceneManager.OpenScene(BattlePath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        File.WriteAllText(Review + "/ReloadPersistence.txt", "Reloaded saved BattleScene\n" + Status());
        AuditScene();
    }
}
