using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSourceWeaponCheck
    {
        static string VerifySamples(FrankBattlePairPlayback playback, FrankBattlePair config,
            EquipmentSnapshot equipment, bool injected)
        {
            var attack = playback.AttackerActor;
            var reaction = playback.ReceiverActor;
            var pose = attack.Pose;
            var renderers = pose.weaponRenderers;
            Require(renderers != null && renderers.Length > 0 && renderers.All(r => r),
                "Source did not expose concrete tracked weapon renderers.");
            var rendererIds = renderers.Select(r => r.GetEntityId()).ToArray();
            var native = NativeRenderers(config.attackerDriver, attack.activeDriver);
            var socket = pose.driver.transform.Find(Socket);
            Require(socket, "Exact source weapon socket missing from instantiated driver.");
            Transform prop = null;
            if (injected)
            {
                prop = renderers[0].transform;
                while (prop && prop.parent != socket)
                    prop = prop.parent;
                Require(prop && prop.parent == socket, "Tracked weapon is not attached to the exact socket.");
                Require(prop.GetComponentsInChildren<Renderer>(true).ToHashSet().SetEquals(renderers),
                    "Pose does not track exactly the injected prop renderer set.");
                Require(MeshIds(prop.gameObject).SequenceEqual(MeshIds(config.attackerWeaponPrefab)),
                    "Tracked prop does not contain the requested prefab meshes.");
                Require(!native.Intersect(renderers).Any(), "Injected prop reused a native weapon renderer.");
            }
            else
            {
                Require(native.ToHashSet().SetEquals(renderers), "Null prefab replaced native renderer ownership.");
            }
            int expectedTransforms = config.attackerDriver.GetComponentsInChildren<Transform>(true).Length;
            int expectedRenderers = config.attackerDriver.GetComponentsInChildren<Renderer>(true).Length;
            if (injected)
            {
                expectedTransforms += config.attackerWeaponPrefab.GetComponentsInChildren<Transform>(true).Length;
                expectedRenderers += config.attackerWeaponPrefab.GetComponentsInChildren<Renderer>(true).Length;
            }
            var transforms = attack.GetComponentsInChildren<Transform>(true)
                .Concat(reaction.GetComponentsInChildren<Transform>(true))
                .Concat(attack.character.GetComponentsInChildren<Transform>(true))
                .Concat(reaction.character.GetComponentsInChildren<Transform>(true)).Distinct().ToArray();
            var times = new[] { 0f, .06f, playback.Duration * .25f, playback.Duration * .5f,
                playback.Duration * .85f, playback.Duration };
            var snapshots = new TransformSnapshot[times.Length];
            float maximumGripError = 0;
            float settledGripError = 0;
            var gripLimbs = pose.limbs.Where(limb => limb.sourceKnuckle).ToArray();
            if (injected)
                Require(gripLimbs.Length > 0, "Injected source has no calibrated grip limbs to validate.");
            void VerifyOwnership()
            {
                Require(playback.AttackerActor == attack && playback.ReceiverActor == reaction,
                    "Sampling replaced an actor.");
                Require(ReferenceEquals(pose.weaponRenderers, renderers) &&
                    renderers.All(r => r) && rendererIds.SequenceEqual(renderers.Select(r => r.GetEntityId())),
                    "Sampling changed tracked renderer ownership.");
                Require(attack.activeDriver.GetComponentsInChildren<Transform>(true).Length == expectedTransforms &&
                    attack.activeDriver.GetComponentsInChildren<Renderer>(true).Length == expectedRenderers,
                    "Unexpected injected object or renderer count.");
                Require(renderers.All(Visible), "A tracked source weapon renderer is hidden.");
                Require(attack.GetComponentsInChildren<Renderer>(true).Where(Visible).ToHashSet()
                    .SetEquals(renderers), "Attacker displays an untracked or duplicate native renderer.");
                Require(!reaction.GetComponentsInChildren<Renderer>(true).Any(Visible) &&
                    reaction.Pose.weaponRenderers.All(r => !Visible(r)),
                    "Target source actor displays weapon or source-body renderers.");
                equipment.RequireHidden();
                if (!injected)
                    return;
                foreach (var limb in gripLimbs)
                {
                    Require(limb.alignGrip && limb.sourceFingerJoint && limb.targetKnuckle &&
                        limb.targetFingerJoint, "Injected source lost calibrated grip alignment.");
                    float error = Vector3.Distance(limb.SourceGrip, limb.TargetGrip);
                    Require(float.IsFinite(error), "Injected source has a non-finite current grip error.");
                    maximumGripError = Mathf.Max(maximumGripError, error);
                    if (playback.SampleTime >= playback.Move.sourcePair.entryBlendSeconds)
                    {
                        settledGripError = Mathf.Max(settledGripError, error);
                        Require(error <= .025f, "Settled grip separates from its source: " + error);
                    }
                }
                Require(native.All(r => r && !r.enabled), "Native source weapon renderer remained enabled.");
                Require(prop && prop.parent == socket && prop.localPosition.sqrMagnitude < 1e-10f &&
                    Quaternion.Angle(prop.localRotation, Quaternion.identity) < .001f &&
                    (prop.localScale - Vector3.one).sqrMagnitude < 1e-10f,
                    "Source prop lost exact socket attachment or local identity.");
                Require(!prop.GetComponentsInChildren<Collider>(true).Any(c => c.enabled) &&
                    !prop.GetComponentsInChildren<Collider2D>(true).Any(c => c.enabled),
                    "Injected source prop contains an enabled collider.");
            }
            for (int i = 0; i < times.Length; i++)
            {
                playback.EvaluateAt(times[i]);
                VerifyOwnership();
                snapshots[i] = new TransformSnapshot(transforms);
            }
            float maximumPositionError = 0;
            float maximumRotationError = 0;
            for (int repeat = 0; repeat < 4; repeat++)
            {
                playback.EvaluateAt(playback.Duration);
                VerifyOwnership();
                snapshots[snapshots.Length - 1].RequireUnchanged(transforms,
                    ref maximumPositionError, ref maximumRotationError);
            }
            for (int i = times.Length - 1; i >= 0; i--)
            {
                playback.EvaluateAt(times[i]);
                VerifyOwnership();
                snapshots[i].RequireUnchanged(transforms, ref maximumPositionError, ref maximumRotationError);
            }
            string drift = FormattableString.Invariant(
                $"maximum repeat/backward drift={maximumPositionError:R}m/{maximumRotationError:R}deg");
            string grip = injected ? FormattableString.Invariant(
                $"; aligned grip limbs={gripLimbs.Length}; entry-inclusive grip error={maximumGripError:R}m; settled grip error={settledGripError:R}m") : "";
            return $"16 samples; tracked renderers={renderers.Length}; injected={injected}; " + drift + grip;
        }

        static bool Visible(Renderer renderer) => renderer && renderer.enabled && renderer.gameObject.activeInHierarchy;

        static string[] MeshIds(GameObject root) => root.GetComponentsInChildren<MeshFilter>(true)
            .Select(f => f.sharedMesh).Concat(root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Select(r => r.sharedMesh)).Where(mesh => mesh).Select(mesh => mesh.GetEntityId().ToString())
            .OrderBy(id => id).ToArray();

        static Renderer[] NativeRenderers(FrankTestDriver prefab, FrankTestDriver instance)
        {
            return prefab.pose.weaponRenderers.Select(renderer =>
            {
                string path = AnimationUtility.CalculateTransformPath(renderer.transform, prefab.transform);
                var transform = string.IsNullOrEmpty(path) ? instance.transform : instance.transform.Find(path);
                Require(transform, "Native renderer path missing from instantiated source driver.");
                int index = Array.IndexOf(renderer.GetComponents<Renderer>(), renderer);
                return transform.GetComponents<Renderer>()[index];
            }).ToArray();
        }

        sealed class TransformSnapshot
        {
            readonly Vector3[] positions;
            readonly Quaternion[] rotations;
            readonly Vector3[] scales;

            public TransformSnapshot(Transform[] transforms)
            {
                positions = transforms.Select(t => t.position).ToArray();
                rotations = transforms.Select(t => t.rotation).ToArray();
                scales = transforms.Select(t => t.localScale).ToArray();
            }

            public void RequireUnchanged(Transform[] transforms, ref float positionError, ref float rotationError)
            {
                for (int i = 0; i < transforms.Length; i++)
                {
                    Require(transforms[i], "Sampling destroyed an existing transform.");
                    float distance = Vector3.Distance(positions[i], transforms[i].position);
                    float angle = Quaternion.Angle(rotations[i], transforms[i].rotation);
                    positionError = Mathf.Max(positionError, distance);
                    rotationError = Mathf.Max(rotationError, angle);
                    Require(distance < .0001f && angle < .1f &&
                        Vector3.Distance(scales[i], transforms[i].localScale) < .00001f,
                        $"Cumulative transform drift: id={transforms[i].GetEntityId()} " +
                        $"position={distance:R} rotation={angle:R}.");
                }
            }
        }
    }
}
