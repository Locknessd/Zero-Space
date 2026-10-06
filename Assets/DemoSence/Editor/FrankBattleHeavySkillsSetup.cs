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
        const string SkillAssets = AccentAssets + "/Skills";

        static GameObject SkillBurst(string name, string source, float scale, float life)
        {
            var root = new GameObject(name);
            try
            {
                var copy = AddPackBurst(root, source, scale, 1.4f);
                foreach (var p in copy.GetComponentsInChildren<ParticleSystem>(true))
                {
                    SingleBurst(p, life);
                    var main = p.main; main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    var velocity = p.velocityOverLifetime; velocity.enabled = false;
                    var force = p.forceOverLifetime; force.enabled = false;
                    var collision = p.collision; collision.enabled = false;
                }
                foreach (var collider in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
                foreach (var body in copy.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(body);
                FillParticleMaterialSlots(root);
                return PrefabUtility.SaveAsPrefabAsset(root, SkillAssets + "/" + name + ".prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        // Bake one continuous recoil/fall from four authored source poses. The
        // victim starts upright, rather than inheriting an already fallen combo tail.
        static AnimationClip SkillReaction(CharacterCombat victim, CombatTripletData template, BattleSfxBank.Move profile)
        {
            string path = SkillAssets + "/SkillSingleKnockdown.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (!clip) { clip = new AnimationClip { name = "SkillSingleKnockdown" }; AssetDatabase.CreateAsset(clip, path); }
            clip.ClearCurves(); clip.frameRate = 60;
            var scene = EditorSceneManager.NewPreviewScene(); FrankTestActor actor = null;
            try
            {
                var target = Object.Instantiate(victim.Animator.gameObject); SceneManager.MoveGameObjectToScene(target, scene);
                target.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var root = new GameObject("Skill reaction pose bake"); SceneManager.MoveGameObjectToScene(root, scene);
                actor = root.AddComponent<FrankTestActor>(); actor.character = target.GetComponent<Animator>(); actor.characterName = victim.name;
                actor.ConfigureSource(template.sourcePair.receiverDriver, template.sourcePair.reaction, false, false, false);
                float hit = profile.cues.First(c => c.group.EndsWith("hit", StringComparison.Ordinal)).seconds;
                float fall = profile.cues.First(c => c.group == "body_fall").seconds;
                float[] sourceTimes = { Mathf.Max(0, hit - .2f), hit + .08f, fall - .12f, fall + .08f, fall + .08f };
                float[] times = { 0, .2f, .65f, .85f, 1.1f };
                var driver = actor.Pose.driver.transform;
                var bones = driver.GetComponentsInChildren<Transform>(true);
                var positions = new Vector3[bones.Length, times.Length]; var rotations = new Quaternion[bones.Length, times.Length];
                var hipsPoints = new Vector3[times.Length];
                for (int sample = 0; sample < times.Length; sample++)
                {
                    actor.Evaluate(sourceTimes[sample]);
                    hipsPoints[sample] = actor.Pose.sourceHips.position;
                    for (int bone = 0; bone < bones.Length; bone++)
                    { positions[bone,sample] = bones[bone].localPosition; rotations[bone,sample] = bones[bone].localRotation; }
                }
                float travel = hipsPoints.Max(p => new Vector2(p.x - hipsPoints[0].x, p.z - hipsPoints[0].z).magnitude);
                float gain = travel > .65f ? .65f / travel : 1;
                var motionRoots = new HashSet<Transform>();
                for (var bone = actor.Pose.sourceHips; bone; bone = bone == driver ? null : bone.parent) motionRoots.Add(bone);
                for (int bone = 0; bone < bones.Length; bone++)
                    if (motionRoots.Contains(bones[bone]))
                        for (int sample = 1; sample < times.Length; sample++)
                        {
                            var position = positions[bone,sample]; var origin = positions[bone,0];
                            position.x = origin.x + (position.x - origin.x) * gain;
                            position.z = origin.z + (position.z - origin.z) * gain;
                            positions[bone,sample] = position;
                        }
                for (int bone = 0; bone < bones.Length; bone++)
                {
                    int index = bone; string bonePath = PathOf(bones[bone], driver);
                    foreach (int axis in new[] { 0, 1, 2 })
                    {
                        int component = axis;
                        clip.SetCurve(bonePath, typeof(Transform), "m_LocalPosition." + "xyz"[axis],
                            new AnimationCurve(times.Select((time, sample) => new Keyframe(time, positions[index,sample][component])).ToArray()));
                    }
                    foreach (int axis in new[] { 0, 1, 2, 3 })
                    {
                        int component = axis;
                        clip.SetCurve(bonePath, typeof(Transform), "m_LocalRotation." + "xyzw"[axis],
                            new AnimationCurve(times.Select((time, sample) => new Keyframe(time, rotations[index,sample][component])).ToArray()));
                    }
                }
            }
            finally { if (actor) actor.Clear(); EditorSceneManager.ClosePreviewScene(scene); }
            clip.EnsureQuaternionContinuity();
            AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
            var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = false;
            settings.startTime = 0; settings.stopTime = 1.1f;
            AnimationUtility.SetAnimationClipSettings(clip, settings); EditorUtility.SetDirty(clip); return clip;
        }

        static FrankTestDriver SkillDriver(CharacterCombat fighter, BattleSkill skill, AnimationClip attack)
        {
            string model = skill == BattleSkill.Archer ? ArcherPack + "/Demo scene/Archer/Character.fbx" :
                WhiteMagePack + "/Demo scene/Demo scene files/Character.fbx";
            var holder = new GameObject("Battle skill calibration");
            try
            {
                var actor = holder.AddComponent<FrankTestActor>(); actor.character = fighter.Animator;
                actor.characterName = fighter.name;
                actor.attackDrivers = new[] { fighter.heavyCombatMoves[0].sourcePair.attackerDriver };
                var driver = MakeCalibratedDriver(actor, new[] { attack }, model, model, SkillAssets, skill.ToString(), false, out _);
                if (skill == BattleSkill.Archer)
                {
                    string path = AssetDatabase.GetAssetPath(driver); var root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        var pose = root.GetComponent<FrankPoseRetarget>();
                        var bow = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ArcherPack + "/Demo scene/Archer/Bow.fbx"));
                        bow.name = "Battle bow";
                        bow.transform.SetParent(HumanBone(pose.driver.transform, pose.sourceHumanAvatar, "LeftHand"), false);
                        // Grip authored in the Archer demo, kept in the source skeleton's space.
                        bow.transform.localPosition = new Vector3(0, -.023f, 0); bow.transform.localRotation = Quaternion.identity;
                        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BattleWeapons/Spear_Wood.mat");
                        if (!material) material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BattleWeapons/Katana_WrapAndScabbard.mat");
                        if (!material) throw new Exception("Missing battle bow material.");
                        pose.weaponRenderers = bow.GetComponentsInChildren<Renderer>(true);
                        foreach (var renderer in pose.weaponRenderers) renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => material).ToArray();
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                    driver = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<FrankTestDriver>();
                }
                return driver;
            }
            finally { Object.DestroyImmediate(holder); }
        }

        [MenuItem("Tools/Battle/Combat accents/7 Archer and White Mage heavy skills")]
        public static void InstallBattleHeavySkills()
        {
            PrepareCombatAccent(); Directory.CreateDirectory(SkillAssets + "/Drivers"); AssetDatabase.Refresh();
            var archer = new BattleVfxPlayer.SkillVariant { skill = BattleSkill.Archer,
                cast = SkillBurst("ArcherCharge", ArcherPack + "/Prefabs/Light arrow/EnergyAccumulation.prefab", .15f, .2f),
                projectile = SkillBurst("ArcherArrow", ArcherPack + "/Prefabs/Light arrow/Projectile.prefab", .22f, .24f),
                impact = SkillBurst("ArcherContact", ArcherPack + "/Prefabs/Light arrow/ArrowHit.prefab", .2f, .23f) };
            var mage = new BattleVfxPlayer.SkillVariant { skill = BattleSkill.WhiteMage,
                cast = SkillBurst("MageCharge", WhiteMagePack + "/Prefabs/Projectiles/Light Flash.prefab", .16f, .22f),
                projectile = SkillBurst("MageBolt", WhiteMagePack + "/Prefabs/Projectiles/Light projectile.prefab", .24f, .24f),
                impact = SkillBurst("MageContact", WhiteMagePack + "/Prefabs/Projectiles/Light hit.prefab", .22f, .25f) };
            var report = new StringBuilder();
            ComicScene((game, camera) =>
            {
                var bank = game.battleVfx.timeline;
                var template = game.leftCombat.heavyCombatMoves.Single(m => m.moveName == "Heavy_6");
                var templateProfile = bank.FindMove(template);
                var reaction = SkillReaction(game.rightCombat, template, templateProfile);
                float landingOffset = .65f;
                foreach (var skill in new[] { BattleSkill.Archer, BattleSkill.WhiteMage })
                {
                    var attack = AssetDatabase.LoadAssetAtPath<AnimationClip>(skill == BattleSkill.Archer ?
                        ArcherPack + "/Demo scene/Archer/Aim Recoil.anim" : WhiteMagePack + "/Demo scene/Demo scene files/FrontAttack.anim");
                    if (!attack || !reaction) throw new Exception("Missing skill animation.");
                    float shot = skill == BattleSkill.Archer ? .18f : .85f;
                    var cues = new List<BattleSfxBank.Cue> { new BattleSfxBank.Cue { seconds = .04f, group = "skill_cast" } };
                    for (int i = 0; i < (skill == BattleSkill.Archer ? 3 : 1); i++)
                    {
                        cues.Add(new BattleSfxBank.Cue { seconds = shot + i * .23f, group = "skill_shot" });
                        cues.Add(new BattleSfxBank.Cue { seconds = shot + i * .23f + .18f, group = "heavy_hit" });
                    }
                    cues.Add(new BattleSfxBank.Cue { seconds = shot + .18f + landingOffset, group = "body_fall", finalLanding = true });
                    cues.Add(new BattleSfxBank.Cue { seconds = shot + .2f + landingOffset, group = "comic_fall" });
                    string label = "Heavy_" + skill;
                    bank.moves = bank.moves.Where(m => m.label != label).Append(new BattleSfxBank.Move {
                        label = label, attack = attack, reaction = reaction, cues = cues.OrderBy(c => c.seconds).ToArray() }).ToArray();
                    foreach (var fighter in new[] { game.leftCombat, game.rightCombat })
                    {
                        fighter.Initialize();
                        var old = fighter.heavyCombatMoves.Single(m => m.moveName == "Heavy_6").sourcePair;
                        var pair = new FrankBattlePair { attackerDriver = SkillDriver(fighter, skill, attack), receiverDriver = old.receiverDriver,
                            attack = attack, reaction = reaction, getUp = old.getUp, reactionDelay = shot + .18f - .2f,
                            showWeapon = skill == BattleSkill.Archer, receiverOffset = new Vector3(0, 0, 2.4f),
                            receiverRotation = old.receiverRotation, pepeAttacks = fighter == game.rightCombat };
                        var move = new CombatTripletData { moveName = label, skill = skill, weapon = TrumpWeaponManager.WeaponType.None,
                            attackRange = 2.4f, attackAnim = attack, hitAnim = reaction, getUpAnim = pair.getUp, sourcePair = pair };
                        fighter.heavyCombatMoves = fighter.heavyCombatMoves.Where(m => m.skill != skill).Append(move).ToArray();
                        EditorUtility.SetDirty(fighter); report.AppendLine("PASS " + fighter.name + " " + label + ": calibrated native pack animation; " + (skill == BattleSkill.Archer ? "3" : "1") + " damage contacts.");
                    }
                }
                foreach (var id in new[] { "skill_cast", "skill_shot" })
                {
                    var original = bank.FindGroup(id == "skill_cast" ? "light_swing" : "thrust_swing");
                    bank.groups = bank.groups.Where(g => g.id != id).Append(new BattleSfxBank.Group {
                        id = id, clips = original.clips, clipGains = original.clipGains, volume = original.volume * .75f, pitch = original.pitch }).ToArray();
                }
                game.battleVfx.skillVariants = new[] { archer, mage }; game.battleVfx.maxInstances = 48;
                EditorUtility.SetDirty(bank); EditorUtility.SetDirty(game.battleVfx);
            });
            AssetDatabase.SaveAssets(); File.WriteAllText(AccentReview + "/Step7.txt", report.ToString());
        }
    }
}
