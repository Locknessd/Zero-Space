using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Editor tool to automatically generate/pair Attack/Hit presets for selected GameObjects
/// that have the runtime `AnimationController` component.
///
/// Behavior:
/// - Scans left/right AnimatorController states for names containing "Attack"/"Hit".
/// - If numeric suffixes are present (e.g. Attack5 and Hit5) pairs by number.
/// - Otherwise pairs by index order (first attack with first hit, ...).
/// - Writes `AnimationController.presets` on the component with `Pair{N}` keys.
/// </summary>
public static class ApplyAttackHitPresets
{
    [MenuItem("Tools/Animator/AutoApply Attack-Hit Presets To Selected AnimationController")]
    public static void ApplyToSelected()
    {
        var gos = Selection.gameObjects;
        if (gos == null || gos.Length == 0)
        {
            Debug.LogWarning("ApplyAttackHitPresets: No GameObjects selected. Select GameObjects that have an AnimationController component.");
            return;
        }

        int totalApplied = 0;
        foreach (var go in gos)
        {
            var animComp = go.GetComponent<global::AnimationController>();
            if (animComp == null)
            {
                Debug.LogWarning($"ApplyAttackHitPresets: GameObject '{go.name}' does not have AnimationController component.");
                continue;
            }

            try
            {
                int applied = ProcessAnimationController(animComp);
                if (applied > 0)
                {
                    Debug.Log($"ApplyAttackHitPresets: Applied {applied} presets to AnimationController on '{go.name}'.");
                    totalApplied += applied;
                }
                else
                {
                    Debug.LogWarning($"ApplyAttackHitPresets: No pairs found for '{go.name}'.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"ApplyAttackHitPresets: Failed for '{go.name}': {ex}");
            }
        }

        if (totalApplied == 0) Debug.Log("ApplyAttackHitPresets: No presets applied.");
    }

    // Force create up to `desiredCount` pairs using all available heuristics and final index fallback.
    static int ProcessAnimationControllerForced(global::AnimationController animComp, int desiredCount)
    {
        if (animComp == null) return 0;

        var left = animComp.leftAnimator;
        var right = animComp.rightAnimator;
        if (left == null || right == null) return 0;

        var leftController = left.runtimeAnimatorController as AnimatorController;
        var rightController = right.runtimeAnimatorController as AnimatorController;
        if (leftController == null || rightController == null) return 0;

        var leftStates = CollectPlainStateNames(leftController);
        var rightStates = CollectPlainStateNames(rightController);

        // reuse existing Process logic by extracting candidates
        var attackRegex = new Regex(@"attack[_\s-]?(\d+)$", RegexOptions.IgnoreCase);
        var hitRegex = new Regex(@"hit[_\s-]?(\d+)$", RegexOptions.IgnoreCase);

        var attacksByNum = new Dictionary<int, string>();
        var hitsByNum = new Dictionary<int, string>();
        var attackCandidates = new List<string>();
        var hitCandidates = new List<string>();

        foreach (var s in leftStates)
        {
            var m = attackRegex.Match(s);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int n)) attacksByNum[n] = s;
            if (s.IndexOf("attack", StringComparison.OrdinalIgnoreCase) >= 0) attackCandidates.Add(s);
            // fallback: extract any numeric suffix/pattern anywhere in the name
            if (!attacksByNum.Any() )
            {
                var md = Regex.Match(s, "(\\d+)");
                if (md.Success && int.TryParse(md.Groups[1].Value, out int nd)) attacksByNum[nd] = s;
            }
        }
        foreach (var s in rightStates)
        {
            var m = hitRegex.Match(s);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int n)) hitsByNum[n] = s;
            if (s.IndexOf("hit", StringComparison.OrdinalIgnoreCase) >= 0) hitCandidates.Add(s);
            if (!hitsByNum.Any())
            {
                var md = Regex.Match(s, "(\\d+)");
                if (md.Success && int.TryParse(md.Groups[1].Value, out int nd)) hitsByNum[nd] = s;
            }
        }

        var pairs = new List<global::AnimationController.AnimationPair>();

        // numeric matches
        var nums = new List<int>(attacksByNum.Keys);
        nums.Sort();
        foreach (var num in nums) if (hitsByNum.ContainsKey(num))
        {
            var pair = new global::AnimationController.AnimationPair(); pair.key = "Pair" + num; pair.leftState = attacksByNum[num]; pair.rightState = hitsByNum[num]; pairs.Add(pair);
        }

        // prepare candidates
        if (attackCandidates.Count == 0) attackCandidates = leftStates.Where(s => s.IndexOf("attack", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        if (hitCandidates.Count == 0) hitCandidates = rightStates.Where(s => s.IndexOf("hit", StringComparison.OrdinalIgnoreCase) >= 0).ToList();

        // fuzzy match
        string Normalize(string v) => Regex.Replace(v ?? string.Empty, "[^a-z0-9]", "").ToLowerInvariant();
        int CommonPrefixLen(string a, string b) { int n = Math.Min(a.Length, b.Length); int i = 0; while (i < n && a[i] == b[i]) i++; return i; }
        var remainingAttacks = attackCandidates.Where(s => !pairs.Any(p => p.leftState == s)).ToList();
        var remainingHits = hitCandidates.Where(s => !pairs.Any(p => p.rightState == s)).ToList();
        var normHits = remainingHits.ToDictionary(h => h, h => Normalize(h));
        var usedHits = new HashSet<string>();
        foreach (var a in remainingAttacks.ToList())
        {
            if (pairs.Count >= desiredCount) break;
            var na = Normalize(a);
            string match = null;
            foreach (var kv in normHits) if (!usedHits.Contains(kv.Key) && na == kv.Value) { match = kv.Key; break; }
            if (match == null)
            {
                foreach (var kv in normHits) if (!usedHits.Contains(kv.Key) && (na.Contains(kv.Value) || kv.Value.Contains(na))) { match = kv.Key; break; }
            }
            if (match == null)
            {
                int bestScore = 0; string bestKey = null;
                foreach (var kv in normHits) if (!usedHits.Contains(kv.Key)) { int score = CommonPrefixLen(na, kv.Value); if (score > bestScore) { bestScore = score; bestKey = kv.Key; } }
                if (bestScore >= 3) match = bestKey;
            }
            if (match != null)
            {
                var pair = new global::AnimationController.AnimationPair(); pair.key = "Pair" + (pairs.Count + 1); pair.leftState = a; pair.rightState = match; pairs.Add(pair); usedHits.Add(match);
            }
        }

        // final fallback: fill by index from any remaining states (left/right) until desiredCount
        var usedLeft = new HashSet<string>(pairs.Select(p => p.leftState));
        var usedRight = new HashSet<string>(pairs.Select(p => p.rightState));
        var allLeft = leftStates.Where(s => !usedLeft.Contains(s)).ToList();
        var allRight = rightStates.Where(s => !usedRight.Contains(s)).ToList();
        int fi = 0;
        while (pairs.Count < desiredCount && fi < Math.Min(allLeft.Count, allRight.Count))
        {
            var pair = new global::AnimationController.AnimationPair(); pair.key = "Pair" + (pairs.Count + 1); pair.leftState = allLeft[fi]; pair.rightState = allRight[fi]; pairs.Add(pair); fi++; }

        if (pairs.Count == 0) return 0;

        Undo.RecordObject(animComp, "Force Auto Apply Attack-Hit Presets");
        animComp.presets = pairs.ToArray();
        EditorUtility.SetDirty(animComp);
        return pairs.Count;
    }

    [MenuItem("Tools/Animator/Force Apply 5 Attack-Hit Pairs To Selected AnimationController")]
    public static void ForceApplyFiveToSelected()
    {
        var gos = Selection.gameObjects;
        if (gos == null || gos.Length == 0)
        {
            Debug.LogWarning("ApplyAttackHitPresets: No GameObjects selected. Select GameObjects that have an AnimationController component.");
            return;
        }

        int totalApplied = 0;
        foreach (var go in gos)
        {
            var animComp = go.GetComponent<global::AnimationController>();
            if (animComp == null)
            {
                Debug.LogWarning($"ApplyAttackHitPresets: GameObject '{go.name}' does not have AnimationController component.");
                continue;
            }

            try
            {
                int applied = ProcessAnimationControllerForced(animComp, 5);
                if (applied > 0)
                {
                    Debug.Log($"ApplyAttackHitPresets: Force applied {applied} presets to AnimationController on '{go.name}' (requested 5).");
                    totalApplied += applied;
                }
                else
                {
                    Debug.LogWarning($"ApplyAttackHitPresets: No pairs found for '{go.name}' (force).");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"ApplyAttackHitPresets: Failed for '{go.name}': {ex}");
            }
        }

        if (totalApplied == 0) Debug.Log("ApplyAttackHitPresets: No presets applied.");
    }

    static int ProcessAnimationController(global::AnimationController animComp)
    {
        if (animComp == null) return 0;

        var left = animComp.leftAnimator;
        var right = animComp.rightAnimator;
        if (left == null || right == null)
        {
            Debug.LogWarning("ApplyAttackHitPresets: leftAnimator or rightAnimator is null on component '" + animComp.name + "'.");
            return 0;
        }

        var leftController = left.runtimeAnimatorController as AnimatorController;
        var rightController = right.runtimeAnimatorController as AnimatorController;
        if (leftController == null || rightController == null)
        {
            Debug.LogWarning("ApplyAttackHitPresets: left/right AnimatorController asset missing or not an AnimatorController.");
            return 0;
        }

        var leftStates = CollectPlainStateNames(leftController);
        var rightStates = CollectPlainStateNames(rightController);

        // find named Attack/Hit candidates
        var attackRegex = new Regex(@"attack[_\s-]?(\d+)$", RegexOptions.IgnoreCase);
        var hitRegex = new Regex(@"hit[_\s-]?(\d+)$", RegexOptions.IgnoreCase);

        var attacksByNum = new Dictionary<int, string>();
        var hitsByNum = new Dictionary<int, string>();
        var attackCandidates = new List<string>();
        var hitCandidates = new List<string>();

        foreach (var s in leftStates)
        {
            var m = attackRegex.Match(s);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int n)) attacksByNum[n] = s;
            if (s.IndexOf("attack", StringComparison.OrdinalIgnoreCase) >= 0) attackCandidates.Add(s);
            // fallback: extract any numeric suffix/pattern anywhere in the name
            if (!attacksByNum.Any() )
            {
                var md = Regex.Match(s, "(\\d+)");
                if (md.Success && int.TryParse(md.Groups[1].Value, out int nd)) attacksByNum[nd] = s;
            }
        }
        foreach (var s in rightStates)
        {
            var m = hitRegex.Match(s);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int n)) hitsByNum[n] = s;
            if (s.IndexOf("hit", StringComparison.OrdinalIgnoreCase) >= 0) hitCandidates.Add(s);
            if (!hitsByNum.Any())
            {
                var md = Regex.Match(s, "(\\d+)");
                if (md.Success && int.TryParse(md.Groups[1].Value, out int nd)) hitsByNum[nd] = s;
            }
        }

        var pairs = new List<global::AnimationController.AnimationPair>();

        // numeric intersection
        var nums = new List<int>(attacksByNum.Keys);
        nums.Sort();

        // Add numeric-matched pairs first (AttackN <-> HitN)
        foreach (var num in nums)
        {
            if (hitsByNum.ContainsKey(num))
            {
                var pair = new global::AnimationController.AnimationPair();
                pair.key = "Pair" + num;
                pair.leftState = attacksByNum[num];
                pair.rightState = hitsByNum[num];
                pairs.Add(pair);
            }
        }

        // Prepare candidate lists if empty
        if (attackCandidates.Count == 0)
            attackCandidates = leftStates.Where(s => s.IndexOf("attack", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        if (hitCandidates.Count == 0)
            hitCandidates = rightStates.Where(s => s.IndexOf("hit", StringComparison.OrdinalIgnoreCase) >= 0).ToList();

        // Exclude any already-used states (from numeric pairing)
        var usedLeft = new HashSet<string>(pairs.Select(p => p.leftState));
        var usedRight = new HashSet<string>(pairs.Select(p => p.rightState));

        var remainingAttacks = attackCandidates.Where(s => !usedLeft.Contains(s)).ToList();
        var remainingHits = hitCandidates.Where(s => !usedRight.Contains(s)).ToList();

        // Try fuzzy/normalized matching for remaining candidates
        string Normalize(string v) => Regex.Replace(v ?? string.Empty, "[^a-z0-9]", "").ToLowerInvariant();
        int CommonPrefixLen(string a, string b)
        {
            int n = Math.Min(a.Length, b.Length);
            int i = 0; while (i < n && a[i] == b[i]) i++; return i;
        }

        var normHits = remainingHits.ToDictionary(h => h, h => Normalize(h));
        var usedHits = new HashSet<string>();
        foreach (var a in remainingAttacks.ToList())
        {
            var na = Normalize(a);
            // exact normalized match
            string match = null;
            foreach (var kv in normHits)
            {
                if (usedHits.Contains(kv.Key)) continue;
                if (na == kv.Value) { match = kv.Key; break; }
            }

            // containment match
            if (match == null)
            {
                foreach (var kv in normHits)
                {
                    if (usedHits.Contains(kv.Key)) continue;
                    if (na.Contains(kv.Value) || kv.Value.Contains(na)) { match = kv.Key; break; }
                }
            }

            // prefix similarity
            if (match == null)
            {
                int bestScore = 0; string bestKey = null;
                foreach (var kv in normHits)
                {
                    if (usedHits.Contains(kv.Key)) continue;
                    int score = CommonPrefixLen(na, kv.Value);
                    if (score > bestScore) { bestScore = score; bestKey = kv.Key; }
                }
                // require reasonable prefix match
                if (bestScore >= Math.Min(4, Math.Min(na.Length, (bestKey != null ? Normalize(bestKey).Length : 0)) / 2)) match = bestKey;
            }

            if (match != null)
            {
                var pair = new global::AnimationController.AnimationPair();
                pair.key = "Pair" + (pairs.Count + 1);
                pair.leftState = a;
                pair.rightState = match;
                pairs.Add(pair);
                usedHits.Add(match);
            }
        }

        // remove used hits from remainingHits and attacks that were paired
        remainingHits = remainingHits.Where(h => !usedHits.Contains(h)).ToList();
        remainingAttacks = remainingAttacks.Where(a => !pairs.Any(p => p.leftState == a)).ToList();

        // Pair any remaining by index order
        int startIndex = pairs.Count > 0 ? pairs.Count + 1 : 1;
        int countFallback = Math.Min(remainingAttacks.Count, remainingHits.Count);
        for (int i = 0; i < countFallback; i++)
        {
            var pair = new global::AnimationController.AnimationPair();
            pair.key = "Pair" + (startIndex + i);
            pair.leftState = remainingAttacks[i];
            pair.rightState = remainingHits[i];
            pairs.Add(pair);
        }

        if (pairs.Count == 0) return 0;

        Undo.RecordObject(animComp, "Auto Apply Attack-Hit Presets");
        animComp.presets = pairs.ToArray();
        EditorUtility.SetDirty(animComp);
        return pairs.Count;
    }

    static List<string> CollectPlainStateNames(AnimatorController controller)
    {
        var list = new List<string>();
        foreach (var layer in controller.layers)
        {
            CollectStatesRecursive(layer.stateMachine, list);
        }
        // dedupe preserving order
        var outList = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in list)
        {
            if (string.IsNullOrEmpty(s)) continue;
            if (!seen.Contains(s)) { seen.Add(s); outList.Add(s); }
        }
        return outList;
    }

    static void CollectStatesRecursive(AnimatorStateMachine sm, List<string> outList)
    {
        foreach (var cs in sm.states)
        {
            if (cs.state != null) outList.Add(cs.state.name);
        }
        foreach (var child in sm.stateMachines)
        {
            CollectStatesRecursive(child.stateMachine, outList);
        }
    }
}
