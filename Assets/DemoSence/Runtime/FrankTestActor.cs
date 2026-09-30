using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace FrankRetarget
{
    public sealed class FrankTestActor : MonoBehaviour
    {
        public string characterName;
        public Animator character;
        public FrankTestDriver[] attackDrivers, reactionDrivers;
        public FrankTestDriver activeDriver;
        public FrankTestDriver unarmedDriver, comboDriver;
        public FrankTestDriver greatSwordAttackDriver, greatSwordReactionDriver;
        public FrankWeaponRig equipped;
        public AnimationClip clip;
        PlayableGraph graph;
        AnimationClipPlayable playable;
        public FrankPoseRetarget Pose => activeDriver?activeDriver.pose:null;

        public void Configure(int motion,bool attacking,bool alternate,FrankWeaponRig weapon,int weaponIndex)
        {
            Clear();
            var sourceDrivers=attacking?attackDrivers:reactionDrivers;
            if(sourceDrivers==null || motion<0 || motion>=sourceDrivers.Length || !sourceDrivers[motion])
            {
                // The legacy Frank GreatSword slot was removed when the authored
                // Execution_Sample drivers replaced it. Keep that old library
                // selection harmless; the dedicated GreatSword tab uses
                // ConfigureGreatSword below and has its own calibrated drivers.
                clip=null;
                return;
            }
            activeDriver=Instantiate(sourceDrivers[motion],transform,false);
            activeDriver.name=(attacking?"Attack":"Reaction")+" animation skeleton";
            activeDriver.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);
            activeDriver.Bind(character);
            var pose=activeDriver.pose;
            clip=Array.Find(pose.sourceClips,c=>c.name.EndsWith("Hit2")==(!attacking&&alternate&&motion==6));
            if(!clip)clip=pose.sourceClips[0];
            bool native=attacking&&weapon&&weaponIndex==motion;
            foreach(var r in pose.weaponRenderers)if(r)r.enabled=native;
            if(weapon&&!native)
            {
                equipped=Instantiate(weapon,activeDriver.transform,false);
                equipped.name="Equipped "+weapon.name;
                equipped.Bind(pose.driver.transform);
            }
            foreach(var limb in pose.limbs)
                if(limb.sourceKnuckle)limb.alignGrip=weapon!=null;
            // Original reaction fingers are left free when no weapon is equipped.
            pose.transferFingers=attacking||weapon;
            StartGraph();
        }
        public void ConfigureUnarmed(AnimationClip animation, bool attacking)
        { ConfigureClip(unarmedDriver,animation,attacking,false); }
        public void ConfigureCombo(AnimationClip animation,bool attacking)
        { ConfigureClip(comboDriver,animation,attacking,attacking); }
        public void ConfigureGreatSword(AnimationClip animation,bool attacking,GameObject weaponPrefab)
        {
            Clear();
            var driver=attacking?greatSwordAttackDriver:greatSwordReactionDriver;
            if(!driver || !animation) return;
            activeDriver=Instantiate(driver,transform,false);
            activeDriver.name=(attacking?"GreatSword attack":"GreatSword reaction")+" animation skeleton";
            activeDriver.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);
            activeDriver.Bind(character);
            clip=animation;
            var pose=activeDriver.pose;
            pose.isAttacker=attacking;
            pose.transferFingers=true;
            // The source driver may contain a calibration weapon. It is not used for
            // this library; the sample mesh below follows the authored IK socket.
            foreach(var renderer in pose.weaponRenderers)if(renderer)renderer.enabled=false;
            if(attacking && weaponPrefab)
            {
                // Execution_Sample parents GreatSword_01 to the authored IK hand
                // socket. Keep that socket and its local identity transform instead
                // of rebuilding the sword mesh around a different hand convention.
                // The sample scene parents the sword to the authored
                // ik_hand_root/ik_hand_gun/ik_hand_r chain. Resolve that
                // chain first; the name-only fallback keeps older generated
                // drivers compatible without guessing a different hand.
                var socket=pose.driver.transform.Find("ik_hand_root/ik_hand_gun/ik_hand_r");
                if(!socket)
                    foreach(var handRoot in pose.driver.transform.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="ik_hand_root"))
                    {
                        socket=handRoot.Find("ik_hand_gun/ik_hand_r");
                        if(socket)break;
                    }
                if(!socket)socket=pose.driver.transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="ik_hand_r");
                if(!socket)
                {
                    Debug.LogError("GreatSword Execution_Sample driver has no authored ik_hand_r socket.");
                    socket=pose.driver.transform;
                }
                var sword=Instantiate(weaponPrefab,socket,false);
                sword.name="GreatSword_01 (Execution Sample)";
                sword.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);
                sword.transform.localScale=Vector3.one;
                foreach(var renderer in sword.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.enabled=true;
                    if(renderer is SkinnedMeshRenderer sk)sk.updateWhenOffscreen=true;
                }
            }
            StartGraph();
        }
        void ConfigureClip(FrankTestDriver driver,AnimationClip animation,bool attacking,bool weapons)
        {
            Clear();
            activeDriver=Instantiate(driver,transform,false);
            activeDriver.name=(attacking?"Active":"Passive")+" animation skeleton";
            activeDriver.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);
            activeDriver.Bind(character);
            clip=animation;
            activeDriver.pose.isAttacker=attacking;
            activeDriver.pose.transferFingers=true;
            foreach(var limb in activeDriver.pose.limbs)limb.alignGrip=weapons&&limb.sourceKnuckle;
            foreach(var renderer in activeDriver.pose.weaponRenderers)renderer.enabled=weapons;
            StartGraph();
        }
        void StartGraph()
        {
            var pose=activeDriver.pose;
            graph=PlayableGraph.Create(characterName+" preview");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            playable=AnimationClipPlayable.Create(graph,clip);
            playable.SetApplyFootIK(false);playable.SetApplyPlayableIK(false);playable.SetSpeed(0);
            var output=AnimationPlayableOutput.Create(graph,"Authored motion",pose.driver);
            output.SetSourcePlayable(playable);graph.Play();
            Evaluate(0);
        }
        public void Evaluate(float time)
        {
            if(!activeDriver||!graph.IsValid())return;
            double t=Mathf.Clamp(time,0,clip.length);
            playable.SetTime(t);graph.Evaluate(0);
            if(equipped){equipped.ApplyGrasp();equipped.Follow();}
            Pose.ApplyPose();
        }
        public void Clear()
        {
            if(graph.IsValid())graph.Destroy();
            if(activeDriver)
            {
                activeDriver.gameObject.SetActive(false);
                if(Application.isPlaying)Destroy(activeDriver.gameObject);else DestroyImmediate(activeDriver.gameObject);
            }
            activeDriver=null;equipped=null;
        }
        void OnDestroy(){if(graph.IsValid())graph.Destroy();}
    }
}
