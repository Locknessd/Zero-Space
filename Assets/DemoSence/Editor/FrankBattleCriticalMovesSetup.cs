using System;
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
        const string CriticalReview = "GeneratedAssets/BattleCriticalMovesReview";
        static readonly TrumpWeaponManager.WeaponType[] CriticalWeapons = {
            TrumpWeaponManager.WeaponType.Katana, TrumpWeaponManager.WeaponType.Assassin };

        static CombatTripletData CriticalBattleMove(CharacterCombat fighter, FrankCombinationTester tester, TrumpWeaponManager.WeaponType weapon)
        {
            bool pepe = fighter.name == "Pepe";
            var attacker = pepe ? tester.pepe : tester.mankey;
            var receiver = pepe ? tester.mankey : tester.pepe;
            int index = Array.IndexOf(PairWeapons, weapon);
            var pair = new FrankBattlePair {
                attackerDriver = attacker.attackDrivers[index], receiverDriver = receiver.reactionDrivers[index],
                receiverOffset = tester.receiverOffsets[index], receiverRotation = tester.receiverRotations[index],
                pepeAttacks = pepe,
                getUp = fighter.heavyCombatMoves.Select(m => m.sourcePair?.getUp).FirstOrDefault(c => c) ?? DefaultBattleGetUp()
            };
            pair.attack = pair.attackerDriver.pose.sourceClips.First(c => !c.name.EndsWith("Hit2"));
            pair.reaction = pair.receiverDriver.pose.sourceClips.First(c => !c.name.EndsWith("Hit2"));
            if (!pair.Valid || !pair.getUp || Mathf.Abs(pair.receiverOffset.x) > .001f || Mathf.Abs(pair.receiverOffset.y) > .001f)
                throw new Exception("Invalid critical pair " + weapon);
            return new CombatTripletData {
                moveName = "Heavy_" + weapon, weapon = weapon, sourcePair = pair,
                attackAnim = pair.attack, hitAnim = pair.reaction, getUpAnim = pair.getUp,
                attackRange = Mathf.Abs(pair.receiverOffset.z)
            };
        }

        public static void SurveyBattleCriticalMoves()
        {
            Directory.CreateDirectory(CriticalReview);
            var source = EditorSceneManager.OpenPreviewScene(Output + "/Frank_Damages_Mankey_Pepe.unity");
            var battle = EditorSceneManager.OpenPreviewScene(SfxScene);
            try
            {
                var tester = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FrankCombinationTester>(true)).Single();
                var game = battle.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var attacker = game.leftCombat;
                var receiver = game.rightCombat;
                foreach (var fighter in new[] { attacker, receiver }) { fighter.battleSfx = null; fighter.battleVfx = null; fighter.hitEffect = null; }
                foreach (var canvas in battle.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true))) canvas.gameObject.SetActive(false);
                var camera = battle.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>()).Single(c => c.CompareTag("MainCamera"));
                camera.scene = battle;
                camera.orthographic = true;
                var target = new RenderTexture(400, 280, 24);
                var stamp = new Texture2D(400, 280, TextureFormat.RGB24, false);
                target.Create();
                camera.targetTexture = target;
                try
                {
                    foreach (var weapon in CriticalWeapons)
                    {
                        attacker.ResetCombat(); receiver.ResetCombat();
                        var move = CriticalBattleMove(attacker, tester, weapon);
                        attacker.transform.position = new Vector3(-.5f, 0, -.5f);
                        receiver.transform.position = attacker.transform.position + Vector3.right * move.attackRange;
                        if (!attacker.ExecuteAttack(move, receiver)) throw new Exception("Cannot survey " + weapon);
                        var player = attacker.SourcePlayback;
                        var csv = new StringBuilder("time,attackerX,attackerY,receiverX,receiverY,rightHandX,rightHandY,leftHandX,leftHandY,headX,headY\n");
                        for (int frame = 0; frame <= Mathf.CeilToInt(player.Duration * 30); frame++)
                        {
                            float time = Mathf.Min(player.Duration, frame / 30f);
                            player.EvaluateAt(time);
                            Vector3 a = attacker.Animator.GetBoneTransform(HumanBodyBones.Hips).position;
                            Vector3 b = receiver.Animator.GetBoneTransform(HumanBodyBones.Hips).position;
                            Vector3 rh = attacker.Animator.GetBoneTransform(HumanBodyBones.RightHand).position;
                            Vector3 lh = attacker.Animator.GetBoneTransform(HumanBodyBones.LeftHand).position;
                            Vector3 head = receiver.Animator.GetBoneTransform(HumanBodyBones.Head).position;
                            csv.AppendLine(FormattableString.Invariant($"{time:F4},{a.x:F4},{a.y:F4},{b.x:F4},{b.y:F4},{rh.x:F4},{rh.y:F4},{lh.x:F4},{lh.y:F4},{head.x:F4},{head.y:F4}"));
                        }
                        File.WriteAllText(CriticalReview + "/" + weapon + "-motion.csv", csv.ToString());
                        var sheet = new Texture2D(1600, 1400, TextureFormat.RGB24, false);
                        var times = new StringBuilder();
                        try
                        {
                            for (int frame = 0; frame < 20; frame++)
                            {
                                float time = player.Duration * frame / 19f;
                                player.EvaluateAt(time);
                                var bones = new[] { attacker, receiver }.SelectMany(f => new[] { HumanBodyBones.Hips, HumanBodyBones.Head,
                                    HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot }
                                    .Select(b => f.Animator.GetBoneTransform(b))).Where(t => t).ToArray();
                                var bounds = new Bounds(bones[0].position, Vector3.zero);
                                foreach (var bone in bones) bounds.Encapsulate(bone.position);
                                camera.transform.position = bounds.center + new Vector3(0, .2f, 10);
                                camera.transform.LookAt(bounds.center);
                                camera.orthographicSize = Mathf.Max(1.3f, bounds.extents.y + .6f, (bounds.extents.x + .8f) / camera.aspect);
                                camera.Render();
                                var previous = RenderTexture.active;
                                try { RenderTexture.active = target; stamp.ReadPixels(new Rect(0, 0, 400, 280), 0, 0); stamp.Apply(); }
                                finally { RenderTexture.active = previous; }
                                sheet.SetPixels(frame % 4 * 400, (4 - frame / 4) * 280, 400, 280, stamp.GetPixels());
                                times.AppendLine($"{frame}: {time:F3}s");
                            }
                            sheet.Apply();
                            File.WriteAllBytes(CriticalReview + "/" + weapon + "-motion.png", sheet.EncodeToPNG());
                            File.WriteAllText(CriticalReview + "/" + weapon + "-times.txt", times.ToString());
                        }
                        finally { Object.DestroyImmediate(sheet); player.Cancel(); }
                    }
                }
                finally { camera.targetTexture = null; Object.DestroyImmediate(target); Object.DestroyImmediate(stamp); }
            }
            finally { EditorSceneManager.ClosePreviewScene(battle); EditorSceneManager.ClosePreviewScene(source); }
        }

        [MenuItem("Tools/Battle/Add Katana and Assassin heavy pairs")]
        public static void InstallBattleCriticalMoves()
        {
            Directory.CreateDirectory(CriticalReview);
            if (!File.Exists(CriticalReview + "/BattleSceneBefore.unity.txt")) File.Copy(SfxScene, CriticalReview + "/BattleSceneBefore.unity.txt");
            if (!File.Exists(CriticalReview + "/BattleSfxBankBefore.asset.txt")) File.Copy(SfxBankPath, CriticalReview + "/BattleSfxBankBefore.asset.txt");
            var source = EditorSceneManager.OpenPreviewScene(Output + "/Frank_Damages_Mankey_Pepe.unity");
            var battle = SceneManager.GetSceneByPath(SfxScene);
            bool opened = !battle.IsValid() || !battle.isLoaded;
            if (opened) battle = EditorSceneManager.OpenScene(SfxScene, OpenSceneMode.Additive);
            try
            {
                var tester = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FrankCombinationTester>(true)).Single();
                var game = battle.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var bank = game.battleSfx.bank;
                var profiles = bank.moves.ToList();
                var report = new StringBuilder();
                Undo.RecordObjects(new Object[] { game.leftCombat, game.rightCombat, bank }, "Add Katana and Assassin heavy pairs");
                foreach (var fighter in new[] { game.leftCombat, game.rightCombat })
                {
                    var moves = fighter.heavyCombatMoves.ToList();
                    foreach (var weapon in CriticalWeapons)
                    {
                        var move = CriticalBattleMove(fighter, tester, weapon);
                        int index = moves.FindIndex(m => m.weapon == weapon);
                        if (index < 0) moves.Add(move); else moves[index] = move;
                        profiles.RemoveAll(p => p.attack == move.attackAnim && p.reaction == move.hitAnim);
                        profiles.Add(new BattleSfxBank.Move { label = move.moveName, attack = move.attackAnim,
                            reaction = move.hitAnim, cues = SfxCuesFor(move) });
                        report.AppendLine($"{fighter.name}: {move.moveName} = {move.attackAnim.name} / {move.hitAnim.name}; range={move.attackRange:F6}m; shared source clock; GetUp={move.getUpAnim.name}.");
                    }
                    fighter.heavyCombatMoves = moves.ToArray();
                    EditorUtility.SetDirty(fighter);
                }
                bank.moves = profiles.ToArray();
                EditorUtility.SetDirty(bank);
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(battle);
                if (!EditorSceneManager.SaveScene(battle)) throw new Exception("Could not save new heavy pairs.");
                report.AppendLine("Saved both fighters' heavy pools and shared SFX/VFX contact timelines.");
                File.WriteAllText(CriticalReview + "/Install.txt", report.ToString());
            }
            finally { if (opened) EditorSceneManager.CloseScene(battle, true); EditorSceneManager.ClosePreviewScene(source); }
        }
    }
}
