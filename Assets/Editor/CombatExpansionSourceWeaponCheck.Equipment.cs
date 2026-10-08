using System;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSourceWeaponCheck
    {
        sealed class EquipmentSnapshot
        {
            readonly TrumpWeaponManager[] managers;
            readonly GameObject[][] roots;
            readonly bool[][] active;
            readonly ulong[] tokens;
            readonly CharacterCombat[] fighters;
            readonly FrankCombatWeaponRig[] combatRigs;
            readonly FrankWeaponRig[] boundRigs;
            readonly Renderer[] fighterRenderers;
            readonly bool[] visible;
            readonly Renderer[] managedRenderers;
            readonly int sourceManagerCount;
            readonly int targetManagerCount;

            public string Report => $"assigned combat rigs={combatRigs.Count(rig => rig)}; " +
                $"FrankWeaponRig components={boundRigs.Length}; manager-token assertions: " +
                $"source={ManagerReport(sourceManagerCount)}, target={ManagerReport(targetManagerCount)}";

            static string ManagerReport(int count) => count == 0 ? "N/A (none present)" : $"tested {count}";

            public EquipmentSnapshot(CharacterCombat source, CharacterCombat target)
            {
                var sourceManagers = source.GetComponentsInChildren<TrumpWeaponManager>(true);
                var targetManagers = target.GetComponentsInChildren<TrumpWeaponManager>(true);
                sourceManagerCount = sourceManagers.Length;
                targetManagerCount = targetManagers.Length;
                fighters = new[] { source, target };
                combatRigs = fighters.Select(fighter => fighter.weaponRig).ToArray();
                boundRigs = fighters.SelectMany(fighter =>
                    fighter.GetComponentsInChildren<FrankWeaponRig>(true)).Distinct().ToArray();
                managers = sourceManagers.Concat(targetManagers).Distinct().ToArray();
                tokens = new ulong[managers.Length];
                foreach (var manager in managers)
                {
                    Require(manager.isActiveAndEnabled && !manager.IsUnarmedPresentation,
                        "Equipment is not available before source playback.");
                    manager.EquipCombatWeapon(TrumpWeaponManager.WeaponType.GreatSword);
                    Require(manager.greatSwordSet && manager.greatSwordSet.activeInHierarchy,
                        "Equipment baseline did not expose a visible GreatSword set.");
                }
                roots = managers.Select(manager => new[]
                {
                    manager.warriorShieldSet,
                    manager.greatSwordSet,
                    manager.spearSet,
                    manager.katanaSet,
                    manager.dualDaggersSet,
                    manager.assassinSet,
                    manager.twoHandedAxeSet,
                    manager.katanaSwordMesh,
                    manager.katanaCaseMesh,
                    manager.katanaDummyHandle
                }.Where(root => root).Distinct().ToArray()).ToArray();
                active = roots.Select(group => group.Select(root => root.activeSelf).ToArray()).ToArray();
                managedRenderers = roots.SelectMany(group => group)
                    .SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).Distinct().ToArray();
                fighterRenderers = CurrentRenderers();
                visible = fighterRenderers.Select(Visible).ToArray();
                RequireRigState();
            }

            public void RequireHidden()
            {
                RequireRigState();
                Require(CurrentRenderers().ToHashSet().SetEquals(fighterRenderers),
                    "Source playback injected an untracked renderer into a visible fighter hierarchy.");
                for (int i = 0; i < fighterRenderers.Length; i++)
                    if (!managedRenderers.Contains(fighterRenderers[i]))
                        Require(Visible(fighterRenderers[i]) == visible[i],
                            "Source playback changed an unrelated fighter renderer's visibility.");
                for (int i = 0; i < managers.Length; i++)
                {
                    Require(managers[i].IsUnarmedPresentation &&
                        managers[i].ActiveWeapon == TrumpWeaponManager.WeaponType.None,
                        "Pair lost equipment suppression ownership.");
                    Require(roots[i].All(root => !root.activeSelf) &&
                        !roots[i].SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).Any(Visible),
                        "Base equipment remains active or visible during source playback.");
                }
            }

            public void AcquireExternalOwners()
            {
                for (int i = 0; i < managers.Length; i++)
                {
                    tokens[i] = managers[i].BeginUnarmedPresentation();
                    Require(tokens[i] != 0, "Equipment returned an invalid ownership token.");
                }
                RequireHidden();
            }

            public void RequireExternalOwners()
            {
                Require(tokens.All(token => token != 0), "Missing external equipment owner token.");
                RequireHidden();
            }

            public void ReleaseExternalOwners()
            {
                for (int i = 0; i < managers.Length; i++)
                {
                    if (tokens[i] == 0)
                        continue;
                    managers[i].EndUnarmedPresentation(tokens[i], true);
                    tokens[i] = 0;
                }
            }

            public void RequireRestored()
            {
                RequireRigState();
                Require(CurrentRenderers().ToHashSet().SetEquals(fighterRenderers) &&
                    fighterRenderers.All(renderer => renderer) &&
                    fighterRenderers.Select(Visible).SequenceEqual(visible),
                    "Cancellation/rollback retained extra fighter renderers or changed baseline visibility.");
                for (int i = 0; i < managers.Length; i++)
                {
                    Require(!managers[i].IsUnarmedPresentation &&
                        managers[i].ActiveWeapon == TrumpWeaponManager.WeaponType.GreatSword,
                        "Pair leaked an equipment token or changed the authoritative weapon request.");
                    Require(roots[i].Select(root => root.activeSelf).SequenceEqual(active[i]),
                        "Equipment root activity did not restore after cancellation/rollback.");
                    Require(managers[i].greatSwordSet.activeInHierarchy &&
                        managers[i].greatSwordSet.GetComponentsInChildren<Renderer>(true).Any(Visible),
                        "Requested equipment was not visible after restoration.");
                }
            }

            Renderer[] CurrentRenderers() => fighters.SelectMany(fighter =>
                fighter.GetComponentsInChildren<Renderer>(true)).Distinct().ToArray();

            void RequireRigState()
            {
                for (int i = 0; i < fighters.Length; i++)
                {
                    Require(fighters[i].weaponRig == combatRigs[i], "Source playback replaced a combat weapon rig.");
                    if (combatRigs[i])
                        Require(!combatRigs[i].ActiveDriver && combatRigs[i].SampleTime == 0,
                            "A base combat weapon driver retained ownership alongside source playback.");
                }
                foreach (var rig in boundRigs)
                    Require(rig && rig.meshes != null && !rig.meshes.Any(Visible),
                        "A base FrankWeaponRig displays a duplicate weapon.");
            }
        }
    }
}
