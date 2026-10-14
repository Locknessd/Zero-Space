using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void CaptureBattleRosterHeights()
        {
            const string folder = "GeneratedAssets/BattleRoster/HeightPreviews";
            Directory.CreateDirectory(folder);
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                game.GetComponent<BattleLightingRig>()?.InitializeRig();
                game.battleSfx.enabled = false;
                foreach (var fighter in fighters)
                    fighter.battleSfx = game.battleSfx;
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FrankCinematicCamera>())
                    .First(c => c.GetComponent<Camera>() && c.CompareTag("MainCamera"));
                foreach (var entry in game.characterCatalog.characters)
                {
                    foreach (var fighter in fighters)
                    {
                        fighter.ResetCombat();
                        fighter.GetComponent<BattleCharacterAppearance>().TryApply(entry.id);
                    }
                    fighters[0].transform.SetPositionAndRotation(new Vector3(-1.1f, 0, 0),
                        Quaternion.LookRotation(Vector3.right));
                    fighters[1].transform.SetPositionAndRotation(new Vector3(1.1f, 0, 0),
                        Quaternion.LookRotation(Vector3.left));
                    game.uiManager.SetFighterName(MemeBattleUI.Side.Left, entry.displayName);
                    game.uiManager.SetFighterName(MemeBattleUI.Side.Right, entry.displayName);
                    game.GetComponent<BattleCharacterSelector>()?.RefreshPortraits();
                    camera.InvalidateCharacterGeometry();
                    CaptureBattleLook(scene, folder + "/" + entry.id + "_idle.png");
                    var source = fighters[0];
                    var move = source.lightCombatMoves.Single(m => m.moveName == "SlapFace_Sequence1");
                    source.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    fighters[1].transform.position = Vector3.right * move.attackRange;
                    source.ExecuteAttack(move, fighters[1]);
                    var pair = source.SourcePlayback;
                    var cue = game.battleSfx.bank.FindMove(move).cues.First(c => c.group == "light_hit");
                    pair.EvaluateAt(cue.seconds);
                    game.battleVfx.AdvanceSequence(pair, pair.SampleTime);
                    camera.InvalidateCharacterGeometry();
                    CaptureBattleLook(scene, folder + "/" + entry.id + "_slap.png");
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
