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
        const string AssassinSlashReview = "GeneratedAssets/AssassinSlashReview";

        static void ConfigureAssassinSlashStyle(BattleWeaponTrails.Style style)
        {
            style.color = new Color(.82f, .44f, 1, .92f);
            style.lifetime = .16f; style.bladeStart = .05f;
            style.thrustBladeStart = .12f; style.followThroughSeconds = .14f;
            style.thrustWidth = .08f;
            const string path = WeaponVfxAssets + "/AssassinRibbon.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(VfxFolder + "/Shaders/AssassinRibbon.shader");
                if (!shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader)) throw new Exception("Invalid Assassin ribbon shader.");
                material = new Material(shader); AssetDatabase.CreateAsset(material, path);
            }
            style.material = material;
        }

        [MenuItem("Tools/Battle/VFX/Improve Assassin combo slashes")]
        public static void InstallAssassinSlashVfx()
        {
            var settings = new BattleWeaponTrails.Style { weapon = TrumpWeaponManager.WeaponType.Assassin };
            ConfigureAssassinSlashStyle(settings);
            string before = File.ReadAllText(SfxScene);
            var component = Regex.Match(before, @"(?s)(  m_EditorClassIdentifier: Assembly-CSharp::BattleWeaponTrails\r?\n.*?  styles:\r?\n)(.*?)(  staticBlades:)");
            if (!component.Success) throw new Exception("Saved weapon trail component missing.");
            var old = Regex.Match(component.Groups[2].Value, @"(?m)^  - weapon: 6\r?\n(?:    .*\r?\n)+");
            if (!old.Success) throw new Exception("Saved Assassin trail style missing.");
            string nl = before.Contains("\r\n") ? "\r\n" : "\n";
            string updated = string.Join(nl, new[] {
                "  - weapon: 6", "    color: {r: 0.82, g: 0.44, b: 1, a: 0.92}",
                "    lifetime: 0.16", "    bladeStart: 0.05", "    thrustBladeStart: 0.12", "    followThroughSeconds: 0.14",
                "    thrustWidth: 0.08",
                "    material: {fileID: 2100000, guid: " + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(settings.material)) + ", type: 2}", "" });
            int start = component.Groups[2].Index + old.Index;
            // Update only this serialized style. Do not save the user's other
            // unsaved scene edits just to persist the VFX configuration.
            string saved = before.Substring(0, start) + updated + before.Substring(start + old.Length);
            if (saved != before) File.WriteAllText(SfxScene, saved, new UTF8Encoding(false));
            var live = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(SfxScene);
            if (live.IsValid() && live.isLoaded)
            foreach (var trails in live.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BattleWeaponTrails>(true)))
            {
                Undo.RecordObject(trails, "Improve Assassin slashes");
                ConfigureAssassinSlashStyle(trails.styles.Single(s => s.weapon == TrumpWeaponManager.WeaponType.Assassin));
                EditorUtility.SetDirty(trails);
            }
            File.WriteAllText(AssassinSlashReview + "/Installation.txt",
                "Assassin: full blade/thrust ribbons, 160 ms fade, up to 140 ms follow-through bounded by the next swing, 240 Hz cached sweep, soft body-overlap pass. No new textures. Only Assassin scene style changed; contact/audio/damage bank preserved.\n");
        }

        public static void CaptureAssassinSlashesBefore() => CaptureAssassinSlashes("Before");
        public static void CaptureAssassinSlashesAfter() => CaptureAssassinSlashes("After");

        // Render every stroke using the game camera, with impact particles hidden so
        // a contact flash cannot disguise a missing or prematurely stopped slash.
        static void CaptureAssassinSlashes(string stage)
        {
            Directory.CreateDirectory(AssassinSlashReview + "/" + stage);
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var rt = RenderTexture.GetTemporary(1280, 720, 24);
            var stamp = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            BattleLightingRig lighting = null;
            var report = new StringBuilder("fighter,stroke,phase,time,visiblePixels,leadingEdgeGap\n");
            var scratch = new Mesh();
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var vfx = game.battleVfx; var trails = vfx.weaponTrails;
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var f in fighters) f.Initialize();
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single(c => c.CompareTag("MainCamera"));
                camera.scene = scene; camera.targetTexture = rt;
                foreach (var canvas in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)))
                { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
                lighting = game.GetComponent<BattleLightingRig>(); lighting.InitializeRig();
                var director = camera.GetComponent<FrankCinematicCamera>();
                foreach (var source in fighters)
                {
                    var move = source.heavyCombatMoves.Single(m => m.weapon == TrumpWeaponManager.WeaponType.Assassin);
                    var hits = vfx.timeline.FindMove(move).cues.Where(IsContactCue).ToArray();
                    if (hits.Length != 8) throw new Exception("Assassin combo must retain all eight contacts.");
                    for (int stroke = 0; stroke < hits.Length; stroke++)
                    foreach (bool afterContact in new[] { false, true })
                    {
                        foreach (var f in fighters) f.ResetCombat(); vfx.ResetForMatch();
                        var target = fighters.Single(f => f != source); bool reverse = source == fighters[1];
                        source.Animator.transform.position = new Vector3(reverse ? .5f : -.5f, 0, 0);
                        target.Animator.transform.position = source.Animator.transform.position + Vector3.right * (reverse ? -move.attackRange : move.attackRange);
                        var roots = new List<GameObject>();
                        Action<string, GameObject> observe = (id, root) => { if (!roots.Contains(root)) roots.Add(root); };
                        vfx.EffectPlayed += observe;
                        try
                        {
                            source.ExecuteAttack(move, target); var pair = source.SourcePlayback;
                            // The second stroke now follows the actual release of
                            // the held knife. Its motion starts less than 25 ms
                            // before contact, so inspect the moving blade rather
                            // than demanding a slash on the stationary hold.
                            float beforeContact = stroke == 1 && hits[stroke].group == "light_hit" ? 1f / 240 : .025f;
                            float time = hits[stroke].seconds + (afterContact ? .055f : -beforeContact);
                            pair.AdvanceTo(time);
                            foreach (var root in roots.Where(r => !trails.Owns(r))) root.SetActive(false);
                            var active = roots.Where(r => trails.Owns(r) && r.activeSelf).ToArray();
                            if (stroke == 1)
                            {
                                var geometry = new StringBuilder();
                                foreach (var filter in active.SelectMany(r => r.GetComponentsInChildren<MeshFilter>()))
                                {
                                    var mesh = filter.sharedMesh; var points = mesh.vertices; var indices = mesh.triangles; float area = 0;
                                    for (int i = 0; i < indices.Length; i += 3)
                                        area += Vector3.Cross(points[indices[i + 1]] - points[indices[i]], points[indices[i + 2]] - points[indices[i]]).magnitude * .5f;
                                    var renderer = filter.GetComponent<MeshRenderer>();
                                    geometry.AppendLine($"{filter.transform.parent.name}: vertices={points.Length}, area={area:F8}, bounds={mesh.bounds}, material={renderer.sharedMaterial.name}, passes={renderer.sharedMaterial.passCount}");
                                }
                                File.WriteAllText($"{AssassinSlashReview}/{stage}/{source.name}_Thrust_{afterContact}.txt", geometry.ToString());
                            }
                            float gap = 0;
                            float latestTip = -1;
                            foreach (var filter in active.SelectMany(r => r.GetComponentsInChildren<MeshFilter>()))
                            {
                                var vertices = filter.sharedMesh.vertices;
                                var uv = filter.sharedMesh.uv;
                                if (vertices.Length > 0 && uv[uv.Length - 1].y > latestTip)
                                {
                                    latestTip = uv[uv.Length - 1].y;
                                    gap = NearestWeaponVertex(pair, filter.transform.TransformPoint(vertices[vertices.Length - 1]), false, scratch);
                                }
                            }
                            lighting.RefreshLighting(.22f); director.ResetView(); director.Apply(0, true); Canvas.ForceUpdateCanvases();
                            CaptureStrongPose(camera, fighters, pair, stamp, rt);
                            var withSlash = stamp.GetPixels32();
                            string phase = afterContact ? "FollowThrough" : "Swing";
                            File.WriteAllBytes($"{AssassinSlashReview}/{stage}/{source.name}_{stroke + 1:00}_{phase}.png", stamp.EncodeToPNG());
                            foreach (var root in active) root.SetActive(false);
                            CaptureStrongPose(camera, fighters, pair, stamp, rt);
                            var withoutSlash = stamp.GetPixels32();
                            int visible = 0;
                            for (int p = 0; p < withSlash.Length; p++)
                                if (Math.Abs(withSlash[p].r - withoutSlash[p].r) > 8 || Math.Abs(withSlash[p].g - withoutSlash[p].g) > 8 || Math.Abs(withSlash[p].b - withoutSlash[p].b) > 8) visible++;
                            report.AppendLine(FormattableString.Invariant($"{source.name},{stroke + 1},{phase},{time:F6},{visible},{gap:F6}"));
                            pair.Cancel();
                        }
                        finally { vfx.EffectPlayed -= observe; }
                    }
                }
            }
            finally
            {
                File.WriteAllText(AssassinSlashReview + "/" + stage + ".csv", report.ToString());
                if (lighting) lighting.ShutdownRig(); RenderTexture.active = previous;
                Object.DestroyImmediate(scratch); Object.DestroyImmediate(stamp); RenderTexture.ReleaseTemporary(rt);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        public static void ValidateAssassinSlashes()
        {
            string bankBeforeValidation = File.ReadAllText("Assets/Audio/Battle/BattleSfxBank.asset");
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var report = new StringBuilder(); var scratch = new Mesh();
            int cases = 0, comparisons = 0;
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var vfx = game.battleVfx; var trails = vfx.weaponTrails;
                var style = trails.styles.Single(s => s.weapon == TrumpWeaponManager.WeaponType.Assassin);
                if (!style.material || !style.material.shader.isSupported || ShaderUtil.ShaderHasError(style.material.shader) ||
                    style.material.passCount != 2 || style.material.GetTexturePropertyNames().Length != 0 || style.thrustWidth <= 0)
                    throw new Exception("Assassin material, straight-thrust width or texture-free shader invalid.");
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var f in fighters) f.Initialize();
                foreach (var source in fighters)
                foreach (bool mirrored in new[] { false, true })
                foreach (bool lethal in new[] { false, true })
                foreach (int fps in new[] { 15, 60 })
                {
                    foreach (var f in fighters) f.ResetCombat(); vfx.ResetForMatch();
                    var move = source.heavyCombatMoves.Single(m => m.weapon == TrumpWeaponManager.WeaponType.Assassin);
                    var target = fighters.Single(f => f != source);
                    source.Animator.transform.position = Vector3.zero;
                    target.Animator.transform.position = Vector3.right * move.attackRange * (mirrored ? -1 : 1);
                    var seen = new Dictionary<float, GameObject>(); int contacts = 0;
                    Action<string, GameObject> observe = (id, root) =>
                    {
                        if (trails.Owns(root))
                        {
                            float time = source.SourcePlayback.SampleTime;
                            if (seen.ContainsKey(time)) throw new Exception("Repeated Assassin slash.");
                            seen.Add(time, root);
                        }
                        if (id == "light_hit" || id == "heavy_hit") contacts++;
                    };
                    vfx.EffectPlayed += observe;
                    try
                    {
                        source.ExecuteAttack(move, target, lethal); var pair = source.SourcePlayback;
                        for (float time = 0; time < pair.Duration; time += 1f / fps)
                        {
                            pair.AdvanceTo(time);
                            int poses = trails.SampledPoseCount, effects = vfx.PlayedEffectCount;
                            foreach (var root in seen.Values.Distinct().Where(r => r && r.activeSelf))
                            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
                            {
                                var mesh = filter.sharedMesh; var vertices = mesh.vertices; var uv = mesh.uv;
                                if (vertices.Length == 0) continue;
                                float tipTime = uv[uv.Length - 1].y;
                                if (tipTime > time + .00001f) throw new Exception("Ribbon displayed a future blade position.");
                                pair.EvaluateAt(tipTime);
                                float gap = NearestWeaponVertex(pair, filter.transform.TransformPoint(vertices[vertices.Length - 1]), false, scratch);
                                if (gap > .025f) throw new Exception("Assassin slash detached from the actual blade.");
                                comparisons++; pair.EvaluateAt(time);
                            }
                            vfx.AdvanceSequence(pair, time); vfx.AdvanceSequence(pair, Mathf.Max(0, time - .1f)); trails.Advance(time);
                            if (trails.SampledPoseCount != poses || vfx.PlayedEffectCount != effects || Mathf.Abs(pair.SampleTime - time) > .00001f)
                                throw new Exception("Pause/rewind rebuilt VFX or changed the combat clock.");
                        }
                        pair.AdvanceTo(pair.Duration);
                        if (seen.Count != 8 || contacts != 8) throw new Exception("An Assassin swing/contact was lost.");
                        trails.Advance(pair.Duration + .3f);
                        if (trails.ActiveTrailCount != 0) throw new Exception("Assassin slash survived recovery.");
                        pair.Cancel();
                        if (trails.ActiveTrailCount != 0 || vfx.ActiveEffectCount != 0 || trails.PooledTrailCount > trails.maxTrails)
                            throw new Exception("Assassin cancellation/pool regression.");
                        report.AppendLine($"PASS {source.name}: eight sweeps/eight blade contacts, mirror={mirrored}, lethal={lethal}, fps={fps}"); cases++;
                    }
                    finally { vfx.EffectPlayed -= observe; source.SourcePlayback.Cancel(); }
                }
                var rows = File.ReadAllLines(AssassinSlashReview + "/After.csv").Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Split(',')).ToArray();
                if (rows.Length != 32 || rows.Any(r => int.Parse(r[4]) < 40)) throw new Exception("A reviewed Assassin stroke is still invisible in the game camera.");
                if (File.ReadAllText("Assets/Audio/Battle/BattleSfxBank.asset") != bankBeforeValidation)
                    throw new Exception("Slash validation modified damage, contact or audio timing.");
                report.AppendLine($"ALL PASS: {cases} full combos, {comparisons} blade endpoint checks, all 32 rendered swing/follow-through views visible; cached sampling, same clock, deduplication, recovery and cancellation; damage/audio/contact bank unchanged during validation.");
            }
            catch (Exception error) { report.AppendLine("FAIL " + error); throw; }
            finally { Object.DestroyImmediate(scratch); EditorSceneManager.ClosePreviewScene(scene); File.WriteAllText(AssassinSlashReview + "/Validation.txt", report.ToString()); }
        }
    }
}
