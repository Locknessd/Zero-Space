using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        const string AllHeavyReview = "GeneratedAssets/AllHeavyContactReview";

        [Serializable]
        sealed class HeavyCueSnapshot { public BattleSfxBank.Cue[] cues; }

        sealed class ReviewedHeavyStroke
        {
            public string fighter, move, stroke, group, swing, source, renderer;
            public int oldIndex;
            public float time;
            public HumanBodyBones bone;
            public Vector3 offset;
        }

        static ReviewedHeavyStroke[] ReadReviewedHeavyStrokes() => File.ReadAllLines(AllHeavyReview + "/ApprovedSelection.csv")
            .Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => {
                var r = l.Split(',');
                return new ReviewedHeavyStroke {
                    fighter = r[0], move = r[1], stroke = r[2], oldIndex = int.Parse(r[3]), time = ParseContactFloat(r[4]),
                    group = r[5], swing = r[6], source = r[7], renderer = r[8], bone = (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), r[11]),
                    offset = new Vector3(ParseContactFloat(r[12]), ParseContactFloat(r[13]), ParseContactFloat(r[14]))
                };
            }).ToArray();

        static int ReviewedHeavyHitCount(string move)
        {
            switch (move)
            {
                case "Heavy_5": return 7;
                case "Heavy_6": return 4;
                case "Heavy_7": return 8;
                case "Heavy_2": return 4;
                case "Heavy_8": return 9;
                case "Heavy_Katana": return 2;
                case "Heavy_Assassin": return 8;
                default: throw new Exception("Heavy animation has not been reviewed: " + move);
            }
        }

        static void RequireAllHeavyCoverage(BattleSfxBank.Move profile, string fighter, string move, ReviewedHeavyStroke[] selected)
        {
            var strokes = selected.Where(s => s.fighter == fighter && s.move == move).ToArray();
            var hits = profile.cues.Where(IsContactCue).ToArray();
            if (strokes.Length != ReviewedHeavyHitCount(move) || hits.Length != strokes.Length ||
                strokes.Any(s => hits.Count(h => Mathf.Abs(h.seconds - s.time) < .00001f && h.group == s.group && h.contactSource == s.source) != 1))
                throw new Exception("Missing or duplicate reviewed stroke: " + fighter + "/" + move);
        }

        static string HeavyOriginalCuePath(string fighter, string move) => AllHeavyReview + "/" + fighter + "_" + move + "_OriginalCues.json";

        [MenuItem("Tools/Battle/Apply all reviewed Heavy contacts")]
        public static void InstallAllReviewedHeavyContacts()
        {
            var selected = ReadReviewedHeavyStrokes();
            if (selected.Length != 84) throw new Exception("Expected 84 individually reviewed Heavy strikes.");
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            string sceneBefore = File.ReadAllText(SfxScene);
            var report = new StringBuilder("fighter,move,stroke,oldTime,newTime,source,renderer,bone,surfaceGap\n");
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var bank = game.battleVfx.timeline; var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var f in fighters) { f.battleSfx = null; f.battleVfx = null; f.hitEffect = null; }
                var pending = new Dictionary<BattleSfxBank.Move, BattleSfxBank.Cue[]>();
                foreach (var source in fighters)
                foreach (var move in source.heavyCombatMoves)
                {
                    var profile = bank.FindMove(move); string originalPath = HeavyOriginalCuePath(source.name, move.moveName);
                    // Snapshot the pre-review cues once so reapplying never duplicates
                    // a new strike or interprets a shifted array index as another hit.
                    if (!File.Exists(originalPath)) File.WriteAllText(originalPath, JsonUtility.ToJson(new HeavyCueSnapshot { cues = profile.cues }, true));
                    var original = JsonUtility.FromJson<HeavyCueSnapshot>(File.ReadAllText(originalPath)).cues;
                    var originalHits = original.Where(IsContactCue).ToArray();
                    var rows = selected.Where(s => s.fighter == source.name && s.move == move.moveName).OrderBy(s => s.time).ToArray();
                    if (!rows.Where(s => s.oldIndex >= 0).Select(s => s.oldIndex).OrderBy(i => i).SequenceEqual(Enumerable.Range(0, originalHits.Length)))
                        throw new Exception("Existing Heavy strike was lost or assigned twice.");
                    var cues = original.Select(c => JsonUtility.FromJson<BattleSfxBank.Cue>(JsonUtility.ToJson(c))).ToList();
                    var usedSwings = new HashSet<BattleSfxBank.Cue>();
                    var swings = new Dictionary<int, BattleSfxBank.Cue>();
                    for (int index = 0; index < originalHits.Length; index++)
                    {
                        var swing = original.Where(c => c.seconds < originalHits[index].seconds && c.group.EndsWith("swing", StringComparison.Ordinal) && !usedSwings.Contains(c))
                            .OrderBy(c => c.seconds).LastOrDefault();
                        if (swing == null) throw new Exception("Existing Heavy strike has no unique swing.");
                        usedSwings.Add(swing); swings.Add(index, cues[Array.IndexOf(original, swing)]);
                    }
                    var target = fighters.Single(f => f != source);
                    foreach (var f in fighters) f.ResetCombat();
                    source.Animator.transform.position = Vector3.zero; target.Animator.transform.position = Vector3.right * move.attackRange;
                    if (!source.ExecuteAttack(move, target)) throw new Exception("Heavy installation pose rejected.");
                    var pair = source.SourcePlayback;
                    using (var body = new ContactBody(target))
                    using (var probe = new ContactProbe())
                    foreach (var row in rows)
                    {
                        pair.EvaluateAt(row.time); body.Sample();
                        var renderer = pair.AttackerActor.Pose.weaponRenderers.SingleOrDefault(r => r.name == row.renderer);
                        probe.Sample(pair, source, body, true, r => r == renderer);
                        int kind = Array.IndexOf(probe.names, row.source);
                        if (kind < 0 || kind < 2 && !renderer) throw new Exception("Unknown reviewed Heavy striker.");
                        var point = target.Animator.GetBoneTransform(row.bone).TransformPoint(row.offset);
                        float gap = Vector3.Distance(probe.points[kind], body.Surface(probe.points[kind]));
                        if (gap > .013f || Vector3.Distance(point, body.Surface(point)) > .005f || Vector3.Distance(point, probe.points[kind]) > .013f)
                            throw new Exception("Reviewed Heavy contact has moved: " + source.name + "/" + move.moveName + "/" + row.stroke);
                        BattleSfxBank.Cue cue; float oldTime = -1;
                        if (row.oldIndex >= 0)
                        {
                            oldTime = originalHits[row.oldIndex].seconds;
                            cue = cues[Array.IndexOf(original, originalHits[row.oldIndex])];
                            var swing = swings[row.oldIndex];
                            swing.seconds = Mathf.Max(0, swing.seconds + row.time - oldTime); swing.group = row.swing;
                        }
                        else
                        {
                            cue = new BattleSfxBank.Cue(); cues.Add(cue);
                            cues.Add(new BattleSfxBank.Cue { seconds = Mathf.Max(0, row.time - .075f), group = row.swing });
                        }
                        cue.seconds = row.time; cue.group = row.group; cue.contactSource = row.source;
                        cue.hasContactPoint = true; cue.contactBone = row.bone; cue.contactOffset = row.offset;
                        report.AppendLine(FormattableString.Invariant($"{source.name},{move.moveName},{row.stroke},{oldTime:F6},{row.time:F6},{row.source},{row.renderer},{row.bone},{gap:F6}"));
                    }
                    pair.Cancel(); var sorted = cues.OrderBy(c => c.seconds).ToArray();
                    RequireAllHeavyCoverage(new BattleSfxBank.Move { cues = sorted }, source.name, move.moveName, selected);
                    pending.Add(profile, sorted);
                }
                if (pending.Count != 14) throw new Exception("Not all 14 Heavy animation pairs were staged.");
                Undo.RecordObject(bank, "Align every Heavy strike to its moving weapon/body contact");
                foreach (var change in pending) change.Key.cues = change.Value;
                EditorUtility.SetDirty(bank); AssetDatabase.SaveAssetIfDirty(bank);
                if (File.ReadAllText(SfxScene) != sceneBefore) throw new Exception("Heavy contact installation changed the scene.");
                File.WriteAllText(AllHeavyReview + "/Installation.csv", report.ToString());
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        public static void ValidateAllReviewedHeavyContacts()
        {
            var selected = ReadReviewedHeavyStrokes();
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var report = new StringBuilder(); int cases = 0, checkedHits = 0;
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var vfx = game.battleVfx; var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var source in fighters)
                foreach (var move in source.heavyCombatMoves)
                {
                    var profile = vfx.timeline.FindMove(move); var rows = selected.Where(s => s.fighter == source.name && s.move == move.moveName).ToArray();
                    RequireAllHeavyCoverage(profile, source.name, move.moveName, selected);
                    var missing = new BattleSfxBank.Move { cues = profile.cues.Where(c => c != profile.cues.First(IsContactCue)).ToArray() };
                    bool rejected = false;
                    try { RequireAllHeavyCoverage(missing, source.name, move.moveName, selected); } catch (Exception) { rejected = true; }
                    if (!rejected) throw new Exception("Coverage accepted a missing Heavy strike.");
                    foreach (bool mirrored in new[] { false, true })
                    foreach (bool lethal in new[] { false, true })
                    foreach (bool skipped in new[] { false, true })
                    {
                        foreach (var f in fighters) f.ResetCombat(); vfx.ResetForMatch(); game.battleSfx.ResetForMatch();
                        var target = fighters.Single(f => f != source);
                        source.Animator.transform.position = Vector3.zero;
                        target.Animator.transform.position = Vector3.right * move.attackRange * (mirrored ? -1 : 1);
                        if (!source.ExecuteAttack(move, target, lethal)) throw new Exception("Heavy validation rejected attack.");
                        var pair = source.SourcePlayback; var damageTimes = BattleHitDamageSequence.ContactTimes(profile, pair.Duration);
                        if (damageTimes.Length != rows.Length + (move.moveName == "Heavy_Assassin" ? 1 : 0)) throw new Exception("Missing Heavy damage contact.");
                        long total = lethal ? 1000 : 103, hp = 1000, sum = 0; int applied = 0, emitted = 0;
                        var damage = new BattleHitDamageSequence(1000, lethal ? 0 : 897, total, damageTimes, (value, amount) => { hp = value; sum += amount; applied++; });
                        Action<FrankBattlePairPlayback, float> advance = (owner, time) => damage.Advance(time);
                        pair.TimelineAdvanced += advance;
                        using (var body = new ContactBody(target))
                        using (var probe = new ContactProbe())
                        {
                            Action<string, GameObject> observe = (id, root) =>
                            {
                                if (id != "light_hit" && id != "heavy_hit") return;
                                var cue = profile.cues.Single(c => IsContactCue(c) && Mathf.Abs(c.seconds - pair.SampleTime) < .0001f);
                                var row = rows.Single(s => Mathf.Abs(s.time - cue.seconds) < .00001f);
                                int reached = damageTimes.Count(t => t <= cue.seconds);
                                long expected = 1000 - (total / damageTimes.Length * reached + Math.Max(0, reached - (damageTimes.Length - total % damageTimes.Length)));
                                if (applied != reached || hp != expected) throw new Exception("Heavy impact preceded its exact damage.");
                                var renderer = pair.AttackerActor.Pose.weaponRenderers.SingleOrDefault(r => r.name == row.renderer);
                                int kind = Array.IndexOf(probe.names, row.source);
                                var point = target.Animator.GetBoneTransform(cue.contactBone).TransformPoint(cue.contactOffset);
                                pair.EvaluateAt(cue.seconds - 1f / 480);
                                var previous = HeavyBladePoints(pair, vfx.weaponTrails);
                                var previousLimb = kind >= 2 ? source.Animator.GetBoneTransform(ContactLimbs[kind - 2]).position : Vector3.zero;
                                pair.EvaluateAt(cue.seconds);
                                var current = HeavyBladePoints(pair, vfx.weaponTrails);
                                float speed = renderer ? Mathf.Max(Vector3.Distance(current[renderer][0], previous[renderer][0]), Vector3.Distance(current[renderer][1], previous[renderer][1])) * 480
                                    : Vector3.Distance(source.Animator.GetBoneTransform(ContactLimbs[kind - 2]).position, previousLimb) * 480;
                                body.Sample(); probe.Sample(pair, source, body, true, r => r == renderer);
                                float gap = Vector3.Distance(probe.points[kind], body.Surface(probe.points[kind]));
                                float surfaceError = Vector3.Distance(point, body.Surface(point)), effectError = Vector3.Distance(root.transform.position, point);
                                if (gap > .013f || surfaceError > .005f || effectError > .016f || Vector3.Distance(point, probe.points[kind]) > .013f || speed < .9f)
                                    throw new Exception(FormattableString.Invariant($"Heavy contact mismatch: {source.name}/{move.moveName}/{row.stroke} at {cue.seconds:F6}s; mirror={mirrored}, lethal={lethal}, skip={skipped}, gap={gap:F6}, body={surfaceError:F6}, effect={effectError:F6}, speed={speed:F3}."));
                                var cores = root.GetComponentsInChildren<ParticleSystem>().Where(p => p.name == "Contact core" || p.name == "Contact shockwave" || p.name == "Contact echo").ToArray();
                                if (cores.Length != 3 || cores.Any(p => p.particleCount != 1 || !p.isPlaying)) throw new Exception("Heavy contact core/rings did not appear immediately.");
                                report.AppendLine(FormattableString.Invariant($"HIT {source.name}/{move.moveName}/{row.stroke} at {cue.seconds:F6}s {row.renderer}; gap={gap:F6}m speed={speed:F3}m/s HP={hp}; mirror={mirrored} lethal={lethal} skip={skipped}"));
                                emitted++; checkedHits++;
                            };
                            vfx.EffectPlayed += observe;
                            try
                            {
                                if (skipped) pair.AdvanceTo(pair.Duration);
                                else foreach (float time in damageTimes)
                                {
                                    int before = emitted, damageBefore = applied; pair.AdvanceTo(time - .0001f);
                                    if (emitted != before || applied != damageBefore) throw new Exception("Early Heavy contact.");
                                    pair.AdvanceTo(time);
                                    if (applied != damageBefore + 1) throw new Exception("Heavy damage did not occur at contact.");
                                    int count = emitted; pair.AdvanceTo(time); pair.AdvanceTo(time - .2f);
                                    if (count != emitted || applied != damageBefore + 1) throw new Exception("Repeated Heavy contact.");
                                }
                                pair.AdvanceTo(pair.Duration);
                                if (emitted != rows.Length || applied != damageTimes.Length || hp != (lethal ? 0 : 897) || sum != total)
                                    throw new Exception("Heavy combo lost a strike or the accepted server damage total.");
                                int finished = emitted; pair.Cancel(); pair.AdvanceTo(pair.Duration);
                                if (emitted != finished || vfx.ActiveEffectCount != 0) throw new Exception("Cancelled Heavy combo emitted or retained VFX.");
                            }
                            finally { vfx.EffectPlayed -= observe; pair.TimelineAdvanced -= advance; damage.Cancel(); pair.Cancel(); }
                        }
                        cases++;
                    }
                }
                report.AppendLine($"ALL PASS: {cases} full-combo cases; {checkedHits} moving striker/body contacts; all 14 Heavy animations covered; missing-stroke regression rejected; exact server damage/HP totals; mirror, lethal, skipped frames, immediate core/rings, cancellation and deduplication.");
            }
            catch (Exception error) { report.AppendLine("FAIL " + error); throw; }
            finally { EditorSceneManager.ClosePreviewScene(scene); File.WriteAllText(AllHeavyReview + "/Validation.txt", report.ToString()); }
        }

        public static void CaptureAllReviewedHeavyContacts()
        {
            Directory.CreateDirectory(AllHeavyReview + "/After");
            var selected = ReadReviewedHeavyStrokes();
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var rt = RenderTexture.GetTemporary(1280, 720, 24);
            var stamp = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var previousActive = RenderTexture.active; BattleLightingRig lighting = null;
            var report = new StringBuilder("fighter,move,row,stroke,time,source,renderer,bone\n");
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat }; var vfx = game.battleVfx;
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single(c => c.CompareTag("MainCamera"));
                camera.scene = scene; camera.enabled = false; camera.orthographic = true;
                foreach (var canvas in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true))) canvas.gameObject.SetActive(false);
                foreach (var f in fighters) f.Initialize();
                lighting = game.GetComponent<BattleLightingRig>(); lighting.InitializeRig(); lighting.RefreshLighting(.22f);
                var director = camera.GetComponent<FrankCinematicCamera>();
                foreach (var source in fighters)
                foreach (var move in source.heavyCombatMoves)
                {
                    var rows = selected.Where(s => s.fighter == source.name && s.move == move.moveName).OrderBy(s => s.time).ToArray();
                    var sheet = new Texture2D(1200, rows.Length * 280, TextureFormat.RGB24, false);
                    try
                    {
                        for (int index = 0; index < rows.Length; index++)
                        {
                            var row = rows[index];
                            foreach (var f in fighters) f.ResetCombat(); vfx.ResetForMatch();
                            var target = fighters.Single(f => f != source); bool reverse = source == fighters[1];
                            source.Animator.transform.position = new Vector3(reverse ? .5f : -.5f, 0, 0);
                            target.Animator.transform.position = source.Animator.transform.position + Vector3.right * move.attackRange * (reverse ? -1 : 1);
                            var roots = new HashSet<GameObject>(); GameObject contact = null;
                            Action<string, GameObject> observe = (id, root) => {
                                roots.Add(root);
                                if ((id == "light_hit" || id == "heavy_hit") && Mathf.Abs(source.SourcePlayback.SampleTime - row.time) < .0001f) contact = root;
                            };
                            vfx.EffectPlayed += observe;
                            try
                            {
                                source.ExecuteAttack(move, target); var pair = source.SourcePlayback;
                                pair.EvaluateAt(row.time - .025f);
                                CaptureContactPose(camera, fighters, pair, sheet, 0, (rows.Length - index - 1) * 280);
                                pair.AdvanceTo(row.time);
                                if (!contact) throw new Exception("Reviewed Heavy stroke has no rendered contact.");
                                foreach (var root in roots)
                                {
                                    root.SetActive(root == contact);
                                    if (root == contact) foreach (var p in root.GetComponentsInChildren<ParticleSystem>()) p.Simulate(.035f, false, true, false);
                                }
                                CaptureContactPose(camera, fighters, pair, sheet, 400, (rows.Length - index - 1) * 280);
                                camera.targetTexture = rt; director.ResetView(); director.Apply(0, true);
                                CaptureStrongPose(camera, fighters, pair, stamp, rt);
                                File.WriteAllBytes($"{AllHeavyReview}/After/{source.name}_{move.moveName}_{row.stroke}.png", stamp.EncodeToPNG());
                                // The game camera director restores perspective for
                                // the full-size preview; contact sheets use a close
                                // orthographic view for every before/after pose.
                                camera.orthographic = true;
                                foreach (var root in roots) root.SetActive(false);
                                pair.EvaluateAt(row.time + .025f);
                                CaptureContactPose(camera, fighters, pair, sheet, 800, (rows.Length - index - 1) * 280);
                                report.AppendLine(FormattableString.Invariant($"{source.name},{move.moveName},{index},{row.stroke},{row.time:F6},{row.source},{row.renderer},{row.bone}"));
                                pair.Cancel();
                            }
                            finally { vfx.EffectPlayed -= observe; source.SourcePlayback?.Cancel(); }
                        }
                        sheet.Apply(); File.WriteAllBytes($"{AllHeavyReview}/After/{source.name}_{move.moveName}_Contacts.png", sheet.EncodeToPNG());
                        File.WriteAllText(AllHeavyReview + "/After.csv", report.ToString());
                    }
                    finally { Object.DestroyImmediate(sheet); }
                }
            }
            finally
            {
                if (lighting) lighting.ShutdownRig();
                RenderTexture.active = previousActive; Object.DestroyImmediate(stamp); RenderTexture.ReleaseTemporary(rt);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static Dictionary<Renderer, Vector3[]> HeavyBladePoints(FrankBattlePairPlayback pair, BattleWeaponTrails trails)
        {
            var points = new Dictionary<Renderer, Vector3[]>();
            foreach (var renderer in pair.AttackerActor.Pose.weaponRenderers)
            {
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.name.IndexOf("case", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                var skin = renderer as SkinnedMeshRenderer;
                var mesh = skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                var blade = trails.staticBlades.SingleOrDefault(b => b.mesh == mesh);
                if (blade == null) throw new Exception("Uncalibrated weapon: " + renderer.name);
                if (skin && blade.baseBones.Length > 0)
                {
                    var bones = skin.bones;
                    Vector3 Skin(BattleWeaponTrails.BonePoint[] samples)
                    {
                        Vector3 p = Vector3.zero;
                        foreach (var sample in samples) p += bones[sample.bone].TransformPoint(sample.point) * sample.weight;
                        return p;
                    }
                    points.Add(renderer, new[] { Skin(blade.baseBones), Skin(blade.tipBones) });
                }
                else points.Add(renderer, new[] { renderer.transform.TransformPoint(blade.bladeBase), renderer.transform.TransformPoint(blade.bladeTip) });
            }
            return points;
        }

        public static void SurveyAllHeavyAnimations()
        {
            Directory.CreateDirectory(AllHeavyReview);
            if (!File.Exists(AllHeavyReview + "/BankBefore.asset.txt")) File.Copy("Assets/Audio/Battle/BattleSfxBank.asset", AllHeavyReview + "/BankBefore.asset.txt");
            if (!File.Exists(AllHeavyReview + "/SceneBefore.unity.txt")) File.Copy(SfxScene, AllHeavyReview + "/SceneBefore.unity.txt");
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            BattleLightingRig lighting = null;
            var inventory = new StringBuilder("fighter,move,weapon,duration,index,time,group,source,bone\n");
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat }; var bank = game.battleVfx.timeline;
                foreach (var f in fighters) { f.battleSfx = null; f.battleVfx = null; f.hitEffect = null; }
                foreach (var canvas in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true))) canvas.gameObject.SetActive(false);
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single(c => c.CompareTag("MainCamera"));
                camera.scene = scene; camera.enabled = false; camera.orthographic = true;
                lighting = game.GetComponent<BattleLightingRig>(); lighting.InitializeRig(); lighting.RefreshLighting(.22f);
                foreach (var source in fighters)
                foreach (var move in source.heavyCombatMoves)
                {
                    var target = fighters.Single(f => f != source);
                    foreach (var f in fighters) f.ResetCombat();
                    source.Animator.transform.position = Vector3.zero; target.Animator.transform.position = Vector3.right * move.attackRange;
                    if (!source.ExecuteAttack(move, target)) throw new Exception("Survey attack rejected.");
                    var pair = source.SourcePlayback; string prefix = AllHeavyReview + "/" + source.name + "_" + move.moveName;
                    int index = 0;
                    foreach (var cue in bank.FindMove(move).cues.Where(IsContactCue))
                        inventory.AppendLine(FormattableString.Invariant($"{source.name},{move.moveName},{move.weapon},{pair.Duration:F6},{index++},{cue.seconds:F6},{cue.group},{cue.contactSource},{cue.contactBone}"));
                    var csv = new StringBuilder("time,weaponGap,shieldGap,leftHandGap,rightHandGap,leftFootGap,rightFootGap,bladeSpeed,shieldSpeed,tipX,tipY,tipZ\n");
                    Dictionary<Renderer, Vector3[]> previous = null;
                    using (var body = new ContactBody(target))
                    using (var probe = new ContactProbe())
                    for (int frame = 0; frame <= Mathf.CeilToInt(pair.Duration * 120); frame++)
                    {
                        float time = Mathf.Min(pair.Duration, frame / 120f); pair.EvaluateAt(time);
                        body.Sample(); probe.Sample(pair, source, body);
                        var current = HeavyBladePoints(pair, game.GetComponent<BattleWeaponTrails>());
                        float weaponSpeed = 0, shieldSpeed = 0; Vector3 tip = Vector3.zero;
                        foreach (var entry in current)
                        {
                            bool shield = entry.Key.name.IndexOf("shield", StringComparison.OrdinalIgnoreCase) >= 0;
                            if (!shield) tip = entry.Value[1];
                            if (previous == null || !previous.TryGetValue(entry.Key, out var last)) continue;
                            float speed = Vector3.Distance(entry.Value[1], last[1]) * 120;
                            if (shield) shieldSpeed = Mathf.Max(shieldSpeed, speed); else weaponSpeed = Mathf.Max(weaponSpeed, speed);
                        }
                        csv.AppendLine(FormattableString.Invariant($"{time:F6},{probe.distances[0]:F6},{probe.distances[1]:F6},{probe.distances[2]:F6},{probe.distances[3]:F6},{probe.distances[4]:F6},{probe.distances[5]:F6},{weaponSpeed:F6},{shieldSpeed:F6},{tip.x:F6},{tip.y:F6},{tip.z:F6}"));
                        previous = current;
                    }
                    File.WriteAllText(prefix + "_Motion.csv", csv.ToString());
                    int tiles = Mathf.CeilToInt(pair.Duration / .1f) + 1, rows = Mathf.CeilToInt(tiles / 6f);
                    var sheet = new Texture2D(2400, rows * 280, TextureFormat.RGB24, false);
                    var labels = new StringBuilder("row,column,time\n");
                    try
                    {
                        for (int tile = 0; tile < tiles; tile++)
                        {
                            float time = Mathf.Min(pair.Duration, tile * .1f); pair.EvaluateAt(time);
                            int row = tile / 6, col = tile % 6;
                            CaptureContactPose(camera, fighters, pair, sheet, col * 400, (rows - row - 1) * 280);
                            labels.AppendLine(FormattableString.Invariant($"{row},{col},{time:F3}"));
                        }
                        sheet.Apply(); File.WriteAllBytes(prefix + "_Timeline.png", sheet.EncodeToPNG());
                        File.WriteAllText(prefix + "_Timeline.csv", labels.ToString());
                    }
                    finally { Object.DestroyImmediate(sheet); pair.Cancel(); }
                    File.WriteAllText(AllHeavyReview + "/Inventory.csv", inventory.ToString());
                    File.AppendAllText(AllHeavyReview + "/Progress.txt", $"SURVEY {source.name}/{move.moveName}: full {pair.Duration:F3}s motion and poses\n");
                }
                File.AppendAllText(AllHeavyReview + "/Progress.txt", "ALL 14 ANIMATIONS SURVEYED\n");
            }
            finally { if (lighting) lighting.ShutdownRig(); EditorSceneManager.ClosePreviewScene(scene); }
        }

        public static void RefineAllHeavyContactWindows()
        {
            var plan = File.ReadAllLines(AllHeavyReview + "/StrokePlan.csv").Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Split(',')).ToArray();
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var selection = new StringBuilder("fighter,move,stroke,oldIndex,time,group,swing,source,renderer,gap,speed,bone,x,y,z\n");
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var f in fighters) { f.battleSfx = null; f.battleVfx = null; f.hitEffect = null; }
                foreach (var source in fighters)
                foreach (var move in source.heavyCombatMoves)
                {
                    var target = fighters.Single(f => f != source);
                    foreach (var f in fighters) f.ResetCombat();
                    source.Animator.transform.position = Vector3.zero; target.Animator.transform.position = Vector3.right * move.attackRange;
                    source.ExecuteAttack(move, target); var pair = source.SourcePlayback;
                    using (var body = new ContactBody(target))
                    using (var probe = new ContactProbe())
                    foreach (var row in plan.Where(r => r[0] == source.name && r[1] == move.moveName))
                    {
                        float from = ParseContactFloat(row[4]), to = ParseContactFloat(row[5]); int kind = Array.IndexOf(probe.names, row[6]);
                        var samples = new StringBuilder("time,renderer,gap,speed,bone,x,y,z\n"); bool chosen = false;
                        pair.EvaluateAt(from - 1f / 480);
                        var previous = HeavyBladePoints(pair, game.GetComponent<BattleWeaponTrails>());
                        var previousLimb = kind >= 2 ? source.Animator.GetBoneTransform(ContactLimbs[kind - 2]).position : Vector3.zero;
                        for (int frame = 0; frame <= Mathf.CeilToInt((to - from) * 480); frame++)
                        {
                            float time = Mathf.Min(to, from + frame / 480f); pair.EvaluateAt(time);
                            var current = HeavyBladePoints(pair, game.GetComponent<BattleWeaponTrails>());
                            var speeds = current.ToDictionary(e => e.Key, e => previous.TryGetValue(e.Key, out var last)
                                ? Mathf.Max(Vector3.Distance(e.Value[0], last[0]), Vector3.Distance(e.Value[1], last[1])) * 480 : 0);
                            var candidates = current.Keys.Where(r => (r.name.IndexOf("shield", StringComparison.OrdinalIgnoreCase) >= 0) == (kind == 1)).ToArray();
                            float fastest = candidates.Length == 0 ? 0 : candidates.Max(r => speeds[r]);
                            body.Sample();
                            foreach (var renderer in kind >= 2 ? new Renderer[] { null } : candidates)
                            {
                                float speed = renderer ? speeds[renderer] : Vector3.Distance(source.Animator.GetBoneTransform(ContactLimbs[kind - 2]).position, previousLimb) * 480;
                                probe.Sample(pair, source, body, true, r => r == renderer);
                                var point = body.Surface(probe.points[kind]); float gap = Vector3.Distance(point, probe.points[kind]);
                                var bone = NearestContactBone(target, point); var local = target.Animator.GetBoneTransform(bone).InverseTransformPoint(point);
                                string name = renderer ? renderer.name : row[6];
                                samples.AppendLine(FormattableString.Invariant($"{time:F6},{name},{gap:F6},{speed:F6},{bone},{local.x:F7},{local.y:F7},{local.z:F7}"));
                                // Require an actual moving strike. A blade merely held
                                // inside the body is not the second hand's next attack.
                                if (!chosen && gap <= .012f && speed >= 1 && (kind >= 2 || speed >= fastest * .5f))
                                {
                                    selection.AppendLine(FormattableString.Invariant($"{source.name},{move.moveName},{row[2]},{row[3]},{time:F6},{row[7]},{row[8]},{row[6]},{name},{gap:F6},{speed:F6},{bone},{local.x:F7},{local.y:F7},{local.z:F7}"));
                                    chosen = true;
                                }
                            }
                            previous = current;
                            if (kind >= 2) previousLimb = source.Animator.GetBoneTransform(ContactLimbs[kind - 2]).position;
                        }
                        string prefix = AllHeavyReview + "/" + source.name + "_" + move.moveName + "_" + row[2];
                        File.WriteAllText(prefix + "_Fine.csv", samples.ToString());
                        File.AppendAllText(AllHeavyReview + "/RefinementProgress.txt", $"{(chosen ? "SELECTED" : "NO MOVING CONTACT")} {source.name}/{move.moveName}/{row[2]}\n");
                        File.WriteAllText(AllHeavyReview + "/Selection.csv", selection.ToString());
                    }
                    pair.Cancel();
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
