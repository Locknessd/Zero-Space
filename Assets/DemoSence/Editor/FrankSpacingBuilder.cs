using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;
namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        static FrankPairSpacing.Volume[] BodyVolumes(FrankTestActor actor)
        {
            var animator=actor.character;ReferencePose(animator.transform,animator.avatar);
            var result=new System.Collections.Generic.List<FrankPairSpacing.Volume>();
            foreach(string name in new[]{"Hips","Spine","Chest","Head"})
            {
                var bone=HumanBone(animator.transform,animator.avatar,name);
                var points=new System.Collections.Generic.List<Vector3>();
                foreach(var sk in animator.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.enabled))
                {
                    var vs=sk.sharedMesh.vertices;var weights=sk.sharedMesh.boneWeights;var bs=sk.bones;
                    if(weights.Length!=vs.Length)continue;
                    int index=Array.IndexOf(bs,bone);if(index<0)continue;
                    var matrix=bone.localToWorldMatrix*sk.sharedMesh.bindposes[index];
                    for(int i=0;i<vs.Length;i++)if(weights[i].boneIndex0==index&&weights[i].weight0>.5f)points.Add(matrix.MultiplyPoint3x4(vs[i]));
                }
                if(points.Count==0)continue;
                var bounds=new Bounds(points[0],Vector3.zero);foreach(var point in points)bounds.Encapsulate(point);
                float radius=Mathf.Max(bounds.extents.x,bounds.extents.z)*.94f;
                float halfSegment=Mathf.Max(0,bounds.extents.y-radius);
                foreach(float y in halfSegment>.015f?new[]{-halfSegment,halfSegment}:new[]{0f})
                    result.Add(new FrankPairSpacing.Volume{bone=PathOf(bone,animator.transform),center=bone.InverseTransformPoint(bounds.center+Vector3.up*y),radius=radius});
            }
            return result.ToArray();
        }
        static Vector3[] Centers(FrankTestActor actor,FrankPairSpacing.Volume[] volumes)=>volumes.Select(v=>actor.character.transform.Find(v.bone).TransformPoint(v.center)).ToArray();
        static float Penetration(Vector3[] a,Vector3[] b,FrankPairSpacing.Volume[] va,FrankPairSpacing.Volume[] vb,Vector3 shift)
        {
            float worst=0;for(int i=0;i<a.Length;i++)for(int j=0;j<b.Length;j++)worst=Mathf.Max(worst,va[i].radius+vb[j].radius-Vector3.Distance(a[i],b[j]+shift));return worst;
        }
        static float Clearance(Vector3[] a,Vector3[] b,FrankPairSpacing.Volume[] va,FrankPairSpacing.Volume[] vb,Vector3 direction)
        {
            float amount=0;
            for(int iteration=0;iteration<4;iteration++)for(int i=0;i<a.Length;i++)for(int j=0;j<b.Length;j++)
            {
                Vector3 delta=b[j]-a[i];float radius=va[i].radius+vb[j].radius+.025f;
                if((delta+direction*amount).sqrMagnitude>=radius*radius)continue;
                float along=Vector3.Dot(delta,direction);
                float perpendicular=delta.sqrMagnitude-along*along;
                amount=Mathf.Max(amount,-along+Mathf.Sqrt(Mathf.Max(0,radius*radius-perpendicular)));
            }
            return amount;
        }
        [MenuItem("Tools/Frank Retarget/Build body spacing for Vol10")]
        public static void BuildBodySpacing()
        {
            string folder=Output+"/Reports/Spacing";Directory.CreateDirectory(folder);
            var scene=EditorSceneManager.OpenScene(Output+"/Frank_Damages_Mankey_Pepe.unity",OpenSceneMode.Single);
            var tester=Object.FindAnyObjectByType<FrankCombinationTester>();
            tester.mankey.Clear();tester.pepe.Clear();
            string path=Vol10Output+"/BodySpacing.asset";
            var asset=AssetDatabase.LoadAssetAtPath<FrankPairSpacing>(path);
            if(!asset){asset=ScriptableObject.CreateInstance<FrankPairSpacing>();AssetDatabase.CreateAsset(asset,path);}
            asset.mankey=BodyVolumes(tester.mankey);asset.pepe=BodyVolumes(tester.pepe);
            asset.corrections=new FrankPairSpacing.Correction[tester.unarmedLibrary.pairs.Length*2];
            tester.pairSpacing=null;tester.unarmed=true;
            var log=new StringBuilder("motion,attacker,maxBodyOverlapBefore,maxBodyOverlapAfter,maxRelativeOffset,maxOffsetFrameStep\n");
            for(int motion=0;motion<tester.unarmedLibrary.pairs.Length;motion++)for(int role=0;role<2;role++)
            {
                tester.unarmedMotion=motion;tester.pepeAttacks=role==1;tester.Configure();
                var va=role==0?asset.mankey:asset.pepe;var vb=role==0?asset.pepe:asset.mankey;
                int count=Mathf.CeilToInt(tester.Duration*60)+1;
                var raw=new Vector3[count];var ca=new Vector3[count][];var cb=new Vector3[count][];
                const int directions=32;
                var candidates=new Vector3[count,directions];var cost=new float[count,directions];var previous=new int[count,directions];
                for(int f=0;f<count;f++)
                {
                    tester.Seek(tester.Duration*f/(count-1));ca[f]=Centers(tester.Attacker,va);cb[f]=Centers(tester.Receiver,vb);
                    for(int d=0;d<directions;d++)
                    {
                        float angle=d*Mathf.PI*2/directions;var direction=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle));
                        var v=direction*Clearance(ca[f],cb[f],va,vb,direction);candidates[f,d]=v;
                        float best=float.PositiveInfinity;int parent=0;
                        if(f==0)best=0;
                        else for(int k=0;k<directions;k++)
                        {
                            float score=cost[f-1,k]+60*(v-candidates[f-1,k]).sqrMagnitude;
                            if(score<best){best=score;parent=k;}
                        }
                        float reach=Mathf.Max(0,FrankPairSpacing.ReachError(tester.Attacker.Pose,tester.Receiver.Pose,v)-.025f);
                        cost[f,d]=best+v.sqrMagnitude+250*reach*reach;previous[f,d]=parent;
                    }
                }
                int last=0;for(int d=1;d<directions;d++)if(cost[count-1,d]<cost[count-1,last])last=d;
                for(int f=count-1;f>=0;f--){raw[f]=candidates[f,last];last=previous[f,last];}
                // Plan a continuous path through the full take, then filter symmetrically.
                // Unlike choosing the pelvis direction each frame, stacked/inverted throws
                // cannot suddenly flip which side the extra body clearance is on.
                var smooth=new Vector3[count];
                for(int f=0;f<count;f++)
                {
                    float total=0;for(int k=-5;k<=5;k++){float weight=Mathf.Exp(-k*k/12f);smooth[f]+=raw[Mathf.Clamp(f+k,0,count-1)]*weight;total+=weight;}
                    smooth[f]/=total;
                }
                float before=0,after=0,max=0,step=0;
                for(int f=0;f<count;f++)
                {
                    before=Mathf.Max(before,Penetration(ca[f],cb[f],va,vb,Vector3.zero));after=Mathf.Max(after,Penetration(ca[f],cb[f],va,vb,smooth[f]));max=Mathf.Max(max,smooth[f].magnitude);
                    if(f>0)step=Mathf.Max(step,Vector3.Distance(smooth[f],smooth[f-1]));
                }
                asset.corrections[motion*2+role]=new FrankPairSpacing.Correction{clip=tester.unarmedLibrary.pairs[motion].sourceName,duration=tester.Duration,separation=smooth};
                log.AppendLine($"{tester.unarmedLibrary.pairs[motion].sourceName},{tester.Attacker.characterName},{before:R},{after:R},{max:R},{step:R}");
            }
            EditorUtility.SetDirty(asset);AssetDatabase.SaveAssetIfDirty(asset);
            tester.pairSpacing=asset;tester.bodySpacing=1;tester.unarmedMotion=1;tester.pepeAttacks=false;tester.paused=false;tester.Configure();tester.demoCamera.rect=new Rect(0,0,1,1);
            EditorSceneManager.SaveScene(scene);File.WriteAllText(folder+"/body-clearance.csv",log.ToString());
        }
        public static void InspectBodySpacing()
        {
            var scene=EditorSceneManager.OpenPreviewScene(Output+"/Frank_Damages_Mankey_Pepe.unity");
            var t=scene.GetRootGameObjects().Select(g=>g.GetComponent<FrankCombinationTester>()).First(x=>x);
            var log=new StringBuilder();
            foreach(var actor in new[]{t.mankey,t.pepe})
            {
                actor.Clear();var a=actor.character;ReferencePose(a.transform,a.avatar);
                log.AppendLine(actor.characterName+" scale="+a.transform.lossyScale);
                foreach(var name in new[]{"Hips","Spine","Chest","Neck","Head","LeftUpperLeg","LeftFoot"})
                {var bone=HumanBone(a.transform,a.avatar,name);log.AppendLine(name+" "+bone.name+" "+bone.position);}
                foreach(var sk in a.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.enabled))
                {
                    var mesh=new Mesh();sk.BakeMesh(mesh);var vs=sk.sharedMesh.vertices;var weights=sk.sharedMesh.boneWeights;var bs=sk.bones;
                    var matrices=bs.Select((b,i)=>b.localToWorldMatrix*sk.sharedMesh.bindposes[i]).ToArray();
                    if(weights.Length!=vs.Length){log.AppendLine(sk.name+" weights="+weights.Length+" verts="+vs.Length);Object.DestroyImmediate(mesh);continue;}
                    foreach(var name in new[]{"Hips","Spine","Chest","Neck","Head"})
                    {
                        var bone=HumanBone(a.transform,a.avatar,name);var points=Enumerable.Range(0,vs.Length).Where(i=>weights[i].boneIndex0<bs.Length&&bs[weights[i].boneIndex0]==bone&&weights[i].weight0>.5f).Select(i=>matrices[weights[i].boneIndex0].MultiplyPoint3x4(vs[i])).ToArray();
                        if(points.Length==0)continue;
                        var bounds=new Bounds(points[0],Vector3.zero);foreach(var p in points)bounds.Encapsulate(p);
                        log.AppendLine(name+" verts="+points.Length+" center="+bounds.center+" extents="+bounds.extents);
                    }
                    Object.DestroyImmediate(mesh);
                }
            }
            File.WriteAllText("Temp/FrankRetarget/body-spacing.txt",log.ToString());EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
