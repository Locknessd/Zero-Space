using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionKbComboStudy
    {
        const string HighOutput = "GeneratedAssets/CombatExpansion/KbComboStudy/HighReactionLinks";
        static readonly float[] HighOnsets = { .1125f, .504166667f, .966666667f };

        public static void CaptureHighReactionLinks()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            Directory.CreateDirectory(HighOutput);
            File.WriteAllText(HighOutput + "/Scope.txt",
                "PROVISIONAL correction candidates; no gameplay registration or contact acceptance.\n" +
                "Same native JabL/JabR/HookR attack steps at 0/0.4/0.8s and 0/0.08/0.08s blends.\n" +
                "Previous grounded-geometry work is NOT assumed: the original study exposed floor penetration.\n" +
                "Head frontal weak reactions replace the two inappropriate mid-body responses.\n" +
                "Final high-right and high-left weak reactions are compared, not selected by clip name.\n" +
                "Candidate reaction onsets 0.1125/0.504166667/0.966666667 seconds remain unaccepted.\n" +
                "Spacings 0.8/0.9m, both avatars and lane directions. Transient 240Hz floor-lift envelope.\n" +
                "361Hz independent floor check; max permitted lift 0.2m, cushion 0.01m.\n" +
                "Contact checks use only Head skin and actual LeftHand then RightHand.\n" +
                "One closest sample per strike is measured here; dense timing remains required.\n" +
                "No source assets, gameplay scenes or persistent grounding assets are changed.\n");
            int completed = 0;
            try
            {
                foreach (long finalReaction in new long[] { 7400006, 7400008 })
                {
                    var sources = HighReactionSources(finalReaction);
                    for (int assignment = 0; assignment < 2; assignment++)
                    {
                        using var session = new SourceSession();
                        typeof(CombatPositioningController).GetProperty("Instance",
                            BindingFlags.Public | BindingFlags.Static).SetValue(null, null);
                        foreach (var fighter in session.Fighters)
                        {
                            fighter.battleSfx = null;
                            fighter.battleVfx = null;
                            fighter.hitEffect = null;
                            if (!fighter.Initialize())
                                throw new InvalidOperationException("Cannot initialize " + fighter.name);
                        }
                        FrankPairGrounding grounding = null;
                        try
                        {
                            foreach (int lane in new[] { 1, -1 })
                            foreach (float spacing in new[] { .8f, .9f })
                            {
                                File.WriteAllText(HighOutput + "/Status.txt", "RUNNING " + completed + "/16\n");
                                CaptureHighReactionCase(session.Fighters, sources, assignment, lane, spacing,
                                    ref grounding);
                                RequireSourcesUnchanged(sources);
                                completed++;
                            }
                        }
                        finally
                        {
                            if (grounding)
                                UnityEngine.Object.DestroyImmediate(grounding);
                        }
                    }
                }
                File.WriteAllText(HighOutput + "/Status.txt", "CAPTURED_PROVISIONAL " + completed + "/16\n" +
                    "Source links, directional reaction choice and accepted contact timing require review.\n");
            }
            catch (Exception error)
            {
                File.WriteAllText(HighOutput + "/Status.txt", "FAILED " + completed + "/16\n" + error);
                throw;
            }
        }

        static SourceRecord[] HighReactionSources(long finalReaction)
        {
            var result = ResolveSources();
            long[] reactions = { 7400004, finalReaction };
            string path = AssetDatabase.GUIDToAssetPath(ReactionGuid);
            for (int index = 0; index < reactions.Length; index++)
            {
                long wanted = reactions[index];
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(value =>
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long id) &&
                    guid == ReactionGuid && id == wanted);
                if (!clip.isHumanMotion || clip.legacy || Mathf.Abs(clip.length - .833333373f) > .00001f)
                    throw new InvalidOperationException("Unexpected native high reaction: " + wanted);
                result[index + 3] = new SourceRecord
                {
                    guid = ReactionGuid,
                    path = path,
                    localId = wanted,
                    name = clip.name,
                    length = clip.length,
                    clip = clip,
                    fileHash = Hash(path),
                    metaHash = Hash(path + ".meta")
                };
            }
            return result;
        }
    }
}
