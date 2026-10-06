using System;
using UnityEngine;

namespace FrankRetarget
{
    /// <summary>
    /// The four authored execution pairs from GreatSword_Animset's sample scene.
    /// Clips stay as direct references to the original, uncompressed FBX takes so
    /// the importer remains the single source of animation data.
    /// </summary>
    public sealed class FrankGreatSwordLibrary : ScriptableObject
    {
        public Pair[] pairs;

        [Serializable]
        public sealed class Pair
        {
            public string label;
            public string sourceName;
            public AnimationClip attacker;
            public AnimationClip receiver;
            public Vector3 receiverOffset;
            public Quaternion receiverRotation = Quaternion.Euler(0, 180, 0);
        }
    }
}
