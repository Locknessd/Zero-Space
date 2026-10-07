using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionThrowPlayCheck
    {
        static Vector3[] positions;
        static Quaternion[] rotations;
        static Scene[] scenes;
        static TrumpWeaponManager[] equipment;
        static TrumpWeaponManager[] originalEquipment;
        static TrumpWeaponManager.WeaponType[] originalWeapons;
        static Camera view;
        static Vector3 cameraPosition;
        static Quaternion cameraRotation;
        static float cameraFov, cameraSize;
        static int nativeBefore;

        static void SaveState()
        {
            positions = fighters.Select(fighter => fighter.Animator.transform.position).ToArray();
            rotations = fighters.Select(fighter => fighter.Animator.transform.rotation).ToArray();
            scenes = fighters.Select(fighter => fighter.gameObject.scene).ToArray();
            originalEquipment = fighters.SelectMany(fighter =>
                fighter.GetComponentsInChildren<TrumpWeaponManager>(true)).ToArray();
            Require(originalEquipment.All(manager => !manager.IsUnarmedPresentation),
                "Preexisting equipment must be free of source-presentation ownership.");
            originalWeapons = originalEquipment.Select(manager => manager.ActiveWeapon).ToArray();
            priorInput = game.enableLocalInputTesting;
            priorTimeScale = Time.timeScale;
            nativeBefore = Object.FindObjectsByType<FrankTestActor>().Length;
            healthBefore = HealthSnapshot();
            view = shake.GetComponent<Camera>();
            Require(view, "Battle shake must belong to a camera.");
            cameraPosition = view.transform.position;
            cameraRotation = view.transform.rotation;
            cameraFov = view.fieldOfView;
            cameraSize = view.orthographicSize;
        }

        static void CreateFixtures()
        {
            equipment = new TrumpWeaponManager[2];
            for (int i = 0; i < equipment.Length; i++)
            {
                var root = new GameObject("Throw validation equipment");
                root.transform.SetParent(fighters[i].transform, false);
                var manager = root.AddComponent<TrumpWeaponManager>();
                equipment[i] = manager;
                manager.autoEquipWithAnimation = false;
                manager.dualDaggersSet = Prop(root.transform, "Dual daggers");
                manager.katanaSet = Prop(root.transform, "Katana");
                manager.katanaSwordMesh = Prop(root.transform, "Sword");
                manager.katanaCaseMesh = Prop(root.transform, "Sheath");
                manager.katanaDummyHandle = Prop(root.transform, "Handle");
                manager.EquipCombatWeapon(TrumpWeaponManager.WeaponType.DualDaggers);
            }
        }

        static GameObject Prop(Transform parent, string name)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.AddComponent<BoxCollider>().isTrigger = true;
            root.AddComponent<TrailRenderer>().emitting = false;
            return root;
        }

        static string HealthSnapshot()
        {
            var field = typeof(GameManager).GetField("_characterHp", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null, "Health observation field unavailable.");
            var health = (Dictionary<PlayerUI.Side, long>)field.GetValue(game);
            return string.Join(";", health.OrderBy(item => item.Key).Select(item => item.Key + "=" + item.Value));
        }

        static void ResetLethalCase()
        {
            // Keep health and round state untouched; only the accepted local fixture is reset.
            activeCase = false;
            foreach (var fighter in fighters)
                fighter.ResetCombat();
            Require(!pair.Playing && !pair.AttackerActor && !pair.ReceiverActor &&
                equipment.All(manager => !manager.IsUnarmedPresentation &&
                    manager.ActiveWeapon == TrumpWeaponManager.WeaponType.DualDaggers),
                "Reset did not clear the accepted lethal terminal pose and held equipment.");
            Require(fighters.All(fighter => !fighter.IsDead && !fighter.IsBusy && fighter.Animator.enabled),
                "Reset failed to restore both accepted-lethal participants.");
            report.AppendLine("PASS lethal reset: native source references, death and equipment hold released.");
        }

        static void RestoreState()
        {
            var errors = new List<string>();
            void Attempt(Action operation)
            {
                try
                {
                    operation();
                }
                catch (Exception error)
                {
                    errors.Add(error.Message);
                }
            }
            if (game)
            {
                if (game.battleVfx)
                {
                    game.battleVfx.SequenceBegan -= SequenceBegan;
                    game.battleVfx.ContactOccurred -= Contact;
                }
                game.enableLocalInputTesting = priorInput;
            }
            foreach (var fighter in fighters)
                if (fighter)
                    fighter.SequenceEnded -= SequenceEnded;
            // Remove subscriptions before invalidating any sequence IDs or cancellation callbacks.
            Attempt(() =>
            {
                if (game && game.IsEventQueueBusy)
                    game.ResetCombatQueue();
                else
                    foreach (var fighter in fighters)
                        if (fighter)
                            fighter.ResetCombat();
            });
            Attempt(() =>
            {
                if (game && game.battleVfx)
                    game.battleVfx.ClearEffects();
                if (feedback)
                    feedback.ResetFeedback();
                if (lighting)
                    lighting.ClearFlashes();
                if (shake)
                    shake.ResetShake();
            });
            Time.timeScale = priorTimeScale;
            for (int i = 0; i < fighters.Length; i++)
            {
                int index = i;
                Attempt(() =>
                {
                    if (equipment != null && equipment[index])
                    {
                        equipment[index].gameObject.SetActive(false);
                        Object.Destroy(equipment[index].gameObject);
                    }
                    if (fighters[index])
                        fighters[index].Animator.transform.SetPositionAndRotation(positions[index], rotations[index]);
                });
            }
            Attempt(() =>
            {
                for (int i = 0; i < originalEquipment.Length; i++)
                    Require(!originalEquipment[i] || !originalEquipment[i].IsUnarmedPresentation &&
                        originalEquipment[i].ActiveWeapon == originalWeapons[i],
                        "Preexisting equipment failed to restore its original request.");
            });
            Attempt(() =>
            {
                if (director)
                    director.ResetView();
                if (!view)
                    return;
                view.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                view.fieldOfView = cameraFov;
                view.orthographicSize = cameraSize;
            });
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join("; ", errors));
        }
    }
}
