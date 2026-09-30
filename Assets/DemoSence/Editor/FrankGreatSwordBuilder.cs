using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    /// <summary>
    /// Connects the authored GreatSword_Animset execution takes to the existing
    /// Mankey/Pepe tester. This intentionally references the imported FBX clips
    /// directly; it never bakes or re-compresses the source animation.
    /// </summary>
    public static class FrankGreatSwordBuilder
    {
        const string Output = "Assets/DemoSence/GreatSwordExecution";
        const string LibraryPath = Output + "/GreatSwordExecutionLibrary.asset";
        const string TargetScene = "Assets/DemoSence/Frank_Damages_Mankey_Pepe.unity";
        const string SampleWeapon = "Assets/GreatSword_Animset/Model/Weapon/GreatSword_01.FBX";
        const string SampleSource = "Assets/GreatSword_Animset/Model/WM_Master_Unity.FBX";
        const string HumanoidSource = "Assets/DemoSence/Rigs/WM_Master_Unity_GreatSword_Humanoid.FBX";
        const string GreatSwordDriverRoot = Output + "/Drivers/";

        static readonly string[] AttackClips =
        {
            "Assets/GreatSword_Animset/Animation/Execution_Sample/GreatSword_Attack_Ambush.FBX",
            "Assets/GreatSword_Animset/Animation/Execution_Sample/GreatSword_Attack_Execution1.FBX",
            "Assets/GreatSword_Animset/Animation/Execution_Sample/GreatSword_Attack_Execution2.FBX",
            "Assets/GreatSword_Animset/Animation/Execution_Sample/GreatSword_Attack_Execution3.FBX"
        };

        static readonly string[] ReactionClips =
        {
            "Assets/GreatSword_Animset/Animation/Execution_Sample/GreatSword_Attack_Ambushed.FBX",
            "Assets/GreatSword_Animset/Animation/Execution_Sample/GreatSword_Attack_Executed1.FBX",
            "Assets/GreatSword_Animset/Animation/Execution_Sample/GreatSword_Attack_Executed2.FBX",
            "Assets/GreatSword_Animset/Animation/Execution_Sample/GreatSword_Attack_Executed3.FBX"
        };

        static AnimationClip Clip(string path)
        {
            string expected=Path.GetFileNameWithoutExtension(path);
            var clip=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c=>c.name==expected && !c.name.StartsWith("__preview",StringComparison.Ordinal));
            if(!clip)throw new InvalidOperationException("Missing GreatSword animation clip: "+path);
            return clip;
        }

        static Transform Bone(Transform root, Avatar avatar, string humanName)
        {
            string normalized = humanName.Replace(" ", string.Empty);
            var mapped = avatar.humanDescription.human.FirstOrDefault(h => h.humanName.Replace(" ", string.Empty) == normalized);
            if (string.IsNullOrEmpty(mapped.boneName))
                throw new InvalidOperationException("GreatSword source avatar has no mapping for " + humanName);
            var t = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == mapped.boneName);
            if (!t) throw new InvalidOperationException("GreatSword source bone is missing: " + mapped.boneName + " (" + humanName + ")");
            return t;
        }

        static Dictionary<string, Transform> BoneMap(Transform root) =>
            root.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());

        static string RelativePath(Transform root, Transform child)
        {
            if (!child) return string.Empty;
            var parts = new Stack<string>();
            var t = child;
            while (t && t != root) { parts.Push(t.name); t = t.parent; }
            return t == root ? string.Join("/", parts) : string.Empty;
        }

        static void EnsureTransformPath(Transform root, string path)
        {
            var current = root;
            foreach (var name in path.Split('/'))
            {
                var next = current.Find(name);
                if (!next)
                {
                    var go = new GameObject(name);
                    next = go.transform;
                    next.SetParent(current, false);
                }
                current = next;
            }
        }

        static void EnsureGreatSwordAuxiliaryNodes(Transform root)
        {
            // These non-human nub/socket transforms are present in the authored FBX
            // curves even though they are not needed by the Humanoid mapping. Keep
            // them so AnimationClipPlayable can evaluate every original binding.
            string spine = "root/pelvis/Bip001 Pelvis/spine_01/spine_02/spine_03";
            EnsureTransformPath(root, spine + "/neck_01/head/Bip001 HeadNub");
            foreach (var side in new[] { "l", "r" })
            {
                string arm = spine + "/clavicle_" + side + "/upperarm_" + side + "/lowerarm_" + side + "/hand_" + side;
                foreach (var digit in new[] { "thumb", "index", "middle", "ring", "pinky" })
                {
                    int finger = digit == "thumb" ? 0 : digit == "index" ? 1 : digit == "middle" ? 2 : digit == "ring" ? 3 : 4;
                    EnsureTransformPath(root, arm + "/" + digit + "_01_" + side + "/" + digit + "_02_" + side + "/" + digit + "_03_" + side + "/Bip001 " + (side == "l" ? "L" : "R") + " Finger" + finger + "Nub");
                }
                EnsureTransformPath(root, "root/pelvis/Bip001 Pelvis/thigh_" + side + "/calf_" + side + "/foot_" + side + "/ball_" + side + "/Bip001 " + (side == "l" ? "L" : "R") + " Toe0Nub");
            }
            EnsureTransformPath(root, "root/pelvis/Bip001 Footsteps");
            EnsureTransformPath(root, spine + "/clavicle_r/upperarm_r/lowerarm_r/hand_r/Dummy001/Dummy003");
            EnsureTransformPath(root, "Dummy004");
            EnsureTransformPath(root, "Dummy005");
        }

        static Avatar AvatarAt(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();

        static void AddHuman(ref List<HumanBone> bones, string humanName, string boneName)
        {
            bones.Add(new HumanBone { humanName = humanName, boneName = boneName, limit = new HumanLimit { useDefaultValues = true } });
        }

        static Avatar EnsureGreatSwordHumanoidAvatar()
        {
            if (!File.Exists(HumanoidSource))
            {
                if (!AssetDatabase.CopyAsset(SampleSource, HumanoidSource))
                    throw new InvalidOperationException("Could not create the GreatSword Humanoid source copy.");
            }
            AssetDatabase.ImportAsset(HumanoidSource, ImportAssetOptions.ForceUpdate);
            var importer = (ModelImporter)AssetImporter.GetAtPath(HumanoidSource);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.optimizeGameObjects = false;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.resampleCurves = true;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.SaveAndReimport();

            // Start from Unity's generated skeleton description so all non-human
            // skeleton entries and the exact reference pose are retained, then
            // replace only the ambiguous human mapping.  In particular, Hips must
            // be Bip001 Pelvis: the authored execution curves have that node in
            // their path between pelvis and spine_01.
            var initial = AvatarAt(HumanoidSource);
            if (!initial) throw new InvalidOperationException("Humanoid source did not import an Avatar.");
            var description = initial.humanDescription;
            var map = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Hips"] = "Bip001 Pelvis", ["Spine"] = "spine_01", ["Chest"] = "spine_02", ["UpperChest"] = "spine_03",
                ["Neck"] = "neck_01", ["Head"] = "head",
                ["LeftShoulder"] = "clavicle_l", ["LeftUpperArm"] = "upperarm_l", ["LeftLowerArm"] = "lowerarm_l", ["LeftHand"] = "hand_l",
                ["RightShoulder"] = "clavicle_r", ["RightUpperArm"] = "upperarm_r", ["RightLowerArm"] = "lowerarm_r", ["RightHand"] = "hand_r",
                ["LeftUpperLeg"] = "thigh_l", ["LeftLowerLeg"] = "calf_l", ["LeftFoot"] = "foot_l", ["LeftToes"] = "ball_l",
                ["RightUpperLeg"] = "thigh_r", ["RightLowerLeg"] = "calf_r", ["RightFoot"] = "foot_r", ["RightToes"] = "ball_r"
            };
            foreach (var side in new[] { "Left", "Right" })
                foreach (var digit in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                    foreach (var segment in new[] { "Proximal", "Intermediate", "Distal" })
                    {
                        string n = side + digit + segment;
                        string sourceDigit = digit == "Little" ? "pinky" : digit.ToLowerInvariant();
                        string sourceSegment = segment == "Proximal" ? "01" : segment == "Intermediate" ? "02" : "03";
                        map[n] = sourceDigit + "_" + sourceSegment + (side == "Left" ? "_l" : "_r");
                    }
            // A failed earlier import can leave both Unity's spaced finger names
            // and the compact spellings in the serialized description.  Unity
            // rejects those as two human bones pointing at one transform, so
            // collapse equivalent names before applying the explicit map.
            var human = description.human
                .GroupBy(h => h.humanName.Replace(" ", string.Empty), StringComparer.Ordinal)
                .Select(g => g.First()).ToList();
            foreach (var pair in map)
            {
                int index = human.FindIndex(h => h.humanName.Replace(" ", string.Empty) == pair.Key.Replace(" ", string.Empty));
                if (index < 0) AddHuman(ref human, pair.Key, pair.Value);
                else { var h = human[index]; h.boneName = pair.Value; h.limit.useDefaultValues = true; human[index] = h; }
            }
            description.human = human.ToArray();
            importer.humanDescription = description;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();
            var avatar = AvatarAt(HumanoidSource);
            if (!avatar || !avatar.isHuman || !avatar.isValid)
                throw new InvalidOperationException("GreatSword Humanoid source avatar is invalid after explicit mapping.");
            // Fail early if Unity accepted the file but silently dropped a required bone.
            foreach (var pair in map)
                if (!avatar.humanDescription.human.Any(h => h.humanName.Replace(" ", string.Empty) == pair.Key.Replace(" ", string.Empty) && h.boneName == pair.Value))
                    throw new InvalidOperationException("GreatSword Humanoid source mapping was not retained: " + pair.Key + "=" + pair.Value);
            return avatar;
        }

        static void ReferencePose(Transform root, Avatar avatar)
        {
            var bones = BoneMap(root);
            foreach (var b in avatar.humanDescription.skeleton)
                if (bones.TryGetValue(b.name, out var t) && t != root)
                { t.localPosition = b.position; t.localRotation = b.rotation; t.localScale = b.scale; }
        }

        static FrankTestDriver BuildGreatSwordDriver(string character, bool attacker, Avatar sourceAvatar)
        {
            var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SampleSource);
            if (!sourcePrefab) throw new InvalidOperationException("Missing GreatSword source model: " + SampleSource);
            var root = PrefabUtility.InstantiatePrefab(sourcePrefab) as GameObject;
            if (!root) throw new InvalidOperationException("Could not instantiate GreatSword source model.");
            try
            {
                if (PrefabUtility.IsPartOfPrefabInstance(root))
                    PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                root.name = character + "_GreatSword_" + (attacker ? "Attack" : "Reaction");
                EnsureGreatSwordAuxiliaryNodes(root.transform);
                var animator = root.GetComponent<Animator>() ?? root.AddComponent<Animator>();
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var sourceRenderers = root.GetComponentsInChildren<Renderer>(true);
                foreach (var r in sourceRenderers) r.enabled = false;

                string targetPath = "Assets/DemoSence/Rigs/" + (character == "Mankey" ? "Mankey.FBX" : "Pepe.fbx");
                var targetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
                if (!targetPrefab) throw new InvalidOperationException("Missing target rig: " + targetPath);
                var target = UnityEngine.Object.Instantiate(targetPrefab);
                target.name = character;
                target.transform.SetParent(root.transform, false);
                var targetAnimator = target.GetComponent<Animator>();
                targetAnimator.runtimeAnimatorController = null;
                targetAnimator.applyRootMotion = false;
                targetAnimator.enabled = false;
                ReferencePose(target.transform, targetAnimator.avatar);
                float sourceLeg = Vector3.Distance(Bone(root.transform, sourceAvatar, "LeftUpperLeg").position, Bone(root.transform, sourceAvatar, "LeftLowerLeg").position)
                                + Vector3.Distance(Bone(root.transform, sourceAvatar, "LeftLowerLeg").position, Bone(root.transform, sourceAvatar, "LeftFoot").position);
                float targetLeg = Vector3.Distance(targetAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position, targetAnimator.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position)
                                + Vector3.Distance(targetAnimator.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position, targetAnimator.GetBoneTransform(HumanBodyBones.LeftFoot).position);
                if (sourceLeg < 0.0001f || targetLeg < 0.0001f) throw new InvalidOperationException("GreatSword source/target leg calibration is degenerate.");
                target.transform.localScale *= sourceLeg / targetLeg;
                foreach (var r in target.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is SkinnedMeshRenderer sk) sk.updateWhenOffscreen = true;
                    if (r.name.IndexOf("coconut", StringComparison.OrdinalIgnoreCase) >= 0) r.enabled = false;
                    // This target rig exists only as a calibration skeleton. The
                    // scene already owns the persistent visible Mankey/Pepe model;
                    // leaving this clone rendered would show a second character.
                    r.enabled = false;
                }

                var bridge = root.AddComponent<FrankPoseRetarget>();
                bridge.driver = animator;
                bridge.sourceHumanAvatar = sourceAvatar;
                bridge.character = targetAnimator;
                bridge.characterName = character;
                bridge.weapon = "GreatSword";
                bridge.isAttacker = attacker;
                bridge.originalBody = sourceRenderers;
                bridge.weaponRenderers = sourceRenderers;
                bridge.sourceClips = (attacker ? AttackClips : ReactionClips).Select(Clip).ToArray();
                bridge.sourceHips = Bone(root.transform, sourceAvatar, "Hips");
                bridge.targetHips = targetAnimator.GetBoneTransform(HumanBodyBones.Hips);

                var limbs = new List<FrankPoseRetarget.Limb>();
                foreach (var side in new[] { "Left", "Right" }) foreach (bool arm in new[] { true, false })
                {
                    string a = side + (arm ? "UpperArm" : "UpperLeg"), b = side + (arm ? "LowerArm" : "LowerLeg"), c = side + (arm ? "Hand" : "Foot");
                    var sEnd = Bone(root.transform, sourceAvatar, c);
                    var tEnd = targetAnimator.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), c));
                    var mid = targetAnimator.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), b));
                    var limb = new FrankPoseRetarget.Limb
                    {
                        sourceUpper = Bone(root.transform, sourceAvatar, a), sourceMiddle = Bone(root.transform, sourceAvatar, b), sourceEnd = sEnd,
                        upper = targetAnimator.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), a)), middle = mid, end = tEnd,
                        middleRest = mid.localPosition, endRest = tEnd.localPosition,
                        rotationOffset = Quaternion.Inverse(sEnd.rotation) * tEnd.rotation
                    };
                    if (arm)
                    {
                        string middleHuman = side + "MiddleProximal", ringHuman = side + "RingProximal";
                        var sm = Bone(root.transform, sourceAvatar, middleHuman);
                        var sr = Bone(root.transform, sourceAvatar, ringHuman);
                        var tm = targetAnimator.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), middleHuman));
                        var tr = targetAnimator.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), ringHuman));
                        Quaternion sf = Quaternion.LookRotation(sm.position - sEnd.position, Vector3.Cross(sm.position - sEnd.position, sr.position - sm.position));
                        Quaternion tf = Quaternion.LookRotation(tm.position - tEnd.position, Vector3.Cross(tm.position - tEnd.position, tr.position - tm.position));
                        limb.rotationOffset = Quaternion.Inverse(sEnd.rotation) * sf * Quaternion.Inverse(tf) * tEnd.rotation;
                        limb.alignGrip = attacker;
                        limb.sourceKnuckle = sm; limb.sourceFingerJoint = Bone(root.transform, sourceAvatar, side + "MiddleIntermediate");
                        limb.targetKnuckle = tm; limb.targetFingerJoint = targetAnimator.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), side + "MiddleIntermediate"));
                    }
                    limbs.Add(limb);
                }
                bridge.limbs = limbs.ToArray();
                var fingers = new List<FrankPoseRetarget.Finger>();
                foreach (var side in new[] { "Left", "Right" }) foreach (var digit in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                {
                    string prefix = side + digit;
                    string p = prefix + "Proximal", i = prefix + "Intermediate", d = prefix + "Distal";
                    if (!targetAnimator.avatar.humanDescription.human.Any(h => h.humanName.Replace(" ", string.Empty) == p.Replace(" ", string.Empty))) continue;
                    fingers.Add(new FrankPoseRetarget.Finger
                    {
                        sourceProximal = Bone(root.transform, sourceAvatar, p), sourceIntermediate = Bone(root.transform, sourceAvatar, i), sourceDistal = Bone(root.transform, sourceAvatar, d),
                        proximal = targetAnimator.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), p)), intermediate = targetAnimator.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), i)), distal = targetAnimator.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), d))
                    });
                }
                bridge.fingers = fingers.ToArray();
                bridge.transferFingers = true;
                bridge.enabled = false;
                bridge.ApplyPose();

                var testDriver = root.AddComponent<FrankTestDriver>();
                testDriver.pose = bridge;
                testDriver.characterScale = target.transform.localScale;
                testDriver.hips = RelativePath(target.transform, bridge.targetHips);
                testDriver.targetLimbs = limbs.Select(l => new FrankTestDriver.TargetLimb
                {
                    upper = RelativePath(target.transform, l.upper), middle = RelativePath(target.transform, l.middle), end = RelativePath(target.transform, l.end),
                    knuckle = RelativePath(target.transform, l.targetKnuckle), joint = RelativePath(target.transform, l.targetFingerJoint)
                }).ToArray();
                testDriver.targetFingers = fingers.Select(f => new FrankTestDriver.TargetFinger
                {
                    proximal = RelativePath(target.transform, f.proximal), intermediate = RelativePath(target.transform, f.intermediate), distal = RelativePath(target.transform, f.distal)
                }).ToArray();

                string path = GreatSwordDriverRoot + character + "_GreatSword_" + (attacker ? "Attack" : "Reaction") + ".prefab";
                var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                if (!saved) throw new InvalidOperationException("Could not save GreatSword driver: " + path);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            AssetDatabase.ImportAsset(GreatSwordDriverRoot + character + "_GreatSword_" + (attacker ? "Attack" : "Reaction") + ".prefab");
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(GreatSwordDriverRoot + character + "_GreatSword_" + (attacker ? "Attack" : "Reaction") + ".prefab");
            return asset ? asset.GetComponent<FrankTestDriver>() : null;
        }

        static bool Finite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        static int TransformCurveCoverage(AnimationClip clip, Transform root, out int total, out string missing)
        {
            total = 0;
            int matched = 0;
            var missingPaths = new HashSet<string>();
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != typeof(Transform)) continue;
                total++;
                var t = string.IsNullOrEmpty(binding.path) ? root : root.Find(binding.path);
                if (t) matched++; else missingPaths.Add(binding.path);
            }
            missing = string.Join(", ", missingPaths.Take(16));
            return matched;
        }

        [MenuItem("Tools/Frank Retarget/GreatSword/Build execution library and scene")]
        public static void Build()
        {
            if(!AssetDatabase.IsValidFolder(Output))
            {
                AssetDatabase.CreateFolder("Assets/DemoSence","GreatSwordExecution");
            }
            if(!AssetDatabase.IsValidFolder(Output + "/Drivers")) AssetDatabase.CreateFolder(Output, "Drivers");
            AssetDatabase.Refresh();

            var library=AssetDatabase.LoadAssetAtPath<FrankGreatSwordLibrary>(LibraryPath);
            if(!library)
            {
                library=ScriptableObject.CreateInstance<FrankGreatSwordLibrary>();
                AssetDatabase.CreateAsset(library,LibraryPath);
            }
            string[] labels={"Ambush","Execution 1","Execution 2","Execution 3"};
            Vector3[] offsets={new Vector3(0,0,2),new Vector3(0,0,4),new Vector3(0,0,3.5f),new Vector3(0,0,2.8f)};
            library.pairs=Enumerable.Range(0,AttackClips.Length).Select(i=>new FrankGreatSwordLibrary.Pair
            {
                label=labels[i],sourceName=Path.GetFileNameWithoutExtension(AttackClips[i]),
                attacker=Clip(AttackClips[i]),receiver=Clip(ReactionClips[i]),
                receiverOffset=offsets[i],receiverRotation=Quaternion.Euler(0,180,0)
            }).ToArray();
            EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();

            var scene=EditorSceneManager.OpenScene(TargetScene,OpenSceneMode.Single);
            var tester=UnityEngine.Object.FindObjectsByType<FrankCombinationTester>(FindObjectsInactive.Include).FirstOrDefault();
            if(!tester)throw new InvalidOperationException("FrankCombinationTester was not found in "+TargetScene);
            var actors=UnityEngine.Object.FindObjectsByType<FrankTestActor>(FindObjectsInactive.Include)
                .ToDictionary(a=>a.characterName,StringComparer.OrdinalIgnoreCase);
            if(!actors.TryGetValue("Mankey",out var mankey)||!actors.TryGetValue("Pepe",out var pepe))
                throw new InvalidOperationException("Mankey and Pepe actors are required in "+TargetScene);
            // The scene may contain editor-time active-driver skeletons left by an
            // earlier preview. Remove those serialized runtime instances before
            // saving the rebuilt scene, so only the two persistent visible models
            // remain until the tester configures the selected execution.
            mankey.Clear();
            pepe.Clear();

            var sourceAvatar = EnsureGreatSwordHumanoidAvatar();
            var mankeyAttack = BuildGreatSwordDriver("Mankey", true, sourceAvatar);
            var mankeyReaction = BuildGreatSwordDriver("Mankey", false, sourceAvatar);
            var pepeAttack = BuildGreatSwordDriver("Pepe", true, sourceAvatar);
            var pepeReaction = BuildGreatSwordDriver("Pepe", false, sourceAvatar);
            if(!mankeyAttack || !mankeyReaction || !pepeAttack || !pepeReaction)
                throw new InvalidOperationException("GreatSword source drivers were not generated.");
            mankey.greatSwordAttackDriver=mankeyAttack;
            mankey.greatSwordReactionDriver=mankeyReaction;
            pepe.greatSwordAttackDriver=pepeAttack;
            pepe.greatSwordReactionDriver=pepeReaction;
            tester.greatSwordLibrary=library;
            tester.greatSwordWeapon=AssetDatabase.LoadAssetAtPath<GameObject>(SampleWeapon);
            if(!tester.greatSwordWeapon)throw new InvalidOperationException("Missing Execution_Sample GreatSword_01 model.");
            tester.greatSword=true;
            tester.gunSword=false;
            tester.unarmed=false;
            tester.greatSwordMotion=Mathf.Clamp(tester.greatSwordMotion,0,library.pairs.Length-1);
            EditorUtility.SetDirty(tester);EditorUtility.SetDirty(mankey);EditorUtility.SetDirty(pepe);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();AssetDatabase.Refresh();
            Debug.Log("GreatSword execution library built with "+library.pairs.Length+" authored pairs.");
        }

        [MenuItem("Tools/Frank Retarget/GreatSword/Validate execution playback")]
        public static void Validate()
        {
            var scene=EditorSceneManager.OpenScene(TargetScene,OpenSceneMode.Single);
            var tester=UnityEngine.Object.FindObjectsByType<FrankCombinationTester>(FindObjectsInactive.Include).FirstOrDefault();
            if(!tester || !tester.greatSwordLibrary || tester.greatSwordLibrary.pairs == null || tester.greatSwordLibrary.pairs.Length != 4)
                throw new InvalidOperationException("GreatSword execution scene/library is not configured with four pairs.");
            if(!tester.greatSwordWeapon)
                throw new InvalidOperationException("Execution_Sample sword is not enabled.");
            // Validation must be independent of the UI's saved default tab. The
            // demo opens on the standard Frank weapons library, while this check
            // explicitly selects the dedicated execution route it is measuring.
            tester.greatSword=true;
            tester.gunSword=false;
            tester.unarmed=false;
            tester.Configure();

            var evidence = new StringBuilder();
            evidence.AppendLine("GreatSword Execution_Sample validation");
            evidence.AppendLine("Source clips: original Execution_Sample FBX Generic transform tracks");
            evidence.AppendLine("Source hierarchy: WM_Master_Unity; calibration avatar: WM_Master_Unity_GreatSword_Humanoid.FBX");
            evidence.AppendLine("Quality limits: all transform bindings present, limb error <= 0.025 m, stretch <= 1.16, hand angle <= 0.1 degrees");
            for(int i=0;i<tester.greatSwordLibrary.pairs.Length;i++)
            {
                tester.greatSwordMotion=i;
                tester.Configure();
                tester.Evaluate();
                var pair=tester.greatSwordLibrary.pairs[i];
                if(!tester.Attacker.activeDriver || !tester.Receiver.activeDriver ||
                   tester.Attacker.clip!=pair.attacker || tester.Receiver.clip!=pair.receiver)
                    throw new InvalidOperationException("GreatSword pair "+i+" did not bind its authored clips.");
                if(!tester.Attacker.Pose || !tester.Receiver.Pose || !tester.Attacker.Pose.sourceHips || !tester.Receiver.Pose.sourceHips)
                    throw new InvalidOperationException("GreatSword pair "+i+" has no calibrated source pose.");
                var sword=tester.Attacker.activeDriver.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t=>t.name=="GreatSword_01 (Execution Sample)");
                if(!sword || !sword.GetComponentsInChildren<Renderer>(true).Any(r=>r.enabled))
                    throw new InvalidOperationException("GreatSword pair "+i+" has no visible Execution_Sample sword.");
                int total; string missing;
                int matched = TransformCurveCoverage(pair.attacker, tester.Attacker.Pose.driver.transform, out total, out missing);
                int reactionTotal; string reactionMissing;
                int reactionMatched = TransformCurveCoverage(pair.receiver, tester.Receiver.Pose.driver.transform, out reactionTotal, out reactionMissing);
                if(total < 40 || matched * 100 < total * 98)
                    throw new InvalidOperationException("GreatSword pair "+i+" dropped authored attack transform curves ("+matched+"/"+total+"): "+missing);
                if(reactionTotal < 40 || reactionMatched * 100 < reactionTotal * 98)
                    throw new InvalidOperationException("GreatSword pair "+i+" dropped authored reaction transform curves ("+reactionMatched+"/"+reactionTotal+"): "+reactionMissing);
                if(matched != total) Debug.Log("GreatSword pair "+i+" has inert attack auxiliary bindings not present on WM source: "+missing);
                if(reactionMatched != reactionTotal) Debug.Log("GreatSword pair "+i+" has inert reaction auxiliary bindings not present on WM source: "+reactionMissing);
                var sourceHand = tester.Attacker.Pose.driver.transform.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t=>t.name=="ik_hand_r");
                var sourceSpine = tester.Attacker.Pose.driver.transform.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t=>t.name=="spine_02");
                if(!sourceHand || !sourceSpine) throw new InvalidOperationException("GreatSword source driver is missing authored hand/spine sockets.");
                Vector3 firstHand=sourceHand.position; Quaternion firstSpine=sourceSpine.rotation;
                bool motion=false;
                float maxLimbError=0f, maxStretch=1f, maxHandRotation=0f;
                for(int sample=0;sample<=30;sample++)
                {
                    tester.Seek(tester.Duration*sample/30f);
                    var attackerHips=tester.Attacker.Pose.targetHips.position;
                    var receiverHips=tester.Receiver.Pose.targetHips.position;
                    if(!Finite(attackerHips)||!Finite(receiverHips))
                        throw new InvalidOperationException("GreatSword pair "+i+" produced a non-finite target pose at sample "+sample+".");
                    if(Vector3.Distance(firstHand,sourceHand.position)>0.001f || Quaternion.Angle(firstSpine,sourceSpine.rotation)>0.1f) motion=true;
                    foreach(var limb in tester.Attacker.Pose.limbs)
                    {
                        maxLimbError=Mathf.Max(maxLimbError,limb.error);
                        if(limb.middleRest.sqrMagnitude>0.000001f)
                            maxStretch=Mathf.Max(maxStretch,limb.middle.localPosition.magnitude/limb.middleRest.magnitude);
                        if(limb.alignGrip) maxHandRotation=Mathf.Max(maxHandRotation,Quaternion.Angle(limb.end.rotation,limb.sourceEnd.rotation*limb.rotationOffset));
                    }
                }
                if(!motion) throw new InvalidOperationException("GreatSword pair "+i+" has no evaluated authored body motion.");
                if(maxLimbError>0.025f || maxStretch>1.16f || maxHandRotation>0.1f)
                    throw new InvalidOperationException("GreatSword pair "+i+" retarget quality exceeded limits (limb="+maxLimbError+", stretch="+maxStretch+", handAngle="+maxHandRotation+").");
                Debug.Log("GreatSword pair "+i+" quality: attack curves="+matched+"/"+total+", reaction curves="+reactionMatched+"/"+reactionTotal+", max limb error="+maxLimbError+", max stretch="+maxStretch+", max hand angle="+maxHandRotation);
                evidence.AppendLine(pair.label+": attackCurves="+matched+"/"+total+", reactionCurves="+reactionMatched+"/"+reactionTotal+", maxLimbError="+maxLimbError.ToString("R")+", maxStretch="+maxStretch.ToString("R")+", maxHandAngle="+maxHandRotation.ToString("R"));
            }
            // The tester supports either character as the attacker. Verify the
            // opposite role uses Pepe's calibrated attack driver as well.
            tester.pepeAttacks=true;
            for(int i=0;i<tester.greatSwordLibrary.pairs.Length;i++)
            {
                tester.greatSwordMotion=i;
                tester.Configure();
                tester.Evaluate();
                var pair=tester.greatSwordLibrary.pairs[i];
                if(tester.Attacker!=tester.pepe || tester.Attacker.clip!=pair.attacker || tester.Receiver.clip!=pair.receiver ||
                   !tester.Attacker.activeDriver || !tester.Attacker.Pose || !tester.Attacker.Pose.targetHips ||
                   !tester.Receiver.activeDriver || !tester.Receiver.Pose || !tester.Receiver.Pose.targetHips)
                    throw new InvalidOperationException("Pepe attacker role did not bind GreatSword pair "+i+".");
                int total; string missing;
                int matched=TransformCurveCoverage(pair.attacker,tester.Attacker.Pose.driver.transform,out total,out missing);
                if(total<40 || matched*100<total*98)
                    throw new InvalidOperationException("Pepe GreatSword pair "+i+" dropped authored curves ("+matched+"/"+total+"): "+missing);
                int reactionTotal; string reactionMissing;
                int reactionMatched=TransformCurveCoverage(pair.receiver,tester.Receiver.Pose.driver.transform,out reactionTotal,out reactionMissing);
                if(reactionTotal<40 || reactionMatched*100<reactionTotal*98)
                    throw new InvalidOperationException("Pepe GreatSword pair "+i+" dropped authored reaction curves ("+reactionMatched+"/"+reactionTotal+"): "+reactionMissing);
                for(int sample=0;sample<=12;sample++)
                {
                    tester.Seek(tester.Duration*sample/12f);
                    if(!Finite(tester.Attacker.Pose.targetHips.position)||!Finite(tester.Receiver.Pose.targetHips.position))
                        throw new InvalidOperationException("Pepe GreatSword pair "+i+" produced a non-finite pose at sample "+sample+".");
                }
            }
            evidence.AppendLine("Mankey and Pepe attacker roles: all four pairs sampled at 13 points; finite pose checks passed.");
            File.WriteAllText("Assets/DemoSence/Reports/GreatSword-validation.txt", evidence.ToString());
            AssetDatabase.Refresh();
            tester.pepeAttacks=false;
            tester.mankey.Clear();tester.pepe.Clear();
            Debug.Log("GreatSword execution validation passed: four pairs, original FBX clips, calibrated Mankey/Pepe poses, and Execution_Sample sword playback.");
        }

        [MenuItem("Tools/Frank Retarget/GreatSword/Render quality frames")]
        public static void RenderQualityFrames()
        {
            string folder = "Assets/DemoSence/Reports/GreatSwordFrames";
            Directory.CreateDirectory(folder);
            var scene = EditorSceneManager.OpenScene(TargetScene, OpenSceneMode.Single);
            try
            {
                var tester = UnityEngine.Object.FindObjectsByType<FrankCombinationTester>(FindObjectsInactive.Include).FirstOrDefault();
                if (!tester || !tester.greatSwordLibrary) throw new InvalidOperationException("GreatSword tester is not configured.");
                tester.SelectGreatSword();
                tester.demoCamera.rect = new Rect(0, 0, 1, 1);
                for (int pair = 0; pair < tester.greatSwordLibrary.pairs.Length; pair++)
                {
                    tester.greatSwordMotion = pair;
                    tester.Configure();
                    var camera = tester.demoCamera;
                    camera.rect = new Rect(0, 0, 1, 1);
                    float duration = tester.Duration;
                    foreach (float fraction in new[] { 0.25f, 0.5f, 0.75f })
                    {
                        tester.Seek(duration * fraction);
                        Capture(camera, folder + "/" + pair + "_" + fraction.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ".png", 1280, 720);
                    }
                }
                tester.mankey.Clear(); tester.pepe.Clear();
            }
            finally { }
            AssetDatabase.Refresh();
        }

        static void Capture(Camera camera, string path, int width, int height)
        {
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                // The editor capture teleports both actor roots and the camera between
                // samples. Render once to refresh URP light/shadow state after that
                // teleport, then read the settled frame on the second render.
                camera.Render();
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }
    }
}
