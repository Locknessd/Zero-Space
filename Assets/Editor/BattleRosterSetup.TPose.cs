using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static partial class BattleRosterSetup
{
    // Use the installed Unity version's own Enforce T-Pose operation, as used by Configure Avatar.
    static void EnforceReferencePose(GameObject model, HumanBone[] mapping)
    {
        var tool = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AvatarSetupTool", true);
        var wrapper = tool.GetNestedType("BoneWrapper", BindingFlags.NonPublic | BindingFlags.Public);
        var bones = Array.CreateInstance(wrapper, HumanTrait.BoneCount);
        var transforms = model.GetComponentsInChildren<Transform>(true)
            .GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
        for (int i = 0; i < HumanTrait.BoneCount; i++)
        {
            string name = HumanTrait.BoneName[i];
            var match = mapping.FirstOrDefault(h => h.humanName.Replace(" ", "") == name.Replace(" ", ""));
            Transform transform = null;
            if (!string.IsNullOrEmpty(match.boneName))
                transforms.TryGetValue(match.boneName, out transform);
            bones.SetValue(Activator.CreateInstance(wrapper, new object[] { name, transform }), i);
        }
        tool.GetMethod("MakePoseValid", BindingFlags.Public | BindingFlags.Static)
            .Invoke(null, new object[] { bones });
        bool valid = (bool)tool.GetMethod("IsPoseValid", BindingFlags.Public | BindingFlags.Static)
            .Invoke(null, new object[] { bones });
        if (!valid)
            throw new InvalidOperationException(model.name + ": reference T-Pose is invalid.");
    }
}
