using System;
using UnityEngine;

namespace FrankRetarget
{
    // A source skeleton and its calibration only: no visible character is stored here.
    public sealed partial class FrankTestDriver : MonoBehaviour
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
        public void Bind(Animator character, bool preserveCharacterScale = false)
        {
            pose.enabled=false;
            if (preserveCharacterScale)
            {
                var appearance = character.GetComponent<BattleCharacterAppearance>();
                var scale = appearance ? appearance.SourceMotionScale(characterScale) : character.transform.localScale;
                // Battle owns visible model size. Resize the hidden source equally
                // so its authored hips, limb contacts and weapons still fit.
                transform.localScale = Vector3.Scale(transform.localScale, new Vector3(
                    scale.x / characterScale.x, scale.y / characterScale.y, scale.z / characterScale.z));
            }
            else character.transform.localScale=characterScale;
            pose.character=character;
            if (!character.transform.Find(hips))
            {
                BindHumanoid(character);
                pose.Initialize();
                return;
            }
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
