using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordRecoveryStudy
    {
        public const string Output = "GeneratedAssets/CombatExpansion/GreatSwordRecoveryStudy";
        const string FrankRoot = "Assets/Selected/Frank_Damages/Asset/Animations/Damages_InPlace/";
        static readonly HumanBodyBones[] Bones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Chest, HumanBodyBones.Head,
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot
        };

        sealed class RecoveryClip
        {
            public AnimationClip clip;
            public string role;
            public string references;
        }

        [MenuItem("Tools/Battle/Studies/GreatSword recovery poses")]
        public static void Capture()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            Directory.CreateDirectory(Output);
            string statusPath = Output + "/Status.txt";
            File.WriteAllText(statusPath, "RUNNING recovery motion capture\n");
            var singletonTypes = new[]
            {
                typeof(CombatPositioningController), typeof(GameManager), typeof(MemeBattleUI),
                typeof(MortalKombatCamera), typeof(WebSocketManager)
            };
            var properties = singletonTypes.Select(t => t.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static)).Where(p => p != null && p.CanWrite).ToArray();
            var values = properties.Select(p => p.GetValue(null)).ToArray();
            var scene = default(UnityEngine.SceneManagement.Scene);
            var index = new StringBuilder("Recovery motion evidence only; no gameplay or transition approval.\n" +
                "Ambush and Execution1 require prone recovery; Execution2 and Execution3 require supine recovery.\n" +
                "Candidate names do not establish suitability. Inspect initial pose and complete motion.\n" +
                "Each PNG has 12 samples, left to right then top to bottom; paired oblique and side views.\n" +
                "Each TXT identifies exact seconds, clip GUID/localID, import rig, and actual fighter Avatar.\n" +
                "CSV contains world positions and right/up/forward orientation bases at 0, 0.1, 0.25 and end.\n" +
                "Humanoid clips run directly on the fighter Avatar, primed at zero then advanced at 60 Hz.\n" +
                "Root motion is applied by Animator; no controller blend or endpoint alignment is simulated.\n\n");
            try
            {
                scene = EditorSceneManager.OpenPreviewScene(CombatExpansionInventory.Battle);
                var game = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var candidates = new[]
                {
                    LoadCandidate("Frank_Damage@Damage_Getup01_P_iP.FBX"),
                    LoadCandidate("Frank_Damage@Damage_Getup02_L_iP.FBX")
                };
                foreach (var fighter in new[] { game.leftCombat, game.rightCombat })
                {
                    if (!fighter || !fighter.Animator || !fighter.Animator.avatar ||
                        !fighter.Animator.avatar.isHuman || !fighter.Animator.avatar.isValid)
                        throw new InvalidOperationException("Missing actual valid fighter Avatar.");
                    var current = fighter.lightCombatMoves
                        .Where(m => m != null && m.sourcePair != null && m.sourcePair.getUp)
                        .GroupBy(m => m.sourcePair.getUp)
                        .Select(g => new RecoveryClip
                        {
                            clip = g.Key,
                            role = "current_light_getup",
                            references = string.Join("; ", g.Select(m => m.moveName))
                        }).ToArray();
                    if (current.Length == 0)
                        throw new InvalidOperationException("No light sourcePair.getUp on " + fighter.name);
                    foreach (var source in current.Concat(candidates))
                    {
                        ValidateClip(source.clip);
                        string identity = CombatExpansionInventory.Identity(source.clip);
                        string stem = fighter.name + "_" + source.role + "_" + identity.Replace(':', '_');
                        using (var preview = new RecoveryPreview(fighter.Animator, source.clip))
                        {
                            preview.WriteBones(Output + "/" + stem + ".csv");
                            preview.WriteSheet(Output + "/" + stem + ".png");
                            File.WriteAllText(Output + "/" + stem + ".txt",
                                Describe(fighter, source, preview.FrameSeconds));
                        }
                        index.AppendLine(stem);
                        index.AppendLine("  " + source.clip.name + " | " + identity);
                    }
                }
                File.WriteAllText(Output + "/Index.txt", index.ToString());
                File.WriteAllText(statusPath, "CAPTURED recovery motion evidence. Visual selection, controller " +
                    "transitions, endpoint alignment and living victim PlayMode checks remain required.\n");
            }
            catch (Exception error)
            {
                File.WriteAllText(statusPath, "FAILED: " + error + "\nPartial files are not a completed study.\n");
                throw;
            }
            finally
            {
                try
                {
                    if (scene.IsValid())
                        EditorSceneManager.ClosePreviewScene(scene);
                }
                finally
                {
                    for (int i = 0; i < properties.Length; i++)
                        properties[i].SetValue(null, values[i]);
                }
            }
        }

        static RecoveryClip LoadCandidate(string file)
        {
            string path = FrankRoot + file;
            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            if (clips.Length != 1)
                throw new InvalidOperationException("Expected one nonpreview animation subasset: " + path);
            ValidateClip(clips[0]);
            return new RecoveryClip { clip = clips[0], role = "candidate", references = "Unassigned" };
        }

        static void ValidateClip(AnimationClip clip)
        {
            if (!clip || clip.legacy || !clip.humanMotion || clip.length <= .25f ||
                !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long localId) ||
                string.IsNullOrEmpty(guid) || localId == 0)
                throw new InvalidOperationException("Recovery clip must be a persistent humanoid animation.");
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) as ModelImporter;
            if (importer && importer.animationType != ModelImporterAnimationType.Human)
                throw new InvalidOperationException("Recovery source import rig is not Humanoid: " + clip.name);
        }

        static string Describe(CharacterCombat fighter, RecoveryClip source, float[] seconds)
        {
            string path = AssetDatabase.GetAssetPath(source.clip);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            return "Recovery motion capture only; suitability requires visual review.\n" +
                "Fighter: " + fighter.name + "\nAvatar: " + fighter.Animator.avatar.name + "\nAvatar identity: " +
                CombatExpansionInventory.Identity(fighter.Animator.avatar) + "\nAvatar path: " +
                AssetDatabase.GetAssetPath(fighter.Animator.avatar) + "\nRole: " + source.role +
                "\nCurrent move references: " + source.references + "\nClip: " + source.clip.name +
                "\nClip path: " + path + "\nClip GUID:localID: " + CombatExpansionInventory.Identity(source.clip) +
                "\nImport rig: " + (importer ? importer.animationType.ToString() : "Native animation asset") +
                "\nHuman motion: " + source.clip.humanMotion + "\nDuration seconds: " +
                source.clip.length.ToString("R", CultureInfo.InvariantCulture) + "\nFrame seconds: " +
                string.Join(", ", seconds.Select(t => t.ToString("R", CultureInfo.InvariantCulture))) +
                "\nFrames 1-12 read across each row then down; each frame has oblique view then side view.\n" +
                "Clip copy has looping disabled; root motion enabled; direct actual Avatar playable at 60 Hz.\n";
        }
    }
}
