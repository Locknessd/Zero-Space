using System;
using System.Collections.Generic;
using System.Linq;
using FrankRetarget;
using UnityEngine;

public static partial class BattlePresentationContactSetup
{
    public sealed class AxeContactProbe
    {
        readonly Surface body;
        readonly CharacterCombat receiver;

        public AxeContactProbe(CharacterCombat victim)
        {
            receiver = victim;
            body = Receiver(victim, new List<string>());
        }

        public float Measure(FrankTestActor attacker, bool left, out Vector3 contact,
            out HumanBodyBones anchor, out Vector3 offset, out Vector3 bladeCenter)
        {
            string socketName = left ? "L_axe_wp" : "R_axe_wp";
            var socket = attacker.activeDriver.GetComponentsInChildren<Transform>(true)
                .Single(t => t.name == socketName);
            var surface = new Surface();
            foreach (var renderer in attacker.Pose.weaponRenderers)
                if (Visible(renderer) && (renderer.transform == socket || renderer.transform.IsChildOf(socket)))
                    AddMesh(surface, renderer, null, new List<string>());
            if (surface.vertices.Count == 0)
                throw new InvalidOperationException("No native axe mesh below " + socketName);
            var hand = attacker.Pose.limbs.First(l => l.sourceKnuckle &&
                l.end == attacker.character.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand));
            Vector3 grip = hand.SourceGrip;
            float farthest = surface.vertices.Max(p => Vector3.Distance(p, grip));
            float threshold = farthest * .65f;
            // The distal head is measured separately from the haft and hand.
            // This geometric selection still requires visual approval of the blade/contact pose.
            var blade = new Surface();
            blade.vertices.AddRange(surface.vertices);
            for (int i = 0; i < surface.triangles.Count; i += 3)
            {
                bool distal = true;
                for (int vertex = 0; vertex < 3; vertex++)
                    distal &= Vector3.Distance(surface.vertices[surface.triangles[i + vertex]], grip) >= threshold;
                if (!distal)
                    continue;
                blade.triangles.Add(surface.triangles[i]);
                blade.triangles.Add(surface.triangles[i + 1]);
                blade.triangles.Add(surface.triangles[i + 2]);
            }
            blade.Build();
            bladeCenter = blade.Bounds.center;
            contact = Contact(blade, body);
            var bone = NearestAnchor(receiver, contact, out anchor);
            offset = bone.InverseTransformPoint(contact);
            return blade.Distance(contact);
        }
    }
}
