using System.Collections.Generic;
using System.IO;
using System.Linq;
using FrankRetarget;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class BattlePresentationWorkbench
{
    static readonly List<Transform> heldWeaponBones = new List<Transform>();
    static readonly List<Matrix4x4> heldWeaponMatrices = new List<Matrix4x4>();
    static FrankBattlePairPlayback heldWeaponPair;
    static int heldWeaponFrames;
    static int heldWeaponContacts;
    static int lastHeldFrame;
    static string heldWeaponFailure;

    public static void BeginWeaponHoldAudit()
    {
        Battle().battleVfx.ContactOccurred -= RecordHeldWeapon;
        Battle().battleVfx.ContactOccurred += RecordHeldWeapon;
        RenderPipelineManager.endCameraRendering -= CheckHeldWeapon;
        RenderPipelineManager.endCameraRendering += CheckHeldWeapon;
        heldWeaponBones.Clear();
        heldWeaponMatrices.Clear();
        heldWeaponPair = null;
        heldWeaponFrames = 0;
        heldWeaponContacts = 0;
        heldWeaponFailure = null;
        lastHeldFrame = -1;
    }

    static void RecordHeldWeapon(BattleVfxPlayer.Impact impact)
    {
        heldWeaponBones.Clear();
        heldWeaponMatrices.Clear();
        heldWeaponPair = impact.playback;
        foreach (var skin in impact.playback.AttackerActor.Pose.weaponRenderers.OfType<SkinnedMeshRenderer>())
        {
            if (!skin.enabled || !skin.gameObject.activeInHierarchy)
                continue;
            foreach (var bone in skin.bones)
            {
                if (!bone || heldWeaponBones.Contains(bone))
                    continue;
                heldWeaponBones.Add(bone);
                heldWeaponMatrices.Add(bone.localToWorldMatrix);
            }
        }
        if (heldWeaponBones.Count > 0)
            heldWeaponContacts++;
    }

    static void CheckHeldWeapon(ScriptableRenderContext context, Camera camera)
    {
        if (camera != Camera.main || Time.frameCount == lastHeldFrame || !heldWeaponPair ||
            !heldWeaponPair.Playing || !heldWeaponPair.IsContactHeld || heldWeaponBones.Count == 0)
            return;
        lastHeldFrame = Time.frameCount;
        heldWeaponFrames++;
        for (int i = 0; i < heldWeaponBones.Count; i++)
        {
            if (!heldWeaponBones[i])
                continue;
            var current = heldWeaponBones[i].localToWorldMatrix;
            for (int n = 0; n < 16; n++)
            {
                if (Mathf.Abs(current[n] - heldWeaponMatrices[i][n]) <= .0001f)
                    continue;
                heldWeaponFailure = $"Weapon bone moved during hold: {heldWeaponBones[i].name}; " +
                    $"frame={Time.frameCount}; sample={heldWeaponPair.SampleTime:R}";
            }
        }
    }

    public static void FinishWeaponHoldAudit()
    {
        Battle().battleVfx.ContactOccurred -= RecordHeldWeapon;
        RenderPipelineManager.endCameraRendering -= CheckHeldWeapon;
        string result = heldWeaponFailure ?? (heldWeaponFrames > 0 ? "PASS" : "FAIL no held frames observed");
        File.WriteAllText(Review + "/WeaponHoldRenderValidation.txt",
            $"{result}\nContacts with native skinned weapons={heldWeaponContacts}; " +
            $"held render frames checked={heldWeaponFrames}\n" +
            "World bone matrices checked at endCameraRendering against the exact contact event pose.\n");
        heldWeaponPair = null;
    }
}
