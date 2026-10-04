using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        internal const string AnimationTestReview = "GeneratedAssets/BattleAnimationTestReview";

        [MenuItem("Tools/Battle/Install animation test panel")]
        public static void InstallBattleAnimationTestPanel()
        {
            Directory.CreateDirectory(AnimationTestReview);
            if (!File.Exists(AnimationTestReview + "/BattleSceneBefore.unity.txt"))
                File.Copy(SfxScene, AnimationTestReview + "/BattleSceneBefore.unity.txt");
            var result = new StringBuilder();
            ComicScene((game, camera) =>
            {
                var panel = game.gameObject.scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<BattleAnimationTestPanel>(true)).SingleOrDefault();
                if (!panel)
                {
                    var root = new GameObject("Battle Animation Test");
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, game.gameObject.scene);
                    panel = Undo.AddComponent<BattleAnimationTestPanel>(root);
                }
                panel.battle = game;
                panel.openOnStart = false;
                panel.enableInReleaseBuilds = false;
                EditorUtility.SetDirty(panel);
                foreach (var fighter in new[] { game.leftCombat, game.rightCombat })
                {
                    if (!fighter) throw new Exception("Missing Battle fighter.");
                    var pools = new[] { fighter.lightCombatMoves, fighter.heavyCombatMoves };
                    for (int i = 0; i < pools.Length; i++)
                        foreach (var move in pools[i])
                        {
                            if (move == null || !move.IsValid || move.sourcePair != null && !move.sourcePair.Valid)
                                throw new Exception("Invalid animation test pair on " + fighter.name);
                            result.AppendLine(fighter.name + " / " + (i == 0 ? "Light" : "Heavy") + " / " + move.moveName +
                                " / " + move.attackAnim.name + " > " + move.hitAnim.name + " > " + (move.getUpAnim ? move.getUpAnim.name : "Hold"));
                        }
                }
            });
            File.WriteAllText(AnimationTestReview + "/InstalledPairs.txt", result.ToString());
        }

        [MenuItem("Tools/Battle/Check animation test panel in Play Mode")]
        public static void BattleAnimationTestPlayCheck() => FrankBattleAnimationTestPlayCheck.Start();

        public static void BattleAnimationTestCleanupCheck() => FrankBattleAnimationTestPlayCheck.Start(true);
    }
}
