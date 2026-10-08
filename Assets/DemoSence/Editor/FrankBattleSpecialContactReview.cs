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
        const string SpecialContactReview = "GeneratedAssets/BattleSpecialContactReview";
        static bool SpecialHeavy(CombatTripletData move) => move.moveName == "Heavy_7" || move.moveName == "Heavy_Assassin";

        public static void SurveySpecialHeavyFullMotion()
        {
            Directory.CreateDirectory(SpecialContactReview);
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var csv = new StringBuilder("fighter,move,time,kind,distance,x,y,z\n");
            var inventory = new StringBuilder();
            BattleLightingRig lighting = null;
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var bank = game.battleVfx.timeline;
                if (!File.Exists(SpecialContactReview + "/ContactBankBefore.asset.txt"))
                    File.Copy(AssetDatabase.GetAssetPath(bank), SpecialContactReview + "/ContactBankBefore.asset.txt");
                if (!File.Exists(SpecialContactReview + "/BattleSceneBefore.unity.txt"))
                    File.Copy(SfxScene, SpecialContactReview + "/BattleSceneBefore.unity.txt");
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var f in fighters) { f.battleSfx = null; f.battleVfx = null; f.hitEffect = null; }
                foreach (var canvas in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true))) canvas.gameObject.SetActive(false);
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single(c => c.CompareTag("MainCamera"));
                camera.scene = scene; camera.enabled = false; camera.orthographic = true;
                lighting = game.GetComponent<BattleLightingRig>(); lighting.InitializeRig(); lighting.RefreshLighting(.22f);
                foreach (var source in fighters)
                foreach (var move in source.heavyCombatMoves.Where(SpecialHeavy))
                {
                    foreach (var f in fighters) f.ResetCombat();
                    var target = fighters.Single(f => f != source);
                    source.Animator.transform.position = Vector3.zero;
                    target.Animator.transform.position = Vector3.right * move.attackRange;
                    if (!source.ExecuteAttack(move, target)) throw new Exception("Full motion survey rejected attack.");
                    var pair = source.SourcePlayback;
                    inventory.AppendLine($"{source.name}/{move.moveName} duration={pair.Duration:F5}, scale={source.Animator.transform.lossyScale}");
                    foreach (var cue in bank.FindMove(move).cues)
                        inventory.AppendLine($"  {cue.seconds:F5}s {cue.group} {cue.contactSource} damagingLanding={cue.damageOnLanding}");
                    using (var body = new ContactBody(target))
                    using (var probe = new ContactProbe())
                    for (int frame = 0; frame <= Mathf.CeilToInt(pair.Duration * 120); frame++)
                    {
                        float time = Mathf.Min(pair.Duration, frame / 120f);
                        pair.EvaluateAt(time); body.Sample(); probe.Sample(pair, source, body);
                        for (int k = 0; k < probe.names.Length; k++)
                        {
                            var p = probe.points[k];
                            csv.AppendLine(FormattableString.Invariant($"{source.name},{move.moveName},{time:F5},{probe.names[k]},{probe.distances[k]:F5},{p.x:F5},{p.y:F5},{p.z:F5}"));
                        }
                    }
                    int tiles = Mathf.CeilToInt(pair.Duration / .15f) + 1;
                    int rows = Mathf.CeilToInt(tiles / 6f);
                    var sheet = new Texture2D(2400, rows * 280, TextureFormat.RGB24, false);
                    var labels = new StringBuilder("row,column,time\n");
                    try
                    {
                        for (int tile = 0; tile < tiles; tile++)
                        {
                            float time = Mathf.Min(pair.Duration, tile * .15f); pair.EvaluateAt(time);
                            int row = tile / 6, col = tile % 6;
                            CaptureContactPose(camera, fighters, pair, sheet, col * 400, (rows - row - 1) * 280);
                            labels.AppendLine(FormattableString.Invariant($"{row},{col},{time:F3}"));
                        }
                        sheet.Apply(); string path = SpecialContactReview + "/" + source.name + "_" + move.moveName;
                        File.WriteAllBytes(path + "_Timeline.png", sheet.EncodeToPNG());
                        File.WriteAllText(path + "_Timeline.csv", labels.ToString());
                    }
                    finally { Object.DestroyImmediate(sheet); pair.Cancel(); }
                    File.WriteAllText(SpecialContactReview + "/FullMotion.csv", csv.ToString());
                    File.WriteAllText(SpecialContactReview + "/BeforeCues.txt", inventory.ToString());
                }
            }
            finally
            {
                if (lighting) lighting.ShutdownRig();
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        public static void CaptureSpecialHeavyContactCandidates()
        {
            var plan = File.ReadAllLines(SpecialContactReview + "/CandidatePlan.csv").Skip(1)
                .Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Split(',')).ToArray();
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            BattleLightingRig lighting = null;
            var report = new StringBuilder("fighter,move,candidate,time,gap,kind,x,y,z\n");
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var f in fighters) { f.battleSfx = null; f.battleVfx = null; f.hitEffect = null; }
                foreach (var canvas in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true))) canvas.gameObject.SetActive(false);
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single(c => c.CompareTag("MainCamera"));
                camera.scene = scene; camera.enabled = false; camera.orthographic = true;
                lighting = game.GetComponent<BattleLightingRig>(); lighting.InitializeRig(); lighting.RefreshLighting(.22f);
                foreach (var source in fighters)
                foreach (var move in source.heavyCombatMoves.Where(SpecialHeavy))
                {
                    var rows = plan.Where(r => r[0] == source.name && r[1] == move.moveName).ToArray();
                    var target = fighters.Single(f => f != source);
                    foreach (var f in fighters) f.ResetCombat();
                    source.Animator.transform.position = Vector3.zero; target.Animator.transform.position = Vector3.right * move.attackRange;
                    source.ExecuteAttack(move, target); var pair = source.SourcePlayback;
                    var sheet = new Texture2D(2400, rows.Length * 280, TextureFormat.RGB24, false);
                    using (var body = new ContactBody(target))
                    using (var probe = new ContactProbe())
                    try
                    {
                        for (int row = 0; row < rows.Length; row++)
                        {
                            float center = ParseContactFloat(rows[row][3]);
                            var offsets = new[] { -.09f, -.045f, -.008333f, .008333f, .045f, .09f };
                            for (int col = 0; col < offsets.Length; col++)
                            {
                                float time = center + offsets[col]; pair.EvaluateAt(time);
                                body.Sample(); probe.Sample(pair, source, body, true);
                                var point = body.Surface(probe.points[0]);
                                report.AppendLine(FormattableString.Invariant($"{source.name},{move.moveName},{row},{time:F5},{Vector3.Distance(point,probe.points[0]):F5},Weapon,{point.x:F5},{point.y:F5},{point.z:F5}"));
                                CaptureContactPose(camera, fighters, pair, sheet, col * 400, (rows.Length - row - 1) * 280);
                            }
                        }
                        sheet.Apply();
                        File.WriteAllBytes(SpecialContactReview + "/" + source.name + "_" + move.moveName + "_Candidates.png", sheet.EncodeToPNG());
                    }
                    finally { Object.DestroyImmediate(sheet); pair.Cancel(); }
                    File.WriteAllText(SpecialContactReview + "/Candidates.csv", report.ToString());
                }
            }
            finally { if (lighting) lighting.ShutdownRig(); EditorSceneManager.ClosePreviewScene(scene); }
        }

        // The windows come from reviewing the full motion, including the strokes
        // absent from the original cue bank. Keep this independent of cue counts.
        static readonly Vector2[] SpearContactWindows = {
            new Vector2(1.15f, 1.26f), new Vector2(2.11f, 2.18f), new Vector2(2.39f, 2.45f),
            new Vector2(2.70f, 2.79f), new Vector2(3.28f, 3.35f), new Vector2(3.58f, 3.75f),
            new Vector2(4.61f, 4.73f), new Vector2(5.42f, 5.60f)
        };
        static readonly Vector2[] AssassinContactWindows = {
            new Vector2(.83f, .94f), new Vector2(1.30f, 1.40f), new Vector2(1.67f, 1.75f),
            new Vector2(1.83f, 1.89f), new Vector2(2.00f, 2.08f), new Vector2(2.35f, 2.42f),
            new Vector2(2.49f, 2.56f), new Vector2(2.97f, 3.06f)
        };

        static void RequireSpecialContactCoverage(BattleSfxBank.Move profile, CombatTripletData move)
        {
            var windows = move.moveName == "Heavy_7" ? SpearContactWindows : AssassinContactWindows;
            var hits = profile.cues.Where(IsContactCue).ToArray();
            if (hits.Length != windows.Length || windows.Any(w => hits.Count(h => h.seconds >= w.x && h.seconds <= w.y) != 1))
                throw new Exception("A reviewed weapon stroke has no unique hit cue: " + move.moveName);
        }

        public static void RefineSpecialHeavyMissingContacts()
        {
            var plan = File.ReadAllLines(SpecialContactReview + "/MissingContacts.csv").Skip(1)
                .Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Split(',')).ToArray();
            var selection = new StringBuilder("fighter,move,stroke,time,group,swing,gap\n");
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var f in fighters) { f.battleSfx = null; f.battleVfx = null; f.hitEffect = null; }
                foreach (var source in fighters)
                foreach (var move in source.heavyCombatMoves.Where(SpecialHeavy))
                {
                    var target = fighters.Single(f => f != source);
                    foreach (var f in fighters) f.ResetCombat();
                    source.Animator.transform.position = Vector3.zero; target.Animator.transform.position = Vector3.right * move.attackRange;
                    source.ExecuteAttack(move, target); var pair = source.SourcePlayback;
                    using (var body = new ContactBody(target))
                    using (var probe = new ContactProbe())
                    foreach (var row in plan.Where(r => r[0] == source.name && r[1] == move.moveName))
                    {
                        float center = ParseContactFloat(row[3]), selected = -1, bestGap = float.PositiveInfinity, bestTime = 0;
                        // Fast knife cuts can cross the whole body between two 60 Hz frames.
                        // Resolve entry at 480 Hz on the actual blade faces and posed skin.
                        for (int frame = -24; frame <= 24; frame++)
                        {
                            float time = center + frame / 480f; pair.EvaluateAt(time);
                            body.Sample(); probe.Sample(pair, source, body, true);
                            float gap = Vector3.Distance(probe.points[0], body.Surface(probe.points[0]));
                            if (gap < bestGap) { bestGap = gap; bestTime = time; }
                            if (selected < 0 && gap <= .012f) selected = time;
                        }
                        if (selected < 0)
                        {
                            if (bestGap > .035f) throw new Exception("Candidate does not touch the body: " + source.name + "/" + row[2] + ", gap=" + bestGap);
                            selected = bestTime;
                        }
                        pair.EvaluateAt(selected); body.Sample(); probe.Sample(pair, source, body, true);
                        float selectedGap = Vector3.Distance(probe.points[0], body.Surface(probe.points[0]));
                        selection.AppendLine(FormattableString.Invariant($"{source.name},{move.moveName},{row[2]},{selected:F6},{row[4]},{row[5]},{selectedGap:F6}"));
                        File.WriteAllText(SpecialContactReview + "/MissingSelection.csv", selection.ToString());
                    }
                    pair.Cancel();
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [MenuItem("Tools/Battle/Apply reviewed Spear and Assassin missing contacts")]
        public static void InstallSpecialHeavyMissingContacts()
        {
            if (File.Exists(AllHeavyReview + "/ApprovedSelection.csv")) { InstallAllReviewedHeavyContacts(); return; }
            var selections = File.ReadAllLines(SpecialContactReview + "/MissingSelection.csv").Skip(1)
                .Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Split(',')).ToArray();
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var report = new StringBuilder();
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var bank = game.battleVfx.timeline;
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var f in fighters) { f.battleSfx = null; f.battleVfx = null; f.hitEffect = null; }
                // Work on copied cue arrays; a rejected geometry check must never
                // partially edit the live bank or save sampled animation poses.
                var pending = new Dictionary<BattleSfxBank.Move, BattleSfxBank.Cue[]>();
                foreach (var source in fighters)
                foreach (var move in source.heavyCombatMoves.Where(SpecialHeavy))
                {
                    var profile = bank.FindMove(move);
                    var cues = profile.cues.ToList();
                    var target = fighters.Single(f => f != source);
                    foreach (var f in fighters) f.ResetCombat();
                    source.Animator.transform.position = Vector3.zero; target.Animator.transform.position = Vector3.right * move.attackRange;
                    source.ExecuteAttack(move, target); var pair = source.SourcePlayback;
                    using (var body = new ContactBody(target))
                    using (var probe = new ContactProbe())
                    foreach (var row in selections.Where(r => r[0] == source.name && r[1] == move.moveName))
                    {
                        float time = ParseContactFloat(row[3]);
                        // Reapplying the reviewed selection does not create another hit or swing.
                        if (cues.Any(c => IsContactCue(c) && Mathf.Abs(c.seconds - time) < .004f)) continue;
                        pair.EvaluateAt(time); body.Sample(); probe.Sample(pair, source, body, true);
                        var contact = body.Surface(probe.points[0]); float gap = Vector3.Distance(contact, probe.points[0]);
                        if (gap > .035f) throw new Exception("Selected strike no longer touches the body.");
                        var bone = NearestContactBone(target, contact);
                        cues.Add(new BattleSfxBank.Cue {
                            seconds = time, group = row[4], hasContactPoint = true,
                            contactSource = "Weapon", contactBone = bone,
                            contactOffset = target.Animator.GetBoneTransform(bone).InverseTransformPoint(contact)
                        });
                        cues.Add(new BattleSfxBank.Cue { seconds = time - .075f, group = row[5] });
                        report.AppendLine(FormattableString.Invariant($"ADD {source.name}/{move.moveName} {row[2]}: {time:F6}s {row[4]} -> {bone}, gap={gap:F6}m"));
                    }
                    pair.Cancel();
                    var reviewed = new BattleSfxBank.Move { cues = cues.OrderBy(c => c.seconds).ToArray() };
                    RequireSpecialContactCoverage(reviewed, move);
                    pending[profile] = reviewed.cues;
                }
                Undo.RecordObject(bank, "Complete Spear and Assassin contact timeline");
                foreach (var change in pending) change.Key.cues = change.Value;
                EditorUtility.SetDirty(bank); AssetDatabase.SaveAssets();
                File.WriteAllText(SpecialContactReview + "/Installation.txt", report +
                    "Both Spear combos: 8 weapon contacts. Both Assassin combos: 8 weapon contacts + the existing damaging throw landing.\n" +
                    "Accepted server damage totals, other move profiles, scene settings and existing calibrated contacts preserved.\n");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        public static void ValidateSpecialHeavyContactCoverage()
        {
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var report = new StringBuilder(); int cases = 0, hits = 0;
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var vfx = game.battleVfx; var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var source in fighters)
                foreach (var move in source.heavyCombatMoves.Where(SpecialHeavy))
                {
                    var profile = vfx.timeline.FindMove(move); RequireSpecialContactCoverage(profile, move);
                    // Removing a reviewed stroke must fail, even if every remaining cue plays correctly.
                    var missing = new BattleSfxBank.Move { cues = profile.cues.Where(c => c != profile.cues.First(IsContactCue)).ToArray() };
                    bool rejected = false;
                    try { RequireSpecialContactCoverage(missing, move); } catch (Exception) { rejected = true; }
                    if (!rejected) throw new Exception("Coverage test accepts an incomplete combo.");
                    foreach (bool mirrored in new[] { false, true })
                    foreach (bool lethal in new[] { false, true })
                    foreach (bool skipped in new[] { false, true })
                    {
                        foreach (var f in fighters) f.ResetCombat(); vfx.ResetForMatch(); game.battleSfx.ResetForMatch();
                        var target = fighters.Single(f => f != source);
                        source.Animator.transform.position = Vector3.zero;
                        target.Animator.transform.position = Vector3.right * move.attackRange * (mirrored ? -1 : 1);
                        if (!source.ExecuteAttack(move, target, lethal)) throw new Exception("Coverage attack rejected.");
                        var pair = source.SourcePlayback;
                        var damageTimes = BattleHitDamageSequence.ContactTimes(profile, pair.Duration);
                        if (damageTimes.Length != (move.moveName == "Heavy_Assassin" ? 9 : 8)) throw new Exception("Incomplete damage contacts.");
                        long hp = 1000, sum = 0; int applied = 0, emitted = 0;
                        var damage = new BattleHitDamageSequence(1000, lethal ? 0 : 897, lethal ? 1000 : 103, damageTimes,
                            (value, amount) => { hp = value; sum += amount; applied++; });
                        Action<FrankBattlePairPlayback, float> advance = (owner, time) => damage.Advance(time);
                        pair.TimelineAdvanced += advance;
                        using (var body = new ContactBody(target))
                        using (var probe = new ContactProbe())
                        {
                            Action<string, GameObject> observe = (id, root) =>
                            {
                                if (id != "light_hit" && id != "heavy_hit") return;
                                var cue = profile.cues.Single(c => IsContactCue(c) && Mathf.Abs(c.seconds - pair.SampleTime) < .0001f);
                                if (applied != damageTimes.Count(t => t <= cue.seconds)) throw new Exception("Impact precedes damage.");
                                var point = target.Animator.GetBoneTransform(cue.contactBone).TransformPoint(cue.contactOffset);
                                body.Sample(); probe.Sample(pair, source, body, true);
                                float gap = Vector3.Distance(probe.points[0], body.Surface(probe.points[0]));
                                float surfaceError = Vector3.Distance(point, body.Surface(point));
                                float effectError = Vector3.Distance(root.transform.position, point);
                                if (!cue.hasContactPoint || gap > .035f || Vector3.Distance(point, body.Surface(point)) > .005f ||
                                    effectError > .016f) throw new Exception(FormattableString.Invariant($"Impact left the blade/body contact: {source.name}/{move.moveName} at {cue.seconds:F6}s, mirror={mirrored}, lethal={lethal}, skip={skipped}, gap={gap:F6}, surface={surfaceError:F6}, effect={effectError:F6}, anchor={cue.contactBone}."));
                                var rings = root.GetComponentsInChildren<ParticleSystem>().Where(p => p.name == "Contact shockwave" || p.name == "Contact echo").ToArray();
                                if (rings.Length != 2 || rings.Any(p => p.particleCount != 1)) throw new Exception("Missing immediate contact rings.");
                                report.AppendLine(FormattableString.Invariant($"HIT {source.name}/{move.moveName} #{emitted} {cue.seconds:F6}s gap={gap:F6}m mirror={mirrored} lethal={lethal} skip={skipped}"));
                                emitted++; hits++;
                            };
                            vfx.EffectPlayed += observe;
                            try
                            {
                                if (skipped) pair.AdvanceTo(pair.Duration);
                                else foreach (float time in damageTimes)
                                {
                                    int before = emitted; pair.AdvanceTo(time - .0001f);
                                    if (emitted != before) throw new Exception("Early contact effect.");
                                    pair.AdvanceTo(time); int count = emitted;
                                    pair.AdvanceTo(time); pair.AdvanceTo(time - .2f);
                                    if (count != emitted) throw new Exception("Duplicate contact effect.");
                                }
                                pair.AdvanceTo(pair.Duration);
                                if (emitted != 8 || applied != damageTimes.Length || hp != (lethal ? 0 : 897) || sum != (lethal ? 1000 : 103))
                                    throw new Exception("A stroke or the exact server damage total was lost.");
                                pair.Cancel();
                                if (vfx.ActiveEffectCount != 0) throw new Exception("Cancelled combo left VFX active.");
                            }
                            finally { vfx.EffectPlayed -= observe; pair.TimelineAdvanced -= advance; damage.Cancel(); pair.Cancel(); }
                        }
                        cases++;
                    }
                }
                report.AppendLine($"ALL PASS: {cases} full-combo cases; {hits} blade/body contacts; all eight reviewed strokes covered for each fighter and weapon; missing-stroke regression rejected; exact HP totals; mirror, lethal, frame skip, immediate rings, cancel and duplicate checks.");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); File.WriteAllText(SpecialContactReview + "/Validation.txt", report.ToString()); }
        }

        public static void TightenSpecialHeavyExistingContacts()
        {
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var report = new StringBuilder("fighter,move,oldTime,newTime,oldSwing,newSwing,oldGap,newGap\n");
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var bank = game.battleVfx.timeline; var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var f in fighters) { f.battleSfx = null; f.battleVfx = null; f.hitEffect = null; }
                var pending = new Dictionary<BattleSfxBank.Move, BattleSfxBank.Cue[]>();
                foreach (var source in fighters)
                foreach (var move in source.heavyCombatMoves.Where(SpecialHeavy))
                {
                    var profile = bank.FindMove(move);
                    var cues = profile.cues.Select(c => JsonUtility.FromJson<BattleSfxBank.Cue>(JsonUtility.ToJson(c))).ToArray();
                    var target = fighters.Single(f => f != source);
                    foreach (var f in fighters) f.ResetCombat();
                    source.Animator.transform.position = Vector3.zero; target.Animator.transform.position = Vector3.right * move.attackRange;
                    source.ExecuteAttack(move, target); var pair = source.SourcePlayback;
                    using (var body = new ContactBody(target))
                    using (var probe = new ContactProbe())
                    foreach (var cue in cues.Where(IsContactCue))
                    {
                        pair.EvaluateAt(cue.seconds); body.Sample(); probe.Sample(pair, source, body, true);
                        float originalGap = Vector3.Distance(probe.points[0], body.Surface(probe.points[0]));
                        if (originalGap <= .012f) continue;
                        float originalTime = cue.seconds, selected = -1;
                        for (int frame = -12; frame <= 12; frame++)
                        {
                            float time = originalTime + frame / 480f; pair.EvaluateAt(time);
                            body.Sample(); probe.Sample(pair, source, body, true);
                            if (Vector3.Distance(probe.points[0], body.Surface(probe.points[0])) > .012f) continue;
                            selected = time; break;
                        }
                        if (selected < 0) throw new Exception("Existing special hit has no close contact pose: " + source.name + "/" + move.moveName);
                        var swing = cues.Where(c => c.seconds < originalTime && c.group.EndsWith("swing", StringComparison.Ordinal)).LastOrDefault();
                        float previousSwing = swing?.seconds ?? -1;
                        if (swing != null) swing.seconds += selected - originalTime;
                        pair.EvaluateAt(selected); body.Sample(); probe.Sample(pair, source, body, true);
                        var point = body.Surface(probe.points[0]);
                        cue.seconds = selected; cue.contactBone = NearestContactBone(target, point);
                        cue.contactOffset = target.Animator.GetBoneTransform(cue.contactBone).InverseTransformPoint(point);
                        report.AppendLine(FormattableString.Invariant($"{source.name},{move.moveName},{originalTime:F6},{selected:F6},{previousSwing:F6},{swing?.seconds ?? -1:F6},{originalGap:F6},{Vector3.Distance(probe.points[0],point):F6}"));
                    }
                    pair.Cancel(); RequireSpecialContactCoverage(new BattleSfxBank.Move { cues = cues }, move);
                    pending[profile] = cues.OrderBy(c => c.seconds).ToArray();
                }
                Undo.RecordObject(bank, "Align remaining Spear contact to first surface entry");
                foreach (var change in pending) change.Key.cues = change.Value;
                EditorUtility.SetDirty(bank); AssetDatabase.SaveAssets();
                File.WriteAllText(SpecialContactReview + "/ExistingContactRefinement.csv", report.ToString());
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        public static void CaptureSpecialHeavyContactEffects()
        {
            var selections = File.ReadAllLines(SpecialContactReview + "/MissingSelection.csv").Skip(1)
                .Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Split(',')).ToArray();
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var rt = RenderTexture.GetTemporary(1280, 720, 24);
            var stamp = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var previousActive = RenderTexture.active;
            BattleLightingRig lighting = null; BattleSfxBank beforeBank = null;
            var report = new StringBuilder();
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var vfx = game.battleVfx; var currentBank = vfx.timeline;
                beforeBank = Object.Instantiate(currentBank);
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var source in fighters)
                foreach (var move in source.heavyCombatMoves.Where(SpecialHeavy))
                {
                    var profile = beforeBank.FindMove(move);
                    var newRows = selections.Where(r => r[0] == source.name && r[1] == move.moveName).ToArray();
                    profile.cues = profile.cues.Where(c => !newRows.Any(r =>
                        c.group == r[4] && Mathf.Abs(c.seconds - ParseContactFloat(r[3])) < .004f ||
                        c.group == r[5] && Mathf.Abs(c.seconds - (ParseContactFloat(r[3]) - .075f)) < .004f)).ToArray();
                }
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>()).Single(c => c.CompareTag("MainCamera"));
                camera.scene = scene; camera.targetTexture = rt;
                foreach (var canvas in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)))
                { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
                foreach (var fighter in fighters) fighter.Initialize();
                lighting = game.GetComponent<BattleLightingRig>(); lighting.InitializeRig();
                var director = camera.GetComponent<FrankCinematicCamera>();
                foreach (string stage in new[] { "Before", "After" })
                {
                    Directory.CreateDirectory(SpecialContactReview + "/" + stage);
                    vfx.timeline = game.battleSfx.bank = stage == "Before" ? beforeBank : currentBank;
                    foreach (var source in fighters)
                    foreach (var move in source.heavyCombatMoves.Where(SpecialHeavy))
                    foreach (var row in selections.Where(r => r[0] == source.name && r[1] == move.moveName))
                    {
                        foreach (var f in fighters) f.ResetCombat(); vfx.ResetForMatch();
                        var target = fighters.Single(f => f != source); bool reverse = source == fighters[1];
                        source.Animator.transform.position = new Vector3(reverse ? .5f : -.5f, 0, 0);
                        target.Animator.transform.position = source.Animator.transform.position + Vector3.right * (reverse ? -move.attackRange : move.attackRange);
                        float time = ParseContactFloat(row[3]); var roots = new HashSet<GameObject>(); GameObject contact = null;
                        Action<string, GameObject> observe = (id, root) =>
                        {
                            roots.Add(root);
                            if ((id == "light_hit" || id == "heavy_hit") && Mathf.Abs(source.SourcePlayback.SampleTime - time) < .0001f) contact = root;
                        };
                        vfx.EffectPlayed += observe;
                        try
                        {
                            source.ExecuteAttack(move, target); var pair = source.SourcePlayback; pair.AdvanceTo(time);
                            if ((stage == "After") != (contact != null)) throw new Exception("Incorrect before/after contact preview.");
                            foreach (var root in roots)
                            {
                                root.SetActive(root == contact);
                                if (root != contact) continue;
                                foreach (var p in root.GetComponentsInChildren<ParticleSystem>()) p.Simulate(.035f, false, true, false);
                            }
                            lighting.RefreshLighting(.22f); director.ResetView(); director.Apply(0, true); Canvas.ForceUpdateCanvases();
                            CaptureStrongPose(camera, fighters, pair, stamp, rt);
                            string path = $"{SpecialContactReview}/{stage}/{source.name}_{move.moveName}_{row[2]}.png";
                            File.WriteAllBytes(path, stamp.EncodeToPNG()); report.AppendLine($"PASS {stage} {source.name}/{move.moveName}/{row[2]} at {time:F6}s contact={(contact ? contact.name : "none")}");
                            pair.Cancel();
                        }
                        finally { vfx.EffectPlayed -= observe; }
                    }
                }
                File.WriteAllText(SpecialContactReview + "/ContactPreviews.txt", report.ToString());
            }
            finally
            {
                if (lighting) lighting.ShutdownRig(); if (beforeBank) Object.DestroyImmediate(beforeBank);
                RenderTexture.active = previousActive; Object.DestroyImmediate(stamp); RenderTexture.ReleaseTemporary(rt);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
