using System;
using UnityEngine;

namespace FrankRetarget
{
    // A source skeleton and its calibration only: no visible character is stored here.
    public sealed class FrankTestDriver : MonoBehaviour
    {
        public FrankPoseRetarget pose;
        public Vector3 characterScale;
        public string hips;
        public TargetLimb[] targetLimbs;
        public TargetFinger[] targetFingers;
        [Serializable] public sealed class TargetLimb
        {
            public string upper, middle, end, knuckle, joint;
        }
        [Serializable] public sealed class TargetFinger
        {
            public string proximal, intermediate, distal;
        }
        public void Bind(Animator character)
        {
            pose.enabled=false;
            character.transform.localScale=characterScale;
            pose.character=character;
            pose.targetHips=character.transform.Find(hips);
            for(int i=0;i<targetLimbs.Length;i++)
            {
                var p=targetLimbs[i];var l=pose.limbs[i];
                l.upper=character.transform.Find(p.upper);l.middle=character.transform.Find(p.middle);l.end=character.transform.Find(p.end);
                l.targetKnuckle=string.IsNullOrEmpty(p.knuckle)?null:character.transform.Find(p.knuckle);
                l.targetFingerJoint=string.IsNullOrEmpty(p.joint)?null:character.transform.Find(p.joint);
            }
            for(int i=0;i<targetFingers.Length;i++)
            {
                var p=targetFingers[i];var f=pose.fingers[i];
                f.proximal=character.transform.Find(p.proximal);f.intermediate=character.transform.Find(p.intermediate);f.distal=character.transform.Find(p.distal);
            }
            pose.Initialize();
        }
    }
}
