using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        const string FOOutput = "GeneratedAssets/CombatExpansion/SamuraiStudy/FighterOrientation/Execution02";
        const string FOGroundingPath = OrientationVariantAssets +
            "/Grounding/Samurai_Execution02_OriginalOrientation_Grounding.asset";
        const string FOScope = "PROVISIONAL Execution02 original-orientation clips on actual Mankey/Pepe fighters. " +
            "Native role drivers/Avatars, armed PlayerA, unarmed PlayerB, fingers transferred, spacing 1.7m. " +
            "Both assignments and lane directions; separate variant grounding; original source clocks. " +
            "No gameplay registration or approved damage contacts. Geometry alone never approves integration. ";

        static string FOClipPath(int role)
        {
            return OrientationVariantAssets + "/" + Roles[role] + "_Execution02_OriginalOrientation.FBX";
        }

        static void FOValidateOverrides(int execution, AnimationClip attack, AnimationClip reaction)
        {
            if (attack == null && reaction == null)
                return;
            if (!attack || !reaction || execution != 2)
                throw new InvalidOperationException("Both Execution02 orientation clips are required together.");
            var originals = ResolveSources();
            var clips = new[] { attack, reaction };
            for (int role = 0; role < clips.Length; role++)
            {
                var clip = clips[role];
                var original = originals.Single(s => s.role == Roles[role] && s.localId == 7400004);
                string path = FOClipPath(role);
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid) || guid == Guids[role] ||
                    AssetDatabase.GetAssetPath(clip) != path ||
                    CombatExpansionInventory.Identity(clip) != guid + ":7400004" ||
                    !clip.humanMotion || clip.legacy || clip.name != original.clip ||
                    !float.IsFinite(clip.length) || clip.length <= 0 || clip.length != original.durationSeconds ||
                    clip.frameRate != original.framesPerSecond ||
                    !AnimationUtility.GetAnimationClipSettings(clip).keepOriginalOrientation)
                    throw new InvalidOperationException("Unexpected orientation clip identity/type/timing: " + path);
            }
        }

        static SourceRecord FOVariantSource(SourceRecord original, AnimationClip clip)
        {
            var result = JsonUtility.FromJson<SourceRecord>(JsonUtility.ToJson(original));
            result.path = AssetDatabase.GetAssetPath(clip);
            result.guid = AssetDatabase.AssetPathToGUID(result.path);
            result.rootSettings.keepOriginalOrientation = true;
            result.asset = clip;
            // Avatar deliberately stays the original native source Avatar used by the role driver.
            return result;
        }
    }
}
