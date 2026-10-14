using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static partial class BattleRosterSetup
{
    const string Root = "Assets/Characters/BattleRoster";
    const string Review = "GeneratedAssets/BattleRoster";
    const string BattlePath = "Assets/Scenes/BattleScene.unity";
    static readonly string[] Models = { "Cash_Monkey", "Neon_Nutcase", "Neon_Streetcat", "Ninja_Neko" };
    static string ModelPath(string id) => "Assets/Models/" + id + "/" + id + "_Unity.fbx";

    public static void Inspect()
    {
        Directory.CreateDirectory(Review);
        var report = new StringBuilder();
        foreach (var id in Models)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(id));
            report.AppendLine("MODEL " + id);
            if (!model)
                throw new Exception("Missing model " + id);
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                report.AppendLine(" BONE " + AnimationUtility.CalculateTransformPath(t, model.transform) +
                    " position=" + t.localPosition + " rotation=" + t.localEulerAngles + " scale=" + t.localScale);
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                report.AppendLine(" SKIN " + skin.name + " bounds=" + skin.bounds + " bones=" + skin.bones.Length +
                    " materials=" + string.Join(",", skin.sharedMaterials.Select(m => m ? m.name : "null")));
            var animator = model.GetComponent<Animator>();
            report.AppendLine(" AVATAR " + (animator ? animator.avatar : null));
        }
        var scene = EditorSceneManager.OpenPreviewScene(BattlePath);
        try
        {
            var game = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<GameManager>(true)).Single();
            foreach (var fighter in new[] { game.leftCombat, game.rightCombat })
            {
                var animator = fighter.GetComponent<Animator>();
                report.AppendLine("FIGHTER " + fighter.name + " scale=" + fighter.transform.localScale +
                    " avatar=" + AssetDatabase.GetAssetPath(animator.avatar));
                report.AppendLine(" COMPONENTS " + string.Join(",", fighter.GetComponents<Component>()
                    .Where(c => c).Select(c => c.GetType().Name)));
                foreach (Transform child in fighter.transform)
                    report.AppendLine(" CHILD " + child.name + " active=" + child.gameObject.activeSelf);
                report.AppendLine(" MOVES " + fighter.lightCombatMoves.Length + "/" + fighter.heavyCombatMoves.Length);
                foreach (var binding in fighter.weaponRig.bindings)
                    report.AppendLine(" DRIVER " + binding.weapon + " " + binding.driver.characterScale);
            }
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
        File.WriteAllText(Review + "/Inspection.txt", report.ToString());
    }
}

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void InspectBattleRoster() => BattleRosterSetup.Inspect();
    }
}
