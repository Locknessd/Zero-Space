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
    public static class CombatExpansionThrowGrounding
    {
        public static void Bake()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before grounding calibration.");
            var positioning = CombatPositioningController.Instance;
            var property = typeof(CombatPositioningController).GetProperty("Instance");
            var scene = EditorSceneManager.OpenPreviewScene(CombatExpansionInventory.Battle);
            var report = new StringBuilder("attacker,action,role,seconds,sourceClearance,liftMetres\n");
            try
            {
                property.SetValue(null, null);
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var fighter in fighters)
                {
                    fighter.battleSfx = null;
                    fighter.battleVfx = null;
                    fighter.hitEffect = null;
                    fighter.Initialize();
                }
                foreach (var spec in CombatExpansionThrowSetup.Specs)
                {
                    var tracks = new List<FrankPairGrounding.Track>();
                    string actionId = null;
                    foreach (var source in fighters)
                    {
                        var target = fighters.Single(f => f != source);
                        foreach (var fighter in fighters)
                            fighter.ResetCombat();
                        var move = CombatExpansionThrowSetup.MakeMove(source, target, spec.index);
                        move.grounding = null;
                        actionId = move.moveName;
                        source.Animator.transform.position = Vector3.zero;
                        target.Animator.transform.position = Vector3.right * (move.attackRange * .99f);
                        if (!source.ExecuteAttack(move, target))
                            throw new InvalidOperationException("Cannot sample " + actionId);
                        var pair = source.SourcePlayback;
                        try
                        {
                            int samples = Mathf.CeilToInt(pair.Duration * 240);
                            var actors = new[] { source, target };
                            var sampled = actors.Select((actor, index) => new FrankPairGrounding.Track
                            {
                                avatar = actor.Animator.avatar,
                                receiver = index == 1,
                                duration = pair.Duration,
                                lift = new float[samples + 1]
                            }).ToArray();
                            for (int frame = 0; frame <= samples; frame++)
                            {
                                float seconds = pair.Duration * frame / samples;
                                pair.EvaluateAt(seconds);
                                for (int role = 0; role < 2; role++)
                                {
                                    float clearance = BattlePresentationContactSetup.MeasureGroundClearance(actors[role]);
                                    float lift = Mathf.Max(0, .01f - clearance);
                                    if (!float.IsFinite(lift) || lift > .75f)
                                        throw new InvalidOperationException(actionId + " needs manual floor adaptation: " + lift);
                                    sampled[role].lift[frame] = lift;
                                    report.AppendLine(FormattableString.Invariant(
                                        $"{source.name},{actionId},{role},{seconds:R},{clearance:R},{lift:R}"));
                                }
                            }
                            tracks.AddRange(sampled);
                        }
                        finally
                        {
                            pair.Cancel();
                        }
                    }
                    string path = "Assets/CombatExpansion/Actions/" + actionId + "_Grounding.asset";
                    var asset = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(path);
                    if (!asset)
                    {
                        asset = ScriptableObject.CreateInstance<FrankPairGrounding>();
                        AssetDatabase.CreateAsset(asset, path);
                    }
                    asset.tracks = tracks.ToArray();
                    EditorUtility.SetDirty(asset);
                    AssetDatabase.SaveAssetIfDirty(asset);
                }
            }
            finally
            {
                File.WriteAllText(CombatExpansionInventory.Output + "/ThrowGrounding.csv", report.ToString());
                EditorSceneManager.ClosePreviewScene(scene);
                property.SetValue(null, positioning);
            }
        }
    }
}
