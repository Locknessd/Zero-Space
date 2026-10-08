using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordGrounding
    {
        const int BakeRate = 240;
        const int ValidationRate = 361;
        const float MaximumLift = .75f;
        const string ActionRoot = "Assets/CombatExpansion/Actions/";

        public static void Bake() => BakeIndices(new[] { 0, 1, 2, 3 });

        public static void BakeAmbush() => BakeIndices(new[] { 0 });

        static void BakeIndices(int[] indices)
        {
            var report = new StringBuilder("fighter,action,role,seconds,clearance,baseLift,storedLift\n");
            try
            {
                WithFighters(fighters =>
                {
                    foreach (int index in indices)
                    {
                        var tracks = new List<FrankPairGrounding.Track>();
                        string action = null;
                        foreach (var source in fighters)
                        {
                            var target = fighters.Single(f => f != source);
                            Reset(fighters);
                            var move = CombatExpansionGreatSwordStudy.MakeMove(source, target, index);
                            move.grounding = null;
                            if (action != null && action != move.moveName)
                                throw new InvalidOperationException("Action differs between avatars.");
                            action = move.moveName;
                            Sample(source, target, move, 1, pair =>
                            {
                                int intervals = Intervals(pair.Duration, BakeRate);
                                var actors = new[] { source, target };
                                var sampled = actors.Select((actor, role) => new FrankPairGrounding.Track
                                {
                                    avatar = actor.Animator.avatar,
                                    receiver = role == 1,
                                    duration = pair.Duration,
                                    lift = new float[intervals + 1]
                                }).ToArray();
                                var clearances = actors.Select(actor => new float[intervals + 1]).ToArray();
                                for (int frame = 0; frame <= intervals; frame++)
                                {
                                    float seconds = pair.Duration * frame / intervals;
                                    pair.EvaluateAt(seconds);
                                    for (int role = 0; role < actors.Length; role++)
                                    {
                                        float clearance = Clearance(actors[role]);
                                        float lift = Mathf.Max(0, .01f - clearance);
                                        if (!float.IsFinite(lift) || lift > MaximumLift)
                                            throw new InvalidOperationException(action +
                                                " needs manual floor adaptation: " + lift);
                                        sampled[role].lift[frame] = lift;
                                        clearances[role][frame] = clearance;
                                    }
                                }
                                for (int role = 0; role < actors.Length; role++)
                                {
                                    var original = sampled[role].lift;
                                    var stored = new float[original.Length];
                                    // Read only original lifts so padding never spreads beyond one adjacent sample.
                                    for (int frame = 0; frame <= intervals; frame++)
                                    {
                                        float lift = Mathf.Max(original[frame],
                                            Mathf.Max(original[Mathf.Max(0, frame - 1)],
                                                original[Mathf.Min(intervals, frame + 1)]));
                                        if (!float.IsFinite(lift) || lift > MaximumLift)
                                            throw new InvalidOperationException(action +
                                                " needs manual floor adaptation: " + lift);
                                        stored[frame] = lift;
                                        float seconds = pair.Duration * frame / intervals;
                                        float clearance = clearances[role][frame];
                                        report.AppendLine(FormattableString.Invariant(
                                            $"{Csv(actors[role].name)},{Csv(action)},{Role(role)},") +
                                            FormattableString.Invariant(
                                                $"{seconds:R},{clearance:R},{original[frame]:R},{lift:R}"));
                                    }
                                    sampled[role].lift = stored;
                                }
                                tracks.AddRange(sampled);
                            });
                        }
                        string path = AssetPath(action);
                        var asset = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(path);
                        if (!asset)
                        {
                            if (AssetDatabase.LoadMainAssetAtPath(path))
                                throw new InvalidOperationException("Unexpected asset type at " + path);
                            asset = ScriptableObject.CreateInstance<FrankPairGrounding>();
                            AssetDatabase.CreateAsset(asset, path);
                        }
                        asset.tracks = tracks.ToArray();
                        EditorUtility.SetDirty(asset);
                        AssetDatabase.SaveAssetIfDirty(asset);
                    }
                });
            }
            finally
            {
                WriteReport(indices.Length == 4 ? "GreatSwordGrounding.csv" : "GreatSwordAmbushGrounding.csv",
                    report.ToString());
            }
        }

        static void WithFighters(Action<CharacterCombat[]> action)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before GreatSword grounding.");
            var positioning = CombatPositioningController.Instance;
            var property = typeof(CombatPositioningController).GetProperty("Instance");
            var scene = default(UnityEngine.SceneManagement.Scene);
            try
            {
                scene = EditorSceneManager.OpenPreviewScene(CombatExpansionInventory.Battle);
                property.SetValue(null, null);
                var game = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                if (fighters.Any(f => !f) || fighters[0] == fighters[1])
                    throw new InvalidOperationException("BattleScene must have two distinct fighters.");
                foreach (var fighter in fighters)
                {
                    fighter.battleSfx = null;
                    fighter.battleVfx = null;
                    fighter.hitEffect = null;
                    fighter.Initialize();
                    if (!fighter.Animator || !fighter.Animator.avatar || !fighter.Animator.avatar.isHuman ||
                        !fighter.Animator.GetBoneTransform(HumanBodyBones.Hips))
                        throw new InvalidOperationException("Invalid humanoid fighter: " + fighter.name);
                }
                if (fighters[0].Animator.avatar == fighters[1].Animator.avatar)
                    throw new InvalidOperationException("Expected two distinct BattleScene avatars.");
                action(fighters);
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
                    property.SetValue(null, positioning);
                }
            }
        }

        static void Reset(CharacterCombat[] fighters)
        {
            foreach (var fighter in fighters)
                fighter.ResetCombat();
        }

        static void Sample(CharacterCombat source, CharacterCombat target, CombatTripletData move,
            int direction, Action<FrankBattlePairPlayback> sample)
        {
            if (!float.IsFinite(move.attackRange) || move.attackRange <= 0)
                throw new InvalidOperationException("Invalid range for " + move.moveName);
            source.Animator.transform.position = Vector3.zero;
            target.Animator.transform.position = Vector3.right * (direction * move.attackRange * .99f);
            try
            {
                if (!source.ExecuteAttack(move, target) || !source.SourcePlayback || !source.SourcePlayback.Playing)
                    throw new InvalidOperationException("Cannot sample " + move.moveName);
                sample(source.SourcePlayback);
            }
            finally
            {
                if (source.SourcePlayback)
                    source.SourcePlayback.Cancel();
            }
        }

        static int Intervals(float duration, int rate)
        {
            if (!float.IsFinite(duration) || duration <= 0)
                throw new InvalidOperationException("Invalid pair duration: " + duration);
            return Mathf.Max(1, Mathf.CeilToInt(duration * rate));
        }

        static float Clearance(CharacterCombat fighter)
        {
            foreach (var transform in fighter.Animator.GetComponentsInChildren<Transform>(true))
            {
                var matrix = transform.localToWorldMatrix;
                for (int component = 0; component < 16; component++)
                    if (!float.IsFinite(matrix[component]))
                        throw new InvalidOperationException("Nonfinite transform: " + transform.name);
            }
            float clearance = BattlePresentationContactSetup.MeasureGroundClearance(fighter);
            if (!float.IsFinite(clearance))
                throw new InvalidOperationException("Missing or invalid rendered body geometry: " + fighter.name);
            return clearance;
        }

        static string AssetPath(string action) => ActionRoot + action + "_Grounding.asset";
        static string Role(int role) => role == 0 ? "attacker" : "receiver";
        static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

        static void WriteReport(string name, string contents)
        {
            Directory.CreateDirectory(CombatExpansionInventory.Output);
            File.WriteAllText(Path.Combine(CombatExpansionInventory.Output, name), contents);
        }
    }
}
