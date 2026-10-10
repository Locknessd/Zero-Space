using System;
using System.IO;
using System.Linq;
using System.Text;
using FrankRetarget;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class BattleComboAuthoring
    {
        internal const string Battle = "Assets/Scenes/BattleScene.unity";
        internal const string Root = "Assets/DemoSence/InsaneCombos/";
        internal const string Report = "GeneratedAssets/AttackPresentationReview";

        internal static FrankComboLibrary Library =>
            AssetDatabase.LoadAssetAtPath<FrankComboLibrary>(Root + "ComboLibrary.asset");

        internal static CombatTripletData MakeMove(CharacterCombat source, CharacterCombat target,
            FrankComboLibrary.Pair data)
        {
            var recovery = source.heavyCombatMoves.First(m => m.moveName == "Heavy_Katana");
            var driver = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Drivers/" +
                source.name + "_InsaneCombos.prefab").GetComponent<FrankTestDriver>();
            var passive = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Drivers/" +
                target.name + "_InsaneCombos.prefab").GetComponent<FrankTestDriver>();
            Vector3 offset = data.receiverOffset;
            if (data.combo == 1)
                offset.z -= .08f;
            if (data.combo == 2)
                offset.z -= .12f;
            if (data.combo == 3)
                offset.z -= .06f;
            return new CombatTripletData
            {
                moveName = data.sourceName,
                attackAnim = data.attack,
                hitAnim = data.reaction,
                getUpAnim = recovery.getUpAnim,
                attackRange = offset.z,
                weapon = TrumpWeaponManager.WeaponType.GunSword,
                grounding = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(
                    Root + data.sourceName + "_Grounding.asset"),
                sourcePair = new FrankBattlePair
                {
                    attack = data.attack,
                    reaction = data.reaction,
                    attackerDriver = driver,
                    receiverDriver = passive,
                    receiverOffset = offset,
                    receiverRotation = Quaternion.Euler(0, 180, 0),
                    pepeAttacks = source.name == "Pepe",
                    showWeapon = true,
                    transferReceiverFingers = true,
                    entryBlendSeconds = .08f,
                    getUp = recovery.getUpAnim,
                    recoveryGrounding = recovery.sourcePair.recoveryGrounding,
                    receiverFloorLift = Array.Empty<float>()
                }
            };
        }

        public static void Inspect()
        {
            Directory.CreateDirectory(Report);
            var report = new StringBuilder();
            var csv = new StringBuilder("attacker,combo,seconds,swordX,swordY,swordZ,gunX,gunY,gunZ,headY,hipsY\n");
            foreach (var data in Library.pairs)
                report.AppendLine(
                    $"{data.sourceName} combo={data.combo} step={data.step} attack={data.attack.length:R} " +
                    $"reaction={data.reaction.length:R} match={data.sourceMatchTime:R} impact={data.impactTime:R} " +
                    $"ground={data.groundTime:R} offset={data.receiverOffset}");
            var previous = CombatPositioningController.Instance;
            var property = typeof(CombatPositioningController).GetProperty("Instance");
            var scene = EditorSceneManager.OpenPreviewScene(Battle);
            try
            {
                property.SetValue(null, null);
                var game = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var f in fighters)
                {
                    f.battleSfx = null;
                    f.battleVfx = null;
                    f.hitEffect = null;
                    f.Initialize();
                }
                foreach (var source in fighters)
                foreach (var data in Library.pairs.Where(p => p.step == 0))
                {
                    foreach (var f in fighters)
                        f.ResetCombat();
                    var target = fighters.Single(f => f != source);
                    var move = MakeMove(source, target, data);
                    source.transform.position = Vector3.zero;
                    target.transform.position = Vector3.right * move.attackRange;
                    if (!source.ExecuteAttack(move, target))
                        throw new InvalidOperationException("Rejected " + source.name + "/" + move.moveName);
                    var playback = source.SourcePlayback;
                    try
                    {
                        foreach (var renderer in playback.AttackerActor.Pose.weaponRenderers)
                            report.AppendLine(source.name + "/" + data.sourceName + " renderer=" + renderer.name +
                                " scale=" + renderer.transform.lossyScale + " materials=" +
                                string.Join(";", renderer.sharedMaterials.Select(m => m
                                    ? AssetDatabase.GetAssetPath(m) + ":" + m.shader.name : "MISSING")));
                        if (source.name == "Mankey")
                            BattleAttackReview.Capture(scene, playback,
                                Enumerable.Range(0, 20).Select(i => playback.Duration * i / 19).ToArray(),
                                Report + "/" + data.sourceName + "_source.png");
                        for (int i = 0; i <= Mathf.CeilToInt(playback.Duration * 60); i++)
                        {
                            float time = Mathf.Min(i / 60f, playback.Duration);
                            playback.EvaluateAt(time);
                            var sword = playback.AttackerActor.Pose.weaponRenderers
                                .First(r => r.name.IndexOf("sword", StringComparison.OrdinalIgnoreCase) >= 0);
                            var gun = playback.AttackerActor.Pose.weaponRenderers
                                .First(r => r.name.IndexOf("gun", StringComparison.OrdinalIgnoreCase) >= 0);
                            var a = sword.bounds.center;
                            var b = gun.bounds.center;
                            csv.AppendLine(
                                $"{source.name},{data.sourceName},{time:R},{a.x:R},{a.y:R},{a.z:R}," +
                                $"{b.x:R},{b.y:R},{b.z:R}," +
                                $"{target.Animator.GetBoneTransform(HumanBodyBones.Head).position.y:R}," +
                                $"{target.Animator.GetBoneTransform(HumanBodyBones.Hips).position.y:R}");
                        }
                    }
                    finally
                    {
                        playback.Cancel();
                    }
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                property.SetValue(null, previous);
                File.WriteAllText(Report + "/ComboInventory.txt", report.ToString());
                File.WriteAllText(Report + "/ComboPoses.csv", csv.ToString());
            }
        }
    }
}
