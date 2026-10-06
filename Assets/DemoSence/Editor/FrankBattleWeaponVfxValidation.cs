using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        [MenuItem("Tools/Battle/VFX/Validate aligned weapon trails")]
        public static void ValidateAlignedWeaponVfx()
        {
            Directory.CreateDirectory(WeaponVfxReview);
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var report = new StringBuilder(); var scratch = new Mesh();
            int cases = 0, sweeps = 0, points = 0; float worst = 0;
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var vfx = game.battleVfx; var trails = vfx.weaponTrails;
                if (!trails || !trails.material || trails.styles.Length != 7 || !vfx.shieldImpact ||
                    trails.styles.Select(s => s.color).Distinct().Count() != 7 || ShaderUtil.ShaderHasError(trails.material.shader))
                    throw new Exception("Saved trail styles/material/contact bindings missing.");
                foreach (var weapon in new[] { TrumpWeaponManager.WeaponType.Spear, TrumpWeaponManager.WeaponType.DualDaggers, TrumpWeaponManager.WeaponType.Assassin })
                    if (!vfx.impactVariants.Single(v => v.weapon == weapon).stab) throw new Exception("Missing piercing contact " + weapon);
                var fighters = new[] { game.leftCombat, game.rightCombat }; foreach (var f in fighters) f.Initialize();
                foreach (var attacker in fighters)
                foreach (var move in attacker.lightCombatMoves.Concat(attacker.heavyCombatMoves))
                foreach (bool lethal in new[] { false, true })
                {
                    foreach (var f in fighters) f.ResetCombat(); vfx.ResetForMatch();
                    var receiver = fighters.Single(f => f != attacker);
                    attacker.transform.position = Vector3.zero;
                    receiver.transform.position = (attacker == game.leftCombat ? Vector3.right : Vector3.left) * move.attackRange;
                    var roots = new HashSet<GameObject>(); int contacts = 0, trailsSeen = 0;
                    Action<BattleVfxPlayer.Impact> onContact = impact => contacts++;
                    Action<string, GameObject> onEffect = (id, root) =>
                    {
                        if (trails.Owns(root)) { roots.Add(root); trailsSeen++; }
                        else if (id == "blade_slash" || id == "thrust_swing")
                            throw new Exception("Weapon sweep fell back to camera-facing prefab: " + move.moveName + "/" + id);
                        if (id == "light_hit" || id == "heavy_hit")
                        {
                            var timing = vfx.timeline.FindMove(move).cues.First(c =>
                                Mathf.Abs(c.seconds-attacker.SourcePlayback.SampleTime) < .00001f &&
                                (c.group == "light_hit" || c.group == "heavy_hit" || c.group == "stab_hit"));
                            GameObject expected = null;
                            if (timing.contactSource == "Shield") expected = vfx.shieldImpact;
                            else if (timing.group == "stab_hit") expected = vfx.impactVariants.Single(v => v.weapon == move.weapon).stab;
                            else if (!string.IsNullOrEmpty(timing.contactSource) && timing.contactSource != "Weapon")
                                expected = id == "heavy_hit" ? vfx.heavyHit : vfx.lightHit;
                            if (expected && root.name != expected.name + "(Clone)") throw new Exception("Wrong contact style for " + timing.contactSource);
                        }
                    };
                    vfx.ContactOccurred += onContact; vfx.EffectPlayed += onEffect;
                    try
                    {
                        if (!attacker.ExecuteAttack(move, receiver, lethal)) throw new Exception("Attack rejected.");
                        var pair = attacker.SourcePlayback; var profile = vfx.timeline.FindMove(move);
                        var times = new SortedSet<float>(profile.cues.Select(c => c.seconds));
                        foreach (var cue in profile.cues.Where(c => c.group.EndsWith("swing", StringComparison.Ordinal)))
                        { times.Add(cue.seconds + .035f); times.Add(cue.seconds + .07f); }
                        foreach (float time in times.Where(t => t <= pair.Duration))
                        {
                            pair.EvaluateAt(time); vfx.AdvanceSequence(pair, time);
                            if (Mathf.Abs(pair.SampleTime - time) > .00001f) throw new Exception("Trail sampling changed the shared animation clock.");
                            foreach (var root in roots.Where(r => r && r.activeSelf))
                            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
                            {
                                var mesh = filter.sharedMesh; if (mesh.vertexCount == 0) continue;
                                var vertices = mesh.vertices; var uv = mesh.uv;
                                Vector3 edge = filter.transform.TransformPoint(vertices[vertices.Length - 1]);
                                float sampleTime = uv[uv.Length - 1].y;
                                pair.EvaluateAt(sampleTime);
                                float distance = NearestWeaponVertex(pair, edge, root.name == "Shield sweep", scratch);
                                if (distance > .025f) throw new Exception($"Detached ribbon: {attacker.name}/{move.moveName} t={sampleTime:F4} error={distance:F4}m.");
                                worst = Mathf.Max(worst,distance); points++;
                                pair.EvaluateAt(time);
                            }
                            int count = vfx.PlayedEffectCount, poses = trails.SampledPoseCount;
                            vfx.AdvanceSequence(pair,time); vfx.AdvanceSequence(pair,Mathf.Max(0,time-.1f));
                            if (vfx.PlayedEffectCount != count || trails.SampledPoseCount != poses) throw new Exception("Repeated/reversed clock rebuilt or replayed trails.");
                            trails.Advance(time);
                            if (trails.SampledPoseCount != poses) throw new Exception("Frame update performed CPU skinning.");
                        }
                        int expectedContacts = profile.cues.Count(c => c.group == "light_hit" || c.group == "heavy_hit" || c.group == "stab_hit" || c.group == "body_fall" || c.group == "knockout_fall");
                        if (contacts != expectedContacts) throw new Exception("Contact count changed.");
                        if (move.weapon != TrumpWeaponManager.WeaponType.None && trailsSeen == 0) throw new Exception("No aligned weapon sweep.");
                        trails.Advance(pair.Duration + .3f);
                        if (trails.ActiveTrailCount != 0) throw new Exception("Ribbon remained after recovery.");
                        pair.Cancel();
                        if (vfx.ActiveEffectCount != 0 || trails.ActiveTrailCount != 0) throw new Exception("Cancel left VFX active.");
                        sweeps += trailsSeen; cases++;
                        report.AppendLine($"PASS {attacker.name}/{move.moveName} lethal={lethal}: {trailsSeen} aligned sweeps, {contacts} contacts.");
                    }
                    finally { vfx.ContactOccurred -= onContact; vfx.EffectPlayed -= onEffect; }
                }
                // A complete combo can be crossed by one presentation update.
                foreach (var f in fighters) f.ResetCombat(); vfx.ResetForMatch();
                var combo = game.leftCombat.heavyCombatMoves.Single(m => m.weapon == TrumpWeaponManager.WeaponType.DualDaggers);
                game.leftCombat.transform.position = Vector3.zero; game.rightCombat.transform.position = Vector3.right * combo.attackRange;
                if (!game.leftCombat.ExecuteAttack(combo, game.rightCombat)) throw new Exception("Frame-skip combo rejected.");
                var skipped = game.leftCombat.SourcePlayback;
                skipped.EvaluateAt(skipped.Duration); vfx.AdvanceSequence(skipped,skipped.Duration);
                if (trails.ActiveTrailCount != 0 || Mathf.Abs(skipped.SampleTime-skipped.Duration) > .00001f) throw new Exception("Skipped frame left an old sweep or changed pose.");
                skipped.Cancel();
                if (trails.PooledTrailCount > trails.maxTrails) throw new Exception("Trail pool exceeded its limit.");
                report.AppendLine($"PASS {cases} cases, {sweeps} sweeps; {points} blade endpoint comparisons; worst error {worst:F5}m.");
                report.AppendLine($"PASS skipped combo, deduplication, animation-clock restoration, cancellation and recovery; pool={trails.PooledTrailCount}/{trails.maxTrails}.");
                File.WriteAllText(WeaponVfxReview + "/Validation.txt", report.ToString());
            }
            catch (Exception error) { File.WriteAllText(WeaponVfxReview + "/Validation.txt", report + "FAIL " + error); throw; }
            finally { Object.DestroyImmediate(scratch); EditorSceneManager.ClosePreviewScene(scene); }
        }

        static float NearestWeaponVertex(FrankBattlePairPlayback pair, Vector3 point, bool shield, Mesh scratch)
        {
            float nearest = float.PositiveInfinity;
            foreach (var renderer in pair.AttackerActor.Pose.weaponRenderers)
            {
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    renderer.name.IndexOf("case", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (renderer.name.IndexOf("shield", StringComparison.OrdinalIgnoreCase) >= 0) != shield) continue;
                Vector3[] vertices;
                if (renderer is SkinnedMeshRenderer skin) { skin.BakeMesh(scratch,true); vertices = scratch.vertices; }
                else { var filter = renderer.GetComponent<MeshFilter>(); if (!filter || !filter.sharedMesh) continue; vertices = ReadWeaponVertices(filter.sharedMesh); }
                var matrix = renderer.transform.localToWorldMatrix;
                foreach (var vertex in vertices) nearest = Mathf.Min(nearest,Vector3.Distance(point,matrix.MultiplyPoint3x4(vertex)));
            }
            return nearest;
        }

        public static void AuditWeaponVfxDependencies()
        {
            string before = File.ReadAllText("BuildOptimization/Originals/WeaponVfxAlignment/BattleScene.unity");
            var oldPaths = Regex.Matches(before,@"guid: ([a-f0-9]{32})").Cast<Match>()
                .Select(m => AssetDatabase.GUIDToAssetPath(m.Groups[1].Value)).Where(p => !string.IsNullOrEmpty(p)).Distinct().ToArray();
            var oldDependencies = new HashSet<string>(AssetDatabase.GetDependencies(oldPaths,true));
            var added = AssetDatabase.GetDependencies(SfxScene,true).Where(p => !oldDependencies.Contains(p) && p != SfxScene).OrderBy(p => p).ToArray();
            var textures = added.Where(p => AssetImporter.GetAtPath(p) is TextureImporter).ToArray();
            var report = new StringBuilder($"New texture dependencies: {textures.Length}\n");
            foreach (string path in added) report.AppendLine(path);
            File.WriteAllText(WeaponVfxReview + "/Dependencies.txt",report.ToString());
            if (textures.Length != 0) throw new Exception("Unexpected extra texture dependency.");
        }

        public static void CancelPendingWeaponVfxPlayCheck() => CombatFeelPlayValidation.CancelPending();
    }
}
