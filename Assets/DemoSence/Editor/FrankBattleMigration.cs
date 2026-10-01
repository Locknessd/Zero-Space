using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void RemoveTrumpWeaponManagersFromBattle()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Single);
            try
            {
                var fighters = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<CharacterCombat>(true))
                    .Where(c => c != null && (c.name == "Mankey" || c.name == "Pepe"))
                    .ToArray();
                if (fighters.Length != 2) throw new InvalidOperationException("Expected Mankey and Pepe CharacterCombat objects.");
                foreach (var fighter in fighters)
                {
                    var manager = fighter.GetComponent<TrumpWeaponManager>();
                    if (manager) Object.DestroyImmediate(manager);
                }
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save BattleScene after removing TrumpWeaponManager.");
                File.WriteAllText("Temp/FrankRetarget/remove-trump-manager.txt", "PASS: removed TrumpWeaponManager from Mankey and Pepe; CharacterCombat owns weapon visibility.\n");
            }
            finally { }
        }

        public static void RepairWeaponMeshesFromSource()
        {
            var battle = EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Single);
            var source = EditorSceneManager.OpenPreviewScene("Assets/Selected/Frank_Damages/Demo/Frank_Damages.unity");
            try
            {
                var fighters = battle.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CharacterCombat>(true)).ToArray();
                if (fighters.Length != 2) throw new InvalidOperationException("Expected two fighters.");
                var sourceObjects = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                    .GroupBy(t => t.name)
                    .ToDictionary(g => g.Key, g => g.First().gameObject);

                Transform FindBone(Transform root, bool left)
                {
                    string hand = left ? "lefthand" : "righthand";
                    return root.GetComponentsInChildren<Transform>(true)
                        .FirstOrDefault(t => t.name.Replace(" ", "").Replace("_", "").ToLowerInvariant().Contains(hand));
                }

                bool IsOldWeaponObject(GameObject go)
                {
                    string n = go.name.ToLowerInvariant();
                    return n.Contains("weapon") || n.Contains("sword") || n.Contains("spear") ||
                           n.Contains("shield") || n.Contains("dagger") || n.Contains("katana") ||
                           n.Contains("assassin") || n.Contains("axe") || n.Contains("set (");
                }

                GameObject BuildStaticWeapon(GameObject original, Transform fighterRoot, string name, bool leftHand, bool dual)
                {
                    if (!original) return null;
                    var sourceRoot = source.GetRootGameObjects().FirstOrDefault(r => original.transform.IsChildOf(r.transform) || original == r);
                    if (!sourceRoot) sourceRoot = source.GetRootGameObjects().FirstOrDefault();
                    var sourceHand = FindBone(sourceRoot.transform, leftHand);
                    var targetHand = FindBone(fighterRoot, leftHand);
                    if (!targetHand) targetHand = fighterRoot;

                    var container = new GameObject(name);
                    SceneManager.MoveGameObjectToScene(container, battle);
                    container.transform.SetParent(targetHand, false);
                    container.SetActive(false);

                    foreach (var smr in original.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        if (!smr.sharedMesh) continue;
                        var baked = new Mesh { name = name + "_Mesh_" + smr.gameObject.name };
                        smr.BakeMesh(baked, true);
                        var part = new GameObject(smr.gameObject.name + "_Static");
                        SceneManager.MoveGameObjectToScene(part, battle);
                        part.transform.SetParent(container.transform, false);
                        if (sourceHand)
                        {
                            part.transform.localPosition = sourceHand.InverseTransformPoint(smr.transform.position);
                            part.transform.localRotation = Quaternion.Inverse(sourceHand.rotation) * smr.transform.rotation;
                            var handScale = sourceHand.lossyScale;
                            var sourceScale = smr.transform.lossyScale;
                            part.transform.localScale = new Vector3(
                                handScale.x == 0 ? 1 : sourceScale.x / handScale.x,
                                handScale.y == 0 ? 1 : sourceScale.y / handScale.y,
                                handScale.z == 0 ? 1 : sourceScale.z / handScale.z);
                        }
                        else
                        {
                            part.transform.localPosition = smr.transform.position;
                            part.transform.localRotation = smr.transform.rotation;
                            part.transform.localScale = smr.transform.lossyScale;
                        }
                        var filter = part.AddComponent<MeshFilter>();
                        filter.sharedMesh = baked;
                        var renderer = part.AddComponent<MeshRenderer>();
                        renderer.sharedMaterials = smr.sharedMaterials;
                        renderer.enabled = true;
                    }
                    foreach (var mr in original.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        var filterSource = mr.GetComponent<MeshFilter>();
                        if (!filterSource || !filterSource.sharedMesh) continue;
                        var part = new GameObject(mr.gameObject.name + "_Static");
                        SceneManager.MoveGameObjectToScene(part, battle);
                        part.transform.SetParent(container.transform, false);
                        part.transform.localPosition = sourceHand ? sourceHand.InverseTransformPoint(mr.transform.position) : mr.transform.position;
                        part.transform.localRotation = sourceHand ? Quaternion.Inverse(sourceHand.rotation) * mr.transform.rotation : mr.transform.rotation;
                        part.transform.localScale = mr.transform.lossyScale;
                        var filter = part.AddComponent<MeshFilter>();
                        filter.sharedMesh = filterSource.sharedMesh;
                        var renderer = part.AddComponent<MeshRenderer>();
                        renderer.sharedMaterials = mr.sharedMaterials;
                        renderer.enabled = true;
                    }
                    return container;
                }
                foreach (var fighter in fighters)
                {
                    var manager = fighter.GetComponent<TrumpWeaponManager>();
                    if (!manager) continue;
                    var oldSets = new[] { manager.warriorShieldSet, manager.greatSwordSet, manager.spearSet, manager.katanaSet,
                        manager.dualDaggersSet, manager.assassinSet, manager.twoHandedAxeSet }.Where(x => x != null).Distinct().ToArray();
                    foreach (var old in oldSets) Object.DestroyImmediate(old);
                    // Previous repair attempts left whole Trump weapon prefabs (including an Animator
                    // with the Trump avatar) under the fighter. Remove every matching descendant, not
                    // only direct children and not only the objects currently referenced by the manager.
                    var stale = fighter.transform.GetComponentsInChildren<Transform>(true)
                        .Where(t => t != fighter.transform && IsOldWeaponObject(t.gameObject))
                        .OrderByDescending(t => t.GetComponentsInParent<Transform>(true).Length)
                        .ToArray();
                    foreach (var child in stale)
                        if (child != null && child.gameObject != null) Object.DestroyImmediate(child.gameObject);

                    var root = fighter.transform;
                    var shield = BuildStaticWeapon(sourceObjects.TryGetValue("Shield", out var shieldSource) ? shieldSource : null, root, fighter.name + "_Shield_Visible", true, false);
                    var greatSword = BuildStaticWeapon(sourceObjects.TryGetValue("Frank_Damage@Damage_Critical_GreatSword", out var greatSource) ? greatSource : null, root, fighter.name + "_GreatSword_Visible", false, false);
                    var spear = BuildStaticWeapon(sourceObjects.TryGetValue("Frank_Damage@Damage_Critical_Spear", out var spearSource) ? spearSource : null, root, fighter.name + "_Spear_Visible", false, false);
                    var assassin = BuildStaticWeapon(sourceObjects.TryGetValue("Sword", out var swordSource) ? swordSource : null, root, fighter.name + "_Assassin_Visible", false, false);
                    var axe = BuildStaticWeapon(sourceObjects.TryGetValue("Frank_Damage@Damage_Critical_GreatSword", out var axeSource) ? axeSource : null, root, fighter.name + "_Axe_Visible", false, false);
                    var dual = new GameObject(fighter.name + "_DualDaggers_Visible");
                    SceneManager.MoveGameObjectToScene(dual, battle);
                    dual.transform.SetParent(root, false);
                    dual.SetActive(false);
                    var leftDagger = BuildStaticWeapon(sourceObjects.TryGetValue("Sword_L", out var leftSource) ? leftSource : null, root, fighter.name + "_DaggerL_Visible", true, true);
                    var rightDagger = BuildStaticWeapon(sourceObjects.TryGetValue("Sword_R", out var rightSource) ? rightSource : null, root, fighter.name + "_DaggerR_Visible", false, true);
                    if (leftDagger) leftDagger.transform.SetParent(dual.transform, true);
                    if (rightDagger) rightDagger.transform.SetParent(dual.transform, true);
                    var data = new SerializedObject(manager);
                    data.FindProperty("warriorShieldSet").objectReferenceValue = shield;
                    data.FindProperty("greatSwordSet").objectReferenceValue = greatSword;
                    data.FindProperty("spearSet").objectReferenceValue = spear;
                    data.FindProperty("dualDaggersSet").objectReferenceValue = dual;
                    data.FindProperty("assassinSet").objectReferenceValue = assassin;
                    data.FindProperty("twoHandedAxeSet").objectReferenceValue = axe;
                    data.ApplyModifiedPropertiesWithoutUndo();
                    manager.EquipWeapon(TrumpWeaponManager.WeaponType.None);
                }
                EditorSceneManager.MarkSceneDirty(battle);
                if (!EditorSceneManager.SaveScene(battle)) throw new IOException("Failed to save BattleScene weapon meshes.");
                File.WriteAllText("Temp/FrankRetarget/source-weapon-repair.txt", "PASS: rebuilt weapon sets as static meshes attached to Mankey/Pepe hand bones; removed stale Trump weapon rigs.\n");
            }
            finally { EditorSceneManager.ClosePreviewScene(source); }
        }

        public static void InspectSourceWeapons()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Selected/Frank_Damages/Demo/Frank_Damages.unity");
            var report = new StringBuilder();
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.name.Contains("Sword") && !t.name.Contains("Spear") && !t.name.Contains("Shield") && !t.name.Contains("Weapon")) continue;
                    var renderers = t.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length > 0)
                        report.AppendLine(t.name + " path=" + AnimationUtility.CalculateTransformPath(t, root.transform) + " renderers=" + renderers.Length);
                }
                File.WriteAllText("Temp/FrankRetarget/source-weapons.txt", report.ToString());
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        public static void DiagnoseHeavyWeapons()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
            var report = new StringBuilder();
            try
            {
                var manager = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { manager.leftCombat, manager.rightCombat };
                foreach (var fighter in fighters) fighter.Initialize();
                foreach (var fighter in fighters)
                {
                    report.AppendLine("FIGHTER " + fighter.name);
                    foreach (var move in fighter.heavyCombatMoves)
                    {
                        foreach (var f in fighters) f.ResetCombat();
                        bool started = fighter.ExecuteAttack(move, fighters.First(f => f != fighter));
                        string token = move.weapon == TrumpWeaponManager.WeaponType.WarriorShield ? "Shield" :
                            move.weapon == TrumpWeaponManager.WeaponType.GreatSword ? "GreatSword" :
                            move.weapon == TrumpWeaponManager.WeaponType.Spear ? "Spear" :
                            move.weapon == TrumpWeaponManager.WeaponType.Katana ? "Katana" :
                            move.weapon == TrumpWeaponManager.WeaponType.DualDaggers ? "DualDaggers" :
                            move.weapon == TrumpWeaponManager.WeaponType.Assassin ? "Assassin" :
                            move.weapon == TrumpWeaponManager.WeaponType.TwoHandedAxe ? "Axe" : null;
                        var selected = string.IsNullOrEmpty(token) ? null : fighter.transform.GetComponentsInChildren<Transform>(true)
                            .FirstOrDefault(t => t.name.EndsWith("_Visible", StringComparison.OrdinalIgnoreCase) &&
                                                 t.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)?.gameObject;
                        var renderers = selected ? selected.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
                        report.AppendLine(move.moveName + " started=" + started + " weapon=" + move.weapon +
                            " activeSelf=" + (selected && selected.activeSelf) + " active=" + (selected && selected.activeInHierarchy) +
                            " renderers=" + renderers.Length + " enabled=" + renderers.Count(r => r && r.enabled) +
                            " pos=" + (selected ? selected.transform.position.ToString() : "null"));
                    }
                }
                File.WriteAllText("Temp/FrankRetarget/heavy-weapons.txt", report.ToString());
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        public static void RepairCombatClipRootHeight()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
            var changed = new List<string>();
            try
            {
                var clips = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<CharacterCombat>(true))
                    .SelectMany(c => c.lightCombatMoves.Concat(c.heavyCombatMoves))
                    .Where(m => m != null)
                    .SelectMany(m => new[] { m.attackAnim, m.hitAnim, m.getUpAnim })
                    .Where(c => c != null)
                    .Distinct()
                    .ToArray();
                foreach (var group in clips.GroupBy(AssetDatabase.GetAssetPath))
                {
                    if (string.IsNullOrEmpty(group.Key)) continue;
                    var importer = AssetImporter.GetAtPath(group.Key) as ModelImporter;
                    if (importer == null) continue;
                    var animations = importer.clipAnimations;
                    if (animations == null || animations.Length == 0) animations = importer.defaultClipAnimations;
                    bool dirty = false;
                    foreach (var clip in animations)
                    {
                        if (!group.Any(c => c.name == clip.name)) continue;
                        clip.lockRootHeightY = true;
                        clip.keepOriginalPositionY = true;
                        clip.heightFromFeet = true;
                        clip.lockRootPositionXZ = true;
                        clip.keepOriginalPositionXZ = true;
                        dirty = true;
                        changed.Add(Path.GetFileName(group.Key) + ":" + clip.name);
                    }
                    if (dirty)
                    {
                        importer.clipAnimations = animations;
                        importer.SaveAndReimport();
                    }
                }
                File.WriteAllText("Temp/FrankRetarget/root-height-repair.txt", "PASS: locked root height for " + changed.Count + " combat clips.\n" + string.Join("\n", changed));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        public static void DiagnoseHeavyBattle()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
            var report = new StringBuilder();
            try
            {
                var manager = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { manager.leftCombat, manager.rightCombat };
                foreach (var fighter in fighters) fighter.Initialize();
                foreach (var fighter in fighters)
                {
                    report.AppendLine("FIGHTER " + fighter.name);
                    foreach (var move in fighter.heavyCombatMoves)
                    {
                        foreach (var f in fighters) f.ResetCombat();
                        bool started = fighter.ExecuteAttack(move, fighters.First(f => f != fighter));
                        fighter.Animator.Update(0.05f);
                        var clips = fighter.Animator.GetCurrentAnimatorClipInfo(0);
                        report.AppendLine(move.moveName + " started=" + started + " clips=" + string.Join(",", clips.Select(c => c.clip ? c.clip.name : "null")) + " attack=" + (move.attackAnim ? move.attackAnim.name : "null") + " human=" + (move.attackAnim && move.attackAnim.humanMotion));
                    }
                }
                File.WriteAllText("Temp/FrankRetarget/heavy-diagnosis.txt", report.ToString());
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        public static void RepairBattleWeapons()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Single);
            var fighters = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CharacterCombat>(true)).ToArray();
            if (fighters.Length != 2) throw new InvalidOperationException("Expected exactly two CharacterCombat components.");
            var source = fighters.FirstOrDefault(f => f.GetComponent<TrumpWeaponManager>()?.greatSwordSet != null);
            var target = fighters.FirstOrDefault(f => f != source);
            if (!source || !target) throw new InvalidOperationException("Could not identify weapon source and target.");
            var sourceWeapons = source.GetComponent<TrumpWeaponManager>();
            var targetWeapons = target.GetComponent<TrumpWeaponManager>();
            if (!sourceWeapons || !targetWeapons) throw new InvalidOperationException("Both fighters need TrumpWeaponManager.");

            GameObject CloneSet(GameObject original)
            {
                if (!original) return null;
                var clone = Object.Instantiate(original, target.transform, false);
                clone.name = original.name + " (" + target.name + ")";
                var targetBones = target.GetComponentsInChildren<Transform>(true)
                    .GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                foreach (var renderer in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    renderer.bones = renderer.bones.Select(b => b && targetBones.TryGetValue(b.name, out var mapped) ? mapped : null).ToArray();
                    if (renderer.rootBone && targetBones.TryGetValue(renderer.rootBone.name, out var rootBone)) renderer.rootBone = rootBone;
                }
                return clone;
            }

            var sourceData = new SerializedObject(sourceWeapons);
            var targetData = new SerializedObject(targetWeapons);
            foreach (var name in new[] { "warriorShieldSet", "greatSwordSet", "spearSet", "katanaSet", "dualDaggersSet", "assassinSet", "twoHandedAxeSet", "katanaSwordMesh", "katanaCaseMesh", "katanaDummyHandle" })
            {
                var sourceProp = sourceData.FindProperty(name);
                var targetProp = targetData.FindProperty(name);
                if (sourceProp == null || targetProp == null || sourceProp.objectReferenceValue == null) continue;
                var original = sourceProp.objectReferenceValue as GameObject;
                var clone = CloneSet(original);
                targetProp.objectReferenceValue = clone;
            }
            targetData.FindProperty("characterAnimator").objectReferenceValue = target.Animator;
            targetData.ApplyModifiedPropertiesWithoutUndo();
            sourceWeapons.EquipWeapon(TrumpWeaponManager.WeaponType.None);
            targetWeapons.EquipWeapon(TrumpWeaponManager.WeaponType.None);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save BattleScene after weapon repair.");
            File.WriteAllText("Temp/FrankRetarget/weapon-repair.txt", "PASS: cloned and remapped weapon sets from " + source.name + " to " + target.name + ".\n");
        }

        public static void CleanupBattleExtras()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Single);
            int removed = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var extra in root.GetComponentsInChildren<Transform>(true)
                    .Where(t => t.name == "Shield" || t.name == "Shield_Root")
                    .OrderByDescending(t => t.GetComponentsInChildren<Transform>(true).Length)
                    .ToArray())
                {
                    if (extra && extra != root.transform) { Object.DestroyImmediate(extra.gameObject); removed++; }
                }
                foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(r => r.bones == null || r.bones.Any(b => !b)).ToArray())
                {
                    if (renderer) { Object.DestroyImmediate(renderer.gameObject); removed++; }
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            File.WriteAllText("Temp/FrankRetarget/cleanup.txt", "Removed " + removed + " obsolete shield objects.\n");
        }

        public static void ValidateBattleModels()
        {
            var scene=EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
            var report=new StringBuilder();
            try
            {
                var roots=scene.GetRootGameObjects();
                var actors=roots.SelectMany(r=>r.GetComponentsInChildren<Animator>(true)).ToArray();
                var manager=roots.SelectMany(r=>r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters=new[]{manager.leftCombat,manager.rightCombat};
                if(actors.Length!=2 || fighters[0].name!="Mankey" || fighters[1].name!="Pepe")throw new Exception("Incorrect fighters");
                foreach(var sk in roots.SelectMany(r=>r.GetComponentsInChildren<SkinnedMeshRenderer>(true)))
                    if(sk.bones.Any(b=>!b))throw new Exception("Missing skin bone: "+sk.name);
                foreach(var fighter in fighters)
                {
                    if(!fighter.Initialize())throw new Exception("Initialization failed: "+fighter.name);
                    if(fighter.lightCombatMoves.Concat(fighter.heavyCombatMoves).Any(m=>m==null || !m.IsValid))throw new Exception("Invalid combat move");
                    report.AppendLine("PASS "+fighter.name+": valid avatar, skin bones, controller, light and heavy moves");
                }
                if(fighters[0].Animator.runtimeAnimatorController==fighters[1].Animator.runtimeAnimatorController)throw new Exception("Shared runtime overrides");
                var camera=roots.SelectMany(r=>r.GetComponentsInChildren<MortalKombatCamera>(true)).Single();
                if(camera.targetLeft!=fighters[0].transform || camera.targetRight!=fighters[1].transform)throw new Exception("Camera references incorrect");
                fighters[0].transform.position=Vector3.zero;
                fighters[1].transform.position=Vector3.right;
                for(int side=0;side<2;side++)
                foreach(var move in fighters[side].lightCombatMoves.Concat(fighters[side].heavyCombatMoves))
                {
                    foreach(var fighter in fighters)fighter.ResetCombat();
                    if(!fighters[side].ExecuteAttack(move,fighters[1-side]))throw new Exception("Cannot start "+move.moveName);
                    for(int tick=0;tick<1800 && fighters.Any(f=>f.IsBusy);tick++)
                        foreach(var fighter in fighters)fighter.Animator.Update(1f/60f);
                    if(fighters.Any(f=>f.IsBusy || !f.LastSequenceSucceeded))throw new Exception("Unfinished sequence: "+fighters[side].name+" "+move.moveName);
                    report.AppendLine("PASS "+fighters[side].name+" "+move.moveName+": attack/hit/getup completes");
                }
                report.AppendLine("PASS only two animators; isolated runtime overrides; camera references");
                File.WriteAllText("Temp/FrankRetarget/battle-validation.txt",report.ToString());
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        public static void MigrateBattle()
        {
            const string battlePath = "Assets/Scenes/BattleScene.unity";
            const string botPath = "Assets/Anim/AnimatorBot.controller";
            var battle = SceneManager.GetSceneByPath(battlePath);
            Directory.CreateDirectory("Temp/FrankRetarget/BattleBackup");
            foreach(var path in new[]{battlePath, botPath, "Assets/Anim/AnimatorOverride.overrideController"})
                File.Copy(path, "Temp/FrankRetarget/BattleBackup/" + Path.GetFileName(path), true);
            var bot = AssetDatabase.LoadAssetAtPath<AnimatorController>(botPath);
            if(bot.layers.Length == 0)
            {
                File.WriteAllText(botPath, File.ReadAllText("Assets/Resources/Combat/TripletCombat.controller").Replace("m_Name: TripletCombat", "m_Name: AnimatorBot"));
                AssetDatabase.ImportAsset(botPath, ImportAssetOptions.ForceUpdate);
                bot = AssetDatabase.LoadAssetAtPath<AnimatorController>(botPath);
            }
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>("Assets/Anim/AnimatorOverride.overrideController");
            controller.runtimeAnimatorController = bot;
            EditorUtility.SetDirty(controller);
            if(!battle.IsValid()) battle = EditorSceneManager.OpenScene(battlePath, OpenSceneMode.Additive);
            foreach (var temporary in battle.GetRootGameObjects().Where(r => r.name == "Mankey" || r.name == "Pepe").ToArray())
                Object.DestroyImmediate(temporary);
            var source = EditorSceneManager.OpenPreviewScene("Assets/DemoSence/Frank_Damages_Mankey_Pepe.unity");
            try
            {
                var manager = battle.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<GameManager>(true)).Single();
                var oldFighters = new[]{manager.leftCombat, manager.rightCombat};
                if(oldFighters.Any(c=>!c)) throw new InvalidOperationException("Missing battle fighter reference.");
                var obsolete = battle.GetRootGameObjects().Where(r=>r.name == "[Models_Showcase]" || r.GetComponent<CharacterCombat>()).ToArray();
                var replacements = new Dictionary<Object,Object>();
                var newFighters = new List<CharacterCombat>();
                for(int side=0; side<2; side++)
                {
                    string actorName = side==0 ? "Mankey" : "Pepe";
                    var original = source.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Animator>(true)).Single(a=>a.name==actorName);
                    var old = oldFighters[side];
                    var oldAnimator = old.GetComponent<Animator>();
                    var model = Object.Instantiate(original.gameObject);
                    model.name = actorName;
                    SceneManager.MoveGameObjectToScene(model,battle);
                    model.transform.SetPositionAndRotation(old.transform.position,old.transform.rotation);
                    model.transform.localScale = original.transform.lossyScale;
                    var animator = model.GetComponent<Animator>();
                    animator.enabled=true;
                    animator.runtimeAnimatorController=controller;
                    animator.applyRootMotion=false;
                    animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                    if(!animator.avatar || !animator.avatar.isValid || !animator.avatar.isHuman) throw new InvalidOperationException(actorName+" invalid humanoid avatar");
                    // The demo stores a posed skeleton. Restore the avatar reference pose before animation.
                    ReferencePose(model.transform, animator.avatar);
                    var combat = model.AddComponent<CharacterCombat>();
                    EditorUtility.CopySerialized(old,combat);
                    combat.combatController=controller;
                    var combatData=new SerializedObject(combat);
                    combatData.FindProperty("animator").objectReferenceValue=animator;
                    combatData.ApplyModifiedPropertiesWithoutUndo();
                    replacements[old]=combat;
                    replacements[old.gameObject]=model;
                    replacements[old.transform]=model.transform;
                    replacements[oldAnimator]=animator;
                    for(int bone=0;bone<(int)HumanBodyBones.LastBone;bone++)
                    {
                        var before=oldAnimator.GetBoneTransform((HumanBodyBones)bone);
                        var after=animator.GetBoneTransform((HumanBodyBones)bone);
                        if(before && after) { replacements[before]=after; replacements[before.gameObject]=after.gameObject; }
                    }
                    if(old.hitEffect)
                    {
                        var effect=Object.Instantiate(old.hitEffect,model.transform);
                        effect.transform.position=old.hitEffect.transform.position;
                        combat.hitEffect=effect;
                        replacements[old.hitEffect]=effect;
                    }
                    var oldWeapons=old.GetComponent<TrumpWeaponManager>();
                    if(oldWeapons)
                    {
                        var weapons=model.AddComponent<TrumpWeaponManager>();
                        EditorUtility.CopySerialized(oldWeapons,weapons);
                        replacements[oldWeapons]=weapons;
                        var weaponData=new SerializedObject(weapons);
                        var property=weaponData.GetIterator();
                        while(property.Next(true))
                        {
                            if(property.propertyType!=SerializedPropertyType.ObjectReference)continue;
                            if(property.objectReferenceValue is GameObject set && set.transform.IsChildOf(old.transform) && !replacements.ContainsKey(set))
                            {
                                var clone=Object.Instantiate(set);
                                clone.name=set.name;
                                clone.transform.SetParent(model.transform,true);
                                clone.transform.SetPositionAndRotation(set.transform.position,set.transform.rotation);
                                var before=set.GetComponentsInChildren<Transform>(true);
                                var after=clone.GetComponentsInChildren<Transform>(true);
                                for(int i=0;i<before.Length;i++) { replacements[before[i]]=after[i]; replacements[before[i].gameObject]=after[i].gameObject; }
                            }
                        }
                    }
                    newFighters.Add(combat);
                }
                int remapped=0;
                foreach(var component in battle.GetRootGameObjects().Where(r=>!obsolete.Contains(r)).SelectMany(r=>r.GetComponentsInChildren<Component>(true)))
                {
                    if(!component || component is Transform)continue;
                    var data=new SerializedObject(component);
                    var property=data.GetIterator();
                    while(property.Next(true))
                    {
                        if(property.propertyType!=SerializedPropertyType.ObjectReference || !property.objectReferenceValue)continue;
                        if(replacements.TryGetValue(property.objectReferenceValue,out var replacement))
                        { property.objectReferenceValue=replacement; remapped++; }
                        else
                        {
                            var referenced=property.objectReferenceValue as GameObject;
                            if(property.objectReferenceValue is Component c) referenced=c.gameObject;
                            if(referenced && obsolete.Contains(referenced.transform.root.gameObject))
                            {
                                // The old character may have weapon meshes whose SkinnedMeshRenderer
                                // bones do not exist on Mankey/Pepe. Those optional references must be
                                // cleared instead of leaving a dangling Unity object reference.
                                property.objectReferenceValue = null;
                                remapped++;
                            }
                        }
                    }
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                foreach(var root in obsolete)Object.DestroyImmediate(root);
                var overrides=new List<KeyValuePair<AnimationClip,AnimationClip>>();
                controller.GetOverrides(overrides);
                for(int i=0;i<overrides.Count;i++)
                {
                    var entry=overrides[i];
                    var clip=entry.Key.name=="IdlePlaceholder" ? newFighters[0].idleAnim : entry.Key.name=="WalkPlaceholder" ? newFighters[0].walkAnim : entry.Key.name=="VictoryPlaceholder" ? newFighters[0].victoryAnim : entry.Value;
                    overrides[i]=new KeyValuePair<AnimationClip,AnimationClip>(entry.Key,clip);
                }
                controller.ApplyOverrides(overrides);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
                var actors=battle.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Animator>(true)).ToArray();
                if(actors.Length!=2)throw new InvalidOperationException("Expected only two animators, found "+actors.Length);
                EditorSceneManager.MarkSceneDirty(battle);
                if(!EditorSceneManager.SaveScene(battle))throw new IOException("Failed to save BattleScene");
                File.WriteAllText("Temp/FrankRetarget/battle-migration.txt","PASS: Mankey left, Pepe right; only two animators; "+remapped+" references remapped; avatars valid; AnimatorBot and AnimatorOverride assigned.\n");
            }
            finally { EditorSceneManager.ClosePreviewScene(source); }
        }

        public static void InspectBattle()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
            var report = new StringBuilder();
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    report.AppendLine("ROOT " + root.name);
                    foreach (var a in root.GetComponentsInChildren<Animator>(true))
                        report.AppendLine(" ANIM " + AnimationUtility.CalculateTransformPath(a.transform, root.transform) + " active=" + a.gameObject.activeInHierarchy + " position=" + a.transform.position + " scale=" + a.transform.lossyScale);
                    foreach (var c in root.GetComponentsInChildren<CharacterCombat>(true))
                        report.AppendLine(" COMBAT " + c.name + " light=" + c.lightCombatMoves.Length + " heavy=" + c.heavyCombatMoves.Length + " idle=" + AssetDatabase.GetAssetPath(c.idleAnim));
                    foreach (var c in root.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c && !(c is TrumpWeaponManager)))
                    {
                        var so = new SerializedObject(c);
                        var p = so.GetIterator();
                        while(p.Next(true))
                            if(p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue is Component target && target.GetComponentInParent<Animator>() != null)
                                report.AppendLine(" REF " + c.GetType().Name + "." + p.propertyPath + " -> " + target.name + "/" + target.GetType().Name);
                    }
                }
                File.WriteAllText("Temp/FrankRetarget/battle-inspect.txt", report.ToString());
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
