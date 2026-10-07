using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        internal static FrankTestDriver BuildHumanoidStudyDriver(Animator character, string fighter,
            string modelPath, AnimationClip[] clips, string outputPath, bool weapons)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (!model || !avatar || !avatar.isValid || !avatar.isHuman || !importer ||
                importer.animationType != ModelImporterAnimationType.Human || importer.optimizeGameObjects)
                throw new InvalidOperationException("Source requires an exposed valid humanoid rig: " + modelPath);
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = Object.Instantiate(model);
                SceneManager.MoveGameObjectToScene(root, scene);
                var target = Object.Instantiate(character.gameObject).GetComponent<Animator>();
                SceneManager.MoveGameObjectToScene(target.gameObject, scene);
                StripStudyComponents(root, false);
                StripStudyComponents(target.gameObject, false);
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                target.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                target.transform.localScale = character.transform.localScale;
                ReferencePose(root.transform, avatar);
                ReferencePose(target.transform, target.avatar);
                Func<string, Transform> sb = n => HumanBone(root.transform, avatar, n);
                Func<string, Transform> tb = n => HumanBone(target.transform, target.avatar, n);
                float sourceLeg = Vector3.Distance(sb("LeftUpperLeg").position, sb("LeftLowerLeg").position) +
                    Vector3.Distance(sb("LeftLowerLeg").position, sb("LeftFoot").position);
                float targetLeg = Vector3.Distance(tb("LeftUpperLeg").position, tb("LeftLowerLeg").position) +
                    Vector3.Distance(tb("LeftLowerLeg").position, tb("LeftFoot").position);
                if (!float.IsFinite(sourceLeg) || sourceLeg <= .0001f || targetLeg <= .0001f)
                    throw new InvalidOperationException("Invalid calibration leg lengths: " + modelPath);
                root.transform.localScale *= targetLeg / sourceLeg;
                var source = root.GetComponent<Animator>();
                if (!source)
                    source = root.AddComponent<Animator>();
                source.avatar = avatar;
                source.runtimeAnimatorController = null;
                source.applyRootMotion = true;
                source.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                source.enabled = true;
                foreach (var other in root.GetComponentsInChildren<Animator>(true).Where(a => a != source))
                    Object.DestroyImmediate(other);
                var meshes = root.GetComponentsInChildren<Renderer>(true);
                var axes = meshes.Where(r => weapons && IsStudyAxe(r.transform, root.transform)).ToArray();
                if (weapons && (axes.Length < 2 || !Bones(root.transform).ContainsKey("L_axe_wp") ||
                    !Bones(root.transform).ContainsKey("R_axe_wp")))
                    throw new InvalidOperationException("Native axe meshes or sockets missing: " + modelPath);
                foreach (var renderer in meshes)
                {
                    if (!axes.Contains(renderer))
                    {
                        Object.DestroyImmediate(renderer);
                        continue;
                    }
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(StudyAxeMaterial).ToArray();
                    renderer.enabled = true;
                }
                var pose = root.AddComponent<FrankPoseRetarget>();
                pose.enabled = false;
                pose.driver = source;
                pose.sourceHumanAvatar = avatar;
                pose.character = target;
                pose.sourceHips = sb("Hips");
                pose.targetHips = tb("Hips");
                pose.characterName = fighter;
                pose.originalBody = Array.Empty<Renderer>();
                pose.weaponRenderers = axes;
                pose.sourceClips = clips;
                pose.transferFingers = true;
                var limbs = new List<FrankPoseRetarget.Limb>();
                foreach (string side in new[] { "Left", "Right" })
                foreach (bool arm in new[] { true, false })
                {
                    string upper = side + (arm ? "UpperArm" : "UpperLeg");
                    string middle = side + (arm ? "LowerArm" : "LowerLeg");
                    string end = side + (arm ? "Hand" : "Foot");
                    var limb = new FrankPoseRetarget.Limb
                    {
                        sourceUpper = sb(upper), sourceMiddle = sb(middle), sourceEnd = sb(end),
                        upper = tb(upper), middle = tb(middle), end = tb(end),
                        middleRest = tb(middle).localPosition, endRest = tb(end).localPosition,
                        rotationOffset = Quaternion.Inverse(sb(end).rotation) * tb(end).rotation,
                        alignGrip = weapons && arm
                    };
                    if (arm)
                    {
                        var sm = sb(side + " Middle Proximal");
                        var sr = sb(side + " Ring Proximal");
                        var tm = tb(side + " Middle Proximal");
                        var tr = tb(side + " Ring Proximal");
                        var sf = Quaternion.LookRotation(sm.position - sb(end).position,
                            Vector3.Cross(sm.position - sb(end).position, sr.position - sm.position));
                        var tf = Quaternion.LookRotation(tm.position - tb(end).position,
                            Vector3.Cross(tm.position - tb(end).position, tr.position - tm.position));
                        limb.rotationOffset = Quaternion.Inverse(sb(end).rotation) * sf *
                            Quaternion.Inverse(tf) * tb(end).rotation;
                        limb.sourceKnuckle = sm;
                        limb.sourceFingerJoint = sb(side + " Middle Intermediate");
                        limb.targetKnuckle = tm;
                        limb.targetFingerJoint = tb(side + " Middle Intermediate");
                    }
                    limbs.Add(limb);
                }
                pose.limbs = limbs.ToArray();
                pose.fingers = (from side in new[] { "Left", "Right" }
                    from digit in new[] { "Thumb", "Index", "Middle", "Ring", "Little" }
                    let prefix = side + " " + digit + " "
                    where avatar.humanDescription.human.Any(h => h.humanName == prefix + "Distal") &&
                        target.avatar.humanDescription.human.Any(h => h.humanName == prefix + "Distal")
                    select new FrankPoseRetarget.Finger
                    {
                        sourceProximal = sb(prefix + "Proximal"),
                        sourceIntermediate = sb(prefix + "Intermediate"), sourceDistal = sb(prefix + "Distal"),
                        proximal = tb(prefix + "Proximal"), intermediate = tb(prefix + "Intermediate"),
                        distal = tb(prefix + "Distal")
                    }).ToArray();
                var driver = root.AddComponent<FrankTestDriver>();
                driver.pose = pose;
                driver.characterScale = target.transform.localScale;
                driver.hips = PathOf(pose.targetHips, target.transform);
                driver.targetLimbs = pose.limbs.Select(l => new FrankTestDriver.TargetLimb
                {
                    upper = PathOf(l.upper, target.transform), middle = PathOf(l.middle, target.transform),
                    end = PathOf(l.end, target.transform), knuckle = PathOf(l.targetKnuckle, target.transform),
                    joint = PathOf(l.targetFingerJoint, target.transform)
                }).ToArray();
                driver.targetFingers = pose.fingers.Select(f => new FrankTestDriver.TargetFinger
                {
                    proximal = PathOf(f.proximal, target.transform),
                    intermediate = PathOf(f.intermediate, target.transform),
                    distal = PathOf(f.distal, target.transform)
                }).ToArray();
                Object.DestroyImmediate(target.gameObject);
                root.name = fighter + "_" + AssetDatabase.AssetPathToGUID(modelPath);
                return PrefabUtility.SaveAsPrefabAsset(root, outputPath).GetComponent<FrankTestDriver>();
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        internal static void StripStudyComponents(GameObject root, bool keepRetarget)
        {
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour && !(keepRetarget && (behaviour is FrankTestDriver || behaviour is FrankPoseRetarget)))
                    Object.DestroyImmediate(behaviour);
            foreach (var camera in root.GetComponentsInChildren<Camera>(true))
                Object.DestroyImmediate(camera);
            foreach (var light in root.GetComponentsInChildren<Light>(true))
                Object.DestroyImmediate(light);
            foreach (var audio in root.GetComponentsInChildren<AudioSource>(true))
                Object.DestroyImmediate(audio);
            foreach (var listener in root.GetComponentsInChildren<AudioListener>(true))
                Object.DestroyImmediate(listener);
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);
            foreach (var animation in root.GetComponentsInChildren<Animation>(true))
                Object.DestroyImmediate(animation);
            foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
                Object.DestroyImmediate(body);
        }

        static bool IsStudyAxe(Transform node, Transform root)
        {
            for (var t = node; t && t != root; t = t.parent)
                if (t.name.IndexOf("axe", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        static Material StudyAxeMaterial(Material source)
        {
            string id = source ? CombatExpansionInventory.Identity(source).Replace(':', '_') : "default";
            string path = CombatExpansionHumanoidStudy.AssetsRoot + "/Materials/Axe_" + id + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material)
                return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader)
                throw new InvalidOperationException("URP Lit shader is required for owned axe materials");
            material = new Material(shader) { name = "Study Axe " + id };
            if (source && source.HasProperty("_MainTex"))
                material.SetTexture("_BaseMap", source.GetTexture("_MainTex"));
            material.SetColor("_BaseColor", source && source.HasProperty("_Color") ? source.color : Color.gray);
            material.SetFloat("_Smoothness", .32f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
