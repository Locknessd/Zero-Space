using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    [InitializeOnLoad]
    public static class CombatExpansionEquipmentPlayCheck
    {
        static readonly string[] Cases =
        {
            "completion", "newer equipment", "cancel", "victim disable", "attacker disable",
            "victim death", "attacker death", "round reset", "lethal completion", "overlapping owner"
        };
        const string Report = "GeneratedAssets/CombatExpansion/EquipmentPlayMode.txt";
        static readonly StringBuilder report = new StringBuilder();
        static GameManager game;
        static CharacterCombat[] fighters;
        static TrumpWeaponManager[] equipment;
        static Vector3[] positions;
        static Quaternion[] rotations;
        static CharacterCombat source, target;
        static FrankBattlePairPlayback pair;
        static bool running, acted, priorInput;
        static int step, victimIndex;
        static double began;
        static ulong outerOwner;

        static CombatExpansionEquipmentPlayCheck()
        {
            EditorApplication.update += Tick;
        }

        public static void Begin()
        {
            if (!EditorApplication.isPlaying || running)
                throw new InvalidOperationException("Begin once in BattleScene Play Mode.");
            game = Object.FindAnyObjectByType<GameManager>();
            if (!game || game.IsAnimationTestMode || game.leftCombat.IsBusy || game.rightCombat.IsBusy)
                throw new InvalidOperationException("Battle must be idle outside the animation browser.");
            fighters = new[] { game.leftCombat, game.rightCombat };
            positions = fighters.Select(f => f.Animator.transform.position).ToArray();
            rotations = fighters.Select(f => f.Animator.transform.rotation).ToArray();
            priorInput = game.enableLocalInputTesting;
            game.enableLocalInputTesting = false;
            equipment = fighters.Select(CreateEquipment).ToArray();
            step = 0;
            running = true;
            report.Clear();
            report.AppendLine("RUNNING v2: actual Battle fighters and shared paired playback; lethal final pose retained.");
            StartCase();
        }

        static TrumpWeaponManager CreateEquipment(CharacterCombat fighter)
        {
            var root = new GameObject("Owned paired equipment validation");
            root.transform.SetParent(fighter.transform, false);
            var manager = root.AddComponent<TrumpWeaponManager>();
            manager.autoEquipWithAnimation = false;
            manager.dualDaggersSet = Prop(root.transform, "Both dagger hands");
            Prop(manager.dualDaggersSet.transform, "Left hand");
            Prop(manager.dualDaggersSet.transform, "Right hand");
            manager.katanaSet = Prop(root.transform, "Katana");
            manager.katanaSwordMesh = Prop(root.transform, "Loose sword");
            manager.katanaCaseMesh = Prop(root.transform, "Loose sheath");
            manager.katanaDummyHandle = Prop(root.transform, "Loose hilt");
            return manager;
        }

        static GameObject Prop(Transform parent, string name)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.AddComponent<BoxCollider>().isTrigger = true;
            root.AddComponent<TrailRenderer>().emitting = false;
            return root;
        }

        static void StartCase()
        {
            foreach (var fighter in fighters)
                fighter.gameObject.SetActive(true);
            game.ResetCombatQueue();
            foreach (var manager in equipment)
                manager.EquipCombatWeapon(TrumpWeaponManager.WeaponType.DualDaggers);
            int sourceIndex = step / Cases.Length;
            victimIndex = 1 - sourceIndex;
            source = fighters[sourceIndex];
            target = fighters[victimIndex];
            var move = source.lightCombatMoves.Single(m => m.moveName == "Vol10_CmnAtemi2");
            float direction = sourceIndex == 0 ? 1 : -1;
            source.Animator.transform.position = Vector3.left * direction * move.attackRange * .5f;
            target.Animator.transform.position = Vector3.right * direction * move.attackRange * .5f;
            outerOwner = step % Cases.Length == 9 ? equipment[victimIndex].BeginUnarmedPresentation() : 0;
            if (!source.ExecuteAttack(move, target, step % Cases.Length == 8))
                throw new InvalidOperationException("Actual paired action rejected.");
            pair = source.SourcePlayback;
            acted = false;
            began = EditorApplication.timeSinceStartup;
            AssertSuppressed(equipment[0]);
            AssertSuppressed(equipment[1]);
            File.WriteAllText(Report, report.ToString());
        }

        static void Tick()
        {
            if (!running)
                return;
            try
            {
                if (!EditorApplication.isPlaying)
                    throw new InvalidOperationException("Play Mode ended before lifecycle checks completed.");
                if (EditorApplication.timeSinceStartup - began > 8)
                    throw new TimeoutException("Equipment case did not finish: " + Cases[step % Cases.Length]);
                int kind = step % Cases.Length;
                if (pair.Playing)
                {
                    AssertSuppressed(equipment[0]);
                    AssertSuppressed(equipment[1]);
                }
                if (!acted && pair.SampleTime >= .2f)
                {
                    acted = true;
                    switch (kind)
                    {
                        case 1:
                            equipment[victimIndex].EquipCombatWeapon(TrumpWeaponManager.WeaponType.Katana);
                            AssertSuppressed(equipment[victimIndex]);
                            break;
                        case 2: pair.Cancel(); break;
                        case 3: target.gameObject.SetActive(false); break;
                        case 4: source.gameObject.SetActive(false); break;
                        case 5: target.MarkDead(); break;
                        case 6: source.MarkDead(); break;
                        case 7: game.ResetCombatQueue(); break;
                    }
                }
                if (!acted || pair.Playing)
                    return;
                CheckCompletion(kind);
                report.AppendLine("PASS " + source.name + " -> " + target.name + ": " + Cases[kind]);
                step++;
                if (step < Cases.Length * 2)
                    StartCase();
                else
                {
                    report.AppendLine("PASS all 20 paired equipment lifecycle cases on both fighter roles.");
                    Finish();
                }
            }
            catch (Exception error)
            {
                report.AppendLine("FAIL " + error);
                Finish();
            }
        }

        static void CheckCompletion(int kind)
        {
            if (source.IsBusy || target.IsBusy || !source.IsDead && !source.Animator.enabled ||
                !target.IsDead && !target.Animator.enabled)
                throw new InvalidOperationException($"Paired actor ownership did not release. " +
                    $"source busy={source.IsBusy} dead={source.IsDead} animator={source.Animator.enabled}; " +
                    $"target busy={target.IsBusy} dead={target.IsDead} animator={target.Animator.enabled}");
            for (int index = 0; index < 2; index++)
            {
                var manager = equipment[index];
                bool suppress = !fighters[index].isActiveAndEnabled || fighters[index].IsDead ||
                    kind == 9 && index == victimIndex;
                if (suppress)
                    AssertSuppressed(manager);
                else
                {
                    var expected = kind == 1 && index == victimIndex
                        ? TrumpWeaponManager.WeaponType.Katana : TrumpWeaponManager.WeaponType.DualDaggers;
                    if (manager.IsUnarmedPresentation || manager.ActiveWeapon != expected)
                        throw new InvalidOperationException("Wrong authoritative equipment restored.");
                }
            }
            if (outerOwner != 0)
            {
                equipment[victimIndex].EndUnarmedPresentation(outerOwner, true);
                if (equipment[victimIndex].IsUnarmedPresentation)
                    throw new InvalidOperationException("Outer owner did not release.");
            }
        }

        static void AssertSuppressed(TrumpWeaponManager manager)
        {
            if (!manager.IsUnarmedPresentation || manager.ActiveWeapon != TrumpWeaponManager.WeaponType.None ||
                manager.dualDaggersSet.activeSelf || manager.katanaSet.activeSelf ||
                manager.katanaSwordMesh.activeSelf || manager.katanaCaseMesh.activeSelf ||
                manager.katanaDummyHandle.activeSelf ||
                manager.GetComponentsInChildren<Collider>(false).Any(c => c.enabled))
                throw new InvalidOperationException("Weapon prop or hitbox escaped paired ownership.");
        }

        static void Finish()
        {
            running = false;
            foreach (var fighter in fighters)
                if (fighter)
                    fighter.gameObject.SetActive(true);
            if (game)
            {
                game.ResetCombatQueue();
                game.enableLocalInputTesting = priorInput;
            }
            for (int index = 0; index < fighters.Length; index++)
            {
                if (equipment[index])
                    Object.Destroy(equipment[index].gameObject);
                if (fighters[index])
                    fighters[index].Animator.transform.SetPositionAndRotation(positions[index], rotations[index]);
            }
            File.WriteAllText(Report, report.ToString());
        }
    }
}
