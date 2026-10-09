using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        const string FOBindingPath = FOOutput + "/Bake/GroundingBinding.json";

        [Serializable]
        sealed class FOBinding
        {
            public string groundingIdentity, groundingSha256;
            public string[] clipIdentities, avatarIdentities;
            public float duration;
            public int bakeRate = GroundingBakeRate;
            public float cushion = GroundingCushion, maximumLift = GroundingMaximumLift;
            public NativeReferenceFile[] inputs;
        }

        static FOBinding FOBind(FrankPairGrounding grounding, AnimationClip[] clips, CharacterCombat[] fighters)
        {
            var paths = new HashSet<string>();
            foreach (var clip in clips)
                FOAddFile(paths, AssetDatabase.GetAssetPath(clip));
            foreach (string guid in Guids)
                FOAddFile(paths, AssetDatabase.GUIDToAssetPath(guid));
            foreach (var fighter in fighters)
            {
                FOAddFile(paths, AssetDatabase.GetAssetPath(fighter.Animator.avatar));
                for (int role = 0; role < 2; role++)
                    foreach (string dependency in AssetDatabase.GetDependencies(
                        DriverPath(fighter.name, role, role == 0), true))
                        FOAddFile(paths, dependency);
            }
            return new FOBinding
            {
                groundingIdentity = CombatExpansionInventory.Identity(grounding),
                groundingSha256 = NativeReferenceHash(FOGroundingPath),
                clipIdentities = clips.Select(CombatExpansionInventory.Identity).ToArray(),
                avatarIdentities = fighters.Select(f => CombatExpansionInventory.Identity(f.Animator.avatar)).ToArray(),
                duration = Mathf.Max(clips[0].length, clips[1].length),
                inputs = paths.OrderBy(p => p).Select(p => new NativeReferenceFile
                {
                    path = p,
                    sha256 = NativeReferenceHash(p)
                }).ToArray()
            };
        }

        static FrankPairGrounding FOLoadGrounding(AnimationClip[] clips, CharacterCombat[] fighters)
        {
            FOValidateOverrides(2, clips[0], clips[1]);
            CheckGroundingFighters(fighters);
            var grounding = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(FOGroundingPath);
            if (!grounding || !File.Exists(FOBindingPath))
                throw new InvalidOperationException("Bake variant grounding and its source binding first.");
            string bakeReportPath = FOOutput + "/Bake/Provenance.json";
            var bakeReport = File.Exists(bakeReportPath) ?
                JsonUtility.FromJson<FOReport>(File.ReadAllText(bakeReportPath)) : null;
            if (bakeReport == null || !bakeReport.sourceGuardsPassed ||
                !bakeReport.status.StartsWith("COMPLETED Bake", StringComparison.Ordinal))
                throw new InvalidOperationException("Latest variant bake did not complete with preservation guards.");
            CheckGroundingTracks(grounding.tracks, fighters);
            var binding = JsonUtility.FromJson<FOBinding>(File.ReadAllText(FOBindingPath));
            if (binding == null || JsonUtility.ToJson(binding) != JsonUtility.ToJson(FOBind(grounding, clips, fighters)))
                throw new InvalidOperationException("Stale variant grounding/source/driver binding; rebake required.");
            foreach (var track in grounding.tracks)
                if (!float.IsFinite(track.duration) || Mathf.Abs(track.duration - binding.duration) > .0001f)
                    throw new InvalidOperationException("Invalid variant grounding duration.");
            return grounding;
        }
    }
}
