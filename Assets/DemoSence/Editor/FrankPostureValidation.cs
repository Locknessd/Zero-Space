using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        // Normalize in double precision: Quaternion.Angle assumes unit inputs and can
        // report spurious ~0.2 degree steps for deeply nested, almost-stationary bones.
        static float RotationAngle(Quaternion a,Quaternion b)
        {
            double dot=(double)a.x*b.x+(double)a.y*b.y+(double)a.z*b.z+(double)a.w*b.w;
            double aa=(double)a.x*a.x+(double)a.y*a.y+(double)a.z*a.z+(double)a.w*a.w;
            double bb=(double)b.x*b.x+(double)b.y*b.y+(double)b.z*b.z+(double)b.w*b.w;
            return (float)(2*Math.Acos(Math.Min(1,Math.Abs(dot)/Math.Sqrt(aa*bb)))*180/Math.PI);
        }
        static Quaternion RestRotation(Transform t,Transform root,Avatar avatar)
        {
            var bones=avatar.humanDescription.skeleton.GroupBy(b=>b.name).ToDictionary(g=>g.Key,g=>g.First());
            Quaternion q=Quaternion.identity;
            while(t&&t!=root){q=(bones.TryGetValue(t.name,out var b)?b.rotation:t.localRotation)*q;t=t.parent;}
            return q;
        }
        [MenuItem("Tools/Frank Retarget/Validate neck, ankles and toes at 60 Hz")]
        public static void ValidatePosture()
        {
            string folder=Output+"/Reports/Posture";Directory.CreateDirectory(folder);
            var scene=EditorSceneManager.OpenPreviewScene(Output+"/Frank_Damages_Mankey_Pepe.unity");
            var tester=scene.GetRootGameObjects().Select(g=>g.GetComponent<FrankCombinationTester>()).First(t=>t);
            var csv=new StringBuilder("character,clip,frames,headOrientationErrorDeg,ankleToeErrorDeg,pelvisError,contactError,rotationStepErrorDeg\n");
            int clips=0,frames=0;float headError=0,footError=0,pelvisError=0,contactError=0,stepError=0,maxNeck=0,maxAuthoredNeck=0,maxNeckStep=0,maxAddedNeckStep=0,neckSeekDrift=0;
            var jumps=new StringBuilder();
            var visited=new HashSet<string>();
            try
            {
                for(int role=0;role<2;role++)for(int motion=0;motion<8;motion++)
                {
                    tester.gunSword=false;tester.unarmed=false;tester.pepeAttacks=role==1;tester.motion=motion;tester.attackerWeapon=4;tester.receiverWeapon=6;tester.Configure();
                    foreach(var actor in new[]{tester.Attacker,tester.Receiver})
                    {
                        if(!visited.Add(actor.characterName+actor.clip.name))continue;
                        var p=actor.Pose;
                        var names=new[]{"Neck","Head","LeftFoot","RightFoot","LeftToes","RightToes"};
                        var src=names.Select(n=>HumanBone(p.driver.transform,p.sourceHumanAvatar,n)).ToArray();
                        var dst=names.Select(n=>HumanBone(p.character.transform,p.character.avatar,n)).ToArray();
                        var offsets=src.Select((b,i)=>Quaternion.Inverse(RestRotation(b,p.driver.transform,p.sourceHumanAvatar))*RestRotation(dst[i],p.character.transform,p.character.avatar)).ToArray();
                        var previousSource=new Quaternion[6];var previousTarget=new Quaternion[6];
                        Quaternion previousParent=dst[0].parent.rotation;
                        int count=Mathf.CeilToInt(actor.clip.length*60)+1;
                        float h=0,f=0,hip=0,contact=0,rotationStep=0;
                        for(int frame=0;frame<count;frame++)
                        {
                            actor.Evaluate(Mathf.Min(frame/60f,actor.clip.length));
                            maxNeck=Mathf.Max(maxNeck,p.Posture.NeckBend);
                            maxAuthoredNeck=Mathf.Max(maxAuthoredNeck,p.Posture.AuthoredNeckBend);
                            hip=Mathf.Max(hip,Vector3.Distance(p.sourceHips.position,p.targetHips.position));
                            foreach(var limb in p.limbs)contact=Mathf.Max(contact,Vector3.Distance(limb.end.position,limb.Goal));
                            for(int b=0;b<6;b++)
                            {
                                float error=RotationAngle(src[b].rotation*offsets[b],dst[b].rotation);
                                if(b==1)h=Mathf.Max(h,error);else if(b>=2)f=Mathf.Max(f,error);
                                if(frame>0&&b==0)
                                {
                                    float neckStep=RotationAngle(previousTarget[b],dst[b].rotation);
                                    maxNeckStep=Mathf.Max(maxNeckStep,neckStep);
                                    maxAddedNeckStep=Mathf.Max(maxAddedNeckStep,neckStep-RotationAngle(previousParent,dst[0].parent.rotation)-RotationAngle(previousSource[1],src[1].rotation));
                                    if(neckStep>25)jumps.AppendLine($"{actor.characterName} {actor.clip.name} t={frame/60f} neck={neckStep} parent={RotationAngle(previousParent,dst[0].parent.rotation)} head={RotationAngle(previousSource[1],src[1].rotation)} sourceNeck={RotationAngle(previousSource[0],src[0].rotation)}");
                                }
                                if(frame>0&&b!=0)rotationStep=Mathf.Max(rotationStep,Mathf.Abs(RotationAngle(previousSource[b],src[b].rotation)-RotationAngle(previousTarget[b],dst[b].rotation)));
                                previousSource[b]=src[b].rotation;previousTarget[b]=dst[b].rotation;
                                if(!float.IsFinite(dst[b].position.sqrMagnitude)||!float.IsFinite(dst[b].rotation.w))throw new Exception("Non-finite posture.");
                            }
                            previousParent=dst[0].parent.rotation;
                        }
                        // Evaluate backward and forward again to catch offsets calibrated from
                        // the previous sampled frame, accumulated transforms, or branch flips.
                        var endNeck=dst[0].rotation;
                        foreach(float time in new[]{actor.clip.length*.9f,0,actor.clip.length*.2f,actor.clip.length})
                        {
                            actor.Evaluate(time);
                            for(int b=0;b<6;b++)
                            {
                                float error=RotationAngle(src[b].rotation*offsets[b],dst[b].rotation);
                                if(b==1)h=Mathf.Max(h,error);else if(b>=2)f=Mathf.Max(f,error);
                            }
                        }
                        neckSeekDrift=Mathf.Max(neckSeekDrift,RotationAngle(endNeck,dst[0].rotation));
                        csv.AppendLine($"{actor.characterName},{actor.clip.name},{count},{h:R},{f:R},{hip:R},{contact:R},{rotationStep:R}");
                        clips++;frames+=count;headError=Mathf.Max(headError,h);footError=Mathf.Max(footError,f);pelvisError=Mathf.Max(pelvisError,hip);contactError=Mathf.Max(contactError,contact);stepError=Mathf.Max(stepError,rotationStep);
                    }
                }
                bool passed=clips==30&&headError<.1f&&footError<.1f&&pelvisError<.001f&&contactError<.005f&&stepError<.15f&&maxNeck<=65.01f&&maxAddedNeckStep<5f&&neckSeekDrift<.01f;
                string report=$"Unity: {Application.unityVersion}\nPassed: {passed}\nCharacter/clip combinations: {clips}\n60 Hz pose samples: {frames}\nMax head orientation error: {headError:R} degrees\nMax ankle/toe rotation error: {footError:R} degrees\nMax pelvis error: {pelvisError:R} m\nMax hand/ankle contact error: {contactError:R} m\nMax difference in source/target rotation step: {stepError:R} degrees\nMax neck bend: {maxNeck:R} degrees (authored: {maxAuthoredNeck:R})\nMax neck step at 60 Hz: {maxNeckStep:R} degrees\nMax extra neck step beyond torso and head movement: {maxAddedNeckStep:R} degrees\nNeck reverse-seek drift: {neckSeekDrift:R} degrees\nAll seven attacks and eight reactions on both characters. Includes reverse seeking, endpoint holds, and equipped receivers.\n";
                File.WriteAllText(folder+"/neck-steps.txt",jumps.ToString());
                File.WriteAllText(folder+"/validation.txt",report);File.WriteAllText(folder+"/validation.csv",csv.ToString());
                if(!passed)throw new Exception(report);
            }
            finally{tester.mankey.Clear();tester.pepe.Clear();EditorSceneManager.ClosePreviewScene(scene);}
        }
        public static void SaveCorrectedPosture()
        {
            var scene=EditorSceneManager.OpenScene(Output+"/Frank_Damages_Mankey_Pepe.unity",OpenSceneMode.Single);
            var tester=scene.GetRootGameObjects().Select(g=>g.GetComponent<FrankCombinationTester>()).First(t=>t);
            tester.Configure();tester.demoCamera.rect=new Rect(0,0,1,1);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
