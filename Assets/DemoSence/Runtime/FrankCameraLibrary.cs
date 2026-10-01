using System;
using UnityEngine;

namespace FrankRetarget
{
    /// <summary>World-space camera takes authored for one animation pair and character role.</summary>
    [CreateAssetMenu(menuName = "Frank Demo/Cinematic Camera Library")]
    public sealed class FrankCameraLibrary : ScriptableObject
    {
        public Shot[] shots = Array.Empty<Shot>();

        [Serializable]
        public sealed class Shot
        {
            public string key;
            public string label;
            [TextArea] public string direction;
            public float duration;
            public Frame[] frames = Array.Empty<Frame>();
        }

        [Serializable]
        public struct Frame
        {
            public float time;
            public Vector3 position;
            public Vector3 focus;
            public float fov;
        }

        public Shot Find(string key)
        {
            if (shots == null) return null;
            foreach (var shot in shots)
                if (shot != null && shot.key == key) return shot;
            return null;
        }
    }
}
