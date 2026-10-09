using System;
using UnityEngine;

namespace FrankRetarget
{
    public sealed partial class FrankBattlePairPlayback
    {
        CombatLethalPairVariant lethalVariant;

        public bool UsesLethalReceiverVariant => lethalVariant != null;
        public AnimationClip ReceiverClip => lethalVariant != null ? lethalVariant.reaction : pair?.reaction;
        public FrankPairGrounding ActiveGrounding => lethalVariant != null ? lethalVariant.grounding : Move?.grounding;
        public CombatActionDefinition.Contact[] ActiveContacts => lethalVariant != null ? lethalVariant.contacts
            : Move?.actionDefinition ? Move.actionDefinition.contacts : Array.Empty<CombatActionDefinition.Contact>();
    }
}
