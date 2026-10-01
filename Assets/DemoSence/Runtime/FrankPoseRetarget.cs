using System;
using UnityEngine;

namespace FrankRetarget
{
    /// <summary>
    /// The original Animator owns time, transitions and all prop tracks. Read its evaluated
    /// skeleton after animation and transfer a Humanoid pose, then restore authored world-space
    /// pelvis travel and contact points. No root delta is integrated twice or reset on state exit.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class FrankPoseRetarget : MonoBehaviour
    {
        public Animator driver;
        public Avatar sourceHumanAvatar;
        public Animator character;
        public string characterName;
        public string weapon;
        public bool isAttacker;
        public Transform sourceHips;
        public Transform targetHips;
        public Limb[] limbs;
        public Renderer[] originalBody;
        public Renderer[] weaponRenderers;
        public AnimationClip[] sourceClips;
        public Finger[] fingers;
        public bool transferFingers=true;
        public bool constrainTargetHipsDepth;
        float constrainedTargetHipsDepth;

        HumanPoseHandler sourceHandler, targetHandler;
        HumanPose pose;
        public FrankPostureCalibration Posture { get; private set; }
        [Serializable]
        public sealed class Finger
        {
            public Transform sourceProximal,sourceIntermediate,sourceDistal;
            public Transform proximal,intermediate,distal;
            public void Apply()
            {
                proximal.rotation=Quaternion.FromToRotation(intermediate.position-proximal.position,sourceIntermediate.position-sourceProximal.position)*proximal.rotation;
                intermediate.rotation=Quaternion.FromToRotation(distal.position-intermediate.position,sourceDistal.position-sourceIntermediate.position)*intermediate.rotation;
            }
        }

        [Serializable]
        public sealed class Limb
        {
            public Transform sourceUpper, sourceMiddle, sourceEnd;
            public Transform upper, middle, end;
            public Quaternion rotationOffset = Quaternion.identity;
            public Vector3 positionOffset;
            public Vector3 middleRest, endRest;
            public Transform sourceKnuckle, sourceFingerJoint, targetKnuckle, targetFingerJoint;
            public bool alignGrip;
            [NonSerialized] public float error;
            public Vector3 SourceGrip => (sourceKnuckle.position+sourceFingerJoint.position)*0.5f;
            public Vector3 TargetGrip => (targetKnuckle.position+targetFingerJoint.position)*0.5f;
            public Vector3 Goal
            {
                get
                {
                    if(!alignGrip)return sourceEnd.position+sourceEnd.rotation*positionOffset;
                    Quaternion desired=sourceEnd.rotation*rotationOffset;
                    Vector3 gripFromWrist=Quaternion.Inverse(end.rotation)*(TargetGrip-end.position);
                    return SourceGrip-desired*gripFromWrist;
                }
            }

            public void Solve(){Solve(Goal);}
            public void Solve(Vector3 goal)
            {
                // Restore lengths every frame: stretch must never accumulate across loops.
                middle.localPosition = middleRest;
                end.localPosition = endRest;
                Vector3 start=upper.position;
                float a=Vector3.Distance(start,middle.position), b=Vector3.Distance(middle.position,end.position);
                Vector3 delta=goal-start;
                float distance=delta.magnitude;
                if(distance<0.00001f || a<0.00001f || b<0.00001f)return;
                // Small reach compensation accommodates different proportions at authored contacts.
                float stretch=Mathf.Clamp((distance+0.00001f)/(a+b),1f,1.16f);
                middle.localPosition=middleRest*stretch;
                end.localPosition=endRest*stretch;
                a*=stretch; b*=stretch;
                Vector3 direction=delta/distance;
                Vector3 pole=sourceMiddle.position-sourceUpper.position;
                pole-=direction*Vector3.Dot(pole,direction);
                if(pole.sqrMagnitude<0.000001f)
                {
                    pole=middle.position-start;
                    pole-=direction*Vector3.Dot(pole,direction);
                }
                if(pole.sqrMagnitude<0.000001f) pole=Vector3.Cross(direction,Mathf.Abs(direction.y)<0.9f?Vector3.up:Vector3.right);
                float d=Mathf.Clamp(distance,Mathf.Abs(a-b)+0.00001f,a+b-0.00001f);
                float along=(a*a-b*b+d*d)/(2*d);
                Vector3 elbow=start+direction*along+pole.normalized*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
                upper.rotation=Quaternion.FromToRotation(middle.position-start,elbow-start)*upper.rotation;
                middle.rotation=Quaternion.FromToRotation(end.position-middle.position,goal-middle.position)*middle.rotation;
                end.rotation=sourceEnd.rotation*rotationOffset;
                error=Vector3.Distance(end.position,goal);
            }
        }

        void OnEnable() { Initialize(); }
        public void Initialize()
        {
            if(sourceHandler!=null || !driver || !character || !sourceHumanAvatar || !character.avatar)return;
            if(!sourceHumanAvatar.isHuman || !character.avatar.isHuman)
                throw new InvalidOperationException("Retargeting needs valid Humanoid calibration avatars.");
            driver.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            // The visible skeleton has one writer. Its Animator supplies the avatar only.
            character.enabled=false;
            sourceHandler=new HumanPoseHandler(sourceHumanAvatar,driver.transform);
            targetHandler=new HumanPoseHandler(character.avatar,character.transform);
            Posture=new FrankPostureCalibration(this);
            foreach(var r in originalBody) if(r)r.enabled=false;
        }

        void LateUpdate() { ApplyPose(); }
        public void ApplyPose()
        {
            Initialize();
            if(sourceHandler==null)return;
            sourceHandler.GetHumanPose(ref pose);

            // GetHumanPose includes the driver root rotation in its parent space. Convert
            // through that parent to world, then into the visible root space. This also
            // handles a driver nested under a rotated persistent actor.
            Quaternion sourceParent=driver.transform.parent?driver.transform.parent.rotation:Quaternion.identity;
            pose.bodyRotation=Quaternion.Inverse(character.transform.rotation)*sourceParent*pose.bodyRotation;
            pose.bodyPosition=Vector3.zero;
            targetHandler.SetHumanPose(ref pose);

            // Preserve the authored pelvis and IK contacts in the same coordinate space.
            targetHips.position=sourceHips.position;
            // Some source avatars report neck/head muscle values beyond +/- 4. Replaying
            // those values through another avatar over-rotates its neck. Quaternion transfer
            // preserves the authored orientation without extrapolating muscle limits.
            Posture.ApplyCervical();
            // Transfer finger segment directions after aligning the palms. A valid Humanoid
            // avatar does not guarantee matching finger bend axes on these stylized rigs.
            foreach(var limb in limbs)if(limb.alignGrip)limb.end.rotation=limb.sourceEnd.rotation*limb.rotationOffset;
            if(transferFingers && fingers!=null)foreach(var finger in fingers)finger.Apply();
            foreach(var limb in limbs)limb.Solve();
            // Foot IK changes the ankle orientation after Humanoid evaluation. Reapply the
            // authored toe orientation afterward so toe bend is not counted a second time.
            foreach(var bone in Posture.toes)bone.Apply();
            if (constrainTargetHipsDepth && targetHips)
            {
                Vector3 locked = targetHips.position;
                locked.z = constrainedTargetHipsDepth;
                targetHips.position = locked;
            }
        }

        public void LockTargetHipsDepth(float depth)
        {
            if (!float.IsFinite(depth)) return;
            constrainedTargetHipsDepth = depth;
            constrainTargetHipsDepth = true;
            if (targetHips)
            {
                Vector3 locked = targetHips.position;
                locked.z = depth;
                targetHips.position = locked;
            }
        }

        void OnDisable() { Release(); }
        void OnDestroy() { Release(); }
        void Release()
        {
            sourceHandler?.Dispose(); targetHandler?.Dispose();
            sourceHandler=null; targetHandler=null;Posture=null;
        }
    }
}
