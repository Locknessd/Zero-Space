using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FrankRetarget;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class BattleComboAuthoring
    {
        internal static void BakeGrounding()
        {
            var report = new StringBuilder("combo,actor,receiver,seconds,floor,lift\n");
            WithStudy((scene, game, fighters) =>
            {
                foreach (var data in Library.pairs.Where(p => p.step == 0))
                {
                    var tracks = new List<FrankPairGrounding.Track>();
                    foreach (var source in fighters)
                    {
                        foreach (var fighter in fighters)
                            fighter.ResetCombat();
                        var target = fighters.Single(f => f != source);
                        var move = MakeMove(source, target, data);
                        move.grounding = null;
                        source.transform.position = Vector3.zero;
                        target.transform.position = Vector3.right * move.attackRange;
                        if (!source.ExecuteAttack(move, target))
                            throw new InvalidOperationException("Grounding pair rejected.");
                        var playback = source.SourcePlayback;
                        try
                        {
                            int intervals = Mathf.CeilToInt(playback.Duration * 240);
                            var actors = new[] { source, target };
                            var sampled = actors.Select((actor, role) => new FrankPairGrounding.Track
                            {
                                avatar = actor.Animator.avatar,
                                receiver = role == 1,
                                duration = playback.Duration,
                                lift = new float[intervals + 1]
                            }).ToArray();
                            for (int frame = 0; frame <= intervals; frame++)
                            {
                                float seconds = playback.Duration * frame / intervals;
                                playback.EvaluateAt(seconds);
                                for (int role = 0; role < actors.Length; role++)
                                {
                                    float floor = BattlePresentationContactSetup.EvaluatedFloor(actors[role]);
                                    if (!float.IsFinite(floor))
                                        throw new InvalidOperationException("Cannot sample the visible skin floor.");
                                    float lift = Mathf.Max(0, .002f - floor);
                                    if (lift > .75f)
                                        throw new InvalidOperationException("Unexpected source floor error: " + lift);
                                    sampled[role].lift[frame] = lift;
                                    report.AppendLine($"{data.sourceName},{actors[role].name},{role == 1}," +
                                        $"{seconds:R},{floor:R},{lift:R}");
                                }
                            }
                            tracks.AddRange(sampled);
                        }
                        finally
                        {
                            playback.Cancel();
                        }
                    }
                    string path = Root + data.sourceName + "_Grounding.asset";
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
            });
            File.WriteAllText(Report + "/Grounding.csv", report.ToString());
        }
    }
}
