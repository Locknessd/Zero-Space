using System.Collections.Generic;

namespace FrankRetarget
{
    public sealed partial class FrankBattlePairPlayback
    {
        readonly List<EquipmentOwner> equipmentOwners = new List<EquipmentOwner>(4);

        readonly struct EquipmentOwner
        {
            public readonly CharacterCombat fighter;
            public readonly TrumpWeaponManager manager;
            public readonly ulong token;

            public EquipmentOwner(CharacterCombat fighter, TrumpWeaponManager manager)
            {
                this.fighter = fighter;
                this.manager = manager;
                token = manager.BeginUnarmedPresentation();
            }
        }

        void AcquireEquipment(CharacterCombat source, CharacterCombat target)
        {
            ReleaseEquipment();
            AcquireEquipment(source);
            AcquireEquipment(target);
        }

        void AcquireEquipment(CharacterCombat fighter)
        {
            // The pair's source rig supplies any attacker weapon. Both base equipment
            // hierarchies must stay inactive, including victim colliders and emitters.
            foreach (var manager in fighter.GetComponentsInChildren<TrumpWeaponManager>(true))
                equipmentOwners.Add(new EquipmentOwner(fighter, manager));
        }

        void ReleaseEquipment()
        {
            foreach (var owner in equipmentOwners)
                if (owner.manager)
                    owner.manager.EndUnarmedPresentation(owner.token,
                        owner.fighter && owner.fighter.isActiveAndEnabled && !owner.fighter.IsDead);
            equipmentOwners.Clear();
        }

        public void CancelIfUnexpectedDeath(CharacterCombat fighter)
        {
            // An accepted lethal receiver outcome keeps its authored terminal pose.
            // A new death outside that outcome interrupts the currently owned action.
            if (Playing && (fighter == attacker || fighter == receiver && !lethal))
                Cancel();
        }
    }
}
