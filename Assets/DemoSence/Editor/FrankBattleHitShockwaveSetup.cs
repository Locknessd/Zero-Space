using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        const string HitShockwaveAssets = "Assets/Vfx/Battle/HitShockwaves";
        const string HitShockwaveReview = "GeneratedAssets/BattleHitShockwaveReview";

        static IEnumerable<GameObject> ContactPrefabs(BattleVfxPlayer vfx) =>
            new[] { vfx.lightHit, vfx.heavyHit, vfx.shieldImpact }
                .Concat(vfx.impactVariants.SelectMany(v => new[] { v.light, v.heavy, v.stab }))
                .Where(p => p).Distinct();

        public static void CaptureBattleHitShockwavesBefore() => CaptureBattleHitShockwaves("Before");
        public static void CaptureBattleHitShockwavesAfter() => CaptureBattleHitShockwaves("After");

        static Mesh HitShockwaveMesh()
        {
            var vertices = new List<Vector3>(); var colors = new List<Color32>(); var triangles = new List<int>();
            Band(.5f, .439f, new Color(.055f, .04f, .07f, .8f));
            Band(.49f, .449f, Color.white);
            Band(.478f, .463f, new Color(1, 1, 1, .65f));
            string path = HitShockwaveAssets + "/ContactRing.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh) { mesh = new Mesh { name = "ContactRing" }; AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh); return mesh;
            void Band(float outer, float inner, Color color)
            {
                const int segments = 40;
                for (int i = 0; i < segments; i++)
                {
                    float a = i * Mathf.PI * 2 / segments, b = (i + 1) * Mathf.PI * 2 / segments;
                    int n = vertices.Count;
                    vertices.Add(new Vector3(Mathf.Cos(a) * outer, Mathf.Sin(a) * outer, 0));
                    vertices.Add(new Vector3(Mathf.Cos(a) * inner, Mathf.Sin(a) * inner, 0));
                    vertices.Add(new Vector3(Mathf.Cos(b) * inner, Mathf.Sin(b) * inner, 0));
                    vertices.Add(new Vector3(Mathf.Cos(b) * outer, Mathf.Sin(b) * outer, 0));
                    for (int c = 0; c < 4; c++) colors.Add(color);
                    triangles.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 });
                }
            }
        }

        static void AddHitShockwave(GameObject root, Mesh mesh, Material material, Color tint, bool heavy, bool echo)
        {
            var child = new GameObject(echo ? "Contact echo" : "Contact shockwave");
            child.transform.SetParent(root.transform, false);
            // Its centre stays on the authored contact; the plane faces the battle camera.
            child.transform.localPosition = new Vector3(0, 0, -.003f);
            var p = child.AddComponent<ParticleSystem>(); p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = p.main; main.duration = .05f; main.loop = main.playOnAwake = false;
            main.startDelay = 0; main.startLifetime = heavy ? (echo ? .3f : .25f) : (echo ? .24f : .19f);
            main.startSpeed = 0; main.startSize = heavy ? (echo ? .78f : 1.08f) : (echo ? .62f : .82f);
            main.startColor = echo ? Color.Lerp(tint, Color.white, .65f) : tint;
            main.maxParticles = 1; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.useUnscaledTime = false; main.stopAction = ParticleSystemStopAction.None;
            var emission = p.emission; emission.rateOverTime = 0; emission.rateOverDistance = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, (short)1) });
            var shape = p.shape; shape.enabled = false;
            var size = p.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(
                new Keyframe(0, echo ? .11f : .18f), new Keyframe(.2f, echo ? .42f : .65f),
                new Keyframe(.6f, .96f), new Keyframe(1, 1.12f)));
            var fade = p.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient(); gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(echo ? .85f : 1, 0), new GradientAlphaKey(echo ? .75f : 1, .25f), new GradientAlphaKey(0, 1) });
            fade.color = gradient;
            var renderer = p.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = mesh; renderer.sharedMaterial = material; renderer.alignment = ParticleSystemRenderSpace.Local;
            renderer.sortingOrder = echo ? 17 : 18;
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color });
        }

        [MenuItem("Tools/Battle/VFX/Apply contact shockwaves")]
        public static void InstallBattleHitShockwaves()
        {
            Directory.CreateDirectory(HitShockwaveAssets); Directory.CreateDirectory(HitShockwaveReview);
            AssetDatabase.Refresh();
            var mesh = HitShockwaveMesh();
            var material = AssetDatabase.LoadAssetAtPath<Material>(ComicAssets + "/ComicParticle.mat");
            if (!material) throw new Exception("Missing depth-tested comic particle material.");
            var originalSources = new Dictionary<string, string>();
            string manifest = HitShockwaveReview + "/Sources.tsv";
            if (File.Exists(manifest)) foreach (var line in File.ReadAllLines(manifest))
            { var fields = line.Split('\t'); originalSources[fields[0]] = fields[1]; }
            string bankBefore = File.ReadAllText("Assets/Audio/Battle/BattleSfxBank.asset");
            ComicScene((game, camera) =>
            {
                var vfx = game.battleVfx;
                var replacements = new Dictionary<GameObject, GameObject>();
                foreach (var selected in ContactPrefabs(vfx))
                {
                    string sourcePath = AssetDatabase.GetAssetPath(selected);
                    if (originalSources.TryGetValue(sourcePath, out var original)) sourcePath = original;
                    if (sourcePath.StartsWith(HitShockwaveAssets, StringComparison.Ordinal))
                        throw new Exception("Missing original contact template; refusing to stack strength changes.");
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                    bool heavy = selected == vfx.heavyHit || selected == vfx.shieldImpact || vfx.impactVariants.Any(v => v.heavy == selected);
                    var variant = vfx.impactVariants.FirstOrDefault(v => v.light == selected || v.heavy == selected || v.stab == selected);
                    var style = variant == null ? null : vfx.weaponTrails.styles.FirstOrDefault(s => s.weapon == variant.weapon);
                    Color tint = style == null ? new Color(1, .78f, .18f) : Color.Lerp(style.color, Color.white, .35f);
                    var root = Object.Instantiate(source); root.name = source.name + "Shockwave";
                    try
                    {
                        foreach (var p in root.GetComponentsInChildren<ParticleSystem>(true))
                        {
                            var main = p.main; main.startDelay = 0; main.useUnscaledTime = false;
                            if (p.name != "Contact core") continue;
                            // A full-strength flash is visible on the first contact frame,
                            // including the entire hit-stop, rather than growing after it.
                            var size = p.sizeOverLifetime; size.enabled = true;
                            size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(
                                new Keyframe(0, 1.05f), new Keyframe(.12f, 1.2f), new Keyframe(1, .55f)));
                        }
                        AddHitShockwave(root, mesh, material, tint, heavy, false);
                        AddHitShockwave(root, mesh, material, tint, heavy, true);
                        FillParticleMaterialSlots(root);
                        string path = HitShockwaveAssets + "/" + root.name + ".prefab";
                        replacements[selected] = PrefabUtility.SaveAsPrefabAsset(root, path);
                        originalSources[path] = sourcePath;
                    }
                    finally { Object.DestroyImmediate(root); }
                }
                Undo.RecordObject(vfx, "Strengthen hits and add contact shockwaves");
                vfx.lightHit = replacements[vfx.lightHit]; vfx.heavyHit = replacements[vfx.heavyHit];
                if (vfx.shieldImpact) vfx.shieldImpact = replacements[vfx.shieldImpact];
                foreach (var variant in vfx.impactVariants)
                {
                    if (variant.light) variant.light = replacements[variant.light];
                    if (variant.heavy) variant.heavy = replacements[variant.heavy];
                    if (variant.stab) variant.stab = replacements[variant.stab];
                }
                vfx.lightContactScale = 1.4f; vfx.heavyContactScale = 1.55f;
                EditorUtility.SetDirty(vfx);
            });
            if (bankBefore != File.ReadAllText("Assets/Audio/Battle/BattleSfxBank.asset"))
                throw new Exception("Contact shockwaves changed shared contact timing.");
            File.WriteAllLines(manifest, originalSources.OrderBy(p => p.Key).Select(p => p.Key + "\t" + p.Value));
            File.WriteAllText(HitShockwaveReview + "/Installation.txt",
                "Light contacts +40%; heavy contacts +55%; full-strength core from birth; two expanding camera-facing contact rings per hit.\n" +
                "Shared timeline, source animation, ground effects, swings and global scale preserved. No new textures.\n");
        }

        static void CaptureBattleHitShockwaves(string stage)
        {
            Directory.CreateDirectory(HitShockwaveReview + "/" + stage);
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var rt = RenderTexture.GetTemporary(1280, 720, 24);
            var stamp = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var previousActive = RenderTexture.active;
            var report = new StringBuilder();
            BattleLightingRig lighting = null;
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                if (stage == "Before")
                {
                    File.Copy(SfxScene, HitShockwaveReview + "/BattleSceneBefore.unity.txt", true);
                    File.Copy(AssetDatabase.GetAssetPath(game.battleVfx.timeline), HitShockwaveReview + "/ContactBankBefore.asset.txt", true);
                    File.WriteAllLines(HitShockwaveReview + "/TexturesBefore.txt", AssetDatabase.GetDependencies(SfxScene, true)
                        .Where(p => AssetDatabase.LoadAssetAtPath<Texture>(p)).OrderBy(p => p));
                }
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>())
                    .Single(c => c.CompareTag("MainCamera"));
                camera.scene = scene; camera.targetTexture = rt;
                foreach (var canvas in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)))
                { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var fighter in fighters) fighter.Initialize();
                lighting = game.GetComponent<BattleLightingRig>(); lighting.InitializeRig(); game.uiManager.SetTurnNumber(1);
                var director = camera.GetComponent<FrankCinematicCamera>();
                foreach (var source in fighters)
                foreach (var move in source.heavyCombatMoves)
                {
                    foreach (var fighter in fighters) fighter.ResetCombat(); game.battleVfx.ResetForMatch();
                    var target = fighters.Single(f => f != source);
                    bool reverse = source == fighters[1];
                    source.Animator.transform.position = new Vector3(reverse ? .5f : -.5f, 0, 0);
                    target.Animator.transform.position = source.Animator.transform.position + Vector3.right * (reverse ? -move.attackRange : move.attackRange);
                    var cue = game.battleVfx.timeline.FindMove(move).cues.First(IsContactCue);
                    var effects = new Dictionary<GameObject, float>();
                    GameObject contactRoot = null;
                    Action<string, GameObject> observe = (id, root) =>
                    {
                        effects[root] = source.SourcePlayback.SampleTime;
                        if (id == "light_hit" || id == "heavy_hit")
                        {
                            contactRoot = root;
                            report.AppendLine($"BIRTH {source.name}/{move.moveName} {cue.seconds:F5}s: " +
                                string.Join(", ", root.GetComponentsInChildren<ParticleSystem>().Select(p => p.name + "=" + p.particleCount)));
                        }
                    };
                    game.battleVfx.EffectPlayed += observe;
                    try
                    {
                        if (!source.ExecuteAttack(move, target)) throw new Exception("Contact preview rejected attack.");
                        var pair = source.SourcePlayback;
                        pair.AdvanceTo(cue.seconds);
                        if (!contactRoot) throw new Exception("Missing contact preview.");
                        lighting.RefreshLighting(.22f); director.ResetView(); director.Apply(0, true);
                        Canvas.ForceUpdateCanvases();
                        foreach (float age in new[] { 0f, .05f, .12f, .22f })
                        {
                            // At age zero preserve the particles emitted synchronously by runtime.
                            // Later samples show only this contact, excluding stale swing letters.
                            foreach (var entry in effects)
                            {
                                entry.Key.SetActive(entry.Key == contactRoot);
                                if (entry.Key != contactRoot || age == 0) continue;
                                foreach (var p in entry.Key.GetComponentsInChildren<ParticleSystem>())
                                {
                                    p.useAutoRandomSeed = false; p.randomSeed = 321;
                                    p.Simulate(age, false, true, false);
                                }
                            }
                            CaptureStrongPose(camera, fighters, pair, stamp, rt);
                            var path = $"{HitShockwaveReview}/{stage}/{source.name}_{move.moveName}_{age:F2}.png";
                            File.WriteAllBytes(path, stamp.EncodeToPNG());
                        }
                        pair.Cancel();
                    }
                    finally { game.battleVfx.EffectPlayed -= observe; }
                }
                File.WriteAllText(HitShockwaveReview + "/" + stage + "/BirthParticles.txt", report.ToString());
            }
            finally
            {
                if (lighting) lighting.ShutdownRig();
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(stamp); RenderTexture.ReleaseTemporary(rt);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [MenuItem("Tools/Battle/VFX/Validate contact shockwaves")]
        public static void ValidateBattleHitShockwaves()
        {
            Directory.CreateDirectory(HitShockwaveReview);
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var report = new StringBuilder();
            int cases = 0, contacts = 0;
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var vfx = game.battleVfx;
                if (vfx.lightContactScale != 1.4f || vfx.heavyContactScale != 1.55f)
                    throw new Exception("Contact strength is not saved in the Battle scene.");
                foreach (var prefab in ContactPrefabs(vfx))
                {
                    var root = Object.Instantiate(prefab); SceneManager.MoveGameObjectToScene(root, scene);
                    try
                    {
                        var rings = root.GetComponentsInChildren<ParticleSystem>().Where(p => p.name == "Contact shockwave" || p.name == "Contact echo").ToArray();
                        if (rings.Length != 2) throw new Exception("Contact needs two rings: " + prefab.name);
                        foreach (var ring in rings)
                        {
                            var main = ring.main;
                            if (main.useUnscaledTime || main.startDelay.constantMax != 0 || main.startSpeed.constantMax != 0 ||
                                main.maxParticles != 1 || main.simulationSpace != ParticleSystemSimulationSpace.Local)
                                throw new Exception("Ring could detach, start late or ignore hit-stop: " + prefab.name);
                            var particle = new ParticleSystem.Particle[1];
                            ring.Simulate(.001f, false, true, false); ring.GetParticles(particle);
                            if (ring.particleCount != 1) throw new Exception("Ring has no particle at birth.");
                            float birth = particle[0].GetCurrentSize(ring);
                            ring.Simulate(.07f, false, true, false); ring.GetParticles(particle);
                            float expansion = particle[0].GetCurrentSize(ring);
                            if (expansion < birth * 2) throw new Exception("Ring does not expand visibly: " + prefab.name);
                            ring.Simulate(.36f, false, true, false);
                            if (ring.particleCount != 0) throw new Exception("Ring did not fade/end promptly.");
                        }
                        report.AppendLine("PASS " + prefab.name + ": two immediate rings expand and end in <=0.30 game seconds.");
                    }
                    finally { Object.DestroyImmediate(root); }
                }
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var source in fighters)
                foreach (var move in source.heavyCombatMoves)
                foreach (bool mirrored in new[] { false, true })
                foreach (bool lethal in new[] { false, true })
                foreach (bool skipped in new[] { false, true })
                {
                    foreach (var f in fighters) f.ResetCombat(); vfx.ResetForMatch();
                    var target = fighters.Single(f => f != source);
                    source.Animator.transform.position = Vector3.zero;
                    target.Animator.transform.position = Vector3.right * move.attackRange * (mirrored ? -1 : 1);
                    var profile = vfx.timeline.FindMove(move);
                    int emitted = 0;
                    Action<string, GameObject> observe = (id, root) =>
                    {
                        if (id != "light_hit" && id != "heavy_hit") return;
                        var pair = source.SourcePlayback;
                        var cue = profile.cues.Single(c => IsContactCue(c) && Mathf.Abs(c.seconds - pair.SampleTime) < .0001f);
                        var bone = target.Animator.GetBoneTransform(cue.contactBone);
                        if (!cue.hasContactPoint || !bone || Vector3.Distance(root.transform.position, bone.TransformPoint(cue.contactOffset)) > .016f)
                            throw new Exception("Hit burst left its exact body contact: " + source.name + "/" + move.moveName);
                        foreach (var p in root.GetComponentsInChildren<ParticleSystem>().Where(p =>
                            p.name == "Contact core" || p.name == "Contact shockwave" || p.name == "Contact echo"))
                            if (p.particleCount != 1 || !p.isPlaying)
                                throw new Exception("No immediate flash/ring on the contact frame: " + root.name + "/" + p.name);
                        emitted++; contacts++;
                    };
                    vfx.EffectPlayed += observe;
                    try
                    {
                        if (!source.ExecuteAttack(move, target, lethal)) throw new Exception("Contact validation rejected attack.");
                        var pair = source.SourcePlayback;
                        if (emitted != 0) throw new Exception("Contact flash fired before contact.");
                        if (skipped) pair.AdvanceTo(pair.Duration);
                        else foreach (var cue in profile.cues.Where(IsContactCue))
                        {
                            int before = emitted; pair.AdvanceTo(cue.seconds - .0001f);
                            if (before != emitted) throw new Exception("Hit flash fired early.");
                            pair.AdvanceTo(cue.seconds);
                            if (emitted != before + 1) throw new Exception("Missing contact flash.");
                            pair.AdvanceTo(cue.seconds); pair.AdvanceTo(cue.seconds - .2f);
                            if (emitted != before + 1) throw new Exception("Duplicate contact flash.");
                        }
                        if (emitted != profile.cues.Count(IsContactCue)) throw new Exception("Frame skip dropped contact flashes.");
                        int finished = emitted; pair.Cancel(); pair.AdvanceTo(pair.Duration);
                        if (emitted != finished || vfx.ActiveEffectCount != 0) throw new Exception("Cancelled combo left contact effects.");
                        if (vfx.PooledEffectCount > vfx.maxInstances + vfx.weaponTrails.maxTrails) throw new Exception("VFX pool grew beyond its cap.");
                    }
                    finally { vfx.EffectPlayed -= observe; source.SourcePlayback?.Cancel(); }
                    cases++;
                }
                string beforeBank = File.ReadAllText(HitShockwaveReview + "/ContactBankBefore.asset.txt");
                if (beforeBank != File.ReadAllText(AssetDatabase.GetAssetPath(vfx.timeline)))
                    throw new Exception("Previously calibrated contact timing changed.");
                var beforeTextures = File.ReadAllLines(HitShockwaveReview + "/TexturesBefore.txt");
                var addedTextures = AssetDatabase.GetDependencies(SfxScene, true).Where(p => AssetDatabase.LoadAssetAtPath<Texture>(p))
                    .Except(beforeTextures).ToArray();
                if (addedTextures.Length != 0) throw new Exception("Contact effects added textures: " + string.Join(", ", addedTextures));
                report.AppendLine($"ALL PASS: {cases} fighter/mirror/lethal/frame-skip cases; {contacts} exact contact births; no early or duplicate hit, all hits survive skipped frames; cancelled effects cleared; pool bounded; calibrated timing unchanged; 0 new textures.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                File.WriteAllText(HitShockwaveReview + "/Validation.txt", report.ToString());
            }
        }
    }
}
