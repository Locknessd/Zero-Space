using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrankRetarget
{
    public sealed class FrankWeaponRig : MonoBehaviour
    {
        public Transform[] bodyBones;
        public Transform[] fingerBones;
        public Quaternion[] gripRotations;
        public Renderer[] meshes;
        Transform[] sources, sourceFingers;
        public void Bind(Transform source)
        {
            var map=new Dictionary<string,Transform>();
            foreach(var t in source.GetComponentsInChildren<Transform>(true))map.TryAdd(t.name,t);
            sources=new Transform[bodyBones.Length];sourceFingers=new Transform[fingerBones.Length];
            for(int i=0;i<sources.Length;i++)map.TryGetValue(bodyBones[i].name,out sources[i]);
            for(int i=0;i<sourceFingers.Length;i++)map.TryGetValue(fingerBones[i].name,out sourceFingers[i]);
        }
        // The selected weapon supplies a closed grasp; the selected clip still supplies arm motion.
        public void ApplyGrasp()
        {
            for(int i=0;i<sourceFingers.Length;i++)if(sourceFingers[i])sourceFingers[i].localRotation=gripRotations[i];
        }
        public void Follow()
        {
            for(int i=0;i<sources.Length;i++)if(sources[i])bodyBones[i].SetPositionAndRotation(sources[i].position,sources[i].rotation);
        }
    }
}
