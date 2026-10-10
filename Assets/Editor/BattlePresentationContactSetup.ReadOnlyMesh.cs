using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

public static partial class BattlePresentationContactSetup
{
    static void ReadContactMesh(Mesh mesh, out Vector3[] vertices, out int[] triangles)
    {
        using (var data = MeshUtility.AcquireReadOnlyMeshData(mesh))
        using (var positions = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp))
        {
            data[0].GetVertices(positions);
            vertices = positions.ToArray();
            var indices = new List<int>();
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                using (var native = new NativeArray<int>((int)mesh.GetIndexCount(submesh), Allocator.Temp))
                {
                    data[0].GetIndices(native, submesh);
                    indices.AddRange(native.ToArray());
                }
            }
            triangles = indices.ToArray();
        }
    }
}
