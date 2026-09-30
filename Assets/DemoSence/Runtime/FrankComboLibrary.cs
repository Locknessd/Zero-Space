using System;
using UnityEngine;
namespace FrankRetarget
{
    public sealed class FrankComboLibrary : ScriptableObject
    {
        public Pair[] pairs;
        [Serializable] public sealed class Pair
        {
            public string label, sourceName;
            public int combo, step;
            public AnimationClip attack, reaction;
            public Vector3 receiverOffset;
            public float impactTime, groundTime, sourceMatchTime;
            public float[] mankeyFloor, pepeFloor;
            public float FloorOffset(bool mankey,float time)
            {
                var samples=mankey?mankeyFloor:pepeFloor;
                if(samples==null||samples.Length==0)return 0;
                float frame=Mathf.Clamp01(time/reaction.length)*(samples.Length-1);int i=(int)frame;
                return Mathf.Lerp(samples[i],samples[Mathf.Min(i+1,samples.Length-1)],frame-i);
            }
        }
    }
}
