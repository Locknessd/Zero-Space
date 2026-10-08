using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    /// <summary>Independent current-bone geometry audit of the grounded GreatSword source camera.</summary>
    public static partial class CombatExpansionGreatSwordCameraCheck
    {
        const string Output = "GeneratedAssets/CombatExpansion/GreatSwordCameraValidation.txt";
        const float Margin = .049f;

        public static void Validate()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            var report = new StringBuilder();
            report.AppendLine("Grounded GreatSword camera; four pairs, both attacking avatars, both directions.");
            report.AppendLine("480x320 aspect 1.5; immediate BattleScene camera, .12 s onward at 60 Hz.");
            report.AppendLine("Includes every study-sheet time and endpoint; current bone/bindpose body vertices.");
            report.AppendLine("Includes static body/face meshes and injected sword using current transform vertices.");
            report.AppendLine("Body viewport margin >= .049; near plane; no oversized framing approval.");
            report.AppendLine("Bake(false)+TR checked independently against bone weights; raw bake bounds audited.");
            report.AppendLine("Raw versus baked-vertex versus recalculated bounds; " +
                "runtime limiting renderer attributed.");
            report.AppendLine("Distance oracle: renderer-local AABBs reconstructed from current bone vertices,");
            report.AppendLine("then rotated to world, matching the runtime envelope; 25 percent allowance unchanged.");
            report.AppendLine("Tight world AABBs are diagnostic only: they omit rotation-induced envelope corners.");
            report.AppendLine("Isolated editor sampling only; live transitions and rendered pixels are not certified.");
            var positioning = CombatPositioningController.Instance;
            var property = typeof(CombatPositioningController).GetProperty("Instance");
            var scene = EditorSceneManager.OpenPreviewScene(CombatExpansionInventory.Battle);
            var texture = RenderTexture.GetTemporary(480, 320, 24);
            int cases = 0;
            int failures = 0;
            string error = null;
            try
            {
                property.SetValue(null, null);
                var roots = scene.GetRootGameObjects();
                var game = roots.SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var camera = roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true))
                    .First(c => c.CompareTag("MainCamera"));
                var framing = camera.GetComponent<FrankCinematicCamera>();
                if (!framing || framing.battle != game || !framing.library || !framing.cinematic)
                    throw new InvalidOperationException("BattleScene cinematic camera is not configured.");
                camera.scene = scene;
                camera.targetTexture = texture;
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var fighter in fighters)
                {
                    fighter.battleSfx = null;
                    fighter.battleVfx = null;
                    fighter.hitEffect = null;
                    fighter.Initialize();
                }
                foreach (var source in fighters)
                for (int index = 0; index < 4; index++)
                foreach (int direction in new[] { 1, -1 })
                {
                    foreach (var fighter in fighters)
                        fighter.ResetCombat();
                    var target = fighters.Single(f => f != source);
                    var move = CombatExpansionGreatSwordStudy.MakeMove(source, target, index);
                    if (!move.grounding)
                        throw new InvalidOperationException("Missing grounding: " + move.moveName);
                    source.Animator.transform.position = Vector3.left * direction * move.attackRange * .5f;
                    target.Animator.transform.position = Vector3.right * direction * move.attackRange * .5f;
                    if (!source.ExecuteAttack(move, target))
                        throw new InvalidOperationException("Could not begin " + move.moveName);
                    var pair = source.SourcePlayback;
                    try
                    {
                        framing.ResetView();
                        failures += Sample(source, target, pair, camera, framing, direction, report);
                        cases++;
                    }
                    finally
                    {
                        pair.Cancel();
                    }
                }
                camera.targetTexture = null;
                if (cases != 16 || failures != 0)
                    throw new InvalidOperationException($"Camera audit: {cases}/16 cases, {failures} failing samples.");
            }
            catch (Exception exception)
            {
                error = exception.ToString();
                throw;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                property.SetValue(null, positioning);
                RenderTexture.ReleaseTemporary(texture);
                report.Insert(0, error == null ? "PASS\n" : "FAIL\n");
                report.AppendLine($"Completed cases: {cases}/16; failing samples: {failures}.");
                if (error != null)
                    report.AppendLine(error);
                Directory.CreateDirectory(Path.GetDirectoryName(Output));
                File.WriteAllText(Output, report.ToString());
            }
        }
    }
}
