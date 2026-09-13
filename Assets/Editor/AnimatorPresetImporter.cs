#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public class AnimatorPresetImporter : EditorWindow
{
    string attackerPath = "Assets/Selected/ShowcaseControllers/Frank_Attacker_Master.controller";
    string victimPath = "Assets/Selected/ShowcaseControllers/Frank_Victim_Master.controller";

    Vector2 scroll;
    List<string> attackerNames = new List<string>();
    List<string> victimNames = new List<string>();

    [MenuItem("Tools/Animator Preset Importer")]
    static void Open()
    {
        GetWindow<AnimatorPresetImporter>("Animator Preset Importer");
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Controller file paths (YAML .controller files)", EditorStyles.boldLabel);
        attackerPath = EditorGUILayout.TextField("Attacker controller", attackerPath);
        victimPath = EditorGUILayout.TextField("Victim controller", victimPath);

        if (GUILayout.Button("Scan files"))
        {
            ScanFiles();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Found attacker states", EditorStyles.boldLabel);
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(120));
        foreach (var s in attackerNames) EditorGUILayout.LabelField(s);
        EditorGUILayout.EndScrollView();

        EditorGUILayout.LabelField("Found victim states", EditorStyles.boldLabel);
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(120));
        foreach (var s in victimNames) EditorGUILayout.LabelField(s);
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Select the GameObject in the scene that has the AnimationController component, then press 'Apply presets to selected AnimationController'. The tool will create paired presets (Pair1, Pair2, ...) using the state names found in files.", MessageType.Info);

        if (GUILayout.Button("Apply presets to selected AnimationController"))
        {
            ApplyToSelected();
        }

        if (GUILayout.Button("Create Attack1 / Hit1 presets for selected AnimationController"))
        {
            CreateAttackHitPresets();
        }

        if (GUILayout.Button("Rename Attack/Hit Clips (rename assets)"))
        {
            RenameAttackHitClips();
        }

        if (GUILayout.Button("Auto Find & Rename Attack/Hit Pairs"))
        {
            if (EditorUtility.DisplayDialog("Auto Find & Rename", "This will scan the project for AnimatorControllers, attempt to pair attacker/victim controllers and rename clips to AttackN/HitN. Proceed?", "Yes", "No"))
            {
                AutoFindAndRename();
            }
        }
    }

    void ScanFiles()
    {
        attackerNames.Clear();
        victimNames.Clear();
        if (File.Exists(attackerPath))
        {
            try
            {
                foreach (var line in File.ReadAllLines(attackerPath))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("m_Name:"))
                    {
                        var name = trimmed.Substring("m_Name:".Length).Trim();
                        if (!string.IsNullOrEmpty(name)) attackerNames.Add(name);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Error reading attacker controller: " + e);
            }
        }
        else
        {
            Debug.LogWarning("Attacker controller file not found: " + attackerPath);
        }

        if (File.Exists(victimPath))
        {
            try
            {
                foreach (var line in File.ReadAllLines(victimPath))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("m_Name:"))
                    {
                        var name = trimmed.Substring("m_Name:".Length).Trim();
                        if (!string.IsNullOrEmpty(name)) victimNames.Add(name);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Error reading victim controller: " + e);
            }
        }
        else
        {
            Debug.LogWarning("Victim controller file not found: " + victimPath);
        }

        // Deduplicate while preserving order
        attackerNames = Dedup(attackerNames);
        victimNames = Dedup(victimNames);
    }

    List<string> Dedup(List<string> src)
    {
        var seen = new HashSet<string>();
        var outList = new List<string>();
        foreach (var s in src)
        {
            if (string.IsNullOrEmpty(s)) continue;
            if (!seen.Contains(s)) { seen.Add(s); outList.Add(s); }
        }
        return outList;
    }

    void ApplyToSelected()
    {
        var go = Selection.activeGameObject;
        if (go == null)
        {
            Debug.LogError("No GameObject selected. Select the GameObject that has the AnimationController component.");
            return;
        }

        var animCtrl = go.GetComponent<AnimationController>();
        if (animCtrl == null)
        {
            Debug.LogError("Selected GameObject does not have AnimationController component.");
            return;
        }

        int count = Math.Min(attackerNames.Count, victimNames.Count);
        if (count == 0)
        {
            Debug.LogError("No matching state names found in scanned files.");
            return;
        }

        Undo.RecordObject(animCtrl, "Apply animation presets");

        var presets = new AnimationController.AnimationPair[count];
        for (int i = 0; i < count; i++)
        {
            presets[i] = new AnimationController.AnimationPair();
            presets[i].key = "Pair" + (i + 1);
            presets[i].leftState = attackerNames[i];
            presets[i].rightState = victimNames[i];
        }

        animCtrl.presets = presets;
        EditorUtility.SetDirty(animCtrl);

        Debug.Log($"Applied {count} presets to AnimationController on '{go.name}'. Keys: Pair1..Pair{count}");
    }

    void CreateAttackHitPresets()
    {
        var go = Selection.activeGameObject;
        if (go == null)
        {
            Debug.LogError("No GameObject selected. Select the GameObject that has the AnimationController component.");
            return;
        }

        var animCtrl = go.GetComponent<AnimationController>();
        if (animCtrl == null)
        {
            Debug.LogError("Selected GameObject does not have AnimationController component.");
            return;
        }

        if (attackerNames.Count == 0 || victimNames.Count == 0)
        {
            Debug.LogError("Scanner has not found attacker/victim states. Run 'Scan files' first.");
            return;
        }

        Undo.RecordObject(animCtrl, "Create Attack1/Hit1 presets");

        var presets = new AnimationController.AnimationPair[2];

        presets[0] = new AnimationController.AnimationPair();
        presets[0].key = "Attack1";
        presets[0].leftState = attackerNames[0];
        // rightState can be an associated hit state; use first victim state
        presets[0].rightState = victimNames[0];

        presets[1] = new AnimationController.AnimationPair();
        presets[1].key = "Hit1";
        // Hit1 maps to victim hit only
        presets[1].leftState = string.Empty;
        presets[1].rightState = victimNames[0];

        animCtrl.presets = presets;
        EditorUtility.SetDirty(animCtrl);

        Debug.Log($"Created Attack1 and Hit1 presets on AnimationController on '{go.name}'");
    }

    void RenameAttackHitClips()
    {
        // Load animator controller assets
        var attackerControllerAsset = AssetDatabase.LoadAssetAtPath<AnimatorController>(attackerPath);
        var victimControllerAsset = AssetDatabase.LoadAssetAtPath<AnimatorController>(victimPath);

        if (attackerControllerAsset == null)
        {
            Debug.LogError("Attacker AnimatorController asset not found at path: " + attackerPath);
            return;
        }

        if (victimControllerAsset == null)
        {
            Debug.LogError("Victim AnimatorController asset not found at path: " + victimPath);
            return;
        }

        // Collect clips from states in order (deduplicated)
        List<AnimationClip> attackerClips = CollectStateClips(attackerControllerAsset);
        List<AnimationClip> victimClips = CollectStateClips(victimControllerAsset);

        int count = Math.Min(attackerClips.Count, victimClips.Count);
        if (count == 0)
        {
            Debug.LogError("No matching animation clips found to rename.");
            return;
        }

        // Rename assets: Attack1.anim / Hit1.anim ...
        for (int i = 0; i < count; i++)
        {
            var aClip = attackerClips[i];
            var vClip = victimClips[i];

            if (aClip != null)
            {
                var aPath = AssetDatabase.GetAssetPath(aClip);
                var aNewName = $"Attack{(i + 1)}.anim";
                if (!string.IsNullOrEmpty(aPath))
                {
                    var res = AssetDatabase.RenameAsset(aPath, aNewName);
                    if (!string.IsNullOrEmpty(res)) Debug.LogWarning($"RenameAsset returned: {res}");
                    else Debug.Log($"Renamed '{aPath}' -> '{aNewName}'");
                }
            }

            if (vClip != null)
            {
                var vPath = AssetDatabase.GetAssetPath(vClip);
                var vNewName = $"Hit{(i + 1)}.anim";
                if (!string.IsNullOrEmpty(vPath))
                {
                    var res = AssetDatabase.RenameAsset(vPath, vNewName);
                    if (!string.IsNullOrEmpty(res)) Debug.LogWarning($"RenameAsset returned: {res}");
                    else Debug.Log($"Renamed '{vPath}' -> '{vNewName}'");
                }
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Optionally update presets on selected AnimationController if present
        var go = Selection.activeGameObject;
        if (go != null)
        {
            var animCtrl = go.GetComponent<AnimationController>();
            if (animCtrl != null)
            {
                Undo.RecordObject(animCtrl, "Apply Attack/Hit presets after rename");
                var presets = new AnimationController.AnimationPair[count];
                for (int i = 0; i < count; i++)
                {
                    presets[i] = new AnimationController.AnimationPair();
                    presets[i].key = "Pair" + (i + 1);
                    presets[i].leftState = $"Attack{(i + 1)}";
                    presets[i].rightState = $"Hit{(i + 1)}";
                }
                animCtrl.presets = presets;
                EditorUtility.SetDirty(animCtrl);
                Debug.Log($"Updated presets on '{go.name}' to Attack/Hit pairs (1..{count}).");
            }
        }
    }

    List<AnimationClip> CollectStateClips(AnimatorController controller)
    {
        var list = new List<AnimationClip>();
        var seen = new HashSet<string>();

        foreach (var layer in controller.layers)
        {
            var states = layer.stateMachine.states;
            foreach (var cs in states)
            {
                var motion = cs.state.motion as AnimationClip;
                if (motion == null) continue;
                var path = AssetDatabase.GetAssetPath(motion);
                if (string.IsNullOrEmpty(path)) continue;
                if (seen.Add(path)) list.Add(motion);
            }
        }

        return list;
    }

    void AutoFindAndRename()
    {
        // find all animator controllers in project
        var guids = AssetDatabase.FindAssets("t:AnimatorController");
        var controllers = new List<string>();
        foreach (var g in guids)
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            controllers.Add(p);
        }

        var attackers = new List<string>();
        var victims = new List<string>();

        foreach (var p in controllers)
        {
            var name = Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
            if (name.Contains("attacker") || name.Contains("attack")) attackers.Add(p);
            else if (name.Contains("victim") || name.Contains("hit")) victims.Add(p);
        }

        // Fallback: if none detected by keywords, try to split controllers by folder/name pattern
        if (attackers.Count == 0 || victims.Count == 0)
        {
            // try to find pairs by looking for controllers with same prefix
            var byPrefix = new Dictionary<string, List<string>>();
            foreach (var p in controllers)
            {
                var fn = Path.GetFileNameWithoutExtension(p);
                var tokens = fn.Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
                var prefix = tokens.Length > 0 ? tokens[0] : fn;
                if (!byPrefix.ContainsKey(prefix)) byPrefix[prefix] = new List<string>();
                byPrefix[prefix].Add(p);
            }

            foreach (var kv in byPrefix)
            {
                var list = kv.Value;
                if (list.Count >= 2)
                {
                    // assume first is attacker and second is victim
                    attackers.Add(list[0]);
                    victims.Add(list[1]);
                }
            }
        }

        if (attackers.Count == 0 || victims.Count == 0)
        {
            Debug.LogError("AutoFindAndRename: Could not find attacker/victim controllers in project.");
            return;
        }

        int totalPairs = 0;
        // For each attacker try to find best matching victim
        foreach (var atk in attackers)
        {
            string matchedVictim = FindVictimForAttacker(atk, victims);
            if (string.IsNullOrEmpty(matchedVictim)) continue;

            var attackerControllerAsset = AssetDatabase.LoadAssetAtPath<AnimatorController>(atk);
            var victimControllerAsset = AssetDatabase.LoadAssetAtPath<AnimatorController>(matchedVictim);
            if (attackerControllerAsset == null || victimControllerAsset == null) continue;

            var attackerClips = CollectStateClips(attackerControllerAsset);
            var victimClips = CollectStateClips(victimControllerAsset);
            int count = Math.Min(attackerClips.Count, victimClips.Count);
            if (count == 0) continue;

            for (int i = 0; i < count; i++)
            {
                var aClip = attackerClips[i];
                var vClip = victimClips[i];
                if (aClip != null)
                {
                    var aPath = AssetDatabase.GetAssetPath(aClip);
                    var aNewName = $"Attack{(i + 1)}.anim";
                    AssetDatabase.RenameAsset(aPath, aNewName);
                }
                if (vClip != null)
                {
                    var vPath = AssetDatabase.GetAssetPath(vClip);
                    var vNewName = $"Hit{(i + 1)}.anim";
                    AssetDatabase.RenameAsset(vPath, vNewName);
                }
            }

            totalPairs += count;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"AutoFindAndRename: Renamed {totalPairs} Attack/Hit pairs.");
    }

    string FindVictimForAttacker(string attackerPath, List<string> victims)
    {
        var atkName = Path.GetFileNameWithoutExtension(attackerPath).ToLowerInvariant();
        // direct replacements
        var candidates = new[] { atkName.Replace("attacker", "victim"), atkName.Replace("attack", "hit") };
        foreach (var c in candidates)
        {
            foreach (var v in victims)
            {
                var vn = Path.GetFileNameWithoutExtension(v).ToLowerInvariant();
                if (vn == c) return v;
            }
        }

        // fallback: choose victim with highest common prefix
        string best = null;
        int bestScore = 0;
        foreach (var v in victims)
        {
            var vn = Path.GetFileNameWithoutExtension(v).ToLowerInvariant();
            int score = CommonPrefixLength(atkName, vn);
            if (score > bestScore) { bestScore = score; best = v; }
        }

        // require at least 3 chars in common
        return bestScore >= 3 ? best : null;
    }

    int CommonPrefixLength(string a, string b)
    {
        int i = 0; int len = Math.Min(a.Length, b.Length);
        for (; i < len; i++) if (a[i] != b[i]) break;
        return i;
    }
}
#endif
