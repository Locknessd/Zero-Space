using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        static readonly TrumpWeaponManager.WeaponType[] PairWeapons = {
            TrumpWeaponManager.WeaponType.TwoHandedAxe, TrumpWeaponManager.WeaponType.Assassin,
            TrumpWeaponManager.WeaponType.DualDaggers, TrumpWeaponManager.WeaponType.GreatSword,
            TrumpWeaponManager.WeaponType.Katana, TrumpWeaponManager.WeaponType.Spear,
            TrumpWeaponManager.WeaponType.WarriorShield };

        static AnimationClip DefaultLightGetUp()
        {
            const string path = "Assets/InsaneGun_Sword_Set/Animation/Humanoid/rise_01.fbx";
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => c && c.name == "rise_01");
        }

        public static void InstallBattlePairs()
        {
            var source = EditorSceneManager.OpenPreviewScene("Assets/DemoSence/Frank_Damages_Mankey_Pepe.unity");
            try
            {
                var tester = source.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<FrankCombinationTester>(true)).Single();
                var battle = EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity");
                var game = battle.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<GameManager>(true)).Single();
                var report = new StringBuilder();
                foreach (var fighter in new[] { game.leftCombat, game.rightCombat })
                {
                    bool pepe = fighter.name == "Pepe";
                    var a = pepe ? tester.pepe : tester.mankey;
                    var b = pepe ? tester.mankey : tester.pepe;
                    foreach (var move in fighter.lightCombatMoves.Concat(fighter.heavyCombatMoves))
                    {
                        var data = new FrankBattlePair { pepeAttacks = pepe };
                        if (move.weapon != TrumpWeaponManager.WeaponType.None)
                        {
                            int index = Array.IndexOf(PairWeapons, move.weapon);
                            if (index < 0) throw new Exception("Unknown weapon " + move.weapon);
                            data.attackerDriver = a.attackDrivers[index];
                            data.receiverDriver = b.reactionDrivers[index];
                            data.attack = data.attackerDriver.pose.sourceClips.First(c => !c.name.EndsWith("Hit2"));
                            data.reaction = data.receiverDriver.pose.sourceClips.First(c => !c.name.EndsWith("Hit2"));
                            data.receiverOffset = tester.receiverOffsets[index];
                            data.receiverRotation = tester.receiverRotations[index];
                        }
                        else
                        {
                            int index = Array.FindIndex(tester.unarmedLibrary.pairs, p => p.attacker.name == move.attackAnim.name);
                            if (index < 0) throw new Exception("No source pair for " + move.attackAnim.name);
                            var p = tester.unarmedLibrary.pairs[index];
                            data.attackerDriver = a.unarmedDriver;
                            data.receiverDriver = b.unarmedDriver;
                            data.attack = p.attacker;
                            data.reaction = p.receiver;
                            data.receiverOffset = p.receiverOffset;
                            data.receiverRotation = p.receiverRotation;
                            data.spacing = tester.pairSpacing;
                            data.bodySpacing = tester.bodySpacing;
                            data.unarmedIndex = index;
                            data.getUp = DefaultLightGetUp();
                        }
                        if (!data.Valid) throw new Exception("Incomplete source pair " + move.moveName);
                        if (Mathf.Abs(data.receiverOffset.x) > .001f || Mathf.Abs(data.receiverOffset.y) > .001f)
                            throw new Exception("Source pair is not compatible with battle lane " + move.moveName);
                        move.sourcePair = data;
                        move.attackRange = Mathf.Abs(data.receiverOffset.z);
                        // Keep these fields descriptive; source playback uses the calibrated drivers.
                        move.attackAnim = data.attack;
                        move.hitAnim = data.reaction;
                        move.getUpAnim = null;
                        report.AppendLine($"{fighter.name} {move.moveName}: {data.attack.name} / {data.reaction.name}, offset={data.receiverOffset}, shared duration={Mathf.Max(data.attack.length, data.reaction.length)}");
                    }
                    EditorUtility.SetDirty(fighter);
                }
                EditorSceneManager.MarkSceneDirty(battle);
                EditorSceneManager.SaveScene(battle);
                File.WriteAllText("Temp/FrankRetarget/battle-pairs-install.txt", report.ToString());
            }
            finally { EditorSceneManager.ClosePreviewScene(source); }
        }

        public static void ValidateBattlePairs()
        {
            var battle = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
            var source = EditorSceneManager.OpenPreviewScene("Assets/DemoSence/Frank_Damages_Mankey_Pepe.unity");
            var report = new StringBuilder();
            try
            {
                var tester = source.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<FrankCombinationTester>(true)).Single();
                var game = battle.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var a in fighters)
                foreach (var move in a.lightCombatMoves.Concat(a.heavyCombatMoves))
                {
                    var b = fighters.First(f => f != a);
                    foreach (var f in fighters) f.ResetCombat();
                    float sign = a == game.leftCombat ? -1 : 1;
                    var rotation = Quaternion.LookRotation(Vector3.right * sign);
                    Vector3 origin = new Vector3(3, 0, -.5f);
                    a.transform.SetPositionAndRotation(origin, rotation);
                    b.transform.SetPositionAndRotation(origin + rotation * move.sourcePair.receiverOffset, rotation * move.sourcePair.receiverRotation);
                    tester.pepeAttacks = a.name == "Pepe";
                    tester.gunSword = tester.greatSword = false;
                    tester.unarmed = move.sourcePair.unarmedIndex >= 0;
                    tester.unarmedMotion = move.sourcePair.unarmedIndex;
                    tester.motion = Mathf.Max(0, Array.IndexOf(PairWeapons, move.weapon));
                    tester.attackerWeapon = tester.motion + 1;
                    tester.receiverWeapon = 0;
                    tester.Configure();
                    if (!a.ExecuteAttack(move, b)) throw new Exception("Start failed " + move.moveName);
                    var player = a.SourcePlayback;
                    float error = 0;
                    for (int frame = 0; frame <= 120; frame++)
                    {
                        float seconds = player.Duration * frame / 120f;
                        player.EvaluateAt(seconds);
                        tester.Seek(seconds);
                        foreach (var actors in new[] { new[] { player.AttackerActor, tester.Attacker }, new[] { player.ReceiverActor, tester.Receiver } })
                        {
                            var actual = actors[0].character;
                            var expected = actors[1].character;
                            for (int bone = 0; bone < (int)HumanBodyBones.LastBone; bone++)
                            {
                                var x = actual.GetBoneTransform((HumanBodyBones)bone);
                                var y = expected.GetBoneTransform((HumanBodyBones)bone);
                                if (x && y) error = Mathf.Max(error, Vector3.Distance(x.position, origin + rotation * y.position));
                            }
                            foreach (var r in actors[0].Pose.weaponRenderers)
                                if (actors[0] == player.AttackerActor && move.weapon != TrumpWeaponManager.WeaponType.None && !r.enabled)
                                    throw new Exception("Hidden source weapon");
                        }
                    }
                    report.AppendLine($"{a.name} {move.moveName}: 121 samples, max bone position error against source = {error:F6}m");
                    if (error > .015f) throw new Exception("Source pose mismatch " + a.name + " " + move.moveName);
                    player.Cancel();
                }
                report.AppendLine("PASS all 16 pairs match source under a rigid rotation into the battle lane.");
            }
            catch (Exception e) { report.AppendLine("FAIL " + e); throw; }
            finally
            {
                File.WriteAllText("Temp/FrankRetarget/battle-pairs-validation.txt", report.ToString());
                EditorSceneManager.ClosePreviewScene(battle);
                EditorSceneManager.ClosePreviewScene(source);
            }
        }
    }
}
