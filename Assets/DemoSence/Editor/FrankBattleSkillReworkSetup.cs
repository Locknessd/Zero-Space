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
        const string SkillReworkReview = "GeneratedAssets/BattleSkillReworkReview";
        const string SkillReworkAssets = "Assets/Vfx/Battle/SkillRework";
        static readonly string[] SkillReworkCandidates =
        {
            ArcherPack + "/Prefabs/Fire arrow/FireAccumulation.prefab",
            ArcherPack + "/Prefabs/Fire arrow/Fire projectile.prefab",
            ArcherPack + "/Prefabs/Fire arrow/FireArrow hit.prefab",
            "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Explosions/Variants/CFXR3 Fire Explosion A (no smoke).prefab",
            WhiteMagePack + "/Prefabs/Energy strike/Glowing orb.prefab",
            ArcherPack + "/Prefabs/Arrow AoE/ArrowAOE.prefab",
            WhiteMagePack + "/Prefabs/Light bomb/Light bomb cast.prefab",
            WhiteMagePack + "/Prefabs/Light ray/Light ray cast.prefab",
            WhiteMagePack + "/Prefabs/Energy strike/Energy strike.prefab",
            WhiteMagePack + "/Prefabs/Light bomb/Light bomb.prefab",
            WhiteMagePack + "/Prefabs/Magic rain/Magic rain.prefab",
            WhiteMagePack + "/Prefabs/Magic circle/Magic circle cast.prefab"
        };

        public static void SurveyReplacementSkillAnimations()
        {
            Directory.CreateDirectory(SkillReworkReview);
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
            var renderTarget = new RenderTexture(1280, 720, 24);
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).First(c => c.CompareTag("MainCamera"));
                camera.scene = scene; camera.targetTexture = renderTarget;
                game.leftCombat.Initialize(); game.rightCombat.Initialize();
                var candidates = new[] { ArcherPack + "/Demo scene/Archer/Draw Arrow.anim", WhiteMagePack + "/Demo scene/Demo scene files/HandUpFast.anim",
                    WhiteMagePack + "/Demo scene/Demo scene files/StuffUp.anim", WhiteMagePack + "/Demo scene/Demo scene files/Attack1.anim" };
                var diagnostic = new StringBuilder();
                for (int i = 0; i < candidates.Length; i++)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(candidates[i]);
                    var skill = i == 0 ? BattleSkill.Archer : BattleSkill.WhiteMage;
                    var original = game.leftCombat.heavyCombatMoves.Single(m => m.skill == skill);
                    var surveyDriver = AssetDatabase.LoadAssetAtPath<GameObject>(SkillAssets + "/Drivers/" + game.leftCombat.name + "_" + skill + ".prefab").GetComponent<FrankTestDriver>();
                    var pair = new FrankBattlePair { attackerDriver = surveyDriver, receiverDriver = original.sourcePair.receiverDriver,
                        attack = clip, reaction = original.sourcePair.reaction, receiverOffset = original.sourcePair.receiverOffset,
                        receiverRotation = original.sourcePair.receiverRotation, reactionDelay = 100, showWeapon = i == 0 };
                    var move = new CombatTripletData { moveName = "Survey", sourcePair = pair, attackRange = original.attackRange, skill = skill,
                        attackAnim = clip, hitAnim = pair.reaction };
                    game.leftCombat.ResetCombat(); game.rightCombat.ResetCombat();
                    game.leftCombat.Animator.transform.position = new Vector3(-.5f, 0, 0);
                    game.rightCombat.Animator.transform.position = new Vector3(1.9f, 0, 0);
                    if (!game.leftCombat.ExecuteAttack(move, game.rightCombat, false)) throw new Exception("Cannot survey " + clip.name);
                    var playback = game.leftCombat.SourcePlayback;
                    var sourceAnimator = playback.AttackerActor.Pose.driver;
                    diagnostic.AppendLine(clip.name + " animator=" + sourceAnimator.enabled + " avatar=" + sourceAnimator.avatar.name + " human=" + sourceAnimator.isHuman);
                    sourceAnimator.Rebind(); sourceAnimator.Update(0);
                    foreach (float fraction in new[] { .15f, .35f, .55f, .75f })
                    {
                        playback.EvaluateAt(clip.length * fraction);
                        diagnostic.AppendLine(fraction + " source L=" + sourceAnimator.GetBoneTransform(HumanBodyBones.LeftHand).position + " R=" + sourceAnimator.GetBoneTransform(HumanBodyBones.RightHand).position +
                            " hips=" + sourceAnimator.GetBoneTransform(HumanBodyBones.Hips).position);
                        game.battleVfx.ClearEffects();
                        var director = camera.GetComponent<FrankCinematicCamera>();
                        director.ResetView(); director.Apply(0, true);
                        CaptureBattleCamera(camera, SkillReworkReview + "/Pose" + i + "_" + fraction.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + ".png");
                    }
                    playback.Cancel();
                }
                File.WriteAllText(SkillReworkReview + "/AnimationDiagnostic.txt", diagnostic.ToString());
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); Object.DestroyImmediate(renderTarget); }
        }

        public static void SurveyReplacementBattleSkills()
        {
            Directory.CreateDirectory(SkillReworkReview);
            var scene = EditorSceneManager.NewPreviewScene();
            var cameraRoot = new GameObject("Skill replacement particle survey");
            SceneManager.MoveGameObjectToScene(cameraRoot, scene);
            var camera = cameraRoot.AddComponent<Camera>(); camera.enabled = false; camera.scene = scene;
            camera.orthographic = true; camera.orthographicSize = 1.65f;
            camera.transform.position = new Vector3(0, .8f, 4); camera.transform.LookAt(new Vector3(0, .4f, 0));
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.06f, .07f, .10f);
            var target = new RenderTexture(400, 300, 24); camera.targetTexture = target;
            var tile = new Texture2D(400, 300, TextureFormat.RGB24, false);
            var sheet = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var report = new StringBuilder();
            try
            {
                for (int i = 0; i < SkillReworkCandidates.Length; i++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SkillReworkCandidates[i]);
                    if (!prefab) throw new Exception("Missing replacement candidate " + SkillReworkCandidates[i]);
                    var root = Object.Instantiate(prefab); SceneManager.MoveGameObjectToScene(root, scene);
                    try
                    {
                        PrepareNewVfxCopy(root);
                        root.transform.position = Vector3.zero; root.transform.localScale *= .25f;
                        foreach (var collider in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
                        var materials = new List<Material>();
                        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                        {
                            renderer.sharedMaterials = renderer.sharedMaterials.Select(m =>
                            {
                                if (!m) return m;
                                var copy = new Material(m); copy.DisableKeyword("SOFTPARTICLES_ON"); materials.Add(copy); return copy;
                            }).ToArray();
                        }
                        foreach (float time in new[] { .25f, .55f, .9f })
                        {
                            foreach (var particles in root.GetComponentsInChildren<ParticleSystem>(true))
                            {
                                particles.useAutoRandomSeed = false; particles.randomSeed = 412;
                                particles.Simulate(time, false, true, true);
                            }
                            foreach (var animator in root.GetComponentsInChildren<Animator>())
                                if (animator.runtimeAnimatorController)
                                    animator.runtimeAnimatorController.animationClips.First().SampleAnimation(animator.gameObject, time);
                            camera.Render(); RenderTexture.active = target;
                            tile.ReadPixels(new Rect(0, 0, 400, 300), 0, 0); tile.Apply();
                            string filename = "Candidate" + i.ToString("D2") + "_" + time.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + ".png";
                            File.WriteAllBytes(SkillReworkReview + "/" + filename, tile.EncodeToPNG());
                            if (time == .55f) sheet.SetPixels(i % 4 * 400, (2 - i / 4) * 300, 400, 300, tile.GetPixels());
                        }
                        foreach (var material in materials) Object.DestroyImmediate(material);
                        report.AppendLine(i.ToString("D2") + ": " + SkillReworkCandidates[i]);
                    }
                    finally { Object.DestroyImmediate(root); }
                }
                sheet.Apply(); File.WriteAllBytes(SkillReworkReview + "/Candidates.png", sheet.EncodeToPNG());
                foreach (string folder in new[] { ArcherPack + "/Demo scene/Archer", WhiteMagePack + "/Demo scene/Demo scene files" })
                    foreach (string path in AssetDatabase.FindAssets("t:AnimationClip", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".anim")))
                    {
                        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                        report.AppendLine("ANIM " + clip.name + ": length=" + clip.length + " human=" + clip.humanMotion + " loop=" + clip.isLooping + " events=" +
                            string.Join(",", AnimationUtility.GetAnimationEvents(clip).Select(e => e.time + ":" + e.functionName)));
                    }
                File.WriteAllText(SkillReworkReview + "/Candidates.txt", report.ToString());
            }
            finally
            {
                RenderTexture.active = previous; Object.DestroyImmediate(tile); Object.DestroyImmediate(sheet);
                Object.DestroyImmediate(target); EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        // Preserve the pack's shapes, trails and particle motion. Only make emission
        // finite and bounded; the old installer flattened every layer to a .2s pop.
        static void AddReplacementLayer(GameObject parent, string path, float scale, float life, bool warm = false)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!asset) throw new Exception("Missing selected skill effect: " + path);
            var copy = Object.Instantiate(asset, parent.transform, false);
            PrepareNewVfxCopy(copy); copy.transform.localScale *= scale;
            // Pack prefabs include demo spawn heights. Battle already supplies the
            // wrist/chest world anchor, so those offsets must not be applied twice.
            copy.transform.localPosition = Vector3.zero;
            if (path.EndsWith("/Light bomb.prefab", StringComparison.Ordinal))
                copy.transform.localPosition = copy.transform.localRotation * new Vector3(0, -1.1f * scale, 0);
            if (path.EndsWith("/Energy strike.prefab", StringComparison.Ordinal))
            {
                // The source is a twelve-orb area attack. Keep one orb and the
                // central shockwave for a single, readable contact on a fighter.
                foreach (Transform child in copy.transform.Cast<Transform>().ToArray())
                    if (child.name.StartsWith("GlowingOrb", StringComparison.Ordinal) && child.name != "GlowingOrb0" || child.name == "Craters")
                        Object.DestroyImmediate(child.gameObject);
                    else child.localPosition = Vector3.zero;
                var center = copy.GetComponent<ParticleSystem>();
                if (center) { var shape = center.shape; shape.position = Vector3.zero; }
            }
            foreach (var c in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var b in copy.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(b);
            foreach (var animator in copy.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);
            foreach (var p in copy.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = p.main;
                main.loop = false; main.playOnAwake = false; main.duration = Mathf.Min(main.duration, life);
                main.startLifetime = new ParticleSystem.MinMaxCurve(Mathf.Clamp(main.startLifetime.constantMax, .18f, life));
                main.startDelay = Mathf.Min(main.startDelay.constantMax, .12f);
                main.simulationSpeed = 1; main.simulationSpace = ParticleSystemSimulationSpace.Local;
                var emission = p.emission;
                var bursts = new ParticleSystem.Burst[emission.burstCount]; emission.GetBursts(bursts);
                float rate = emission.rateOverTime.constantMax;
                emission.rateOverTime = 0; emission.rateOverDistance = 0;
                if (bursts.Length == 0 && rate > 0)
                    bursts = new[] { new ParticleSystem.Burst(0, (short)Mathf.Clamp(Mathf.CeilToInt(rate * Mathf.Min(life, .5f)), 4, 45)) };
                for (int i = 0; i < bursts.Length; i++)
                { bursts[i].cycleCount = 1; bursts[i].time = Mathf.Min(bursts[i].time, .12f); }
                emission.SetBursts(bursts); var sub = p.subEmitters; sub.enabled = false;
                // A cast must read while the hand winds up, rather than blink once.
                if (parent.name.EndsWith("Charge", StringComparison.Ordinal)) main.startLifetime = life;
                var renderer = p.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterials = renderer.sharedMaterials.Select(NewPackBattleMaterial).ToArray();
                if (warm)
                {
                    main.startColor = new Color(1, .52f, .12f, 1);
                    var color = p.colorOverLifetime;
                    if (color.enabled)
                    {
                        var old = color.color.gradient; var gradient = new Gradient();
                        gradient.SetKeys(old.colorKeys.Select(k => new GradientColorKey(new Color(1, .65f, .22f), k.time)).ToArray(), old.alphaKeys);
                        color.color = gradient;
                    }
                }
                if (parent.name.EndsWith("Charge", StringComparison.Ordinal) || parent.name.EndsWith("Impact", StringComparison.Ordinal))
                {
                    main.startSpeed = Mathf.Min(main.startSpeed.constantMax, parent.name.EndsWith("Charge", StringComparison.Ordinal) ? .5f : 4);
                    var velocity = p.velocityOverLifetime; velocity.enabled = false;
                    var force = p.forceOverLifetime; force.enabled = false;
                }
                if (p.name == "Darkness" || p.name == "FlareBlack" || p.name == "FireGround") p.gameObject.SetActive(false);
                if (parent.name == "MeteorImpact" && new[] { "Ring", "DarkWave", "Dissolve", "Light" }.Contains(p.name))
                    p.gameObject.SetActive(false);
            }
        }

        static GameObject ReplacementEffect(string name, Action<GameObject> layers)
        {
            var root = new GameObject(name);
            try { layers(root); FillParticleMaterialSlots(root); return PrefabUtility.SaveAsPrefabAsset(root, SkillReworkAssets + "/" + name + ".prefab"); }
            finally { Object.DestroyImmediate(root); }
        }

        public static void InspectReplacementEffectLayers()
        {
            var report = new StringBuilder();
            foreach (var name in new[] { "MeteorCharge", "MeteorArrow", "MeteorImpact", "CelestialCharge", "CelestialImpact" })
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(SkillReworkAssets + "/" + name + ".prefab");
                foreach (var p in root.GetComponentsInChildren<ParticleSystem>(true))
                    report.AppendLine(name + "/" + PathOf(p.transform, root.transform) + " active=" + p.gameObject.activeSelf + " pos=" + root.transform.InverseTransformPoint(p.transform.position) +
                        " size=" + p.main.startSize.constantMax + " shape=" + p.shape.shapeType + ":" + p.shape.position + " life=" + p.main.startLifetime.constantMax);
            }
            File.WriteAllText(SkillReworkReview + "/ParticleLayers.txt", report.ToString());
        }

        // Bake the evaluated source skeleton at 60 fps. Humanoid source clips remain
        // untouched; the private clip combines a real draw, charge and release.
        static AnimationClip BakeReplacementMotion(CharacterCombat fighter, FrankTestDriver driver, string name,
            AnimationClip[] sources, float[] durations, float[] starts, float[] ends, bool attacking)
        {
            string path = SkillReworkAssets + "/" + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (!clip) { clip = new AnimationClip { name = name }; AssetDatabase.CreateAsset(clip, path); }
            clip.ClearCurves(); clip.frameRate = 60;
            var scene = EditorSceneManager.NewPreviewScene(); FrankTestActor actor = null;
            try
            {
                var target = Object.Instantiate(fighter.Animator.gameObject); SceneManager.MoveGameObjectToScene(target, scene);
                target.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var holder = new GameObject(name + " motion bake"); SceneManager.MoveGameObjectToScene(holder, scene);
                actor = holder.AddComponent<FrankTestActor>(); actor.character = target.GetComponent<Animator>(); actor.characterName = fighter.name;
                float length = durations.Sum(); int frames = Mathf.CeilToInt(length * 60) + 1;
                var positions = new List<Vector3[]>(); var rotations = new List<Quaternion[]>();
                string[] paths = null; int previousSegment = -1;
                Vector3[] blendPositions = null; Quaternion[] blendRotations = null;
                for (int frame = 0; frame < frames; frame++)
                {
                    float time = Mathf.Min(frame / 60f, length), start = 0; int segment = 0;
                    while (segment < durations.Length - 1 && time >= start + durations[segment]) start += durations[segment++];
                    if (segment != previousSegment)
                    {
                        if (positions.Count > 0) { blendPositions = positions.Last(); blendRotations = rotations.Last(); }
                        actor.ConfigureSource(driver, sources[segment], attacking, false, false);
                        actor.Pose.driver.Rebind(); actor.Pose.driver.Update(0); previousSegment = segment;
                    }
                    float local = time - start;
                    actor.Evaluate(Mathf.Lerp(starts[segment], ends[segment], Mathf.Clamp01(local / durations[segment])));
                    var root = actor.Pose.driver.transform;
                    var bones = root.GetComponentsInChildren<Transform>(true);
                    if (paths == null) paths = bones.Select(b => PathOf(b, root)).ToArray();
                    var ps = bones.Select(b => b.localPosition).ToArray(); var rs = bones.Select(b => b.localRotation).ToArray();
                    if (segment > 0 && local < .08f)
                        for (int b = 0; b < bones.Length; b++)
                        { ps[b] = Vector3.Lerp(blendPositions[b], ps[b], local / .08f); rs[b] = Quaternion.Slerp(blendRotations[b], rs[b], local / .08f); }
                    positions.Add(ps); rotations.Add(rs);
                }
                for (int bone = 0; bone < paths.Length; bone++)
                {
                    for (int axis = 0; axis < 3; axis++)
                    {
                        var keys = new Keyframe[frames];
                        for (int f = 0; f < frames; f++) keys[f] = new Keyframe(Mathf.Min(f / 60f, length), positions[f][bone][axis]);
                        clip.SetCurve(paths[bone], typeof(Transform), "m_LocalPosition." + "xyz"[axis], new AnimationCurve(keys));
                    }
                    for (int axis = 0; axis < 4; axis++)
                    {
                        var keys = new Keyframe[frames];
                        for (int f = 0; f < frames; f++) keys[f] = new Keyframe(Mathf.Min(f / 60f, length), rotations[f][bone][axis]);
                        clip.SetCurve(paths[bone], typeof(Transform), "m_LocalRotation." + "xyzw"[axis], new AnimationCurve(keys));
                    }
                }
            }
            finally { if (actor) actor.Clear(); EditorSceneManager.ClosePreviewScene(scene); }
            clip.EnsureQuaternionContinuity(); AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
            var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = false;
            settings.startTime = 0; settings.stopTime = durations.Sum(); AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip); return clip;
        }

        static FrankTestDriver ReplacementDriver(FrankTestDriver old, CharacterCombat fighter, BattleSkill skill, AnimationClip attack)
        {
            string path = SkillReworkAssets + "/Drivers/" + fighter.name + "_" + skill + ".prefab";
            if (!File.Exists(path) && !AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(old), path)) throw new Exception("Cannot copy skill driver.");
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                root.name = fighter.name + "_" + skill;
                var pose = root.GetComponent<FrankPoseRetarget>();
                pose.sourceClips = new[] { attack };
                // These clips contain evaluated transform curves. Let the skeleton
                // play them as Generic; its separate Humanoid calibration avatar is
                // still used by the retargeter. Otherwise Mecanim applies a second
                // Humanoid body offset and pushes the caster into the ground.
                pose.driver.avatar = null;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<FrankTestDriver>();
        }

        [MenuItem("Tools/Battle/Replace Archer and White Mage skills")]
        public static void InstallReplacementBattleSkills()
        {
            Directory.CreateDirectory(SkillReworkReview); Directory.CreateDirectory(SkillReworkAssets + "/Drivers"); AssetDatabase.Refresh();
            if (!File.Exists(SkillReworkReview + "/BattleSceneBefore.unity.txt")) File.Copy(SfxScene, SkillReworkReview + "/BattleSceneBefore.unity.txt");
            var archer = new BattleVfxPlayer.SkillVariant { skill = BattleSkill.Archer, flightSeconds = .22f, chargeFollowSeconds = .8f,
                cast = ReplacementEffect("MeteorCharge", root => {
                    AddReplacementLayer(root, ArcherPack + "/Prefabs/Fire arrow/FireAccumulation.prefab", .24f, .85f);
                    AddReplacementLayer(root, WhiteMagePack + "/Prefabs/Light bomb/Light bomb.prefab", .10f, .8f, true); }),
                projectile = ReplacementEffect("MeteorArrow", root => {
                    AddReplacementLayer(root, ArcherPack + "/Prefabs/Fire arrow/Fire projectile.prefab", .24f, .45f);
                    AddReplacementLayer(root, WhiteMagePack + "/Prefabs/Light bomb/Light bomb.prefab", .15f, .45f, true); }),
                impact = ReplacementEffect("MeteorImpact", root => {
                    AddReplacementLayer(root, ArcherPack + "/Prefabs/Fire arrow/FireArrow hit.prefab", .25f, .48f);
                    AddReplacementLayer(root, "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Explosions/Variants/CFXR3 Fire Explosion A (no smoke).prefab", .075f, .4f); }) };
            var mage = new BattleVfxPlayer.SkillVariant { skill = BattleSkill.WhiteMage, descendingStrike = true, flightSeconds = .28f, chargeFollowSeconds = .6f,
                cast = ReplacementEffect("CelestialCharge", root => AddReplacementLayer(root, WhiteMagePack + "/Prefabs/Light bomb/Light bomb.prefab", .13f, .72f)),
                projectile = ReplacementEffect("CelestialComet", root => AddReplacementLayer(root, WhiteMagePack + "/Prefabs/Light bomb/Light bomb.prefab", .23f, .48f)),
                impact = ReplacementEffect("CelestialImpact", root => AddReplacementLayer(root, WhiteMagePack + "/Prefabs/Energy strike/Energy strike.prefab", .25f, .65f)) };
            var report = new StringBuilder();
            ComicScene((game, camera) =>
            {
                var bank = game.battleVfx.timeline;
                var template = game.leftCombat.heavyCombatMoves.Single(m => m.moveName == "Heavy_6");
                var profile = bank.FindMove(template);
                float lastHit = profile.cues.First(c => c.group == "heavy_hit").seconds;
                float ground = lastHit + .65f;
                // The combo's last hit already starts in mid-air. Its dedicated
                // upright single-hit reaction avoids moving the victim before hit.
                var reaction = AssetDatabase.LoadAssetAtPath<AnimationClip>(SkillAssets + "/SkillSingleKnockdown.anim");
                if (!reaction) throw new Exception("Missing upright skill reaction.");
                foreach (var skill in new[] { BattleSkill.Archer, BattleSkill.WhiteMage })
                {
                    var old = game.leftCombat.heavyCombatMoves.Single(m => m.skill == skill);
                    var sourceDriver = AssetDatabase.LoadAssetAtPath<GameObject>(SkillAssets + "/Drivers/" + game.leftCombat.name + "_" + skill + ".prefab").GetComponent<FrankTestDriver>();
                    AnimationClip attack;
                    if (skill == BattleSkill.Archer)
                    {
                        var draw = AssetDatabase.LoadAssetAtPath<AnimationClip>(ArcherPack + "/Demo scene/Archer/Draw Arrow.anim");
                        var aim = AssetDatabase.LoadAssetAtPath<AnimationClip>(ArcherPack + "/Demo scene/Archer/Aim Overdraw.anim");
                        var recoil = AssetDatabase.LoadAssetAtPath<AnimationClip>(ArcherPack + "/Demo scene/Archer/Aim Recoil.anim");
                        attack = BakeReplacementMotion(game.leftCombat, sourceDriver, "MeteorShot", new[] { draw, aim, recoil },
                            new[] { .75f, .18f, .7f }, new[] { 0f, .1f, 0f }, new[] { draw.length - .001f, .28f, recoil.length - .001f }, true);
                    }
                    else
                    {
                        var raise = AssetDatabase.LoadAssetAtPath<AnimationClip>(WhiteMagePack + "/Demo scene/Demo scene files/HandUpFast.anim");
                        attack = BakeReplacementMotion(game.leftCombat, sourceDriver, "CelestialSmite", new[] { raise },
                            new[] { 1.95f }, new[] { 0f }, new[] { raise.length - .001f }, true);
                    }
                    float shot = skill == BattleSkill.Archer ? 1.06f : .86f;
                    float hit = shot + (skill == BattleSkill.Archer ? archer.flightSeconds : mage.flightSeconds);
                    var cues = new[] {
                        new BattleSfxBank.Cue { seconds = skill == BattleSkill.Archer ? .25f : .35f, group = "skill_cast" },
                        new BattleSfxBank.Cue { seconds = shot, group = "skill_shot" },
                        new BattleSfxBank.Cue { seconds = hit, group = "heavy_hit" },
                        new BattleSfxBank.Cue { seconds = hit + ground - lastHit, group = "body_fall", finalLanding = true },
                        new BattleSfxBank.Cue { seconds = hit + ground - lastHit + .02f, group = "comic_fall" } };
                    string label = "Heavy_" + skill;
                    bank.moves = bank.moves.Where(m => m.label != label).Append(new BattleSfxBank.Move { label = label, attack = attack, reaction = reaction, cues = cues }).ToArray();
                    foreach (var fighter in new[] { game.leftCombat, game.rightCombat })
                    {
                        var move = fighter.heavyCombatMoves.Single(m => m.skill == skill);
                        move.attackAnim = move.sourcePair.attack = attack; move.hitAnim = move.sourcePair.reaction = reaction;
                        move.sourcePair.attackerDriver = ReplacementDriver(move.sourcePair.attackerDriver, fighter, skill, attack);
                        move.sourcePair.receiverDriver = fighter.heavyCombatMoves.Single(m => m.moveName == "Heavy_6").sourcePair.receiverDriver;
                        move.sourcePair.reactionDelay = hit - .2f;
                        EditorUtility.SetDirty(fighter);
                        report.AppendLine("PASS " + fighter.name + " " + label + ": " + attack.name + ", 1 contact @" + hit + ", landing @" + cues[3].seconds + ", authored 60fps motion.");
                    }
                }
                game.battleVfx.skillVariants = new[] { archer, mage };
                EditorUtility.SetDirty(game.battleVfx); EditorUtility.SetDirty(bank);
            });
            AssetDatabase.SaveAssets(); File.WriteAllText(SkillReworkReview + "/Installation.txt", report.ToString());
            AssetDatabase.DeleteAsset(SkillReworkAssets + "/SkillAuthoredKnockdown.anim");
        }
    }
}
