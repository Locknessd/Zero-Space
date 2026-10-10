using System;
using System.Collections.Generic;
using System.Linq;
using FrankRetarget;
using UnityEngine;

public static partial class BattlePresentationContactSetup
{
    public sealed class BladeContactProbe
    {
        readonly CharacterCombat receiver;
        readonly Surface body;
        readonly Surface blade;
        public Vector3 Tip { get; }
        public Vector3 Base { get; }

        public BladeContactProbe(FrankBattlePairPlayback pair, CharacterCombat attacker, CharacterCombat victim)
        {
            receiver = victim;
            body = Receiver(victim, new List<string>());
            var weapon = Striker(pair, attacker, "Weapon", new List<string>());
            var hand = attacker.Animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (!hand)
                throw new InvalidOperationException("Blade probe needs the visible right hand grip.");
            Vector3 grip = hand.position;
            Tip = weapon.vertices.OrderByDescending(p => (p - grip).sqrMagnitude).First();
            Vector3 axis = (Tip - grip).normalized;
            float length = Vector3.Distance(Tip, grip);
            if (length < .5f)
                throw new InvalidOperationException("GreatSword blade span is unexpectedly short.");
            // The proximal fifth contains the grip and guard. Keep the distal blade
            // faces separately from those incidental overlaps, as in the axe probe.
            float minimum = length * .2f;
            Base = grip + axis * minimum;
            blade = new Surface();
            blade.vertices.AddRange(weapon.vertices);
            for (int i = 0; i < weapon.triangles.Count; i += 3)
            {
                bool selected = true;
                for (int corner = 0; corner < 3; corner++)
                    selected &= Vector3.Dot(weapon.vertices[weapon.triangles[i + corner]] - grip, axis) >= minimum;
                if (!selected)
                    continue;
                for (int corner = 0; corner < 3; corner++)
                    blade.triangles.Add(weapon.triangles[i + corner]);
            }
            blade.Build();
        }

        public float Measure(out Vector3 contact, out HumanBodyBones anchor, out Vector3 offset)
        {
            contact = Contact(blade, body);
            var bone = NearestAnchor(receiver, contact, out anchor);
            offset = bone.InverseTransformPoint(contact);
            return blade.Distance(contact);
        }

        public float BladeDistance(Vector3 world) => blade.Distance(world);
        public float BodyDistance(Vector3 world) => body.Distance(world);
        public Vector3 BodyPoint(Vector3 world) => body.Nearest(world);
        public Vector3 BladePoint(Vector3 world) => blade.Nearest(world);
        public float MeasureReceiverShift(Vector3 shift) => ReceiverShiftGap(blade, body, shift);
    }
}
