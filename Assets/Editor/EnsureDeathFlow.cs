using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Editor tool that hardens a character Animator Controller for Meme Battle death flow.
///
/// PROBLEM IT FIXES:
/// The default controller had the "IsDead -> KB_TopKO" transition authored ONLY from the
/// Idle state. That means if the character was mid-attack or mid-hit when its last HP was
/// drained, IsDead could not interrupt it and the character would NOT collapse into the
/// death pose -- it would keep playing the hit/attack and never lie down.
///
/// WHAT IT DOES:
/// 1. Ensures "IsDead -> KB_TopKO" exists as an ANY STATE transition, so death instantly
///    interrupts attack, hit, getup or knockdown states (death has highest priority).
/// 2. Removes the old Idle-only "IsDead -> KB_TopKO" transitions so death is authored once.
///
/// USAGE:
/// Select one or more GameObjects that have a CharacterAnimatorBridge component, or select
/// the .controller asset directly, then run the menu item.
/// </summary>
public static class EnsureDeathFlow
{
    private const string Menu = "Tools/Animator/Ensure Death Flow (Any State -> KB_TopKO)";

    [MenuItem(Menu)]
    public static void EnsureOnSelected()
    {
        bool anyProcessed = false;

        // Case 1: selected GameObjects with a CharacterAnimatorBridge.
        foreach (var go in Selection.gameObjects)
        {
            var bridge = go.GetComponent<CharacterAnimatorBridge>();
            if (bridge == null) continue;

            var animator = go.GetComponent<Animator>();
            if (animator == null)
            {
                Debug.LogWarning($"EnsureDeathFlow: '{go.name}' has no Animator component.", go);
                continue;
            }

            var controller = animator.runtimeAnimatorController as AnimatorController;
            if (controller == null)
            {
                Debug.LogWarning($"EnsureDeathFlow: '{go.name}' Animator has no AnimatorController asset (runtime controllers cannot be edited).", go);
                continue;
            }

            Process(controller, animator.GetBool("IsDead") == false ? "IsDead" : "IsDead", "KB_TopKO");
            anyProcessed = true;
        }

        // Case 2: selected .controller assets directly.
        foreach (var obj in Selection.objects)
        {
            if (obj is AnimatorController controller)
            {
                Process(controller, "IsDead", "KB_TopKO");
                anyProcessed = true;
            }
        }

        if (!anyProcessed)
        {
            Debug.LogWarning("EnsureDeathFlow: nothing processed. Select a GameObject with CharacterAnimatorBridge/Animator, or a .controller asset.");
        }
    }

    private static void Process(AnimatorController controller, string isDeadParam, string deathStateName)
    {
        if (controller == null) return;

        // 1. Verify the required parameter and state exist.
        bool hasIsDead = false;
        foreach (var p in controller.parameters)
        {
            if (p.name == isDeadParam && p.type == AnimatorControllerParameterType.Bool)
            {
                hasIsDead = true;
                break;
            }
        }
        if (!hasIsDead)
        {
            Debug.LogError($"EnsureDeathFlow: controller '{controller.name}' has no Bool parameter '{isDeadParam}'.");
            return;
        }

        AnimatorState deathState = FindStateRecursive(controller, deathStateName);
        if (deathState == null)
        {
            Debug.LogError($"EnsureDeathFlow: controller '{controller.name}' has no state named '{deathStateName}'.");
            return;
        }

        var sm = controller.layers[0].stateMachine;

        // 2. Remove any existing IsDead transitions to KB_TopKO (from any state or state machine),
        //    so we author death exactly once as an Any State transition.
        int removed = 0;

        // Remove from AnyStateTransitions.
        removed += RemoveMatchingTransitions(sm.anyStateTransitions, sm, isDeadParam, deathState, true);

        // Remove from every state's transitions.
        foreach (var childState in sm.states)
        {
            if (childState.state == null) continue;
            removed += RemoveMatchingTransitions(childState.state.transitions, null, isDeadParam, deathState, false, childState.state);
        }

        // 3. Add the Any State -> KB_TopKO transition (death has highest priority).
        var transition = sm.AddAnyStateTransition(deathState);
        transition.name = $"{isDeadParam} -> {deathStateName} (Any State)";
        transition.hasExitTime = false;
        transition.exitTime = 0f;
        transition.duration = 0f;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = false;
        transition.interruptionSource = TransitionInterruptionSource.Source;
        transition.orderedInterruption = true;
        transition.AddCondition(AnimatorConditionMode.If, 0f, isDeadParam);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        Debug.Log($"EnsureDeathFlow: '{controller.name}' -> death flow ensured. " +
                  $"Any State -> {deathStateName} on {isDeadParam}=true. Removed {removed} old IsDead transition(s).");
    }

    private static int RemoveMatchingTransitions(
        AnimatorStateTransition[] transitions,
        AnimatorStateMachine ownerSm,
        string isDeadParam,
        AnimatorState deathState,
        bool fromAnyState,
        AnimatorState ownerState = null)
    {
        if (transitions == null) return 0;
        int removed = 0;

        // Collect matches first, then remove (can't modify while iterating).
        var toRemove = new List<AnimatorStateTransition>();
        foreach (var t in transitions)
        {
            if (t == null) continue;
            if (t.destinationState != deathState && t.destinationStateMachine != null) continue;
            if (t.destinationState != deathState) continue;

            // Does this transition carry the IsDead condition?
            bool hasCondition = false;
            foreach (var c in t.conditions)
            {
                if (c.parameter == isDeadParam)
                {
                    hasCondition = true;
                    break;
                }
            }
            if (hasCondition) toRemove.Add(t);
        }

        foreach (var t in toRemove)
        {
            if (fromAnyState && ownerSm != null)
            {
                ownerSm.RemoveAnyStateTransition(t);
            }
            else if (ownerState != null)
            {
                ownerState.RemoveTransition(t);
            }
            removed++;
        }

        return removed;
    }

    private static AnimatorState FindStateRecursive(AnimatorController controller, string stateName)
    {
        foreach (var layer in controller.layers)
        {
            var found = FindStateRecursive(layer.stateMachine, stateName);
            if (found != null) return found;
        }
        return null;
    }

    private static AnimatorState FindStateRecursive(AnimatorStateMachine sm, string stateName)
    {
        foreach (var cs in sm.states)
        {
            if (cs.state != null && cs.state.name == stateName) return cs.state;
        }
        foreach (var child in sm.stateMachines)
        {
            var found = FindStateRecursive(child.stateMachine, stateName);
            if (found != null) return found;
        }
        return null;
    }
}
