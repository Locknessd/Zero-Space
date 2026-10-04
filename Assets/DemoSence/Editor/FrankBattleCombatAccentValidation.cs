using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void ValidateBattleCombatAccents()
        {
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene); var report = new StringBuilder();
            BattleLightingRig lighting = null;
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>()).Single(c => c.CompareTag("MainCamera"));
                var vfx = game.battleVfx; var shake = camera.GetComponent<BattleCameraShake>();
                var feedback = game.GetComponent<BattleImpactFeedback>();
                lighting = game.GetComponent<BattleLightingRig>(); lighting.InitializeRig(); shake.Bind(); feedback.Bind();
                if (vfx.impactVariants.Length != 7 || vfx.bladeSlashVariants.Select(v => v.prefab).Distinct().Count() != 7 || vfx.skillVariants.Length != 2)
                    throw new Exception("Missing weapon/skill variation.");
                foreach (var fighter in new[] { game.leftCombat, game.rightCombat })
                {
                    fighter.Initialize();
                    foreach (var skill in new[] { BattleSkill.Archer, BattleSkill.WhiteMage })
                    {
                        var move = fighter.heavyCombatMoves.Single(m => m.skill == skill);
                        var cues = vfx.timeline.FindMove(move).cues;
                        if (cues.Count(c => c.group == "heavy_hit") != (skill == BattleSkill.Archer ? 3 : 1) ||
                            cues.Last().seconds > Mathf.Max(move.sourcePair.attack.length, move.sourcePair.reactionDelay + move.sourcePair.reaction.length))
                            throw new Exception("Invalid skill damage/animation timeline.");
                    }
                }
                float originalKey = lighting.key.intensity, originalFill = lighting.fill.intensity;
                Vector3 cameraPosition = camera.transform.position; Quaternion cameraRotation = camera.transform.rotation;
                var heavy = game.leftCombat.heavyCombatMoves.Single(m => m.moveName == "Heavy_6");
                game.leftCombat.transform.position = Vector3.zero; game.rightCombat.transform.position = Vector3.right * heavy.attackRange;
                int initialShake = shake.ShakeCount;
                if (!game.leftCombat.ExecuteAttack(heavy, game.rightCombat, true)) throw new Exception("Accent attack rejected.");
                var pair = game.leftCombat.SourcePlayback; var profile = vfx.timeline.FindMove(heavy);
                if (shake.ShakeCount != initialShake) throw new Exception("Shake began during windup.");
                lighting.RefreshLighting(.2f);
                if (Mathf.Abs(lighting.key.intensity - originalKey * .9f) > .001f || Mathf.Abs(lighting.fill.intensity - originalFill * .82f) > .001f)
                    throw new Exception("Combat dimming did not reach its authored level.");
                int finishers = 0; Action<string, GameObject> observe = (id, root) => { if (vfx.IsFinishingContact) finishers++; };
                vfx.EffectPlayed += observe;
                try
                {
                    foreach (var cue in profile.cues)
                    {
                        pair.EvaluateAt(cue.seconds); vfx.AdvanceSequence(pair, cue.seconds);
                        int played = vfx.PlayedEffectCount;
                        vfx.BeginSequence(pair, game.leftCombat, game.rightCombat, heavy, true);
                        vfx.AdvanceSequence(pair, cue.seconds);
                        if (played != vfx.PlayedEffectCount) throw new Exception("Repeated Begin replayed a contact.");
                    }
                }
                finally { vfx.EffectPlayed -= observe; }
                int expectedShake = profile.cues.Count(c => c.group == "heavy_hit" || c.group == "body_fall");
                if (shake.ShakeCount - initialShake != expectedShake || finishers != 1) throw new Exception("Shake/finishing contact count mismatch.");
                shake.ApplyShake(); Vector3 first = camera.transform.position; shake.ApplyShake();
                if (Vector3.Distance(first, camera.transform.position) > .00001f || Vector3.Distance(first, cameraPosition) > .055f)
                    throw new Exception("Camera shake accumulated or exceeded its limit.");
                shake.AdvanceShake(.3f); shake.ApplyShake();
                if (Vector3.Distance(cameraPosition, camera.transform.position) > .00001f || Quaternion.Angle(cameraRotation, camera.transform.rotation) > .001f)
                    throw new Exception("Shake left camera drift.");
                pair.Cancel(); lighting.RefreshLighting(.4f);
                if (Mathf.Abs(lighting.key.intensity - originalKey) > .001f || Mathf.Abs(lighting.fill.intensity - originalFill) > .001f || vfx.ActiveEffectCount != 0)
                    throw new Exception("Cancellation left lighting or effects active.");
                report.AppendLine("PASS seven unique weapon slashes, fourteen weapon impacts and two skills on both fighters.");
                report.AppendLine("PASS one finisher contact; repeated Begin/Advance deduplicated; shake only at heavy/floor contacts; bounded displacement; no camera drift.");
                report.AppendLine("PASS combat light levels and restoration after cancellation.");
                File.WriteAllText(AccentReview + "/AccentValidation.txt", report.ToString());
            }
            catch (Exception error) { File.WriteAllText(AccentReview + "/AccentValidation.txt", report + "FAIL " + error); throw; }
            finally { if (lighting) lighting.ShutdownRig(); EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
