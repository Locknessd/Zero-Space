using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        [Serializable] public class CameraObservation { public float time; public Vector3 min,max,attacker,receiver,hand,otherHand,foot,otherFoot; }
        [Serializable] public class CameraTake { public string key,label,attack,reaction; public float duration,impact,ground; public CameraObservation[] samples; }
        [Serializable] public class CameraSurveyData { public CameraTake[] takes; }
        public static IEnumerable<(string family,int index,string label)> CameraEntries(FrankCombinationTester t)
        {
            for(int i=0;i<8;i++)yield return ("frank",i,FrankCombinationTester.MotionNames[i]);
            for(int i=0;i<t.unarmedLibrary.pairs.Length;i++)yield return ("vol10",i,t.unarmedLibrary.pairs[i].label);
            for(int i=0;i<t.comboLibrary.pairs.Length;i++)yield return ("combo",i,"Combo "+t.comboLibrary.pairs[i].combo+" "+t.comboLibrary.pairs[i].label);
            for(int i=0;i<t.greatSwordLibrary.pairs.Length;i++)yield return ("execution",i,t.greatSwordLibrary.pairs[i].label);
        }
        public static void SelectCameraEntry(FrankCombinationTester t,string family,int index,int role)
        {
            t.greatSword=family=="execution";t.unarmed=family=="vol10";t.gunSword=family=="combo";
            t.motion=family=="frank"?index:0;t.unarmedMotion=index;t.comboMotion=index;t.greatSwordMotion=index;
            t.pepeAttacks=role==1;t.attackerWeapon=family=="frank"?Mathf.Min(index,6)+1:0;t.receiverWeapon=0;
            t.paused=true;t.Configure();
        }
        public static Bounds CameraGeometry(FrankCombinationTester t,Mesh bake)
        {
            var all=t.mankey.character.GetComponentsInChildren<Renderer>(true).Concat(t.pepe.character.GetComponentsInChildren<Renderer>(true));
            if(t.mankey.activeDriver)all=all.Concat(t.mankey.activeDriver.GetComponentsInChildren<Renderer>(true));
            if(t.pepe.activeDriver)all=all.Concat(t.pepe.activeDriver.GetComponentsInChildren<Renderer>(true));
            bool initialized=false;Bounds result=new Bounds();
            foreach(var r in all)
            {
                if(!r.enabled||!r.gameObject.activeInHierarchy||r is ParticleSystemRenderer||r is TrailRenderer)continue;
                Bounds b;
                if(r is SkinnedMeshRenderer sk)
                {
                    sk.BakeMesh(bake,true);var local=bake.bounds;
                    b=new Bounds(sk.transform.TransformPoint(local.center),Vector3.zero);
                    for(int i=0;i<8;i++)b.Encapsulate(sk.transform.TransformPoint(local.center+Vector3.Scale(local.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))));
                }
                else b=r.bounds;
                if(!initialized){result=b;initialized=true;}else result.Encapsulate(b);
            }
            return result;
        }
        [MenuItem("Tools/Frank Retarget/Cameras/Survey all animation pairs")]
        public static void CameraSurvey()
        {
            Directory.CreateDirectory(Output+"/Reports/Cinematography");
            var scene=EditorSceneManager.OpenPreviewScene(Output+"/Frank_Damages_Mankey_Pepe.unity");
            var t=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<FrankCombinationTester>()).First();
            var mesh=new Mesh();var takes=new List<CameraTake>();
            try
            {
                foreach(var entry in CameraEntries(t))for(int role=0;role<2;role++)
                {
                    SelectCameraEntry(t,entry.family,entry.index,role);
                    var take=new CameraTake{key=entry.family+"/"+entry.index+"/"+(role==0?"mankey":"pepe"),label=entry.label,attack=t.Attacker.clip.name,reaction=t.Receiver.clip.name,duration=t.Duration};
                    if(entry.family=="combo"){take.impact=t.comboLibrary.pairs[entry.index].impactTime;take.ground=t.comboLibrary.pairs[entry.index].groundTime;}
                    int count=Mathf.CeilToInt(t.Duration*10)+1;take.samples=new CameraObservation[count];
                    for(int i=0;i<count;i++)
                    {
                        float time=t.Duration*i/(count-1);t.Seek(time);var b=CameraGeometry(t,mesh);
                        var limbs=t.Attacker.Pose.limbs;
                        take.samples[i]=new CameraObservation{time=time,min=b.min,max=b.max,attacker=t.Attacker.Pose.targetHips.position,receiver=t.Receiver.Pose.targetHips.position,hand=limbs[0].end.position,otherHand=limbs[2].end.position,foot=limbs[1].end.position,otherFoot=limbs[3].end.position};
                    }
                    takes.Add(take);Debug.Log("Camera surveyed "+take.key+" "+take.label);
                }
                File.WriteAllText("/tmp/frank-camera-survey.json",JsonUtility.ToJson(new CameraSurveyData{takes=takes.ToArray()}));
            }
            finally{t.mankey.Clear();t.pepe.Clear();UnityEngine.Object.DestroyImmediate(mesh);EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
