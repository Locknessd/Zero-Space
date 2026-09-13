using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class AnimatorDiagnostics
{
    [MenuItem("Tools/Animator/Diagnose Selected AnimationControllers and Presets")]
    static void DiagnoseSelected()
    {
        var gos = Selection.gameObjects;
        if (gos == null || gos.Length == 0)
        {
            Debug.LogWarning("AnimatorDiagnostics: No GameObjects selected. Select a GameObject that has AnimationController component.");
            return;
        }

        foreach (var go in gos)
        {
            var animCtrlComp = go.GetComponent<global::AnimationController>();
            if (animCtrlComp == null)
            {
                Debug.LogWarning($"AnimatorDiagnostics: GameObject '{go.name}' has no AnimationController component.");
                continue;
            }

            Debug.Log($"AnimatorDiagnostics: Diagnosing '{go.name}'");

            CheckAnimator(animCtrlComp.leftAnimator, "Left", animCtrlComp.presets);
            CheckAnimator(animCtrlComp.rightAnimator, "Right", animCtrlComp.presets);
        }
    }

    [MenuItem("Tools/Animator/Fix Presets: Swap Left/Right If States Mismatched (Selected GameObjects)")]
    static void FixPresetsSwapSelected()
    {
        var gos = Selection.gameObjects;
        if (gos == null || gos.Length == 0)
        {
            Debug.LogWarning("AnimatorDiagnostics: No GameObjects selected.");
            return;
        }

        foreach (var go in gos)
        {
            var animCtrlComp = go.GetComponent<global::AnimationController>();
            if (animCtrlComp == null) continue;
            var left = animCtrlComp.leftAnimator;
            var right = animCtrlComp.rightAnimator;
            if (left == null || right == null) continue;

            bool anyChanged = false;
            var presets = animCtrlComp.presets;
            if (presets == null || presets.Length == 0) continue;

            for (int i = 0; i < presets.Length; i++)
            {
                var p = presets[i];
                if (p == null) continue;
                bool leftHasLeft = AnimatorHasStateAny(left, p.leftState);
                bool leftHasRight = AnimatorHasStateAny(left, p.rightState);
                bool rightHasLeft = AnimatorHasStateAny(right, p.leftState);
                bool rightHasRight = AnimatorHasStateAny(right, p.rightState);

                // If left doesn't have its assigned state but does have the right state's name, and right doesn't have its own but has the left's, swap.
                if (!leftHasLeft && leftHasRight && !rightHasRight && rightHasLeft)
                {
                    var tmp = p.leftState;
                    p.leftState = p.rightState;
                    p.rightState = tmp;
                    anyChanged = true;
                    Debug.Log($"AnimatorDiagnostics: Swapped preset '{p.key}' on '{go.name}' because left/right states appeared reversed.");
                }
            }

            if (anyChanged)
            {
                Undo.RecordObject(animCtrlComp, "Fix Presets Swap");
                EditorUtility.SetDirty(animCtrlComp);
                Debug.Log($"AnimatorDiagnostics: Updated presets on '{go.name}'.");
            }
        }
    }

    static bool AnimatorHasStateAny(Animator animator, string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName)) return false;
        var rc = animator.runtimeAnimatorController as AnimatorController;
        if (rc == null) return false;
        int hash = Animator.StringToHash(stateName);
        for (int li = 0; li < rc.layers.Length; li++) if (animator.HasState(li, hash)) return true;
        return false;
    }

    static void CheckAnimator(Animator animator, string sideName, AnimationController.AnimationPair[] presets)
    {
        if (animator == null)
        {
            Debug.LogWarning($"AnimatorDiagnostics: {sideName} animator is null.");
            return;
        }

        var rac = animator.runtimeAnimatorController as AnimatorController;
        if (rac == null)
        {
            Debug.LogWarning($"AnimatorDiagnostics: {sideName} animator '{animator.name}' has no AnimatorController asset (runtime controller null or not an AnimatorController).");
            return;
        }

        Debug.Log($"AnimatorDiagnostics: {sideName} animator uses controller '{rac.name}' with {rac.layers.Length} layers and {rac.parameters.Length} parameters.");

        // collect states
        var stateList = new List<string>();
        for (int li = 0; li < rac.layers.Length; li++)
        {
            var layer = rac.layers[li];
            CollectStatesRecursive(layer.stateMachine, layer.name, stateList);
        }

        Debug.Log($"AnimatorDiagnostics: {sideName} controller states ({stateList.Count}):\n  {string.Join("\n  ", stateList.ToArray())}");

        // print parameters
        var paramNames = new List<string>();
        foreach (var p in rac.parameters) paramNames.Add(p.name + " (" + p.type + ")");
        Debug.Log($"AnimatorDiagnostics: {sideName} controller parameters ({paramNames.Count}):\n  {string.Join("\n  ", paramNames.ToArray())}");

        if (presets == null || presets.Length == 0)
        {
            Debug.LogWarning($"AnimatorDiagnostics: No presets defined on AnimationController component.");
            return;
        }

        foreach (var p in presets)
        {
            if (p == null) continue;
            // check left/right mapping depending on side
            string want = sideName == "Left" ? p.leftState : p.rightState;
            if (string.IsNullOrEmpty(want))
            {
                Debug.Log($"AnimatorDiagnostics: preset '{p.key}' -> {sideName} state is empty.");
                continue;
            }

            bool hasParam = false;
            foreach (var par in rac.parameters) if (par.name == want && par.type == AnimatorControllerParameterType.Trigger) { hasParam = true; break; }

            bool hasState = stateList.Contains(want);

            Debug.Log($"AnimatorDiagnostics: preset '{p.key}' wants '{want}' on {sideName}: stateFound={hasState} triggerParamFound={hasParam}");
        }
    }

    static void CollectStatesRecursive(AnimatorStateMachine sm, string pathPrefix, List<string> outList)
    {
        if (sm == null) return;
        // collect states in this state machine
        foreach (var cs in sm.states)
        {
            var s = cs.state;
            if (s == null) continue;
            string path = string.IsNullOrEmpty(pathPrefix) ? s.name : pathPrefix + "/" + s.name;
            outList.Add(path);
            // also add plain name for quick match
            if (!outList.Contains(s.name)) outList.Add(s.name);
        }

        // recurse into nested state machines
        foreach (var child in sm.stateMachines)
        {
            var childSM = child.stateMachine;
            if (childSM == null) continue;
            string childPrefix = string.IsNullOrEmpty(pathPrefix) ? childSM.name : pathPrefix + "/" + childSM.name;
            CollectStatesRecursive(childSM, childPrefix, outList);
        }
    }
}
