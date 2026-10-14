using System.Collections.Generic;
using System.Linq;
using FrankRetarget;
using UnityEngine;

public static partial class BattlePresentationContactSetup
{
    public static Bounds EvaluatedBounds(Animator animator)
    {
        bool first = true;
        var bounds = new Bounds();
        var baked = new Mesh();
        try
        {
            foreach (var skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!Visible(skin) || !skin.sharedMesh || skin.sharedMesh.vertexCount < 1000)
                    continue;
                skin.BakeMesh(baked, false);
                foreach (var point in RenderedVertices(skin, baked, new List<string>()))
                {
                    if (first)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        first = false;
                    }
                    else
                        bounds.Encapsulate(point);
                }
            }
            if (first)
                throw new System.InvalidOperationException("No character skin: " + animator.name);
            return bounds;
        }
        finally
        {
            Object.DestroyImmediate(baked);
        }
    }

    public static float EvaluatedFloor(CharacterCombat fighter)
    {
        float minimum = float.PositiveInfinity;
        var baked = new Mesh();
        try
        {
            foreach (var skin in fighter.Animator.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!Visible(skin) || !skin.sharedMesh || skin.sharedMesh.vertexCount < 1000)
                    continue;
                skin.BakeMesh(baked, false);
                var vertices = RenderedVertices(skin, baked, new List<string>());
                foreach (var point in vertices)
                    minimum = Mathf.Min(minimum, point.y);
            }
            return minimum;
        }
        finally
        {
            Object.DestroyImmediate(baked);
        }
    }

    public sealed class ContactProbe
    {
        readonly CharacterCombat receiver;
        readonly Surface body;
        readonly Surface striker;

        public ContactProbe(FrankBattlePairPlayback pair, CharacterCombat attacker,
            CharacterCombat victim, string selection)
        {
            receiver = victim;
            body = Receiver(victim, new List<string>());
            if (selection != "Gun")
                striker = Striker(pair, attacker, selection, new List<string>());
        }

        public float Measure(out Vector3 contact, out HumanBodyBones bone, out Vector3 offset)
        {
            contact = Contact(striker, body);
            var anchor = NearestAnchor(receiver, contact, out bone);
            offset = anchor.InverseTransformPoint(contact);
            return striker.Distance(contact);
        }

        public float MeasureQuick(out Vector3 contact, out HumanBodyBones bone, out Vector3 offset)
        {
            float best = float.PositiveInfinity;
            contact = Vector3.zero;
            foreach (var point in striker.Samples())
            {
                Vector3 candidate = body.Nearest(point);
                float gap = (candidate - point).sqrMagnitude;
                if (gap >= best)
                    continue;
                best = gap;
                contact = candidate;
            }
            for (int i = 0; i < 16; i++)
                contact = body.Nearest(striker.Nearest(contact));
            var anchor = NearestAnchor(receiver, contact, out bone);
            offset = anchor.InverseTransformPoint(contact);
            return striker.Distance(contact);
        }

        public Vector3 BodyPoint(Vector3 near) => body.Nearest(near);

        public Vector3 FacePoint(Vector3 hand)
        {
            var head = new Surface();
            var bone = receiver.Animator.GetBoneTransform(HumanBodyBones.Head);
            foreach (var skin in receiver.Animator.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (Visible(skin) && skin.sharedMesh && skin.sharedMesh.vertexCount >= 1000)
                    AddMesh(head, skin, bone, new List<string>());
            head.Build();
            var center = head.Bounds.center;
            var direction = Vector3.ProjectOnPlane(hand - center, Vector3.up).normalized;
            return head.Nearest(center + direction * head.Bounds.size.magnitude);
        }

        public float MeasureSparse(out Vector3 contact, out HumanBodyBones bone, out Vector3 offset)
        {
            float best = float.PositiveInfinity;
            contact = Vector3.zero;
            int step = Mathf.Max(1, striker.vertices.Count / 384);
            for (int i = 0; i < striker.vertices.Count; i += step)
            {
                var point = striker.vertices[i];
                var candidate = body.Nearest(point);
                float gap = (candidate - point).sqrMagnitude;
                if (gap >= best)
                    continue;
                best = gap;
                contact = candidate;
            }
            for (int i = 0; i < 16; i++)
                contact = body.Nearest(striker.Nearest(contact));
            var anchor = NearestAnchor(receiver, contact, out bone);
            offset = anchor.InverseTransformPoint(contact);
            return striker.Distance(contact);
        }
        public float BodyDistance(Vector3 point) => body.Distance(point);
        public float SourceDistance(Vector3 point) => striker == null ? 0 : striker.Distance(point);
        public float FloorHeight => body.vertices.Min(p => p.y);

        // A constant entry-spacing change translates the receiver's whole evaluated surface.
        public float MeasureReceiverShift(Vector3 shift)
        {
            if (striker == null)
                throw new System.InvalidOperationException("A ranged shot has no melee contact surface.");
            return ReceiverShiftGap(striker, body, shift);
        }

        public void GroundAnchor(out HumanBodyBones bone, out Vector3 offset)
        {
            Vector3 point = body.vertices.OrderBy(p => p.y).First();
            var anchor = NearestAnchor(receiver, point, out bone);
            offset = anchor.InverseTransformPoint(point);
        }
    }

    static float ReceiverShiftGap(Surface striker, Surface body, Vector3 shift)
    {
        float best = float.PositiveInfinity;
        Vector3 contact = Vector3.zero;
        foreach (var point in striker.Samples())
        {
            Vector3 candidate = body.Nearest(point - shift) + shift;
            float distance = (candidate - point).sqrMagnitude;
            if (distance >= best)
                continue;
            best = distance;
            contact = candidate;
        }
        for (int i = 0; i < 16; i++)
            contact = body.Nearest(striker.Nearest(contact) - shift) + shift;
        return striker.Distance(contact);
    }
}
