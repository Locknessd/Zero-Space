using System;
using UnityEngine;
namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        static void CABlendShapes(SkinnedMeshRenderer skin, Vector3[] positions)
        {
            Mesh mesh = skin.sharedMesh;
            var first = new Vector3[positions.Length];
            var second = new Vector3[positions.Length];
            for (int shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                float weight = skin.GetBlendShapeWeight(shape);
                if (Mathf.Abs(weight) < .000001f)
                    continue;
                int frames = mesh.GetBlendShapeFrameCount(shape);
                if (frames == 0)
                    continue;
                int high = 0;
                while (high < frames - 1 && mesh.GetBlendShapeFrameWeight(shape, high) < weight)
                    high++;
                int low = high - 1;
                if (high == frames - 1 && weight > mesh.GetBlendShapeFrameWeight(shape, high) && frames > 1)
                    low = high - 1;
                float lower = low < 0 ? 0 : mesh.GetBlendShapeFrameWeight(shape, low);
                float upper = mesh.GetBlendShapeFrameWeight(shape, high);
                Array.Clear(first, 0, first.Length);
                if (low >= 0)
                    mesh.GetBlendShapeFrameVertices(shape, low, first, null, null);
                mesh.GetBlendShapeFrameVertices(shape, high, second, null, null);
                float blend = Mathf.Abs(upper - lower) < .000001f ? 1 : (weight - lower) / (upper - lower);
                for (int vertex = 0; vertex < positions.Length; vertex++)
                    positions[vertex] += Vector3.LerpUnclamped(first[vertex], second[vertex], blend);
            }
        }

    }
}
