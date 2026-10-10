using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordStudy
    {
        public const string Output = "GeneratedAssets/CombatExpansion/GreatSwordStudy";
        const string Root = "Assets/DemoSence/GreatSwordExecution/";
        public const string Socket = "root/ik_hand_root/ik_hand_gun/ik_hand_r";
        public const string Weapon = "Assets/GreatSword_Animset/Model/Weapon/GreatSword_01.FBX";
        public const string AmbushRetreat = "Assets/CombatExpansion/Animations/GreatSword_Ambush_Retreat.anim";

        public static CombatTripletData MakeMove(CharacterCombat source, CharacterCombat target, int index,
            bool originalAmbush = false)
        {
            var library = AssetDatabase.LoadAssetAtPath<FrankGreatSwordLibrary>(Root + "GreatSwordExecutionLibrary.asset");
            var original = library.pairs[index];
            var receiverOffset = original.receiverOffset;
            // Battle avatars need closer entries than the source demo's mannequin spacing.
            if (index == 1)
                receiverOffset.z = 3.675f;
            else if (index == 2)
                receiverOffset.z = 3.2f;
            else if (index == 3)
                receiverOffset.z = 2.6f;
            var attack = index == 0 && !originalAmbush
                ? AssetDatabase.LoadAssetAtPath<AnimationClip>(AmbushRetreat) : original.attacker;
            if (!attack)
                throw new InvalidOperationException("Bake the authored Ambush retreat before building its move.");
            var attackDriver = AssetDatabase.LoadAssetAtPath<FrankTestDriver>(
                Root + "Drivers/" + source.name + "_GreatSword_Attack.prefab");
            var reactionDriver = AssetDatabase.LoadAssetAtPath<FrankTestDriver>(
                Root + "Drivers/" + target.name + "_GreatSword_Reaction.prefab");
            if (!attackDriver || !reactionDriver)
                throw new InvalidOperationException("Missing calibrated GreatSword source driver.");
            var getUp = Recovery(index);
            return new CombatTripletData
            {
                moveName = "Frank_" + original.sourceName,
                attackAnim = attack,
                hitAnim = original.receiver,
                getUpAnim = getUp,
                attackRange = Mathf.Abs(receiverOffset.z),
                weapon = TrumpWeaponManager.WeaponType.GreatSword,
                grounding = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>("Assets/CombatExpansion/Actions/Frank_" +
                    original.sourceName + "_Grounding.asset"),
                sourcePair = new FrankBattlePair
                {
                    attackerDriver = attackDriver,
                    receiverDriver = reactionDriver,
                    attack = attack,
                    reaction = original.receiver,
                    getUp = getUp,
                    recoveryBlendSeconds = .12f,
                    recoveryGrounding = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(
                        "Assets/CombatExpansion/Actions/Frank_GreatSword_" +
                        (index < 2 ? "Prone" : "Supine") + "Recovery_Grounding.asset"),
                    receiverOffset = receiverOffset,
                    receiverRotation = original.receiverRotation,
                    entryBlendSeconds = .12f,
                    maximumAlignmentError = .15f,
                    constrainDepthAfterSpacing = true,
                    pepeAttacks = source.name == "Pepe",
                    cameraKey = "execution/" + index,
                    showWeapon = true,
                    attackerWeaponPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Weapon),
                    attackerWeaponSocket = Socket
                }
            };
        }

        delegate void SamplePair(CharacterCombat source, CharacterCombat target, CombatTripletData move,
            FrankBattlePairPlayback pair, Camera camera, FrankCinematicCamera framing, int direction);

        static void EachPair(SamplePair sample, int[] directions, bool grounded = false)
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            var positioning = CombatPositioningController.Instance;
            var property = typeof(CombatPositioningController).GetProperty("Instance");
            var scene = EditorSceneManager.OpenPreviewScene(CombatExpansionInventory.Battle);
            try
            {
                property.SetValue(null, null);
                var roots = scene.GetRootGameObjects();
                var game = roots.SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var camera = roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true))
                    .First(c => c.CompareTag("MainCamera"));
                var framing = roots.SelectMany(r => r.GetComponentsInChildren<FrankCinematicCamera>(true)).First();
                camera.scene = scene;
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var fighter in fighters)
                {
                    fighter.battleSfx = null;
                    fighter.battleVfx = null;
                    fighter.hitEffect = null;
                    fighter.Initialize();
                }
                foreach (var source in fighters)
                for (int index = 0; index < 4; index++)
                foreach (int direction in directions)
                {
                    foreach (var fighter in fighters)
                        fighter.ResetCombat();
                    var target = fighters.Single(f => f != source);
                    var move = MakeMove(source, target, index);
                    if (grounded && !move.grounding)
                        throw new InvalidOperationException("Missing grounding for " + move.moveName);
                    if (!grounded) move.grounding = null;
                    source.Animator.transform.position = Vector3.left * direction * move.attackRange * .5f;
                    target.Animator.transform.position = Vector3.right * direction * move.attackRange * .5f;
                    if (!source.ExecuteAttack(move, target))
                        throw new InvalidOperationException("Could not begin " + move.moveName);
                    var pair = source.SourcePlayback;
                    try
                    {
                        sample(source, target, move, pair, camera, framing, direction);
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
    }
}
