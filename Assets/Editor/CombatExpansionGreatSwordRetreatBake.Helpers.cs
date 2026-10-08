using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordRetreatBake
    {
        static readonly string[] PositionHelpers = { "Dummy004", Footsteps };
        static readonly string[] RotationHelpers = { Footsteps, HandDummy };

        static void PrepareHelperOffsets(Segment[] segments, StringBuilder report)
        {
            foreach (string fighter in Fighters)
                foreach (string path in PositionHelpers.Union(RotationHelpers))
                    ProveUnusedHelper(NativeDriver(fighter), fighter, path, report);
            foreach (string path in PositionHelpers)
            {
                for (int index = 0; index < segments.Length; index++)
                {
                    var segment = segments[index];
                    Vector3 offset = Vector3.zero;
                    // Ambush is untouched. Later segments retain every native key and derivative.
                    if (index > 0)
                    {
                        var previous = segments[index - 1];
                        offset = previous.helperOffsets[path] + HelperPosition(previous, path, previous.clip.length) -
                            HelperPosition(segment, path, 0);
                    }
                    segment.helperOffsets.Add(path, offset);
                    report.AppendLine(FormattableString.Invariant(
                        $"HELPER_OFFSET segment={index}; path={path}; localX={offset.x:R}; ") +
                        FormattableString.Invariant($"localY={offset.y:R}; localZ={offset.z:R}; ") +
                        "constant translation; native displacement and scale unchanged");
                }
            }
            PrepareHelperRotations(segments, report);
        }

        static Vector3 HelperPosition(Segment segment, string path, float time)
        {
            var position = Vector3.zero;
            for (int axis = 0; axis < 3; axis++)
            {
                string key = path + "|m_LocalPosition." + "xyz"[axis];
                if (!segment.curves.TryGetValue(key, out var curve))
                    throw new InvalidOperationException("Missing reconciled helper position: " + key);
                position[axis] = curve.Evaluate(time);
            }
            return position;
        }

        static void ApplyHelperOffsets(Segment segment, Dictionary<string, Transform> bones)
        {
            foreach (var pair in segment.helperOffsets)
                bones[pair.Key].localPosition += pair.Value;
            foreach (var pair in segment.helperRotations)
                bones[pair.Key].localRotation = pair.Value * bones[pair.Key].localRotation;
        }

        static void ProveUnusedHelper(Animator driver, string fighter, string path, StringBuilder report)
        {
            Transform helper = driver.transform.Find(path);
            if (!helper || helper.childCount != 0)
                throw new InvalidOperationException("Helper dependency proof requires an existing leaf: " +
                    fighter + "/" + path);
            var attached = helper.GetComponents<Component>();
            if (attached.Any(c => !c || !(c is Transform)))
                throw new InvalidOperationException("Helper has an attached component or missing script: " +
                    fighter + "/" + path);
            Transform prefabRoot = driver.transform.root;
            var components = prefabRoot.GetComponentsInChildren<Component>(true);
            if (components.Any(c => !c))
                throw new InvalidOperationException("Cannot prove helper dependencies with missing prefab scripts.");
            var avatars = new HashSet<Avatar>();
            int inspected = 0;
            int structural = 0;
            foreach (var component in components)
            {
                if (component is SkinnedMeshRenderer skin &&
                    (skin.rootBone == helper || skin.bones.Contains(helper)))
                    DependencyFailure(fighter, path, component, "skin bones/rootBone");
                if (component is Animator animator && animator.isHuman)
                    for (int bone = 0; bone < (int)HumanBodyBones.LastBone; bone++)
                        if (animator.GetBoneTransform((HumanBodyBones)bone) == helper)
                            DependencyFailure(fighter, path, component, "human bone " + (HumanBodyBones)bone);
                using var serialized = new SerializedObject(component);
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    inspected++;
                    if (property.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        var target = property.objectReferenceValue;
                        if (target is Avatar avatar)
                            avatars.Add(avatar);
                        if (target != helper && target != helper.gameObject)
                            continue;
                        bool hierarchy = component == helper.parent &&
                            property.propertyPath.StartsWith("m_Children.Array.data[", StringComparison.Ordinal);
                        bool ownObject = component == helper && property.propertyPath == "m_GameObject";
                        if (hierarchy || ownObject)
                        {
                            structural++;
                            continue;
                        }
                        DependencyFailure(fighter, path, component, property.propertyPath);
                    }
                    if (property.propertyType == SerializedPropertyType.String)
                    {
                        string value = property.stringValue;
                        if (value == path || value == helper.name ||
                            (!string.IsNullOrEmpty(value) && value.EndsWith("/" + helper.name,
                                StringComparison.Ordinal)))
                            DependencyFailure(fighter, path, component, property.propertyPath + " string=" + value);
                    }
                }
            }
            foreach (var avatar in avatars)
                foreach (var human in avatar.humanDescription.human ?? Array.Empty<HumanBone>())
                    if (human.boneName == helper.name)
                        throw new InvalidOperationException("Helper has Avatar human role: " + fighter + "/" + path +
                            "; avatar=" + avatar.name + "; humanName=" + human.humanName);
            report.AppendLine(FormattableString.Invariant(
                $"HELPER_DEPENDENCY_PROOF fighter={fighter}; path={path}; children=0; attached=TransformOnly; ") +
                FormattableString.Invariant(
                $"componentsScanned={components.Length}; propertiesScanned={inspected}; avatarsScanned={avatars.Count}; ") +
                FormattableString.Invariant($"structuralReferences={structural}; consumers=0; ") +
                "no renderer/skin influence, retarget human role, serialized object consumer or socket/path string");
        }

        static void DependencyFailure(string fighter, string path, Component component, string property)
        {
            string owner = AnimationUtility.CalculateTransformPath(component.transform, component.transform.root);
            throw new InvalidOperationException("Helper has a dependency: " + fighter + "/" + path +
                "; consumer=" + owner + " " + component.GetType().Name + "; property=" + property);
        }
    }
}
