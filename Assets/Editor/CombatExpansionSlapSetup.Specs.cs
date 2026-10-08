using System;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapSetup
    {
        // Root enables this only after reviewing the grounded surface contact capture.
        public static bool ContactsReviewed = true;

        sealed class Spec
        {
            public int sequence;
            public string attacker;
            public float seconds;
            public string Id => "SlapFace_Sequence" + sequence;
            public string Key => Id + "_" + attacker;
        }

        static readonly Spec[] Specs =
        {
            new Spec { sequence = 1, attacker = "Mankey", seconds = 1.4f },
            new Spec { sequence = 1, attacker = "Pepe", seconds = 1.391666667f },
            new Spec { sequence = 2, attacker = "Mankey", seconds = .933333333f },
            new Spec { sequence = 2, attacker = "Pepe", seconds = .905f }
        };

        static Spec FindSpec(string attacker, int sequence)
        {
            foreach (var spec in Specs)
                if (spec.attacker == attacker && spec.sequence == sequence)
                    return spec;
            throw new ArgumentException("Expected SlapFace sequence 1 or 2 and attacker Mankey or Pepe.");
        }
    }
}
