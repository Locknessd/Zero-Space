using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void CaptureBattleRoster()
        {
            const string folder = "GeneratedAssets/BattleRoster/Previews";
            Directory.CreateDirectory(folder);
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>())
                    .Single(c => c.CompareTag("MainCamera"));
                game.GetComponent<BattleLightingRig>()?.InitializeRig();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var entry in game.characterCatalog.characters.Skip(2))
                {
                    foreach (var fighter in fighters)
                    {
                        fighter.ResetCombat();
                        fighter.GetComponent<BattleCharacterAppearance>().TryApply(entry.id);
                    }
                    game.uiManager.SetFighterName(MemeBattleUI.Side.Left, entry.displayName);
                    game.uiManager.SetFighterName(MemeBattleUI.Side.Right, entry.displayName);
                    camera.GetComponent<FrankCinematicCamera>().InvalidateCharacterGeometry();
                    CaptureBattleLook(scene, folder + "/" + entry.id + "_idle.png");
                    var move = game.leftCombat.heavyCombatMoves.First(m =>
                        m.weapon == TrumpWeaponManager.WeaponType.Assassin);
                    game.leftCombat.transform.position = Vector3.zero;
                    game.rightCombat.transform.position = Vector3.right * move.attackRange;
                    if (!game.leftCombat.ExecuteAttack(move, game.rightCombat))
                        throw new System.Exception("Cannot capture " + entry.id);
                    var pair = game.leftCombat.SourcePlayback;
                    pair.EvaluateAt(Mathf.Min(.6f, pair.Duration * .35f));
                    game.battleVfx.AdvanceSequence(pair, pair.SampleTime);
                    camera.GetComponent<FrankCinematicCamera>().InvalidateCharacterGeometry();
                    CaptureBattleLook(scene, folder + "/" + entry.id + "_assassin.png");
                    pair.Cancel();
                    game.battleVfx.ResetForMatch();
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
