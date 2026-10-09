using System;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapLethalAuthoring
    {
        // Read upstream Humanoid DOFs before converting the resulting skeleton back into muscles.
        // This job never writes the animation stream, solves IK, or changes root motion.
        struct ReadMusclesJob : IAnimationJob
        {
            [ReadOnly] public NativeArray<MuscleHandle> handles;
            public NativeArray<float> values;

            public void ProcessAnimation(AnimationStream stream)
            {
                var human = stream.AsHuman();
                for (int i = 0; i < handles.Length; i++)
                    values[i] = human.GetMuscle(handles[i]);
            }

            public void ProcessRootMotion(AnimationStream stream)
            {
            }
        }

        sealed class MuscleStreamCapture : IDisposable
        {
            NativeArray<MuscleHandle> handles;
            NativeArray<float> values;

            public MuscleStreamCapture()
            {
                try
                {
                    var available = new MuscleHandle[MuscleHandle.muscleHandleCount];
                    MuscleHandle.GetMuscleHandles(available);
                    // Never assume handle enumeration matches HumanTrait ordering, especially fingers.
                    var ordered = Enumerable.Range(0, HumanTrait.MuscleCount).Select(index =>
                    {
                        var matches = available.Where(handle => handle.name == HumanTrait.MuscleName[index] ||
                            handle.name == MuscleBindingName(index)).ToArray();
                        if (matches.Length != 1)
                            throw new InvalidOperationException("Ambiguous/missing native muscle handle: " +
                                HumanTrait.MuscleName[index] + "; available=" +
                                string.Join(",", available.Select(handle => handle.name)));
                        return matches[0];
                    }).ToArray();
                    if (ordered.Length != available.Length || ordered.Distinct().Count() != ordered.Length)
                        throw new InvalidOperationException("Native muscle handle schema is not bijective.");
                    handles = new NativeArray<MuscleHandle>(ordered, Allocator.Persistent);
                    values = new NativeArray<float>(ordered.Length, Allocator.Persistent);
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public AnimationScriptPlayable Connect(PlayableGraph graph, AnimationClipPlayable input)
            {
                for (int i = 0; i < values.Length; i++)
                    values[i] = float.NaN;
                var job = new ReadMusclesJob { handles = handles, values = values };
                var playable = AnimationScriptPlayable.Create(graph, job, 1);
                graph.Connect(input, 0, playable, 0);
                playable.SetInputWeight(0, 1);
                return playable;
            }

            public float[] Read()
            {
                var result = values.ToArray();
                if (result.Any(value => !float.IsFinite(value)))
                    throw new InvalidOperationException("Native muscle stream capture did not produce finite values.");
                return result;
            }

            public void Dispose()
            {
                if (handles.IsCreated)
                    handles.Dispose();
                if (values.IsCreated)
                    values.Dispose();
            }
        }
    }
}
