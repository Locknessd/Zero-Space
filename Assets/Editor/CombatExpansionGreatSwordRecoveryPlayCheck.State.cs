using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordRecoveryPlayCheck
    {
        static Vector3[] positions;
        static Quaternion[] rotations;
        static bool[] enabledStates, animatorEnabled;
        static float[] animatorSpeeds;
        static CombatTripletData[][] lightMoves, heavyMoves;
        static GameObject[] activeObjects;
        static bool[] activeStates;
        static TrumpWeaponManager[] originalEquipment, equipment;
        static TrumpWeaponManager.WeaponType[] originalWeapons;
        static FrankBattlePairPlayback[] originalPairs;
        static bool priorInput, saved;
        static float priorTimeScale;
        static int nativeBefore;
        static string healthBefore;
        static Camera view;
        static Vector3 cameraPosition;
        static Quaternion cameraRotation;
        static float cameraFov, cameraSize;

        static void SaveState()
        {
            saved = false;
            equipment = null;
            pair = null;
            ownedObjects = null;
            positions = fighters.Select(f => f.Animator.transform.position).ToArray();
            rotations = fighters.Select(f => f.Animator.transform.rotation).ToArray();
            enabledStates = fighters.Select(f => f.enabled).ToArray();
            animatorEnabled = fighters.Select(f => f.Animator.enabled).ToArray();
            animatorSpeeds = fighters.Select(f => f.Animator.speed).ToArray();
            lightMoves = fighters.Select(f => f.lightCombatMoves).ToArray();
            heavyMoves = fighters.Select(f => f.heavyCombatMoves).ToArray();
            activeObjects = fighters.SelectMany(f => f.GetComponentsInChildren<Transform>(true))
                .Select(t => t.gameObject).Distinct().ToArray();
            activeStates = activeObjects.Select(item => item.activeSelf).ToArray();
            originalEquipment = fighters.SelectMany(f => f.GetComponentsInChildren<TrumpWeaponManager>(true)).ToArray();
            Require(originalEquipment.All(manager => !manager.IsUnarmedPresentation),
                "Preexisting equipment must be free of presentation ownership.");
            originalWeapons = originalEquipment.Select(manager => manager.ActiveWeapon).ToArray();
            originalPairs = fighters.Select(f => f.GetComponent<FrankBattlePairPlayback>()).ToArray();
            priorInput = game.enableLocalInputTesting;
            priorTimeScale = Time.timeScale;
            nativeBefore = Object.FindObjectsByType<FrankTestActor>(FindObjectsInactive.Include).Length;
            healthBefore = HealthSnapshot();
            view = Camera.main;
            if (view)
            {
                cameraPosition = view.transform.position;
                cameraRotation = view.transform.rotation;
                cameraFov = view.fieldOfView;
                cameraSize = view.orthographicSize;
            }
            saved = true;
        }

        static string HealthSnapshot()
        {
            var field = typeof(GameManager).GetField("_characterHp", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null, "Authoritative health observation field unavailable.");
            var health = (Dictionary<PlayerUI.Side, long>)field.GetValue(game);
            return string.Join(";", health.OrderBy(item => item.Key).Select(item => item.Key + "=" + item.Value));
        }

        static void CreateFixtures()
        {
            equipment = new TrumpWeaponManager[fighters.Length];
            for (int i = 0; i < fighters.Length; i++)
            {
                var root = new GameObject("GreatSword recovery equipment fixture");
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
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            item.AddComponent<BoxCollider>().isTrigger = true;
            item.AddComponent<TrailRenderer>().emitting = false;
            return item;
        }

        static void ResetCase()
        {
            Require(HealthSnapshot() == healthBefore, "Lifecycle fixture changed authoritative health.");
            foreach (var fighter in fighters)
                fighter.enabled = true;
            game.ResetCombatQueue();
            Require(fighters.All(f => !f.IsBusy && !f.IsDead && f.Animator.enabled),
                "Authoritative reset failed to restore both participants.");
            if (pair)
                Require(!pair.Playing && !pair.AttackerActor && !pair.ReceiverActor,
                    "Reset failed to clear source actors, including accepted lethal hold.");
            if (equipment != null)
                Require(equipment.All(manager => !manager.IsUnarmedPresentation &&
                    manager.ActiveWeapon == TrumpWeaponManager.WeaponType.DualDaggers &&
                    manager.dualDaggersSet.activeSelf), "Reset failed to restore fixture equipment.");
            for (int i = 0; i < originalEquipment.Length; i++)
                Require(!originalEquipment[i].IsUnarmedPresentation &&
                    originalEquipment[i].ActiveWeapon == originalWeapons[i],
                    "Reset failed to preserve preexisting equipment request.");
            // Destroy is deferred; the next post-late frame checks destroyed objects before new ownership.
        }

        static void CheckReleasedObjects()
        {
            Require(ownedObjects == null || ownedObjects.All(item => !item),
                "Reset left a source actor, instantiated driver or source prop alive.");
            Require(Object.FindObjectsByType<FrankTestActor>(FindObjectsInactive.Include).Length == nativeBefore,
                "Reset leaked a native source actor.");
        }

        static void RestoreState()
        {
            if (!saved)
                return;
            var errors = new List<string>();
            void Attempt(Action action)
            {
                try
                {
                    action();
                }
                catch (Exception error)
                {
                    errors.Add(error.Message);
                }
            }
            foreach (var fighter in fighters)
                if (fighter)
                    fighter.SequenceEnded -= SequenceEnded;
            Time.timeScale = priorTimeScale;
            Attempt(() =>
            {
                if (game)
                    game.ResetCombatQueue();
            });
            if (game)
                game.enableLocalInputTesting = priorInput;
            for (int i = 0; i < fighters.Length; i++)
            {
                int index = i;
                Attempt(() =>
                {
                    var fighter = fighters[index];
                    if (equipment != null && equipment[index])
                    {
                        equipment[index].gameObject.SetActive(false);
                        Object.Destroy(equipment[index].gameObject);
                    }
                    if (!fighter)
                        return;
                    fighter.lightCombatMoves = lightMoves[index];
                    fighter.heavyCombatMoves = heavyMoves[index];
                    fighter.Animator.transform.SetPositionAndRotation(positions[index], rotations[index]);
                    fighter.Animator.enabled = animatorEnabled[index];
                    fighter.Animator.speed = animatorSpeeds[index];
                    fighter.enabled = enabledStates[index];
                    var addedPair = fighter.GetComponent<FrankBattlePairPlayback>();
                    if (!originalPairs[index] && addedPair)
                        Object.Destroy(addedPair);
                });
            }
            Attempt(() =>
            {
                for (int i = 0; i < activeObjects.Length; i++)
                    if (activeObjects[i])
                        activeObjects[i].SetActive(activeStates[i]);
                for (int i = 0; i < originalEquipment.Length; i++)
                    Require(!originalEquipment[i] || !originalEquipment[i].IsUnarmedPresentation &&
                        originalEquipment[i].ActiveWeapon == originalWeapons[i],
                        "Cleanup failed to restore preexisting equipment.");
            });
            Attempt(() =>
            {
                if (!view)
                    return;
                var framing = view.GetComponent<FrankCinematicCamera>();
                if (framing)
                    framing.ResetView();
                view.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                view.fieldOfView = cameraFov;
                view.orthographicSize = cameraSize;
            });
            saved = false;
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join("; ", errors));
        }
    }
}
