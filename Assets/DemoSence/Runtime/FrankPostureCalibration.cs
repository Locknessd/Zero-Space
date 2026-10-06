using System.Collections.Generic;
using UnityEngine;

namespace FrankRetarget
{
    /// <summary>Bone-space calibration from avatar reference poses, never from an animated frame.</summary>
    public sealed class FrankPostureCalibration
    {
        public sealed class Rotation
        {
            public string name;
            public Transform source, target;
            public Quaternion offset;
            public Quaternion Goal => source.rotation*offset;
            public void Apply(){target.rotation=Goal;}
        }
        public readonly Rotation[] cervical, toes;
        readonly Quaternion neckRestInParent, headRestInNeck;
        public float NeckBend { get; private set; }
        public float AuthoredNeckBend { get; private set; }
        public const float NeckBendLimit=65f;

        public void ApplyCervical()
        {
            if(cervical.Length!=2){foreach(var bone in cervical)bone.Apply();return;}
            var neck=cervical[0];var head=cervical[1];
            Quaternion neutralNeck=neck.target.parent.rotation*neckRestInParent;
            Quaternion neutralHead=neutralNeck*headRestInNeck;
            Quaternion headGoal=head.Goal;
            // Share the look rotation between the two joints instead of copying a sharp
            // source-neck bend into characters with very different head/neck proportions.
            Quaternion lookDelta=headGoal*Quaternion.Inverse(neutralHead);
            // At a nearly reversed look direction the shortest quaternion arc changes
            // sign. Fade the neck share there instead of creating a one-frame neck snap.
            float lookAngle=Quaternion.Angle(Quaternion.identity,lookDelta);
            float share=.45f*(1f-Mathf.SmoothStep(0,1,Mathf.InverseLerp(120f,180f,lookAngle)));
            Quaternion desired=Quaternion.Slerp(Quaternion.identity,lookDelta,share)*neutralNeck;
            AuthoredNeckBend=Quaternion.Angle(neutralNeck,neck.Goal);
            float angle=Quaternion.Angle(neutralNeck,desired);
            // A soft limit has no sudden stop at the edge of the comfortable bend range.
            float softRange=NeckBendLimit-40f;
            float softened=angle<=40f?angle:40f+softRange*(float)System.Math.Tanh((angle-40f)/softRange);
            neck.target.rotation=Quaternion.Slerp(neutralNeck,desired,angle>0.0001f?softened/angle:1f);
            NeckBend=Quaternion.Angle(neutralNeck,neck.target.rotation);
            // Preserve the authored gaze; the head joint supplies the remaining rotation.
            head.target.rotation=headGoal;
        }

        public FrankPostureCalibration(FrankPoseRetarget pose)
        {
            var source=new Reference(pose.driver.transform,pose.sourceHumanAvatar);
            var target=new Reference(pose.character.transform,pose.character.avatar);
            cervical=Map(source,target,new[]{"Neck","Head"});
            toes=Map(source,target,new[]{"LeftToes","RightToes"});
            if(cervical.Length==2)
            {
                var neck=cervical[0].target;var head=cervical[1].target;
                neckRestInParent=Quaternion.Inverse(target.WorldRotation(neck.parent))*target.WorldRotation(neck);
                headRestInNeck=Quaternion.Inverse(target.WorldRotation(neck))*target.WorldRotation(head);
            }
            foreach(string side in new[]{"Left","Right"})
            {
                var foot=Map(source,target,new[]{side+"Foot"});
                if(foot.Length==0)continue;
                foreach(var limb in pose.limbs)
                    if(limb.end==foot[0].target)limb.rotationOffset=foot[0].offset;
            }
        }
        static Rotation[] Map(Reference source,Reference target,string[] names)
        {
            var result=new List<Rotation>();
            foreach(string name in names)
            {
                if(!source.TryBone(name,out var s)||!target.TryBone(name,out var t))continue;
                result.Add(new Rotation{name=name,source=s,target=t,
                    offset=Quaternion.Inverse(source.WorldRotation(s))*target.WorldRotation(t)});
            }
            return result.ToArray();
        }
        sealed class Reference
        {
            readonly Transform root;
            readonly Dictionary<string,Transform> transforms=new Dictionary<string,Transform>();
            readonly Dictionary<string,string> human=new Dictionary<string,string>();
            readonly Dictionary<string,Quaternion> rotations=new Dictionary<string,Quaternion>();
            public Reference(Transform root,Avatar avatar)
            {
                this.root=root;
                foreach(var t in root.GetComponentsInChildren<Transform>(true))transforms.TryAdd(t.name,t);
                var description=avatar.humanDescription;
                foreach(var b in description.human)human[b.humanName]=b.boneName;
                foreach(var b in description.skeleton)rotations.TryAdd(b.name,b.rotation);
            }
            public bool TryBone(string name,out Transform bone)
            {
                bone=null;return human.TryGetValue(name,out string mapped)&&transforms.TryGetValue(mapped,out bone);
            }
            public Quaternion WorldRotation(Transform bone)
            {
                Quaternion q=Quaternion.identity;
                while(bone&&bone!=root)
                {
                    q=(rotations.TryGetValue(bone.name,out var rest)?rest:bone.localRotation)*q;
                    bone=bone.parent;
                }
                return q;
            }
        }
    }
}
