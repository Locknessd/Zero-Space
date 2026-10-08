using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSourceWeaponCheck
    {
        const string Root = "Assets/DemoSence/GreatSwordExecution/";
        const string Socket = "root/ik_hand_root/ik_hand_gun/ik_hand_r";
        const string Weapon = "Assets/GreatSword_Animset/Model/Weapon/GreatSword_01.FBX";
        static readonly string[] AttackIds =
        {
            "610fba6d346a64f4583237c3ef4f7e46:7400000",
            "139b0ae28ee0b4f4daa324a3b26584c3:7400000",
            "a56c5ae66a018d145b445cbf48c599cd:7400000",
            "9e99703246e7cbc4197d85121ad0d07c:7400000"
        };
        static readonly string[] ReactionIds =
        {
            "59a3af1cef1d25a4aa16dd3a87e12f17:7400000",
            "8a8cfa30410d4de4eaeee65cbe422897:7400000",
            "ace62d913ecb84646a9d95d649c77378:7400000",
            "6c5dd4f4f7bf14d4ba40eadba2ef8cc1:7400000"
        };

        public static void Validate()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            var positioning = CombatPositioningController.Instance;
            var property = typeof(CombatPositioningController).GetProperty("Instance");
            var scene = default(Scene);
            var log = new List<string>
            {
                "Isolated BattleScene sampling/cancellation only; no Play Mode, contacts or combat success claim."
            };
            int executed = 0;
            int passed = 0;
            string current = "setup";
            try
            {
                scene = EditorSceneManager.OpenPreviewScene(CombatExpansionInventory.Battle);
                property.SetValue(null, null);
                var game = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                Require(fighters.All(f => f) && fighters.Distinct().Count() == 2, "Missing distinct fighters.");
                foreach (var fighter in fighters)
                {
                    fighter.battleSfx = null;
                    fighter.battleVfx = null;
                    fighter.hitEffect = null;
                    Require(fighter.Initialize(), "Fighter initialization failed.");
                }
                var library = AssetDatabase.LoadAssetAtPath<FrankGreatSwordLibrary>(Root +
                    "GreatSwordExecutionLibrary.asset");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Weapon);
                Require(library && library.pairs?.Length == 4 && prefab, "Missing four-pair library or weapon.");
                for (int index = 0; index < library.pairs.Length; index++)
                {
                    var entry = library.pairs[index];
                    Require(entry != null && entry.attacker && entry.receiver, "Missing direct source clips.");
                    Require(CombatExpansionInventory.Identity(entry.attacker) == AttackIds[index] &&
                        CombatExpansionInventory.Identity(entry.receiver) == ReactionIds[index],
                        "Unexpected source clip identities at pair " + index);
                    foreach (var source in fighters)
                    foreach (int direction in new[] { 1, -1 })
                    foreach (string mode in new[] { "injected", "null-prefab", "invalid-socket" })
                    {
                        var target = fighters.Single(f => f != source);
                        current = $"pair={index} sourceId={source.GetEntityId()} " +
                            $"targetId={target.GetEntityId()} direction={direction} mode={mode}";
                        executed++;
                        string detail = RunCase(source, target, entry, direction, mode, prefab);
                        passed++;
                        log.Add("PASS " + current + "; " + detail);
                    }
                }
            }
            catch (Exception error)
            {
                log.Add("FAIL " + current + ": " + error);
                throw;
            }
            finally
            {
                try
                {
                    if (scene.IsValid())
                        EditorSceneManager.ClosePreviewScene(scene);
                }
                finally
                {
                    property.SetValue(null, positioning);
                    log.Add($"Executed cases={executed}; passed={passed}; failed={executed - passed}.");
                    Directory.CreateDirectory(CombatExpansionInventory.Output);
                    File.WriteAllLines(CombatExpansionInventory.Output + "/SourceWeaponValidation.txt", log);
                }
            }
        }

        static CombatTripletData Move(CharacterCombat source, CharacterCombat target,
            FrankGreatSwordLibrary.Pair entry, GameObject prefab, string socket)
        {
            return new CombatTripletData
            {
                moveName = "SourceWeaponValidation",
                attackAnim = entry.attacker,
                hitAnim = entry.receiver,
                attackRange = Mathf.Abs(entry.receiverOffset.z),
                sourcePair = new FrankBattlePair
                {
                    attackerDriver = Driver(source, "Attack"),
                    receiverDriver = Driver(target, "Reaction"),
                    attack = entry.attacker,
                    reaction = entry.receiver,
                    receiverOffset = entry.receiverOffset,
                    receiverRotation = entry.receiverRotation,
                    entryBlendSeconds = .12f,
                    constrainDepthAfterSpacing = true,
                    maximumAlignmentError = .15f,
                    showWeapon = true,
                    attackerWeaponPrefab = prefab,
                    attackerWeaponSocket = socket
                }
            };
        }

        static FrankTestDriver Driver(CharacterCombat fighter, string role)
        {
            var drivers = new[] { "Mankey", "Pepe" }
                .Select(name => AssetDatabase.LoadAssetAtPath<FrankTestDriver>(Root +
                    "Drivers/" + name + "_GreatSword_" + role + ".prefab")).ToArray();
            Require(drivers.All(d => d && d.pose && d.pose.character), "Missing source driver calibration.");
            return drivers.Single(d => d.pose.character.avatar == fighter.Animator.avatar);
        }

        static string RunCase(CharacterCombat source, CharacterCombat target,
            FrankGreatSwordLibrary.Pair entry, int direction, string mode, GameObject prefab)
        {
            source.ResetCombat();
            target.ResetCombat();
            float range = Mathf.Abs(entry.receiverOffset.z);
            Require(range > 0 && float.IsFinite(range), "Invalid authored range.");
            source.Animator.transform.position = Vector3.left * direction * range * .5f;
            target.Animator.transform.position = Vector3.right * direction * range * .5f;
            bool invalid = mode == "invalid-socket";
            var move = Move(source, target, entry, mode == "null-prefab" ? null : prefab,
                invalid ? Socket + "/__missing_source_weapon_socket__" : Socket);
            if (mode == "injected")
            {
                move.grounding = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(
                    "Assets/CombatExpansion/Actions/Frank_" + entry.sourceName + "_Grounding.asset");
                Require(move.grounding, "Grounded source weapon validation needs the baked pair track.");
            }
            var equipment = new EquipmentSnapshot(source, target);
            var previousActors = SceneActors(source.gameObject.scene);
            var playback = source.GetComponent<FrankBattlePairPlayback>();
            FrankTestActor attacker = null;
            FrankTestActor receiver = null;
            Renderer[] tracked = Array.Empty<Renderer>();
            string detail = "";
            try
            {
                if (invalid)
                {
                    equipment.AcquireExternalOwners();
                    bool threw = false;
                    try
                    {
                        source.ExecuteAttack(move, target);
                    }
                    catch (ArgumentException error) when (error.ParamName == "socketPath")
                    {
                        threw = true;
                    }
                    playback = source.GetComponent<FrankBattlePairPlayback>();
                    Require(threw, "Invalid socket did not throw its expected argument exception.");
                    RequireReleased(source, target, playback);
                    Require(SceneActors(source.gameObject.scene).SetEquals(previousActors),
                        "Invalid socket leaked a source actor.");
                    equipment.RequireExternalOwners();
                    equipment.ReleaseExternalOwners();
                    equipment.RequireRestored();
                    detail = "expected socket exception; actor rollback; base rig/renderer state restored";
                }
                else
                {
                    Require(source.ExecuteAttack(move, target), "Source pair refused to begin.");
                    playback = source.SourcePlayback;
                    Require(playback && playback.Playing && target.SourcePlayback == playback &&
                        source.IsBusy && target.IsBusy, "Both participants did not acquire pair ownership.");
                    attacker = playback.AttackerActor;
                    receiver = playback.ReceiverActor;
                    Require(attacker.character == source.Animator && receiver.character == target.Animator,
                        "Pair bound the wrong visible actors.");
                    tracked = attacker.Pose.weaponRenderers.ToArray();
                    detail = VerifySamples(playback, move.sourcePair, equipment, mode == "injected");
                    playback.Cancel();
                    Require(!attacker && !receiver && tracked.All(r => !r),
                        "Cancel retained actors or weapon renderers.");
                    RequireReleased(source, target, playback);
                    Require(SceneActors(source.gameObject.scene).SetEquals(previousActors), "Cancel leaked an actor.");
                    equipment.RequireRestored();
                    detail += "; Cancel destroyed actors/props and restored base rig/renderer state";
                }
                return detail + "; " + equipment.Report;
            }
            finally
            {
                if (playback)
                    playback.Cancel();
                equipment.ReleaseExternalOwners();
            }
        }

        static HashSet<FrankTestActor> SceneActors(Scene scene) => scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<FrankTestActor>(true))
            .ToHashSet();

        static void RequireReleased(CharacterCombat source, CharacterCombat target, FrankBattlePairPlayback pair)
        {
            Require(!source.IsBusy && !target.IsBusy && !source.IsDead && !target.IsDead,
                "Cancellation/rollback retained busy or death state.");
            Require(!pair || !pair.Playing && !pair.AttackerActor && !pair.ReceiverActor,
                "Cancellation/rollback retained playback actors.");
        }

        static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
