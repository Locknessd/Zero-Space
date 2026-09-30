using System;
using UnityEngine;

namespace FrankRetarget
{
    // Baked corrections are indexed by animation time, so seeking never depends on
    // a previous frame. Only the visible unarmed poses receive these offsets.
    public sealed class FrankPairSpacing : ScriptableObject
    {
        [Serializable] public sealed class Volume
        {
            public string bone;
            public Vector3 center;
            public float radius;
        }
        [Serializable] public sealed class Correction
        {
            public string clip;
            public float duration;
            public Vector3[] separation;
            public Vector3 At(float time)
            {
                if(separation==null || separation.Length==0)return Vector3.zero;
                float frame=Mathf.Clamp01(time/duration)*(separation.Length-1);
                int i=Mathf.Min((int)frame,separation.Length-1);
                return Vector3.Lerp(separation[i],separation[Mathf.Min(i+1,separation.Length-1)],frame-i);
            }
        }
        public Volume[] mankey, pepe;
        public Correction[] corrections;
        public Vector3 Separation(int motion,bool pepeAttacks,float time)
        {
            int i=motion*2+(pepeAttacks?1:0);
            return corrections!=null && i<corrections.Length?corrections[i].At(time):Vector3.zero;
        }
        public static void Apply(FrankTestActor attacker,FrankTestActor receiver,Vector3 separation)
        {
            if(separation.sqrMagnitude<1e-10f)return;
            var a=attacker.Pose;var b=receiver.Pose;
            // Contact takes priority at the few extreme holds: reduce extra clearance
            // before asking an arm to exceed its existing reach limit.
            if(ReachError(a,b,separation)>.03f)
            {
                float low=0,high=1;
                for(int iteration=0;iteration<10;iteration++)
                {
                    float fraction=(low+high)*.5f;
                    if(ReachError(a,b,separation*fraction)>.03f)high=fraction;else low=fraction;
                }
                separation*=low;
            }
            Vector3 da=-separation*.5f, db=separation*.5f;
            // Work out both sides before solving either: contact detection always reads
            // the same uncorrected pose, including hands holding the other actor's ankles.
            var ga=Goals(a,b,da,db);var gb=Goals(b,a,db,da);
            a.targetHips.position+=da;b.targetHips.position+=db;
            for(int i=0;i<a.limbs.Length;i++)a.limbs[i].Solve(ga[i]);
            for(int i=0;i<b.limbs.Length;i++)b.limbs[i].Solve(gb[i]);
            foreach(var bone in a.Posture.toes)bone.Apply();
            foreach(var bone in b.Posture.toes)bone.Apply();
        }
        public static float ReachError(FrankPoseRetarget a,FrankPoseRetarget b,Vector3 separation)
        {
            float worst=0;
            for(int side=0;side<2;side++)
            {
                var own=side==0?a:b;var other=side==0?b:a;var offset=separation*(side==0?-.5f:.5f);
                var goals=Goals(own,other,offset,-offset);
                for(int i=0;i<own.limbs.Length;i++)
                {
                    var limb=own.limbs[i];
                    float length=limb.middle.parent.TransformVector(limb.middleRest).magnitude+limb.end.parent.TransformVector(limb.endRest).magnitude;
                    worst=Mathf.Max(worst,Vector3.Distance(limb.upper.position+offset,goals[i])-length*1.16f);
                }
            }
            return worst;
        }
        static Vector3[] Goals(FrankPoseRetarget own,FrankPoseRetarget other,Vector3 offset,Vector3 otherOffset)
        {
            var result=new Vector3[own.limbs.Length];
            for(int i=0;i<result.Length;i++)
            {
                var limb=own.limbs[i];Vector3 move=offset;
                if(!limb.sourceKnuckle)
                {
                    // Keep planted feet anchored; airborne feet travel with the body.
                    move*=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.16f,.38f,limb.sourceEnd.position.y));
                }
                else
                {
                    float best=0;Vector3 contactMove=offset;
                    foreach(var partner in other.limbs)
                    {
                        float distance=Vector3.Distance(limb.sourceEnd.position,partner.sourceEnd.position);
                        float weight=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.10f,.28f,distance));
                        if(weight<=best)continue;
                        best=weight;
                        contactMove=partner.sourceKnuckle?(offset+otherOffset)*.5f:
                            otherOffset*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.16f,.38f,partner.sourceEnd.position.y));
                    }
                    // A torso hold follows the partner's torso rather than pulling apart
                    // with its own character. Free hands keep their authored gesture.
                    float torsoDistance=Vector3.Distance(limb.sourceEnd.position,other.sourceHips.position);
                    float torso=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.22f,.48f,torsoDistance));
                    if(torso>best){best=torso;contactMove=otherOffset;}
                    move=Vector3.Lerp(offset,contactMove,best);
                }
                result[i]=limb.Goal+move;
            }
            return result;
        }
    }
}
