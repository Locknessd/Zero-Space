using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static class CombatExpansionThrowGroundingCheck
    {
        public static void Validate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before checking sampled ground clearance.");
            var positioning = CombatPositioningController.Instance;
            var property = typeof(CombatPositioningController).GetProperty("Instance");
            var scene = EditorSceneManager.OpenPreviewScene(CombatExpansionInventory.Battle);
            var report = new StringBuilder("attacker,action,direction,role,minimumClearance,seconds,samples\n");
            float worst = float.PositiveInfinity;
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
                foreach (var source in fighters)
                foreach (var spec in CombatExpansionThrowSetup.Specs)
                foreach (int direction in new[] { 1, -1 })
                {
                    var target = fighters.Single(f => f != source);
                    foreach (var fighter in fighters)
                        fighter.ResetCombat();
                    var move = source.heavyCombatMoves.Single(m => m.sourcePair?.unarmedIndex == spec.index);
                    if (!move.grounding)
                        throw new InvalidOperationException("Missing saved grounding: " + move.moveName);
                    source.Animator.transform.position = Vector3.zero;
                    target.Animator.transform.position = Vector3.right * (direction * move.attackRange * .99f);
                    if (!source.ExecuteAttack(move, target))
                        throw new InvalidOperationException("Could not begin saved action " + move.moveName);
                    var pair = source.SourcePlayback;
                    try
                    {
                        var actors = new[] { source, target };
                        var minima = new[] { float.PositiveInfinity, float.PositiveInfinity };
                        var times = new float[2];
                        int samples = Mathf.CeilToInt(pair.Duration * 361);
                        for (int frame = 0; frame <= samples; frame++)
                        {
                            float seconds = pair.Duration * frame / samples;
                            pair.EvaluateAt(seconds);
                            for (int role = 0; role < 2; role++)
                            {
                                float clearance = BattlePresentationContactSetup.MeasureGroundClearance(actors[role]);
                                if (!float.IsFinite(clearance))
                                    throw new InvalidOperationException("Nonfinite floor clearance.");
                                if (clearance < minima[role])
                                {
                                    minima[role] = clearance;
                                    times[role] = seconds;
                                }
                            }
                        }
                        for (int role = 0; role < 2; role++)
                        {
                            worst = Mathf.Min(worst, minima[role]);
                            report.AppendLine(FormattableString.Invariant(
                                $"{source.name},{move.moveName},{direction},{role},{minima[role]:R},{times[role]:R},{samples + 1}"));
                        }
                    }
                    finally
                    {
                        pair.Cancel();
                    }
                }
                if (worst < -.025f)
                    throw new InvalidOperationException("Grounding interpolation penetrates more than 0.025 m: " + worst);
                File.WriteAllText(CombatExpansionInventory.Output + "/ThrowGroundingValidation.txt",
                    "PASS: 36 saved paired cases, both avatars and directions. " +
                    FormattableString.Invariant($"Both skins sampled at 361 Hz; minimum clearance {worst:R} m.\n") +
                    "This verifies sampled floor clearance only; grips, source quality and recovery need separate review.\n");
            }
            finally
            {
                File.WriteAllText(CombatExpansionInventory.Output + "/ThrowGroundingValidation.csv", report.ToString());
                EditorSceneManager.ClosePreviewScene(scene);
                property.SetValue(null, positioning);
            }
        }
    }
}
