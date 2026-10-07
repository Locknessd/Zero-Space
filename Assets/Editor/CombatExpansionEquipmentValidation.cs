using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using EquipmentWeapon = TrumpWeaponManager.WeaponType;

namespace FrankRetarget.Editor
{
    public static class CombatExpansionEquipmentValidation
    {
        public static void Validate()
        {
            var report = new StringBuilder();
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject actor = null;
            try
            {
                actor = new GameObject("Equipment ownership validation");
                SceneManager.MoveGameObjectToScene(actor, scene);
                var manager = actor.AddComponent<TrumpWeaponManager>();
                var animator = actor.AddComponent<Animator>();
                typeof(TrumpWeaponManager)
                    .GetField("characterAnimator", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(manager, animator);
                var roots = CreateEquipment(actor.transform, manager);
                CheckLifecycle(manager, roots, report);
                report.AppendLine("PASS: all temporary unarmed equipment lifecycle checks.");
            }
            catch (Exception exception)
            {
                report.AppendLine("FAIL: " + exception);
                throw;
            }
            finally
            {
                if (actor != null) UnityEngine.Object.DestroyImmediate(actor);
                EditorSceneManager.ClosePreviewScene(scene);
                Directory.CreateDirectory("GeneratedAssets/CombatExpansion");
                File.WriteAllText("GeneratedAssets/CombatExpansion/EquipmentValidation.txt", report.ToString());
            }
        }

        private static GameObject[] CreateEquipment(Transform parent, TrumpWeaponManager manager)
        {
            var roots = new GameObject[10];
            for (var index = 0; index < roots.Length; index++)
            {
                var root = new GameObject("Weapon root or loose prop " + index);
                root.transform.SetParent(parent, false);
                var child = new GameObject("Collider and effects");
                child.transform.SetParent(root.transform, false);
                child.AddComponent<BoxCollider>();
                var trail = child.AddComponent<TrailRenderer>();
                trail.time = 100f;
                var particles = child.AddComponent<ParticleSystem>();
                var main = particles.main;
                main.playOnAwake = false;
                main.startLifetime = 100f;
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                roots[index] = root;
            }
            manager.warriorShieldSet = roots[0];
            manager.greatSwordSet = roots[1];
            manager.spearSet = roots[2];
            manager.katanaSet = roots[3];
            manager.dualDaggersSet = roots[4];
            manager.assassinSet = roots[5];
            manager.twoHandedAxeSet = roots[6];
            manager.katanaSwordMesh = roots[7];
            manager.katanaCaseMesh = roots[8];
            manager.katanaDummyHandle = roots[9];
            return roots;
        }

        private static void CheckLifecycle(TrumpWeaponManager manager, GameObject[] roots, StringBuilder report)
        {
            manager.EquipCombatWeapon(EquipmentWeapon.Katana);
            Require(roots[3].activeSelf && roots[7].activeSelf && roots[8].activeSelf && roots[9].activeSelf,
                "Katana and loose props must initially be equipped.");
            // Deliberately contaminate every set to verify both hands and unrelated emitters get cleared.
            foreach (var root in roots)
            {
                root.SetActive(true);
                var trail = root.GetComponentInChildren<TrailRenderer>();
                trail.AddPosition(Vector3.zero);
                trail.AddPosition(Vector3.one);
                root.GetComponentInChildren<ParticleSystem>().Emit(3);
            }
            var first = manager.BeginUnarmedPresentation();
            Require(first != 0, "Tokens must be nonzero.");
            Hidden(manager, roots);
            Require(Desired(manager) == EquipmentWeapon.Katana && manager.CombatWeaponLocked,
                "Begin must preserve authoritative weapon and combat ownership.");
            Require(!manager.autoEquipWithAnimation, "Begin must preserve auto equipment setting.");
            foreach (var root in roots)
            {
                Require(root.GetComponentInChildren<TrailRenderer>(true).positionCount == 0,
                    "Old trails must be cleared.");
                Require(root.GetComponentInChildren<ParticleSystem>(true).particleCount == 0,
                    "Old particles must be cleared.");
            }
            report.AppendLine("PASS: all seven roots, three loose props, colliders, trails and particles suppressed.");

            var second = manager.BeginUnarmedPresentation();
            Require(second != first, "Overlapping owners need distinct tokens.");
            manager.EndUnarmedPresentation(first, true);
            manager.EndUnarmedPresentation(first, false);
            Hidden(manager, roots);
            manager.EquipCombatWeapon(EquipmentWeapon.Spear);
            Hidden(manager, roots);
            manager.EndUnarmedPresentation(second, true);
            Require(manager.ActiveWeapon == EquipmentWeapon.Spear && roots[2].activeSelf,
                "The last owner must restore the latest combat equipment request.");
            Require(manager.CombatWeaponLocked, "Combat ownership must survive a restoring release.");
            report.AppendLine("PASS: overlapping owners, out-of-order and duplicate releases, latest combat gear.");

            var third = manager.BeginUnarmedPresentation();
            manager.EndUnarmedPresentation(second, false);
            manager.EndUnarmedPresentation(0, false);
            Hidden(manager, roots);
            manager.autoEquipWithAnimation = true;
            manager.EquipWeapon(EquipmentWeapon.DualDaggers);
            Hidden(manager, roots);
            manager.EndUnarmedPresentation(third, true);
            Require(manager.ActiveWeapon == EquipmentWeapon.DualDaggers && roots[4].activeSelf,
                "Plain authoritative gear changes must survive suppression.");
            Require(manager.autoEquipWithAnimation, "Explicit auto equipment changes must survive release.");

            var fourth = manager.BeginUnarmedPresentation();
            manager.defaultIdleWeapon = EquipmentWeapon.GreatSword;
            manager.ReleaseCombatWeapon();
            Hidden(manager, roots);
            Require(!manager.CombatWeaponLocked && Desired(manager) == EquipmentWeapon.GreatSword,
                "Explicit combat release must update desired gear and ownership while hidden.");
            manager.EndUnarmedPresentation(fourth, true);
            Require(manager.ActiveWeapon == EquipmentWeapon.GreatSword && roots[1].activeSelf,
                "Combat release must restore latest idle equipment.");
            report.AppendLine("PASS: stale generation, explicit ordinary equip and combat release.");

            var interrupted = manager.BeginUnarmedPresentation();
            var overlapping = manager.BeginUnarmedPresentation();
            manager.EndUnarmedPresentation(interrupted, false);
            manager.EndUnarmedPresentation(overlapping, true);
            Hidden(manager, roots);
            Require(Desired(manager) == EquipmentWeapon.GreatSword, "Death hold must retain authoritative gear.");
            manager.defaultIdleWeapon = EquipmentWeapon.Katana;
            Invoke(manager, "CheckAnimatorStateAndAutoEquip");
            Invoke(manager, "OnValidate");
            Invoke(manager, "Start");
            Hidden(manager, roots);
            Require(Desired(manager) == EquipmentWeapon.GreatSword, "Automatic checks cannot mutate held gear.");
            manager.ResetUnarmedPresentation();
            Require(manager.ActiveWeapon == EquipmentWeapon.GreatSword && roots[1].activeSelf,
                "Reset must restore latest requested state, not changed idle defaults.");
            manager.EndUnarmedPresentation(interrupted, false);
            Require(!manager.IsUnarmedPresentation, "Reset must invalidate old callbacks.");

            var dead = manager.BeginUnarmedPresentation();
            manager.EndUnarmedPresentation(dead, false);
            manager.EquipWeapon(EquipmentWeapon.Assassin);
            Require(!manager.IsUnarmedPresentation && roots[5].activeSelf,
                "Explicit new equip must clear a nonrestoring hold.");
            report.AppendLine("PASS: nonrestoring overlap holds, automatic idle guard, reset, explicit recovery.");

            var beforeDisable = manager.BeginUnarmedPresentation();
            manager.enabled = false;
            Hidden(manager, roots);
            manager.enabled = true;
            manager.EndUnarmedPresentation(beforeDisable, true);
            Hidden(manager, roots);
            var afterDisable = manager.BeginUnarmedPresentation();
            Require(afterDisable != beforeDisable, "Disable must not recycle tokens.");
            manager.EquipCombatWeapon(EquipmentWeapon.TwoHandedAxe);
            manager.EndUnarmedPresentation(beforeDisable, false);
            Hidden(manager, roots);
            manager.EndUnarmedPresentation(afterDisable, true);
            Require(!manager.IsUnarmedPresentation && roots[6].activeSelf,
                "Fresh generation must restore authoritative gear after disable.");

            var beforeReset = manager.BeginUnarmedPresentation();
            manager.ResetUnarmedPresentation();
            var afterReset = manager.BeginUnarmedPresentation();
            manager.EndUnarmedPresentation(beforeReset, false);
            Hidden(manager, roots);
            roots[0].SetActive(true);
            roots[7].SetActive(true);
            Invoke(manager, "LateUpdate");
            Hidden(manager, roots);
            manager.EndUnarmedPresentation(afterReset, true);
            Require(roots[6].activeSelf && !manager.IsUnarmedPresentation,
                "Reset generation must ignore stale callbacks and repair external activation.");
            report.AppendLine("PASS: component disable, re-enable, reset generation, external visibility repair.");
        }

        private static void Hidden(TrumpWeaponManager manager, GameObject[] roots)
        {
            Require(manager.IsUnarmedPresentation && manager.ActiveWeapon == EquipmentWeapon.None,
                "Suppression must expose bare hands.");
            foreach (var root in roots)
            {
                Require(!root.activeSelf && !root.activeInHierarchy, "A registered root or prop remains active.");
                Require(!root.GetComponentInChildren<Collider>(true).gameObject.activeInHierarchy,
                    "A weapon collider hierarchy remains active.");
            }
        }

        private static EquipmentWeapon Desired(TrumpWeaponManager manager)
        {
            return (EquipmentWeapon)typeof(TrumpWeaponManager)
                .GetField("activeWeapon", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager);
        }

        private static void Invoke(TrumpWeaponManager manager, string method)
        {
            typeof(TrumpWeaponManager).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(manager, null);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
