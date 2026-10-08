using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static class CombatExpansionAxeAdaptationCheck
    {
        [Serializable]
        sealed class Report
        {
            public CombatExpansionHumanoidStudy.StudyRecord[] clips;
        }

        public static void Audit()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            var records = JsonUtility.FromJson<Report>(File.ReadAllText(
                CombatExpansionHumanoidStudy.ReportsRoot + "/AxeCombos/Study.json")).clips;
            var battle = EditorSceneManager.OpenPreviewScene(CombatExpansionInventory.Battle);
            var rows = new List<string>
            {
                "fighter,identity,clip,travelErrorMetres,repeatErrorMetres,leftGripError,rightGripError"
            };
            try
            {
                var fighters = battle.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<CharacterCombat>(true)).ToArray();
                foreach (var record in records)
                {
                    var fighter = fighters.Single(f => f.name == record.fighter);
                    rows.Add(Measure(fighter, record));
                }
                File.WriteAllLines(CombatExpansionInventory.Output + "/AxeAdaptation.csv", rows);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(battle);
            }
        }

        static string Measure(CharacterCombat fighter, CombatExpansionHumanoidStudy.StudyRecord record)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var character = Object.Instantiate(fighter.Animator.gameObject).GetComponent<Animator>();
                SceneManager.MoveGameObjectToScene(character.gameObject, scene);
                FrankRetargetBuilder.StripStudyComponents(character.gameObject, false);
                character.runtimeAnimatorController = null;
                character.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                character.transform.localScale = fighter.Animator.transform.lossyScale;
                var root = new GameObject("Actual battle actor sampling");
                SceneManager.MoveGameObjectToScene(root, scene);
                var actor = root.AddComponent<FrankTestActor>();
                actor.character = character;
                actor.characterName = fighter.name;
                var template = AssetDatabase.LoadAssetAtPath<FrankTestDriver>(record.driverPath);
                var clip = AssetDatabase.LoadAllAssetsAtPath(record.sourcePath).OfType<AnimationClip>()
                    .Single(c => CombatExpansionInventory.Identity(c) == record.guid + ":" + record.localId);
                actor.ConfigureSource(template, clip, true, true, false, true);
                var arms = actor.Pose.limbs.Where(l => l.sourceKnuckle).ToArray();
                Vector3 start = actor.Pose.sourceHips.position;
                var errors = new float[2];
                int frames = Mathf.CeilToInt(clip.length * 60);
                for (int frame = 0; frame <= frames; frame++)
                {
                    actor.Evaluate(Mathf.Min(frame / 60f, clip.length));
                    for (int hand = 0; hand < arms.Length; hand++)
                        errors[hand] = Mathf.Max(errors[hand],
                            Vector3.Distance(arms[hand].SourceGrip, arms[hand].TargetGrip));
                }
                Vector3 end = actor.Pose.sourceHips.position;
                float travelError = Vector3.Distance(end - start, record.hipsEnd - record.hipsStart);
                actor.Evaluate(clip.length);
                float repeatError = Vector3.Distance(end, actor.Pose.sourceHips.position);
                if (!float.IsFinite(travelError) || !float.IsFinite(repeatError))
                    throw new InvalidOperationException("Nonfinite axe sampling: " + record.variant);
                return FormattableString.Invariant(
                    $"{fighter.name},{record.guid}:{record.localId},{record.variant},{travelError:R},{repeatError:R},{errors[0]:R},{errors[1]:R}");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
