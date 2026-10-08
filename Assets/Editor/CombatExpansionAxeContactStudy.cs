using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionAxeContactStudy
    {
        const string AxeGuid = "5b18d7ef2de2c7e45acf6b7c15d8a3ce";
        const string HitGuid = "932279eb1c22db24385eb04c03856eb1";
        const string Output = "GeneratedAssets/CombatExpansion/AxeContactStudy";

        public static CombatTripletData MakeMove(CharacterCombat source, CharacterCombat target, float range)
        {
            var attack = Clip(AxeGuid, 7400000);
            var reaction = Clip(HitGuid, 7400024);
            return new CombatTripletData
            {
                moveName = "Axe_Combo_01_Study",
                attackAnim = attack,
                hitAnim = reaction,
                attackRange = range,
                sourcePair = new FrankBattlePair
                {
                    attack = attack,
                    reaction = reaction,
                    attackerDriver = Driver(source, AxeGuid),
                    receiverDriver = Driver(target, HitGuid),
                    receiverOffset = Vector3.forward * range,
                    receiverRotation = Quaternion.Euler(0, 180, 0),
                    showWeapon = true,
                    reactionDelay = attack.length,
                    maximumAlignmentError = .15f
                }
            };
        }

        public static void Scan()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            Directory.CreateDirectory(Output);
            var positioning = CombatPositioningController.Instance;
            var property = typeof(CombatPositioningController).GetProperty("Instance");
            var scene = EditorSceneManager.OpenPreviewScene(CombatExpansionInventory.Battle);
            try
            {
                property.SetValue(null, null);
                var game = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var fighter in fighters)
                {
                    fighter.battleSfx = null;
                    fighter.battleVfx = null;
                    fighter.hitEffect = null;
                    fighter.Initialize();
                }
                foreach (var source in fighters)
                foreach (float range in new[] { .8f, 1.2f, 1.6f })
                {
                    var target = fighters.Single(f => f != source);
                    foreach (var fighter in fighters)
                        fighter.ResetCombat();
                    source.Animator.transform.position = Vector3.left * range * .5f;
                    target.Animator.transform.position = Vector3.right * range * .5f;
                    var move = MakeMove(source, target, range);
                    if (!source.ExecuteAttack(move, target))
                        throw new InvalidOperationException("Axe contact study could not start.");
                    var pair = source.SourcePlayback;
                    try
                    {
                        pair.ReceiverActor.Pose.transferFingers = true;
                        pair.EvaluateAt(0);
                        var probe = new BattlePresentationContactSetup.AxeContactProbe(target);
                        var rows = new List<string>
                        {
                            "seconds,hand,gap,region,contactX,contactY,contactZ,offsetX,offsetY,offsetZ,bladeX,bladeY,bladeZ"
                        };
                        for (int frame = 0; frame <= 72; frame++)
                        {
                            float seconds = frame / 60f;
                            pair.EvaluateAt(seconds);
                            foreach (bool left in new[] { true, false })
                            {
                                float gap = probe.Measure(pair.AttackerActor, left, out var point,
                                    out var bone, out var offset, out var blade);
                                string hand = left ? "Left" : "Right";
                                rows.Add(FormattableString.Invariant(
                                    $"{seconds:R},{hand},{gap:R},{bone},{point.x:R},{point.y:R},{point.z:R},{offset.x:R},{offset.y:R},{offset.z:R},{blade.x:R},{blade.y:R},{blade.z:R}"));
                            }
                        }
                        string name = source.name + "_" + Mathf.RoundToInt(range * 100) + "cm.csv";
                        File.WriteAllLines(Output + "/" + name, rows);
                    }
                    finally
                    {
                        pair.Cancel();
                    }
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                property.SetValue(null, positioning);
            }
        }

        static AnimationClip Clip(string guid, long localId)
        {
            return AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid))
                .OfType<AnimationClip>().Single(c => CombatExpansionInventory.Identity(c) == guid + ":" + localId);
        }

        static FrankTestDriver Driver(CharacterCombat fighter, string guid)
        {
            return AssetDatabase.LoadAssetAtPath<FrankTestDriver>(CombatExpansionHumanoidStudy.AssetsRoot +
                "/Drivers/" + fighter.name + "_" + guid + ".prefab");
        }
    }
}
