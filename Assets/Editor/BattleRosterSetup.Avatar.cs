using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static partial class BattleRosterSetup
{
    // FBX contains an authored -90 degree object transform in addition to its file-axis conversion.
    // Fold that object transform into its immediate children so the persistent battle root can face the opponent.
    static void BakeVisualRoot(GameObject model, string id)
    {
        var animator = model.GetComponent<Animator>();
        var description = animator.avatar.humanDescription;
        var children = model.transform.Cast<Transform>().ToArray();
        var positions = children.Select(t => t.position).ToArray();
        var rotations = children.Select(t => t.rotation).ToArray();
        model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        for (int i = 0; i < children.Length; i++)
            children[i].SetPositionAndRotation(positions[i], rotations[i]);
        EnforceReferencePose(model, description.human);
        description.skeleton = model.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone
        {
            name = t.name,
            position = t.localPosition,
            rotation = t.localRotation,
            scale = t.localScale
        }).ToArray();
        var avatar = AvatarBuilder.BuildHumanAvatar(model, description);
        avatar.name = id + " Battle Humanoid";
        if (!avatar.isHuman || !avatar.isValid)
        {
            UnityEngine.Object.DestroyImmediate(avatar);
            throw new InvalidOperationException("Invalid normalized battle avatar: " + id);
        }
        Directory.CreateDirectory(Root + "/Avatars");
        AssetDatabase.Refresh();
        string path = Root + "/Avatars/" + id + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Avatar>(path);
        if (existing)
        {
            EditorUtility.CopySerialized(avatar, existing);
            UnityEngine.Object.DestroyImmediate(avatar);
            avatar = existing;
        }
        else
            AssetDatabase.CreateAsset(avatar, path);
        animator.avatar = avatar;
        animator.Rebind();
    }
}
