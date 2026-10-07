using System.Collections.Generic;
using FrankRetarget;
using UnityEngine;

public static partial class BattlePresentationContactSetup
{
    /// <summary>Uses the same evaluated triangle geometry as existing battle contact validation.</summary>
    public static bool TryMeasureContact(FrankBattlePairPlayback pair, CharacterCombat source,
        CharacterCombat receiver, string striker, out HumanBodyBones anchor, out Vector3 offset,
        out float gap, out Vector3 world)
    {
        var diagnostics = new List<string>();
        var body = Receiver(receiver, diagnostics);
        var limb = Striker(pair, source, striker, diagnostics);
        world = Contact(limb, body);
        gap = limb.Distance(world);
        var bone = NearestAnchor(receiver, world, out anchor);
        offset = bone.InverseTransformPoint(world);
        return gap <= MaximumSourceGap;
    }
}
