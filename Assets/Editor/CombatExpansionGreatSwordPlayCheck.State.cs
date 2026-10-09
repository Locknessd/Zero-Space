using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordPlayCheck
    {
        static Vector3[] positions;
        static Quaternion[] rotations;
        static Scene[] scenes;
        static TrumpWeaponManager[] equipment, originalEquipment;
        static EquipmentState[] equipmentState;
        static FrankBattlePairPlayback[] originalPairs;
        static readonly List<GameObject> fixtures = new List<GameObject>();
        static bool priorInput, saved;
        static float priorTimeScale;
        static int nativeBefore;
        static string healthBefore;
        static bool[] animatorEnabled;
        static float[] animatorSpeeds;
        static Camera view;
        static Vector3 cameraPosition;
        static Quaternion cameraRotation;
        static float cameraFov, cameraSize;
        static readonly FieldInfo RequestedWeaponField = typeof(TrumpWeaponManager)
            .GetField("activeWeapon", BindingFlags.Instance | BindingFlags.NonPublic);

        sealed class EquipmentState
        {
            public TrumpWeaponManager manager;
            public TrumpWeaponManager.WeaponType weapon;
            public bool locked, automatic;
            readonly GameObject[] objects;
            readonly bool[] active;
            readonly Renderer[] renderers;
            readonly bool[] visible;

            public EquipmentState(TrumpWeaponManager manager)
            {
                this.manager = manager;
                weapon = RequestedWeapon(manager);
                locked = manager.CombatWeaponLocked;
                automatic = manager.autoEquipWithAnimation;
                objects = WeaponObjects(manager).Where(root => root)
                    .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                    .Select(item => item.gameObject).Distinct().ToArray();
                active = objects.Select(item => item.activeSelf).ToArray();
                renderers = objects.SelectMany(item => item.GetComponents<Renderer>()).Distinct().ToArray();
                visible = renderers.Select(renderer => renderer.enabled).ToArray();
            }

            public void Restore(TrumpWeaponManager.WeaponType latest)
            {
                if (!manager)
                    return;
                if (locked)
                    manager.EquipCombatWeapon(latest);
                else
                {
                    if (manager.CombatWeaponLocked)
                        manager.ReleaseCombatWeapon();
                    manager.EquipWeapon(latest);
                }
                manager.autoEquipWithAnimation = automatic;
                if (latest != weapon)
                    return;
                for (int i = 0; i < objects.Length; i++)
                    if (objects[i])
                        objects[i].SetActive(active[i]);
                for (int i = 0; i < renderers.Length; i++)
                    if (renderers[i])
                        renderers[i].enabled = visible[i];
            }
        }

        static TrumpWeaponManager.WeaponType RequestedWeapon(TrumpWeaponManager manager)
        {
            Require(RequestedWeaponField != null, "Requested equipment observation field unavailable.");
            return (TrumpWeaponManager.WeaponType)RequestedWeaponField.GetValue(manager);
        }

        static IEnumerable<GameObject> WeaponObjects(TrumpWeaponManager manager)
        {
            return new[] { manager.warriorShieldSet, manager.greatSwordSet, manager.spearSet,
                manager.katanaSet, manager.dualDaggersSet, manager.assassinSet, manager.twoHandedAxeSet,
                manager.katanaSwordMesh, manager.katanaCaseMesh, manager.katanaDummyHandle };
        }

        static void SaveState()
        {
            fixtures.Clear();
            equipment = null;
            pair = null;
            ownedObjects = null;
            positions = fighters.Select(f => f.Animator.transform.position).ToArray();
            rotations = fighters.Select(f => f.Animator.transform.rotation).ToArray();
            scenes = fighters.Select(f => f.gameObject.scene).ToArray();
            animatorEnabled = fighters.Select(f => f.Animator.enabled).ToArray();
            animatorSpeeds = fighters.Select(f => f.Animator.speed).ToArray();
            originalPairs = fighters.Select(f => f.GetComponent<FrankBattlePairPlayback>()).ToArray();
            originalEquipment = fighters.SelectMany(f => f.GetComponentsInChildren<TrumpWeaponManager>(true)).ToArray();
            Require(originalEquipment.All(manager => !manager.IsUnarmedPresentation),
                "Original equipment must have no preexisting source presentation owner.");
            equipmentState = originalEquipment.Select(manager => new EquipmentState(manager)).ToArray();
            priorInput = game.enableLocalInputTesting;
            priorTimeScale = Time.timeScale;
            nativeBefore = Object.FindObjectsByType<FrankTestActor>(FindObjectsInactive.Include).Length;
            healthBefore = HealthSnapshot();
            view = shake.GetComponent<Camera>();
            Require(view, "Battle shake must belong to a camera.");
            cameraPosition = view.transform.position;
            cameraRotation = view.transform.rotation;
            cameraFov = view.fieldOfView;
            cameraSize = view.orthographicSize;
            saved = true;
        }

        static void CreateFixtures()
        {
            foreach (var fighter in fighters)
            {
                if (fighter.GetComponentsInChildren<TrumpWeaponManager>(true).Length > 0)
                    continue;
                var root = new GameObject("GreatSword gameplay equipment fixture");
                fixtures.Add(root);
                root.transform.SetParent(fighter.transform, false);
                var manager = root.AddComponent<TrumpWeaponManager>();
                manager.autoEquipWithAnimation = false;
                manager.dualDaggersSet = Prop(root.transform, "Dual daggers");
                manager.katanaSet = Prop(root.transform, "Katana");
                manager.katanaSwordMesh = Prop(root.transform, "Sword");
                manager.katanaCaseMesh = Prop(root.transform, "Sheath");
                manager.katanaDummyHandle = Prop(root.transform, "Handle");
                manager.EquipCombatWeapon(TrumpWeaponManager.WeaponType.DualDaggers);
            }
            equipment = fighters.SelectMany(f => f.GetComponentsInChildren<TrumpWeaponManager>(true)).ToArray();
        }

        static GameObject Prop(Transform parent, string name)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            item.AddComponent<BoxCollider>().isTrigger = true;
            item.AddComponent<TrailRenderer>().emitting = false;
            return item;
        }

        static string HealthSnapshot()
        {
            var field = typeof(GameManager).GetField("_characterHp", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null, "Authoritative health observation field unavailable.");
            var health = (Dictionary<PlayerUI.Side, long>)field.GetValue(game);
            return string.Join(";", health.OrderBy(item => item.Key).Select(item => item.Key + "=" + item.Value));
        }

        static void ResetLethalCase()
        {
            foreach (var fighter in fighters)
                fighter.ResetCombat();
            Require(!pair.Playing && !pair.AttackerActor && !pair.ReceiverActor,
                "Reset retained accepted-lethal native source references.");
            CheckEquipmentRestored(false);
            Require(fighters.All(f => !f.IsDead && !f.IsBusy && f.Animator.enabled),
                "Reset did not restore both accepted-lethal participants.");
            Require(HealthSnapshot() == healthBefore, "Accepted-lethal reset changed authoritative health.");
            report.AppendLine("PASS lethal reset: native source actors, death and held equipment released.");
        }

        static void RestoreState()
        {
            if (!saved)
                return;
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
            if (game && game.battleVfx)
            {
                game.battleVfx.SequenceBegan -= SequenceBegan;
                game.battleVfx.ContactOccurred -= Contact;
                game.battleVfx.EffectPlayed -= EffectPlayed;
            }
            if (game && game.battleSfx)
                game.battleSfx.CuePlayed -= AudioPlayed;
            Application.logMessageReceived -= UnexpectedLog;
            foreach (var fighter in fighters)
                if (fighter)
                    fighter.SequenceEnded -= SequenceEnded;
            // Preserve the latest authoritative request, including updates received under suppression.
            var latestWeapons = equipmentState.Select(state => state.manager ?
                RequestedWeapon(state.manager) : state.weapon).ToArray();
            try
            {
                Attempt(() =>
                {
                    if (game)
                        game.ResetCombatQueue();
                });
                Attempt(() =>
                {
                    if (game && game.battleSfx && pair)
                        game.battleSfx.EndSequence(pair, true);
                    if (game && game.battleVfx)
                        game.battleVfx.ClearEffects();
                    if (feedback)
                        feedback.ResetFeedback();
                    if (lighting)
                        lighting.ClearFlashes();
                    if (shake)
                        shake.ResetShake();
                });
            }
            finally
            {
                Time.timeScale = priorTimeScale;
                if (game)
                    game.enableLocalInputTesting = priorInput;
                foreach (var fixture in fixtures)
                    if (fixture)
                    {
                        fixture.SetActive(false);
                        Object.Destroy(fixture);
                    }
                for (int i = 0; i < fighters.Length; i++)
                {
                    int index = i;
                    Attempt(() =>
                    {
                        var fighter = fighters[index];
                        if (!fighter)
                            return;
                        fighter.Animator.transform.SetPositionAndRotation(positions[index], rotations[index]);
                        fighter.Animator.enabled = animatorEnabled[index];
                        fighter.Animator.speed = animatorSpeeds[index];
                        var addedPair = fighter.GetComponent<FrankBattlePairPlayback>();
                        if (!originalPairs[index] && addedPair)
                            Object.Destroy(addedPair);
                    });
                }
                for (int i = 0; i < equipmentState.Length; i++)
                {
                    int index = i;
                    Attempt(() => equipmentState[index].Restore(latestWeapons[index]));
                }
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
                Attempt(() => Require(!game || HealthSnapshot() == healthBefore,
                    "Cleanup changed authoritative health."));
                saved = false;
            }
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join("; ", errors));
        }
    }
}
