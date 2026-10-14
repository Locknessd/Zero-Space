using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class BattleRosterSetup
{
    [MenuItem("Tools/Battle/Characters/Normalize character heights")]
    public static void NormalizeHeights()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(BattlePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened)
            scene = EditorSceneManager.OpenScene(BattlePath, OpenSceneMode.Additive);
        try
        {
            string backup = Review + "/BeforeUniformHeight_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(backup);
            EditorSceneManager.SaveScene(scene, backup + "/BattleScene.unity", true);
            var catalog = AssetDatabase.LoadAssetAtPath<BattleCharacterCatalog>(Root + "/BattleCharacters.asset");
            File.Copy(AssetDatabase.GetAssetPath(catalog), backup + "/BattleCharacters.asset");
            foreach (var entry in catalog.characters)
            {
                var copy = Object.Instantiate(entry.prefab);
                try
                {
                    var animator = copy.GetComponent<Animator>();
                    RestoreReferencePose(animator);
                    entry.referenceHeight = ReferenceBounds(animator).size.y;
                }
                finally
                {
                    Object.DestroyImmediate(copy);
                }
            }
            catalog.characterHeight = 2;
            catalog.sourceMotionScale = catalog.characterHeight /
                (catalog.Find("mankey").referenceHeight * .8033418f);
            EditorUtility.SetDirty(catalog);
            var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
            foreach (var fighter in new[] { game.leftCombat, game.rightCombat })
            {
                var appearance = fighter.GetComponent<BattleCharacterAppearance>();
                if (!appearance.TryApply(appearance.CurrentCharacterId))
                    throw new Exception("Cannot normalize " + fighter.name);
                foreach (var move in fighter.lightCombatMoves.Where(m =>
                    m.moveName.StartsWith("SlapFace_", StringComparison.Ordinal)))
                {
                    // Leave room for stylized heads while the palm follows its calibrated face anchor.
                    float spacing = Mathf.Max(1, move.sourcePair.receiverOffset.z);
                    move.attackRange += spacing - move.sourcePair.receiverOffset.z;
                    move.sourcePair.receiverOffset.z = spacing;
                }
                EditorUtility.SetDirty(fighter);
                EditorUtility.SetDirty(fighter.transform);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            InspectHeights();
        }
        finally
        {
            if (opened)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    public static void InspectHeights()
    {
        var report = new StringBuilder();
        var catalog = AssetDatabase.LoadAssetAtPath<BattleCharacterCatalog>(Root + "/BattleCharacters.asset");
        foreach (var entry in catalog.characters)
        {
            var copy = Object.Instantiate(entry.prefab);
            try
            {
                var animator = copy.GetComponent<Animator>();
                RestoreReferencePose(animator);
                var bounds = ReferenceBounds(animator);
                float head = animator.GetBoneTransform(HumanBodyBones.Head).position.y - bounds.min.y;
                float hips = animator.GetBoneTransform(HumanBodyBones.Hips).position.y - bounds.min.y;
                report.AppendLine(entry.id + " height=" + bounds.size.y + " floor=" + bounds.min.y +
                    " head=" + head + " hips=" + hips + " leg=" + entry.referenceLegLength);
            }
            finally
            {
                Object.DestroyImmediate(copy);
            }
        }
        File.WriteAllText(Review + "/Heights.txt", report.ToString());
    }

    static Bounds ReferenceBounds(Animator animator)
    {
        return BattlePresentationContactSetup.EvaluatedBounds(animator);
    }
}

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void InspectBattleRosterHeights() => BattleRosterSetup.InspectHeights();
        public static void NormalizeBattleRosterHeights() => BattleRosterSetup.NormalizeHeights();
    }
}
