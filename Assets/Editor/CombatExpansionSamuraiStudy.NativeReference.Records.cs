using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        const string NativeReferenceOutput = "GeneratedAssets/CombatExpansion/SamuraiStudy/NativePairReference";
        const string NativeReferenceScope = "NATIVE SOURCE REFERENCE ONLY. Original imported A/B prefabs and " +
            "source humanoid Avatars at original prefab scale. Executions01/02/03, whole-pair yaw +90/-90. " +
            "A origin/yaw0; B (0,0,1.7)/yaw180 before pair rotation. No fighter adaptation, alignment, " +
            "height correction or root flattening. Event-free nonlooping clones, manual root motion 60Hz, " +
            "30Hz measurements plus exact endpoints and fractions; shorter clips hold end. " +
            "Exact selected BladeR 111 faces " +
            "(all localZ <= -.33) against all visible native B body skin triangles; both B weapons disabled. " +
            "Sheath/grip/guard excluded. Zero gap means geometric overlap, never approved damaging contact. " +
            "Sheets show fixed side/oblique views at coarse local/global minima, endpoints and fixed fractions. " +
            "Original source poses retained; actual Battle fighters remain the final integration targets.";

        [Serializable]
        sealed class NativeReferenceReport
        {
            public string scope = NativeReferenceScope;
            public string status = "RUNNING; partial native reference only";
            public string error;
            public string utc = DateTime.UtcNow.ToString("O");
            public string unityVersion = Application.unityVersion;
            public bool sourceGuardsPassed;
            public SourceRecord[] sources;
            public NativeReferenceFile[] guardedFiles;
            public List<NativeReferenceCase> cases = new List<NativeReferenceCase>();
        }

        [Serializable]
        sealed class NativeReferenceFile
        {
            public string path, sha256;
        }

        [Serializable]
        sealed class NativeReferenceCase
        {
            public int execution, direction;
            public string status = "RUNNING", error, bladeMesh;
            public string[] bodyMeshes, avatars, prefabs, sheets;
            public Vector3[] prefabScales, animatorLossyScales;
            public float[] avatarHumanScales;
            public float duration, attackDuration, receiverDuration;
            public NativeReferenceFrame minimum, start, end;
            public float[] sheetSeconds;
            public List<string> geometry = new List<string>();
            public List<NativeReferenceFrame> frames = new List<NativeReferenceFrame>();
        }

        [Serializable]
        sealed class NativeReferenceFrame
        {
            public float seconds, gap;
            public Vector3 bladePoint, bodyPoint;
            public NativeReferencePose[] poses;
        }

        [Serializable]
        sealed class NativeReferencePose
        {
            public string role, bone;
            public Vector3 worldPosition, localPosition, localScale, lossyScale;
            public Quaternion worldRotation, localRotation;
        }

        static NativeReferencePose NativeReferenceTransform(string role, string bone, Transform node)
        {
            var value = new NativeReferencePose
            {
                role = role, bone = bone, worldPosition = node.position, localPosition = node.localPosition,
                localScale = node.localScale, lossyScale = node.lossyScale,
                worldRotation = node.rotation, localRotation = node.localRotation
            };
            foreach (var vector in new[] { value.worldPosition, value.localPosition,
                value.localScale, value.lossyScale })
                for (int axis = 0; axis < 3; axis++)
                    if (!float.IsFinite(vector[axis]))
                        throw new InvalidOperationException("Nonfinite native transform " + bone);
            foreach (var rotation in new[] { value.worldRotation, value.localRotation })
                for (int axis = 0; axis < 4; axis++)
                    if (!float.IsFinite(rotation[axis]))
                        throw new InvalidOperationException("Nonfinite native rotation " + bone);
            return value;
        }

        static NativeReferencePose[] NativeReferencePoses(NativeReferenceActor[] actors)
        {
            var result = new List<NativeReferencePose>();
            foreach (var actor in actors)
            {
                result.Add(NativeReferenceTransform(actor.Source.role, "AnimatorRoot", actor.Animator.transform));
                foreach (var bone in new[] { HumanBodyBones.Hips, HumanBodyBones.Head,
                    HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                    result.Add(NativeReferenceTransform(actor.Source.role, bone.ToString(),
                        actor.Animator.GetBoneTransform(bone)));
                result.Add(NativeReferenceTransform(actor.Source.role, "BladeR", actor.Blade));
            }
            return result.ToArray();
        }

        static NativeReferenceFile[] NativeReferenceGuard(SourceRecord[] sources)
        {
            return sources.Select(s => s.path).Distinct()
                .SelectMany(p => AssetDatabase.GetDependencies(p, true)).Distinct()
                .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal))
                .SelectMany(p => new[] { p, p + ".meta" }).Where(File.Exists).OrderBy(p => p)
                .Select(p => new NativeReferenceFile { path = p, sha256 = NativeReferenceHash(p) }).ToArray();
        }

        static string NativeReferenceHash(string path)
        {
            using var hash = SHA256.Create();
            using var stream = File.OpenRead(path);
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
        }

        static string NativeReferenceSourceSnapshot(SourceRecord[] sources)
        {
            return string.Join("\n", sources.Select(s => JsonUtility.ToJson(s)));
        }
    }
}
