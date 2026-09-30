using System;
using UnityEngine;

namespace FrankRetarget
{
    public sealed class FrankUnarmedLibrary : ScriptableObject
    {
        public Pair[] pairs;
        [Serializable] public sealed class Pair
        {
            public string label, sourceName;
            public AnimationClip attacker, receiver;
            public Vector3 receiverOffset;
            public Quaternion receiverRotation = Quaternion.identity;
        }
    }
}
