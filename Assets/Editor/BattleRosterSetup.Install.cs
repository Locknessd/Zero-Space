using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static partial class BattleRosterSetup
{
    [MenuItem("Tools/Battle/Characters/Install character roster")]
    public static void Install()
    {
        Directory.CreateDirectory(Root + "/Prefabs");
        Directory.CreateDirectory(Root + "/Materials");
        AssetDatabase.Refresh();
        var previous = SceneManager.GetActiveScene();
        var scene = SceneManager.GetSceneByPath(BattlePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened)
            scene = EditorSceneManager.OpenScene(BattlePath, OpenSceneMode.Additive);
        string backup = Review + "/Backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        Directory.CreateDirectory(backup);
        EditorSceneManager.SaveScene(scene, backup + "/BattleScene.unity", true);
        File.WriteAllText(Review + "/BackupPath.txt", backup);
        try
        {
            var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
            var fighters = new[] { game.leftCombat, game.rightCombat };
            string catalogPath = Root + "/BattleCharacters.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<BattleCharacterCatalog>(catalogPath);
            if (!catalog)
            {
                catalog = ScriptableObject.CreateInstance<BattleCharacterCatalog>();
                AssetDatabase.CreateAsset(catalog, catalogPath);
            }
            var entries = new List<BattleCharacterCatalog.Entry>();
            entries.Add(OriginalEntry(fighters[0], "mankey", "Mankey"));
            entries.Add(OriginalEntry(fighters[1], "pepe", "Meme"));
            foreach (var id in Models)
                entries.Add(NewEntry(id));
            catalog.characters = entries.ToArray();
            EditorUtility.SetDirty(catalog);
            for (int i = 0; i < fighters.Length; i++)
            {
                var fighter = fighters[i];
                var appearance = fighter.GetComponent<BattleCharacterAppearance>();
                if (!appearance)
                {
                    appearance = Undo.AddComponent<BattleCharacterAppearance>(fighter.gameObject);
                    appearance.motionScale = fighter.transform.localScale;
                    appearance.referenceLegLength = entries[i].referenceLegLength * fighter.transform.localScale.x;
                    appearance.initialCharacterId = entries[i].id;
                    appearance.startupCharacterId = entries[i].id;
                    appearance.visualRoots = fighter.transform.Cast<Transform>()
                        .Where(t => !t.GetComponentInChildren<ParticleSystem>(true)).ToArray();
                }
                appearance.catalog = catalog;
                EditorUtility.SetDirty(appearance);
            }
            game.characterCatalog = catalog;
            var selector = game.GetComponent<BattleCharacterSelector>();
            if (!selector)
                selector = Undo.AddComponent<BattleCharacterSelector>(game.gameObject);
            selector.battle = game;
            EditorUtility.SetDirty(game);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            File.WriteAllText(Review + "/Install.txt", "Installed six interchangeable character appearances.\n" +
                "Original fighter objects, HP, move pools, VFX, audio and camera references retained.\n" + backup);
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded)
                SceneManager.SetActiveScene(previous);
            if (opened)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    static BattleCharacterCatalog.Entry OriginalEntry(CharacterCombat fighter, string id, string label)
    {
        string path = Root + "/Prefabs/" + id + ".prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!prefab)
        {
            var copy = Object.Instantiate(fighter.gameObject);
            copy.name = id;
            try
            {
                foreach (var c in copy.GetComponents<Component>())
                    if (!(c is Transform) && !(c is Animator))
                        Object.DestroyImmediate(c);
                foreach (Transform child in copy.transform.Cast<Transform>().ToArray())
                    if (child.GetComponentInChildren<ParticleSystem>(true))
                        Object.DestroyImmediate(child.gameObject);
                var animator = copy.GetComponent<Animator>();
                animator.runtimeAnimatorController = null;
                animator.enabled = true;
                copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                copy.transform.localScale = Vector3.one;
                RestoreReferencePose(animator);
                prefab = PrefabUtility.SaveAsPrefabAsset(copy, path);
            }
            finally
            {
                Object.DestroyImmediate(copy);
            }
        }
        return Entry(id, label, prefab);
    }

    static BattleCharacterCatalog.Entry NewEntry(string modelId)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(modelId));
        var source = model.GetComponent<Animator>();
        if (!source || !source.avatar || !source.avatar.isValid || !source.avatar.isHuman)
            throw new InvalidOperationException(modelId + " must be converted to Humanoid first.");
        var copy = Object.Instantiate(model);
        string id = modelId.ToLowerInvariant();
        string path = Root + "/Prefabs/" + id + ".prefab";
        try
        {
            copy.name = modelId;
            copy.transform.position = Vector3.zero;
            copy.transform.localScale = Vector3.one;
            ApplyModelMaterials(copy, modelId);
            RestoreReferencePose(copy.GetComponent<Animator>());
            BakeVisualRoot(copy, modelId);
            foreach (var skin in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skin.updateWhenOffscreen = true;
                skin.quality = SkinQuality.Bone4;
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(copy, path);
            return Entry(id, modelId.Replace('_', ' '), prefab);
        }
        finally
        {
            Object.DestroyImmediate(copy);
        }
    }

    static BattleCharacterCatalog.Entry Entry(string id, string label, GameObject prefab)
    {
        var copy = Object.Instantiate(prefab);
        try
        {
            var animator = copy.GetComponent<Animator>();
            RestoreReferencePose(animator);
            var upper = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var lower = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            var foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            return new BattleCharacterCatalog.Entry
            {
                id = id,
                displayName = label,
                prefab = prefab,
                referenceHeight = ReferenceBounds(animator).size.y,
                referenceLegLength = Vector3.Distance(upper.position, lower.position) +
                    Vector3.Distance(lower.position, foot.position)
            };
        }
        finally
        {
            Object.DestroyImmediate(copy);
        }
    }

    static void RestoreReferencePose(Animator animator)
    {
        var bones = animator.GetComponentsInChildren<Transform>(true)
            .GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
        foreach (var bone in animator.avatar.humanDescription.skeleton)
        {
            if (!bones.TryGetValue(bone.name, out var target) || target == animator.transform)
                continue;
            target.SetLocalPositionAndRotation(bone.position, bone.rotation);
            target.localScale = bone.scale;
        }
    }
}

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void InstallBattleRoster() => BattleRosterSetup.Install();
    }
}
