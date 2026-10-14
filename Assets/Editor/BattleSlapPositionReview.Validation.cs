using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    public static partial class BattleSlapPositionReview
    {
        static void CheckFinalSpacing(CharacterCombat source, CharacterCombat target, CombatTripletData move,
            float midpoint)
        {
            float sourceX = source.Animator.transform.position.x;
            float targetX = target.Animator.transform.position.x;
            if (Mathf.Abs(Mathf.Abs(sourceX - targetX) - move.sourcePair.standingRecoverySpacing) > .0001f ||
                Mathf.Abs((sourceX + targetX) * .5f - midpoint) > .0001f)
                throw new InvalidOperationException("Slap survivors did not reach the configured spacing and midpoint.");
        }

        static void CheckStableEvaluation(FrankBattlePairPlayback playback, CharacterCombat[] fighters, bool pause)
        {
            var bones = fighters.SelectMany(f => f.Animator.GetComponentsInChildren<Transform>(true)).ToArray();
            var positions = bones.Select(b => b.position).ToArray();
            if (pause)
                foreach (var fighter in fighters)
                    fighter.Animator.speed = 0;
            try
            {
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    if (pause)
                    {
                        playback.PrepareRecoveryPoseEvaluation();
                        foreach (var fighter in fighters)
                            fighter.Animator.Update(1f / 30);
                    }
                    playback.EvaluateRecoveryPose();
                    for (int i = 0; i < bones.Length; i++)
                        if (Vector3.Distance(bones[i].position, positions[i]) > .0001f)
                            throw new InvalidOperationException("Repeated/paused recovery moved " + bones[i].name);
                }
            }
            finally
            {
                if (pause)
                    foreach (var fighter in fighters)
                        fighter.Animator.speed = 1;
            }
        }

        static void ValidatePreservedMotion()
        {
            var before = File.ReadAllLines(Output + "/Before.csv").Skip(1).ToArray();
            var after = File.ReadAllLines(Output + "/After.csv").Skip(1).ToArray();
            if (before.Length != after.Length)
                throw new InvalidOperationException("Incomplete Slap before/after pose coverage.");
            for (int row = 0; row < before.Length; row++)
            {
                var old = before[row].Split(',');
                var current = after[row].Split(',');
                if (!old.Take(5).SequenceEqual(current.Take(5)))
                    throw new InvalidOperationException("Slap before/after sampling differs.");
                float offset = Read(current[5]) - Read(old[5]);
                if (!old[3].StartsWith("recovery") && Mathf.Abs(offset) > .0001f)
                    throw new InvalidOperationException("Slap root moved before standing recovery.");
                for (int column = 5; column < old.Length; column++)
                {
                    float expected = Read(old[column]) + ((column - 5) % 3 == 0 ? offset : 0);
                    if (Mathf.Abs(Read(current[column]) - expected) > .0001f)
                        throw new InvalidOperationException(
                            $"Slap pose changed beyond recovery X: row={row}, field={column}");
                }
            }
            File.WriteAllText(Output + "/Validation.txt",
                "PASS: both SlapFace sequences, both attacker avatars and both directions (8 native cases).\n" +
                "Final survivor root spacing: 1.6m; unchanged midpoint; continuous recovery entry.\n" +
                "Repeated evaluation and paused Animator clocks preserve all transforms.\n" +
                "Before/after hips and feet retain their poses, floor height and depth; only recovery X changes.\n");
            SyncLoadedScene();
        }

        static void SyncLoadedScene()
        {
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/BattleScene.unity");
            if (!scene.IsValid() || !scene.isLoaded) return;
            bool changed = false;
            foreach (var fighter in scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<CharacterCombat>(true)))
            {
                var moves = fighter.lightCombatMoves.Where(move => move != null &&
                    move.moveName.StartsWith("SlapFace_") && move.sourcePair != null).ToArray();
                if (moves.All(move => Mathf.Abs(move.sourcePair.standingRecoverySpacing - 1.6f) < .0001f))
                    continue;
                Undo.RecordObject(fighter, "Correct SlapFace idle spacing");
                foreach (var move in moves)
                    move.sourcePair.standingRecoverySpacing = 1.6f;
                EditorUtility.SetDirty(fighter);
                changed = true;
            }
            if (changed)
                EditorSceneManager.MarkSceneDirty(scene);
            File.AppendAllText(Output + "/Validation.txt",
                "Open BattleScene Slap settings synchronized; other unsaved scene settings preserved.\n");
        }

        static float Read(string value) => float.Parse(value, CultureInfo.InvariantCulture);
    }
}
