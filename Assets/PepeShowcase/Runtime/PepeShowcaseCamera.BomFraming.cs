using UnityEngine;

namespace ZeroSpace.Showcase
{
    public sealed partial class PepeShowcaseCamera
    {
        Bounds BomViewBounds(PepeAttackShowcase showcase, Quaternion rotation)
        {
            var inverse = Quaternion.Inverse(rotation);
            var hips = showcase.character.GetBoneTransform(HumanBodyBones.Hips).position;
            var bounds = new Bounds(inverse * hips, Vector3.zero);
            var body = new Bounds(hips, Vector3.zero);
            foreach (var human in FramingBones)
            {
                var bone = showcase.character.GetBoneTransform(human);
                if (!bone) continue;
                bounds.Encapsulate(inverse * bone.position);
                body.Encapsulate(bone.position);
            }
            float reach = Mathf.Max(1.65f, body.size.y * 1.25f);
            foreach (var renderer in showcase.Actor.Pose.weaponRenderers)
            {
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                string name = renderer.name.ToLowerInvariant();
                if (name.Contains("fx") || name.Contains("treak") || name.Contains("trail")) continue;
                if (!WeaponBounds(renderer, out var weapon) ||
                    Vector3.Distance(weapon.center, body.center) > reach) continue;
                if (renderer is SkinnedMeshRenderer)
                {
                    var indices = baked.triangles;
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        var a = vertices[indices[i]];
                        var b = vertices[indices[i + 1]];
                        var c = vertices[indices[i + 2]];
                        if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-12f) continue;
                        bounds.Encapsulate(inverse * a);
                        bounds.Encapsulate(inverse * b);
                        bounds.Encapsulate(inverse * c);
                    }
                }
                else
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (!filter || !filter.sharedMesh) continue;
                    var local = filter.sharedMesh.bounds;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var point = local.center + Vector3.Scale(local.extents, new Vector3(
                            (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1,
                            (corner & 4) == 0 ? -1 : 1));
                        bounds.Encapsulate(inverse * renderer.transform.TransformPoint(point));
                    }
                }
            }
            // Fit posed geometry in the camera's axes, avoiding inflated world-axis box corners.
            bounds.Expand(.12f);
            return bounds;
        }
    }
}
